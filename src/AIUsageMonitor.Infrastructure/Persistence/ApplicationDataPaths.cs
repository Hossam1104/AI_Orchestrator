namespace AIUsageMonitor.Infrastructure.Persistence;

/// <summary>
/// Resolves the per-user storage layout. Runtime data never lives beside the executable or
/// under Program Files, so a self-contained install can be updated without touching user data.
/// </summary>
public sealed class ApplicationDataPaths
{
    public const string ApplicationDirectoryName = "AIUsageMonitor";

    public ApplicationDataPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        RootDirectory = Path.GetFullPath(rootDirectory);
        HistoryDirectory = Path.Combine(RootDirectory, "history");
        AlertsDirectory = Path.Combine(RootDirectory, "alerts");
        SyncDirectory = Path.Combine(RootDirectory, "sync");
        LogsDirectory = Path.Combine(RootDirectory, "logs");
        ProjectsDirectory = Path.Combine(RootDirectory, "projects");
        WorkspacesDirectory = Path.Combine(RootDirectory, "workspaces");
        WorkspaceLocksDirectory = Path.Combine(RootDirectory, "locks", "workspaces");
    }

    public string RootDirectory { get; }

    public string HistoryDirectory { get; }

    public string AlertsDirectory { get; }

    public string SyncDirectory { get; }

    public string LogsDirectory { get; }

    /// <summary>
    /// Root directory for GUID-scoped project state. The legacy root and all existing stores are
    /// intentionally preserved; this is an additive APO-27 layout extension.
    /// </summary>
    public string ProjectsDirectory { get; }

    /// <summary>Managed isolated workspace roots. Callers supply GUIDs only.</summary>
    public string WorkspacesDirectory { get; }

    internal string WorkspaceLocksDirectory { get; }

    public string SettingsFile => Path.Combine(RootDirectory, "settings.json");

    public string ProvidersFile => Path.Combine(RootDirectory, "providers.json");

    public string ConnectionsFile => Path.Combine(RootDirectory, "connections.json");

    public string SubscriptionsFile => Path.Combine(RootDirectory, "subscriptions.json");

    public string QuotaDefinitionsFile => Path.Combine(RootDirectory, "quota-definitions.json");

    public string AlertRulesFile => Path.Combine(RootDirectory, "alert-rules.json");

    public string ProjectsFile => Path.Combine(RootDirectory, "projects.json");

    public string AgentsFile => Path.Combine(RootDirectory, "agents.json");

    public string RoutingPolicyFile => Path.Combine(RootDirectory, "routing-policy.json");

    public static ApplicationDataPaths CreateDefault()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException(
                "The Windows LocalApplicationData location is unavailable; persistent storage cannot be resolved safely.");
        }

        var root = Path.Combine(localApplicationData, ApplicationDirectoryName);
        return new ApplicationDataPaths(root);
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(HistoryDirectory);
        Directory.CreateDirectory(AlertsDirectory);
        Directory.CreateDirectory(SyncDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ProjectsDirectory);
        Directory.CreateDirectory(WorkspacesDirectory);
        Directory.CreateDirectory(WorkspaceLocksDirectory);
    }

    public Task EnsureDirectoriesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureDirectories();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves the complete project-scoped layout without creating it. The only project path
    /// component accepted is a canonical GUID, so a caller cannot inject a relative path.
    /// </summary>
    public ProjectDataPaths GetProjectPaths(Guid projectId)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project id cannot be empty.", nameof(projectId));
        }

        var projectDirectory = Path.Combine(ProjectsDirectory, projectId.ToString("D"));
        return new ProjectDataPaths(projectId, projectDirectory);
    }

    public string GetProjectDirectory(Guid projectId) => GetProjectPaths(projectId).RootDirectory;

    public string GetProjectOrchestrationDirectory(Guid projectId) =>
        GetProjectPaths(projectId).OrchestrationDirectory;

    /// <summary>Project-isolated append-only human approval lifecycle events.</summary>
    public string GetProjectApprovalsDirectory(Guid projectId) =>
        GetProjectPaths(projectId).ApprovalsDirectory;

    public string GetProjectRoutingPolicyFile(Guid projectId) => GetProjectPaths(projectId).RoutingPolicyFile;

    public string GetProjectAgentOverridesFile(Guid projectId) =>
        GetProjectPaths(projectId).AgentOverridesFile;

    public string GetProjectContextReferenceFile(Guid projectId) =>
        GetProjectPaths(projectId).ContextReferenceFile;

    public string GetProjectWorkGraphsDirectory(Guid projectId) =>
        GetProjectPaths(projectId).WorkGraphsDirectory;

    public string GetProjectHandoffsDirectory(Guid projectId) =>
        GetProjectPaths(projectId).HandoffsDirectory;

    public string GetProjectContinuationDirectory(Guid projectId) =>
        GetProjectPaths(projectId).ContinuationDirectory;

    public string GetProjectContinuationHeadSlotAFile(Guid projectId) =>
        Path.Combine(GetProjectContinuationDirectory(projectId), "head-a.json");

    public string GetProjectContinuationHeadSlotBFile(Guid projectId) =>
        Path.Combine(GetProjectContinuationDirectory(projectId), "head-b.json");

    public string GetProjectRecoveryCheckpointsDirectory(Guid projectId) =>
        GetProjectPaths(projectId).RecoveryCheckpointsDirectory;

    public string GetProjectRoutingDecisionsDirectory(Guid projectId) =>
        GetProjectPaths(projectId).RoutingDecisionsDirectory;

    public string GetRoutingDecisionDirectory(Guid projectId, Guid decisionId)
    {
        if (decisionId == Guid.Empty)
        {
            throw new ArgumentException("Decision id cannot be empty.", nameof(decisionId));
        }

        return Path.Combine(GetProjectRoutingDecisionsDirectory(projectId), decisionId.ToString("D"));
    }

    public string GetRoutingDecisionFile(Guid projectId, Guid decisionId) =>
        Path.Combine(GetRoutingDecisionDirectory(projectId, decisionId), "decision.json");

    public string GetRecoveryCheckpointDirectory(Guid projectId, Guid checkpointId)
    {
        if (checkpointId == Guid.Empty)
        {
            throw new ArgumentException("Checkpoint id cannot be empty.", nameof(checkpointId));
        }

        return Path.Combine(GetProjectRecoveryCheckpointsDirectory(projectId), checkpointId.ToString("D"));
    }

    public string GetRecoveryCheckpointFile(Guid projectId, Guid checkpointId) =>
        Path.Combine(GetRecoveryCheckpointDirectory(projectId, checkpointId), "checkpoint.json");

    public string GetHandoffPackageDirectory(Guid projectId, Guid packageId)
    {
        if (packageId == Guid.Empty)
        {
            throw new ArgumentException("Package id cannot be empty.", nameof(packageId));
        }

        return Path.Combine(GetProjectHandoffsDirectory(projectId), packageId.ToString("D"));
    }

    public string GetHandoffPackageFile(Guid projectId, Guid packageId) =>
        Path.Combine(GetHandoffPackageDirectory(projectId, packageId), "package.json");

    public string GetWorkGraphDirectory(Guid projectId, Guid graphId)
    {
        if (graphId == Guid.Empty)
        {
            throw new ArgumentException("Graph id cannot be empty.", nameof(graphId));
        }

        return Path.Combine(GetProjectWorkGraphsDirectory(projectId), graphId.ToString("D"));
    }

    public string GetWorkGraphFile(Guid projectId, Guid graphId) =>
        Path.Combine(GetWorkGraphDirectory(projectId, graphId), "graph.json");

    public string GetWorkGraphCompletionEvidenceDirectory(Guid projectId, Guid graphId) =>
        Path.Combine(GetWorkGraphDirectory(projectId, graphId), "completion-evidence");

    public string GetWorkGraphCompletionEvidenceFile(Guid projectId, Guid graphId, Guid nodeId)
    {
        if (nodeId == Guid.Empty)
        {
            throw new ArgumentException("Node id cannot be empty.", nameof(nodeId));
        }

        return Path.Combine(
            GetWorkGraphCompletionEvidenceDirectory(projectId, graphId),
            $"node-{nodeId:D}.json");
    }

    public string GetProjectContractsDirectory(Guid projectId) =>
        GetProjectPaths(projectId).ContractsDirectory;

    public string GetPlanningExecutionContractDirectory(Guid projectId, Guid contractId)
    {
        if (contractId == Guid.Empty)
        {
            throw new ArgumentException("Contract id cannot be empty.", nameof(contractId));
        }

        return Path.Combine(GetProjectContractsDirectory(projectId), contractId.ToString("D"));
    }

    public string GetPlanningExecutionContractRevisionFile(Guid projectId, Guid contractId, int revision)
    {
        if (revision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        return Path.Combine(
            GetPlanningExecutionContractDirectory(projectId, contractId),
            $"revision-{revision:D6}.json");
    }

    public string GetProjectRunsDirectory(Guid projectId) => GetProjectPaths(projectId).RunsDirectory;

    /// <summary>Immutable create-once authorities for one bounded execution run.</summary>
    public string GetProjectExecutionRunAuthoritiesDirectory(Guid projectId) =>
        Path.Combine(GetProjectRunsDirectory(projectId), "authorities");

    public string GetExecutionRunAuthorityDirectory(Guid projectId, Guid runId)
    {
        ValidateGuid(runId, nameof(runId));
        return Path.Combine(GetProjectExecutionRunAuthoritiesDirectory(projectId), runId.ToString("D"));
    }

    public string GetExecutionRunAuthorityFile(Guid projectId, Guid runId) =>
        Path.Combine(GetExecutionRunAuthorityDirectory(projectId, runId), "authority.json");

    public string GetProjectEvidenceDirectory(Guid projectId) => GetProjectPaths(projectId).EvidenceDirectory;

    public string GetProjectValidationDirectory(Guid projectId) =>
        Path.Combine(GetProjectEvidenceDirectory(projectId), "validation");

    public string GetProjectValidationPlansDirectory(Guid projectId) =>
        Path.Combine(GetProjectValidationDirectory(projectId), "plans");

    public string GetValidationPlanDirectory(Guid projectId, Guid planId, int revision)
    {
        ValidateGuid(planId, nameof(planId));
        if (revision <= 0) throw new ArgumentOutOfRangeException(nameof(revision));
        return Path.Combine(GetProjectValidationPlansDirectory(projectId), planId.ToString("D"), "revisions", revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public string GetValidationPlanFile(Guid projectId, Guid planId, int revision) =>
        Path.Combine(GetValidationPlanDirectory(projectId, planId, revision), "plan.json");

    public string GetProjectValidationEvidenceDirectory(Guid projectId) =>
        Path.Combine(GetProjectValidationDirectory(projectId), "evidence");

    public string GetValidationEvidenceRevisionDirectory(Guid projectId, Guid planId, int revision)
    {
        ValidateGuid(planId, nameof(planId));
        if (revision <= 0) throw new ArgumentOutOfRangeException(nameof(revision));
        return Path.Combine(GetProjectValidationEvidenceDirectory(projectId), planId.ToString("D"), "revisions", revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public string GetValidationEvidenceDirectory(Guid projectId, Guid planId, int revision, Guid evidenceId)
    {
        ValidateGuid(planId, nameof(planId));
        if (revision <= 0) throw new ArgumentOutOfRangeException(nameof(revision));
        ValidateGuid(evidenceId, nameof(evidenceId));
        return Path.Combine(GetValidationEvidenceRevisionDirectory(projectId, planId, revision), evidenceId.ToString("D"));
    }

    public string GetValidationEvidenceFile(Guid projectId, Guid planId, int revision, Guid evidenceId) =>
        Path.Combine(GetValidationEvidenceDirectory(projectId, planId, revision, evidenceId), "evidence.json");

    public string GetProjectValidationDecisionsDirectory(Guid projectId) =>
        Path.Combine(GetProjectValidationDirectory(projectId), "decisions");

    public string GetValidationDecisionDirectory(Guid projectId, Guid decisionId)
    {
        ValidateGuid(decisionId, nameof(decisionId));
        return Path.Combine(GetProjectValidationDecisionsDirectory(projectId), decisionId.ToString("D"));
    }

    public string GetValidationDecisionFile(Guid projectId, Guid decisionId) =>
        Path.Combine(GetValidationDecisionDirectory(projectId, decisionId), "decision.json");

    public string GetProjectReviewsDirectory(Guid projectId) => GetProjectPaths(projectId).ReviewsDirectory;

    public string GetProjectReviewWorkflowDirectory(Guid projectId) => GetProjectPaths(projectId).ReviewWorkflowDirectory;

    public string GetProjectActivityDirectory(Guid projectId) => GetProjectPaths(projectId).ActivityDirectory;

    public string GetProjectTrackerAuditDirectory(Guid projectId) => GetProjectPaths(projectId).TrackerAuditDirectory;

    public string GetProjectWorkspacesDirectory(Guid projectId) =>
        Path.Combine(GetProjectPaths(projectId).RootDirectory, "workspaces");

    public string GetWorkspacePreparationPlanDirectory(Guid projectId, Guid planId)
    {
        ValidateGuid(planId, nameof(planId));
        return Path.Combine(GetProjectWorkspacesDirectory(projectId), "plans", planId.ToString("D"));
    }

    public string GetWorkspacePreparationPlanFile(Guid projectId, Guid planId) =>
        Path.Combine(GetWorkspacePreparationPlanDirectory(projectId, planId), "plan.json");

    public string GetWorkspaceReceiptDirectory(Guid projectId, Guid workspaceId)
    {
        ValidateGuid(workspaceId, nameof(workspaceId));
        return Path.Combine(GetProjectWorkspacesDirectory(projectId), workspaceId.ToString("D"));
    }

    public string GetWorkspaceReceiptFile(Guid projectId, Guid workspaceId) =>
        Path.Combine(GetWorkspaceReceiptDirectory(projectId, workspaceId), "receipt.json");

    public string GetWorkspaceApprovalEvidenceDirectory(Guid projectId, Guid workspaceId, Guid approvalId)
    {
        ValidateGuid(approvalId, nameof(approvalId));
        return Path.Combine(GetWorkspaceReceiptDirectory(projectId, workspaceId), "approval-evidence", approvalId.ToString("D"));
    }

    public string GetWorkspaceApprovalEvidenceFile(Guid projectId, Guid workspaceId, Guid approvalId) =>
        Path.Combine(GetWorkspaceApprovalEvidenceDirectory(projectId, workspaceId, approvalId), "approval.json");

    public string GetWorkspaceApprovalEvidenceByPlanDirectory(Guid projectId, Guid workspaceId, Guid planId)
    {
        ValidateGuid(planId, nameof(planId));
        return Path.Combine(GetWorkspaceReceiptDirectory(projectId, workspaceId), "approval-evidence-by-plan", planId.ToString("D"));
    }

    public string GetWorkspaceApprovalEvidenceByPlanFile(Guid projectId, Guid workspaceId, Guid planId) =>
        Path.Combine(GetWorkspaceApprovalEvidenceByPlanDirectory(projectId, workspaceId, planId), "approval.json");

    public string GetManagedWorkspaceRoot(Guid projectId, Guid workspaceId) =>
        Path.Combine(WorkspacesDirectory, projectId.ToString("D"), workspaceId.ToString("D"));

    public string GetManagedWorkspaceRepositoryPath(Guid projectId, Guid workspaceId) =>
        Path.Combine(GetManagedWorkspaceRoot(projectId, workspaceId), "repo");

    internal string GetWorkspaceLockFile(string repositoryIdentity) =>
        Path.Combine(WorkspaceLocksDirectory, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(repositoryIdentity))).ToLowerInvariant() + ".lock");

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty) throw new ArgumentException("Identifier cannot be empty.", parameterName);
    }

    public void EnsureProjectDirectories(Guid projectId)
    {
        var projectPaths = GetProjectPaths(projectId);
        EnsureDirectories();
        Directory.CreateDirectory(projectPaths.RootDirectory);
        Directory.CreateDirectory(projectPaths.OrchestrationDirectory);
        Directory.CreateDirectory(projectPaths.ApprovalsDirectory);
        Directory.CreateDirectory(projectPaths.ContractsDirectory);
        Directory.CreateDirectory(projectPaths.WorkGraphsDirectory);
        Directory.CreateDirectory(projectPaths.HandoffsDirectory);
        Directory.CreateDirectory(projectPaths.ContinuationDirectory);
        Directory.CreateDirectory(projectPaths.RecoveryCheckpointsDirectory);
        Directory.CreateDirectory(projectPaths.RoutingDecisionsDirectory);
        Directory.CreateDirectory(projectPaths.RunsDirectory);
        Directory.CreateDirectory(GetProjectExecutionRunAuthoritiesDirectory(projectId));
        Directory.CreateDirectory(projectPaths.EvidenceDirectory);
        Directory.CreateDirectory(GetProjectValidationPlansDirectory(projectId));
        Directory.CreateDirectory(GetProjectValidationEvidenceDirectory(projectId));
        Directory.CreateDirectory(GetProjectValidationDecisionsDirectory(projectId));
        Directory.CreateDirectory(projectPaths.ReviewsDirectory);
        Directory.CreateDirectory(projectPaths.ReviewWorkflowDirectory);
        Directory.CreateDirectory(projectPaths.ActivityDirectory);
        Directory.CreateDirectory(projectPaths.TrackerAuditDirectory);
        Directory.CreateDirectory(projectPaths.RootDirectory);
    }

    public Task EnsureProjectDirectoriesAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureProjectDirectories(projectId);
        return Task.CompletedTask;
    }

    public string GetMonthlyPartition(string directory, DateTimeOffset timestamp) =>
        Path.Combine(directory, $"{timestamp.UtcDateTime:yyyy-MM}.jsonl");
}

