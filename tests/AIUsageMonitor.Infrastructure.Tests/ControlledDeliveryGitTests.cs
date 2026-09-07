using System.Diagnostics;
using AIUsageMonitor.Application.Delivery;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Infrastructure.Git;

namespace AIUsageMonitor.Infrastructure.Tests;

public sealed class ControlledDeliveryGitTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "apo-63-git-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExactCommitAndNormalPush_VerifyNewHeadAndBareRemote()
    {
        var working = Path.Combine(_root, "working");
        var bare = Path.Combine(_root, "origin.git");
        Initialize(working, bare);
        var initial = Git(working, "rev-parse", "HEAD").Trim();
        File.WriteAllText(Path.Combine(working, "tracked.txt"), "updated\n");

        var commit = await new LocalDeliveryGitService().CommitExactChangesAsync(Command(working, bare, "task", initial, initial, ["tracked.txt"]));
        Assert.True(commit.Status == SourceControlDeliveryStatus.Verified, commit.ErrorMessage);
        Assert.NotNull(commit.NewHeadSha);

        var pushCommand = Command(working, bare, "task", initial, commit.NewHeadSha!, ["tracked.txt"]);
        var pushed = await new LocalDeliveryGitService().PushExactHeadAsync(pushCommand);

        Assert.True(pushed.Status == SourceControlDeliveryStatus.Verified, pushed.ErrorMessage);
        Assert.Equal(commit.NewHeadSha, GitBare(bare, "rev-parse", "refs/heads/task").Trim());
    }

    [Fact]
    public async Task UnexpectedUntrackedFile_IsRejectedBeforeStaging()
    {
        var working = Path.Combine(_root, "working");
        var bare = Path.Combine(_root, "origin.git");
        Initialize(working, bare);
        var initial = Git(working, "rev-parse", "HEAD").Trim();
        File.WriteAllText(Path.Combine(working, "tracked.txt"), "updated\n");
        File.WriteAllText(Path.Combine(working, "unexpected.txt"), "owner change\n");

        var result = await new LocalDeliveryGitService().CommitExactChangesAsync(Command(working, bare, "task", initial, initial, ["tracked.txt"]));

        Assert.Equal(SourceControlDeliveryStatus.Blocked, result.Status);
        Assert.Contains("unexpected", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Git(working, "diff", "--name-only", "--cached"));
    }

    [Fact]
    public async Task PushOnDefaultBranch_IsRejectedWithoutMutation()
    {
        var working = Path.Combine(_root, "working");
        var bare = Path.Combine(_root, "origin.git");
        Initialize(working, bare);
        var head = Git(working, "rev-parse", "HEAD").Trim();
        var result = await new LocalDeliveryGitService().PushExactHeadAsync(Command(working, bare, "main", head, head, []));

        Assert.Equal(SourceControlDeliveryStatus.Blocked, result.Status);
        Assert.False(result.MutationSent);
    }

    [Fact]
    public async Task ReadOnlyPushReconciliation_ReturnsAlreadyAppliedForExactRemoteHead()
    {
        var working = Path.Combine(_root, "working");
        var bare = Path.Combine(_root, "origin.git");
        Initialize(working, bare);
        var head = Git(working, "rev-parse", "HEAD").Trim();
        var command = Command(working, bare, "task", head, head, []);
        var service = new LocalDeliveryGitService();
        var pushed = await service.PushExactHeadAsync(command);

        var reconciled = await service.ReconcileAsync(command);

        Assert.Equal(SourceControlDeliveryStatus.Verified, pushed.Status);
        Assert.Equal(SourceControlDeliveryStatus.AlreadyApplied, reconciled.Status);
        Assert.True(reconciled.MutationSent);
    }

    private SourceControlDeliveryCommand Command(string working, string bare, string branch, string baseSha, string headSha, IReadOnlyList<string> paths)
    {
        var contract = new PlanningExecutionContractReference(Guid.NewGuid(), 1, 1, new string('a', 64));
        var target = new SourceControlDeliveryTarget(
            AIUsageMonitor.Application.RemoteEvidence.RemoteRepositoryProvider.GitHub,
            bare, "local-repository", "local/repository", "main", baseSha, branch, headSha);
        var evidence = new SourceControlDeliveryEvidence(new string('b', 64), headSha, baseSha);
        return new(
            Guid.NewGuid(), Guid.NewGuid(), "APO-63", contract,
            paths.Count == 0 ? SourceControlDeliveryOperationKind.PushExactHead : SourceControlDeliveryOperationKind.CommitExactChanges,
            target, "test-executor", new string('c', 64), "audit:test", evidence,
            workspacePath: working, expectedParentHeadSha: baseSha, allowedChangedPaths: paths, commitMessage: "controlled delivery test");
    }

    private void Initialize(string working, string bare)
    {
        Directory.CreateDirectory(_root);
        Git(_root, "init", "--bare", bare);
        Git(_root, "init", "-b", "main", working);
        Git(working, "config", "user.email", "apo-test@example.invalid");
        Git(working, "config", "user.name", "APO Test");
        File.WriteAllText(Path.Combine(working, "tracked.txt"), "initial\n");
        Git(working, "add", "--", "tracked.txt");
        Git(working, "commit", "-m", "initial");
        Git(working, "remote", "add", "origin", bare);
        Git(working, "push", "origin", "main:main");
        Git(working, "switch", "-c", "task");
    }

    private static string Git(string directory, params string[] arguments) => Run("git", directory, arguments);

    private static string GitBare(string directory, params string[] arguments) => Run("git", directory, arguments);

    private static string Run(string fileName, string directory, IReadOnlyList<string> arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = directory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        Assert.True(process.Start());
        process.WaitForExit();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.ExitCode == 0, error);
        return output;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }
}
