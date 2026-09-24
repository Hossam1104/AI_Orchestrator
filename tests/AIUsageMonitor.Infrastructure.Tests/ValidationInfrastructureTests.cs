using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Routing;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Application.Validation;
using AIUsageMonitor.Application.Workspaces;
using AIUsageMonitor.Infrastructure.Execution;
using AIUsageMonitor.Infrastructure.Persistence;
using AIUsageMonitor.Infrastructure.Persistence.Repositories;
using AIUsageMonitor.Infrastructure.Validation;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageMonitor.Infrastructure.Tests;

public sealed class ValidationInfrastructureTests
{
    [Fact]
    public async Task DotNetCollector_UsesStructuredNoRestoreArgumentsAndDoesNotPersistOutput()
    {
        var host = new FakeProcessHost(new BoundedProcessResult(BoundedProcessOutcome.ExitedSuccessfully, 0, "passed", string.Empty, false, false, false, true, TimeSpan.FromSeconds(1)));
        var context = CreateContext(@"src\App.csproj");
        CreateTarget(context, @"src\App.csproj");
        var collector = new DotNetValidationEvidenceCollector(host, new FixedClock(context.Plan.CreatedAt.AddMinutes(1)), new HandoffRedactionService());

        var evidence = await collector.CaptureAsync(context);

        Assert.Equal(ValidationEvidenceState.Available, evidence.State);
        Assert.Equal(ValidationOutcome.Passed, evidence.Outcome);
        Assert.Equal("dotnet", host.Request!.ExecutablePath);
        Assert.Equal(["build", Path.Combine("src", "App.csproj"), "--no-restore", "--nologo"], host.Request.Arguments);
        Assert.Equal(1, host.InvocationCount);
        Assert.Equal(6, evidence.StdoutBytes);
        Assert.Null(evidence.DiagnosticSummary);
    }

    [Fact]
    public async Task DotNetCollector_RejectsTargetEscapeBeforeStartingProcess()
    {
        var host = new FakeProcessHost(new BoundedProcessResult(BoundedProcessOutcome.ExitedSuccessfully, 0, string.Empty, string.Empty, false, false, false, true, TimeSpan.Zero));
        var context = CreateContext(@"..\outside.csproj");
        var collector = new DotNetValidationEvidenceCollector(host, new FixedClock(context.Plan.CreatedAt.AddMinutes(1)), new HandoffRedactionService());

        var evidence = await collector.CaptureAsync(context);

        Assert.Equal(ValidationEvidenceState.Invalid, evidence.State);
        Assert.False(host.Called);
    }

    [Fact]
    public async Task DotNetCollector_AllowsExistingSupportedTargetInsideWorkspace()
    {
        var host = new FakeProcessHost(new BoundedProcessResult(BoundedProcessOutcome.ExitedSuccessfully, 0, string.Empty, string.Empty, false, false, false, true, TimeSpan.Zero));
        var context = CreateContext("project.sln");
        CreateTarget(context, "project.sln");
        var collector = new DotNetValidationEvidenceCollector(host, new FixedClock(context.Plan.CreatedAt.AddMinutes(1)), new HandoffRedactionService());

        var evidence = await collector.CaptureAsync(context);

        Assert.Equal(ValidationEvidenceState.Available, evidence.State);
        Assert.Equal(ValidationOutcome.Passed, evidence.Outcome);
        Assert.Equal(1, host.InvocationCount);
    }

    [Theory]
    [InlineData("@response.sln", "@response.sln")]
    [InlineData("-option.sln", "-option.sln")]
    [InlineData(@".\@response.sln", "@response.sln")]
    [InlineData(@".\-option.sln", "-option.sln")]
    [InlineData(@"folder\..\@response.sln", "@response.sln")]
    [InlineData(@"folder\..\-option.sln", "-option.sln")]
    public async Task DotNetCollector_RejectsUnsafeNormalizedTargetArgumentsWithoutProcessInvocation(string candidate, string existingTarget)
    {
        var host = new FakeProcessHost(new BoundedProcessResult(BoundedProcessOutcome.ExitedSuccessfully, 0, string.Empty, string.Empty, false, false, false, true, TimeSpan.Zero));
        var context = CreateContext(candidate);
        CreateTarget(context, existingTarget);
        var collector = new DotNetValidationEvidenceCollector(host, new FixedClock(context.Plan.CreatedAt.AddMinutes(1)), new HandoffRedactionService());

        var evidence = await collector.CaptureAsync(context);

        Assert.NotEqual(ValidationOutcome.Passed, evidence.Outcome);
        Assert.Equal(ValidationEvidenceState.Invalid, evidence.State);
        Assert.Equal(0, host.InvocationCount);
    }

