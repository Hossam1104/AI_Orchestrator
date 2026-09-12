namespace AIUsageMonitor.Domain.Providers;

/// <summary>
/// How the application is connected to a provider, independent of current <see cref="ProviderConnectionStatus"/>.
/// </summary>
public enum ProviderConnectionType
{
    LocalSession,
    ApiKey,
    ExternalManual,
    OfficialApi,
    OAuth,
    OfficialCli,
    LocalMetadata,
    Manual,
    Unknown
}
