using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Routing;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Application.Workspaces;

namespace AIUsageMonitor.Application.Orchestration;

/// <summary>Owner-authored bounded intent. Internal authority identifiers are created by the coordinator.</summary>
public sealed class OrchestrationWorkRequest
{
    public OrchestrationWorkRequest(
        Guid projectId,
        string ownerReference,
        string title,
        string objective,
        string? workItemReference = null,
        IReadOnlyList<string>? acceptanceCriteria = null,
        IReadOnlyList<string>? constraints = null,
        IReadOnlyList<string>? validationExpectations = null,
        RoutingTaskClassification? classification = null)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("A registered project is required.", nameof(projectId));
        }

        ProjectId = projectId;
        RequestId = Guid.NewGuid();
        OwnerReference = Required(ownerReference, nameof(ownerReference), 300);
        Title = Required(title, nameof(title), 500);
        Objective = Required(objective, nameof(objective), 4_000);
        WorkItemReference = Optional(workItemReference, nameof(workItemReference), 200);
        AcceptanceCriteria = NormalizeLines(acceptanceCriteria, nameof(acceptanceCriteria), required: true);
        Constraints = NormalizeLines(constraints, nameof(constraints), required: false);
        ValidationExpectations = NormalizeLines(validationExpectations, nameof(validationExpectations), required: false);
        Classification = classification ?? new RoutingTaskClassification(
            RoutingScopeScale.Bounded,
            RoutingTaskRisk.Moderate,
            RoutingBlastRadius.Module,
            RoutingValidationCost.Moderate,
            AgentRole.Executor,
            capacityRequirement: RoutingCapacityRequirement.Optional);
    }

    public Guid ProjectId { get; }
    public Guid RequestId { get; }
    public string OwnerReference { get; }
    public string Title { get; }
    public string Objective { get; }
    public string? WorkItemReference { get; }
    public IReadOnlyList<string> AcceptanceCriteria { get; }
    public IReadOnlyList<string> Constraints { get; }
    public IReadOnlyList<string> ValidationExpectations { get; }
    public RoutingTaskClassification Classification { get; }

    private static IReadOnlyList<string> NormalizeLines(
        IReadOnlyList<string>? values,
        string parameterName,
        bool required)
    {
        var normalized = (values ?? Array.Empty<string>())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Required(value, parameterName, 4_000))
            .ToArray();
        if (required && normalized.Length == 0)
        {
            throw new ArgumentException("At least one acceptance criterion is required.", parameterName);
        }

        if (normalized.Length > 32)
        {
            throw new ArgumentException("A bounded request cannot contain more than 32 entries.", parameterName);
        }

        return normalized;
    }

    private static string Required(string value, string parameterName, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A bounded text value is required.", parameterName)
            : value.Trim().Length > maximumLength
                ? throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName)
                : value.Trim();

    private static string? Optional(string? value, string parameterName, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, parameterName, maximumLength);
}

public enum ExecutionPreparationStage
{
    None,
    ProjectContext,
    Repository,
    PlanningContract,
    WorkGraph,
    Routing,
    Handoff,
    WorkspacePlan,
    Workspace,
    RecoveryCheckpoint,
    Execution
}

public enum ExecutionPreparationStatus
{
    Prepared,
    InvalidRequest,
    ProjectNotFound,
    ContextUnavailable,
    RepositoryUnavailable,
    RepositoryNotClean,
    PlannerUnavailable,
    PlanningContractFailed,
    WorkGraphFailed,
    RoutingFailed,
    HandoffFailed,
    WorkspacePlanFailed,
    WorkspacePreparationFailed,
    RecoveryCheckpointFailed,
    Cancelled,
    PersistenceUnavailable,
    Failed
}

public enum ExecutionCoordinatorState
{
    Draft,
    Preparing,
    Ready,
    Running,
    Waiting,
    Cancelling,
    Cancelled,
    Failed,
    Completed,
    ValidationPending,
    HumanActionRequired
}

