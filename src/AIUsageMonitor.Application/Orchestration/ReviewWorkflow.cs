using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Application.Validation;

namespace AIUsageMonitor.Application.Orchestration;

public static class ReviewWorkflowLimits
{
    public const int MaxRemediationAttempts = 2;
    public const int MaxLifecycleEvents = 1024;
    public const int MaxReviewRecords = 512;
    public const int MaxEvidenceReferences = 32;
    public const int MaxReferenceLength = 1_000;
    public const int MaxAuthorityReferenceLength = 500;
    public const int MaxReasonLength = 2_000;
    public const int MaxOwnerAttentionReasonLength = 500;
}

public enum ReviewWorkflowEventKind
{
    FindingAdjudicated,
    RemediationStarted,
    RemediationCompleted,
    RevalidationRecorded,
    RereviewLinked,
    HumanDecisionRequired
}

public enum ReviewFindingAdjudication
{
    Accepted,
    Rejected,
    Deferred
}

public enum ReviewAuthorityKind
{
    Planner,
    AcceptanceAuthority,
    HumanOwner,
    Other,
    Reviewer
}

public enum ReviewWorkflowState
{
    AwaitingAdjudication,
    ReadyForAcceptanceAuthority,
    RemediationRequired,
    RevalidationRequired,
    RereviewRequired,
    HumanDecisionRequired
}

public enum ReviewWorkflowNextAction
{
    AdjudicateFindings,
    RunRemediation,
    RunRevalidation,
    RunRereview,
    HumanDecision,
    SendToAcceptanceAuthority
}

public enum ReviewWorkflowMutationStatus
{
    Created,
    InvalidRequest,
    NotFound,
    Conflict,
    PersistenceUnavailable
}

/// <summary>
/// A typed, append-only event for the review/remediation boundary. The event intentionally stores
/// references and bounded metadata only; reviewer output, prompts, source, diffs, and secrets are
/// never part of this contract.
/// </summary>
public sealed class ReviewWorkflowEvent
{
    public ReviewWorkflowEvent(
        Guid eventId,
        Guid projectId,
        Guid rootReviewId,
        Guid currentReviewId,
        ReviewWorkflowEventKind kind,
        DateTimeOffset occurredAt,
        string? findingId = null,
        ReviewFindingAdjudication? disposition = null,
        string? authorityReference = null,
        ReviewAuthorityKind? authorityKind = null,
        string? reason = null,
        int? attemptNumber = null,
        ExecutionRunAuthorityReference? executionRunAuthorityReference = null,
        HandoffPackageReference? handoffPackageReference = null,
        IReadOnlyList<string>? evidenceReferences = null,
        ValidationGateDecisionReference? validationDecisionReference = null,
        ValidationGateDecisionState? validationState = null,
        Guid? linkedReviewId = null)
    {
        if (eventId == Guid.Empty || projectId == Guid.Empty || rootReviewId == Guid.Empty || currentReviewId == Guid.Empty)
            throw new ArgumentException("Review workflow event identity is required.");
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (occurredAt == default)
            throw new ArgumentException("Review workflow event time is required.", nameof(occurredAt));

        EventId = eventId;
        ProjectId = projectId;
        RootReviewId = rootReviewId;
        CurrentReviewId = currentReviewId;
        Kind = kind;
        OccurredAt = occurredAt;
        FindingId = OptionalText(findingId, nameof(findingId), ReviewWorkflowLimits.MaxReferenceLength);
        Disposition = disposition;
        AuthorityReference = OptionalText(authorityReference, nameof(authorityReference), ReviewWorkflowLimits.MaxAuthorityReferenceLength);
        AuthorityKind = authorityKind;
        Reason = OptionalText(reason, nameof(reason), ReviewWorkflowLimits.MaxReasonLength);
        AttemptNumber = attemptNumber;
        ExecutionRunAuthorityReference = executionRunAuthorityReference;
        HandoffPackageReference = handoffPackageReference;
        EvidenceReferences = CopyEvidenceReferences(evidenceReferences);
        ValidationDecisionReference = validationDecisionReference;
        ValidationState = validationState;
        LinkedReviewId = linkedReviewId;

        ValidateShape();
    }

    public Guid EventId { get; }
    public Guid ProjectId { get; }
    public Guid RootReviewId { get; }
    public Guid CurrentReviewId { get; }
    public ReviewWorkflowEventKind Kind { get; }
    public DateTimeOffset OccurredAt { get; }
    public string? FindingId { get; }
    public ReviewFindingAdjudication? Disposition { get; }
    public string? AuthorityReference { get; }
    public ReviewAuthorityKind? AuthorityKind { get; }
    public string? Reason { get; }
    public int? AttemptNumber { get; }
    public ExecutionRunAuthorityReference? ExecutionRunAuthorityReference { get; }
    public HandoffPackageReference? HandoffPackageReference { get; }
    public IReadOnlyList<string> EvidenceReferences { get; }
    public ValidationGateDecisionReference? ValidationDecisionReference { get; }
    public ValidationGateDecisionState? ValidationState { get; }
    public Guid? LinkedReviewId { get; }

    internal ReviewWorkflowEvent WithOccurredAt(DateTimeOffset occurredAt) => new(
        EventId, ProjectId, RootReviewId, CurrentReviewId, Kind, occurredAt, FindingId, Disposition,
        AuthorityReference, AuthorityKind, Reason, AttemptNumber, ExecutionRunAuthorityReference,
        HandoffPackageReference, EvidenceReferences, ValidationDecisionReference, ValidationState,
        LinkedReviewId);

    private void ValidateShape()
    {
        switch (Kind)
        {
            case ReviewWorkflowEventKind.FindingAdjudicated:
                Required(FindingId, nameof(FindingId));
                if (!Disposition.HasValue || !Enum.IsDefined(Disposition.Value))
                    throw new ArgumentException("A finding adjudication is required.", nameof(Disposition));
                Required(AuthorityReference, nameof(AuthorityReference));
                if (!AuthorityKind.HasValue || !Enum.IsDefined(AuthorityKind.Value))
                    throw new ArgumentException("An adjudication authority kind is required.", nameof(AuthorityKind));
                if (AuthorityKind == ReviewAuthorityKind.Reviewer)
                    throw new ArgumentException("A reviewer cannot be an adjudication authority.", nameof(AuthorityKind));
                Required(Reason, nameof(Reason));
                break;
            case ReviewWorkflowEventKind.RemediationStarted:
                ValidateAttempt();
                break;
            case ReviewWorkflowEventKind.RemediationCompleted:
                ValidateAttempt();
                if (ExecutionRunAuthorityReference is null)
                    throw new ArgumentException("Remediation completion requires an exact execution-run authority reference.");
                break;
            case ReviewWorkflowEventKind.RevalidationRecorded:
                ValidateAttempt();
                if (ValidationDecisionReference is null || !ValidationState.HasValue || !Enum.IsDefined(ValidationState.Value))
                    throw new ArgumentException("Revalidation requires an exact validation-gate decision reference and state.");
                break;
            case ReviewWorkflowEventKind.RereviewLinked:
                if (!LinkedReviewId.HasValue || LinkedReviewId.Value == Guid.Empty || LinkedReviewId.Value == CurrentReviewId)
                    throw new ArgumentException("A re-review link must identify a different review.", nameof(LinkedReviewId));
                break;
            case ReviewWorkflowEventKind.HumanDecisionRequired:
                Required(Reason, nameof(Reason), ReviewWorkflowLimits.MaxOwnerAttentionReasonLength);
                break;
        }
    }

