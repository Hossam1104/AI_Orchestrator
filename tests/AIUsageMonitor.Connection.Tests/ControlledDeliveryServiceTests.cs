using AIUsageMonitor.Application.Approvals;
using AIUsageMonitor.Application.Delivery;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.RemoteEvidence;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Application.Validation;

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

    private static SourceControlDeliveryService CreateService(PlanningExecutionContract contract, FakeDeliveryAdapter adapter, InMemoryAuditStore audit) =>
        new(new FakeContractRepository(contract), new EmptyValidationRepository(), new EmptyApprovalService(), new EmptyReviewService(), [adapter], new EmptyLocalGit(), audit);

    private static SourceControlDeliveryCommand CreateCommand(PlanningExecutionContract contract, SourceControlRemoteEvidence current, SourceControlDeliveryOperationKind operation, string? expectedHeadSha = null)
    {
        var target = new SourceControlDeliveryTarget(RemoteRepositoryProvider.GitHub, "https://github.com/owner/repo.git", "1", "owner/repo", "main", BaseSha, "task", expectedHeadSha ?? HeadSha, "42");
        var evidence = new SourceControlDeliveryEvidence(current.Fingerprint, expectedHeadSha ?? HeadSha, BaseSha);
        return new(Guid.NewGuid(), ProjectId, "APO-63", contract.Reference, operation, target,
            "test", new string('b', 64), "audit:test", evidence, pullRequestTitle: "APO-63");
    }

    private static SourceControlRemoteEvidence CreateEvidence(string headSha, Guid? projectId = null)
    {
        var repo = new RemoteRepositoryIdentity(RemoteRepositoryProvider.GitHub, RemoteEvidenceSource.GitHubRest, "1", "owner/repo", "owner", "repo", "main");
        var repository = new RemoteRepositoryEvidence(projectId ?? ProjectId, RemoteEvidenceState.Available, RemoteEvidenceSource.GitHubRest, DateTimeOffset.UtcNow, repo, RemoteEvidenceState.Available,
            reviewState: RemoteEvidenceState.Available);
        return new(repository, new RemoteBranchEvidence("task", headSha, false), new RemoteBranchEvidence("main", BaseSha, true), new RemotePullRequestEvidence("42", "open", true, "task", "main", headSha, BaseSha, RemoteMergeability.Available));
    }

    private sealed class FakeDeliveryAdapter(SourceControlRemoteEvidence evidence) : IRemoteSourceControlDeliveryAdapter
    {
        public RemoteRepositoryProvider Provider => RemoteRepositoryProvider.GitHub;
        public int MutationCount { get; private set; }
        public Task<SourceControlRemoteEvidence> ReadAsync(SourceControlDeliveryCommand command, CancellationToken cancellationToken = default) => Task.FromResult(evidence);
        public Task<SourceControlRemoteMutationResult> MutateAsync(SourceControlDeliveryCommand command, SourceControlRemoteEvidence currentEvidence, CancellationToken cancellationToken = default)
        {
            MutationCount++;
            return Task.FromResult(new SourceControlRemoteMutationResult(SourceControlDeliveryStatus.Verified));
        }
    }

    private sealed class InMemoryAuditStore : ISourceControlDeliveryAuditStore
    {
        private readonly List<SourceControlDeliveryAuditEvent> _values = [];
        public Task<SourceControlDeliveryAuditReadResult> FindAsync(Guid projectId, Guid commandId, CancellationToken cancellationToken = default)
        {
            var value = _values.LastOrDefault(item => item.ProjectId == projectId && item.CommandId == commandId);
            SourceControlDeliveryAuditReadResult result = value is null
                ? new(SourceControlDeliveryAuditReadState.Missing)
                : new(SourceControlDeliveryAuditReadState.Found, value);
            return Task.FromResult(result);
        }
        public Task AppendAsync(SourceControlDeliveryAuditEvent value, CancellationToken cancellationToken = default) { _values.Add(value); return Task.CompletedTask; }
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

    private sealed class EmptyApprovalService : IHumanApprovalService
    {
        public Task<HumanApprovalOperationResult> RequestAsync(HumanApprovalRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new HumanApprovalOperationResult(HumanApprovalMutationStatus.InvalidRequest));
        public Task<HumanApprovalOperationResult> EscalateAsync(Guid projectId, Guid requestId, string escalationReference, CancellationToken cancellationToken = default) => Task.FromResult(new HumanApprovalOperationResult(HumanApprovalMutationStatus.InvalidRequest));
        public Task<HumanApprovalOperationResult> ApproveAsync(HumanApprovalDecisionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new HumanApprovalOperationResult(HumanApprovalMutationStatus.InvalidRequest));
        public Task<HumanApprovalOperationResult> RejectAsync(HumanApprovalDecisionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new HumanApprovalOperationResult(HumanApprovalMutationStatus.InvalidRequest));
        public Task<HumanApprovalOperationResult> WaiveAsync(HumanApprovalDecisionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new HumanApprovalOperationResult(HumanApprovalMutationStatus.InvalidRequest));
        public Task<HumanApprovalEvaluation> EvaluateAsync(HumanApprovalEvaluationContext context, Guid requestId, CancellationToken cancellationToken = default) => Task.FromResult(new HumanApprovalEvaluation(context.ProjectId, requestId, HumanApprovalState.Pending, false, HumanApprovalReasonCode.Pending, HumanApprovalNextAction.AwaitOwnerDecision, true));
        public Task<HumanApprovalInboxReadResult> ReadInboxAsync(Guid projectId, IReadOnlyDictionary<Guid, HumanApprovalEvaluationContext>? currentContexts = null, CancellationToken cancellationToken = default) => Task.FromResult(new HumanApprovalInboxReadResult(HumanApprovalHistoryReadStatus.Success));
    }

    private sealed class EmptyReviewService : IReviewWorkflowService
    {
        public Task<ReviewWorkflowMutationResult> AdjudicateFindingAsync(ReviewFindingAdjudicationRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ReviewWorkflowMutationResult> StartRemediationAsync(ReviewRemediationStartRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ReviewWorkflowMutationResult> CompleteRemediationAsync(ReviewRemediationCompletionRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ReviewWorkflowMutationResult> RecordRevalidationAsync(ReviewRevalidationRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ReviewWorkflowMutationResult> LinkRereviewAsync(ReviewRereviewLinkRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ReviewWorkflowMutationResult> RequireHumanDecisionAsync(ReviewHumanDecisionRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ReviewWorkflowInboxReadResult> ReadInboxAsync(Guid projectId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ReviewWorkflowCaseReadResult> ReadCaseAsync(Guid projectId, Guid rootReviewId, CancellationToken cancellationToken = default) => Task.FromResult(new ReviewWorkflowCaseReadResult(projectId, rootReviewId, null, [], [], HistoryReadStatus.Success));
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
