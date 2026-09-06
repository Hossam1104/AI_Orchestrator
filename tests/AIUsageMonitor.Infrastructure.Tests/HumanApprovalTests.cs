using AIUsageMonitor.Application.Approvals;
using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageMonitor.Infrastructure.Tests;

public sealed class HumanApprovalTests
{
    [Fact]
    public async Task ExactApproval_RemainsAuthorizedAfterRestart_AndStalesWhenTargetMoves()
    {
        using var scope = new ApprovalScope();
        var request = CreateRequest(scope);
        var context = CreateContext(request);

        var created = await scope.Service.RequestAsync(request);
        Assert.True(created.Succeeded);
        var pending = await scope.Service.EvaluateAsync(context, request.RequestId);
        Assert.Equal(HumanApprovalState.Pending, pending.EffectiveState);
        Assert.False(pending.CanProceed);
        Assert.Equal(RecoveryGateState.Pending, HumanApprovalRecoveryProjection.ToRecoveryGateSnapshot(pending).State);

        var approved = await scope.Service.ApproveAsync(
            new HumanApprovalDecisionRequest(
                request.ProjectId,
                request.RequestId,
                scope.Owner,
                "Owner approved the exact protected merge target.",
                context));
        Assert.True(approved.Succeeded);

        var exact = await scope.Service.EvaluateAsync(context, request.RequestId);
        Assert.Equal(HumanApprovalState.Approved, exact.EffectiveState);
        Assert.True(exact.CanProceed);
        Assert.Equal(RecoveryGateState.Satisfied, HumanApprovalRecoveryProjection.ToRecoveryGateSnapshot(exact).State);
        Assert.NotNull(exact.SatisfyingReference);

        scope.Service.Dispose();
        scope.Store.Dispose();
        using var restartedStore = CreateStore(scope);
        using var restartedService = new HumanApprovalService(
            restartedStore,
            new LocalSingleOwnerAuthority("owner-1"),
            new HandoffRedactionService(),
            scope.Clock);

        var afterRestart = await restartedService.EvaluateAsync(context, request.RequestId);
        Assert.Equal(HumanApprovalState.Approved, afterRestart.EffectiveState);
        Assert.True(afterRestart.CanProceed);

        var inbox = await restartedService.ReadInboxAsync(
            request.ProjectId,
            new Dictionary<Guid, HumanApprovalEvaluationContext> { [request.RequestId] = context });
        Assert.True(inbox.IsUsable);
        Assert.Single(inbox.Items);
        Assert.Equal(request.Target.ContentHash, inbox.Items[0].TargetFingerprint);
        Assert.Equal(request.EvidenceRevision.ContentHash, inbox.Items[0].EvidenceRevisionHash);
        Assert.True(inbox.Items[0].CurrentContextKnown);
        Assert.False(inbox.Items[0].IsStale);

        var movedTarget = HumanApprovalTarget.ProtectedBranchMerge(
            "github.com/example/repository",
            "main",
            new string('a', 40),
            "feature/x",
            new string('c', 40),
            "merge feature x into main");
        var stale = await restartedService.EvaluateAsync(
            new HumanApprovalEvaluationContext(
                request.ProjectId,
                request.ContractReference,
                movedTarget,
                request.EvidenceRevision),
            request.RequestId);
        Assert.Equal(HumanApprovalState.Stale, stale.EffectiveState);
        Assert.Equal(HumanApprovalReasonCode.StaleTarget, stale.ReasonCode);
        Assert.False(stale.CanProceed);
    }

