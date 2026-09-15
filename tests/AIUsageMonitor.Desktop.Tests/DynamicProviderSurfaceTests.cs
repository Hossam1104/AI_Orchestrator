using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Desktop.ViewModels;
using AIUsageMonitor.Domain.Providers;
using AIUsageMonitor.Domain.Quotas;
using AIUsageMonitor.Domain.Subscriptions;
using AIUsageMonitor.Providers;

namespace AIUsageMonitor.Desktop.Tests;

public sealed class DynamicProviderSurfaceTests
{
    [Fact]
    public async Task Cards_RenderBuiltInsAndCustomDefinitionsFromTheRegistry()
    {
        var repository = new InMemoryDefinitionRepository();
        var registry = new ProviderRegistry(
            [
                new StubProvider(ProviderCode.Codex),
                new StubProvider(ProviderCode.Claude),
                new StubProvider(ProviderCode.Antigravity)
            ],
            repository);
        await registry.SaveCustomAsync(new ProviderDefinitionEdit(
            "Custom Manual",
            null,
            ProviderAuthenticationMode.ExternalManual,
            ProviderCapacityMode.Manual,
            enabled: true));
        await registry.SaveCustomAsync(new ProviderDefinitionEdit(
            "Custom API",
            null,
            ProviderAuthenticationMode.ApiKey,
            ProviderCapacityMode.Unavailable,
            enabled: true));

        var viewModel = new AiCapacityViewModel(registry, new StubConnectionService());

        Assert.Equal(5, viewModel.Cards.Count);
        Assert.Equal(
            ["Codex", "Claude", "Antigravity", "Custom Manual", "Custom API"],
            viewModel.Cards.Select(card => card.DisplayName));
        Assert.All(viewModel.Cards.Skip(3), card => Assert.True(card.IsCustom));
        Assert.False(viewModel.Cards[3].CanRefresh);
        Assert.False(viewModel.Cards[4].CanRefresh);
        Assert.Equal(ProviderAuthenticationMode.ApiKey, viewModel.Cards[4].Definition.AuthenticationMode);
    }

    [Fact]
    public void CapabilityDrivenCardActions_KeepLocalSessionAndManualCapacityDistinct()
    {
        var definition = ProviderDefinition.BuiltIn(
            Guid.NewGuid(),
            ProviderCode.Claude,
            "Claude",
            ProviderAuthenticationMode.LocalSession,
            ProviderCapacityMode.Manual,
            ProviderCapabilities.SupportsLocalSessionDetection | ProviderCapabilities.SupportsConfiguration,
            1);
        var card = new ProviderCapacityCardViewModel(definition, new StubProvider(ProviderCode.Claude), new StubConnectionService());

        Assert.True(card.CanCheckSession);
        Assert.False(card.CanRefresh);
        Assert.Equal("Manual", card.CapacityText);
        Assert.Equal("Local session not checked", card.AuthenticationText);
    }

    [Fact]
    public async Task CustomEditor_ValidatesSafeMetadataAndSupportsAllDeclaredAuthModes()
    {
        var repository = new InMemoryDefinitionRepository();
        var registry = new ProviderRegistry(
            [new StubProvider(ProviderCode.Codex), new StubProvider(ProviderCode.Claude), new StubProvider(ProviderCode.Antigravity)],
            repository);
        var service = new StubConnectionService();
        var definition = ProviderDefinition.Custom(
            Guid.NewGuid(),
            "Custom provider",
            null,
            ProviderAuthenticationMode.ExternalManual,
            ProviderCapacityMode.Manual,
            enabled: true,
            sortOrder: 3,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        var editor = new ProviderConnectionEditorViewModel(definition, null, service, registry);

        Assert.Contains(ProviderAuthenticationMode.LocalSession, editor.AuthenticationModeOptions);
        Assert.Contains(ProviderAuthenticationMode.ApiKey, editor.AuthenticationModeOptions);
        Assert.Contains(ProviderAuthenticationMode.ExternalManual, editor.AuthenticationModeOptions);
        Assert.False(await editor.SaveAsync("not-allowed-in-manual-mode"));
        Assert.Contains("API key can only be saved", editor.ValidationMessage, StringComparison.OrdinalIgnoreCase);

        editor.AuthenticationMode = ProviderAuthenticationMode.ApiKey;
        editor.CapacityMode = ProviderCapacityMode.Unavailable;
        Assert.True(await editor.SaveAsync("synthetic-secret"));
        Assert.NotNull(service.LastEdit);
        Assert.Equal(definition.Id, service.LastEdit!.ProviderId);
    }

    private sealed class InMemoryDefinitionRepository : IProviderDefinitionRepository
    {
        private readonly List<ProviderDefinition> _items = [];

        public Task<IReadOnlyList<ProviderDefinition>> GetCustomAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProviderDefinition>>(_items.ToArray());

        public Task UpsertCustomAsync(ProviderDefinition definition, CancellationToken cancellationToken = default)
        {
            var index = _items.FindIndex(item => item.Id == definition.Id);
            if (index >= 0) _items[index] = definition;
            else _items.Add(definition);
            return Task.CompletedTask;
        }

        public Task RemoveCustomAsync(Guid providerId, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(item => item.Id == providerId);
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

    private sealed class StubConnectionService : IProviderConnectionService
    {
        public ProviderConnectionEdit? LastEdit { get; private set; }
        public Task<ProviderConnection?> GetAsync(ProviderCode code, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProviderConnection?>(null);
        public Task<ProviderConnection?> GetAsync(Guid providerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProviderConnection?>(null);
        public Task<IReadOnlyList<ProviderConnection>> LoadAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProviderConnection>>([]);
        public Task<ProviderConnection> SaveAsync(ProviderConnectionEdit edit, CancellationToken cancellationToken = default)
        {
            LastEdit = edit;
            return Task.FromResult(new ProviderConnection(
                Guid.NewGuid(),
                edit.ProviderId ?? Guid.NewGuid(),
                edit.ConnectionType,
                ProviderConnectionStatus.Updating,
                null,
                null,
                null,
                null,
                null,
                "opaque-reference",
                edit.Configuration));
        }
        public Task<ProviderConnection?> RecordRefreshAsync(ProviderRefreshResult result, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProviderConnection?>(null);
    }
}
