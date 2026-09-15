using System.Security.Cryptography;
using System.Text;
using AIUsageMonitor.Domain.Providers;

namespace AIUsageMonitor.Providers.Common;

internal static class ProviderIdentity
{
    // The built-in table lives in the Domain layer so the Application-layer registry contract and
    // the Providers adapters cannot drift into two different identifiers for the same provider.
    public static Guid ForProvider(ProviderCode code) => BuiltInProviderIdentity.ForProvider(code);

    public static Guid ForAccount(ProviderCode code, string externalAccountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalAccountId);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"apo-provider-account:{code}:{externalAccountId}"));
        return new Guid(bytes[..16]);
    }
}
