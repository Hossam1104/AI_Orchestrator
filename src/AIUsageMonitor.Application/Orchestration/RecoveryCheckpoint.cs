using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Routing;
using AIUsageMonitor.Application.Workspaces;

namespace AIUsageMonitor.Application.Orchestration;

/// <summary>Semantic version of the immutable recovery checkpoint authority.</summary>
public static class RecoveryCheckpointSchema
{
    /// <summary>
    /// Version 2 adds the optional <see cref="RecoveryCheckpoint.RoutingDecisionReference"/> and
    /// <see cref="RecoveryCheckpoint.WorkspacePreparationPlanReference"/> bindings so a Ready
    /// checkpoint carries enough durable authority to rehydrate a startable execution after a
    /// process restart (APO-70). Version 1 records read back as <c>MigrationRequired</c>.
    /// </summary>
    public const int CurrentVersion = 2;
}

public static class RecoveryCheckpointLimits
{
    public const int MaxCanonicalPayloadBytes = 128 * 1024;
    public const int MaxEvidenceReferences = 64;
    public const int MaxBlockers = 32;
    public const int MaxSelectedAgentRoles = 32;
    public const int MaxGateSnapshots = 3;
    public const int MaxDescriptionLength = 4_000;
    public const int MaxReferenceLength = 1_000;
    public const int MaxBlockerReferenceLength = 1_000;

    public static bool TryValidateEvidenceReferences(
        IReadOnlyList<RecoveryEvidenceReference>? values,
        out string? errorMessage)
    {
        errorMessage = null;
        if (values is null)
        {
            errorMessage = "Recovery evidence references are required.";
            return false;
        }

        if (values.Count > MaxEvidenceReferences)
        {
            errorMessage = $"Recovery evidence references exceed the supported capacity of {MaxEvidenceReferences}.";
            return false;
        }

        if (values.Any(static value => value is null))
        {
            errorMessage = "Recovery evidence references cannot contain null entries.";
            return false;
        }

        if (values.Select(static value => value.EvidenceId).Distinct().Count() != values.Count)
        {
            errorMessage = "Recovery evidence identifiers must be unique.";
            return false;
        }

        return true;
    }
}

public enum RecoveryCheckpointLifecycleState
{
    Ready,
    Waiting,
    Blocked,
    ApprovalRequired,
    Interrupted,
    Failed,
    Cancelled,
    Completed
}

public enum RecoveryEvidenceKind
{
    Repository,
    Tracker,
    Routing,
    Validation,
    Review,
    Approval,
    Delivery,
    Other
}

public enum RecoveryEvidenceFreshness
{
    Verified,
    PointInTime,
    Stale,
    Unknown,
    NotApplicable
}

public enum RecoveryGateKind
{
    Validation,
    Review,
    Approval
}

public enum RecoveryGateState
{
    NotRequired,
    Pending,
    Satisfied,
    Failed,
    Unknown
}

public enum RecoveryBlockerKind
{
    Dependency,
    RepositoryEvidence,
    TrackerEvidence,
    RoutingEvidence,
    ValidationGate,
    ReviewGate,
    ApprovalGate,
    Contract,
    Context,
    ExternalCondition,
    Other
}

public enum RecoveryNextSafeAction
{
    ContinueFromCheckpoint,
    RefreshRepositoryEvidence,
    RefreshTrackerEvidence,
    RefreshRoutingEvidence,
    RunValidation,
    RequestReview,
    RequestApproval,
    ResolveBlocker,
    Replan,
    InspectProjectContext,
    NoActionCompleted
}

public enum SmartContinueResolutionState
{
    Resumable,
    Blocked,
    Stale,
    Completed,
    ApprovalRequired,
    ContextInsufficient,
    ProjectNotFound,
    CheckpointMissing,
    UnsupportedVersion,
    MigrationRequired,
    IntegrityFailure,
    Unavailable
}

