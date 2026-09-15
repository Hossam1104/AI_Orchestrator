using System.IO;
using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.MissionControl;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.Routing;
using AIUsageMonitor.Application.Trackers;
using AIUsageMonitor.Desktop.ViewModels;
using AIUsageMonitor.Infrastructure;
using AIUsageMonitor.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace AIUsageMonitor.Desktop.Tests;

/// <summary>
/// Exercises the exact production composition root (AddInfrastructure + AddProviders +
/// AddDesktopWorkspaceServices) through the real Microsoft.Extensions.DependencyInjection
/// container, guarding against APO-36: AiCapacityViewModel's degraded-fallback constructor made
/// the implicit registration ambiguous and silently dropped App.OnStartup into the degraded shell.
/// </summary>
public sealed class ProductionCompositionTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "apo-composition-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ProductionComposition_ResolvesAiCapacityViewModel()
    {
        using var provider = BuildProvider();

        var aiCapacity = provider.GetRequiredService<AiCapacityViewModel>();

        Assert.NotNull(aiCapacity);
    }

    [Fact]
    public void ProductionComposition_AiCapacityIsNotDegraded()
    {
        using var provider = BuildProvider();

        var aiCapacity = provider.GetRequiredService<AiCapacityViewModel>();

        Assert.False(aiCapacity.IsDegraded);
        Assert.Equal(3, aiCapacity.Cards.Count);
    }

    [Fact]
    public void ProductionComposition_ResolvesMainWindowViewModel()
    {
        using var provider = BuildProvider();

        var mainWindowViewModel = provider.GetRequiredService<MainWindowViewModel>();

        Assert.NotNull(mainWindowViewModel);
    }

    [Fact]
    public void ProductionComposition_ResolvesMissionControlReadModelAndViewModel()
    {
        using var provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService<IMissionControlReadModelService>());
        var missionControl = provider.GetRequiredService<MissionControlViewModel>();
        Assert.Same(missionControl, provider.GetRequiredService<MainWindowViewModel>().MissionControl);
    }

    [Fact]
    public void MainWindowViewModel_UsesResolvedNormalAiCapacity()
    {
        using var provider = BuildProvider();

        var aiCapacity = provider.GetRequiredService<AiCapacityViewModel>();
        var mainWindowViewModel = provider.GetRequiredService<MainWindowViewModel>();

        Assert.Same(aiCapacity, mainWindowViewModel.AiCapacity);
        Assert.False(mainWindowViewModel.AiCapacity.IsDegraded);
    }

    [Fact]
    public void ProjectsViewModel_IsResolvedWithRegistryService()
    {
        using var provider = BuildProvider();

        var projects = provider.GetRequiredService<ProjectsViewModel>();

        Assert.True(projects.IsStorageAvailable);
        Assert.Same(projects, provider.GetRequiredService<MainWindowViewModel>().Projects);
    }

    [Fact]
    public void ProductionComposition_ResolvesRepositoryVerificationServices()
    {
        using var provider = BuildProvider();

        var inspector = provider.GetRequiredService<AIUsageMonitor.Application.Projects.ILocalRepositoryInspector>();
        var stateService = provider.GetRequiredService<AIUsageMonitor.Application.Projects.IProjectRepositoryStateService>();

        Assert.NotNull(inspector);
        Assert.NotNull(stateService);
    }

    [Fact]
    public void ProductionComposition_ResolvesAgentRegistryTruthServices()
    {
        using var provider = BuildProvider();

        var registry = provider.GetRequiredService<IAgentRegistryService>();
        var catalog = provider.GetRequiredService<IDefaultAgentCatalog>();

        Assert.NotNull(registry);
        Assert.Equal(6, catalog.GetDefaults().Count);
    }

    [Fact]
    public void ProductionComposition_ResolvesOnboardingAndContextServices()
    {
        using var provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService<IProjectOnboardingService>());
        Assert.NotNull(provider.GetRequiredService<IProjectContextReferenceRepository>());
        Assert.NotNull(provider.GetRequiredService<IProjectContextResolver>());
        Assert.True(provider.GetRequiredService<ProjectsViewModel>().IsStorageAvailable);
    }

    [Fact]
    public void ProductionComposition_ResolvesPlanningContractServices()
    {
        using var provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService<IPlanningExecutionContractRepository>());
        Assert.NotNull(provider.GetRequiredService<IPlanningExecutionContractService>());
    }

    [Fact]
    public void ProductionComposition_ResolvesExactPlannerExecutionAndPolicyServices()
    {
        using var provider = BuildProvider();

        Assert.Single(provider.GetServices<IPlannerAdapter>());
        Assert.Single(provider.GetServices<IExecutionAdapter>());
        Assert.NotNull(provider.GetRequiredService<IPlannerAdapterResolver>());
        Assert.NotNull(provider.GetRequiredService<IExecutableRoutingPolicyResolver>());
    }

    [Fact]
    public void ProductionComposition_ResolvesTrackerBoundaryAndAudit()
    {
        using var provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService<IWorkItemTrackerAdapterResolver>());
        Assert.NotNull(provider.GetRequiredService<ITrackerSynchronizationService>());
        Assert.NotNull(provider.GetRequiredService<ITrackerMutationAuditRepository>());
        Assert.Single(provider.GetServices<IWorkItemTrackerAdapter>());
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(_tempRoot);
        services.AddProviders();
        services.AddDesktopWorkspaceServices();
        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
