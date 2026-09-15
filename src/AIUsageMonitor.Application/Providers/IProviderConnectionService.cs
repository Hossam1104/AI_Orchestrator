using AIUsageMonitor.Domain.Providers;

namespace AIUsageMonitor.Application.Providers;

public interface IProviderConnectionService
{
    Task<ProviderConnection?> GetAsync(
        ProviderCode code,
        CancellationToken cancellationToken = default);

    Task<ProviderConnection?> GetAsync(
        Guid providerId,
        CancellationToken cancellationToken = default) =>
        Task.FromException<ProviderConnection?>(new NotSupportedException("Custom provider connections are unavailable."));

    Task<IReadOnlyList<ProviderConnection>> LoadAllAsync(
        CancellationToken cancellationToken = default);

    Task<ProviderConnection> SaveAsync(
        ProviderConnectionEdit edit,
        CancellationToken cancellationToken = default);

    Task<ProviderConnection> SaveAsync(
        Guid providerId,
        ProviderConnectionType connectionType,
        IReadOnlyDictionary<string, string?> configuration,
        string? secret = null,
        bool removeCredential = false,
        CancellationToken cancellationToken = default) =>
        SaveAsync(
            ProviderConnectionEdit.ForProvider(
                providerId,
                connectionType,
                configuration,
                secret,
                removeCredential),
            cancellationToken);

    Task<ProviderConnection?> RecordRefreshAsync(
        ProviderRefreshResult result,
        CancellationToken cancellationToken = default);
}
