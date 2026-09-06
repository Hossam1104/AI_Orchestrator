using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Application.Validation;
using AIUsageMonitor.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageMonitor.Infrastructure.Tests;

public sealed class ReviewWorkflowTests
{
    [Fact]
    public async Task HappyPath_RemainsTraceableThroughRereviewAndReadyForAcceptanceAuthority()
    {
        using var harness = CreateHarness();
        var root = Guid.NewGuid();
        await harness.Reviews.AppendReviewAsync(Review(root, harness.ProjectId, harness.Clock.UtcNow, blockingFinding: true));

        var initial = await harness.Service.ReadCaseAsync(harness.ProjectId, root);
        Assert.Equal(ReviewWorkflowState.AwaitingAdjudication, initial.InboxItem!.WorkflowState);
        Assert.Equal(ReviewWorkflowNextAction.AdjudicateFindings, initial.InboxItem.NextRequiredAction);

        Assert.True((await harness.Service.AdjudicateFindingAsync(new(
            harness.ProjectId, root, root, "F1", ReviewFindingAdjudication.Accepted,
            "planner:sol", ReviewAuthorityKind.Planner, "Accepted for bounded remediation.") )).Succeeded);
        Assert.True((await harness.Service.AdjudicateFindingAsync(new(
            harness.ProjectId, root, root, "F2", ReviewFindingAdjudication.Rejected,
            "planner:sol", ReviewAuthorityKind.Planner, "Finding is not actionable.") )).Succeeded);

        var remediationRequired = await harness.Service.ReadCaseAsync(harness.ProjectId, root);
        Assert.Equal(ReviewWorkflowState.RemediationRequired, remediationRequired.InboxItem!.WorkflowState);
        var started = await harness.Service.StartRemediationAsync(new(harness.ProjectId, root, root));
        Assert.True(started.Succeeded);
        Assert.Equal(1, started.Event!.AttemptNumber);

        var completed = await harness.Service.CompleteRemediationAsync(new(
            harness.ProjectId, root, root, 1, EvidenceReferences: ["validation-evidence:attempt-1"]));
        Assert.True(completed.Succeeded, completed.ErrorMessage);

        harness.Validation.Decision = CreateDecision(harness.ProjectId, harness.Clock.UtcNow, ValidationGateDecisionState.Satisfied);
        var revalidated = await harness.Service.RecordRevalidationAsync(new(
            harness.ProjectId, root, root, 1, harness.Validation.Decision.Reference));
        Assert.True(revalidated.Succeeded, revalidated.ErrorMessage);
        Assert.Equal(ValidationGateDecisionState.Satisfied, revalidated.Event!.ValidationState);

        var rereviewId = Guid.NewGuid();
        harness.Clock.UtcNowValue = harness.Clock.UtcNow.AddMinutes(1);
        await harness.Reviews.AppendReviewAsync(Review(rereviewId, harness.ProjectId, harness.Clock.UtcNow, blockingFinding: false));
        harness.Clock.UtcNowValue = harness.Clock.UtcNow.AddMinutes(1);
        Assert.True((await harness.Service.LinkRereviewAsync(new(harness.ProjectId, root, root, rereviewId))).Succeeded);

        var final = await harness.Service.ReadCaseAsync(harness.ProjectId, root);
        Assert.True(final.IsUsable, final.ErrorMessage);
        Assert.Equal(ReviewWorkflowState.ReadyForAcceptanceAuthority, final.InboxItem!.WorkflowState);
        Assert.Equal(ReviewWorkflowNextAction.SendToAcceptanceAuthority, final.InboxItem.NextRequiredAction);
        Assert.Equal(rereviewId, final.InboxItem.CurrentReviewId);
        Assert.Equal(2, final.Reviews.Count);
        Assert.Equal(6, final.Events.Count);
        Assert.Contains(final.Events, value => value.Kind == ReviewWorkflowEventKind.FindingAdjudicated && value.FindingId == "F1");
        Assert.Contains(final.Events, value => value.Kind == ReviewWorkflowEventKind.RevalidationRecorded);
        Assert.Equal(1, final.InboxItem.RemediationAttemptCount);
        Assert.False(final.InboxItem.OwnerAttentionRequired);

        var historical = await harness.Reviews.ReadAllReviewsAsync(harness.ProjectId);
        var original = Assert.Single(historical.Records, value => value.ReviewId == root);
        Assert.Equal("legacy", original.Findings.Single(value => value.FindingId == "F1").Disposition);
        Assert.True(original.Findings.Single(value => value.FindingId == "F1").Blocking);
    }

