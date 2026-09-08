using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Approvals;
using AIUsageMonitor.Application.MissionControl;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Application.Validation;

namespace AIUsageMonitor.Connection.Tests;

public sealed class MissionControlReadModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProjectIsolation_DoesNotLeakExecutionEvidenceAcrossProjects()
    {
        var alpha = CreateProject("Alpha", "C:\\alpha");
        var beta = CreateProject("Beta", "C:\\beta");
        var alphaRun = Run(alpha, ExecutionRunStatus.Running, "ALPHA-1", "Alpha work");
        var betaRun = Run(beta, ExecutionRunStatus.Accepted, "BETA-1", "Beta work");
        var service = CreateService([alpha, beta], executions: [betaRun, alphaRun]);

        var snapshot = await service.ReadAsync(alpha.Id);

        Assert.Equal(alpha.Id, snapshot.ProjectId);
        Assert.Equal(MissionControlState.Running, snapshot.State);
        Assert.Equal("Alpha work", snapshot.CurrentWork.Title);
        Assert.DoesNotContain("Beta", snapshot.CurrentWork.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingSources_ProduceUnknownAndExplicitLimitations()
    {
        var project = CreateProject("No evidence", "C:\\no-evidence");
        var service = CreateService([project]);

        var snapshot = await service.ReadAsync(project.Id);

        Assert.Equal(MissionControlState.Unknown, snapshot.State);
        Assert.Equal("No current work selected", snapshot.CurrentWork.Title);
        Assert.Contains(snapshot.Limitations, value => value.Section == "Project context");
        Assert.Contains(snapshot.Limitations, value => value.Section == "Runtime");
        Assert.DoesNotContain("Ready", snapshot.StateReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExplicitExecutionStates_MapTruthfully()
    {
        var cases = new[]
        {
            (ExecutionRunStatus.Running, MissionControlState.Running),
            (ExecutionRunStatus.Waiting, MissionControlState.Waiting),
            (ExecutionRunStatus.Blocked, MissionControlState.Blocked),
            (ExecutionRunStatus.Failed, MissionControlState.Failed),
            (ExecutionRunStatus.Accepted, MissionControlState.Accepted)
        };

        foreach (var (runStatus, expected) in cases)
        {
            var project = CreateProject(runStatus.ToString(), $"C:\\{runStatus}");
            var service = CreateService([project], executions: [Run(project, runStatus, "WORK-1", "Current work")]);

            var snapshot = await service.ReadAsync(project.Id);

            Assert.Equal(expected, snapshot.State);
        }
    }

    [Fact]
    public async Task CompletedExecution_DoesNotBecomeAccepted()
    {
        var project = CreateProject("Completed", "C:\\completed");
        var service = CreateService([project], executions: [Run(project, ExecutionRunStatus.Completed, "WORK-1", "Completed work")]);

        var snapshot = await service.ReadAsync(project.Id);

        Assert.Equal(MissionControlState.Unknown, snapshot.State);
        Assert.Contains(snapshot.Limitations, value => value.Message.Contains("not acceptance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StaleRunningExecution_IsNotPresentedAsRunning()
    {
        var project = CreateProject("Stale", "C:\\stale");
        var stale = new ExecutionRun(
            project.Id,
            Guid.NewGuid(),
            ExecutionRunStatus.Running,
            Now.AddHours(-2),
            workItemReference: "WORK-1",
            taskTitle: "Old work",
            recordedAt: Now.AddHours(-2));
        var service = CreateService([project], executions: [stale]);

        var snapshot = await service.ReadAsync(project.Id);

        Assert.Equal(MissionControlState.Unknown, snapshot.State);
        Assert.Contains(snapshot.Limitations, value => value.Kind == MissionControlLimitationKind.EvidenceStale);
    }

    [Fact]
    public async Task ReviewState_IsUsedOnlyWhenItCanDriveCurrentState()
    {
        var project = CreateProject("Review", "C:\\review");
        var review = ReviewItem(project, ReviewWorkflowState.AwaitingAdjudication, ownerAttention: true);
        var service = CreateService([project], reviewItems: [review]);

        var snapshot = await service.ReadAsync(project.Id);

        Assert.Equal(MissionControlState.Review, snapshot.State);
        Assert.Contains(snapshot.AttentionItems, value => value.State == MissionControlState.Review);
    }

    [Fact]
    public async Task UnrelatedReview_DoesNotOverrideFreshCurrentExecution()
    {
        var project = CreateProject("Running with old review", "C:\\running-review");
        var review = ReviewItem(project, ReviewWorkflowState.HumanDecisionRequired, ownerAttention: true, timestamp: Now.AddDays(-5));
        var run = Run(project, ExecutionRunStatus.Running, "WORK-2", "Current execution");
        var service = CreateService([project], executions: [run], reviewItems: [review]);

        var snapshot = await service.ReadAsync(project.Id);

        Assert.Equal(MissionControlState.Running, snapshot.State);
        Assert.Contains(snapshot.Limitations, value => value.Kind == MissionControlLimitationKind.CorrelationUnavailable);
    }

    [Fact]
    public async Task HumanApprovalRequired_RequiresCurrentKnownApprovalContext()
    {
        var project = CreateProject("Approval", "C:\\approval");
        var historicalOnly = ApprovalItem(project, currentContextKnown: false);
        var approvalService = new FakeApprovalService([historicalOnly]);
        var service = CreateService([project], approvals: approvalService);

        var historicalSnapshot = await service.ReadAsync(project.Id);

        Assert.NotEqual(MissionControlState.HumanApprovalRequired, historicalSnapshot.State);
        Assert.Contains(historicalSnapshot.Limitations, value => value.Kind == MissionControlLimitationKind.CorrelationUnavailable);

        var current = ApprovalItem(project, currentContextKnown: true);
        approvalService.Items = [current];

        var currentSnapshot = await service.ReadAsync(project.Id);

        Assert.Equal(MissionControlState.HumanApprovalRequired, currentSnapshot.State);
        Assert.Contains(currentSnapshot.AttentionItems, value => value.State == MissionControlState.HumanApprovalRequired);
    }

    [Fact]
    public async Task WaitingDoesNotHideAnExplicitProjectBlocker()
    {
        var project = CreateProject("Blocked", "C:\\blocked", ProjectStatus.Blocked);
        var service = CreateService([project], executions: [Run(project, ExecutionRunStatus.Waiting, "WORK-1", "Waiting work")]);

        var snapshot = await service.ReadAsync(project.Id);

        Assert.Equal(MissionControlState.Blocked, snapshot.State);
        Assert.Contains(snapshot.AttentionItems, value => value.State == MissionControlState.Blocked);
    }

    [Fact]
    public async Task PartialSubsystemFailure_LeavesUsableSnapshotAndExplainsFailure()
    {
        var project = CreateProject("Partial", "C:\\partial");
        var orchestration = new FakeOrchestrationStore { ThrowOnRead = true };
        var service = CreateService([project], orchestration: orchestration);

        var snapshot = await service.ReadAsync(project.Id);

        Assert.NotNull(snapshot);
        Assert.Equal(project.Id, snapshot.ProjectId);
        Assert.Contains(snapshot.Limitations, value => value.Section == "Execution" && value.Kind == MissionControlLimitationKind.EvidenceUnavailable);
    }

    [Fact]
    public async Task ProjectContextRolesAndNextSafeAction_AreProjectedWithoutGuessing()
    {
        var project = CreateProject("Context", "C:\\context");
        var context = CreateContext(project.Id, ProjectNextSafeAction.ReviewProjectContext);
        var service = CreateService([project], context: context);

        var snapshot = await service.ReadAsync(project.Id);

        Assert.Contains(snapshot.Roles.Assignments, value => value.Role == nameof(AgentRole.Planner));
        Assert.Equal("Review project context", snapshot.CurrentWork.NextSafeAction);
        Assert.Equal("Repository integration skipped", snapshot.Repository.StatusText);
    }

    private static MissionControlReadModelService CreateService(
        IReadOnlyList<Project> projects,
        IReadOnlyList<ExecutionRun>? executions = null,
        ProjectContextReference? context = null,
        IReadOnlyList<ReviewInboxItem>? reviewItems = null,
        IReadOnlyList<HumanApprovalInboxItem>? approvalItems = null,
        FakeOrchestrationStore? orchestration = null,
        FakeApprovalService? approvals = null)
    {
        return new(
            new FakeProjectRegistry(projects),
            new FakeContextRepository(context),
            orchestration ?? new FakeOrchestrationStore { Executions = executions ?? [] },
            new FakeReviewService(reviewItems ?? []),
            approvals ?? new FakeApprovalService(approvalItems ?? []),
            new FixedClock());
    }

    private static Project CreateProject(string name, string path, ProjectStatus status = ProjectStatus.Active) =>
        new(Guid.NewGuid(), name, path, null, status, Now.AddDays(-1), Now.AddDays(-1));

    private static ExecutionRun Run(Project project, ExecutionRunStatus status, string reference, string title) =>
        new(project.Id, Guid.NewGuid(), status, Now.AddMinutes(-2), workItemReference: reference, taskTitle: title, recordedAt: Now.AddMinutes(-1));

    private static ReviewInboxItem ReviewItem(Project project, ReviewWorkflowState state, bool ownerAttention, DateTimeOffset? timestamp = null) =>
        new()
        {
            ProjectId = project.Id,
            RootReviewId = Guid.NewGuid(),
            CurrentReviewId = Guid.NewGuid(),
            LatestTimestamp = timestamp ?? Now.AddMinutes(-1),
            ReviewerReference = "reviewer:test",
            CurrentVerdict = "Needs attention",
            CurrentSeverity = "High",
            WorkflowState = state,
            TotalCurrentFindings = 1,
            BlockingFindingCount = 1,
            PendingAdjudicationCount = 1,
            OwnerAttentionRequired = ownerAttention,
            OwnerAttentionReason = "Review action is required.",
            NextRequiredAction = state == ReviewWorkflowState.HumanDecisionRequired
                ? ReviewWorkflowNextAction.HumanDecision
                : ReviewWorkflowNextAction.AdjudicateFindings
        };

    private static HumanApprovalInboxItem ApprovalItem(Project project, bool currentContextKnown) =>
        new()
        {
            ProjectId = project.Id,
            RequestId = Guid.NewGuid(),
            ActionKind = HumanApprovalActionKind.ProtectedBranchMerge,
            RequestedAt = Now.AddMinutes(-1),
            ExpiresAt = Now.AddHours(1),
            EffectiveState = HumanApprovalState.Pending,
            OwnerAttentionRequired = true,
            SafeTargetSummary = "Protected target",
            CurrentContextKnown = currentContextKnown,
            NextRequiredAction = HumanApprovalNextAction.AwaitOwnerDecision
        };

    private static ProjectContextReference CreateContext(Guid projectId, ProjectNextSafeAction nextSafeAction) =>
        new(
            projectId,
            Guid.NewGuid(),
            ProjectContextContract.CurrentVersion,
            Now.AddDays(-1),
            Now.AddDays(-1),
            ProjectRepositoryContextReference.Skipped(projectId, "C:\\context"),
            new ProjectTrackerContextReference(TrackerReferenceState.NotConfigured),
            [new ProjectModelRoleReference(Guid.NewGuid(), "Planner model", [AgentRole.Planner], true, AgentAvailability.Available, AgentAuthenticationState.Authenticated, AgentEntitlementState.VerifiedAvailable)],
            new ProjectCurrentWorkReference(CurrentWorkState.NotSelected),
            [],
            null,
            null,
            nextSafeAction);

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FakeProjectRegistry(IReadOnlyList<Project> projects) : IProjectRegistryService
    {
        public Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken cancellationToken = default) => Task.FromResult(projects);
        public Task<Project> CreateProjectAsync(ProjectEdit edit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Project> UpdateProjectAsync(Guid projectId, ProjectEdit edit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeContextRepository(ProjectContextReference? context) : IProjectContextReferenceRepository
    {
        public Task<ProjectContextReadResult> GetAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(context is null
                ? new ProjectContextReadResult(ProjectContextReadState.Missing)
                : new ProjectContextReadResult(ProjectContextReadState.Valid, context));

        public Task UpsertAsync(ProjectContextReference value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeOrchestrationStore : IProjectOrchestrationStore
    {
        public IReadOnlyList<ExecutionRun> Executions { get; set; } = [];
        public bool ThrowOnRead { get; set; }

        public Task<HistoryReadResult<ExecutionRun>> ReadExecutionRunsAsync(Guid projectId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        {
            if (ThrowOnRead) throw new IOException("test-only failure");
            return Task.FromResult(new HistoryReadResult<ExecutionRun>(Executions, HistoryReadStatus.Success));
        }

        public Task AppendExecutionRunAsync(ExecutionRun run, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AppendEvidenceAsync(EvidenceMetadata evidence, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HistoryReadResult<EvidenceMetadata>> ReadEvidenceAsync(Guid projectId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => Empty<EvidenceMetadata>();
        public Task AppendReviewAsync(ReviewMetadata review, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HistoryReadResult<ReviewMetadata>> ReadReviewsAsync(Guid projectId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => Empty<ReviewMetadata>();
        public Task AppendActivityAsync(ActivityAuditRecord activity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HistoryReadResult<ActivityAuditRecord>> ReadActivityAsync(Guid projectId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => Empty<ActivityAuditRecord>();

        private static Task<HistoryReadResult<T>> Empty<T>() => Task.FromResult(new HistoryReadResult<T>([], HistoryReadStatus.Success));
    }

    private sealed class FakeReviewService(IReadOnlyList<ReviewInboxItem> items) : IReviewWorkflowService
    {
        public Task<ReviewWorkflowInboxReadResult> ReadInboxAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(new ReviewWorkflowInboxReadResult(items, HistoryReadStatus.Success));
        public Task<ReviewWorkflowCaseReadResult> ReadCaseAsync(Guid projectId, Guid rootReviewId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReviewWorkflowMutationResult> AdjudicateFindingAsync(ReviewFindingAdjudicationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReviewWorkflowMutationResult> StartRemediationAsync(ReviewRemediationStartRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReviewWorkflowMutationResult> CompleteRemediationAsync(ReviewRemediationCompletionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReviewWorkflowMutationResult> RecordRevalidationAsync(ReviewRevalidationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReviewWorkflowMutationResult> LinkRereviewAsync(ReviewRereviewLinkRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReviewWorkflowMutationResult> RequireHumanDecisionAsync(ReviewHumanDecisionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeApprovalService(IReadOnlyList<HumanApprovalInboxItem> items) : IHumanApprovalService
    {
        public IReadOnlyList<HumanApprovalInboxItem> Items { get; set; } = items;
        public Task<HumanApprovalInboxReadResult> ReadInboxAsync(Guid projectId, IReadOnlyDictionary<Guid, HumanApprovalEvaluationContext>? currentContexts = null, CancellationToken cancellationToken = default) => Task.FromResult(new HumanApprovalInboxReadResult(HumanApprovalHistoryReadStatus.Success, Items));
        public Task<HumanApprovalOperationResult> RequestAsync(HumanApprovalRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HumanApprovalOperationResult> EscalateAsync(Guid projectId, Guid requestId, string escalationReference, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HumanApprovalOperationResult> ApproveAsync(HumanApprovalDecisionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HumanApprovalOperationResult> RejectAsync(HumanApprovalDecisionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HumanApprovalOperationResult> WaiveAsync(HumanApprovalDecisionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HumanApprovalEvaluation> EvaluateAsync(HumanApprovalEvaluationContext context, Guid requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
