using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Application.MissionControl;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AIUsageMonitor.Desktop;

/// <summary>
/// Composition-root wiring for the Desktop shell's projects and AI capacity workspace. Registered
/// separately from <c>AddInfrastructure</c>/<c>AddProviders</c> so App.OnStartup and composition
/// regression tests exercise the identical registration path.
/// </summary>
public static class DesktopServiceCollectionExtensions
{
    public static IServiceCollection AddDesktopWorkspaceServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IProjectRegistryService, ProjectRegistryService>();

        // AiCapacityViewModel exposes an additional degraded/manual constructor for the
        // no-persistence fallback shell; an implicit AddSingleton<AiCapacityViewModel>() leaves
        // Microsoft DI unable to pick a single constructor, so the normal provider-backed
        // constructor is selected explicitly here.
        services.AddSingleton(provider => new AiCapacityViewModel(
            provider.GetRequiredService<IProviderRegistry>(),
            provider.GetRequiredService<IProviderConnectionService>()));

        services.AddSingleton<ProjectsViewModel>(provider => new ProjectsViewModel(
            provider.GetRequiredService<IProjectRegistryService>(),
            provider.GetRequiredService<IProjectRepositoryStateService>(),
            provider.GetRequiredService<IProjectOnboardingService>(),
            provider.GetRequiredService<AIUsageMonitor.Application.Agents.IDefaultAgentCatalog>(),
            provider.GetRequiredService<IProjectFolderPreferenceService>()));
        services.AddSingleton<IMissionControlReadModelService, MissionControlReadModelService>();
        services.AddSingleton<MissionControlViewModel>(provider => new MissionControlViewModel(
            provider.GetRequiredService<IProjectRegistryService>(),
            provider.GetRequiredService<IMissionControlReadModelService>()));
        services.AddSingleton<ExecutionViewModel>(provider => new ExecutionViewModel(
            provider.GetRequiredService<IProjectRegistryService>(),
            provider.GetRequiredService<IExecutionCoordinator>()));
        services.AddSingleton<MainWindowViewModel>(provider => new MainWindowViewModel(
            provider.GetRequiredService<MissionControlViewModel>(),
            provider.GetRequiredService<AiCapacityViewModel>(),
            provider.GetRequiredService<ProjectsViewModel>(),
            provider.GetRequiredService<ExecutionViewModel>()));
        services.AddSingleton<MainWindow>();

        return services;
    }
}