    [Fact]
    public async Task ContractAndEvidenceChanges_AreStale_AndOriginalRequestIsImmutable()
    {
        using var scope = new ApprovalScope();
        var request = CreateRequest(scope);
        var context = CreateContext(request);
        Assert.True((await scope.Service.RequestAsync(request)).Succeeded);
        Assert.True((await scope.Service.ApproveAsync(new HumanApprovalDecisionRequest(
            request.ProjectId, request.RequestId, scope.Owner, "Approved exact bindings.", context))).Succeeded);

        var changedContract = new PlanningExecutionContractReference(
            request.ContractReference.ContractId,
            request.ContractReference.Revision + 1,
            request.ContractReference.SchemaVersion,
            new string('f', 64));
        var staleContract = await scope.Service.EvaluateAsync(
            new HumanApprovalEvaluationContext(request.ProjectId, changedContract, request.Target, request.EvidenceRevision),
            request.RequestId);
        Assert.Equal(HumanApprovalState.Stale, staleContract.EffectiveState);
        Assert.Equal(HumanApprovalReasonCode.StaleContract, staleContract.ReasonCode);
        Assert.False(staleContract.CanProceed);

        var changedEvidence = new HumanApprovalEvidenceRevision([
            new HumanApprovalEvidenceReference("validation-decision", "validation:decision-2", Guid.NewGuid(), 1, new string('1', 64))
        ]);
        var staleEvidence = await scope.Service.EvaluateAsync(
            new HumanApprovalEvaluationContext(request.ProjectId, request.ContractReference, request.Target, changedEvidence),
            request.RequestId);
        Assert.Equal(HumanApprovalState.Stale, staleEvidence.EffectiveState);
        Assert.Equal(HumanApprovalReasonCode.StaleEvidence, staleEvidence.ReasonCode);
        Assert.False(staleEvidence.CanProceed);

        var history = await scope.Store.ReadAsync(request.ProjectId, request.RequestId);
        Assert.True(history.IsUsable);
        Assert.Single(history.Histories);
        Assert.Equal(2, history.Histories[0].Events.Count);
        Assert.Equal(request.ContentHash, history.Histories[0].Events[0].Request!.ContentHash);
        Assert.Equal(HumanApprovalEventKind.Approved, history.Histories[0].Events[1].Kind);
    }

    [Fact]
    public async Task OwnerAuthority_IsExplicit_AndRejectedRequestCannotBeApprovedOrWaived()
    {
        using var scope = new ApprovalScope();
        var request = CreateRequest(scope);
        var context = CreateContext(request);
        Assert.True((await scope.Service.RequestAsync(request)).Succeeded);

        var unauthorized = await scope.Service.ApproveAsync(new HumanApprovalDecisionRequest(
            request.ProjectId,
            request.RequestId,
            new HumanOwnerAuthority("planner", "local-owner"),
            "This is not an owner decision.",
            context));
        Assert.Equal(HumanApprovalMutationStatus.Unauthorized, unauthorized.Status);

        var rejected = await scope.Service.RejectAsync(new HumanApprovalDecisionRequest(
            request.ProjectId, request.RequestId, scope.Owner, "Owner rejected this exact operation.", context));
        Assert.True(rejected.Succeeded);
        var approveAfterReject = await scope.Service.ApproveAsync(new HumanApprovalDecisionRequest(
            request.ProjectId, request.RequestId, scope.Owner, "Attempted reversal.", context));
        var waiveAfterReject = await scope.Service.WaiveAsync(new HumanApprovalDecisionRequest(
            request.ProjectId, request.RequestId, scope.Owner, "Attempted waiver after rejection.", context));
        Assert.Equal(HumanApprovalMutationStatus.AlreadyTerminal, approveAfterReject.Status);
        Assert.Equal(HumanApprovalMutationStatus.AlreadyTerminal, waiveAfterReject.Status);

        var evaluation = await scope.Service.EvaluateAsync(context, request.RequestId);
        Assert.Equal(HumanApprovalState.Rejected, evaluation.EffectiveState);
        Assert.False(evaluation.CanProceed);
        Assert.Equal(RecoveryGateState.Failed, HumanApprovalRecoveryProjection.ToRecoveryGateSnapshot(evaluation).State);

        Assert.Throws<ArgumentException>(() => new HumanApprovalEvent(
            Guid.NewGuid(),
            request.ProjectId,
            request.RequestId,
            HumanApprovalEventKind.Approved,
            scope.Clock.UtcNow,
            HumanApprovalActorKind.Automation,
            "automation",
            "Automation cannot issue a terminal human decision."));
    }

