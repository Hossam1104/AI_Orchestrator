using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.Routing;
using AIUsageMonitor.Application.Workspaces;

namespace AIUsageMonitor.Infrastructure.Persistence;

internal sealed class RecoveryCheckpointRecord
{
    public string RecordType { get; set; } = string.Empty;
    public Guid ProjectId { get; set; }
    public Guid CheckpointId { get; set; }
    public int SchemaVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public RecoveryCheckpointLifecycleState LifecycleState { get; set; }
    public RecoveryContextReferenceRecord Context { get; set; } = new();
    public Guid PlanningContractId { get; set; }
    public int PlanningContractRevision { get; set; }
    public int PlanningContractSchemaVersion { get; set; }
    public string PlanningContractContentHash { get; set; } = string.Empty;
    public Guid? WorkGraphId { get; set; }
    public int? WorkGraphSchemaVersion { get; set; }
    public string? WorkGraphContentHash { get; set; }
    public Guid? WorkGraphNodeId { get; set; }
    public Guid? HandoffPackageId { get; set; }
    public int? HandoffPackageSchemaVersion { get; set; }
    public string? HandoffPackageContentHash { get; set; }
    public Guid? RoutingDecisionId { get; set; }
    public int? RoutingDecisionSchemaVersion { get; set; }
    public string? RoutingDecisionContentHash { get; set; }
    public Guid? WorkspacePreparationPlanProjectId { get; set; }
    public Guid? WorkspacePreparationPlanId { get; set; }
    public int? WorkspacePreparationPlanSchemaVersion { get; set; }
    public string? WorkspacePreparationPlanContentHash { get; set; }
    public RecoveryCheckpointReferenceRecord? PreviousCheckpointReference { get; set; }
    public List<RecoveryAgentRoleReferenceRecord> SelectedAgentRoleReferences { get; set; } = [];
    public List<RecoveryEvidenceReferenceRecord> EvidenceReferences { get; set; } = [];
    public List<RecoveryGateSnapshotRecord> GateSnapshots { get; set; } = [];
    public List<RecoveryBlockerRecord> Blockers { get; set; } = [];
    public RecoveryNextSafeAction NextSafeAction { get; set; }
    public string? Explanation { get; set; }
    public string? ContentHash { get; set; }

    public static RecoveryCheckpointRecord FromApplication(RecoveryCheckpoint value) => new()
    {
        RecordType = "recovery-checkpoint",
        ProjectId = value.ProjectId,
        CheckpointId = value.CheckpointId,
        SchemaVersion = value.SchemaVersion,
        CreatedAt = value.CreatedAt,
        LifecycleState = value.LifecycleState,
        Context = RecoveryContextReferenceRecord.FromApplication(value.Context),
        PlanningContractId = value.PlanningContractReference.ContractId,
        PlanningContractRevision = value.PlanningContractReference.Revision,
        PlanningContractSchemaVersion = value.PlanningContractReference.SchemaVersion,
        PlanningContractContentHash = value.PlanningContractReference.ContentHash,
        WorkGraphId = value.WorkGraphReference?.GraphId,
        WorkGraphSchemaVersion = value.WorkGraphReference?.SchemaVersion,
        WorkGraphContentHash = value.WorkGraphReference?.ContentHash,
        WorkGraphNodeId = value.WorkGraphNodeId,
        HandoffPackageId = value.HandoffPackageReference?.PackageId,
        HandoffPackageSchemaVersion = value.HandoffPackageReference?.SchemaVersion,
        HandoffPackageContentHash = value.HandoffPackageReference?.ContentHash,
        RoutingDecisionId = value.RoutingDecisionReference?.DecisionId,
        RoutingDecisionSchemaVersion = value.RoutingDecisionReference?.SchemaVersion,
        RoutingDecisionContentHash = value.RoutingDecisionReference?.ContentHash,
        WorkspacePreparationPlanProjectId = value.WorkspacePreparationPlanReference?.ProjectId,
        WorkspacePreparationPlanId = value.WorkspacePreparationPlanReference?.PlanId,
        WorkspacePreparationPlanSchemaVersion = value.WorkspacePreparationPlanReference?.SchemaVersion,
        WorkspacePreparationPlanContentHash = value.WorkspacePreparationPlanReference?.ContentHash,
        PreviousCheckpointReference = value.PreviousCheckpointReference is null
            ? null
            : RecoveryCheckpointReferenceRecord.FromApplication(value.PreviousCheckpointReference),
        SelectedAgentRoleReferences = value.SelectedAgentRoleReferences.Select(RecoveryAgentRoleReferenceRecord.FromApplication).ToList(),
        EvidenceReferences = value.EvidenceReferences.Select(RecoveryEvidenceReferenceRecord.FromApplication).ToList(),
        GateSnapshots = value.GateSnapshots.Select(RecoveryGateSnapshotRecord.FromApplication).ToList(),
        Blockers = value.Blockers.Select(RecoveryBlockerRecord.FromApplication).ToList(),
        NextSafeAction = value.NextSafeAction,
        Explanation = value.Explanation,
        ContentHash = value.ContentHash
    };