    [Fact]
    public async Task Adjudication_RejectsUnknownDuplicateAndReviewerAuthority()
    {
        using var harness = CreateHarness();
        var root = Guid.NewGuid();
        await harness.Reviews.AppendReviewAsync(Review(root, harness.ProjectId, harness.Clock.UtcNow, blockingFinding: true));

        var unknown = await harness.Service.AdjudicateFindingAsync(new(
            harness.ProjectId, root, root, "missing", ReviewFindingAdjudication.Accepted,
            "planner", ReviewAuthorityKind.Planner, "reason"));
        Assert.Equal(ReviewWorkflowMutationStatus.NotFound, unknown.Status);

        var reviewer = await harness.Service.AdjudicateFindingAsync(new(
            harness.ProjectId, root, root, "F1", ReviewFindingAdjudication.Accepted,
            "reviewer:opus", ReviewAuthorityKind.Reviewer, "reason"));
        Assert.Equal(ReviewWorkflowMutationStatus.InvalidRequest, reviewer.Status);

        var accepted = await harness.Service.AdjudicateFindingAsync(new(
            harness.ProjectId, root, root, "F1", ReviewFindingAdjudication.Accepted,
            "planner:sol", ReviewAuthorityKind.Planner, "reason"));
        Assert.True(accepted.Succeeded);
        var duplicate = await harness.Service.AdjudicateFindingAsync(new(
            harness.ProjectId, root, root, "F1", ReviewFindingAdjudication.Rejected,
            "planner:sol", ReviewAuthorityKind.Planner, "second reason"));
        Assert.Equal(ReviewWorkflowMutationStatus.Conflict, duplicate.Status);
    }

