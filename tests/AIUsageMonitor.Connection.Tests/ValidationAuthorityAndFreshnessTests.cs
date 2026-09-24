using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Routing;
using AIUsageMonitor.Application.Validation;
using AIUsageMonitor.Application.Workspaces;

namespace AIUsageMonitor.Connection.Tests;

public sealed class ValidationAuthorityAndFreshnessTests
{
    [Fact]
    public void ProjectMismatchFailsClosed()
    {
        var fixture = CreateFixture();
        Assert.False(Validate(fixture, CreateAuthority(fixture, projectId: Guid.NewGuid())).IsValid);
    }

    [Fact]
    public void RunReferenceMismatchFailsClosed()
    {
        var fixture = CreateFixture();
        Assert.False(Validate(fixture, CreateAuthority(fixture, runId: Guid.NewGuid())).IsValid);
    }

    [Fact]
    public void PlanningContractMismatchFailsClosed()
    {
        var fixture = CreateFixture();
        Assert.False(Validate(fixture, CreateAuthority(fixture, contract: new PlanningExecutionContractReference(Guid.NewGuid(), 2, 1, Hash('1')))).IsValid);
    }

    [Fact]
    public void WorkGraphReferenceMismatchFailsClosed()
    {
        var fixture = CreateFixture();
        Assert.False(Validate(fixture, CreateAuthority(fixture, graph: new WorkGraphReference(Guid.NewGuid(), 2, Hash('1')))).IsValid);
    }

    [Fact]
    public void WorkGraphNodeMismatchFailsClosed()
    {
        var fixture = CreateFixture();
        Assert.False(Validate(fixture, CreateAuthority(fixture, nodeId: Guid.NewGuid())).IsValid);
    }

    [Fact]
    public void HandoffReferenceMismatchFailsClosed()
    {
        var fixture = CreateFixture();
        Assert.False(Validate(fixture, CreateAuthority(fixture, handoff: new HandoffPackageReference(Guid.NewGuid(), 2, Hash('1')))).IsValid);
    }

    [Fact]
    public void WorkspaceIdMismatchFailsClosed()
    {
        var fixture = CreateFixture();
        Assert.False(Validate(fixture, CreateAuthority(fixture, workspaceId: Guid.NewGuid())).IsValid);
    }

    [Fact]
    public void WorkspacePathMismatchFailsClosed()
    {
        var fixture = CreateFixture();
        Assert.False(Validate(fixture, CreateAuthority(fixture, workspacePath: Path.Combine(fixture.Workspace, "other"))).IsValid);
    }

