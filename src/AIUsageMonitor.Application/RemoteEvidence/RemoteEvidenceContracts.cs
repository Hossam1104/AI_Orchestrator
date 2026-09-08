using AIUsageMonitor.Application.Projects;

namespace AIUsageMonitor.Application.RemoteEvidence;

public enum RemoteRepositoryProvider
{
    GitHub,
    AzureRepos
}

public enum RemoteEvidenceState
{
    Available,
    Partial,
    AuthenticationRequired,
    PermissionDenied,
    Unsupported,
    Unavailable,
    Stale,
    NotConfigured,
    InvalidResponse,
    RateLimited,
    Cancelled
}

public enum RemoteEvidenceSource
{
    Unknown,
    GitHubRest,
    AzureDevOpsRest
}

public enum RemoteStatusKind
{
    CommitStatus,
    CheckRun,
    PullRequestStatus,
    WorkflowRun,
    Build
}

public enum RemoteCiState
{
    Passing,
    Failing,
    Pending,
    Cancelled,
    NoEvidence,
    Unknown
}

public enum RemoteMergeability
{
    Available,
    Conflicting,
    Calculating,
    Unknown,
    Unsupported,
    Unavailable
}

public sealed class RemoteRepositoryEvidenceRequest
{
    public RemoteRepositoryEvidenceRequest(
        Guid projectId,
        string? repositoryProvider,
        string? repositoryUrl,
        string? repositoryId = null,
        IReadOnlyDictionary<string, string?>? repositoryMetadata = null,
        string? requestedBranch = null,
        int? pullRequestNumber = null,
        string? credentialReference = null)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project id cannot be empty.", nameof(projectId));
        }

        if (pullRequestNumber is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pullRequestNumber));
        }

        ProjectId = projectId;
        RepositoryProvider = Normalize(repositoryProvider);
        RepositoryUrl = Normalize(repositoryUrl);
        RepositoryId = Normalize(repositoryId);
        RepositoryMetadata = CopyMetadata(repositoryMetadata);
        RequestedBranch = Normalize(requestedBranch);
        PullRequestNumber = pullRequestNumber;
        CredentialReference = Normalize(credentialReference) ??
            GetMetadataValue(RepositoryMetadata, "credentialReference", "credentialRef", "authRef");
    }

    public Guid ProjectId { get; }

    public string? RepositoryProvider { get; }

    public string? RepositoryUrl { get; }

    public string? RepositoryId { get; }

    public IReadOnlyDictionary<string, string?> RepositoryMetadata { get; }

    public string? RequestedBranch { get; }

    public int? PullRequestNumber { get; }

    public string? CredentialReference { get; }

    public static RemoteRepositoryEvidenceRequest FromProject(
        Project project,
        string? requestedBranch = null,
        int? pullRequestNumber = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        return new(
            project.Id,
            project.RepositoryProvider,
            project.RepositoryUrl,
            project.RepositoryId,
            project.RepositoryMetadata,
            requestedBranch,
            pullRequestNumber);
    }

    private static IReadOnlyDictionary<string, string?> CopyMetadata(
        IReadOnlyDictionary<string, string?>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, string?>(metadata, StringComparer.OrdinalIgnoreCase);
    }

    private static string? GetMetadataValue(
        IReadOnlyDictionary<string, string?> metadata,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public interface IRemoteRepositoryEvidenceProvider
{
    RemoteRepositoryProvider Provider { get; }

    Task<RemoteRepositoryEvidence> InspectAsync(
        RemoteRepositoryEvidenceRequest request,
        CancellationToken cancellationToken = default);
}

public interface IRemoteRepositoryEvidenceService
{
    Task<RemoteRepositoryEvidence> InspectAsync(
        Project project,
        string? requestedBranch = null,
        int? pullRequestNumber = null,
        CancellationToken cancellationToken = default);
}

public sealed class RemoteRepositoryIdentity
{
    public RemoteRepositoryIdentity(
        RemoteRepositoryProvider provider,
        RemoteEvidenceSource source,
        string providerRepositoryId,
        string canonicalName,
        string ownerOrOrganization,
        string repositoryName,
        string defaultBranch,
        string? projectName = null,
        Uri? webUrl = null)
    {
        Provider = provider;
        Source = source;
        ProviderRepositoryId = Required(providerRepositoryId, nameof(providerRepositoryId));
        CanonicalName = Required(canonicalName, nameof(canonicalName));
        OwnerOrOrganization = Required(ownerOrOrganization, nameof(ownerOrOrganization));
        RepositoryName = Required(repositoryName, nameof(repositoryName));
        DefaultBranch = Required(defaultBranch, nameof(defaultBranch));
        ProjectName = Optional(projectName);
        WebUrl = webUrl;
    }

    public RemoteRepositoryProvider Provider { get; }

    public RemoteEvidenceSource Source { get; }

    public string ProviderRepositoryId { get; }

    public string CanonicalName { get; }

    public string OwnerOrOrganization { get; }

    public string RepositoryName { get; }

    public string DefaultBranch { get; }

    public string? ProjectName { get; }

    public Uri? WebUrl { get; }

    private static string Required(string value, string parameterName)
    {
        var normalized = Optional(value);
        return normalized is null
            ? throw new ArgumentException("Remote identity value is required.", parameterName)
            : normalized;
    }

    private static string? Optional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= 1_024
            ? normalized
            : throw new ArgumentException("Remote identity value exceeds its supported bound.", nameof(value));
    }
}