    [Fact]
    public async Task Escalation_IsSingleMarker_AndOwnerDecisionResolvesIt_WhileWaiverRemainsDistinct()
    {
        using var scope = new ApprovalScope();
        var request = CreateRequest(scope);
        var context = CreateContext(request);
        Assert.True((await scope.Service.RequestAsync(request)).Succeeded);

        var escalated = await scope.Service.EscalateAsync(request.ProjectId, request.RequestId, "scheduler:approval-attention");
        Assert.True(escalated.Succeeded);
        Assert.Equal(HumanApprovalState.Escalated, (await scope.Service.EvaluateAsync(context, request.RequestId)).EffectiveState);
        Assert.True((await scope.Service.EvaluateAsync(context, request.RequestId)).OwnerAttentionRequired);
        Assert.False((await scope.Service.EvaluateAsync(context, request.RequestId)).CanProceed);
        Assert.Equal(RecoveryGateState.Pending, HumanApprovalRecoveryProjection.ToRecoveryGateSnapshot(
            await scope.Service.EvaluateAsync(context, request.RequestId)).State);

        var duplicateEscalation = await scope.Service.EscalateAsync(request.ProjectId, request.RequestId, "scheduler:approval-attention");
        Assert.Equal(HumanApprovalMutationStatus.Duplicate, duplicateEscalation.Status);

        var waived = await scope.Service.WaiveAsync(new HumanApprovalDecisionRequest(
            request.ProjectId, request.RequestId, scope.Owner, "Owner waived this exact bounded risk.", context));
        Assert.True(waived.Succeeded);
        var evaluation = await scope.Service.EvaluateAsync(context, request.RequestId);
        Assert.Equal(HumanApprovalState.Waived, evaluation.EffectiveState);
        Assert.True(evaluation.CanProceed);
        Assert.Equal(RecoveryGateState.Satisfied, HumanApprovalRecoveryProjection.ToRecoveryGateSnapshot(evaluation).State);
        Assert.Equal(HumanApprovalReasonCode.ExactWaived, evaluation.ReasonCode);
    }

    [Fact]
    public async Task Expiry_AppliesToPendingAndApprovedDecisions_AndDoesNotReviveAfterRestart()
    {
        using var scope = new ApprovalScope();
        var request = CreateRequest(scope);
        Assert.True((await scope.Service.RequestAsync(request)).Succeeded);
        scope.Clock.UtcNow = request.ExpiresAt;
        var pending = await scope.Service.EvaluateAsync(CreateContext(request), request.RequestId);
        Assert.Equal(HumanApprovalState.Expired, pending.EffectiveState);
        Assert.False(pending.CanProceed);

        using var approvedScope = new ApprovalScope();
        var approvedRequest = CreateRequest(approvedScope);
        var approvedContext = CreateContext(approvedRequest);
        Assert.True((await approvedScope.Service.RequestAsync(approvedRequest)).Succeeded);
        Assert.True((await approvedScope.Service.ApproveAsync(new HumanApprovalDecisionRequest(
            approvedRequest.ProjectId, approvedRequest.RequestId, approvedScope.Owner, "Approved before expiry.", approvedContext))).Succeeded);
        approvedScope.Clock.UtcNow = approvedRequest.ExpiresAt;
        var expired = await approvedScope.Service.EvaluateAsync(approvedContext, approvedRequest.RequestId);
        Assert.Equal(HumanApprovalState.Expired, expired.EffectiveState);
        Assert.False(expired.CanProceed);
        Assert.Equal(RecoveryGateState.Failed, HumanApprovalRecoveryProjection.ToRecoveryGateSnapshot(expired).State);
    }

