using System.Security.Cryptography;
using System.Text;
using AIUsageMonitor.Application.Approvals;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.RemoteEvidence;
using AIUsageMonitor.Application.Trackers;
using AIUsageMonitor.Application.Validation;
using AIUsageMonitor.Application.Workspaces;

namespace AIUsageMonitor.Application.Delivery;

public static class SourceControlDeliveryLimits
{
    public const int MaxWorkItemIdentityLength = 300;
    public const int MaxActorReferenceLength = 300;
    public const int MaxAuditIdentityLength = 300;
    public const int MaxRefLength = 300;
    public const int MaxShaLength = 128;
    public const int MaxRepositoryIdentityLength = 1_024;
    public const int MaxRepositoryUrlLength = 1_024;
    public const int MaxTitleLength = 300;
    public const int MaxBodyLength = 20_000;
    public const int MaxCommentLength = 10_000;
    public const int MaxReviewers = 20;
    public const int MaxChangedPaths = 128;
    public const int MaxPathLength = 512;
    public const int MaxCommitMessageLength = 500;
    public const int MaxDiagnosticLength = 2_000;
    public const int MaxAuditRecords = 4_096;
}

public enum SourceControlDeliveryOperationKind
{
    CommitExactChanges,
    PushExactHead,
    CreateDraftPullRequest,
    UpdatePullRequestMetadata,
    AddDeliveryComment,
    RequestReviewers,
    MarkReadyForReview,
    MergePullRequest
}

public enum SourceControlDeliveryStatus
{
    Verified,
    AlreadyApplied,
    Blocked,
    Stale,
    Conflict,
    ReconciliationRequired,
    Failed,
    Unsupported,
    InvalidAuthority
}

public enum SourceControlDeliveryAuditReadState
{
    Missing,
    Found,
    Corrupt,
    Unavailable
}

public enum SourceControlDeliveryAuditEventKind
{
    Planned,
    Attempted,
    Succeeded,
    VerificationFailed,
    ReconciliationRequired,
    Rejected
}

public sealed class SourceControlDeliveryTarget
{
    public SourceControlDeliveryTarget(
        RemoteRepositoryProvider provider,
        string repositoryUrl,
        string providerRepositoryId,
        string canonicalRepositoryIdentity,
        string baseRef,
        string baseSha,
        string headRef,
        string headSha,
        string? pullRequestId = null)
    {
        if (!Enum.IsDefined(provider)) throw new ArgumentException("Repository provider is undefined.", nameof(provider));
        Provider = provider;
        RepositoryUrl = Required(repositoryUrl, nameof(repositoryUrl), SourceControlDeliveryLimits.MaxRepositoryUrlLength);
        ProviderRepositoryId = Required(providerRepositoryId, nameof(providerRepositoryId), SourceControlDeliveryLimits.MaxRepositoryIdentityLength);
        CanonicalRepositoryIdentity = Required(canonicalRepositoryIdentity, nameof(canonicalRepositoryIdentity), SourceControlDeliveryLimits.MaxRepositoryIdentityLength);
        BaseRef = Ref(baseRef, nameof(baseRef));
        BaseSha = Sha(baseSha, nameof(baseSha));
        HeadRef = Ref(headRef, nameof(headRef));
        HeadSha = Sha(headSha, nameof(headSha));
        PullRequestId = Optional(pullRequestId, nameof(pullRequestId), SourceControlDeliveryLimits.MaxRepositoryIdentityLength);
    }

    public RemoteRepositoryProvider Provider { get; }
    public string RepositoryUrl { get; }
    public string ProviderRepositoryId { get; }
    public string CanonicalRepositoryIdentity { get; }
    public string BaseRef { get; }
    public string BaseSha { get; }
    public string HeadRef { get; }
    public string HeadSha { get; }
    public string? PullRequestId { get; }

    public SourceControlDeliveryTarget WithPullRequest(string pullRequestId) => new(
        Provider, RepositoryUrl, ProviderRepositoryId, CanonicalRepositoryIdentity,
        BaseRef, BaseSha, HeadRef, HeadSha, pullRequestId);

    private static string Required(string value, string parameterName, int max) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length > max || value.Any(char.IsControl)
            ? throw new ArgumentException("A bounded target value is required.", parameterName)
            : value.Trim();

    private static string Ref(string value, string parameterName)
    {
        var normalized = Required(value, parameterName, SourceControlDeliveryLimits.MaxRefLength);
        if (normalized.StartsWith("-", StringComparison.Ordinal) || normalized.Contains("..", StringComparison.Ordinal) ||
            normalized.Contains('\0') || normalized.Contains('\\') || normalized.Contains(' '))
            throw new ArgumentException("The branch reference is unsafe.", parameterName);
        return normalized.StartsWith("refs/heads/", StringComparison.Ordinal)
            ? normalized["refs/heads/".Length..]
            : normalized;
    }

    private static string Sha(string value, string parameterName)
    {
        var normalized = Required(value, parameterName, SourceControlDeliveryLimits.MaxShaLength);
        return normalized.Length is >= 7 and <= 128 && normalized.All(Uri.IsHexDigit)
            ? normalized.ToLowerInvariant()
            : throw new ArgumentException("The commit identity is not a valid hexadecimal object id.", parameterName);
    }

    private static string? Optional(string? value, string parameterName, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, parameterName, max);
}

public sealed class SourceControlDeliveryEvidence
{
    public SourceControlDeliveryEvidence(
        string remoteEvidenceFingerprint,
        string expectedHeadSha,
        string expectedBaseSha,
        ValidationGateDecisionReference? validationDecisionReference = null,
        Guid? reviewRootId = null,
        Guid? currentReviewId = null,
        Guid? humanApprovalRequestId = null,
        HumanApprovalEvidenceRevision? humanApprovalEvidenceRevision = null,
        string? currentPolicyReference = null,
        ExecutionRunAuthorityReference? executionRunAuthorityReference = null)
    {
        RemoteEvidenceFingerprint = Sha256(remoteEvidenceFingerprint, nameof(remoteEvidenceFingerprint));
        ExpectedHeadSha = Sha(expectedHeadSha, nameof(expectedHeadSha));
        ExpectedBaseSha = Sha(expectedBaseSha, nameof(expectedBaseSha));
        if (reviewRootId == Guid.Empty || currentReviewId == Guid.Empty || humanApprovalRequestId == Guid.Empty)
            throw new ArgumentException("Evidence identifiers cannot be empty when supplied.");
        if ((reviewRootId.HasValue && !currentReviewId.HasValue) || (!reviewRootId.HasValue && currentReviewId.HasValue))
            throw new ArgumentException("Review root and current review must be supplied together.");
        ValidationDecisionReference = validationDecisionReference;
        ReviewRootId = reviewRootId;
        CurrentReviewId = currentReviewId;
        HumanApprovalRequestId = humanApprovalRequestId;
        HumanApprovalEvidenceRevision = humanApprovalEvidenceRevision;
        CurrentPolicyReference = Optional(currentPolicyReference, nameof(currentPolicyReference), 200);
        ExecutionRunAuthorityReference = executionRunAuthorityReference;
    }

    public string RemoteEvidenceFingerprint { get; }
    public string ExpectedHeadSha { get; }
    public string ExpectedBaseSha { get; }
    public ValidationGateDecisionReference? ValidationDecisionReference { get; }
    public Guid? ReviewRootId { get; }
    public Guid? CurrentReviewId { get; }
    public Guid? HumanApprovalRequestId { get; }
    public HumanApprovalEvidenceRevision? HumanApprovalEvidenceRevision { get; }
    public string? CurrentPolicyReference { get; }
    public ExecutionRunAuthorityReference? ExecutionRunAuthorityReference { get; }

