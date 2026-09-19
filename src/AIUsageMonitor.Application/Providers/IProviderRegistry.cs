using AIUsageMonitor.Domain.Providers;

namespace AIUsageMonitor.Application.Providers;

/// <summary>
/// The set of provider integrations known to the application, independent of whether each
/// one is currently detected, connected, or enabled.
/// </summary>
public interface IProviderRegistry
{
    IReadOnlyList<IAiUsageProvider> GetAll();

    IAiUsageProvider? Find(ProviderCode code);

    IReadOnlyList<ProviderDefinition> GetDefinitions() =>
        GetAll()
            .Select(CreateCompatibilityDefinition)
            .ToArray();

    ProviderDefinition? FindDefinition(Guid providerId) =>
        GetDefinitions().FirstOrDefault(definition => definition.Id == providerId);

    Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    Task<ProviderDefinition> SaveCustomAsync(
        ProviderDefinitionEdit edit,
        CancellationToken cancellationToken = default) =>
        Task.FromException<ProviderDefinition>(new NotSupportedException("Custom provider registration is unavailable."));

    Task RemoveCustomAsync(Guid providerId, CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException("Custom provider registration is unavailable."));

    private static ProviderDefinition CreateCompatibilityDefinition(IAiUsageProvider provider) =>
        ProviderPolicy.CreateBuiltInDefinition(provider);
}
