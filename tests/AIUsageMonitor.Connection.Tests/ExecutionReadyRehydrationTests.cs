using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Routing;
using AIUsageMonitor.Application.Workspaces;

namespace AIUsageMonitor.Connection.Tests;

public sealed class ExecutionReadyRehydrationTests
{
    [Fact]
    public async Task ReadyAuthority_RehydratesExactAuthoritiesWithoutPlannerRoutingOrWorkspacePreparation()
    {
        var fixture = new Fixture();

        var result = await fixture.Rehydrator.TryRehydrateAsync(fixture.Project.Id);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(fixture.Contract.Reference, result.PreparedExecution!.Contract.Reference);
        Assert.Equal(fixture.Graph.Reference, result.PreparedExecution.Graph.Reference);
        Assert.Equal(fixture.Node.NodeId, result.PreparedExecution.Request.WorkGraphNodeId);
        Assert.Equal(fixture.Handoff.Reference, result.PreparedExecution.Handoff.Reference);
        Assert.Equal(fixture.Routing.Reference, result.PreparedExecution.RoutingDecision.Reference);
        Assert.Equal(fixture.Plan.Reference, result.PreparedExecution.WorkspacePlan.Reference);
        Assert.Equal(fixture.Checkpoint.Reference, result.PreparedExecution.Checkpoint.Reference);
        Assert.Equal(fixture.Executor.Id, result.PreparedExecution.Executor.Id);
        Assert.Null(result.PreparedExecution.Planner);
        Assert.Equal(1, fixture.Resolver.Calls);
        Assert.Equal(1, fixture.WorkspaceInspection.Calls);
    }

    [Fact]
    public async Task ReadyAuthority_RehydratesEquivalentCanonicalWorkspacePath()
    {
        var fixture = new Fixture(receiptWorkspacePath: EquivalentWorkspacePath());

        var result = await fixture.Rehydrator.TryRehydrateAsync(fixture.Project.Id);

        Assert.True(result.Succeeded, result.ErrorMessage);
    }

    [Fact]
    public async Task ReadyAuthority_RejectsDifferentWorkspacePath()
    {
        var fixture = new Fixture(receiptWorkspacePath: @"C:\apo-other-workspace");

        var result = await fixture.Rehydrator.TryRehydrateAsync(fixture.Project.Id);

        Assert.Equal(ExecutionRehydrationStatus.WorkspaceUnavailable, result.Status);
        Assert.Null(result.PreparedExecution);
    }

    [Theory]
    [InlineData(Failure.SourceMoved, ExecutionRehydrationStatus.SourceMoved)]
    [InlineData(Failure.SourceDirty, ExecutionRehydrationStatus.SourceDirty)]
    [InlineData(Failure.WorkspaceMissing, ExecutionRehydrationStatus.WorkspaceUnavailable)]
    [InlineData(Failure.ExecutorDisabled, ExecutionRehydrationStatus.ExecutorUnavailable)]
    [InlineData(Failure.RoutingMismatch, ExecutionRehydrationStatus.AuthorityMismatch)]
    [InlineData(Failure.Superseded, ExecutionRehydrationStatus.NotResumable)]
    public async Task UnsafePersistedAuthority_FailsClosedWithoutExecutor(Failure failure, ExecutionRehydrationStatus expected)
    {
        var fixture = new Fixture(failure);

        var result = await fixture.Rehydrator.TryRehydrateAsync(fixture.Project.Id);

        Assert.Equal(expected, result.Status);
        Assert.Null(result.PreparedExecution);
    }

    [Fact]
    public async Task ConsumedReadyCheckpoint_FailsClosedBeforeWorkspaceOrExecutorRestore()
    {
        var fixture = new Fixture();
        fixture.Claims.Authority = fixture.CreateClaim();

        var result = await fixture.Rehydrator.TryRehydrateAsync(fixture.Project.Id);

        Assert.Equal(ExecutionRehydrationStatus.AlreadyStarted, result.Status);
        Assert.Null(result.PreparedExecution);
        Assert.Equal(1, fixture.Resolver.Calls);
        Assert.Equal(0, fixture.WorkspaceInspection.Calls);
    }

