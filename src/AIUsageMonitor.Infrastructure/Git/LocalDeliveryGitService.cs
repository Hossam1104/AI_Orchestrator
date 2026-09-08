using System.Globalization;
using AIUsageMonitor.Application.Delivery;
using AIUsageMonitor.Application.Projects;

namespace AIUsageMonitor.Infrastructure.Git;

/// <summary>
/// The only Infrastructure seam allowed to mutate a managed repository for APO-63. Every Git
/// invocation is an argument-list call; the service never constructs a shell command string.
/// </summary>
public sealed class LocalDeliveryGitService : ILocalDeliveryGitService, ILocalDeliveryGitReconciliation
{
    private readonly IGitCommandRunner _runner;

    public LocalDeliveryGitService()
        : this(new SystemGitCommandRunner())
    {
    }

    internal LocalDeliveryGitService(IGitCommandRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<LocalDeliveryGitResult> CommitExactChangesAsync(
        SourceControlDeliveryCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.WorkspacePath) || command.ExpectedParentHeadSha is null ||
            command.AllowedChangedPaths.Count == 0 || string.IsNullOrWhiteSpace(command.CommitMessage))
            return Failure("The exact commit command is incomplete.");

        var workspace = NormalizeWorkspace(command.WorkspacePath);
        var branch = await ReadAsync(workspace, ["symbolic-ref", "--quiet", "--short", "HEAD"], cancellationToken).ConfigureAwait(false);
        if (!Succeeded(branch))
            return Failure(branch.TimedOut ? "The current branch check timed out." : "The workspace is detached or its current branch could not be read.");
        if (!string.Equals(branch.StandardOutput.Trim(), command.Target.HeadRef, StringComparison.Ordinal) ||
            IsDefaultBranch(branch.StandardOutput.Trim(), command.Target.BaseRef))
            return Failure("The exact commit must target the expected non-default task branch.");

        var head = await ReadAsync(workspace, ["rev-parse", "--verify", "HEAD"], cancellationToken).ConfigureAwait(false);
        if (!Succeeded(head) || !string.Equals(head.StandardOutput.Trim(), command.ExpectedParentHeadSha, StringComparison.OrdinalIgnoreCase))
            return Failure("The workspace parent HEAD does not match the immutable commit authority.");

        var status = await ReadAsync(workspace, ["status", "--porcelain=v1", "-z", "--untracked-files=all"], cancellationToken).ConfigureAwait(false);
        if (!Succeeded(status)) return Failure("The workspace status could not be read.");
        var statusPaths = ParseStatusPaths(status.StandardOutput, out var conflicted);
        if (conflicted || statusPaths.Any(path => !command.AllowedChangedPaths.Contains(path, StringComparer.Ordinal)))
            return Failure("The workspace contains an unexpected, conflicted, or unallowlisted change.");

        var stagedBefore = await ReadPathSetAsync(workspace, ["diff", "--name-only", "--cached", "-z"], cancellationToken).ConfigureAwait(false);
        if (stagedBefore is null || stagedBefore.Any(path => !command.AllowedChangedPaths.Contains(path, StringComparer.Ordinal)))
            return Failure("The workspace contains pre-existing staged content outside the exact allowlist.");

        var addArguments = new List<string> { "add", "--" };
        addArguments.AddRange(command.AllowedChangedPaths);
        var add = await MutateAsync(workspace, addArguments, cancellationToken).ConfigureAwait(false);
        if (!Succeeded(add)) return Failure("Exact-path staging failed.");

        var stagedAfter = await ReadPathSetAsync(workspace, ["diff", "--name-only", "--cached", "-z"], cancellationToken).ConfigureAwait(false);
        if (stagedAfter is null || !stagedAfter.SequenceEqual(command.AllowedChangedPaths.OrderBy(path => path, StringComparer.Ordinal), StringComparer.Ordinal))
            return Failure("The staged path set does not exactly match the authorized path set.");

