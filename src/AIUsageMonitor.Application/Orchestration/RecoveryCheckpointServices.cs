using System.Collections.Concurrent;
using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Routing;
using AIUsageMonitor.Application.Security;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Application.Workspaces;

namespace AIUsageMonitor.Application.Orchestration;

public sealed class RecoveryCheckpointCreationRequest
{
    public RecoveryCheckpointCreationRequest(
        Guid projectId,
        Guid checkpointId,
        RecoveryCheckpointLifecycleState lifecycleState,
        PlanningExecutionContractReference planningContractReference,
        IReadOnlyList<RecoveryEvidenceReference>? evidenceReferences = null,
        IReadOnlyList<RecoveryGateSnapshot>? gateSnapshots = null,
        IReadOnlyList<RecoveryBlocker>? blockers = null,
        RecoveryNextSafeAction nextSafeAction = RecoveryNextSafeAction.ContinueFromCheckpoint,
        string? explanation = null,
        DateTimeOffset? createdAt = null,
        WorkGraphReference? workGraphReference = null,
        Guid? workGraphNodeId = null,
        HandoffPackageReference? handoffPackageReference = null,
        RoutingDecisionReference? routingDecisionReference = null,
        WorkspacePreparationPlanReference? workspacePreparationPlanReference = null,
        RecoveryCheckpointReference? previousCheckpointReference = null,
        IReadOnlyList<RecoveryAgentRoleReference>? selectedAgentRoleReferences = null)
    {
        ProjectId = projectId;
        CheckpointId = checkpointId;
        LifecycleState = lifecycleState;
        PlanningContractReference = planningContractReference ?? throw new ArgumentNullException(nameof(planningContractReference));
        EvidenceReferences = evidenceReferences ?? Array.Empty<RecoveryEvidenceReference>();
        GateSnapshots = gateSnapshots ?? Array.Empty<RecoveryGateSnapshot>();
        Blockers = blockers ?? Array.Empty<RecoveryBlocker>();
        NextSafeAction = nextSafeAction;
        Explanation = explanation;
        CreatedAt = createdAt;
        WorkGraphReference = workGraphReference;
        WorkGraphNodeId = workGraphNodeId;
        HandoffPackageReference = handoffPackageReference;
        RoutingDecisionReference = routingDecisionReference;
        WorkspacePreparationPlanReference = workspacePreparationPlanReference;
        PreviousCheckpointReference = previousCheckpointReference;
        SelectedAgentRoleReferences = selectedAgentRoleReferences ?? Array.Empty<RecoveryAgentRoleReference>();
    }

    public Guid ProjectId { get; }
    public Guid CheckpointId { get; }
    public RecoveryCheckpointLifecycleState LifecycleState { get; }
    public PlanningExecutionContractReference PlanningContractReference { get; }
    public IReadOnlyList<RecoveryEvidenceReference> EvidenceReferences { get; }
    public IReadOnlyList<RecoveryGateSnapshot> GateSnapshots { get; }
    public IReadOnlyList<RecoveryBlocker> Blockers { get; }
    public RecoveryNextSafeAction NextSafeAction { get; }
    public string? Explanation { get; }
    public DateTimeOffset? CreatedAt { get; }
    public WorkGraphReference? WorkGraphReference { get; }
    public Guid? WorkGraphNodeId { get; }
    public HandoffPackageReference? HandoffPackageReference { get; }
    public RoutingDecisionReference? RoutingDecisionReference { get; }
    public WorkspacePreparationPlanReference? WorkspacePreparationPlanReference { get; }
    public RecoveryCheckpointReference? PreviousCheckpointReference { get; }
    public IReadOnlyList<RecoveryAgentRoleReference> SelectedAgentRoleReferences { get; }
}

public enum RecoveryCheckpointCreationStatus
{
    Created,
    ProjectNotFound,
    InvalidRequest,
    ContextMissing,
    ContextInvalid,
    ContractMissing,
    ContractInvalid,
    ContractMismatch,
    GraphMissing,
    GraphInvalid,
    GraphNodeMismatch,
    HandoffMissing,
    HandoffInvalid,
    HandoffMismatch,
    RoutingMissing,
    RoutingInvalid,
    RoutingMismatch,
    WorkspacePlanMissing,
    WorkspacePlanInvalid,
    WorkspacePlanMismatch,
    PredecessorMissing,
    PredecessorInvalid,
    InvalidLineage,
    RedactionRejected,
    EvidenceCapacityExceeded,
    EvidenceConflict,
    CheckpointConflict,
    HeadConflict,
    HeadPublicationFailed,
    PersistenceUnavailable
}

public sealed record RecoveryCheckpointCreationResult(
    RecoveryCheckpointCreationStatus Status,
    RecoveryCheckpoint? Checkpoint = null,
    ContinuationHead? Head = null,
    string? ErrorMessage = null)
{
    public bool Succeeded => Status == RecoveryCheckpointCreationStatus.Created && Checkpoint is not null && Head is not null;
}