    private void ValidateAttempt()
    {
        if (!AttemptNumber.HasValue || AttemptNumber.Value is < 1 or > ReviewWorkflowLimits.MaxRemediationAttempts)
            throw new ArgumentOutOfRangeException(nameof(AttemptNumber), "Remediation attempts are bounded to 1..2.");
    }

    private static IReadOnlyList<string> CopyEvidenceReferences(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
            return Array.Empty<string>();
        if (values.Count > ReviewWorkflowLimits.MaxEvidenceReferences)
            throw new ArgumentException("Evidence references exceed the supported bound.", nameof(values));

        var result = new List<string>(values.Count);
        foreach (var value in values)
        {
            var normalized = Required(value, nameof(values), ReviewWorkflowLimits.MaxReferenceLength);
            if (!result.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                result.Add(normalized);
        }
        return result.AsReadOnly();
    }

    private static string? OptionalText(string? value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        if (normalized.Any(char.IsControl))
            throw new ArgumentException("Workflow text contains unsupported control characters.", parameterName);
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException("Workflow text exceeds its supported bound.", parameterName);
    }

    private static string Required(string? value, string parameterName, int maximumLength = ReviewWorkflowLimits.MaxReasonLength) =>
        OptionalText(value, parameterName, maximumLength) ?? throw new ArgumentException("A bounded workflow value is required.", parameterName);
}

public sealed record ReviewFindingAdjudicationRequest(
    Guid ProjectId,
    Guid RootReviewId,
    Guid CurrentReviewId,
    string FindingId,
    ReviewFindingAdjudication Disposition,
    string AuthorityReference,
    ReviewAuthorityKind AuthorityKind,
    string Reason,
    Guid? EventId = null);

public sealed record ReviewRemediationStartRequest(
    Guid ProjectId,
    Guid RootReviewId,
    Guid CurrentReviewId,
    ExecutionRunAuthorityReference? ExecutionRunAuthorityReference = null,
    HandoffPackageReference? HandoffPackageReference = null,
    Guid? EventId = null);

public sealed record ReviewRemediationCompletionRequest(
    Guid ProjectId,
    Guid RootReviewId,
    Guid CurrentReviewId,
    int AttemptNumber,
    ExecutionRunAuthorityReference? ExecutionRunAuthorityReference = null,
    HandoffPackageReference? HandoffPackageReference = null,
    IReadOnlyList<string>? EvidenceReferences = null,
    Guid? EventId = null);

public sealed record ReviewRevalidationRequest(
    Guid ProjectId,
    Guid RootReviewId,
    Guid CurrentReviewId,
    int AttemptNumber,
    ValidationGateDecisionReference ValidationDecisionReference,
    Guid? EventId = null);

public sealed record ReviewRereviewLinkRequest(
    Guid ProjectId,
    Guid RootReviewId,
    Guid PreviousReviewId,
    Guid RereviewId,
    Guid? EventId = null);

public sealed record ReviewHumanDecisionRequest(
    Guid ProjectId,
    Guid RootReviewId,
    Guid CurrentReviewId,
    string Reason,
    Guid? EventId = null);

public sealed record ReviewWorkflowMutationResult(
    ReviewWorkflowMutationStatus Status,
    ReviewWorkflowEvent? Event = null,
    string? ErrorMessage = null)
{
    public bool Succeeded => Status == ReviewWorkflowMutationStatus.Created && Event is not null;
}

/// <summary>Read-only provider-independent summary consumed later by Mission Control.</summary>
public sealed class ReviewInboxItem
{
    public Guid ProjectId { get; internal set; }
    public Guid RootReviewId { get; internal set; }
    public Guid CurrentReviewId { get; internal set; }
    public DateTimeOffset LatestTimestamp { get; internal set; }
    public string ReviewerReference { get; internal set; } = string.Empty;
    public string CurrentVerdict { get; internal set; } = string.Empty;
    public string CurrentSeverity { get; internal set; } = string.Empty;
    public ReviewWorkflowState WorkflowState { get; internal set; }
    public int TotalCurrentFindings { get; internal set; }
    public int BlockingFindingCount { get; internal set; }
    public int PendingAdjudicationCount { get; internal set; }
    public int AcceptedCount { get; internal set; }
    public int RejectedCount { get; internal set; }
    public int DeferredCount { get; internal set; }
    public int RemediationAttemptCount { get; internal set; }
    public string? LastRemediationReference { get; internal set; }
    public ValidationGateDecisionReference? LatestValidationReference { get; internal set; }
    public ValidationGateDecisionState? LatestValidationState { get; internal set; }
    public bool OwnerAttentionRequired { get; internal set; }
    public string? OwnerAttentionReason { get; internal set; }
    public ReviewWorkflowNextAction NextRequiredAction { get; internal set; }
    public int? ActiveRemediationAttempt { get; internal set; }
}

public interface IReviewMetadataReader
{
    Task<HistoryReadResult<ReviewMetadata>> ReadAllReviewsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}

public enum ReviewWorkflowStoreWriteStatus
{
    Created,
    DuplicateEvent,
    Unavailable
}

public sealed record ReviewWorkflowStoreWriteResult(
    ReviewWorkflowStoreWriteStatus Status,
    string? ErrorMessage = null)
{
    public bool Succeeded => Status == ReviewWorkflowStoreWriteStatus.Created;
}

public interface IReviewWorkflowStore
{
    Task<ReviewWorkflowStoreWriteResult> AppendAsync(
        ReviewWorkflowEvent value,
        CancellationToken cancellationToken = default);

    Task<HistoryReadResult<ReviewWorkflowEvent>> ReadAllAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}

public sealed class ReviewWorkflowInboxReadResult
{
    public ReviewWorkflowInboxReadResult(
        IReadOnlyList<ReviewInboxItem> items,
        HistoryReadStatus status,
        IReadOnlyList<HistoryReadIssue>? issues = null,
        string? errorMessage = null)
    {
        Items = items?.ToArray() ?? throw new ArgumentNullException(nameof(items));
        Status = status;
        Issues = (issues ?? Array.Empty<HistoryReadIssue>()).ToArray();
        ErrorMessage = errorMessage;
    }

    public IReadOnlyList<ReviewInboxItem> Items { get; }
    public HistoryReadStatus Status { get; }
    public IReadOnlyList<HistoryReadIssue> Issues { get; }
    public string? ErrorMessage { get; }
    public bool IsUsable => Status == HistoryReadStatus.Success && ErrorMessage is null;
}

public sealed class ReviewWorkflowCaseReadResult
{
    public ReviewWorkflowCaseReadResult(
        Guid projectId,
        Guid rootReviewId,
        ReviewInboxItem? inboxItem,
        IReadOnlyList<ReviewMetadata> reviews,
        IReadOnlyList<ReviewWorkflowEvent> events,
        HistoryReadStatus status,
        IReadOnlyList<HistoryReadIssue>? issues = null,
        string? errorMessage = null)
    {
        ProjectId = projectId;
        RootReviewId = rootReviewId;
        InboxItem = inboxItem;
        Reviews = reviews?.ToArray() ?? throw new ArgumentNullException(nameof(reviews));
        Events = events?.ToArray() ?? throw new ArgumentNullException(nameof(events));
        Status = status;
        Issues = (issues ?? Array.Empty<HistoryReadIssue>()).ToArray();
        ErrorMessage = errorMessage;
    }

