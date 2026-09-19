namespace AIUsageMonitor.Application.Projects;

/// <summary>
/// Provider-independent result produced by Infrastructure for one local path.
/// </summary>
public sealed class LocalRepositoryInspection
{
    public LocalRepositoryInspection(
        RepositoryVerificationStatus status,
        string registeredLocalPath,
        string? repositoryRoot = null,
        bool? localPathIsRepositoryRoot = null,
        string? branchName = null,
        bool isDetachedHead = false,
        string? headSha = null,
        string? headShortSha = null,
        string? upstreamBranch = null,
        bool? isClean = null,
        int changedFileTotal = 0,
        IReadOnlyList<RepositoryChangedFile>? changedFiles = null,
        bool changedFilesTruncated = false,
        IReadOnlyList<RepositoryRemote>? remotes = null,
        RepositoryRemoteComparison remoteComparison = RepositoryRemoteComparison.NotConfigured,
        DateTimeOffset? capturedAt = null,
        string? safeErrorMessage = null,
        ProjectWorkspaceDiscovery? workspace = null)
    {
        if (string.IsNullOrWhiteSpace(registeredLocalPath))
        {
            throw new ArgumentException("Registered local path is required.", nameof(registeredLocalPath));
        }

        if (changedFileTotal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(changedFileTotal));
        }

        if (changedFilesTruncated && changedFileTotal <= (changedFiles?.Count ?? 0))
        {
            throw new ArgumentException(
                "Truncated change evidence must contain fewer entries than the total.",
                nameof(changedFilesTruncated));
        }

        Status = status;
        RegisteredLocalPath = registeredLocalPath;
        RepositoryRoot = repositoryRoot;
        LocalPathIsRepositoryRoot = localPathIsRepositoryRoot;
        BranchName = branchName;
        IsDetachedHead = isDetachedHead;
        HeadSha = headSha;
        HeadShortSha = headShortSha;
        UpstreamBranch = upstreamBranch;
        IsClean = isClean;
        ChangedFileTotal = changedFileTotal;
        ChangedFiles = (changedFiles ?? Array.Empty<RepositoryChangedFile>()).ToArray();
        ChangedFilesTruncated = changedFilesTruncated;
        Remotes = (remotes ?? Array.Empty<RepositoryRemote>()).ToArray();
        RemoteComparison = remoteComparison;
        CapturedAt = capturedAt ?? DateTimeOffset.UtcNow;
        SafeErrorMessage = safeErrorMessage;
        Workspace = workspace;
    }

    public RepositoryVerificationStatus Status { get; }

    public string RegisteredLocalPath { get; }

    public string? RepositoryRoot { get; }

    public bool? LocalPathIsRepositoryRoot { get; }

    public string? BranchName { get; }

    public bool IsDetachedHead { get; }

    public string? HeadSha { get; }

    public string? HeadShortSha { get; }

    public string? UpstreamBranch { get; }

    public bool? IsClean { get; }

    public int ChangedFileTotal { get; }

    public IReadOnlyList<RepositoryChangedFile> ChangedFiles { get; }

    public bool ChangedFilesTruncated { get; }

    public IReadOnlyList<RepositoryRemote> Remotes { get; }

    public RepositoryRemoteComparison RemoteComparison { get; }

    public DateTimeOffset CapturedAt { get; }

    public string? SafeErrorMessage { get; }

    public ProjectWorkspaceDiscovery? Workspace { get; }
}