    public RecoveryCheckpoint ToApplication() => ToApplication(ContentHash);

    public RecoveryCheckpoint ToApplicationForIntegrityValidation() => ToApplication(null);

    private RecoveryCheckpoint ToApplication(string? contentHash) => new(
        ProjectId,
        CheckpointId,
        SchemaVersion,
        CreatedAt,
        LifecycleState,
        Context.ToApplication(),
        new PlanningExecutionContractReference(
            PlanningContractId,
            PlanningContractRevision,
            PlanningContractSchemaVersion,
            PlanningContractContentHash),
        WorkGraphId is null
            ? null
            : new WorkGraphReference(
                WorkGraphId.Value,
                WorkGraphSchemaVersion ?? 0,
                WorkGraphContentHash ?? string.Empty),
        WorkGraphNodeId,
        HandoffPackageId is null
            ? null
            : new HandoffPackageReference(
                HandoffPackageId.Value,
                HandoffPackageSchemaVersion ?? 0,
                HandoffPackageContentHash ?? string.Empty),
        RoutingDecisionId is null
            ? null
            : new RoutingDecisionReference(
                RoutingDecisionId.Value,
                RoutingDecisionSchemaVersion ?? 0,
                RoutingDecisionContentHash ?? string.Empty),
        WorkspacePreparationPlanId is null
            ? null
            : new WorkspacePreparationPlanReference(
                WorkspacePreparationPlanId.Value,
                WorkspacePreparationPlanSchemaVersion ?? 0,
                WorkspacePreparationPlanContentHash ?? string.Empty,
                WorkspacePreparationPlanProjectId ?? Guid.Empty),
        PreviousCheckpointReference?.ToApplication(),
        (SelectedAgentRoleReferences ?? []).Select(static value => value.ToApplication()).ToArray(),
        (EvidenceReferences ?? []).Select(static value => value.ToApplication()).ToArray(),
        (GateSnapshots ?? []).Select(static value => value.ToApplication()).ToArray(),
        (Blockers ?? []).Select(static value => value.ToApplication()).ToArray(),
        NextSafeAction,
        Explanation,
        contentHash);
}

internal sealed class RecoveryContextReferenceRecord
{
    public Guid ContextId { get; set; }
    public int ContextContractVersion { get; set; }
    public DateTimeOffset ContextUpdatedAt { get; set; }

    public static RecoveryContextReferenceRecord FromApplication(RecoveryContextReference value) => new()
    {
        ContextId = value.ContextId,
        ContextContractVersion = value.ContextContractVersion,
        ContextUpdatedAt = value.ContextUpdatedAt
    };

    public RecoveryContextReference ToApplication() => new(ContextId, ContextContractVersion, ContextUpdatedAt);
}

internal sealed class RecoveryCheckpointReferenceRecord
{
    public Guid CheckpointId { get; set; }
    public int SchemaVersion { get; set; }
    public string ContentHash { get; set; } = string.Empty;

    public static RecoveryCheckpointReferenceRecord FromApplication(RecoveryCheckpointReference value) => new()
    {
        CheckpointId = value.CheckpointId,
        SchemaVersion = value.SchemaVersion,
        ContentHash = value.ContentHash
    };

    public RecoveryCheckpointReference ToApplication() => new(CheckpointId, SchemaVersion, ContentHash);
}

