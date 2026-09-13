using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Routing;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Application.Workspaces;

namespace AIUsageMonitor.Connection.Tests;

public sealed class ExecutionPreparationCoordinatorTests
{
    [Fact]
    public void OwnerRequestCreatesItsOwnAuthorityIdAndNormalizesOptionalLines()
    {
        var projectId = Guid.NewGuid();
        var request = new OrchestrationWorkRequest(projectId, "owner", "Bounded change", "Make the requested bounded change.", acceptanceCriteria: ["First criterion", "", "Second criterion"], constraints: [" Keep the scope local. "]);

        Assert.NotEqual(Guid.Empty, request.RequestId);
        Assert.Equal(projectId, request.ProjectId);
        Assert.Equal(["First criterion", "Second criterion"], request.AcceptanceCriteria);
        Assert.Equal(["Keep the scope local."], request.Constraints);
    }

    [Fact]
    public void OwnerRequestRequiresAtLeastOneAcceptanceCriterion() =>
        Assert.Throws<ArgumentException>(() => new OrchestrationWorkRequest(Guid.NewGuid(), "owner", "Bounded change", "Make the requested bounded change.", acceptanceCriteria: []));

    [Fact]
    public async Task PrepareAsyncBuildsExactAuthorityChainAndPreservesOwnerIntent()
    {
        var fixture = new Fixture();
        var request = fixture.Request();

        var result = await fixture.Coordinator.PrepareAsync(request);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.NotNull(result.PreparedExecution);
        Assert.Equal(request.OwnerReference, fixture.Contract.LastRequest!.OwnerReference);
        Assert.Equal(request.AcceptanceCriteria, fixture.Planner.LastPlan!.AcceptanceCriteria);
        Assert.Equal(request.Constraints, fixture.Planner.LastPlan.Constraints);
        Assert.Equal(fixture.Contract.Value!.Reference, result.PreparedExecution!.Contract.Reference);
        Assert.Equal(fixture.Graph.Value!.Reference, result.PreparedExecution.Graph.Reference);
        Assert.Equal(fixture.Routing.Value!.Reference, result.PreparedExecution.RoutingDecision.Reference);
        Assert.Equal(fixture.WorkspacePlan.Value!.Reference, result.PreparedExecution.WorkspacePlan.Reference);
        Assert.Equal(fixture.Recovery.Checkpoint!.Reference, result.PreparedExecution.Checkpoint.Reference);
        Assert.Equal(ExecutionCoordinatorState.Ready, (await fixture.Coordinator.GetCurrentRunAsync()).State);
    }

    [Fact]
    public async Task PrepareAsyncRejectsPlannerResultBeforeCreatingDownstreamAuthorities()
    {
        var fixture = new Fixture { InvalidPlanner = true };
        var result = await fixture.Coordinator.PrepareAsync(fixture.Request());

        Assert.Equal(ExecutionPreparationStatus.PlannerInvalid, result.Status);
        Assert.Equal(ExecutionPreparationStage.Planning, result.Stage);
        Assert.Equal(0, fixture.Contract.Calls);
        Assert.Equal(0, fixture.Graph.Calls);
        Assert.Equal(0, fixture.Routing.Calls);
        Assert.Equal(0, fixture.Handoff.Calls);
        Assert.Equal(0, fixture.WorkspacePlan.Calls);
        Assert.Equal(0, fixture.Recovery.Calls);
    }

    [Theory]
    [InlineData(FailurePoint.Contract, ExecutionPreparationStatus.PlanningContractFailed, ExecutionPreparationStage.PlanningContract)]
    [InlineData(FailurePoint.Graph, ExecutionPreparationStatus.WorkGraphFailed, ExecutionPreparationStage.WorkGraph)]
    [InlineData(FailurePoint.Policy, ExecutionPreparationStatus.RoutingFailed, ExecutionPreparationStage.Routing)]
    [InlineData(FailurePoint.Routing, ExecutionPreparationStatus.RoutingFailed, ExecutionPreparationStage.Routing)]
    [InlineData(FailurePoint.Handoff, ExecutionPreparationStatus.HandoffFailed, ExecutionPreparationStage.Handoff)]
    [InlineData(FailurePoint.WorkspacePlan, ExecutionPreparationStatus.WorkspacePlanFailed, ExecutionPreparationStage.WorkspacePlan)]
    [InlineData(FailurePoint.Workspace, ExecutionPreparationStatus.WorkspacePreparationFailed, ExecutionPreparationStage.Workspace)]
    [InlineData(FailurePoint.Recovery, ExecutionPreparationStatus.RecoveryCheckpointFailed, ExecutionPreparationStage.RecoveryCheckpoint)]
    public async Task PrepareAsyncMapsAuthorityFailureToTheCorrectStage(FailurePoint failure, ExecutionPreparationStatus expectedStatus, ExecutionPreparationStage expectedStage)
    {
        var fixture = new Fixture { Failure = failure };
        var result = await fixture.Coordinator.PrepareAsync(fixture.Request());

        Assert.Equal(expectedStatus, result.Status);
        Assert.NotNull(result.ErrorMessage);
        Assert.Equal(expectedStage, result.Stage);
        Assert.Null(result.PreparedExecution);
    }

