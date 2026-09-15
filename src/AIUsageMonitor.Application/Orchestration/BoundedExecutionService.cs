using System.Collections.Concurrent;
using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Routing;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Application.Workspaces;

namespace AIUsageMonitor.Application.Orchestration;

public sealed class BoundedExecutionRequest
{
    public BoundedExecutionRequest(
        Guid projectId,
        Guid runId,
        PlanningExecutionContractReference planningContractReference,
        WorkGraphReference workGraphReference,
        Guid workGraphNodeId,
        HandoffPackageReference handoffPackageReference,
        RoutingDecisionReference routingDecisionReference,
        WorkspacePreparationPlanReference workspacePreparationPlanReference,
        RecoveryCheckpointReference currentRecoveryCheckpointReference)
    {
        ProjectId = projectId;
        RunId = runId;
        PlanningContractReference = planningContractReference ?? throw new ArgumentNullException(nameof(planningContractReference));
        WorkGraphReference = workGraphReference ?? throw new ArgumentNullException(nameof(workGraphReference));
        WorkGraphNodeId = workGraphNodeId;
        HandoffPackageReference = handoffPackageReference ?? throw new ArgumentNullException(nameof(handoffPackageReference));
        RoutingDecisionReference = routingDecisionReference ?? throw new ArgumentNullException(nameof(routingDecisionReference));
        WorkspacePreparationPlanReference = workspacePreparationPlanReference ?? throw new ArgumentNullException(nameof(workspacePreparationPlanReference));
        CurrentRecoveryCheckpointReference = currentRecoveryCheckpointReference ?? throw new ArgumentNullException(nameof(currentRecoveryCheckpointReference));
    }

    public Guid ProjectId { get; }
    public Guid RunId { get; }
    public PlanningExecutionContractReference PlanningContractReference { get; }
    public WorkGraphReference WorkGraphReference { get; }
    public Guid WorkGraphNodeId { get; }
    public HandoffPackageReference HandoffPackageReference { get; }
    public RoutingDecisionReference RoutingDecisionReference { get; }
    public WorkspacePreparationPlanReference WorkspacePreparationPlanReference { get; }
    public RecoveryCheckpointReference CurrentRecoveryCheckpointReference { get; }
}

public enum BoundedExecutionStatus
{
    Succeeded,
    InvalidRequest,
    ProjectNotFound,
    ContextUnavailable,
    ContextStale,
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
    AgentMissing,
    AgentMismatch,
    AgentUnavailable,
    ConnectionUnsupported,
    AdapterUnsupported,
    AdapterConfigurationConflict,
    BudgetInvalid,
    WorkspacePlanMissing,
    WorkspacePlanInvalid,
    WorkspacePlanMismatch,
    WorkspaceNotPrepared,
    WorkspaceConflict,
    CheckpointMissing,
    CheckpointInvalid,
    CheckpointNotCurrent,
    CheckpointBlocked,
    CheckpointApprovalRequired,
    CheckpointCompleted,
    PersistenceUnavailable,
    AuthorityConflict,
    AlreadyStarted,
    PreRunCheckpointFailed,
    RunningHistoryFailed,
    AdapterFailed,
    Cancelled,
    TimedOut,
    ResidualExecutionActive,
    BudgetExceeded,
    EvidenceCapacityExceeded,
    EvidenceConflict,
    TerminalCheckpointFailed,
    AuditPersistenceFailed,
    ProjectBusy,
    ProjectNotExecutable
}

public sealed record BoundedExecutionResult(
    BoundedExecutionStatus Status,
    ExecutionRunAuthority? Authority = null,
    ExecutionAdapterResult? AdapterResult = null,
    RecoveryCheckpoint? TerminalCheckpoint = null,
    string? ErrorMessage = null,
    int AdapterInvocationCount = 0)
{
    public bool Succeeded => Status == BoundedExecutionStatus.Succeeded;
    public bool RecoveryRequired => Status is BoundedExecutionStatus.AlreadyStarted or BoundedExecutionStatus.ResidualExecutionActive;
}