    private static string Sha(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length is < 7 or > 128 || !value.Trim().All(Uri.IsHexDigit)
            ? throw new ArgumentException("A valid hexadecimal commit identity is required.", parameterName)
            : value.Trim().ToLowerInvariant();

    private static string Sha256(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length != 64 || !value.Trim().All(Uri.IsHexDigit)
            ? throw new ArgumentException("A SHA-256 fingerprint is required.", parameterName)
            : value.Trim().ToLowerInvariant();

    private static string? Optional(string? value, string parameterName, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= max && !value.Any(char.IsControl)
            ? value.Trim()
            : throw new ArgumentException("The bounded evidence value is invalid.", parameterName);
}

public sealed class SourceControlDeliveryCommand
{
    public SourceControlDeliveryCommand(
        Guid commandId,
        Guid projectId,
        string workItemIdentity,
        PlanningExecutionContractReference contractReference,
        SourceControlDeliveryOperationKind operationKind,
        SourceControlDeliveryTarget target,
        string actorReference,
        string evidenceRevision,
        string auditIdentity,
        SourceControlDeliveryEvidence evidence,
        string? credentialReference = null,
        string? workspacePath = null,
        string? expectedParentHeadSha = null,
        IReadOnlyList<string>? allowedChangedPaths = null,
        string? commitMessage = null,
        string? pullRequestTitle = null,
        string? pullRequestBody = null,
        string? deliveryComment = null,
        IReadOnlyList<string>? reviewers = null,
        TrackerSynchronizationRequest? trackerRequest = null,
        TrackerSynchronizationPlan? trackerPlan = null,
        TrackerSynchronizationOperation? trackerOperation = null,
        TrackerMutationAuthority? trackerAuthority = null,
        WorkspacePreparationPlanReference? workspaceReference = null)
    {
        if (commandId == Guid.Empty || projectId == Guid.Empty) throw new ArgumentException("Command and project identities are required.");
        if (!Enum.IsDefined(operationKind)) throw new ArgumentException("Delivery operation is undefined.", nameof(operationKind));
        CommandId = commandId;
        ProjectId = projectId;
        WorkItemIdentity = Required(workItemIdentity, nameof(workItemIdentity), SourceControlDeliveryLimits.MaxWorkItemIdentityLength);
        ContractReference = contractReference ?? throw new ArgumentNullException(nameof(contractReference));
        OperationKind = operationKind;
        Target = target ?? throw new ArgumentNullException(nameof(target));
        ActorReference = Required(actorReference, nameof(actorReference), SourceControlDeliveryLimits.MaxActorReferenceLength);
        EvidenceRevision = Sha256(evidenceRevision, nameof(evidenceRevision));
        AuditIdentity = Required(auditIdentity, nameof(auditIdentity), SourceControlDeliveryLimits.MaxAuditIdentityLength);
        Evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        CredentialReference = Optional(credentialReference, nameof(credentialReference), 300);
        WorkspacePath = Optional(workspacePath, nameof(workspacePath), 4_000);
        ExpectedParentHeadSha = expectedParentHeadSha is null ? null : Sha(expectedParentHeadSha, nameof(expectedParentHeadSha));
        AllowedChangedPaths = NormalizePaths(allowedChangedPaths);
        CommitMessage = Optional(commitMessage, nameof(commitMessage), SourceControlDeliveryLimits.MaxCommitMessageLength);
        PullRequestTitle = Optional(pullRequestTitle, nameof(pullRequestTitle), SourceControlDeliveryLimits.MaxTitleLength);
        PullRequestBody = Optional(pullRequestBody, nameof(pullRequestBody), SourceControlDeliveryLimits.MaxBodyLength);
        DeliveryComment = Optional(deliveryComment, nameof(deliveryComment), SourceControlDeliveryLimits.MaxCommentLength);
        Reviewers = NormalizeReviewers(reviewers);
        TrackerRequest = trackerRequest;
        TrackerPlan = trackerPlan;
        TrackerOperation = trackerOperation;
        TrackerAuthority = trackerAuthority;
        WorkspaceReference = workspaceReference;

        if (operationKind is SourceControlDeliveryOperationKind.CommitExactChanges or SourceControlDeliveryOperationKind.PushExactHead && string.IsNullOrWhiteSpace(WorkspacePath))
            throw new ArgumentException("Local Git operations require a registered workspace path.", nameof(workspacePath));
        if (operationKind == SourceControlDeliveryOperationKind.CommitExactChanges &&
            (string.IsNullOrWhiteSpace(ExpectedParentHeadSha) || AllowedChangedPaths.Count == 0 || string.IsNullOrWhiteSpace(CommitMessage)))
            throw new ArgumentException("Exact commits require parent HEAD, changed paths, and a bounded commit message.");
        if (operationKind is SourceControlDeliveryOperationKind.UpdatePullRequestMetadata or SourceControlDeliveryOperationKind.CreateDraftPullRequest &&
            string.IsNullOrWhiteSpace(PullRequestTitle))
            throw new ArgumentException("Pull-request creation and metadata updates require a title.", nameof(pullRequestTitle));
        if (operationKind == SourceControlDeliveryOperationKind.AddDeliveryComment && string.IsNullOrWhiteSpace(DeliveryComment))
            throw new ArgumentException("A bounded delivery comment is required.", nameof(deliveryComment));
        if (operationKind == SourceControlDeliveryOperationKind.RequestReviewers && Reviewers.Count == 0)
            throw new ArgumentException("At least one reviewer is required.", nameof(reviewers));
        if (operationKind is SourceControlDeliveryOperationKind.UpdatePullRequestMetadata or SourceControlDeliveryOperationKind.AddDeliveryComment or
            SourceControlDeliveryOperationKind.RequestReviewers or SourceControlDeliveryOperationKind.MarkReadyForReview or SourceControlDeliveryOperationKind.MergePullRequest &&
            string.IsNullOrWhiteSpace(Target.PullRequestId))
            throw new ArgumentException("This pull-request operation requires an exact pull-request identity.", nameof(target));
        if (operationKind is SourceControlDeliveryOperationKind.MarkReadyForReview or SourceControlDeliveryOperationKind.MergePullRequest &&
            (Evidence.ValidationDecisionReference is null || Evidence.ReviewRootId is null || Evidence.HumanApprovalRequestId is null || Evidence.HumanApprovalEvidenceRevision is null || string.IsNullOrWhiteSpace(Evidence.CurrentPolicyReference)))
            throw new ArgumentException("Ready and merge operations require current validation, review, and approval evidence.", nameof(evidence));
    }

    public Guid CommandId { get; }
    public Guid ProjectId { get; }
    public string WorkItemIdentity { get; }
    public PlanningExecutionContractReference ContractReference { get; }
    public SourceControlDeliveryOperationKind OperationKind { get; }
    public SourceControlDeliveryTarget Target { get; }
    public string ActorReference { get; }
    public string EvidenceRevision { get; }
    public string AuditIdentity { get; }
    public SourceControlDeliveryEvidence Evidence { get; }
    public string? CredentialReference { get; }
    public string? WorkspacePath { get; }
    public string? ExpectedParentHeadSha { get; }
    public IReadOnlyList<string> AllowedChangedPaths { get; }
    public string? CommitMessage { get; }
    public string? PullRequestTitle { get; }
    public string? PullRequestBody { get; }
    public string? DeliveryComment { get; }
    public IReadOnlyList<string> Reviewers { get; }
    public TrackerSynchronizationRequest? TrackerRequest { get; }
    public TrackerSynchronizationPlan? TrackerPlan { get; }
    public TrackerSynchronizationOperation? TrackerOperation { get; }
    public TrackerMutationAuthority? TrackerAuthority { get; }
    public WorkspacePreparationPlanReference? WorkspaceReference { get; }
    public string CommandContentHash => SourceControlDeliveryIntent.Compute(this);

    public SourceControlDeliveryCommand WithTarget(SourceControlDeliveryTarget target) => new(
        CommandId, ProjectId, WorkItemIdentity, ContractReference, OperationKind, target, ActorReference,
        EvidenceRevision, AuditIdentity, Evidence, CredentialReference, WorkspacePath, ExpectedParentHeadSha,
        AllowedChangedPaths, CommitMessage, PullRequestTitle, PullRequestBody, DeliveryComment, Reviewers,
        TrackerRequest, TrackerPlan, TrackerOperation, TrackerAuthority, WorkspaceReference);

    public SourceControlDeliveryCommand WithWorkspacePath(string workspacePath) => new(
        CommandId, ProjectId, WorkItemIdentity, ContractReference, OperationKind, Target, ActorReference,
        EvidenceRevision, AuditIdentity, Evidence, CredentialReference, workspacePath, ExpectedParentHeadSha,
        AllowedChangedPaths, CommitMessage, PullRequestTitle, PullRequestBody, DeliveryComment, Reviewers,
        TrackerRequest, TrackerPlan, TrackerOperation, TrackerAuthority, WorkspaceReference);

    private static string Required(string value, string parameterName, int max) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length > max || value.Any(char.IsControl)
            ? throw new ArgumentException("A bounded command value is required.", parameterName)
            : value.Trim();

    private static string? Optional(string? value, string parameterName, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, parameterName, max);

    private static string Sha(string value, string parameterName) =>
        value.Trim().Length is >= 7 and <= 128 && value.Trim().All(Uri.IsHexDigit)
            ? value.Trim().ToLowerInvariant()
            : throw new ArgumentException("A valid hexadecimal commit identity is required.", parameterName);

    private static string Sha256(string value, string parameterName) =>
        value.Trim().Length == 64 && value.Trim().All(Uri.IsHexDigit)
            ? value.Trim().ToLowerInvariant()
            : throw new ArgumentException("A SHA-256 command identity is required.", parameterName);

    private static IReadOnlyList<string> NormalizePaths(IReadOnlyList<string>? paths)
    {
        var values = paths?.ToArray() ?? Array.Empty<string>();
        if (values.Length > SourceControlDeliveryLimits.MaxChangedPaths) throw new ArgumentException("Changed paths exceed the supported bound.", nameof(paths));
        var normalized = values.Select((value, index) => Required(value, $"paths[{index}]", SourceControlDeliveryLimits.MaxPathLength).Replace('\\', '/')).ToArray();
        if (normalized.Any(value => value.StartsWith("/", StringComparison.Ordinal) || value.Contains("../", StringComparison.Ordinal) || value == ".." || value.Contains('\0')) ||
            normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            throw new ArgumentException("Changed paths must be unique, relative, and traversal-safe.", nameof(paths));
        return normalized;
    }

    private static IReadOnlyList<string> NormalizeReviewers(IReadOnlyList<string>? reviewers)
    {
        var values = reviewers?.ToArray() ?? Array.Empty<string>();
        if (values.Length > SourceControlDeliveryLimits.MaxReviewers) throw new ArgumentException("Reviewer count exceeds the supported bound.", nameof(reviewers));
        var normalized = values.Select((value, index) => Required(value, $"reviewers[{index}]", 300)).ToArray();
        if (normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length) throw new ArgumentException("Reviewers must be unique.", nameof(reviewers));
        return normalized;
    }
}

internal static class SourceControlDeliveryIntent
{
    public static string Compute(SourceControlDeliveryCommand command)
    {
        var payload = string.Join("\u001f", [
            command.ProjectId.ToString("D"),
            command.WorkItemIdentity,
            command.ContractReference.ToString(),
            command.OperationKind.ToString(),
            command.Target.Provider.ToString(),
            command.Target.RepositoryUrl,
            command.Target.ProviderRepositoryId,
            command.Target.CanonicalRepositoryIdentity,
            command.Target.BaseRef,
            command.Target.BaseSha,
            command.Target.HeadRef,
            command.Target.HeadSha,
            command.Target.PullRequestId ?? string.Empty,
            command.Evidence.ExecutionRunAuthorityReference?.ToString() ?? string.Empty,
            command.Evidence.ValidationDecisionReference?.ToString() ?? string.Empty,
            command.Evidence.ReviewRootId?.ToString("D") ?? string.Empty,
            command.Evidence.CurrentReviewId?.ToString("D") ?? string.Empty,
            command.Evidence.HumanApprovalRequestId?.ToString("D") ?? string.Empty,
            command.Evidence.HumanApprovalEvidenceRevision?.ContentHash ?? string.Empty,
            command.Evidence.CurrentPolicyReference ?? string.Empty,
            command.EvidenceRevision,
            command.AuditIdentity,
            command.WorkspaceReference is null ? string.Empty : $"{command.WorkspaceReference.ProjectId:D}/{command.WorkspaceReference.PlanId:D}/{command.WorkspaceReference.SchemaVersion}/{command.WorkspaceReference.ContentHash}",
            command.ExpectedParentHeadSha ?? string.Empty,
            string.Join("\u001e", command.AllowedChangedPaths),
            command.CommitMessage ?? string.Empty,
            command.PullRequestTitle ?? string.Empty,
            command.PullRequestBody ?? string.Empty,
            command.DeliveryComment ?? string.Empty,
            string.Join("\u001e", command.Reviewers),
            TrackerPlanIdentity(command.TrackerPlan),
            TrackerOperationIdentity(command.TrackerOperation),
            command.TrackerAuthority?.ContentHash ?? string.Empty,
            command.TrackerRequest?.ProjectId.ToString("D") ?? string.Empty
        ]);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public static string TrackerPlanIdentity(TrackerSynchronizationPlan? plan) => plan is null
        ? string.Empty
        : string.Join("\u001e", plan.ProjectId, plan.EvidenceState, plan.EvidenceFingerprint ?? string.Empty,
            string.Join("\u001f", plan.Operations.Select(TrackerOperationIdentity)),
            string.Join("\u001f", plan.Conflicts), string.Join("\u001f", plan.UnsupportedChanges), string.Join("\u001f", plan.Blockers));

    public static string TrackerOperationIdentity(TrackerSynchronizationOperation? operation) => operation is null
        ? string.Empty
        : string.Join("\u001e", operation.Kind, operation.Target.CanonicalIdentity, operation.ExpectedStateIdentity,
            operation.CommentBody ?? string.Empty, operation.StatusId ?? string.Empty);
}

public sealed class SourceControlRemoteEvidence
{
    public SourceControlRemoteEvidence(
        RemoteRepositoryEvidence repositoryEvidence,
        RemoteBranchEvidence? headBranch,
        RemoteBranchEvidence? baseBranch,
        RemotePullRequestEvidence? pullRequest = null)
    {
        RepositoryEvidence = repositoryEvidence ?? throw new ArgumentNullException(nameof(repositoryEvidence));
        HeadBranch = headBranch;
        BaseBranch = baseBranch;
        PullRequest = pullRequest;
    }

    public RemoteRepositoryEvidence RepositoryEvidence { get; }
    public RemoteBranchEvidence? HeadBranch { get; }
    public RemoteBranchEvidence? BaseBranch { get; }
    public RemotePullRequestEvidence? PullRequest { get; }

    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|",
        RepositoryEvidence.Repository?.ProviderRepositoryId,
        RepositoryEvidence.Repository?.CanonicalName,
        HeadBranch?.BranchName,
        HeadBranch?.CommitId,
        BaseBranch?.BranchName,
        BaseBranch?.CommitId,
        PullRequest?.Id,
        PullRequest?.State,
        PullRequest?.IsDraft,
        PullRequest?.HeadCommitId,
        PullRequest?.BaseCommitId,
        PullRequest?.Title,
        PullRequest?.Body,
        PullRequest?.MergeCommitId,
        PullRequest?.ProviderNodeId)))).ToLowerInvariant();
}

public sealed record SourceControlRemoteMutationResult(
    SourceControlDeliveryStatus Status,
    string? ErrorMessage = null,
    string? PullRequestId = null,
    string? MergeCommitSha = null,
    bool MutationSent = false,
    bool MayHaveModifiedRemote = false,
    SourceControlRemoteEvidence? Evidence = null)
{
    public bool Succeeded => Status is SourceControlDeliveryStatus.Verified or SourceControlDeliveryStatus.AlreadyApplied;
}

public interface IRemoteSourceControlDeliveryAdapter
{
    RemoteRepositoryProvider Provider { get; }