/// <summary>A bounded pointer to the current APO-39 context; it is not a context copy.</summary>
public sealed class RecoveryContextReference
{
    public RecoveryContextReference(Guid contextId, int contextContractVersion, DateTimeOffset contextUpdatedAt)
    {
        if (contextId == Guid.Empty)
        {
            throw new ArgumentException("Context id cannot be empty.", nameof(contextId));
        }

        if (contextContractVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contextContractVersion));
        }

        if (contextUpdatedAt == default)
        {
            throw new ArgumentException("Context updated time is required.", nameof(contextUpdatedAt));
        }

        ContextId = contextId;
        ContextContractVersion = contextContractVersion;
        ContextUpdatedAt = contextUpdatedAt;
    }

    public Guid ContextId { get; }
    public int ContextContractVersion { get; }
    public DateTimeOffset ContextUpdatedAt { get; }
}

/// <summary>Content-integrity evidence for one immutable recovery checkpoint.</summary>
public sealed class RecoveryCheckpointReference
{
    public RecoveryCheckpointReference(Guid checkpointId, int schemaVersion, string contentHash)
    {
        if (checkpointId == Guid.Empty)
        {
            throw new ArgumentException("Checkpoint id cannot be empty.", nameof(checkpointId));
        }

        if (schemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        }

        if (!IsSha256(contentHash))
        {
            throw new ArgumentException("Content hash must be a SHA-256 hexadecimal value.", nameof(contentHash));
        }

        CheckpointId = checkpointId;
        SchemaVersion = schemaVersion;
        ContentHash = contentHash.ToLowerInvariant();
    }

    public Guid CheckpointId { get; }
    public int SchemaVersion { get; }
    /// <summary>Content-integrity evidence, not a signature or authentication proof.</summary>
    public string ContentHash { get; }

    public override string ToString() =>
        $"checkpoint:{CheckpointId:D}/schema:{SchemaVersion}/sha256:{ContentHash}";

    public static bool IsSha256(string? value) =>
        value is not null && value.Length == 64 && value.All(static character => Uri.IsHexDigit(character));
}

/// <summary>Bounded model-role selection metadata. It does not select or invoke an agent.</summary>
public sealed class RecoveryAgentRoleReference
{
    public RecoveryAgentRoleReference(Guid agentId, AgentRole role, string? selectionEvidenceReference = null)
    {
        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("Agent id cannot be empty.", nameof(agentId));
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentException("Agent role is undefined.", nameof(role));
        }

        AgentId = agentId;
        Role = role;
        SelectionEvidenceReference = NormalizeOptional(selectionEvidenceReference, nameof(selectionEvidenceReference));
    }

    public Guid AgentId { get; }
    public AgentRole Role { get; }
    public string? SelectionEvidenceReference { get; }

    private static string? NormalizeOptional(string? value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Length <= RecoveryCheckpointLimits.MaxReferenceLength
                ? value.Trim()
                : throw new ArgumentException(
                    $"The value cannot exceed {RecoveryCheckpointLimits.MaxReferenceLength} characters.",
                    parameterName);
}

/// <summary>Reference-only evidence with explicit freshness semantics.</summary>
public sealed class RecoveryEvidenceReference
{
    public RecoveryEvidenceReference(
        Guid evidenceId,
        RecoveryEvidenceKind kind,
        string reference,
        DateTimeOffset? observedAt,
        RecoveryEvidenceFreshness freshness,
        DateTimeOffset? validUntil = null,
        string? contentHash = null)
    {
        if (evidenceId == Guid.Empty)
        {
            throw new ArgumentException("Evidence id cannot be empty.", nameof(evidenceId));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException("Evidence kind is undefined.", nameof(kind));
        }

        if (!Enum.IsDefined(freshness))
        {
            throw new ArgumentException("Evidence freshness is undefined.", nameof(freshness));
        }

        if (freshness == RecoveryEvidenceFreshness.Verified && observedAt is null)
        {
            throw new ArgumentException("Verified evidence requires an observation time.", nameof(observedAt));
        }

        if (validUntil is not null && observedAt is not null && validUntil < observedAt)
        {
            throw new ArgumentException("Evidence validity cannot precede observation time.", nameof(validUntil));
        }

        if (contentHash is not null && !RecoveryCheckpointReference.IsSha256(contentHash))
        {
            throw new ArgumentException("Evidence content hash must be a SHA-256 hexadecimal value.", nameof(contentHash));
        }

        EvidenceId = evidenceId;
        Kind = kind;
        Reference = RequiredText(reference, nameof(reference), RecoveryCheckpointLimits.MaxReferenceLength);
        ObservedAt = observedAt;
        Freshness = freshness;
        ValidUntil = validUntil;
        ContentHash = contentHash?.ToLowerInvariant();
    }