public interface IRecoveryCheckpointService
{
    Task<RecoveryCheckpointCreationResult> CreateAsync(
        RecoveryCheckpointCreationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves all referenced durable authorities, then publishes an immutable checkpoint followed by
/// a two-slot continuation head. It never executes the returned next action.
/// </summary>
public sealed class RecoveryCheckpointService : IRecoveryCheckpointService
{
    private readonly IProjectRepository _projects;
    private readonly IProjectContextReferenceRepository _contexts;
    private readonly IPlanningExecutionContractRepository _contracts;
    private readonly IWorkGraphRepository _graphs;
    private readonly IHandoffPackageRepository _handoffs;
    private readonly IRoutingDecisionRepository _routing;
    private readonly IWorkspacePreparationPlanRepository _workspacePlans;
    private readonly IRecoveryCheckpointRepository _checkpoints;
    private readonly IContinuationHeadRepository _heads;
    private readonly IHandoffRedactionService _redaction;
    private readonly IClock _clock;
    private readonly ProjectPublicationLockManager _publicationLocks = new();

    public RecoveryCheckpointService(
        IProjectRepository projects,
        IProjectContextReferenceRepository contexts,
        IPlanningExecutionContractRepository contracts,
        IWorkGraphRepository graphs,
        IHandoffPackageRepository handoffs,
        IRoutingDecisionRepository routing,
        IWorkspacePreparationPlanRepository workspacePlans,
        IRecoveryCheckpointRepository checkpoints,
        IContinuationHeadRepository heads,
        IHandoffRedactionService redaction,
        IClock clock)
    {
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
        _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
        _handoffs = handoffs ?? throw new ArgumentNullException(nameof(handoffs));
        _routing = routing ?? throw new ArgumentNullException(nameof(routing));
        _workspacePlans = workspacePlans ?? throw new ArgumentNullException(nameof(workspacePlans));
        _checkpoints = checkpoints ?? throw new ArgumentNullException(nameof(checkpoints));
        _heads = heads ?? throw new ArgumentNullException(nameof(heads));
        _redaction = redaction ?? throw new ArgumentNullException(nameof(redaction));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<RecoveryCheckpointCreationResult> CreateAsync(
        RecoveryCheckpointCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            ValidateRequestIdentity(request);
        }
        catch (ArgumentException exception)
        {
            return new(RecoveryCheckpointCreationStatus.InvalidRequest, ErrorMessage: exception.Message);
        }

        using var publicationLock = await _publicationLocks.AcquireAsync(request.ProjectId, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var project = await _projects.GetByIdAsync(request.ProjectId, cancellationToken).ConfigureAwait(false);
            if (project is null)
            {
                return new(RecoveryCheckpointCreationStatus.ProjectNotFound, ErrorMessage: "Project was not found.");
            }

            var contextRead = await _contexts.GetAsync(request.ProjectId, cancellationToken).ConfigureAwait(false);
            if (contextRead.State == ProjectContextReadState.Missing)
            {
                return new(RecoveryCheckpointCreationStatus.ContextMissing, ErrorMessage: "Project context is missing.");
            }

            if (contextRead.State != ProjectContextReadState.Valid || contextRead.Context is null)
            {
                return new(RecoveryCheckpointCreationStatus.ContextInvalid, ErrorMessage: "Project context is not valid.");
            }

            var context = contextRead.Context;
            if (context.ProjectId != request.ProjectId || context.ContractVersion != ProjectContextContract.CurrentVersion)
            {
                return new(RecoveryCheckpointCreationStatus.ContextInvalid, ErrorMessage: "Project context identity or version is invalid.");
            }

            var contractRead = await _contracts.GetAsync(
                    request.ProjectId,
                    request.PlanningContractReference.ContractId,
                    request.PlanningContractReference.Revision,
                    cancellationToken)
                .ConfigureAwait(false);
            if (contractRead.State == PlanningContractReadState.Missing)
            {
                return new(RecoveryCheckpointCreationStatus.ContractMissing, ErrorMessage: "The exact planning contract revision is missing.");
            }

            if (contractRead.State == PlanningContractReadState.Unavailable)
            {
                return new(RecoveryCheckpointCreationStatus.PersistenceUnavailable, ErrorMessage: "Planning contract persistence is unavailable.");
            }

            if (!contractRead.IsValid || contractRead.Contract is null)
            {
                return new(RecoveryCheckpointCreationStatus.ContractInvalid, ErrorMessage: "The exact planning contract revision is not valid.");
            }

            var contract = contractRead.Contract;
            if (contract.ProjectId != request.ProjectId ||
                !SameContractReference(contract.Reference, request.PlanningContractReference) ||
                contract.Context.ProjectContextId != context.ContextId ||
                contract.Context.ProjectContextContractVersion != context.ContractVersion)
            {
                return new(RecoveryCheckpointCreationStatus.ContractMismatch, ErrorMessage: "The exact planning contract does not bind to the current project context.");
            }

            var graphStatus = await ValidateGraphAsync(request, contract, cancellationToken).ConfigureAwait(false);
            if (graphStatus is not null)
            {
                return graphStatus;
            }

            var handoffStatus = await ValidateHandoffAsync(request, contract, cancellationToken).ConfigureAwait(false);
            if (handoffStatus is not null)
            {
                return handoffStatus;
            }

            var routingStatus = await ValidateRoutingAsync(request, contract, context, cancellationToken).ConfigureAwait(false);
            if (routingStatus is not null)
            {
                return routingStatus;
            }

            var workspacePlanStatus = await ValidateWorkspacePlanAsync(request, contract, cancellationToken).ConfigureAwait(false);
            if (workspacePlanStatus is not null)
            {
                return workspacePlanStatus;
            }

            var predecessorStatus = await ValidatePredecessorAsync(request, cancellationToken).ConfigureAwait(false);
            if (predecessorStatus is not null)
            {
                return predecessorStatus;
            }

            var headRead = await _heads.GetAsync(request.ProjectId, cancellationToken).ConfigureAwait(false);
            if (headRead.State == ContinuationHeadReadState.Unavailable)
            {
                return new(RecoveryCheckpointCreationStatus.PersistenceUnavailable, ErrorMessage: "Continuation-head persistence is unavailable.");
            }

            if (headRead.State is not ContinuationHeadReadState.Missing and not ContinuationHeadReadState.Valid)
            {
                return new(RecoveryCheckpointCreationStatus.HeadConflict, ErrorMessage: "The continuation head is not a trusted publication point.");
            }

            if (headRead.IsValid && headRead.Head is not null)
            {
                var head = headRead.Head;
                if (request.PreviousCheckpointReference is null)
                {
                    return new(RecoveryCheckpointCreationStatus.InvalidLineage, ErrorMessage: "A new root cannot replace an existing continuation head.");
                }

                if (!SameCheckpointReference(request.PreviousCheckpointReference, head.LatestCheckpointReference))
                {
                    return new(RecoveryCheckpointCreationStatus.InvalidLineage, ErrorMessage: "The predecessor is not the current canonical checkpoint.");
                }

                if (head.Generation == long.MaxValue)
                {
                    return new(RecoveryCheckpointCreationStatus.HeadConflict, ErrorMessage: "Continuation-head generation is exhausted.");
                }
            }
            else if (request.PreviousCheckpointReference is not null)
            {
                return new(RecoveryCheckpointCreationStatus.HeadConflict, ErrorMessage: "A predecessor was supplied but no trusted continuation head exists.");
            }

            RecoveryCheckpoint checkpoint;
            try
            {
                checkpoint = BuildCheckpoint(request, context, cancellationToken);
            }
            catch (RedactionRejectedException)
            {
                return new(RecoveryCheckpointCreationStatus.RedactionRejected, ErrorMessage: "Checkpoint text crossed the authority redaction boundary.");
            }
            catch (ArgumentException exception)
            {
                return new(RecoveryCheckpointCreationStatus.InvalidRequest, ErrorMessage: exception.Message);
            }

            var checkpointWrite = await _checkpoints.CreateAsync(checkpoint, cancellationToken).ConfigureAwait(false);
            if (checkpointWrite.Status == RecoveryCheckpointRepositoryWriteStatus.CheckpointConflict)
            {
                return new(RecoveryCheckpointCreationStatus.CheckpointConflict, ErrorMessage: checkpointWrite.ErrorMessage);
            }

            if (!checkpointWrite.Succeeded)
            {
                return new(RecoveryCheckpointCreationStatus.PersistenceUnavailable, ErrorMessage: checkpointWrite.ErrorMessage ?? "Checkpoint persistence is unavailable.");
            }

            var generation = headRead.IsValid && headRead.Head is not null
                ? headRead.Head.Generation + 1
                : 1;
            var lastSafe = IsLastKnownSafe(checkpoint.LifecycleState)
                ? checkpoint.Reference
                : headRead.Head?.LastSafeCheckpointReference;
            var newHead = new ContinuationHead(
                request.ProjectId,
                ContinuationHeadSchema.CurrentVersion,
                generation,
                checkpoint.Reference,
                lastSafe,
                checkpoint.CreatedAt);

            var headWrite = await _heads.PublishAsync(newHead, cancellationToken).ConfigureAwait(false);
            if (headWrite.Status == ContinuationHeadRepositoryWriteStatus.HeadConflict)
            {
                return new(RecoveryCheckpointCreationStatus.HeadConflict, checkpoint, ErrorMessage: headWrite.ErrorMessage);
            }

            if (!headWrite.Succeeded)
            {
                return new(RecoveryCheckpointCreationStatus.HeadPublicationFailed, checkpoint, ErrorMessage: headWrite.ErrorMessage ?? "Continuation-head publication failed.");
            }

            return new(RecoveryCheckpointCreationStatus.Created, checkpoint, newHead);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return new(RecoveryCheckpointCreationStatus.PersistenceUnavailable, ErrorMessage: "Recovery persistence is unavailable.");
        }
        catch (IOException)
        {
            return new(RecoveryCheckpointCreationStatus.PersistenceUnavailable, ErrorMessage: "Recovery persistence is unavailable.");
        }
    }

    private async Task<RecoveryCheckpointCreationResult?> ValidateGraphAsync(
        RecoveryCheckpointCreationRequest request,
        PlanningExecutionContract contract,
        CancellationToken cancellationToken)
    {
        if (request.WorkGraphReference is null)
        {
            if (request.WorkGraphNodeId is not null)
            {
                return new(RecoveryCheckpointCreationStatus.GraphNodeMismatch, ErrorMessage: "A work-graph node requires an exact graph reference.");
            }

            return null;
        }

        var read = await _graphs.GetAsync(request.ProjectId, request.WorkGraphReference.GraphId, cancellationToken).ConfigureAwait(false);
        if (read.State == WorkGraphReadState.Missing)
        {
            return new(RecoveryCheckpointCreationStatus.GraphMissing, ErrorMessage: "The exact work graph is missing.");
        }

        if (read.State == WorkGraphReadState.Unavailable)
        {
            return new(RecoveryCheckpointCreationStatus.PersistenceUnavailable, ErrorMessage: "Work-graph persistence is unavailable.");
        }

        if (!read.IsValid || read.Graph is null)
        {
            return new(RecoveryCheckpointCreationStatus.GraphInvalid, ErrorMessage: "The exact work graph is not valid.");
        }

        var graph = read.Graph;
        if (graph.ProjectId != request.ProjectId || !SameGraphReference(graph.Reference, request.WorkGraphReference))
        {
            return new(RecoveryCheckpointCreationStatus.GraphInvalid, ErrorMessage: "The work graph does not match its exact reference.");
        }

        if (request.WorkGraphNodeId is null)
        {
            return new(RecoveryCheckpointCreationStatus.GraphNodeMismatch, ErrorMessage: "An exact work-graph node is required when a graph is bound.");
        }

        var node = graph.Nodes.FirstOrDefault(value => value.NodeId == request.WorkGraphNodeId.Value);
        if (node is null || !SameContractReference(node.ContractReference, contract.Reference))
        {
            return new(RecoveryCheckpointCreationStatus.GraphNodeMismatch, ErrorMessage: "The work-graph node does not bind to the exact planning contract.");
        }

        return null;
    }

    private async Task<RecoveryCheckpointCreationResult?> ValidateHandoffAsync(
        RecoveryCheckpointCreationRequest request,
        PlanningExecutionContract contract,
        CancellationToken cancellationToken)
    {
        if (request.HandoffPackageReference is null)
        {
            return null;
        }

        var read = await _handoffs.GetAsync(
                request.ProjectId,
                request.HandoffPackageReference.PackageId,
                cancellationToken)
            .ConfigureAwait(false);
        if (read.State == HandoffPackageReadState.Missing)
        {
            return new(RecoveryCheckpointCreationStatus.HandoffMissing, ErrorMessage: "The exact handoff package is missing.");
        }

        if (read.State == HandoffPackageReadState.Unavailable)
        {
            return new(RecoveryCheckpointCreationStatus.PersistenceUnavailable, ErrorMessage: "Handoff persistence is unavailable.");
        }

        if (!read.IsValid || read.Package is null)
        {
            return new(RecoveryCheckpointCreationStatus.HandoffInvalid, ErrorMessage: "The exact handoff package is not valid.");
        }

        var package = read.Package;
        if (package.ProjectId != request.ProjectId ||
            !SameHandoffReference(package.Reference, request.HandoffPackageReference) ||
            !SameContractReference(package.PlanningContractReference, contract.Reference) ||
            package.Context.ContextId != contract.Context.ProjectContextId ||
            package.Context.ContextContractVersion != contract.Context.ProjectContextContractVersion ||
            (package.WorkGraphReference is not null) != (request.WorkGraphReference is not null) ||
            (package.WorkGraphReference is not null && request.WorkGraphReference is not null &&
             !SameGraphReference(package.WorkGraphReference, request.WorkGraphReference)) ||
            (package.WorkGraphNodeId != request.WorkGraphNodeId))
        {
            return new(RecoveryCheckpointCreationStatus.HandoffMismatch, ErrorMessage: "The handoff package does not match the checkpoint authorities.");
        }

        return null;
    }

    private async Task<RecoveryCheckpointCreationResult?> ValidateRoutingAsync(
        RecoveryCheckpointCreationRequest request,
        PlanningExecutionContract contract,
        ProjectContextReference context,
        CancellationToken cancellationToken)
    {
        if (request.RoutingDecisionReference is null)
        {
            return null;
        }

        var read = await _routing.GetAsync(request.ProjectId, request.RoutingDecisionReference.DecisionId, cancellationToken)
            .ConfigureAwait(false);
        if (read.State == RoutingDecisionReadState.Missing)
        {
            return new(RecoveryCheckpointCreationStatus.RoutingMissing, ErrorMessage: "The exact routing decision is missing.");
        }

        if (!read.IsValid || read.Decision is null)
        {
            return new(read.State == RoutingDecisionReadState.Unavailable
                ? RecoveryCheckpointCreationStatus.PersistenceUnavailable
                : RecoveryCheckpointCreationStatus.RoutingInvalid, ErrorMessage: "The exact routing decision is not valid.");
        }

        var decision = read.Decision;
        var contextIdentity = new WorkspaceContextIdentity(context.ProjectId, context.ContextId, context.ContractVersion, context.UpdatedAt);
        if (decision.ProjectId != request.ProjectId ||
            !SameRoutingReference(decision.Reference, request.RoutingDecisionReference) ||
            !WorkspacePreparationPlanningService.IsUsableRoutingDecision(decision, request.ProjectId, contract.Reference, contextIdentity))
        {
            return new(RecoveryCheckpointCreationStatus.RoutingMismatch, ErrorMessage: "The routing decision is not the exact usable decision for this checkpoint.");
        }

        return null;
    }

    private async Task<RecoveryCheckpointCreationResult?> ValidateWorkspacePlanAsync(
        RecoveryCheckpointCreationRequest request,
        PlanningExecutionContract contract,
        CancellationToken cancellationToken)
    {
        if (request.WorkspacePreparationPlanReference is null)
        {
            return null;
        }

        var read = await _workspacePlans.GetAsync(request.ProjectId, request.WorkspacePreparationPlanReference.PlanId, cancellationToken)
            .ConfigureAwait(false);
        if (read.State == WorkspacePreparationPlanReadState.Missing)
        {
            return new(RecoveryCheckpointCreationStatus.WorkspacePlanMissing, ErrorMessage: "The exact workspace preparation plan is missing.");
        }

        if (read.State != WorkspacePreparationPlanReadState.Valid || read.Plan is null)
        {
            return new(read.State == WorkspacePreparationPlanReadState.Unavailable
                ? RecoveryCheckpointCreationStatus.PersistenceUnavailable
                : RecoveryCheckpointCreationStatus.WorkspacePlanInvalid, ErrorMessage: "The exact workspace preparation plan is not valid.");
        }

        var plan = read.Plan;
        if (plan.ProjectId != request.ProjectId ||
            !SameWorkspacePlanReference(plan.Reference, request.WorkspacePreparationPlanReference) ||
            !SameContractReference(plan.ContractReference, contract.Reference) ||
            (plan.WorkGraphReference is not null) != (request.WorkGraphReference is not null) ||
            (plan.WorkGraphReference is not null && request.WorkGraphReference is not null &&
             !SameGraphReference(plan.WorkGraphReference, request.WorkGraphReference)) ||
            plan.WorkGraphNodeId != request.WorkGraphNodeId ||
            !SameRoutingReference(plan.RoutingDecisionReference, request.RoutingDecisionReference))
        {
            return new(RecoveryCheckpointCreationStatus.WorkspacePlanMismatch, ErrorMessage: "The workspace preparation plan does not bind to the checkpoint authorities.");
        }

        return null;
    }

    private async Task<RecoveryCheckpointCreationResult?> ValidatePredecessorAsync(
        RecoveryCheckpointCreationRequest request,
        CancellationToken cancellationToken)
    {
        var reference = request.PreviousCheckpointReference;
        if (reference is null)
        {
            return null;
        }

        if (reference.CheckpointId == request.CheckpointId)
        {
            return new(RecoveryCheckpointCreationStatus.InvalidLineage, ErrorMessage: "A checkpoint cannot reference itself.");
        }

        var read = await _checkpoints.GetAsync(request.ProjectId, reference.CheckpointId, cancellationToken).ConfigureAwait(false);
        if (read.State == RecoveryCheckpointReadState.Missing)
        {
            return new(RecoveryCheckpointCreationStatus.PredecessorMissing, ErrorMessage: "The exact predecessor checkpoint is missing.");
        }

        if (read.State == RecoveryCheckpointReadState.Unavailable)
        {
            return new(RecoveryCheckpointCreationStatus.PersistenceUnavailable, ErrorMessage: "Recovery checkpoint persistence is unavailable.");
        }

        if (!read.IsValid || read.Checkpoint is null)
        {
            return new(RecoveryCheckpointCreationStatus.PredecessorInvalid, ErrorMessage: "The exact predecessor checkpoint is not valid.");
        }

        if (read.Checkpoint.ProjectId != request.ProjectId || !SameCheckpointReference(read.Checkpoint.Reference, reference))
        {
            return new(RecoveryCheckpointCreationStatus.InvalidLineage, ErrorMessage: "The predecessor checkpoint does not match its exact reference.");
        }

        return null;
    }

    private RecoveryCheckpoint BuildCheckpoint(
        RecoveryCheckpointCreationRequest request,
        ProjectContextReference context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var redactedExplanation = request.Explanation is null ? null : RedactDescription(request.Explanation);
        var redactedBlockers = new List<RecoveryBlocker>(request.Blockers.Count);
        foreach (var blocker in request.Blockers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (blocker is null)
            {
                throw new ArgumentException("Blockers cannot contain null entries.", nameof(request));
            }

            ValidateAuthorityText(blocker.BlockerId);
            ValidateAuthorityText(blocker.Reference);
            redactedBlockers.Add(new RecoveryBlocker(
                blocker.BlockerId,
                blocker.Kind,
                RedactDescription(blocker.Description),
                blocker.Reference,
                blocker.OwnerActionRequired));
        }

        foreach (var evidence in request.EvidenceReferences)
        {
            if (evidence is null)
            {
                throw new ArgumentException("Evidence references cannot contain null entries.", nameof(request));
            }

            ValidateAuthorityText(evidence.Reference);
        }

        foreach (var agent in request.SelectedAgentRoleReferences)
        {
            if (agent is null)
            {
                throw new ArgumentException("Agent-role references cannot contain null entries.", nameof(request));
            }

            ValidateAuthorityText(agent.SelectionEvidenceReference);
        }

        var evidenceIds = request.EvidenceReferences.Select(static value => value.EvidenceId).ToHashSet();
        foreach (var gate in request.GateSnapshots)
        {
            if (gate is null || gate.SupportingEvidenceIds.Any(id => !evidenceIds.Contains(id)))
            {
                throw new ArgumentException("Gate evidence must reference evidence carried by the checkpoint.", nameof(request));
            }
        }

        return new RecoveryCheckpoint(
            request.ProjectId,
            request.CheckpointId,
            RecoveryCheckpointSchema.CurrentVersion,
            request.CreatedAt ?? _clock.UtcNow,
            request.LifecycleState,
            new RecoveryContextReference(context.ContextId, context.ContractVersion, context.UpdatedAt),
            request.PlanningContractReference,
            request.WorkGraphReference,
            request.WorkGraphNodeId,
            request.HandoffPackageReference,
            request.RoutingDecisionReference,
            request.WorkspacePreparationPlanReference,
            request.PreviousCheckpointReference,
            request.SelectedAgentRoleReferences,
            request.EvidenceReferences,
            request.GateSnapshots,
            redactedBlockers,
            request.NextSafeAction,
            redactedExplanation);
    }

    private void ValidateAuthorityText(string? value)
    {
        if (value is not null && _redaction.ValidateIdentityText(value).RequiresRedaction)
        {
            throw new RedactionRejectedException();
        }
    }

    private string RedactDescription(string value)
    {
        try
        {
            return _redaction.Redact(value).Value;
        }
        catch (ArgumentException exception)
        {
            throw new RedactionRejectedException("Checkpoint descriptive text is not valid.", exception);
        }
    }

    private static bool IsLastKnownSafe(RecoveryCheckpointLifecycleState state) => state is
        RecoveryCheckpointLifecycleState.Ready or
        RecoveryCheckpointLifecycleState.Waiting or
        RecoveryCheckpointLifecycleState.Blocked or
        RecoveryCheckpointLifecycleState.ApprovalRequired;

    private static bool SameContractReference(PlanningExecutionContractReference left, PlanningExecutionContractReference right) =>
        left.ContractId == right.ContractId &&
        left.Revision == right.Revision &&
        left.SchemaVersion == right.SchemaVersion &&
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static bool SameGraphReference(WorkGraphReference left, WorkGraphReference right) =>
        left.GraphId == right.GraphId &&
        left.SchemaVersion == right.SchemaVersion &&
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static bool SameHandoffReference(HandoffPackageReference left, HandoffPackageReference right) =>
        left.PackageId == right.PackageId &&
        left.SchemaVersion == right.SchemaVersion &&
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static bool SameCheckpointReference(RecoveryCheckpointReference left, RecoveryCheckpointReference right) =>
        left.CheckpointId == right.CheckpointId &&
        left.SchemaVersion == right.SchemaVersion &&
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static bool SameRoutingReference(RoutingDecisionReference? left, RoutingDecisionReference? right) =>
        (left is null && right is null) ||
        (left is not null && right is not null &&
         left.DecisionId == right.DecisionId &&
         left.SchemaVersion == right.SchemaVersion &&
         string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase));

    private static bool SameWorkspacePlanReference(WorkspacePreparationPlanReference? left, WorkspacePreparationPlanReference? right) =>
        left is not null && right is not null &&
        left.ProjectId == right.ProjectId &&
        left.PlanId == right.PlanId &&
        left.SchemaVersion == right.SchemaVersion &&
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static void ValidateRequestIdentity(RecoveryCheckpointCreationRequest request)
    {
        if (request.ProjectId == Guid.Empty)
        {
            throw new ArgumentException("Project id cannot be empty.", nameof(request));
        }

        if (request.CheckpointId == Guid.Empty)
        {
            throw new ArgumentException("Checkpoint id cannot be empty.", nameof(request));
        }

        if (!Enum.IsDefined(request.LifecycleState) || !Enum.IsDefined(request.NextSafeAction))
        {
            throw new ArgumentException("Checkpoint lifecycle or next-safe-action value is undefined.", nameof(request));
        }
    }

    private sealed class RedactionRejectedException : Exception
    {
        public RedactionRejectedException()
            : base("Checkpoint authority text crossed the redaction boundary.")
        {
        }

        public RedactionRejectedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

}

/// <summary>
/// Project-keyed publication gates with an evictable, reference-counted entry lifecycle.
/// Entry lookup and reference acquisition share one synchronization boundary so an acquire
/// cannot retain a detached entry while another caller retires and replaces it.
/// </summary>
internal sealed class ProjectPublicationLockManager
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Entry> _entries = [];

    internal int EntryCount
    {
        get
        {
            lock (_sync)
            {
                return _entries.Count;
            }
        }
    }

    public async Task<IDisposable> AcquireAsync(Guid projectId, CancellationToken cancellationToken)
    {
        Entry entry;
        lock (_sync)
        {
            if (!_entries.TryGetValue(projectId, out entry!))
            {
                entry = new Entry();
                _entries.Add(projectId, entry);
            }

            // This reference is acquired in the same critical section as dictionary lookup.
            // Retirement therefore cannot remove this entry between lookup and increment.
            entry.Users++;
        }

        try
        {
            await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new Lease(this, projectId, entry);
        }
        catch
        {
            ReleaseReference(projectId, entry);
            throw;
        }
    }

    private void Release(Guid projectId, Entry entry)
    {
        entry.Gate.Release();
        ReleaseReference(projectId, entry);
    }

    private void ReleaseReference(Guid projectId, Entry entry)
    {
        lock (_sync)
        {
            entry.Users--;
            if (entry.Users == 0 &&
                _entries.TryGetValue(projectId, out var current) &&
                ReferenceEquals(current, entry))
            {
                _entries.Remove(projectId);
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int Users { get; set; }
    }

    private sealed class Lease(ProjectPublicationLockManager owner, Guid projectId, Entry entry) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                owner.Release(projectId, entry);
            }
        }
    }
}

