using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Planning;

namespace AIUsageMonitor.Application.Approvals;

public static class HumanApprovalSchema
{
    public const int CurrentVersion = 1;
}

public static class HumanApprovalLimits
{
    public const int MaxEventsPerProject = 512;
    public const int MaxEvidenceReferences = 64;
    public const int MaxTextLength = 2_000;
    public const int MaxSummaryLength = 500;
    public const int MaxPolicyIdentityLength = 200;
}

public enum HumanApprovalActionKind
{
    ProtectedBranchMerge,
    DestructiveOperation,
    CredentialChange,
    BillingChange,
    MaterialArchitectureChange,
    MaterialBusinessRequirementChange,
    ProductionDeployment,
    OwnerDefinedGatedAction
}

public enum HumanApprovalEventKind
{
    Requested,
    Escalated,
    Approved,
    Rejected,
    Waived
}

public enum HumanApprovalState
{
    Pending,
    Escalated,
    Approved,
    Rejected,
    Waived,
    Expired,
    Stale
}

public enum HumanApprovalActorKind
{
    Requester,
    Automation,
    HumanOwner
}

public enum HumanOwnerAuthorityKind
{
    LocalSingleOwner
}

public enum HumanApprovalReasonCode
{
    ExactApproved,
    ExactWaived,
    Pending,
    Escalated,
    Rejected,
    Expired,
    StaleTarget,
    StaleEvidence,
    StaleContract,
    ProjectMismatch,
    RequestNotFound,
    InvalidHistory,
    UnauthorizedOwner,
    NotRequired
}

public enum HumanApprovalNextAction
{
    RequestApproval,
    AwaitOwnerDecision,
    CreateFreshApprovalRequest,
    ResolveRejection,
    ProceedWithAuthorizedAction
}

/// <summary>
/// Replaceable V1 authority marker for the one local human owner. It is not a password, token,
/// cookie, or authentication payload and is never persisted as an authority secret.
/// </summary>
public sealed class HumanOwnerAuthority
{
    public HumanOwnerAuthority(
        string ownerReference,
        string authorityReference,
        HumanOwnerAuthorityKind kind = HumanOwnerAuthorityKind.LocalSingleOwner)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentException("Owner authority kind is undefined.", nameof(kind));

        OwnerReference = Required(ownerReference, nameof(ownerReference), HumanApprovalLimits.MaxTextLength);
        AuthorityReference = Required(authorityReference, nameof(authorityReference), HumanApprovalLimits.MaxTextLength);
        Kind = kind;
    }

    public string OwnerReference { get; }

    public string AuthorityReference { get; }

    public HumanOwnerAuthorityKind Kind { get; }

    private static string Required(string value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A bounded authority reference is required.", parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
        if (normalized.Any(static character => char.IsControl(character)))
            throw new ArgumentException("Authority references cannot contain control characters.", parameterName);
        return normalized;
    }
}

public interface IHumanOwnerAuthority
{
    bool IsAuthorized(HumanOwnerAuthority authority);
}

/// <summary>Minimum replaceable single-owner authority implementation for local V1 use.</summary>
public sealed class LocalSingleOwnerAuthority : IHumanOwnerAuthority
{
    public LocalSingleOwnerAuthority(string ownerReference, string authorityReference = "local-owner")
    {
        ExpectedOwnerReference = Required(ownerReference, nameof(ownerReference));
        ExpectedAuthorityReference = Required(authorityReference, nameof(authorityReference));
    }

    public string ExpectedOwnerReference { get; }

    public string ExpectedAuthorityReference { get; }

    public bool IsAuthorized(HumanOwnerAuthority authority) =>
        authority is not null &&
        authority.Kind == HumanOwnerAuthorityKind.LocalSingleOwner &&
        string.Equals(authority.OwnerReference, ExpectedOwnerReference, StringComparison.Ordinal) &&
        string.Equals(authority.AuthorityReference, ExpectedAuthorityReference, StringComparison.Ordinal);

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("The configured owner reference is required.", parameterName);
        var normalized = value.Trim();
        if (normalized.Any(static character => char.IsControl(character)))
            throw new ArgumentException("The configured owner reference cannot contain control characters.", parameterName);
        return normalized;
    }
}