public interface IBoundedExecutionService
{
    Task<BoundedExecutionResult> ExecuteAsync(
        BoundedExecutionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IExecutionBudgetTimeoutProvider
{
    TimeSpan GetTimeout(ExecutionBudgetEnvelope budgets);
}

public interface IExecutionInvocationGate
{
    /// <summary>Runs immediately before the one permitted adapter method call.</summary>
    void BeforeAdapterInvocation();
}

public interface IBoundedExecutionTiming
{
    TimeSpan FinalizationTimeout { get; }
    TimeSpan AdapterCancellationDrainTimeout { get; }
}

public sealed class DefaultBoundedExecutionTiming : IBoundedExecutionTiming
{
    public TimeSpan FinalizationTimeout => TimeSpan.FromSeconds(5);
    public TimeSpan AdapterCancellationDrainTimeout => TimeSpan.FromSeconds(2);
}

public static class BoundedExecutionLimits
{
    public const long MaxElapsedMinutes = 240;
    public static readonly TimeSpan MaxExecutionTimeout = TimeSpan.FromMinutes(MaxElapsedMinutes);
}

public sealed class ExecutionBudgetTimeoutProvider : IExecutionBudgetTimeoutProvider
{
    public TimeSpan GetTimeout(ExecutionBudgetEnvelope budgets)
    {
        ArgumentNullException.ThrowIfNull(budgets);
        return TimeSpan.FromMinutes(Math.Min(budgets.ElapsedMinutes, BoundedExecutionLimits.MaxElapsedMinutes));
    }
}

/// <summary>
/// Executes exactly one bounded adapter call after resolving every durable APO authority. It
/// never retries, schedules another node, switches models, or turns validation requirements into
/// commands.
/// </summary>
public sealed class BoundedExecutionService : IBoundedExecutionService
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> ProjectLocks = new();
    private static readonly ConcurrentDictionary<Guid, ResidualExecution> ResidualExecutions = new();

    private readonly IProjectContextResolver _contexts;
    private readonly IPlanningExecutionContractRepository _contracts;
    private readonly IWorkGraphRepository _graphs;
    private readonly IHandoffPackageRepository _handoffs;
    private readonly IRoutingDecisionRepository _routing;
    private readonly IAgentRegistryService _agents;
    private readonly IWorkspacePreparationPlanRepository _workspacePlans;
    private readonly IWorkspaceRecoveryInspectionService _workspaceInspection;
    private readonly IRecoveryCheckpointRepository _checkpoints;
    private readonly IContinuationHeadRepository _heads;
    private readonly IRecoveryCheckpointService _checkpointService;
    private readonly IExecutionRunAuthorityRepository _authorities;
    private readonly IExecutionAdapterResolver _adapterResolver;
    private readonly IProjectOrchestrationStore _history;
    private readonly IHandoffRedactionService _redaction;
    private readonly IClock _clock;
    private readonly IExecutionBudgetTimeoutProvider _timeoutProvider;
    private readonly IExecutionInvocationGate _invocationGate;
    private readonly IBoundedExecutionTiming _timing;

    public BoundedExecutionService(
        IProjectContextResolver contexts,
        IPlanningExecutionContractRepository contracts,
        IWorkGraphRepository graphs,
        IHandoffPackageRepository handoffs,
        IRoutingDecisionRepository routing,
        IAgentRegistryService agents,
        IWorkspacePreparationPlanRepository workspacePlans,
        IWorkspaceRecoveryInspectionService workspaceInspection,
        IRecoveryCheckpointRepository checkpoints,
        IContinuationHeadRepository heads,
        IRecoveryCheckpointService checkpointService,
        IExecutionRunAuthorityRepository authorities,
        IExecutionAdapterResolver adapterResolver,
        IProjectOrchestrationStore history,
        IHandoffRedactionService redaction,
        IClock clock,
        IExecutionBudgetTimeoutProvider? timeoutProvider = null,
        IExecutionInvocationGate? invocationGate = null,
        IBoundedExecutionTiming? timing = null)
    {
        _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
        _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
        _handoffs = handoffs ?? throw new ArgumentNullException(nameof(handoffs));
        _routing = routing ?? throw new ArgumentNullException(nameof(routing));
        _agents = agents ?? throw new ArgumentNullException(nameof(agents));
        _workspacePlans = workspacePlans ?? throw new ArgumentNullException(nameof(workspacePlans));
        _workspaceInspection = workspaceInspection ?? throw new ArgumentNullException(nameof(workspaceInspection));
        _checkpoints = checkpoints ?? throw new ArgumentNullException(nameof(checkpoints));
        _heads = heads ?? throw new ArgumentNullException(nameof(heads));
        _checkpointService = checkpointService ?? throw new ArgumentNullException(nameof(checkpointService));
        _authorities = authorities ?? throw new ArgumentNullException(nameof(authorities));
        _adapterResolver = adapterResolver ?? throw new ArgumentNullException(nameof(adapterResolver));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _redaction = redaction ?? throw new ArgumentNullException(nameof(redaction));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _timeoutProvider = timeoutProvider ?? new ExecutionBudgetTimeoutProvider();
        _invocationGate = invocationGate ?? NoOpExecutionInvocationGate.Instance;
        _timing = timing ?? new DefaultBoundedExecutionTiming();
        ValidateTiming(_timing);
    }

    public async Task<BoundedExecutionResult> ExecuteAsync(
        BoundedExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return new(BoundedExecutionStatus.InvalidRequest, ErrorMessage: "An execution request is required.");
        }

        if (!ValidateRequest(request, out var requestError))
        {
            return new(BoundedExecutionStatus.InvalidRequest, ErrorMessage: requestError);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new(
                BoundedExecutionStatus.Cancelled,
                ErrorMessage: "Execution cancellation was requested before durable execution state was created.");
        }

        var gate = ProjectLocks.GetOrAdd(request.ProjectId, static _ => new SemaphoreSlim(1, 1));
        if (!gate.Wait(0))
        {
            return new(BoundedExecutionStatus.ProjectBusy, ErrorMessage: "Another bounded execution is active for this project.");
        }

        try
        {
            if (TryGetActiveResidual(request.ProjectId, out _))
            {
                return new(
                    BoundedExecutionStatus.ProjectBusy,
                    ErrorMessage: "A prior bounded execution is still active after cancellation or timeout.");
            }

            return await ExecuteUnderProjectLockAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(
                BoundedExecutionStatus.Cancelled,
                ErrorMessage: "Execution cancellation was requested before durable execution state was created.");
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<BoundedExecutionResult> ExecuteUnderProjectLockAsync(
        BoundedExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ExecutionRunAuthority? authority = null;
        PlanningExecutionContract? contract = null;
        RecoveryCheckpoint? preRunCheckpoint = null;
        ExecutionAdapterResult? adapterResult = null;
        var invocationCount = 0;
        var authorityPersisted = false;

        try
        {
        // The immutable authority is the anti-replay boundary. Inspect it before validating a
        // mutable continuation head so a replay remains recoverable even after the first attempt
        // has published a newer terminal checkpoint.
        var existingAuthority = await _authorities.GetAsync(request.ProjectId, request.RunId, cancellationToken).ConfigureAwait(false);
        if (existingAuthority.IsValid && existingAuthority.Authority is not null)
        {
            var sameBoundAuthorities = SameAuthorityRequest(request, existingAuthority.Authority);
            return sameBoundAuthorities
                ? new(
                    BoundedExecutionStatus.AlreadyStarted,
                    existingAuthority.Authority,
                    ErrorMessage: "This exact RunId already has a durable execution authority; recovery inspection is required.")
                : new(
                    BoundedExecutionStatus.AuthorityConflict,
                    existingAuthority.Authority,
                    ErrorMessage: "The RunId is already bound to a different immutable execution authority.");
        }

        if (existingAuthority.State != ExecutionRunAuthorityReadState.Missing)
        {
            return Failure(
                existingAuthority.State == ExecutionRunAuthorityReadState.Unavailable
                    ? BoundedExecutionStatus.PersistenceUnavailable
                    : BoundedExecutionStatus.AuthorityConflict,
                existingAuthority.ErrorMessage ?? "The existing execution authority could not be read safely.");
        }

        var contextResolution = await _contexts.ResolveAsync(request.ProjectId, cancellationToken).ConfigureAwait(false);
        if (contextResolution.State == ProjectContextResolutionState.ProjectNotFound)
        {
            return Failure(BoundedExecutionStatus.ProjectNotFound, contextResolution.ErrorMessage);
        }

        if (contextResolution.State != ProjectContextResolutionState.Ready || contextResolution.View is null)
        {
            return Failure(BoundedExecutionStatus.ContextUnavailable, contextResolution.ErrorMessage ?? "Project context is not ready.");
        }

        var context = contextResolution.View.Context;
        if (contextResolution.View.Project.Status != ProjectStatus.Active)
        {
            return Failure(BoundedExecutionStatus.ProjectNotExecutable, "Only an Active project may run a bounded execution step.");
        }

        var contractRead = await _contracts.GetAsync(
                request.ProjectId,
                request.PlanningContractReference.ContractId,
                request.PlanningContractReference.Revision,
                cancellationToken)
            .ConfigureAwait(false);
        if (contractRead.State == PlanningContractReadState.Missing)
        {
            return Failure(BoundedExecutionStatus.ContractMissing, contractRead.ErrorMessage);
        }

        if (!contractRead.IsValid || contractRead.Contract is null)
        {
            return Failure(contractRead.State == PlanningContractReadState.Unavailable
                ? BoundedExecutionStatus.PersistenceUnavailable
                : BoundedExecutionStatus.ContractInvalid, contractRead.ErrorMessage);
        }

        var resolvedContract = contractRead.Contract;
        contract = resolvedContract;
        if (resolvedContract.ProjectId != request.ProjectId ||
            !SameContract(resolvedContract.Reference, request.PlanningContractReference) ||
            resolvedContract.Context.ProjectContextId != context.ContextId ||
            resolvedContract.Context.ProjectContextContractVersion != context.ContractVersion)
        {
            return Failure(BoundedExecutionStatus.ContractMismatch, "The exact planning contract does not bind to the current project context.");
        }

        var graphRead = await _graphs.GetAsync(request.ProjectId, request.WorkGraphReference.GraphId, cancellationToken).ConfigureAwait(false);
        if (graphRead.State == WorkGraphReadState.Missing)
        {
            return Failure(BoundedExecutionStatus.GraphMissing, graphRead.ErrorMessage);
        }

        if (!graphRead.IsValid || graphRead.Graph is null)
        {
            return Failure(graphRead.State == WorkGraphReadState.Unavailable
                ? BoundedExecutionStatus.PersistenceUnavailable
                : BoundedExecutionStatus.GraphInvalid, graphRead.ErrorMessage);
        }

        var graph = graphRead.Graph;
        var node = graph.Nodes.FirstOrDefault(value => value.NodeId == request.WorkGraphNodeId);
        if (graph.ProjectId != request.ProjectId || !SameGraph(graph.Reference, request.WorkGraphReference))
        {
            return Failure(BoundedExecutionStatus.GraphInvalid, "The exact work graph does not match its reference.");
        }

        if (node is null || !SameContract(node.ContractReference, request.PlanningContractReference))
        {
            return Failure(BoundedExecutionStatus.GraphNodeMismatch, "The exact work-graph node does not bind to the planning contract.");
        }

        var handoffRead = await _handoffs.GetAsync(request.ProjectId, request.HandoffPackageReference.PackageId, cancellationToken).ConfigureAwait(false);
        if (handoffRead.State == HandoffPackageReadState.Missing)
        {
            return Failure(BoundedExecutionStatus.HandoffMissing, handoffRead.ErrorMessage);
        }

        if (!handoffRead.IsValid || handoffRead.Package is null)
        {
            return Failure(handoffRead.State == HandoffPackageReadState.Unavailable
                ? BoundedExecutionStatus.PersistenceUnavailable
                : BoundedExecutionStatus.HandoffInvalid, handoffRead.ErrorMessage);
        }

        var handoff = handoffRead.Package;
        if (handoff.ProjectId != request.ProjectId ||
            !SameHandoff(handoff.Reference, request.HandoffPackageReference) ||
            handoff.Transition != HandoffTransition.PlannerToExecutor ||
            handoff.SourceRole != HandoffRole.Planner ||
            handoff.TargetRole != HandoffRole.Executor ||
            handoff.ExecutionScope is null ||
            !SameContract(handoff.PlanningContractReference, request.PlanningContractReference) ||
            !SameGraph(handoff.WorkGraphReference, request.WorkGraphReference) ||
            handoff.WorkGraphNodeId != request.WorkGraphNodeId ||
            handoff.Context.ContextId != context.ContextId ||
            handoff.Context.ContextContractVersion != context.ContractVersion)
        {
            return Failure(BoundedExecutionStatus.HandoffMismatch, "The handoff is not an exact Planner-to-Executor package for this run.");
        }

        var routingRead = await _routing.GetAsync(request.ProjectId, request.RoutingDecisionReference.DecisionId, cancellationToken).ConfigureAwait(false);
        if (routingRead.State == RoutingDecisionReadState.Missing)
        {
            return Failure(BoundedExecutionStatus.RoutingMissing, routingRead.ErrorMessage);
        }

        if (!routingRead.IsValid || routingRead.Decision is null)
        {
            return Failure(routingRead.State == RoutingDecisionReadState.Unavailable
                ? BoundedExecutionStatus.PersistenceUnavailable
                : BoundedExecutionStatus.RoutingInvalid, routingRead.ErrorMessage);
        }

        var routing = routingRead.Decision;
        var contextIdentity = new WorkspaceContextIdentity(context.ProjectId, context.ContextId, context.ContractVersion, context.UpdatedAt);
        if (!SameRouting(routing.Reference, request.RoutingDecisionReference) ||
            !WorkspacePreparationPlanningService.IsUsableRoutingDecision(routing, request.ProjectId, request.PlanningContractReference, contextIdentity))
        {
            return Failure(BoundedExecutionStatus.RoutingMismatch, "The routing decision is not the exact usable decision for this run.");
        }

        var selectedAgentId = routing.SelectedAgentId!.Value;
        var selectedAssessment = routing.CandidateAssessments.FirstOrDefault(value => value.AgentId == selectedAgentId);
        var agentResolution = await _agents.ResolveAsync(request.ProjectId, selectedAgentId, cancellationToken).ConfigureAwait(false);
        if (!agentResolution.Found || agentResolution.Agent is null)
        {
            return Failure(BoundedExecutionStatus.AgentMissing, "The routed agent is not present in the effective project registry.");
        }

        var selectedAgent = agentResolution.Agent;
        if (selectedAssessment is null ||
            routing.Recommendation is null ||
            !SameAgentIdentity(selectedAssessment.Candidate.Identity, routing.Recommendation.SelectedAgentIdentity) ||
            !SameAgentSnapshot(selectedAssessment.Candidate, selectedAgent) ||
            selectedAgent.Id != selectedAgentId)
        {
            return Failure(BoundedExecutionStatus.AgentMismatch, "The effective selected agent does not match the recorded routing identity.");
        }

        var connectionStatus = BoundedExecutionAgentEligibility.Validate(selectedAgent, out var connectionMessage);
        if (connectionStatus is not null)
        {
            return Failure(connectionStatus.Value, connectionMessage);
        }

        var workspacePlanRead = await _workspacePlans.GetAsync(
                request.ProjectId,
                request.WorkspacePreparationPlanReference.PlanId,
                cancellationToken)
            .ConfigureAwait(false);
        if (workspacePlanRead.State == WorkspacePreparationPlanReadState.Missing)
        {
            return Failure(BoundedExecutionStatus.WorkspacePlanMissing, workspacePlanRead.ErrorMessage);
        }

        if (workspacePlanRead.State != WorkspacePreparationPlanReadState.Valid || workspacePlanRead.Plan is null)
        {
            return Failure(workspacePlanRead.State == WorkspacePreparationPlanReadState.Unavailable
                ? BoundedExecutionStatus.PersistenceUnavailable
                : BoundedExecutionStatus.WorkspacePlanInvalid, workspacePlanRead.ErrorMessage);
        }

        var workspacePlan = workspacePlanRead.Plan;
        if (workspacePlan.ProjectId != request.ProjectId ||
            !SameWorkspacePlan(workspacePlan.Reference, request.WorkspacePreparationPlanReference) ||
            !SameContract(workspacePlan.ContractReference, request.PlanningContractReference) ||
            !SameGraph(workspacePlan.WorkGraphReference, request.WorkGraphReference) ||
            workspacePlan.WorkGraphNodeId != request.WorkGraphNodeId ||
            !SameRouting(workspacePlan.RoutingDecisionReference, request.RoutingDecisionReference))
        {
            return Failure(BoundedExecutionStatus.WorkspacePlanMismatch, "The workspace plan does not bind to the exact run authorities.");
        }

        var workspaceRecovery = await _workspaceInspection
            .InspectAsync(request.WorkspacePreparationPlanReference, cancellationToken)
            .ConfigureAwait(false);
        if (workspaceRecovery.State != WorkspaceRecoveryState.PreparedAndRecorded || workspaceRecovery.Receipt is null)
        {
            return Failure(
                workspaceRecovery.State is WorkspaceRecoveryState.Conflict or WorkspaceRecoveryState.ForeignWorkspace
                    ? BoundedExecutionStatus.WorkspaceConflict
                    : BoundedExecutionStatus.WorkspaceNotPrepared,
                workspaceRecovery.ErrorMessage ?? $"Workspace state is {workspaceRecovery.State}; PreparedAndRecorded is required.");
        }

        var receipt = workspaceRecovery.Receipt;
        if (receipt.ProjectId != request.ProjectId || receipt.WorkspaceId != workspacePlan.WorkspaceId ||
            !SameWorkspacePlan(receipt.PlanReference, request.WorkspacePreparationPlanReference) ||
            !string.Equals(receipt.WorkspacePath, workspacePlan.ProposedWorkspacePath, StringComparison.Ordinal) ||
            !string.Equals(receipt.BaseCommitSha, workspacePlan.BaseCommitSha, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(receipt.ActualHeadCommitSha, workspacePlan.BaseCommitSha, StringComparison.OrdinalIgnoreCase))
        {
            return Failure(BoundedExecutionStatus.WorkspaceConflict, "The prepared workspace receipt is not an exact clean authority match.");
        }

        var checkpointRead = await _checkpoints.GetAsync(
                request.ProjectId,
                request.CurrentRecoveryCheckpointReference.CheckpointId,
                cancellationToken)
            .ConfigureAwait(false);
        if (checkpointRead.State == RecoveryCheckpointReadState.Missing)
        {
            return Failure(BoundedExecutionStatus.CheckpointMissing, checkpointRead.ErrorMessage);
        }

        if (!checkpointRead.IsValid || checkpointRead.Checkpoint is null)
        {
            return Failure(checkpointRead.State == RecoveryCheckpointReadState.Unavailable
                ? BoundedExecutionStatus.PersistenceUnavailable
                : BoundedExecutionStatus.CheckpointInvalid, checkpointRead.ErrorMessage);
        }

        var currentCheckpoint = checkpointRead.Checkpoint;
        if (currentCheckpoint.ProjectId != request.ProjectId ||
            !SameCheckpoint(currentCheckpoint.Reference, request.CurrentRecoveryCheckpointReference) ||
            currentCheckpoint.Context.ContextId != context.ContextId ||
            currentCheckpoint.Context.ContextContractVersion != context.ContractVersion ||
            currentCheckpoint.Context.ContextUpdatedAt != context.UpdatedAt ||
            !SameContract(currentCheckpoint.PlanningContractReference, request.PlanningContractReference) ||
            !SameGraph(currentCheckpoint.WorkGraphReference, request.WorkGraphReference) ||
            currentCheckpoint.WorkGraphNodeId != request.WorkGraphNodeId ||
            !SameHandoff(currentCheckpoint.HandoffPackageReference, request.HandoffPackageReference))
        {
            return Failure(BoundedExecutionStatus.ContextStale, "The current recovery checkpoint is stale or does not bind to the run authorities.");
        }

        var headRead = await _heads.GetAsync(request.ProjectId, cancellationToken).ConfigureAwait(false);
        if (!headRead.IsValid || headRead.Head is null || !SameCheckpoint(headRead.Head.LatestCheckpointReference, currentCheckpoint.Reference))
        {
            return Failure(BoundedExecutionStatus.CheckpointNotCurrent, "The supplied recovery checkpoint is not the current canonical continuation head.");
        }

        if (currentCheckpoint.LifecycleState == RecoveryCheckpointLifecycleState.Blocked)
        {
            return Failure(BoundedExecutionStatus.CheckpointBlocked, "The current recovery checkpoint is blocked.");
        }

        if (currentCheckpoint.LifecycleState == RecoveryCheckpointLifecycleState.ApprovalRequired)
        {
            return Failure(BoundedExecutionStatus.CheckpointApprovalRequired, "The current recovery checkpoint requires approval.");
        }

        if (currentCheckpoint.Blockers.Count > 0)
        {
            return Failure(BoundedExecutionStatus.CheckpointBlocked, "The current recovery checkpoint has unresolved blockers.");
        }

        if (currentCheckpoint.GateSnapshots.Any(value => value.State is RecoveryGateState.Pending or RecoveryGateState.Unknown or RecoveryGateState.Failed))
        {
            var approvalPending = currentCheckpoint.GateSnapshots.Any(value =>
                value.Kind == RecoveryGateKind.Approval &&
                value.State is RecoveryGateState.Pending or RecoveryGateState.Unknown or RecoveryGateState.Failed);
            return Failure(
                approvalPending ? BoundedExecutionStatus.CheckpointApprovalRequired : BoundedExecutionStatus.CheckpointBlocked,
                "The current recovery checkpoint has an unresolved gate.");
        }

        if (currentCheckpoint.SelectedAgentRoleReferences.Count > 0 &&
            !currentCheckpoint.SelectedAgentRoleReferences.Any(value => value.AgentId == selectedAgent.Id && value.Role == AgentRole.Executor))
        {
            return Failure(BoundedExecutionStatus.AgentMismatch, "The current recovery checkpoint does not authorize the routed executor identity.");
        }

        if (currentCheckpoint.LifecycleState == RecoveryCheckpointLifecycleState.Completed)
        {
            return Failure(BoundedExecutionStatus.CheckpointCompleted, "The current recovery checkpoint is already completed.");
        }

        if (currentCheckpoint.LifecycleState != RecoveryCheckpointLifecycleState.Ready ||
            currentCheckpoint.NextSafeAction != RecoveryNextSafeAction.ContinueFromCheckpoint)
        {
            return Failure(BoundedExecutionStatus.CheckpointNotCurrent, "The current recovery checkpoint does not permit a bounded execution step.");
        }

        if (!ExecutionBudgetEnvelope.TryCreate(resolvedContract.ExecutionBudgets, out var budgets, out var budgetError))
        {
            return Failure(BoundedExecutionStatus.BudgetInvalid, budgetError);
        }

        var adapterResolution = _adapterResolver.Resolve(selectedAgent);
        if (adapterResolution.Status == ExecutionAdapterResolutionStatus.Unsupported)
        {
            return Failure(BoundedExecutionStatus.AdapterUnsupported, adapterResolution.ErrorMessage);
        }

        if (!adapterResolution.Succeeded || adapterResolution.Adapter is null)
        {
            return Failure(BoundedExecutionStatus.AdapterConfigurationConflict, adapterResolution.ErrorMessage);
        }

        if (budgets!.ToolInvocations.HasValue && !adapterResolution.Adapter.Descriptor.SupportedBudgetMetrics.Contains(PlanningBudgetKind.ToolInvocations))
        {
            return Failure(BoundedExecutionStatus.AdapterUnsupported, "The selected adapter cannot enforce the ToolInvocations budget.");
        }

        if (budgets.ModelTurns.HasValue && !adapterResolution.Adapter.Descriptor.SupportedBudgetMetrics.Contains(PlanningBudgetKind.ModelTurns))
        {
            return Failure(BoundedExecutionStatus.AdapterUnsupported, "The selected adapter cannot enforce the ModelTurns budget.");
        }

        if (budgets.ElapsedMinutes > TimeSpan.MaxValue.TotalMinutes)
        {
            return Failure(BoundedExecutionStatus.BudgetInvalid, "The ElapsedMinutes budget exceeds the supported runtime range.");
        }

        try
        {
            authority = new ExecutionRunAuthority(
                request.ProjectId,
                request.RunId,
                _clock.UtcNow,
                request.PlanningContractReference,
                request.WorkGraphReference,
                request.WorkGraphNodeId,
                request.HandoffPackageReference,
                request.RoutingDecisionReference,
                request.WorkspacePreparationPlanReference,
                workspacePlan.WorkspaceId,
                receipt.WorkspacePath,
                receipt.ContentHash,
                currentCheckpoint.Reference,
                selectedAgent.Id,
                selectedAgent.Provider!,
                selectedAgent.ModelIdentifier!,
                selectedAgent.ConnectionMode,
                adapterResolution.Adapter.Descriptor.AdapterIdentifier,
                budgets);
        }
        catch (ArgumentException exception)
        {
            return Failure(BoundedExecutionStatus.InvalidRequest, exception.Message);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var evidencePreflight = ValidateExecutionEvidenceCapacity(currentCheckpoint, authority);
        if (evidencePreflight.Status is not null)
        {
            return new(evidencePreflight.Status.Value, ErrorMessage: evidencePreflight.ErrorMessage);
        }

        var authorityWrite = await _authorities.CreateAsync(authority, cancellationToken).ConfigureAwait(false);
        if (!authorityWrite.Succeeded)
        {
            if (authorityWrite.Status == ExecutionRunAuthorityRepositoryWriteStatus.RunConflict)
            {
                var existing = await _authorities.GetAsync(request.ProjectId, request.RunId, cancellationToken).ConfigureAwait(false);
                if (existing.IsValid && existing.Authority is not null)
                {
                    return string.Equals(existing.Authority.ContentHash, authority.ContentHash, StringComparison.OrdinalIgnoreCase)
                        ? new(BoundedExecutionStatus.AlreadyStarted, existing.Authority, ErrorMessage: "This exact run authority already exists; recovery inspection is required.")
                        : new(BoundedExecutionStatus.AuthorityConflict, ErrorMessage: "The RunId is already bound to a different immutable authority.");
                }

                return Failure(BoundedExecutionStatus.PersistenceUnavailable, existing.ErrorMessage ?? "The existing run authority could not be read safely.");
            }

            return Failure(BoundedExecutionStatus.PersistenceUnavailable, authorityWrite.ErrorMessage ?? "Run-authority persistence is unavailable.");
        }

        authorityPersisted = true;

        var plannedWrite = await AppendRunAsync(authority, resolvedContract, ExecutionRunStatus.Planned, null, null, cancellationToken).ConfigureAwait(false);
        if (!plannedWrite.Succeeded)
        {
            return new(BoundedExecutionStatus.RunningHistoryFailed, authority, ErrorMessage: plannedWrite.ErrorMessage, AdapterInvocationCount: 0);
        }

        var preRunCheckpointCreation = await CreateExecutionCheckpointAsync(
                currentCheckpoint,
                authority,
                request,
                RecoveryCheckpointLifecycleState.Waiting,
                RecoveryNextSafeAction.ResolveBlocker,
                "Bounded execution is prepared; inspect this run authority before any replay.",
                cancellationToken)
            .ConfigureAwait(false);
        if (!preRunCheckpointCreation.Succeeded || preRunCheckpointCreation.Checkpoint is null)
        {
            return new(MapCheckpointCreationFailure(preRunCheckpointCreation.Status, BoundedExecutionStatus.PreRunCheckpointFailed), authority, ErrorMessage: preRunCheckpointCreation.ErrorMessage, AdapterInvocationCount: 0);
        }
        preRunCheckpoint = preRunCheckpointCreation.Checkpoint;

        var runningWrite = await AppendRunAsync(authority, resolvedContract, ExecutionRunStatus.Running, null, null, cancellationToken).ConfigureAwait(false);
        if (!runningWrite.Succeeded)
        {
            return new(
                BoundedExecutionStatus.RunningHistoryFailed,
                authority,
                TerminalCheckpoint: preRunCheckpoint,
                ErrorMessage: runningWrite.ErrorMessage,
                AdapterInvocationCount: 0);
        }

        _invocationGate.BeforeAdapterInvocation();
        var invocation = cancellationToken.IsCancellationRequested
            ? new AdapterInvocation(
                new ExecutionAdapterResult(ExecutionAdapterOutcome.Cancelled, "Execution cancellation was requested before adapter invocation.", "caller-cancelled"),
                0,
                null)
            : await InvokeAdapterOnceAsync(
                adapterResolution.Adapter,
                new ExecutionAdapterRequest(authority, selectedAgent, resolvedContract, handoff, receipt, cancellationToken),
                budgets,
                cancellationToken)
                .ConfigureAwait(false);
        invocationCount = invocation.InvocationCount;
        if (invocation.ResidualTask is not null)
        {
            RegisterResidualExecution(request.ProjectId, invocation.ResidualTask);
        }

        adapterResult = ApplyReportedBudgetLimits(invocation.Result, budgets);
        return await FinalizeExecutionAsync(
                authority,
                resolvedContract,
                request,
                preRunCheckpoint!,
                adapterResult,
                invocationCount)
            .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!authorityPersisted || authority is null)
            {
                return new(
                    BoundedExecutionStatus.Cancelled,
                    ErrorMessage: "Execution cancellation was requested before durable execution state was created.");
            }

            if (contract is null || preRunCheckpoint is null)
            {
                return new(
                    BoundedExecutionStatus.Cancelled,
                    authority,
                    ErrorMessage: "Execution cancellation was requested before a terminal checkpoint could be prepared.",
                    AdapterInvocationCount: invocationCount);
            }

            adapterResult ??= new ExecutionAdapterResult(
                ExecutionAdapterOutcome.Cancelled,
                "Execution cancellation was requested before adapter invocation.",
                "caller-cancelled");
            return await FinalizeExecutionAsync(
                    authority,
                    contract,
                    request,
                    preRunCheckpoint,
                    adapterResult,
                    invocationCount)
                .ConfigureAwait(false);
        }
    }

    private async Task<AdapterInvocation> InvokeAdapterOnceAsync(
        IExecutionAdapter adapter,
        ExecutionAdapterRequest request,
        ExecutionBudgetEnvelope budgets,
        CancellationToken callerCancellationToken)
    {
        if (callerCancellationToken.IsCancellationRequested)
        {
            return new(
                new ExecutionAdapterResult(ExecutionAdapterOutcome.Cancelled, "Execution cancellation was requested before adapter invocation.", "caller-cancelled"),
                0,
                null);
        }

        var timeout = _timeoutProvider.GetTimeout(budgets);
        if (timeout <= TimeSpan.Zero || timeout > BoundedExecutionLimits.MaxExecutionTimeout)
        {
            return new(
                new ExecutionAdapterResult(ExecutionAdapterOutcome.InvalidResult, "The elapsed execution budget is outside the supported runtime range.", "invalid-timeout"),
                0,
                null);
        }

        using var timeoutCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(callerCancellationToken, timeoutCts.Token);
        var boundedRequest = new ExecutionAdapterRequest(
            request.Authority,
            request.SelectedAgent,
            request.Contract,
            request.Handoff,
            request.WorkspaceReceipt,
            linkedCts.Token);
        Task<ExecutionAdapterResult> adapterTask;
        try
        {
            adapterTask = adapter.ExecuteAsync(boundedRequest, linkedCts.Token);
            if (adapterTask is null)
            {
                return new(
                    new ExecutionAdapterResult(ExecutionAdapterOutcome.InvalidResult, "The adapter returned no task.", "invalid-result", mayHaveModifiedWorkspace: true),
                    1,
                    null);
            }
        }
        catch (Exception)
        {
            return new(
                new ExecutionAdapterResult(
                    ExecutionAdapterOutcome.AdapterUnavailable,
                    "The adapter failed before returning a terminal result.",
                    "adapter-exception",
                    mayHaveModifiedWorkspace: true),
                1,
                null);
        }

        var timeoutTask = Task.Delay(timeout);
        var callerSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationRegistration = callerCancellationToken.Register(static state =>
            ((TaskCompletionSource<bool>)state!).TrySetResult(true), callerSignal);
        var completed = await Task.WhenAny(adapterTask, timeoutTask, callerSignal.Task).ConfigureAwait(false);
        if (completed == adapterTask)
        {
            try
            {
                var result = await adapterTask.ConfigureAwait(false);
                return new(
                    result ?? new ExecutionAdapterResult(ExecutionAdapterOutcome.InvalidResult, "Adapter returned no result.", "invalid-result", mayHaveModifiedWorkspace: true),
                    1,
                    null);
            }
            catch (OperationCanceledException)
            {
                return new(
                    new ExecutionAdapterResult(
                        callerCancellationToken.IsCancellationRequested ? ExecutionAdapterOutcome.Cancelled : ExecutionAdapterOutcome.TimedOut,
                        "The adapter cancelled before returning a terminal result.",
                        callerCancellationToken.IsCancellationRequested ? "caller-cancelled" : "elapsed-timeout",
                        mayHaveModifiedWorkspace: true),
                    1,
                    null);
            }
            catch (Exception)
            {
                return new(
                    new ExecutionAdapterResult(ExecutionAdapterOutcome.AdapterUnavailable, "The adapter failed before returning a terminal result.", "adapter-exception", mayHaveModifiedWorkspace: true),
                    1,
                    null);
            }
        }

        var callerCancelled = completed == callerSignal.Task || callerCancellationToken.IsCancellationRequested;
        linkedCts.Cancel();
        var drained = await Task.WhenAny(adapterTask, Task.Delay(_timing.AdapterCancellationDrainTimeout)).ConfigureAwait(false);
        if (drained == adapterTask)
        {
            try
            {
                _ = await adapterTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The cancellation/timeout classification remains authoritative.
            }

            return new(
                new ExecutionAdapterResult(
                    callerCancelled ? ExecutionAdapterOutcome.Cancelled : ExecutionAdapterOutcome.TimedOut,
                    callerCancelled ? "Execution cancellation was requested." : "The ElapsedMinutes budget elapsed.",
                    callerCancelled ? "caller-cancelled" : "elapsed-timeout",
                    mayHaveModifiedWorkspace: true),
                1,
                null);
        }

        return new(
            new ExecutionAdapterResult(
                ExecutionAdapterOutcome.TerminationUnconfirmed,
                callerCancelled
                    ? "Execution cancellation was requested, but adapter termination was not confirmed within the bounded drain."
                    : "The ElapsedMinutes budget elapsed, but adapter termination was not confirmed within the bounded drain.",
                callerCancelled ? "caller-cancelled-termination-unconfirmed" : "elapsed-timeout-termination-unconfirmed",
                mayHaveModifiedWorkspace: true),
            1,
            adapterTask);
    }

    private async Task<BoundedExecutionResult> FinalizeExecutionAsync(
        ExecutionRunAuthority authority,
        PlanningExecutionContract contract,
        BoundedExecutionRequest request,
        RecoveryCheckpoint sourceCheckpoint,
        ExecutionAdapterResult adapterResult,
        int invocationCount)
    {
        var terminalStatus = MapTerminalStatus(adapterResult.Outcome);
        var terminalCheckpointState = MapCheckpointLifecycle(adapterResult.Outcome);
        var nextSafeAction = adapterResult.Outcome == ExecutionAdapterOutcome.Succeeded
            ? RecoveryNextSafeAction.RunValidation
            : RecoveryNextSafeAction.ResolveBlocker;
        var explanation = adapterResult.Outcome switch
        {
            ExecutionAdapterOutcome.Succeeded => "Bounded execution step completed; independent validation/evidence is required.",
            ExecutionAdapterOutcome.Cancelled => "Bounded execution was cancelled before the adapter completed; inspect recovery evidence before another execution attempt.",
            ExecutionAdapterOutcome.TimedOut => "Bounded execution timed out after adapter termination was confirmed; inspect recovery evidence before another execution attempt.",
            ExecutionAdapterOutcome.TerminationUnconfirmed => "Adapter termination was not confirmed within the bounded cancellation drain; the workspace may still be changing and owner/recovery inspection is required.",
            _ => "Bounded execution stopped; inspect recovery evidence before another execution attempt."
        };

        using var finalizationCts = new CancellationTokenSource(_timing.FinalizationTimeout);
        try
        {
            var terminalCheckpoint = await CreateExecutionCheckpointAsync(
                    sourceCheckpoint,
                    authority,
                    request,
                    terminalCheckpointState,
                    nextSafeAction,
                    explanation,
                    finalizationCts.Token,
                    adapterResult.Outcome == ExecutionAdapterOutcome.BudgetExceeded ? adapterResult.StopReason : null)
                .ConfigureAwait(false);
            if (!terminalCheckpoint.Succeeded || terminalCheckpoint.Checkpoint is null)
            {
                return new(
                    MapCheckpointCreationFailure(terminalCheckpoint.Status, BoundedExecutionStatus.TerminalCheckpointFailed),
                    authority,
                    adapterResult,
                    ErrorMessage: terminalCheckpoint.ErrorMessage,
                    AdapterInvocationCount: invocationCount);
            }

            var terminalWrite = await AppendRunAsync(
                    authority,
                    contract,
                    terminalStatus,
                    adapterResult.Summary,
                    adapterResult.StopReason,
                    finalizationCts.Token)
                .ConfigureAwait(false);
            if (!terminalWrite.Succeeded)
            {
                return new(
                    BoundedExecutionStatus.AuditPersistenceFailed,
                    authority,
                    adapterResult,
                    terminalCheckpoint.Checkpoint,
                    terminalWrite.ErrorMessage,
                    invocationCount);
            }

            return new(
                MapServiceStatus(adapterResult.Outcome),
                authority,
                adapterResult,
                terminalCheckpoint.Checkpoint,
                AdapterInvocationCount: invocationCount);
        }
        catch (OperationCanceledException)
        {
            return new(
                BoundedExecutionStatus.TerminalCheckpointFailed,
                authority,
                adapterResult,
                ErrorMessage: "Bounded execution finalization exceeded its independent persistence deadline.",
                AdapterInvocationCount: invocationCount);
        }
        catch (Exception)
        {
            return new(
                BoundedExecutionStatus.TerminalCheckpointFailed,
                authority,
                adapterResult,
                ErrorMessage: "Bounded execution finalization failed before its terminal state became durable.",
                AdapterInvocationCount: invocationCount);
        }
    }

    private async Task<RecoveryCheckpointCreationResult> CreateExecutionCheckpointAsync(
        RecoveryCheckpoint source,
        ExecutionRunAuthority authority,
        BoundedExecutionRequest request,
        RecoveryCheckpointLifecycleState lifecycleState,
        RecoveryNextSafeAction nextSafeAction,
        string explanation,
        CancellationToken cancellationToken,
        string? budgetBlocker = null)
    {
        var evidence = source.EvidenceReferences.ToList();
        var executionEvidence = CreateExecutionEvidence(authority);
        var existingEvidence = evidence.FirstOrDefault(value => value.EvidenceId == executionEvidence.EvidenceId);
        if (existingEvidence is not null)
        {
            if (!SameEvidence(existingEvidence, executionEvidence))
            {
                return new(
                    RecoveryCheckpointCreationStatus.EvidenceConflict,
                    ErrorMessage: "The stable execution-run evidence identity conflicts with the checkpoint lineage.");
            }
        }
        else
        {
            if (evidence.Count >= RecoveryCheckpointLimits.MaxEvidenceReferences)
            {
                return new(
                    RecoveryCheckpointCreationStatus.EvidenceCapacityExceeded,
                    ErrorMessage: "The recovery checkpoint cannot accommodate the required execution-run evidence reference.");
            }

            evidence.Add(executionEvidence);
        }

        var roles = source.SelectedAgentRoleReferences.ToList();
        if (!roles.Any(value => value.AgentId == authority.AgentId && value.Role == AgentRole.Executor))
        {
            roles.Add(new RecoveryAgentRoleReference(authority.AgentId, AgentRole.Executor, authority.RoutingDecisionReference.ToString()));
        }

        var blockers = source.Blockers.ToList();
        if (!string.IsNullOrWhiteSpace(budgetBlocker))
        {
            blockers.Add(new RecoveryBlocker(
                $"execution-budget-{authority.RunId:N}",
                RecoveryBlockerKind.Other,
                budgetBlocker,
                authority.Reference.ToString(),
                ownerActionRequired: true));
        }

        return await _checkpointService.CreateAsync(
                new RecoveryCheckpointCreationRequest(
                    request.ProjectId,
                    Guid.NewGuid(),
                    lifecycleState,
                    request.PlanningContractReference,
                    evidence,
                    source.GateSnapshots,
                    blockers,
                    nextSafeAction,
                    explanation,
                    _clock.UtcNow,
                    request.WorkGraphReference,
                    request.WorkGraphNodeId,
                    request.HandoffPackageReference,
                    previousCheckpointReference: source.Reference,
                    selectedAgentRoleReferences: roles),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static RecoveryEvidenceReference CreateExecutionEvidence(ExecutionRunAuthority authority) =>
        new(
            authority.RunId,
            RecoveryEvidenceKind.Other,
            $"execution-run:{authority.ProjectId:D}/{authority.RunId:D}/{authority.ContentHash}",
            authority.CreatedAt,
            RecoveryEvidenceFreshness.PointInTime,
            contentHash: authority.ContentHash);

    private static EvidencePreflightResult ValidateExecutionEvidenceCapacity(
        RecoveryCheckpoint source,
        ExecutionRunAuthority authority)
    {
        var expected = CreateExecutionEvidence(authority);
        var existing = source.EvidenceReferences.FirstOrDefault(value => value.EvidenceId == expected.EvidenceId);
        if (existing is not null && !SameEvidence(existing, expected))
        {
            return new(BoundedExecutionStatus.EvidenceConflict, "The stable execution-run evidence identity conflicts with the current checkpoint.");
        }

        return existing is null && source.EvidenceReferences.Count >= RecoveryCheckpointLimits.MaxEvidenceReferences
            ? new(BoundedExecutionStatus.EvidenceCapacityExceeded, "The current recovery checkpoint has no capacity for the required execution-run evidence reference.")
            : new(null, null);
    }

    private static bool SameEvidence(RecoveryEvidenceReference left, RecoveryEvidenceReference right) =>
        left.EvidenceId == right.EvidenceId &&
        left.Kind == right.Kind &&
        string.Equals(left.Reference, right.Reference, StringComparison.Ordinal) &&
        left.ObservedAt == right.ObservedAt &&
        left.Freshness == right.Freshness &&
        left.ValidUntil == right.ValidUntil &&
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static BoundedExecutionStatus MapCheckpointCreationFailure(
        RecoveryCheckpointCreationStatus status,
        BoundedExecutionStatus fallback) => status switch
        {
            RecoveryCheckpointCreationStatus.EvidenceCapacityExceeded => BoundedExecutionStatus.EvidenceCapacityExceeded,
            RecoveryCheckpointCreationStatus.EvidenceConflict => BoundedExecutionStatus.EvidenceConflict,
            _ => fallback
        };

    private async Task<HistoryAppendResult> AppendRunAsync(
        ExecutionRunAuthority authority,
        PlanningExecutionContract contract,
        ExecutionRunStatus status,
        string? outcome,
        string? stopReason,
        CancellationToken cancellationToken)
    {
        try
        {
            var recordedAt = _clock.UtcNow;
            var safeOutcome = Sanitize(outcome, "bounded execution step returned a terminal result.", 500);
            var safeStopReason = Sanitize(stopReason, null, 500);
            var run = new ExecutionRun(
                authority.ProjectId,
                authority.RunId,
                status,
                authority.CreatedAt,
                status is ExecutionRunStatus.Planned or ExecutionRunStatus.Running ? null : recordedAt,
                contract.WorkItem.Reference,
                contract.WorkItem.Title,
                authority.AgentId,
                $"{authority.Provider}:{authority.ModelIdentifier}",
                safeOutcome,
                safeStopReason,
                authority.PlanningContractReference.ToString(),
                Guid.NewGuid(),
                recordedAt);
            await _history.AppendExecutionRunAsync(run, cancellationToken).ConfigureAwait(false);
            return new(true);
        }
        catch (OperationCanceledException)
        {
            return new(false, "Execution history append was cancelled before it became durable.");
        }
        catch (Exception)
        {
            return new(false, "Execution history persistence is unavailable.");
        }
    }

    private string? Sanitize(string? value, string? fallback, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        try
        {
            var redacted = _redaction.Redact(value).Value.Trim();
            return redacted.Length <= maximumLength ? redacted : redacted[..maximumLength];
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private static ExecutionAdapterResult ApplyReportedBudgetLimits(
        ExecutionAdapterResult result,
        ExecutionBudgetEnvelope budgets)
    {
        if (result.Outcome != ExecutionAdapterOutcome.Succeeded || result.Usage is null)
        {
            return result;
        }

        var usage = result.Usage;
        // ChangedFiles/ChangedLines are retained as post-execution evidence constraints. The
        // adapter/model cannot self-certify repository evidence, so only runtime-enforceable
        // invocation/turn metrics can produce a bounded budget terminal outcome here.
        var exceeded = budgets.ToolInvocations.HasValue && usage.ToolInvocations > budgets.ToolInvocations ||
            budgets.ModelTurns.HasValue && usage.ModelTurns > budgets.ModelTurns;
        return exceeded
            ? new ExecutionAdapterResult(
                ExecutionAdapterOutcome.BudgetExceeded,
                "The bounded execution step exceeded an immutable budget.",
                "budget-exceeded",
                usage,
                result.MayHaveModifiedWorkspace,
                result.EvidenceReferences)
            : result;
    }

    private static ExecutionRunStatus MapTerminalStatus(ExecutionAdapterOutcome outcome) => outcome switch
    {
        ExecutionAdapterOutcome.Succeeded => ExecutionRunStatus.Completed,
        ExecutionAdapterOutcome.Cancelled => ExecutionRunStatus.Cancelled,
        _ => ExecutionRunStatus.Failed
    };

    private static RecoveryCheckpointLifecycleState MapCheckpointLifecycle(ExecutionAdapterOutcome outcome) => outcome switch
    {
        ExecutionAdapterOutcome.Succeeded => RecoveryCheckpointLifecycleState.Ready,
        ExecutionAdapterOutcome.Cancelled => RecoveryCheckpointLifecycleState.Cancelled,
        ExecutionAdapterOutcome.TimedOut => RecoveryCheckpointLifecycleState.Interrupted,
        ExecutionAdapterOutcome.TerminationUnconfirmed => RecoveryCheckpointLifecycleState.Interrupted,
        ExecutionAdapterOutcome.BudgetExceeded => RecoveryCheckpointLifecycleState.Blocked,
        _ => RecoveryCheckpointLifecycleState.Failed
    };

    private static BoundedExecutionStatus MapServiceStatus(ExecutionAdapterOutcome outcome) => outcome switch
    {
        ExecutionAdapterOutcome.Succeeded => BoundedExecutionStatus.Succeeded,
        ExecutionAdapterOutcome.Cancelled => BoundedExecutionStatus.Cancelled,
        ExecutionAdapterOutcome.TimedOut => BoundedExecutionStatus.TimedOut,
        ExecutionAdapterOutcome.TerminationUnconfirmed => BoundedExecutionStatus.ResidualExecutionActive,
        ExecutionAdapterOutcome.BudgetExceeded => BoundedExecutionStatus.BudgetExceeded,
        _ => BoundedExecutionStatus.AdapterFailed
    };

    private static bool ValidateRequest(BoundedExecutionRequest request, out string message)
    {
        if (request.ProjectId == Guid.Empty || request.RunId == Guid.Empty || request.WorkGraphNodeId == Guid.Empty ||
            request.WorkspacePreparationPlanReference.ProjectId != request.ProjectId ||
            request.PlanningContractReference.ContractId == Guid.Empty ||
            request.HandoffPackageReference.PackageId == Guid.Empty ||
            request.RoutingDecisionReference.DecisionId == Guid.Empty ||
            request.CurrentRecoveryCheckpointReference.CheckpointId == Guid.Empty)
        {
            message = "Project, run, graph-node, project-bound workspace, and authority identifiers are required.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static BoundedExecutionResult Failure(BoundedExecutionStatus status, string? message) =>
        new(status, ErrorMessage: message);

    private static bool SameContract(PlanningExecutionContractReference left, PlanningExecutionContractReference right) =>
        left is not null && right is not null && left.ContractId == right.ContractId && left.Revision == right.Revision &&
        left.SchemaVersion == right.SchemaVersion && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static bool SameGraph(WorkGraphReference? left, WorkGraphReference? right) =>
        left is not null && right is not null && left.GraphId == right.GraphId &&
        left.SchemaVersion == right.SchemaVersion && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static bool SameHandoff(HandoffPackageReference? left, HandoffPackageReference? right) =>
        left is not null && right is not null && left.PackageId == right.PackageId && left.SchemaVersion == right.SchemaVersion &&
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static bool SameRouting(RoutingDecisionReference? left, RoutingDecisionReference? right) =>
        left is null && right is null || left is not null && right is not null && left.DecisionId == right.DecisionId &&
        left.SchemaVersion == right.SchemaVersion && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static bool SameWorkspacePlan(WorkspacePreparationPlanReference? left, WorkspacePreparationPlanReference? right) =>
        left is not null && right is not null && left.ProjectId == right.ProjectId && left.PlanId == right.PlanId &&
        left.SchemaVersion == right.SchemaVersion && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static bool SameCheckpoint(RecoveryCheckpointReference? left, RecoveryCheckpointReference? right) =>
        left is not null && right is not null && left.CheckpointId == right.CheckpointId && left.SchemaVersion == right.SchemaVersion &&
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static bool SameAuthorityRequest(BoundedExecutionRequest request, ExecutionRunAuthority authority) =>
        authority.ProjectId == request.ProjectId &&
        authority.RunId == request.RunId &&
        SameContract(authority.PlanningContractReference, request.PlanningContractReference) &&
        SameGraph(authority.WorkGraphReference, request.WorkGraphReference) &&
        authority.WorkGraphNodeId == request.WorkGraphNodeId &&
        SameHandoff(authority.HandoffPackageReference, request.HandoffPackageReference) &&
        SameRouting(authority.RoutingDecisionReference, request.RoutingDecisionReference) &&
        SameWorkspacePlan(authority.WorkspacePreparationPlanReference, request.WorkspacePreparationPlanReference) &&
        SameCheckpoint(authority.InputRecoveryCheckpointReference, request.CurrentRecoveryCheckpointReference);

    private static bool SameAgentIdentity(AgentIdentity left, AgentIdentity right) =>
        left.Id == right.Id && string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal) &&
        string.Equals(left.Provider, right.Provider, StringComparison.Ordinal) &&
        string.Equals(left.ModelIdentifier, right.ModelIdentifier, StringComparison.Ordinal);

    private static bool SameAgentIdentity(AgentIdentity identity, EffectiveAgentDefinition agent) =>
        identity.Id == agent.Id && string.Equals(identity.DisplayName, agent.Name, StringComparison.Ordinal) &&
        string.Equals(identity.Provider, agent.Provider, StringComparison.Ordinal) &&
        string.Equals(identity.ModelIdentifier, agent.ModelIdentifier, StringComparison.Ordinal);

    private static bool SameAgentSnapshot(RoutingAgentSnapshot snapshot, EffectiveAgentDefinition agent) =>
        snapshot.ProjectId == agent.ProjectId &&
        snapshot.AgentId == agent.Id &&
        SameAgentIdentity(snapshot.Identity, agent) &&
        snapshot.RegistryUpdatedAt == agent.GlobalDefinition.UpdatedAt &&
        snapshot.Enabled == agent.Enabled &&
        snapshot.RoleCapabilities.SequenceEqual(agent.RoleCapabilities.OrderBy(static value => value)) &&
        snapshot.Capabilities.SequenceEqual(NormalizeRoutingTags(agent.Capabilities), StringComparer.Ordinal) &&
        snapshot.Limitations.SequenceEqual(NormalizeLimitations(agent.Limitations), StringComparer.Ordinal) &&
        snapshot.ConnectionMode == agent.ConnectionMode &&
        snapshot.SupportedConnectionModes.SequenceEqual(agent.SupportedConnectionModes.OrderBy(static value => value)) &&
        snapshot.Availability == agent.Availability &&
        snapshot.AuthenticationState == agent.AuthenticationState &&
        snapshot.EntitlementState == agent.EntitlementState;

    private static IReadOnlyList<string> NormalizeRoutingTags(IReadOnlyList<string> values) =>
        values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> NormalizeLimitations(IReadOnlyList<string> values) =>
        values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

    private static void ValidateTiming(IBoundedExecutionTiming timing)
    {
        if (timing.FinalizationTimeout <= TimeSpan.Zero || timing.AdapterCancellationDrainTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timing), "Bounded execution timing values must be positive.");
        }

        var maximum = TimeSpan.FromMinutes(5);
        if (timing.FinalizationTimeout > maximum || timing.AdapterCancellationDrainTimeout > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(timing), "Bounded execution timing values cannot exceed five minutes.");
        }
    }

    private static bool TryGetActiveResidual(Guid projectId, out ResidualExecution? residual)
    {
        if (!ResidualExecutions.TryGetValue(projectId, out residual))
        {
            return false;
        }

        if (!residual.AdapterTask.IsCompleted)
        {
            return true;
        }

        ObserveResidualTask(residual.AdapterTask);
        RemoveResidual(projectId, residual);
        residual = null;
        return false;
    }

    private static void RegisterResidualExecution(Guid projectId, Task adapterTask)
    {
        var residual = new ResidualExecution(adapterTask);
        if (!ResidualExecutions.TryAdd(projectId, residual))
        {
            ObserveResidualTask(adapterTask);
            return;
        }

        _ = adapterTask.ContinueWith(
            completedTask =>
            {
                ObserveResidualTask(completedTask);
                RemoveResidual(projectId, residual);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void RemoveResidual(Guid projectId, ResidualExecution residual) =>
        ((ICollection<KeyValuePair<Guid, ResidualExecution>>)ResidualExecutions)
            .Remove(new KeyValuePair<Guid, ResidualExecution>(projectId, residual));

    private static void ObserveResidualTask(Task task)
    {
        _ = task.Exception;
    }

    private sealed record HistoryAppendResult(bool Succeeded, string? ErrorMessage = null);

    private sealed record AdapterInvocation(ExecutionAdapterResult Result, int InvocationCount, Task? ResidualTask);

    private sealed record EvidencePreflightResult(BoundedExecutionStatus? Status, string? ErrorMessage);

    private sealed record ResidualExecution(Task AdapterTask);

    private sealed class NoOpExecutionInvocationGate : IExecutionInvocationGate
    {
        public static NoOpExecutionInvocationGate Instance { get; } = new();

        public void BeforeAdapterInvocation()
        {
        }
    }
}

/// <summary>One canonical executor eligibility rule for both new and restored bounded runs.</summary>
internal static class BoundedExecutionAgentEligibility
{
    internal static BoundedExecutionStatus? Validate(EffectiveAgentDefinition agent, out string message)
    {
        if (!agent.Enabled || agent.Availability == AgentAvailability.Disabled)
        {
            message = "The selected agent is disabled.";
            return BoundedExecutionStatus.AgentUnavailable;
        }

        if (agent.Availability != AgentAvailability.Available ||
            agent.AuthenticationState == AgentAuthenticationState.AuthenticationRequired ||
            agent.EntitlementState == AgentEntitlementState.VerifiedUnavailable)
        {
            message = "The selected agent is unavailable or requires authentication.";
            return BoundedExecutionStatus.AgentUnavailable;
        }

        if (agent.ConnectionMode is AgentConnectionMode.InteractiveOnly or AgentConnectionMode.Manual or AgentConnectionMode.Unsupported or AgentConnectionMode.Unknown)
        {
            message = "The selected agent does not expose a supported bounded execution connection mode.";
            return BoundedExecutionStatus.ConnectionUnsupported;
        }

        if (!agent.RoleCapabilities.Contains(AgentRole.Executor) ||
            !agent.SupportedConnectionModes.Contains(agent.ConnectionMode) ||
            string.IsNullOrWhiteSpace(agent.Provider) ||
            string.IsNullOrWhiteSpace(agent.ModelIdentifier))
        {
            message = "The selected agent is not an exact executable Executor identity.";
            return BoundedExecutionStatus.AgentMismatch;
        }

        message = string.Empty;
        return null;
    }
}