/// <summary>
/// Paths beneath one registered project's GUID directory. These paths are derived, never loaded
/// from project metadata, so project records cannot redirect one project's stream to another.
/// </summary>
public sealed class ProjectDataPaths
{
    internal ProjectDataPaths(Guid projectId, string rootDirectory)
    {
        ProjectId = projectId;
        RootDirectory = rootDirectory;
        OrchestrationDirectory = Path.Combine(rootDirectory, "orchestration");
        RunsDirectory = Path.Combine(OrchestrationDirectory, "runs");
        EvidenceDirectory = Path.Combine(OrchestrationDirectory, "evidence");
        ReviewsDirectory = Path.Combine(OrchestrationDirectory, "reviews");
        ReviewWorkflowDirectory = Path.Combine(OrchestrationDirectory, "review-workflow");
        ApprovalsDirectory = Path.Combine(OrchestrationDirectory, "approvals");
        ActivityDirectory = Path.Combine(OrchestrationDirectory, "activity");
        TrackerAuditDirectory = Path.Combine(OrchestrationDirectory, "tracker-audit");
        ContractsDirectory = Path.Combine(rootDirectory, "contracts");
        WorkGraphsDirectory = Path.Combine(rootDirectory, "work-graphs");
        HandoffsDirectory = Path.Combine(rootDirectory, "handoffs");
        ContinuationDirectory = Path.Combine(rootDirectory, "continuation");
        RecoveryCheckpointsDirectory = Path.Combine(ContinuationDirectory, "checkpoints");
        RoutingDecisionsDirectory = Path.Combine(rootDirectory, "routing", "decisions");
        RoutingPolicyFile = Path.Combine(rootDirectory, "routing-policy.json");
        AgentOverridesFile = Path.Combine(rootDirectory, "agent-overrides.json");
        ContextReferenceFile = Path.Combine(rootDirectory, "context-reference.json");
    }