public sealed class HumanApprovalEvidenceReference
{
    public HumanApprovalEvidenceReference(
        string kind,
        string reference,
        Guid? evidenceId = null,
        int? schemaVersion = null,
        string? contentHash = null)
    {
        Kind = Required(kind, nameof(kind), 120);
        Reference = Required(reference, nameof(reference), HumanApprovalLimits.MaxTextLength);
        if (evidenceId == Guid.Empty)
            throw new ArgumentException("Evidence id cannot be empty when supplied.", nameof(evidenceId));
        if (schemaVersion is <= 0)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        if (contentHash is not null && !IsSha256(contentHash))
            throw new ArgumentException("Evidence content hash must be SHA-256.", nameof(contentHash));
        EvidenceId = evidenceId;
        SchemaVersion = schemaVersion;
        ContentHash = contentHash?.ToLowerInvariant();
    }

    public Guid? EvidenceId { get; }

    public string Kind { get; }

    public string Reference { get; }

    public int? SchemaVersion { get; }

    public string? ContentHash { get; }

    public static HumanApprovalEvidenceReference FromValidationDecision(
        AIUsageMonitor.Application.Validation.ValidationGateDecisionReference reference) =>
        new("validation-decision", reference.ToString(), reference.DecisionId, reference.SchemaVersion, reference.ContentHash);

    public static HumanApprovalEvidenceReference FromReviewIdentity(
        string reviewIdentity,
        Guid? reviewId = null) =>
        new("review-workflow", reviewIdentity, reviewId);

    internal string CanonicalKey =>
        $"{Kind}\u001f{Reference}\u001f{EvidenceId?.ToString("D") ?? string.Empty}\u001f{SchemaVersion?.ToString() ?? string.Empty}\u001f{ContentHash ?? string.Empty}";

