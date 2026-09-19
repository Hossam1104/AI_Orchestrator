namespace AIUsageMonitor.Application.Providers;

/// <summary>
/// Stable keys for the small non-secret settings persisted alongside a provider connection.
/// The values are configuration metadata only; credential material remains in secure storage.
/// </summary>
public static class ProviderConnectionConfigurationKeys
{
    public const string AuthenticationMode = "provider.authenticationMode";
    public const string CapacityMode = "provider.capacityMode";
    public const string CopilotScope = "copilot.scope";
    public const string CopilotUsername = "copilot.username";
    public const string CopilotOrganization = "copilot.organization";
    public const string AnthropicChannel = "anthropic.channel";
    public const string KimiServerAddress = "kimi.serverAddress";
}
