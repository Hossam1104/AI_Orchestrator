using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Domain.Providers;
using AIUsageMonitor.Providers.Claude;
using AIUsageMonitor.Providers.Copilot;
using AIUsageMonitor.Providers.Kimi;

namespace AIUsageMonitor.Providers.Common;

/// <summary>
/// Immutable typed provider options captured as one unit at the start of a provider refresh.
/// </summary>
public sealed record ProviderRuntimeSettingsSnapshot(
    CopilotOptions Copilot,
    AnthropicOptions Anthropic,
    KimiOptions Kimi)
{
    public static ProviderRuntimeSettingsSnapshot Defaults() => new(
        new CopilotOptions(),
        new AnthropicOptions(),
        new KimiOptions());
}

public interface IProviderRuntimeSettingsAccessor : IProviderRuntimeSettingsUpdater
{
    ProviderRuntimeSettingsSnapshot Current { get; }

    void Replace(ProviderRuntimeSettingsSnapshot snapshot);
}

/// <summary>
/// Thread-safe copy-on-write settings store. A provider sees either the old or new complete
/// snapshot; no individual option can change halfway through a request.
/// </summary>
public sealed class ProviderRuntimeSettingsAccessor : IProviderRuntimeSettingsAccessor
{
    private ProviderRuntimeSettingsSnapshot _current;

    public ProviderRuntimeSettingsAccessor()
        : this(ProviderRuntimeSettingsSnapshot.Defaults())
    {
    }

    public ProviderRuntimeSettingsAccessor(
        CopilotOptions copilot,
        AnthropicOptions anthropic,
        KimiOptions kimi)
        : this(new ProviderRuntimeSettingsSnapshot(copilot, anthropic, kimi))
    {
    }

    public ProviderRuntimeSettingsAccessor(ProviderRuntimeSettingsSnapshot initial)
    {
        _current = initial ?? throw new ArgumentNullException(nameof(initial));
    }

    public ProviderRuntimeSettingsSnapshot Current => Volatile.Read(ref _current);

    public void Replace(ProviderRuntimeSettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Interlocked.Exchange(ref _current, snapshot);
    }

    public void Apply(
        ProviderCode code,
        string? credentialReference,
        IReadOnlyDictionary<string, string?> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var current = Current;

        switch (code)
        {
            case ProviderCode.Copilot:
                Replace(new ProviderRuntimeSettingsSnapshot(
                    BuildCopilotOptions(credentialReference, configuration, current.Copilot),
                    current.Anthropic,
                    current.Kimi));
                break;
            case ProviderCode.Claude:
                var authenticationMode = ProviderAuthenticationMode.LocalSession;
                if (configuration.TryGetValue(ProviderConnectionConfigurationKeys.AuthenticationMode, out var modeText) &&
                    Enum.TryParse<ProviderAuthenticationMode>(modeText, ignoreCase: true, out var parsedMode))
                {
                    authenticationMode = parsedMode;
                }
                else if (!string.IsNullOrWhiteSpace(credentialReference))
                {
                    // Existing pre-remediation Claude records were API-key records. Preserve
                    // their behavior during migration without changing the new default.
                    authenticationMode = ProviderAuthenticationMode.ApiKey;
                }

                Replace(new ProviderRuntimeSettingsSnapshot(
                    current.Copilot,
                    new AnthropicOptions
                    {
                        CredentialReference = credentialReference,
                        StartingAt = current.Anthropic.StartingAt,
                        AuthenticationMode = authenticationMode
                    },
                    current.Kimi));
                break;
            case ProviderCode.Kimi:
                Replace(new ProviderRuntimeSettingsSnapshot(
                    current.Copilot,
                    current.Anthropic,
                    BuildKimiOptions(credentialReference, configuration, current.Kimi)));
                break;
            case ProviderCode.Codex:
            case ProviderCode.Antigravity:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown provider code.");
        }
    }

    private static CopilotOptions BuildCopilotOptions(
        string? credentialReference,
        IReadOnlyDictionary<string, string?> configuration,
        CopilotOptions previous)
    {
        var scope = previous.Scope;
        if (configuration.TryGetValue(ProviderConnectionConfigurationKeys.CopilotScope, out var scopeText) &&
            Enum.TryParse<CopilotBillingScope>(scopeText, ignoreCase: true, out var parsedScope))
        {
            scope = parsedScope;
        }

        return new CopilotOptions
        {
            CredentialReference = credentialReference,
            Scope = scope,
            Username = configuration.TryGetValue(ProviderConnectionConfigurationKeys.CopilotUsername, out var username)
                ? username
                : previous.Username,
            Organization = configuration.TryGetValue(ProviderConnectionConfigurationKeys.CopilotOrganization, out var organization)
                ? organization
                : previous.Organization,
            Enterprise = previous.Enterprise
        };
    }

    private static KimiOptions BuildKimiOptions(
        string? credentialReference,
        IReadOnlyDictionary<string, string?> configuration,
        KimiOptions previous)
    {
        var address = previous.ServerAddress;
        if (configuration.TryGetValue(ProviderConnectionConfigurationKeys.KimiServerAddress, out var addressText) &&
            !string.IsNullOrWhiteSpace(addressText) &&
            Uri.TryCreate(addressText, UriKind.Absolute, out var parsedAddress))
        {
            address = parsedAddress;
        }

        return new KimiOptions
        {
            CredentialReference = credentialReference,
            ServerAddress = address
        };
    }
}