    public Guid ProjectId { get; }

    public string RootDirectory { get; }

    public string OrchestrationDirectory { get; }

    public string RunsDirectory { get; }

    public string EvidenceDirectory { get; }

    public string ReviewsDirectory { get; }

    /// <summary>Append-only typed lifecycle events for the review/remediation workflow.</summary>
    public string ReviewWorkflowDirectory { get; }

    /// <summary>Monthly JSONL for the provider-independent APO-49 approval authority.</summary>
    public string ApprovalsDirectory { get; }

    public string ActivityDirectory { get; }

    /// <summary>Project-isolated append-only evidence for bounded tracker mutations.</summary>
    public string TrackerAuditDirectory { get; }

    public string ContractsDirectory { get; }

    /// <summary>Immutable dependency-aware graph snapshots for this project.</summary>
    public string WorkGraphsDirectory { get; }

    /// <summary>Immutable planner/executor/reviewer lifecycle packages for this project.</summary>
    public string HandoffsDirectory { get; }

    /// <summary>Mutable two-slot continuation-head directory for this project.</summary>
    public string ContinuationDirectory { get; }

    /// <summary>Immutable recovery checkpoints referenced by the continuation head.</summary>
    public string RecoveryCheckpointsDirectory { get; }

    /// <summary>Immutable, GUID-scoped explainable routing decisions.</summary>
    public string RoutingDecisionsDirectory { get; }

    public string RoutingPolicyFile { get; }

    /// <summary>
    /// Project-specific agent configuration only. Global agent truth remains in the root
    /// <c>agents.json</c> document.
    /// </summary>
    public string AgentOverridesFile { get; }

    /// <summary>Single current APO-39 onboarding context document for this project boundary.</summary>
    public string ContextReferenceFile { get; }
}
