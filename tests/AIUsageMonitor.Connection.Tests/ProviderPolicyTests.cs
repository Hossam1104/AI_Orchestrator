using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Domain.Providers;

namespace AIUsageMonitor.Connection.Tests;

public sealed class ProviderPolicyTests
{
    [Theory]
    [InlineData(ProviderConnectionType.LocalSession, ProviderAuthenticationMode.LocalSession)]
    [InlineData(ProviderConnectionType.ApiKey, ProviderAuthenticationMode.ApiKey)]
    [InlineData(ProviderConnectionType.OfficialApi, ProviderAuthenticationMode.ApiKey)]
    [InlineData(ProviderConnectionType.ExternalManual, ProviderAuthenticationMode.ExternalManual)]
    [InlineData(ProviderConnectionType.Manual, ProviderAuthenticationMode.ExternalManual)]
    public void ConnectionTypeMapping_IsCanonical(
        ProviderConnectionType connectionType,
        ProviderAuthenticationMode expected)
    {
        Assert.Equal(expected, ProviderPolicy.AuthenticationModeFor(connectionType));
    }

    [Fact]
    public void ClaudeAutomaticCapacity_RequiresTheExplicitApiKeyChannel()
    {
        var definition = ProviderPolicy.CreateBuiltInDefinition(ProviderCode.Claude);

        Assert.False(ProviderPolicy.SupportsAutomaticCapacity(
            definition,
            ProviderAuthenticationMode.LocalSession));
        Assert.True(ProviderPolicy.SupportsAutomaticCapacity(
            definition,
            ProviderAuthenticationMode.ApiKey));
    }
}