    private static string Required(string value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A bounded evidence reference is required.", parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
        if (normalized.Any(static character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
            throw new ArgumentException("Evidence references cannot contain unsupported control characters.", parameterName);
        return normalized;
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(static character => Uri.IsHexDigit(character));
}

public sealed class HumanApprovalEvidenceRevision
{
    public HumanApprovalEvidenceRevision(
        IReadOnlyList<HumanApprovalEvidenceReference> references,
        string? contentHash = null,
        int schemaVersion = HumanApprovalSchema.CurrentVersion)
    {
        if (schemaVersion != HumanApprovalSchema.CurrentVersion)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), "Unsupported human approval evidence revision schema.");
        ArgumentNullException.ThrowIfNull(references);
        if (references.Count == 0)
            throw new ArgumentException("At least one immutable evidence reference is required.", nameof(references));
        if (references.Count > HumanApprovalLimits.MaxEvidenceReferences)
            throw new ArgumentException("The evidence revision exceeds its bounded reference capacity.", nameof(references));
        if (references.Any(static value => value is null))
            throw new ArgumentException("Evidence references cannot contain null entries.", nameof(references));

        References = references
            .OrderBy(static value => value.CanonicalKey, StringComparer.Ordinal)
            .ToArray();
        if (References.Select(static value => value.CanonicalKey).Distinct(StringComparer.Ordinal).Count() != References.Count)
            throw new ArgumentException("Evidence references must be unique.", nameof(references));

        SchemaVersion = schemaVersion;
        ContentHash = ComputeContentHash(this);
        if (contentHash is not null && !string.Equals(contentHash, ContentHash, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The supplied evidence revision hash does not match its references.", nameof(contentHash));
    }

    public int SchemaVersion { get; }

    public IReadOnlyList<HumanApprovalEvidenceReference> References { get; }

    public string ContentHash { get; }

    private static string ComputeContentHash(HumanApprovalEvidenceRevision value)
    {
        var payload = value.References.Select(static reference => new
        {
            reference.EvidenceId,
            reference.Kind,
            reference.Reference,
            reference.SchemaVersion,
            reference.ContentHash
        }).ToArray();
        return HumanApprovalIntegrity.Hash(payload);
    }
}

/// <summary>Exact typed target binding. Repository merge targets require all immutable refs.</summary>
public sealed class HumanApprovalTarget
{
    public HumanApprovalTarget(
        HumanApprovalActionKind actionKind,
        string safeSummary,
        string? canonicalRepositoryIdentity = null,
        string? baseRef = null,
        string? baseSha = null,
        string? headRef = null,
        string? headSha = null,
        string? operationFingerprint = null,
        string? contentHash = null)
    {
        if (!Enum.IsDefined(actionKind))
            throw new ArgumentException("Approval action kind is undefined.", nameof(actionKind));

        ActionKind = actionKind;
        SafeSummary = Required(safeSummary, nameof(safeSummary), HumanApprovalLimits.MaxSummaryLength);
        CanonicalRepositoryIdentity = Optional(canonicalRepositoryIdentity, nameof(canonicalRepositoryIdentity), HumanApprovalLimits.MaxTextLength);
        BaseRef = Optional(baseRef, nameof(baseRef), 300);
        BaseSha = Optional(baseSha, nameof(baseSha), 64);
        HeadRef = Optional(headRef, nameof(headRef), 300);
        HeadSha = Optional(headSha, nameof(headSha), 64);
        OperationFingerprint = Optional(operationFingerprint, nameof(operationFingerprint), 64)?.ToLowerInvariant();

        if (actionKind == HumanApprovalActionKind.ProtectedBranchMerge)
        {
            if (CanonicalRepositoryIdentity is null || BaseRef is null || BaseSha is null || HeadRef is null || HeadSha is null)
                throw new ArgumentException("Protected branch merge targets require repository, base, and head identity.", nameof(actionKind));
            if (!IsGitObjectId(BaseSha) || !IsGitObjectId(HeadSha))
                throw new ArgumentException("Protected branch merge base and head must be full Git object ids.", nameof(baseSha));
            if (OperationFingerprint is not null)
                throw new ArgumentException("Repository merge targets use their exact refs rather than an opaque fingerprint.", nameof(operationFingerprint));
        }
        else
        {
            if (OperationFingerprint is null || !IsSha256(OperationFingerprint))
                throw new ArgumentException("Non-repository approval targets require a SHA-256 operation fingerprint.", nameof(operationFingerprint));
            if (CanonicalRepositoryIdentity is not null || BaseRef is not null || BaseSha is not null || HeadRef is not null || HeadSha is not null)
                throw new ArgumentException("Non-repository targets cannot carry repository merge identity.", nameof(actionKind));
        }

        ContentHash = ComputeContentHash(this);
        if (contentHash is not null && !string.Equals(contentHash, ContentHash, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The supplied target hash does not match the target payload.", nameof(contentHash));
    }

    public HumanApprovalActionKind ActionKind { get; }

    public string SafeSummary { get; }

    public string? CanonicalRepositoryIdentity { get; }

    public string? BaseRef { get; }

    public string? BaseSha { get; }

    public string? HeadRef { get; }

    public string? HeadSha { get; }

    public string? OperationFingerprint { get; }

    public string ContentHash { get; }

    public static HumanApprovalTarget ProtectedBranchMerge(
        string repositoryIdentity,
        string baseRef,
        string baseSha,
        string headRef,
        string headSha,
        string safeSummary) =>
        new(HumanApprovalActionKind.ProtectedBranchMerge, safeSummary, repositoryIdentity, baseRef, baseSha, headRef, headSha);

    public static HumanApprovalTarget Fingerprinted(
        HumanApprovalActionKind actionKind,
        string operationFingerprint,
        string safeSummary) =>
        new(actionKind, safeSummary, operationFingerprint: operationFingerprint);

    private static string ComputeContentHash(HumanApprovalTarget value) => HumanApprovalIntegrity.Hash(new
    {
        value.ActionKind,
        value.SafeSummary,
        value.CanonicalRepositoryIdentity,
        value.BaseRef,
        value.BaseSha,
        value.HeadRef,
        value.HeadSha,
        value.OperationFingerprint
    });

    private static string Required(string value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A bounded target value is required.", parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
        if (normalized.Any(static character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
            throw new ArgumentException("Target values cannot contain unsupported control characters.", parameterName);
        return normalized;
    }

    private static string? Optional(string? value, string parameterName, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, parameterName, maximumLength);

    private static bool IsSha256(string value) => value.Length == 64 && value.All(static character => Uri.IsHexDigit(character));

    private static bool IsGitObjectId(string value) => (value.Length is 40 or 64) && value.All(static character => Uri.IsHexDigit(character));
}

public sealed class HumanApprovalRequest
{
    public HumanApprovalRequest(
        Guid projectId,
        Guid requestId,
        HumanApprovalActionKind actionKind,
        PlanningExecutionContractReference contractReference,
        HumanApprovalTarget target,
        HumanApprovalEvidenceRevision evidenceRevision,
        string requesterReference,
        DateTimeOffset requestedAt,
        DateTimeOffset expiresAt,
        string reason,
        string policyReference,
        string? contentHash = null)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("Project id cannot be empty.", nameof(projectId));
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request id cannot be empty.", nameof(requestId));
        if (!Enum.IsDefined(actionKind))
            throw new ArgumentException("Approval action kind is undefined.", nameof(actionKind));
        ArgumentNullException.ThrowIfNull(contractReference);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(evidenceRevision);
        if (target.ActionKind != actionKind)
            throw new ArgumentException("The request action and target action must match.", nameof(target));
        if (requestedAt == default)
            throw new ArgumentException("RequestedAt is required.", nameof(requestedAt));
        if (expiresAt <= requestedAt)
            throw new ArgumentException("ExpiresAt must be later than RequestedAt.", nameof(expiresAt));

        ProjectId = projectId;
        RequestId = requestId;
        ActionKind = actionKind;
        ContractReference = contractReference;
        Target = target;
        EvidenceRevision = evidenceRevision;
        RequesterReference = Required(requesterReference, nameof(requesterReference));
        RequestedAt = requestedAt;
        ExpiresAt = expiresAt;
        Reason = Required(reason, nameof(reason));
        PolicyReference = Required(policyReference, nameof(policyReference), HumanApprovalLimits.MaxPolicyIdentityLength);
        ContentHash = HumanApprovalIntegrity.ComputeRequestContentHash(this);
        if (contentHash is not null && !string.Equals(contentHash, ContentHash, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The supplied request hash does not match the request payload.", nameof(contentHash));
        Reference = new HumanApprovalReference(RequestId, HumanApprovalSchema.CurrentVersion, ContentHash);
    }

    public Guid ProjectId { get; }
    public Guid RequestId { get; }
    public HumanApprovalActionKind ActionKind { get; }
    public PlanningExecutionContractReference ContractReference { get; }
    public HumanApprovalTarget Target { get; }
    public HumanApprovalEvidenceRevision EvidenceRevision { get; }
    public string RequesterReference { get; }
    public DateTimeOffset RequestedAt { get; }
    public DateTimeOffset ExpiresAt { get; }
    public string Reason { get; }
    public string PolicyReference { get; }
    public string ContentHash { get; }
    public HumanApprovalReference Reference { get; }

    private static string Required(string value, string parameterName, int maximumLength = HumanApprovalLimits.MaxTextLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A bounded approval value is required.", parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
        if (normalized.Any(static character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
            throw new ArgumentException("Approval values cannot contain unsupported control characters.", parameterName);
        return normalized;
    }
}

public sealed class HumanApprovalReference
{
    public HumanApprovalReference(Guid requestId, int schemaVersion, string contentHash, Guid? eventId = null, HumanApprovalEventKind? eventKind = null)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request id cannot be empty.", nameof(requestId));
        if (eventId == Guid.Empty)
            throw new ArgumentException("Event id cannot be empty when supplied.", nameof(eventId));
        if (schemaVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        if (!IsSha256(contentHash))
            throw new ArgumentException("Approval content hash must be SHA-256.", nameof(contentHash));
        if (eventKind is not null && !Enum.IsDefined(eventKind.Value))
            throw new ArgumentException("Approval event kind is undefined.", nameof(eventKind));
        RequestId = requestId;
        EventId = eventId;
        SchemaVersion = schemaVersion;
        EventKind = eventKind;
        ContentHash = contentHash.ToLowerInvariant();
    }

    public Guid RequestId { get; }
    public Guid? EventId { get; }
    public int SchemaVersion { get; }
    public HumanApprovalEventKind? EventKind { get; }
    public string ContentHash { get; }

    private static bool IsSha256(string value) => value.Length == 64 && value.All(static character => Uri.IsHexDigit(character));
}

public sealed class HumanApprovalEvent
{
    public HumanApprovalEvent(
        Guid eventId,
        Guid projectId,
        Guid requestId,
        HumanApprovalEventKind kind,
        DateTimeOffset occurredAt,
        HumanApprovalActorKind actorKind,
        string actorReference,
        string? reason = null,
        HumanApprovalRequest? request = null,
        string? contentHash = null)
    {
        if (eventId == Guid.Empty)
            throw new ArgumentException("Event id cannot be empty.", nameof(eventId));
        if (projectId == Guid.Empty)
            throw new ArgumentException("Project id cannot be empty.", nameof(projectId));
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request id cannot be empty.", nameof(requestId));
        if (!Enum.IsDefined(kind))
            throw new ArgumentException("Approval event kind is undefined.", nameof(kind));
        if (!Enum.IsDefined(actorKind))
            throw new ArgumentException("Approval actor kind is undefined.", nameof(actorKind));
        if (occurredAt == default)
            throw new ArgumentException("Event timestamp is required.", nameof(occurredAt));

        EventId = eventId;
        ProjectId = projectId;
        RequestId = requestId;
        Kind = kind;
        OccurredAt = occurredAt;
        ActorKind = actorKind;
        ActorReference = Required(actorReference, nameof(actorReference));
        Reason = Optional(reason, nameof(reason));
        Request = request;

        if (kind == HumanApprovalEventKind.Requested)
        {
            if (request is null || request.ProjectId != projectId || request.RequestId != requestId || request.RequestedAt != occurredAt)
                throw new ArgumentException("A Requested event must carry the exact immutable request.", nameof(request));
            if (actorKind != HumanApprovalActorKind.Requester || !string.Equals(ActorReference, request.RequesterReference, StringComparison.Ordinal))
                throw new ArgumentException("A Requested event must be issued by the request reference.", nameof(actorKind));
            if (Reason is not null)
                throw new ArgumentException("Requested events do not carry a separate decision reason.", nameof(reason));
        }
        else if (request is not null)
        {
            throw new ArgumentException("Only a Requested event may carry the immutable request.", nameof(request));
        }

        if (kind is HumanApprovalEventKind.Approved or HumanApprovalEventKind.Rejected or HumanApprovalEventKind.Waived)
        {
            if (actorKind != HumanApprovalActorKind.HumanOwner || Reason is null)
                throw new ArgumentException("Terminal decisions require a human owner and a reason.", nameof(actorKind));
        }

        ContentHash = HumanApprovalIntegrity.ComputeEventContentHash(this);
        if (contentHash is not null && !string.Equals(contentHash, ContentHash, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The supplied event hash does not match the event payload.", nameof(contentHash));
        Reference = new HumanApprovalReference(RequestId, HumanApprovalSchema.CurrentVersion, ContentHash, EventId, Kind);
    }

    public Guid EventId { get; }
    public Guid ProjectId { get; }
    public Guid RequestId { get; }
    public HumanApprovalEventKind Kind { get; }
    public DateTimeOffset OccurredAt { get; }
    public HumanApprovalActorKind ActorKind { get; }
    public string ActorReference { get; }
    public string? Reason { get; }
    public HumanApprovalRequest? Request { get; }
    public string ContentHash { get; }
    public HumanApprovalReference Reference { get; }

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A bounded actor reference is required.", parameterName);
        var normalized = value.Trim();
        if (normalized.Length > HumanApprovalLimits.MaxTextLength)
            throw new ArgumentException($"The value cannot exceed {HumanApprovalLimits.MaxTextLength} characters.", parameterName);
        if (normalized.Any(static character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
            throw new ArgumentException("Actor references cannot contain unsupported control characters.", parameterName);
        return normalized;
    }

    private static string? Optional(string? value, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, parameterName);
}

public sealed class HumanApprovalEvaluationContext
{
    public HumanApprovalEvaluationContext(
        Guid projectId,
        PlanningExecutionContractReference contractReference,
        HumanApprovalTarget target,
        HumanApprovalEvidenceRevision evidenceRevision)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("Project id cannot be empty.", nameof(projectId));
        ContractReference = contractReference ?? throw new ArgumentNullException(nameof(contractReference));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        EvidenceRevision = evidenceRevision ?? throw new ArgumentNullException(nameof(evidenceRevision));
        ProjectId = projectId;
    }

    public Guid ProjectId { get; }
    public PlanningExecutionContractReference ContractReference { get; }
    public HumanApprovalTarget Target { get; }
    public HumanApprovalEvidenceRevision EvidenceRevision { get; }
}

public sealed class HumanApprovalDecisionRequest
{
    public HumanApprovalDecisionRequest(
        Guid projectId,
        Guid requestId,
        HumanOwnerAuthority ownerAuthority,
        string reason,
        HumanApprovalEvaluationContext currentContext)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("Project id cannot be empty.", nameof(projectId));
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request id cannot be empty.", nameof(requestId));
        OwnerAuthority = ownerAuthority ?? throw new ArgumentNullException(nameof(ownerAuthority));
        CurrentContext = currentContext ?? throw new ArgumentNullException(nameof(currentContext));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required for a terminal decision.", nameof(reason));
        Reason = reason.Trim();
        ProjectId = projectId;
        RequestId = requestId;
    }

    public Guid ProjectId { get; }
    public Guid RequestId { get; }
    public HumanOwnerAuthority OwnerAuthority { get; }
    public string Reason { get; }
    public HumanApprovalEvaluationContext CurrentContext { get; }
}

public sealed class HumanApprovalEvaluation
{
    public HumanApprovalEvaluation(
        Guid projectId,
        Guid requestId,
        HumanApprovalState effectiveState,
        bool canProceed,
        HumanApprovalReasonCode reasonCode,
        HumanApprovalNextAction nextAction,
        bool ownerAttentionRequired,
        HumanApprovalRequest? request = null,
        HumanApprovalReference? satisfyingReference = null,
        string? reason = null)
    {
        ProjectId = projectId;
        RequestId = requestId;
        EffectiveState = effectiveState;
        CanProceed = canProceed;
        ReasonCode = reasonCode;
        NextAction = nextAction;
        OwnerAttentionRequired = ownerAttentionRequired;
        Request = request;
        SatisfyingReference = satisfyingReference;
        Reason = reason ?? reasonCode.ToString();
    }

    public Guid ProjectId { get; }
    public Guid RequestId { get; }
    public HumanApprovalState EffectiveState { get; }
    public bool CanProceed { get; }
    public HumanApprovalReasonCode ReasonCode { get; }
    public HumanApprovalNextAction NextAction { get; }
    public bool OwnerAttentionRequired { get; }
    public HumanApprovalRequest? Request { get; }
    public HumanApprovalReference? SatisfyingReference { get; }
    public string Reason { get; }
}

public sealed record HumanApprovalOperationResult(
    HumanApprovalMutationStatus Status,
    HumanApprovalEvent? Event = null,
    string? ErrorMessage = null)
{
    public bool Succeeded => Status == HumanApprovalMutationStatus.Created && Event is not null;
}

public enum HumanApprovalMutationStatus
{
    Created,
    InvalidRequest,
    NotFound,
    Unauthorized,
    Expired,
    Stale,
    AlreadyTerminal,
    Duplicate,
    CapacityExceeded,
    Unavailable
}

public sealed class HumanApprovalHistory
{
    public HumanApprovalHistory(Guid projectId, Guid requestId, IReadOnlyList<HumanApprovalEvent> events)
    {
        ProjectId = projectId;
        RequestId = requestId;
        Events = events ?? throw new ArgumentNullException(nameof(events));
    }

    public Guid ProjectId { get; }
    public Guid RequestId { get; }
    public IReadOnlyList<HumanApprovalEvent> Events { get; }
}

public enum HumanApprovalHistoryReadStatus
{
    Success,
    Missing,
    Unavailable,
    Corrupt,
    Unsupported
}

public sealed class HumanApprovalStoreReadResult
{
    public HumanApprovalStoreReadResult(
        HumanApprovalHistoryReadStatus status,
        IReadOnlyList<HumanApprovalHistory>? histories = null,
        string? errorMessage = null)
    {
        Status = status;
        Histories = histories ?? Array.Empty<HumanApprovalHistory>();
        ErrorMessage = errorMessage;
    }

    public HumanApprovalHistoryReadStatus Status { get; }
    public IReadOnlyList<HumanApprovalHistory> Histories { get; }
    public string? ErrorMessage { get; }
    public bool IsUsable => Status is HumanApprovalHistoryReadStatus.Success or HumanApprovalHistoryReadStatus.Missing;
}

public interface IHumanApprovalStore
{
    Task<HumanApprovalStoreReadResult> ReadAsync(Guid projectId, Guid requestId, CancellationToken cancellationToken = default);

    Task<HumanApprovalStoreReadResult> ReadProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<HumanApprovalOperationResult> AppendAsync(HumanApprovalEvent value, CancellationToken cancellationToken = default);
}

public sealed class HumanApprovalInboxItem
{
    public Guid ProjectId { get; internal set; }
    public Guid RequestId { get; internal set; }
    public HumanApprovalActionKind ActionKind { get; internal set; }
    public DateTimeOffset RequestedAt { get; internal set; }
    public DateTimeOffset ExpiresAt { get; internal set; }
    public HumanApprovalState EffectiveState { get; internal set; }
    public bool OwnerAttentionRequired { get; internal set; }
    public string RequesterReference { get; internal set; } = string.Empty;
    public string? DecisionActorReference { get; internal set; }
    public DateTimeOffset? DecisionTimestamp { get; internal set; }
    public string SafeTargetSummary { get; internal set; } = string.Empty;
    public string TargetFingerprint { get; internal set; } = string.Empty;
    public string EvidenceRevisionHash { get; internal set; } = string.Empty;
    public bool CurrentContextKnown { get; internal set; }
    public bool IsStale { get; internal set; }
    public HumanApprovalNextAction NextRequiredAction { get; internal set; }
    public HumanApprovalReference? SatisfyingApprovalReference { get; internal set; }
}

public sealed class HumanApprovalInboxReadResult
{
    public HumanApprovalInboxReadResult(
        HumanApprovalHistoryReadStatus status,
        IReadOnlyList<HumanApprovalInboxItem>? items = null,
        string? errorMessage = null)
    {
        Status = status;
        Items = items ?? Array.Empty<HumanApprovalInboxItem>();
        ErrorMessage = errorMessage;
    }

    public HumanApprovalHistoryReadStatus Status { get; }
    public IReadOnlyList<HumanApprovalInboxItem> Items { get; }
    public string? ErrorMessage { get; }
    public bool IsUsable => Status is HumanApprovalHistoryReadStatus.Success or HumanApprovalHistoryReadStatus.Missing;
}

public interface IHumanApprovalService
{
    Task<HumanApprovalOperationResult> RequestAsync(HumanApprovalRequest request, CancellationToken cancellationToken = default);
    Task<HumanApprovalOperationResult> EscalateAsync(Guid projectId, Guid requestId, string escalationReference, CancellationToken cancellationToken = default);
    Task<HumanApprovalOperationResult> ApproveAsync(HumanApprovalDecisionRequest request, CancellationToken cancellationToken = default);
    Task<HumanApprovalOperationResult> RejectAsync(HumanApprovalDecisionRequest request, CancellationToken cancellationToken = default);
    Task<HumanApprovalOperationResult> WaiveAsync(HumanApprovalDecisionRequest request, CancellationToken cancellationToken = default);
    Task<HumanApprovalEvaluation> EvaluateAsync(HumanApprovalEvaluationContext context, Guid requestId, CancellationToken cancellationToken = default);
    Task<HumanApprovalInboxReadResult> ReadInboxAsync(
        Guid projectId,
        IReadOnlyDictionary<Guid, HumanApprovalEvaluationContext>? currentContexts = null,
        CancellationToken cancellationToken = default);
}

public static class HumanApprovalRecoveryProjection
{
    public static RecoveryGateSnapshot ToRecoveryGateSnapshot(HumanApprovalEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        var state = evaluation.CanProceed &&
                    (evaluation.EffectiveState is HumanApprovalState.Approved or HumanApprovalState.Waived)
            ? RecoveryGateState.Satisfied
            : evaluation.EffectiveState is HumanApprovalState.Pending or HumanApprovalState.Escalated
                ? RecoveryGateState.Pending
                : RecoveryGateState.Failed;

        var supportingEvidence = state == RecoveryGateState.Satisfied && evaluation.SatisfyingReference?.EventId is Guid eventId
            ? new[] { eventId }
            : Array.Empty<Guid>();
        return new RecoveryGateSnapshot(RecoveryGateKind.Approval, state, supportingEvidence);
    }
}

public static class HumanApprovalIntegrity
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string ComputeRequestContentHash(HumanApprovalRequest value) => Hash(new
    {
        value.ProjectId,
        value.RequestId,
        value.ActionKind,
        contract = new
        {
            value.ContractReference.ContractId,
            value.ContractReference.Revision,
            value.ContractReference.SchemaVersion,
            value.ContractReference.ContentHash
        },
        target = new
        {
            value.Target.ActionKind,
            value.Target.SafeSummary,
            value.Target.CanonicalRepositoryIdentity,
            value.Target.BaseRef,
            value.Target.BaseSha,
            value.Target.HeadRef,
            value.Target.HeadSha,
            value.Target.OperationFingerprint,
            value.Target.ContentHash
        },
        evidence = new
        {
            value.EvidenceRevision.SchemaVersion,
            value.EvidenceRevision.ContentHash,
            references = value.EvidenceRevision.References.Select(static reference => new
            {
                reference.EvidenceId,
                reference.Kind,
                reference.Reference,
                reference.SchemaVersion,
                reference.ContentHash
            }).ToArray()
        },
        value.RequesterReference,
        value.RequestedAt,
        value.ExpiresAt,
        value.Reason,
        value.PolicyReference
    });

    public static string ComputeEventContentHash(HumanApprovalEvent value) => Hash(new
    {
        value.EventId,
        value.ProjectId,
        value.RequestId,
        value.Kind,
        value.OccurredAt,
        value.ActorKind,
        value.ActorReference,
        value.Reason,
        requestHash = value.Request?.ContentHash
    });

    internal static string Hash<T>(T value)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Options));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