public sealed record SmartContinueResult
{
    public SmartContinueResult(
        SmartContinueResolutionState ResolutionState,
        Guid ProjectId,
        RecoveryCheckpointReference? LatestCheckpointReference = null,
        RecoveryCheckpointReference? SelectedCheckpointReference = null,
        bool FallbackToLastKnownGood = false,
        RecoveryCheckpointLifecycleState? LatestLifecycleState = null,
        IReadOnlyList<RecoveryBlocker>? Blockers = null,
        IReadOnlyList<RecoveryEvidenceReference>? StaleEvidence = null,
        RecoveryGateState? RequiredGateState = null,
        RecoveryNextSafeAction NextSafeAction = RecoveryNextSafeAction.InspectProjectContext,
        string Explanation = "")
    {
        this.ResolutionState = ResolutionState;
        this.ProjectId = ProjectId;
        this.LatestCheckpointReference = LatestCheckpointReference;
        this.SelectedCheckpointReference = SelectedCheckpointReference;
        this.FallbackToLastKnownGood = FallbackToLastKnownGood;
        this.LatestLifecycleState = LatestLifecycleState;
        this.Blockers = Blockers ?? Array.Empty<RecoveryBlocker>();
        this.StaleEvidence = StaleEvidence ?? Array.Empty<RecoveryEvidenceReference>();
        this.RequiredGateState = RequiredGateState;
        this.NextSafeAction = NextSafeAction;
        this.Explanation = Explanation;
    }