    [Fact]
    public void WorkspaceReceiptHashMismatchFailsClosed()
    {
        var fixture = CreateFixture();
        var receipt = CreateReceipt(fixture, actualHead: Sha40('1'));
        var result = ValidationAuthorityBindingValidator.Validate(fixture.Plan, fixture.Authority, receipt, fixture.Checkpoint);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void CurrentRecoveryCheckpointMismatchFailsClosed()
    {
        var fixture = CreateFixture();
        var checkpoint = CreateCheckpoint(fixture, Guid.NewGuid());
        Assert.False(Validate(fixture, fixture.Authority, checkpoint).IsValid);
    }

    [Fact]
    public void TerminalCheckpointCannotImpersonateAuthorityInput()
    {
        var fixture = CreateFixture();
        Assert.False(Validate(fixture, CreateAuthority(fixture, inputCheckpoint: fixture.Checkpoint.Reference)).IsValid);
    }

    [Fact]
    public void ExactAuthorityBindingComputesDeterministicNotBefore()
    {
        var fixture = CreateFixture();
        var first = Validate(fixture, fixture.Authority);
        var second = Validate(fixture, fixture.Authority);

        Assert.True(first.IsValid);
        Assert.Equal(first.NotBefore, second.NotBefore);
        Assert.Equal(fixture.Now, first.NotBefore);
    }

    [Fact]
    public void EvidenceExactlyAtLowerBoundaryIsAccepted()
    {
        var fixture = CreateFixture();
        var evidence = CreateEvidence(fixture, fixture.Plan.Requirements[0], fixture.Plan.EvidenceNotBefore);

        var decision = ValidationGateEvaluator.Evaluate(fixture.Plan, [evidence], fixture.Now.AddMinutes(1));

        Assert.Equal(ValidationGateDecisionState.Satisfied, decision.State);
    }

    [Fact]
    public void EvidenceJustBeforeLowerBoundaryIsStale()
    {
        var fixture = CreateFixture();
        var evidence = CreateEvidence(fixture, fixture.Plan.Requirements[0], fixture.Plan.EvidenceNotBefore.AddTicks(-1));

        var decision = ValidationGateEvaluator.Evaluate(fixture.Plan, [evidence], fixture.Now.AddMinutes(1));

        Assert.Equal(ValidationGateDecisionState.Stale, decision.State);
        Assert.Equal(ValidationReasonCodes.EvidenceBeforeValidationEpoch, decision.RequirementDecisions.Single().ReasonCode);
    }

    [Fact]
    public void EvidenceExactlyAtMaxAgeIsAccepted()
    {
        var fixture = CreateFixture(new ValidationRequirement("tests", ValidationEvidenceKind.Test, true, ValidationCoverageScope.Targeted, ValidationBaselineRelation.Standalone, "collector", maxAge: TimeSpan.FromMinutes(5)));
        var evidence = CreateEvidence(fixture, fixture.Plan.Requirements[0], fixture.Now.AddMinutes(5));

        var decision = ValidationGateEvaluator.Evaluate(fixture.Plan, [evidence], fixture.Now.AddMinutes(10));

        Assert.Equal(ValidationGateDecisionState.Satisfied, decision.State);
    }

    [Fact]
    public void EvidenceJustBeyondMaxAgeIsStale()
    {
        var fixture = CreateFixture(new ValidationRequirement("tests", ValidationEvidenceKind.Test, true, ValidationCoverageScope.Targeted, ValidationBaselineRelation.Standalone, "collector", maxAge: TimeSpan.FromMinutes(5)));
        var evidence = CreateEvidence(fixture, fixture.Plan.Requirements[0], fixture.Now.AddMinutes(5).AddTicks(-1));

        var decision = ValidationGateEvaluator.Evaluate(fixture.Plan, [evidence], fixture.Now.AddMinutes(10));

        Assert.Equal(ValidationGateDecisionState.Stale, decision.State);
        Assert.Equal(ValidationReasonCodes.EvidenceStale, decision.RequirementDecisions.Single().ReasonCode);
    }

    [Fact]
    public void ProviderStaleEvidenceHasExplicitStaleDecision()
    {
        var fixture = CreateFixture();
        var evidence = CreateEvidence(fixture, fixture.Plan.Requirements[0], fixture.Now, ValidationEvidenceState.Stale);

        var decision = ValidationGateEvaluator.Evaluate(fixture.Plan, [evidence], fixture.Now);

        Assert.Equal(ValidationGateDecisionState.Stale, decision.State);
        Assert.Equal(ValidationReasonCodes.ProviderEvidenceStale, decision.RequirementDecisions.Single().ReasonCode);
    }

    [Fact]
    public void FutureEvidenceFailsClosedWithTimeIntegrityReason()
    {
        var fixture = CreateFixture();
        var evidence = CreateEvidence(fixture, fixture.Plan.Requirements[0], fixture.Now.AddTicks(1));

        var decision = ValidationGateEvaluator.Evaluate(fixture.Plan, [evidence], fixture.Now);

        Assert.Equal(ValidationGateDecisionState.Stale, decision.State);
        Assert.Equal(ValidationReasonCodes.EvidenceTimestampInFuture, decision.RequirementDecisions.Single().ReasonCode);
        Assert.False(decision.RequirementDecisions.Single().Fresh);
    }

    private static ValidationAuthorityBindingResult Validate(Fixture fixture, ExecutionRunAuthority authority, RecoveryCheckpoint? checkpoint = null) =>
        ValidationAuthorityBindingValidator.Validate(fixture.Plan, authority, fixture.Receipt, checkpoint ?? fixture.Checkpoint);

    private static Fixture CreateFixture(ValidationRequirement? requirement = null)
    {
        var now = new DateTimeOffset(2026, 8, 31, 10, 0, 0, TimeSpan.Zero);
        var projectId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var contract = new PlanningExecutionContractReference(Guid.NewGuid(), 1, 1, Hash('a'));
        var graph = new WorkGraphReference(Guid.NewGuid(), 1, Hash('b'));
        var handoff = new HandoffPackageReference(Guid.NewGuid(), 1, Hash('c'));
        var routing = new RoutingDecisionReference(Guid.NewGuid(), 1, Hash('d'));
        var workspacePlan = new WorkspacePreparationPlanReference(Guid.NewGuid(), 1, Hash('e'), projectId);
        var workspace = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "apo-authority-tests"));
        var inputCheckpoint = new RecoveryCheckpoint(projectId, Guid.NewGuid(), RecoveryCheckpointSchema.CurrentVersion, now.AddMinutes(-2),
            RecoveryCheckpointLifecycleState.Ready, new RecoveryContextReference(Guid.NewGuid(), 1, now), contract, graph, Guid.NewGuid(), handoff,
            nextSafeAction: RecoveryNextSafeAction.ContinueFromCheckpoint);
        var receipt = CreateReceipt(projectId, workspaceId, workspacePlan, workspace, now, Sha40('f'));
        var authority = new ExecutionRunAuthority(projectId, Guid.NewGuid(), now.AddMinutes(-1), contract, graph, inputCheckpoint.WorkGraphNodeId!.Value, handoff, routing, workspacePlan,
            workspaceId, workspace, receipt.ContentHash, inputCheckpoint.Reference, Guid.NewGuid(), "provider", "model", AgentConnectionMode.Cli, "adapter", new ExecutionBudgetEnvelope(1, 1));
        var executionEvidence = new RecoveryEvidenceReference(authority.RunId, RecoveryEvidenceKind.Other,
            $"execution-run:{authority.ProjectId:D}/{authority.RunId:D}/{authority.ContentHash}", authority.CreatedAt,
            RecoveryEvidenceFreshness.PointInTime, contentHash: authority.ContentHash);
        var preRunCheckpoint = new RecoveryCheckpoint(projectId, Guid.NewGuid(), RecoveryCheckpointSchema.CurrentVersion, now,
            RecoveryCheckpointLifecycleState.Waiting, inputCheckpoint.Context, contract, graph, inputCheckpoint.WorkGraphNodeId, handoff,
            previousCheckpointReference: inputCheckpoint.Reference, evidenceReferences: [executionEvidence], nextSafeAction: RecoveryNextSafeAction.ResolveBlocker);
        var checkpoint = new RecoveryCheckpoint(projectId, Guid.NewGuid(), RecoveryCheckpointSchema.CurrentVersion, now,
            RecoveryCheckpointLifecycleState.Ready, inputCheckpoint.Context, contract, graph, inputCheckpoint.WorkGraphNodeId, handoff,
            previousCheckpointReference: preRunCheckpoint.Reference, evidenceReferences: [executionEvidence], nextSafeAction: RecoveryNextSafeAction.RunValidation);
        var plan = new ValidationPlan(projectId, Guid.NewGuid(), 1, now, authority.Reference, contract, graph, checkpoint.WorkGraphNodeId!.Value,
            workspaceId, workspace, receipt.ContentHash, checkpoint.Reference,
            [requirement ?? new ValidationRequirement("tests", ValidationEvidenceKind.Test, true, ValidationCoverageScope.Targeted, ValidationBaselineRelation.Standalone, "collector")], handoff);
        return new(now, workspace, plan, authority, receipt, inputCheckpoint, preRunCheckpoint, checkpoint);
    }

    private static ExecutionRunAuthority CreateAuthority(Fixture fixture, Guid? projectId = null, Guid? runId = null,
        PlanningExecutionContractReference? contract = null, WorkGraphReference? graph = null, Guid? nodeId = null,
        HandoffPackageReference? handoff = null, Guid? workspaceId = null, string? workspacePath = null,
        RecoveryCheckpointReference? inputCheckpoint = null)
    {
        var effectiveProject = projectId ?? fixture.Plan.ProjectId;
        var workspacePlan = effectiveProject == fixture.Plan.ProjectId
            ? new WorkspacePreparationPlanReference(Guid.NewGuid(), 1, Hash('e'), effectiveProject)
            : new WorkspacePreparationPlanReference(Guid.NewGuid(), 1, Hash('e'));
        return new ExecutionRunAuthority(effectiveProject, runId ?? fixture.Authority.RunId, fixture.Now,
            contract ?? fixture.Plan.PlanningContractReference, graph ?? fixture.Plan.WorkGraphReference, nodeId ?? fixture.Plan.WorkGraphNodeId,
            handoff ?? fixture.Authority.HandoffPackageReference, fixture.Authority.RoutingDecisionReference, workspacePlan,
            workspaceId ?? fixture.Plan.WorkspaceId, workspacePath ?? fixture.Plan.WorkspacePath,
            fixture.Plan.WorkspaceReceiptContentHash, inputCheckpoint ?? fixture.Authority.InputRecoveryCheckpointReference,
            fixture.Authority.AgentId, fixture.Authority.Provider, fixture.Authority.ModelIdentifier, fixture.Authority.ConnectionMode,
            fixture.Authority.AdapterIdentifier, fixture.Authority.Budgets);
    }

    private static WorkspacePreparationReceipt CreateReceipt(Fixture fixture, string actualHead) =>
        CreateReceipt(fixture.Plan.ProjectId, fixture.Plan.WorkspaceId,
            new WorkspacePreparationPlanReference(Guid.NewGuid(), 1, Hash('e'), fixture.Plan.ProjectId), fixture.Workspace,
            fixture.Now, actualHead);

    private static WorkspacePreparationReceipt CreateReceipt(Guid projectId, Guid workspaceId, WorkspacePreparationPlanReference planReference,
        string workspace, DateTimeOffset now, string actualHead) =>
        new(projectId, workspaceId, Guid.NewGuid(), now, planReference, workspace, "main", Sha40('f'), actualHead, "local-repository", "test");

    private static RecoveryCheckpoint CreateCheckpoint(Fixture fixture, Guid id) =>
        new(fixture.Plan.ProjectId, id, RecoveryCheckpointSchema.CurrentVersion, fixture.Now, RecoveryCheckpointLifecycleState.Ready,
            fixture.Checkpoint.Context, fixture.Plan.PlanningContractReference, fixture.Plan.WorkGraphReference, fixture.Plan.WorkGraphNodeId,
            fixture.Plan.HandoffPackageReference, nextSafeAction: RecoveryNextSafeAction.RunValidation);

    private static ValidationEvidence CreateEvidence(Fixture fixture, ValidationRequirement requirement, DateTimeOffset capturedAt,
        ValidationEvidenceState state = ValidationEvidenceState.Available) =>
        new(fixture.Plan.ProjectId, Guid.NewGuid(), fixture.Plan.Reference, requirement.RequirementId, fixture.Authority.RunId,
            fixture.Authority.Reference, fixture.Plan.PlanningContractReference, fixture.Plan.WorkGraphReference, fixture.Plan.WorkGraphNodeId,
            fixture.Plan.CurrentRecoveryCheckpointReference, fixture.Plan.WorkspaceId, fixture.Plan.WorkspacePath, fixture.Plan.WorkspaceReceiptContentHash,
            requirement.CollectorIdentifier, requirement.EvidenceKind, state, state == ValidationEvidenceState.Available ? ValidationOutcome.Passed : ValidationOutcome.Unknown,
            requirement.Coverage, requirement.BaselineRelation, capturedAt, validationDefinitionId: requirement.ValidationDefinitionId);

    private static string Hash(char value) => new(value, 64);
    private static string Sha40(char value) => new(value, 40);

    private sealed record Fixture(DateTimeOffset Now, string Workspace, ValidationPlan Plan, ExecutionRunAuthority Authority,
        WorkspacePreparationReceipt Receipt, RecoveryCheckpoint InputCheckpoint, RecoveryCheckpoint PreRunCheckpoint, RecoveryCheckpoint Checkpoint);
}