    [Fact]
    public async Task PrepareAsyncPropagatesCancellationWithoutCallingDownstreamAuthorities()
    {
        var fixture = new Fixture { BlockPlanner = true };
        using var cancellation = new CancellationTokenSource();
        var prepare = fixture.Coordinator.PrepareAsync(fixture.Request(), cancellation.Token);
        await fixture.Planner.Started.Task;

        cancellation.Cancel();
        var result = await prepare;

        Assert.Equal(ExecutionPreparationStatus.Cancelled, result.Status);
        Assert.Equal(0, fixture.Contract.Calls);
    }

    [Fact]
    public async Task StartAndCancelEndsInTerminalCancelledState()
    {
        var fixture = new Fixture();
        var prepared = await fixture.Coordinator.PrepareAsync(fixture.Request());
        Assert.True(prepared.Succeeded, prepared.ErrorMessage);
        var start = fixture.Coordinator.StartAsync();
        await fixture.Execution.Started.Task;

        var cancellation = await fixture.Coordinator.CancelAsync();
        var result = await start;

        Assert.True(cancellation.Requested);
        Assert.Equal(ExecutionCoordinatorState.Cancelled, result.State);
        Assert.Equal(BoundedExecutionStatus.Cancelled, result.Result!.Status);
        Assert.Equal(ExecutionCoordinatorState.Cancelled, (await fixture.Coordinator.GetCurrentRunAsync()).State);
    }