public sealed record ExecutionPreparationResult(
    ExecutionPreparationStatus Status,
    ExecutionPreparationStage Stage = ExecutionPreparationStage.None,
    PreparedExecution? PreparedExecution = null,
    string? ErrorMessage = null)
{
    public bool Succeeded => Status == ExecutionPreparationStatus.Prepared && PreparedExecution is not null;
}

public sealed record ExecutionStartResult(
    ExecutionCoordinatorState State,
    BoundedExecutionResult? Result = null,
    string? ErrorMessage = null);

public sealed record ExecutionCancellationResult(
    bool Requested,
    string? ErrorMessage = null);

public sealed record ExecutionRunSnapshot(
    Guid? RunId,
    ExecutionCoordinatorState State,
    string? Message,
    BoundedExecutionResult? Result);

public sealed class PreparedExecution
{
    public PreparedExecution(
        Guid runId,
        EffectiveAgentDefinition planner,
        EffectiveAgentDefinition executor,
        PlanningExecutionContract contract,
        WorkGraph graph,
        HandoffPackage handoff,
        WorkspacePreparationPlan workspacePlan,
        WorkspacePreparationReceipt workspaceReceipt,
        RecoveryCheckpoint checkpoint,
        RoutingDecision routingDecision,
        BoundedExecutionRequest request)
    {
        RunId = runId;
        Planner = planner ?? throw new ArgumentNullException(nameof(planner));
        Executor = executor ?? throw new ArgumentNullException(nameof(executor));
        Contract = contract ?? throw new ArgumentNullException(nameof(contract));
        Graph = graph ?? throw new ArgumentNullException(nameof(graph));
        Handoff = handoff ?? throw new ArgumentNullException(nameof(handoff));
        WorkspacePlan = workspacePlan ?? throw new ArgumentNullException(nameof(workspacePlan));
        WorkspaceReceipt = workspaceReceipt ?? throw new ArgumentNullException(nameof(workspaceReceipt));
        Checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
        RoutingDecision = routingDecision ?? throw new ArgumentNullException(nameof(routingDecision));
        Request = request ?? throw new ArgumentNullException(nameof(request));
    }

    public Guid RunId { get; }
    public EffectiveAgentDefinition Planner { get; }
    public EffectiveAgentDefinition Executor { get; }
    public PlanningExecutionContract Contract { get; }
    public WorkGraph Graph { get; }
    public HandoffPackage Handoff { get; }
    public WorkspacePreparationPlan WorkspacePlan { get; }
    public WorkspacePreparationReceipt WorkspaceReceipt { get; }
    public RecoveryCheckpoint Checkpoint { get; }
    public RoutingDecision RoutingDecision { get; }
    public BoundedExecutionRequest Request { get; }
}

public interface IExecutionCoordinator
{
    Task<ExecutionPreparationResult> PrepareAsync(
        OrchestrationWorkRequest request,
        CancellationToken cancellationToken = default);

    Task<ExecutionStartResult> StartAsync(CancellationToken cancellationToken = default);

    Task<ExecutionCancellationResult> CancelAsync(CancellationToken cancellationToken = default);

    Task<ExecutionRunSnapshot> GetCurrentRunAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Converts owner intent into existing immutable orchestration authorities, then delegates start
/// to the bounded execution service. It never writes persistence directly or invokes a provider.
/// </summary>
public sealed class ExecutionCoordinator : IExecutionCoordinator
{
    private readonly IProjectContextResolver _contexts;
    private readonly IProjectRepositoryStateService _repositories;
    private readonly IPlanningExecutionContractService _contracts;
    private readonly IWorkGraphService _graphs;
    private readonly IRoutingDecisionService _routing;
    private readonly IHandoffPackageService _handoffs;
    private readonly IWorkspacePreparationPlanningService _workspacePlanning;
    private readonly IWorkspacePreparationService _workspace;
    private readonly IRecoveryCheckpointService _recovery;
    private readonly IBoundedExecutionService _execution;
    private readonly IHandoffRedactionService _redaction;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private PreparedExecution? _prepared;
    private CancellationTokenSource? _runCancellation;
    private BoundedExecutionResult? _lastResult;
    private ExecutionCoordinatorState _state = ExecutionCoordinatorState.Draft;
    private string? _message;

