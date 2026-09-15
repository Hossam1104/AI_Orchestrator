using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Routing;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Application.Validation;
using AIUsageMonitor.Application.Workspaces;
using AIUsageMonitor.Infrastructure.Validation;

namespace AIUsageMonitor.Infrastructure.Tests;

public sealed class BoundedExecutionServiceTests
{
    [Fact]
    public void ProductionTimeoutProvider_EnforcesDeterministicHardCeiling()
    {
        var timeout = new ExecutionBudgetTimeoutProvider().GetTimeout(new ExecutionBudgetEnvelope(1, 100_000));

        Assert.Equal(BoundedExecutionLimits.MaxExecutionTimeout, timeout);
    }

    [Fact]
    public async Task SuccessfulRun_RecordsFullSingleStepLifecycle()
    {
        using var harness = ExecutionHarness.Create();

        var result = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.Succeeded, result.Status);
        Assert.Equal(1, harness.Adapter.InvocationCount);
        Assert.Equal(harness.Agent.Id, harness.Adapter.LastRequest!.SelectedAgent.Id);
        Assert.Equal(harness.Receipt.WorkspacePath, harness.Adapter.LastRequest.WorkspacePath);
        Assert.Equal(harness.Contract.Reference, harness.Adapter.LastRequest.Contract.Reference);
        Assert.Equal(harness.Handoff.Reference, harness.Adapter.LastRequest.Handoff.Reference);
        Assert.Equal(harness.Receipt.ContentHash, result.Authority!.WorkspaceReceiptContentHash);
        Assert.Equal(harness.CurrentCheckpoint.Reference.ContentHash, result.Authority.InputRecoveryCheckpointReference.ContentHash);
        Assert.Equal(
            [ExecutionRunStatus.Planned, ExecutionRunStatus.Running, ExecutionRunStatus.Completed],
            harness.History.Runs.Select(value => value.Status));
        Assert.Equal(2, harness.CheckpointService.Created.Count);
        Assert.Equal(RecoveryCheckpointLifecycleState.Waiting, harness.CheckpointService.Created[0].LifecycleState);
        Assert.Equal(RecoveryCheckpointLifecycleState.Ready, result.TerminalCheckpoint!.LifecycleState);
        Assert.Equal(RecoveryNextSafeAction.RunValidation, result.TerminalCheckpoint.NextSafeAction);
        Assert.NotEqual(ExecutionRunStatus.Accepted, harness.History.Runs[^1].Status);
    }

    [Fact]
    public async Task ActualBoundedExecutionTerminalFeedsApo48PlanCaptureAndGate()
    {
        using var harness = ExecutionHarness.Create();
        var execution = await harness.Service.ExecuteAsync(harness.Request);
        var authority = execution.Authority!;
        var terminal = execution.TerminalCheckpoint!;
        var requirement = new ValidationRequirement("tests", ValidationEvidenceKind.Test, true,
            ValidationCoverageScope.Targeted, ValidationBaselineRelation.Standalone, "integration");
        var plan = new ValidationPlan(harness.Project.Id, Guid.NewGuid(), 1, terminal.CreatedAt,
            authority.Reference, authority.PlanningContractReference, authority.WorkGraphReference!, authority.WorkGraphNodeId,
            harness.Receipt.WorkspaceId, harness.Receipt.WorkspacePath, harness.Receipt.ContentHash, terminal.Reference,
            [requirement], authority.HandoffPackageReference);
        var plans = new IntegrationValidationPlanRepository(plan);
        var evidence = new IntegrationValidationEvidenceRepository();
        var validation = new ValidationEvidenceService(plans, evidence, new IntegrationProjectRepository(harness.Project), harness.Authorities,
            new IntegrationReceiptRepository(harness.Receipt), harness.Checkpoints, harness.Heads,
            new IntegrationCollectorResolver(), new FixedClock(DateTimeOffset.Parse("2026-08-28T10:00:00+00:00")));

        var planWrite = await validation.CreatePlanAsync(plan);
        var capture = await validation.CaptureAsync(new ValidationCaptureRequest(plan.ProjectId, plan.Reference, requirement.RequirementId, terminal.Reference));
        var gate = new ValidationGateService(plans, evidence, new IntegrationDecisionRepository(), harness.Authorities,
            new IntegrationReceiptRepository(harness.Receipt), harness.Checkpoints, harness.Heads, harness.CheckpointService,
            new FixedClock(DateTimeOffset.Parse("2026-08-28T10:00:00+00:00")));
        var decision = await gate.EvaluateAsync(new ValidationGateRequest(plan.ProjectId, plan.Reference, terminal.Reference));

        Assert.True(execution.Succeeded);
        Assert.NotEqual(authority.InputRecoveryCheckpointReference, terminal.Reference);
        Assert.Equal(RecoveryCheckpointLifecycleState.Ready, terminal.LifecycleState);
        Assert.Equal(RecoveryNextSafeAction.RunValidation, terminal.NextSafeAction);
        Assert.True(planWrite.Succeeded, planWrite.ErrorMessage);
        Assert.True(capture.Succeeded);
        Assert.Equal(terminal.Reference.ToString(), capture.Evidence!.CurrentRecoveryCheckpointReference.ToString());
        Assert.True(decision.Succeeded, decision.ErrorMessage);
        Assert.Equal(ValidationGateDecisionState.Satisfied, decision.Decision!.State);
    }

    [Fact]
    public async Task ActualSecondRunTerminalCannotValidateOlderAuthorityDespiteInheritedEvidence()
    {
        using var harness = ExecutionHarness.Create();
        var runA = await harness.Service.ExecuteAsync(harness.Request);
        var authorityA = runA.Authority!;
        var preRunA = harness.CheckpointService.Created[0];
        var terminalA = runA.TerminalCheckpoint!;
        var requirement = new ValidationRequirement("tests", ValidationEvidenceKind.Test, true,
            ValidationCoverageScope.Targeted, ValidationBaselineRelation.Standalone, "integration");
        var planA = new ValidationPlan(harness.Project.Id, Guid.NewGuid(), 1, terminalA.CreatedAt,
            authorityA.Reference, authorityA.PlanningContractReference, authorityA.WorkGraphReference, authorityA.WorkGraphNodeId,
            harness.Receipt.WorkspaceId, harness.Receipt.WorkspacePath, harness.Receipt.ContentHash, terminalA.Reference,
            [requirement], authorityA.HandoffPackageReference);
        var evidenceA = new IntegrationValidationEvidenceRepository();
        var validationA = new ValidationEvidenceService(new IntegrationValidationPlanRepository(planA), evidenceA,
            new IntegrationProjectRepository(harness.Project), harness.Authorities, new IntegrationReceiptRepository(harness.Receipt),
            harness.Checkpoints, harness.Heads, new IntegrationCollectorResolver(), new FixedClock(DateTimeOffset.Parse("2026-08-28T10:00:00+00:00")));
        Assert.True((await validationA.CreatePlanAsync(planA)).Succeeded);
        Assert.True((await validationA.CaptureAsync(new ValidationCaptureRequest(planA.ProjectId, planA.Reference, requirement.RequirementId, terminalA.Reference))).Succeeded);
        var gateA = new ValidationGateService(new IntegrationValidationPlanRepository(planA), evidenceA,
            new IntegrationDecisionRepository(), harness.Authorities, new IntegrationReceiptRepository(harness.Receipt), harness.Checkpoints,
            harness.Heads, harness.CheckpointService, new FixedClock(DateTimeOffset.Parse("2026-08-28T10:00:00+00:00")));
        var decisionA = await gateA.EvaluateAsync(new ValidationGateRequest(planA.ProjectId, planA.Reference, terminalA.Reference));
        var continuationA = decisionA.Recovery!.Checkpoint!;

        var requestB = new BoundedExecutionRequest(harness.Request.ProjectId, Guid.NewGuid(), harness.Request.PlanningContractReference,
            harness.Request.WorkGraphReference, harness.Request.WorkGraphNodeId, harness.Request.HandoffPackageReference,
            harness.Request.RoutingDecisionReference, harness.Request.WorkspacePreparationPlanReference, continuationA.Reference);
        var runB = await harness.Service.ExecuteAsync(requestB);
        var authorityB = runB.Authority!;
        var preRunB = harness.CheckpointService.Created[3];
        var terminalB = runB.TerminalCheckpoint!;

        Assert.True(runA.Succeeded);
        Assert.Equal(RecoveryCheckpointLifecycleState.Ready, terminalA.LifecycleState);
        Assert.Equal(RecoveryNextSafeAction.RunValidation, terminalA.NextSafeAction);
        Assert.Equal(preRunA.Reference, terminalA.PreviousCheckpointReference);
        Assert.Equal(harness.CurrentCheckpoint.Reference, preRunA.PreviousCheckpointReference);
        Assert.Contains(terminalB.EvidenceReferences, value => value.EvidenceId == authorityA.RunId);
        Assert.Contains(terminalB.EvidenceReferences, value => value.EvidenceId == authorityB.RunId);

        var attackPlan = new ValidationPlan(harness.Project.Id, Guid.NewGuid(), 1, terminalB.CreatedAt,
            authorityA.Reference, authorityA.PlanningContractReference, authorityA.WorkGraphReference, authorityA.WorkGraphNodeId,
            harness.Receipt.WorkspaceId, harness.Receipt.WorkspacePath, harness.Receipt.ContentHash, terminalB.Reference,
            [requirement], authorityA.HandoffPackageReference);
        var attackCollector = new IntegrationCollectorResolver();
        var attackValidation = new ValidationEvidenceService(new IntegrationValidationPlanRepository(attackPlan),
            new IntegrationValidationEvidenceRepository(), new IntegrationProjectRepository(harness.Project), harness.Authorities,
            new IntegrationReceiptRepository(harness.Receipt), harness.Checkpoints, harness.Heads, attackCollector,
            new FixedClock(DateTimeOffset.Parse("2026-08-28T10:00:00+00:00")));
        Assert.False((await attackValidation.CreatePlanAsync(attackPlan)).Succeeded);
        var attackCapture = await attackValidation.CaptureAsync(new ValidationCaptureRequest(attackPlan.ProjectId, attackPlan.Reference,
            requirement.RequirementId, terminalB.Reference));

        Assert.False(attackCapture.Succeeded);
        Assert.Equal(0, attackCollector.InvocationCount);

        var planB = new ValidationPlan(harness.Project.Id, Guid.NewGuid(), 1, terminalB.CreatedAt,
            authorityB.Reference, authorityB.PlanningContractReference, authorityB.WorkGraphReference, authorityB.WorkGraphNodeId,
            harness.Receipt.WorkspaceId, harness.Receipt.WorkspacePath, harness.Receipt.ContentHash, terminalB.Reference,
            [requirement], authorityB.HandoffPackageReference);
        var evidenceB = new IntegrationValidationEvidenceRepository();
        var legitimateCollector = new IntegrationCollectorResolver();
        var validationB = new ValidationEvidenceService(new IntegrationValidationPlanRepository(planB), evidenceB,
            new IntegrationProjectRepository(harness.Project), harness.Authorities, new IntegrationReceiptRepository(harness.Receipt),
            harness.Checkpoints, harness.Heads, legitimateCollector, new FixedClock(DateTimeOffset.Parse("2026-08-28T10:00:00+00:00")));
        Assert.True((await validationB.CreatePlanAsync(planB)).Succeeded);
        Assert.True((await validationB.CaptureAsync(new ValidationCaptureRequest(planB.ProjectId, planB.Reference,
            requirement.RequirementId, terminalB.Reference))).Succeeded);
        var gateB = new ValidationGateService(new IntegrationValidationPlanRepository(planB), evidenceB,
            new IntegrationDecisionRepository(), harness.Authorities, new IntegrationReceiptRepository(harness.Receipt), harness.Checkpoints,
            harness.Heads, harness.CheckpointService, new FixedClock(DateTimeOffset.Parse("2026-08-28T10:00:00+00:00")));
        var decisionB = await gateB.EvaluateAsync(new ValidationGateRequest(planB.ProjectId, planB.Reference, terminalB.Reference));

        Assert.Equal(1, legitimateCollector.InvocationCount);
        Assert.True(decisionB.Succeeded, decisionB.ErrorMessage);
        Assert.Equal(ValidationGateDecisionState.Satisfied, decisionB.Decision!.State);
        Assert.Equal(preRunB.Reference, terminalB.PreviousCheckpointReference);
        Assert.Equal(continuationA.Reference, preRunB.PreviousCheckpointReference);
    }

    [Fact]
    public async Task CallerCancellation_IsRecordedWithoutRetryOrCompletedClaim()
    {
        using var harness = ExecutionHarness.Create(hangAdapter: true);
        using var cancellation = new CancellationTokenSource();
        var execution = harness.Service.ExecuteAsync(harness.Request, cancellation.Token);
        await harness.Adapter.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        var result = await execution;

        Assert.Equal(BoundedExecutionStatus.Cancelled, result.Status);
        Assert.Equal(1, harness.Adapter.InvocationCount);
        Assert.True(harness.Adapter.CancellationObserved);
        Assert.DoesNotContain(harness.History.Runs, value => value.Status == ExecutionRunStatus.Completed);
        Assert.Equal(ExecutionRunStatus.Cancelled, harness.History.Runs[^1].Status);
        Assert.Equal(RecoveryCheckpointLifecycleState.Cancelled, result.TerminalCheckpoint!.LifecycleState);
        Assert.Equal(RecoveryNextSafeAction.ResolveBlocker, result.TerminalCheckpoint.NextSafeAction);
    }

    [Fact]
    public async Task ElapsedBudget_RequestsAdapterCancellationAndDoesNotRetry()
    {
        using var harness = ExecutionHarness.Create(
            hangAdapter: true,
            timeoutProvider: new FixedTimeoutProvider(TimeSpan.FromMilliseconds(80)));

        var result = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.TimedOut, result.Status);
        Assert.Equal(1, harness.Adapter.InvocationCount);
        Assert.True(harness.Adapter.CancellationObserved);
        Assert.DoesNotContain(harness.History.Runs, value => value.Status == ExecutionRunStatus.Completed);
        Assert.Equal(ExecutionRunStatus.Failed, harness.History.Runs[^1].Status);
        Assert.Equal(RecoveryCheckpointLifecycleState.Interrupted, result.TerminalCheckpoint!.LifecycleState);
    }

    [Fact]
    public async Task AdapterFailure_IsTerminalAndDoesNotRetry()
    {
        using var harness = ExecutionHarness.Create(
            adapterResult: new ExecutionAdapterResult(ExecutionAdapterOutcome.Failed, "sanitized failure", "adapter-failed"));

        var result = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.AdapterFailed, result.Status);
        Assert.Equal(1, harness.Adapter.InvocationCount);
        Assert.Equal(ExecutionRunStatus.Failed, harness.History.Runs[^1].Status);
        Assert.Equal("sanitized failure", harness.History.Runs[^1].Outcome);
        Assert.DoesNotContain(harness.History.Runs, value => value.Status == ExecutionRunStatus.Completed);
        Assert.Equal(RecoveryCheckpointLifecycleState.Failed, result.TerminalCheckpoint!.LifecycleState);
    }

    [Fact]
    public async Task ReportedToolBudgetOverrun_IsBudgetExceededWithNoSecondAttempt()
    {
        using var harness = ExecutionHarness.Create(
            adapterResult: new ExecutionAdapterResult(
                ExecutionAdapterOutcome.Succeeded,
                "completed",
                usage: new ExecutionAdapterUsageMetrics(toolInvocations: 11)));

        var result = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.BudgetExceeded, result.Status);
        Assert.Equal(1, harness.Adapter.InvocationCount);
        Assert.Equal(ExecutionRunStatus.Failed, harness.History.Runs[^1].Status);
        Assert.Equal(RecoveryCheckpointLifecycleState.Blocked, result.TerminalCheckpoint!.LifecycleState);
        Assert.Contains("budget", result.TerminalCheckpoint.Blockers[0].Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReportedModelTurnBudgetOverrun_IsBudgetExceededWithNoSecondAttempt()
    {
        using var harness = ExecutionHarness.Create(
            adapterResult: new ExecutionAdapterResult(
                ExecutionAdapterOutcome.Succeeded,
                "completed",
                usage: new ExecutionAdapterUsageMetrics(modelTurns: 3)));

        var result = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.BudgetExceeded, result.Status);
        Assert.Equal(1, harness.Adapter.InvocationCount);
        Assert.Equal(RecoveryCheckpointLifecycleState.Blocked, result.TerminalCheckpoint!.LifecycleState);
    }

    [Fact]
    public async Task ChangedFileAndLineReports_RemainEvidenceConstraints_NotIndependentAcceptance()
    {
        using var harness = ExecutionHarness.Create(
            adapterResult: new ExecutionAdapterResult(
                ExecutionAdapterOutcome.Succeeded,
                "step finished",
                usage: new ExecutionAdapterUsageMetrics(changedFiles: 99, changedLines: 999)));

        var result = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.Succeeded, result.Status);
        Assert.Equal(RecoveryNextSafeAction.RunValidation, result.TerminalCheckpoint!.NextSafeAction);
        Assert.NotEqual(ExecutionRunStatus.Accepted, harness.History.Runs[^1].Status);
    }

    [Fact]
    public async Task MissingAttemptsOrElapsedBudget_IsRejectedBeforeAdapter()
    {
        using var attemptsHarness = ExecutionHarness.Create(budgets: [new PlanningExecutionBudget(PlanningBudgetKind.ElapsedMinutes, 1)]);
        var attemptsResult = await attemptsHarness.Service.ExecuteAsync(attemptsHarness.Request);
        Assert.Equal(BoundedExecutionStatus.BudgetInvalid, attemptsResult.Status);
        Assert.Equal(0, attemptsHarness.Adapter.InvocationCount);

        using var elapsedHarness = ExecutionHarness.Create(budgets: [new PlanningExecutionBudget(PlanningBudgetKind.Attempts, 1)]);
        var elapsedResult = await elapsedHarness.Service.ExecuteAsync(elapsedHarness.Request);
        Assert.Equal(BoundedExecutionStatus.BudgetInvalid, elapsedResult.Status);
        Assert.Equal(0, elapsedHarness.Adapter.InvocationCount);
    }

    [Fact]
    public async Task InteractiveOnlyAgent_IsRejectedBeforeAdapter()
    {
        using var harness = ExecutionHarness.Create(connectionMode: AgentConnectionMode.InteractiveOnly);

        var result = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.ConnectionUnsupported, result.Status);
        Assert.Equal(0, harness.Adapter.InvocationCount);
    }

    [Theory]
    [InlineData(AgentConnectionMode.Manual)]
    [InlineData(AgentConnectionMode.Unsupported)]
    [InlineData(AgentConnectionMode.Unknown)]
    public async Task NonAutonomousConnectionModes_AreRejectedBeforeAdapter(AgentConnectionMode mode)
    {
        using var harness = ExecutionHarness.Create(connectionMode: mode);

        var result = await harness.Service.ExecuteAsync(harness.Request);

        Assert.NotEqual(BoundedExecutionStatus.Succeeded, result.Status);
        Assert.Equal(0, harness.Adapter.InvocationCount);
    }

    [Fact]
    public async Task NoProductionAdapter_FailsClosedBeforeInvocation()
    {
        using var harness = ExecutionHarness.Create(noAdapter: true);

        var result = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.AdapterUnsupported, result.Status);
        Assert.Equal(0, harness.Adapter.InvocationCount);
    }

    [Fact]
    public async Task MultipleExactAdapters_FailsClosedBeforeInvocation()
    {
        using var harness = ExecutionHarness.Create(multipleAdapters: true);

        var result = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.AdapterConfigurationConflict, result.Status);
        Assert.Equal(0, harness.Adapter.InvocationCount);
    }

    [Fact]
    public async Task PreparedWithoutReceipt_IsRejectedBeforeAdapter()
    {
        using var harness = ExecutionHarness.Create(workspaceState: WorkspaceRecoveryState.PreparedWithoutReceipt);

        var result = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.WorkspaceNotPrepared, result.Status);
        Assert.Equal(0, harness.Adapter.InvocationCount);
    }

    [Fact]
    public async Task WorkspaceConflict_IsRejectedBeforeAdapter()
    {
        using var harness = ExecutionHarness.Create(workspaceState: WorkspaceRecoveryState.Conflict);

        var result = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.WorkspaceConflict, result.Status);
        Assert.Equal(0, harness.Adapter.InvocationCount);
    }

    [Fact]
    public async Task WorkspacePlanContractMismatch_IsRejectedBeforeAdapter()
    {
        using var harness = ExecutionHarness.Create();
        var request = new BoundedExecutionRequest(
            harness.Request.ProjectId,
            harness.Request.RunId,
            harness.Request.PlanningContractReference,
            harness.Request.WorkGraphReference,
            harness.Request.WorkGraphNodeId,
            harness.Request.HandoffPackageReference,
            harness.Request.RoutingDecisionReference,
            new WorkspacePreparationPlanReference(
                harness.Plan.PlanId,
                harness.Plan.Reference.SchemaVersion,
                new string('f', 64),
                harness.Request.ProjectId),
            harness.Request.CurrentRecoveryCheckpointReference);

        var result = await harness.Service.ExecuteAsync(request);

        Assert.Equal(BoundedExecutionStatus.WorkspacePlanMismatch, result.Status);
        Assert.Equal(0, harness.Adapter.InvocationCount);
    }

    [Theory]
    [InlineData(RecoveryCheckpointLifecycleState.Blocked, BoundedExecutionStatus.CheckpointBlocked)]
    [InlineData(RecoveryCheckpointLifecycleState.ApprovalRequired, BoundedExecutionStatus.CheckpointApprovalRequired)]
    [InlineData(RecoveryCheckpointLifecycleState.Completed, BoundedExecutionStatus.CheckpointCompleted)]
    public async Task NonExecutableCheckpointStates_AreRejectedBeforeAdapter(
        RecoveryCheckpointLifecycleState state,
        BoundedExecutionStatus expected)
    {
        using var harness = ExecutionHarness.Create(checkpointState: state);

        var result = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(expected, result.Status);
        Assert.Equal(0, harness.Adapter.InvocationCount);
    }

    [Fact]
    public async Task CurrentCheckpointWithUnresolvedBlocker_IsRejectedBeforeAdapter()
    {
        using var harness = ExecutionHarness.Create(checkpointHasBlocker: true);

        var result = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.CheckpointBlocked, result.Status);
        Assert.Equal(0, harness.Adapter.InvocationCount);
    }

    [Fact]
    public async Task AuthorityPersistenceFailure_IsRejectedBeforeAdapter()
    {
        using var harness = ExecutionHarness.Create(authorityPersistenceFailure: true);

        var result = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.PersistenceUnavailable, result.Status);
        Assert.Equal(0, harness.Adapter.InvocationCount);
    }

    [Fact]
    public async Task NonActiveProject_IsRejectedBeforeAdapter()
    {
        using var harness = ExecutionHarness.Create(projectStatus: ProjectStatus.Paused);

        var result = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.ProjectNotExecutable, result.Status);
        Assert.Equal(0, harness.Adapter.InvocationCount);
    }

    [Fact]
    public async Task UnknownProject_IsRejectedBeforeAdapter()
    {
        using var harness = ExecutionHarness.Create();
        var request = new BoundedExecutionRequest(
            Guid.NewGuid(),
            harness.Request.RunId,
            harness.Request.PlanningContractReference,
            harness.Request.WorkGraphReference,
            harness.Request.WorkGraphNodeId,
            harness.Request.HandoffPackageReference,
            harness.Request.RoutingDecisionReference,
            harness.Request.WorkspacePreparationPlanReference,
            harness.Request.CurrentRecoveryCheckpointReference);

        var result = await harness.Service.ExecuteAsync(request);

        Assert.Equal(BoundedExecutionStatus.InvalidRequest, result.Status);
        Assert.Equal(0, harness.Adapter.InvocationCount);
    }

    [Fact]
    public async Task StaleContext_IsRejectedBeforeAdapter()
    {
        using var harness = ExecutionHarness.Create(staleContext: true);

        var result = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.ContractMismatch, result.Status);
        Assert.Equal(0, harness.Adapter.InvocationCount);
    }

    [Fact]
    public async Task NonCurrentCheckpoint_IsRejectedBeforeAdapter()
    {
        using var harness = ExecutionHarness.Create(checkpointIsCurrent: false);

        var result = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.CheckpointNotCurrent, result.Status);
        Assert.Equal(0, harness.Adapter.InvocationCount);
    }

    [Fact]
    public async Task WrongGraphNode_IsRejectedBeforeAdapter()
    {
        using var harness = ExecutionHarness.Create();
        var request = new BoundedExecutionRequest(
            harness.Request.ProjectId,
            harness.Request.RunId,
            harness.Request.PlanningContractReference,
            harness.Request.WorkGraphReference,
            Guid.NewGuid(),
            harness.Request.HandoffPackageReference,
            harness.Request.RoutingDecisionReference,
            harness.Request.WorkspacePreparationPlanReference,
            harness.Request.CurrentRecoveryCheckpointReference);

        var result = await harness.Service.ExecuteAsync(request);

        Assert.Equal(BoundedExecutionStatus.GraphNodeMismatch, result.Status);
        Assert.Equal(0, harness.Adapter.InvocationCount);
    }

    [Fact]
    public async Task WrongImmutableReferenceHash_IsRejectedBeforeAdapter()
    {
        using var harness = ExecutionHarness.Create();
        var request = new BoundedExecutionRequest(
            harness.Request.ProjectId,
            harness.Request.RunId,
            new PlanningExecutionContractReference(
                harness.Contract.ContractId,
                harness.Contract.Revision,
                harness.Contract.SchemaVersion,
                new string('f', 64)),
            harness.Request.WorkGraphReference,
            harness.Request.WorkGraphNodeId,
            harness.Request.HandoffPackageReference,
            harness.Request.RoutingDecisionReference,
            harness.Request.WorkspacePreparationPlanReference,
            harness.Request.CurrentRecoveryCheckpointReference);

        var result = await harness.Service.ExecuteAsync(request);

        Assert.Equal(BoundedExecutionStatus.ContractMismatch, result.Status);
        Assert.Equal(0, harness.Adapter.InvocationCount);
    }

    [Fact]
    public async Task GraphHandoffAndRoutingHashMismatches_AreRejectedBeforeAdapter()
    {
        using var graphHarness = ExecutionHarness.Create();
        var graphRequest = new BoundedExecutionRequest(
            graphHarness.Request.ProjectId,
            graphHarness.Request.RunId,
            graphHarness.Request.PlanningContractReference,
            new WorkGraphReference(
                graphHarness.Graph.GraphId,
                graphHarness.Graph.SchemaVersion,
                new string('f', 64)),
            graphHarness.Request.WorkGraphNodeId,
            graphHarness.Request.HandoffPackageReference,
            graphHarness.Request.RoutingDecisionReference,
            graphHarness.Request.WorkspacePreparationPlanReference,
            graphHarness.Request.CurrentRecoveryCheckpointReference);
        var graphResult = await graphHarness.Service.ExecuteAsync(graphRequest);
        Assert.Equal(BoundedExecutionStatus.GraphInvalid, graphResult.Status);
        Assert.Equal(0, graphHarness.Adapter.InvocationCount);

        using var handoffHarness = ExecutionHarness.Create();
        var handoffRequest = new BoundedExecutionRequest(
            handoffHarness.Request.ProjectId,
            handoffHarness.Request.RunId,
            handoffHarness.Request.PlanningContractReference,
            handoffHarness.Request.WorkGraphReference,
            handoffHarness.Request.WorkGraphNodeId,
            new HandoffPackageReference(handoffHarness.Handoff.PackageId, handoffHarness.Handoff.SchemaVersion, new string('f', 64)),
            handoffHarness.Request.RoutingDecisionReference,
            handoffHarness.Request.WorkspacePreparationPlanReference,
            handoffHarness.Request.CurrentRecoveryCheckpointReference);
        var handoffResult = await handoffHarness.Service.ExecuteAsync(handoffRequest);
        Assert.Equal(BoundedExecutionStatus.HandoffMismatch, handoffResult.Status);
        Assert.Equal(0, handoffHarness.Adapter.InvocationCount);

        using var routingHarness = ExecutionHarness.Create();
        var routingRequest = new BoundedExecutionRequest(
            routingHarness.Request.ProjectId,
            routingHarness.Request.RunId,
            routingHarness.Request.PlanningContractReference,
            routingHarness.Request.WorkGraphReference,
            routingHarness.Request.WorkGraphNodeId,
            routingHarness.Request.HandoffPackageReference,
            new RoutingDecisionReference(routingHarness.Routing.DecisionId, routingHarness.Routing.SchemaVersion, new string('f', 64)),
            routingHarness.Request.WorkspacePreparationPlanReference,
            routingHarness.Request.CurrentRecoveryCheckpointReference);
        var routingResult = await routingHarness.Service.ExecuteAsync(routingRequest);
        Assert.Equal(BoundedExecutionStatus.RoutingMismatch, routingResult.Status);
        Assert.Equal(0, routingHarness.Adapter.InvocationCount);
    }

    [Fact]
    public async Task CheckpointSelectedAgentMismatch_IsRejectedBeforeAdapter()
    {
        using var harness = ExecutionHarness.Create(checkpointExecutorId: Guid.NewGuid());

        var result = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.AgentMismatch, result.Status);
        Assert.Equal(0, harness.Adapter.InvocationCount);
    }

    [Theory]
    [InlineData(false, AgentAvailability.Unavailable, AgentAuthenticationState.NotRequired)]
    [InlineData(false, AgentAvailability.Available, AgentAuthenticationState.AuthenticationRequired)]
    [InlineData(false, AgentAvailability.Available, AgentAuthenticationState.NotRequired)]
    public async Task UnavailableOrDisabledAgent_IsRejectedBeforeAdapter(
        bool enabled,
        AgentAvailability availability,
        AgentAuthenticationState authentication)
    {
        using var harness = ExecutionHarness.Create(
            agentEnabled: enabled,
            agentAvailability: availability,
            authenticationState: authentication);

        var result = await harness.Service.ExecuteAsync(harness.Request);

        Assert.NotEqual(BoundedExecutionStatus.Succeeded, result.Status);
        Assert.Equal(0, harness.Adapter.InvocationCount);
    }

    [Fact]
    public async Task PreRunCheckpointFailure_LeavesDurableAuthorityAndReplayDoesNotInvoke()
    {
        using var harness = ExecutionHarness.Create(failCheckpointCreationNumber: 1);

        var first = await harness.Service.ExecuteAsync(harness.Request);
        var replay = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.PreRunCheckpointFailed, first.Status);
        Assert.Equal(BoundedExecutionStatus.AlreadyStarted, replay.Status);
        Assert.Equal(0, harness.Adapter.InvocationCount);
    }

    [Fact]
    public async Task CrashAfterDurableClaim_DifferentRunIdForSameReadyCheckpoint_DoesNotInvoke()
    {
        using var harness = ExecutionHarness.Create(failCheckpointCreationNumber: 1);
        var first = await harness.Service.ExecuteAsync(harness.Request);
        var replay = await harness.Service.ExecuteAsync(new BoundedExecutionRequest(
            harness.Request.ProjectId, Guid.NewGuid(), harness.Request.PlanningContractReference,
            harness.Request.WorkGraphReference, harness.Request.WorkGraphNodeId, harness.Request.HandoffPackageReference,
            harness.Request.RoutingDecisionReference, harness.Request.WorkspacePreparationPlanReference,
            harness.Request.CurrentRecoveryCheckpointReference));

        Assert.Equal(BoundedExecutionStatus.PreRunCheckpointFailed, first.Status);
        Assert.Equal(BoundedExecutionStatus.AlreadyStarted, replay.Status);
        Assert.Equal(0, harness.Adapter.InvocationCount);
    }

    [Fact]
    public async Task RunningHistoryFailure_LeavesPreCheckpointAndReplayDoesNotInvoke()
    {
        using var harness = ExecutionHarness.Create(failHistoryStatus: ExecutionRunStatus.Running);

        var first = await harness.Service.ExecuteAsync(harness.Request);
        var replay = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.RunningHistoryFailed, first.Status);
        Assert.Equal(BoundedExecutionStatus.AlreadyStarted, replay.Status);
        Assert.Equal(0, harness.Adapter.InvocationCount);
        Assert.Single(harness.CheckpointService.Created);
    }

    [Fact]
    public async Task TerminalCheckpointFailure_NeverProducesCompletedAndReplayDoesNotInvoke()
    {
        using var harness = ExecutionHarness.Create(failCheckpointCreationNumber: 2);

        var first = await harness.Service.ExecuteAsync(harness.Request);
        var replay = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.TerminalCheckpointFailed, first.Status);
        Assert.Equal(BoundedExecutionStatus.AlreadyStarted, replay.Status);
        Assert.Equal(1, harness.Adapter.InvocationCount);
        Assert.DoesNotContain(harness.History.Runs, value => value.Status == ExecutionRunStatus.Completed);
    }

    [Fact]
    public async Task TerminalHistoryFailure_PreservesCheckpointAndReplayDoesNotInvoke()
    {
        using var harness = ExecutionHarness.Create(failHistoryStatus: ExecutionRunStatus.Completed);

        var first = await harness.Service.ExecuteAsync(harness.Request);
        var replay = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.AuditPersistenceFailed, first.Status);
        Assert.Equal(BoundedExecutionStatus.AlreadyStarted, replay.Status);
        Assert.Equal(1, harness.Adapter.InvocationCount);
        Assert.Equal(2, harness.CheckpointService.Created.Count);
        Assert.Equal(RecoveryCheckpointLifecycleState.Ready, harness.CheckpointService.Created[^1].LifecycleState);
    }

    [Fact]
    public async Task NonCooperativeTimeout_ReturnsResidualOutcomeAndConservativeTerminalState()
    {
        using var harness = ExecutionHarness.Create(
            hangAdapter: true,
            ignoreCancellation: true,
            timeoutProvider: new FixedTimeoutProvider(TimeSpan.FromMilliseconds(20)),
            timing: new TestBoundedExecutionTiming(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(20)));

        try
        {
            var result = await harness.Service.ExecuteAsync(harness.Request).WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(BoundedExecutionStatus.ResidualExecutionActive, result.Status);
            Assert.Equal(ExecutionAdapterOutcome.TerminationUnconfirmed, result.AdapterResult!.Outcome);
            Assert.Equal(1, result.AdapterInvocationCount);
            Assert.Equal(1, harness.Adapter.InvocationCount);
            Assert.Equal(ExecutionRunStatus.Failed, harness.History.Runs[^1].Status);
            Assert.DoesNotContain(harness.History.Runs, value => value.Status is ExecutionRunStatus.Completed or ExecutionRunStatus.Cancelled);
            Assert.Equal(RecoveryCheckpointLifecycleState.Interrupted, result.TerminalCheckpoint!.LifecycleState);
            Assert.Equal(RecoveryNextSafeAction.ResolveBlocker, result.TerminalCheckpoint.NextSafeAction);
            Assert.Contains("termination was not confirmed", result.TerminalCheckpoint.Explanation, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            harness.Adapter.Release(new ExecutionAdapterResult(ExecutionAdapterOutcome.Failed, "test release", "test-release"));
            await harness.Adapter.ActiveTask!.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task NonCooperativeCallerCancellation_ReturnsResidualOutcomeAndDoesNotClaimCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        using var harness = ExecutionHarness.Create(
            hangAdapter: true,
            ignoreCancellation: true,
            timing: new TestBoundedExecutionTiming(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(20)));
        var execution = harness.Service.ExecuteAsync(harness.Request, cancellation.Token);
        await harness.Adapter.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        try
        {
            cancellation.Cancel();
            var result = await execution.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(BoundedExecutionStatus.ResidualExecutionActive, result.Status);
            Assert.Equal(ExecutionAdapterOutcome.TerminationUnconfirmed, result.AdapterResult!.Outcome);
            Assert.Equal(1, result.AdapterInvocationCount);
            Assert.Equal(ExecutionRunStatus.Failed, harness.History.Runs[^1].Status);
            Assert.DoesNotContain(harness.History.Runs, value => value.Status is ExecutionRunStatus.Completed or ExecutionRunStatus.Cancelled);
            Assert.Equal(RecoveryCheckpointLifecycleState.Interrupted, result.TerminalCheckpoint!.LifecycleState);
        }
        finally
        {
            harness.Adapter.Release(new ExecutionAdapterResult(ExecutionAdapterOutcome.Failed, "test release", "test-release"));
            await harness.Adapter.ActiveTask!.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task ResidualProjectGuard_BlocksSecondRunAndClearsOnlyAfterAdapterCompletion()
    {
        using var harness = ExecutionHarness.Create(
            hangAdapter: true,
            ignoreCancellation: true,
            timeoutProvider: new FixedTimeoutProvider(TimeSpan.FromMilliseconds(20)),
            timing: new TestBoundedExecutionTiming(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(20)));
        var secondRequest = NewRunRequest(harness.Request);

        try
        {
            var first = await harness.Service.ExecuteAsync(harness.Request).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(BoundedExecutionStatus.ResidualExecutionActive, first.Status);

            var blocked = await harness.Service.ExecuteAsync(secondRequest);
            Assert.Equal(BoundedExecutionStatus.ProjectBusy, blocked.Status);
            Assert.Equal(1, harness.Adapter.InvocationCount);

            var activeAdapterTask = harness.Adapter.ActiveTask!;
            harness.Adapter.Release(new ExecutionAdapterResult(ExecutionAdapterOutcome.Failed, "test release", "test-release"));
            await activeAdapterTask.WaitAsync(TimeSpan.FromSeconds(2));

            var afterCompletion = await harness.Service.ExecuteAsync(secondRequest);
            Assert.NotEqual(BoundedExecutionStatus.ProjectBusy, afterCompletion.Status);
            Assert.Equal(1, harness.Adapter.InvocationCount);
        }
        finally
        {
            harness.Adapter.Release(new ExecutionAdapterResult(ExecutionAdapterOutcome.Failed, "test release", "test-release"));
            if (harness.Adapter.ActiveTask is { } activeTask)
            {
                await activeTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
    }

    [Fact]
    public async Task TerminationUnconfirmedAdapterResult_UsesResidualTerminalMapping()
    {
        using var harness = ExecutionHarness.Create(
            adapterResult: new ExecutionAdapterResult(
                ExecutionAdapterOutcome.TerminationUnconfirmed,
                "termination uncertain",
                "termination-unconfirmed",
                mayHaveModifiedWorkspace: true));

        var result = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.ResidualExecutionActive, result.Status);
        Assert.Equal(1, result.AdapterInvocationCount);
        Assert.Equal(ExecutionRunStatus.Failed, harness.History.Runs[^1].Status);
        Assert.Equal(RecoveryCheckpointLifecycleState.Interrupted, result.TerminalCheckpoint!.LifecycleState);
        Assert.Equal(RecoveryNextSafeAction.ResolveBlocker, result.TerminalCheckpoint.NextSafeAction);
    }

    [Fact]
    public async Task AlreadyCancelledToken_ReturnsTypedResultWithoutDurableStateOrInvocation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var harness = ExecutionHarness.Create();

        var result = await harness.Service.ExecuteAsync(harness.Request, cancellation.Token);

        Assert.Equal(BoundedExecutionStatus.Cancelled, result.Status);
        Assert.Equal(0, result.AdapterInvocationCount);
        Assert.Equal(0, harness.Authorities.Count);
        Assert.Empty(harness.History.Runs);
        Assert.Empty(harness.CheckpointService.Created);
        Assert.Equal(0, harness.Adapter.InvocationCount);
    }

    [Fact]
    public async Task CancellationDuringPreflight_ReturnsTypedResultWithoutDurableStateOrInvocation()
    {
        using var cancellation = new CancellationTokenSource();
        using var harness = ExecutionHarness.Create(onContextResolved: cancellation.Cancel);

        var result = await harness.Service.ExecuteAsync(harness.Request, cancellation.Token);

        Assert.Equal(BoundedExecutionStatus.Cancelled, result.Status);
        Assert.Equal(0, result.AdapterInvocationCount);
        Assert.Null(result.Authority);
        Assert.Equal(0, harness.Authorities.Count);
        Assert.Empty(harness.History.Runs);
        Assert.Empty(harness.CheckpointService.Created);
        Assert.Equal(0, harness.Adapter.InvocationCount);
    }

    [Fact]
    public async Task CancellationImmediatelyBeforeAdapter_FinalizesTypedCancellationAndPreservesReplayAuthority()
    {
        using var cancellation = new CancellationTokenSource();
        using var harness = ExecutionHarness.Create(invocationGate: new CancellingInvocationGate(cancellation));

        var result = await harness.Service.ExecuteAsync(harness.Request, cancellation.Token);
        var replay = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.Cancelled, result.Status);
        Assert.Equal(0, result.AdapterInvocationCount);
        Assert.NotNull(result.Authority);
        Assert.Equal(BoundedExecutionStatus.AlreadyStarted, replay.Status);
        Assert.Equal(0, harness.Adapter.InvocationCount);
        Assert.DoesNotContain(harness.History.Runs, value => value.Status == ExecutionRunStatus.Completed);
        Assert.Equal(ExecutionRunStatus.Cancelled, harness.History.Runs[^1].Status);
        Assert.Equal(RecoveryCheckpointLifecycleState.Cancelled, result.TerminalCheckpoint!.LifecycleState);
        Assert.Equal(RecoveryNextSafeAction.ResolveBlocker, result.TerminalCheckpoint.NextSafeAction);
    }

    [Fact]
    public async Task SynchronousAdapterThrow_IsTypedTerminalFailureWithOneInvocation()
    {
        using var harness = ExecutionHarness.Create(throwSynchronously: true);

        var result = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.AdapterFailed, result.Status);
        Assert.Equal(ExecutionAdapterOutcome.AdapterUnavailable, result.AdapterResult!.Outcome);
        Assert.Equal(1, result.AdapterInvocationCount);
        Assert.Equal(1, harness.Adapter.InvocationCount);
        Assert.Equal(ExecutionRunStatus.Failed, harness.History.Runs[^1].Status);
        Assert.Equal(RecoveryCheckpointLifecycleState.Failed, result.TerminalCheckpoint!.LifecycleState);
        Assert.DoesNotContain("sensitive", result.AdapterResult.Summary ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(AgentSnapshotDrift.ConnectionMode)]
    [InlineData(AgentSnapshotDrift.SupportedConnectionModes)]
    [InlineData(AgentSnapshotDrift.Enabled)]
    [InlineData(AgentSnapshotDrift.Availability)]
    [InlineData(AgentSnapshotDrift.Authentication)]
    [InlineData(AgentSnapshotDrift.Entitlement)]
    [InlineData(AgentSnapshotDrift.ExecutorRoleRemoved)]
    [InlineData(AgentSnapshotDrift.RoleCapabilities)]
    [InlineData(AgentSnapshotDrift.Capabilities)]
    [InlineData(AgentSnapshotDrift.Limitations)]
    [InlineData(AgentSnapshotDrift.RegistryUpdatedAt)]
    public async Task RoutingSnapshotDrift_FailsClosedBeforeAdapter(AgentSnapshotDrift drift)
    {
        using var harness = ExecutionHarness.Create(agentSnapshotDrift: drift);

        var result = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.AgentMismatch, result.Status);
        Assert.Equal(0, result.AdapterInvocationCount);
        Assert.Equal(0, harness.Adapter.InvocationCount);
        Assert.Empty(harness.Authorities.Values);
        Assert.Empty(harness.History.Runs);
    }

    [Fact]
    public async Task SixtyThreeInputEvidenceReferences_StayAtSixtyFourAcrossCheckpointLineage()
    {
        using var harness = ExecutionHarness.Create(initialEvidenceCount: 63);

        var result = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.Succeeded, result.Status);
        Assert.Equal(1, result.AdapterInvocationCount);
        Assert.Equal(64, harness.CheckpointService.Created[0].EvidenceReferences.Count);
        Assert.Equal(64, result.TerminalCheckpoint!.EvidenceReferences.Count);
        Assert.Equal(1, harness.CheckpointService.Created[0].EvidenceReferences.Count(value => value.EvidenceId == harness.Request.RunId));
        Assert.Equal(1, result.TerminalCheckpoint.EvidenceReferences.Count(value => value.EvidenceId == harness.Request.RunId));
        Assert.Equal(RecoveryNextSafeAction.RunValidation, result.TerminalCheckpoint.NextSafeAction);
    }

    [Fact]
    public async Task SixtyFourUnrelatedEvidenceReferences_FailBeforeAuthorityAndAdapter()
    {
        using var harness = ExecutionHarness.Create(initialEvidenceCount: 64);

        var result = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.EvidenceCapacityExceeded, result.Status);
        Assert.Equal(0, result.AdapterInvocationCount);
        Assert.Equal(0, harness.Adapter.InvocationCount);
        Assert.Equal(0, harness.Authorities.Count);
        Assert.Empty(harness.History.Runs);
        Assert.Empty(harness.CheckpointService.Created);
    }

    [Fact]
    public async Task ConflictingStableRunEvidence_FailsClosedBeforeAuthorityAndAdapter()
    {
        using var harness = ExecutionHarness.Create(conflictingRunEvidence: true);

        var result = await harness.Service.ExecuteAsync(harness.Request);

        Assert.Equal(BoundedExecutionStatus.EvidenceConflict, result.Status);
        Assert.Equal(0, result.AdapterInvocationCount);
        Assert.Equal(0, harness.Adapter.InvocationCount);
        Assert.Equal(0, harness.Authorities.Count);
        Assert.Empty(harness.History.Runs);
        Assert.Empty(harness.CheckpointService.Created);
    }

    [Fact]
    public async Task ConcurrentProjectExecution_FailsFastAsProjectBusy()
    {
        using var harness = ExecutionHarness.Create(hangAdapter: true);
        var firstExecution = harness.Service.ExecuteAsync(harness.Request);
        await harness.Adapter.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var second = await harness.Service.ExecuteAsync(
            new BoundedExecutionRequest(
                harness.Request.ProjectId,
                Guid.NewGuid(),
                harness.Request.PlanningContractReference,
                harness.Request.WorkGraphReference,
                harness.Request.WorkGraphNodeId,
                harness.Request.HandoffPackageReference,
                harness.Request.RoutingDecisionReference,
                harness.Request.WorkspacePreparationPlanReference,
                harness.Request.CurrentRecoveryCheckpointReference));
        harness.Adapter.Release(new ExecutionAdapterResult(ExecutionAdapterOutcome.Cancelled, "released", "test-release"));

        var first = await firstExecution;

        Assert.Equal(BoundedExecutionStatus.ProjectBusy, second.Status);
        Assert.Equal(BoundedExecutionStatus.Cancelled, first.Status);
        Assert.Equal(1, harness.Adapter.InvocationCount);
    }

    private static BoundedExecutionRequest NewRunRequest(BoundedExecutionRequest request) => new(
        request.ProjectId,
        Guid.NewGuid(),
        request.PlanningContractReference,
        request.WorkGraphReference,
        request.WorkGraphNodeId,
        request.HandoffPackageReference,
        request.RoutingDecisionReference,
        request.WorkspacePreparationPlanReference,
        request.CurrentRecoveryCheckpointReference);

    public enum AgentSnapshotDrift
    {
        ConnectionMode,
        SupportedConnectionModes,
        Enabled,
        Availability,
        Authentication,
        Entitlement,
        ExecutorRoleRemoved,
        RoleCapabilities,
        Capabilities,
        Limitations,
        RegistryUpdatedAt
    }

    private sealed class ExecutionHarness : IDisposable
    {
        private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-28T10:00:00+00:00");

        private ExecutionHarness(
            Project project,
            ProjectContextReference context,
            PlanningExecutionContract contract,
            WorkGraph graph,
            WorkGraphNode node,
            HandoffPackage handoff,
            RoutingDecision routing,
            WorkspacePreparationPlan plan,
            WorkspacePreparationReceipt receipt,
            ProjectContextReference resolvedContext,
            RecoveryCheckpoint currentCheckpoint,
            BoundedExecutionRequest request,
            FakeAgentRegistry agents,
            FakeExecutionAdapter adapter,
            FakeWorkspaceInspection workspaceInspection,
            IExecutionAdapterResolver adapterResolver,
            FakeCheckpointRepository checkpoints,
            FakeContinuationHeadRepository heads,
            FakeCheckpointService checkpointService,
            FakeRunAuthorityRepository authorities,
            FakeHistory history,
            IExecutionBudgetTimeoutProvider timeoutProvider,
            IExecutionInvocationGate? invocationGate,
            IBoundedExecutionTiming? timing,
            Action? onContextResolved)
        {
            Project = project;
            Context = context;
            Contract = contract;
            Graph = graph;
            Node = node;
            Handoff = handoff;
            Routing = routing;
            Plan = plan;
            Receipt = receipt;
            CurrentCheckpoint = currentCheckpoint;
            Request = request;
            Agent = agents.Agent;
            Adapter = adapter;
            WorkspaceInspection = workspaceInspection;
            Checkpoints = checkpoints;
            Heads = heads;
            CheckpointService = checkpointService;
            Authorities = authorities;
            History = history;
            Service = new BoundedExecutionService(
                new FakeContextResolver(project, resolvedContext, onContextResolved),
                new FakeContractRepository(contract),
                new FakeGraphRepository(graph),
                new FakeHandoffRepository(handoff),
                new FakeRoutingRepository(routing),
                agents,
                new FakeWorkspacePlanRepository(plan),
                workspaceInspection,
                checkpoints,
                heads,
                checkpointService,
                authorities,
                adapterResolver,
                history,
                new HandoffRedactionService(),
                new FixedClock(Now),
                timeoutProvider,
                invocationGate,
                timing);
        }

        public Project Project { get; }
        public ProjectContextReference Context { get; }
        public PlanningExecutionContract Contract { get; }
        public WorkGraph Graph { get; }
        public WorkGraphNode Node { get; }
        public HandoffPackage Handoff { get; }
        public RoutingDecision Routing { get; }
        public WorkspacePreparationPlan Plan { get; }
        public WorkspacePreparationReceipt Receipt { get; }
        public RecoveryCheckpoint CurrentCheckpoint { get; }
        public BoundedExecutionRequest Request { get; }
        public EffectiveAgentDefinition Agent { get; }
        public FakeExecutionAdapter Adapter { get; }
        public FakeCheckpointRepository Checkpoints { get; }
        public FakeContinuationHeadRepository Heads { get; }
        public FakeCheckpointService CheckpointService { get; }
        public FakeRunAuthorityRepository Authorities { get; }
        public FakeHistory History { get; }
        public BoundedExecutionService Service { get; }

        public static ExecutionHarness Create(
            bool hangAdapter = false,
            ExecutionAdapterResult? adapterResult = null,
            IExecutionBudgetTimeoutProvider? timeoutProvider = null,
            AgentConnectionMode connectionMode = AgentConnectionMode.Cli,
            WorkspaceRecoveryState workspaceState = WorkspaceRecoveryState.PreparedAndRecorded,
            bool checkpointIsCurrent = true,
            int? failCheckpointCreationNumber = null,
            ExecutionRunStatus? failHistoryStatus = null,
            IReadOnlyList<PlanningExecutionBudget>? budgets = null,
            bool noAdapter = false,
            bool multipleAdapters = false,
            RecoveryCheckpointLifecycleState checkpointState = RecoveryCheckpointLifecycleState.Ready,
            bool checkpointHasBlocker = false,
            bool authorityPersistenceFailure = false,
            ProjectStatus projectStatus = ProjectStatus.Active,
            bool staleContext = false,
            Guid? checkpointExecutorId = null,
            bool agentEnabled = true,
            AgentAvailability? agentAvailability = null,
            AgentAuthenticationState authenticationState = AgentAuthenticationState.NotRequired,
            AgentEntitlementState entitlementState = AgentEntitlementState.VerifiedAvailable,
            bool ignoreCancellation = false,
            bool throwSynchronously = false,
            AgentSnapshotDrift? agentSnapshotDrift = null,
            int initialEvidenceCount = 0,
            bool conflictingRunEvidence = false,
            Guid? requestedRunId = null,
            IExecutionInvocationGate? invocationGate = null,
            IBoundedExecutionTiming? timing = null,
            Action? onContextResolved = null)
        {
            var projectId = Guid.NewGuid();
            var contextId = Guid.NewGuid();
            var agentId = Guid.NewGuid();
            var workspaceId = Guid.NewGuid();
            var contract = CreateContract(projectId, contextId, agentId, budgets);
            var context = new ProjectContextReference(
                projectId,
                contextId,
                ProjectContextContract.CurrentVersion,
                Now,
                Now,
                ProjectRepositoryContextReference.Skipped(projectId, @"C:\APO-test"),
                new ProjectTrackerContextReference(TrackerReferenceState.Skipped),
                [],
                new ProjectCurrentWorkReference(CurrentWorkState.NotSelected),
                [],
                null,
                null,
                ProjectNextSafeAction.ReadyForPlanning);
            var resolvedContext = staleContext
                ? new ProjectContextReference(
                    projectId,
                    Guid.NewGuid(),
                    ProjectContextContract.CurrentVersion,
                    Now,
                    Now,
                    ProjectRepositoryContextReference.Skipped(projectId, @"C:\APO-test"),
                    new ProjectTrackerContextReference(TrackerReferenceState.Skipped),
                    [],
                    new ProjectCurrentWorkReference(CurrentWorkState.NotSelected),
                    [],
                    null,
                    null,
                    ProjectNextSafeAction.ReadyForPlanning)
                : context;
            var project = new Project(projectId, "Execution project", @"C:\APO-test", null, projectStatus, Now, Now);
            var node = new WorkGraphNode(Guid.NewGuid(), contract.Reference);
            var graph = new WorkGraph(projectId, Guid.NewGuid(), WorkGraphSchema.CurrentVersion, Now, [node], []);
            var definition = new AgentDefinition(
                agentId,
                "Test executor",
                "Executor",
                connectionMode,
                agentAvailability ?? (connectionMode == AgentConnectionMode.Unsupported ? AgentAvailability.Unsupported : AgentAvailability.Available),
                agentEnabled,
                Now,
                Now,
                provider: "TestProvider",
                capabilities: ["bounded"],
                roleCapabilities: [AgentRole.Executor],
                supportedConnectionModes: connectionMode is AgentConnectionMode.Unknown or AgentConnectionMode.Unsupported ? null : [connectionMode],
                authenticationState: authenticationState,
                entitlementState: entitlementState,
                modelIdentifier: "TestModel");
            var routedAgent = new EffectiveAgentDefinition(projectId, definition, null);
            var currentAgent = new EffectiveAgentDefinition(projectId, CreateDriftedDefinition(definition, agentSnapshotDrift), null);
            var routing = CreateRouting(projectId, contract, contextId, routedAgent);
            var handoff = CreateHandoff(projectId, contract, context, graph, node, budgets);
            var baseSha = new string('a', 40);
            var workspacePath = $@"C:\APO-managed\{projectId:D}\{workspaceId:D}";
            var discovery = new WorkspaceRepositoryDiscovery(
                WorkspaceRepositoryDiscoveryStatus.Available,
                @"C:\APO-test",
                @"C:\APO-test",
                @"C:\APO-test\.git",
                headCommitSha: baseSha,
                branchName: "main",
                isClean: true);
            var plan = new WorkspacePreparationPlan(
                projectId,
                workspaceId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Now,
                new WorkspaceContextIdentity(projectId, contextId, 1, Now),
                contract.Reference,
                graph.Reference,
                node.NodeId,
                routing.Reference,
                discovery,
                baseSha,
                "apo-run",
                workspacePath,
                WorkspacePreparationPolicy.RequireCleanSource,
                true,
                "prepared");
            var receipt = new WorkspacePreparationReceipt(
                projectId,
                workspaceId,
                plan.CorrelationId,
                Now,
                plan.Reference,
                workspacePath,
                plan.WorkspaceBranch,
                baseSha,
                baseSha,
                @"C:\APO-test",
                "APO-test-owner");
            var runId = requestedRunId ?? Guid.NewGuid();
            var evidence = Enumerable.Range(0, initialEvidenceCount)
                .Select(index => new RecoveryEvidenceReference(Guid.NewGuid(), RecoveryEvidenceKind.Other, $"fixture:{index}", Now, RecoveryEvidenceFreshness.PointInTime))
                .ToList();
            if (conflictingRunEvidence)
            {
                evidence.Add(new RecoveryEvidenceReference(
                    runId,
                    RecoveryEvidenceKind.Other,
                    "execution-run:conflicting",
                    Now,
                    RecoveryEvidenceFreshness.PointInTime,
                    contentHash: new string('f', 64)));
            }

            var currentCheckpoint = new RecoveryCheckpoint(
                projectId,
                Guid.NewGuid(),
                RecoveryCheckpointSchema.CurrentVersion,
                Now,
                checkpointState,
                new RecoveryContextReference(contextId, 1, Now),
                contract.Reference,
                graph.Reference,
                node.NodeId,
                handoff.Reference,
                selectedAgentRoleReferences: [new RecoveryAgentRoleReference(checkpointExecutorId ?? agentId, AgentRole.Executor, routing.Reference.ToString())],
                evidenceReferences: evidence,
                gateSnapshots: checkpointState == RecoveryCheckpointLifecycleState.Ready ? [] : checkpointState == RecoveryCheckpointLifecycleState.ApprovalRequired ? [new RecoveryGateSnapshot(RecoveryGateKind.Approval, RecoveryGateState.Pending)] : [],
                blockers: checkpointHasBlocker ? [new RecoveryBlocker("test-blocker", RecoveryBlockerKind.Other, "test blocker", ownerActionRequired: true)] : [],
                nextSafeAction: RecoveryNextSafeAction.ContinueFromCheckpoint,
                explanation: "ready");
            var heads = new FakeContinuationHeadRepository(currentCheckpoint, checkpointIsCurrent);
            var checkpoints = new FakeCheckpointRepository(currentCheckpoint);
            var checkpointService = new FakeCheckpointService(context, heads, checkpoints, failCheckpointCreationNumber);
            var workspaceInspection = new FakeWorkspaceInspection(receipt, workspaceState);
            var authorities = new FakeRunAuthorityRepository(authorityPersistenceFailure);
            var history = new FakeHistory(failHistoryStatus);
            var adapter = new FakeExecutionAdapter(
                new ExecutionAdapterDescriptor("test-adapter", [connectionMode], [PlanningBudgetKind.ToolInvocations, PlanningBudgetKind.ModelTurns]),
                hangAdapter,
                adapterResult,
                ignoreCancellation,
                throwSynchronously);
            var adapters = noAdapter
                ? Array.Empty<IExecutionAdapter>()
                : multipleAdapters
                    ? new IExecutionAdapter[] { adapter, new FakeExecutionAdapter(adapter.Descriptor, false, null, false, false) }
                    : new IExecutionAdapter[] { adapter };
            var request = new BoundedExecutionRequest(
                projectId,
                runId,
                contract.Reference,
                graph.Reference,
                node.NodeId,
                handoff.Reference,
                routing.Reference,
                plan.Reference,
                currentCheckpoint.Reference);
            var harness = new ExecutionHarness(
                project,
                context,
                contract,
                graph,
                node,
                handoff,
                routing,
                plan,
                receipt,
                resolvedContext,
                currentCheckpoint,
                request,
                new FakeAgentRegistry(currentAgent),
                adapter,
                workspaceInspection,
                new ExecutionAdapterResolver(adapters),
                checkpoints,
                heads,
                checkpointService,
                authorities,
                history,
                timeoutProvider ?? new FixedTimeoutProvider(TimeSpan.FromSeconds(5)),
                invocationGate,
                timing,
                onContextResolved);
            return harness;
        }

        public FakeWorkspaceInspection WorkspaceInspection { get; }

        public void Dispose() { }

        private static AgentDefinition CreateDriftedDefinition(AgentDefinition source, AgentSnapshotDrift? drift)
        {
            if (drift is null)
            {
                return source;
            }

            var connectionMode = drift == AgentSnapshotDrift.ConnectionMode ? AgentConnectionMode.Api : source.ConnectionMode;
            var supportedConnectionModes = drift == AgentSnapshotDrift.ConnectionMode
                ? new[] { connectionMode }
                : drift == AgentSnapshotDrift.SupportedConnectionModes
                ? new[] { source.ConnectionMode, AgentConnectionMode.Api }.Distinct().ToArray()
                : source.SupportedConnectionModes;
            var enabled = drift == AgentSnapshotDrift.Enabled ? !source.Enabled : source.Enabled;
            var availability = drift == AgentSnapshotDrift.Availability ? AgentAvailability.Unavailable : source.Availability;
            var authentication = drift == AgentSnapshotDrift.Authentication ? AgentAuthenticationState.Authenticated : source.AuthenticationState;
            var entitlement = drift == AgentSnapshotDrift.Entitlement ? AgentEntitlementState.VerifiedUnavailable : source.EntitlementState;
            var roles = drift switch
            {
                AgentSnapshotDrift.ExecutorRoleRemoved => [AgentRole.Planner],
                AgentSnapshotDrift.RoleCapabilities => new[] { AgentRole.Executor, AgentRole.Reviewer },
                _ => source.RoleCapabilities
            };
            var capabilities = drift == AgentSnapshotDrift.Capabilities ? ["changed-capability"] : source.Capabilities;
            var limitations = drift == AgentSnapshotDrift.Limitations ? ["changed-limitation"] : source.Limitations;
            var updatedAt = drift == AgentSnapshotDrift.RegistryUpdatedAt ? source.UpdatedAt.AddMinutes(1) : source.UpdatedAt;

            return new AgentDefinition(
                source.Id,
                source.Name,
                source.Role,
                connectionMode,
                availability,
                enabled,
                source.CreatedAt,
                updatedAt,
                source.Provider,
                capabilities,
                limitations,
                source.CostAndQuotaMetadata,
                roles,
                supportedConnectionModes,
                authentication,
                entitlement,
                source.ModelIdentifier,
                source.RolePolicyMetadata,
                source.LastConnectionResult);
        }

        private static PlanningExecutionContract CreateContract(Guid projectId, Guid contextId, Guid agentId, IReadOnlyList<PlanningExecutionBudget>? budgets) => new(
            projectId,
            Guid.NewGuid(),
            PlanningExecutionContractSchema.CurrentVersion,
            1,
            Now,
            "APO-45 test owner",
            agentId,
            new PlanningContextBinding(contextId, ProjectContextContract.CurrentVersion),
            new PlanningWorkItem(PlanningWorkItemSource.Jira, "APO-45", "Bounded execution"),
            new PlanningRepositoryTarget(PlanningRepositoryMode.None),
            [new PlanningScopeClause("include", "one bounded step")],
            [],
            [new PlanningScopeClause("forbid", "autonomous loop")],
            [new PlanningDeliverable("step", "bounded step", true)],
            [new PlanningValidationRequirement("validate", PlanningValidationKind.Test, "later validation", true)],
            [new PlanningAcceptanceCriterion("accept", "later acceptance", true)],
            budgets ?? [
                new PlanningExecutionBudget(PlanningBudgetKind.Attempts, 1),
                new PlanningExecutionBudget(PlanningBudgetKind.ElapsedMinutes, 1),
                new PlanningExecutionBudget(PlanningBudgetKind.ToolInvocations, 10),
                new PlanningExecutionBudget(PlanningBudgetKind.ModelTurns, 2)],
            [
                new PlanningStopCondition("target", PlanningStopConditionKind.ImmutableTargetMoved, "target"),
                new PlanningStopCondition("scope", PlanningStopConditionKind.ScopeViolation, "scope"),
                new PlanningStopCondition("budget", PlanningStopConditionKind.BudgetExceeded, "budget")],
            [],
            null,
            null);

        private static RoutingDecision CreateRouting(Guid projectId, PlanningExecutionContract contract, Guid contextId, EffectiveAgentDefinition agent)
        {
            var classification = new RoutingTaskClassification(
                RoutingScopeScale.Bounded,
                RoutingTaskRisk.Low,
                RoutingBlastRadius.Local,
                RoutingValidationCost.Low,
                AgentRole.Executor,
                capacityRequirement: RoutingCapacityRequirement.NotApplicable);
            var policy = new RoutingPolicySnapshot(
                "execution-policy",
                AgentRole.Executor,
                capacityRequirement: RoutingCapacityRequirement.NotApplicable);
            var input = new RoutingInputSnapshot(
                projectId,
                contract.Reference,
                new RoutingContextReference(contextId, 1, Now),
                classification,
                policy,
                [RoutingAgentSnapshot.FromEffective(agent)],
                [],
                null,
                Now);
            var evaluation = new RoutingDecisionEngine().Evaluate(input);
            return new RoutingDecision(projectId, Guid.NewGuid(), RoutingDecisionSchema.CurrentVersion, Now, evaluation);
        }

        private static HandoffPackage CreateHandoff(
            Guid projectId,
            PlanningExecutionContract contract,
            ProjectContextReference context,
            WorkGraph graph,
            WorkGraphNode node,
            IReadOnlyList<PlanningExecutionBudget>? budgets)
        {
            var scopeBudgets = budgets ?? [new PlanningExecutionBudget(PlanningBudgetKind.Attempts, 1), new PlanningExecutionBudget(PlanningBudgetKind.ElapsedMinutes, 1), new PlanningExecutionBudget(PlanningBudgetKind.ToolInvocations, 10), new PlanningExecutionBudget(PlanningBudgetKind.ModelTurns, 2)];
            var scope = new HandoffExecutionScope(
                [new PlanningScopeClause("include", "one bounded step")],
                [],
                [new PlanningScopeClause("forbid", "replay")],
                [new PlanningDeliverable("step", "bounded step", true)],
                [new PlanningValidationRequirement("validate", PlanningValidationKind.Test, "later validation", true)],
                scopeBudgets,
                [
                    new PlanningStopCondition("target", PlanningStopConditionKind.ImmutableTargetMoved, "target"),
                    new PlanningStopCondition("scope", PlanningStopConditionKind.ScopeViolation, "scope"),
                    new PlanningStopCondition("budget", PlanningStopConditionKind.BudgetExceeded, "budget")],
                [],
                null,
                null);
            var scopeCount = 1 + 1 + 1 + 1 + scopeBudgets.Count + 3;
            return new HandoffPackage(
                projectId,
                Guid.NewGuid(),
                HandoffPackageSchema.CurrentVersion,
                Now,
                HandoffTransition.PlannerToExecutor,
                HandoffRole.Planner,
                HandoffRole.Executor,
                contract.Reference,
                contract.WorkItem,
                new HandoffContextReference(context.ContextId, context.ContractVersion, Now, Now),
                new PlanningRepositoryTarget(PlanningRepositoryMode.None),
                graph.Reference,
                node.NodeId,
                null,
                scope,
                null,
                null,
                null,
                [],
                [],
                [],
                null,
                [],
                "Execute one bounded step",
                new HandoffRedactionMetadata(false, 0, []),
                new HandoffPackageSizeMetadata(HandoffPackageLimits.MaxCanonicalPayloadBytes, 0, 0, 0, 0, 0, scopeCount));
        }
    }

    private sealed class IntegrationProjectRepository(Project project) : IProjectRepository
    {
        public Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Project>>([project]);
        public Task<Project?> GetByIdAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<Project?>(projectId == project.Id ? project : null);
        public Task UpsertAsync(Project value, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class IntegrationReceiptRepository(WorkspacePreparationReceipt receipt) : IWorkspacePreparationReceiptRepository
    {
        public Task<WorkspacePreparationReceiptWriteResult> CreateAsync(WorkspacePreparationReceipt value, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspacePreparationReceiptWriteResult(WorkspacePreparationReceiptWriteStatus.Created));
        public Task<WorkspacePreparationReceiptReadResult> GetAsync(Guid projectId, Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(projectId == receipt.ProjectId && workspaceId == receipt.WorkspaceId ? new WorkspacePreparationReceiptReadResult(WorkspacePreparationReceiptReadState.Valid, receipt) : new WorkspacePreparationReceiptReadResult(WorkspacePreparationReceiptReadState.Missing));
    }

    private sealed class IntegrationValidationPlanRepository(ValidationPlan plan) : IValidationPlanRepository
    {
        public Task<ValidationPlanRepositoryWriteResult> CreateAsync(ValidationPlan value, CancellationToken cancellationToken = default) => Task.FromResult(new ValidationPlanRepositoryWriteResult(ValidationPlanRepositoryWriteStatus.Created));
        public Task<ValidationPlanReadResult> GetAsync(Guid projectId, ValidationPlanReference reference, CancellationToken cancellationToken = default) => Task.FromResult(projectId == plan.ProjectId && Same(reference, plan.Reference) ? new ValidationPlanReadResult(ValidationPlanReadState.Valid, plan) : new ValidationPlanReadResult(ValidationPlanReadState.Missing));
        private static bool Same(ValidationPlanReference left, ValidationPlanReference right) => left.ProjectId == right.ProjectId && left.PlanId == right.PlanId && left.Revision == right.Revision && left.SchemaVersion == right.SchemaVersion && left.ContentHash == right.ContentHash;
    }

    private sealed class IntegrationValidationEvidenceRepository : IValidationEvidenceRepository
    {
        private readonly List<ValidationEvidence> _values = [];
        public Task<ValidationEvidenceRepositoryWriteResult> CreateAsync(ValidationEvidence evidence, CancellationToken cancellationToken = default) { _values.Add(evidence); return Task.FromResult(new ValidationEvidenceRepositoryWriteResult(ValidationEvidenceRepositoryWriteStatus.Created)); }
        public Task<ValidationEvidenceReadResult> GetAsync(Guid projectId, ValidationPlanReference planReference, ValidationEvidenceReference evidenceReference, CancellationToken cancellationToken = default) => Task.FromResult(_values.FirstOrDefault(value => value.ProjectId == projectId && Same(value.PlanReference, planReference) && Same(value.Reference, evidenceReference)) is { } value ? new ValidationEvidenceReadResult(ValidationEvidenceReadState.Valid, value) : new ValidationEvidenceReadResult(ValidationEvidenceReadState.Missing));
        public Task<ValidationEvidenceSetReadResult> GetForPlanAsync(Guid projectId, ValidationPlanReference planReference, CancellationToken cancellationToken = default) => Task.FromResult(new ValidationEvidenceSetReadResult(ValidationEvidenceSetReadState.Valid, _values.Where(value => value.ProjectId == projectId && Same(value.PlanReference, planReference)).ToArray()));
        private static bool Same(ValidationPlanReference left, ValidationPlanReference right) => left.ProjectId == right.ProjectId && left.PlanId == right.PlanId && left.Revision == right.Revision && left.SchemaVersion == right.SchemaVersion && left.ContentHash == right.ContentHash;
        private static bool Same(ValidationEvidenceReference left, ValidationEvidenceReference right) => left.EvidenceId == right.EvidenceId && left.SchemaVersion == right.SchemaVersion && left.ContentHash == right.ContentHash;
    }

    private sealed class IntegrationDecisionRepository : IValidationGateDecisionRepository
    {
        public Task<ValidationDecisionRepositoryWriteResult> CreateAsync(ValidationGateDecision decision, CancellationToken cancellationToken = default) => Task.FromResult(new ValidationDecisionRepositoryWriteResult(ValidationDecisionRepositoryWriteStatus.Created));
        public Task<ValidationDecisionReadResult> GetAsync(Guid projectId, Guid decisionId, CancellationToken cancellationToken = default) => Task.FromResult(new ValidationDecisionReadResult(ValidationDecisionReadState.Missing));
    }

    private sealed class IntegrationCollectorResolver : IValidationEvidenceCollectorResolver
    {
        private readonly IntegrationCollector _collector = new();
        public int InvocationCount => _collector.InvocationCount;
        public ValidationCollectorResolution Resolve(string collectorIdentifier, ValidationEvidenceKind kind) =>
            collectorIdentifier == _collector.Descriptor.Identifier && kind == ValidationEvidenceKind.Test
                ? new(ValidationCollectorResolutionStatus.Resolved, _collector)
                : new(ValidationCollectorResolutionStatus.Unsupported);
    }

    private sealed class IntegrationCollector : IValidationEvidenceCollector
    {
        public ValidationEvidenceCollectorDescriptor Descriptor { get; } = new("integration", [ValidationEvidenceKind.Test], false, true);
        public int InvocationCount { get; private set; }
        public Task<ValidationEvidence> CaptureAsync(ValidationCollectionContext context, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return Task.FromResult(new ValidationEvidence(context.Plan.ProjectId, Guid.NewGuid(), context.Plan.Reference, context.Requirement.RequirementId,
                context.Authority.RunId, context.Authority.Reference, context.Plan.PlanningContractReference, context.Plan.WorkGraphReference, context.Plan.WorkGraphNodeId,
                context.CurrentCheckpoint.Reference, context.Plan.WorkspaceId, context.Plan.WorkspacePath, context.Plan.WorkspaceReceiptContentHash,
                context.Requirement.CollectorIdentifier, context.Requirement.EvidenceKind, ValidationEvidenceState.Available, ValidationOutcome.Passed,
                context.Requirement.Coverage, context.Requirement.BaselineRelation, context.Plan.EvidenceNotBefore,
                validationDefinitionId: context.Requirement.ValidationDefinitionId));
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class FixedTimeoutProvider(TimeSpan timeout) : IExecutionBudgetTimeoutProvider
    {
        public TimeSpan GetTimeout(ExecutionBudgetEnvelope budgets) => timeout;
    }

    private sealed class TestBoundedExecutionTiming(TimeSpan finalizationTimeout, TimeSpan adapterCancellationDrainTimeout) : IBoundedExecutionTiming
    {
        public TimeSpan FinalizationTimeout { get; } = finalizationTimeout;
        public TimeSpan AdapterCancellationDrainTimeout { get; } = adapterCancellationDrainTimeout;
    }

    private sealed class CancellingInvocationGate(CancellationTokenSource cancellation) : IExecutionInvocationGate
    {
        public void BeforeAdapterInvocation() => cancellation.Cancel();
    }

    private sealed class FakeContextResolver(Project project, ProjectContextReference context, Action? onResolved) : IProjectContextResolver
    {
        public Task<ProjectContextResolution> ResolveAsync(Guid projectId, CancellationToken cancellationToken = default)
        {
            if (projectId != project.Id)
            {
                return Task.FromResult(new ProjectContextResolution(ProjectContextResolutionState.ProjectNotFound));
            }

            onResolved?.Invoke();
            return Task.FromResult(new ProjectContextResolution(ProjectContextResolutionState.Ready, new ProjectContextView(project, context, [])));
        }
    }

    private sealed class FakeContractRepository(PlanningExecutionContract contract) : IPlanningExecutionContractRepository
    {
        public Task<PlanningContractRepositoryWriteResult> CreateAsync(PlanningExecutionContract value, CancellationToken cancellationToken = default) => Task.FromResult(new PlanningContractRepositoryWriteResult(PlanningContractRepositoryWriteStatus.Created));
        public Task<PlanningContractReadResult> GetAsync(Guid projectId, Guid contractId, int revision, CancellationToken cancellationToken = default) => Task.FromResult(projectId == contract.ProjectId && contractId == contract.ContractId && revision == contract.Revision ? new PlanningContractReadResult(PlanningContractReadState.Valid, contract) : new PlanningContractReadResult(PlanningContractReadState.Missing));
        public Task<PlanningContractReadResult> GetLatestAsync(Guid projectId, Guid contractId, CancellationToken cancellationToken = default) => GetAsync(projectId, contractId, contract.Revision, cancellationToken);
        public Task<PlanningContractRevisionListResult> ListRevisionsAsync(Guid projectId, Guid contractId, CancellationToken cancellationToken = default) => Task.FromResult(new PlanningContractRevisionListResult(PlanningContractReadState.Valid, [contract]));
    }

    private sealed class FakeGraphRepository(WorkGraph graph) : IWorkGraphRepository
    {
        public Task<WorkGraphRepositoryWriteResult> CreateAsync(WorkGraph value, CancellationToken cancellationToken = default) => Task.FromResult(new WorkGraphRepositoryWriteResult(WorkGraphRepositoryWriteStatus.Created));
        public Task<WorkGraphReadResult> GetAsync(Guid projectId, Guid graphId, CancellationToken cancellationToken = default) => Task.FromResult(projectId == graph.ProjectId && graphId == graph.GraphId ? new WorkGraphReadResult(WorkGraphReadState.Valid, graph) : new WorkGraphReadResult(WorkGraphReadState.Missing));
    }

    private sealed class FakeHandoffRepository(HandoffPackage package) : IHandoffPackageRepository
    {
        public Task<HandoffPackageRepositoryWriteResult> CreateAsync(HandoffPackage value, CancellationToken cancellationToken = default) => Task.FromResult(new HandoffPackageRepositoryWriteResult(HandoffPackageRepositoryWriteStatus.Created));
        public Task<HandoffPackageReadResult> GetAsync(Guid projectId, Guid packageId, CancellationToken cancellationToken = default) => Task.FromResult(projectId == package.ProjectId && packageId == package.PackageId ? new HandoffPackageReadResult(HandoffPackageReadState.Valid, package) : new HandoffPackageReadResult(HandoffPackageReadState.Missing));
    }

    private sealed class FakeRoutingRepository(RoutingDecision decision) : IRoutingDecisionRepository
    {
        public Task<RoutingDecisionRepositoryWriteResult> CreateAsync(RoutingDecision value, CancellationToken cancellationToken = default) => Task.FromResult(new RoutingDecisionRepositoryWriteResult(RoutingDecisionRepositoryWriteStatus.Created));
        public Task<RoutingDecisionReadResult> GetAsync(Guid projectId, Guid decisionId, CancellationToken cancellationToken = default) => Task.FromResult(projectId == decision.ProjectId && decisionId == decision.DecisionId ? new RoutingDecisionReadResult(RoutingDecisionReadState.Valid, decision) : new RoutingDecisionReadResult(RoutingDecisionReadState.Missing));
    }

    private sealed class FakeAgentRegistry(EffectiveAgentDefinition agent) : IAgentRegistryService
    {
        public EffectiveAgentDefinition Agent { get; } = agent;
        public Task<IReadOnlyList<EffectiveAgentDefinition>> GetEffectiveAgentsAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<EffectiveAgentDefinition>>([Agent]);
        public Task<AgentRegistryResolution> ResolveAsync(Guid projectId, Guid agentId, CancellationToken cancellationToken = default) => Task.FromResult(projectId == Agent.ProjectId && agentId == Agent.Id ? AgentRegistryResolution.FoundResult(Agent) : AgentRegistryResolution.NotFoundResult());
    }

    private sealed class FakeWorkspacePlanRepository(WorkspacePreparationPlan plan) : IWorkspacePreparationPlanRepository
    {
        public Task<WorkspacePreparationPlanWriteResult> CreateAsync(WorkspacePreparationPlan value, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspacePreparationPlanWriteResult(WorkspacePreparationPlanWriteStatus.Created));
        public Task<WorkspacePreparationPlanReadResult> GetAsync(Guid projectId, Guid planId, CancellationToken cancellationToken = default) => Task.FromResult(projectId == plan.ProjectId && planId == plan.PlanId ? new WorkspacePreparationPlanReadResult(WorkspacePreparationPlanReadState.Valid, plan) : new WorkspacePreparationPlanReadResult(WorkspacePreparationPlanReadState.Missing));
    }

    private sealed class FakeWorkspaceInspection(WorkspacePreparationReceipt receipt, WorkspaceRecoveryState state) : IWorkspaceRecoveryInspectionService
    {
        public WorkspaceRecoveryState State { get; set; } = state;
        public Task<WorkspaceRecoveryInspectionResult> InspectAsync(WorkspacePreparationPlanReference planReference, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkspaceRecoveryInspectionResult(State, State == WorkspaceRecoveryState.PreparedAndRecorded ? receipt : null));
    }

    private sealed class FakeCheckpointRepository(RecoveryCheckpoint current) : IRecoveryCheckpointRepository
    {
        public Dictionary<Guid, RecoveryCheckpoint> Values { get; } = new() { [current.CheckpointId] = current };
        public Task<RecoveryCheckpointRepositoryWriteResult> CreateAsync(RecoveryCheckpoint checkpoint, CancellationToken cancellationToken = default) { Values[checkpoint.CheckpointId] = checkpoint; return Task.FromResult(new RecoveryCheckpointRepositoryWriteResult(RecoveryCheckpointRepositoryWriteStatus.Created)); }
        public Task<RecoveryCheckpointReadResult> GetAsync(Guid projectId, Guid checkpointId, CancellationToken cancellationToken = default) => Task.FromResult(Values.TryGetValue(checkpointId, out var checkpoint) && checkpoint.ProjectId == projectId ? new RecoveryCheckpointReadResult(RecoveryCheckpointReadState.Valid, checkpoint) : new RecoveryCheckpointReadResult(RecoveryCheckpointReadState.Missing));
    }

    private sealed class FakeContinuationHeadRepository(RecoveryCheckpoint current, bool isCurrent) : IContinuationHeadRepository
    {
        public ContinuationHead? Current { get; set; } = new ContinuationHead(current.ProjectId, ContinuationHeadSchema.CurrentVersion, 1, isCurrent ? current.Reference : new RecoveryCheckpointReference(Guid.NewGuid(), 1, new string('e', 64)), null, current.CreatedAt);
        public Task<ContinuationHeadReadResult> GetAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(Current is null ? new ContinuationHeadReadResult(ContinuationHeadReadState.Missing) : new ContinuationHeadReadResult(ContinuationHeadReadState.Valid, Current));
        public Task<ContinuationHeadRepositoryWriteResult> PublishAsync(ContinuationHead head, CancellationToken cancellationToken = default) { Current = head; return Task.FromResult(new ContinuationHeadRepositoryWriteResult(ContinuationHeadRepositoryWriteStatus.Published)); }
    }

    private sealed class FakeCheckpointService(
        ProjectContextReference context,
        FakeContinuationHeadRepository heads,
        FakeCheckpointRepository checkpoints,
        int? failCreationNumber) : IRecoveryCheckpointService
    {
        public List<RecoveryCheckpoint> Created { get; } = [];
        public Task<RecoveryCheckpointCreationResult> CreateAsync(RecoveryCheckpointCreationRequest request, CancellationToken cancellationToken = default)
        {
            if (failCreationNumber == Created.Count + 1)
            {
                return Task.FromResult(new RecoveryCheckpointCreationResult(RecoveryCheckpointCreationStatus.PersistenceUnavailable, ErrorMessage: "forced checkpoint failure"));
            }

            var checkpoint = new RecoveryCheckpoint(
                request.ProjectId,
                request.CheckpointId,
                RecoveryCheckpointSchema.CurrentVersion,
                request.CreatedAt ?? DateTimeOffset.UtcNow,
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
                request.Blockers,
                request.NextSafeAction,
                request.Explanation);
            Created.Add(checkpoint);
            checkpoints.Values[checkpoint.CheckpointId] = checkpoint;
            var generation = (heads.Current?.Generation ?? 0) + 1;
            var head = new ContinuationHead(checkpoint.ProjectId, ContinuationHeadSchema.CurrentVersion, generation, checkpoint.Reference, heads.Current?.LastSafeCheckpointReference, checkpoint.CreatedAt);
            heads.Current = head;
            return Task.FromResult(new RecoveryCheckpointCreationResult(RecoveryCheckpointCreationStatus.Created, checkpoint, head));
        }
    }

    private sealed class FakeRunAuthorityRepository(bool failCreate) : IExecutionRunAuthorityRepository
    {
        private readonly Dictionary<(Guid ProjectId, Guid RunId), ExecutionRunAuthority> _values = new();
        public IReadOnlyDictionary<(Guid ProjectId, Guid RunId), ExecutionRunAuthority> Values => _values;
        public int Count => _values.Count;

        public Task<ExecutionRunAuthorityRepositoryWriteResult> CreateAsync(ExecutionRunAuthority authority, CancellationToken cancellationToken = default) =>
            Task.FromResult(failCreate
                ? new ExecutionRunAuthorityRepositoryWriteResult(ExecutionRunAuthorityRepositoryWriteStatus.Unavailable, "forced authority failure")
                : _values.TryAdd((authority.ProjectId, authority.RunId), authority)
                ? new ExecutionRunAuthorityRepositoryWriteResult(ExecutionRunAuthorityRepositoryWriteStatus.Created)
                : new ExecutionRunAuthorityRepositoryWriteResult(ExecutionRunAuthorityRepositoryWriteStatus.RunConflict));
        public Task<ExecutionRunAuthorityReadResult> GetAsync(Guid projectId, Guid runId, CancellationToken cancellationToken = default) => Task.FromResult(_values.TryGetValue((projectId, runId), out var authority) ? new ExecutionRunAuthorityReadResult(ExecutionRunAuthorityReadState.Valid, authority) : new ExecutionRunAuthorityReadResult(ExecutionRunAuthorityReadState.Missing));
        public Task<ExecutionRunAuthorityReadResult> GetByInputCheckpointAsync(Guid projectId, RecoveryCheckpointReference inputCheckpointReference, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.Values.FirstOrDefault(authority => authority.ProjectId == projectId && authority.InputRecoveryCheckpointReference.CheckpointId == inputCheckpointReference.CheckpointId && authority.InputRecoveryCheckpointReference.SchemaVersion == inputCheckpointReference.SchemaVersion && string.Equals(authority.InputRecoveryCheckpointReference.ContentHash, inputCheckpointReference.ContentHash, StringComparison.OrdinalIgnoreCase)) is { } authority
                ? new ExecutionRunAuthorityReadResult(ExecutionRunAuthorityReadState.Valid, authority)
                : new ExecutionRunAuthorityReadResult(ExecutionRunAuthorityReadState.Missing));
    }

    private sealed class FakeHistory(ExecutionRunStatus? failStatus) : IProjectOrchestrationStore
    {
        public List<ExecutionRun> Runs { get; } = [];
        public Task AppendExecutionRunAsync(ExecutionRun run, CancellationToken cancellationToken = default)
        {
            if (run.Status == failStatus)
            {
                throw new IOException("forced history failure");
            }
            Runs.Add(run);
            return Task.CompletedTask;
        }
        public Task<HistoryReadResult<ExecutionRun>> ReadExecutionRunsAsync(Guid projectId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => Task.FromResult(new HistoryReadResult<ExecutionRun>(Runs, HistoryReadStatus.Success));
        public Task AppendEvidenceAsync(EvidenceMetadata evidence, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<HistoryReadResult<EvidenceMetadata>> ReadEvidenceAsync(Guid projectId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => Task.FromResult(new HistoryReadResult<EvidenceMetadata>([], HistoryReadStatus.Success));
        public Task AppendReviewAsync(ReviewMetadata review, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<HistoryReadResult<ReviewMetadata>> ReadReviewsAsync(Guid projectId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => Task.FromResult(new HistoryReadResult<ReviewMetadata>([], HistoryReadStatus.Success));
        public Task AppendActivityAsync(ActivityAuditRecord activity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<HistoryReadResult<ActivityAuditRecord>> ReadActivityAsync(Guid projectId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => Task.FromResult(new HistoryReadResult<ActivityAuditRecord>([], HistoryReadStatus.Success));
    }

    private sealed class FakeExecutionAdapter(
        ExecutionAdapterDescriptor descriptor,
        bool hang,
        ExecutionAdapterResult? result,
        bool ignoreCancellation,
        bool throwSynchronously) : IExecutionAdapter
    {
        private TaskCompletionSource<ExecutionAdapterResult>? _release;
        public ExecutionAdapterDescriptor Descriptor { get; } = descriptor;
        public int InvocationCount { get; private set; }
        public ExecutionAdapterRequest? LastRequest { get; private set; }
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }
        public Task<ExecutionAdapterResult>? ActiveTask => _release?.Task;

        public Task<ExecutionAdapterResult> ExecuteAsync(ExecutionAdapterRequest request, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            LastRequest = request;
            Started.TrySetResult(true);
            if (throwSynchronously)
            {
                throw new InvalidOperationException("sensitive adapter failure details");
            }

            if (!hang)
            {
                return Task.FromResult(result ?? new ExecutionAdapterResult(ExecutionAdapterOutcome.Succeeded, "completed"));
            }

            _release = new TaskCompletionSource<ExecutionAdapterResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!ignoreCancellation)
            {
                cancellationToken.Register(() =>
                {
                    CancellationObserved = true;
                    _release.TrySetResult(new ExecutionAdapterResult(ExecutionAdapterOutcome.Cancelled, "cancelled", "test-cancel"));
                });
            }

            return _release.Task;
        }

        public void Release(ExecutionAdapterResult result) => _release?.TrySetResult(result);
    }
}
