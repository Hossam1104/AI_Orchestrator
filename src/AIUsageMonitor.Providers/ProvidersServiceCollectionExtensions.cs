using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Application.Delivery;
using AIUsageMonitor.Application.RemoteEvidence;
using AIUsageMonitor.Application.Trackers;
using AIUsageMonitor.Providers.Antigravity;
using AIUsageMonitor.Providers.Claude;
using AIUsageMonitor.Providers.Codex;
using AIUsageMonitor.Providers.Common;
using AIUsageMonitor.Providers.Copilot;
using AIUsageMonitor.Providers.Kimi;
using AIUsageMonitor.Providers.Jira;
using AIUsageMonitor.Providers.Remote;
using Microsoft.Extensions.DependencyInjection;

namespace AIUsageMonitor.Providers;

public static class ProvidersServiceCollectionExtensions
{
    public static IServiceCollection AddProviders(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IExecutableLocator, SystemExecutableLocator>();
        services.AddSingleton<CopilotOptions>();
        services.AddSingleton<AnthropicOptions>();
        services.AddSingleton<KimiOptions>();
        services.AddSingleton<IProviderRuntimeSettingsAccessor>(provider =>
            new ProviderRuntimeSettingsAccessor(
                provider.GetRequiredService<CopilotOptions>(),
                provider.GetRequiredService<AnthropicOptions>(),
                provider.GetRequiredService<KimiOptions>()));
        services.AddSingleton<IProviderRuntimeSettingsUpdater>(provider =>
            provider.GetRequiredService<IProviderRuntimeSettingsAccessor>());
        services.AddSingleton<IProviderIdentityCatalog, ProviderIdentityCatalog>();

        services.AddHttpClient(CopilotProvider.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.github.com/", UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AI_Orchestrator/1.0");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
        });

        services.AddHttpClient(ClaudeProvider.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.anthropic.com/", UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AI_Orchestrator/1.0");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });

        services.AddHttpClient(KimiProvider.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AI_Orchestrator/1.0");
        });

        services.AddHttpClient(JiraWorkItemTrackerAdapter.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AI_Orchestrator/1.0");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });

        services.AddHttpClient(GitHubRemoteRepositoryEvidenceProvider.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.github.com/", UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AI_Orchestrator/1.0");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });

        services.AddHttpClient(AzureReposRemoteRepositoryEvidenceProvider.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://dev.azure.com/", UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AI_Orchestrator/1.0");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });

        services.AddSingleton<CopilotProvider>(provider => new CopilotProvider(
            provider.GetRequiredService<AIUsageMonitor.Application.Time.IClock>(),
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<AIUsageMonitor.Application.Security.ISecureCredentialStore>(),
            provider.GetRequiredService<IProviderRuntimeSettingsAccessor>()));
        services.AddSingleton<ClaudeProvider>(provider => new ClaudeProvider(
            provider.GetRequiredService<AIUsageMonitor.Application.Time.IClock>(),
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<AIUsageMonitor.Application.Security.ISecureCredentialStore>(),
            provider.GetRequiredService<IExecutableLocator>(),
            provider.GetRequiredService<IProviderRuntimeSettingsAccessor>()));
        services.AddSingleton<KimiProvider>(provider => new KimiProvider(
            provider.GetRequiredService<AIUsageMonitor.Application.Time.IClock>(),
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<AIUsageMonitor.Application.Security.ISecureCredentialStore>(),
            provider.GetRequiredService<IExecutableLocator>(),
            provider.GetRequiredService<IProviderRuntimeSettingsAccessor>()));
        services.AddSingleton<CodexProvider>();
        services.AddSingleton<AntigravityProvider>();
        services.AddSingleton<JiraWorkItemTrackerAdapter>();
        services.AddSingleton<IWorkItemTrackerAdapter>(provider => provider.GetRequiredService<JiraWorkItemTrackerAdapter>());
        services.AddSingleton<GitHubRemoteRepositoryEvidenceProvider>();
        services.AddSingleton<AzureReposRemoteRepositoryEvidenceProvider>();
        services.AddSingleton<IRemoteRepositoryEvidenceProvider>(provider =>
            provider.GetRequiredService<GitHubRemoteRepositoryEvidenceProvider>());
        services.AddSingleton<IRemoteRepositoryEvidenceProvider>(provider =>
            provider.GetRequiredService<AzureReposRemoteRepositoryEvidenceProvider>());
        services.AddSingleton<IRemoteRepositoryEvidenceService, RemoteRepositoryEvidenceService>();
        services.AddSingleton<GitHubRemoteSourceControlDeliveryAdapter>();
        services.AddSingleton<AzureReposRemoteSourceControlDeliveryAdapter>();
        services.AddSingleton<IRemoteSourceControlDeliveryAdapter>(provider =>
            provider.GetRequiredService<GitHubRemoteSourceControlDeliveryAdapter>());
        services.AddSingleton<IRemoteSourceControlDeliveryAdapter>(provider =>
            provider.GetRequiredService<AzureReposRemoteSourceControlDeliveryAdapter>());

        services.AddSingleton<IAiUsageProvider>(provider => provider.GetRequiredService<CodexProvider>());
        services.AddSingleton<IAiUsageProvider>(provider => provider.GetRequiredService<ClaudeProvider>());
        services.AddSingleton<IAiUsageProvider>(provider => provider.GetRequiredService<KimiProvider>());
        services.AddSingleton<IAiUsageProvider>(provider => provider.GetRequiredService<CopilotProvider>());
        services.AddSingleton<IAiUsageProvider>(provider => provider.GetRequiredService<AntigravityProvider>());

        services.AddSingleton<IProviderRegistry, ProviderRegistry>();
        services.AddSingleton<IProviderDiscoveryService, ProviderDiscoveryService>();
        return services;
    }
}