    public ExecutionCoordinator(
        IProjectContextResolver contexts,
        IProjectRepositoryStateService repositories,
        IPlanningExecutionContractService contracts,
        IWorkGraphService graphs,
        IRoutingDecisionService routing,
        IHandoffPackageService handoffs,
        IWorkspacePreparationPlanningService workspacePlanning,
        IWorkspacePreparationService workspace,
        IRecoveryCheckpointService recovery,
        IBoundedExecutionService execution,
        IHandoffRedactionService redaction,
        IClock clock)
    {
        _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
        _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
        _routing = routing ?? throw new ArgumentNullException(nameof(routing));
        _handoffs = handoffs ?? throw new ArgumentNullException(nameof(handoffs));
        _workspacePlanning = workspacePlanning ?? throw new ArgumentNullException(nameof(workspacePlanning));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
        _execution = execution ?? throw new ArgumentNullException(nameof(execution));
        _redaction = redaction ?? throw new ArgumentNullException(nameof(redaction));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ExecutionPreparationResult> PrepareAsync(
        OrchestrationWorkRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return new(ExecutionPreparationStatus.InvalidRequest, ErrorMessage: "A work request is required.");
        }

        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state is ExecutionCoordinatorState.Preparing or ExecutionCoordinatorState.Running or ExecutionCoordinatorState.Cancelling)
            {
                return new(ExecutionPreparationStatus.Failed, ErrorMessage: "The current execution must finish before another request can be prepared.");
            }

            _prepared = null;
            _lastResult = null;
            _message = null;
            _state = ExecutionCoordinatorState.Preparing;
        }
        finally
        {
            _stateGate.Release();
        }