    public SmartContinueResolutionState ResolutionState { get; init; }
    public Guid ProjectId { get; init; }
    public RecoveryCheckpointReference? LatestCheckpointReference { get; init; }
    public RecoveryCheckpointReference? SelectedCheckpointReference { get; init; }
    public bool FallbackToLastKnownGood { get; init; }
    public RecoveryCheckpointLifecycleState? LatestLifecycleState { get; init; }
    public IReadOnlyList<RecoveryBlocker> Blockers { get; init; }
    public IReadOnlyList<RecoveryEvidenceReference> StaleEvidence { get; init; }
    public RecoveryGateState? RequiredGateState { get; init; }
    public RecoveryNextSafeAction NextSafeAction { get; init; }
    public string Explanation { get; init; }
}

public interface ISmartContinueResolver
{
    Task<SmartContinueResult> ResolveAsync(Guid projectId, CancellationToken cancellationToken = default);
}

/// <summary>Read-only durable recovery resolver. It never refreshes external evidence or writes state.</summary>
public sealed class SmartContinueResolver : ISmartContinueResolver
{
    private readonly IProjectRepository _projects;
    private readonly IProjectContextReferenceRepository _contexts;
    private readonly IPlanningExecutionContractRepository _contracts;
    private readonly IWorkGraphRepository _graphs;
    private readonly IHandoffPackageRepository _handoffs;
    private readonly IRoutingDecisionRepository _routing;
    private readonly IWorkspacePreparationPlanRepository _workspacePlans;
    private readonly IRecoveryCheckpointRepository _checkpoints;
    private readonly IContinuationHeadRepository _heads;
    private readonly IClock _clock;