    [Fact]
    public async Task CorruptHistory_FailsClosed_AndCapacityBoundaryRejectsBeforeWrite()
    {
        using (var scope = new ApprovalScope())
        {
            var request = CreateRequest(scope);
            Assert.True((await scope.Service.RequestAsync(request)).Succeeded);
            var path = scope.Paths.GetMonthlyPartition(
                scope.Paths.GetProjectApprovalsDirectory(request.ProjectId),
                request.RequestedAt);
            var persisted = await File.ReadAllTextAsync(path);
            await File.WriteAllTextAsync(path, persisted.Replace(
                request.ContentHash,
                new string('e', 64),
                StringComparison.Ordinal));
            var evaluation = await scope.Service.EvaluateAsync(CreateContext(request), request.RequestId);
            Assert.Equal(HumanApprovalReasonCode.InvalidHistory, evaluation.ReasonCode);
            Assert.False(evaluation.CanProceed);
        }

        using var capacityScope = new ApprovalScope();
        var projectId = Guid.NewGuid();
        var start = capacityScope.Clock.UtcNow.AddMinutes(-10);
        for (var index = 0; index < HumanApprovalLimits.MaxEventsPerProject; index++)
        {
            var request = CreateRequest(capacityScope, start.AddSeconds(index), projectId);
            var value = new HumanApprovalEvent(
                Guid.NewGuid(),
                request.ProjectId,
                request.RequestId,
                HumanApprovalEventKind.Requested,
                request.RequestedAt,
                HumanApprovalActorKind.Requester,
                request.RequesterReference,
                request: request);
            Assert.True((await capacityScope.Store.AppendAsync(value)).Succeeded);
        }

        var overflow = CreateRequest(capacityScope, start.AddSeconds(HumanApprovalLimits.MaxEventsPerProject), projectId);
        var overflowEvent = new HumanApprovalEvent(
            Guid.NewGuid(),
            overflow.ProjectId,
            overflow.RequestId,
            HumanApprovalEventKind.Requested,
            overflow.RequestedAt,
            HumanApprovalActorKind.Requester,
            overflow.RequesterReference,
            request: overflow);
        var rejected = await capacityScope.Store.AppendAsync(overflowEvent);
        Assert.Equal(HumanApprovalMutationStatus.CapacityExceeded, rejected.Status);
        var reread = await capacityScope.Store.ReadProjectAsync(projectId);
        Assert.True(reread.IsUsable);
        Assert.Equal(HumanApprovalLimits.MaxEventsPerProject, reread.Histories.Count);
    }

    [Fact]
    public async Task ProjectIsolation_PreventsApprovalFromSatisfyingAnotherProject()
    {
        using var scope = new ApprovalScope();
        var request = CreateRequest(scope);
        var context = CreateContext(request);
        Assert.True((await scope.Service.RequestAsync(request)).Succeeded);
        Assert.True((await scope.Service.ApproveAsync(new HumanApprovalDecisionRequest(
            request.ProjectId, request.RequestId, scope.Owner, "Approved project A exact target.", context))).Succeeded);

        var projectBContext = new HumanApprovalEvaluationContext(
            Guid.NewGuid(),
            request.ContractReference,
            request.Target,
            request.EvidenceRevision);
        var projectBEvaluation = await scope.Service.EvaluateAsync(projectBContext, request.RequestId);
        Assert.Equal(HumanApprovalReasonCode.RequestNotFound, projectBEvaluation.ReasonCode);
        Assert.False(projectBEvaluation.CanProceed);
        var projectBRead = await scope.Store.ReadAsync(projectBContext.ProjectId, request.RequestId);
        Assert.Equal(HumanApprovalHistoryReadStatus.Missing, projectBRead.Status);
    }

    [Fact]
    public async Task NonRepositoryActions_UseOpaqueFingerprintedTargets_AndRemainExactBound()
    {
        using var scope = new ApprovalScope();
        var target = HumanApprovalTarget.Fingerprinted(
            HumanApprovalActionKind.CredentialChange,
            new string('a', 64),
            "rotate configured credential reference");
        var request = new HumanApprovalRequest(
            scope.ProjectId,
            Guid.NewGuid(),
            HumanApprovalActionKind.CredentialChange,
            new PlanningExecutionContractReference(Guid.NewGuid(), 1, 1, new string('b', 64)),
            target,
            new HumanApprovalEvidenceRevision([
                new HumanApprovalEvidenceReference("review", "review:credential-change", Guid.NewGuid(), 1, new string('c', 64))
            ]),
            "requester:executor-1",
            scope.Clock.UtcNow.AddMinutes(-1),
            scope.Clock.UtcNow.AddHours(1),
            "Credential rotation requires explicit owner approval.",
            "apo-49/v1");
        var context = CreateContext(request);

        Assert.True((await scope.Service.RequestAsync(request)).Succeeded);
        Assert.True((await scope.Service.ApproveAsync(new HumanApprovalDecisionRequest(
            request.ProjectId, request.RequestId, scope.Owner, "Approved the opaque operation fingerprint.", context))).Succeeded);
        var exact = await scope.Service.EvaluateAsync(context, request.RequestId);
        Assert.Equal(HumanApprovalState.Approved, exact.EffectiveState);
        Assert.True(exact.CanProceed);

        var changedFingerprint = HumanApprovalTarget.Fingerprinted(
            HumanApprovalActionKind.CredentialChange,
            new string('d', 64),
            "rotate configured credential reference");
        var stale = await scope.Service.EvaluateAsync(
            new HumanApprovalEvaluationContext(request.ProjectId, request.ContractReference, changedFingerprint, request.EvidenceRevision),
            request.RequestId);
        Assert.Equal(HumanApprovalReasonCode.StaleTarget, stale.ReasonCode);
        Assert.False(stale.CanProceed);
    }

