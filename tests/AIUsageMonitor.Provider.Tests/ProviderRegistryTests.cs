using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Domain.Common;
using AIUsageMonitor.Domain.Providers;
using AIUsageMonitor.Domain.Quotas;
using AIUsageMonitor.Domain.Subscriptions;
using AIUsageMonitor.Domain.Usage;
using AIUsageMonitor.Providers.Common;
using AIUsageMonitor.Providers;

namespace AIUsageMonitor.Provider.Tests;

public sealed class ProviderRegistryTests
{
    [Fact]
    public void DefaultCatalog_ContainsOnlySupportedBuiltInsInStableOrder()
    {
        var registry = CreateRegistry(new InMemoryDefinitionRepository());

        var definitions = registry.GetDefinitions();

        Assert.Equal(
            [ProviderCode.Codex, ProviderCode.Claude, ProviderCode.Antigravity],
            definitions.Select(definition => definition.BuiltInCode!.Value));
        Assert.All(definitions, definition => Assert.Equal(ProviderKind.BuiltIn, definition.Kind));
        Assert.DoesNotContain(definitions, definition => definition.BuiltInCode == ProviderCode.Kimi);
        Assert.DoesNotContain(definitions, definition => definition.BuiltInCode == ProviderCode.Copilot);
    }

    [Fact]
    public async Task CustomDefinitions_PersistWithStableIdsAndDoNotBecomeAutomaticAdapters()
    {
        var repository = new InMemoryDefinitionRepository();
        var registry = CreateRegistry(repository);

        var first = await registry.SaveCustomAsync(new ProviderDefinitionEdit(
            "  Future   API  ",
            "Research endpoint",
            ProviderAuthenticationMode.ApiKey,
            ProviderCapacityMode.Unavailable,
            enabled: true,
            description: "Manual registration."));
        var second = await registry.SaveCustomAsync(new ProviderDefinitionEdit(
            "Local Model",
            null,
            ProviderAuthenticationMode.ExternalManual,
            ProviderCapacityMode.Manual,
            enabled: false));

        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.Equal(ProviderKind.Custom, first.Kind);
        Assert.Equal("Future API", first.DisplayName);
        Assert.Equal("Research endpoint", first.DisplayLabel);
        Assert.False(second.Enabled);
        Assert.Equal(5, registry.GetDefinitions().Count);
        Assert.Equal(first.Id, Assert.Single(await repository.GetCustomAsync(), definition => definition.Id == first.Id).Id);
        Assert.Equal(first, registry.FindDefinition(first.Id));
        Assert.Equal(ProviderCapacityMode.Unavailable, first.CapacityMode);
        Assert.False(first.HasCapability(ProviderCapabilities.SupportsCapacityRefresh));
    }

    [Fact]
    public async Task CustomDefinitions_RejectDuplicateNamesAndBuiltInRemoval()
    {
        var registry = CreateRegistry(new InMemoryDefinitionRepository());
        var custom = await registry.SaveCustomAsync(new ProviderDefinitionEdit(
            "Provider A",
            null,
            ProviderAuthenticationMode.ExternalManual,
            ProviderCapacityMode.Manual,
            enabled: true));

        await Assert.ThrowsAsync<ArgumentException>(() => registry.SaveCustomAsync(new ProviderDefinitionEdit(
            " provider   a ",
            null,
            ProviderAuthenticationMode.ExternalManual,
            ProviderCapacityMode.Manual,
            enabled: true)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.RemoveCustomAsync(
            registry.GetDefinitions().Single(definition => definition.BuiltInCode == ProviderCode.Codex).Id));

        await registry.RemoveCustomAsync(custom.Id);
        Assert.DoesNotContain(registry.GetDefinitions(), definition => definition.Id == custom.Id);
    }

    [Fact]
    public async Task Initialize_IgnoresLegacyBuiltInCodesButRetainsTheirPersistedRecords()
    {
        var repository = new InMemoryDefinitionRepository();
        await repository.UpsertCustomAsync(ProviderDefinition.Custom(
            Guid.Parse("f73e6d44-d3ff-4d5e-8eb5-c568bcd3a1a1"),
            "Legacy Copilot record",
            null,
            ProviderAuthenticationMode.ApiKey,
            ProviderCapacityMode.Unavailable,
            enabled: true,
            sortOrder: 20,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));
        var registry = CreateRegistry(repository);

        await registry.InitializeAsync();

        Assert.Contains(registry.GetDefinitions(), definition => definition.DisplayName == "Legacy Copilot record");
        Assert.DoesNotContain(registry.GetDefinitions(), definition => definition.BuiltInCode == ProviderCode.Copilot);
        Assert.Single(await repository.GetCustomAsync());
    }

    private static ProviderRegistry CreateRegistry(InMemoryDefinitionRepository repository) => new(
        [
            new StubProvider(ProviderCode.Antigravity),
            new StubProvider(ProviderCode.Claude),
            new StubProvider(ProviderCode.Codex),
            new StubProvider(ProviderCode.Kimi),
            new StubProvider(ProviderCode.Copilot)
        ],
        repository);

    private sealed class InMemoryDefinitionRepository : IProviderDefinitionRepository
    {
        private readonly List<ProviderDefinition> _definitions = [];

        public Task<IReadOnlyList<ProviderDefinition>> GetCustomAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProviderDefinition>>(_definitions.ToArray());

        public Task UpsertCustomAsync(ProviderDefinition definition, CancellationToken cancellationToken = default)
        {
            var index = _definitions.FindIndex(item => item.Id == definition.Id);
            if (index >= 0)
            {
                _definitions[index] = definition;
            }
            else
            {
                _definitions.Add(definition);
            }

            return Task.CompletedTask;
        }

        public Task RemoveCustomAsync(Guid providerId, CancellationToken cancellationToken = default)
        {
            _definitions.RemoveAll(definition => definition.Id == providerId);
            return Task.CompletedTask;
        }
    }

    private sealed class StubProvider : IAiUsageProvider
    {
        public StubProvider(ProviderCode code) => Code = code;

        public ProviderCode Code { get; }

        public Task<ProviderDetectionResult> DetectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderDetectionResult(Code, false, "test", DateTimeOffset.UtcNow));

        public Task<ProviderConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ProviderConnectionStatus.NotConfigured);

        public Task<ProviderAccount?> GetAccountAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ProviderAccount?>(null);

        public Task<Subscription?> GetSubscriptionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Subscription?>(null);

        public Task<IReadOnlyList<QuotaWindow>> GetQuotasAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<QuotaWindow>>([]);

        public Task<ProviderRefreshResult> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ProviderRefreshResult.Unsupported(Code, DateTimeOffset.UtcNow));
    }
}