    public Guid ProjectId { get; }
    public Guid RootReviewId { get; }
    public ReviewInboxItem? InboxItem { get; }
    public IReadOnlyList<ReviewMetadata> Reviews { get; }
    public IReadOnlyList<ReviewWorkflowEvent> Events { get; }
    public HistoryReadStatus Status { get; }
    public IReadOnlyList<HistoryReadIssue> Issues { get; }
    public string? ErrorMessage { get; }
    public bool IsUsable => Status == HistoryReadStatus.Success && InboxItem is not null && ErrorMessage is null;
}

public interface IReviewWorkflowService
{
    Task<ReviewWorkflowMutationResult> AdjudicateFindingAsync(ReviewFindingAdjudicationRequest request, CancellationToken cancellationToken = default);
    Task<ReviewWorkflowMutationResult> StartRemediationAsync(ReviewRemediationStartRequest request, CancellationToken cancellationToken = default);
    Task<ReviewWorkflowMutationResult> CompleteRemediationAsync(ReviewRemediationCompletionRequest request, CancellationToken cancellationToken = default);
    Task<ReviewWorkflowMutationResult> RecordRevalidationAsync(ReviewRevalidationRequest request, CancellationToken cancellationToken = default);
    Task<ReviewWorkflowMutationResult> LinkRereviewAsync(ReviewRereviewLinkRequest request, CancellationToken cancellationToken = default);
    Task<ReviewWorkflowMutationResult> RequireHumanDecisionAsync(ReviewHumanDecisionRequest request, CancellationToken cancellationToken = default);
    Task<ReviewWorkflowInboxReadResult> ReadInboxAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<ReviewWorkflowCaseReadResult> ReadCaseAsync(Guid projectId, Guid rootReviewId, CancellationToken cancellationToken = default);
}

public sealed class ReviewWorkflowService : IReviewWorkflowService
{
    private readonly IReviewMetadataReader _reviews;
    private readonly IReviewWorkflowStore _events;
    private readonly IValidationGateDecisionRepository? _validationDecisions;
    private readonly IExecutionRunAuthorityRepository? _runAuthorities;
    private readonly IHandoffPackageRepository? _handoffs;
    private readonly IClock _clock;
    private readonly IHandoffRedactionService _redaction;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    public ReviewWorkflowService(
        IReviewMetadataReader reviews,
        IReviewWorkflowStore events,
        IClock clock,
        IValidationGateDecisionRepository? validationDecisions = null,
        IExecutionRunAuthorityRepository? runAuthorities = null,
        IHandoffPackageRepository? handoffs = null,
        IHandoffRedactionService? redaction = null)
    {
        _reviews = reviews ?? throw new ArgumentNullException(nameof(reviews));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _validationDecisions = validationDecisions;
        _runAuthorities = runAuthorities;
        _handoffs = handoffs;
        _redaction = redaction ?? new HandoffRedactionService();
    }

    public Task<ReviewWorkflowMutationResult> AdjudicateFindingAsync(ReviewFindingAdjudicationRequest request, CancellationToken cancellationToken = default) =>
        MutateAsync(() => AdjudicateFindingCoreAsync(request, cancellationToken), cancellationToken);

    private async Task<ReviewWorkflowMutationResult> AdjudicateFindingCoreAsync(ReviewFindingAdjudicationRequest request, CancellationToken cancellationToken)
    {
        if (request is null) return Invalid("An adjudication request is required.");
        if (!ValidIdentity(request.ProjectId, request.RootReviewId, request.CurrentReviewId) ||
            !Bounded(request.FindingId, ReviewWorkflowLimits.MaxReferenceLength) ||
            !Bounded(request.AuthorityReference, ReviewWorkflowLimits.MaxAuthorityReferenceLength) ||
            !Bounded(request.Reason, ReviewWorkflowLimits.MaxReasonLength) ||
            !SafeIdentity(request.FindingId) || !SafeIdentity(request.AuthorityReference))
            return Invalid("Adjudication identity, authority, finding, and reason are required.");
        if (!Enum.IsDefined(request.Disposition) || !Enum.IsDefined(request.AuthorityKind))
            return Invalid("Adjudication values are undefined.");
        if (request.AuthorityKind == ReviewAuthorityKind.Reviewer)
            return Invalid("The reviewer cannot act as an adjudication or acceptance authority.");

        var context = await LoadAsync(request.ProjectId, request.RootReviewId, cancellationToken).ConfigureAwait(false);
        if (!context.IsUsable || context.InboxItem is null)
            return FromReadFailure(context);
        if (context.InboxItem.CurrentReviewId != request.CurrentReviewId)
            return Invalid("The supplied review is not the current review in this workflow.");
        if (context.InboxItem.WorkflowState is ReviewWorkflowState.RevalidationRequired or
            ReviewWorkflowState.RereviewRequired or ReviewWorkflowState.HumanDecisionRequired)
            return Invalid("Finding adjudication is closed until the workflow returns to a current review decision state.");

        var review = context.Reviews.SingleOrDefault(value => value.ReviewId == request.CurrentReviewId);
        var finding = review?.Findings.SingleOrDefault(value => string.Equals(value.FindingId, request.FindingId, StringComparison.OrdinalIgnoreCase));
        if (finding is null)
            return new(ReviewWorkflowMutationStatus.NotFound, ErrorMessage: "The finding does not exist in the exact current review.");
        if (context.Events.Any(value => value.Kind == ReviewWorkflowEventKind.FindingAdjudicated &&
                                        value.CurrentReviewId == request.CurrentReviewId &&
                                        string.Equals(value.FindingId, request.FindingId, StringComparison.OrdinalIgnoreCase)))
            return new(ReviewWorkflowMutationStatus.Conflict, ErrorMessage: "The finding already has an authoritative adjudication.");
        if (string.Equals(review!.ReviewerReference, request.AuthorityReference.Trim(), StringComparison.OrdinalIgnoreCase) &&
            request.AuthorityKind == ReviewAuthorityKind.AcceptanceAuthority)
            return Invalid("The reviewer cannot adjudicate its own finding as product acceptance.");

        var value = new ReviewWorkflowEvent(
            request.EventId ?? Guid.NewGuid(), request.ProjectId, request.RootReviewId, request.CurrentReviewId,
            ReviewWorkflowEventKind.FindingAdjudicated, _clock.UtcNow, request.FindingId, request.Disposition,
            request.AuthorityReference, request.AuthorityKind, RedactReason(request.Reason));
        return await AppendAsync(value, cancellationToken).ConfigureAwait(false);
    }

    public Task<ReviewWorkflowMutationResult> StartRemediationAsync(ReviewRemediationStartRequest request, CancellationToken cancellationToken = default) =>
        MutateAsync(() => StartRemediationCoreAsync(request, cancellationToken), cancellationToken);

    private async Task<ReviewWorkflowMutationResult> StartRemediationCoreAsync(ReviewRemediationStartRequest request, CancellationToken cancellationToken)
    {
        if (request is null || !ValidIdentity(request.ProjectId, request.RootReviewId, request.CurrentReviewId))
            return Invalid("Remediation identity is required.");
        var context = await LoadAsync(request.ProjectId, request.RootReviewId, cancellationToken).ConfigureAwait(false);
        if (!context.IsUsable || context.InboxItem is null) return FromReadFailure(context);
        if (context.InboxItem.CurrentReviewId != request.CurrentReviewId)
            return Invalid("The supplied review is not the current review in this workflow.");
        if (context.InboxItem.WorkflowState != ReviewWorkflowState.RemediationRequired)
            return Invalid("Remediation is allowed only for a workflow requiring remediation.");
        if (context.InboxItem.ActiveRemediationAttempt.HasValue)
            return new(ReviewWorkflowMutationStatus.Conflict, ErrorMessage: "A remediation attempt is already active.");
        if (context.InboxItem.RemediationAttemptCount >= ReviewWorkflowLimits.MaxRemediationAttempts)
            return Invalid("The bounded remediation-attempt limit has been exhausted.");
        if (!await ValidateReferencesAsync(request.ProjectId, request.ExecutionRunAuthorityReference, request.HandoffPackageReference, cancellationToken).ConfigureAwait(false))
            return Invalid("A remediation authority reference is missing, invalid, or belongs to another project.");

        var attempt = context.InboxItem.RemediationAttemptCount + 1;
        var value = new ReviewWorkflowEvent(
            request.EventId ?? Guid.NewGuid(), request.ProjectId, request.RootReviewId, request.CurrentReviewId,
            ReviewWorkflowEventKind.RemediationStarted, _clock.UtcNow, attemptNumber: attempt,
            executionRunAuthorityReference: request.ExecutionRunAuthorityReference,
            handoffPackageReference: request.HandoffPackageReference);
        return await AppendAsync(value, cancellationToken).ConfigureAwait(false);
    }

