using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.RemoteEvidence;
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

public sealed class ValidationApo48RemediationTests
{
    [Fact]
    public async Task RemoteCiExplicitPullRequestBindsToPullRequestHeadInsteadOfBranch()
    {
        var requirement = RemoteRequirement(pullRequestNumber: 42, expectedCommit: "B");
        var fixture = CreateFixture(requirement);
        var collector = new RemoteValidationEvidenceCollector(new FakeRemoteService(Remote("B", "B", RemoteCiState.Passing, ["B"])));

        var evidence = await collector.CaptureAsync(fixture.Context);

        Assert.Equal(ValidationEvidenceState.Available, evidence.State);
        Assert.Equal(ValidationOutcome.Passed, evidence.Outcome);
        Assert.Equal("B", evidence.RemoteCommitId);
    }

    [Fact]
    public async Task RemoteCiWrongExpectedShaCannotSatisfyGate()
    {
        var requirement = RemoteRequirement(pullRequestNumber: 42, expectedCommit: "A");
        var fixture = CreateFixture(requirement);
        var collector = new RemoteValidationEvidenceCollector(new FakeRemoteService(Remote("B", "B", RemoteCiState.Passing, ["B"])));
        var evidence = await collector.CaptureAsync(fixture.Context);

        var decision = ValidationGateEvaluator.Evaluate(fixture.Plan, [evidence], fixture.Now.AddMinutes(1));

        Assert.Equal(ValidationGateDecisionState.Blocked, decision.State);
        Assert.Equal(ValidationReasonCodes.RepositoryMismatch, decision.RequirementDecisions.Single().ReasonCode);
    }

    [Fact]
    public async Task RemoteCiConflictingCommitEvidenceFailsClosed()
    {
        var fixture = CreateFixture(RemoteRequirement(pullRequestNumber: 42, expectedCommit: "B"));
        var collector = new RemoteValidationEvidenceCollector(new FakeRemoteService(Remote("B", "B", RemoteCiState.Passing, ["B", "C"])));

        var evidence = await collector.CaptureAsync(fixture.Context);

        Assert.Equal(ValidationEvidenceState.Invalid, evidence.State);
        Assert.Equal(ValidationReasonCodes.RemoteCiCommitConflict, evidence.ReasonCode);
        Assert.NotEqual(ValidationOutcome.Passed, evidence.Outcome);
    }

    [Fact]
    public async Task RemoteCiMissingCommitEvidenceCannotPass()
    {
        var fixture = CreateFixture(RemoteRequirement(pullRequestNumber: 42, expectedCommit: "B"));
        var remote = Remote("B", "B", RemoteCiState.Passing, [null]);
        var collector = new RemoteValidationEvidenceCollector(new FakeRemoteService(remote));

        var evidence = await collector.CaptureAsync(fixture.Context);

        Assert.Equal(ValidationEvidenceState.Invalid, evidence.State);
        Assert.Equal(ValidationReasonCodes.RemoteCiCommitIdentityMissing, evidence.ReasonCode);
    }

    [Fact]
    public async Task RemoteCiFailingEvidencePreservesFailureOutcome()
    {
        var fixture = CreateFixture(RemoteRequirement(pullRequestNumber: 42, expectedCommit: "B"));
        var collector = new RemoteValidationEvidenceCollector(new FakeRemoteService(Remote("B", "B", RemoteCiState.Failing, ["B"])));

        var evidence = await collector.CaptureAsync(fixture.Context);

        Assert.Equal(ValidationEvidenceState.Available, evidence.State);
        Assert.Equal(ValidationOutcome.Failed, evidence.Outcome);
    }

    [Fact]
    public async Task RemoteCiPartialEvidenceCannotBecomePassedFromAggregateResult()
    {
        var fixture = CreateFixture(RemoteRequirement(expectedCommit: "B"));
        var collector = new RemoteValidationEvidenceCollector(new FakeRemoteService(
            Remote("B", null, RemoteCiState.Passing, ["B"], RemoteEvidenceState.Partial)));

        var evidence = await collector.CaptureAsync(fixture.Context);

        Assert.Equal(ValidationEvidenceState.Partial, evidence.State);
        Assert.Equal(ValidationOutcome.Unknown, evidence.Outcome);
    }

    [Theory]
    [InlineData(RemoteEvidenceState.Partial, ValidationEvidenceState.Partial)]
    [InlineData(RemoteEvidenceState.AuthenticationRequired, ValidationEvidenceState.AuthenticationRequired)]
    [InlineData(RemoteEvidenceState.PermissionDenied, ValidationEvidenceState.PermissionDenied)]
    [InlineData(RemoteEvidenceState.RateLimited, ValidationEvidenceState.RateLimited)]
    [InlineData(RemoteEvidenceState.Stale, ValidationEvidenceState.Stale)]
    [InlineData(RemoteEvidenceState.Unsupported, ValidationEvidenceState.Unsupported)]
    [InlineData(RemoteEvidenceState.Unavailable, ValidationEvidenceState.Unavailable)]
    [InlineData(RemoteEvidenceState.NotConfigured, ValidationEvidenceState.Missing)]
    [InlineData(RemoteEvidenceState.InvalidResponse, ValidationEvidenceState.Invalid)]
    public async Task RemoteCiNonPassingStateIsNeverUpgraded(RemoteEvidenceState remoteState, ValidationEvidenceState expectedState)
    {
        var fixture = CreateFixture(RemoteRequirement());
        var remote = Remote("A", null, RemoteCiState.Unknown, [], remoteState);
        var collector = new RemoteValidationEvidenceCollector(new FakeRemoteService(remote));

        var evidence = await collector.CaptureAsync(fixture.Context);

        Assert.Equal(expectedState, evidence.State);
        Assert.NotEqual(ValidationOutcome.Passed, evidence.Outcome);
    }

    [Fact]
    public async Task TrustedPlanCreationRejectsAuthorityMismatchWithoutPersistingPlan()
    {
        var fixture = CreateFixture();
        var plans = new FakePlanRepository(fixture.Plan);
        var service = CreateEvidenceService(fixture, plans, new FakeEvidenceRepository([]), new FakeCollectorResolver());
        var mismatched = CreateAuthority(fixture, runId: Guid.NewGuid());
        var authorities = new FakeAuthorityRepository(mismatched);
        service = CreateEvidenceService(fixture, plans, new FakeEvidenceRepository([]), new FakeCollectorResolver(), authorities);

        var result = await service.CreatePlanAsync(fixture.Plan);

        Assert.False(result.Succeeded);
        Assert.Equal(0, plans.CreateCalls);
    }

    [Fact]
    public async Task CaptureDoesNotInvokeCollectorAfterAuthorityMismatch()
    {
        var fixture = CreateFixture();
        var collector = new FakeCollector();
        var resolver = new FakeCollectorResolver(collector);
        var service = CreateEvidenceService(fixture, new FakePlanRepository(fixture.Plan), new FakeEvidenceRepository([]), resolver,
            new FakeAuthorityRepository(CreateAuthority(fixture, contract: new PlanningExecutionContractReference(Guid.NewGuid(), 2, 1, Hash('1')))));

        var result = await service.CaptureAsync(new ValidationCaptureRequest(fixture.Plan.ProjectId, fixture.Plan.Reference,
            fixture.Requirement.RequirementId, fixture.Plan.CurrentRecoveryCheckpointReference));

        Assert.False(result.Succeeded);
        Assert.Equal(0, collector.InvocationCount);
    }