public sealed class RemoteBranchEvidence
{
    public RemoteBranchEvidence(string branchName, string commitId, bool isDefaultBranch)
    {
        BranchName = Required(branchName, nameof(branchName));
        CommitId = Required(commitId, nameof(commitId));
        IsDefaultBranch = isDefaultBranch;
    }

    public string BranchName { get; }

    public string CommitId { get; }

    public bool IsDefaultBranch { get; }

    private static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value) || value.Length > 1_024
            ? throw new ArgumentException("Remote branch value is invalid.", parameterName)
            : value.Trim();
}

public sealed class RemotePullRequestEvidence
{
    public RemotePullRequestEvidence(
        string id,
        string state,
        bool? isDraft,
        string? sourceBranch,
        string? targetBranch,
        string? headCommitId,
        string? baseCommitId,
        RemoteMergeability mergeability,
        Uri? webUrl = null,
        string? title = null,
        string? body = null,
        string? mergeCommitId = null,
        string? providerNodeId = null)
    {
        Id = Required(id, nameof(id));
        State = Required(state, nameof(state));
        IsDraft = isDraft;
        SourceBranch = Optional(sourceBranch);
        TargetBranch = Optional(targetBranch);
        HeadCommitId = Optional(headCommitId);
        BaseCommitId = Optional(baseCommitId);
        Mergeability = mergeability;
        WebUrl = webUrl;
        Title = Optional(title);
        Body = body is null ? null : body.Length <= 20_000 ? body : body[..19_999] + "…";
        MergeCommitId = Optional(mergeCommitId);
        ProviderNodeId = Optional(providerNodeId);
    }

    public string Id { get; }

    public string State { get; }

    public bool? IsDraft { get; }

    public string? SourceBranch { get; }

    public string? TargetBranch { get; }

    public string? HeadCommitId { get; }

    public string? BaseCommitId { get; }

    public RemoteMergeability Mergeability { get; }

    public Uri? WebUrl { get; }

    public string? Title { get; }

    public string? Body { get; }

    public string? MergeCommitId { get; }

    public string? ProviderNodeId { get; }

    private static string Required(string value, string parameterName) =>
        Optional(value) is { } normalized
            ? normalized
            : throw new ArgumentException("Pull request value is required.", parameterName);

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= 1_024
            ? value.Trim()
            : throw new ArgumentException("Pull request value exceeds its supported bound.", nameof(value));
}

public sealed class RemoteReviewEvidence
{
    public RemoteReviewEvidence(
        string reviewer,
        string state,
        bool requested,
        DateTimeOffset? submittedAt = null,
        string? reviewId = null)
    {
        Reviewer = Required(reviewer, nameof(reviewer));
        State = Required(state, nameof(state));
        Requested = requested;
        SubmittedAt = submittedAt;
        ReviewId = Optional(reviewId);
    }

    public string Reviewer { get; }

    public string State { get; }

    public bool Requested { get; }

    public DateTimeOffset? SubmittedAt { get; }

    public string? ReviewId { get; }

    private static string Required(string value, string parameterName) =>
        Optional(value) is { } normalized
            ? normalized
            : throw new ArgumentException("Review value is required.", parameterName);

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= 1_024
            ? value.Trim()
            : throw new ArgumentException("Review value exceeds its supported bound.", nameof(value));
}

public sealed class RemoteStatusEvidence
{
    public RemoteStatusEvidence(
        RemoteStatusKind kind,
        string name,
        string state,
        string? conclusion = null,
        string? commitId = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        Uri? webUrl = null)
    {
        Kind = kind;
        Name = Required(name, nameof(name));
        State = Required(state, nameof(state));
        Conclusion = Optional(conclusion);
        CommitId = Optional(commitId);
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        WebUrl = webUrl;
    }

    public RemoteStatusKind Kind { get; }

    public string Name { get; }

    public string State { get; }

    public string? Conclusion { get; }

    public string? CommitId { get; }

    public DateTimeOffset? CreatedAt { get; }

    public DateTimeOffset? UpdatedAt { get; }

    public Uri? WebUrl { get; }

    private static string Required(string value, string parameterName) =>
        Optional(value) is { } normalized
            ? normalized
            : throw new ArgumentException("Status value is required.", parameterName);

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= 1_024
            ? value.Trim()
            : throw new ArgumentException("Status value exceeds its supported bound.", nameof(value));
}

