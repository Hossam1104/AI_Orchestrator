using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Routing;
using AIUsageMonitor.Application.Workspaces;

namespace AIUsageMonitor.Application.Orchestration;

/// <summary>Smallest typed restore-failure taxonomy for APO-70 persisted-Ready rehydration.</summary>
public enum ExecutionRehydrationStatus
{
    Restored,
    NotResumable,
    AuthorityMismatch,
    SourceUnavailable,
    SourceMoved,
    SourceDirty,
    WorkspaceUnavailable,
    ExecutorUnavailable,
    ExecutorMismatch,
    Failed
}

public sealed record ExecutionRehydrationResult(
    ExecutionRehydrationStatus Status,
    PreparedExecution? PreparedExecution = null,
    string? ErrorMessage = null)
{
    public bool Succeeded => Status == ExecutionRehydrationStatus.Restored && PreparedExecution is not null;
}

/// <summary>
/// Restores a genuinely startable <see cref="PreparedExecution"/> for a new coordinator instance
/// from durable authorities alone. It never invents a value that was not already durably
/// persisted, never re-runs planning or routing to recreate memory state, and fails closed on any
/// authority, source, workspace, or executor inconsistency instead of silently re-routing.
/// </summary>
public interface IReadyExecutionRehydrator
{
    Task<ExecutionRehydrationResult> TryRehydrateAsync(Guid projectId, CancellationToken cancellationToken = default);
}

public sealed class ReadyExecutionRehydrator : IReadyExecutionRehydrator
{
    private readonly ISmartContinueResolver _resolver;
    private readonly IProjectRepository _projects;
    private readonly IProjectRepositoryStateService _repositoryState;
    private readonly IPlanningExecutionContractRepository _contracts;
    private readonly IWorkGraphRepository _graphs;
    private readonly IHandoffPackageRepository _handoffs;
    private readonly IRoutingDecisionRepository _routing;
    private readonly IWorkspacePreparationPlanRepository _workspacePlans;
    private readonly IRecoveryCheckpointRepository _checkpoints;
    private readonly IWorkspaceRecoveryInspectionService _workspaceInspection;
    private readonly IAgentRegistryService _agents;

