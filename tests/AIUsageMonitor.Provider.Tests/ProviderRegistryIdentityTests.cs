using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Domain.Providers;
using AIUsageMonitor.Domain.Quotas;
using AIUsageMonitor.Domain.Subscriptions;
using AIUsageMonitor.Providers;

namespace AIUsageMonitor.Provider.Tests;

/// <summary>
/// Registry identity and naming facts. Both are user-visible truthfulness concerns: an unstable
/// identifier silently detaches a definition from the connection stored against it, and two
/// registrations sharing one rendered label leave the operator unable to tell them apart.
/// </summary>
public sealed class ProviderRegistryIdentityTests
{
    [Fact]
    public void TheCompatibilityDefinitionsKeepAStableIdentityAcrossCalls()
    {
        IProviderRegistry registry = new CompatibilityRegistry();

        var first = registry.GetDefinitions();
        var second = registry.GetDefinitions();

        Assert.Equal(
            first.Select(definition => definition.Id),
            second.Select(definition => definition.Id));
    }

    [Fact]
    public void TheCompatibilityDefinitionsCanBeFoundByTheIdentityTheyReported()
    {
        IProviderRegistry registry = new CompatibilityRegistry();

        var reported = registry.GetDefinitions().Single(definition => definition.BuiltInCode == ProviderCode.Claude);

        Assert.Equal(reported.Id, registry.FindDefinition(reported.Id)?.Id);
    }

    [Fact]
    public void TheCompatibilityIdentityMatchesTheCanonicalBuiltInIdentity()
    {
        IProviderRegistry registry = new CompatibilityRegistry();

        var definition = registry.GetDefinitions().Single(candidate => candidate.BuiltInCode == ProviderCode.Codex);

        Assert.Equal(BuiltInProviderIdentity.ForProvider(ProviderCode.Codex), definition.Id);
    }

    [Fact]
    public async Task ADisplayLabelCollidingWithAnExistingRenderedNameIsRejected()
    {
        var registry = new ProviderRegistry([], new MemoryDefinitionRepository());
        await registry.SaveCustomAsync(CreateEdit("Research API", displayLabel: null));

        var collision = await Assert.ThrowsAsync<ArgumentException>(() =>
            registry.SaveCustomAsync(CreateEdit("Analytics API", displayLabel: "Research API")));

        Assert.Contains("already registered", collision.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ADisplayLabelCollidingWithAnotherLabelIsRejected()
    {
        var registry = new ProviderRegistry([], new MemoryDefinitionRepository());
        await registry.SaveCustomAsync(CreateEdit("Analytics API", displayLabel: "Shared label"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            registry.SaveCustomAsync(CreateEdit("Research API", displayLabel: "shared label")));
    }

    [Fact]
    public async Task ADistinctDisplayLabelIsStillAccepted()
    {
        var registry = new ProviderRegistry([], new MemoryDefinitionRepository());
        await registry.SaveCustomAsync(CreateEdit("Research API", displayLabel: null));

        var saved = await registry.SaveCustomAsync(CreateEdit("Analytics API", displayLabel: "Analytics"));

        Assert.Equal("Analytics", saved.EffectiveDisplayName);
        Assert.Equal(2, registry.GetDefinitions().Count(definition => definition.Kind == ProviderKind.Custom));
    }

    [Fact]
    public async Task RelabellingAnExistingRegistrationIsNotTreatedAsACollisionWithItself()
    {
        var registry = new ProviderRegistry([], new MemoryDefinitionRepository());
        var saved = await registry.SaveCustomAsync(CreateEdit("Research API", displayLabel: "Research"));

        var relabelled = await registry.SaveCustomAsync(
            CreateEdit("Research API", displayLabel: "Research", id: saved.Id));

        Assert.Equal(saved.Id, relabelled.Id);
    }

    private static ProviderDefinitionEdit CreateEdit(string displayName, string? displayLabel, Guid? id = null) =>
        new(
            displayName,
            displayLabel,
            ProviderAuthenticationMode.ExternalManual,
            ProviderCapacityMode.Manual,
            enabled: true,
            description: null,
            id);

    /// <summary>An implementation that relies on the contract's default definition projection.</summary>
    private sealed class CompatibilityRegistry : IProviderRegistry
    {
        private readonly IReadOnlyList<IAiUsageProvider> _providers =
        [
            new StubProvider(ProviderCode.Codex),
            new StubProvider(ProviderCode.Claude),
            new StubProvider(ProviderCode.Antigravity)
        ];

        public IReadOnlyList<IAiUsageProvider> GetAll() => _providers;

        public IAiUsageProvider? Find(ProviderCode code) =>
            _providers.FirstOrDefault(provider => provider.Code == code);
    }

    private sealed class StubProvider(ProviderCode code) : IAiUsageProvider
    {
        public ProviderCode Code { get; } = code;

        public Task<ProviderDetectionResult> DetectAsync(CancellationToken cancellationToken = default) =>
            Unsupported<ProviderDetectionResult>();

        public Task<ProviderConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken = default) =>
            Unsupported<ProviderConnectionStatus>();

        public Task<ProviderAccount?> GetAccountAsync(CancellationToken cancellationToken = default) =>
            Unsupported<ProviderAccount?>();

        public Task<Subscription?> GetSubscriptionAsync(CancellationToken cancellationToken = default) =>
            Unsupported<Subscription?>();

        public Task<IReadOnlyList<QuotaWindow>> GetQuotasAsync(CancellationToken cancellationToken = default) =>
            Unsupported<IReadOnlyList<QuotaWindow>>();

        public Task<ProviderRefreshResult> RefreshAsync(CancellationToken cancellationToken = default) =>
            Unsupported<ProviderRefreshResult>();

        // These definitions-only tests never exercise provider behaviour; a stub that quietly
        // returned empty evidence would be the fake data this product forbids.
        private static Task<T> Unsupported<T>() =>
            Task.FromException<T>(new NotSupportedException("This stub exposes only its provider code."));
    }

    private sealed class MemoryDefinitionRepository : IProviderDefinitionRepository
    {
        private readonly Dictionary<Guid, ProviderDefinition> _definitions = [];

        public Task<IReadOnlyList<ProviderDefinition>> GetCustomAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProviderDefinition>>(_definitions.Values.ToArray());

        public Task UpsertCustomAsync(ProviderDefinition definition, CancellationToken cancellationToken = default)
        {
            _definitions[definition.Id] = definition;
            return Task.CompletedTask;
        }

        public Task RemoveCustomAsync(Guid providerId, CancellationToken cancellationToken = default)
        {
            _definitions.Remove(providerId);
            return Task.CompletedTask;
        }
    }
}