    [Fact]
    public async Task PreRunCheckpointCannotImpersonateTheValidationTerminal()
    {
        var fixture = CreateFixture();
        var plan = PlanFor(fixture, fixture.InputCheckpoint, fixture.Now, fixture.Now);

        var result = await ValidateAsync(fixture, plan, checkpoint: fixture.InputCheckpoint);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task WrongRunTerminalCannotValidateThePlan()
    {
        var fixture = CreateFixture();

        var result = await ValidateAsync(fixture, authority: CreateAuthority(fixture, runId: Guid.NewGuid()));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task NonCurrentTerminalCannotValidateThePlan()
    {
        var fixture = CreateFixture();
        var heads = new FakeContinuationHeadRepository(fixture.Checkpoint) { Latest = new RecoveryCheckpointReference(Guid.NewGuid(), 1, Hash('9')) };

        var result = await ValidateAsync(fixture, heads: heads);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task MissingExecutionEvidenceCannotValidateTheTerminal()
    {
        var fixture = CreateFixture();
        var terminal = RebuildTerminal(fixture, []);

        var result = await ValidateAsync(fixture, PlanFor(fixture, terminal), checkpoint: terminal,
            heads: new FakeContinuationHeadRepository(terminal));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task WrongExecutionEvidenceCannotValidateTheTerminal()
    {
        var fixture = CreateFixture();
        var wrong = new RecoveryEvidenceReference(fixture.Authority.RunId, RecoveryEvidenceKind.Other, "execution-run:wrong",
            fixture.Authority.CreatedAt, RecoveryEvidenceFreshness.PointInTime, contentHash: Hash('9'));
        var terminal = RebuildTerminal(fixture, [wrong]);

        var result = await ValidateAsync(fixture, PlanFor(fixture, terminal), checkpoint: terminal,
            heads: new FakeContinuationHeadRepository(terminal));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task InheritedExecutionHistoryIsAcceptedWhenTargetIsIntroducedAtPreRun()
    {
        var fixture = CreateFixture(inheritedExecutionEvidenceCount: 2);

        var result = await ValidateAsync(fixture);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task TargetExecutionEvidenceAlreadyPresentAtInputFailsClosed()
    {
        var fixture = CreateFixture();
        var input = RebuildCheckpoint(fixture.InputCheckpoint, fixture.InputCheckpoint.EvidenceReferences.Append(CreateExecutionEvidence(fixture.Authority)).ToArray());

        var result = await ValidateAsync(fixture, inputCheckpoint: input);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ExtraExecutionEvidenceIntroducedAtPreRunFailsClosed()
    {
        var fixture = CreateFixture();
        var rogue = CreateExecutionEvidence(fixture.Plan.ProjectId, Guid.NewGuid(), Hash('9'), fixture.Now);
        var preRun = RebuildCheckpoint(fixture.PreRunCheckpoint, fixture.PreRunCheckpoint.EvidenceReferences.Append(rogue).ToArray());
        var terminal = RebuildCheckpoint(fixture.Checkpoint, preRun.EvidenceReferences, preRun.Reference);

        var result = await ValidateAsync(fixture, PlanFor(fixture, terminal), checkpoint: terminal, preRunCheckpoint: preRun);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ExtraExecutionEvidenceIntroducedAtTerminalFailsClosed()
    {
        var fixture = CreateFixture();
        var rogue = CreateExecutionEvidence(fixture.Plan.ProjectId, Guid.NewGuid(), Hash('9'), fixture.Now);
        var terminal = RebuildCheckpoint(fixture.Checkpoint, fixture.Checkpoint.EvidenceReferences.Append(rogue).ToArray());

        var result = await ValidateAsync(fixture, PlanFor(fixture, terminal), checkpoint: terminal);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task WrongDirectPredecessorFailsClosed()
    {
        var fixture = CreateFixture();
        var terminal = RebuildCheckpoint(fixture.Checkpoint, fixture.Checkpoint.EvidenceReferences, fixture.InputCheckpoint.Reference);

        var result = await ValidateAsync(fixture, PlanFor(fixture, terminal), checkpoint: terminal);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task PreRunWrongInputFailsClosed()
    {
        var fixture = CreateFixture();
        var preRun = RebuildCheckpoint(fixture.PreRunCheckpoint, fixture.PreRunCheckpoint.EvidenceReferences,
            new RecoveryCheckpointReference(Guid.NewGuid(), RecoveryCheckpointSchema.CurrentVersion, Hash('8')));
        var terminal = RebuildCheckpoint(fixture.Checkpoint, preRun.EvidenceReferences, preRun.Reference);

        var result = await ValidateAsync(fixture, PlanFor(fixture, terminal), checkpoint: terminal, preRunCheckpoint: preRun);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task MissingTargetEvidenceOnPreRunFailsClosed()
    {
        var fixture = CreateFixture();
        var preRun = RebuildCheckpoint(fixture.PreRunCheckpoint, []);
        var terminal = RebuildCheckpoint(fixture.Checkpoint, fixture.Checkpoint.EvidenceReferences, preRun.Reference);

        var result = await ValidateAsync(fixture, PlanFor(fixture, terminal), checkpoint: terminal, preRunCheckpoint: preRun);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task MissingTargetEvidenceOnTerminalFailsClosed()
    {
        var fixture = CreateFixture();
        var terminal = RebuildCheckpoint(fixture.Checkpoint, []);

        var result = await ValidateAsync(fixture, PlanFor(fixture, terminal), checkpoint: terminal);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task WrongTargetAuthorityHashOrReferenceFailsClosed()
    {
        var fixture = CreateFixture();
        var wrongHash = Hash('9');
        var wrong = CreateExecutionEvidence(fixture.Plan.ProjectId, fixture.Authority.RunId, wrongHash, fixture.Authority.CreatedAt);
        var preRun = RebuildCheckpoint(fixture.PreRunCheckpoint, [wrong]);
        var terminal = RebuildCheckpoint(fixture.Checkpoint, [wrong], preRun.Reference);

        var result = await ValidateAsync(fixture, PlanFor(fixture, terminal), checkpoint: terminal, preRunCheckpoint: preRun);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task BrokenCheckpointLineageCannotValidateTheTerminal()
    {
        var fixture = CreateFixture();
        var terminal = RebuildTerminal(fixture, fixture.Checkpoint.EvidenceReferences,
            new RecoveryCheckpointReference(Guid.NewGuid(), 1, Hash('9')));

        var result = await ValidateAsync(fixture, PlanFor(fixture, terminal), checkpoint: terminal,
            heads: new FakeContinuationHeadRepository(terminal));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidationNotBeforeIncludesTheActualTerminalTimestamp()
    {
        var fixture = CreateFixture();
        var plan = PlanFor(fixture, fixture.Checkpoint, fixture.Checkpoint.CreatedAt, fixture.Checkpoint.CreatedAt);

        var result = await ValidateAsync(fixture, plan);

        Assert.True(result.IsValid);
        Assert.Equal(fixture.Checkpoint.CreatedAt, result.NotBefore);
    }

    [Fact]
    public async Task ExplicitBaselineIsPassedToProductionRegressionCapture()
    {
        const string definition = "validation-definition:production-regression";
        var baselineRequirement = new ValidationRequirement("tests", ValidationEvidenceKind.Test, true, ValidationCoverageScope.Targeted,
            ValidationBaselineRelation.Baseline, "fake", validationDefinitionId: definition);
        var baselineFixture = CreateFixture(baselineRequirement);
        var baseline = CreateEvidence(baselineFixture);
        var regressionRequirement = new ValidationRequirement("tests", ValidationEvidenceKind.Test, true, ValidationCoverageScope.Targeted,
            ValidationBaselineRelation.Regression, "fake", validationDefinitionId: definition,
            baselineBinding: new ValidationBaselineBinding(baselineFixture.Plan.Reference, baseline.Reference, definition));
        var regressionFixture = CreateFixture(regressionRequirement, baselineFixture.Plan.ProjectId);
        var plans = new FakePlanRepository(baselineFixture.Plan, regressionFixture.Plan);
        var evidenceRepository = new FakeEvidenceRepository([baseline]);
        var collector = new FakeCollector();
        var service = CreateEvidenceService(regressionFixture, plans, evidenceRepository, new FakeCollectorResolver(collector));

        var planWrite = await service.CreatePlanAsync(regressionFixture.Plan);
        var capture = await service.CaptureAsync(new ValidationCaptureRequest(regressionFixture.Plan.ProjectId, regressionFixture.Plan.Reference,
            regressionRequirement.RequirementId, regressionFixture.Plan.CurrentRecoveryCheckpointReference));

        Assert.True(planWrite.Succeeded);
        Assert.True(capture.Succeeded);
        Assert.Equal(1, collector.InvocationCount);
        Assert.Equal(baseline.Reference.EvidenceId, capture.Evidence!.BaselineEvidenceReference!.EvidenceId);
        Assert.Equal(baseline.Reference.ContentHash, capture.Evidence.BaselineEvidenceReference.ContentHash);
    }

    [Fact]
    public async Task MissingExplicitBaselineCannotSatisfyRegressionPlan()
    {
        const string definition = "validation-definition:missing-baseline";
        var baselineRequirement = new ValidationRequirement("tests", ValidationEvidenceKind.Test, true, ValidationCoverageScope.Targeted,
            ValidationBaselineRelation.Baseline, "fake", validationDefinitionId: definition);
        var baselineFixture = CreateFixture(baselineRequirement);
        var missing = new ValidationEvidenceReference(Guid.NewGuid(), 1, Hash('9'));
        var regressionRequirement = new ValidationRequirement("tests", ValidationEvidenceKind.Test, true, ValidationCoverageScope.Targeted,
            ValidationBaselineRelation.Regression, "fake", validationDefinitionId: definition,
            baselineBinding: new ValidationBaselineBinding(baselineFixture.Plan.Reference, missing, definition));
        var regressionFixture = CreateFixture(regressionRequirement, baselineFixture.Plan.ProjectId);
        var service = CreateEvidenceService(regressionFixture, new FakePlanRepository(baselineFixture.Plan, regressionFixture.Plan),
            new FakeEvidenceRepository([]), new FakeCollectorResolver());

        var result = await service.CreatePlanAsync(regressionFixture.Plan);

        Assert.False(result.Succeeded);
        Assert.Equal(ValidationPlanRepositoryWriteStatus.PlanConflict, result.Status);
    }

    [Fact]
    public async Task BaselineDefinitionMismatchCannotSatisfyRegressionPlan()
    {
        var baselineRequirement = new ValidationRequirement("tests", ValidationEvidenceKind.Test, true, ValidationCoverageScope.Targeted,
            ValidationBaselineRelation.Baseline, "fake", validationDefinitionId: "validation-definition:one");
        var baselineFixture = CreateFixture(baselineRequirement);
        var baseline = CreateEvidence(baselineFixture);
        var regressionRequirement = new ValidationRequirement("tests", ValidationEvidenceKind.Test, true, ValidationCoverageScope.Targeted,
            ValidationBaselineRelation.Regression, "fake", validationDefinitionId: "validation-definition:two",
            baselineBinding: new ValidationBaselineBinding(baselineFixture.Plan.Reference, baseline.Reference, "validation-definition:two"));
        var regressionFixture = CreateFixture(regressionRequirement, baselineFixture.Plan.ProjectId);
        var service = CreateEvidenceService(regressionFixture, new FakePlanRepository(baselineFixture.Plan, regressionFixture.Plan),
            new FakeEvidenceRepository([baseline]), new FakeCollectorResolver());

        var result = await service.CreatePlanAsync(regressionFixture.Plan);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ValidationPlanRevisionsCoexistAndResolveExactly()
    {
        using var store = new TemporaryStore();
        var fixture = CreateFixture();
        var revisionTwo = CreateRevision(fixture, 2);
        var repository = new JsonValidationPlanRepository(store.Paths, store.Files, NullLogger<JsonValidationPlanRepository>.Instance);

        Assert.Equal(ValidationPlanRepositoryWriteStatus.Created, (await repository.CreateAsync(fixture.Plan)).Status);
        Assert.Equal(ValidationPlanRepositoryWriteStatus.Created, (await repository.CreateAsync(revisionTwo)).Status);
        Assert.True((await repository.GetAsync(fixture.Plan.ProjectId, fixture.Plan.Reference)).IsValid);
        Assert.True((await repository.GetAsync(revisionTwo.ProjectId, revisionTwo.Reference)).IsValid);
    }

    [Fact]
    public async Task WrongPlanRevisionOrHashCannotResolveAnotherRevision()
    {
        using var store = new TemporaryStore();
        var fixture = CreateFixture();
        var repository = new JsonValidationPlanRepository(store.Paths, store.Files, NullLogger<JsonValidationPlanRepository>.Instance);
        await repository.CreateAsync(fixture.Plan);
        var wrongHash = new ValidationPlanReference(fixture.Plan.ProjectId, fixture.Plan.PlanId, fixture.Plan.Revision, fixture.Plan.SchemaVersion, Hash('9'));

        var result = await repository.GetAsync(fixture.Plan.ProjectId, wrongHash);

        Assert.Equal(ValidationPlanReadState.IntegrityFailure, result.State);
    }

    [Fact]
    public async Task CurrentPlanEvidenceIsCompleteDespiteMoreThan128UnrelatedRecords()
    {
        using var store = new TemporaryStore();
        var fixture = CreateFixture();
        var other = CreateRevision(fixture, 2);
        var repository = new JsonValidationEvidenceRepository(store.Paths, store.Files, NullLogger<JsonValidationEvidenceRepository>.Instance);
        await repository.CreateAsync(CreateEvidence(fixture));
        for (var index = 0; index < ValidationLimits.MaxEvidenceItems + 1; index++)
            await repository.CreateAsync(CreateEvidence(fixture, other, Guid.NewGuid()));

        var result = await repository.GetForPlanAsync(fixture.Plan.ProjectId, fixture.Plan.Reference);

        Assert.True(result.IsComplete);
        Assert.Single(result.Evidence!);
    }

    [Fact]
    public async Task ExactPlanEvidenceCapacityIsExplicitlyBounded()
    {
        using var store = new TemporaryStore();
        var fixture = CreateFixture();
        var repository = new JsonValidationEvidenceRepository(store.Paths, store.Files, NullLogger<JsonValidationEvidenceRepository>.Instance);
        for (var index = 0; index < ValidationLimits.MaxEvidenceItems; index++)
            await repository.CreateAsync(CreateEvidence(fixture, fixture.Plan, Guid.NewGuid()));

        var result = await repository.GetForPlanAsync(fixture.Plan.ProjectId, fixture.Plan.Reference);

        Assert.Equal(ValidationEvidenceSetReadState.Valid, result.State);
        Assert.Equal(ValidationLimits.MaxEvidenceItems, result.Evidence!.Count);
    }

    [Fact]
    public async Task ExactPlanEvidenceEnumeratorStopsAfterCapacityPlusOne()
    {
        using var store = new TemporaryStore();
        var fixture = CreateFixture();
        var yielded = 0;
        IEnumerable<string> Enumerate(string _)
        {
            while (yielded < ValidationLimits.MaxEvidenceItems + 20)
            {
                yielded++;
                yield return Path.Combine(fixture.Workspace, yielded.ToString());
            }
        }
        var repository = new JsonValidationEvidenceRepository(store.Paths, store.Files, NullLogger<JsonValidationEvidenceRepository>.Instance, Enumerate);

        var result = await repository.GetForPlanAsync(fixture.Plan.ProjectId, fixture.Plan.Reference);

        Assert.Equal(ValidationEvidenceSetReadState.CapacityExceeded, result.State);
        Assert.Equal(ValidationLimits.MaxEvidenceItems + 1, yielded);
    }

    [Fact]
    public async Task EvidenceSetCorruptionIsNotReturnedAsOrdinaryMissingEvidence()
    {
        using var store = new TemporaryStore();
        var fixture = CreateFixture();
        var evidence = CreateEvidence(fixture);
        var repository = new JsonValidationEvidenceRepository(store.Paths, store.Files, NullLogger<JsonValidationEvidenceRepository>.Instance);
        await repository.CreateAsync(evidence);
        var path = store.Paths.GetValidationEvidenceFile(fixture.Plan.ProjectId, fixture.Plan.PlanId, fixture.Plan.Revision, evidence.EvidenceId);
        File.WriteAllText(path, "{");

        var result = await repository.GetForPlanAsync(fixture.Plan.ProjectId, fixture.Plan.Reference);

        Assert.Equal(ValidationEvidenceSetReadState.Invalid, result.State);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task PlanHashTamperingIsRejectedOnExactRead()
    {
        using var store = new TemporaryStore();
        var fixture = CreateFixture();
        var repository = new JsonValidationPlanRepository(store.Paths, store.Files, NullLogger<JsonValidationPlanRepository>.Instance);
        await repository.CreateAsync(fixture.Plan);
        var path = store.Paths.GetValidationPlanFile(fixture.Plan.ProjectId, fixture.Plan.PlanId, fixture.Plan.Revision);
        File.WriteAllText(path, File.ReadAllText(path).Replace(fixture.Plan.ContentHash, Hash('9'), StringComparison.Ordinal));

        var result = await repository.GetAsync(fixture.Plan.ProjectId, fixture.Plan.Reference);

        Assert.Equal(ValidationPlanReadState.IntegrityFailure, result.State);
    }

    [Fact]
    public async Task EvidenceHashTamperingIsRejectedOnExactRead()
    {
        using var store = new TemporaryStore();
        var fixture = CreateFixture();
        var evidence = CreateEvidence(fixture);
        var repository = new JsonValidationEvidenceRepository(store.Paths, store.Files, NullLogger<JsonValidationEvidenceRepository>.Instance);
        await repository.CreateAsync(evidence);
        var path = store.Paths.GetValidationEvidenceFile(fixture.Plan.ProjectId, fixture.Plan.PlanId, fixture.Plan.Revision, evidence.EvidenceId);
        File.WriteAllText(path, File.ReadAllText(path).Replace(evidence.ContentHash, Hash('9'), StringComparison.Ordinal));

        var result = await repository.GetAsync(fixture.Plan.ProjectId, fixture.Plan.Reference, evidence.Reference);

        Assert.Equal(ValidationEvidenceReadState.IntegrityFailure, result.State);
    }

    [Fact]
    public async Task FreshEvidenceMapsToVerifiedRecoveryFreshness()
    {
        var fixture = CreateFixture();
        var result = await EvaluateGate(fixture, CreateEvidence(fixture));

        Assert.Equal(RecoveryEvidenceFreshness.Verified, result.Recovery!.Checkpoint!.EvidenceReferences.Single(value => value.Kind == RecoveryEvidenceKind.Validation).Freshness);
    }

    [Fact]
    public async Task StaleEvidenceMapsToStaleRecoveryFreshness()
    {
        var fixture = CreateFixture();
        var result = await EvaluateGate(fixture, CreateEvidence(fixture, capturedAt: fixture.Now, state: ValidationEvidenceState.Stale));

        Assert.Equal(RecoveryEvidenceFreshness.Stale, result.Recovery!.Checkpoint!.EvidenceReferences.Single(value => value.Kind == RecoveryEvidenceKind.Validation).Freshness);
    }

    [Fact]
    public async Task FutureEvidenceMapsToUnknownRecoveryFreshness()
    {
        var fixture = CreateFixture();
        var result = await EvaluateGate(fixture, CreateEvidence(fixture, capturedAt: fixture.Now.AddTicks(1)));

        Assert.Equal(RecoveryEvidenceFreshness.Unknown, result.Recovery!.Checkpoint!.EvidenceReferences.Single(value => value.Kind == RecoveryEvidenceKind.Validation).Freshness);
    }

    [Fact]
    public async Task SatisfiedGateAtRecoveryEvidenceCapacityPersistsDecisionAndCheckpoint()
    {
        var fixture = CreateFixture(inheritedExecutionEvidenceCount: RecoveryCheckpointLimits.MaxEvidenceReferences - 2);
        var decisions = new FakeDecisionRepository();
        var recovery = new FakeRecoveryService(fixture.Checkpoint);
        var service = CreateGateService(fixture, decisions, recovery);

        var result = await service.EvaluateAsync(new ValidationGateRequest(
            fixture.Plan.ProjectId, fixture.Plan.Reference, fixture.Plan.CurrentRecoveryCheckpointReference));

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(ValidationGateDecisionState.Satisfied, result.Decision!.State);
        Assert.Equal(1, decisions.CreateCalls);
        Assert.Equal(1, recovery.CreateCalls);
        Assert.Equal(RecoveryCheckpointLimits.MaxEvidenceReferences, recovery.LastRequest!.EvidenceReferences.Count);
    }

    [Fact]
    public async Task SatisfiedGateOverRecoveryEvidenceCapacityDoesNotPersistDecisionOrCheckpoint()
    {
        var fixture = CreateFixture(inheritedExecutionEvidenceCount: RecoveryCheckpointLimits.MaxEvidenceReferences - 1);
        var decisions = new FakeDecisionRepository();
        var recovery = new FakeRecoveryService(fixture.Checkpoint);
        var service = CreateGateService(fixture, decisions, recovery);

        var result = await service.EvaluateAsync(new ValidationGateRequest(
            fixture.Plan.ProjectId, fixture.Plan.Reference, fixture.Plan.CurrentRecoveryCheckpointReference));

        Assert.False(result.Succeeded);
        Assert.Null(result.Decision);
        Assert.Contains("capacity", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, decisions.CreateCalls);
        Assert.Equal(0, recovery.CreateCalls);
        Assert.Null(recovery.LastRequest);
    }

    [Fact]
    public async Task SecurityCaptureIsRejectedUntilAllRequiredNonSecurityEvidenceExists()
    {
        var fixture = WithRequirements(CreateFixture(),
            new ValidationRequirement("tests", ValidationEvidenceKind.Test, true, ValidationCoverageScope.Targeted, ValidationBaselineRelation.Standalone, "dotnet"),
            SecurityRequirement(),
            new ValidationRequirement("build", ValidationEvidenceKind.Build, false, ValidationCoverageScope.Targeted, ValidationBaselineRelation.Standalone, "dotnet"));
        var security = new SecurityValidationEvidenceCollector(new HandoffRedactionService(), new FixedClock(fixture.Now));
        var service = CreateEvidenceService(fixture, new FakePlanRepository(fixture.Plan), new FakeEvidenceRepository([]), new FakeCollectorResolver(new FakeCollector("dotnet"), security));
        Assert.True((await service.CreatePlanAsync(fixture.Plan)).Succeeded);

        var result = await service.CaptureAsync(new ValidationCaptureRequest(fixture.Plan.ProjectId, fixture.Plan.Reference,
            "security", fixture.Plan.CurrentRecoveryCheckpointReference));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SecurityCaptureBindsTheCompletePreSecurityEvidenceSnapshot()
    {
        var fixture = WithRequirements(CreateFixture(),
            new ValidationRequirement("tests", ValidationEvidenceKind.Test, true, ValidationCoverageScope.Targeted, ValidationBaselineRelation.Standalone, "dotnet"),
            SecurityRequirement(),
            new ValidationRequirement("build", ValidationEvidenceKind.Build, false, ValidationCoverageScope.Targeted, ValidationBaselineRelation.Standalone, "dotnet"));
        var evidence = new FakeEvidenceRepository([]);
        var security = new SecurityValidationEvidenceCollector(new HandoffRedactionService(), new FixedClock(fixture.Now));
        var service = CreateEvidenceService(fixture, new FakePlanRepository(fixture.Plan), evidence, new FakeCollectorResolver(new FakeCollector("dotnet"), security));
        Assert.True((await service.CreatePlanAsync(fixture.Plan)).Succeeded);
        Assert.True((await service.CaptureAsync(new ValidationCaptureRequest(fixture.Plan.ProjectId, fixture.Plan.Reference,
            "tests", fixture.Plan.CurrentRecoveryCheckpointReference))).Succeeded);
        Assert.True((await service.CaptureAsync(new ValidationCaptureRequest(fixture.Plan.ProjectId, fixture.Plan.Reference,
            "build", fixture.Plan.CurrentRecoveryCheckpointReference))).Succeeded);

        var result = await service.CaptureAsync(new ValidationCaptureRequest(fixture.Plan.ProjectId, fixture.Plan.Reference,
            "security", fixture.Plan.CurrentRecoveryCheckpointReference));

        Assert.True(result.Succeeded);
        Assert.Equal(ValidationEvidenceKind.Security, result.Evidence!.Kind);
        Assert.Equal(2, result.Evidence.ValidatedEvidenceReferences.Count);
        Assert.Equal(result.Evidence.ValidatedEvidenceSetHash,
            ValidationEvidenceSnapshot.FromReferences(result.Evidence.ValidatedEvidenceReferences).Hash);
    }

    [Fact]
    public async Task SecurityEvidenceSnapshotRoundTripsWithItsContentHash()
    {
        using var store = new TemporaryStore();
        var fixture = WithRequirements(CreateFixture(),
            new ValidationRequirement("tests", ValidationEvidenceKind.Test, true, ValidationCoverageScope.Targeted, ValidationBaselineRelation.Standalone, "dotnet"),
            SecurityRequirement());
        var testEvidence = CreateEvidence(fixture, requirement: fixture.Plan.Requirements.Single(value => value.RequirementId == "tests"));
        var security = new SecurityValidationEvidenceCollector(new HandoffRedactionService(), new FixedClock(fixture.Now));
        var securityEvidence = await security.CaptureAsync(new ValidationCollectionContext(fixture.Plan,
            fixture.Plan.Requirements.Single(value => value.RequirementId == "security"), fixture.Project, fixture.Authority,
            fixture.Receipt, fixture.Checkpoint, [testEvidence]));
        var repository = new JsonValidationEvidenceRepository(store.Paths, store.Files, NullLogger<JsonValidationEvidenceRepository>.Instance);
        await repository.CreateAsync(testEvidence);
        await repository.CreateAsync(securityEvidence);

        var result = await repository.GetAsync(fixture.Plan.ProjectId, fixture.Plan.Reference, securityEvidence.Reference);

        Assert.True(result.IsValid);
        Assert.Equal(securityEvidence.ValidatedEvidenceSetHash, result.Evidence!.ValidatedEvidenceSetHash);
        Assert.Equal(securityEvidence.ValidatedEvidenceReferences.Single().ContentHash,
            result.Evidence.ValidatedEvidenceReferences.Single().ContentHash);
        Assert.Equal(securityEvidence.ContentHash, result.Evidence.ContentHash);
    }

    [Fact]
    public async Task NonSecurityCaptureIsRejectedAfterSecurityBoundary()
    {
        var fixture = WithRequirements(CreateFixture(),
            new ValidationRequirement("tests", ValidationEvidenceKind.Test, true, ValidationCoverageScope.Targeted, ValidationBaselineRelation.Standalone, "dotnet"),
            SecurityRequirement(),
            new ValidationRequirement("build", ValidationEvidenceKind.Build, false, ValidationCoverageScope.Targeted, ValidationBaselineRelation.Standalone, "dotnet"));
        var evidence = new FakeEvidenceRepository([]);
        var security = new SecurityValidationEvidenceCollector(new HandoffRedactionService(), new FixedClock(fixture.Now));
        var service = CreateEvidenceService(fixture, new FakePlanRepository(fixture.Plan), evidence, new FakeCollectorResolver(new FakeCollector("dotnet"), security));
        Assert.True((await service.CreatePlanAsync(fixture.Plan)).Succeeded);
        Assert.True((await service.CaptureAsync(new ValidationCaptureRequest(fixture.Plan.ProjectId, fixture.Plan.Reference,
            "tests", fixture.Plan.CurrentRecoveryCheckpointReference))).Succeeded);
        Assert.True((await service.CaptureAsync(new ValidationCaptureRequest(fixture.Plan.ProjectId, fixture.Plan.Reference,
            "security", fixture.Plan.CurrentRecoveryCheckpointReference))).Succeeded);

        var result = await service.CaptureAsync(new ValidationCaptureRequest(fixture.Plan.ProjectId, fixture.Plan.Reference,
            "build", fixture.Plan.CurrentRecoveryCheckpointReference));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ChangedEvidenceAfterSecurityInvalidatesTheSnapshotAtTheGate()
    {
        var fixture = WithRequirements(CreateFixture(),
            new ValidationRequirement("tests", ValidationEvidenceKind.Test, true, ValidationCoverageScope.Targeted, ValidationBaselineRelation.Standalone, "dotnet"),
            SecurityRequirement(),
            new ValidationRequirement("build", ValidationEvidenceKind.Build, false, ValidationCoverageScope.Targeted, ValidationBaselineRelation.Standalone, "dotnet"));
        var evidence = new FakeEvidenceRepository([]);
        var security = new SecurityValidationEvidenceCollector(new HandoffRedactionService(), new FixedClock(fixture.Now));
        var service = CreateEvidenceService(fixture, new FakePlanRepository(fixture.Plan), evidence, new FakeCollectorResolver(new FakeCollector("dotnet"), security));
        Assert.True((await service.CreatePlanAsync(fixture.Plan)).Succeeded);
        Assert.True((await service.CaptureAsync(new ValidationCaptureRequest(fixture.Plan.ProjectId, fixture.Plan.Reference,
            "tests", fixture.Plan.CurrentRecoveryCheckpointReference))).Succeeded);
        Assert.True((await service.CaptureAsync(new ValidationCaptureRequest(fixture.Plan.ProjectId, fixture.Plan.Reference,
            "security", fixture.Plan.CurrentRecoveryCheckpointReference))).Succeeded);
        evidence.Add(CreateEvidence(fixture, requirement: fixture.Plan.Requirements.Single(value => value.RequirementId == "build")));

        var result = await EvaluateGate(fixture, evidence.Values.ToArray());

        Assert.Equal(ValidationGateDecisionState.Blocked, result.Decision!.State);
        Assert.Equal(ValidationReasonCodes.SecurityEvidenceSetMismatch,
            result.Decision.RequirementDecisions.Single(value => value.RequirementId == "security").ReasonCode);
    }

    [Fact]
    public void TamperedSecuritySnapshotReferenceCannotSatisfyGate()
    {
        var fixture = WithRequirements(CreateFixture(),
            new ValidationRequirement("tests", ValidationEvidenceKind.Test, true, ValidationCoverageScope.Targeted, ValidationBaselineRelation.Standalone, "fake"),
            SecurityRequirement());
        var testEvidence = CreateEvidence(fixture, requirement: fixture.Plan.Requirements.Single(value => value.RequirementId == "tests"));
        var reference = new ValidationEvidenceReference(Guid.NewGuid(), 1, Hash('9'));
        var snapshot = ValidationEvidenceSnapshot.FromReferences([reference]);
        var tampered = CreateEvidence(fixture, requirement: fixture.Plan.Requirements.Single(value => value.RequirementId == "security"),
            validatedEvidenceSetHash: snapshot.Hash, validatedEvidenceReferences: snapshot.References);

        var decision = ValidationGateEvaluator.Evaluate(fixture.Plan, [testEvidence, tampered], fixture.Now.AddMinutes(1));

        Assert.Equal(ValidationGateDecisionState.Blocked, decision.State);
        Assert.Equal(ValidationReasonCodes.SecurityEvidenceSetMismatch,
            decision.RequirementDecisions.Single(value => value.RequirementId == "security").ReasonCode);
    }

    [Fact]
    public async Task FreshFailedEvidenceRetainsVerifiedFreshnessButFailsGate()
    {
        var fixture = CreateFixture();
        var result = await EvaluateGate(fixture, CreateEvidence(fixture, outcome: ValidationOutcome.Failed));

        Assert.Equal(ValidationGateDecisionState.Failed, result.Decision!.State);
        Assert.Equal(RecoveryEvidenceFreshness.Verified, result.Recovery!.Checkpoint!.EvidenceReferences.Single(value => value.Kind == RecoveryEvidenceKind.Validation).Freshness);
    }

    [Theory]
    [InlineData(BoundedProcessOutcome.NonZeroExit, ValidationEvidenceState.Available, ValidationOutcome.Failed)]
    [InlineData(BoundedProcessOutcome.TimedOut, ValidationEvidenceState.TimedOut, ValidationOutcome.Unknown)]
    [InlineData(BoundedProcessOutcome.Cancelled, ValidationEvidenceState.Cancelled, ValidationOutcome.Unknown)]
    [InlineData(BoundedProcessOutcome.StartFailed, ValidationEvidenceState.Unavailable, ValidationOutcome.Unknown)]
    [InlineData(BoundedProcessOutcome.TerminationFailure, ValidationEvidenceState.Unavailable, ValidationOutcome.Unknown)]
    public async Task DotNetCollectorPreservesBoundedProcessOutcome(BoundedProcessOutcome processOutcome, ValidationEvidenceState expectedState, ValidationOutcome expectedOutcome)
    {
        var fixture = CreateFixture(new ValidationRequirement("tests", ValidationEvidenceKind.Test, true, ValidationCoverageScope.Targeted, ValidationBaselineRelation.Standalone, "fake", targetPath: "project.csproj"));
        CreateTarget(fixture, "project.csproj");
        var result = new BoundedProcessResult(processOutcome, processOutcome == BoundedProcessOutcome.NonZeroExit ? 1 : null, "", "", false, false, false, true, TimeSpan.Zero);
        var collector = new DotNetValidationEvidenceCollector(new FakeProcessHost(result), new FixedClock(fixture.Now), new HandoffRedactionService());

        var evidence = await collector.CaptureAsync(fixture.Context);

        Assert.Equal(expectedState, evidence.State);
        Assert.Equal(expectedOutcome, evidence.Outcome);
    }

    [Fact]
    public async Task DotNetCollectorReportsOutputTruncationWithoutPersistingOutput()
    {
        var fixture = CreateFixture(new ValidationRequirement("tests", ValidationEvidenceKind.Test, true, ValidationCoverageScope.Targeted, ValidationBaselineRelation.Standalone, "fake", targetPath: "project.csproj"));
        CreateTarget(fixture, "project.csproj");
        var result = new BoundedProcessResult(BoundedProcessOutcome.ExitedSuccessfully, 0, "safe", "", true, false, false, true, TimeSpan.Zero);
        var evidence = await new DotNetValidationEvidenceCollector(new FakeProcessHost(result), new FixedClock(fixture.Now), new HandoffRedactionService()).CaptureAsync(fixture.Context);

        Assert.True(evidence.OutputTruncated);
        Assert.Null(evidence.DiagnosticSummary);
    }

    private static ValidationEvidenceService CreateEvidenceService(Fixture fixture, FakePlanRepository plans, FakeEvidenceRepository evidence,
        FakeCollectorResolver resolver, FakeAuthorityRepository? authorities = null, FakeContinuationHeadRepository? heads = null) =>
        new(plans, evidence, new FakeProjectRepository(fixture.Project), authorities ?? new FakeAuthorityRepository(fixture.Authority),
            new FakeReceiptRepository(fixture.Receipt), new FakeCheckpointRepository(fixture.InputCheckpoint, fixture.PreRunCheckpoint, fixture.Checkpoint),
            heads ?? new FakeContinuationHeadRepository(fixture.Checkpoint), resolver, new FixedClock(fixture.Now));

    private static async Task<ValidationGateEvaluationResult> EvaluateGate(Fixture fixture, params ValidationEvidence[] evidence)
    {
        var recovery = new FakeRecoveryService(fixture.Checkpoint);
        var service = new ValidationGateService(new FakePlanRepository(fixture.Plan), new FakeEvidenceRepository(evidence), new FakeDecisionRepository(),
            new FakeAuthorityRepository(fixture.Authority), new FakeReceiptRepository(fixture.Receipt), new FakeCheckpointRepository(fixture.InputCheckpoint, fixture.PreRunCheckpoint, fixture.Checkpoint),
            new FakeContinuationHeadRepository(fixture.Checkpoint),
            recovery, new FixedClock(fixture.Now));
        return await service.EvaluateAsync(new ValidationGateRequest(fixture.Plan.ProjectId, fixture.Plan.Reference, fixture.Plan.CurrentRecoveryCheckpointReference));
    }

    private static ValidationGateService CreateGateService(Fixture fixture, FakeDecisionRepository decisions, FakeRecoveryService recovery) =>
        new(new FakePlanRepository(fixture.Plan), new FakeEvidenceRepository([CreateEvidence(fixture)]), decisions,
            new FakeAuthorityRepository(fixture.Authority), new FakeReceiptRepository(fixture.Receipt),
            new FakeCheckpointRepository(fixture.InputCheckpoint, fixture.PreRunCheckpoint, fixture.Checkpoint),
            new FakeContinuationHeadRepository(fixture.Checkpoint), recovery, new FixedClock(fixture.Now));

    private static ValidationRequirement RemoteRequirement(int? pullRequestNumber = null, string? expectedCommit = null) =>
        new("remote-ci", ValidationEvidenceKind.RemoteCi, true, ValidationCoverageScope.Targeted, ValidationBaselineRelation.Standalone,
            RemoteValidationEvidenceCollector.CollectorIdentifier, expectedRemoteCommitId: expectedCommit, requestedBranch: "main", pullRequestNumber: pullRequestNumber);

    private static RemoteRepositoryEvidence Remote(string branchCommit, string? pullRequestCommit, RemoteCiState result,
        IReadOnlyList<string?> commits, RemoteEvidenceState state = RemoteEvidenceState.Available)
    {
        var repository = new RemoteRepositoryIdentity(RemoteRepositoryProvider.GitHub, RemoteEvidenceSource.GitHubRest, "repo-1", "owner/repo", "owner", "repo", "main");
        var branch = new RemoteBranchEvidence("main", branchCommit, true);
        var pullRequest = pullRequestCommit is null ? null : new RemotePullRequestEvidence("42", "open", false, "feature", "main", pullRequestCommit, "base", RemoteMergeability.Available);
        var runs = commits.Select((commit, index) => new RemoteCiRunEvidence(RemoteStatusKind.CheckRun, $"run-{index}", "checks", result, headCommitId: commit)).ToArray();
        return new(Guid.NewGuid(), state, RemoteEvidenceSource.GitHubRest, new DateTimeOffset(2026, 8, 31, 10, 0, 0, TimeSpan.Zero), repository,
            RemoteEvidenceState.Available, branch, RemoteEvidenceState.Available, pullRequest,
            pullRequest is null ? RemoteEvidenceState.NotConfigured : RemoteEvidenceState.Available, ciRuns: runs,
            ciState: state, ciResult: result);
    }

    private static Fixture WithRequirements(Fixture fixture, params ValidationRequirement[] requirements)
    {
        var plan = new ValidationPlan(fixture.Plan.ProjectId, fixture.Plan.PlanId, fixture.Plan.Revision, fixture.Plan.CreatedAt,
            fixture.Authority.Reference, fixture.Plan.PlanningContractReference, fixture.Plan.WorkGraphReference, fixture.Plan.WorkGraphNodeId,
            fixture.Plan.WorkspaceId, fixture.Plan.WorkspacePath, fixture.Plan.WorkspaceReceiptContentHash, fixture.Checkpoint.Reference,
            requirements, fixture.Plan.HandoffPackageReference, evidenceNotBefore: fixture.Plan.EvidenceNotBefore);
        var context = new ValidationCollectionContext(plan, requirements[0], fixture.Project, fixture.Authority, fixture.Receipt, fixture.Checkpoint, []);
        return fixture with { Plan = plan, Requirement = requirements[0], Context = context };
    }

    private static ValidationPlan PlanFor(Fixture fixture, RecoveryCheckpoint checkpoint, DateTimeOffset? createdAt = null, DateTimeOffset? evidenceNotBefore = null) =>
        new(fixture.Plan.ProjectId, fixture.Plan.PlanId, fixture.Plan.Revision, createdAt ?? checkpoint.CreatedAt,
            fixture.Authority.Reference, fixture.Plan.PlanningContractReference, fixture.Plan.WorkGraphReference, fixture.Plan.WorkGraphNodeId,
            fixture.Plan.WorkspaceId, fixture.Plan.WorkspacePath, fixture.Plan.WorkspaceReceiptContentHash, checkpoint.Reference,
            fixture.Plan.Requirements, fixture.Plan.HandoffPackageReference, evidenceNotBefore: evidenceNotBefore ?? checkpoint.CreatedAt);

    private static RecoveryCheckpoint RebuildTerminal(Fixture fixture, IReadOnlyList<RecoveryEvidenceReference> evidence,
        RecoveryCheckpointReference? previous = null) =>
        new(fixture.Checkpoint.ProjectId, fixture.Checkpoint.CheckpointId, fixture.Checkpoint.SchemaVersion, fixture.Checkpoint.CreatedAt,
            fixture.Checkpoint.LifecycleState, fixture.Checkpoint.Context, fixture.Checkpoint.PlanningContractReference,
            fixture.Checkpoint.WorkGraphReference, fixture.Checkpoint.WorkGraphNodeId, fixture.Checkpoint.HandoffPackageReference,
            fixture.Checkpoint.RoutingDecisionReference, fixture.Checkpoint.WorkspacePreparationPlanReference,
            previous ?? fixture.Checkpoint.PreviousCheckpointReference, fixture.Checkpoint.SelectedAgentRoleReferences, evidence,
            fixture.Checkpoint.GateSnapshots, fixture.Checkpoint.Blockers, fixture.Checkpoint.NextSafeAction, fixture.Checkpoint.Explanation);

    private static async Task<ValidationAuthorityBindingResult> ValidateAsync(Fixture fixture, ValidationPlan? plan = null,
        ExecutionRunAuthority? authority = null, RecoveryCheckpoint? checkpoint = null, FakeContinuationHeadRepository? heads = null,
        RecoveryCheckpoint? inputCheckpoint = null, RecoveryCheckpoint? preRunCheckpoint = null)
    {
        var current = checkpoint ?? fixture.Checkpoint;
        var values = new[] { inputCheckpoint ?? fixture.InputCheckpoint, preRunCheckpoint ?? fixture.PreRunCheckpoint, current }
            .GroupBy(value => value.CheckpointId)
            .Select(group => group.Last())
            .ToArray();
        var repository = new FakeCheckpointRepository(values);
        return await ValidationAuthorityBindingValidator.ValidateAsync(plan ?? fixture.Plan, authority ?? fixture.Authority, fixture.Receipt,
            current, repository, heads ?? new FakeContinuationHeadRepository(current));
    }

    private static ValidationRequirement SecurityRequirement() =>
        new("security", ValidationEvidenceKind.Security, true, ValidationCoverageScope.Targeted,
            ValidationBaselineRelation.Standalone, SecurityValidationEvidenceCollector.CollectorIdentifier);

    private static Fixture CreateFixture(ValidationRequirement? requirement = null, Guid? projectId = null, DateTimeOffset? now = null,
        int inheritedExecutionEvidenceCount = 0)
    {
        var capturedAt = now ?? new DateTimeOffset(2026, 8, 31, 10, 0, 0, TimeSpan.Zero);
        var id = projectId ?? Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var contract = new PlanningExecutionContractReference(Guid.NewGuid(), 1, 1, Hash('a'));
        var graph = new WorkGraphReference(Guid.NewGuid(), 1, Hash('b'));
        var handoff = new HandoffPackageReference(Guid.NewGuid(), 1, Hash('c'));
        var routing = new RoutingDecisionReference(Guid.NewGuid(), 1, Hash('d'));
        var workspacePlan = new WorkspacePreparationPlanReference(Guid.NewGuid(), 1, Hash('e'), id);
        var workspace = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "apo-remediation-workspace"));
        var inheritedEvidence = Enumerable.Range(0, inheritedExecutionEvidenceCount)
            .Select(index =>
            {
                var runId = Guid.NewGuid();
                var contentHash = Hash("0123456789abcdef"[index % 16]);
                return new RecoveryEvidenceReference(runId, RecoveryEvidenceKind.Other,
                    $"execution-run:{id:D}/{runId:D}/{contentHash}", capturedAt.AddMinutes(-3),
                    RecoveryEvidenceFreshness.PointInTime, contentHash: contentHash);
            })
            .ToArray();
        var inputCheckpoint = new RecoveryCheckpoint(id, Guid.NewGuid(), RecoveryCheckpointSchema.CurrentVersion, capturedAt.AddMinutes(-2), RecoveryCheckpointLifecycleState.Ready,
            new RecoveryContextReference(Guid.NewGuid(), 1, capturedAt), contract, graph, Guid.NewGuid(), handoff,
            evidenceReferences: inheritedEvidence, nextSafeAction: RecoveryNextSafeAction.ContinueFromCheckpoint);
        var receipt = new WorkspacePreparationReceipt(id, workspaceId, Guid.NewGuid(), capturedAt, workspacePlan, workspace, "main", Sha40('f'), Sha40('f'), "local-repository", "test");
        var authority = new ExecutionRunAuthority(id, Guid.NewGuid(), capturedAt.AddMinutes(-1), contract, graph, inputCheckpoint.WorkGraphNodeId!.Value, handoff, routing, workspacePlan,
            workspaceId, workspace, receipt.ContentHash, inputCheckpoint.Reference, Guid.NewGuid(), "provider", "model", AgentConnectionMode.Cli, "adapter", new ExecutionBudgetEnvelope(1, 1));
        var executionEvidence = new RecoveryEvidenceReference(authority.RunId, RecoveryEvidenceKind.Other,
            $"execution-run:{authority.ProjectId:D}/{authority.RunId:D}/{authority.ContentHash}", authority.CreatedAt,
            RecoveryEvidenceFreshness.PointInTime, contentHash: authority.ContentHash);
        var preRunCheckpoint = new RecoveryCheckpoint(id, Guid.NewGuid(), RecoveryCheckpointSchema.CurrentVersion, capturedAt, RecoveryCheckpointLifecycleState.Waiting,
            inputCheckpoint.Context, contract, graph, inputCheckpoint.WorkGraphNodeId, handoff, previousCheckpointReference: inputCheckpoint.Reference,
            evidenceReferences: inheritedEvidence.Append(executionEvidence).ToArray(), nextSafeAction: RecoveryNextSafeAction.ResolveBlocker);
        var checkpoint = new RecoveryCheckpoint(id, Guid.NewGuid(), RecoveryCheckpointSchema.CurrentVersion, capturedAt, RecoveryCheckpointLifecycleState.Ready,
            inputCheckpoint.Context, contract, graph, inputCheckpoint.WorkGraphNodeId, handoff, previousCheckpointReference: preRunCheckpoint.Reference,
            evidenceReferences: inheritedEvidence.Append(executionEvidence).ToArray(), nextSafeAction: RecoveryNextSafeAction.RunValidation);
        var value = requirement ?? new ValidationRequirement("tests", ValidationEvidenceKind.Test, true, ValidationCoverageScope.Targeted, ValidationBaselineRelation.Standalone, "fake");
        var plan = new ValidationPlan(id, Guid.NewGuid(), 1, capturedAt, authority.Reference, contract, graph, checkpoint.WorkGraphNodeId!.Value,
            workspaceId, workspace, receipt.ContentHash, checkpoint.Reference, [value], handoff);
        var project = new Project(id, "Validation", workspace, null, ProjectStatus.Active, capturedAt, capturedAt);
        var context = new ValidationCollectionContext(plan, value, project, authority, receipt, checkpoint, []);
        return new(capturedAt, plan, value, project, authority, receipt, inputCheckpoint, preRunCheckpoint, checkpoint, context, workspace);
    }

    private static ValidationPlan CreateRevision(Fixture fixture, int revision) =>
        new(fixture.Plan.ProjectId, fixture.Plan.PlanId, revision, fixture.Plan.CreatedAt, fixture.Authority.Reference,
            fixture.Plan.PlanningContractReference, fixture.Plan.WorkGraphReference, fixture.Plan.WorkGraphNodeId, fixture.Plan.WorkspaceId,
            fixture.Plan.WorkspacePath, fixture.Plan.WorkspaceReceiptContentHash, fixture.Plan.CurrentRecoveryCheckpointReference,
            fixture.Plan.Requirements, fixture.Plan.HandoffPackageReference, evidenceNotBefore: fixture.Plan.EvidenceNotBefore);

    private static RecoveryCheckpoint RebuildCheckpoint(RecoveryCheckpoint source,
        IReadOnlyList<RecoveryEvidenceReference> evidence, RecoveryCheckpointReference? previous = null) =>
        new(source.ProjectId, source.CheckpointId, source.SchemaVersion, source.CreatedAt, source.LifecycleState, source.Context,
            source.PlanningContractReference, source.WorkGraphReference, source.WorkGraphNodeId, source.HandoffPackageReference,
            source.RoutingDecisionReference, source.WorkspacePreparationPlanReference,
            previous ?? source.PreviousCheckpointReference, source.SelectedAgentRoleReferences, evidence, source.GateSnapshots,
            source.Blockers, source.NextSafeAction, source.Explanation);

    private static RecoveryEvidenceReference CreateExecutionEvidence(ExecutionRunAuthority authority) =>
        CreateExecutionEvidence(authority.ProjectId, authority.RunId, authority.ContentHash, authority.CreatedAt);

    private static RecoveryEvidenceReference CreateExecutionEvidence(Guid projectId, Guid runId, string contentHash, DateTimeOffset observedAt) =>
        new(runId, RecoveryEvidenceKind.Other, $"execution-run:{projectId:D}/{runId:D}/{contentHash}", observedAt,
            RecoveryEvidenceFreshness.PointInTime, contentHash: contentHash);

    private static ExecutionRunAuthority CreateAuthority(Fixture fixture, Guid? runId = null, PlanningExecutionContractReference? contract = null) =>
        new(fixture.Plan.ProjectId, runId ?? fixture.Authority.RunId, fixture.Now, contract ?? fixture.Plan.PlanningContractReference,
            fixture.Plan.WorkGraphReference, fixture.Plan.WorkGraphNodeId, fixture.Authority.HandoffPackageReference, fixture.Authority.RoutingDecisionReference,
            new WorkspacePreparationPlanReference(Guid.NewGuid(), 1, Hash('e'), fixture.Plan.ProjectId), fixture.Plan.WorkspaceId, fixture.Plan.WorkspacePath,
            fixture.Plan.WorkspaceReceiptContentHash, fixture.Authority.InputRecoveryCheckpointReference, fixture.Authority.AgentId, fixture.Authority.Provider,
            fixture.Authority.ModelIdentifier, fixture.Authority.ConnectionMode, fixture.Authority.AdapterIdentifier, fixture.Authority.Budgets);

    private static ValidationEvidence CreateEvidence(Fixture fixture, ValidationPlan? plan = null, Guid? evidenceId = null,
        ValidationEvidenceState state = ValidationEvidenceState.Available, ValidationOutcome? outcome = null,
        DateTimeOffset? capturedAt = null, ValidationBaselineRelation? baselineRelation = null, ValidationRequirement? requirement = null,
        string? validatedEvidenceSetHash = null, IReadOnlyList<ValidationEvidenceReference>? validatedEvidenceReferences = null) =>
        new((plan ?? fixture.Plan).ProjectId, evidenceId ?? Guid.NewGuid(), (plan ?? fixture.Plan).Reference, (requirement ?? fixture.Requirement).RequirementId,
            fixture.Authority.RunId, fixture.Authority.Reference, fixture.Plan.PlanningContractReference, fixture.Plan.WorkGraphReference, fixture.Plan.WorkGraphNodeId,
            fixture.Plan.CurrentRecoveryCheckpointReference, fixture.Plan.WorkspaceId, fixture.Plan.WorkspacePath, fixture.Plan.WorkspaceReceiptContentHash,
            (requirement ?? fixture.Requirement).CollectorIdentifier, (requirement ?? fixture.Requirement).EvidenceKind, state, outcome ?? (state == ValidationEvidenceState.Available ? ValidationOutcome.Passed : ValidationOutcome.Unknown),
            (requirement ?? fixture.Requirement).Coverage, baselineRelation ?? (requirement ?? fixture.Requirement).BaselineRelation, capturedAt ?? fixture.Now,
            validationDefinitionId: (requirement ?? fixture.Requirement).ValidationDefinitionId,
            validatedEvidenceSetHash: validatedEvidenceSetHash, validatedEvidenceReferences: validatedEvidenceReferences);

    private static string Hash(char value) => new(value, 64);
    private static string Sha40(char value) => new(value, 40);

    private static void CreateTarget(Fixture fixture, string target)
    {
        var path = Path.GetFullPath(Path.Combine(fixture.Workspace, target));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "<Project />");
    }

    private sealed record Fixture(DateTimeOffset Now, ValidationPlan Plan, ValidationRequirement Requirement, Project Project,
        ExecutionRunAuthority Authority, WorkspacePreparationReceipt Receipt, RecoveryCheckpoint InputCheckpoint, RecoveryCheckpoint PreRunCheckpoint, RecoveryCheckpoint Checkpoint,
        ValidationCollectionContext Context, string Workspace);

    private sealed class FixedClock(DateTimeOffset value) : IClock { public DateTimeOffset UtcNow => value; }

    private sealed class FakeRemoteService(RemoteRepositoryEvidence value) : IRemoteRepositoryEvidenceService
    {
        public Task<RemoteRepositoryEvidence> InspectAsync(Project project, string? requestedBranch = null, int? pullRequestNumber = null, CancellationToken cancellationToken = default) => Task.FromResult(new RemoteRepositoryEvidence(project.Id, value.State, value.Source, value.CapturedAt, value.Repository, value.RepositoryState, value.Branch, value.BranchState, value.PullRequest, value.PullRequestState, value.Reviews, value.ReviewState, value.Statuses, value.StatusState, value.Checks, value.CheckState, value.CiRuns, value.CiState, value.CiResult, value.Limitations, value.SafeErrorMessage));
    }

    private sealed class FakeProcessHost(BoundedProcessResult result) : IBoundedProcessHost
    {
        public Task<BoundedProcessResult> RunAsync(BoundedProcessRequest request, CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class FakeCollector : IValidationEvidenceCollector
    {
        private readonly string _identifier;
        public FakeCollector(string identifier = "fake") => _identifier = identifier;
        public int InvocationCount { get; private set; }
        public ValidationEvidenceCollectorDescriptor Descriptor => new(_identifier, [ValidationEvidenceKind.Test, ValidationEvidenceKind.Build], false, true);
        public Task<ValidationEvidence> CaptureAsync(ValidationCollectionContext context, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return Task.FromResult(new ValidationEvidence(context.Plan.ProjectId, Guid.NewGuid(), context.Plan.Reference, context.Requirement.RequirementId,
                context.Authority.RunId, context.Authority.Reference, context.Plan.PlanningContractReference, context.Plan.WorkGraphReference, context.Plan.WorkGraphNodeId,
                context.CurrentCheckpoint.Reference, context.Plan.WorkspaceId, context.Plan.WorkspacePath, context.Plan.WorkspaceReceiptContentHash,
                context.Requirement.CollectorIdentifier, context.Requirement.EvidenceKind, ValidationEvidenceState.Available, ValidationOutcome.Passed,
                context.Requirement.Coverage, context.Requirement.BaselineRelation, context.Plan.CreatedAt, baselineEvidenceReference: context.BaselineEvidenceReference,
                validationDefinitionId: context.Requirement.ValidationDefinitionId));
        }
    }

    private sealed class FakeCollectorResolver : IValidationEvidenceCollectorResolver
    {
        private readonly IReadOnlyList<IValidationEvidenceCollector> _collectors;
        public FakeCollectorResolver(FakeCollector? collector = null, params IValidationEvidenceCollector[] additional) =>
            _collectors = new IValidationEvidenceCollector[] { collector ?? new() }.Concat(additional).ToArray();
        public ValidationCollectorResolution Resolve(string collectorIdentifier, ValidationEvidenceKind kind)
        {
            var collector = _collectors.FirstOrDefault(value => value.Descriptor.Identifier == collectorIdentifier && value.Descriptor.SupportedKinds.Contains(kind));
            return collector is null
                ? new(ValidationCollectorResolutionStatus.Unsupported)
                : new(ValidationCollectorResolutionStatus.Resolved, collector);
        }
    }

    private sealed class FakePlanRepository(params ValidationPlan[] values) : IValidationPlanRepository
    {
        private readonly Dictionary<ValidationPlanReference, ValidationPlan> _values = values.ToDictionary(value => value.Reference);
        public int CreateCalls { get; private set; }
        public Task<ValidationPlanRepositoryWriteResult> CreateAsync(ValidationPlan value, CancellationToken cancellationToken = default) { CreateCalls++; _values[value.Reference] = value; return Task.FromResult(new ValidationPlanRepositoryWriteResult(ValidationPlanRepositoryWriteStatus.Created)); }
        public Task<ValidationPlanReadResult> GetAsync(Guid projectId, ValidationPlanReference reference, CancellationToken cancellationToken = default) => Task.FromResult(_values.TryGetValue(reference, out var value) ? new ValidationPlanReadResult(ValidationPlanReadState.Valid, value) : new ValidationPlanReadResult(ValidationPlanReadState.Missing));
    }

    private sealed class FakeEvidenceRepository(IReadOnlyList<ValidationEvidence> values) : IValidationEvidenceRepository
    {
        private readonly List<ValidationEvidence> _values = values.ToList();
        public IReadOnlyList<ValidationEvidence> Values => _values;
        public void Add(ValidationEvidence value) => _values.Add(value);
        public Task<ValidationEvidenceRepositoryWriteResult> CreateAsync(ValidationEvidence evidence, CancellationToken cancellationToken = default) { _values.Add(evidence); return Task.FromResult(new ValidationEvidenceRepositoryWriteResult(ValidationEvidenceRepositoryWriteStatus.Created)); }
        public Task<ValidationEvidenceReadResult> GetAsync(Guid projectId, ValidationPlanReference planReference, ValidationEvidenceReference evidenceReference, CancellationToken cancellationToken = default) => Task.FromResult(_values.FirstOrDefault(value => value.ProjectId == projectId && value.PlanReference.PlanId == planReference.PlanId && value.PlanReference.Revision == planReference.Revision && value.EvidenceId == evidenceReference.EvidenceId && value.ContentHash == evidenceReference.ContentHash) is { } value ? new ValidationEvidenceReadResult(ValidationEvidenceReadState.Valid, value) : new ValidationEvidenceReadResult(ValidationEvidenceReadState.Missing));
        public Task<ValidationEvidenceSetReadResult> GetForPlanAsync(Guid projectId, ValidationPlanReference planReference, CancellationToken cancellationToken = default) => Task.FromResult(new ValidationEvidenceSetReadResult(ValidationEvidenceSetReadState.Valid, _values.Where(value => value.ProjectId == projectId && value.PlanReference.PlanId == planReference.PlanId && value.PlanReference.Revision == planReference.Revision && value.PlanReference.ContentHash == planReference.ContentHash).ToArray()));
    }

    private sealed class FakeProjectRepository(Project project) : IProjectRepository
    {
        public Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Project>>([project]);
        public Task<Project?> GetByIdAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<Project?>(projectId == project.Id ? project : null);
        public Task UpsertAsync(Project value, CancellationToken cancellationToken = default) => Task.CompletedTask;
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

    private sealed class FakeCheckpointRepository(params RecoveryCheckpoint[] checkpoints) : IRecoveryCheckpointRepository
    {
        private readonly Dictionary<Guid, RecoveryCheckpoint> _values = checkpoints.ToDictionary(value => value.CheckpointId);
        public Task<RecoveryCheckpointRepositoryWriteResult> CreateAsync(RecoveryCheckpoint value, CancellationToken cancellationToken = default) => Task.FromResult(new RecoveryCheckpointRepositoryWriteResult(RecoveryCheckpointRepositoryWriteStatus.Created));
        public Task<RecoveryCheckpointReadResult> GetAsync(Guid projectId, Guid checkpointId, CancellationToken cancellationToken = default) => Task.FromResult(_values.TryGetValue(checkpointId, out var checkpoint) && checkpoint.ProjectId == projectId ? new RecoveryCheckpointReadResult(RecoveryCheckpointReadState.Valid, checkpoint) : new RecoveryCheckpointReadResult(RecoveryCheckpointReadState.Missing));
    }

    private sealed class FakeContinuationHeadRepository(RecoveryCheckpoint checkpoint) : IContinuationHeadRepository
    {
        public RecoveryCheckpointReference Latest { get; set; } = checkpoint.Reference;
        public Task<ContinuationHeadReadResult> GetAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ContinuationHeadReadResult(ContinuationHeadReadState.Valid,
                new ContinuationHead(projectId, ContinuationHeadSchema.CurrentVersion, 1, Latest, null, checkpoint.CreatedAt)));
        public Task<ContinuationHeadRepositoryWriteResult> PublishAsync(ContinuationHead head, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ContinuationHeadRepositoryWriteResult(ContinuationHeadRepositoryWriteStatus.Published));
    }

    private sealed class FakeDecisionRepository : IValidationGateDecisionRepository
    {
        public int CreateCalls { get; private set; }
        public Task<ValidationDecisionRepositoryWriteResult> CreateAsync(ValidationGateDecision decision, CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            return Task.FromResult(new ValidationDecisionRepositoryWriteResult(ValidationDecisionRepositoryWriteStatus.Created));
        }

        public Task<ValidationDecisionReadResult> GetAsync(Guid projectId, Guid decisionId, CancellationToken cancellationToken = default) => Task.FromResult(new ValidationDecisionReadResult(ValidationDecisionReadState.Missing));
    }

    private sealed class FakeRecoveryService(RecoveryCheckpoint predecessor) : IRecoveryCheckpointService
    {
        public int CreateCalls { get; private set; }
        public RecoveryCheckpointCreationRequest? LastRequest { get; private set; }

        public Task<RecoveryCheckpointCreationResult> CreateAsync(RecoveryCheckpointCreationRequest request, CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            LastRequest = request;
            var checkpoint = new RecoveryCheckpoint(request.ProjectId, request.CheckpointId, RecoveryCheckpointSchema.CurrentVersion, request.CreatedAt ?? predecessor.CreatedAt, request.LifecycleState,
                predecessor.Context, request.PlanningContractReference, request.WorkGraphReference, request.WorkGraphNodeId, request.HandoffPackageReference,
                request.RoutingDecisionReference, request.WorkspacePreparationPlanReference, request.PreviousCheckpointReference, request.SelectedAgentRoleReferences,
                request.EvidenceReferences, request.GateSnapshots, request.Blockers, request.NextSafeAction, request.Explanation);
            var head = new ContinuationHead(request.ProjectId, 1, 2, checkpoint.Reference, predecessor.Reference, checkpoint.CreatedAt);
            return Task.FromResult(new RecoveryCheckpointCreationResult(RecoveryCheckpointCreationStatus.Created, checkpoint, head));
        }
    }
}