    public enum Failure { None, SourceMoved, SourceDirty, WorkspaceMissing, ExecutorDisabled, RoutingMismatch, Superseded }

    private static string EquivalentWorkspacePath() => OperatingSystem.IsWindows()
        ? @"C:\APO-WORKSPACE\"
        : @"C:\apo-workspace" + Path.DirectorySeparatorChar;

    private sealed class Fixture
    {
        private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
        private const string SourcePath = @"C:\apo-source";
        private const string WorkspacePath = @"C:\apo-workspace";
        private const string Head = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        internal readonly Project Project;
        internal readonly PlanningExecutionContract Contract;
        internal readonly WorkGraph Graph;
        internal readonly WorkGraphNode Node;
        internal readonly HandoffPackage Handoff;
        internal readonly RoutingDecision Routing;
        internal readonly WorkspacePreparationPlan Plan;
        internal readonly RecoveryCheckpoint Checkpoint;
        internal readonly EffectiveAgentDefinition Executor;
        internal readonly FakeResolver Resolver;
        internal readonly FakeWorkspaceInspection WorkspaceInspection;
        internal readonly ClaimRepository Claims = new();
        internal readonly ReadyExecutionRehydrator Rehydrator;

        internal Fixture(Failure failure = Failure.None, string? receiptWorkspacePath = null)
        {
            var projectId = Guid.NewGuid();
            var plannerId = Guid.NewGuid();
            Project = new Project(projectId, "Recovery test", SourcePath, "main", ProjectStatus.Active, Now, Now);
            Executor = Agent(projectId, "Executor");
            Contract = new PlanningExecutionContract(projectId, Guid.NewGuid(), PlanningExecutionContractSchema.CurrentVersion, 1, Now,
                "owner:test", plannerId, new PlanningContextBinding(Guid.NewGuid(), ProjectContextContract.CurrentVersion),
                new PlanningWorkItem(PlanningWorkItemSource.Manual, "APO-70", "Recovery"),
                new PlanningRepositoryTarget(PlanningRepositoryMode.LocalGit, SourcePath, "main", Head),
                [new PlanningScopeClause("include", "prepared workspace")], [], [new PlanningScopeClause("forbid", "replay")],
                [new PlanningDeliverable("result", "bounded result", true)],
                [new PlanningValidationRequirement("test", PlanningValidationKind.Test, "focused", true)],
                [new PlanningAcceptanceCriterion("criterion", "done", true)],
                [new PlanningExecutionBudget(PlanningBudgetKind.Attempts, 1), new PlanningExecutionBudget(PlanningBudgetKind.ElapsedMinutes, 1)],
                [new PlanningStopCondition("target", PlanningStopConditionKind.ImmutableTargetMoved, "target"), new PlanningStopCondition("scope", PlanningStopConditionKind.ScopeViolation, "scope"), new PlanningStopCondition("budget", PlanningStopConditionKind.BudgetExceeded, "budget")], [], null, null);
            Node = new WorkGraphNode(Guid.NewGuid(), Contract.Reference);
            Graph = new WorkGraph(projectId, Guid.NewGuid(), WorkGraphSchema.CurrentVersion, Now, [Node], []);
            Routing = CreateRouting(projectId, Contract, Executor);
            Handoff = CreateHandoff(projectId, Contract, Graph, Node);
            Plan = new WorkspacePreparationPlan(projectId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now,
                new WorkspaceContextIdentity(projectId, Contract.Context.ProjectContextId, ProjectContextContract.CurrentVersion, Now), Contract.Reference,
                Graph.Reference, Node.NodeId, Routing.Reference,
                new WorkspaceRepositoryDiscovery(WorkspaceRepositoryDiscoveryStatus.Available, SourcePath, SourcePath, SourcePath, isBareRepository: false, headCommitSha: Head, branchName: "main", isClean: true),
                Head, "apo-recovery", WorkspacePath, WorkspacePreparationPolicy.RequireCleanSource, true, "prepared");
            Checkpoint = new RecoveryCheckpoint(projectId, Guid.NewGuid(), RecoveryCheckpointSchema.CurrentVersion, Now,
                RecoveryCheckpointLifecycleState.Ready, new RecoveryContextReference(Contract.Context.ProjectContextId, ProjectContextContract.CurrentVersion, Now),
                Contract.Reference, Graph.Reference, Node.NodeId, Handoff.Reference, Routing.Reference, Plan.Reference,
                selectedAgentRoleReferences: [new RecoveryAgentRoleReference(plannerId, AgentRole.Planner), new RecoveryAgentRoleReference(Executor.Id, AgentRole.Executor)],
                nextSafeAction: RecoveryNextSafeAction.ContinueFromCheckpoint);

            Resolver = new FakeResolver(new SmartContinueResult(
                failure == Failure.Superseded ? SmartContinueResolutionState.Blocked : SmartContinueResolutionState.Resumable,
                projectId, Checkpoint.Reference, Checkpoint.Reference, LatestLifecycleState: failure == Failure.Superseded ? RecoveryCheckpointLifecycleState.Waiting : RecoveryCheckpointLifecycleState.Ready,
                NextSafeAction: failure == Failure.Superseded ? RecoveryNextSafeAction.ResolveBlocker : RecoveryNextSafeAction.ContinueFromCheckpoint));
            WorkspaceInspection = new FakeWorkspaceInspection(new WorkspacePreparationReceipt(projectId, Plan.WorkspaceId, Plan.CorrelationId, Now, Plan.Reference, receiptWorkspacePath ?? WorkspacePath, Plan.WorkspaceBranch, Head, Head, SourcePath, "owner:test"), failure == Failure.WorkspaceMissing ? WorkspaceRecoveryState.NotPrepared : WorkspaceRecoveryState.PreparedAndRecorded);
            var source = failure == Failure.SourceMoved ? new LocalRepositoryInspection(RepositoryVerificationStatus.AvailableClean, SourcePath, SourcePath, true, "main", false, new string('b', 40), "bbbbbbb", isClean: true)
                : new LocalRepositoryInspection(RepositoryVerificationStatus.AvailableClean, SourcePath, SourcePath, true, "main", false, Head, "aaaaaaa", isClean: failure != Failure.SourceDirty);
            var persistedRouting = failure == Failure.RoutingMismatch
                ? new RoutingDecision(projectId, Routing.DecisionId, RoutingDecisionSchema.CurrentVersion, Now.AddMinutes(1), new RoutingEvaluation(Routing.Input, Routing.Outcome, Routing.CandidateAssessments, Routing.OriginalRecommendation, Routing.Recommendation, Routing.OwnerOverrideDisposition, Routing.Confidence, Routing.Limitations, Routing.ReasonCodes))
                : Routing;
            Rehydrator = new ReadyExecutionRehydrator(Resolver, new ProjectRepository(Project), new RepositoryState(source), new ContractRepository(Contract), new GraphRepository(Graph), new HandoffRepository(Handoff), new RoutingRepository(persistedRouting, failure == Failure.RoutingMismatch), new WorkspacePlanRepository(Plan), new CheckpointRepository(Checkpoint), WorkspaceInspection, new AgentRegistry(Executor, failure == Failure.ExecutorDisabled), Claims);
        }