    public Guid EvidenceId { get; }
    public RecoveryEvidenceKind Kind { get; }
    public string Reference { get; }
    public DateTimeOffset? ObservedAt { get; }
    public RecoveryEvidenceFreshness Freshness { get; }
    public DateTimeOffset? ValidUntil { get; }
    public string? ContentHash { get; }

    private static string RequiredText(string value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A bounded text value is required.", parameterName);
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
    }
}

public sealed class RecoveryGateSnapshot
{
    public RecoveryGateSnapshot(
        RecoveryGateKind kind,
        RecoveryGateState state,
        IReadOnlyList<Guid>? supportingEvidenceIds = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException("Gate kind is undefined.", nameof(kind));
        }

        if (!Enum.IsDefined(state))
        {
            throw new ArgumentException("Gate state is undefined.", nameof(state));
        }

        var values = supportingEvidenceIds ?? Array.Empty<Guid>();
        if (values.Count > RecoveryCheckpointLimits.MaxEvidenceReferences)
        {
            throw new ArgumentException("A gate contains too many supporting evidence references.", nameof(supportingEvidenceIds));
        }

        var normalized = values.Distinct().OrderBy(static value => value).ToArray();
        if (normalized.Any(static value => value == Guid.Empty))
        {
            throw new ArgumentException("Gate evidence identifiers cannot be empty.", nameof(supportingEvidenceIds));
        }

        if (state == RecoveryGateState.Satisfied && normalized.Length == 0)
        {
            throw new ArgumentException("A satisfied gate requires supporting evidence.", nameof(supportingEvidenceIds));
        }

        Kind = kind;
        State = state;
        SupportingEvidenceIds = Array.AsReadOnly(normalized);
    }

    public RecoveryGateKind Kind { get; }
    public RecoveryGateState State { get; }
    public IReadOnlyList<Guid> SupportingEvidenceIds { get; }
}

public sealed class RecoveryBlocker
{
    public RecoveryBlocker(
        string blockerId,
        RecoveryBlockerKind kind,
        string description,
        string? reference = null,
        bool ownerActionRequired = false)
    {
        BlockerId = RequiredText(blockerId, nameof(blockerId), 120);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException("Blocker kind is undefined.", nameof(kind));
        }

        Kind = kind;
        Description = RequiredText(description, nameof(description), RecoveryCheckpointLimits.MaxDescriptionLength);
        Reference = OptionalText(reference, nameof(reference), RecoveryCheckpointLimits.MaxBlockerReferenceLength);
        OwnerActionRequired = ownerActionRequired;
    }

    public string BlockerId { get; }
    public RecoveryBlockerKind Kind { get; }
    public string Description { get; }
    public string? Reference { get; }
    public bool OwnerActionRequired { get; }

    private static string RequiredText(string value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A bounded text value is required.", parameterName);
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
    }

    private static string? OptionalText(string? value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
    }
}

