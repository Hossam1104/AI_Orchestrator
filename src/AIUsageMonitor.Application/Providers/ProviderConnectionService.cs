using AIUsageMonitor.Application.Security;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Domain.Providers;

namespace AIUsageMonitor.Application.Providers;

/// <summary>
/// Coordinates connection-state persistence, secure credential staging, and the live provider
/// settings seam. The JSON connection write is the commit point for a credential replacement.
/// </summary>
public sealed class ProviderConnectionService : IProviderConnectionService
{
    private readonly IProviderConnectionRepository _repository;
    private readonly IProviderIdentityCatalog _identityCatalog;
    private readonly ISecureCredentialStore _credentialStore;
    private readonly IProviderRuntimeSettingsUpdater _runtimeSettings;
    private readonly IClock _clock;

    public ProviderConnectionService(
        IProviderConnectionRepository repository,
        IProviderIdentityCatalog identityCatalog,
        ISecureCredentialStore credentialStore,
        IProviderRuntimeSettingsUpdater runtimeSettings,
        IClock clock)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _identityCatalog = identityCatalog ?? throw new ArgumentNullException(nameof(identityCatalog));
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        _runtimeSettings = runtimeSettings ?? throw new ArgumentNullException(nameof(runtimeSettings));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public Task<ProviderConnection?> GetAsync(
        ProviderCode code,
        CancellationToken cancellationToken = default) =>
        _repository.GetByProviderIdAsync(_identityCatalog.GetProviderId(code), cancellationToken);

    public Task<ProviderConnection?> GetAsync(
        Guid providerId,
        CancellationToken cancellationToken = default)
    {
        if (providerId == Guid.Empty)
        {
            throw new ArgumentException("Provider id cannot be empty.", nameof(providerId));
        }

        return _repository.GetByProviderIdAsync(providerId, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderConnection>> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        var connections = new List<ProviderConnection>();
        foreach (var code in Enum.GetValues<ProviderCode>())
        {
            var connection = await GetAsync(code, cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                continue;
            }

            connections.Add(connection);
            var configuration = connection.Configuration.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
            if (!configuration.ContainsKey(ProviderConnectionConfigurationKeys.AuthenticationMode))
            {
                configuration[ProviderConnectionConfigurationKeys.AuthenticationMode] =
                    connection.ConnectionType switch
                    {
                        ProviderConnectionType.ApiKey or ProviderConnectionType.OfficialApi => ProviderAuthenticationMode.ApiKey.ToString(),
                        ProviderConnectionType.LocalSession => ProviderAuthenticationMode.LocalSession.ToString(),
                        ProviderConnectionType.ExternalManual or ProviderConnectionType.Manual => ProviderAuthenticationMode.ExternalManual.ToString(),
                        _ => ProviderAuthenticationMode.None.ToString()
                    };
            }

            _runtimeSettings.Apply(code, connection.CredentialReference, configuration);
        }

        return connections;
    }

    public async Task<ProviderConnection> SaveAsync(
        ProviderConnectionEdit edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        var providerId = edit.ProviderId ?? (edit.Code is { } code
            ? _identityCatalog.GetProviderId(code)
            : throw new ArgumentException("A provider code or provider id is required.", nameof(edit)));
        var previous = await _repository.GetByProviderIdAsync(providerId, cancellationToken)
            .ConfigureAwait(false);

        var previousCredentialReference = previous?.CredentialReference;
        var newCredentialReference = previousCredentialReference;
        var stagedCredentialReference = (string?)null;

        if (!string.IsNullOrWhiteSpace(edit.Secret))
        {
            stagedCredentialReference = CreateCredentialReference(providerId, edit.Code);
            newCredentialReference = stagedCredentialReference;
            try
            {
                await _credentialStore.StoreAsync(
                        stagedCredentialReference,
                        edit.Secret,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                await TryRemoveCredentialAsync(stagedCredentialReference).ConfigureAwait(false);
                throw;
            }
        }
        else if (edit.RemoveCredential)
        {
            newCredentialReference = null;
        }

        var connection = new ProviderConnection(
            previous?.Id ?? Guid.NewGuid(),
            providerId,
            edit.ConnectionType,
            newCredentialReference is null
                ? ProviderConnectionStatus.NotConfigured
                : ProviderConnectionStatus.Updating,
            previous?.AccountDisplayName,
            previous?.LastSuccessfulSync,
            previous?.LastAttempt,
            lastErrorCode: null,
            lastErrorMessage: null,
            newCredentialReference,
            edit.Configuration);

        try
        {
            await _repository.UpsertAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (stagedCredentialReference is not null)
            {
                await TryRemoveCredentialAsync(stagedCredentialReference).ConfigureAwait(false);
            }

            throw;
        }

        if (edit.Code is { } builtInCode)
        {
            _runtimeSettings.Apply(builtInCode, newCredentialReference, connection.Configuration);
        }

        if (stagedCredentialReference is not null &&
            previousCredentialReference is not null &&
            !string.Equals(stagedCredentialReference, previousCredentialReference, StringComparison.OrdinalIgnoreCase))
        {
            await TryRemoveCredentialAsync(previousCredentialReference).ConfigureAwait(false);
        }
        else if (edit.RemoveCredential && previousCredentialReference is not null)
        {
            await TryRemoveCredentialAsync(previousCredentialReference).ConfigureAwait(false);
        }

        return connection;
    }

    public async Task<ProviderConnection?> RecordRefreshAsync(
        ProviderRefreshResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        var providerId = _identityCatalog.GetProviderId(result.Code);
        var previous = await _repository.GetByProviderIdAsync(providerId, cancellationToken)
            .ConfigureAwait(false);
        if (previous is null)
        {
            return null;
        }

        var successful = result.Outcome is ProviderRefreshOutcome.Success or ProviderRefreshOutcome.Partial;
        var connection = new ProviderConnection(
            previous.Id,
            previous.ProviderId,
            previous.ConnectionType,
            MapRefreshStatus(result.Outcome),
            result.Account?.DisplayName ?? previous.AccountDisplayName,
            successful ? result.CompletedAt : previous.LastSuccessfulSync,
            result.CompletedAt,
            result.ErrorCode,
            result.ErrorMessage,
            previous.CredentialReference,
            previous.Configuration);

        await _repository.UpsertAsync(connection, cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static ProviderConnectionStatus MapRefreshStatus(ProviderRefreshOutcome outcome) => outcome switch
    {
        ProviderRefreshOutcome.Success => ProviderConnectionStatus.Connected,
        ProviderRefreshOutcome.Partial => ProviderConnectionStatus.Partial,
        ProviderRefreshOutcome.AuthenticationRequired => ProviderConnectionStatus.AuthenticationRequired,
        ProviderRefreshOutcome.Unsupported => ProviderConnectionStatus.Unsupported,
        ProviderRefreshOutcome.Stale => ProviderConnectionStatus.Stale,
        ProviderRefreshOutcome.ProviderError => ProviderConnectionStatus.Error,
        _ => ProviderConnectionStatus.Error
    };

    private static string CreateCredentialReference(Guid providerId, ProviderCode? code) =>
        code is { } builtInCode
            ? $"apo-{builtInCode.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}"
            : $"apo-custom-{providerId:N}-{Guid.NewGuid():N}";

    private async Task TryRemoveCredentialAsync(string credentialReference)
    {
        try
        {
            await _credentialStore.RemoveAsync(credentialReference).ConfigureAwait(false);
        }
        catch
        {
            // The committed connection already points at the new state. A cleanup failure must
            // not turn a valid save into a false failure or expose secure-store diagnostics.
        }
    }
}
