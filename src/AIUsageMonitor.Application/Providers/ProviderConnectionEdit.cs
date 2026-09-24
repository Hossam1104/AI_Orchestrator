using AIUsageMonitor.Domain.Providers;

namespace AIUsageMonitor.Application.Providers;

/// <summary>
/// A provider connection edit containing non-secret configuration and, only for the duration of
/// the save call, an optional newly entered secret. The secret is never part of a persisted model.
/// </summary>
public sealed class ProviderConnectionEdit
{
    public ProviderConnectionEdit(
        ProviderCode code,
        ProviderConnectionType connectionType,
        IReadOnlyDictionary<string, string?> configuration,
        string? secret = null,
        bool removeCredential = false)
    {
        Configuration = new Dictionary<string, string?>(configuration ??
            new Dictionary<string, string?>(), StringComparer.OrdinalIgnoreCase);
        Code = code;
        ConnectionType = connectionType;
        Secret = secret;
        RemoveCredential = removeCredential;
    }

    private ProviderConnectionEdit(
        Guid providerId,
        ProviderConnectionType connectionType,
        IReadOnlyDictionary<string, string?> configuration,
        string? secret,
        bool removeCredential)
    {
        if (providerId == Guid.Empty)
        {
            throw new ArgumentException("Provider id cannot be empty.", nameof(providerId));
        }

        Configuration = new Dictionary<string, string?>(configuration ??
            new Dictionary<string, string?>(), StringComparer.OrdinalIgnoreCase);
        ProviderId = providerId;
        ConnectionType = connectionType;
        Secret = secret;
        RemoveCredential = removeCredential;
    }

    public static ProviderConnectionEdit ForProvider(
        Guid providerId,
        ProviderConnectionType connectionType,
        IReadOnlyDictionary<string, string?> configuration,
        string? secret = null,
        bool removeCredential = false) =>
        new(providerId, connectionType, configuration, secret, removeCredential);

    public ProviderCode? Code { get; }

    public Guid? ProviderId { get; }

    public ProviderConnectionType ConnectionType { get; }

    public IReadOnlyDictionary<string, string?> Configuration { get; }

    public string? Secret { get; }

    public bool RemoveCredential { get; }
}