/// <summary>
/// Immutable, project-isolated recovery boundary. It contains references and bounded metadata,
/// never chat, prompts, repository contents, credentials, or executable behavior.
/// </summary>
public sealed class RecoveryCheckpoint
{
    public RecoveryCheckpoint(
        Guid projectId,
        Guid checkpointId,
        int schemaVersion,
        DateTimeOffset createdAt,
        RecoveryCheckpointLifecycleState lifecycleState,
        RecoveryContextReference context,
        PlanningExecutionContractReference planningContractReference,
        WorkGraphReference? workGraphReference = null,
        Guid? workGraphNodeId = null,
        HandoffPackageReference? handoffPackageReference = null,
        RoutingDecisionReference? routingDecisionReference = null,
        WorkspacePreparationPlanReference? workspacePreparationPlanReference = null,
        RecoveryCheckpointReference? previousCheckpointReference = null,
        IReadOnlyList<RecoveryAgentRoleReference>? selectedAgentRoleReferences = null,
        IReadOnlyList<RecoveryEvidenceReference>? evidenceReferences = null,
        IReadOnlyList<RecoveryGateSnapshot>? gateSnapshots = null,
        IReadOnlyList<RecoveryBlocker>? blockers = null,
        RecoveryNextSafeAction nextSafeAction = RecoveryNextSafeAction.ContinueFromCheckpoint,
        string? explanation = null,
        string? contentHash = null)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project id cannot be empty.", nameof(projectId));
        }

        if (checkpointId == Guid.Empty)
        {
            throw new ArgumentException("Checkpoint id cannot be empty.", nameof(checkpointId));
        }

        if (schemaVersion != RecoveryCheckpointSchema.CurrentVersion)
        {
            throw new ArgumentException(
                $"Only recovery checkpoint schema {RecoveryCheckpointSchema.CurrentVersion} is supported.",
                nameof(schemaVersion));
        }

        if (createdAt == default)
        {
            throw new ArgumentException("Checkpoint creation time is required.", nameof(createdAt));
        }

        if (!Enum.IsDefined(lifecycleState))
        {
            throw new ArgumentException("Checkpoint lifecycle state is undefined.", nameof(lifecycleState));
        }

        Context = context ?? throw new ArgumentNullException(nameof(context));
        PlanningContractReference = planningContractReference ?? throw new ArgumentNullException(nameof(planningContractReference));

        if (workGraphNodeId is not null && workGraphReference is null)
        {
            throw new ArgumentException("A work-graph node requires a work-graph reference.", nameof(workGraphNodeId));
        }

        if (previousCheckpointReference?.CheckpointId == checkpointId)
        {
            throw new ArgumentException("A checkpoint cannot reference itself as its predecessor.", nameof(previousCheckpointReference));
        }

        if (workspacePreparationPlanReference is not null && workspacePreparationPlanReference.ProjectId != projectId)
        {
            throw new ArgumentException(
                "Workspace preparation plan reference belongs to another project.",
                nameof(workspacePreparationPlanReference));
        }

        if (!Enum.IsDefined(nextSafeAction))
        {
            throw new ArgumentException("Next-safe-action value is undefined.", nameof(nextSafeAction));
        }

        ProjectId = projectId;
        CheckpointId = checkpointId;
        SchemaVersion = schemaVersion;
        CreatedAt = createdAt;
        LifecycleState = lifecycleState;
        WorkGraphReference = workGraphReference;
        WorkGraphNodeId = workGraphNodeId;
        HandoffPackageReference = handoffPackageReference;
        RoutingDecisionReference = routingDecisionReference;
        WorkspacePreparationPlanReference = workspacePreparationPlanReference;
        PreviousCheckpointReference = previousCheckpointReference;
        SelectedAgentRoleReferences = NormalizeAgentRoles(selectedAgentRoleReferences);
        EvidenceReferences = NormalizeEvidence(evidenceReferences);
        GateSnapshots = NormalizeGates(gateSnapshots);
        Blockers = NormalizeBlockers(blockers);
        NextSafeAction = nextSafeAction;
        Explanation = NormalizeOptional(explanation, nameof(explanation), RecoveryCheckpointLimits.MaxDescriptionLength);
        ContentHash = string.Empty;

        var calculatedHash = RecoveryCheckpointIntegrity.ComputeContentHash(this);
        if (contentHash is not null &&
            (!RecoveryCheckpointReference.IsSha256(contentHash) ||
             !string.Equals(contentHash, calculatedHash, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("The supplied checkpoint content hash does not match the payload.", nameof(contentHash));
        }

        ContentHash = calculatedHash;
        Reference = new RecoveryCheckpointReference(CheckpointId, SchemaVersion, ContentHash);
    }

    public Guid ProjectId { get; }
    public Guid CheckpointId { get; }
    public int SchemaVersion { get; }
    public DateTimeOffset CreatedAt { get; }
    public RecoveryCheckpointLifecycleState LifecycleState { get; }
    public RecoveryContextReference Context { get; }
    public PlanningExecutionContractReference PlanningContractReference { get; }
    public WorkGraphReference? WorkGraphReference { get; }
    public Guid? WorkGraphNodeId { get; }
    public HandoffPackageReference? HandoffPackageReference { get; }
    public RoutingDecisionReference? RoutingDecisionReference { get; }
    public WorkspacePreparationPlanReference? WorkspacePreparationPlanReference { get; }
    public RecoveryCheckpointReference? PreviousCheckpointReference { get; }
    public IReadOnlyList<RecoveryAgentRoleReference> SelectedAgentRoleReferences { get; }
    public IReadOnlyList<RecoveryEvidenceReference> EvidenceReferences { get; }
    public IReadOnlyList<RecoveryGateSnapshot> GateSnapshots { get; }
    public IReadOnlyList<RecoveryBlocker> Blockers { get; }
    public RecoveryNextSafeAction NextSafeAction { get; }
    public string? Explanation { get; }
    /// <summary>SHA-256 content-integrity evidence, not a signature or authentication proof.</summary>
    public string ContentHash { get; private set; }
    public RecoveryCheckpointReference Reference { get; private set; }

    private static IReadOnlyList<RecoveryAgentRoleReference> NormalizeAgentRoles(
        IReadOnlyList<RecoveryAgentRoleReference>? values)
    {
        var result = Normalize(values, RecoveryCheckpointLimits.MaxSelectedAgentRoles, nameof(values));
        if (result.GroupBy(static value => (value.AgentId, value.Role)).Any(static group => group.Count() > 1))
        {
            throw new ArgumentException("Selected agent-role references must be unique.", nameof(values));
        }

        return result
            .OrderBy(static value => value.AgentId)
            .ThenBy(static value => value.Role)
            .ToArray();
    }

    private static IReadOnlyList<RecoveryEvidenceReference> NormalizeEvidence(
        IReadOnlyList<RecoveryEvidenceReference>? values)
    {
        var result = (values ?? Array.Empty<RecoveryEvidenceReference>()).ToArray();
        if (!RecoveryCheckpointLimits.TryValidateEvidenceReferences(result, out var errorMessage))
        {
            throw new ArgumentException(errorMessage, nameof(values));
        }

        return result.OrderBy(static value => value.EvidenceId).ToArray();
    }

    private static IReadOnlyList<RecoveryGateSnapshot> NormalizeGates(
        IReadOnlyList<RecoveryGateSnapshot>? values)
    {
        var result = Normalize(values, RecoveryCheckpointLimits.MaxGateSnapshots, nameof(values));
        if (result.GroupBy(static value => value.Kind).Any(static group => group.Count() > 1))
        {
            throw new ArgumentException("Gate kinds must be unique.", nameof(values));
        }

        return result.OrderBy(static value => value.Kind).ToArray();
    }

    private static IReadOnlyList<RecoveryBlocker> NormalizeBlockers(
        IReadOnlyList<RecoveryBlocker>? values)
    {
        var result = Normalize(values, RecoveryCheckpointLimits.MaxBlockers, nameof(values));
        if (result.GroupBy(static value => value.BlockerId, StringComparer.OrdinalIgnoreCase).Any(static group => group.Count() > 1))
        {
            throw new ArgumentException("Blocker identifiers must be unique.", nameof(values));
        }

        return result.OrderBy(static value => value.BlockerId, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<T> Normalize<T>(IReadOnlyList<T>? values, int maximum, string parameterName)
        where T : class
    {
        var result = (values ?? Array.Empty<T>()).ToArray();
        if (result.Length > maximum || result.Any(static value => value is null))
        {
            throw new ArgumentException("The checkpoint collection is invalid or exceeds its bound.", parameterName);
        }

        return result;
    }

    private static string? NormalizeOptional(string? value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
    }
}

/// <summary>Deterministic SHA-256 integrity over every checkpoint authority field except its hash.</summary>
public static class RecoveryCheckpointIntegrity
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string ComputeContentHash(RecoveryCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        var json = JsonSerializer.Serialize(CreatePayload(checkpoint), Options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    public static int ComputeCanonicalPayloadBytes(RecoveryCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        var json = JsonSerializer.Serialize(CreatePayload(checkpoint), Options);
        return Encoding.UTF8.GetByteCount(json);
    }

    private static object CreatePayload(RecoveryCheckpoint value) => new
    {
        value.ProjectId,
        value.CheckpointId,
        value.SchemaVersion,
        value.CreatedAt,
        value.LifecycleState,
        context = new
        {
            value.Context.ContextId,
            value.Context.ContextContractVersion,
            value.Context.ContextUpdatedAt
        },
        planningContractReference = new
        {
            value.PlanningContractReference.ContractId,
            value.PlanningContractReference.Revision,
            value.PlanningContractReference.SchemaVersion,
            value.PlanningContractReference.ContentHash
        },
        workGraphReference = value.WorkGraphReference is null ? null : new
        {
            value.WorkGraphReference.GraphId,
            value.WorkGraphReference.SchemaVersion,
            value.WorkGraphReference.ContentHash
        },
        value.WorkGraphNodeId,
        handoffPackageReference = value.HandoffPackageReference is null ? null : new
        {
            value.HandoffPackageReference.PackageId,
            value.HandoffPackageReference.SchemaVersion,
            value.HandoffPackageReference.ContentHash
        },
        routingDecisionReference = value.RoutingDecisionReference is null ? null : new
        {
            value.RoutingDecisionReference.DecisionId,
            value.RoutingDecisionReference.SchemaVersion,
            value.RoutingDecisionReference.ContentHash
        },
        workspacePreparationPlanReference = value.WorkspacePreparationPlanReference is null ? null : new
        {
            value.WorkspacePreparationPlanReference.ProjectId,
            value.WorkspacePreparationPlanReference.PlanId,
            value.WorkspacePreparationPlanReference.SchemaVersion,
            value.WorkspacePreparationPlanReference.ContentHash
        },
        previousCheckpointReference = value.PreviousCheckpointReference is null ? null : new
        {
            value.PreviousCheckpointReference.CheckpointId,
            value.PreviousCheckpointReference.SchemaVersion,
            value.PreviousCheckpointReference.ContentHash
        },
        selectedAgentRoleReferences = value.SelectedAgentRoleReferences.Select(static item => new
        {
            item.AgentId,
            item.Role,
            item.SelectionEvidenceReference
        }).ToArray(),
        evidenceReferences = value.EvidenceReferences.Select(static item => new
        {
            item.EvidenceId,
            item.Kind,
            item.Reference,
            item.ObservedAt,
            item.Freshness,
            item.ValidUntil,
            item.ContentHash
        }).ToArray(),
        gateSnapshots = value.GateSnapshots.Select(static item => new
        {
            item.Kind,
            item.State,
            supportingEvidenceIds = item.SupportingEvidenceIds.ToArray()
        }).ToArray(),
        blockers = value.Blockers.Select(static item => new
        {
            item.BlockerId,
            item.Kind,
            item.Description,
            item.Reference,
            item.OwnerActionRequired
        }).ToArray(),
        value.NextSafeAction,
        value.Explanation
    };

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
