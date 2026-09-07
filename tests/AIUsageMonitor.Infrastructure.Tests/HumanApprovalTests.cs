using AIUsageMonitor.Application.Approvals;
using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Infrastructure;
using AIUsageMonitor.Infrastructure.Approvals;
using AIUsageMonitor.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;

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
                scope.IssueCapability(request, context),
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
            new LocalSingleOwnerDecisionAuthority("owner-1"),
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
                request.EvidenceRevision,
                request.PolicyReference),
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
            request.ProjectId, request.RequestId, scope.IssueCapability(request, context), "Approved exact bindings.", context))).Succeeded);

        var changedContract = new PlanningExecutionContractReference(
            request.ContractReference.ContractId,
            request.ContractReference.Revision + 1,
            request.ContractReference.SchemaVersion,
            new string('f', 64));
        var staleContract = await scope.Service.EvaluateAsync(
            new HumanApprovalEvaluationContext(request.ProjectId, changedContract, request.Target, request.EvidenceRevision, request.PolicyReference),
            request.RequestId);
        Assert.Equal(HumanApprovalState.Stale, staleContract.EffectiveState);
        Assert.Equal(HumanApprovalReasonCode.StaleContract, staleContract.ReasonCode);
        Assert.False(staleContract.CanProceed);

        var changedEvidence = new HumanApprovalEvidenceRevision([
            new HumanApprovalEvidenceReference("validation-decision", "validation:decision-2", Guid.NewGuid(), 1, new string('1', 64))
        ]);
        var staleEvidence = await scope.Service.EvaluateAsync(
            new HumanApprovalEvaluationContext(request.ProjectId, request.ContractReference, request.Target, changedEvidence, request.PolicyReference),
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
    public async Task OwnerDecisionCapability_IsExplicit_AndRejectedRequestCannotBeApprovedOrWaived()
    {
        using var scope = new ApprovalScope();
        var request = CreateRequest(scope);
        var context = CreateContext(request);
        Assert.True((await scope.Service.RequestAsync(request)).Succeeded);

        var unauthorized = await scope.Service.ApproveAsync(new HumanApprovalDecisionRequest(
            request.ProjectId,
            request.RequestId,
            new LocalSingleOwnerDecisionAuthority("planner").IssueTrustedOwnerDecisionCapability(
                CreateIntent(request, context, HumanApprovalEventKind.Approved)),
            "This is not an owner decision.",
            context));
        Assert.Equal(HumanApprovalMutationStatus.Unauthorized, unauthorized.Status);

        var rejected = await scope.Service.RejectAsync(new HumanApprovalDecisionRequest(
            request.ProjectId, request.RequestId,
            scope.IssueCapability(request, context, HumanApprovalEventKind.Rejected),
            "Owner rejected this exact operation.", context));
        Assert.True(rejected.Succeeded);
        var approveAfterReject = await scope.Service.ApproveAsync(new HumanApprovalDecisionRequest(
            request.ProjectId, request.RequestId, scope.IssueCapability(request, context, HumanApprovalEventKind.Approved), "Attempted reversal.", context));
        var waiveAfterReject = await scope.Service.WaiveAsync(new HumanApprovalDecisionRequest(
            request.ProjectId, request.RequestId, scope.IssueCapability(request, context, HumanApprovalEventKind.Waived), "Attempted waiver after rejection.", context));
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
            request.ProjectId, request.RequestId, scope.IssueCapability(request, context, HumanApprovalEventKind.Waived), "Owner waived this exact bounded risk.", context));
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
            approvedRequest.ProjectId, approvedRequest.RequestId, approvedScope.IssueCapability(approvedRequest, approvedContext), "Approved before expiry.", approvedContext))).Succeeded);
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
    public async Task OverCapacityPersistedHistory_FailsClosedWithoutChangingBytesOrAuthorization()
    {
        using var scope = new ApprovalScope();
        var projectId = Guid.NewGuid();
        var start = scope.Clock.UtcNow.AddMinutes(-1);
        HumanApprovalRequest? approvedRequest = null;
        var directory = scope.Paths.GetProjectApprovalsDirectory(projectId);

        for (var index = 0; index < HumanApprovalLimits.MaxEventsPerProject; index++)
        {
            var request = CreateRequest(scope, start.AddTicks(index), projectId);
            approvedRequest ??= request;
            var requested = new HumanApprovalEvent(
                Guid.NewGuid(),
                request.ProjectId,
                request.RequestId,
                HumanApprovalEventKind.Requested,
                request.RequestedAt,
                HumanApprovalActorKind.Requester,
                request.RequesterReference,
                request: request);
            await scope.Events.AppendAsync(directory, requested.OccurredAt, HumanApprovalEventRecord.FromApplication(requested));
        }

        var terminal = new HumanApprovalEvent(
            Guid.NewGuid(),
            projectId,
            approvedRequest!.RequestId,
            HumanApprovalEventKind.Approved,
            start.AddTicks(HumanApprovalLimits.MaxEventsPerProject),
            HumanApprovalActorKind.HumanOwner,
            "owner-1",
            "Direct fixture approval is inside the over-capacity stream.");
        await scope.Events.AppendAsync(directory, terminal.OccurredAt, HumanApprovalEventRecord.FromApplication(terminal));

        var path = scope.Paths.GetMonthlyPartition(directory, start);
        var before = await File.ReadAllBytesAsync(path);
        var read = await scope.Store.ReadProjectAsync(projectId);
        Assert.False(read.IsUsable);
        Assert.Equal(HumanApprovalHistoryReadStatus.Corrupt, read.Status);
        var after = await File.ReadAllBytesAsync(path);
        Assert.Equal(before, after);

        var evaluation = await scope.Service.EvaluateAsync(CreateContext(approvedRequest), approvedRequest.RequestId);
        Assert.Equal(HumanApprovalReasonCode.InvalidHistory, evaluation.ReasonCode);
        Assert.False(evaluation.CanProceed);
        Assert.Equal(RecoveryGateState.Failed, HumanApprovalRecoveryProjection.ToRecoveryGateSnapshot(evaluation).State);
    }

    [Fact]
    public async Task ProjectIsolation_PreventsApprovalFromSatisfyingAnotherProject()
    {
        using var scope = new ApprovalScope();
        var request = CreateRequest(scope);
        var context = CreateContext(request);
        Assert.True((await scope.Service.RequestAsync(request)).Succeeded);
        Assert.True((await scope.Service.ApproveAsync(new HumanApprovalDecisionRequest(
            request.ProjectId, request.RequestId, scope.IssueCapability(request, context), "Approved project A exact target.", context))).Succeeded);

        var projectBContext = new HumanApprovalEvaluationContext(
            Guid.NewGuid(),
            request.ContractReference,
            request.Target,
            request.EvidenceRevision,
            request.PolicyReference);
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
            request.ProjectId, request.RequestId, scope.IssueCapability(request, context), "Approved the opaque operation fingerprint.", context))).Succeeded);
        var exact = await scope.Service.EvaluateAsync(context, request.RequestId);
        Assert.Equal(HumanApprovalState.Approved, exact.EffectiveState);
        Assert.True(exact.CanProceed);

        var changedFingerprint = HumanApprovalTarget.Fingerprinted(
            HumanApprovalActionKind.CredentialChange,
            new string('d', 64),
            "rotate configured credential reference");
        var stale = await scope.Service.EvaluateAsync(
            new HumanApprovalEvaluationContext(request.ProjectId, request.ContractReference, changedFingerprint, request.EvidenceRevision, request.PolicyReference),
            request.RequestId);
        Assert.Equal(HumanApprovalReasonCode.StaleTarget, stale.ReasonCode);
        Assert.False(stale.CanProceed);
    }

    [Fact]
    public async Task OwnerDecisionCapability_IsOpaque_AndVisibleIdentityStringsCannotAuthorize()
    {
        using var scope = new ApprovalScope();
        var request = CreateRequest(scope);
        var context = CreateContext(request);
        Assert.True((await scope.Service.RequestAsync(request)).Succeeded);

        Assert.Empty(typeof(HumanOwnerDecisionCapability).GetConstructors(BindingFlags.Instance | BindingFlags.Public));
        Assert.DoesNotContain(
            typeof(HumanOwnerDecisionCapability).GetMethods(BindingFlags.Static | BindingFlags.Public),
            method => method.Name.Contains("Issue", StringComparison.OrdinalIgnoreCase));

        var sameVisibleIdentityCapability = new LocalSingleOwnerDecisionAuthority("owner-1", "local-owner")
            .IssueTrustedOwnerDecisionCapability(CreateIntent(request, context, HumanApprovalEventKind.Approved));
        var forged = await scope.Service.ApproveAsync(new HumanApprovalDecisionRequest(
            request.ProjectId,
            request.RequestId,
            sameVisibleIdentityCapability,
            "Matching visible strings are not owner proof.",
            context));
        Assert.Equal(HumanApprovalMutationStatus.Unauthorized, forged.Status);

        foreach (var impersonatedReference in new[] { "planner", "executor", "reviewer", "automation" })
        {
            var capability = new LocalSingleOwnerDecisionAuthority(impersonatedReference)
                .IssueTrustedOwnerDecisionCapability(CreateIntent(request, context, HumanApprovalEventKind.Approved));
            var unauthorized = await scope.Service.ApproveAsync(new HumanApprovalDecisionRequest(
                request.ProjectId,
                request.RequestId,
                capability,
                $"The {impersonatedReference} cannot decide.",
                context));
            Assert.Equal(HumanApprovalMutationStatus.Unauthorized, unauthorized.Status);
        }

        var approved = await scope.Service.ApproveAsync(new HumanApprovalDecisionRequest(
            request.ProjectId,
            request.RequestId,
            scope.IssueCapability(request, context),
            "The verified owner approved the exact request.",
            context));
        Assert.True(approved.Succeeded);

        var history = await scope.Store.ReadAsync(request.ProjectId, request.RequestId);
        Assert.Equal("owner-1", history.Histories.Single().Events.Single(value => value.Kind == HumanApprovalEventKind.Approved).ActorReference);
        var path = scope.Paths.GetMonthlyPartition(scope.Paths.GetProjectApprovalsDirectory(request.ProjectId), request.RequestedAt);
        var persisted = await File.ReadAllTextAsync(path);
        Assert.Contains("owner-1", persisted);
        Assert.DoesNotContain("capability", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("proof", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("material", persisted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InboxWithoutCurrentContext_IsUnknownButPreservesHistoricalDecisions()
    {
        using var scope = new ApprovalScope();
        var approvedRequest = CreateRequest(scope);
        var waivedRequest = CreateRequest(scope);
        var rejectedRequest = CreateRequest(scope);
        Assert.True((await scope.Service.RequestAsync(approvedRequest)).Succeeded);
        Assert.True((await scope.Service.RequestAsync(waivedRequest)).Succeeded);
        Assert.True((await scope.Service.RequestAsync(rejectedRequest)).Succeeded);

        Assert.True((await scope.Service.ApproveAsync(new HumanApprovalDecisionRequest(
            approvedRequest.ProjectId, approvedRequest.RequestId, scope.IssueCapability(approvedRequest, CreateContext(approvedRequest), HumanApprovalEventKind.Approved),
            "Approved for unknown-context inbox coverage.", CreateContext(approvedRequest)))).Succeeded);
        Assert.True((await scope.Service.WaiveAsync(new HumanApprovalDecisionRequest(
            waivedRequest.ProjectId, waivedRequest.RequestId, scope.IssueCapability(waivedRequest, CreateContext(waivedRequest), HumanApprovalEventKind.Waived),
            "Waived for unknown-context inbox coverage.", CreateContext(waivedRequest)))).Succeeded);
        Assert.True((await scope.Service.RejectAsync(new HumanApprovalDecisionRequest(
            rejectedRequest.ProjectId, rejectedRequest.RequestId, scope.IssueCapability(rejectedRequest, CreateContext(rejectedRequest), HumanApprovalEventKind.Rejected),
            "Rejected for unknown-context inbox coverage.", CreateContext(rejectedRequest)))).Succeeded);

        var inbox = await scope.Service.ReadInboxAsync(scope.ProjectId);
        Assert.True(inbox.IsUsable);
        Assert.Equal(3, inbox.Items.Count);
        var items = inbox.Items.ToDictionary(value => value.RequestId);
        var expectedDecisionKinds = new Dictionary<Guid, HumanApprovalEventKind>
        {
            [approvedRequest.RequestId] = HumanApprovalEventKind.Approved,
            [waivedRequest.RequestId] = HumanApprovalEventKind.Waived,
            [rejectedRequest.RequestId] = HumanApprovalEventKind.Rejected
        };
        foreach (var request in new[] { approvedRequest, waivedRequest, rejectedRequest })
        {
            var item = items[request.RequestId];
            Assert.False(item.CurrentContextKnown);
            Assert.Equal(HumanApprovalState.CurrentContextUnknown, item.EffectiveState);
            Assert.Null(item.SatisfyingApprovalReference);
            Assert.False(item.IsStale);
            Assert.Equal(HumanApprovalNextAction.ResolveCurrentContext, item.NextRequiredAction);
            Assert.Equal(expectedDecisionKinds[request.RequestId], item.HistoricalDecisionKind);
            Assert.Equal("owner-1", item.DecisionActorReference);
            Assert.NotNull(item.DecisionTimestamp);
        }

        Assert.Equal(HumanApprovalEventKind.Waived, items[waivedRequest.RequestId].HistoricalDecisionKind);
        Assert.Equal(HumanApprovalEventKind.Rejected, items[rejectedRequest.RequestId].HistoricalDecisionKind);
    }

    [Fact]
    public async Task PolicyRevisionDrift_IsStaleAtEvaluationDecisionAndRestart()
    {
        using var scope = new ApprovalScope();
        var request = CreateRequest(scope);
        var policyV1 = CreateContext(request, "apo-49/v1");
        Assert.True((await scope.Service.RequestAsync(request)).Succeeded);
        Assert.True((await scope.Service.ApproveAsync(new HumanApprovalDecisionRequest(
            request.ProjectId, request.RequestId, scope.IssueCapability(request, policyV1),
            "Approved under policy v1.", policyV1))).Succeeded);

        var exact = await scope.Service.EvaluateAsync(policyV1, request.RequestId);
        Assert.Equal(HumanApprovalState.Approved, exact.EffectiveState);
        Assert.True(exact.CanProceed);

        var policyV2 = CreateContext(request, "apo-49/v2");
        var stale = await scope.Service.EvaluateAsync(policyV2, request.RequestId);
        Assert.Equal(HumanApprovalState.Stale, stale.EffectiveState);
        Assert.Equal(HumanApprovalReasonCode.StalePolicy, stale.ReasonCode);
        Assert.False(stale.CanProceed);
        Assert.Null(stale.SatisfyingReference);

        var inbox = await scope.Service.ReadInboxAsync(
            scope.ProjectId,
            new Dictionary<Guid, HumanApprovalEvaluationContext> { [request.RequestId] = policyV2 });
        Assert.Equal(HumanApprovalState.Stale, inbox.Items.Single().EffectiveState);
        Assert.True(inbox.Items.Single().IsStale);
        Assert.Null(inbox.Items.Single().SatisfyingApprovalReference);

        var decisionRequest = CreateRequest(scope);
        Assert.True((await scope.Service.RequestAsync(decisionRequest)).Succeeded);
        var driftedDecision = await scope.Service.ApproveAsync(new HumanApprovalDecisionRequest(
            decisionRequest.ProjectId,
            decisionRequest.RequestId,
            scope.IssueCapability(decisionRequest, CreateContext(decisionRequest, "apo-49/v1")),
            "Decision-time policy drift must fail closed.",
            CreateContext(decisionRequest, "apo-49/v2")));
        Assert.Equal(HumanApprovalMutationStatus.Stale, driftedDecision.Status);
        var driftedHistory = await scope.Store.ReadAsync(decisionRequest.ProjectId, decisionRequest.RequestId);
        Assert.Single(driftedHistory.Histories.Single().Events);

        scope.Service.Dispose();
        scope.Store.Dispose();
        using var restartedStore = CreateStore(scope);
        using var restartedService = new HumanApprovalService(
            restartedStore,
            new LocalSingleOwnerDecisionAuthority("owner-1"),
            new HandoffRedactionService(),
            scope.Clock);
        var afterRestart = await restartedService.EvaluateAsync(policyV2, request.RequestId);
        Assert.Equal(HumanApprovalState.Stale, afterRestart.EffectiveState);
        Assert.Equal(HumanApprovalReasonCode.StalePolicy, afterRestart.ReasonCode);
        Assert.False(afterRestart.CanProceed);
    }

    [Fact]
    public void RecoveryProjection_DoesNotSatisfyUnknownCurrentContext()
    {
        var evaluation = new HumanApprovalEvaluation(
            Guid.NewGuid(),
            Guid.NewGuid(),
            HumanApprovalState.CurrentContextUnknown,
            false,
            HumanApprovalReasonCode.CurrentContextUnknown,
            HumanApprovalNextAction.ResolveCurrentContext,
            true);

        var projection = HumanApprovalRecoveryProjection.ToRecoveryGateSnapshot(evaluation);
        Assert.Equal(RecoveryGateState.Failed, projection.State);

        var incompleteApprovedEvaluation = new HumanApprovalEvaluation(
            Guid.NewGuid(),
            Guid.NewGuid(),
            HumanApprovalState.Approved,
            true,
            HumanApprovalReasonCode.ExactApproved,
            HumanApprovalNextAction.ProceedWithAuthorizedAction,
            false);
        Assert.Equal(
            RecoveryGateState.Failed,
            HumanApprovalRecoveryProjection.ToRecoveryGateSnapshot(incompleteApprovedEvaluation).State);
    }

    [Fact]
    public async Task ExactCapability_CannotReplayAcrossTerminalDecisionKinds()
    {
        using var scope = new ApprovalScope();
        var request = CreateRequest(scope);
        var context = CreateContext(request);
        Assert.True((await scope.Service.RequestAsync(request)).Succeeded);

        var approveCapability = scope.IssueCapability(request, context, HumanApprovalEventKind.Approved);
        var waived = await scope.Service.WaiveAsync(new HumanApprovalDecisionRequest(
            request.ProjectId,
            request.RequestId,
            approveCapability,
            "A capability for approval cannot waive.",
            context));
        var rejected = await scope.Service.RejectAsync(new HumanApprovalDecisionRequest(
            request.ProjectId,
            request.RequestId,
            approveCapability,
            "A capability for approval cannot reject.",
            context));

        Assert.Equal(HumanApprovalMutationStatus.Unauthorized, waived.Status);
        Assert.Equal(HumanApprovalMutationStatus.Unauthorized, rejected.Status);
        var undecided = await scope.Store.ReadAsync(request.ProjectId, request.RequestId);
        Assert.Single(undecided.Histories.Single().Events);

        var approved = await scope.Service.ApproveAsync(new HumanApprovalDecisionRequest(
            request.ProjectId,
            request.RequestId,
            approveCapability,
            "The exact approval capability was used for approval.",
            context));
        Assert.True(approved.Succeeded);
    }

    [Fact]
    public async Task ExactCapability_CannotReplayAcrossRequestsOrProjects()
    {
        using var scope = new ApprovalScope();
        var requestA = CreateRequest(scope);
        var requestB = CreateRequest(scope, projectId: scope.ProjectId);
        var requestProjectB = CreateRequest(scope, projectId: Guid.NewGuid());
        Assert.True((await scope.Service.RequestAsync(requestA)).Succeeded);
        Assert.True((await scope.Service.RequestAsync(requestB)).Succeeded);
        Assert.True((await scope.Service.RequestAsync(requestProjectB)).Succeeded);

        var capabilityForA = scope.IssueCapability(requestA, CreateContext(requestA));
        var replayedOnB = await scope.Service.ApproveAsync(new HumanApprovalDecisionRequest(
            requestB.ProjectId,
            requestB.RequestId,
            capabilityForA,
            "Request A capability must not approve Request B.",
            CreateContext(requestB)));
        var replayedOnProjectB = await scope.Service.ApproveAsync(new HumanApprovalDecisionRequest(
            requestProjectB.ProjectId,
            requestProjectB.RequestId,
            capabilityForA,
            "Project A capability must not approve Project B.",
            CreateContext(requestProjectB)));

        Assert.Equal(HumanApprovalMutationStatus.Unauthorized, replayedOnB.Status);
        Assert.Equal(HumanApprovalMutationStatus.Unauthorized, replayedOnProjectB.Status);
        Assert.Single((await scope.Store.ReadAsync(requestB.ProjectId, requestB.RequestId)).Histories.Single().Events);
        Assert.Single((await scope.Store.ReadAsync(requestProjectB.ProjectId, requestProjectB.RequestId)).Histories.Single().Events);
    }

    [Fact]
    public async Task ExactCapability_RejectsTargetContractEvidenceAndPolicyDriftBeforeWriting()
    {
        using var scope = new ApprovalScope();
        var request = CreateRequest(scope);
        var exactContext = CreateContext(request);
        Assert.True((await scope.Service.RequestAsync(request)).Succeeded);
        var capability = scope.IssueCapability(request, exactContext);

        var movedTarget = HumanApprovalTarget.ProtectedBranchMerge(
            "github.com/example/repository",
            "main",
            new string('a', 40),
            "feature/x",
            new string('c', 40),
            "merge feature x into main");
        var changedContract = new PlanningExecutionContractReference(
            request.ContractReference.ContractId,
            request.ContractReference.Revision + 1,
            request.ContractReference.SchemaVersion,
            new string('f', 64));
        var changedEvidence = new HumanApprovalEvidenceRevision([
            new HumanApprovalEvidenceReference("validation-decision", "validation:decision-2", Guid.NewGuid(), 1, new string('1', 64))
        ]);

        var driftedContexts = new[]
        {
            new HumanApprovalEvaluationContext(request.ProjectId, request.ContractReference, movedTarget, request.EvidenceRevision, request.PolicyReference),
            new HumanApprovalEvaluationContext(request.ProjectId, changedContract, request.Target, request.EvidenceRevision, request.PolicyReference),
            new HumanApprovalEvaluationContext(request.ProjectId, request.ContractReference, request.Target, changedEvidence, request.PolicyReference),
            CreateContext(request, "apo-49/v2")
        };
        foreach (var driftedContext in driftedContexts)
        {
            var result = await scope.Service.ApproveAsync(new HumanApprovalDecisionRequest(
                request.ProjectId,
                request.RequestId,
                capability,
                "A stale current binding must not consume the exact capability.",
                driftedContext));
            Assert.Equal(HumanApprovalMutationStatus.Stale, result.Status);
        }

        var history = await scope.Store.ReadAsync(request.ProjectId, request.RequestId);
        Assert.Single(history.Histories.Single().Events);

        var approved = await scope.Service.ApproveAsync(new HumanApprovalDecisionRequest(
            request.ProjectId,
            request.RequestId,
            capability,
            "The capability remains valid for the still-current exact intent.",
            exactContext));
        Assert.True(approved.Succeeded);
    }

    [Fact]
    public void DecisionCapabilitiesHaveDistinctIntentHashes_AndApplicationCannotMint()
    {
        using var scope = new ApprovalScope();
        var request = CreateRequest(scope);
        var context = CreateContext(request);
        var approved = scope.IssueCapability(request, context, HumanApprovalEventKind.Approved);
        var rejected = scope.IssueCapability(request, context, HumanApprovalEventKind.Rejected);
        var waived = scope.IssueCapability(request, context, HumanApprovalEventKind.Waived);

        Assert.NotEqual(approved.IntentHash, rejected.IntentHash);
        Assert.NotEqual(approved.IntentHash, waived.IntentHash);
        Assert.NotEqual(rejected.IntentHash, waived.IntentHash);

        var applicationAssembly = typeof(HumanApprovalService).Assembly;
        Assert.Null(applicationAssembly.GetType(
            "AIUsageMonitor.Infrastructure.Approvals.LocalSingleOwnerDecisionAuthority",
            throwOnError: false));
        Assert.DoesNotContain(
            applicationAssembly.GetTypes(),
            type => type.Name.Contains("Issuer", StringComparison.OrdinalIgnoreCase) ||
                    type.Name.Contains("CapabilityFactory", StringComparison.OrdinalIgnoreCase));

        var services = new ServiceCollection();
        var root = Path.Combine(Path.GetTempPath(), "AIUsageMonitorCompositionTests", Guid.NewGuid().ToString("N"));
        try
        {
            services.AddInfrastructure(root);
            Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IHumanOwnerDecisionVerifier));
            Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(LocalSingleOwnerDecisionAuthority));
            Assert.DoesNotContain(services, descriptor => descriptor.ServiceType.Name.Contains("Issuer", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
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

    private static HumanApprovalEvaluationContext CreateContext(HumanApprovalRequest request, string? currentPolicyReference = null) =>
        new(request.ProjectId, request.ContractReference, request.Target, request.EvidenceRevision, currentPolicyReference ?? request.PolicyReference);

    private static HumanOwnerDecisionIntent CreateIntent(
        HumanApprovalRequest request,
        HumanApprovalEvaluationContext context,
        HumanApprovalEventKind kind) =>
        new(
            request.ProjectId,
            request.RequestId,
            kind,
            request.Reference.SchemaVersion,
            request.Reference.ContentHash,
            context.ContractReference,
            context.Target.ActionKind,
            context.Target.ContentHash,
            context.EvidenceRevision.SchemaVersion,
            context.EvidenceRevision.ContentHash,
            context.CurrentPolicyReference);

    private static JsonHumanApprovalStore CreateStore(ApprovalScope scope) => new(
        scope.Paths,
        scope.Events,
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
            OwnerAuthority = new LocalSingleOwnerDecisionAuthority("owner-1");
            Events = new JsonlEventStore<HumanApprovalEventRecord>(
                Paths,
                Files,
                NullLogger<JsonlEventStore<HumanApprovalEventRecord>>.Instance);
            Store = CreateStore(this);
            Service = new HumanApprovalService(Store, OwnerAuthority, new HandoffRedactionService(), Clock);
        }

        public string RootDirectory { get; }
        public ApplicationDataPaths Paths { get; }
        public JsonFileStore Files { get; }
        public TestClock Clock { get; }
        public Guid ProjectId { get; }
        public LocalSingleOwnerDecisionAuthority OwnerAuthority { get; }
        public HumanOwnerDecisionCapability IssueCapability(
            HumanApprovalRequest request,
            HumanApprovalEvaluationContext context,
            HumanApprovalEventKind kind = HumanApprovalEventKind.Approved) =>
            OwnerAuthority.IssueTrustedOwnerDecisionCapability(CreateIntent(request, context, kind));
        public JsonlEventStore<HumanApprovalEventRecord> Events { get; }
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