    private static HumanApprovalRequest CreateRequest(
        ApprovalScope scope,
        DateTimeOffset? requestedAt = null,
        Guid? projectId = null)
    {
        var when = requestedAt ?? scope.Clock.UtcNow.AddMinutes(-1);
        var evidence = new HumanApprovalEvidenceRevision([
            new HumanApprovalEvidenceReference(
                "validation-decision",
                "validation:decision-1",
                Guid.NewGuid(),
                1,
                new string('d', 64)),
            HumanApprovalEvidenceReference.FromReviewIdentity("review:root-1/current-1")
        ]);
        var target = HumanApprovalTarget.ProtectedBranchMerge(
            "github.com/example/repository",
            "main",
            new string('a', 40),
            "feature/x",
            new string('b', 40),
            "merge feature x into main");
        return new(
            projectId ?? scope.ProjectId,
            Guid.NewGuid(),
            HumanApprovalActionKind.ProtectedBranchMerge,
            new PlanningExecutionContractReference(Guid.NewGuid(), 1, 1, new string('c', 64)),
            target,
            evidence,
            "requester:executor-1",
            when,
            when.AddHours(1),
            "Protected branch merge requires owner approval.",
            "apo-49/v1");
    }

    private static HumanApprovalEvaluationContext CreateContext(HumanApprovalRequest request) =>
        new(request.ProjectId, request.ContractReference, request.Target, request.EvidenceRevision);

    private static JsonHumanApprovalStore CreateStore(ApprovalScope scope) => new(
        scope.Paths,
        new JsonlEventStore<HumanApprovalEventRecord>(
            scope.Paths,
            scope.Files,
            NullLogger<JsonlEventStore<HumanApprovalEventRecord>>.Instance),
        NullLogger<JsonHumanApprovalStore>.Instance);

    private sealed class ApprovalScope : IDisposable
    {
        public ApprovalScope()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "AIUsageMonitorApprovalTests", Guid.NewGuid().ToString("N"));
            Paths = new ApplicationDataPaths(RootDirectory);
            Files = new JsonFileStore(NullLogger<JsonFileStore>.Instance);
            Paths.EnsureDirectories();
            Clock = new TestClock(DateTimeOffset.Parse("2026-09-06T10:00:00+00:00"));
            ProjectId = Guid.NewGuid();
            Owner = new HumanOwnerAuthority("owner-1", "local-owner");
            Store = CreateStore(this);
            Service = new HumanApprovalService(Store, new LocalSingleOwnerAuthority("owner-1"), new HandoffRedactionService(), Clock);
        }

        public string RootDirectory { get; }
        public ApplicationDataPaths Paths { get; }
        public JsonFileStore Files { get; }
        public TestClock Clock { get; }
        public Guid ProjectId { get; }
        public HumanOwnerAuthority Owner { get; }
        public JsonHumanApprovalStore Store { get; }
        public HumanApprovalService Service { get; }

        public void Dispose()
        {
            Service.Dispose();
            Store.Dispose();
            try
            {
                if (Directory.Exists(RootDirectory))
                    Directory.Delete(RootDirectory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class TestClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
