using AIUsageMonitor.Application.Approvals;
using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Time;

namespace AIUsageMonitor.Application.MissionControl;

/// <summary>
/// Composes only project-scoped, persisted/read-only application contracts. It deliberately does
/// not inspect Git, contact remote services, refresh providers, mutate approvals, or execute work.
/// </summary>
public sealed class MissionControlReadModelService : IMissionControlReadModelService
{
    private static readonly TimeSpan HistoryLookback = TimeSpan.FromDays(30);
    private static readonly TimeSpan RunningFreshness = TimeSpan.FromMinutes(15);

    private readonly IProjectRegistryService _projects;
    private readonly IProjectContextReferenceRepository _contexts;
    private readonly IProjectOrchestrationStore _orchestration;
    private readonly IReviewWorkflowService _reviews;
    private readonly IHumanApprovalService _approvals;
    private readonly IClock _clock;

    public MissionControlReadModelService(
        IProjectRegistryService projects,
        IProjectContextReferenceRepository contexts,
        IProjectOrchestrationStore orchestration,
        IReviewWorkflowService reviews,
        IHumanApprovalService approvals,
        IClock clock)
    {
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        _orchestration = orchestration ?? throw new ArgumentNullException(nameof(orchestration));
        _reviews = reviews ?? throw new ArgumentNullException(nameof(reviews));
        _approvals = approvals ?? throw new ArgumentNullException(nameof(approvals));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<MissionControlSnapshot> ReadAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("Project id is required.", nameof(projectId));

        var project = (await _projects.GetProjectsAsync(cancellationToken).ConfigureAwait(false))
            .SingleOrDefault(value => value.Id == projectId);
        if (project is null)
            throw new KeyNotFoundException("The selected project was not found.");

        var now = _clock.UtcNow;
        var limitations = new List<MissionControlLimitation>();

        ProjectContextReference? context = null;
        try
        {
            var contextRead = await _contexts.GetAsync(projectId, cancellationToken).ConfigureAwait(false);
            if (contextRead.State == ProjectContextReadState.Valid &&
                contextRead.Context is not null &&
                contextRead.Context.ProjectId == projectId)
            {
                context = contextRead.Context;
            }
            else
            {
                AddContextLimitation(contextRead, limitations);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            limitations.Add(new(
                "Project context",
                MissionControlLimitationKind.EvidenceUnavailable,
                "Project context could not be read.",
                "Project context persistence"));
        }

        IReadOnlyList<ExecutionRun> executions = Array.Empty<ExecutionRun>();
        try
        {
            var read = await _orchestration
                .ReadExecutionRunsAsync(projectId, now - HistoryLookback, now, cancellationToken)
                .ConfigureAwait(false);
            if (read.Status != HistoryReadStatus.Success || read.Issues.Count > 0)
            {
                limitations.Add(new(
                    "Execution",
                    MissionControlLimitationKind.EvidenceUnavailable,
                    "Execution history is incomplete or unavailable.",
                    "Project orchestration history"));
            }

            executions = read.Records
                .Where(value => value.ProjectId == projectId)
                .ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            limitations.Add(new(
                "Execution",
                MissionControlLimitationKind.EvidenceUnavailable,
                "Execution history could not be read.",
                "Project orchestration history"));
        }

        IReadOnlyList<ReviewInboxItem> reviewItems = Array.Empty<ReviewInboxItem>();
        try
        {
            var read = await _reviews.ReadInboxAsync(projectId, cancellationToken).ConfigureAwait(false);
            if (!read.IsUsable)
            {
                limitations.Add(new(
                    "Review",
                    MissionControlLimitationKind.EvidenceUnavailable,
                    read.ErrorMessage ?? "Review workflow evidence is incomplete or unavailable.",
                    "Review workflow inbox"));
            }
            else
            {
                reviewItems = read.Items
                    .Where(value => value.ProjectId == projectId)
                    .OrderByDescending(value => value.LatestTimestamp)
                    .ThenBy(value => value.RootReviewId)
                    .Take(20)
                    .ToArray();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            limitations.Add(new(
                "Review",
                MissionControlLimitationKind.EvidenceUnavailable,
                "Review workflow evidence could not be read.",
                "Review workflow inbox"));
        }

        IReadOnlyList<HumanApprovalInboxItem> approvalItems = Array.Empty<HumanApprovalInboxItem>();
        try
        {
            // V1 has no persisted current-action/delivery authority from which the complete
            // contract/target/evidence/policy tuple can be reconstructed safely. Pass an
            // explicit empty context set so HumanApprovalService remains the sole evaluator and
            // unresolved requests stay CurrentContextUnknown instead of being inferred current.
            var currentApprovalContexts = new Dictionary<Guid, HumanApprovalEvaluationContext>();
            var read = await _approvals.ReadInboxAsync(projectId, currentApprovalContexts, cancellationToken).ConfigureAwait(false);
            if (!read.IsUsable)
            {
                limitations.Add(new(
                    "Approvals",
                    MissionControlLimitationKind.EvidenceUnavailable,
                    read.ErrorMessage ?? "Human approval evidence is incomplete or unavailable.",
                    "Human approval inbox"));
            }
            else
            {
                approvalItems = read.Items
                    .Where(value => value.ProjectId == projectId)
                    .OrderByDescending(value => value.RequestedAt)
                    .Take(20)
                    .ToArray();

                if (approvalItems.Any(value => !value.CurrentContextKnown))
                {
                    limitations.Add(new(
                        "Approvals",
                        MissionControlLimitationKind.CorrelationUnavailable,
                        "Historical approval evidence exists, but the exact current execution/action context is unavailable; Mission Control cannot determine the current approval state safely.",
                        "Human approval inbox"));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            limitations.Add(new(
                "Approvals",
                MissionControlLimitationKind.EvidenceUnavailable,
                "Human approval evidence could not be read.",
                "Human approval inbox"));
        }

        var latestExecution = LatestExecution(executions);
        var latestReview = reviewItems.FirstOrDefault();
        var currentReview = SelectCurrentReview(reviewItems, latestExecution);
        var review = currentReview ?? latestReview;
        var executionState = MapExecutionState(latestExecution, now, limitations);
        var reviewCanDriveState = currentReview is not null;
        if (latestReview is not null && !reviewCanDriveState)
        {
            limitations.Add(new(
                "Review",
                MissionControlLimitationKind.CorrelationUnavailable,
                latestExecution is null
                    ? "Review evidence exists, but no authoritative current work or execution run exists to correlate it; it is shown as historical/detail evidence only."
                    : "The latest review does not expose an exact binding to the latest execution checkpoint, so it is shown as historical/detail evidence and does not replace current execution state.",
                "Review workflow and execution history",
                latestReview.LatestTimestamp));
        }

        var currentApproval = approvalItems.FirstOrDefault(IsCurrentPendingApproval);
        var staleApproval = approvalItems.FirstOrDefault(value =>
            value.CurrentContextKnown &&
            value.IsStale);
        var approvalContextUnavailable = approvalItems.Count > 0 &&
            approvalItems.All(value => !value.CurrentContextKnown);

        var reviewRequiresHumanDecision = review is not null &&
            reviewCanDriveState &&
            review.OwnerAttentionRequired &&
            review.WorkflowState == ReviewWorkflowState.HumanDecisionRequired;

        var state = ResolveState(
            project,
            latestExecution,
            executionState,
            review,
            reviewCanDriveState,
            currentApproval,
            reviewRequiresHumanDecision,
            staleApproval);

        var nextSafeAction = ResolveNextSafeAction(context, latestExecution, currentReview, currentApproval, staleApproval);
        var currentWork = BuildCurrentWork(latestExecution, executionState, currentReview, reviewCanDriveState, nextSafeAction);
        var roles = BuildRoles(context);
        var repository = BuildRepository(context);
        var tracker = BuildTracker(context);
        var validation = BuildValidation(review, reviewCanDriveState);
        var reviewSummary = BuildReview(review, reviewCanDriveState);
        var approvalSummary = BuildApproval(approvalItems, currentApproval, staleApproval);
        var runtime = BuildRuntime(latestExecution, now, limitations);

        AddRemoteAndTrackerLimitations(project, context, limitations);
        AddRuntimeLimitation(latestExecution, limitations);
        if (limitations.Count == 0 && state == MissionControlState.Unknown)
        {
            limitations.Add(new(
                "Overall state",
                MissionControlLimitationKind.NoEvidence,
                "No authoritative current execution, review, approval, or acceptance evidence is available.",
                "Mission Control read model"));
        }

        return new MissionControlSnapshot(
            project.Id,
            now,
            state,
            ResolveStateReason(state, project, latestExecution, review, currentApproval, staleApproval),
            new(project.Id, project.Name, project.Status, project.LocalPath, project.UpdatedAt),
            currentWork,
            roles,
            repository,
            tracker,
            validation,
            reviewSummary,
            approvalSummary,
            runtime,
            BuildAttentionItems(project, latestExecution, executionState, currentReview, reviewCanDriveState, currentApproval, staleApproval, approvalContextUnavailable ? approvalItems[0] : null),
            limitations);
    }

    private static MissionControlState ResolveState(
        Project project,
        ExecutionRun? latestExecution,
        MissionControlState? executionState,
        ReviewInboxItem? review,
        bool reviewCanDriveState,
        HumanApprovalInboxItem? currentApproval,
        bool reviewRequiresHumanDecision,
        HumanApprovalInboxItem? staleApproval)
    {
        if (currentApproval is not null || reviewRequiresHumanDecision)
            return MissionControlState.HumanApprovalRequired;
        if (staleApproval is not null)
            return MissionControlState.Blocked;
        if (latestExecution?.Status == ExecutionRunStatus.Failed || executionState == MissionControlState.Failed)
            return MissionControlState.Failed;
        if (project.Status == ProjectStatus.Blocked || latestExecution?.Status == ExecutionRunStatus.Blocked)
            return MissionControlState.Blocked;
        if (review is not null && reviewCanDriveState)
        {
            return review.WorkflowState == ReviewWorkflowState.HumanDecisionRequired
                ? MissionControlState.HumanApprovalRequired
                : MissionControlState.Review;
        }
        return executionState ?? MissionControlState.Unknown;
    }

    private static MissionControlState? MapExecutionState(
        ExecutionRun? execution,
        DateTimeOffset now,
        List<MissionControlLimitation> limitations)
    {
        if (execution is null)
            return null;

        if (execution.Status == ExecutionRunStatus.Running && now - execution.RecordedAt > RunningFreshness)
        {
            limitations.Add(new(
                "Execution",
                MissionControlLimitationKind.EvidenceStale,
                "The latest running execution checkpoint is stale; Mission Control will not claim that execution is currently running.",
                "Project orchestration history",
                execution.RecordedAt));
            return null;
        }

        return execution.Status switch
        {
            ExecutionRunStatus.Running => MissionControlState.Running,
            ExecutionRunStatus.Waiting or ExecutionRunStatus.Planned => MissionControlState.Waiting,
            ExecutionRunStatus.Blocked => MissionControlState.Blocked,
            ExecutionRunStatus.Failed => MissionControlState.Failed,
            ExecutionRunStatus.Review => MissionControlState.Review,
            ExecutionRunStatus.Accepted => MissionControlState.Accepted,
            ExecutionRunStatus.HumanApprovalRequired => MissionControlState.HumanApprovalRequired,
            ExecutionRunStatus.Completed => AddCompletedLimitation(limitations, execution),
            ExecutionRunStatus.Cancelled => null,
            _ => null
        };
    }

    private static MissionControlState? AddCompletedLimitation(
        List<MissionControlLimitation> limitations,
        ExecutionRun execution)
    {
        limitations.Add(new(
            "Execution",
            MissionControlLimitationKind.NoEvidence,
            "Execution completed, but completion is not acceptance evidence; the governance boundary remains explicit.",
            "Project orchestration history",
            execution.RecordedAt));
        return null;
    }

    private static ExecutionRun? LatestExecution(IReadOnlyList<ExecutionRun> executions) =>
        executions
            .GroupBy(value => value.RunId)
            .Select(group => group
                .OrderByDescending(value => value.RecordedAt)
                .ThenByDescending(value => value.RecordId)
                .First())
            .OrderByDescending(value => value.RecordedAt)
            .ThenByDescending(value => value.RecordId)
            .FirstOrDefault();

    private static ReviewInboxItem? SelectCurrentReview(
        IReadOnlyList<ReviewInboxItem> reviews,
        ExecutionRun? latestExecution) =>
        latestExecution is null
            ? null
            : reviews
                .Where(value => value.BoundRunId == latestExecution.RunId)
                .OrderByDescending(value => value.LatestTimestamp)
                .ThenBy(value => value.RootReviewId)
                .FirstOrDefault();

    private static bool IsCurrentExecution(ExecutionRun? execution, MissionControlState? executionState) =>
        executionState is not null &&
        execution?.Status is ExecutionRunStatus.Planned or ExecutionRunStatus.Waiting or
            ExecutionRunStatus.Running or ExecutionRunStatus.Blocked or ExecutionRunStatus.Review or
            ExecutionRunStatus.HumanApprovalRequired;

    private static MissionControlCurrentWorkSummary BuildCurrentWork(
        ExecutionRun? execution,
        MissionControlState? executionState,
        ReviewInboxItem? currentReview,
        bool reviewCanDriveState,
        string nextSafeAction)
    {
        var executionIsCurrent = IsCurrentExecution(execution, executionState);
        var reviewIsCurrent = reviewCanDriveState && currentReview is not null;
        var hasWork = (executionIsCurrent || reviewIsCurrent) &&
            (execution?.WorkItemReference is not null || execution?.TaskTitle is not null);
        var state = reviewIsCurrent
            ? currentReview!.WorkflowState == ReviewWorkflowState.HumanDecisionRequired
                ? MissionControlState.HumanApprovalRequired
                : MissionControlState.Review
            : executionIsCurrent
                ? executionState ?? MissionControlState.Unknown
                : MissionControlState.Unknown;
        var currentStep = reviewIsCurrent
            ? $"Review workflow: {currentReview!.WorkflowState}"
            : executionIsCurrent
                ? execution!.TaskTitle ?? execution.WorkItemReference ?? "Current step unavailable"
                : "No authoritative current-step evidence";
        return new(
            hasWork,
            hasWork ? execution!.WorkItemReference ?? "Current work reference unavailable" : "No authoritative current work evidence",
            hasWork ? execution!.TaskTitle ?? "Current work title unavailable" : "No current work selected",
            currentStep,
            state,
            reviewIsCurrent ? currentReview!.LatestTimestamp : executionIsCurrent ? execution!.RecordedAt : null,
            hasWork ? execution!.RunId : null,
            nextSafeAction);
    }

    private static MissionControlRoleSummary BuildRoles(ProjectContextReference? context)
    {
        if (context is null)
            return new([], "Role assignments unavailable");

        var assignments = context.ModelRoleReferences
            .SelectMany(reference => reference.Roles.Select(role => new MissionControlRoleAssignment(
                role.ToString(),
                reference.DisplayName,
                reference.Enabled,
                reference.Availability,
                reference.Authentication,
                reference.Entitlement)))
            .OrderBy(value => value.Role, StringComparer.Ordinal)
            .ThenBy(value => value.DisplayName, StringComparer.Ordinal)
            .Take(16)
            .ToArray();
        return new(assignments, assignments.Length == 0 ? "No roles assigned" : $"{assignments.Length} configured role assignment(s)");
    }

    private static MissionControlRepositorySummary BuildRepository(ProjectContextReference? context)
    {
        if (context is null)
        {
            return new(
                RepositorySelectionState.Skipped,
                RepositoryVerificationStatus.NotInspected,
                "Repository context unavailable",
                "Not available",
                "Not available",
                "Not available",
                "Not available",
                "Not available",
                null);
        }

        var repository = context.Repository;
        var statusText = repository.Selection == RepositorySelectionState.Skipped
            ? "Repository integration skipped"
            : repository.VerificationStatus switch
            {
                RepositoryVerificationStatus.AvailableClean => "Persisted repository evidence: clean",
                RepositoryVerificationStatus.AvailableDirty => "Persisted repository evidence: changes present",
                RepositoryVerificationStatus.NotInspected => "Repository not inspected",
                _ => repository.VerificationStatus.ToString()
            };
        var remotes = repository.ConfiguredRemotes.Count == 0
            ? "No configured local remote evidence"
            : string.Join(", ", repository.ConfiguredRemotes.Select(value => value.Name));
        return new(
            repository.Selection,
            repository.VerificationStatus,
            statusText,
            repository.IsDetachedHead ? "Detached HEAD" : repository.BranchName ?? "Not available",
            repository.RepositoryRoot ?? "Not available",
            "Not available",
            "Not available",
            remotes,
            repository.CapturedAt);
    }

    private static MissionControlTrackerSummary BuildTracker(ProjectContextReference? context)
    {
        if (context is null)
            return new(TrackerReferenceState.NotConfigured, "Not available", "Not available", "Not available", "Tracker context unavailable");

        var tracker = context.Tracker;
        var status = tracker.State switch
        {
            TrackerReferenceState.ConfiguredUnverified => "Configured / unverified",
            TrackerReferenceState.Skipped => "Skipped",
            TrackerReferenceState.NotConfigured => "Not configured",
            _ => tracker.State.ToString()
        };
        return new(tracker.State, tracker.Type ?? "Not configured", tracker.Reference ?? "Not configured", "Not available", status);
    }

    private static MissionControlValidationSummary BuildValidation(ReviewInboxItem? review, bool isCurrent)
    {
        if (review?.LatestValidationState is null)
            return new(null, "No validation-gate evidence", "Not available", review?.LatestTimestamp);

        if (!isCurrent)
        {
            return new(
                review.LatestValidationState,
                "Historical / uncorrelated review validation",
                "Historical review evidence only",
                review.LatestTimestamp);
        }

        return new(
            review.LatestValidationState,
            review.LatestValidationState.Value.ToString(),
            review.LatestValidationReference?.ToString() ?? "Not available",
            review.LatestTimestamp);
    }

    private static MissionControlReviewSummary BuildReview(ReviewInboxItem? review, bool isCurrent) => review is null
        ? new(false, "No review evidence", "Not available", "Not available", 0, 0, false, "", "Unknown", null)
        : new(
            true,
            isCurrent ? review.WorkflowState.ToString() : "Historical / uncorrelated",
            review.CurrentVerdict,
            review.CurrentSeverity,
            review.BlockingFindingCount,
            review.PendingAdjudicationCount,
            review.OwnerAttentionRequired,
            review.OwnerAttentionReason ?? "No owner attention reason recorded",
            isCurrent ? review.NextRequiredAction.ToString() : "Correlation unavailable",
            review.LatestTimestamp);

    private static MissionControlApprovalSummary BuildApproval(
        IReadOnlyList<HumanApprovalInboxItem> items,
        HumanApprovalInboxItem? currentApproval,
        HumanApprovalInboxItem? staleApproval)
    {
        var currentPendingCount = items.Count(IsCurrentPendingApproval);
        if (currentApproval is not null)
        {
            return new(
                true,
                currentPendingCount,
                currentApproval.EffectiveState.ToString(),
                currentApproval.SafeTargetSummary,
                currentApproval.NextRequiredAction.ToString(),
                currentApproval.RequestedAt);
        }

        if (staleApproval is not null)
        {
            return new(
                true,
                currentPendingCount,
                "Stale / blocked",
                staleApproval.SafeTargetSummary,
                staleApproval.NextRequiredAction.ToString(),
                staleApproval.RequestedAt);
        }

        if (items.Count > 0 && items.All(value => !value.CurrentContextKnown))
        {
            return new(
                true,
                0,
                "Current approval context unavailable",
                "Not available",
                FormatApprovalAction(HumanApprovalNextAction.ResolveCurrentContext),
                items[0].RequestedAt);
        }

        return items.Count == 0
            ? new(false, 0, "No current approval evidence", "Not available", "Unknown", null)
            : new(true, currentPendingCount, "No current approval requiring action", "Not available", "Unknown", items[0].RequestedAt);
    }

    private static bool IsCurrentPendingApproval(HumanApprovalInboxItem value) =>
        value.CurrentContextKnown &&
        value.OwnerAttentionRequired &&
        value.EffectiveState is HumanApprovalState.Pending or HumanApprovalState.Escalated;

    private static MissionControlRuntimeSummary BuildRuntime(
        ExecutionRun? execution,
        DateTimeOffset now,
        List<MissionControlLimitation> limitations)
    {
        if (execution is null)
            return new(false, null, "No execution evidence", "No persisted process evidence", null, null);

        var staleRunning = execution.Status == ExecutionRunStatus.Running && now - execution.RecordedAt > RunningFreshness;
        return new(
            true,
            execution.Status,
            staleRunning ? "Stale running checkpoint" : execution.Status.ToString(),
            "Process liveness is not separately persisted",
            execution.RecordedAt,
            execution.RunId);
    }

    private static string ResolveNextSafeAction(
        ProjectContextReference? context,
        ExecutionRun? execution,
        ReviewInboxItem? review,
        HumanApprovalInboxItem? approval,
        HumanApprovalInboxItem? staleApproval)
    {
        if (approval is not null)
            return FormatApprovalAction(approval.NextRequiredAction);
        if (staleApproval is not null)
            return FormatApprovalAction(staleApproval.NextRequiredAction);
        if (review?.OwnerAttentionRequired == true)
            return FormatReviewAction(review.NextRequiredAction);
        if (execution?.Status == ExecutionRunStatus.Blocked && !string.IsNullOrWhiteSpace(execution.StopReason))
            return $"Resolve blocker: {Safe(execution.StopReason)}";
        return context?.NextSafeAction switch
        {
            ProjectNextSafeAction.ReadyForPlanning => "Ready for planning",
            ProjectNextSafeAction.ReviewRepository => "Review repository context",
            ProjectNextSafeAction.ReviewProjectContext => "Review project context",
            _ => "Unknown"
        };
    }

    private static string FormatApprovalAction(HumanApprovalNextAction action) => action switch
    {
        HumanApprovalNextAction.AwaitOwnerDecision => "Await owner decision",
        HumanApprovalNextAction.CreateFreshApprovalRequest => "Create a fresh approval request",
        HumanApprovalNextAction.ResolveRejection => "Resolve the rejected approval",
        HumanApprovalNextAction.ResolveCurrentContext => "Resolve the current approval context",
        HumanApprovalNextAction.ProceedWithAuthorizedAction => "Proceed only with the authorized action",
        _ => "Request approval"
    };

    private static string FormatReviewAction(ReviewWorkflowNextAction action) => action switch
    {
        ReviewWorkflowNextAction.AdjudicateFindings => "Adjudicate review findings",
        ReviewWorkflowNextAction.RunRemediation => "Run bounded remediation",
        ReviewWorkflowNextAction.RunRevalidation => "Run revalidation",
        ReviewWorkflowNextAction.RunRereview => "Run re-review",
        ReviewWorkflowNextAction.HumanDecision => "Make the required human decision",
        ReviewWorkflowNextAction.SendToAcceptanceAuthority => "Send to acceptance authority",
        _ => "Review the current workflow"
    };

    private static string ResolveStateReason(
        MissionControlState state,
        Project project,
        ExecutionRun? execution,
        ReviewInboxItem? review,
        HumanApprovalInboxItem? approval,
        HumanApprovalInboxItem? staleApproval) => state switch
        {
            MissionControlState.HumanApprovalRequired => approval is not null
                ? "A current human approval gate requires owner attention."
                : "The current review workflow requires a human decision.",
            MissionControlState.Failed => execution?.StopReason is { } stopReason
                ? Safe(stopReason)
                : execution?.Outcome is { } outcome
                    ? Safe(outcome)
                    : "Current execution evidence records failure.",
            MissionControlState.Blocked => staleApproval is not null
                ? "The current approval evidence is stale and blocks safe progression."
                : project.Status == ProjectStatus.Blocked
                    ? "The project lifecycle is explicitly blocked."
                    : execution?.StopReason is { } stopReason
                        ? Safe(stopReason)
                        : "Current execution evidence records a blocker.",
            MissionControlState.Review => review?.OwnerAttentionReason ?? "A current review boundary requires attention.",
            MissionControlState.Running => "Current execution evidence is fresh and marked Running.",
            MissionControlState.Waiting => "Current execution evidence is explicitly waiting.",
            MissionControlState.Accepted => "Current exact execution evidence is marked Accepted.",
            _ => "No authoritative current state is available."
        };

    private static void AddContextLimitation(
        ProjectContextReadResult result,
        List<MissionControlLimitation> limitations)
    {
        var (kind, message) = result.State switch
        {
            ProjectContextReadState.Missing => (MissionControlLimitationKind.NoEvidence, "No persisted project context is available."),
            ProjectContextReadState.UnsupportedVersion => (MissionControlLimitationKind.EvidenceUnavailable, "The persisted project context uses an unsupported version."),
            ProjectContextReadState.Invalid => (MissionControlLimitationKind.EvidenceUnavailable, "The persisted project context is invalid."),
            ProjectContextReadState.Unavailable => (MissionControlLimitationKind.EvidenceUnavailable, "Project context persistence is unavailable."),
            _ => (MissionControlLimitationKind.EvidenceUnavailable, "Project context is not available for this project.")
        };
        limitations.Add(new("Project context", kind, message, "Project context persistence"));
    }

    private static void AddRemoteAndTrackerLimitations(
        Project project,
        ProjectContextReference? context,
        List<MissionControlLimitation> limitations)
    {
        if (context?.Tracker.State == TrackerReferenceState.ConfiguredUnverified ||
            (!string.IsNullOrWhiteSpace(project.TrackerType) && !string.IsNullOrWhiteSpace(project.TrackerId)))
        {
            limitations.Add(new(
                "Tracker",
                MissionControlLimitationKind.EvidenceUnavailable,
                "Tracker identity is configured, but no persisted observed tracker evidence is available. Mission Control does not perform a live tracker read.",
                "Tracker configuration"));
        }

        if (!string.IsNullOrWhiteSpace(project.RepositoryProvider) || !string.IsNullOrWhiteSpace(project.RepositoryUrl))
        {
            limitations.Add(new(
                "Remote repository",
                MissionControlLimitationKind.EvidenceUnavailable,
                "No persisted remote SCM/CI evidence source is available in V1. Mission Control does not contact the remote repository.",
                "Remote repository evidence boundary"));
        }
    }

    private static void AddRuntimeLimitation(
        ExecutionRun? execution,
        List<MissionControlLimitation> limitations)
    {
        if (execution is not null)
        {
            limitations.Add(new(
                "Runtime",
                MissionControlLimitationKind.NoEvidence,
                "Persisted execution checkpoints are available, but no separate process-liveness evidence is persisted for Mission Control.",
                "Runtime/process evidence boundary",
                execution.RecordedAt));
        }
        else
        {
            limitations.Add(new(
                "Runtime",
                MissionControlLimitationKind.NoEvidence,
                "No current runtime or process evidence is available.",
                "Runtime/process evidence boundary"));
        }
    }

    private static IReadOnlyList<MissionControlAttentionItem> BuildAttentionItems(
        Project project,
        ExecutionRun? execution,
        MissionControlState? executionState,
        ReviewInboxItem? review,
        bool reviewCanDriveState,
        HumanApprovalInboxItem? approval,
        HumanApprovalInboxItem? staleApproval,
        HumanApprovalInboxItem? approvalContextUnavailable)
    {
        var items = new List<MissionControlAttentionItem>();
        if (approval is not null)
        {
            items.Add(new(
                MissionControlAttentionSeverity.Critical,
                MissionControlState.HumanApprovalRequired,
                "Human approval required",
                approval.SafeTargetSummary,
                "Human approval inbox",
                approval.RequestedAt,
                FormatApprovalAction(approval.NextRequiredAction)));
        }
        if (staleApproval is not null)
        {
            items.Add(new(
                MissionControlAttentionSeverity.High,
                MissionControlState.Blocked,
                "Approval evidence is stale",
                "The current approval request no longer matches its authoritative context.",
                "Human approval inbox",
                staleApproval.RequestedAt,
                FormatApprovalAction(staleApproval.NextRequiredAction)));
        }
        if (approvalContextUnavailable is not null)
        {
            items.Add(new(
                MissionControlAttentionSeverity.Medium,
                MissionControlState.Unknown,
                "Approval context unavailable",
                "Historical approval evidence exists, but the current binding cannot be verified.",
                "Human approval inbox",
                approvalContextUnavailable.RequestedAt,
                FormatApprovalAction(HumanApprovalNextAction.ResolveCurrentContext)));
        }
        if (project.Status == ProjectStatus.Blocked || execution?.Status == ExecutionRunStatus.Blocked)
        {
            items.Add(new(
                MissionControlAttentionSeverity.High,
                MissionControlState.Blocked,
                "Progress is blocked",
                execution?.StopReason is { } stopReason
                    ? Safe(stopReason)
                    : "The project lifecycle is explicitly blocked.",
                execution is null ? "Project registry" : "Execution history",
                execution?.RecordedAt,
                execution?.StopReason is { Length: > 0 } reason ? $"Resolve blocker: {Safe(reason)}" : "Review the project blocker"));
        }
        if (execution?.Status == ExecutionRunStatus.Failed)
        {
            items.Add(new(
                MissionControlAttentionSeverity.High,
                MissionControlState.Failed,
                "Execution failed",
                execution.StopReason is { } stopReason
                    ? Safe(stopReason)
                    : execution.Outcome is { } outcome
                        ? Safe(outcome)
                        : "The current execution checkpoint records failure.",
                "Execution history",
                execution.RecordedAt,
                "Review the failure evidence"));
        }
        if (review is not null && reviewCanDriveState && review.OwnerAttentionRequired)
        {
            var state = review.WorkflowState == ReviewWorkflowState.HumanDecisionRequired
                ? MissionControlState.HumanApprovalRequired
                : MissionControlState.Review;
            items.Add(new(
                review.WorkflowState == ReviewWorkflowState.HumanDecisionRequired
                    ? MissionControlAttentionSeverity.Critical
                    : MissionControlAttentionSeverity.High,
                state,
                "Review workflow needs attention",
                review.OwnerAttentionReason ?? "The current review workflow requires its next action.",
                "Review workflow inbox",
                review.LatestTimestamp,
                FormatReviewAction(review.NextRequiredAction)));
        }
        if (items.Count == 0 && executionState == MissionControlState.Running)
        {
            items.Add(new(
                MissionControlAttentionSeverity.Informational,
                MissionControlState.Running,
                "Execution is running",
                "Current execution evidence is fresh and marked Running.",
                "Execution history",
                execution?.RecordedAt,
                "Monitor the current execution evidence"));
        }
        return items.Take(12).ToArray();
    }

    private static string Safe(string value) => new HandoffRedactionService().Redact(value).Value;
}