        try
        {
            var contextResolution = await _contexts.ResolveAsync(request.ProjectId, cancellationToken).ConfigureAwait(false);
            if (contextResolution.State == ProjectContextResolutionState.ProjectNotFound)
            {
                return await FailAsync(ExecutionPreparationStatus.ProjectNotFound, ExecutionPreparationStage.ProjectContext, contextResolution.ErrorMessage ?? "Project was not found.").ConfigureAwait(false);
            }

            if (contextResolution.State != ProjectContextResolutionState.Ready || contextResolution.View is null)
            {
                return await FailAsync(ExecutionPreparationStatus.ContextUnavailable, ExecutionPreparationStage.ProjectContext, contextResolution.ErrorMessage ?? "Project context is not ready.").ConfigureAwait(false);
            }

            var view = contextResolution.View;
            var plannerCandidates = view.EffectiveAgents
                .Where(agent => agent.Enabled && agent.RoleCapabilities.Contains(AgentRole.Planner))
                .ToArray();
            if (plannerCandidates.Length != 1)
            {
                return await FailAsync(ExecutionPreparationStatus.PlannerUnavailable, ExecutionPreparationStage.ProjectContext, plannerCandidates.Length == 0
                    ? "No enabled planner is configured for this project."
                    : "More than one enabled planner is configured; planner authority is ambiguous.").ConfigureAwait(false);
            }

            if (_redaction.ValidateIdentityText(request.OwnerReference).RequiresRedaction ||
                (request.WorkItemReference is not null && _redaction.ValidateIdentityText(request.WorkItemReference).RequiresRedaction))
            {
                return await FailAsync(ExecutionPreparationStatus.InvalidRequest, ExecutionPreparationStage.PlanningContract, "Owner or work-item identity crossed the redaction boundary.").ConfigureAwait(false);
            }

            var repository = await _repositories.VerifyAsync(view.Project, cancellationToken).ConfigureAwait(false);
            if (repository.Status is not (RepositoryVerificationStatus.AvailableClean or RepositoryVerificationStatus.AvailableDirty) ||
                string.IsNullOrWhiteSpace(repository.HeadSha) ||
                string.IsNullOrWhiteSpace(repository.BranchName) ||
                repository.IsDetachedHead)
            {
                return await FailAsync(ExecutionPreparationStatus.RepositoryUnavailable, ExecutionPreparationStage.Repository, repository.SafeErrorMessage ?? "Exact local repository identity is unavailable.").ConfigureAwait(false);
            }

            if (repository.IsClean != true)
            {
                return await FailAsync(ExecutionPreparationStatus.RepositoryNotClean, ExecutionPreparationStage.Repository, "The source worktree is not clean; preparation stopped before any worktree mutation.").ConfigureAwait(false);
            }

            var now = _clock.UtcNow;
            var contractId = Guid.NewGuid();
            var planner = plannerCandidates[0];
            var safeTitle = _redaction.Redact(request.Title).Value;
            var safeObjective = _redaction.Redact(request.Objective).Value;
            var contractResult = await _contracts.CreateAsync(new PlanningExecutionContractRequest(
                request.ProjectId,
                contractId,
                1,
                request.OwnerReference,
                planner.Id,
                new PlanningWorkItem(
                    PlanningWorkItemSource.Manual,
                    request.WorkItemReference ?? $"desktop:{request.RequestId:D}",
                    safeTitle),
                new PlanningRepositoryTarget(PlanningRepositoryMode.LocalGit, view.Project.LocalPath, repository.BranchName, repository.HeadSha),
                [new PlanningScopeClause("objective", safeObjective)],
                request.Constraints.Select((value, index) => new PlanningScopeClause($"constraint-{index + 1}", _redaction.Redact(value).Value)).ToArray(),
                [new PlanningScopeClause("forbidden-default", "Do not modify files outside the approved bounded work request or its prepared workspace.")],
                [new PlanningDeliverable("bounded-result", "Complete the approved bounded request and return truthful execution evidence.", true)],
                BuildValidations(request.ValidationExpectations),
                request.AcceptanceCriteria.Select((value, index) => new PlanningAcceptanceCriterion($"criterion-{index + 1}", _redaction.Redact(value).Value, true)).ToArray(),
                [new PlanningExecutionBudget(PlanningBudgetKind.Attempts, 1), new PlanningExecutionBudget(PlanningBudgetKind.ElapsedMinutes, 30)],
                BuildStopConditions()), cancellationToken).ConfigureAwait(false);
            if (!contractResult.Succeeded || contractResult.Contract is null)
            {
                return await FailAsync(ExecutionPreparationStatus.PlanningContractFailed, ExecutionPreparationStage.PlanningContract, contractResult.ErrorMessage ?? contractResult.Status.ToString()).ConfigureAwait(false);
            }

            var contract = contractResult.Contract;
            var nodeId = Guid.NewGuid();
            var graphResult = await _graphs.CreateAsync(new WorkGraphCreationRequest(
                request.ProjectId,
                Guid.NewGuid(),
                WorkGraphSchema.CurrentVersion,
                now,
                [new WorkGraphNode(nodeId, contract.Reference)],
                Array.Empty<WorkGraphEdge>()), cancellationToken).ConfigureAwait(false);
            if (!graphResult.Succeeded || graphResult.Graph is null)
            {
                return await FailAsync(ExecutionPreparationStatus.WorkGraphFailed, ExecutionPreparationStage.WorkGraph, graphResult.ErrorMessage ?? graphResult.Status.ToString()).ConfigureAwait(false);
            }

            var policyId = view.Context.RoutingPolicyReference ?? $"desktop-routing:{request.ProjectId:D}";
            if (_redaction.ValidateIdentityText(policyId).RequiresRedaction)
            {
                return await FailAsync(ExecutionPreparationStatus.RoutingFailed, ExecutionPreparationStage.Routing, "The project routing policy reference crossed the redaction boundary.").ConfigureAwait(false);
            }

            var policy = new RoutingPolicySnapshot(
                policyId,
                request.Classification.RequiredRole,
                capacityRequirement: request.Classification.CapacityRequirement,
                independentReviewRequired: request.Classification.IndependentReviewRequired,
                securityReviewRequired: request.Classification.SecurityReviewRequired,
                ownerApprovalRequired: request.Classification.OwnerApprovalRequired,
                requireSupportedConnection: request.Classification.RequiresSupportedConnection,
                requireVerifiedAvailability: request.Classification.RequiresVerifiedAvailability,
                requireAuthenticatedAccess: request.Classification.RequiresAuthenticatedAccess,
                requireVerifiedEntitlement: request.Classification.RequiresVerifiedEntitlement,
                policyReference: view.Context.RoutingPolicyReference,
                reason: "Owner-authored bounded work request.");
            var routingResult = await _routing.CreateAsync(new RoutingDecisionRequest(
                request.ProjectId,
                contract.Reference,
                request.Classification,
                policy), cancellationToken).ConfigureAwait(false);
            if (!routingResult.Succeeded || routingResult.Decision is null || routingResult.Decision.SelectedAgentId is null)
            {
                return await FailAsync(ExecutionPreparationStatus.RoutingFailed, ExecutionPreparationStage.Routing, routingResult.ErrorMessage ?? "Routing produced no eligible executor.").ConfigureAwait(false);
            }

            var executor = view.EffectiveAgents.FirstOrDefault(agent => agent.Id == routingResult.Decision.SelectedAgentId.Value);
            if (executor is null)
            {
                return await FailAsync(ExecutionPreparationStatus.RoutingFailed, ExecutionPreparationStage.Routing, "Routing selected an agent outside the current project context.").ConfigureAwait(false);
            }

            var handoffResult = await _handoffs.CreateAsync(new HandoffPackageCreationRequest(
                request.ProjectId,
                Guid.NewGuid(),
                HandoffTransition.PlannerToExecutor,
                contract.Reference,
                now,
                workGraphReference: graphResult.Graph.Reference,
                workGraphNodeId: nodeId,
                limitations: ["Execution is bounded to the prepared workspace and exact authority references."],
                nextAction: "Execute the bounded request through the exact resolved adapter."), cancellationToken).ConfigureAwait(false);
            if (!handoffResult.Succeeded || handoffResult.Package is null)
            {
                return await FailAsync(ExecutionPreparationStatus.HandoffFailed, ExecutionPreparationStage.Handoff, handoffResult.ErrorMessage ?? handoffResult.Status.ToString()).ConfigureAwait(false);
            }

            var workspaceId = Guid.NewGuid();
            var planResult = await _workspacePlanning.CreatePlanAsync(new WorkspacePreparationRequest(
                request.ProjectId,
                workspaceId,
                Guid.NewGuid(),
                request.RequestId,
                contract.Reference,
                repository.HeadSha,
                $"apo/execute/{request.RequestId:N}",
                WorkspacePreparationPolicy.RequireCleanSource,
                graphResult.Graph.Reference,
                nodeId,
                routingResult.Decision.Reference), cancellationToken).ConfigureAwait(false);
            if (!planResult.Succeeded || planResult.Plan is null)
            {
                return await FailAsync(ExecutionPreparationStatus.WorkspacePlanFailed, ExecutionPreparationStage.WorkspacePlan, planResult.ErrorMessage ?? planResult.Status.ToString()).ConfigureAwait(false);
            }

            var approval = new WorkspacePreparationApproval(
                Guid.NewGuid(),
                planResult.Plan.Reference,
                request.OwnerReference,
                _clock.UtcNow,
                "Owner explicitly prepared this bounded execution workspace.");
            var preparedResult = await _workspace.PrepareAsync(planResult.Plan.Reference, approval, cancellationToken).ConfigureAwait(false);
            if (!preparedResult.Succeeded)
            {
                return await FailAsync(ExecutionPreparationStatus.WorkspacePreparationFailed, ExecutionPreparationStage.Workspace, preparedResult.ErrorMessage ?? preparedResult.Status.ToString()).ConfigureAwait(false);
            }

            var receiptResult = await _workspace.FinalizeReceiptAsync(planResult.Plan.Reference, approval, cancellationToken).ConfigureAwait(false);
            if (!receiptResult.Succeeded || receiptResult.Receipt is null)
            {
                return await FailAsync(ExecutionPreparationStatus.WorkspacePreparationFailed, ExecutionPreparationStage.Workspace, receiptResult.ErrorMessage ?? "Prepared workspace receipt was not durably recorded.").ConfigureAwait(false);
            }

            var checkpointResult = await _recovery.CreateAsync(new RecoveryCheckpointCreationRequest(
                request.ProjectId,
                Guid.NewGuid(),
                RecoveryCheckpointLifecycleState.Ready,
                contract.Reference,
                [
                    receiptResult.Receipt.ToRecoveryEvidenceReference(_clock.UtcNow, RecoveryEvidenceFreshness.Verified),
                    new RecoveryEvidenceReference(Guid.NewGuid(), RecoveryEvidenceKind.Routing, $"routing-decision:{routingResult.Decision.DecisionId:D}", _clock.UtcNow, RecoveryEvidenceFreshness.Verified, contentHash: routingResult.Decision.ContentHash)
                ],
                nextSafeAction: RecoveryNextSafeAction.ContinueFromCheckpoint,
                explanation: "All pre-execution authorities are durable; execution remains bounded by the exact request.",
                createdAt: _clock.UtcNow,
                workGraphReference: graphResult.Graph.Reference,
                workGraphNodeId: nodeId,
                handoffPackageReference: handoffResult.Package.Reference,
                selectedAgentRoleReferences: [
                    new RecoveryAgentRoleReference(planner.Id, AgentRole.Planner, "configured planner"),
                    new RecoveryAgentRoleReference(executor.Id, AgentRole.Executor, "routing decision")
                ]), cancellationToken).ConfigureAwait(false);
            if (!checkpointResult.Succeeded || checkpointResult.Checkpoint is null)
            {
                return await FailAsync(ExecutionPreparationStatus.RecoveryCheckpointFailed, ExecutionPreparationStage.RecoveryCheckpoint, checkpointResult.ErrorMessage ?? checkpointResult.Status.ToString()).ConfigureAwait(false);
            }

            var runId = Guid.NewGuid();
            var exactRequest = new BoundedExecutionRequest(
                request.ProjectId,
                runId,
                contract.Reference,
                graphResult.Graph.Reference,
                nodeId,
                handoffResult.Package.Reference,
                routingResult.Decision.Reference,
                planResult.Plan.Reference,
                checkpointResult.Checkpoint.Reference);
            var prepared = new PreparedExecution(
                runId,
                planner,
                executor,
                contract,
                graphResult.Graph,
                handoffResult.Package,
                planResult.Plan,
                receiptResult.Receipt,
                checkpointResult.Checkpoint,
                routingResult.Decision,
                exactRequest);
            await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _prepared = prepared;
                _state = ExecutionCoordinatorState.Ready;
                _message = "Ready to start through the bounded execution service.";
            }
            finally
            {
                _stateGate.Release();
            }

