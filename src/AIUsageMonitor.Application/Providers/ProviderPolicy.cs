using AIUsageMonitor.Domain.Providers;

namespace AIUsageMonitor.Application.Providers;

/// <summary>Canonical provider authentication and capacity policy shared by workflows and presentation.</summary>
public static class ProviderPolicy
{
    public static ProviderAuthenticationMode AuthenticationModeFor(
        ProviderConnectionType connectionType,
        ProviderAuthenticationMode fallback = ProviderAuthenticationMode.None) => connectionType switch
        {
            ProviderConnectionType.LocalSession => ProviderAuthenticationMode.LocalSession,
            ProviderConnectionType.ApiKey or ProviderConnectionType.OfficialApi => ProviderAuthenticationMode.ApiKey,
            ProviderConnectionType.ExternalManual or ProviderConnectionType.Manual => ProviderAuthenticationMode.ExternalManual,
            _ => fallback
        };

    public static ProviderConnectionType ConnectionTypeFor(ProviderAuthenticationMode authenticationMode) => authenticationMode switch
    {
        ProviderAuthenticationMode.LocalSession => ProviderConnectionType.LocalSession,
        ProviderAuthenticationMode.ApiKey => ProviderConnectionType.ApiKey,
        ProviderAuthenticationMode.ExternalManual => ProviderConnectionType.ExternalManual,
        _ => ProviderConnectionType.Unknown
    };

    public static ProviderCapacityState CapacityStateFor(ProviderCapacityMode capacityMode) => capacityMode switch
    {
        ProviderCapacityMode.Manual => ProviderCapacityState.Manual,
        ProviderCapacityMode.Automatic => ProviderCapacityState.Unknown,
        ProviderCapacityMode.Unavailable => ProviderCapacityState.Unavailable,
        _ => ProviderCapacityState.Unknown
    };

    public static bool SupportsAutomaticCapacity(
        ProviderDefinition definition,
        ProviderAuthenticationMode authenticationMode) =>
        definition.HasCapability(ProviderCapabilities.SupportsCapacityRefresh) &&
        authenticationMode != ProviderAuthenticationMode.ExternalManual &&
        (authenticationMode == ProviderAuthenticationMode.ApiKey || definition.BuiltInCode != ProviderCode.Claude);

    public static void ValidateConnection(ProviderConnectionEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);

        var authenticationMode = AuthenticationModeFor(edit.ConnectionType);
        if (edit.Configuration.TryGetValue(
                ProviderConnectionConfigurationKeys.AuthenticationMode,
                out var configuredAuthenticationMode) &&
            !string.IsNullOrWhiteSpace(configuredAuthenticationMode))
        {
            if (!Enum.TryParse<ProviderAuthenticationMode>(
                    configuredAuthenticationMode,
                    ignoreCase: true,
                    out var parsedAuthenticationMode) ||
                parsedAuthenticationMode != authenticationMode)
            {
                throw new ArgumentException(
                    "The persisted authentication mode must match the selected connection type.",
                    nameof(edit));
            }
        }

        if (!string.IsNullOrWhiteSpace(edit.Secret) && authenticationMode != ProviderAuthenticationMode.ApiKey)
        {
            throw new ArgumentException(
                "API key can only be saved when API key authentication mode is selected.",
                nameof(edit));
        }

        if (!edit.Configuration.TryGetValue(
                ProviderConnectionConfigurationKeys.CapacityMode,
                out var configuredCapacityMode) ||
            string.IsNullOrWhiteSpace(configuredCapacityMode))
        {
            return;
        }

        if (!Enum.TryParse<ProviderCapacityMode>(configuredCapacityMode, ignoreCase: true, out var capacityMode))
        {
            throw new ArgumentException("The persisted capacity mode is not supported.", nameof(edit));
        }

        if (capacityMode != ProviderCapacityMode.Automatic)
        {
            return;
        }

        if (edit.Code is not { } code || !SupportsAutomaticCapacity(CreateBuiltInDefinition(code), authenticationMode))
        {
            throw new ArgumentException(
                "Automatic capacity is unavailable for the selected provider and authentication channel.",
                nameof(edit));
        }
    }

    public static ProviderDefinition CreateBuiltInDefinition(IAiUsageProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return CreateBuiltInDefinition(provider.Code);
    }

    public static ProviderDefinition CreateBuiltInDefinition(
        ProviderCode code,
        string? displayNameOverride = null)
    {
        var (name, authenticationMode, capacityMode, capabilities, description, sortOrder) = code switch
        {
            ProviderCode.Codex => (
                "Codex",
                ProviderAuthenticationMode.LocalSession,
                ProviderCapacityMode.Manual,
                ProviderCapabilities.SupportsLocalSessionDetection |
                    ProviderCapabilities.SupportsApiKey |
                    ProviderCapabilities.SupportsConfiguration,
                "Uses the existing authenticated local Codex session when available; API key is an optional fallback.",
                0),
            ProviderCode.Claude => (
                "Claude",
                ProviderAuthenticationMode.LocalSession,
                ProviderCapacityMode.Manual,
                ProviderCapabilities.SupportsLocalSessionDetection |
                    ProviderCapabilities.SupportsApiKey |
                    ProviderCapabilities.SupportsCapacityRefresh |
                    ProviderCapabilities.SupportsConfiguration,
                "Uses the existing authenticated local Claude session; organization API usage is available only through explicit API-key fallback.",
                1),
            ProviderCode.Kimi => (
                "Kimi",
                ProviderAuthenticationMode.ApiKey,
                ProviderCapacityMode.Automatic,
                ProviderCapabilities.SupportsApiKey |
                    ProviderCapabilities.SupportsCapacityRefresh |
                    ProviderCapabilities.SupportsConfiguration,
                "Provider capacity is available through the declared typed adapter.",
                2),
            ProviderCode.Copilot => (
                "GitHub Copilot",
                ProviderAuthenticationMode.ApiKey,
                ProviderCapacityMode.Automatic,
                ProviderCapabilities.SupportsApiKey |
                    ProviderCapabilities.SupportsCapacityRefresh |
                    ProviderCapabilities.SupportsConfiguration,
                "Legacy provider settings retained for compatibility.",
                3),
            ProviderCode.Antigravity => (
                "Antigravity",
                ProviderAuthenticationMode.ExternalManual,
                ProviderCapacityMode.Manual,
                ProviderCapabilities.None,
                "External/manual provider state is shown without inventing machine-readable capacity.",
                2),
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Provider code is not supported.")
        };

        return ProviderDefinition.BuiltIn(
            BuiltInProviderIdentity.ForProvider(code),
            code,
            displayNameOverride ?? name,
            authenticationMode,
            capacityMode,
            capabilities,
            sortOrder,
            description: description);
    }
}