    public Task<ReviewWorkflowMutationResult> CompleteRemediationAsync(ReviewRemediationCompletionRequest request, CancellationToken cancellationToken = default) =>
        MutateAsync(() => CompleteRemediationCoreAsync(request, cancellationToken), cancellationToken);

    private async Task<ReviewWorkflowMutationResult> CompleteRemediationCoreAsync(ReviewRemediationCompletionRequest request, CancellationToken cancellationToken)
    {
        if (request is null || !ValidIdentity(request.ProjectId, request.RootReviewId, request.CurrentReviewId))
            return Invalid("Remediation identity is required.");
        if (request.AttemptNumber is < 1 or > ReviewWorkflowLimits.MaxRemediationAttempts)
            return Invalid("Remediation attempt must be numbered 1..2.");
        if (!ValidEvidenceReferences(request.EvidenceReferences))
            return Invalid("Remediation evidence references are invalid or exceed their supported bound.");
        if (request.ExecutionRunAuthorityReference is null)
            return Invalid("Remediation completion requires an exact execution-run authority reference.");
        var context = await LoadAsync(request.ProjectId, request.RootReviewId, cancellationToken).ConfigureAwait(false);
        if (!context.IsUsable || context.InboxItem is null) return FromReadFailure(context);
        if (context.InboxItem.CurrentReviewId != request.CurrentReviewId || context.InboxItem.ActiveRemediationAttempt != request.AttemptNumber)
            return Invalid("Completion must bind to the exact active remediation attempt.");
        if (!await ValidateReferencesAsync(request.ProjectId, request.ExecutionRunAuthorityReference, request.HandoffPackageReference, cancellationToken).ConfigureAwait(false))
            return Invalid("A remediation authority reference is missing, invalid, or belongs to another project.");

        var value = new ReviewWorkflowEvent(
            request.EventId ?? Guid.NewGuid(), request.ProjectId, request.RootReviewId, request.CurrentReviewId,
            ReviewWorkflowEventKind.RemediationCompleted, _clock.UtcNow, attemptNumber: request.AttemptNumber,
            executionRunAuthorityReference: request.ExecutionRunAuthorityReference,
            handoffPackageReference: request.HandoffPackageReference,
            evidenceReferences: request.EvidenceReferences);
        return await AppendAsync(value, cancellationToken).ConfigureAwait(false);
    }

    public Task<ReviewWorkflowMutationResult> RecordRevalidationAsync(ReviewRevalidationRequest request, CancellationToken cancellationToken = default) =>
        MutateAsync(() => RecordRevalidationCoreAsync(request, cancellationToken), cancellationToken);

    private async Task<ReviewWorkflowMutationResult> RecordRevalidationCoreAsync(ReviewRevalidationRequest request, CancellationToken cancellationToken)
    {
        if (request is null || !ValidIdentity(request.ProjectId, request.RootReviewId, request.CurrentReviewId) || request.ValidationDecisionReference is null)
            return Invalid("Revalidation identity and exact validation decision reference are required.");
        if (request.AttemptNumber is < 1 or > ReviewWorkflowLimits.MaxRemediationAttempts)
            return Invalid("Revalidation attempt must be numbered 1..2.");
        if (_validationDecisions is null)
            return Invalid("The APO-48 validation decision repository is unavailable.");

        var context = await LoadAsync(request.ProjectId, request.RootReviewId, cancellationToken).ConfigureAwait(false);
        if (!context.IsUsable || context.InboxItem is null) return FromReadFailure(context);
        if (context.InboxItem.CurrentReviewId != request.CurrentReviewId || context.InboxItem.ActiveRemediationAttempt.HasValue ||
            context.InboxItem.WorkflowState != ReviewWorkflowState.RevalidationRequired)
            return Invalid("Revalidation is allowed only after completion of the exact active remediation attempt.");
        if (context.Events.Any(value => value.Kind == ReviewWorkflowEventKind.RevalidationRecorded && value.AttemptNumber == request.AttemptNumber))
            return new(ReviewWorkflowMutationStatus.Conflict, ErrorMessage: "This remediation attempt already has revalidation.");

        var remediationCompletion = context.Events.SingleOrDefault(value =>
            value.Kind == ReviewWorkflowEventKind.RemediationCompleted && value.AttemptNumber == request.AttemptNumber);
        if (remediationCompletion?.ExecutionRunAuthorityReference is null)
            return Invalid("The exact remediation execution authority is missing from the completed attempt.");

        var decision = await _validationDecisions.GetAsync(request.ProjectId, request.ValidationDecisionReference.DecisionId, cancellationToken).ConfigureAwait(false);
        if (!decision.IsValid || decision.Decision is null || decision.Decision.ProjectId != request.ProjectId ||
            decision.Decision.Reference.DecisionId != request.ValidationDecisionReference.DecisionId ||
            decision.Decision.Reference.SchemaVersion != request.ValidationDecisionReference.SchemaVersion ||
            !string.Equals(decision.Decision.Reference.ContentHash, request.ValidationDecisionReference.ContentHash, StringComparison.OrdinalIgnoreCase) ||
            !Same(decision.Decision.ExecutionRunAuthorityReference, remediationCompletion.ExecutionRunAuthorityReference) ||
            decision.Decision.DecidedAt < remediationCompletion.OccurredAt)
            return Invalid("The exact APO-48 validation-gate decision is missing, stale, partial, or invalid.");

        var value = new ReviewWorkflowEvent(
            request.EventId ?? Guid.NewGuid(), request.ProjectId, request.RootReviewId, request.CurrentReviewId,
            ReviewWorkflowEventKind.RevalidationRecorded, _clock.UtcNow, attemptNumber: request.AttemptNumber,
            validationDecisionReference: decision.Decision.Reference, validationState: decision.Decision.State);
        return await AppendAsync(value, cancellationToken).ConfigureAwait(false);
    }

    public Task<ReviewWorkflowMutationResult> LinkRereviewAsync(ReviewRereviewLinkRequest request, CancellationToken cancellationToken = default) =>
        MutateAsync(() => LinkRereviewCoreAsync(request, cancellationToken), cancellationToken);