            return new(ExecutionPreparationStatus.Prepared, ExecutionPreparationStage.Execution, prepared);
        }
        catch (OperationCanceledException)
        {
            return await FailAsync(ExecutionPreparationStatus.Cancelled, ExecutionPreparationStage.None, "Preparation was cancelled; no execution was started.").ConfigureAwait(false);
        }
        catch (Exception)
        {
            return await FailAsync(ExecutionPreparationStatus.Failed, ExecutionPreparationStage.None, "The orchestration request could not be prepared safely.").ConfigureAwait(false);
        }
    }

    public async Task<ExecutionStartResult> StartAsync(CancellationToken cancellationToken = default)
    {
        PreparedExecution? prepared;
        CancellationTokenSource runCancellation;
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state is ExecutionCoordinatorState.Running or ExecutionCoordinatorState.Cancelling)
            {
                return new(_state, _lastResult, "An execution is already active.");
            }

            if (_prepared is null)
            {
                return new(ExecutionCoordinatorState.Draft, ErrorMessage: "Prepare a bounded request before starting it.");
            }

            prepared = _prepared;
            runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runCancellation = runCancellation;
            _state = ExecutionCoordinatorState.Running;
            _message = "Running through the bounded execution service.";
        }
        finally
        {
            _stateGate.Release();
        }

        BoundedExecutionResult result;
        try
        {
            result = await _execution.ExecuteAsync(prepared.Request, runCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result = new(BoundedExecutionStatus.Cancelled, ErrorMessage: "Execution cancellation was observed.");
        }
        finally
        {
            runCancellation.Dispose();
        }

        var state = MapState(result.Status);
        await _stateGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            _runCancellation = null;
            _lastResult = result;
            _state = state;
            _message = result.ErrorMessage ?? result.AdapterResult?.Summary;
        }
        finally
        {
            _stateGate.Release();
        }

        return new(state, result, result.ErrorMessage);
    }

    public async Task<ExecutionCancellationResult> CancelAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? cancellation;
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state != ExecutionCoordinatorState.Running || _runCancellation is null)
            {
                return new(false, "No bounded execution is currently running.");
            }

            _state = ExecutionCoordinatorState.Cancelling;
            _message = "Cancellation requested; waiting for the bounded service to confirm termination.";
            cancellation = _runCancellation;
        }
        finally
        {
            _stateGate.Release();
        }

        cancellation.Cancel();
        return new(true);
    }

    public async Task<ExecutionRunSnapshot> GetCurrentRunAsync(CancellationToken cancellationToken = default)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return new(_prepared?.RunId, _state, _message, _lastResult);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private async Task<ExecutionPreparationResult> FailAsync(
        ExecutionPreparationStatus status,
        ExecutionPreparationStage stage,
        string message)
    {
        await _stateGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            _prepared = null;
            _state = status == ExecutionPreparationStatus.Cancelled ? ExecutionCoordinatorState.Cancelled : ExecutionCoordinatorState.Failed;
            _message = message;
        }
        finally
        {
            _stateGate.Release();
        }

        return new(status, stage, ErrorMessage: message);
    }

    private static IReadOnlyList<PlanningValidationRequirement> BuildValidations(IReadOnlyList<string> expectations)
    {
        if (expectations.Count == 0)
        {
            return [new PlanningValidationRequirement("manual-review", PlanningValidationKind.ManualInspection, "Owner reviews the bounded result and evidence.", true)];
        }

        return expectations
            .Select((value, index) => new PlanningValidationRequirement($"expectation-{index + 1}", PlanningValidationKind.Custom, value, false))
            .ToArray();
    }

    private static IReadOnlyList<PlanningStopCondition> BuildStopConditions() =>
    [
        new("immutable-target-moved", PlanningStopConditionKind.ImmutableTargetMoved, "Stop if the source identity no longer matches the prepared authority."),
        new("scope-violation", PlanningStopConditionKind.ScopeViolation, "Stop if the bounded request would exceed its approved scope."),
        new("validation-failure", PlanningStopConditionKind.ValidationFailure, "Stop when a required validation fails."),
        new("budget-exceeded", PlanningStopConditionKind.BudgetExceeded, "Stop after the one-attempt or elapsed-time budget is exhausted."),
        new("credential-required", PlanningStopConditionKind.CredentialRequired, "Stop when required access is unavailable."),
        new("owner-approval-required", PlanningStopConditionKind.OwnerApprovalRequired, "Stop when a new owner decision is required."),
        new("context-insufficient", PlanningStopConditionKind.ContextInsufficient, "Stop when project context is no longer sufficient."),
        new("security-boundary", PlanningStopConditionKind.SecurityBoundaryReached, "Stop at a security boundary or redaction failure.")
    ];

    private static ExecutionCoordinatorState MapState(BoundedExecutionStatus status) =>
        status switch
        {
            BoundedExecutionStatus.Succeeded => ExecutionCoordinatorState.Completed,
            BoundedExecutionStatus.Cancelled => ExecutionCoordinatorState.Cancelled,
            BoundedExecutionStatus.AlreadyStarted or BoundedExecutionStatus.ResidualExecutionActive or BoundedExecutionStatus.ProjectBusy => ExecutionCoordinatorState.Waiting,
            BoundedExecutionStatus.CheckpointApprovalRequired or BoundedExecutionStatus.AgentUnavailable or BoundedExecutionStatus.ConnectionUnsupported => ExecutionCoordinatorState.HumanActionRequired,
            _ => ExecutionCoordinatorState.Failed
        };
}
