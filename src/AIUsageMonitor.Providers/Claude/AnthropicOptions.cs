using AIUsageMonitor.Domain.Providers;

namespace AIUsageMonitor.Providers.Claude;

/// <summary>
/// Explicit Anthropic Admin API configuration. This channel is organization API usage, never
/// Claude Pro/Max subscription capacity.
/// </summary>
public sealed class AnthropicOptions
{
    public string? CredentialReference { get; init; }
    public DateTimeOffset? StartingAt { get; init; }
    public ProviderAuthenticationMode AuthenticationMode { get; init; } = ProviderAuthenticationMode.LocalSession;
}
