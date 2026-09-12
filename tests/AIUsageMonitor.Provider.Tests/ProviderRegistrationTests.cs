using AIUsageMonitor.Application.Security;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Application.RemoteEvidence;
using AIUsageMonitor.Domain.Providers;
using AIUsageMonitor.Providers;
using AIUsageMonitor.Providers.Claude;
using AIUsageMonitor.Providers.Copilot;
using AIUsageMonitor.Providers.Jira;
using AIUsageMonitor.Providers.Kimi;
using AIUsageMonitor.Providers.Remote;
using Microsoft.Extensions.DependencyInjection;

namespace AIUsageMonitor.Provider.Tests;

public sealed class ProviderRegistrationTests
{
    [Theory]
    [InlineData(CopilotProvider.HttpClientName)]
    [InlineData(ClaudeProvider.HttpClientName)]
    [InlineData(KimiProvider.HttpClientName)]
    [InlineData(JiraWorkItemTrackerAdapter.HttpClientName)]
    [InlineData(GitHubRemoteRepositoryEvidenceProvider.HttpClientName)]
    [InlineData(AzureReposRemoteRepositoryEvidenceProvider.HttpClientName)]
    public void AddProviders_NamedHttpClientsUseCanonicalUserAgent(string httpClientName)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(new TestClock());
        services.AddSingleton<ISecureCredentialStore>(new TestCredentialStore());
        services.AddProviders();

        using var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        using var client = factory.CreateClient(httpClientName);

        Assert.Equal("AI_Orchestrator/1.0", client.DefaultRequestHeaders.UserAgent.ToString());
    }

    [Fact]
    public void AddProviders_ResolvesEveryV1ProviderExactlyOnce()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(new TestClock());
        services.AddSingleton<ISecureCredentialStore>(new TestCredentialStore());
        services.AddProviders();

        using var serviceProvider = services.BuildServiceProvider();
        var registry = serviceProvider.GetRequiredService<AIUsageMonitor.Application.Providers.IProviderRegistry>();

        Assert.Equal(3, registry.GetAll().Count);
        Assert.Equal(
            new[] { ProviderCode.Codex, ProviderCode.Claude, ProviderCode.Antigravity },
            registry.GetAll().Select(provider => provider.Code));

        foreach (var code in new[] { ProviderCode.Codex, ProviderCode.Claude, ProviderCode.Antigravity })
        {
            Assert.NotNull(registry.Find(code));
        }

        Assert.Null(registry.Find(ProviderCode.Kimi));
        Assert.Null(registry.Find(ProviderCode.Copilot));
    }

    [Fact]
    public void AddProviders_ResolvesBothRemoteEvidenceAdaptersAndService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(new TestClock());
        services.AddSingleton<ISecureCredentialStore>(new TestCredentialStore());
        services.AddProviders();

        using var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetRequiredService<IRemoteRepositoryEvidenceService>());
        Assert.Equal(
            [RemoteRepositoryProvider.GitHub, RemoteRepositoryProvider.AzureRepos],
            serviceProvider.GetServices<IRemoteRepositoryEvidenceProvider>().Select(provider => provider.Provider));
    }
}