    [Fact]
    public async Task BlockingDispositionRules_AreFailClosedAndNonBlockingFindingsDoNotBlock()
    {
        using var accepted = CreateHarness();
        var acceptedRoot = Guid.NewGuid();
        await accepted.Reviews.AppendReviewAsync(Review(acceptedRoot, accepted.ProjectId, accepted.Clock.UtcNow, blockingFinding: true));
        Assert.True((await accepted.Service.AdjudicateFindingAsync(new(accepted.ProjectId, acceptedRoot, acceptedRoot, "F1", ReviewFindingAdjudication.Accepted, "planner", ReviewAuthorityKind.Planner, "fix"))).Succeeded);
        var remediation = await accepted.Service.ReadCaseAsync(accepted.ProjectId, acceptedRoot);
        Assert.Equal(ReviewWorkflowState.RemediationRequired, remediation.InboxItem!.WorkflowState);
        Assert.Equal(ReviewWorkflowNextAction.RunRemediation, remediation.InboxItem.NextRequiredAction);

        using var rejected = CreateHarness();
        var rejectedRoot = Guid.NewGuid();
        await rejected.Reviews.AppendReviewAsync(Review(rejectedRoot, rejected.ProjectId, rejected.Clock.UtcNow, blockingFinding: true));
        Assert.True((await rejected.Service.AdjudicateFindingAsync(new(rejected.ProjectId, rejectedRoot, rejectedRoot, "F1", ReviewFindingAdjudication.Rejected, "planner", ReviewAuthorityKind.Planner, "waived by authority"))).Succeeded);
        var ready = await rejected.Service.ReadCaseAsync(rejected.ProjectId, rejectedRoot);
        Assert.Equal(ReviewWorkflowState.ReadyForAcceptanceAuthority, ready.InboxItem!.WorkflowState);

        using var deferred = CreateHarness();
        var deferredRoot = Guid.NewGuid();
        await deferred.Reviews.AppendReviewAsync(Review(deferredRoot, deferred.ProjectId, deferred.Clock.UtcNow, blockingFinding: true));
        Assert.True((await deferred.Service.AdjudicateFindingAsync(new(deferred.ProjectId, deferredRoot, deferredRoot, "F1", ReviewFindingAdjudication.Deferred, "planner", ReviewAuthorityKind.Planner, "owner decision needed"))).Succeeded);
        var human = await deferred.Service.ReadCaseAsync(deferred.ProjectId, deferredRoot);
        Assert.Equal(ReviewWorkflowState.HumanDecisionRequired, human.InboxItem!.WorkflowState);
        Assert.True(human.InboxItem.OwnerAttentionRequired);
        Assert.Equal(ReviewWorkflowNextAction.HumanDecision, human.InboxItem.NextRequiredAction);

        using var nonBlocking = CreateHarness();
        var nonBlockingRoot = Guid.NewGuid();
        await nonBlocking.Reviews.AppendReviewAsync(Review(nonBlockingRoot, nonBlocking.ProjectId, nonBlocking.Clock.UtcNow, blockingFinding: false));
        var nonBlockingCase = await nonBlocking.Service.ReadCaseAsync(nonBlocking.ProjectId, nonBlockingRoot);
        Assert.Equal(ReviewWorkflowState.ReadyForAcceptanceAuthority, nonBlockingCase.InboxItem!.WorkflowState);
        Assert.Equal(ReviewWorkflowMutationStatus.InvalidRequest,
            (await nonBlocking.Service.StartRemediationAsync(new(nonBlocking.ProjectId, nonBlockingRoot, nonBlockingRoot))).Status);
    }

    [Fact]
    public async Task Remediation_IsBoundedAndFailedFinalRevalidationEscalates()
    {
        using var harness = CreateHarness();
        var root = await CreateAcceptedCaseAsync(harness);

        Assert.True((await harness.Service.StartRemediationAsync(new(harness.ProjectId, root, root))).Succeeded);
        Assert.Equal(ReviewWorkflowMutationStatus.Conflict,
            (await harness.Service.StartRemediationAsync(new(harness.ProjectId, root, root))).Status);
        var completed = await harness.Service.CompleteRemediationAsync(new(harness.ProjectId, root, root, 1, EvidenceReferences: ["attempt-1"]));
        Assert.True(completed.Succeeded, completed.ErrorMessage);
        harness.Validation.Decision = CreateDecision(harness.ProjectId, harness.Clock.UtcNow, ValidationGateDecisionState.Failed);
        var firstRevalidation = await harness.Service.RecordRevalidationAsync(new(harness.ProjectId, root, root, 1, harness.Validation.Decision.Reference));
        Assert.True(firstRevalidation.Succeeded, firstRevalidation.ErrorMessage);

        Assert.True((await harness.Service.StartRemediationAsync(new(harness.ProjectId, root, root))).Succeeded);
        Assert.True((await harness.Service.CompleteRemediationAsync(new(harness.ProjectId, root, root, 2, EvidenceReferences: ["attempt-2"]))).Succeeded);
        harness.Validation.Decision = CreateDecision(harness.ProjectId, harness.Clock.UtcNow, ValidationGateDecisionState.Failed);
        Assert.True((await harness.Service.RecordRevalidationAsync(new(harness.ProjectId, root, root, 2, harness.Validation.Decision.Reference))).Succeeded);

        var exhausted = await harness.Service.ReadCaseAsync(harness.ProjectId, root);
        Assert.Equal(ReviewWorkflowState.HumanDecisionRequired, exhausted.InboxItem!.WorkflowState);
        Assert.True(exhausted.InboxItem.OwnerAttentionRequired);
        var third = await harness.Service.StartRemediationAsync(new(harness.ProjectId, root, root));
        Assert.Equal(ReviewWorkflowMutationStatus.InvalidRequest, third.Status);
    }

