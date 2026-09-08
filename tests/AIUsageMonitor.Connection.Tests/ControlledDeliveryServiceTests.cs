using AIUsageMonitor.Application.Approvals;
using AIUsageMonitor.Application.Delivery;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.RemoteEvidence;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Application.Trackers;
using AIUsageMonitor.Application.Validation;
using AIUsageMonitor.Application.Workspaces;

namespace AIUsageMonitor.Connection.Tests;

public sealed class ControlledDeliveryServiceTests
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ContractId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string BaseSha = "1111111111111111111111111111111111111111";
    private const string HeadSha = "2222222222222222222222222222222222222222";

    [Fact]
    public async Task VerifiedCommand_IsIdempotentAndDoesNotRepeatMutation()
    {
        var contract = ContractFixture.Create(ProjectId, ContractId);
        var adapter = new FakeDeliveryAdapter(CreateEvidence(HeadSha));
        var audit = new InMemoryAuditStore();
        var service = CreateService(contract, adapter, audit);
        var command = CreateCommand(contract, CreateEvidence(HeadSha), SourceControlDeliveryOperationKind.UpdatePullRequestMetadata);

        var first = await service.ExecuteAsync(command);
        var second = await service.ExecuteAsync(command);

        Assert.Equal(SourceControlDeliveryStatus.Verified, first.Status);
        Assert.Equal(SourceControlDeliveryStatus.AlreadyApplied, second.Status);
        Assert.Equal(1, adapter.MutationCount);
    }

    [Fact]
    public async Task MovedRemoteHead_IsStaleAndEmitsNoMutation()
    {
        var contract = ContractFixture.Create(ProjectId, ContractId);
        var moved = CreateEvidence("3333333333333333333333333333333333333333");
        var adapter = new FakeDeliveryAdapter(moved);
        var service = CreateService(contract, adapter, new InMemoryAuditStore());
        var command = CreateCommand(contract, moved, SourceControlDeliveryOperationKind.UpdatePullRequestMetadata, expectedHeadSha: HeadSha);

        var result = await service.ExecuteAsync(command);

        Assert.Equal(SourceControlDeliveryStatus.Stale, result.Status);
        Assert.Equal(0, adapter.MutationCount);
    }

    [Fact]
    public async Task CrossProjectRemoteEvidence_IsRejectedBeforeMutation()
    {
        var contract = ContractFixture.Create(ProjectId, ContractId);
        var wrongProject = CreateEvidence(HeadSha, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        var adapter = new FakeDeliveryAdapter(wrongProject);
        var service = CreateService(contract, adapter, new InMemoryAuditStore());
        var command = CreateCommand(contract, wrongProject, SourceControlDeliveryOperationKind.UpdatePullRequestMetadata);

        var result = await service.ExecuteAsync(command);

        Assert.Equal(SourceControlDeliveryStatus.InvalidAuthority, result.Status);
        Assert.Equal(0, adapter.MutationCount);
    }

    [Fact]
    public async Task LocalWriteWithoutAuthoritativeManagedWorkspace_IsRejectedBeforeGitMutation()
    {
        var contract = ContractFixture.Create(ProjectId, ContractId);
        var local = new CountingLocalGit();
        var service = CreateService(contract, new FakeDeliveryAdapter(CreateEvidence(HeadSha)), new InMemoryAuditStore(), localGit: local);
        var command = CreateLocalCommand(contract, ProjectId, workspaceReference: null, workspacePath: Path.GetTempPath());

        var result = await service.ExecuteAsync(command);

        Assert.Equal(SourceControlDeliveryStatus.InvalidAuthority, result.Status);
        Assert.Equal(0, local.MutationCount);
    }

    [Fact]
    public async Task ManagedWorkspaceAuthority_RejectsOtherProjectAndPathAliasButAllowsExactReceipt()
    {
        var contract = ContractFixture.Create(ProjectId, ContractId);
        var fixture = WorkspaceFixture.Create(ProjectId, contract);
        var local = new CountingLocalGit();
        var service = CreateService(contract, new FakeDeliveryAdapter(CreateEvidence(HeadSha)), new InMemoryAuditStore(), localGit: local,
            workspacePlans: fixture.Plans, workspaceReceipts: fixture.Receipts, workspaceApprovalEvidence: fixture.Approvals,
            workspaceRepositories: fixture.Repositories, workspaceVerifier: fixture.Verifier);

        var exact = await service.ExecuteAsync(CreateLocalCommand(contract, ProjectId, fixture.Plan.Reference, fixture.Receipt.WorkspacePath));
        var otherProjectId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var otherContract = ContractFixture.Create(otherProjectId, Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"));
        var otherService = CreateService(otherContract, new FakeDeliveryAdapter(CreateEvidence(HeadSha, otherProjectId)), new InMemoryAuditStore(), localGit: local,
            workspacePlans: fixture.Plans, workspaceReceipts: fixture.Receipts, workspaceApprovalEvidence: fixture.Approvals,
            workspaceRepositories: fixture.Repositories, workspaceVerifier: fixture.Verifier);
        var otherProject = await otherService.ExecuteAsync(CreateLocalCommand(otherContract, otherProjectId, fixture.Plan.Reference, fixture.Receipt.WorkspacePath));
        var differentPath = await service.ExecuteAsync(CreateLocalCommand(contract, ProjectId, fixture.Plan.Reference, Path.Combine(fixture.Root, "other")));

        Assert.Equal(SourceControlDeliveryStatus.Verified, exact.Status);
        Assert.Equal(SourceControlDeliveryStatus.InvalidAuthority, otherProject.Status);
        Assert.Equal(SourceControlDeliveryStatus.InvalidAuthority, differentPath.Status);
        Assert.Equal(1, local.MutationCount);
    }

    [Fact]
    public async Task SameCommandIdWithChangedImmutableIntent_IsRejectedWithoutSecondMutation()
    {
        var contract = ContractFixture.Create(ProjectId, ContractId);
        var adapter = new FakeDeliveryAdapter(CreateEvidence(HeadSha));
        var service = CreateService(contract, adapter, new InMemoryAuditStore());
        var commandId = Guid.NewGuid();
        var original = CreateCommand(contract, CreateEvidence(HeadSha), SourceControlDeliveryOperationKind.UpdatePullRequestMetadata, commandId: commandId);
        var changed = CreateCommand(contract, CreateEvidence(HeadSha), SourceControlDeliveryOperationKind.UpdatePullRequestMetadata, commandId: commandId, title: "changed immutable payload");

        var first = await service.ExecuteAsync(original);
        var second = await service.ExecuteAsync(changed);

        Assert.Equal(SourceControlDeliveryStatus.Verified, first.Status);
        Assert.Equal(SourceControlDeliveryStatus.InvalidAuthority, second.Status);
        Assert.Equal(1, adapter.MutationCount);
    }

    [Fact]
    public async Task AttemptedAuditSurvivesRestartAndReconcilesWithoutReplay()
    {
        var contract = ContractFixture.Create(ProjectId, ContractId);
        var adapter = new CrashAfterMutationAdapter(CreateEvidence(HeadSha));
        var audit = new InMemoryAuditStore { FailAfterFirstAppend = true };
        var command = CreateCommand(contract, CreateEvidence(HeadSha), SourceControlDeliveryOperationKind.UpdatePullRequestMetadata);
        var firstService = CreateService(contract, adapter, audit);

        await Assert.ThrowsAsync<InvalidOperationException>(() => firstService.ExecuteAsync(command));
        audit.FailAfterFirstAppend = false;
        var restarted = CreateService(contract, adapter, audit);
        var result = await restarted.ExecuteAsync(command);

        Assert.Equal(SourceControlDeliveryStatus.AlreadyApplied, result.Status);
        Assert.Equal(1, adapter.MutationCount);
        Assert.Equal(1, adapter.ReconciliationCount);
    }

    [Fact]
    public async Task ExplicitFailingStatusWithNoWorkflowRuns_BlocksHighRiskDelivery()
    {
        var contract = ContractFixture.Create(ProjectId, ContractId);
        var current = CreateEvidence(HeadSha, statuses: [new RemoteStatusEvidence(RemoteStatusKind.CommitStatus, "required", "failure")], statusState: RemoteEvidenceState.Available);
        var authority = HighRiskFixture.Create(contract, current, mismatch: false);
        var adapter = new FakeDeliveryAdapter(current);
        var service = CreateService(contract, adapter, new InMemoryAuditStore(), validationDecisions: new ConfiguredValidationRepository(authority.Decision),
            approvals: new EmptyApprovalService(authority.Approval), reviews: new EmptyReviewService(authority.ReviewCase));

        var result = await service.ExecuteAsync(authority.Command);

        Assert.Equal(SourceControlDeliveryStatus.Blocked, result.Status);
        Assert.Contains("check", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, adapter.MutationCount);
    }

    [Fact]
    public async Task ValidationExecutionAuthorityMismatch_BlocksBeforeRemoteMutation()
    {
        var contract = ContractFixture.Create(ProjectId, ContractId);
        var current = CreateEvidence(HeadSha);
        var authority = HighRiskFixture.Create(contract, current, mismatch: true);
        var adapter = new FakeDeliveryAdapter(current);
        var service = CreateService(contract, adapter, new InMemoryAuditStore(), validationDecisions: new ConfiguredValidationRepository(authority.Decision),
            approvals: new EmptyApprovalService(authority.Approval), reviews: new EmptyReviewService(authority.ReviewCase));

        var result = await service.ExecuteAsync(authority.Command);

        Assert.Equal(SourceControlDeliveryStatus.Blocked, result.Status);
        Assert.Equal(0, adapter.MutationCount);
    }

    [Fact]
    public async Task VerifiedMergeWithTrackerFailure_ReconcilesTrackerOnlyWithoutSecondMerge()
    {
        var contract = ContractFixture.Create(ProjectId, ContractId);
        var current = CreateEvidence(HeadSha);
        var authority = HighRiskFixture.Create(contract, current, mismatch: false, operation: SourceControlDeliveryOperationKind.MergePullRequest);
        var trackerFixture = TrackerFixture.Create(ProjectId);
        var command = AttachTracker(authority.Command, trackerFixture.Plan, trackerFixture.Operation, trackerFixture.Authority);
        var audit = new InMemoryAuditStore();
        audit.Seed(new SourceControlDeliveryAuditEvent(command.CommandId, command.ProjectId, command.OperationKind, SourceControlDeliveryStatus.ReconciliationRequired,
            DateTimeOffset.UtcNow, command.WorkItemIdentity, command.ContractReference.ToString(), command.Target.CanonicalRepositoryIdentity, command.Target.BaseRef, command.Target.BaseSha,
            command.Target.HeadRef, command.Target.HeadSha, command.ActorReference, command.AuditIdentity, command.EvidenceRevision, command.Evidence.RemoteEvidenceFingerprint,
            PullRequestId: command.Target.PullRequestId, MergeCommitSha: "3333333333333333333333333333333333333333", MutationSent: true, MayHaveModifiedRemote: true,
            CommandContentHash: command.CommandContentHash, RemoteDeliveryVerified: true,
            TrackerPlanIdentity: TrackerFixture.PlanIdentity(trackerFixture.Plan), TrackerOperationIdentity: TrackerFixture.OperationIdentity(trackerFixture.Operation),
            TrackerAuthorityContentHash: trackerFixture.Authority.ContentHash, TrackerOutcome: TrackerMutationOutcome.ReconciliationRequired));
        var adapter = new ReconciliationOnlyAdapter(current);
        var tracker = new RetryingTrackerService();
        var first = await CreateService(contract, adapter, audit, tracker: tracker).ExecuteAsync(command);
        var second = await CreateService(contract, adapter, audit, tracker: tracker).ExecuteAsync(command);

        Assert.Equal(SourceControlDeliveryStatus.ReconciliationRequired, first.Status);
        Assert.Equal(SourceControlDeliveryStatus.AlreadyApplied, second.Status);
        Assert.Equal(1, adapter.MutationCount);
        Assert.Equal(2, tracker.AttemptCount);
        Assert.Equal(1, adapter.ReconciliationCount == 2 ? adapter.MutationCount : -1);
    }

    [Fact]
    public async Task TrackerAuthorityChangeUnderSameCommandId_IsRejected()
    {
        var contract = ContractFixture.Create(ProjectId, ContractId);
        var current = CreateEvidence(HeadSha);
        var authority = HighRiskFixture.Create(contract, current, mismatch: false, operation: SourceControlDeliveryOperationKind.MergePullRequest);
        var firstTracker = TrackerFixture.Create(ProjectId);
        var command = AttachTracker(authority.Command, firstTracker.Plan, firstTracker.Operation, firstTracker.Authority);
        var audit = new InMemoryAuditStore();
        audit.Seed(new SourceControlDeliveryAuditEvent(command.CommandId, command.ProjectId, command.OperationKind, SourceControlDeliveryStatus.ReconciliationRequired,
            DateTimeOffset.UtcNow, command.WorkItemIdentity, command.ContractReference.ToString(), command.Target.CanonicalRepositoryIdentity, command.Target.BaseRef, command.Target.BaseSha,
            command.Target.HeadRef, command.Target.HeadSha, command.ActorReference, command.AuditIdentity, command.EvidenceRevision, command.Evidence.RemoteEvidenceFingerprint,
            PullRequestId: command.Target.PullRequestId, MutationSent: true, MayHaveModifiedRemote: true, CommandContentHash: command.CommandContentHash,
            RemoteDeliveryVerified: true, TrackerPlanIdentity: TrackerFixture.PlanIdentity(firstTracker.Plan), TrackerOperationIdentity: TrackerFixture.OperationIdentity(firstTracker.Operation),
            TrackerAuthorityContentHash: firstTracker.Authority.ContentHash, TrackerOutcome: TrackerMutationOutcome.ReconciliationRequired));
        var changedTracker = TrackerFixture.Create(ProjectId, "Done");
        var changed = AttachTracker(command, changedTracker.Plan, changedTracker.Operation, changedTracker.Authority);
        var result = await CreateService(contract, new ReconciliationOnlyAdapter(current), audit, tracker: new RetryingTrackerService()).ExecuteAsync(changed);

        Assert.Equal(SourceControlDeliveryStatus.InvalidAuthority, result.Status);
    }

    [Fact]
    public async Task AttemptedMergeDiscovery_PersistsRemoteVerificationBeforeTracker()
    {
        var contract = ContractFixture.Create(ProjectId, ContractId);
        var authority = HighRiskFixture.Create(contract, CreateEvidence(HeadSha), mismatch: false, operation: SourceControlDeliveryOperationKind.MergePullRequest);
        var trackerFixture = TrackerFixture.Create(ProjectId);
        var command = AttachTracker(authority.Command, trackerFixture.Plan, trackerFixture.Operation, trackerFixture.Authority);
        var audit = new InMemoryAuditStore();
        audit.Seed(new SourceControlDeliveryAuditEvent(command.CommandId, command.ProjectId, command.OperationKind, SourceControlDeliveryStatus.ReconciliationRequired,
            DateTimeOffset.UtcNow, command.WorkItemIdentity, command.ContractReference.ToString(), command.Target.CanonicalRepositoryIdentity, command.Target.BaseRef, command.Target.BaseSha,
            command.Target.HeadRef, command.Target.HeadSha, command.ActorReference, command.AuditIdentity, command.EvidenceRevision, command.Evidence.RemoteEvidenceFingerprint,
            PullRequestId: command.Target.PullRequestId, MutationSent: true, MayHaveModifiedRemote: true, CommandContentHash: command.CommandContentHash,
            RemoteDeliveryVerified: false, TrackerPlanIdentity: TrackerFixture.PlanIdentity(trackerFixture.Plan), TrackerOperationIdentity: TrackerFixture.OperationIdentity(trackerFixture.Operation),
            TrackerAuthorityContentHash: trackerFixture.Authority.ContentHash, TrackerOutcome: TrackerMutationOutcome.ReconciliationRequired)
        { EventKindOverride = SourceControlDeliveryAuditEventKind.Attempted });
        var adapter = new ReconciliationOnlyAdapter(CreateEvidence(HeadSha));
        var tracker = new RetryingTrackerService();

        var first = await CreateService(contract, adapter, audit, tracker: tracker,
            validationDecisions: new ConfiguredValidationRepository(authority.Decision), approvals: new EmptyApprovalService(authority.Approval), reviews: new EmptyReviewService(authority.ReviewCase))
            .ExecuteAsync(command);

        Assert.Equal(SourceControlDeliveryStatus.ReconciliationRequired, first.Status);
        Assert.Equal(0, tracker.AttemptCount);
        Assert.True(audit.Values.Last().RemoteDeliveryVerified);
        Assert.Null(audit.Values.Last().TrackerOutcome);

        var second = await CreateService(contract, adapter, audit, tracker: tracker,
            validationDecisions: new ConfiguredValidationRepository(authority.Decision), approvals: new EmptyApprovalService(authority.Approval), reviews: new EmptyReviewService(authority.ReviewCase))
            .ExecuteAsync(command);

        Assert.Equal(SourceControlDeliveryStatus.ReconciliationRequired, second.Status);
        Assert.Equal(1, tracker.AttemptCount);
        Assert.Equal(1, adapter.MutationCount);
    }

    [Fact]
    public async Task AttemptedMergeDiscoveryWithoutDurableVerification_DoesNotMutateTracker()
    {
        var contract = ContractFixture.Create(ProjectId, ContractId);
        var authority = HighRiskFixture.Create(contract, CreateEvidence(HeadSha), mismatch: false, operation: SourceControlDeliveryOperationKind.MergePullRequest);
        var trackerFixture = TrackerFixture.Create(ProjectId);
        var command = AttachTracker(authority.Command, trackerFixture.Plan, trackerFixture.Operation, trackerFixture.Authority);
        var audit = new InMemoryAuditStore { FailAfterFirstAppend = true };
        audit.Seed(new SourceControlDeliveryAuditEvent(command.CommandId, command.ProjectId, command.OperationKind, SourceControlDeliveryStatus.ReconciliationRequired,
            DateTimeOffset.UtcNow, command.WorkItemIdentity, command.ContractReference.ToString(), command.Target.CanonicalRepositoryIdentity, command.Target.BaseRef, command.Target.BaseSha,
            command.Target.HeadRef, command.Target.HeadSha, command.ActorReference, command.AuditIdentity, command.EvidenceRevision, command.Evidence.RemoteEvidenceFingerprint,
            PullRequestId: command.Target.PullRequestId, MutationSent: true, MayHaveModifiedRemote: true, CommandContentHash: command.CommandContentHash,
            RemoteDeliveryVerified: false, TrackerPlanIdentity: TrackerFixture.PlanIdentity(trackerFixture.Plan), TrackerOperationIdentity: TrackerFixture.OperationIdentity(trackerFixture.Operation),
            TrackerAuthorityContentHash: trackerFixture.Authority.ContentHash, TrackerOutcome: TrackerMutationOutcome.ReconciliationRequired)
        { EventKindOverride = SourceControlDeliveryAuditEventKind.Attempted });
        var adapter = new ReconciliationOnlyAdapter(CreateEvidence(HeadSha));
        var tracker = new RetryingTrackerService();

        var result = await CreateService(contract, adapter, audit, tracker: tracker,
            validationDecisions: new ConfiguredValidationRepository(authority.Decision), approvals: new EmptyApprovalService(authority.Approval), reviews: new EmptyReviewService(authority.ReviewCase))
            .ExecuteAsync(command);

        Assert.Equal(SourceControlDeliveryStatus.ReconciliationRequired, result.Status);
        Assert.Equal(0, tracker.AttemptCount);
        Assert.False(audit.Values.Last().RemoteDeliveryVerified);
        Assert.Equal(1, adapter.MutationCount);
    }

    [Fact]
    public async Task ExactApprovalEvidenceChain_IsAccepted()
    {
        var contract = ContractFixture.Create(ProjectId, ContractId);
        var authority = HighRiskFixture.Create(contract, CreateEvidence(HeadSha), mismatch: false);
        var adapter = new FakeDeliveryAdapter(CreateEvidence(HeadSha), CreateReadyEvidence());
        var audit = new InMemoryAuditStore();
        var result = await CreateService(contract, adapter, audit,
            validationDecisions: new ConfiguredValidationRepository(authority.Decision),
            approvals: new EmptyApprovalService(authority.Approval), reviews: new EmptyReviewService(authority.ReviewCase))
            .ExecuteAsync(authority.Command);

        Assert.Equal(SourceControlDeliveryStatus.Verified, result.Status);
        Assert.Equal(1, adapter.MutationCount);
    }

    [Fact]
    public async Task ApprovalRequestConsistentWithUnrelatedRevision_IsBlocked()
    {
        var contract = ContractFixture.Create(ProjectId, ContractId);
        var current = HighRiskFixture.Create(contract, CreateEvidence(HeadSha), mismatch: false);
        var unrelated = HighRiskFixture.Create(contract, CreateEvidence(HeadSha), mismatch: false);
        var unrelatedApproval = unrelated.Approval;
        var command = ReplaceEvidence(current.Command, new SourceControlDeliveryEvidence(
            current.Command.Evidence.RemoteEvidenceFingerprint, HeadSha, BaseSha,
            current.Decision.Reference, current.Command.Evidence.ReviewRootId, current.Command.Evidence.CurrentReviewId,
            current.Command.Evidence.HumanApprovalRequestId, unrelatedApproval.Request!.EvidenceRevision, "policy:v1",
            current.Command.Evidence.ExecutionRunAuthorityReference));
        var adapter = new FakeDeliveryAdapter(CreateEvidence(HeadSha));
        var result = await CreateService(contract, adapter, new InMemoryAuditStore(),
            validationDecisions: new ConfiguredValidationRepository(current.Decision),
            approvals: new EmptyApprovalService(unrelatedApproval), reviews: new EmptyReviewService(current.ReviewCase))
            .ExecuteAsync(command);

        Assert.Equal(SourceControlDeliveryStatus.Blocked, result.Status);
        Assert.Equal(0, adapter.MutationCount);
    }

    [Fact]
    public async Task ApprovalRevisionWithWrongReviewLineage_IsBlocked()
    {
        var contract = ContractFixture.Create(ProjectId, ContractId);
        var current = HighRiskFixture.Create(contract, CreateEvidence(HeadSha), mismatch: false);
        var wrongReview = HighRiskFixture.Create(contract, CreateEvidence(HeadSha), mismatch: false);
        var wrongRevision = new HumanApprovalEvidenceRevision([
            HumanApprovalEvidenceReference.FromValidationDecision(current.Decision.Reference),
            HumanApprovalEvidenceReference.FromReviewIdentity($"review:{wrongReview.ReviewCase.RootReviewId:D}/{wrongReview.ReviewCase.InboxItem!.CurrentReviewId:D}", wrongReview.ReviewCase.InboxItem.CurrentReviewId)]);
        var command = ReplaceEvidence(current.Command, new SourceControlDeliveryEvidence(
            current.Command.Evidence.RemoteEvidenceFingerprint, HeadSha, BaseSha, current.Decision.Reference,
            current.Command.Evidence.ReviewRootId, current.Command.Evidence.CurrentReviewId, current.Command.Evidence.HumanApprovalRequestId,
            wrongRevision, "policy:v1", current.Command.Evidence.ExecutionRunAuthorityReference));
        var adapter = new FakeDeliveryAdapter(CreateEvidence(HeadSha));
        var result = await CreateService(contract, adapter, new InMemoryAuditStore(),
            validationDecisions: new ConfiguredValidationRepository(current.Decision),
            approvals: new EmptyApprovalService(current.Approval), reviews: new EmptyReviewService(current.ReviewCase))
            .ExecuteAsync(command);

        Assert.Equal(SourceControlDeliveryStatus.Blocked, result.Status);
        Assert.Equal(0, adapter.MutationCount);
    }

    [Fact]
    public async Task SameCommandAcrossIndependentServices_ClaimsOneMutation()
    {
        var contract = ContractFixture.Create(ProjectId, ContractId);
        var adapter = new FakeDeliveryAdapter(CreateEvidence(HeadSha));
        var firstAudit = new InMemoryAuditStore();
        var secondAudit = new InMemoryAuditStore(firstAudit.SharedState);
        var command = CreateCommand(contract, CreateEvidence(HeadSha), SourceControlDeliveryOperationKind.UpdatePullRequestMetadata);
        var first = CreateService(contract, adapter, firstAudit);
        var second = CreateService(contract, adapter, secondAudit);

        var results = await Task.WhenAll(first.ExecuteAsync(command), second.ExecuteAsync(command));

        Assert.Equal(1, adapter.MutationCount);
        Assert.Contains(results, value => value.Status == SourceControlDeliveryStatus.Verified);
        Assert.Contains(results, value => value.Status is SourceControlDeliveryStatus.ReconciliationRequired or SourceControlDeliveryStatus.AlreadyApplied);
    }

    [Fact]
    public async Task ChangedActorUnderSameCommandId_IsRejectedWithoutSecondMutation()
    {
        var contract = ContractFixture.Create(ProjectId, ContractId);
        var adapter = new FakeDeliveryAdapter(CreateEvidence(HeadSha));
        var audit = new InMemoryAuditStore();
        var original = CreateCommand(contract, CreateEvidence(HeadSha), SourceControlDeliveryOperationKind.UpdatePullRequestMetadata, commandId: Guid.NewGuid());
        var changed = ReplaceCommand(original, actorReference: "different-actor");

        Assert.Equal(SourceControlDeliveryStatus.Verified, (await CreateService(contract, adapter, audit).ExecuteAsync(original)).Status);
        Assert.Equal(SourceControlDeliveryStatus.InvalidAuthority, (await CreateService(contract, adapter, audit).ExecuteAsync(changed)).Status);
        Assert.Equal(1, adapter.MutationCount);
    }

    [Fact]
    public async Task ChangedCredentialUnderSameCommandId_IsRejectedWithoutSecondMutation()
    {
        var contract = ContractFixture.Create(ProjectId, ContractId);
        var adapter = new FakeDeliveryAdapter(CreateEvidence(HeadSha));
        var audit = new InMemoryAuditStore();
        var original = ReplaceCommand(CreateCommand(contract, CreateEvidence(HeadSha), SourceControlDeliveryOperationKind.UpdatePullRequestMetadata, commandId: Guid.NewGuid()), credentialReference: "credential-a");
        var changed = ReplaceCommand(original, credentialReference: "credential-b");

        Assert.Equal(SourceControlDeliveryStatus.Verified, (await CreateService(contract, adapter, audit).ExecuteAsync(original)).Status);
        Assert.Equal(SourceControlDeliveryStatus.InvalidAuthority, (await CreateService(contract, adapter, audit).ExecuteAsync(changed)).Status);
        Assert.Equal(1, adapter.MutationCount);
    }

    [Fact]
    public async Task MissingPersistedCommandHash_FailsClosedWithoutMutation()
    {
        var contract = ContractFixture.Create(ProjectId, ContractId);
        var adapter = new FakeDeliveryAdapter(CreateEvidence(HeadSha));
        var audit = new InMemoryAuditStore();
        var command = CreateCommand(contract, CreateEvidence(HeadSha), SourceControlDeliveryOperationKind.UpdatePullRequestMetadata);
        audit.Seed(new SourceControlDeliveryAuditEvent(command.CommandId, command.ProjectId, command.OperationKind, SourceControlDeliveryStatus.ReconciliationRequired,
            DateTimeOffset.UtcNow, command.WorkItemIdentity, command.ContractReference.ToString(), command.Target.CanonicalRepositoryIdentity, command.Target.BaseRef, command.Target.BaseSha,
            command.Target.HeadRef, command.Target.HeadSha, command.ActorReference, command.AuditIdentity, command.EvidenceRevision, command.Evidence.RemoteEvidenceFingerprint));

        var result = await CreateService(contract, adapter, audit).ExecuteAsync(command);

        Assert.Equal(SourceControlDeliveryStatus.InvalidAuthority, result.Status);
        Assert.Equal(0, adapter.MutationCount);
    }

    [Fact]
    public async Task CapacityClaimRejectsBeforeAttemptedAppendOrMutation()
    {
        var contract = ContractFixture.Create(ProjectId, ContractId);
        var adapter = new FakeDeliveryAdapter(CreateEvidence(HeadSha));
        var audit = new InMemoryAuditStore { ForceCapacityExceeded = true };
        var command = CreateCommand(contract, CreateEvidence(HeadSha), SourceControlDeliveryOperationKind.UpdatePullRequestMetadata);

        var result = await CreateService(contract, adapter, audit).ExecuteAsync(command);

        Assert.Equal(SourceControlDeliveryStatus.Blocked, result.Status);
        Assert.Equal(0, adapter.MutationCount);
        Assert.Empty(audit.Values);
    }

    private static SourceControlDeliveryService CreateService(PlanningExecutionContract contract, IRemoteSourceControlDeliveryAdapter adapter, InMemoryAuditStore audit,
        ILocalDeliveryGitService? localGit = null,
        IValidationGateDecisionRepository? validationDecisions = null,
        IHumanApprovalService? approvals = null,
        IReviewWorkflowService? reviews = null,
        ITrackerSynchronizationService? tracker = null,
        IWorkspacePreparationPlanRepository? workspacePlans = null,
        IWorkspacePreparationReceiptRepository? workspaceReceipts = null,
        IWorkspacePreparationApprovalEvidenceRepository? workspaceApprovalEvidence = null,
        IWorkspaceRepository? workspaceRepositories = null,
        IWorkspacePreparedWorkspaceVerifier? workspaceVerifier = null) =>
        new(new FakeContractRepository(contract), validationDecisions ?? new EmptyValidationRepository(), approvals ?? new EmptyApprovalService(), reviews ?? new EmptyReviewService(), [adapter], localGit ?? new EmptyLocalGit(), audit,
            tracker, workspacePlans, workspaceReceipts, workspaceApprovalEvidence, workspaceRepositories, workspaceVerifier);

    private static SourceControlDeliveryCommand CreateCommand(PlanningExecutionContract contract, SourceControlRemoteEvidence current, SourceControlDeliveryOperationKind operation,
        string? expectedHeadSha = null, Guid? commandId = null, string title = "APO-63")
    {
        var target = new SourceControlDeliveryTarget(RemoteRepositoryProvider.GitHub, "https://github.com/owner/repo.git", "1", "owner/repo", "main", BaseSha, "task", expectedHeadSha ?? HeadSha, "42");
        var evidence = new SourceControlDeliveryEvidence(current.Fingerprint, expectedHeadSha ?? HeadSha, BaseSha);
        return new(commandId ?? Guid.NewGuid(), ProjectId, "APO-63", contract.Reference, operation, target,
            "test", new string('b', 64), "audit:test", evidence, pullRequestTitle: title);
    }

    private static SourceControlDeliveryCommand CreateLocalCommand(PlanningExecutionContract contract, Guid projectId, WorkspacePreparationPlanReference? workspaceReference, string workspacePath)
    {
        var target = new SourceControlDeliveryTarget(RemoteRepositoryProvider.GitHub, "https://github.com/owner/repo.git", "1", "owner/repo", "main", BaseSha, "task", HeadSha);
        return new(Guid.NewGuid(), projectId, "APO-63", contract.Reference, SourceControlDeliveryOperationKind.CommitExactChanges, target,
            "test", new string('b', 64), "audit:local", new SourceControlDeliveryEvidence(new string('c', 64), HeadSha, BaseSha),
            workspacePath: workspacePath, expectedParentHeadSha: BaseSha, allowedChangedPaths: ["tracked.txt"], commitMessage: "controlled commit", workspaceReference: workspaceReference);
    }

    private static SourceControlRemoteEvidence CreateEvidence(string headSha, Guid? projectId = null,
        IReadOnlyList<RemoteStatusEvidence>? statuses = null, RemoteEvidenceState statusState = RemoteEvidenceState.NotConfigured,
        RemoteCiState ciResult = RemoteCiState.NoEvidence, RemoteEvidenceState ciState = RemoteEvidenceState.NotConfigured)
    {
        var repo = new RemoteRepositoryIdentity(RemoteRepositoryProvider.GitHub, RemoteEvidenceSource.GitHubRest, "1", "owner/repo", "owner", "repo", "main");
        var repository = new RemoteRepositoryEvidence(projectId ?? ProjectId, RemoteEvidenceState.Available, RemoteEvidenceSource.GitHubRest, DateTimeOffset.UtcNow, repo, RemoteEvidenceState.Available,
            reviewState: RemoteEvidenceState.Available, statuses: statuses, statusState: statusState, ciState: ciState, ciResult: ciResult);
        return new(repository, new RemoteBranchEvidence("task", headSha, false), new RemoteBranchEvidence("main", BaseSha, true), new RemotePullRequestEvidence("42", "open", true, "task", "main", headSha, BaseSha, RemoteMergeability.Available));
    }

    private sealed class FakeDeliveryAdapter : IRemoteSourceControlDeliveryAdapter
    {
        private readonly SourceControlRemoteEvidence _evidence;
        private readonly SourceControlRemoteEvidence? _mutationEvidence;
        public FakeDeliveryAdapter(SourceControlRemoteEvidence evidence, SourceControlRemoteEvidence? mutationEvidence = null)
        {
            _evidence = evidence;
            _mutationEvidence = mutationEvidence;
        }
        public RemoteRepositoryProvider Provider => RemoteRepositoryProvider.GitHub;
        public int MutationCount { get; private set; }
        public Task<SourceControlRemoteEvidence> ReadAsync(SourceControlDeliveryCommand command, CancellationToken cancellationToken = default) => Task.FromResult(_evidence);
        public Task<SourceControlRemoteMutationResult> MutateAsync(SourceControlDeliveryCommand command, SourceControlRemoteEvidence currentEvidence, CancellationToken cancellationToken = default)
        {
            MutationCount++;
            return Task.FromResult(new SourceControlRemoteMutationResult(SourceControlDeliveryStatus.Verified,
                PullRequestId: command.Target.PullRequestId, Evidence: _mutationEvidence));
        }
    }

    private sealed class CrashAfterMutationAdapter(SourceControlRemoteEvidence evidence) : IRemoteSourceControlDeliveryAdapter, IRemoteSourceControlDeliveryReconciliation
    {
        private bool _applied;
        public RemoteRepositoryProvider Provider => RemoteRepositoryProvider.GitHub;
        public int MutationCount { get; private set; }
        public int ReconciliationCount { get; private set; }
        public Task<SourceControlRemoteEvidence> ReadAsync(SourceControlDeliveryCommand command, CancellationToken cancellationToken = default) => Task.FromResult(evidence);
        public Task<SourceControlRemoteMutationResult> MutateAsync(SourceControlDeliveryCommand command, SourceControlRemoteEvidence currentEvidence, CancellationToken cancellationToken = default)
        {
            MutationCount++;
            _applied = true;
            return Task.FromResult(new SourceControlRemoteMutationResult(SourceControlDeliveryStatus.ReconciliationRequired,
                "simulated crash after remote mutation", PullRequestId: command.Target.PullRequestId, MutationSent: true, MayHaveModifiedRemote: true));
        }
        public Task<SourceControlRemoteMutationResult> ReconcileAsync(SourceControlDeliveryCommand command, CancellationToken cancellationToken = default)
        {
            ReconciliationCount++;
            return Task.FromResult(_applied
                ? new SourceControlRemoteMutationResult(SourceControlDeliveryStatus.AlreadyApplied, PullRequestId: command.Target.PullRequestId, MutationSent: true, MayHaveModifiedRemote: true, Evidence: evidence)
                : new SourceControlRemoteMutationResult(SourceControlDeliveryStatus.ReconciliationRequired, "state is not provable", PullRequestId: command.Target.PullRequestId, MutationSent: true, MayHaveModifiedRemote: true, Evidence: evidence));
        }
    }

    private sealed class CountingLocalGit : ILocalDeliveryGitService
    {
        public int MutationCount { get; private set; }
        public Task<LocalDeliveryGitResult> CommitExactChangesAsync(SourceControlDeliveryCommand command, CancellationToken cancellationToken = default)
        {
            MutationCount++;
            return Task.FromResult(new LocalDeliveryGitResult(SourceControlDeliveryStatus.Verified, NewHeadSha: HeadSha, MutationSent: true));
        }
        public Task<LocalDeliveryGitResult> PushExactHeadAsync(SourceControlDeliveryCommand command, CancellationToken cancellationToken = default)
        {
            MutationCount++;
            return Task.FromResult(new LocalDeliveryGitResult(SourceControlDeliveryStatus.Verified, NewHeadSha: command.Target.HeadSha, MutationSent: true));
        }
    }

    private sealed class ConfiguredValidationRepository(ValidationGateDecision decision) : IValidationGateDecisionRepository
    {
        public Task<ValidationDecisionRepositoryWriteResult> CreateAsync(ValidationGateDecision value, CancellationToken cancellationToken = default) => Task.FromResult(new ValidationDecisionRepositoryWriteResult(ValidationDecisionRepositoryWriteStatus.Created));
        public Task<ValidationDecisionReadResult> GetAsync(Guid projectId, Guid decisionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(projectId == decision.ProjectId && decisionId == decision.DecisionId ? new ValidationDecisionReadResult(ValidationDecisionReadState.Valid, decision) : new ValidationDecisionReadResult(ValidationDecisionReadState.Missing));
    }

    private sealed class ReconciliationOnlyAdapter(SourceControlRemoteEvidence evidence) : IRemoteSourceControlDeliveryAdapter, IRemoteSourceControlDeliveryReconciliation
    {
        public RemoteRepositoryProvider Provider => RemoteRepositoryProvider.GitHub;
        public int MutationCount { get; } = 1;
        public int ReconciliationCount { get; private set; }
        public Task<SourceControlRemoteEvidence> ReadAsync(SourceControlDeliveryCommand command, CancellationToken cancellationToken = default) => Task.FromResult(evidence);
        public Task<SourceControlRemoteMutationResult> MutateAsync(SourceControlDeliveryCommand command, SourceControlRemoteEvidence currentEvidence, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SourceControlRemoteMutationResult(SourceControlDeliveryStatus.InvalidAuthority, "merge replay is not permitted"));
        public Task<SourceControlRemoteMutationResult> ReconcileAsync(SourceControlDeliveryCommand command, CancellationToken cancellationToken = default)
        {
            ReconciliationCount++;
            return Task.FromResult(new SourceControlRemoteMutationResult(SourceControlDeliveryStatus.AlreadyApplied,
                PullRequestId: command.Target.PullRequestId, MergeCommitSha: "3333333333333333333333333333333333333333", MutationSent: true,
                MayHaveModifiedRemote: true, Evidence: evidence));
        }
    }

    private sealed class RetryingTrackerService : ITrackerSynchronizationService
    {
        public int AttemptCount { get; private set; }
        public TrackerSynchronizationPlan CreatePlan(TrackerSynchronizationRequest request) => throw new NotSupportedException();
        public Task<TrackerMutationResult> ExecuteAsync(TrackerSynchronizationPlan plan, TrackerSynchronizationOperation operation, TrackerMutationAuthority authority, CancellationToken cancellationToken = default)
        {
            AttemptCount++;
            return Task.FromResult(AttemptCount == 1
                ? new TrackerMutationResult(TrackerMutationOutcome.ReconciliationRequired, "simulated tracker outage", mayHaveModifiedRemote: true)
                : new TrackerMutationResult(TrackerMutationOutcome.Succeeded, verificationState: TrackerEvidenceState.Available));
        }
    }

    private static SourceControlDeliveryCommand AttachTracker(SourceControlDeliveryCommand command, TrackerSynchronizationPlan plan, TrackerSynchronizationOperation operation, TrackerMutationAuthority authority) =>
        new(command.CommandId, command.ProjectId, command.WorkItemIdentity, command.ContractReference, command.OperationKind, command.Target, command.ActorReference,
            command.EvidenceRevision, command.AuditIdentity, command.Evidence, command.CredentialReference, command.WorkspacePath, command.ExpectedParentHeadSha,
            command.AllowedChangedPaths, command.CommitMessage, command.PullRequestTitle, command.PullRequestBody, command.DeliveryComment, command.Reviewers,
            command.TrackerRequest, plan, operation, authority, command.WorkspaceReference);

    private static SourceControlDeliveryCommand ReplaceEvidence(SourceControlDeliveryCommand command, SourceControlDeliveryEvidence evidence) =>
        new(command.CommandId, command.ProjectId, command.WorkItemIdentity, command.ContractReference, command.OperationKind, command.Target, command.ActorReference,
            command.EvidenceRevision, command.AuditIdentity, evidence, command.CredentialReference, command.WorkspacePath, command.ExpectedParentHeadSha,
            command.AllowedChangedPaths, command.CommitMessage, command.PullRequestTitle, command.PullRequestBody, command.DeliveryComment, command.Reviewers,
            command.TrackerRequest, command.TrackerPlan, command.TrackerOperation, command.TrackerAuthority, command.WorkspaceReference);

    private static SourceControlDeliveryCommand ReplaceCommand(SourceControlDeliveryCommand command, string? actorReference = null, string? credentialReference = null) =>
        new(command.CommandId, command.ProjectId, command.WorkItemIdentity, command.ContractReference, command.OperationKind, command.Target,
            actorReference ?? command.ActorReference, command.EvidenceRevision, command.AuditIdentity, command.Evidence, credentialReference,
            command.WorkspacePath, command.ExpectedParentHeadSha, command.AllowedChangedPaths, command.CommitMessage, command.PullRequestTitle,
            command.PullRequestBody, command.DeliveryComment, command.Reviewers, command.TrackerRequest, command.TrackerPlan, command.TrackerOperation,
            command.TrackerAuthority, command.WorkspaceReference);

    private static SourceControlRemoteEvidence CreateReadyEvidence()
    {
        var current = CreateEvidence(HeadSha);
        return new(current.RepositoryEvidence, current.HeadBranch, current.BaseBranch,
            new RemotePullRequestEvidence("42", "open", false, "task", "main", HeadSha, BaseSha, RemoteMergeability.Available));
    }

    private sealed class TrackerFixture
    {
        private TrackerFixture(TrackerSynchronizationPlan plan, TrackerSynchronizationOperation operation, TrackerMutationAuthority authority)
        {
            Plan = plan;
            Operation = operation;
            Authority = authority;
        }

        public TrackerSynchronizationPlan Plan { get; }
        public TrackerSynchronizationOperation Operation { get; }
        public TrackerMutationAuthority Authority { get; }

        public static TrackerFixture Create(Guid projectId, string statusId = "In Progress")
        {
            var tracker = new TrackerProjectIdentity(TrackerProviderKind.Jira, "APO", new Uri("https://jira.example.invalid/"));
            var workItem = new TrackerWorkItemIdentity(TrackerProviderKind.Jira, "APO", "APO-63", "apo-63");
            var target = new TrackerMutationTarget(workItem);
            var operation = new TrackerSynchronizationOperation(TrackerMutationKind.TransitionStatus, target, "In Progress", statusId: statusId);
            var plan = new TrackerSynchronizationPlan(projectId, TrackerEvidenceState.Available, "tracker-evidence", [operation]);
            var now = new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
            var authority = new TrackerMutationAuthority(Guid.NewGuid(), projectId, tracker, target, TrackerMutationKind.TransitionStatus, "In Progress",
                "owner", "tracker-correlation", now, now.AddHours(1), contentIdentity: statusId);
            return new(plan, operation, authority);
        }

        public static string PlanIdentity(TrackerSynchronizationPlan plan) => string.Join("\u001e", plan.ProjectId, plan.EvidenceState, plan.EvidenceFingerprint ?? string.Empty,
            string.Join("\u001f", plan.Operations.Select(OperationIdentity)), string.Join("\u001f", plan.Conflicts), string.Join("\u001f", plan.UnsupportedChanges), string.Join("\u001f", plan.Blockers));
        public static string OperationIdentity(TrackerSynchronizationOperation operation) => string.Join("\u001e", operation.Kind, operation.Target.CanonicalIdentity, operation.ExpectedStateIdentity,
            operation.CommentBody ?? string.Empty, operation.StatusId ?? string.Empty);
    }

    private sealed class HighRiskFixture
    {
        private HighRiskFixture(SourceControlDeliveryCommand command, ValidationGateDecision decision, HumanApprovalEvaluation approval, ReviewWorkflowCaseReadResult reviewCase)
        {
            Command = command;
            Decision = decision;
            Approval = approval;
            ReviewCase = reviewCase;
        }

        public SourceControlDeliveryCommand Command { get; }
        public ValidationGateDecision Decision { get; }
        public HumanApprovalEvaluation Approval { get; }
        public ReviewWorkflowCaseReadResult ReviewCase { get; }

        public static HighRiskFixture Create(PlanningExecutionContract contract, SourceControlRemoteEvidence current, bool mismatch, SourceControlDeliveryOperationKind operation = SourceControlDeliveryOperationKind.MarkReadyForReview)
        {
            var now = new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
            var decisionAuthority = new ExecutionRunAuthorityReference(Guid.NewGuid(), 1, new string('e', 64));
            var commandAuthority = mismatch ? new ExecutionRunAuthorityReference(Guid.NewGuid(), 1, new string('f', 64)) : decisionAuthority;
            var planReference = new ValidationPlanReference(ProjectId, Guid.NewGuid(), 1, 1, new string('d', 64));
            var checkpointReference = new RecoveryCheckpointReference(Guid.NewGuid(), 1, new string('c', 64));
            var decision = new ValidationGateDecision(ProjectId, Guid.NewGuid(), planReference, decisionAuthority, checkpointReference, now,
                ValidationGateDecisionState.Satisfied, []);
            var rootReviewId = Guid.NewGuid();
            var currentReviewId = Guid.NewGuid();
            var reviewEvent = new ReviewWorkflowEvent(Guid.NewGuid(), ProjectId, rootReviewId, currentReviewId, ReviewWorkflowEventKind.RevalidationRecorded, now,
                attemptNumber: 1, validationDecisionReference: decision.Reference, validationState: ValidationGateDecisionState.Satisfied);
            var inbox = new ReviewInboxItem();
            Set(inbox, nameof(ReviewInboxItem.ProjectId), ProjectId);
            Set(inbox, nameof(ReviewInboxItem.RootReviewId), rootReviewId);
            Set(inbox, nameof(ReviewInboxItem.CurrentReviewId), currentReviewId);
            Set(inbox, nameof(ReviewInboxItem.WorkflowState), ReviewWorkflowState.ReadyForAcceptanceAuthority);
            Set(inbox, nameof(ReviewInboxItem.OwnerAttentionRequired), false);
            Set(inbox, nameof(ReviewInboxItem.LatestValidationReference), decision.Reference);
            var reviewCase = new ReviewWorkflowCaseReadResult(ProjectId, rootReviewId, inbox, [], [reviewEvent], HistoryReadStatus.Success);
            var approvalRevision = new HumanApprovalEvidenceRevision([
                HumanApprovalEvidenceReference.FromValidationDecision(decision.Reference),
                HumanApprovalEvidenceReference.FromReviewIdentity($"review:{rootReviewId:D}/{currentReviewId:D}", currentReviewId)]);
            var approvalTarget = HumanApprovalTarget.ProtectedBranchMerge("owner/repo", "main", BaseSha, "task", HeadSha, "Merge task into main");
            var requestId = Guid.NewGuid();
            var request = new HumanApprovalRequest(ProjectId, requestId, HumanApprovalActionKind.ProtectedBranchMerge, contract.Reference, approvalTarget, approvalRevision,
                "owner", now, now.AddHours(1), "controlled merge", "policy:v1");
            var satisfying = new HumanApprovalReference(requestId, HumanApprovalSchema.CurrentVersion, request.ContentHash, Guid.NewGuid(), HumanApprovalEventKind.Approved);
            var approval = new HumanApprovalEvaluation(ProjectId, requestId, HumanApprovalState.Approved, true, HumanApprovalReasonCode.ExactApproved,
                HumanApprovalNextAction.ProceedWithAuthorizedAction, false, request, satisfying);
            var evidence = new SourceControlDeliveryEvidence(current.Fingerprint, HeadSha, BaseSha, decision.Reference, rootReviewId, currentReviewId,
                requestId, approvalRevision, "policy:v1", commandAuthority);
            var target = new SourceControlDeliveryTarget(RemoteRepositoryProvider.GitHub, "https://github.com/owner/repo.git", "1", "owner/repo", "main", BaseSha, "task", HeadSha, "42");
            var command = new SourceControlDeliveryCommand(Guid.NewGuid(), ProjectId, "APO-63", contract.Reference, operation, target,
                "test", new string('b', 64), "audit:high-risk", evidence, pullRequestTitle: "APO-63");
            return new(command, decision, approval, reviewCase);
        }

        private static void Set<T>(T instance, string propertyName, object? value) => typeof(T).GetProperty(propertyName)!.SetValue(instance, value);
    }

    private sealed class InMemoryAuditStore : ISourceControlDeliveryAuditStore
    {
        public sealed class AuditState
        {
            public List<SourceControlDeliveryAuditEvent> Values { get; } = [];
        }

        private static readonly SemaphoreSlim Gate = new(1, 1);
        private readonly AuditState _state;
        public InMemoryAuditStore(AuditState? state = null) => _state = state ?? new();
        public AuditState SharedState => _state;
        public bool FailAfterFirstAppend { get; set; }
        public bool ForceCapacityExceeded { get; set; }
        public IReadOnlyList<SourceControlDeliveryAuditEvent> Values => _state.Values;
        public void Seed(SourceControlDeliveryAuditEvent value) => _state.Values.Add(value);
        public Task<SourceControlDeliveryAuditReadResult> FindAsync(Guid projectId, Guid commandId, CancellationToken cancellationToken = default)
        {
            var value = _state.Values.LastOrDefault(item => item.ProjectId == projectId && item.CommandId == commandId);
            if (value is not null && !SourceControlDeliveryAuditEvent.IsSha256(value.CommandContentHash))
                return Task.FromResult<SourceControlDeliveryAuditReadResult>(new(SourceControlDeliveryAuditReadState.Corrupt, ErrorMessage: "invalid command hash"));
            SourceControlDeliveryAuditReadResult result = value is null
                ? new(SourceControlDeliveryAuditReadState.Missing)
                : new(SourceControlDeliveryAuditReadState.Found, value);
            return Task.FromResult(result);
        }
        public async Task<SourceControlDeliveryAttemptClaim> TryBeginAttemptAsync(SourceControlDeliveryCommand command, CancellationToken cancellationToken = default)
        {
            await Gate.WaitAsync(cancellationToken);
            try
            {
                var existing = _state.Values.LastOrDefault(item => item.ProjectId == command.ProjectId && item.CommandId == command.CommandId);
                if (existing is not null)
                    return existing.MatchesCommand(command)
                        ? new(SourceControlDeliveryAttemptClaimState.Existing, existing)
                        : new(SourceControlDeliveryAttemptClaimState.ConflictingIntent, existing, "conflicting command intent");
                if (ForceCapacityExceeded || _state.Values.Count >= SourceControlDeliveryLimits.MaxAuditRecords)
                    return new(SourceControlDeliveryAttemptClaimState.CapacityExceeded, ErrorMessage: "capacity");
                var attempted = new SourceControlDeliveryAuditEvent(
                    command.CommandId, command.ProjectId, command.OperationKind, SourceControlDeliveryStatus.ReconciliationRequired,
                    DateTimeOffset.UtcNow, command.WorkItemIdentity, command.ContractReference.ToString(), command.Target.CanonicalRepositoryIdentity,
                    command.Target.BaseRef, command.Target.BaseSha, command.Target.HeadRef, command.Target.HeadSha, command.ActorReference,
                    command.AuditIdentity, command.EvidenceRevision, command.Evidence.RemoteEvidenceFingerprint,
                    PullRequestId: command.Target.PullRequestId, CommandContentHash: command.CommandContentHash,
                    CredentialReference: command.CredentialReference,
                    TrackerPlanIdentity: SourceControlDeliveryIntent.TrackerPlanIdentity(command.TrackerPlan),
                    TrackerOperationIdentity: SourceControlDeliveryIntent.TrackerOperationIdentity(command.TrackerOperation),
                    TrackerAuthorityContentHash: command.TrackerAuthority?.ContentHash)
                {
                    EventKindOverride = SourceControlDeliveryAuditEventKind.Attempted
                };
                _state.Values.Add(attempted);
                return new(SourceControlDeliveryAttemptClaimState.Claimed);
            }
            finally
            {
                Gate.Release();
            }
        }
        public async Task AppendAsync(SourceControlDeliveryAuditEvent value, CancellationToken cancellationToken = default)
        {
            await Gate.WaitAsync(cancellationToken);
            try
            {
                if (FailAfterFirstAppend && _state.Values.Count > 0) throw new InvalidOperationException("simulated outcome persistence failure");
                _state.Values.Add(value);
            }
            finally
            {
                Gate.Release();
            }
        }
    }

    private sealed class FakeContractRepository(PlanningExecutionContract contract) : IPlanningExecutionContractRepository
    {
        public Task<PlanningContractRepositoryWriteResult> CreateAsync(PlanningExecutionContract value, CancellationToken cancellationToken = default) => Task.FromResult(new PlanningContractRepositoryWriteResult(PlanningContractRepositoryWriteStatus.Created));
        public Task<PlanningContractReadResult> GetAsync(Guid projectId, Guid contractId, int revision, CancellationToken cancellationToken = default) => Task.FromResult(projectId == contract.ProjectId && contractId == contract.ContractId && revision == contract.Revision ? new PlanningContractReadResult(PlanningContractReadState.Valid, contract) : new PlanningContractReadResult(PlanningContractReadState.Missing));
        public Task<PlanningContractReadResult> GetLatestAsync(Guid projectId, Guid contractId, CancellationToken cancellationToken = default) => GetAsync(projectId, contractId, 1, cancellationToken);
        public Task<PlanningContractRevisionListResult> ListRevisionsAsync(Guid projectId, Guid contractId, CancellationToken cancellationToken = default) => Task.FromResult(new PlanningContractRevisionListResult(PlanningContractReadState.Valid, [contract]));
    }

    private sealed class EmptyLocalGit : ILocalDeliveryGitService
    {
        public Task<LocalDeliveryGitResult> CommitExactChangesAsync(SourceControlDeliveryCommand command, CancellationToken cancellationToken = default) => Task.FromResult(new LocalDeliveryGitResult(SourceControlDeliveryStatus.Blocked));
        public Task<LocalDeliveryGitResult> PushExactHeadAsync(SourceControlDeliveryCommand command, CancellationToken cancellationToken = default) => Task.FromResult(new LocalDeliveryGitResult(SourceControlDeliveryStatus.Blocked));
    }

    private sealed class EmptyValidationRepository : IValidationGateDecisionRepository
    {
        public Task<ValidationDecisionRepositoryWriteResult> CreateAsync(ValidationGateDecision decision, CancellationToken cancellationToken = default) => Task.FromResult(new ValidationDecisionRepositoryWriteResult(ValidationDecisionRepositoryWriteStatus.Created));
        public Task<ValidationDecisionReadResult> GetAsync(Guid projectId, Guid decisionId, CancellationToken cancellationToken = default) => Task.FromResult(new ValidationDecisionReadResult(ValidationDecisionReadState.Missing));
    }

    private sealed class EmptyApprovalService(HumanApprovalEvaluation? configuredEvaluation = null) : IHumanApprovalService
    {
        public Task<HumanApprovalOperationResult> RequestAsync(HumanApprovalRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new HumanApprovalOperationResult(HumanApprovalMutationStatus.InvalidRequest));
        public Task<HumanApprovalOperationResult> EscalateAsync(Guid projectId, Guid requestId, string escalationReference, CancellationToken cancellationToken = default) => Task.FromResult(new HumanApprovalOperationResult(HumanApprovalMutationStatus.InvalidRequest));
        public Task<HumanApprovalOperationResult> ApproveAsync(HumanApprovalDecisionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new HumanApprovalOperationResult(HumanApprovalMutationStatus.InvalidRequest));
        public Task<HumanApprovalOperationResult> RejectAsync(HumanApprovalDecisionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new HumanApprovalOperationResult(HumanApprovalMutationStatus.InvalidRequest));
        public Task<HumanApprovalOperationResult> WaiveAsync(HumanApprovalDecisionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new HumanApprovalOperationResult(HumanApprovalMutationStatus.InvalidRequest));
        public Task<HumanApprovalEvaluation> EvaluateAsync(HumanApprovalEvaluationContext context, Guid requestId, CancellationToken cancellationToken = default) => Task.FromResult(configuredEvaluation ?? new HumanApprovalEvaluation(context.ProjectId, requestId, HumanApprovalState.Pending, false, HumanApprovalReasonCode.Pending, HumanApprovalNextAction.AwaitOwnerDecision, true));
        public Task<HumanApprovalInboxReadResult> ReadInboxAsync(Guid projectId, IReadOnlyDictionary<Guid, HumanApprovalEvaluationContext>? currentContexts = null, CancellationToken cancellationToken = default) => Task.FromResult(new HumanApprovalInboxReadResult(HumanApprovalHistoryReadStatus.Success));
    }

    private sealed class EmptyReviewService(ReviewWorkflowCaseReadResult? configuredCase = null) : IReviewWorkflowService
    {
        public Task<ReviewWorkflowMutationResult> AdjudicateFindingAsync(ReviewFindingAdjudicationRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ReviewWorkflowMutationResult> StartRemediationAsync(ReviewRemediationStartRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ReviewWorkflowMutationResult> CompleteRemediationAsync(ReviewRemediationCompletionRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ReviewWorkflowMutationResult> RecordRevalidationAsync(ReviewRevalidationRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ReviewWorkflowMutationResult> LinkRereviewAsync(ReviewRereviewLinkRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ReviewWorkflowMutationResult> RequireHumanDecisionAsync(ReviewHumanDecisionRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ReviewWorkflowInboxReadResult> ReadInboxAsync(Guid projectId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ReviewWorkflowCaseReadResult> ReadCaseAsync(Guid projectId, Guid rootReviewId, CancellationToken cancellationToken = default) => Task.FromResult(configuredCase ?? new ReviewWorkflowCaseReadResult(projectId, rootReviewId, null, [], [], HistoryReadStatus.Success));
    }

    private sealed class WorkspaceFixture
    {
        private WorkspaceFixture(
            string root,
            WorkspacePreparationPlan plan,
            WorkspacePreparationReceipt receipt,
            WorkspacePreparationApprovalEvidence approval,
            FakeWorkspacePlanRepository plans,
            FakeWorkspaceReceiptRepository receipts,
            FakeWorkspaceApprovalRepository approvals,
            FakeWorkspaceRepository repositories,
            FakeWorkspaceVerifier verifier)
        {
            Root = root;
            Plan = plan;
            Receipt = receipt;
            Approval = approval;
            Plans = plans;
            Receipts = receipts;
            Approvals = approvals;
            Repositories = repositories;
            Verifier = verifier;
        }

        public string Root { get; }
        public WorkspacePreparationPlan Plan { get; }
        public WorkspacePreparationReceipt Receipt { get; }
        public WorkspacePreparationApprovalEvidence Approval { get; }
        public FakeWorkspacePlanRepository Plans { get; }
        public FakeWorkspaceReceiptRepository Receipts { get; }
        public FakeWorkspaceApprovalRepository Approvals { get; }
        public FakeWorkspaceRepository Repositories { get; }
        public FakeWorkspaceVerifier Verifier { get; }

        public static WorkspaceFixture Create(Guid projectId, PlanningExecutionContract contract)
        {
            var root = Path.Combine(Path.GetTempPath(), "apo-63-authority-" + Guid.NewGuid().ToString("N"));
            var workspace = Path.Combine(root, "workspace");
            Directory.CreateDirectory(workspace);
            var now = new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
            var workspaceId = Guid.NewGuid();
            var planId = Guid.NewGuid();
            var correlationId = Guid.NewGuid();
            var context = new WorkspaceContextIdentity(projectId, Guid.NewGuid(), 1, now);
            var fingerprint = WorkspacePreparationIntegrity.ComputeWorkingTreeStateFingerprint(string.Empty);
            var discovery = new WorkspaceRepositoryDiscovery(
                WorkspaceRepositoryDiscoveryStatus.Available, root, root, root, false, BaseSha, "main", false, true, 0,
                [new WorkspaceWorktreeEvidence(workspace, BaseSha, "task", false, false, false)], ["main"],
                workingTreeStateFingerprint: fingerprint, divergence: new WorkspaceRepositoryDivergence(WorkspaceDivergenceState.Unknown));
            var plan = new WorkspacePreparationPlan(projectId, workspaceId, planId, correlationId, now, context, contract.Reference,
                null, null, null, discovery, BaseSha, "task", workspace, WorkspacePreparationPolicy.RequireCleanSource, true, null);
            var approvalId = Guid.NewGuid();
            var approval = new WorkspacePreparationApprovalEvidence(projectId, workspaceId, approvalId, plan.Reference, "owner", now, now);
            var receipt = new WorkspacePreparationReceipt(projectId, workspaceId, correlationId, now, plan.Reference, workspace, "task", BaseSha, BaseSha, root, "apo-test",
                approvalReference: new WorkspacePreparationApprovalReference(approvalId, WorkspacePreparationApprovalEvidenceSchema.CurrentVersion, approval.ContentHash));
            var plans = new FakeWorkspacePlanRepository(plan);
            var receipts = new FakeWorkspaceReceiptRepository(receipt);
            var approvals = new FakeWorkspaceApprovalRepository(approval);
            var repositories = new FakeWorkspaceRepository(discovery);
            var verifier = new FakeWorkspaceVerifier(workspace, root);
            return new(root, plan, receipt, approval, plans, receipts, approvals, repositories, verifier);
        }
    }

    private sealed class FakeWorkspacePlanRepository(WorkspacePreparationPlan plan) : IWorkspacePreparationPlanRepository
    {
        public Task<WorkspacePreparationPlanWriteResult> CreateAsync(WorkspacePreparationPlan value, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspacePreparationPlanWriteResult(WorkspacePreparationPlanWriteStatus.Created));
        public Task<WorkspacePreparationPlanReadResult> GetAsync(Guid projectId, Guid planId, CancellationToken cancellationToken = default) =>
            Task.FromResult(projectId == plan.ProjectId && planId == plan.PlanId ? new WorkspacePreparationPlanReadResult(WorkspacePreparationPlanReadState.Valid, plan) : new WorkspacePreparationPlanReadResult(WorkspacePreparationPlanReadState.Missing));
    }

    private sealed class FakeWorkspaceReceiptRepository(WorkspacePreparationReceipt receipt) : IWorkspacePreparationReceiptRepository
    {
        public Task<WorkspacePreparationReceiptWriteResult> CreateAsync(WorkspacePreparationReceipt value, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspacePreparationReceiptWriteResult(WorkspacePreparationReceiptWriteStatus.Created));
        public Task<WorkspacePreparationReceiptReadResult> GetAsync(Guid projectId, Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(projectId == receipt.ProjectId && workspaceId == receipt.WorkspaceId ? new WorkspacePreparationReceiptReadResult(WorkspacePreparationReceiptReadState.Valid, receipt) : new WorkspacePreparationReceiptReadResult(WorkspacePreparationReceiptReadState.Missing));
    }

    private sealed class FakeWorkspaceApprovalRepository(WorkspacePreparationApprovalEvidence approval) : IWorkspacePreparationApprovalEvidenceRepository
    {
        public Task<WorkspacePreparationApprovalEvidenceWriteResult> CreateAsync(WorkspacePreparationApprovalEvidence value, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspacePreparationApprovalEvidenceWriteResult(WorkspacePreparationApprovalEvidenceWriteStatus.Created));
        public Task<WorkspacePreparationApprovalEvidenceReadResult> GetAsync(Guid projectId, Guid workspaceId, Guid approvalId, CancellationToken cancellationToken = default) =>
            Task.FromResult(projectId == approval.ProjectId && workspaceId == approval.WorkspaceId && approvalId == approval.ApprovalId
                ? new WorkspacePreparationApprovalEvidenceReadResult(WorkspacePreparationApprovalEvidenceReadState.Valid, approval)
                : new WorkspacePreparationApprovalEvidenceReadResult(WorkspacePreparationApprovalEvidenceReadState.Missing));
        public Task<WorkspacePreparationApprovalEvidenceReadResult> GetForPlanAsync(Guid projectId, Guid workspaceId, Guid planId, CancellationToken cancellationToken = default) =>
            GetAsync(projectId, workspaceId, approval.ApprovalId, cancellationToken);
    }

    private sealed class FakeWorkspaceRepository(WorkspaceRepositoryDiscovery discovery) : IWorkspaceRepository
    {
        public Task<WorkspaceRepositoryDiscovery> DiscoverAsync(string registeredPath, CancellationToken cancellationToken = default) => Task.FromResult(discovery);
        public Task<WorkspaceRepositoryMutationResult> AddExactWorktreeAsync(string commonDirectory, string workspaceBranch, string managedWorkspacePath, string exactBaseCommitSha, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceRepositoryMutationResult(true));
    }

    private sealed class FakeWorkspaceVerifier(string workspace, string root) : IWorkspacePreparedWorkspaceVerifier
    {
        public Task<WorkspacePreparedWorkspaceVerification> VerifyPreparedWorkspaceAsync(string workspacePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkspacePreparedWorkspaceVerification(WorkspacePreparedWorkspaceVerificationStatus.Verified, workspace, true, root, root, BaseSha, "task", false, true, 0));
    }

    private static class ContractFixture
    {
        public static PlanningExecutionContract Create(Guid projectId, Guid contractId) => new(
            projectId, contractId, PlanningExecutionContractSchema.CurrentVersion, 1, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), "owner", Guid.NewGuid(),
            new PlanningContextBinding(Guid.NewGuid(), 1), new PlanningWorkItem(PlanningWorkItemSource.Jira, "APO-63", "Controlled delivery"), new PlanningRepositoryTarget(PlanningRepositoryMode.None),
            [new("include", "delivery")], [new("constraint", "bounded")], [new("forbid", "force push")], [new("deliverable", "delivery", true)],
            [new("validation", PlanningValidationKind.Build, "build", true)], [new("acceptance", "accepted", true)], [new(PlanningBudgetKind.Attempts, 1)],
            [new("target", PlanningStopConditionKind.ImmutableTargetMoved, "target"), new("scope", PlanningStopConditionKind.ScopeViolation, "scope"), new("budget", PlanningStopConditionKind.BudgetExceeded, "budget")], ["governance"], "routing", "safety");
    }
}