        var commit = await MutateAsync(workspace, ["commit", "--message", command.CommitMessage], cancellationToken).ConfigureAwait(false);
        if (!Succeeded(commit)) return Failure("The exact local commit failed.");

        var newHead = await ReadAsync(workspace, ["rev-parse", "--verify", "HEAD"], cancellationToken).ConfigureAwait(false);
        var parent = await ReadAsync(workspace, ["rev-parse", "--verify", "HEAD^"], cancellationToken).ConfigureAwait(false);
        var branchAfter = await ReadAsync(workspace, ["symbolic-ref", "--quiet", "--short", "HEAD"], cancellationToken).ConfigureAwait(false);
        var finalStatus = await ReadAsync(workspace, ["status", "--porcelain=v1", "-z", "--untracked-files=all"], cancellationToken).ConfigureAwait(false);
        if (!Succeeded(newHead) || !Succeeded(parent) || !Succeeded(branchAfter) || !Succeeded(finalStatus) ||
            !string.Equals(parent.StandardOutput.Trim(), command.ExpectedParentHeadSha, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(branchAfter.StandardOutput.Trim(), command.Target.HeadRef, StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(finalStatus.StandardOutput))
            return Failure("The local commit could not be independently verified.");

        return new(SourceControlDeliveryStatus.Verified, NewHeadSha: newHead.StandardOutput.Trim(), MutationSent: true);
    }

    public async Task<LocalDeliveryGitResult> PushExactHeadAsync(
        SourceControlDeliveryCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.WorkspacePath)) return Failure("The exact push command has no registered workspace.");
        var workspace = NormalizeWorkspace(command.WorkspacePath);
        var branch = await ReadAsync(workspace, ["symbolic-ref", "--quiet", "--short", "HEAD"], cancellationToken).ConfigureAwait(false);
        if (!Succeeded(branch)) return Failure("The workspace is detached or its branch could not be read.");
        var currentBranch = branch.StandardOutput.Trim();
        if (!string.Equals(currentBranch, command.Target.HeadRef, StringComparison.Ordinal) || IsDefaultBranch(currentBranch, command.Target.BaseRef))
            return Failure("The exact push must target the configured non-default task branch.");

        var head = await ReadAsync(workspace, ["rev-parse", "--verify", "HEAD"], cancellationToken).ConfigureAwait(false);
        if (!Succeeded(head) || !string.Equals(head.StandardOutput.Trim(), command.Target.HeadSha, StringComparison.OrdinalIgnoreCase))
            return Failure("The local HEAD does not match the immutable push target.");

        var status = await ReadAsync(workspace, ["status", "--porcelain=v1", "-z", "--untracked-files=all"], cancellationToken).ConfigureAwait(false);
        if (!Succeeded(status) || !string.IsNullOrEmpty(status.StandardOutput))
            return Failure("The exact push requires a clean worktree and index.");

        var remote = await ReadAsync(workspace, ["remote", "get-url", "origin"], cancellationToken).ConfigureAwait(false);
        if (!Succeeded(remote) || !RemoteMatches(remote.StandardOutput.Trim(), command.Target.RepositoryUrl))
            return Failure("The configured local origin does not identify the exact delivery repository.");

        var push = await MutateAsync(workspace, ["push", "origin", $"{command.Target.HeadRef}:{command.Target.HeadRef}"], cancellationToken).ConfigureAwait(false);
        if (!Succeeded(push))
        {
            return push.TimedOut || push.Cancelled
                ? new(SourceControlDeliveryStatus.ReconciliationRequired, "The push outcome is uncertain and requires remote reconciliation.", command.Target.HeadSha, true)
                : new(SourceControlDeliveryStatus.Conflict, "The normal fast-forward push was rejected.", MutationSent: true);
        }

