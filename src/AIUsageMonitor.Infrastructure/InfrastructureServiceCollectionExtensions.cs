using AIUsageMonitor.Application.Alerts;
using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Approvals;
using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Quotas;
using AIUsageMonitor.Application.Routing;
using AIUsageMonitor.Application.Security;
using AIUsageMonitor.Application.Settings;
using AIUsageMonitor.Application.Subscriptions;
using AIUsageMonitor.Application.Sync;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Application.Trackers;
using AIUsageMonitor.Application.Usage;
using AIUsageMonitor.Application.Validation;
using AIUsageMonitor.Application.Workspaces;
using AIUsageMonitor.Infrastructure.Persistence;
using AIUsageMonitor.Infrastructure.Persistence.Repositories;
using AIUsageMonitor.Infrastructure.Security;
using AIUsageMonitor.Infrastructure.Execution;
using AIUsageMonitor.Infrastructure.Git;
using AIUsageMonitor.Infrastructure.Workspaces;
using AIUsageMonitor.Infrastructure.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace AIUsageMonitor.Infrastructure;

/// <summary>
/// Composition-root wiring for the local file persistence layer. Small state documents use
/// versioned JSON and append-oriented data uses monthly JSONL partitions under LocalAppData.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string? rootDirectory = null)
    {
        var paths = new ApplicationDataPaths(rootDirectory ?? ApplicationDataPaths.CreateDefault().RootDirectory);
        services.AddSingleton(paths);
        services.AddSingleton<JsonFileStore>();

        services.AddSingleton<JsonlEventStore<UsageSnapshotRecord>>();
        services.AddSingleton<JsonlEventStore<AlertEventRecord>>();
        services.AddSingleton<JsonlEventStore<SyncEventRecord>>();
        services.AddSingleton<JsonlEventStore<ExecutionRunRecord>>();
        services.AddSingleton<JsonlEventStore<EvidenceMetadataRecord>>();
        services.AddSingleton<JsonlEventStore<ReviewMetadataRecord>>();
        services.AddSingleton<JsonlEventStore<ReviewWorkflowEventRecord>>();
        services.AddSingleton<JsonlEventStore<ActivityAuditRecordFile>>();
        services.AddSingleton<JsonlEventStore<TrackerMutationAuditRecord>>();
        services.AddSingleton<JsonlEventStore<HumanApprovalEventRecord>>();

        services.AddSingleton<IUsageSnapshotRepository, JsonUsageSnapshotRepository>();
        services.AddSingleton<IProviderRepository, JsonProviderRepository>();
        services.AddSingleton<IProviderConnectionRepository, JsonProviderConnectionRepository>();
        services.AddSingleton<IProviderConnectionService, ProviderConnectionService>();
        services.AddSingleton<ISubscriptionService, JsonSubscriptionService>();
        services.AddSingleton<IQuotaDefinitionRepository, JsonQuotaDefinitionRepository>();
        services.AddSingleton<IAlertRuleRepository, JsonAlertRuleRepository>();
        services.AddSingleton<IAlertEventRepository, JsonAlertEventRepository>();
        services.AddSingleton<ISyncEventRepository, JsonSyncEventRepository>();
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<IProjectRepository, JsonProjectRepository>();
        services.AddSingleton<ILocalRepositoryInspector, GitLocalRepositoryInspector>();
        services.AddSingleton<IProjectRepositoryStateService, ProjectRepositoryStateService>();
        services.AddSingleton<IAgentRepository, JsonAgentRepository>();
        services.AddSingleton<IAgentProjectOverrideRepository, JsonAgentProjectOverrideRepository>();
        services.AddSingleton<IAgentRegistryService, AgentRegistryService>();
        services.AddSingleton<IDefaultAgentCatalog, DefaultAgentCatalog>();
        services.AddSingleton<IProjectContextReferenceRepository, JsonProjectContextReferenceRepository>();
        services.AddSingleton<IProjectContextResolver, ProjectContextResolver>();
        services.AddSingleton<IProjectOnboardingService, ProjectOnboardingService>();
        services.AddSingleton<IPlanningExecutionContractRepository, JsonPlanningExecutionContractRepository>();
        services.AddSingleton<IPlanningExecutionContractService, PlanningExecutionContractService>();
        services.AddSingleton<IWorkGraphRepository, JsonWorkGraphRepository>();
        services.AddSingleton<IWorkGraphCompletionEvidenceRepository, JsonWorkGraphCompletionEvidenceRepository>();
        services.AddSingleton<IWorkGraphService, WorkGraphService>();
        services.AddSingleton<IWorkGraphCompletionEvidenceService, WorkGraphCompletionEvidenceService>();
        services.AddSingleton<IHandoffPackageRepository, JsonHandoffPackageRepository>();
        services.AddSingleton<IHandoffRedactionService, HandoffRedactionService>();
        services.AddSingleton<IHandoffPackageService, HandoffPackageService>();
        services.AddSingleton<IRecoveryCheckpointRepository, JsonRecoveryCheckpointRepository>();
        services.AddSingleton<IContinuationHeadRepository, JsonContinuationHeadRepository>();
        services.AddSingleton<IRecoveryCheckpointService, RecoveryCheckpointService>();
        services.AddSingleton<ISmartContinueResolver, SmartContinueResolver>();
        services.AddSingleton<WorkGraphScheduler>();
        services.AddSingleton<IRoutingPolicyRepository, JsonRoutingPolicyStore>();
        services.AddSingleton<IRoutingDecisionRepository, JsonRoutingDecisionRepository>();
        services.AddSingleton<IRoutingInputAssembler, RoutingInputAssembler>();
        services.AddSingleton<IRoutingDecisionEngine, RoutingDecisionEngine>();
        services.AddSingleton<IRoutingDecisionService, RoutingDecisionService>();
        services.AddSingleton<JsonProjectOrchestrationStore>();
        services.AddSingleton<IProjectOrchestrationStore>(service => service.GetRequiredService<JsonProjectOrchestrationStore>());
        services.AddSingleton<IReviewMetadataReader>(service => service.GetRequiredService<JsonProjectOrchestrationStore>());
        services.AddSingleton<IReviewWorkflowStore, JsonReviewWorkflowStore>();
        services.AddSingleton<IReviewWorkflowService, ReviewWorkflowService>();
        services.AddSingleton<IExecutionRunAuthorityRepository, JsonExecutionRunAuthorityRepository>();
        services.AddSingleton<IExecutionAdapterResolver, ExecutionAdapterResolver>();
        services.AddSingleton<IExecutionBudgetTimeoutProvider, ExecutionBudgetTimeoutProvider>();
        services.AddSingleton<IBoundedExecutionService, BoundedExecutionService>();
        services.AddSingleton<IBoundedProcessHost, BoundedProcessHost>();
        services.AddSingleton<IManagedWorkspacePathProvider, ManagedWorkspacePathProvider>();
        services.AddSingleton<IWorkspaceRepository, GitWorkspaceRepository>();
        services.AddSingleton<IRepositoryPreparationLock, RepositoryPreparationFileLock>();
        services.AddSingleton<IWorkspacePreparationPlanRepository, JsonWorkspacePreparationPlanRepository>();
        services.AddSingleton<IWorkspacePreparationReceiptRepository, JsonWorkspacePreparationReceiptRepository>();
        services.AddSingleton<IWorkspacePreparationApprovalEvidenceRepository, JsonWorkspacePreparationApprovalEvidenceRepository>();
        services.AddSingleton<IWorkspacePreparationPlanningService, WorkspacePreparationPlanningService>();
        services.AddSingleton<IWorkspacePreparationService, WorkspacePreparationService>();
        services.AddSingleton<IWorkspaceRecoveryInspectionService>(service =>
            (WorkspacePreparationService)service.GetRequiredService<IWorkspacePreparationService>());
        services.AddSingleton<ITrackerMutationAuditRepository, JsonTrackerMutationAuditRepository>();
        services.AddSingleton<IWorkItemTrackerAdapterResolver, WorkItemTrackerAdapterResolver>();
        services.AddSingleton<ITrackerSynchronizationService, TrackerSynchronizationService>();

        services.AddSingleton<IValidationPlanRepository, JsonValidationPlanRepository>();
        services.AddSingleton<IValidationEvidenceRepository, JsonValidationEvidenceRepository>();
        services.AddSingleton<IValidationGateDecisionRepository, JsonValidationGateDecisionRepository>();
        services.AddSingleton<IValidationEvidenceCollector, DotNetValidationEvidenceCollector>();
        services.AddSingleton<IValidationEvidenceCollector, LocalRepositoryValidationEvidenceCollector>();
        services.AddSingleton<IValidationEvidenceCollector, RemoteValidationEvidenceCollector>();
        services.AddSingleton<IValidationEvidenceCollector, TrackerValidationEvidenceCollector>();
        services.AddSingleton<IValidationEvidenceCollector, SecurityValidationEvidenceCollector>();
        services.AddSingleton<IValidationEvidenceCollector, RuntimeValidationEvidenceCollector>();
        services.AddSingleton<IValidationEvidenceCollectorResolver, ValidationEvidenceCollectorResolver>();
        services.AddSingleton<IValidationEvidenceService, ValidationEvidenceService>();
        services.AddSingleton<IValidationGateService, ValidationGateService>();

        // APO-49 is deliberately a local single-owner boundary. The configured identity is an
        // opaque owner reference, never a credential or persisted secret.
        services.AddSingleton<IHumanOwnerAuthority>(new LocalSingleOwnerAuthority(Environment.UserName));
        services.AddSingleton<IHumanApprovalStore, JsonHumanApprovalStore>();
        services.AddSingleton<IHumanApprovalService, HumanApprovalService>();

        // Stateless native-call wrapper; singleton avoids re-allocating it per resolution while
        // matching the lifetime of every other adapter registered here.
        services.AddSingleton<ISecureCredentialStore, WindowsCredentialManagerStore>();

        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}