    private async Task<ReviewWorkflowMutationResult> LinkRereviewCoreAsync(ReviewRereviewLinkRequest request, CancellationToken cancellationToken)
    {
        if (request is null || !ValidIdentity(request.ProjectId, request.RootReviewId, request.PreviousReviewId) || request.RereviewId == Guid.Empty)
            return Invalid("Re-review identity is required.");
        var context = await LoadAsync(request.ProjectId, request.RootReviewId, cancellationToken).ConfigureAwait(false);
        if (!context.IsUsable || context.InboxItem is null) return FromReadFailure(context);
        if (context.InboxItem.CurrentReviewId != request.PreviousReviewId || context.InboxItem.WorkflowState != ReviewWorkflowState.RereviewRequired)
            return Invalid("A re-review can be linked only after successful revalidation of the current attempt.");
        var allReviews = await _reviews.ReadAllReviewsAsync(request.ProjectId, cancellationToken).ConfigureAwait(false);
        if (allReviews.Status != HistoryReadStatus.Success || allReviews.Issues.Count > 0)
            return new(ReviewWorkflowMutationStatus.PersistenceUnavailable, ErrorMessage: "Authoritative review history is incomplete or unavailable.");
        if (allReviews.Records.All(value => value.ReviewId != request.RereviewId))
            return new(ReviewWorkflowMutationStatus.NotFound, ErrorMessage: "The re-review does not exist in authoritative review storage.");

        var allEvents = await _events.ReadAllAsync(request.ProjectId, cancellationToken).ConfigureAwait(false);
        if (allEvents.Status != HistoryReadStatus.Success || allEvents.Issues.Count > 0)
            return new(ReviewWorkflowMutationStatus.PersistenceUnavailable, ErrorMessage: "Authoritative review workflow history is incomplete or unavailable.");

        var successfulRevalidation = context.Events
            .Where(value => value.Kind == ReviewWorkflowEventKind.RevalidationRecorded && value.ValidationState == ValidationGateDecisionState.Satisfied)
            .OrderByDescending(value => value.OccurredAt)
            .ThenByDescending(value => value.EventId)
            .FirstOrDefault();
        if (successfulRevalidation is null)
            return Invalid("A successful revalidation is required before linking a re-review.");

        var review = allReviews.Records.Single(value => value.ReviewId == request.RereviewId);
        var visitedReviews = new HashSet<Guid> { request.RootReviewId, request.PreviousReviewId };
        foreach (var linked in context.Events.Where(value => value.Kind == ReviewWorkflowEventKind.RereviewLinked))
        {
            visitedReviews.Add(linked.CurrentReviewId);
            if (linked.LinkedReviewId.HasValue) visitedReviews.Add(linked.LinkedReviewId.Value);
        }
        if (!visitedReviews.Add(review.ReviewId))
            return new(ReviewWorkflowMutationStatus.Conflict, ErrorMessage: "The re-review is already part of this workflow or would create a cycle.");
        if (review.OccurredAt < successfulRevalidation.OccurredAt)
            return Invalid("The re-review must be created at or after the successful revalidation.");

        if (allEvents.Records.Any(value => value.RootReviewId != request.RootReviewId &&
            (value.RootReviewId == review.ReviewId || value.CurrentReviewId == review.ReviewId || value.LinkedReviewId == review.ReviewId)))
            return new(ReviewWorkflowMutationStatus.Conflict, ErrorMessage: "The re-review is already owned by another workflow.");

        var value = new ReviewWorkflowEvent(
            request.EventId ?? Guid.NewGuid(), request.ProjectId, request.RootReviewId, request.PreviousReviewId,
            ReviewWorkflowEventKind.RereviewLinked, _clock.UtcNow, linkedReviewId: review.ReviewId);
        return await AppendAsync(value, cancellationToken).ConfigureAwait(false);
    }

    public Task<ReviewWorkflowMutationResult> RequireHumanDecisionAsync(ReviewHumanDecisionRequest request, CancellationToken cancellationToken = default) =>
        MutateAsync(() => RequireHumanDecisionCoreAsync(request, cancellationToken), cancellationToken);