        var remoteHead = await ReadAsync(workspace, ["ls-remote", "--heads", "origin", $"refs/heads/{command.Target.HeadRef}"], cancellationToken).ConfigureAwait(false);
        if (!Succeeded(remoteHead))
            return new(SourceControlDeliveryStatus.ReconciliationRequired, "The push succeeded but its remote head could not be independently verified.", command.Target.HeadSha, true);
        var remoteSha = remoteHead.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t', StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 2 && string.Equals(parts[1], $"refs/heads/{command.Target.HeadRef}", StringComparison.Ordinal))
            .Select(parts => parts[0]).FirstOrDefault();
        return string.Equals(remoteSha, command.Target.HeadSha, StringComparison.OrdinalIgnoreCase)
            ? new(SourceControlDeliveryStatus.Verified, NewHeadSha: command.Target.HeadSha, MutationSent: true)
            : new(SourceControlDeliveryStatus.ReconciliationRequired, "The pushed remote head does not match the exact local head.", command.Target.HeadSha, true);
    }

    public async Task<LocalDeliveryGitResult> ReconcileAsync(
        SourceControlDeliveryCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.WorkspacePath))
            return new(SourceControlDeliveryStatus.ReconciliationRequired, "The attempted local operation has no authoritative workspace.");

        var workspace = NormalizeWorkspace(command.WorkspacePath);
        var branch = await ReadAsync(workspace, ["symbolic-ref", "--quiet", "--short", "HEAD"], cancellationToken).ConfigureAwait(false);
        var head = await ReadAsync(workspace, ["rev-parse", "--verify", "HEAD"], cancellationToken).ConfigureAwait(false);
        if (!Succeeded(branch) || !Succeeded(head) || !string.Equals(branch.StandardOutput.Trim(), command.Target.HeadRef, StringComparison.Ordinal) ||
            !string.Equals(head.StandardOutput.Trim(), command.Target.HeadSha, StringComparison.OrdinalIgnoreCase))
            return new(SourceControlDeliveryStatus.ReconciliationRequired, "Read-only local evidence cannot prove the attempted local mutation was applied.", MutationSent: true);

        if (command.OperationKind == SourceControlDeliveryOperationKind.CommitExactChanges)
            return new(SourceControlDeliveryStatus.AlreadyApplied, NewHeadSha: command.Target.HeadSha, MutationSent: true);

        var status = await ReadAsync(workspace, ["status", "--porcelain=v1", "-z", "--untracked-files=all"], cancellationToken).ConfigureAwait(false);
        var remote = await ReadAsync(workspace, ["remote", "get-url", "origin"], cancellationToken).ConfigureAwait(false);
        if (!Succeeded(status) || !string.IsNullOrEmpty(status.StandardOutput) || !Succeeded(remote) || !RemoteMatches(remote.StandardOutput.Trim(), command.Target.RepositoryUrl))
            return new(SourceControlDeliveryStatus.ReconciliationRequired, "Read-only local evidence cannot prove the attempted push target.", MutationSent: true);
        var remoteHead = await ReadAsync(workspace, ["ls-remote", "--heads", "origin", $"refs/heads/{command.Target.HeadRef}"], cancellationToken).ConfigureAwait(false);
        if (!Succeeded(remoteHead))
            return new(SourceControlDeliveryStatus.ReconciliationRequired, "The attempted push cannot be reconciled because remote-head evidence is unavailable.", MutationSent: true);
        var remoteSha = remoteHead.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t', StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 2 && string.Equals(parts[1], $"refs/heads/{command.Target.HeadRef}", StringComparison.Ordinal))
            .Select(parts => parts[0]).FirstOrDefault();
        return string.Equals(remoteSha, command.Target.HeadSha, StringComparison.OrdinalIgnoreCase)
            ? new(SourceControlDeliveryStatus.AlreadyApplied, NewHeadSha: command.Target.HeadSha, MutationSent: true)
            : new(SourceControlDeliveryStatus.ReconciliationRequired, "Remote read-only evidence does not prove the attempted push was applied.", MutationSent: true);
    }

    private Task<GitCommandResult> ReadAsync(string workspace, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        _runner.RunAsync(BuildArguments(workspace, arguments), GitCommandExecutionProfile.ReadOnly, cancellationToken);

    private Task<GitCommandResult> MutateAsync(string workspace, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        _runner.RunAsync(BuildArguments(workspace, arguments), GitCommandExecutionProfile.WorktreeMutation, cancellationToken);

    private static IReadOnlyList<string> BuildArguments(string workspace, IReadOnlyList<string> arguments)
    {
        var result = new List<string>(arguments.Count + 2) { "-C", workspace };
        result.AddRange(arguments);
        return result;
    }

    private static string NormalizeWorkspace(string value) =>
        Path.GetFullPath(value.Trim());

    private static bool Succeeded(GitCommandResult result) =>
        result.ExitCode == 0 && !result.TimedOut && !result.Cancelled && !result.CouldNotStart && !result.OutputTruncated;

    private static bool IsDefaultBranch(string branch, string baseRef) =>
        branch.Equals("main", StringComparison.OrdinalIgnoreCase) || branch.Equals("master", StringComparison.OrdinalIgnoreCase) ||
        branch.Equals(baseRef, StringComparison.Ordinal);

    private static LocalDeliveryGitResult Failure(string message) =>
        new(SourceControlDeliveryStatus.Blocked, message);

    private static IReadOnlyList<string> ParseStatusPaths(string output, out bool conflicted)
    {
        conflicted = false;
        var paths = new List<string>();
        var tokens = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token.Length < 3) continue;
            var indexStatus = token[0];
            var worktreeStatus = token[1];
            conflicted |= indexStatus == 'U' || worktreeStatus == 'U' || indexStatus == worktreeStatus && indexStatus is 'A' or 'D';
            var path = token[3..].Replace('\\', '/');
            paths.Add(path);
            if (indexStatus is 'R' or 'C' || worktreeStatus is 'R' or 'C')
            {
                if (index + 1 < tokens.Length && !string.IsNullOrWhiteSpace(tokens[index + 1]))
                    paths.Add(tokens[++index].Replace('\\', '/'));
                else
                    conflicted = true;
            }
        }
        return paths.Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }

    private async Task<IReadOnlyList<string>?> ReadPathSetAsync(string workspace, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await ReadAsync(workspace, arguments, cancellationToken).ConfigureAwait(false);
        return Succeeded(result)
            ? result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries).Select(path => path.Replace('\\', '/')).OrderBy(path => path, StringComparer.Ordinal).ToArray()
            : null;
    }

    private static bool RemoteMatches(string local, string configured)
    {
        static string? Key(string value)
        {
            value = value.Trim();
            if (Directory.Exists(value) || File.Exists(value) || value.StartsWith("\\\\", StringComparison.Ordinal) || value.Length >= 2 && value[1] == ':')
            {
                try { return $"local:{Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant()}"; }
                catch (ArgumentException) { return null; }
            }
            string host;
            string path;
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                if (uri.IsFile || string.IsNullOrEmpty(uri.Host))
                {
                    try { return $"local:{Path.GetFullPath(uri.LocalPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant()}"; }
                    catch (ArgumentException) { return null; }
                }
                host = uri.Host;
                path = uri.AbsolutePath;
            }
            else
            {
                var at = value.IndexOf('@');
                var colon = value.IndexOf(':', at + 1);
                if (at > 0 && colon > at)
                {
                    host = value[(at + 1)..colon];
                    path = value[(colon + 1)..];
                }
                else
                {
                    var fullPath = Path.GetFullPath(value);
                    return $"local:{fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant()}";
                }
            }
            path = path.Trim('/');
            if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
            return string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(path) ? null : $"{host.ToLowerInvariant()}/{path.ToLowerInvariant()}";
        }

        return Key(local) is { } localKey && Key(configured) is { } configuredKey && localKey == configuredKey;
    }
}