    [Theory]
    [InlineData("missing.csproj")]
    [InlineData("-h")]
    [InlineData("@response.rsp")]
    [InlineData("readme.txt")]
    public async Task DotNetCollector_RejectsInvalidTargetsWithoutProcessInvocation(string target)
    {
        var host = new FakeProcessHost(new BoundedProcessResult(BoundedProcessOutcome.ExitedSuccessfully, 0, string.Empty, string.Empty, false, false, false, true, TimeSpan.Zero));
        var context = CreateContext(target);
        var collector = new DotNetValidationEvidenceCollector(host, new FixedClock(context.Plan.CreatedAt.AddMinutes(1)), new HandoffRedactionService());

        var evidence = await collector.CaptureAsync(context);

        Assert.Equal(ValidationEvidenceState.Invalid, evidence.State);
        Assert.NotEqual(ValidationOutcome.Passed, evidence.Outcome);
        Assert.Equal(0, host.InvocationCount);
    }

    [Fact]
    public async Task DotNetCollector_RejectsOutsideWorkspaceTargetWithoutProcessInvocation()
    {
        var host = new FakeProcessHost(new BoundedProcessResult(BoundedProcessOutcome.ExitedSuccessfully, 0, string.Empty, string.Empty, false, false, false, true, TimeSpan.Zero));
        var outside = Path.Combine(Path.GetTempPath(), "apo-outside-" + Guid.NewGuid().ToString("N") + ".csproj");
        var context = CreateContext(outside);
        var collector = new DotNetValidationEvidenceCollector(host, new FixedClock(context.Plan.CreatedAt.AddMinutes(1)), new HandoffRedactionService());

        var evidence = await collector.CaptureAsync(context);

        Assert.Equal(ValidationEvidenceState.Invalid, evidence.State);
        Assert.Equal(0, host.InvocationCount);
    }

    [Fact]
    public async Task DotNetCollector_DiscardsSecretShapedOutputAndReportsRedactionRejected()
    {
        var host = new FakeProcessHost(new BoundedProcessResult(BoundedProcessOutcome.ExitedSuccessfully, 0, "password=secret-value", string.Empty, false, false, false, true, TimeSpan.Zero));
        var context = CreateContext("App.csproj");
        CreateTarget(context, "App.csproj");
        var collector = new DotNetValidationEvidenceCollector(host, new FixedClock(context.Plan.CreatedAt.AddMinutes(1)), new HandoffRedactionService());

        var evidence = await collector.CaptureAsync(context);

        Assert.Equal(ValidationEvidenceState.RedactionRejected, evidence.State);
        Assert.Equal(ValidationOutcome.Unknown, evidence.Outcome);
        Assert.Null(evidence.DiagnosticSummary);
    }