    private async Task<ReviewWorkflowMutationResult> RequireHumanDecisionCoreAsync(ReviewHumanDecisionRequest request, CancellationToken cancellationToken)
    {
        if (request is null || !ValidIdentity(request.ProjectId, request.RootReviewId, request.CurrentReviewId) ||
            !Bounded(request.Reason, ReviewWorkflowLimits.MaxOwnerAttentionReasonLength))
            return Invalid("Human-decision identity and reason are required.");
        var context = await LoadAsync(request.ProjectId, request.RootReviewId, cancellationToken).ConfigureAwait(false);
        if (!context.IsUsable || context.InboxItem is null) return FromReadFailure(context);
        if (context.InboxItem.CurrentReviewId != request.CurrentReviewId)
            return Invalid("The supplied review is not the current review in this workflow.");
        if (context.InboxItem.WorkflowState == ReviewWorkflowState.HumanDecisionRequired)
            return new(ReviewWorkflowMutationStatus.Conflict, ErrorMessage: "The workflow already requires an owner decision.");
        var value = new ReviewWorkflowEvent(
            request.EventId ?? Guid.NewGuid(), request.ProjectId, request.RootReviewId, request.CurrentReviewId,
            ReviewWorkflowEventKind.HumanDecisionRequired, _clock.UtcNow, reason: RedactReason(request.Reason));
        return await AppendAsync(value, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ReviewWorkflowInboxReadResult> ReadInboxAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
            return new([], HistoryReadStatus.Unavailable, errorMessage: "Project id is required.");
        var reviews = await _reviews.ReadAllReviewsAsync(projectId, cancellationToken).ConfigureAwait(false);
        var events = await _events.ReadAllAsync(projectId, cancellationToken).ConfigureAwait(false);
        var issues = reviews.Issues.Concat(events.Issues).ToArray();
        if (reviews.Status != HistoryReadStatus.Success || events.Status != HistoryReadStatus.Success || issues.Length > 0)
            return new([], Worst(reviews.Status, events.Status), issues, "Review workflow history is incomplete or unavailable; no inbox state was synthesized.");

        try
        {
            var model = BuildModel(projectId, reviews.Records, events.Records);
            return new(model.Items, HistoryReadStatus.Success);
        }
        catch (ReviewWorkflowIntegrityException exception)
        {
            return new([], HistoryReadStatus.Partial, errorMessage: exception.Message);
        }
    }

    public async Task<ReviewWorkflowCaseReadResult> ReadCaseAsync(Guid projectId, Guid rootReviewId, CancellationToken cancellationToken = default)
    {
        if (!ValidIdentity(projectId, rootReviewId))
            return new(projectId, rootReviewId, null, [], [], HistoryReadStatus.Unavailable, errorMessage: "Project and root review ids are required.");
        var reviews = await _reviews.ReadAllReviewsAsync(projectId, cancellationToken).ConfigureAwait(false);
        var events = await _events.ReadAllAsync(projectId, cancellationToken).ConfigureAwait(false);
        var issues = reviews.Issues.Concat(events.Issues).ToArray();
        if (reviews.Status != HistoryReadStatus.Success || events.Status != HistoryReadStatus.Success || issues.Length > 0)
            return new(projectId, rootReviewId, null, reviews.Records, events.Records, Worst(reviews.Status, events.Status), issues,
                "Review workflow history is incomplete or unavailable; no case state was synthesized.");
        try
        {
            var model = BuildModel(projectId, reviews.Records, events.Records);
            if (!model.Cases.TryGetValue(rootReviewId, out var item))
                return new(projectId, rootReviewId, null, [], [], HistoryReadStatus.Success, errorMessage: "The review workflow case was not found.");
            return new(projectId, rootReviewId, item.ToItem(), item.Reviews, item.Events, HistoryReadStatus.Success);
        }
        catch (ReviewWorkflowIntegrityException exception)
        {
            return new(projectId, rootReviewId, null, [], [], HistoryReadStatus.Partial, errorMessage: exception.Message);
        }
    }

    private async Task<ReviewWorkflowCaseReadResult> LoadAsync(Guid projectId, Guid rootReviewId, CancellationToken cancellationToken)
    {
        return await ReadCaseAsync(projectId, rootReviewId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResult> MutateAsync<TResult>(Func<Task<TResult>> mutation, CancellationToken cancellationToken)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await mutation().ConfigureAwait(false);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task<ReviewWorkflowMutationResult> AppendAsync(ReviewWorkflowEvent value, CancellationToken cancellationToken)
    {
        var history = await _events.ReadAllAsync(value.ProjectId, cancellationToken).ConfigureAwait(false);
        if (history.Status != HistoryReadStatus.Success || history.Issues.Count > 0)
            return new(ReviewWorkflowMutationStatus.PersistenceUnavailable, ErrorMessage: "Review workflow history is incomplete or unavailable.");

        var latest = history.Records.OrderByDescending(item => item.OccurredAt).FirstOrDefault();
        if (latest is not null && value.OccurredAt <= latest.OccurredAt)
        {
            if (latest.OccurredAt == DateTimeOffset.MaxValue)
                return new(ReviewWorkflowMutationStatus.PersistenceUnavailable, ErrorMessage: "Review workflow timestamp capacity is exhausted.");
            // A local clock can return the same instant for several lifecycle mutations. Keep
            // service-created events strictly chronological so the persisted EventId tie-breaker
            // is reserved for externally appended equal-time records.
            value = value.WithOccurredAt(latest.OccurredAt.AddTicks(1));
        }

        var result = await _events.AppendAsync(value, cancellationToken).ConfigureAwait(false);
        return result.Status switch
        {
            ReviewWorkflowStoreWriteStatus.Created => new(ReviewWorkflowMutationStatus.Created, value),
            ReviewWorkflowStoreWriteStatus.DuplicateEvent => new(ReviewWorkflowMutationStatus.Conflict, ErrorMessage: result.ErrorMessage),
            _ => new(ReviewWorkflowMutationStatus.PersistenceUnavailable, ErrorMessage: result.ErrorMessage ?? "Review workflow persistence is unavailable.")
        };
    }

    private async Task<bool> ValidateReferencesAsync(
        Guid projectId,
        ExecutionRunAuthorityReference? runReference,
        HandoffPackageReference? handoffReference,
        CancellationToken cancellationToken)
    {
        if (runReference is not null)
        {
            if (_runAuthorities is null) return false;
            var result = await _runAuthorities.GetAsync(projectId, runReference.RunId, cancellationToken).ConfigureAwait(false);
            if (!result.IsValid || result.Authority is null || result.Authority.ProjectId != projectId ||
                result.Authority.Reference.RunId != runReference.RunId ||
                !string.Equals(result.Authority.Reference.ContentHash, runReference.ContentHash, StringComparison.OrdinalIgnoreCase) ||
                result.Authority.Reference.SchemaVersion != runReference.SchemaVersion)
                return false;
        }
        if (handoffReference is not null)
        {
            if (_handoffs is null) return false;
            var result = await _handoffs.GetAsync(projectId, handoffReference.PackageId, cancellationToken).ConfigureAwait(false);
            if (!result.IsValid || result.Package is null || result.Package.ProjectId != projectId ||
                result.Package.Reference.PackageId != handoffReference.PackageId ||
                !string.Equals(result.Package.Reference.ContentHash, handoffReference.ContentHash, StringComparison.OrdinalIgnoreCase) ||
                result.Package.Reference.SchemaVersion != handoffReference.SchemaVersion)
                return false;
        }
        return true;
    }

    private static WorkflowModel BuildModel(Guid projectId, IReadOnlyList<ReviewMetadata> reviewValues, IReadOnlyList<ReviewWorkflowEvent> eventValues)
    {
        if (reviewValues.Count > ReviewWorkflowLimits.MaxReviewRecords || eventValues.Count > ReviewWorkflowLimits.MaxLifecycleEvents)
            throw new ReviewWorkflowIntegrityException("Review workflow history exceeded its supported bound.");
        var reviews = reviewValues.ToList();
        var reviewById = new Dictionary<Guid, ReviewMetadata>();
        foreach (var review in reviews)
        {
            if (review.ProjectId != projectId || !reviewById.TryAdd(review.ReviewId, review))
                throw new ReviewWorkflowIntegrityException("Review history contains a cross-project or duplicate review identity.");
            if (review.Findings.Count > 256)
                throw new ReviewWorkflowIntegrityException("A review contains more findings than the supported bound.");
        }

        var events = eventValues.ToList();
        var eventIds = new HashSet<Guid>();
        foreach (var value in events)
        {
            if (value.ProjectId != projectId || !eventIds.Add(value.EventId))
                throw new ReviewWorkflowIntegrityException("Review workflow history contains a cross-project or duplicate event identity.");
            if (!reviewById.ContainsKey(value.RootReviewId) || !reviewById.ContainsKey(value.CurrentReviewId))
                throw new ReviewWorkflowIntegrityException("Review workflow history references a missing review.");
            if (value.Kind == ReviewWorkflowEventKind.RereviewLinked &&
                (!value.LinkedReviewId.HasValue || !reviewById.ContainsKey(value.LinkedReviewId.Value)))
                throw new ReviewWorkflowIntegrityException("Review workflow history contains a re-review link to a missing review.");
        }

        ValidateWorkflowOwnershipAndCycles(events);

        var roots = new HashSet<Guid>(reviewById.Keys);
        foreach (var value in events)
        {
            if (value.Kind == ReviewWorkflowEventKind.RereviewLinked && value.LinkedReviewId.HasValue)
                roots.Remove(value.LinkedReviewId.Value);
        }

        var cases = new Dictionary<Guid, WorkflowCase>();
        foreach (var root in roots)
        {
            var caseEvents = events.Where(value => value.RootReviewId == root)
                .OrderBy(value => value.OccurredAt).ThenBy(value => value.EventId).ToArray();
            var caseReviews = reviewById.Values.Where(value => value.ReviewId == root ||
                caseEvents.Any(eventValue => eventValue.Kind == ReviewWorkflowEventKind.RereviewLinked && eventValue.LinkedReviewId == value.ReviewId))
                .OrderBy(value => value.OccurredAt).ThenBy(value => value.ReviewId).ToArray();
            var state = new WorkflowCase(root, caseReviews, caseEvents);
            foreach (var value in caseEvents) state.Apply(value, reviewById);
            cases.Add(root, state);
        }

        return new WorkflowModel(cases.Values.Select(value => value.ToItem()).OrderBy(value => value.LatestTimestamp).ThenBy(value => value.RootReviewId).ToArray(), cases);
    }

    private static bool ValidIdentity(Guid projectId, Guid rootReviewId, Guid currentReviewId) =>
        projectId != Guid.Empty && rootReviewId != Guid.Empty && currentReviewId != Guid.Empty;

    private static bool ValidIdentity(Guid projectId, Guid rootReviewId) => projectId != Guid.Empty && rootReviewId != Guid.Empty;

    private static void ValidateWorkflowOwnershipAndCycles(IReadOnlyList<ReviewWorkflowEvent> events)
    {
        var owners = new Dictionary<Guid, Guid>();
        var links = new Dictionary<Guid, Guid>();
        var linkedOwners = new Dictionary<Guid, Guid>();

        foreach (var value in events)
        {
            ClaimOwner(value.RootReviewId, value.RootReviewId, owners);
            ClaimOwner(value.CurrentReviewId, value.RootReviewId, owners);

            if (value.Kind != ReviewWorkflowEventKind.RereviewLinked || !value.LinkedReviewId.HasValue)
                continue;

            ClaimOwner(value.LinkedReviewId.Value, value.RootReviewId, owners);
            if (!linkedOwners.TryAdd(value.LinkedReviewId.Value, value.RootReviewId))
                throw new ReviewWorkflowIntegrityException("Review workflow history contains duplicate re-review ownership.");
            if (!links.TryAdd(value.CurrentReviewId, value.LinkedReviewId.Value))
                throw new ReviewWorkflowIntegrityException("Review workflow history contains duplicate re-review transitions.");
        }

        var visited = new HashSet<Guid>();
        var visiting = new HashSet<Guid>();
        foreach (var reviewId in links.Keys)
        {
            if (HasCycle(reviewId, links, visited, visiting))
                throw new ReviewWorkflowIntegrityException("Review workflow history contains a re-review cycle.");
        }
    }

    private static void ClaimOwner(Guid reviewId, Guid rootReviewId, IDictionary<Guid, Guid> owners)
    {
        if (owners.TryGetValue(reviewId, out var existingRoot) && existingRoot != rootReviewId)
            throw new ReviewWorkflowIntegrityException("Review workflow history contains conflicting lifecycle ownership.");
        owners[reviewId] = rootReviewId;
    }

    private static bool HasCycle(
        Guid reviewId,
        IReadOnlyDictionary<Guid, Guid> links,
        ISet<Guid> visited,
        ISet<Guid> visiting)
    {
        if (visited.Contains(reviewId)) return false;
        if (!visiting.Add(reviewId)) return true;
        if (links.TryGetValue(reviewId, out var next) && HasCycle(next, links, visited, visiting)) return true;
        visiting.Remove(reviewId);
        visited.Add(reviewId);
        return false;
    }

    private static bool Same(ExecutionRunAuthorityReference left, ExecutionRunAuthorityReference right) =>
        left.RunId == right.RunId &&
        left.SchemaVersion == right.SchemaVersion &&
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static bool Bounded(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maximumLength && !value.Any(char.IsControl);

    private bool ValidEvidenceReferences(IReadOnlyList<string>? values) =>
        values is null || (values.Count <= ReviewWorkflowLimits.MaxEvidenceReferences &&
                           values.All(value => Bounded(value, ReviewWorkflowLimits.MaxReferenceLength) && SafeIdentity(value)));

    private bool SafeIdentity(string value) => !_redaction.ValidateIdentityText(value).RequiresRedaction;

    private string RedactReason(string value) => _redaction.Redact(value).Value;

    private static ReviewWorkflowMutationResult Invalid(string message) => new(ReviewWorkflowMutationStatus.InvalidRequest, ErrorMessage: message);

    private static ReviewWorkflowMutationResult FromReadFailure(ReviewWorkflowCaseReadResult result) =>
        result.Status == HistoryReadStatus.Success
            ? new(ReviewWorkflowMutationStatus.NotFound, ErrorMessage: result.ErrorMessage ?? "The review workflow case was not found.")
            : new(ReviewWorkflowMutationStatus.PersistenceUnavailable, ErrorMessage: result.ErrorMessage ?? "Review workflow history is unavailable.");

    private static HistoryReadStatus Worst(HistoryReadStatus left, HistoryReadStatus right) =>
        left == HistoryReadStatus.Unavailable || right == HistoryReadStatus.Unavailable ? HistoryReadStatus.Unavailable :
        left == HistoryReadStatus.Partial || right == HistoryReadStatus.Partial ? HistoryReadStatus.Partial : HistoryReadStatus.Success;

    private sealed class WorkflowModel
    {
        public WorkflowModel(IReadOnlyList<ReviewInboxItem> items, IReadOnlyDictionary<Guid, WorkflowCase> cases) => (Items, Cases) = (items, cases);
        public IReadOnlyList<ReviewInboxItem> Items { get; }
        public IReadOnlyDictionary<Guid, WorkflowCase> Cases { get; }
    }

    private sealed class WorkflowCase
    {
        private readonly List<ReviewMetadata> _reviews;
        private readonly IReadOnlyList<ReviewWorkflowEvent> _events;
        private readonly Dictionary<(Guid ReviewId, string FindingId), ReviewFindingAdjudication> _adjudications = new();
        private readonly HashSet<int> _startedAttempts = [];
        private readonly HashSet<int> _completedAttempts = [];
        private readonly HashSet<int> _revalidatedAttempts = [];
        private Guid _currentReviewId;
        private ReviewWorkflowState _state;
        private int? _activeAttempt;
        private ReviewWorkflowEvent? _lastRemediation;
        private ReviewWorkflowEvent? _latestValidation;
        private bool _ownerAttention;
        private string? _ownerReason;

        public WorkflowCase(Guid rootReviewId, IReadOnlyList<ReviewMetadata> reviews, IReadOnlyList<ReviewWorkflowEvent> events)
        {
            if (reviews.Count == 0 || reviews[0].ReviewId != rootReviewId)
                throw new ReviewWorkflowIntegrityException("A workflow root review is missing.");
            _reviews = reviews.ToList();
            _events = events;
            _currentReviewId = rootReviewId;
            _state = BaseState(reviews[0]);
        }

        public IReadOnlyList<ReviewMetadata> Reviews => _reviews;
        public IReadOnlyList<ReviewWorkflowEvent> Events => _events;

        public void Apply(ReviewWorkflowEvent value, IReadOnlyDictionary<Guid, ReviewMetadata> reviewById)
        {
            if (_state == ReviewWorkflowState.HumanDecisionRequired)
                throw new ReviewWorkflowIntegrityException("A human-decision workflow is terminal until an owner decision boundary is added.");
            if (value.CurrentReviewId != _currentReviewId && value.Kind != ReviewWorkflowEventKind.RereviewLinked)
                throw new ReviewWorkflowIntegrityException("A workflow event is not bound to the current review.");
            if (value.OccurredAt < CurrentReview.OccurredAt)
                throw new ReviewWorkflowIntegrityException("A workflow event predates its bound review.");

            switch (value.Kind)
            {
                case ReviewWorkflowEventKind.FindingAdjudicated:
                    if (_state is ReviewWorkflowState.RevalidationRequired or ReviewWorkflowState.RereviewRequired)
                        throw new ReviewWorkflowIntegrityException("Finding adjudication occurred after the review decision window closed.");
                    ApplyAdjudication(value);
                    break;
                case ReviewWorkflowEventKind.RemediationStarted:
                    if (_state != ReviewWorkflowState.RemediationRequired || _activeAttempt.HasValue || !_startedAttempts.Add(value.AttemptNumber!.Value) || value.AttemptNumber.Value != _startedAttempts.Count)
                        throw new ReviewWorkflowIntegrityException("Remediation start is not a legal bounded transition.");
                    _activeAttempt = value.AttemptNumber;
                    break;
                case ReviewWorkflowEventKind.RemediationCompleted:
                    if (_state != ReviewWorkflowState.RemediationRequired || _activeAttempt != value.AttemptNumber || !_startedAttempts.Contains(value.AttemptNumber!.Value))
                        throw new ReviewWorkflowIntegrityException("Remediation completion is not bound to the active attempt.");
                    _activeAttempt = null;
                    _completedAttempts.Add(value.AttemptNumber.Value);
                    _lastRemediation = value;
                    _state = ReviewWorkflowState.RevalidationRequired;
                    break;
                case ReviewWorkflowEventKind.RevalidationRecorded:
                    if (_state != ReviewWorkflowState.RevalidationRequired || _activeAttempt.HasValue || !_completedAttempts.Contains(value.AttemptNumber!.Value) || !_revalidatedAttempts.Add(value.AttemptNumber.Value))
                        throw new ReviewWorkflowIntegrityException("Revalidation is not a legal transition for this attempt.");
                    _latestValidation = value;
                    _state = value.ValidationState == ValidationGateDecisionState.Satisfied
                        ? ReviewWorkflowState.RereviewRequired
                        : value.AttemptNumber.Value < ReviewWorkflowLimits.MaxRemediationAttempts
                            ? ReviewWorkflowState.RemediationRequired
                            : Human("Validation did not satisfy the APO-48 gate after the final attempt.");
                    break;
                case ReviewWorkflowEventKind.RereviewLinked:
                    if (value.CurrentReviewId != _currentReviewId || _state != ReviewWorkflowState.RereviewRequired || !value.LinkedReviewId.HasValue || !reviewById.TryGetValue(value.LinkedReviewId.Value, out var rereview) || rereview.ProjectId != CurrentReview.ProjectId || rereview.ReviewId == _currentReviewId)
                        throw new ReviewWorkflowIntegrityException("Re-review link is not a legal project-isolated transition.");
                    if (value.OccurredAt < rereview.OccurredAt)
                        throw new ReviewWorkflowIntegrityException("A re-review link cannot predate the re-review record.");
                    if (_latestValidation is null || _latestValidation.ValidationState != ValidationGateDecisionState.Satisfied ||
                        rereview.OccurredAt < _latestValidation.OccurredAt)
                        throw new ReviewWorkflowIntegrityException("A re-review link must use a fresh review created after successful revalidation.");
                    _currentReviewId = rereview.ReviewId;
                    _state = BaseState(rereview);
                    _activeAttempt = null;
                    _ownerAttention = false;
                    _ownerReason = null;
                    break;
                case ReviewWorkflowEventKind.HumanDecisionRequired:
                    _state = Human(value.Reason!);
                    break;
            }
        }

        public ReviewInboxItem ToItem()
        {
            var review = CurrentReview;
            var adjudications = _adjudications.Where(value => value.Key.ReviewId == _currentReviewId).ToArray();
            var byFinding = adjudications.ToDictionary(value => value.Key.FindingId, value => value.Value, StringComparer.OrdinalIgnoreCase);
            var blocking = review.Findings.Count(value => value.Blocking);
            var pending = review.Findings.Count(value => value.Blocking && !byFinding.ContainsKey(value.FindingId));
            var latest = _events.Count == 0 ? review.OccurredAt : _events.Max(value => value.OccurredAt);
            return new ReviewInboxItem
            {
                ProjectId = review.ProjectId,
                RootReviewId = _events.Count == 0 ? review.ReviewId : _events[0].RootReviewId,
                CurrentReviewId = review.ReviewId,
                LatestTimestamp = latest,
                ReviewerReference = review.ReviewerReference,
                CurrentVerdict = review.Verdict,
                CurrentSeverity = review.Severity,
                WorkflowState = _state,
                TotalCurrentFindings = review.Findings.Count,
                BlockingFindingCount = blocking,
                PendingAdjudicationCount = pending,
                AcceptedCount = adjudications.Count(value => value.Value == ReviewFindingAdjudication.Accepted),
                RejectedCount = adjudications.Count(value => value.Value == ReviewFindingAdjudication.Rejected),
                DeferredCount = adjudications.Count(value => value.Value == ReviewFindingAdjudication.Deferred),
                RemediationAttemptCount = _startedAttempts.Count,
                LastRemediationReference = RemediationReference(_lastRemediation),
                LatestValidationReference = _latestValidation?.ValidationDecisionReference,
                LatestValidationState = _latestValidation?.ValidationState,
                OwnerAttentionRequired = _ownerAttention,
                OwnerAttentionReason = _ownerReason,
                NextRequiredAction = NextAction(),
                ActiveRemediationAttempt = _activeAttempt
            };
        }

        private ReviewMetadata CurrentReview => _reviews.Single(value => value.ReviewId == _currentReviewId);

        private void ApplyAdjudication(ReviewWorkflowEvent value)
        {
            var finding = CurrentReview.Findings.SingleOrDefault(item => string.Equals(item.FindingId, value.FindingId, StringComparison.OrdinalIgnoreCase));
            if (finding is null || !_adjudications.TryAdd((_currentReviewId, finding.FindingId), value.Disposition!.Value))
                throw new ReviewWorkflowIntegrityException("A finding adjudication is missing its exact finding or is duplicated.");
            if (value.AuthorityKind == ReviewAuthorityKind.AcceptanceAuthority &&
                string.Equals(value.AuthorityReference, CurrentReview.ReviewerReference, StringComparison.OrdinalIgnoreCase))
                throw new ReviewWorkflowIntegrityException("A reviewer cannot adjudicate its own finding as product acceptance.");
            if (value.Disposition == ReviewFindingAdjudication.Deferred && finding.Blocking)
                _state = Human("A blocking finding was deferred.");
            else
                _state = CalculateState();
        }

        private ReviewWorkflowState CalculateState()
        {
            var review = CurrentReview;
            var current = _adjudications.Where(value => value.Key.ReviewId == _currentReviewId)
                .ToDictionary(value => value.Key.FindingId, value => value.Value, StringComparer.OrdinalIgnoreCase);
            if (review.Findings.Any(value => value.Blocking && current.TryGetValue(value.FindingId, out var disposition) && disposition == ReviewFindingAdjudication.Deferred))
                return Human("A blocking finding was deferred.");
            if (review.Findings.Any(value => value.Blocking && !current.ContainsKey(value.FindingId)))
                return ReviewWorkflowState.AwaitingAdjudication;
            if (review.Findings.Any(value => value.Blocking && current[value.FindingId] == ReviewFindingAdjudication.Accepted))
                return ReviewWorkflowState.RemediationRequired;
            return ReviewWorkflowState.ReadyForAcceptanceAuthority;
        }

        private ReviewWorkflowNextAction NextAction() => _state switch
        {
            ReviewWorkflowState.AwaitingAdjudication => ReviewWorkflowNextAction.AdjudicateFindings,
            ReviewWorkflowState.RemediationRequired => ReviewWorkflowNextAction.RunRemediation,
            ReviewWorkflowState.RevalidationRequired => ReviewWorkflowNextAction.RunRevalidation,
            ReviewWorkflowState.RereviewRequired => ReviewWorkflowNextAction.RunRereview,
            ReviewWorkflowState.HumanDecisionRequired => ReviewWorkflowNextAction.HumanDecision,
            _ => ReviewWorkflowNextAction.SendToAcceptanceAuthority
        };

        private ReviewWorkflowState Human(string reason)
        {
            if (reason.Length > ReviewWorkflowLimits.MaxOwnerAttentionReasonLength)
                throw new ReviewWorkflowIntegrityException("Owner-attention reason exceeds its supported bound.");
            _ownerAttention = true;
            _ownerReason = reason;
            return ReviewWorkflowState.HumanDecisionRequired;
        }

        private static ReviewWorkflowState BaseState(ReviewMetadata review) =>
            review.Findings.Any(value => value.Blocking)
                ? ReviewWorkflowState.AwaitingAdjudication
                : ReviewWorkflowState.ReadyForAcceptanceAuthority;

        private static string? RemediationReference(ReviewWorkflowEvent? value) =>
            value?.ExecutionRunAuthorityReference?.ToString() ?? value?.HandoffPackageReference?.ToString() ?? value?.EvidenceReferences.FirstOrDefault();
    }

    private sealed class ReviewWorkflowIntegrityException(string message) : Exception(message);
}