    Task<SourceControlRemoteEvidence> ReadAsync(SourceControlDeliveryCommand command, CancellationToken cancellationToken = default);

    Task<SourceControlRemoteMutationResult> MutateAsync(
        SourceControlDeliveryCommand command,
        SourceControlRemoteEvidence currentEvidence,
        CancellationToken cancellationToken = default);
}

/// <summary>Optional read-only operation-specific reconciliation implemented by trusted adapters.</summary>
public interface IRemoteSourceControlDeliveryReconciliation
{
    Task<SourceControlRemoteMutationResult> ReconcileAsync(SourceControlDeliveryCommand command, CancellationToken cancellationToken = default);
}

public interface ILocalDeliveryGitService
{
    Task<LocalDeliveryGitResult> CommitExactChangesAsync(SourceControlDeliveryCommand command, CancellationToken cancellationToken = default);
    Task<LocalDeliveryGitResult> PushExactHeadAsync(SourceControlDeliveryCommand command, CancellationToken cancellationToken = default);
}

/// <summary>Read-only recovery seam for an already-attempted local delivery.</summary>
public interface ILocalDeliveryGitReconciliation
{
    Task<LocalDeliveryGitResult> ReconcileAsync(SourceControlDeliveryCommand command, CancellationToken cancellationToken = default);
}

public sealed record LocalDeliveryGitResult(
    SourceControlDeliveryStatus Status,
    string? ErrorMessage = null,
    string? NewHeadSha = null,
    bool MutationSent = false)
{
    public bool Succeeded => Status is SourceControlDeliveryStatus.Verified or SourceControlDeliveryStatus.AlreadyApplied;
}

public sealed record SourceControlDeliveryAuditEvent(
    Guid CommandId,
    Guid ProjectId,
    SourceControlDeliveryOperationKind OperationKind,
    SourceControlDeliveryStatus Status,
    DateTimeOffset OccurredAt,
    string WorkItemIdentity,
    string ContractReference,
    string RepositoryIdentity,
    string BaseRef,
    string BaseSha,
    string HeadRef,
    string ExpectedHeadSha,
    string ActorReference,
    string AuditIdentity,
    string EvidenceRevision,
    string RemoteEvidenceFingerprint,
    string? ActualHeadSha = null,
    string? PullRequestId = null,
    string? MergeCommitSha = null,
    string? ErrorMessage = null,
    bool MutationSent = false,
    bool MayHaveModifiedRemote = false,
    string? CommandContentHash = null,
    bool RemoteDeliveryVerified = false,
    string? TrackerPlanIdentity = null,
    string? TrackerOperationIdentity = null,
    string? TrackerAuthorityContentHash = null,
    TrackerMutationOutcome? TrackerOutcome = null)
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public SourceControlDeliveryAuditEventKind? EventKindOverride { get; init; }
    public SourceControlDeliveryAuditEventKind EventKind => EventKindOverride ?? (Status switch
    {
        SourceControlDeliveryStatus.Verified or SourceControlDeliveryStatus.AlreadyApplied => SourceControlDeliveryAuditEventKind.Succeeded,
        SourceControlDeliveryStatus.ReconciliationRequired => SourceControlDeliveryAuditEventKind.ReconciliationRequired,
        SourceControlDeliveryStatus.Failed or SourceControlDeliveryStatus.Unsupported => SourceControlDeliveryAuditEventKind.VerificationFailed,
        _ => SourceControlDeliveryAuditEventKind.Rejected
    });
    public string? ValidationDecisionReference { get; init; }
    public string? HumanApprovalReference { get; init; }
    public string? PostEvidenceFingerprint { get; init; }
    public string ContentHash => ComputeContentHash();

