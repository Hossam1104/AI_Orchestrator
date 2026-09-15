using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Routing;
using AIUsageMonitor.Application.Workspaces;
using AIUsageMonitor.Desktop.ViewModels;

namespace AIUsageMonitor.Desktop.Tests;

public sealed class ExecutionViewModelTests
{
    [Fact]
    public void PrepareRemainsUnavailableUntilOwnerProvidesRequiredInput()
    {
        var viewModel = NewViewModel(new FakeExecutionCoordinator());
        viewModel.SetPersistenceAvailability(true);
        viewModel.ProjectOptions.Add(new MissionControlProjectOption(Project()));
        viewModel.SelectedProject = viewModel.ProjectOptions[0];

        Assert.False(viewModel.PrepareCommand.CanExecute(null));
        viewModel.Title = "Bounded change";
        viewModel.Objective = "Make the requested bounded change.";
        viewModel.AcceptanceCriteria = "Verify the result.";

        Assert.True(viewModel.PrepareCommand.CanExecute(null));
    }

    [Fact]
    public async Task PreparePublishesReadyAuthoritySummaryAndEnablesStart()
    {
        var coordinator = new FakeExecutionCoordinator { Preparation = new ExecutionPreparationResult(ExecutionPreparationStatus.Prepared, ExecutionPreparationStage.Execution, CreatePreparedExecution()) };
        var viewModel = ReadyInput(coordinator);

        viewModel.PrepareCommand.Execute(null);
        await coordinator.PrepareRequested.Task;
        await WaitUntil(() => viewModel.IsReady);

        Assert.Equal(ExecutionCoordinatorState.Ready, viewModel.State);
        Assert.Equal("Configured planner", viewModel.PlannerText);
        Assert.Equal("Configured executor", viewModel.ExecutorText);
        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.CancelCommand.CanExecute(null));
    }

    [Fact]
    public async Task PrepareFailureIsShownAndDoesNotEnableStart()
    {
        var coordinator = new FakeExecutionCoordinator { Preparation = new ExecutionPreparationResult(ExecutionPreparationStatus.RepositoryNotClean, ExecutionPreparationStage.Repository, ErrorMessage: "The source worktree is not clean.") };
        var viewModel = ReadyInput(coordinator);

        viewModel.PrepareCommand.Execute(null);
        await coordinator.PrepareRequested.Task;
        await WaitUntil(() => viewModel.State == ExecutionCoordinatorState.Failed);

        Assert.Equal("The source worktree is not clean.", viewModel.ErrorMessage);
        Assert.False(viewModel.IsReady);
        Assert.False(viewModel.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task StartPublishesTerminalResultAndKeepsAcceptancePending()
    {
        var coordinator = new FakeExecutionCoordinator { Preparation = PreparedResult(), StartResult = new ExecutionStartResult(ExecutionCoordinatorState.Completed, new BoundedExecutionResult(BoundedExecutionStatus.Succeeded)) };
        var viewModel = ReadyInput(coordinator);
        await Prepare(viewModel, coordinator);

        viewModel.StartCommand.Execute(null);
        await coordinator.StartRequested.Task;
        await WaitUntil(() => viewModel.State == ExecutionCoordinatorState.Completed);

        Assert.Equal("Succeeded", viewModel.TerminalResultText);
        Assert.Equal("Owner validation and acceptance are still pending.", viewModel.ValidationStateText);
        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.CancelCommand.CanExecute(null));
    }

    [Fact]
    public async Task CancelTransitionsRunningExecutionThroughCancellingToTerminalState()
    {
        var coordinator = new FakeExecutionCoordinator { Preparation = PreparedResult(), BlockStart = true, StartResult = new ExecutionStartResult(ExecutionCoordinatorState.Cancelled, new BoundedExecutionResult(BoundedExecutionStatus.Cancelled)) };
        var viewModel = ReadyInput(coordinator);
        await Prepare(viewModel, coordinator);

        viewModel.StartCommand.Execute(null);
        await coordinator.StartRequested.Task;
        await WaitUntil(() => viewModel.State == ExecutionCoordinatorState.Running);
        Assert.True(viewModel.CancelCommand.CanExecute(null));

        viewModel.CancelCommand.Execute(null);
        await coordinator.CancelRequested.Task;
        await WaitUntil(() => viewModel.State == ExecutionCoordinatorState.Cancelling);
        coordinator.ReleaseStart.TrySetResult(true);
        await WaitUntil(() => viewModel.State == ExecutionCoordinatorState.Cancelled);

        Assert.Equal("Cancelled", viewModel.TerminalResultText);
        Assert.False(viewModel.CancelCommand.CanExecute(null));
    }

    [Fact]
    public async Task SelectingAnotherProjectResetsPreparedAuthority()
    {
        var coordinator = new FakeExecutionCoordinator { Preparation = PreparedResult() };
        var viewModel = ReadyInput(coordinator);
        await Prepare(viewModel, coordinator);
        viewModel.ProjectOptions.Add(new MissionControlProjectOption(Project("Second")));

        viewModel.SelectedProject = viewModel.ProjectOptions[1];

        Assert.Equal(ExecutionCoordinatorState.Draft, viewModel.State);
        Assert.False(viewModel.IsReady);
        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.Equal("Not resolved", viewModel.PlannerText);
    }

    private static ExecutionViewModel NewViewModel(FakeExecutionCoordinator coordinator) => new(null, coordinator);

    private static ExecutionViewModel ReadyInput(FakeExecutionCoordinator coordinator)
    {
        var viewModel = NewViewModel(coordinator);
        viewModel.ProjectOptions.Add(new MissionControlProjectOption(Project()));
        viewModel.SelectedProject = viewModel.ProjectOptions[0];
        viewModel.Title = "Bounded change";
        viewModel.Objective = "Make the requested bounded change.";
        viewModel.AcceptanceCriteria = "Verify the result.";
        viewModel.SetPersistenceAvailability(true);
        return viewModel;
    }

    private static async Task Prepare(ExecutionViewModel viewModel, FakeExecutionCoordinator coordinator)
    {
        viewModel.PrepareCommand.Execute(null);
        await coordinator.PrepareRequested.Task;
        await WaitUntil(() => viewModel.IsReady);
    }

    private static async Task WaitUntil(Func<bool> predicate)
    {
        for (var i = 0; i < 100 && !predicate(); i++) await Task.Delay(10);
        Assert.True(predicate());
    }

    private static Project Project(string name = "Test project") => new(Guid.NewGuid(), name, Environment.CurrentDirectory, "main", ProjectStatus.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static ExecutionPreparationResult PreparedResult() => new(ExecutionPreparationStatus.Prepared, ExecutionPreparationStage.Execution, CreatePreparedExecution());

    private sealed class FakeExecutionCoordinator : IExecutionCoordinator
    {
        internal ExecutionPreparationResult Preparation { get; init; } = new(ExecutionPreparationStatus.Failed);
        internal ExecutionStartResult StartResult { get; init; } = new(ExecutionCoordinatorState.Completed);
        internal bool BlockStart { get; init; }
        internal readonly TaskCompletionSource<bool> PrepareRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> StartRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> CancelRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> ReleaseStart = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ExecutionPreparationResult> PrepareAsync(OrchestrationWorkRequest request, CancellationToken cancellationToken = default)
        {
            PrepareRequested.TrySetResult(true);
            return Task.FromResult(Preparation);
        }

        public async Task<ExecutionStartResult> StartAsync(CancellationToken cancellationToken = default)
        {
            StartRequested.TrySetResult(true);
            if (BlockStart) await ReleaseStart.Task;
            return StartResult;
        }

        public Task<ExecutionRehydrationResult> RestoreAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExecutionRehydrationResult(ExecutionRehydrationStatus.NotResumable, ErrorMessage: "No durable Ready checkpoint is safely resumable for this project."));

        public Task<ExecutionCancellationResult> CancelAsync(CancellationToken cancellationToken = default)
        {
            CancelRequested.TrySetResult(true);
            return Task.FromResult(new ExecutionCancellationResult(true));
        }

        public Task<ExecutionRunSnapshot> GetCurrentRunAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ExecutionRunSnapshot(null, ExecutionCoordinatorState.Draft, null, null));
    }

    private static PreparedExecution CreatePreparedExecution()
    {
        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.NewGuid();
        var workspacePath = Environment.CurrentDirectory;
        var contextId = Guid.NewGuid();
        var planner = Agent(AgentRole.Planner, projectId, "Configured planner");
        var executor = Agent(AgentRole.Executor, projectId, "Configured executor");
        var contract = new PlanningExecutionContract(projectId, Guid.NewGuid(), PlanningExecutionContractSchema.CurrentVersion, 1, now, "owner:test", planner.Id, new PlanningContextBinding(contextId, ProjectContextContract.CurrentVersion), new PlanningWorkItem(PlanningWorkItemSource.Manual, "desktop:test", "Bounded execution"), new PlanningRepositoryTarget(PlanningRepositoryMode.LocalGit, workspacePath, "main", new string('a', 40)), [new PlanningScopeClause("include", "one bounded step")], [], [new PlanningScopeClause("forbid", "unrelated work")], [new PlanningDeliverable("result", "bounded result", true)], [new PlanningValidationRequirement("test", PlanningValidationKind.Test, "focused test", true)], [new PlanningAcceptanceCriterion("criterion", "done", true)], [new PlanningExecutionBudget(PlanningBudgetKind.Attempts, 1), new PlanningExecutionBudget(PlanningBudgetKind.ElapsedMinutes, 1), new PlanningExecutionBudget(PlanningBudgetKind.ToolInvocations, 10), new PlanningExecutionBudget(PlanningBudgetKind.ModelTurns, 2)], [new PlanningStopCondition("target", PlanningStopConditionKind.ImmutableTargetMoved, "target"), new PlanningStopCondition("scope", PlanningStopConditionKind.ScopeViolation, "scope"), new PlanningStopCondition("budget", PlanningStopConditionKind.BudgetExceeded, "budget")], [], null, null);
        var node = new WorkGraphNode(Guid.NewGuid(), contract.Reference);
        var graph = new WorkGraph(projectId, Guid.NewGuid(), WorkGraphSchema.CurrentVersion, now, [node], []);
        var classification = new RoutingTaskClassification(RoutingScopeScale.Bounded, RoutingTaskRisk.Low, RoutingBlastRadius.Local, RoutingValidationCost.Low, AgentRole.Executor, capacityRequirement: RoutingCapacityRequirement.NotApplicable, requiresAuthenticatedAccess: true, requiresVerifiedAvailability: true, requiresVerifiedEntitlement: true);
        var policy = new RoutingPolicySnapshot("test-policy", AgentRole.Executor, [executor.Id], capacityRequirement: RoutingCapacityRequirement.NotApplicable, requireAuthenticatedAccess: true, requireVerifiedAvailability: true, requireVerifiedEntitlement: true);
        var routing = new RoutingDecision(projectId, Guid.NewGuid(), RoutingDecisionSchema.CurrentVersion, now, new RoutingDecisionEngine().Evaluate(new RoutingInputSnapshot(projectId, contract.Reference, new RoutingContextReference(contextId, 1, now), classification, policy, [RoutingAgentSnapshot.FromEffective(executor)], [], null, now)));
        var scope = new HandoffExecutionScope([new PlanningScopeClause("include", "one bounded step")], [], [new PlanningScopeClause("forbid", "unrelated work")], [new PlanningDeliverable("result", "bounded result", true)], [new PlanningValidationRequirement("test", PlanningValidationKind.Test, "focused test", true)], contract.ExecutionBudgets, contract.StopConditions, [], null, null);
        var handoff = new HandoffPackage(projectId, Guid.NewGuid(), HandoffPackageSchema.CurrentVersion, now, HandoffTransition.PlannerToExecutor, HandoffRole.Planner, HandoffRole.Executor, contract.Reference, contract.WorkItem, new HandoffContextReference(contextId, 1, now, now), new PlanningRepositoryTarget(PlanningRepositoryMode.None), graph.Reference, node.NodeId, null, scope, null, null, null, [], [], [], null, [], "Execute bounded work", new HandoffRedactionMetadata(false, 0, []), new HandoffPackageSizeMetadata(HandoffPackageLimits.MaxCanonicalPayloadBytes, 0, 0, 0, 0, 0, 11));
        var plan = new WorkspacePreparationPlan(projectId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now, new WorkspaceContextIdentity(projectId, contextId, ProjectContextContract.CurrentVersion, now), contract.Reference, graph.Reference, node.NodeId, routing.Reference, new WorkspaceRepositoryDiscovery(WorkspaceRepositoryDiscoveryStatus.Available, workspacePath, workspacePath, workspacePath, headCommitSha: new string('a', 40), branchName: "main", isClean: true), new string('a', 40), "apo-test", workspacePath, WorkspacePreparationPolicy.RequireCleanSource, true, "prepared");
        var receipt = new WorkspacePreparationReceipt(projectId, plan.WorkspaceId, plan.CorrelationId, now, plan.Reference, workspacePath, plan.WorkspaceBranch, new string('a', 40), new string('a', 40), workspacePath, "owner:test");
        var checkpoint = new RecoveryCheckpoint(projectId, Guid.NewGuid(), RecoveryCheckpointSchema.CurrentVersion, now, RecoveryCheckpointLifecycleState.Ready, new RecoveryContextReference(contextId, ProjectContextContract.CurrentVersion, now), contract.Reference, graph.Reference, node.NodeId, handoff.Reference, selectedAgentRoleReferences: [new RecoveryAgentRoleReference(planner.Id, AgentRole.Planner), new RecoveryAgentRoleReference(executor.Id, AgentRole.Executor)]);
        var runId = Guid.NewGuid();
        var request = new BoundedExecutionRequest(projectId, runId, contract.Reference, graph.Reference, node.NodeId, handoff.Reference, routing.Reference, plan.Reference, checkpoint.Reference);
        return new PreparedExecution(runId, planner, executor, contract, graph, handoff, plan, receipt, checkpoint, routing, request);
    }

    private static EffectiveAgentDefinition Agent(AgentRole role, Guid projectId, string name) => new(projectId, new AgentDefinition(Guid.NewGuid(), name, role.ToString(), AgentConnectionMode.Cli, AgentAvailability.Available, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, provider: "OpenAI", roleCapabilities: [role], supportedConnectionModes: [AgentConnectionMode.Cli], authenticationState: AgentAuthenticationState.Authenticated, entitlementState: AgentEntitlementState.VerifiedAvailable, modelIdentifier: "gpt-test"), null);
}