    [Fact]
    public async Task Revalidation_RequiresExactSuccessfulApo48DecisionAndPreventsEarlyRereview()
    {
        using var harness = CreateHarness();
        var root = await CreateAcceptedCaseAsync(harness);
        Assert.True((await harness.Service.StartRemediationAsync(new(harness.ProjectId, root, root))).Succeeded);
        Assert.True((await harness.Service.CompleteRemediationAsync(new(harness.ProjectId, root, root, 1, EvidenceReferences: ["attempt-1"]))).Succeeded);

        var missing = await harness.Service.RecordRevalidationAsync(new(
            harness.ProjectId, root, root, 1,
            new ValidationGateDecisionReference(Guid.NewGuid(), 1, Hash('a'))));
        Assert.True(missing.Status == ReviewWorkflowMutationStatus.InvalidRequest, missing.ErrorMessage);
        Assert.Equal(ReviewWorkflowState.RevalidationRequired, (await harness.Service.ReadCaseAsync(harness.ProjectId, root)).InboxItem!.WorkflowState);

        var early = await harness.Service.LinkRereviewAsync(new(harness.ProjectId, root, root, Guid.NewGuid()));
        Assert.Equal(ReviewWorkflowMutationStatus.InvalidRequest, early.Status);
    }

    [Fact]
    public async Task Revalidation_RejectsAnExactDecisionFromAnotherProject()
    {
        using var harness = CreateHarness();
        var root = await CreateAcceptedCaseAsync(harness);
        Assert.True((await harness.Service.StartRemediationAsync(new(harness.ProjectId, root, root))).Succeeded);
        var completed = await harness.Service.CompleteRemediationAsync(new(
            harness.ProjectId, root, root, 1, EvidenceReferences: ["attempt-1"]));
        Assert.True(completed.Succeeded, completed.ErrorMessage);

        var otherProjectDecision = CreateDecision(Guid.NewGuid(), harness.Clock.UtcNow, ValidationGateDecisionState.Satisfied);
        harness.Validation.Decision = otherProjectDecision;
        var result = await harness.Service.RecordRevalidationAsync(new(
            harness.ProjectId, root, root, 1, otherProjectDecision.Reference));

        Assert.Equal(ReviewWorkflowMutationStatus.InvalidRequest, result.Status);
        Assert.Equal(ReviewWorkflowState.RevalidationRequired,
            (await harness.Service.ReadCaseAsync(harness.ProjectId, root)).InboxItem!.WorkflowState);
    }

    [Fact]
    public async Task Store_RejectsDuplicateEventsAndKeepsProjectsIsolated()
    {
        using var harness = CreateHarness();
        var otherProject = Guid.NewGuid();
        var root = Guid.NewGuid();
        var otherRoot = Guid.NewGuid();
        await harness.Reviews.AppendReviewAsync(Review(root, harness.ProjectId, harness.Clock.UtcNow, blockingFinding: false));
        await harness.Reviews.AppendReviewAsync(Review(otherRoot, otherProject, harness.Clock.UtcNow, blockingFinding: true));

        var eventValue = new ReviewWorkflowEvent(Guid.NewGuid(), harness.ProjectId, root, root,
            ReviewWorkflowEventKind.HumanDecisionRequired, harness.Clock.UtcNow, reason: "owner attention");
        Assert.Equal(ReviewWorkflowStoreWriteStatus.Created, (await harness.Events.AppendAsync(eventValue)).Status);
        Assert.Equal(ReviewWorkflowStoreWriteStatus.DuplicateEvent, (await harness.Events.AppendAsync(eventValue)).Status);

        var inbox = await harness.Service.ReadInboxAsync(harness.ProjectId);
        Assert.True(inbox.IsUsable);
        Assert.Single(inbox.Items);
        Assert.Equal(harness.ProjectId, inbox.Items[0].ProjectId);
        Assert.Equal(ReviewWorkflowState.HumanDecisionRequired, inbox.Items[0].WorkflowState);
    }

