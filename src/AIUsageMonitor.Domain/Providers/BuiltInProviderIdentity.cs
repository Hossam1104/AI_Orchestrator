namespace AIUsageMonitor.Domain.Providers;

/// <summary>
/// The canonical, stable identifier of each built-in provider. These values are persisted in
/// provider documents and referenced by stored connections, so they must never be regenerated:
/// a fresh identifier per call silently orphans everything already stored against the old one.
/// </summary>
public static class BuiltInProviderIdentity
{
    private static readonly IReadOnlyDictionary<ProviderCode, Guid> ProviderIds =
        new Dictionary<ProviderCode, Guid>
        {
            [ProviderCode.Codex] = new("1cf3c94e-9bcb-4fe4-9b2c-24a0b4f3a901"),
            [ProviderCode.Claude] = new("2d6f54fa-2c0e-4cc5-8bf6-6debf48b3f02"),
            [ProviderCode.Kimi] = new("3a8c3ec8-42e0-4a3c-a0b2-e6a2de7db903"),
            [ProviderCode.Copilot] = new("4bb5d50c-9bd4-4df1-897a-10d395da0404"),
            [ProviderCode.Antigravity] = new("5b544ceb-0ac4-43b6-8c9e-9e27c9f0c505")
        };

    public static Guid ForProvider(ProviderCode code) => ProviderIds[code];
}