    private string ComputeContentHash()
    {
        var payload = string.Join("\u001f", EventId, CommandId, ProjectId, OperationKind, Status, OccurredAt.ToUniversalTime().ToString("O"),
            WorkItemIdentity, ContractReference, RepositoryIdentity, BaseRef, BaseSha, HeadRef, ExpectedHeadSha,
            ActorReference, AuditIdentity, EvidenceRevision, RemoteEvidenceFingerprint, ActualHeadSha, PullRequestId,
            MergeCommitSha, ErrorMessage, MutationSent, MayHaveModifiedRemote, CommandContentHash,
            RemoteDeliveryVerified, TrackerPlanIdentity, TrackerOperationIdentity, TrackerAuthorityContentHash,
            TrackerOutcome, EventKindOverride, ValidationDecisionReference, HumanApprovalReference, PostEvidenceFingerprint);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}

public sealed record SourceControlDeliveryAuditReadResult(
    SourceControlDeliveryAuditReadState State,
    SourceControlDeliveryAuditEvent? Event = null,
    string? ErrorMessage = null);

public interface ISourceControlDeliveryAuditStore
{
    Task<SourceControlDeliveryAuditReadResult> FindAsync(Guid projectId, Guid commandId, CancellationToken cancellationToken = default);
    Task AppendAsync(SourceControlDeliveryAuditEvent value, CancellationToken cancellationToken = default);
}

public interface ISourceControlDeliveryService
{
    Task<SourceControlDeliveryResult> ExecuteAsync(SourceControlDeliveryCommand command, CancellationToken cancellationToken = default);
}

public sealed record SourceControlDeliveryResult(
    SourceControlDeliveryStatus Status,
    string? ErrorMessage = null,
    string? NewHeadSha = null,
    string? PullRequestId = null,
    string? MergeCommitSha = null,
    bool MutationSent = false,
    bool MayHaveModifiedRemote = false,
    SourceControlRemoteEvidence? Evidence = null,
    TrackerMutationResult? TrackerSynchronization = null,
    bool RemoteDeliveryVerified = false)
{
    public bool Succeeded => Status is SourceControlDeliveryStatus.Verified or SourceControlDeliveryStatus.AlreadyApplied;
}

public sealed class SourceControlDeliveryService : ISourceControlDeliveryService
{
    private readonly IPlanningExecutionContractRepository _contracts;
    private readonly IValidationGateDecisionRepository _validationDecisions;
    private readonly IHumanApprovalService _approvals;
    private readonly IReviewWorkflowService _reviews;
    private readonly IReadOnlyDictionary<RemoteRepositoryProvider, IRemoteSourceControlDeliveryAdapter> _adapters;
    private readonly ILocalDeliveryGitService _localGit;
    private readonly ISourceControlDeliveryAuditStore _audit;
    private readonly ITrackerSynchronizationService? _tracker;
    private readonly IWorkspacePreparationPlanRepository? _workspacePlans;
    private readonly IWorkspacePreparationReceiptRepository? _workspaceReceipts;
    private readonly IWorkspacePreparationApprovalEvidenceRepository? _workspaceApprovalEvidence;
    private readonly IWorkspaceRepository? _workspaceRepositories;
    private readonly IWorkspacePreparedWorkspaceVerifier? _workspaceVerifier;
    private readonly ILocalDeliveryGitReconciliation? _localReconciliation;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SourceControlDeliveryService(
        IPlanningExecutionContractRepository contracts,
        IValidationGateDecisionRepository validationDecisions,
        IHumanApprovalService approvals,
        IReviewWorkflowService reviews,
        IEnumerable<IRemoteSourceControlDeliveryAdapter> adapters,
        ILocalDeliveryGitService localGit,
        ISourceControlDeliveryAuditStore audit,
        ITrackerSynchronizationService? tracker = null,
        IWorkspacePreparationPlanRepository? workspacePlans = null,
        IWorkspacePreparationReceiptRepository? workspaceReceipts = null,
        IWorkspacePreparationApprovalEvidenceRepository? workspaceApprovalEvidence = null,
        IWorkspaceRepository? workspaceRepositories = null,
        IWorkspacePreparedWorkspaceVerifier? workspaceVerifier = null)
    {
        _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
        _validationDecisions = validationDecisions ?? throw new ArgumentNullException(nameof(validationDecisions));
        _approvals = approvals ?? throw new ArgumentNullException(nameof(approvals));
        _reviews = reviews ?? throw new ArgumentNullException(nameof(reviews));
        _adapters = adapters?.GroupBy(value => value.Provider).ToDictionary(group => group.Key, group => group.Last()) ?? throw new ArgumentNullException(nameof(adapters));
        _localGit = localGit ?? throw new ArgumentNullException(nameof(localGit));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _tracker = tracker;
        _workspacePlans = workspacePlans;
        _workspaceReceipts = workspaceReceipts;
        _workspaceApprovalEvidence = workspaceApprovalEvidence;
        _workspaceRepositories = workspaceRepositories;
        _workspaceVerifier = workspaceVerifier;
        _localReconciliation = localGit as ILocalDeliveryGitReconciliation;
    }