    [Fact]
    public void CollectorResolverDoesNotGuessOrRankUnknownCollectors()
    {
        var resolver = new ValidationEvidenceCollectorResolver([]);

        var result = resolver.Resolve("unknown", ValidationEvidenceKind.Test);

        Assert.Equal(ValidationCollectorResolutionStatus.Unsupported, result.Status);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ValidationDocumentsRoundTripAndRejectImmutableOverwrite()
    {
        using var store = new TemporaryStore();
        var plan = CreatePlan();
        var plans = new JsonValidationPlanRepository(store.Paths, store.Files, NullLogger<JsonValidationPlanRepository>.Instance);
        var evidenceRepository = new JsonValidationEvidenceRepository(store.Paths, store.Files, NullLogger<JsonValidationEvidenceRepository>.Instance);
        var decisionRepository = new JsonValidationGateDecisionRepository(store.Paths, store.Files, NullLogger<JsonValidationGateDecisionRepository>.Instance);
        var evidence = CreateEvidence(plan);
        var decision = ValidationGateEvaluator.Evaluate(plan, [evidence], plan.CreatedAt.AddMinutes(1));

        Assert.Equal(ValidationPlanRepositoryWriteStatus.Created, (await plans.CreateAsync(plan)).Status);
        Assert.Equal(ValidationPlanRepositoryWriteStatus.PlanConflict, (await plans.CreateAsync(plan)).Status);
        Assert.True((await plans.GetAsync(plan.ProjectId, plan.Reference)).IsValid);
        Assert.Equal(ValidationEvidenceRepositoryWriteStatus.Created, (await evidenceRepository.CreateAsync(evidence)).Status);
        Assert.True((await evidenceRepository.GetAsync(evidence.ProjectId, evidence.PlanReference, evidence.Reference)).IsValid);
        Assert.True((await evidenceRepository.GetForPlanAsync(evidence.ProjectId, evidence.PlanReference)).IsComplete);
        Assert.Equal(ValidationDecisionRepositoryWriteStatus.Created, (await decisionRepository.CreateAsync(decision)).Status);
        Assert.True((await decisionRepository.GetAsync(decision.ProjectId, decision.DecisionId)).IsValid);
    }

    [Fact]
    public async Task SatisfiedGatePublishesValidationRecoveryCheckpointWithoutContinuingAutomatically()
    {
        var context = CreateContext("App.csproj");
        var evidence = CreateEvidence(context.Plan);
        var recovery = new FakeRecoveryService(context.CurrentCheckpoint);
        var service = new ValidationGateService(
            new FakePlanRepository(context.Plan),
            new FakeEvidenceRepository([evidence]),
            new FakeDecisionRepository(),
            new FakeAuthorityRepository(context.Authority),
            new FakeReceiptRepository(context.WorkspaceReceipt),
            new FakeCheckpointRepository(context.CurrentCheckpoint, context.Authority),
            new FakeContinuationHeadRepository(context.CurrentCheckpoint),
            recovery,
            new FixedClock(context.Plan.CreatedAt.AddMinutes(2)));

        var result = await service.EvaluateAsync(new ValidationGateRequest(context.Plan.ProjectId, context.Plan.Reference, context.Plan.CurrentRecoveryCheckpointReference));

        Assert.True(result.Succeeded);
        Assert.Equal(ValidationGateDecisionState.Satisfied, result.Decision!.State);
        Assert.True(result.Recovery!.Succeeded);
        Assert.Equal(RecoveryGateState.Satisfied, recovery.Request!.GateSnapshots.Single(value => value.Kind == RecoveryGateKind.Validation).State);
        Assert.Equal(RecoveryNextSafeAction.ContinueFromCheckpoint, recovery.Request.NextSafeAction);
        Assert.Equal(context.CurrentCheckpoint.Reference.ToString(), recovery.Request.PreviousCheckpointReference!.ToString());
    }

    private static ValidationCollectionContext CreateContext(string target)
    {
        var now = new DateTimeOffset(2026, 8, 31, 10, 0, 0, TimeSpan.Zero);
        var projectId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var contract = new PlanningExecutionContractReference(Guid.NewGuid(), 1, 1, Hash('a'));
        var graph = new WorkGraphReference(Guid.NewGuid(), 1, Hash('b'));
        var handoff = new HandoffPackageReference(Guid.NewGuid(), 1, Hash('c'));
        var routing = new RoutingDecisionReference(Guid.NewGuid(), 1, Hash('d'));
        var workspacePlan = new WorkspacePreparationPlanReference(Guid.NewGuid(), 1, Hash('e'), projectId);
        var workspace = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "apo-validation-workspace-" + Guid.NewGuid().ToString("N")));
        var inputCheckpoint = new RecoveryCheckpoint(projectId, Guid.NewGuid(), RecoveryCheckpointSchema.CurrentVersion, now.AddMinutes(-2), RecoveryCheckpointLifecycleState.Ready,
            new RecoveryContextReference(Guid.NewGuid(), 1, now), contract, graph, Guid.NewGuid(), handoff,
            nextSafeAction: RecoveryNextSafeAction.ContinueFromCheckpoint);
        var receipt = new WorkspacePreparationReceipt(projectId, workspaceId, Guid.NewGuid(), now, workspacePlan, workspace, "apo-validation", new('f', 40), new('f', 40), "local-repository", "test");
        var authority = new ExecutionRunAuthority(projectId, Guid.NewGuid(), now.AddMinutes(-1), contract, graph, inputCheckpoint.WorkGraphNodeId!.Value, handoff, routing, workspacePlan,
            workspaceId, workspace, receipt.ContentHash, inputCheckpoint.Reference, Guid.NewGuid(), "provider", "model", AgentConnectionMode.Cli, "adapter", new ExecutionBudgetEnvelope(1, 1));
        var executionEvidence = new RecoveryEvidenceReference(authority.RunId, RecoveryEvidenceKind.Other,
            $"execution-run:{authority.ProjectId:D}/{authority.RunId:D}/{authority.ContentHash}", authority.CreatedAt,
            RecoveryEvidenceFreshness.PointInTime, contentHash: authority.ContentHash);
        var preRunCheckpoint = new RecoveryCheckpoint(projectId, Guid.NewGuid(), RecoveryCheckpointSchema.CurrentVersion, now, RecoveryCheckpointLifecycleState.Waiting,
            inputCheckpoint.Context, contract, graph, inputCheckpoint.WorkGraphNodeId, handoff, previousCheckpointReference: inputCheckpoint.Reference,
            evidenceReferences: [executionEvidence], nextSafeAction: RecoveryNextSafeAction.ResolveBlocker);
        var checkpoint = new RecoveryCheckpoint(projectId, Guid.NewGuid(), RecoveryCheckpointSchema.CurrentVersion, now, RecoveryCheckpointLifecycleState.Ready,
            inputCheckpoint.Context, contract, graph, inputCheckpoint.WorkGraphNodeId, handoff, previousCheckpointReference: preRunCheckpoint.Reference,
            evidenceReferences: [executionEvidence], nextSafeAction: RecoveryNextSafeAction.RunValidation);
        var requirement = new ValidationRequirement("build", ValidationEvidenceKind.Build, true, ValidationCoverageScope.Targeted, ValidationBaselineRelation.Standalone, DotNetValidationEvidenceCollector.CollectorIdentifier, targetPath: target);
        var plan = new ValidationPlan(projectId, Guid.NewGuid(), 1, now, authority.Reference, contract, graph, checkpoint.WorkGraphNodeId!.Value, workspaceId, workspace, receipt.ContentHash, checkpoint.Reference, [requirement], handoff);
        var project = new Project(projectId, "Validation", workspace, null, ProjectStatus.Active, now, now);
        return new(plan, requirement, project, authority, receipt, checkpoint, []);
    }

    private static void CreateTarget(ValidationCollectionContext context, string target)
    {
        var path = Path.GetFullPath(Path.Combine(context.Plan.WorkspacePath, target));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "<Project />");
    }

    private static ValidationPlan CreatePlan()
    {
        var context = CreateContext("App.csproj");
        return context.Plan;
    }

    private static ValidationEvidence CreateEvidence(ValidationPlan plan)
    {
        var requirement = plan.Requirements[0];
        return new(plan.ProjectId, Guid.NewGuid(), plan.Reference, requirement.RequirementId, plan.ExecutionRunAuthorityReference.RunId,
            plan.ExecutionRunAuthorityReference, plan.PlanningContractReference, plan.WorkGraphReference, plan.WorkGraphNodeId,
            plan.CurrentRecoveryCheckpointReference, plan.WorkspaceId, plan.WorkspacePath, plan.WorkspaceReceiptContentHash,
            requirement.CollectorIdentifier, requirement.EvidenceKind, ValidationEvidenceState.Available, ValidationOutcome.Passed,
            requirement.Coverage, requirement.BaselineRelation, plan.CreatedAt.AddMinutes(1), validationDefinitionId: requirement.ValidationDefinitionId);
    }

    private static string Hash(char value) => new(value, 64);

    private sealed class FixedClock(DateTimeOffset value) : IClock
    {
        public DateTimeOffset UtcNow => value;
    }

    private sealed class FakeProcessHost(BoundedProcessResult result) : IBoundedProcessHost
    {
        public BoundedProcessRequest? Request { get; private set; }
        public bool Called { get; private set; }
        public int InvocationCount { get; private set; }

        public Task<BoundedProcessResult> RunAsync(BoundedProcessRequest request, CancellationToken cancellationToken = default)
        {
            Called = true;
            InvocationCount++;
            Request = request;
            return Task.FromResult(result);
        }
    }

    private sealed class FakePlanRepository(ValidationPlan plan) : IValidationPlanRepository
    {
        public Task<ValidationPlanRepositoryWriteResult> CreateAsync(ValidationPlan value, CancellationToken cancellationToken = default) => Task.FromResult(new ValidationPlanRepositoryWriteResult(ValidationPlanRepositoryWriteStatus.Created));
        public Task<ValidationPlanReadResult> GetAsync(Guid projectId, ValidationPlanReference reference, CancellationToken cancellationToken = default) => Task.FromResult(reference == plan.Reference ? new ValidationPlanReadResult(ValidationPlanReadState.Valid, plan) : new ValidationPlanReadResult(ValidationPlanReadState.Missing));
    }

    private sealed class FakeEvidenceRepository(IReadOnlyList<ValidationEvidence> values) : IValidationEvidenceRepository
    {
        public Task<ValidationEvidenceRepositoryWriteResult> CreateAsync(ValidationEvidence evidence, CancellationToken cancellationToken = default) => Task.FromResult(new ValidationEvidenceRepositoryWriteResult(ValidationEvidenceRepositoryWriteStatus.Created));
        public Task<ValidationEvidenceReadResult> GetAsync(Guid projectId, ValidationPlanReference planReference, ValidationEvidenceReference evidenceReference, CancellationToken cancellationToken = default) => Task.FromResult(values.SingleOrDefault(value => value.EvidenceId == evidenceReference.EvidenceId && value.PlanReference == planReference) is { } value ? new ValidationEvidenceReadResult(ValidationEvidenceReadState.Valid, value) : new ValidationEvidenceReadResult(ValidationEvidenceReadState.Missing));
        public Task<ValidationEvidenceSetReadResult> GetForPlanAsync(Guid projectId, ValidationPlanReference planReference, CancellationToken cancellationToken = default) => Task.FromResult(new ValidationEvidenceSetReadResult(ValidationEvidenceSetReadState.Valid, values.Where(value => value.PlanReference == planReference).ToArray()));
    }

    private sealed class FakeDecisionRepository : IValidationGateDecisionRepository
    {
        public Task<ValidationDecisionRepositoryWriteResult> CreateAsync(ValidationGateDecision decision, CancellationToken cancellationToken = default) => Task.FromResult(new ValidationDecisionRepositoryWriteResult(ValidationDecisionRepositoryWriteStatus.Created));
        public Task<ValidationDecisionReadResult> GetAsync(Guid projectId, Guid decisionId, CancellationToken cancellationToken = default) => Task.FromResult(new ValidationDecisionReadResult(ValidationDecisionReadState.Missing));
    }

    private sealed class FakeAuthorityRepository(ExecutionRunAuthority authority) : IExecutionRunAuthorityRepository
    {
        public Task<ExecutionRunAuthorityRepositoryWriteResult> CreateAsync(ExecutionRunAuthority value, CancellationToken cancellationToken = default) => Task.FromResult(new ExecutionRunAuthorityRepositoryWriteResult(ExecutionRunAuthorityRepositoryWriteStatus.Created));
        public Task<ExecutionRunAuthorityReadResult> GetAsync(Guid projectId, Guid runId, CancellationToken cancellationToken = default) => Task.FromResult(new ExecutionRunAuthorityReadResult(ExecutionRunAuthorityReadState.Valid, authority));
        public Task<ExecutionRunAuthorityReadResult> GetByInputCheckpointAsync(Guid projectId, RecoveryCheckpointReference inputCheckpointReference, CancellationToken cancellationToken = default) => Task.FromResult(new ExecutionRunAuthorityReadResult(ExecutionRunAuthorityReadState.Missing));
    }

    private sealed class FakeReceiptRepository(WorkspacePreparationReceipt receipt) : IWorkspacePreparationReceiptRepository
    {
        public Task<WorkspacePreparationReceiptWriteResult> CreateAsync(WorkspacePreparationReceipt value, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspacePreparationReceiptWriteResult(WorkspacePreparationReceiptWriteStatus.Created));
        public Task<WorkspacePreparationReceiptReadResult> GetAsync(Guid projectId, Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspacePreparationReceiptReadResult(WorkspacePreparationReceiptReadState.Valid, receipt));
    }

    private sealed class FakeCheckpointRepository(RecoveryCheckpoint checkpoint, ExecutionRunAuthority authority) : IRecoveryCheckpointRepository
    {
        private readonly RecoveryCheckpoint _preRun = new(checkpoint.ProjectId, checkpoint.PreviousCheckpointReference!.CheckpointId, checkpoint.SchemaVersion, checkpoint.CreatedAt, RecoveryCheckpointLifecycleState.Waiting,
            checkpoint.Context, checkpoint.PlanningContractReference, checkpoint.WorkGraphReference, checkpoint.WorkGraphNodeId, checkpoint.HandoffPackageReference,
            previousCheckpointReference: authority.InputRecoveryCheckpointReference, evidenceReferences: checkpoint.EvidenceReferences, nextSafeAction: RecoveryNextSafeAction.ResolveBlocker);
        private readonly RecoveryCheckpoint _input = new(checkpoint.ProjectId, authority.InputRecoveryCheckpointReference.CheckpointId, authority.InputRecoveryCheckpointReference.SchemaVersion, checkpoint.CreatedAt.AddMinutes(-2), RecoveryCheckpointLifecycleState.Ready,
            checkpoint.Context, checkpoint.PlanningContractReference, checkpoint.WorkGraphReference, checkpoint.WorkGraphNodeId, checkpoint.HandoffPackageReference,
            nextSafeAction: RecoveryNextSafeAction.ContinueFromCheckpoint);
        public Task<RecoveryCheckpointRepositoryWriteResult> CreateAsync(RecoveryCheckpoint value, CancellationToken cancellationToken = default) => Task.FromResult(new RecoveryCheckpointRepositoryWriteResult(RecoveryCheckpointRepositoryWriteStatus.Created));
        public Task<RecoveryCheckpointReadResult> GetAsync(Guid projectId, Guid checkpointId, CancellationToken cancellationToken = default) => Task.FromResult(checkpointId == checkpoint.CheckpointId ? new RecoveryCheckpointReadResult(RecoveryCheckpointReadState.Valid, checkpoint) : checkpointId == _preRun.CheckpointId ? new RecoveryCheckpointReadResult(RecoveryCheckpointReadState.Valid, _preRun) : checkpointId == _input.CheckpointId ? new RecoveryCheckpointReadResult(RecoveryCheckpointReadState.Valid, _input) : new RecoveryCheckpointReadResult(RecoveryCheckpointReadState.Missing));
    }

    private sealed class FakeContinuationHeadRepository(RecoveryCheckpoint checkpoint) : IContinuationHeadRepository
    {
        public Task<ContinuationHeadReadResult> GetAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ContinuationHeadReadResult(ContinuationHeadReadState.Valid,
                new ContinuationHead(projectId, ContinuationHeadSchema.CurrentVersion, 1, checkpoint.Reference, null, checkpoint.CreatedAt)));
        public Task<ContinuationHeadRepositoryWriteResult> PublishAsync(ContinuationHead head, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ContinuationHeadRepositoryWriteResult(ContinuationHeadRepositoryWriteStatus.Published));
    }

    private sealed class FakeRecoveryService(RecoveryCheckpoint predecessor) : IRecoveryCheckpointService
    {
        public RecoveryCheckpointCreationRequest? Request { get; private set; }

        public Task<RecoveryCheckpointCreationResult> CreateAsync(RecoveryCheckpointCreationRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            var checkpoint = new RecoveryCheckpoint(request.ProjectId, request.CheckpointId, RecoveryCheckpointSchema.CurrentVersion, request.CreatedAt ?? predecessor.CreatedAt, request.LifecycleState,
                predecessor.Context, request.PlanningContractReference, request.WorkGraphReference, request.WorkGraphNodeId, request.HandoffPackageReference,
                request.RoutingDecisionReference, request.WorkspacePreparationPlanReference, request.PreviousCheckpointReference, request.SelectedAgentRoleReferences,
                request.EvidenceReferences, request.GateSnapshots, request.Blockers, request.NextSafeAction, request.Explanation);
            var head = new ContinuationHead(request.ProjectId, 1, 2, checkpoint.Reference, predecessor.Reference, checkpoint.CreatedAt);
            return Task.FromResult(new RecoveryCheckpointCreationResult(RecoveryCheckpointCreationStatus.Created, checkpoint, head));
        }
    }
}
