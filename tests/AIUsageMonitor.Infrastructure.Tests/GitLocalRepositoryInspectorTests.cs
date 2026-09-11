using System.Diagnostics;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Infrastructure.Git;

namespace AIUsageMonitor.Infrastructure.Tests;

public sealed class GitLocalRepositoryInspectorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "apo-git-inspector-tests-" + Guid.NewGuid().ToString("N"));

    public GitLocalRepositoryInspectorTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task MissingLocalPath_ReturnsPathMissingWithoutRunningGit()
    {
        var runner = new FakeGitCommandRunner();
        var result = await CreateInspector(runner).InspectAsync(Path.Combine(_root, "missing"));

        Assert.Equal(RepositoryVerificationStatus.PathMissing, result.Status);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task GitUnavailable_ReturnsTruthfulState()
    {
        var directory = CreateDirectory("unavailable");
        var runner = new FakeGitCommandRunner
        {
            Handler = _ => Task.FromResult(Result(couldNotStart: true))
        };

        var result = await CreateInspector(runner).InspectAsync(directory);

        Assert.Equal(RepositoryVerificationStatus.GitUnavailable, result.Status);
    }

    [Fact]
    public async Task ExistingPath_NotGitRepository_IsNotPathFailure()
    {
        var directory = CreateDirectory("not-repository");
        var runner = ScriptedRunner(directory, rootExitCode: 128);

        var result = await CreateInspector(runner).InspectAsync(directory);

        Assert.Equal(RepositoryVerificationStatus.NotGitRepository, result.Status);
    }

    [Fact]
    public async Task CleanRepository_CollectsMetadataAndSanitizesRemote()
    {
        var directory = CreateDirectory("clean");
        var runner = ScriptedRunner(
            directory,
            status: string.Empty,
            remote: "origin\thttps://user:token@github.com/org/repo.git?secret=query (fetch)\r\n" +
                    "origin\thttps://user:token@github.com/org/repo.git?secret=query (push)\r\n");

        var result = await CreateInspector(runner).InspectAsync(
            directory,
            "git@github.com:org/repo.git");

        Assert.Equal(RepositoryVerificationStatus.AvailableClean, result.Status);
        Assert.True(result.IsClean);
        Assert.Equal(directory, result.RepositoryRoot);
        Assert.True(result.LocalPathIsRepositoryRoot);
        Assert.Equal("main", result.BranchName);
        Assert.False(result.IsDetachedHead);
        Assert.Equal("abcdef1234567890", result.HeadSha);
        Assert.Equal("abcdef1", result.HeadShortSha);
        Assert.Equal("origin/main", result.UpstreamBranch);
        var remote = Assert.Single(result.Remotes);
        Assert.Equal("https://github.com/org/repo.git", remote.SanitizedUrl);
        Assert.DoesNotContain("token", remote.SanitizedUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", remote.SanitizedUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RepositoryRemoteComparison.Match, result.RemoteComparison);
    }

    [Fact]
    public async Task Discovery_ReportsBoundedWorkspacePresenceWithoutReadingContents()
    {
        var directory = CreateDirectory("workspace-discovery");
        Directory.CreateDirectory(Path.Combine(directory, ".ai"));
        File.WriteAllText(Path.Combine(directory, "AGENTS.md"), "owner-controlled content");
        File.WriteAllText(Path.Combine(directory, "workspace.sln"), "solution content");
        var runner = ScriptedRunner(
            directory,
            remote: "origin\thttps://github.com/org/repo.git (fetch)\r\n");

        var result = await CreateInspector(runner).InspectAsync(directory);

        Assert.NotNull(result.Workspace);
        Assert.True(result.Workspace!.Exists);
        Assert.True(result.Workspace.IsReadable);
        Assert.Equal(WorkspaceFileState.Present, result.Workspace.GovernanceFiles["AGENTS.md"]);
        Assert.Equal(WorkspaceFileState.Present, result.Workspace.GovernanceFiles[".ai"]);
        Assert.Equal(WorkspaceFileState.Missing, result.Workspace.GovernanceFiles["CLAUDE.md"]);
        Assert.Contains("workspace.sln", result.Workspace.ProjectFiles);
        Assert.Equal("GitHub", result.Workspace.RemoteProvider);
    }

    [Fact]
    public async Task DirtyTrackedFile_IsParsedAsModified()
    {
        var directory = CreateDirectory("dirty");
        var runner = ScriptedRunner(directory, status: " M src/changed.cs\0");

        var result = await CreateInspector(runner).InspectAsync(directory);

        var change = Assert.Single(result.ChangedFiles);
        Assert.Equal(RepositoryVerificationStatus.AvailableDirty, result.Status);
        Assert.False(result.IsClean);
        Assert.Equal("src/changed.cs", change.RelativePath);
        Assert.Equal(RepositoryChangedFileKind.Modified, change.Kind);
    }

    [Fact]
    public async Task StatusParser_SupportsStagedDeletedRenamedUntrackedAndConflicted()
    {
        var directory = CreateDirectory("statuses");
        // Real Git porcelain -z rename order is `new\0old\0` (the new name comes first).
        var status = string.Join('\0',
            "M  staged.cs",
            " D deleted.cs",
            "R  new.cs",
            "old.cs",
            "?? untracked.cs",
            "UU conflicted.cs") + "\0";
        var runner = ScriptedRunner(directory, status: status);

        var result = await CreateInspector(runner).InspectAsync(directory);

        Assert.Equal(5, result.ChangedFileTotal);
        Assert.Contains(result.ChangedFiles, file => file.RelativePath == "staged.cs" && file.Kind.HasFlag(RepositoryChangedFileKind.Staged));
        Assert.Contains(result.ChangedFiles, file => file.RelativePath == "deleted.cs" && file.Kind.HasFlag(RepositoryChangedFileKind.Deleted));
        var rename = Assert.Single(result.ChangedFiles, file => file.Kind.HasFlag(RepositoryChangedFileKind.Renamed));
        Assert.Equal("new.cs", rename.RelativePath);
        Assert.Equal("old.cs", rename.OriginalRelativePath);
        Assert.Contains(result.ChangedFiles, file => file.Kind.HasFlag(RepositoryChangedFileKind.Untracked));
        Assert.Contains(result.ChangedFiles, file => file.Kind.HasFlag(RepositoryChangedFileKind.Conflicted));
    }

    [Fact]
    public async Task PorcelainZ_RenamePublishesTargetAndOriginalCorrectly()
    {
        var directory = CreateDirectory("rename-order");
        var status = "R  new.cs\0old.cs\0";
        var runner = ScriptedRunner(directory, status: status);

        var result = await CreateInspector(runner).InspectAsync(directory);

        var rename = Assert.Single(result.ChangedFiles);
        Assert.Equal("new.cs", rename.RelativePath);
        Assert.Equal("old.cs", rename.OriginalRelativePath);
        Assert.True(rename.Kind.HasFlag(RepositoryChangedFileKind.Renamed));
    }

    [Theory]
    [InlineData("M  file.cs", RepositoryChangedFileKind.Staged | RepositoryChangedFileKind.Modified)]
    [InlineData(" M file.cs", RepositoryChangedFileKind.Modified)]
    [InlineData("D  file.cs", RepositoryChangedFileKind.Staged | RepositoryChangedFileKind.Deleted)]
    [InlineData(" D file.cs", RepositoryChangedFileKind.Deleted)]
    [InlineData("?? file.cs", RepositoryChangedFileKind.Untracked)]
    public async Task StatusFlags_AreTruthfulForOrdinaryStates(string statusLine, RepositoryChangedFileKind expectedKind)
    {
        var directory = CreateDirectory("status-flags-" + Guid.NewGuid().ToString("N"));
        var runner = ScriptedRunner(directory, status: statusLine + "\0");

        var result = await CreateInspector(runner).InspectAsync(directory);

        var change = Assert.Single(result.ChangedFiles);
        Assert.Equal(expectedKind, change.Kind);
        Assert.False(change.Kind.HasFlag(RepositoryChangedFileKind.Conflicted));
    }

    [Theory]
    [InlineData("UU file.cs")]
    [InlineData("AA file.cs")]
    [InlineData("DD file.cs")]
    [InlineData("AU file.cs")]
    [InlineData("UA file.cs")]
    [InlineData("DU file.cs")]
    [InlineData("UD file.cs")]
    public async Task StatusFlags_ConflictStatesAreConflictedOnlyNotOrdinaryStagedChanges(string statusLine)
    {
        var directory = CreateDirectory("conflict-" + Guid.NewGuid().ToString("N"));
        var runner = ScriptedRunner(directory, status: statusLine + "\0");

        var result = await CreateInspector(runner).InspectAsync(directory);

        var change = Assert.Single(result.ChangedFiles);
        Assert.Equal(RepositoryChangedFileKind.Conflicted, change.Kind);
    }

    [Fact]
    public async Task DetachedHead_PreservesHeadWithoutFabricatingBranch()
    {
        var directory = CreateDirectory("detached");
        var runner = ScriptedRunner(directory, branchExitCode: 1);

        var result = await CreateInspector(runner).InspectAsync(directory);

        Assert.True(result.IsDetachedHead);
        Assert.Null(result.BranchName);
        Assert.Equal("abcdef1234567890", result.HeadSha);
    }

    [Fact]
    public async Task SymbolicRef_ExpectedNonSymbolicExit_IsDetached()
    {
        var directory = CreateDirectory("symbolic-ref-detached");
        var runner = ScriptedRunner(directory, branchExitCode: 1);

        var result = await CreateInspector(runner).InspectAsync(directory);

        Assert.True(result.IsDetachedHead);
        Assert.Null(result.BranchName);
        Assert.NotEqual(RepositoryVerificationStatus.Failed, result.Status);
    }

    [Fact]
    public async Task SymbolicRef_UnexpectedFailure_IsFailed()
    {
        var directory = CreateDirectory("symbolic-ref-unexpected");
        var runner = ScriptedRunner(
            directory,
            branchExitCode: 128,
            branchError: "fatal: unable to read config file");

        var result = await CreateInspector(runner).InspectAsync(directory);

        Assert.Equal(RepositoryVerificationStatus.Failed, result.Status);
        Assert.False(result.IsDetachedHead);
    }

    [Fact]
    public async Task UnbornRepository_HasBranchAndUncreatedHead()
    {
        var directory = CreateDirectory("unborn");
        var runner = ScriptedRunner(directory, headExitCode: 128, status: "?? README.md\0");

        var result = await CreateInspector(runner).InspectAsync(directory);

        Assert.Equal(RepositoryVerificationStatus.AvailableDirty, result.Status);
        Assert.Equal("main", result.BranchName);
        Assert.False(result.IsDetachedHead);
        Assert.Null(result.HeadSha);
        Assert.Equal(1, result.ChangedFileTotal);
    }

    [Fact]
    public async Task Head_AbsentInValidUnbornRepository_HasNoSha()
    {
        var directory = CreateDirectory("unborn-head");
        var runner = ScriptedRunner(
            directory,
            headExitCode: 128,
            headError: "fatal: ambiguous argument 'HEAD': unknown revision or path not in the working tree.",
            status: string.Empty);

        var result = await CreateInspector(runner).InspectAsync(directory);

        Assert.Null(result.HeadSha);
        Assert.Null(result.HeadShortSha);
        Assert.NotEqual(RepositoryVerificationStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Root_NotRepository_IsNotGitRepository()
    {
        var directory = CreateDirectory("root-not-repository");
        var runner = ScriptedRunner(directory, rootExitCode: 128);

        var result = await CreateInspector(runner).InspectAsync(directory);

        Assert.Equal(RepositoryVerificationStatus.NotGitRepository, result.Status);
    }

    [Fact]
    public async Task Root_PermissionOrUnexpectedFailure_IsNotNotGitRepository()
    {
        var directory = CreateDirectory("root-permission");
        var runner = ScriptedRunner(
            directory,
            rootExitCode: 128,
            rootError: "fatal: unable to read current working directory: Permission denied");

        var result = await CreateInspector(runner).InspectAsync(directory);

        Assert.Equal(RepositoryVerificationStatus.Failed, result.Status);
        Assert.NotEqual(RepositoryVerificationStatus.NotGitRepository, result.Status);
    }

    [Fact]
    public async Task GitDisappearsAfterVersion_DoesNotFabricateRepositoryState()
    {
        var directory = CreateDirectory("git-disappears");
        var runner = ScriptedRunner(directory, rootCouldNotStart: true);

        var result = await CreateInspector(runner).InspectAsync(directory);

        Assert.Equal(RepositoryVerificationStatus.GitUnavailable, result.Status);
        Assert.False(result.IsDetachedHead);
        Assert.Null(result.BranchName);
        Assert.Null(result.HeadSha);
    }

    [Fact]
    public async Task UnexpectedUpstreamFailure_DoesNotCreateFakeUpstream()
    {
        var directory = CreateDirectory("upstream-disappears");
        var runner = ScriptedRunner(directory, upstreamCouldNotStart: true);

        var result = await CreateInspector(runner).InspectAsync(directory);

        Assert.Equal(RepositoryVerificationStatus.GitUnavailable, result.Status);
        Assert.Null(result.UpstreamBranch);
    }

    [Fact]
    public async Task RepositoryWithoutRemoteAndUpstreamUnavailableAreExplicit()
    {
        var directory = CreateDirectory("no-remote");
        var runner = ScriptedRunner(directory, upstreamExitCode: 128);

        var result = await CreateInspector(runner).InspectAsync(directory);

        Assert.Empty(result.Remotes);
        Assert.Null(result.UpstreamBranch);
        Assert.Equal(RepositoryRemoteComparison.NotConfigured, result.RemoteComparison);
    }

    [Fact]
    public async Task ChangedFiles_AreBoundedAndTruncatedTruthfully()
    {
        var directory = CreateDirectory("bounded");
        var status = string.Concat(Enumerable.Range(1, 105).Select(index => $"?? file-{index}.txt\0"));
        var runner = ScriptedRunner(directory, status: status);

        var result = await CreateInspector(runner).InspectAsync(directory);

        Assert.Equal(105, result.ChangedFileTotal);
        Assert.Equal(100, result.ChangedFiles.Count);
        Assert.True(result.ChangedFilesTruncated);
    }

    [Fact]
    public async Task GitTimeout_ReturnsBoundedFailure()
    {
        var directory = CreateDirectory("timeout");
        var runner = new FakeGitCommandRunner
        {
            Handler = _ => Task.FromResult(Result(timedOut: true))
        };

        var result = await CreateInspector(runner).InspectAsync(directory);

        Assert.Equal(RepositoryVerificationStatus.Failed, result.Status);
        Assert.Equal("Git verification timed out.", result.SafeErrorMessage);
    }

    [Fact]
    public async Task Cancellation_IsHonored()
    {
        var directory = CreateDirectory("cancelled");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runner = new FakeGitCommandRunner
        {
            Handler = _ => Task.FromResult(Result(cancelled: true))
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateInspector(runner).InspectAsync(directory, cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task PathProbe_Missing_ReturnsPathMissingWithoutRunningGit()
    {
        var directory = CreateDirectory("probe-missing");
        var runner = ScriptedRunner(directory);
        var probe = new FakeLocalPathProbe
        {
            Handler = (_, _) => Task.FromResult(new LocalPathProbeResult(LocalPathProbeStatus.Missing))
        };

        var result = await new GitLocalRepositoryInspector(runner, probe).InspectAsync(directory);

        Assert.Equal(RepositoryVerificationStatus.PathMissing, result.Status);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task PathProbe_NotADirectory_ReturnsPathUnavailableWithoutRunningGit()
    {
        var directory = CreateDirectory("probe-file");
        var runner = ScriptedRunner(directory);
        var probe = new FakeLocalPathProbe
        {
            Handler = (_, _) => Task.FromResult(new LocalPathProbeResult(LocalPathProbeStatus.NotADirectory))
        };

        var result = await new GitLocalRepositoryInspector(runner, probe).InspectAsync(directory);

        Assert.Equal(RepositoryVerificationStatus.PathUnavailable, result.Status);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task PathProbe_Unavailable_ReturnsPathUnavailableAndDiffersFromMissing()
    {
        var directory = CreateDirectory("probe-unavailable");
        var runner = ScriptedRunner(directory);
        var probe = new FakeLocalPathProbe
        {
            Handler = (_, _) => Task.FromResult(new LocalPathProbeResult(LocalPathProbeStatus.Unavailable))
        };

        var result = await new GitLocalRepositoryInspector(runner, probe).InspectAsync(directory);

        Assert.Equal(RepositoryVerificationStatus.PathUnavailable, result.Status);
        Assert.NotEqual(RepositoryVerificationStatus.PathMissing, result.Status);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task PathProbe_Timeout_ReturnsBoundedUnavailableWithoutRunningGit()
    {
        var directory = CreateDirectory("probe-timeout");
        var runner = ScriptedRunner(directory);
        var probe = new FakeLocalPathProbe
        {
            Handler = (_, _) => Task.FromResult(new LocalPathProbeResult(LocalPathProbeStatus.Unavailable, TimedOut: true))
        };

        var result = await new GitLocalRepositoryInspector(runner, probe).InspectAsync(directory);

        Assert.Equal(RepositoryVerificationStatus.PathUnavailable, result.Status);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task PathProbe_Cancellation_IsHonoredWithoutRunningGit()
    {
        var directory = CreateDirectory("probe-cancelled");
        var runner = ScriptedRunner(directory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var probe = new FakeLocalPathProbe
        {
            Handler = (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new LocalPathProbeResult(LocalPathProbeStatus.AvailableDirectory));
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new GitLocalRepositoryInspector(runner, probe).InspectAsync(directory, cancellationToken: cancellation.Token));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task LocalPath_IsPassedAsSingleProcessArgument()
    {
        var directory = CreateDirectory("path with spaces");
        var runner = ScriptedRunner(directory);

        await CreateInspector(runner).InspectAsync(directory);

        Assert.NotEmpty(runner.Calls);
        Assert.All(runner.Calls.Where(call => call.Count > 1 && call[0] == "-C"), call =>
        {
            Assert.Equal(directory, call[1]);
            Assert.Equal(1, call.Count(argument => string.Equals(argument, directory, StringComparison.Ordinal)));
        });
    }

    [Fact]
    public void CommandsUseNoShellAndApplySafeGitEnvironment()
    {
        var startInfo = SystemGitCommandRunner.CreateStartInfo(["-C", "C:\\path with spaces", "status"]);

        Assert.False(startInfo.UseShellExecute);
        Assert.Equal("0", startInfo.Environment["GIT_TERMINAL_PROMPT"]);
        Assert.Equal("0", startInfo.Environment["GIT_OPTIONAL_LOCKS"]);
        Assert.Equal(["-C", "C:\\path with spaces", "status"], startInfo.ArgumentList);
    }

    [Fact]
    public void ScpStyleRemote_IsSanitizedWithoutUserInfo()
    {
        var sanitized = GitLocalRepositoryInspector.SanitizeRemoteUrl("git@github.com:org/repo.git");

        Assert.Equal("github.com:org/repo.git", sanitized);
        Assert.DoesNotContain("@", sanitized, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private GitLocalRepositoryInspector CreateInspector(FakeGitCommandRunner runner) =>
        new(runner);

    private string CreateDirectory(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static FakeGitCommandRunner ScriptedRunner(
        string directory,
        string status = "",
        string remote = "",
        int rootExitCode = 0,
        string? rootError = null,
        int branchExitCode = 0,
        string? branchError = null,
        int headExitCode = 0,
        string? headError = null,
        int upstreamExitCode = 0,
        bool rootCouldNotStart = false,
        bool upstreamCouldNotStart = false)
    {
        var runner = new FakeGitCommandRunner();
        runner.Handler = arguments =>
        {
            if (arguments.SequenceEqual(["--version"]))
            {
                return Task.FromResult(Result(output: "git version 2.0"));
            }

            if (arguments.Contains("--show-toplevel"))
            {
                if (rootCouldNotStart)
                {
                    return Task.FromResult(Result(couldNotStart: true));
                }

                var defaultRootError = rootExitCode != 0
                    ? "fatal: not a git repository (or any of the parent directories): .git"
                    : string.Empty;
                return Task.FromResult(Result(rootExitCode, directory, error: rootError ?? defaultRootError));
            }

            if (arguments.Contains("symbolic-ref"))
            {
                return Task.FromResult(Result(
                    branchExitCode,
                    branchExitCode == 0 ? "main" : "",
                    error: branchError ?? string.Empty));
            }

            if (arguments.Contains("--verify"))
            {
                var defaultHeadError = headExitCode != 0
                    ? "fatal: ambiguous argument 'HEAD': unknown revision or path not in the working tree."
                    : string.Empty;
                return Task.FromResult(Result(
                    headExitCode,
                    headExitCode == 0 ? "abcdef1234567890" : "",
                    error: headError ?? defaultHeadError));
            }

            if (arguments.Contains("@{upstream}"))
            {
                if (upstreamCouldNotStart)
                {
                    return Task.FromResult(Result(couldNotStart: true));
                }

                return Task.FromResult(Result(upstreamExitCode, upstreamExitCode == 0 ? "origin/main" : ""));
            }

            if (arguments.Contains("status"))
            {
                return Task.FromResult(Result(0, status));
            }

            if (arguments.Contains("remote"))
            {
                return Task.FromResult(Result(0, remote));
            }

            return Task.FromResult(Result(1));
        };
        return runner;
    }

    private static GitCommandResult Result(
        int exitCode = 0,
        string output = "",
        string error = "",
        bool timedOut = false,
        bool cancelled = false,
        bool couldNotStart = false) =>
        new(exitCode, output, error, timedOut, cancelled, couldNotStart);

    private sealed class FakeGitCommandRunner : IGitCommandRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Func<IReadOnlyList<string>, Task<GitCommandResult>> Handler { get; set; } =
            _ => Task.FromResult(Result(1));

        public Task<GitCommandResult> RunAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(arguments.ToArray());
            return Handler(arguments);
        }
    }

    private sealed class FakeLocalPathProbe : ILocalPathProbe
    {
        public Func<string, CancellationToken, Task<LocalPathProbeResult>> Handler { get; set; } =
            (_, _) => Task.FromResult(new LocalPathProbeResult(LocalPathProbeStatus.AvailableDirectory));

        public Task<LocalPathProbeResult> ProbeAsync(string path, CancellationToken cancellationToken = default) =>
            Handler(path, cancellationToken);
    }
}