    public async Task<SourceControlDeliveryResult> ExecuteAsync(SourceControlDeliveryCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var previous = await _audit.FindAsync(command.ProjectId, command.CommandId, cancellationToken).ConfigureAwait(false);
            if (previous.State == SourceControlDeliveryAuditReadState.Corrupt || previous.State == SourceControlDeliveryAuditReadState.Unavailable)
                return new(SourceControlDeliveryStatus.InvalidAuthority, previous.ErrorMessage ?? "Delivery audit history is unavailable.");
            if (previous.Event is not null)
            {
                if (!Matches(command, previous.Event))
                    return new(SourceControlDeliveryStatus.InvalidAuthority, "The command identity conflicts with existing delivery audit evidence.");
                if (previous.Event.Status is SourceControlDeliveryStatus.Verified or SourceControlDeliveryStatus.AlreadyApplied)
                    return FromAudit(previous.Event, SourceControlDeliveryStatus.AlreadyApplied);
                if (previous.Event.EventKind is SourceControlDeliveryAuditEventKind.Attempted or SourceControlDeliveryAuditEventKind.ReconciliationRequired ||
                    previous.Event.Status == SourceControlDeliveryStatus.ReconciliationRequired)
                    return await ReconcileAsync(command, previous.Event, cancellationToken).ConfigureAwait(false);
            }

            var contract = await _contracts.GetAsync(command.ProjectId, command.ContractReference.ContractId, command.ContractReference.Revision, cancellationToken).ConfigureAwait(false);
            if (!contract.IsValid || contract.Contract is null || !Same(command.ContractReference, contract.Contract.Reference) || contract.Contract.ProjectId != command.ProjectId)
                return await RecordAsync(command, new(SourceControlDeliveryStatus.InvalidAuthority, "The planning/execution contract is missing, stale, or mismatched."), cancellationToken).ConfigureAwait(false);

            if (command.OperationKind is SourceControlDeliveryOperationKind.CommitExactChanges or SourceControlDeliveryOperationKind.PushExactHead)
            {
                var workspace = await ResolveManagedWorkspaceAsync(command, cancellationToken).ConfigureAwait(false);
                if (workspace.Failure is not null)
                    return await RecordAsync(command, workspace.Failure, cancellationToken).ConfigureAwait(false);
                command = workspace.Command!;
            }

            SourceControlDeliveryResult result;
            var attempted = await AppendAttemptedAsync(command, cancellationToken).ConfigureAwait(false);
            if (!attempted)
                return new(SourceControlDeliveryStatus.InvalidAuthority, "The durable delivery Attempted record could not be persisted; no mutation was sent.");

            if (command.OperationKind == SourceControlDeliveryOperationKind.CommitExactChanges)
            {
                var local = await _localGit.CommitExactChangesAsync(command, cancellationToken).ConfigureAwait(false);
                result = new(local.Status, local.ErrorMessage, local.NewHeadSha, MutationSent: local.MutationSent);
            }
            else if (command.OperationKind == SourceControlDeliveryOperationKind.PushExactHead)
            {
                var local = await _localGit.PushExactHeadAsync(command, cancellationToken).ConfigureAwait(false);
                result = new(local.Status, local.ErrorMessage, local.NewHeadSha, MutationSent: local.MutationSent);
                if (local.Succeeded)
                {
                    if (!_adapters.TryGetValue(command.Target.Provider, out var pushAdapter))
                        result = result with { Status = SourceControlDeliveryStatus.ReconciliationRequired, ErrorMessage = "The local push succeeded but no remote evidence adapter is available.", MayHaveModifiedRemote = true };
                    else
                    {
                        var pushEvidence = await pushAdapter.ReadAsync(command, cancellationToken).ConfigureAwait(false);
                        var pushVerificationFailure = ValidateRemoteEvidence(command, pushEvidence, requireCommandFingerprint: false);
                        result = pushVerificationFailure ?? result with { Evidence = pushEvidence };
                        if (pushVerificationFailure is not null)
                            result = result with { Status = SourceControlDeliveryStatus.ReconciliationRequired, ErrorMessage = "The local push succeeded but independent provider evidence did not verify the exact remote head.", MutationSent = true, MayHaveModifiedRemote = true };
                    }
                }
            }
            else
            {
                if (!_adapters.TryGetValue(command.Target.Provider, out var adapter))
                    return await RecordAsync(command, new(SourceControlDeliveryStatus.Unsupported, "The configured source-control provider is unsupported."), cancellationToken).ConfigureAwait(false);

                var evidence = await adapter.ReadAsync(command, cancellationToken).ConfigureAwait(false);
                var preflight = ValidateRemoteEvidence(command, evidence);
                if (preflight is not null)
                    return await RecordAsync(command, preflight, cancellationToken).ConfigureAwait(false);
                if (command.OperationKind is SourceControlDeliveryOperationKind.MarkReadyForReview or SourceControlDeliveryOperationKind.MergePullRequest)
                {
                    var gateFailure = await ValidateHighRiskGatesAsync(command, evidence, cancellationToken).ConfigureAwait(false);
                    if (gateFailure is not null)
                        return await RecordAsync(command, gateFailure, cancellationToken).ConfigureAwait(false);
                    // The late read is deliberately immediately before the state-changing call.
                    evidence = await adapter.ReadAsync(command, cancellationToken).ConfigureAwait(false);
                    preflight = ValidateRemoteEvidence(command, evidence);
                    if (preflight is not null)
                        return await RecordAsync(command, preflight, cancellationToken).ConfigureAwait(false);
                    if (command.OperationKind == SourceControlDeliveryOperationKind.MergePullRequest)
                    {
                        var lateGateFailure = await ValidateHighRiskGatesAsync(command, evidence, cancellationToken).ConfigureAwait(false);
                        if (lateGateFailure is not null)
                            return await RecordAsync(command, lateGateFailure, cancellationToken).ConfigureAwait(false);
                    }
                }

                var remote = await adapter.MutateAsync(command, evidence, cancellationToken).ConfigureAwait(false);
                result = new(remote.Status, remote.ErrorMessage, PullRequestId: remote.PullRequestId ?? command.Target.PullRequestId,
                    MergeCommitSha: remote.MergeCommitSha, MutationSent: remote.MutationSent,
                    MayHaveModifiedRemote: remote.MayHaveModifiedRemote, Evidence: remote.Evidence,
                    RemoteDeliveryVerified: remote.Status is SourceControlDeliveryStatus.Verified or SourceControlDeliveryStatus.AlreadyApplied);
                if (remote.Status == SourceControlDeliveryStatus.ReconciliationRequired)
                    return await RecordAsync(command, result, cancellationToken).ConfigureAwait(false);
                if (remote.Succeeded && remote.Evidence is null)
                {
                    var verified = await adapter.ReadAsync(command, cancellationToken).ConfigureAwait(false);
                    var verificationFailure = VerifyPostState(command, verified);
                    result = verificationFailure ?? result with { Evidence = verified, RemoteDeliveryVerified = true };
                }
            }

            if (result.Succeeded && command.OperationKind == SourceControlDeliveryOperationKind.MergePullRequest &&
                _tracker is not null && command.TrackerPlan is not null && command.TrackerOperation is not null && command.TrackerAuthority is not null)
            {
                var sync = await _tracker.ExecuteAsync(command.TrackerPlan, command.TrackerOperation, command.TrackerAuthority, cancellationToken).ConfigureAwait(false);
                result = result with { TrackerSynchronization = sync };
                if (sync.Outcome != TrackerMutationOutcome.Succeeded)
                    result = result with { Status = SourceControlDeliveryStatus.ReconciliationRequired, ErrorMessage = "Merge was verified but tracker synchronization requires reconciliation." };
            }

            return await RecordAsync(command, result, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SourceControlDeliveryResult> ReconcileAsync(SourceControlDeliveryCommand command, SourceControlDeliveryAuditEvent previous, CancellationToken cancellationToken)
    {
        if (command.OperationKind is SourceControlDeliveryOperationKind.CommitExactChanges or SourceControlDeliveryOperationKind.PushExactHead)
        {
            var workspace = await ResolveManagedWorkspaceAsync(command, cancellationToken).ConfigureAwait(false);
            if (workspace.Failure is not null)
                return await RecordAsync(command, workspace.Failure, cancellationToken).ConfigureAwait(false);
            if (_localReconciliation is null)
                return new(SourceControlDeliveryStatus.ReconciliationRequired, "The attempted local mutation has no read-only reconciliation implementation.");
            var local = await _localReconciliation.ReconcileAsync(workspace.Command!, cancellationToken).ConfigureAwait(false);
            var localResult = new SourceControlDeliveryResult(local.Status, local.ErrorMessage, local.NewHeadSha, MutationSent: previous.MutationSent || previous.EventKind == SourceControlDeliveryAuditEventKind.Attempted);
            return await RecordAsync(workspace.Command!, localResult, cancellationToken).ConfigureAwait(false);
        }

        if (!_adapters.TryGetValue(command.Target.Provider, out var adapter))
            return new(SourceControlDeliveryStatus.ReconciliationRequired, "The ambiguous operation cannot be reconciled without its provider adapter.");
        if (adapter is IRemoteSourceControlDeliveryReconciliation reconciler)
        {
            var reconciled = await reconciler.ReconcileAsync(command, cancellationToken).ConfigureAwait(false);
            var reconciledResult = new SourceControlDeliveryResult(
                reconciled.Status, reconciled.ErrorMessage, PullRequestId: reconciled.PullRequestId ?? command.Target.PullRequestId,
                MergeCommitSha: reconciled.MergeCommitSha, MutationSent: true,
                MayHaveModifiedRemote: reconciled.MayHaveModifiedRemote, Evidence: reconciled.Evidence,
                RemoteDeliveryVerified: reconciled.Succeeded);
            if (previous.RemoteDeliveryVerified && command.OperationKind == SourceControlDeliveryOperationKind.MergePullRequest)
                reconciledResult = reconciledResult with { RemoteDeliveryVerified = reconciledResult.Succeeded };
            if (reconciledResult.Succeeded && command.OperationKind == SourceControlDeliveryOperationKind.MergePullRequest &&
                _tracker is not null && command.TrackerPlan is not null && command.TrackerOperation is not null && command.TrackerAuthority is not null)
            {
                if (!TrackerAuthorityMatches(previous, command))
                    return new(SourceControlDeliveryStatus.InvalidAuthority, "Tracker synchronization authority conflicts with the attempted command.");
                var sync = await _tracker.ExecuteAsync(command.TrackerPlan, command.TrackerOperation, command.TrackerAuthority, cancellationToken).ConfigureAwait(false);
                reconciledResult = reconciledResult with { TrackerSynchronization = sync };
                if (sync.Outcome != TrackerMutationOutcome.Succeeded)
                    reconciledResult = reconciledResult with { Status = SourceControlDeliveryStatus.ReconciliationRequired, ErrorMessage = "Remote delivery is verified but tracker synchronization remains unresolved." };
            }
            return await RecordAsync(command, reconciledResult, cancellationToken).ConfigureAwait(false);
        }
        var evidence = await adapter.ReadAsync(command, cancellationToken).ConfigureAwait(false);
        var postState = VerifyPostState(command, evidence, previous.MergeCommitSha);
        var result = postState ?? new(SourceControlDeliveryStatus.ReconciliationRequired, "Remote evidence cannot determine whether the previous mutation was applied.", MutationSent: true, MayHaveModifiedRemote: true, Evidence: evidence);
        if (postState is not null && postState.Succeeded)
            result = result with { Status = SourceControlDeliveryStatus.AlreadyApplied, Evidence = evidence, MutationSent = true, MayHaveModifiedRemote = true, RemoteDeliveryVerified = true };

        if (previous.RemoteDeliveryVerified && command.OperationKind == SourceControlDeliveryOperationKind.MergePullRequest &&
            result.RemoteDeliveryVerified && _tracker is not null && command.TrackerPlan is not null && command.TrackerOperation is not null && command.TrackerAuthority is not null)
        {
            if (!TrackerAuthorityMatches(previous, command))
                return new(SourceControlDeliveryStatus.InvalidAuthority, "Tracker synchronization authority conflicts with the attempted command.");
            var sync = await _tracker.ExecuteAsync(command.TrackerPlan, command.TrackerOperation, command.TrackerAuthority, cancellationToken).ConfigureAwait(false);
            result = result with { TrackerSynchronization = sync };
            if (sync.Outcome != TrackerMutationOutcome.Succeeded)
                result = result with { Status = SourceControlDeliveryStatus.ReconciliationRequired, ErrorMessage = "Remote delivery is verified but tracker synchronization remains unresolved." };
        }
        return await RecordAsync(command, result, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SourceControlDeliveryResult> RecordAsync(SourceControlDeliveryCommand command, SourceControlDeliveryResult result, CancellationToken cancellationToken)
    {
        await _audit.AppendAsync(new SourceControlDeliveryAuditEvent(
            command.CommandId, command.ProjectId, command.OperationKind, result.Status, DateTimeOffset.UtcNow,
            command.WorkItemIdentity, command.ContractReference.ToString(), command.Target.CanonicalRepositoryIdentity,
            command.Target.BaseRef, command.Target.BaseSha, command.Target.HeadRef, command.Target.HeadSha,
            command.ActorReference, command.AuditIdentity, command.EvidenceRevision, result.Evidence?.Fingerprint ?? command.Evidence.RemoteEvidenceFingerprint,
            result.NewHeadSha, result.PullRequestId, result.MergeCommitSha, result.ErrorMessage, result.MutationSent, result.MayHaveModifiedRemote,
            command.CommandContentHash, result.RemoteDeliveryVerified,
            SourceControlDeliveryIntent.TrackerPlanIdentity(command.TrackerPlan),
            SourceControlDeliveryIntent.TrackerOperationIdentity(command.TrackerOperation),
            command.TrackerAuthority?.ContentHash,
            result.TrackerSynchronization?.Outcome)
        {
            ValidationDecisionReference = command.Evidence.ValidationDecisionReference?.ToString(),
            HumanApprovalReference = command.Evidence.HumanApprovalRequestId?.ToString("D"),
            PostEvidenceFingerprint = result.Evidence?.Fingerprint
        }, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task<bool> AppendAttemptedAsync(SourceControlDeliveryCommand command, CancellationToken cancellationToken)
    {
        try
        {
            await _audit.AppendAsync(new SourceControlDeliveryAuditEvent(
                command.CommandId, command.ProjectId, command.OperationKind, SourceControlDeliveryStatus.ReconciliationRequired,
                DateTimeOffset.UtcNow, command.WorkItemIdentity, command.ContractReference.ToString(),
                command.Target.CanonicalRepositoryIdentity, command.Target.BaseRef, command.Target.BaseSha,
                command.Target.HeadRef, command.Target.HeadSha, command.ActorReference, command.AuditIdentity,
                command.EvidenceRevision, command.Evidence.RemoteEvidenceFingerprint,
                PullRequestId: command.Target.PullRequestId, MutationSent: false)
            {
                CommandContentHash = command.CommandContentHash,
                EventKindOverride = SourceControlDeliveryAuditEventKind.Attempted,
                ValidationDecisionReference = command.Evidence.ValidationDecisionReference?.ToString(),
                HumanApprovalReference = command.Evidence.HumanApprovalRequestId?.ToString("D")
            }, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<(SourceControlDeliveryCommand? Command, SourceControlDeliveryResult? Failure)> ResolveManagedWorkspaceAsync(
        SourceControlDeliveryCommand command,
        CancellationToken cancellationToken)
    {
        if (command.WorkspaceReference is null || command.WorkspaceReference.ProjectId != command.ProjectId ||
            _workspacePlans is null || _workspaceReceipts is null || _workspaceApprovalEvidence is null ||
            _workspaceRepositories is null || _workspaceVerifier is null)
            return (null, new(SourceControlDeliveryStatus.InvalidAuthority, "Local delivery requires an authoritative managed-workspace reference and receipt repositories."));

        var planRead = await _workspacePlans.GetAsync(command.ProjectId, command.WorkspaceReference.PlanId, cancellationToken).ConfigureAwait(false);
        if (planRead.State != WorkspacePreparationPlanReadState.Valid || planRead.Plan is null ||
            !WorkspacePreparationPlanningService.SameContract(planRead.Plan.ContractReference, command.ContractReference) ||
            planRead.Plan.Reference.PlanId != command.WorkspaceReference.PlanId ||
            planRead.Plan.Reference.SchemaVersion != command.WorkspaceReference.SchemaVersion ||
            !string.Equals(planRead.Plan.ContentHash, command.WorkspaceReference.ContentHash, StringComparison.OrdinalIgnoreCase))
            return (null, new(SourceControlDeliveryStatus.InvalidAuthority, "The managed workspace plan is missing, stale, or mismatched."));

        var receiptRead = await _workspaceReceipts.GetAsync(command.ProjectId, planRead.Plan.WorkspaceId, cancellationToken).ConfigureAwait(false);
        if (receiptRead.State != WorkspacePreparationReceiptReadState.Valid || receiptRead.Receipt is null)
            return (null, new(SourceControlDeliveryStatus.InvalidAuthority, "The managed workspace receipt is missing or cannot be trusted."));
        var receipt = receiptRead.Receipt;
        if (command.WorkspacePath is null || !WorkspaceRepositoryIdentity.AreEqual(command.WorkspacePath, receipt.WorkspacePath) ||
            HasReparsePoint(command.WorkspacePath) || HasReparsePoint(receipt.WorkspacePath))
            return (null, new(SourceControlDeliveryStatus.InvalidAuthority, "The caller workspace path is not the exact authoritative managed workspace."));

        if (receipt.ApprovalReference is null)
            return (null, new(SourceControlDeliveryStatus.InvalidAuthority, "The managed workspace receipt has no durable approval evidence."));
        var approvalRead = await _workspaceApprovalEvidence.GetAsync(command.ProjectId, receipt.WorkspaceId, receipt.ApprovalReference.ApprovalId, cancellationToken).ConfigureAwait(false);
        if (!approvalRead.IsValid || approvalRead.Evidence is null)
            return (null, new(SourceControlDeliveryStatus.InvalidAuthority, "The managed workspace approval evidence is missing or invalid."));
        var discovery = await _workspaceRepositories.DiscoverAsync(planRead.Plan.Repository.RegisteredPath, cancellationToken).ConfigureAwait(false);
        if (!WorkspacePreparationPlanningService.ReceiptMatchesExactPlan(planRead.Plan, receipt, approvalRead.Evidence, receipt.WorkspacePath, discovery, out var associationError))
            return (null, new(SourceControlDeliveryStatus.InvalidAuthority, associationError ?? "The managed workspace receipt no longer matches its authoritative plan."));
        var prepared = await _workspaceVerifier.VerifyPreparedWorkspaceAsync(receipt.WorkspacePath, cancellationToken).ConfigureAwait(false);
        if (!prepared.Succeeded || !WorkspaceRepositoryIdentity.AreEqual(prepared.WorkspacePath, receipt.WorkspacePath) ||
            prepared.CommonDirectory is null || !WorkspaceRepositoryIdentity.AreEqual(prepared.CommonDirectory, receipt.RepositoryIdentity) ||
            !string.Equals(prepared.BranchName, receipt.WorkspaceBranch, StringComparison.Ordinal) || prepared.IsDetached)
            return (null, new(SourceControlDeliveryStatus.InvalidAuthority, "The managed workspace safety receipt is stale or the workspace identity changed."));

        return (command.WithWorkspacePath(receipt.WorkspacePath), null);
    }

    private static bool HasReparsePoint(string path)
    {
        try
        {
            var current = new DirectoryInfo(Path.GetFullPath(path));
            while (current is not null)
            {
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0) return true;
                if (current.Parent is null || string.Equals(current.FullName, current.Root.FullName, StringComparison.OrdinalIgnoreCase)) break;
                current = current.Parent;
            }
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
        catch (ArgumentException) { return true; }
        return false;
    }

    private static bool TrackerAuthorityMatches(SourceControlDeliveryAuditEvent previous, SourceControlDeliveryCommand command) =>
        string.Equals(previous.TrackerPlanIdentity, SourceControlDeliveryIntent.TrackerPlanIdentity(command.TrackerPlan), StringComparison.Ordinal) &&
        string.Equals(previous.TrackerOperationIdentity, SourceControlDeliveryIntent.TrackerOperationIdentity(command.TrackerOperation), StringComparison.Ordinal) &&
        string.Equals(previous.TrackerAuthorityContentHash, command.TrackerAuthority?.ContentHash, StringComparison.OrdinalIgnoreCase);

    private async Task<SourceControlDeliveryResult?> ValidateHighRiskGatesAsync(SourceControlDeliveryCommand command, SourceControlRemoteEvidence evidence, CancellationToken cancellationToken)
    {
        var validationReference = command.Evidence.ValidationDecisionReference!;
        var validation = await _validationDecisions.GetAsync(command.ProjectId, validationReference.DecisionId, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid || validation.Decision is null || validation.Decision.ProjectId != command.ProjectId ||
            validation.Decision.State != ValidationGateDecisionState.Satisfied ||
            validation.Decision.Reference.SchemaVersion != validationReference.SchemaVersion ||
            !string.Equals(validation.Decision.Reference.ContentHash, validationReference.ContentHash, StringComparison.OrdinalIgnoreCase) ||
            command.Evidence.ExecutionRunAuthorityReference is null ||
            !Same(validation.Decision.ExecutionRunAuthorityReference, command.Evidence.ExecutionRunAuthorityReference))
            return new(SourceControlDeliveryStatus.Blocked, "Current exact validation evidence is not satisfied.", Evidence: evidence);

        var review = await _reviews.ReadCaseAsync(command.ProjectId, command.Evidence.ReviewRootId!.Value, cancellationToken).ConfigureAwait(false);
        if (!review.IsUsable || review.InboxItem is null || review.InboxItem.CurrentReviewId != command.Evidence.CurrentReviewId ||
            review.InboxItem.WorkflowState != ReviewWorkflowState.ReadyForAcceptanceAuthority || review.InboxItem.OwnerAttentionRequired ||
            review.InboxItem.LatestValidationReference is null || !Same(review.InboxItem.LatestValidationReference, validationReference) ||
            !review.Events.Any(value => value.Kind == ReviewWorkflowEventKind.RevalidationRecorded &&
                value.CurrentReviewId == command.Evidence.CurrentReviewId && value.ValidationState == ValidationGateDecisionState.Satisfied &&
                value.ValidationDecisionReference is not null && Same(value.ValidationDecisionReference, validationReference)))
            return new(SourceControlDeliveryStatus.Blocked, "The current review workflow is not ready for acceptance authority.", Evidence: evidence);

        var target = HumanApprovalTarget.ProtectedBranchMerge(
            command.Target.CanonicalRepositoryIdentity, command.Target.BaseRef, command.Target.BaseSha,
            command.Target.HeadRef, command.Target.HeadSha, $"Merge {command.Target.HeadRef} into {command.Target.BaseRef}");
        var approvalContext = new HumanApprovalEvaluationContext(
            command.ProjectId, command.ContractReference, target, command.Evidence.HumanApprovalEvidenceRevision!, command.Evidence.CurrentPolicyReference!);
        var approval = await _approvals.EvaluateAsync(approvalContext, command.Evidence.HumanApprovalRequestId!.Value, cancellationToken).ConfigureAwait(false);
        if (!approval.CanProceed || approval.SatisfyingReference is null || approval.EffectiveState is not (HumanApprovalState.Approved or HumanApprovalState.Waived) ||
            approval.Request is null || approval.Request.ProjectId != command.ProjectId ||
            !Same(approval.Request.ContractReference, command.ContractReference) ||
            !string.Equals(approval.Request.Target.ContentHash, target.ContentHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(approval.Request.EvidenceRevision.ContentHash, command.Evidence.HumanApprovalEvidenceRevision!.ContentHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(approval.Request.PolicyReference, command.Evidence.CurrentPolicyReference, StringComparison.Ordinal) ||
            approval.SatisfyingReference.EventKind is not (HumanApprovalEventKind.Approved or HumanApprovalEventKind.Waived))
            return new(SourceControlDeliveryStatus.Blocked, "Current exact owner approval or waiver is not satisfied.", Evidence: evidence);
        if (evidence.PullRequest?.Mergeability != RemoteMergeability.Available)
            return new(SourceControlDeliveryStatus.Blocked, "Remote mergeability is not currently available.", Evidence: evidence);
        if (HasBlockingCiEvidence(evidence.RepositoryEvidence, out var ciReason))
            return new(SourceControlDeliveryStatus.Blocked, ciReason, Evidence: evidence);
        return null;
    }

    private static SourceControlDeliveryResult? ValidateRemoteEvidence(SourceControlDeliveryCommand command, SourceControlRemoteEvidence evidence, bool requireCommandFingerprint = true)
    {
        if (!string.Equals(command.Evidence.ExpectedHeadSha, command.Target.HeadSha, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(command.Evidence.ExpectedBaseSha, command.Target.BaseSha, StringComparison.OrdinalIgnoreCase) ||
            requireCommandFingerprint && !string.Equals(command.Evidence.RemoteEvidenceFingerprint, evidence.Fingerprint, StringComparison.OrdinalIgnoreCase))
            return new(SourceControlDeliveryStatus.Stale, "The command evidence revision does not match the current exact remote target.", Evidence: evidence);
        if (evidence.RepositoryEvidence.ProjectId != command.ProjectId || evidence.RepositoryEvidence.RepositoryState != RemoteEvidenceState.Available ||
            evidence.RepositoryEvidence.Repository is null || evidence.RepositoryEvidence.Repository.Provider != command.Target.Provider ||
            !string.Equals(evidence.RepositoryEvidence.Repository.ProviderRepositoryId, command.Target.ProviderRepositoryId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(evidence.RepositoryEvidence.Repository.CanonicalName, command.Target.CanonicalRepositoryIdentity, StringComparison.OrdinalIgnoreCase))
            return new(SourceControlDeliveryStatus.InvalidAuthority, "Remote repository identity does not match the exact delivery target.", Evidence: evidence);

        if (evidence.BaseBranch is null || !string.Equals(evidence.BaseBranch.BranchName, command.Target.BaseRef, StringComparison.Ordinal) ||
            !string.Equals(evidence.BaseBranch.CommitId, command.Target.BaseSha, StringComparison.OrdinalIgnoreCase))
            return new(SourceControlDeliveryStatus.Stale, "The exact remote base branch or SHA changed.", Evidence: evidence);
        if (evidence.HeadBranch is null || !string.Equals(evidence.HeadBranch.BranchName, command.Target.HeadRef, StringComparison.Ordinal) ||
            !string.Equals(evidence.HeadBranch.CommitId, command.Target.HeadSha, StringComparison.OrdinalIgnoreCase))
            return new(SourceControlDeliveryStatus.Stale, "The exact remote head branch or SHA changed.", Evidence: evidence);
        if (command.Target.PullRequestId is not null)
        {
            var pr = evidence.PullRequest;
            if (pr is null || !string.Equals(pr.Id, command.Target.PullRequestId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(pr.SourceBranch, command.Target.HeadRef, StringComparison.Ordinal) ||
                !string.Equals(pr.TargetBranch, command.Target.BaseRef, StringComparison.Ordinal) ||
                !string.Equals(pr.HeadCommitId, command.Target.HeadSha, StringComparison.OrdinalIgnoreCase) ||
                pr.BaseCommitId is not null && !string.Equals(pr.BaseCommitId, command.Target.BaseSha, StringComparison.OrdinalIgnoreCase))
                return new(SourceControlDeliveryStatus.Stale, "The exact pull-request identity or head/base SHA changed.", Evidence: evidence);
            if (command.OperationKind == SourceControlDeliveryOperationKind.MarkReadyForReview && pr.IsDraft != true)
                return new(SourceControlDeliveryStatus.Conflict, "The exact pull request is no longer Draft.", Evidence: evidence);
            if (command.OperationKind == SourceControlDeliveryOperationKind.MergePullRequest && (pr.IsDraft == true || !pr.State.Equals("open", StringComparison.OrdinalIgnoreCase)))
                return new(SourceControlDeliveryStatus.Conflict, "The exact pull request is not an open, non-draft merge target.", Evidence: evidence);
        }
        return null;
    }

    private static SourceControlDeliveryResult? VerifyPostState(SourceControlDeliveryCommand command, SourceControlRemoteEvidence evidence, string? expectedMergeCommitSha = null)
    {
        if (command.OperationKind == SourceControlDeliveryOperationKind.MergePullRequest)
        {
            var pr = evidence.PullRequest;
            var mergeProof = pr?.MergeCommitId;
            return pr is not null && pr.Id == command.Target.PullRequestId &&
                string.Equals(pr.SourceBranch, command.Target.HeadRef, StringComparison.Ordinal) &&
                string.Equals(pr.TargetBranch, command.Target.BaseRef, StringComparison.Ordinal) &&
                string.Equals(pr.HeadCommitId, command.Target.HeadSha, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(pr.BaseCommitId, command.Target.BaseSha, StringComparison.OrdinalIgnoreCase) &&
                pr.State.Equals("merged", StringComparison.OrdinalIgnoreCase) &&
                mergeProof is { Length: >= 7 } && mergeProof.All(Uri.IsHexDigit) &&
                (expectedMergeCommitSha is null || string.Equals(mergeProof, expectedMergeCommitSha, StringComparison.OrdinalIgnoreCase))
                ? new(SourceControlDeliveryStatus.Verified, PullRequestId: pr.Id, MergeCommitSha: mergeProof, Evidence: evidence, RemoteDeliveryVerified: true)
                : new(SourceControlDeliveryStatus.ReconciliationRequired, "Post-merge evidence does not prove an exact merged pull request.", MutationSent: true, MayHaveModifiedRemote: true, Evidence: evidence);
        }
        if (command.OperationKind == SourceControlDeliveryOperationKind.MarkReadyForReview)
            return evidence.PullRequest is { IsDraft: false } pr && pr.Id == command.Target.PullRequestId &&
                string.Equals(pr.SourceBranch, command.Target.HeadRef, StringComparison.Ordinal) &&
                string.Equals(pr.TargetBranch, command.Target.BaseRef, StringComparison.Ordinal) &&
                string.Equals(pr.HeadCommitId, command.Target.HeadSha, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(pr.BaseCommitId, command.Target.BaseSha, StringComparison.OrdinalIgnoreCase)
                ? new(SourceControlDeliveryStatus.Verified, PullRequestId: evidence.PullRequest.Id, Evidence: evidence)
                : new(SourceControlDeliveryStatus.ReconciliationRequired, "Post-ready evidence does not prove the pull request is ready.", MutationSent: true, MayHaveModifiedRemote: true, Evidence: evidence);
        return null;
    }

    private static bool Matches(SourceControlDeliveryCommand command, SourceControlDeliveryAuditEvent value) =>
        value.ProjectId == command.ProjectId && value.OperationKind == command.OperationKind &&
        (string.IsNullOrWhiteSpace(value.CommandContentHash) || string.Equals(value.CommandContentHash, command.CommandContentHash, StringComparison.OrdinalIgnoreCase)) &&
        string.Equals(value.WorkItemIdentity, command.WorkItemIdentity, StringComparison.Ordinal) &&
        string.Equals(value.ContractReference, command.ContractReference.ToString(), StringComparison.Ordinal) &&
        string.Equals(value.RepositoryIdentity, command.Target.CanonicalRepositoryIdentity, StringComparison.Ordinal) &&
        string.Equals(value.HeadRef, command.Target.HeadRef, StringComparison.Ordinal) &&
        string.Equals(value.ExpectedHeadSha, command.Target.HeadSha, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(value.AuditIdentity, command.AuditIdentity, StringComparison.Ordinal) &&
        string.Equals(value.EvidenceRevision, command.EvidenceRevision, StringComparison.OrdinalIgnoreCase);

    private static SourceControlDeliveryResult FromAudit(SourceControlDeliveryAuditEvent value, SourceControlDeliveryStatus status) =>
        new(status, value.ErrorMessage, value.ActualHeadSha, value.PullRequestId, value.MergeCommitSha, value.MutationSent,
            value.MayHaveModifiedRemote, RemoteDeliveryVerified: value.RemoteDeliveryVerified,
            TrackerSynchronization: value.TrackerOutcome is null ? null : new TrackerMutationResult(value.TrackerOutcome.Value));

    private static bool Same(PlanningExecutionContractReference left, PlanningExecutionContractReference right) =>
        left.ContractId == right.ContractId && left.Revision == right.Revision && left.SchemaVersion == right.SchemaVersion &&
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static bool Same(ExecutionRunAuthorityReference left, ExecutionRunAuthorityReference? right) =>
        right is not null && left.RunId == right.RunId && left.SchemaVersion == right.SchemaVersion &&
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static bool Same(ValidationGateDecisionReference left, ValidationGateDecisionReference right) =>
        left.DecisionId == right.DecisionId && left.SchemaVersion == right.SchemaVersion &&
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static bool HasBlockingCiEvidence(RemoteRepositoryEvidence evidence, out string reason)
    {
        if (evidence.StatusState is not (RemoteEvidenceState.Available or RemoteEvidenceState.NotConfigured) ||
            evidence.CheckState is not (RemoteEvidenceState.Available or RemoteEvidenceState.NotConfigured) ||
            evidence.CiState is not (RemoteEvidenceState.Available or RemoteEvidenceState.NotConfigured))
        {
            reason = "Remote status, check, or CI evidence is partial or unavailable.";
            return true;
        }

        if (evidence.CiResult is RemoteCiState.Failing or RemoteCiState.Pending or RemoteCiState.Cancelled or RemoteCiState.Unknown)
        {
            reason = "Remote CI evidence is explicitly failing, incomplete, or still pending.";
            return true;
        }

        foreach (var status in evidence.Statuses.Concat(evidence.Checks))
        {
            var state = status.State.ToLowerInvariant();
            var conclusion = status.Conclusion?.ToLowerInvariant();
            if (state is "failure" or "error" or "failed" or "failing" or "cancelled" or "canceled" or "pending" or "in_progress" or "queued" or "running" or "waiting" ||
                conclusion is "failure" or "failed" or "timed_out" or "action_required" or "cancelled" or "canceled" or "error" ||
                state == "completed" && conclusion is not ("success" or "succeeded" or "passing"))
            {
                reason = $"Remote check or commit status '{status.Name}' is not an explicit pass.";
                return true;
            }
        }

        reason = string.Empty;
        return false;
    }
}