    [Fact]
    public async Task StartAsyncIsNotReentrantWhileExecutionIsActive()
    {
        var fixture = new Fixture();
        var prepared = await fixture.Coordinator.PrepareAsync(fixture.Request());
        Assert.True(prepared.Succeeded, prepared.ErrorMessage);
        var first = fixture.Coordinator.StartAsync();
        await fixture.Execution.Started.Task;

        var second = await fixture.Coordinator.StartAsync();

        Assert.Equal(ExecutionCoordinatorState.Running, second.State);
        Assert.Contains("already active", second.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        await fixture.Coordinator.CancelAsync();
        await first;
    }

    public enum FailurePoint { None, Contract, Graph, Policy, Routing, Handoff, WorkspacePlan, Workspace, Recovery }

    private sealed class Fixture
    {
        internal readonly DateTimeOffset Now = new(2026, 9, 13, 10, 0, 0, TimeSpan.Zero);
        internal readonly Guid ProjectId = Guid.NewGuid();
        internal readonly string WorkspacePath = @"C:\apo-test";
        internal readonly EffectiveAgentDefinition Executor;
        internal readonly FakePlanner Planner;
        internal readonly FakeContract Contract;
        internal readonly FakeGraph Graph;
        internal readonly FakeRoutingPolicy Policy;
        internal readonly FakeRouting Routing;
        internal readonly FakeHandoff Handoff;
        internal readonly FakeWorkspacePlan WorkspacePlan;
        internal readonly FakeWorkspace Workspace;
        internal readonly FakeRecovery Recovery;
        internal readonly FakeExecution Execution;
        internal readonly ExecutionCoordinator Coordinator;
        internal bool InvalidPlanner;
        internal bool BlockPlanner;
        internal FailurePoint Failure;

        internal Fixture()
        {
            var project = new Project(ProjectId, "APO", WorkspacePath, "main", ProjectStatus.Active, Now, Now);
            var plannerAgent = Agent(AgentRole.Planner, ProjectId);
            Executor = Agent(AgentRole.Executor, ProjectId);
            var context = new ProjectContextReference(ProjectId, Guid.NewGuid(), ProjectContextContract.CurrentVersion, Now, Now,
                new ProjectRepositoryContextReference(ProjectId, WorkspacePath, RepositorySelectionState.Inspect, RepositoryVerificationStatus.AvailableClean, WorkspacePath, true, "main", false, [], Now),
                new ProjectTrackerContextReference(TrackerReferenceState.Skipped),
                [new ProjectModelRoleReference(plannerAgent.Id, plannerAgent.Name, [AgentRole.Planner], true, plannerAgent.Availability, plannerAgent.AuthenticationState, plannerAgent.EntitlementState), new ProjectModelRoleReference(Executor.Id, Executor.Name, [AgentRole.Executor], true, Executor.Availability, Executor.AuthenticationState, Executor.EntitlementState)],
                new ProjectCurrentWorkReference(CurrentWorkState.NotSelected), [], null, null, ProjectNextSafeAction.ReadyForPlanning);

            Planner = new FakePlanner(this);
            Contract = new FakeContract(this, project);
            Graph = new FakeGraph(this);
            Policy = new FakeRoutingPolicy(this, Executor.Id);
            Routing = new FakeRouting(this);
            Handoff = new FakeHandoff(this);
            WorkspacePlan = new FakeWorkspacePlan(this);
            Workspace = new FakeWorkspace(this);
            Recovery = new FakeRecovery(this);
            Execution = new FakeExecution();
            Coordinator = new ExecutionCoordinator(new FakeContext(new ProjectContextView(project, context, [plannerAgent, Executor])), new FakeRepository(), Contract, Graph, Routing, Policy, new FakePlannerResolver(Planner), Handoff, WorkspacePlan, Workspace, Recovery, Execution, new HandoffRedactionService(), new FixedClock(Now));
        }

        internal OrchestrationWorkRequest Request() => new(ProjectId, "owner:apo", "Bounded execution", "Execute the bounded request.", "APO-70", ["Preserve the owner criterion"], ["Stay in the prepared workspace"], classification: Classification());

        private static RoutingTaskClassification Classification() => new(RoutingScopeScale.Bounded, RoutingTaskRisk.Low, RoutingBlastRadius.Local, RoutingValidationCost.Low, AgentRole.Executor, capacityRequirement: RoutingCapacityRequirement.NotApplicable, requiresAuthenticatedAccess: true, requiresVerifiedAvailability: true, requiresVerifiedEntitlement: true);

        private EffectiveAgentDefinition Agent(AgentRole role, Guid projectId) => new(projectId, new AgentDefinition(Guid.NewGuid(), $"Codex {role}", role.ToString(), AgentConnectionMode.Cli, AgentAvailability.Available, true, Now, Now, provider: "OpenAI", roleCapabilities: [role], supportedConnectionModes: [AgentConnectionMode.Cli], authenticationState: AgentAuthenticationState.Authenticated, entitlementState: AgentEntitlementState.VerifiedAvailable, modelIdentifier: "gpt-test"), null);
    }

    private sealed class FixedClock(DateTimeOffset value) : IClock { public DateTimeOffset UtcNow { get; } = value; }
    private sealed class FakeContext(ProjectContextView view) : IProjectContextResolver { public Task<ProjectContextResolution> ResolveAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(new ProjectContextResolution(ProjectContextResolutionState.Ready, view)); }
    private sealed class FakeRepository : IProjectRepositoryStateService
    {
        public Task<RepositoryStateSnapshot> VerifyAsync(Project project, CancellationToken cancellationToken = default) => Task.FromResult(new RepositoryStateSnapshot(project.Id, project.LocalPath, new LocalRepositoryInspection(RepositoryVerificationStatus.AvailableClean, project.LocalPath, project.LocalPath, true, "main", false, new string('a', 40), new string('a', 7), isClean: true)));
    }
    private sealed class FakePlannerResolver(FakePlanner planner) : IPlannerAdapterResolver { public PlannerAdapterResolution Resolve(EffectiveAgentDefinition value) => new(PlannerAdapterResolutionStatus.Resolved, planner); }

    private sealed class FakePlanner(Fixture fixture) : IPlannerAdapter
    {
        internal readonly TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal PlannerPlan? LastPlan;
        public PlannerAdapterDescriptor Descriptor { get; } = new("test-planner", [AgentConnectionMode.Cli]);
        public async Task<PlannerInvocationResult> PlanAsync(PlannerInvocationRequest request, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(true);
            if (fixture.BlockPlanner) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (fixture.InvalidPlanner) return new(PlannerInvocationStatus.InvalidResult, ErrorMessage: "planner output was invalid");
            LastPlan = new PlannerPlan(request.OwnerRequest.Objective, ["prepared workspace"], request.OwnerRequest.AcceptanceCriteria, request.OwnerRequest.Constraints, [new PlanningValidationRequirement("focused", PlanningValidationKind.Test, "Run focused tests", true)], request.OwnerRequest.Classification, [new PlanningStopCondition("target", PlanningStopConditionKind.ImmutableTargetMoved, "target"), new PlanningStopCondition("scope", PlanningStopConditionKind.ScopeViolation, "scope"), new PlanningStopCondition("budget", PlanningStopConditionKind.BudgetExceeded, "budget")], [new PlanningExecutionBudget(PlanningBudgetKind.Attempts, 1), new PlanningExecutionBudget(PlanningBudgetKind.ElapsedMinutes, 1)]);
            return new(PlannerInvocationStatus.Succeeded, LastPlan);
        }
    }

    private sealed class FakeContract(Fixture fixture, Project project) : IPlanningExecutionContractService
    {
        internal int Calls;
        internal PlanningExecutionContract? Value;
        internal PlanningExecutionContractRequest? LastRequest;
        public Task<PlanningExecutionContractCreationResult> CreateAsync(PlanningExecutionContractRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            if (fixture.Failure == FailurePoint.Contract) return Task.FromResult(new PlanningExecutionContractCreationResult(PlanningExecutionContractCreationStatus.PersistenceUnavailable, ErrorMessage: "contract failure"));
            Value = CreateContract(project.Id, request.PlannerAgentId, project.LocalPath, fixture.Now);
            return Task.FromResult(new PlanningExecutionContractCreationResult(PlanningExecutionContractCreationStatus.Created, Value));
        }
    }
    private sealed class FakeGraph(Fixture fixture) : IWorkGraphService
    {
        internal int Calls;
        internal WorkGraph? Value;
        public Task<WorkGraphCreationResult> CreateAsync(WorkGraphCreationRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (fixture.Failure == FailurePoint.Graph) return Task.FromResult(new WorkGraphCreationResult(WorkGraphCreationStatus.PersistenceUnavailable, ErrorMessage: "graph failure"));
            Value = new WorkGraph(request.ProjectId, request.GraphId, request.SchemaVersion, request.CreatedAt, request.Nodes, request.Edges);
            return Task.FromResult(new WorkGraphCreationResult(WorkGraphCreationStatus.Created, Value, request.Nodes[0].NodeId));
        }
    }
    private sealed class FakeRoutingPolicy(Fixture fixture, Guid executorId) : IExecutableRoutingPolicyResolver
    {
        public Task<ExecutableRoutingPolicyResolution> ResolveAsync(Guid projectId, RoutingTaskClassification classification, string? contextPolicyReference = null, CancellationToken cancellationToken = default) => fixture.Failure == FailurePoint.Policy
            ? Task.FromResult(new ExecutableRoutingPolicyResolution(ExecutableRoutingPolicyResolutionStatus.PersistenceUnavailable, ErrorMessage: "policy failure"))
            : Task.FromResult(new ExecutableRoutingPolicyResolution(ExecutableRoutingPolicyResolutionStatus.Resolved, new RoutingPolicySnapshot("test-policy", AgentRole.Executor, [executorId], capacityRequirement: RoutingCapacityRequirement.NotApplicable, requireAuthenticatedAccess: true, requireVerifiedAvailability: true, requireVerifiedEntitlement: true)));
    }
    private sealed class FakeRouting(Fixture fixture) : IRoutingDecisionService
    {
        internal int Calls;
        internal RoutingDecision? Value;
        public Task<RoutingDecisionCreationResult> CreateAsync(RoutingDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (fixture.Failure == FailurePoint.Routing) return Task.FromResult(new RoutingDecisionCreationResult(RoutingDecisionCreationStatus.DecisionConflict, ErrorMessage: "routing failure"));
            var input = new RoutingInputSnapshot(request.ProjectId, request.PlanningContractReference, new RoutingContextReference(Guid.NewGuid(), 1, fixture.Now), request.Classification, request.Policy, [RoutingAgentSnapshot.FromEffective(fixture.Executor)], [], null, fixture.Now);
            Value = new RoutingDecision(request.ProjectId, Guid.NewGuid(), RoutingDecisionSchema.CurrentVersion, fixture.Now, new RoutingDecisionEngine().Evaluate(input));
            return Task.FromResult(new RoutingDecisionCreationResult(RoutingDecisionCreationStatus.Created, Value));
        }
    }
    private sealed class FakeHandoff(Fixture fixture) : IHandoffPackageService
    {
        internal int Calls;
        internal HandoffPackage? Value;
        public Task<HandoffPackageCreationResult> CreateAsync(HandoffPackageCreationRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (fixture.Failure == FailurePoint.Handoff) return Task.FromResult(new HandoffPackageCreationResult(HandoffPackageCreationStatus.PersistenceUnavailable, ErrorMessage: "handoff failure"));
            Value = CreateHandoff(request.ProjectId, fixture.Contract.Value!, fixture.Graph.Value!, fixture.Now);
            return Task.FromResult(new HandoffPackageCreationResult(HandoffPackageCreationStatus.Created, Value));
        }
    }
    private sealed class FakeWorkspacePlan(Fixture fixture) : IWorkspacePreparationPlanningService
    {
        internal int Calls;
        internal WorkspacePreparationPlan? Value;
        public Task<WorkspacePreparationPlanningResult> CreatePlanAsync(WorkspacePreparationRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (fixture.Failure == FailurePoint.WorkspacePlan) return Task.FromResult(new WorkspacePreparationPlanningResult(WorkspacePreparationPlanningStatus.PersistenceUnavailable, ErrorMessage: "workspace plan failure"));
            Value = CreatePlan(request, fixture.Now, fixture.WorkspacePath);
            return Task.FromResult(new WorkspacePreparationPlanningResult(WorkspacePreparationPlanningStatus.Planned, Value));
        }
    }
    private sealed class FakeWorkspace(Fixture fixture) : IWorkspacePreparationService
    {
        internal int Calls;
        internal WorkspacePreparationReceipt? Receipt;
        public Task<WorkspacePreparationResult> PrepareAsync(WorkspacePreparationPlanReference planReference, WorkspacePreparationApproval? approval, CancellationToken cancellationToken = default)
        {
            Calls++;
            return fixture.Failure == FailurePoint.Workspace ? Task.FromResult(new WorkspacePreparationResult(WorkspacePreparationStatus.PersistenceUnavailable, ErrorMessage: "workspace failure")) : Task.FromResult(new WorkspacePreparationResult(WorkspacePreparationStatus.Prepared));
        }
        public Task<WorkspacePreparationResult> FinalizeReceiptAsync(WorkspacePreparationPlanReference planReference, WorkspacePreparationApproval approval, CancellationToken cancellationToken = default)
        {
            Receipt = new WorkspacePreparationReceipt(fixture.ProjectId, fixture.WorkspacePlan.Value!.WorkspaceId, fixture.WorkspacePlan.Value.CorrelationId, fixture.Now, planReference, fixture.WorkspacePath, fixture.WorkspacePlan.Value.WorkspaceBranch, new string('a', 40), new string('a', 40), fixture.WorkspacePath, "owner:apo");
            return Task.FromResult(new WorkspacePreparationResult(WorkspacePreparationStatus.Prepared, Receipt));
        }
    }
    private sealed class FakeRecovery(Fixture fixture) : IRecoveryCheckpointService
    {
        internal int Calls;
        internal RecoveryCheckpointCreationResult? Value;
        internal RecoveryCheckpoint? Checkpoint => Value?.Checkpoint;
        public Task<RecoveryCheckpointCreationResult> CreateAsync(RecoveryCheckpointCreationRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (fixture.Failure == FailurePoint.Recovery) return Task.FromResult(new RecoveryCheckpointCreationResult(RecoveryCheckpointCreationStatus.PersistenceUnavailable, ErrorMessage: "recovery failure"));
            var checkpoint = new RecoveryCheckpoint(request.ProjectId, request.CheckpointId, RecoveryCheckpointSchema.CurrentVersion, fixture.Now, request.LifecycleState, new RecoveryContextReference(Guid.NewGuid(), 1, fixture.Now), request.PlanningContractReference, request.WorkGraphReference!, request.WorkGraphNodeId!.Value, request.HandoffPackageReference!, selectedAgentRoleReferences: [new RecoveryAgentRoleReference(Guid.NewGuid(), AgentRole.Executor)]);
            var head = new ContinuationHead(request.ProjectId, ContinuationHeadSchema.CurrentVersion, 1, checkpoint.Reference, checkpoint.Reference, fixture.Now);
            Value = new RecoveryCheckpointCreationResult(RecoveryCheckpointCreationStatus.Created, checkpoint, head);
            return Task.FromResult(Value);
        }
    }
    private sealed class FakeExecution : IBoundedExecutionService
    {
        internal readonly TaskCompletionSource<CancellationToken> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<BoundedExecutionResult> ExecuteAsync(BoundedExecutionRequest request, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(cancellationToken);
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ContinueWith(static _ => new BoundedExecutionResult(BoundedExecutionStatus.Cancelled), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private static PlanningExecutionContract CreateContract(Guid projectId, Guid agentId, string workspacePath, DateTimeOffset now) => new(projectId, Guid.NewGuid(), PlanningExecutionContractSchema.CurrentVersion, 1, now, "owner:apo", agentId, new PlanningContextBinding(Guid.NewGuid(), ProjectContextContract.CurrentVersion), new PlanningWorkItem(PlanningWorkItemSource.Manual, "APO-70", "Bounded execution"), new PlanningRepositoryTarget(PlanningRepositoryMode.LocalGit, workspacePath, "main", new string('a', 40)), [new PlanningScopeClause("include", "prepared workspace")], [], [new PlanningScopeClause("forbid", "unrelated work")], [new PlanningDeliverable("result", "bounded result", true)], [new PlanningValidationRequirement("test", PlanningValidationKind.Test, "focused test", true)], [new PlanningAcceptanceCriterion("criterion", "done", true)], [new PlanningExecutionBudget(PlanningBudgetKind.Attempts, 1), new PlanningExecutionBudget(PlanningBudgetKind.ElapsedMinutes, 1)], [new PlanningStopCondition("target", PlanningStopConditionKind.ImmutableTargetMoved, "target"), new PlanningStopCondition("scope", PlanningStopConditionKind.ScopeViolation, "scope"), new PlanningStopCondition("budget", PlanningStopConditionKind.BudgetExceeded, "budget")], [], null, null);
    private static HandoffPackage CreateHandoff(Guid projectId, PlanningExecutionContract contract, WorkGraph graph, DateTimeOffset now)
    {
        var scope = new HandoffExecutionScope([new PlanningScopeClause("include", "prepared workspace")], [], [new PlanningScopeClause("forbid", "unrelated work")], [new PlanningDeliverable("result", "bounded result", true)], [new PlanningValidationRequirement("test", PlanningValidationKind.Test, "focused test", true)], contract.ExecutionBudgets, contract.StopConditions, [], null, null);
        return new HandoffPackage(projectId, Guid.NewGuid(), HandoffPackageSchema.CurrentVersion, now, HandoffTransition.PlannerToExecutor, HandoffRole.Planner, HandoffRole.Executor, contract.Reference, contract.WorkItem, new HandoffContextReference(contract.Context.ProjectContextId, 1, now, now), new PlanningRepositoryTarget(PlanningRepositoryMode.None), graph.Reference, graph.Nodes[0].NodeId, null, scope, null, null, null, [], [], [], null, [], "execute", new HandoffRedactionMetadata(false, 0, []), new HandoffPackageSizeMetadata(HandoffPackageLimits.MaxCanonicalPayloadBytes, 0, 0, 0, 0, 0, 9));
    }
    private static WorkspacePreparationPlan CreatePlan(WorkspacePreparationRequest request, DateTimeOffset now, string workspacePath) => new(request.ProjectId, request.WorkspaceId, request.PlanId, request.CorrelationId, now, new WorkspaceContextIdentity(request.ProjectId, request.ContractReference.ContractId, ProjectContextContract.CurrentVersion, now), request.ContractReference, request.WorkGraphReference, request.WorkGraphNodeId, request.RoutingDecisionReference, new WorkspaceRepositoryDiscovery(WorkspaceRepositoryDiscoveryStatus.Available, workspacePath, workspacePath, workspacePath, headCommitSha: new string('a', 40), branchName: "main", isClean: true), new string('a', 40), request.WorkspaceBranch, workspacePath, request.Policy, true, "prepared");
}