    [Fact]
    public async Task EqualTimestampEvents_UseEventIdAsDeterministicTieBreaker()
    {
        using var harness = CreateHarness();
        var root = Guid.NewGuid();
        var occurredAt = harness.Clock.UtcNow;
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var second = new ReviewWorkflowEvent(secondId, harness.ProjectId, root, root,
            ReviewWorkflowEventKind.HumanDecisionRequired, occurredAt, reason: "second");
        var first = new ReviewWorkflowEvent(firstId, harness.ProjectId, root, root,
            ReviewWorkflowEventKind.HumanDecisionRequired, occurredAt, reason: "first");

        Assert.True((await harness.Events.AppendAsync(second)).Succeeded);
        Assert.True((await harness.Events.AppendAsync(first)).Succeeded);

        var result = await harness.Events.ReadAllAsync(harness.ProjectId);
        Assert.Equal([firstId, secondId], result.Records.Select(value => value.EventId));
    }

    [Fact]
    public async Task CorruptWorkflowHistory_DoesNotProduceAHealthyInbox()
    {
        using var harness = CreateHarness();
        var root = Guid.NewGuid();
        await harness.Reviews.AppendReviewAsync(Review(root, harness.ProjectId, harness.Clock.UtcNow, blockingFinding: false));
        var directory = harness.Paths.GetProjectReviewWorkflowDirectory(harness.ProjectId);
        await harness.Paths.EnsureProjectDirectoriesAsync(harness.ProjectId);
        var path = Path.Combine(directory, $"{harness.Clock.UtcNow.UtcDateTime:yyyy-MM}.jsonl");
        await File.WriteAllTextAsync(path, "{ definitely-not-json }\n");

        var result = await harness.Service.ReadInboxAsync(harness.ProjectId);
        Assert.False(result.IsUsable);
        Assert.Empty(result.Items);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public async Task Bounds_RejectOversizedWorkflowTextAndFindingHistory()
    {
        using var harness = CreateHarness();
        var root = Guid.NewGuid();
        await harness.Reviews.AppendReviewAsync(Review(root, harness.ProjectId, harness.Clock.UtcNow, blockingFinding: true));
        var oversized = await harness.Service.AdjudicateFindingAsync(new(
            harness.ProjectId, root, root, "F1", ReviewFindingAdjudication.Accepted,
            "planner", ReviewAuthorityKind.Planner, new string('x', ReviewWorkflowLimits.MaxReasonLength + 1)));
        Assert.Equal(ReviewWorkflowMutationStatus.InvalidRequest, oversized.Status);

        var tooManyFindings = Enumerable.Range(0, 257)
            .Select(index => new ReviewFindingMetadata($"F{index}", "High", "APO-51", "legacy", blocking: false))
            .ToArray();
        var largeReview = new ReviewMetadata(harness.ProjectId, Guid.NewGuid(), harness.Clock.UtcNow, "reviewer", "Pass", "Low", findings: tooManyFindings);
        await harness.Reviews.AppendReviewAsync(largeReview);
        var result = await harness.Service.ReadCaseAsync(harness.ProjectId, largeReview.ReviewId);
        Assert.False(result.IsUsable);
        Assert.Equal(HistoryReadStatus.Partial, result.Status);
    }

    private static async Task<Guid> CreateAcceptedCaseAsync(Harness harness)
    {
        var root = Guid.NewGuid();
        await harness.Reviews.AppendReviewAsync(Review(root, harness.ProjectId, harness.Clock.UtcNow, blockingFinding: true));
        var result = await harness.Service.AdjudicateFindingAsync(new(
            harness.ProjectId, root, root, "F1", ReviewFindingAdjudication.Accepted, "planner", ReviewAuthorityKind.Planner, "fix"));
        Assert.True(result.Succeeded);
        return root;
    }

    private static ReviewMetadata Review(Guid reviewId, Guid projectId, DateTimeOffset occurredAt, bool blockingFinding) =>
        new(projectId, reviewId, occurredAt, "reviewer:opus", "Changes Required", "High", findings:
        [
            new ReviewFindingMetadata("F1", "High", "APO-51:workflow", "legacy", blockingFinding, summary: "bounded finding"),
            new ReviewFindingMetadata("F2", "Low", "APO-51:traceability", "legacy", false, summary: "non-blocking finding")
        ]);

    private static ValidationGateDecision CreateDecision(Guid projectId, DateTimeOffset now, ValidationGateDecisionState state) =>
        new(projectId, Guid.NewGuid(),
            new ValidationPlanReference(projectId, Guid.NewGuid(), 1, 1, Hash('b')),
            new ExecutionRunAuthorityReference(Guid.NewGuid(), 1, Hash('c')),
            new RecoveryCheckpointReference(Guid.NewGuid(), 1, Hash('d')),
            now, state, []);

    private static Harness CreateHarness()
    {
        var temp = new TemporaryStore();
        var reviews = new JsonProjectOrchestrationStore(
            temp.Paths,
            Jsonl<ExecutionRunRecord>(temp),
            Jsonl<EvidenceMetadataRecord>(temp),
            Jsonl<ReviewMetadataRecord>(temp),
            Jsonl<ActivityAuditRecordFile>(temp),
            NullLogger<JsonProjectOrchestrationStore>.Instance);
        var events = new JsonReviewWorkflowStore(
            temp.Paths,
            Jsonl<ReviewWorkflowEventRecord>(temp),
            NullLogger<JsonReviewWorkflowStore>.Instance);
        var clock = new MutableClock(new DateTimeOffset(2026, 9, 6, 10, 0, 0, TimeSpan.Zero));
        var validation = new FakeValidationDecisionRepository();
        var service = new ReviewWorkflowService(reviews, events, clock, validation);
        return new Harness(temp, reviews, events, service, validation, clock);
    }

    private static JsonlEventStore<TRecord> Jsonl<TRecord>(TemporaryStore store) where TRecord : class =>
        new(store.Paths, store.Files, NullLogger<JsonlEventStore<TRecord>>.Instance);

    private static string Hash(char value) => new(value, 64);

    private sealed class Harness(
        TemporaryStore temp,
        JsonProjectOrchestrationStore reviews,
        JsonReviewWorkflowStore events,
        ReviewWorkflowService service,
        FakeValidationDecisionRepository validation,
        MutableClock clock) : IDisposable
    {
        public Guid ProjectId { get; } = Guid.NewGuid();
        public TemporaryStore Temp { get; } = temp;
        public ApplicationDataPaths Paths => Temp.Paths;
        public JsonProjectOrchestrationStore Reviews { get; } = reviews;
        public JsonReviewWorkflowStore Events { get; } = events;
        public ReviewWorkflowService Service { get; } = service;
        public FakeValidationDecisionRepository Validation { get; } = validation;
        public MutableClock Clock { get; } = clock;
        public void Dispose() => Temp.Dispose();
    }

    private sealed class MutableClock(DateTimeOffset initial) : IClock
    {
        public DateTimeOffset UtcNowValue { get; set; } = initial;
        public DateTimeOffset UtcNow => UtcNowValue;
    }

    private sealed class FakeValidationDecisionRepository : IValidationGateDecisionRepository
    {
        public ValidationGateDecision? Decision { get; set; }

        public Task<ValidationDecisionRepositoryWriteResult> CreateAsync(ValidationGateDecision decision, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ValidationDecisionRepositoryWriteResult(ValidationDecisionRepositoryWriteStatus.Created));

        public Task<ValidationDecisionReadResult> GetAsync(Guid projectId, Guid decisionId, CancellationToken cancellationToken = default)
        {
            var decision = Decision;
            return Task.FromResult(decision is not null && decision.ProjectId == projectId && decision.DecisionId == decisionId
                ? new ValidationDecisionReadResult(ValidationDecisionReadState.Valid, decision)
                : new ValidationDecisionReadResult(ValidationDecisionReadState.Missing));
        }
    }
}