        internal ExecutionRunAuthority CreateClaim() => new(Project.Id, Guid.NewGuid(), Now, Contract.Reference, Graph.Reference, Node.NodeId,
            Handoff.Reference, Routing.Reference, Plan.Reference, Plan.WorkspaceId, WorkspacePath, new string('f', 64), Checkpoint.Reference,
            Executor.Id, "OpenAI", "gpt-test", AgentConnectionMode.Cli, "test", new ExecutionBudgetEnvelope(1, 1));

        private static EffectiveAgentDefinition Agent(Guid projectId, string name) => new(projectId,
            new AgentDefinition(Guid.NewGuid(), name, "Executor", AgentConnectionMode.Cli, AgentAvailability.Available, true, Now, Now,
                provider: "OpenAI", roleCapabilities: [AgentRole.Executor], supportedConnectionModes: [AgentConnectionMode.Cli],
                authenticationState: AgentAuthenticationState.Authenticated, entitlementState: AgentEntitlementState.VerifiedAvailable, modelIdentifier: "gpt-test"), null);

        private static RoutingDecision CreateRouting(Guid projectId, PlanningExecutionContract contract, EffectiveAgentDefinition executor)
        {
            var classification = new RoutingTaskClassification(RoutingScopeScale.Bounded, RoutingTaskRisk.Low, RoutingBlastRadius.Local, RoutingValidationCost.Low, AgentRole.Executor, capacityRequirement: RoutingCapacityRequirement.NotApplicable);
            var policy = new RoutingPolicySnapshot("test", AgentRole.Executor, [executor.Id], capacityRequirement: RoutingCapacityRequirement.NotApplicable);
            return new RoutingDecision(projectId, Guid.NewGuid(), RoutingDecisionSchema.CurrentVersion, Now, new RoutingDecisionEngine().Evaluate(new RoutingInputSnapshot(projectId, contract.Reference, new RoutingContextReference(contract.Context.ProjectContextId, 1, Now), classification, policy, [RoutingAgentSnapshot.FromEffective(executor)], [], null, Now)));
        }

