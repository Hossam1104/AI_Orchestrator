using System.Globalization;
using System.Text;
using AIUsageMonitor.Application.Projects;

namespace AIUsageMonitor.Infrastructure.Git;

/// <summary>
/// Read-only local Git inspector. The command set is intentionally small and does not include
/// fetch, pull, push, checkout, config writes, or any other repository mutation.
/// </summary>
public sealed class GitLocalRepositoryInspector : ILocalRepositoryInspector
{
    internal const int MaxChangedFiles = 100;
    internal const int MaxFieldLength = 512;

    private readonly IGitCommandRunner _runner;
    private readonly ILocalPathProbe _pathProbe;

    public GitLocalRepositoryInspector()
        : this(new SystemGitCommandRunner(), new SystemLocalPathProbe())
    {
    }

    internal GitLocalRepositoryInspector(IGitCommandRunner runner)
        : this(runner, new SystemLocalPathProbe())
    {
    }

    internal GitLocalRepositoryInspector(IGitCommandRunner runner, ILocalPathProbe pathProbe)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _pathProbe = pathProbe ?? throw new ArgumentNullException(nameof(pathProbe));
    }

    public async Task<LocalRepositoryInspection> InspectAsync(
        string registeredLocalPath,
        string? registeredRepositoryUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(registeredLocalPath))
        {
            throw new ArgumentException("Registered local path is required.", nameof(registeredLocalPath));
        }

        var capturedAt = DateTimeOffset.UtcNow;

        var probe = await _pathProbe.ProbeAsync(registeredLocalPath, cancellationToken).ConfigureAwait(false);
        var pathState = MapPathProbeStatus(probe);
        if (pathState is not null)
        {
            return CreateResult(pathState.Value, registeredLocalPath, capturedAt);
        }

        var version = await RunAsync(["--version"], cancellationToken).ConfigureAwait(false);
        ThrowIfCancelled(version, cancellationToken);
        if (version.CouldNotStart)
        {
            return CreateResult(RepositoryVerificationStatus.GitUnavailable, registeredLocalPath, capturedAt);
        }

        if (version.TimedOut)
        {
            return CreateFailure(registeredLocalPath, capturedAt, "Git verification timed out.");
        }

        if (version.ExitCode != 0)
        {
            return CreateFailure(registeredLocalPath, capturedAt, "Git verification could not be completed.");
        }

        var root = await RunAsync(
            ["-C", registeredLocalPath, "rev-parse", "--show-toplevel"],
            cancellationToken).ConfigureAwait(false);
        ThrowIfCancelled(root, cancellationToken);
        if (CommonFailure(root, registeredLocalPath, capturedAt) is { } rootFailure)
        {
            return rootFailure;
        }

        if (root.ExitCode != 0)
        {
            return IsNotGitRepositoryFailure(root)
                ? CreateResult(RepositoryVerificationStatus.NotGitRepository, registeredLocalPath, capturedAt)
                : CreateFailure(registeredLocalPath, capturedAt, "Git verification could not be completed.");
        }

        var repositoryRoot = LimitField(root.StandardOutput.Trim());
        if (repositoryRoot.Length == 0)
        {
            return CreateFailure(registeredLocalPath, capturedAt, "Repository root was unavailable.");
        }

        var branchResult = await RunAsync(
            ["-C", registeredLocalPath, "symbolic-ref", "--quiet", "--short", "HEAD"],
            cancellationToken).ConfigureAwait(false);
        ThrowIfCancelled(branchResult, cancellationToken);
        if (CommonFailure(branchResult, registeredLocalPath, capturedAt) is { } branchFailure)
        {
            return branchFailure;
        }

        string? branchName;
        bool isDetachedHead;
        if (branchResult.ExitCode == 0)
        {
            branchName = LimitField(branchResult.StandardOutput.Trim());
            isDetachedHead = false;
        }
        else if (IsDetachedHeadExit(branchResult))
        {
            branchName = null;
            isDetachedHead = true;
        }
        else
        {
            return CreateFailure(registeredLocalPath, capturedAt, "Branch information could not be determined.");
        }

        var headResult = await RunAsync(
            ["-C", registeredLocalPath, "rev-parse", "--verify", "HEAD"],
            cancellationToken).ConfigureAwait(false);
        ThrowIfCancelled(headResult, cancellationToken);
        if (CommonFailure(headResult, registeredLocalPath, capturedAt) is { } headFailure)
        {
            return headFailure;
        }

        string? headSha = null;
        if (headResult.ExitCode == 0)
        {
            var trimmedHead = LimitField(headResult.StandardOutput.Trim());
            headSha = trimmedHead.Length == 0 ? null : trimmedHead;
        }
        else if (!IsUnbornHeadFailure(headResult))
        {
            return CreateFailure(registeredLocalPath, capturedAt, "HEAD information could not be determined.");
        }

        var upstream = await RunAsync(
            ["-C", registeredLocalPath, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"],
            cancellationToken).ConfigureAwait(false);
        ThrowIfCancelled(upstream, cancellationToken);
        if (CommonFailure(upstream, registeredLocalPath, capturedAt) is { } upstreamFailure)
        {
            return upstreamFailure;
        }

        var status = await RunAsync(
            ["-C", registeredLocalPath, "status", "--porcelain=v1", "-z", "--untracked-files=all"],
            cancellationToken).ConfigureAwait(false);
        ThrowIfCancelled(status, cancellationToken);
        if (CommonFailure(status, registeredLocalPath, capturedAt) is { } statusFailure)
        {
            return statusFailure;
        }

        if (status.ExitCode != 0)
        {
            return CreateFailure(registeredLocalPath, capturedAt, "Repository status could not be read.");
        }

        var changes = ParseStatus(status.StandardOutput);
        var remotes = await RunAsync(
            ["-C", registeredLocalPath, "remote", "-v"],
            cancellationToken).ConfigureAwait(false);
        ThrowIfCancelled(remotes, cancellationToken);
        if (CommonFailure(remotes, registeredLocalPath, capturedAt) is { } remotesFailure)
        {
            return remotesFailure;
        }

        if (remotes.ExitCode != 0)
        {
            return CreateFailure(registeredLocalPath, capturedAt, "Repository remotes could not be read.");
        }

        var remoteEntries = ParseRemotes(remotes.StandardOutput);
        return new LocalRepositoryInspection(
            changes.Total == 0
                ? RepositoryVerificationStatus.AvailableClean
                : RepositoryVerificationStatus.AvailableDirty,
            registeredLocalPath,
            repositoryRoot,
            IsSamePath(registeredLocalPath, repositoryRoot),
            branchName,
            isDetachedHead,
            headSha,
            headSha is null ? null : headSha[..Math.Min(7, headSha.Length)],
            upstream.ExitCode == 0 ? LimitField(upstream.StandardOutput.Trim()) : null,
            changes.Total == 0,
            changes.Total,
            changes.Files,
            changes.Truncated,
            remoteEntries,
            CompareRegisteredRemote(registeredRepositoryUrl, remoteEntries),
            capturedAt,
            workspace: WorkspaceDiscovery.Inspect(registeredLocalPath, remoteEntries));
    }

    private async Task<GitCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) =>
        await _runner.RunAsync(arguments, GitCommandExecutionProfile.ReadOnly, cancellationToken).ConfigureAwait(false);

    private static LocalRepositoryInspection? CommonFailure(
        GitCommandResult result,
        string registeredLocalPath,
        DateTimeOffset capturedAt)
    {
        if (result.TimedOut)
        {
            return CreateFailure(registeredLocalPath, capturedAt, "Git verification timed out.");
        }

        if (result.CouldNotStart)
        {
            return CreateResult(RepositoryVerificationStatus.GitUnavailable, registeredLocalPath, capturedAt);
        }

        return null;
    }

    private static RepositoryVerificationStatus? MapPathProbeStatus(LocalPathProbeResult probe) =>
        probe.Status switch
        {
            LocalPathProbeStatus.AvailableDirectory => null,
            LocalPathProbeStatus.Missing => RepositoryVerificationStatus.PathMissing,
            LocalPathProbeStatus.NotADirectory => RepositoryVerificationStatus.PathUnavailable,
            LocalPathProbeStatus.Unavailable => RepositoryVerificationStatus.PathUnavailable,
            _ => RepositoryVerificationStatus.PathUnavailable
        };

    private static LocalRepositoryInspection CreateResult(
        RepositoryVerificationStatus status,
        string path,
        DateTimeOffset capturedAt) =>
        new(status, path, capturedAt: capturedAt, workspace: WorkspaceDiscovery.Inspect(path));

    private static LocalRepositoryInspection CreateFailure(
        string path,
        DateTimeOffset capturedAt,
        string message) =>
        new(
            RepositoryVerificationStatus.Failed,
            path,
            capturedAt: capturedAt,
            safeErrorMessage: message,
            workspace: WorkspaceDiscovery.Inspect(path));

    private static void ThrowIfCancelled(GitCommandResult result, CancellationToken cancellationToken)
    {
        if (result.Cancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    /// <summary>
    /// Only the documented "not a git repository" fatal text proves a non-repository directory.
    /// Any other nonzero exit (permission failure, unexpected fatal error, corrupt state) is
    /// classified as a bounded <c>Failed</c> result by the caller rather than fabricated here.
    /// </summary>
    private static bool IsNotGitRepositoryFailure(GitCommandResult result) =>
        result.ExitCode != 0 &&
        result.StandardError.Contains("not a git repository", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <c>git symbolic-ref --quiet --short HEAD</c> documents exit status 1 for "not a symbolic
    /// ref" (i.e. detached HEAD). Any other nonzero exit is an unexpected failure, not a detached
    /// state.
    /// </summary>
    private static bool IsDetachedHeadExit(GitCommandResult result) => result.ExitCode == 1;

    /// <summary>
    /// <c>git rev-parse --verify HEAD</c> fails with a known "no commits yet" fatal message in a
    /// valid, unborn repository. Any other nonzero exit is an unexpected failure and must not be
    /// silently treated as "HEAD not created yet".
    /// </summary>
    private static bool IsUnbornHeadFailure(GitCommandResult result) =>
        result.ExitCode != 0 &&
        (result.StandardError.Contains("unknown revision", StringComparison.OrdinalIgnoreCase) ||
         result.StandardError.Contains("ambiguous argument", StringComparison.OrdinalIgnoreCase) ||
         result.StandardError.Contains("bad revision", StringComparison.OrdinalIgnoreCase) ||
         result.StandardError.Contains("needed a single revision", StringComparison.OrdinalIgnoreCase));

    private static bool IsSamePath(string left, string right)
    {
        try
        {
            var normalizedLeft = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left));
            var normalizedRight = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static ParsedChanges ParseStatus(string output)
    {
        var tokens = output.Split('\0');
        var total = 0;
        var files = new List<RepositoryChangedFile>(Math.Min(MaxChangedFiles, tokens.Length));

        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token.Length < 4)
            {
                continue;
            }

            var first = token[0];
            var second = token[1];
            var path = token[3..];
            if (path.Length == 0)
            {
                continue;
            }

            // Git's porcelain -z rename/copy record is `XY <new-path>\0<old-path>\0` — the NEW
            // name comes first (already captured above as `path`); the OLD name is the following
            // token. This is the opposite order to the human-readable "old -> new" form.
            string? originalPath = null;
            var isRename = first is 'R' or 'C' || second is 'R' or 'C';
            if (isRename && index + 1 < tokens.Length && tokens[index + 1].Length > 0)
            {
                originalPath = tokens[++index];
            }

            total++;
            var kind = GetChangeKind(first, second, isRename);
            if (files.Count < MaxChangedFiles)
            {
                files.Add(new RepositoryChangedFile(
                    LimitField(path),
                    kind,
                    originalPath is null ? null : LimitField(originalPath)));
            }
        }

        return new ParsedChanges(total, files, total > files.Count);
    }

    private static RepositoryChangedFileKind GetChangeKind(char index, char worktree, bool isRename)
    {
        // A conflicted/unmerged entry is never also an "ordinary" staged/modified/deleted change:
        // its index/worktree characters (e.g. 'A'/'A', 'D'/'D', 'U') reuse the same letters as
        // ordinary status codes but mean something different during a merge conflict.
        if (IsConflicted(index, worktree))
        {
            return RepositoryChangedFileKind.Conflicted;
        }

        var kind = RepositoryChangedFileKind.None;
        if (index != ' ' && index != '?')
        {
            kind |= RepositoryChangedFileKind.Staged;
        }

        if (index is 'M' || worktree is 'M' or 'T')
        {
            kind |= RepositoryChangedFileKind.Modified;
        }

        if (index is 'D' || worktree is 'D')
        {
            kind |= RepositoryChangedFileKind.Deleted;
        }

        if (isRename)
        {
            kind |= RepositoryChangedFileKind.Renamed;
        }

        if (index == '?' && worktree == '?')
        {
            kind |= RepositoryChangedFileKind.Untracked;
        }

        return kind;
    }

    private static bool IsConflicted(char index, char worktree) =>
        index is 'U' || worktree is 'U' ||
        (index == 'A' && worktree == 'A') ||
        (index == 'D' && worktree == 'D');

    private static IReadOnlyList<RepositoryRemote> ParseRemotes(string output)
    {
        var result = new List<RepositoryRemote>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('\t');
            if (separator <= 0)
            {
                continue;
            }

            var name = line[..separator].Trim();
            var remainder = line[(separator + 1)..].Trim();
            var marker = remainder.LastIndexOf(" (", StringComparison.Ordinal);
            var rawUrl = marker > 0 ? remainder[..marker].Trim() : remainder;
            if (name.Length == 0 || rawUrl.Length == 0)
            {
                continue;
            }

            var sanitized = SanitizeRemoteUrl(rawUrl);
            if (!result.Any(remote =>
                    string.Equals(remote.Name, name, StringComparison.Ordinal) &&
                    string.Equals(remote.SanitizedUrl, sanitized, StringComparison.Ordinal)))
            {
                result.Add(new RepositoryRemote(LimitField(name), sanitized));
            }
        }

        return result;
    }

    internal static string SanitizeRemoteUrl(string rawUrl)
        => LimitField(RepositoryUrlSanitizer.Sanitize(rawUrl));

    private static RepositoryRemoteComparison CompareRegisteredRemote(
        string? registeredUrl,
        IReadOnlyList<RepositoryRemote> remotes)
    {
        if (string.IsNullOrWhiteSpace(registeredUrl))
        {
            return RepositoryRemoteComparison.NotConfigured;
        }

        if (remotes.Count == 0)
        {
            return RepositoryRemoteComparison.NoLocalRemote;
        }

        var registeredKey = GetRemoteKey(registeredUrl);
        if (registeredKey is null)
        {
            return RepositoryRemoteComparison.ComparisonUnavailable;
        }

        var localKeys = remotes
            .Select(remote => GetRemoteKey(remote.SanitizedUrl))
            .ToArray();
        if (localKeys.Any(key => key is not null && key == registeredKey))
        {
            return RepositoryRemoteComparison.Match;
        }

        return localKeys.Any(static key => key is null)
            ? RepositoryRemoteComparison.ComparisonUnavailable
            : RepositoryRemoteComparison.Different;
    }

    private static string? GetRemoteKey(string value)
    {
        var sanitized = SanitizeRemoteUrl(value);
        var separator = sanitized.IndexOf(':');
        if (sanitized.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(sanitized, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
            {
                return null;
            }

            var path = uri.AbsolutePath.Trim('/');
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{uri.Host.ToLowerInvariant()}/{path}");
        }

        if (separator > 0)
        {
            var host = sanitized[..separator].Trim().ToLowerInvariant();
            var path = sanitized[(separator + 1)..].Trim('/');
            return $"{host}/{path}";
        }

        return null;
    }

    private static string LimitField(string value)
    {
        if (value.Length <= MaxFieldLength)
        {
            return value;
        }

        return value[..(MaxFieldLength - 1)] + "…";
    }

    private sealed record ParsedChanges(
        int Total,
        IReadOnlyList<RepositoryChangedFile> Files,
        bool Truncated);
}