    public ReadyExecutionRehydrator(
        ISmartContinueResolver resolver,
        IProjectRepository projects,
        IProjectRepositoryStateService repositoryState,
        IPlanningExecutionContractRepository contracts,
        IWorkGraphRepository graphs,
        IHandoffPackageRepository handoffs,
        IRoutingDecisionRepository routing,
        IWorkspacePreparationPlanRepository workspacePlans,
        IRecoveryCheckpointRepository checkpoints,
        IWorkspaceRecoveryInspectionService workspaceInspection,
        IAgentRegistryService agents)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _repositoryState = repositoryState ?? throw new ArgumentNullException(nameof(repositoryState));
        _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
        _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
        _handoffs = handoffs ?? throw new ArgumentNullException(nameof(handoffs));
        _routing = routing ?? throw new ArgumentNullException(nameof(routing));
        _workspacePlans = workspacePlans ?? throw new ArgumentNullException(nameof(workspacePlans));
        _checkpoints = checkpoints ?? throw new ArgumentNullException(nameof(checkpoints));
        _workspaceInspection = workspaceInspection ?? throw new ArgumentNullException(nameof(workspaceInspection));
        _agents = agents ?? throw new ArgumentNullException(nameof(agents));
    }

    public async Task<ExecutionRehydrationResult> TryRehydrateAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
        {
            return new(ExecutionRehydrationStatus.NotResumable, ErrorMessage: "A registered project is required.");
        }

        try
        {
            var continuation = await _resolver.ResolveAsync(projectId, cancellationToken).ConfigureAwait(false);
            if (continuation.ResolutionState != SmartContinueResolutionState.Resumable ||
                continuation.FallbackToLastKnownGood ||
                continuation.LatestLifecycleState != RecoveryCheckpointLifecycleState.Ready ||
                continuation.NextSafeAction != RecoveryNextSafeAction.ContinueFromCheckpoint ||
                continuation.SelectedCheckpointReference is null)
            {
                return new(ExecutionRehydrationStatus.NotResumable, ErrorMessage: string.IsNullOrWhiteSpace(continuation.Explanation)
                    ? "No durable Ready checkpoint is safely resumable for this project."
                    : continuation.Explanation);
            }

            var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
            if (project is null)
            {
                return new(ExecutionRehydrationStatus.NotResumable, ErrorMessage: "Project was not found.");
            }

            var checkpointRead = await _checkpoints.GetAsync(projectId, continuation.SelectedCheckpointReference.CheckpointId, cancellationToken).ConfigureAwait(false);
            if (!checkpointRead.IsValid || checkpointRead.Checkpoint is null ||
                !SameCheckpoint(checkpointRead.Checkpoint.Reference, continuation.SelectedCheckpointReference) ||
                checkpointRead.Checkpoint.ProjectId != projectId ||
                checkpointRead.Checkpoint.LifecycleState != RecoveryCheckpointLifecycleState.Ready)
            {
                return new(ExecutionRehydrationStatus.NotResumable, ErrorMessage: "The selected checkpoint is no longer a valid, current Ready checkpoint.");
            }

            var checkpoint = checkpointRead.Checkpoint;
            if (checkpoint.WorkGraphReference is null || checkpoint.WorkGraphNodeId is null ||
                checkpoint.HandoffPackageReference is null || checkpoint.RoutingDecisionReference is null ||
                checkpoint.WorkspacePreparationPlanReference is null)
            {
                return new(ExecutionRehydrationStatus.AuthorityMismatch, ErrorMessage: "The checkpoint does not carry enough durable authority to rehydrate; a new Prepare is required.");
            }

            var contractRead = await _contracts.GetAsync(projectId, checkpoint.PlanningContractReference.ContractId, checkpoint.PlanningContractReference.Revision, cancellationToken).ConfigureAwait(false);
            if (!contractRead.IsValid || contractRead.Contract is null || contractRead.Contract.ProjectId != projectId ||
                !SameContract(contractRead.Contract.Reference, checkpoint.PlanningContractReference))
            {
                return new(ExecutionRehydrationStatus.AuthorityMismatch, ErrorMessage: "The exact planning contract bound to the checkpoint is not available.");
            }

            var contract = contractRead.Contract;

            var graphRead = await _graphs.GetAsync(projectId, checkpoint.WorkGraphReference.GraphId, cancellationToken).ConfigureAwait(false);
            if (!graphRead.IsValid || graphRead.Graph is null || graphRead.Graph.ProjectId != projectId ||
                !SameGraph(graphRead.Graph.Reference, checkpoint.WorkGraphReference))
            {
                return new(ExecutionRehydrationStatus.AuthorityMismatch, ErrorMessage: "The exact work graph bound to the checkpoint is not valid.");
            }

            var graph = graphRead.Graph;
            var node = graph.Nodes.FirstOrDefault(value => value.NodeId == checkpoint.WorkGraphNodeId.Value);
            if (node is null || !SameContract(node.ContractReference, contract.Reference))
            {
                return new(ExecutionRehydrationStatus.AuthorityMismatch, ErrorMessage: "The exact work-graph node does not bind to the checkpoint contract.");
            }

            var handoffRead = await _handoffs.GetAsync(projectId, checkpoint.HandoffPackageReference.PackageId, cancellationToken).ConfigureAwait(false);
            if (!handoffRead.IsValid || handoffRead.Package is null || handoffRead.Package.ProjectId != projectId ||
                !SameHandoff(handoffRead.Package.Reference, checkpoint.HandoffPackageReference) ||
                !SameContract(handoffRead.Package.PlanningContractReference, contract.Reference) ||
                !SameGraph(handoffRead.Package.WorkGraphReference, checkpoint.WorkGraphReference) ||
                handoffRead.Package.WorkGraphNodeId != checkpoint.WorkGraphNodeId)
            {
                return new(ExecutionRehydrationStatus.AuthorityMismatch, ErrorMessage: "The exact handoff package bound to the checkpoint is not valid.");
            }

            var handoff = handoffRead.Package;

            var routingRead = await _routing.GetAsync(projectId, checkpoint.RoutingDecisionReference.DecisionId, cancellationToken).ConfigureAwait(false);
            if (!routingRead.IsValid || routingRead.Decision is null || routingRead.Decision.ProjectId != projectId ||
                !SameRouting(routingRead.Decision.Reference, checkpoint.RoutingDecisionReference) ||
                routingRead.Decision.SelectedAgentId is null)
            {
                return new(ExecutionRehydrationStatus.AuthorityMismatch, ErrorMessage: "The exact routing decision bound to the checkpoint is not valid.");
            }

            var routing = routingRead.Decision;

            var workspacePlanRead = await _workspacePlans.GetAsync(projectId, checkpoint.WorkspacePreparationPlanReference.PlanId, cancellationToken).ConfigureAwait(false);
            if (workspacePlanRead.State != WorkspacePreparationPlanReadState.Valid || workspacePlanRead.Plan is null ||
                workspacePlanRead.Plan.ProjectId != projectId ||
                !SameWorkspacePlan(workspacePlanRead.Plan.Reference, checkpoint.WorkspacePreparationPlanReference) ||
                !SameContract(workspacePlanRead.Plan.ContractReference, contract.Reference) ||
                !SameGraph(workspacePlanRead.Plan.WorkGraphReference, checkpoint.WorkGraphReference) ||
                workspacePlanRead.Plan.WorkGraphNodeId != checkpoint.WorkGraphNodeId ||
                !SameRouting(workspacePlanRead.Plan.RoutingDecisionReference, checkpoint.RoutingDecisionReference))
            {
                return new(ExecutionRehydrationStatus.AuthorityMismatch, ErrorMessage: "The exact workspace preparation plan bound to the checkpoint is not valid.");
            }

            var workspacePlan = workspacePlanRead.Plan;

            var workspaceRecovery = await _workspaceInspection.InspectAsync(checkpoint.WorkspacePreparationPlanReference, cancellationToken).ConfigureAwait(false);
            if (workspaceRecovery.State != WorkspaceRecoveryState.PreparedAndRecorded || workspaceRecovery.Receipt is null)
            {
                return new(ExecutionRehydrationStatus.WorkspaceUnavailable, ErrorMessage: workspaceRecovery.ErrorMessage ?? $"The prepared workspace is not durably recorded as ready (state: {workspaceRecovery.State}).");
            }

            var receipt = workspaceRecovery.Receipt;
            if (receipt.ProjectId != projectId || receipt.WorkspaceId != workspacePlan.WorkspaceId ||
                !SameWorkspacePlan(receipt.PlanReference, workspacePlan.Reference) ||
                !string.Equals(receipt.WorkspacePath, workspacePlan.ProposedWorkspacePath, StringComparison.Ordinal) ||
                !string.Equals(receipt.BaseCommitSha, workspacePlan.BaseCommitSha, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(receipt.ActualHeadCommitSha, workspacePlan.BaseCommitSha, StringComparison.OrdinalIgnoreCase))
            {
                return new(ExecutionRehydrationStatus.WorkspaceUnavailable, ErrorMessage: "The prepared workspace receipt is not an exact clean authority match.");
            }

            if (contract.RepositoryTarget.Mode != PlanningRepositoryMode.LocalGit ||
                contract.RepositoryTarget.ExpectedHeadCommit is null || contract.RepositoryTarget.ExpectedBranch is null)
            {
                return new(ExecutionRehydrationStatus.SourceUnavailable, ErrorMessage: "The planning contract does not carry an exact local Git source identity.");
            }

            var source = await _repositoryState.VerifyAsync(project, cancellationToken).ConfigureAwait(false);
            if (source.Status is not (RepositoryVerificationStatus.AvailableClean or RepositoryVerificationStatus.AvailableDirty) ||
                string.IsNullOrWhiteSpace(source.HeadSha) || string.IsNullOrWhiteSpace(source.BranchName) || source.IsDetachedHead)
            {
                return new(ExecutionRehydrationStatus.SourceUnavailable, ErrorMessage: source.SafeErrorMessage ?? "Exact local repository identity is unavailable.");
            }

            if (!string.Equals(source.HeadSha, contract.RepositoryTarget.ExpectedHeadCommit, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(source.BranchName, contract.RepositoryTarget.ExpectedBranch, StringComparison.Ordinal))
            {
                return new(ExecutionRehydrationStatus.SourceMoved, ErrorMessage: "The source repository HEAD or branch no longer matches the prepared execution's exact identity; a new Prepare is required.");
            }

            if (source.IsClean != true)
            {
                return new(ExecutionRehydrationStatus.SourceDirty, ErrorMessage: "The source worktree is no longer clean at the exact prepared HEAD; a new Prepare is required.");
            }

            var plannerRoleId = checkpoint.SelectedAgentRoleReferences.FirstOrDefault(value => value.Role == AgentRole.Planner)?.AgentId;
            var executorRoleId = checkpoint.SelectedAgentRoleReferences.FirstOrDefault(value => value.Role == AgentRole.Executor)?.AgentId;
            if (plannerRoleId is null || executorRoleId is null)
            {
                return new(ExecutionRehydrationStatus.AuthorityMismatch, ErrorMessage: "The checkpoint does not carry both a planner and an executor role selection.");
            }

            if (executorRoleId.Value != routing.SelectedAgentId.Value)
            {
                return new(ExecutionRehydrationStatus.ExecutorMismatch, ErrorMessage: "The checkpoint's selected executor does not match the persisted routing decision; execution will not be silently re-routed.");
            }

            var plannerResolution = await _agents.ResolveAsync(projectId, plannerRoleId.Value, cancellationToken).ConfigureAwait(false);
            if (!plannerResolution.Found || plannerResolution.Agent is null ||
                !plannerResolution.Agent.Enabled || !plannerResolution.Agent.RoleCapabilities.Contains(AgentRole.Planner))
            {
                return new(ExecutionRehydrationStatus.ExecutorUnavailable, ErrorMessage: "The configured planner is no longer an eligible enabled agent; a new Prepare is required.");
            }

            var executorResolution = await _agents.ResolveAsync(projectId, executorRoleId.Value, cancellationToken).ConfigureAwait(false);
            if (!executorResolution.Found || executorResolution.Agent is null || executorResolution.Agent.Id != routing.SelectedAgentId.Value)
            {
                return new(ExecutionRehydrationStatus.ExecutorMismatch, ErrorMessage: "The routed executor is no longer present in the effective project registry; execution will not be silently re-routed.");
            }

            var eligibility = ValidateExecutorEligibility(executorResolution.Agent);
            if (eligibility is not null)
            {
                return new(ExecutionRehydrationStatus.ExecutorUnavailable, ErrorMessage: eligibility);
            }

            var runId = Guid.NewGuid();
            var request = new BoundedExecutionRequest(
                projectId,
                runId,
                contract.Reference,
                graph.Reference,
                checkpoint.WorkGraphNodeId.Value,
                handoff.Reference,
                routing.Reference,
                workspacePlan.Reference,
                checkpoint.Reference);

            var prepared = new PreparedExecution(
                runId,
                plannerResolution.Agent,
                executorResolution.Agent,
                contract,
                graph,
                handoff,
                workspacePlan,
                receipt,
                checkpoint,
                routing,
                request);

            return new(ExecutionRehydrationStatus.Restored, prepared);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new(ExecutionRehydrationStatus.Failed, ErrorMessage: "The persisted Ready execution could not be safely rehydrated.");
        }
    }

    private static string? ValidateExecutorEligibility(EffectiveAgentDefinition agent)
    {
        if (!agent.Enabled || agent.Availability == AgentAvailability.Disabled)
        {
            return "The routed executor is disabled.";
        }

        if (agent.Availability != AgentAvailability.Available ||
            agent.AuthenticationState == AgentAuthenticationState.AuthenticationRequired ||
            agent.EntitlementState == AgentEntitlementState.VerifiedUnavailable)
        {
            return "The routed executor is unavailable or requires authentication.";
        }

        if (agent.ConnectionMode is AgentConnectionMode.InteractiveOnly or AgentConnectionMode.Manual or AgentConnectionMode.Unsupported or AgentConnectionMode.Unknown)
        {
            return "The routed executor does not expose a supported bounded execution connection mode.";
        }

        if (!agent.RoleCapabilities.Contains(AgentRole.Executor) ||
            !agent.SupportedConnectionModes.Contains(agent.ConnectionMode) ||
            string.IsNullOrWhiteSpace(agent.Provider) ||
            string.IsNullOrWhiteSpace(agent.ModelIdentifier))
        {
            return "The routed executor is not an exact executable Executor identity.";
        }

        return null;
    }

    private static bool SameCheckpoint(RecoveryCheckpointReference a, RecoveryCheckpointReference b) =>
        a.CheckpointId == b.CheckpointId && a.SchemaVersion == b.SchemaVersion &&
        string.Equals(a.ContentHash, b.ContentHash, StringComparison.Ordinal);

    private static bool SameContract(PlanningExecutionContractReference a, PlanningExecutionContractReference b) =>
        a.ContractId == b.ContractId && a.Revision == b.Revision && a.SchemaVersion == b.SchemaVersion &&
        string.Equals(a.ContentHash, b.ContentHash, StringComparison.Ordinal);

    private static bool SameGraph(WorkGraphReference? a, WorkGraphReference? b) =>
        a is not null && b is not null && a.GraphId == b.GraphId && a.SchemaVersion == b.SchemaVersion &&
        string.Equals(a.ContentHash, b.ContentHash, StringComparison.Ordinal);

    private static bool SameHandoff(HandoffPackageReference a, HandoffPackageReference b) =>
        a.PackageId == b.PackageId && a.SchemaVersion == b.SchemaVersion &&
        string.Equals(a.ContentHash, b.ContentHash, StringComparison.Ordinal);

    private static bool SameRouting(RoutingDecisionReference? a, RoutingDecisionReference? b) =>
        a is not null && b is not null && a.DecisionId == b.DecisionId && a.SchemaVersion == b.SchemaVersion &&
        string.Equals(a.ContentHash, b.ContentHash, StringComparison.Ordinal);

    private static bool SameWorkspacePlan(WorkspacePreparationPlanReference a, WorkspacePreparationPlanReference b) =>
        a.PlanId == b.PlanId && a.SchemaVersion == b.SchemaVersion &&
        string.Equals(a.ContentHash, b.ContentHash, StringComparison.Ordinal);
}