        private static HandoffPackage CreateHandoff(Guid projectId, PlanningExecutionContract contract, WorkGraph graph, WorkGraphNode node)
        {
            var scope = new HandoffExecutionScope([new PlanningScopeClause("include", "prepared workspace")], [], [new PlanningScopeClause("forbid", "replay")], [new PlanningDeliverable("result", "bounded result", true)], [new PlanningValidationRequirement("test", PlanningValidationKind.Test, "focused", true)], contract.ExecutionBudgets, contract.StopConditions, [], null, null);
            return new HandoffPackage(projectId, Guid.NewGuid(), HandoffPackageSchema.CurrentVersion, Now, HandoffTransition.PlannerToExecutor, HandoffRole.Planner, HandoffRole.Executor, contract.Reference, contract.WorkItem, new HandoffContextReference(contract.Context.ProjectContextId, 1, Now, Now), new PlanningRepositoryTarget(PlanningRepositoryMode.None), graph.Reference, node.NodeId, null, scope, null, null, null, [], [], [], null, [], "execute", new HandoffRedactionMetadata(false, 0, []), new HandoffPackageSizeMetadata(HandoffPackageLimits.MaxCanonicalPayloadBytes, 0, 0, 0, 0, 0, 9));
        }
    }

    private sealed class ClaimRepository : IExecutionRunAuthorityRepository
    {
        public ExecutionRunAuthority? Authority { get; set; }
        public Task<ExecutionRunAuthorityRepositoryWriteResult> CreateAsync(ExecutionRunAuthority authority, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExecutionRunAuthorityReadResult> GetAsync(Guid projectId, Guid runId, CancellationToken cancellationToken = default) => Task.FromResult(new ExecutionRunAuthorityReadResult(ExecutionRunAuthorityReadState.Missing));
        public Task<ExecutionRunAuthorityReadResult> GetByInputCheckpointAsync(Guid projectId, RecoveryCheckpointReference inputCheckpointReference, CancellationToken cancellationToken = default) =>
            Task.FromResult(Authority is { } authority && authority.ProjectId == projectId && authority.InputRecoveryCheckpointReference.CheckpointId == inputCheckpointReference.CheckpointId && authority.InputRecoveryCheckpointReference.SchemaVersion == inputCheckpointReference.SchemaVersion && authority.InputRecoveryCheckpointReference.ContentHash == inputCheckpointReference.ContentHash
                ? new ExecutionRunAuthorityReadResult(ExecutionRunAuthorityReadState.Valid, authority)
                : new ExecutionRunAuthorityReadResult(ExecutionRunAuthorityReadState.Missing));
    }

