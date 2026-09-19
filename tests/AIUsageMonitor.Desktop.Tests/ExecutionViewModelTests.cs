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
    public async Task PrepareBlockedReasonExplainsEachMissingRequiredInputInPriorityOrder()
    {
        var viewModel = NewViewModel(new FakeExecutionCoordinator());
        viewModel.SetPersistenceAvailability(true);

        Assert.Equal("Select a registered project.", viewModel.PrepareBlockedReason);

        viewModel.ProjectOptions.Add(new MissionControlProjectOption(Project()));
        viewModel.SelectedProject = viewModel.ProjectOptions[0];
        await WaitUntil(() => viewModel.State == ExecutionCoordinatorState.Draft);

        Assert.Equal("Enter a work request.", viewModel.PrepareBlockedReason);

        viewModel.Title = "Bounded change";
        Assert.Equal("Enter a work request.", viewModel.PrepareBlockedReason);

        viewModel.Objective = "Make the requested bounded change.";
        Assert.Equal("Add acceptance criteria in Owner Mode.", viewModel.PrepareBlockedReason);

        viewModel.AcceptanceCriteria = "Verify the result.";
        Assert.Equal(string.Empty, viewModel.PrepareBlockedReason);
        Assert.True(viewModel.PrepareCommand.CanExecute(null));
    }

    [Fact]
    public async Task CompleteAuthoritativePrefillMakesPrepareReadyWithoutAdvancedEntry()
    {
        var coordinator = new FakeExecutionCoordinator { Preparation = PreparedResult() };
        var viewModel = NewViewModel(coordinator);
        viewModel.SetPersistenceAvailability(true);
        viewModel.ProjectOptions.Add(new MissionControlProjectOption(Project()));
        viewModel.SelectedProject = viewModel.ProjectOptions[0];

        viewModel.ApplyContextPrefill(new ExecutionContextPrefill(
            "Persisted planning contract",
            "Known work",
            "Use the stored bounded request.",
            "APO-70",
            ["The stored acceptance criterion is preserved."],
            ["Keep the existing coordinator."],
            ["Run focused tests."]));

        Assert.True(viewModel.PrepareCommand.CanExecute(null));
        viewModel.PrepareCommand.Execute(null);
        await coordinator.PrepareRequested.Task;
        await WaitUntil(() => viewModel.IsReady);

        Assert.NotNull(coordinator.LastRequest);
        Assert.Equal("Known work", coordinator.LastRequest!.Title);
        Assert.Equal("APO-70", coordinator.LastRequest.WorkItemReference);
        Assert.Equal(["The stored acceptance criterion is preserved."], coordinator.LastRequest.AcceptanceCriteria);
    }

    [Fact]
    public void PartialAuthoritativeContextExposesTheOnlyMissingRequirementInOwnerMode()
    {
        var viewModel = NewViewModel(new FakeExecutionCoordinator());
        viewModel.SetPersistenceAvailability(true);
        viewModel.ProjectOptions.Add(new MissionControlProjectOption(Project()));
        viewModel.SelectedProject = viewModel.ProjectOptions[0];
        viewModel.ApplyContextPrefill(new ExecutionContextPrefill(
            "Mission Control current-work read model",
            "Known work",
            "The current request is known."));

        Assert.False(viewModel.PrepareCommand.CanExecute(null));
        Assert.Equal("Missing before Prepare: acceptance criteria. APO will not invent them.", viewModel.MissingContextText);
        Assert.Equal("Add acceptance criteria in Owner Mode.", viewModel.PrepareBlockedReason);
    }

    [Fact]
    public async Task SelectingAnotherProjectDoesNotReuseThePreviousProjectContext()
    {
        var coordinator = new FakeExecutionCoordinator();
        var viewModel = NewViewModel(coordinator);
        viewModel.SetPersistenceAvailability(true);
        viewModel.ProjectOptions.Add(new MissionControlProjectOption(Project("First")));
        viewModel.ProjectOptions.Add(new MissionControlProjectOption(Project("Second")));

        viewModel.SelectedProject = viewModel.ProjectOptions[0];
        await WaitUntil(() => coordinator.RestoreCalls == 1 && viewModel.State == ExecutionCoordinatorState.Draft);
        viewModel.ApplyContextPrefill(new ExecutionContextPrefill(
            "Project One authoritative context",
            "First work",
            "First objective",
            "APO-ONE",
            ["First acceptance"],
            ["First constraint"],
            ["First validation"]));
        Assert.True(viewModel.PrepareCommand.CanExecute(null));

        viewModel.SelectedProject = viewModel.ProjectOptions[1];
        await WaitUntil(() => coordinator.RestoreCalls == 2 && viewModel.State == ExecutionCoordinatorState.Draft);

        Assert.Equal(string.Empty, viewModel.Title);
        Assert.Equal(string.Empty, viewModel.Objective);
        Assert.Equal(string.Empty, viewModel.WorkItemReference);
        Assert.Equal(string.Empty, viewModel.AcceptanceCriteria);
        Assert.Equal(string.Empty, viewModel.Constraints);
        Assert.Equal(string.Empty, viewModel.ValidationExpectations);
        Assert.Equal("Title is derived from the owner request when Prepare runs.", viewModel.TitleSourceText);
        Assert.Equal("Project registry metadata is available; no authoritative current work is selected yet.", viewModel.ContextSourceText);
        Assert.Equal("No current work selected.", viewModel.CurrentWorkText);
        Assert.False(viewModel.PrepareCommand.CanExecute(null));
        Assert.Equal("Enter a work request.", viewModel.PrepareBlockedReason);

        viewModel.ApplyContextPrefill(new ExecutionContextPrefill(
            "Project Two Mission Control current-work read model",
            "Second work",
            workItemReference: "APO-TWO"));

        Assert.Equal("Second work", viewModel.Title);
        Assert.Equal("APO-TWO", viewModel.WorkItemReference);
        Assert.Equal(string.Empty, viewModel.Objective);
        Assert.Equal(string.Empty, viewModel.AcceptanceCriteria);
        Assert.Equal(string.Empty, viewModel.Constraints);
        Assert.Equal(string.Empty, viewModel.ValidationExpectations);
        Assert.False(viewModel.PrepareCommand.CanExecute(null));
    }

    [Fact]
    public async Task OwnerRequestDerivesTitleWithoutDuplicatingTheRequest()
    {
        var coordinator = new FakeExecutionCoordinator { Preparation = PreparedResult() };
        var viewModel = NewViewModel(coordinator);
        viewModel.ProjectOptions.Add(new MissionControlProjectOption(Project()));
        viewModel.SelectedProject = viewModel.ProjectOptions[0];
        viewModel.SetPersistenceAvailability(true);
        viewModel.Objective = "  Make the owner request work.\r\nAdd no invented scope. ";
        viewModel.AcceptanceCriteria = "Verify the request.";

        viewModel.PrepareCommand.Execute(null);
        await coordinator.PrepareRequested.Task;

        Assert.Equal("Make the owner request work.", coordinator.LastRequest!.Title);
        Assert.Equal("Make the owner request work.\r\nAdd no invented scope.", coordinator.LastRequest.Objective);
    }

    [Fact]
    public async Task PrepareIsTruthfullyBlockedWhileRestoreIsActiveAndReadinessRefreshesWhenItFinishes()
    {
        var coordinator = new FakeExecutionCoordinator { BlockFirstRestore = true };
        var viewModel = NewViewModel(coordinator);
        viewModel.SetPersistenceAvailability(true);
        viewModel.ProjectOptions.Add(new MissionControlProjectOption(Project()));

        viewModel.SelectedProject = viewModel.ProjectOptions[0];
        viewModel.Title = "Bounded change";
        viewModel.Objective = "Make the requested bounded change.";
        viewModel.AcceptanceCriteria = "Verify the result.";

        await coordinator.RestoreStarted.Task;

        Assert.Equal(ExecutionCoordinatorState.Preparing, viewModel.State);
        Assert.False(viewModel.PrepareCommand.CanExecute(null));
        Assert.Equal("Wait for persisted execution recovery to finish.", viewModel.PrepareBlockedReason);

        coordinator.ReleaseRestore.TrySetResult(true);
        await WaitUntil(() => viewModel.State == ExecutionCoordinatorState.Draft);

        Assert.True(viewModel.PrepareCommand.CanExecute(null));
        Assert.Equal(string.Empty, viewModel.PrepareBlockedReason);
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

    [Fact]
    public async Task RestoredReadyAuthorityShowsPersistedPlannerLineageAndEnablesStart()
    {
        var coordinator = new FakeExecutionCoordinator
        {
            RestoreResult = new ExecutionRehydrationResult(ExecutionRehydrationStatus.Restored, CreatePreparedExecution(plannerAsLineageOnly: true))
        };
        var viewModel = NewViewModel(coordinator);
        viewModel.SetPersistenceAvailability(true);
        viewModel.ProjectOptions.Add(new MissionControlProjectOption(Project()));

        viewModel.SelectedProject = viewModel.ProjectOptions[0];
        await WaitUntil(() => viewModel.IsReady);

        Assert.Equal("Persisted lineage", viewModel.PlannerText);
        Assert.Equal("Bounded execution", viewModel.Title);
        Assert.Equal("done", viewModel.AcceptanceCriteria);
        Assert.True(viewModel.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task StaleRestoreCannotOverwriteNewProjectSelection()
    {
        var coordinator = new FakeExecutionCoordinator { BlockFirstRestore = true };
        var viewModel = NewViewModel(coordinator);
        viewModel.SetPersistenceAvailability(true);
        viewModel.ProjectOptions.Add(new MissionControlProjectOption(Project("First")));
        viewModel.ProjectOptions.Add(new MissionControlProjectOption(Project("Second")));

        viewModel.SelectedProject = viewModel.ProjectOptions[0];
        await coordinator.RestoreStarted.Task;
        viewModel.ApplyContextPrefill(new ExecutionContextPrefill(
            "First project authoritative context",
            "First work",
            "First objective",
            "APO-ONE",
            ["First acceptance"],
            ["First constraint"],
            ["First validation"]));
        viewModel.SelectedProject = viewModel.ProjectOptions[1];
        coordinator.ReleaseRestore.TrySetResult(true);
        await WaitUntil(() => coordinator.RestoreCalls == 2 && viewModel.State == ExecutionCoordinatorState.Draft);

        Assert.Equal("Second", viewModel.SelectedProjectText);
        Assert.False(viewModel.IsReady);
        Assert.Equal(string.Empty, viewModel.Title);
        Assert.Equal(string.Empty, viewModel.Objective);
        Assert.Equal(string.Empty, viewModel.WorkItemReference);
        Assert.Equal(string.Empty, viewModel.AcceptanceCriteria);
        Assert.Equal(string.Empty, viewModel.Constraints);
        Assert.Equal(string.Empty, viewModel.ValidationExpectations);
    }

    [Fact]
    public async Task SelectingAnotherProjectIsIgnoredWhileExecutionIsRunning()
    {
        var coordinator = new FakeExecutionCoordinator { Preparation = PreparedResult(), BlockStart = true };
        var viewModel = ReadyInput(coordinator);
        await Prepare(viewModel, coordinator);
        viewModel.ProjectOptions.Add(new MissionControlProjectOption(Project("Second")));

        viewModel.StartCommand.Execute(null);
        await coordinator.StartRequested.Task;
        await WaitUntil(() => viewModel.State == ExecutionCoordinatorState.Running);

        var firstProject = viewModel.SelectedProject;
        viewModel.SelectedProject = viewModel.ProjectOptions[1];

        Assert.Same(firstProject, viewModel.SelectedProject);
        Assert.Equal("Bounded change", viewModel.Title);

        coordinator.ReleaseStart.TrySetResult(true);
        await WaitUntil(() => viewModel.State == ExecutionCoordinatorState.Completed);
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

    [Fact]
    public async Task PrepareCommandEntersPreparingStateExactlyOnceAndCallsCoordinatorOnce()
    {
        var coordinator = new FakeExecutionCoordinator { BlockPrepare = true, Preparation = PreparedResult() };
        var viewModel = ReadyInput(coordinator);
        await WaitUntil(() => viewModel.State == ExecutionCoordinatorState.Draft);

        viewModel.PrepareCommand.Execute(null);
        await coordinator.PrepareRequested.Task;

        Assert.Equal(ExecutionCoordinatorState.Preparing, viewModel.State);
        Assert.True(viewModel.PrepareCommand.IsExecuting);
        Assert.False(viewModel.PrepareCommand.CanExecute(null));

        viewModel.PrepareCommand.Execute(null);

        coordinator.ReleasePrepare.TrySetResult(true);
        await WaitUntil(() => viewModel.IsReady);

        Assert.Equal(1, coordinator.PrepareCalls);
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
        internal bool BlockPrepare { get; init; }
        internal bool BlockFirstRestore { get; init; }
        internal ExecutionRehydrationResult RestoreResult { get; init; } = new(ExecutionRehydrationStatus.NotResumable);
        internal readonly TaskCompletionSource<bool> PrepareRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> StartRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> CancelRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> ReleaseStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> ReleasePrepare = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> RestoreStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> ReleaseRestore = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int RestoreCalls;
        internal int PrepareCalls;
        internal OrchestrationWorkRequest? LastRequest;

        public async Task<ExecutionPreparationResult> PrepareAsync(OrchestrationWorkRequest request, CancellationToken cancellationToken = default)
        {
            PrepareCalls++;
            LastRequest = request;
            PrepareRequested.TrySetResult(true);
            if (BlockPrepare) await ReleasePrepare.Task;
            return Preparation;
        }

        public async Task<ExecutionStartResult> StartAsync(CancellationToken cancellationToken = default)
        {
            StartRequested.TrySetResult(true);
            if (BlockStart) await ReleaseStart.Task;
            return StartResult;
        }

        public async Task<ExecutionRehydrationResult> RestoreAsync(Guid projectId, CancellationToken cancellationToken = default)
        {
            RestoreCalls++;
            RestoreStarted.TrySetResult(true);
            if (BlockFirstRestore && RestoreCalls == 1)
            {
                await ReleaseRestore.Task;
            }

            return RestoreResult;
        }

        public Task<ExecutionCancellationResult> CancelAsync(CancellationToken cancellationToken = default)
        {
            CancelRequested.TrySetResult(true);
            return Task.FromResult(new ExecutionCancellationResult(true));
        }

        public Task<ExecutionRunSnapshot> GetCurrentRunAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ExecutionRunSnapshot(null, ExecutionCoordinatorState.Draft, null, null));
    }

    private static PreparedExecution CreatePreparedExecution(bool plannerAsLineageOnly = false)
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
        return new PreparedExecution(runId, plannerAsLineageOnly ? null : planner, executor, contract, graph, handoff, plan, receipt, checkpoint, routing, request);
    }

    private static EffectiveAgentDefinition Agent(AgentRole role, Guid projectId, string name) => new(projectId, new AgentDefinition(Guid.NewGuid(), name, role.ToString(), AgentConnectionMode.Cli, AgentAvailability.Available, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, provider: "OpenAI", roleCapabilities: [role], supportedConnectionModes: [AgentConnectionMode.Cli], authenticationState: AgentAuthenticationState.Authenticated, entitlementState: AgentEntitlementState.VerifiedAvailable, modelIdentifier: "gpt-test"), null);
}