    public SmartContinueResolver(
        IProjectRepository projects,
        IProjectContextReferenceRepository contexts,
        IPlanningExecutionContractRepository contracts,
        IWorkGraphRepository graphs,
        IHandoffPackageRepository handoffs,
        IRoutingDecisionRepository routing,
        IWorkspacePreparationPlanRepository workspacePlans,
        IRecoveryCheckpointRepository checkpoints,
        IContinuationHeadRepository heads,
        IClock clock)
    {
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
        _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
        _handoffs = handoffs ?? throw new ArgumentNullException(nameof(handoffs));
        _routing = routing ?? throw new ArgumentNullException(nameof(routing));
        _workspacePlans = workspacePlans ?? throw new ArgumentNullException(nameof(workspacePlans));
        _checkpoints = checkpoints ?? throw new ArgumentNullException(nameof(checkpoints));
        _heads = heads ?? throw new ArgumentNullException(nameof(heads));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<SmartContinueResult> ResolveAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project id cannot be empty.", nameof(projectId));
        }

        var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            return new(SmartContinueResolutionState.ProjectNotFound, projectId, Explanation: "Project was not found.");
        }

        var contextRead = await _contexts.GetAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (contextRead.State == ProjectContextReadState.UnsupportedVersion)
        {
            return new(SmartContinueResolutionState.UnsupportedVersion, projectId, NextSafeAction: RecoveryNextSafeAction.InspectProjectContext, Explanation: "The current project context uses a newer supported boundary.");
        }

        if (contextRead.State == ProjectContextReadState.Unavailable)
        {
            return new(SmartContinueResolutionState.Unavailable, projectId, NextSafeAction: RecoveryNextSafeAction.InspectProjectContext, Explanation: "The current project context is unavailable.");
        }

        if (contextRead.State != ProjectContextReadState.Valid || contextRead.Context is null ||
            contextRead.Context.ProjectId != projectId ||
            contextRead.Context.ContractVersion != ProjectContextContract.CurrentVersion)
        {
            return new(SmartContinueResolutionState.ContextInsufficient, projectId, NextSafeAction: RecoveryNextSafeAction.InspectProjectContext, Explanation: "A compatible current project context is not available.");
        }

        var headRead = await _heads.GetAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (headRead.State == ContinuationHeadReadState.Missing)
        {
            return new(SmartContinueResolutionState.CheckpointMissing, projectId, NextSafeAction: RecoveryNextSafeAction.Replan, Explanation: "No canonical continuation checkpoint has been published.");
        }

        if (headRead.State == ContinuationHeadReadState.UnsupportedFutureVersion)
        {
            return new(SmartContinueResolutionState.UnsupportedVersion, projectId, NextSafeAction: RecoveryNextSafeAction.InspectProjectContext, Explanation: "The continuation head uses a newer schema.");
        }

        if (headRead.State == ContinuationHeadReadState.MigrationRequired)
        {
            return new(SmartContinueResolutionState.MigrationRequired, projectId, NextSafeAction: RecoveryNextSafeAction.InspectProjectContext, Explanation: "The continuation head requires an explicit migration.");
        }

        if (headRead.State == ContinuationHeadReadState.Unavailable)
        {
            return new(SmartContinueResolutionState.Unavailable, projectId, NextSafeAction: RecoveryNextSafeAction.InspectProjectContext, Explanation: "The continuation head is unavailable.");
        }

        if (headRead.State != ContinuationHeadReadState.Valid || headRead.Head is null)
        {
            return new(SmartContinueResolutionState.IntegrityFailure, projectId, NextSafeAction: RecoveryNextSafeAction.InspectProjectContext, Explanation: "No trusted continuation head generation is available.");
        }

        var head = headRead.Head;
        var latestRead = await _checkpoints.GetAsync(projectId, head.LatestCheckpointReference.CheckpointId, cancellationToken).ConfigureAwait(false);
        if (!latestRead.IsValid || latestRead.Checkpoint is null ||
            !SameCheckpointReference(latestRead.Checkpoint.Reference, head.LatestCheckpointReference))
        {
            return MapCheckpointFailure(projectId, latestRead, head, "The canonical latest checkpoint is not trusted.");
        }

        var latest = latestRead.Checkpoint;
        var latestBase = new CandidateContext(
            projectId,
            head.LatestCheckpointReference,
            latest,
            headRead.FallbackToPreviousGeneration,
            latest.LifecycleState);

        if (latest.LifecycleState is RecoveryCheckpointLifecycleState.Interrupted or RecoveryCheckpointLifecycleState.Failed or RecoveryCheckpointLifecycleState.Cancelled)
        {
            if (head.LastSafeCheckpointReference is null)
            {
                return new(SmartContinueResolutionState.ContextInsufficient, projectId, head.LatestCheckpointReference, null, true, latest.LifecycleState, Explanation: $"The latest checkpoint is {latest.LifecycleState}, but no valid last-known-safe checkpoint exists.");
            }

            var safeRead = await _checkpoints.GetAsync(projectId, head.LastSafeCheckpointReference.CheckpointId, cancellationToken).ConfigureAwait(false);
            if (!safeRead.IsValid || safeRead.Checkpoint is null ||
                !SameCheckpointReference(safeRead.Checkpoint.Reference, head.LastSafeCheckpointReference))
            {
                return new(SmartContinueResolutionState.ContextInsufficient, projectId, head.LatestCheckpointReference, null, true, latest.LifecycleState, Explanation: $"The latest checkpoint is {latest.LifecycleState}, but its last-known-safe checkpoint is not valid.");
            }

            var safe = safeRead.Checkpoint;
            var authorityFailure = await ValidateAuthorityAsync(safe, contextRead.Context, cancellationToken).ConfigureAwait(false);
            if (authorityFailure is not null)
            {
                return new(SmartContinueResolutionState.ContextInsufficient, projectId, head.LatestCheckpointReference, safe.Reference, true, latest.LifecycleState, Explanation: authorityFailure);
            }

            var recovered = ResolveCandidate(safe, latestBase, latest.LifecycleState, latest.LifecycleState == RecoveryCheckpointLifecycleState.Cancelled);
            return recovered with
            {
                FallbackToLastKnownGood = true,
                Explanation = $"Latest checkpoint was {latest.LifecycleState}; using the last known safe checkpoint. {recovered.Explanation}"
            };
        }

        var latestAuthorityFailure = await ValidateAuthorityAsync(latest, contextRead.Context, cancellationToken).ConfigureAwait(false);
        if (latestAuthorityFailure is not null)
        {
            return new(SmartContinueResolutionState.ContextInsufficient, projectId, head.LatestCheckpointReference, head.LatestCheckpointReference, headRead.FallbackToPreviousGeneration, latest.LifecycleState, Explanation: latestAuthorityFailure);
        }

        return ResolveCandidate(latest, latestBase, latest.LifecycleState, false) with
        {
            FallbackToLastKnownGood = headRead.FallbackToPreviousGeneration
        };
    }

    private async Task<string?> ValidateAuthorityAsync(
        RecoveryCheckpoint checkpoint,
        ProjectContextReference currentContext,
        CancellationToken cancellationToken)
    {
        if (checkpoint.ProjectId != currentContext.ProjectId ||
            checkpoint.Context.ContextId != currentContext.ContextId ||
            checkpoint.Context.ContextContractVersion != currentContext.ContractVersion ||
            checkpoint.Context.ContextUpdatedAt != currentContext.UpdatedAt)
        {
            return "The checkpoint context identity or update version does not match the current project context.";
        }

        var contractRead = await _contracts.GetAsync(
                checkpoint.ProjectId,
                checkpoint.PlanningContractReference.ContractId,
                checkpoint.PlanningContractReference.Revision,
                cancellationToken)
            .ConfigureAwait(false);
        if (!contractRead.IsValid || contractRead.Contract is null)
        {
            return "The exact planning contract bound to the checkpoint is not available.";
        }

        var contract = contractRead.Contract;
        if (contract.ProjectId != checkpoint.ProjectId ||
            !SameContractReference(contract.Reference, checkpoint.PlanningContractReference) ||
            contract.Context.ProjectContextId != checkpoint.Context.ContextId ||
            contract.Context.ProjectContextContractVersion != checkpoint.Context.ContextContractVersion)
        {
            return "The checkpoint's exact planning contract binding is incompatible.";
        }

        if (checkpoint.WorkGraphReference is not null)
        {
            var graphRead = await _graphs.GetAsync(checkpoint.ProjectId, checkpoint.WorkGraphReference.GraphId, cancellationToken).ConfigureAwait(false);
            if (!graphRead.IsValid || graphRead.Graph is null ||
                graphRead.Graph.ProjectId != checkpoint.ProjectId ||
                !SameGraphReference(graphRead.Graph.Reference, checkpoint.WorkGraphReference))
            {
                return "The exact work graph bound to the checkpoint is not valid.";
            }

            var node = graphRead.Graph.Nodes.FirstOrDefault(value => value.NodeId == checkpoint.WorkGraphNodeId);
            if (node is null || !SameContractReference(node.ContractReference, checkpoint.PlanningContractReference))
            {
                return "The exact work-graph node does not bind to the checkpoint contract.";
            }
        }

        if (checkpoint.HandoffPackageReference is not null)
        {
            var handoffRead = await _handoffs.GetAsync(checkpoint.ProjectId, checkpoint.HandoffPackageReference.PackageId, cancellationToken).ConfigureAwait(false);
            if (!handoffRead.IsValid || handoffRead.Package is null ||
                handoffRead.Package.ProjectId != checkpoint.ProjectId ||
                !SameHandoffReference(handoffRead.Package.Reference, checkpoint.HandoffPackageReference) ||
                !SameContractReference(handoffRead.Package.PlanningContractReference, checkpoint.PlanningContractReference) ||
                handoffRead.Package.Context.ContextId != checkpoint.Context.ContextId ||
                handoffRead.Package.Context.ContextContractVersion != checkpoint.Context.ContextContractVersion ||
                (handoffRead.Package.Context.UpdatedAt is not null &&
                 handoffRead.Package.Context.UpdatedAt != checkpoint.Context.ContextUpdatedAt) ||
                (handoffRead.Package.WorkGraphReference is not null) != (checkpoint.WorkGraphReference is not null) ||
                (handoffRead.Package.WorkGraphReference is not null && checkpoint.WorkGraphReference is not null &&
                 !SameGraphReference(handoffRead.Package.WorkGraphReference, checkpoint.WorkGraphReference)) ||
                handoffRead.Package.WorkGraphNodeId != checkpoint.WorkGraphNodeId)
            {
                return "The exact handoff package bound to the checkpoint is not valid or compatible.";
            }
        }

        if (checkpoint.PreviousCheckpointReference is not null)
        {
            var predecessor = await _checkpoints.GetAsync(
                    checkpoint.ProjectId,
                    checkpoint.PreviousCheckpointReference.CheckpointId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!predecessor.IsValid || predecessor.Checkpoint is null ||
                !SameCheckpointReference(predecessor.Checkpoint.Reference, checkpoint.PreviousCheckpointReference))
            {
                return "The checkpoint predecessor authority is not valid.";
            }
        }

        if (checkpoint.RoutingDecisionReference is not null)
        {
            var routingRead = await _routing.GetAsync(checkpoint.ProjectId, checkpoint.RoutingDecisionReference.DecisionId, cancellationToken)
                .ConfigureAwait(false);
            var contextIdentity = new WorkspaceContextIdentity(
                currentContext.ProjectId,
                currentContext.ContextId,
                currentContext.ContractVersion,
                currentContext.UpdatedAt);
            if (!routingRead.IsValid || routingRead.Decision is null ||
                routingRead.Decision.ProjectId != checkpoint.ProjectId ||
                !SameRoutingReference(routingRead.Decision.Reference, checkpoint.RoutingDecisionReference) ||
                !WorkspacePreparationPlanningService.IsUsableRoutingDecision(
                    routingRead.Decision, checkpoint.ProjectId, checkpoint.PlanningContractReference, contextIdentity))
            {
                return "The exact routing decision bound to the checkpoint is not valid or usable against the current context.";
            }
        }

        if (checkpoint.WorkspacePreparationPlanReference is not null)
        {
            var planRead = await _workspacePlans.GetAsync(
                    checkpoint.ProjectId,
                    checkpoint.WorkspacePreparationPlanReference.PlanId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (planRead.State != WorkspacePreparationPlanReadState.Valid || planRead.Plan is null ||
                planRead.Plan.ProjectId != checkpoint.ProjectId ||
                !SameWorkspacePlanReference(planRead.Plan.Reference, checkpoint.WorkspacePreparationPlanReference) ||
                !SameContractReference(planRead.Plan.ContractReference, checkpoint.PlanningContractReference) ||
                (planRead.Plan.WorkGraphReference is not null) != (checkpoint.WorkGraphReference is not null) ||
                (planRead.Plan.WorkGraphReference is not null && checkpoint.WorkGraphReference is not null &&
                 !SameGraphReference(planRead.Plan.WorkGraphReference, checkpoint.WorkGraphReference)) ||
                planRead.Plan.WorkGraphNodeId != checkpoint.WorkGraphNodeId ||
                !SameRoutingReference(planRead.Plan.RoutingDecisionReference, checkpoint.RoutingDecisionReference))
            {
                return "The exact workspace preparation plan bound to the checkpoint is not valid or compatible.";
            }
        }

        return null;
    }

    private SmartContinueResult ResolveCandidate(
        RecoveryCheckpoint candidate,
        CandidateContext latestContext,
        RecoveryCheckpointLifecycleState latestLifecycleState,
        bool cancelled)
    {
        var blockers = candidate.Blockers.ToArray();
        if (candidate.LifecycleState == RecoveryCheckpointLifecycleState.Completed)
        {
            return new(
                SmartContinueResolutionState.Completed,
                candidate.ProjectId,
                latestContext.Reference,
                candidate.Reference,
                latestContext.Fallback,
                latestLifecycleState,
                blockers,
                NextSafeAction: RecoveryNextSafeAction.NoActionCompleted,
                Explanation: "The canonical checkpoint is completed; no older checkpoint will be resumed.");
        }

        if (candidate.LifecycleState == RecoveryCheckpointLifecycleState.Waiting)
        {
            return new(
                SmartContinueResolutionState.Blocked,
                candidate.ProjectId,
                latestContext.Reference,
                candidate.Reference,
                latestContext.Fallback,
                latestLifecycleState,
                blockers,
                NextSafeAction: RecoveryNextSafeAction.ResolveBlocker,
                Explanation: candidate.Explanation ?? "The checkpoint is waiting for an external condition.");
        }

        if (candidate.LifecycleState == RecoveryCheckpointLifecycleState.Blocked)
        {
            return new(
                SmartContinueResolutionState.Blocked,
                candidate.ProjectId,
                latestContext.Reference,
                candidate.Reference,
                latestContext.Fallback,
                latestLifecycleState,
                blockers,
                NextSafeAction: RecoveryNextSafeAction.ResolveBlocker,
                Explanation: candidate.Explanation ?? "The checkpoint is blocked.");
        }

        if (candidate.LifecycleState == RecoveryCheckpointLifecycleState.ApprovalRequired)
        {
            return ApprovalRequired(candidate, latestContext, latestLifecycleState, blockers, "The checkpoint requires owner approval.");
        }

        if (blockers.Length > 0)
        {
            return new(
                SmartContinueResolutionState.Blocked,
                candidate.ProjectId,
                latestContext.Reference,
                candidate.Reference,
                latestContext.Fallback,
                latestLifecycleState,
                blockers,
                NextSafeAction: RecoveryNextSafeAction.ResolveBlocker,
                Explanation: candidate.Explanation ?? "A checkpoint blocker must be resolved before continuation.");
        }

        foreach (var gate in candidate.GateSnapshots)
        {
            if (gate.State is RecoveryGateState.Pending or RecoveryGateState.Unknown)
            {
                return gate.Kind == RecoveryGateKind.Approval
                    ? ApprovalRequired(candidate, latestContext, latestLifecycleState, blockers, "Owner approval is pending or unknown.", gate.State)
                    : new(
                        SmartContinueResolutionState.Blocked,
                        candidate.ProjectId,
                        latestContext.Reference,
                        candidate.Reference,
                        latestContext.Fallback,
                        latestLifecycleState,
                         blockers,
                         RequiredGateState: gate.State,
                         NextSafeAction: gate.Kind == RecoveryGateKind.Validation ? RecoveryNextSafeAction.RunValidation : RecoveryNextSafeAction.RequestReview,
                         Explanation: gate.Kind == RecoveryGateKind.Validation ? "Validation evidence is pending or unknown." : "Independent review evidence is pending or unknown.");
            }

            if (gate.State == RecoveryGateState.Failed)
            {
                return new(
                    SmartContinueResolutionState.Blocked,
                    candidate.ProjectId,
                    latestContext.Reference,
                    candidate.Reference,
                    latestContext.Fallback,
                    latestLifecycleState,
                     blockers,
                     RequiredGateState: gate.State,
                     NextSafeAction: gate.Kind == RecoveryGateKind.Validation ? RecoveryNextSafeAction.RunValidation :
                     gate.Kind == RecoveryGateKind.Review ? RecoveryNextSafeAction.RequestReview : RecoveryNextSafeAction.RequestApproval,
                     Explanation: $"The {gate.Kind.ToString().ToLowerInvariant()} gate failed.");
            }
        }

        var stale = GetStaleEvidence(candidate, _clock.UtcNow);
        if (stale.Count > 0)
        {
            var firstKind = stale[0].Kind;
            var action = firstKind switch
            {
                RecoveryEvidenceKind.Repository => RecoveryNextSafeAction.RefreshRepositoryEvidence,
                RecoveryEvidenceKind.Tracker => RecoveryNextSafeAction.RefreshTrackerEvidence,
                RecoveryEvidenceKind.Routing => RecoveryNextSafeAction.RefreshRoutingEvidence,
                RecoveryEvidenceKind.Validation => RecoveryNextSafeAction.RunValidation,
                RecoveryEvidenceKind.Review => RecoveryNextSafeAction.RequestReview,
                RecoveryEvidenceKind.Approval => RecoveryNextSafeAction.RequestApproval,
                _ => RecoveryNextSafeAction.ResolveBlocker
            };
            var state = firstKind == RecoveryEvidenceKind.Approval
                ? SmartContinueResolutionState.ApprovalRequired
                : SmartContinueResolutionState.Stale;
            return new(
                state,
                candidate.ProjectId,
                latestContext.Reference,
                candidate.Reference,
                latestContext.Fallback,
                latestLifecycleState,
                blockers,
                stale,
                firstKind == RecoveryEvidenceKind.Approval ? RecoveryGateState.Pending : null,
                action,
                cancelled
                    ? "The latest checkpoint was cancelled; the last safe checkpoint is only a recovery candidate and its evidence must be refreshed before any decision."
                    : "Required external evidence is stale or point-in-time; refresh it before continuation.");
        }

        return new(
            cancelled ? SmartContinueResolutionState.Blocked : SmartContinueResolutionState.Resumable,
            candidate.ProjectId,
            latestContext.Reference,
            candidate.Reference,
            latestContext.Fallback,
            latestLifecycleState,
            blockers,
            NextSafeAction: cancelled ? RecoveryNextSafeAction.ResolveBlocker : RecoveryNextSafeAction.ContinueFromCheckpoint,
            Explanation: cancelled
                ? "The latest checkpoint was cancelled; the last safe checkpoint is a recovery candidate and will not be silently restarted."
                : candidate.Explanation ?? "The checkpoint is safe to continue from.");
    }

    private static SmartContinueResult ApprovalRequired(
        RecoveryCheckpoint candidate,
        CandidateContext latestContext,
        RecoveryCheckpointLifecycleState latestLifecycleState,
        IReadOnlyList<RecoveryBlocker> blockers,
        string explanation,
        RecoveryGateState? state = null) => new(
        SmartContinueResolutionState.ApprovalRequired,
        candidate.ProjectId,
        latestContext.Reference,
        candidate.Reference,
        latestContext.Fallback,
        latestLifecycleState,
        blockers,
        RequiredGateState: state,
        NextSafeAction: RecoveryNextSafeAction.RequestApproval,
        Explanation: explanation);

    private static List<RecoveryEvidenceReference> GetStaleEvidence(RecoveryCheckpoint checkpoint, DateTimeOffset now)
    {
        var stale = new List<RecoveryEvidenceReference>();
        foreach (var evidence in checkpoint.EvidenceReferences)
        {
            var mutable = evidence.Kind is RecoveryEvidenceKind.Repository or
                RecoveryEvidenceKind.Tracker or
                RecoveryEvidenceKind.Routing or
                RecoveryEvidenceKind.Validation or
                RecoveryEvidenceKind.Approval;
            var expired = evidence.ValidUntil is not null && now > evidence.ValidUntil.Value;
            if ((mutable && (evidence.Freshness != RecoveryEvidenceFreshness.Verified || evidence.ObservedAt is null || expired)) ||
                (!mutable && evidence.Freshness == RecoveryEvidenceFreshness.Stale) ||
                expired)
            {
                stale.Add(evidence);
            }
        }

        foreach (var gate in checkpoint.GateSnapshots.Where(static value => value.State == RecoveryGateState.Satisfied))
        {
            foreach (var evidenceId in gate.SupportingEvidenceIds)
            {
                var evidence = checkpoint.EvidenceReferences.FirstOrDefault(value => value.EvidenceId == evidenceId);
                if (evidence is not null && (evidence.Freshness != RecoveryEvidenceFreshness.Verified || evidence.ObservedAt is null || (evidence.ValidUntil is not null && now > evidence.ValidUntil.Value)))
                {
                    if (!stale.Contains(evidence))
                    {
                        stale.Add(evidence);
                    }
                }
            }
        }

        return stale;
    }

    private static SmartContinueResult MapCheckpointFailure(
        Guid projectId,
        RecoveryCheckpointReadResult read,
        ContinuationHead head,
        string explanation) => read.State switch
        {
            RecoveryCheckpointReadState.Missing => new(SmartContinueResolutionState.CheckpointMissing, projectId, head.LatestCheckpointReference, Explanation: explanation),
            RecoveryCheckpointReadState.UnsupportedFutureVersion => new(SmartContinueResolutionState.UnsupportedVersion, projectId, head.LatestCheckpointReference, Explanation: explanation),
            RecoveryCheckpointReadState.MigrationRequired => new(SmartContinueResolutionState.MigrationRequired, projectId, head.LatestCheckpointReference, Explanation: explanation),
            RecoveryCheckpointReadState.Unavailable => new(SmartContinueResolutionState.Unavailable, projectId, head.LatestCheckpointReference, Explanation: explanation),
            _ => new(SmartContinueResolutionState.IntegrityFailure, projectId, head.LatestCheckpointReference, Explanation: explanation)
        };

    private sealed record CandidateContext(
        Guid ProjectId,
        RecoveryCheckpointReference Reference,
        RecoveryCheckpoint Checkpoint,
        bool Fallback,
        RecoveryCheckpointLifecycleState LifecycleState);

    private static bool SameContractReference(PlanningExecutionContractReference left, PlanningExecutionContractReference right) =>
        left.ContractId == right.ContractId && left.Revision == right.Revision && left.SchemaVersion == right.SchemaVersion &&
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static bool SameGraphReference(WorkGraphReference left, WorkGraphReference right) =>
        left.GraphId == right.GraphId && left.SchemaVersion == right.SchemaVersion &&
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static bool SameHandoffReference(HandoffPackageReference left, HandoffPackageReference right) =>
        left.PackageId == right.PackageId && left.SchemaVersion == right.SchemaVersion &&
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static bool SameCheckpointReference(RecoveryCheckpointReference left, RecoveryCheckpointReference right) =>
        left.CheckpointId == right.CheckpointId && left.SchemaVersion == right.SchemaVersion &&
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static bool SameRoutingReference(RoutingDecisionReference? left, RoutingDecisionReference? right) =>
        (left is null && right is null) ||
        (left is not null && right is not null &&
         left.DecisionId == right.DecisionId && left.SchemaVersion == right.SchemaVersion &&
         string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase));

    private static bool SameWorkspacePlanReference(WorkspacePreparationPlanReference? left, WorkspacePreparationPlanReference? right) =>
        left is not null && right is not null &&
        left.ProjectId == right.ProjectId && left.PlanId == right.PlanId && left.SchemaVersion == right.SchemaVersion &&
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);
}