public sealed class RemoteCiRunEvidence
{
    public RemoteCiRunEvidence(
        RemoteStatusKind sourceKind,
        string id,
        string name,
        RemoteCiState state,
        string? conclusion = null,
        string? branch = null,
        string? headCommitId = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        DateTimeOffset? completedAt = null,
        Uri? webUrl = null)
    {
        SourceKind = sourceKind;
        Id = Required(id, nameof(id));
        Name = Required(name, nameof(name));
        State = state;
        Conclusion = Optional(conclusion);
        Branch = Optional(branch);
        HeadCommitId = Optional(headCommitId);
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        CompletedAt = completedAt;
        WebUrl = webUrl;
    }

    public RemoteStatusKind SourceKind { get; }

    public string Id { get; }

    public string Name { get; }

    public RemoteCiState State { get; }

    public string? Conclusion { get; }

    public string? Branch { get; }

    public string? HeadCommitId { get; }

    public DateTimeOffset? CreatedAt { get; }

    public DateTimeOffset? UpdatedAt { get; }

    public DateTimeOffset? CompletedAt { get; }

    public Uri? WebUrl { get; }

    private static string Required(string value, string parameterName) =>
        Optional(value) is { } normalized
            ? normalized
            : throw new ArgumentException("CI value is required.", parameterName);

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= 1_024
            ? value.Trim()
            : throw new ArgumentException("CI value exceeds its supported bound.", nameof(value));
}

public sealed class RemoteRepositoryEvidence
{
    public RemoteRepositoryEvidence(
        Guid projectId,
        RemoteEvidenceState state,
        RemoteEvidenceSource source,
        DateTimeOffset capturedAt,
        RemoteRepositoryIdentity? repository = null,
        RemoteEvidenceState repositoryState = RemoteEvidenceState.NotConfigured,
        RemoteBranchEvidence? branch = null,
        RemoteEvidenceState branchState = RemoteEvidenceState.NotConfigured,
        RemotePullRequestEvidence? pullRequest = null,
        RemoteEvidenceState pullRequestState = RemoteEvidenceState.NotConfigured,
        IReadOnlyList<RemoteReviewEvidence>? reviews = null,
        RemoteEvidenceState reviewState = RemoteEvidenceState.NotConfigured,
        IReadOnlyList<RemoteStatusEvidence>? statuses = null,
        RemoteEvidenceState statusState = RemoteEvidenceState.NotConfigured,
        IReadOnlyList<RemoteStatusEvidence>? checks = null,
        RemoteEvidenceState checkState = RemoteEvidenceState.NotConfigured,
        IReadOnlyList<RemoteCiRunEvidence>? ciRuns = null,
        RemoteEvidenceState ciState = RemoteEvidenceState.NotConfigured,
        RemoteCiState ciResult = RemoteCiState.Unknown,
        IReadOnlyList<string>? limitations = null,
        string? safeErrorMessage = null)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project id cannot be empty.", nameof(projectId));
        }

        ProjectId = projectId;
        State = state;
        Source = source;
        CapturedAt = capturedAt;
        Repository = repository;
        RepositoryState = repositoryState;
        Branch = branch;
        BranchState = branchState;
        PullRequest = pullRequest;
        PullRequestState = pullRequestState;
        Reviews = Copy(reviews);
        ReviewState = reviewState;
        Statuses = Copy(statuses);
        StatusState = statusState;
        Checks = Copy(checks);
        CheckState = checkState;
        CiRuns = Copy(ciRuns);
        CiState = ciState;
        CiResult = ciResult;
        Limitations = Copy(limitations);
        SafeErrorMessage = Limit(safeErrorMessage);
    }

    public Guid ProjectId { get; }

    public RemoteEvidenceState State { get; }

    public RemoteEvidenceSource Source { get; }

    public DateTimeOffset CapturedAt { get; }

    public RemoteRepositoryIdentity? Repository { get; }

    public RemoteEvidenceState RepositoryState { get; }

    public RemoteBranchEvidence? Branch { get; }

    public RemoteEvidenceState BranchState { get; }

    public RemotePullRequestEvidence? PullRequest { get; }

    public RemoteEvidenceState PullRequestState { get; }

    public IReadOnlyList<RemoteReviewEvidence> Reviews { get; }

    public RemoteEvidenceState ReviewState { get; }

    public IReadOnlyList<RemoteStatusEvidence> Statuses { get; }

    public RemoteEvidenceState StatusState { get; }

    public IReadOnlyList<RemoteStatusEvidence> Checks { get; }

    public RemoteEvidenceState CheckState { get; }

    public IReadOnlyList<RemoteCiRunEvidence> CiRuns { get; }

    public RemoteEvidenceState CiState { get; }

    public RemoteCiState CiResult { get; }

    public IReadOnlyList<string> Limitations { get; }

    public string? SafeErrorMessage { get; }

    private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T>? values) =>
        values is null || values.Count == 0 ? Array.Empty<T>() : values.ToArray();

    private static string? Limit(string? value) =>
        value is null ? null : value.Length <= 1_000 ? value : value[..999] + "…";
}