    private sealed class FakeResolver(SmartContinueResult value) : ISmartContinueResolver
    {
        public int Calls { get; private set; }
        public Task<SmartContinueResult> ResolveAsync(Guid projectId, CancellationToken cancellationToken = default) { Calls++; return Task.FromResult(value); }
    }
    private sealed class ProjectRepository(Project project) : IProjectRepository
    {
        public Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Project>>([project]);
        public Task<Project?> GetByIdAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(projectId == project.Id ? project : null);
        public Task UpsertAsync(Project value, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class RepositoryState(LocalRepositoryInspection value) : IProjectRepositoryStateService { public Task<RepositoryStateSnapshot> VerifyAsync(Project project, CancellationToken cancellationToken = default) => Task.FromResult(new RepositoryStateSnapshot(project.Id, project.LocalPath, value)); }
    private sealed class ContractRepository(PlanningExecutionContract value) : IPlanningExecutionContractRepository
    {
        public Task<PlanningContractRepositoryWriteResult> CreateAsync(PlanningExecutionContract contract, CancellationToken cancellationToken = default) => Task.FromResult(new PlanningContractRepositoryWriteResult(PlanningContractRepositoryWriteStatus.Created));
        public Task<PlanningContractReadResult> GetAsync(Guid projectId, Guid id, int revision, CancellationToken cancellationToken = default) => Task.FromResult(projectId == value.ProjectId && id == value.ContractId ? new PlanningContractReadResult(PlanningContractReadState.Valid, value) : new PlanningContractReadResult(PlanningContractReadState.Missing));
        public Task<PlanningContractReadResult> GetLatestAsync(Guid projectId, Guid id, CancellationToken cancellationToken = default) => GetAsync(projectId, id, value.Revision, cancellationToken);
        public Task<PlanningContractRevisionListResult> ListRevisionsAsync(Guid projectId, Guid id, CancellationToken cancellationToken = default) => Task.FromResult(new PlanningContractRevisionListResult(PlanningContractReadState.Valid, [value]));
    }
    private sealed class GraphRepository(WorkGraph value) : IWorkGraphRepository { public Task<WorkGraphRepositoryWriteResult> CreateAsync(WorkGraph graph, CancellationToken cancellationToken = default) => Task.FromResult(new WorkGraphRepositoryWriteResult(WorkGraphRepositoryWriteStatus.Created)); public Task<WorkGraphReadResult> GetAsync(Guid projectId, Guid id, CancellationToken cancellationToken = default) => Task.FromResult(projectId == value.ProjectId && id == value.GraphId ? new WorkGraphReadResult(WorkGraphReadState.Valid, value) : new WorkGraphReadResult(WorkGraphReadState.Missing)); }
    private sealed class HandoffRepository(HandoffPackage value) : IHandoffPackageRepository { public Task<HandoffPackageRepositoryWriteResult> CreateAsync(HandoffPackage package, CancellationToken cancellationToken = default) => Task.FromResult(new HandoffPackageRepositoryWriteResult(HandoffPackageRepositoryWriteStatus.Created)); public Task<HandoffPackageReadResult> GetAsync(Guid projectId, Guid id, CancellationToken cancellationToken = default) => Task.FromResult(projectId == value.ProjectId && id == value.PackageId ? new HandoffPackageReadResult(HandoffPackageReadState.Valid, value) : new HandoffPackageReadResult(HandoffPackageReadState.Missing)); }
    private sealed class RoutingRepository(RoutingDecision value, bool mismatch = false) : IRoutingDecisionRepository { public Task<RoutingDecisionRepositoryWriteResult> CreateAsync(RoutingDecision decision, CancellationToken cancellationToken = default) => Task.FromResult(new RoutingDecisionRepositoryWriteResult(RoutingDecisionRepositoryWriteStatus.Created)); public Task<RoutingDecisionReadResult> GetAsync(Guid projectId, Guid id, CancellationToken cancellationToken = default) => Task.FromResult(projectId == value.ProjectId && (mismatch || id == value.DecisionId) ? new RoutingDecisionReadResult(RoutingDecisionReadState.Valid, value) : new RoutingDecisionReadResult(RoutingDecisionReadState.Missing)); }
    private sealed class WorkspacePlanRepository(WorkspacePreparationPlan value) : IWorkspacePreparationPlanRepository { public Task<WorkspacePreparationPlanWriteResult> CreateAsync(WorkspacePreparationPlan plan, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspacePreparationPlanWriteResult(WorkspacePreparationPlanWriteStatus.Created)); public Task<WorkspacePreparationPlanReadResult> GetAsync(Guid projectId, Guid id, CancellationToken cancellationToken = default) => Task.FromResult(projectId == value.ProjectId && id == value.PlanId ? new WorkspacePreparationPlanReadResult(WorkspacePreparationPlanReadState.Valid, value) : new WorkspacePreparationPlanReadResult(WorkspacePreparationPlanReadState.Missing)); }
    private sealed class CheckpointRepository(RecoveryCheckpoint value) : IRecoveryCheckpointRepository { public Task<RecoveryCheckpointRepositoryWriteResult> CreateAsync(RecoveryCheckpoint checkpoint, CancellationToken cancellationToken = default) => Task.FromResult(new RecoveryCheckpointRepositoryWriteResult(RecoveryCheckpointRepositoryWriteStatus.Created)); public Task<RecoveryCheckpointReadResult> GetAsync(Guid projectId, Guid id, CancellationToken cancellationToken = default) => Task.FromResult(projectId == value.ProjectId && id == value.CheckpointId ? new RecoveryCheckpointReadResult(RecoveryCheckpointReadState.Valid, value) : new RecoveryCheckpointReadResult(RecoveryCheckpointReadState.Missing)); }
    private sealed class FakeWorkspaceInspection(WorkspacePreparationReceipt receipt, WorkspaceRecoveryState state) : IWorkspaceRecoveryInspectionService { public int Calls { get; private set; } public Task<WorkspaceRecoveryInspectionResult> InspectAsync(WorkspacePreparationPlanReference planReference, CancellationToken cancellationToken = default) { Calls++; return Task.FromResult(new WorkspaceRecoveryInspectionResult(state, state == WorkspaceRecoveryState.PreparedAndRecorded ? receipt : null)); } }
    private sealed class AgentRegistry(EffectiveAgentDefinition executor, bool disabled) : IAgentRegistryService { private readonly EffectiveAgentDefinition _executor = disabled ? new EffectiveAgentDefinition(executor.ProjectId, new AgentDefinition(executor.Id, executor.Name, executor.Role, executor.ConnectionMode, AgentAvailability.Disabled, false, executor.GlobalDefinition.CreatedAt, executor.GlobalDefinition.UpdatedAt, provider: executor.Provider, roleCapabilities: executor.RoleCapabilities, supportedConnectionModes: executor.SupportedConnectionModes, authenticationState: executor.AuthenticationState, entitlementState: executor.EntitlementState, modelIdentifier: executor.ModelIdentifier), null) : executor; public Task<IReadOnlyList<EffectiveAgentDefinition>> GetEffectiveAgentsAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<EffectiveAgentDefinition>>([_executor]); public Task<AgentRegistryResolution> ResolveAsync(Guid projectId, Guid agentId, CancellationToken cancellationToken = default) => Task.FromResult(projectId == _executor.ProjectId && agentId == _executor.Id ? AgentRegistryResolution.FoundResult(_executor) : AgentRegistryResolution.NotFoundResult()); }
}