internal sealed class RecoveryAgentRoleReferenceRecord
{
    public Guid AgentId { get; set; }
    public AIUsageMonitor.Application.Agents.AgentRole Role { get; set; }
    public string? SelectionEvidenceReference { get; set; }

    public static RecoveryAgentRoleReferenceRecord FromApplication(RecoveryAgentRoleReference value) => new()
    {
        AgentId = value.AgentId,
        Role = value.Role,
        SelectionEvidenceReference = value.SelectionEvidenceReference
    };

    public RecoveryAgentRoleReference ToApplication() => new(AgentId, Role, SelectionEvidenceReference);
}

internal sealed class RecoveryEvidenceReferenceRecord
{
    public Guid EvidenceId { get; set; }
    public RecoveryEvidenceKind Kind { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateTimeOffset? ObservedAt { get; set; }
    public RecoveryEvidenceFreshness Freshness { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }
    public string? ContentHash { get; set; }

    public static RecoveryEvidenceReferenceRecord FromApplication(RecoveryEvidenceReference value) => new()
    {
        EvidenceId = value.EvidenceId,
        Kind = value.Kind,
        Reference = value.Reference,
        ObservedAt = value.ObservedAt,
        Freshness = value.Freshness,
        ValidUntil = value.ValidUntil,
        ContentHash = value.ContentHash
    };

    public RecoveryEvidenceReference ToApplication() => new(
        EvidenceId,
        Kind,
        Reference,
        ObservedAt,
        Freshness,
        ValidUntil,
        ContentHash);
}

internal sealed class RecoveryGateSnapshotRecord
{
    public RecoveryGateKind Kind { get; set; }
    public RecoveryGateState State { get; set; }
    public List<Guid> SupportingEvidenceIds { get; set; } = [];

    public static RecoveryGateSnapshotRecord FromApplication(RecoveryGateSnapshot value) => new()
    {
        Kind = value.Kind,
        State = value.State,
        SupportingEvidenceIds = value.SupportingEvidenceIds.ToList()
    };

    public RecoveryGateSnapshot ToApplication() => new(Kind, State, SupportingEvidenceIds);
}

internal sealed class RecoveryBlockerRecord
{
    public string BlockerId { get; set; } = string.Empty;
    public RecoveryBlockerKind Kind { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public bool OwnerActionRequired { get; set; }

    public static RecoveryBlockerRecord FromApplication(RecoveryBlocker value) => new()
    {
        BlockerId = value.BlockerId,
        Kind = value.Kind,
        Description = value.Description,
        Reference = value.Reference,
        OwnerActionRequired = value.OwnerActionRequired
    };

    public RecoveryBlocker ToApplication() => new(BlockerId, Kind, Description, Reference, OwnerActionRequired);
}

internal sealed class ContinuationHeadRecord
{
    public string RecordType { get; set; } = string.Empty;
    public Guid ProjectId { get; set; }
    public int SchemaVersion { get; set; }
    public long Generation { get; set; }
    public RecoveryCheckpointReferenceRecord LatestCheckpointReference { get; set; } = new();
    public RecoveryCheckpointReferenceRecord? LastSafeCheckpointReference { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? ContentHash { get; set; }

    public static ContinuationHeadRecord FromApplication(ContinuationHead value) => new()
    {
        RecordType = "continuation-head",
        ProjectId = value.ProjectId,
        SchemaVersion = value.SchemaVersion,
        Generation = value.Generation,
        LatestCheckpointReference = RecoveryCheckpointReferenceRecord.FromApplication(value.LatestCheckpointReference),
        LastSafeCheckpointReference = value.LastSafeCheckpointReference is null
            ? null
            : RecoveryCheckpointReferenceRecord.FromApplication(value.LastSafeCheckpointReference),
        UpdatedAt = value.UpdatedAt,
        ContentHash = value.ContentHash
    };

    public ContinuationHead ToApplication() => new(
        ProjectId,
        SchemaVersion,
        Generation,
        LatestCheckpointReference.ToApplication(),
        LastSafeCheckpointReference?.ToApplication(),
        UpdatedAt,
        ContentHash);

    public ContinuationHead ToApplicationForIntegrityValidation() => new(
        ProjectId,
        SchemaVersion,
        Generation,
        LatestCheckpointReference.ToApplication(),
        LastSafeCheckpointReference?.ToApplication(),
        UpdatedAt);
}
