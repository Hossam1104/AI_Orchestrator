using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Approvals;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Validation;

namespace AIUsageMonitor.Application.MissionControl;

public enum MissionControlState
{
    Running,
    Waiting,
    Blocked,
    Failed,
    Review,
    Accepted,
    HumanApprovalRequired,
    Unknown
}

public enum MissionControlAttentionSeverity
{
    Critical,
    High,
    Medium,
    Informational
}

public enum MissionControlLimitationKind
{
    NoEvidence,
    EvidenceUnavailable,
    EvidenceStale,
    NotConfigured,
    CorrelationUnavailable,
    Degraded
}

public sealed record MissionControlProjectSummary(
    Guid ProjectId,
    string Name,
    ProjectStatus Status,
    string LocalPath,
    DateTimeOffset UpdatedAt);

public sealed record MissionControlCurrentWorkSummary(
    bool IsAuthoritative,
    string Reference,
    string Title,
    string CurrentStep,
    MissionControlState State,
    DateTimeOffset? ObservedAt,
    Guid? ExecutionRunId,
    string NextSafeAction);

public sealed record MissionControlRoleSummary(
    IReadOnlyList<MissionControlRoleAssignment> Assignments,
    string StatusText);

public sealed record MissionControlRoleAssignment(
    string Role,
    string DisplayName,
    bool Enabled,
    AgentAvailability Availability,
    AgentAuthenticationState Authentication,
    AgentEntitlementState Entitlement);

public sealed record MissionControlRepositorySummary(
    RepositorySelectionState Selection,
    RepositoryVerificationStatus VerificationStatus,
    string StatusText,
    string Branch,
    string RepositoryRoot,
    string Head,
    string WorkingTree,
    string RemoteText,
    DateTimeOffset? CapturedAt);

public sealed record MissionControlTrackerSummary(
    TrackerReferenceState ConfigurationState,
    string Type,
    string Reference,
    string ObservedState,
    string StatusText);

public sealed record MissionControlValidationSummary(
    ValidationGateDecisionState? State,
    string StatusText,
    string EvidenceReference,
    DateTimeOffset? ObservedAt);

public sealed record MissionControlReviewSummary(
    bool HasEvidence,
    string WorkflowState,
    string Verdict,
    string Severity,
    int BlockingFindingCount,
    int PendingAdjudicationCount,
    bool OwnerAttentionRequired,
    string OwnerAttentionReason,
    string NextRequiredAction,
    DateTimeOffset? ObservedAt);

public sealed record MissionControlApprovalSummary(
    bool HasEvidence,
    int PendingCount,
    string StatusText,
    string TargetSummary,
    string NextRequiredAction,
    DateTimeOffset? ObservedAt);

public sealed record MissionControlRuntimeSummary(
    bool HasExecutionEvidence,
    ExecutionRunStatus? LatestStatus,
    string StatusText,
    string ProcessEvidenceText,
    DateTimeOffset? ObservedAt,
    Guid? RunId);

public sealed record MissionControlAttentionItem(
    MissionControlAttentionSeverity Severity,
    MissionControlState State,
    string Title,
    string Reason,
    string Source,
    DateTimeOffset? ObservedAt,
    string NextSafeAction);

public sealed record MissionControlLimitation(
    string Section,
    MissionControlLimitationKind Kind,
    string Message,
    string Source,
    DateTimeOffset? ObservedAt = null);

public sealed class MissionControlSnapshot
{
    public MissionControlSnapshot(
        Guid projectId,
        DateTimeOffset readAt,
        MissionControlState state,
        string stateReason,
        MissionControlProjectSummary project,
        MissionControlCurrentWorkSummary currentWork,
        MissionControlRoleSummary roles,
        MissionControlRepositorySummary repository,
        MissionControlTrackerSummary tracker,
        MissionControlValidationSummary validation,
        MissionControlReviewSummary review,
        MissionControlApprovalSummary approval,
        MissionControlRuntimeSummary runtime,
        IReadOnlyList<MissionControlAttentionItem>? attentionItems = null,
        IReadOnlyList<MissionControlLimitation>? limitations = null)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("Project id is required.", nameof(projectId));
        if (!Enum.IsDefined(state))
            throw new ArgumentException("Mission Control state is undefined.", nameof(state));
        if (readAt == default)
            throw new ArgumentException("Mission Control read time is required.", nameof(readAt));
        if (string.IsNullOrWhiteSpace(stateReason))
            throw new ArgumentException("Mission Control state reason is required.", nameof(stateReason));

        ProjectId = projectId;
        ReadAt = readAt;
        State = state;
        StateReason = stateReason.Trim();
        Project = project ?? throw new ArgumentNullException(nameof(project));
        CurrentWork = currentWork ?? throw new ArgumentNullException(nameof(currentWork));
        Roles = roles ?? throw new ArgumentNullException(nameof(roles));
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        Tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        Validation = validation ?? throw new ArgumentNullException(nameof(validation));
        Review = review ?? throw new ArgumentNullException(nameof(review));
        Approval = approval ?? throw new ArgumentNullException(nameof(approval));
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        AttentionItems = (attentionItems ?? Array.Empty<MissionControlAttentionItem>()).Take(24).ToArray();
        Limitations = (limitations ?? Array.Empty<MissionControlLimitation>()).Take(32).ToArray();
    }

    public Guid ProjectId { get; }
    public DateTimeOffset ReadAt { get; }
    public MissionControlState State { get; }
    public string StateReason { get; }
    public MissionControlProjectSummary Project { get; }
    public MissionControlCurrentWorkSummary CurrentWork { get; }
    public MissionControlRoleSummary Roles { get; }
    public MissionControlRepositorySummary Repository { get; }
    public MissionControlTrackerSummary Tracker { get; }
    public MissionControlValidationSummary Validation { get; }
    public MissionControlReviewSummary Review { get; }
    public MissionControlApprovalSummary Approval { get; }
    public MissionControlRuntimeSummary Runtime { get; }
    public IReadOnlyList<MissionControlAttentionItem> AttentionItems { get; }
    public IReadOnlyList<MissionControlLimitation> Limitations { get; }
}

public interface IMissionControlReadModelService
{
    Task<MissionControlSnapshot> ReadAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}
