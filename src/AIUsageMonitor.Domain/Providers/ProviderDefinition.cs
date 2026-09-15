namespace AIUsageMonitor.Domain.Providers;

public enum ProviderKind
{
    BuiltIn,
    Custom
}

public enum ProviderAvailability
{
    Detected,
    NotDetected,
    Unknown
}

public enum ProviderAuthenticationMode
{
    LocalSession,
    ApiKey,
    ExternalManual,
    None
}

public enum ProviderAuthenticationState
{
    AuthenticatedLocalSession,
    ApiKeyConfigured,
    AuthenticationRequired,
    ExternalAuthentication,
    NotConfigured,
    Unknown
}

public enum ProviderCapacityState
{
    Available,
    Unavailable,
    Manual,
    Unsupported,
    AuthenticationRequired,
    RefreshFailed,
    Unknown
}

public enum ProviderCapacityMode
{
    Manual,
    Automatic,
    Unavailable,
    Unknown
}

[Flags]
public enum ProviderCapabilities
{
    None = 0,
    SupportsLocalSessionDetection = 1,
    SupportsApiKey = 2,
    SupportsCapacityRefresh = 4,
    SupportsConfiguration = 8
}

/// <summary>
/// Provider identity and capability declaration. Live availability, authentication, and capacity
/// are intentionally separate values so an authenticated provider can truthfully have manual
/// capacity, and an installed tool can still have an unverifiable session.
/// </summary>
public sealed class ProviderDefinition
{
    public ProviderDefinition(
        Guid id,
        ProviderCode? builtInCode,
        string displayName,
        string? displayLabel,
        ProviderKind kind,
        bool enabled,
        int sortOrder,
        ProviderAuthenticationMode authenticationMode,
        ProviderCapacityMode capacityMode,
        ProviderCapabilities capabilities,
        string? description,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Provider definition id cannot be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Provider definition display name is required.", nameof(displayName));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Provider kind is not supported.");
        }

        if (!Enum.IsDefined(authenticationMode))
        {
            throw new ArgumentOutOfRangeException(nameof(authenticationMode), "Provider authentication mode is not supported.");
        }

        if (!Enum.IsDefined(capacityMode))
        {
            throw new ArgumentOutOfRangeException(nameof(capacityMode), "Provider capacity mode is not supported.");
        }

        if (kind == ProviderKind.BuiltIn && builtInCode is null)
        {
            throw new ArgumentException("Built-in providers require a built-in code.", nameof(builtInCode));
        }

        if (kind == ProviderKind.Custom && builtInCode is not null)
        {
            throw new ArgumentException("Custom providers cannot use a built-in code.", nameof(builtInCode));
        }

        if (builtInCode is { } code && !Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(builtInCode), "Provider code is not supported.");
        }

        if (updatedAt < createdAt)
        {
            throw new ArgumentException("UpdatedAt cannot precede CreatedAt.", nameof(updatedAt));
        }

        Id = id;
        BuiltInCode = builtInCode;
        DisplayName = displayName.Trim();
        DisplayLabel = string.IsNullOrWhiteSpace(displayLabel) ? null : displayLabel.Trim();
        Kind = kind;
        Enabled = enabled;
        SortOrder = sortOrder;
        AuthenticationMode = authenticationMode;
        CapacityMode = capacityMode;
        Capabilities = capabilities;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }
    public ProviderCode? BuiltInCode { get; }
    public string DisplayName { get; }
    public string? DisplayLabel { get; }
    public ProviderKind Kind { get; }
    public bool Enabled { get; }
    public int SortOrder { get; }
    public ProviderAuthenticationMode AuthenticationMode { get; }
    public ProviderCapacityMode CapacityMode { get; }
    public ProviderCapabilities Capabilities { get; }
    public string? Description { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; }

    public string StableKey => BuiltInCode is { } code
        ? code.ToString().ToLowerInvariant()
        : Id.ToString("N");

    public string EffectiveDisplayName => string.IsNullOrWhiteSpace(DisplayLabel)
        ? DisplayName
        : DisplayLabel!;

    public bool HasCapability(ProviderCapabilities capability) => (Capabilities & capability) == capability;

    public static ProviderDefinition BuiltIn(
        Guid id,
        ProviderCode code,
        string displayName,
        ProviderAuthenticationMode authenticationMode,
        ProviderCapacityMode capacityMode,
        ProviderCapabilities capabilities,
        int sortOrder,
        DateTimeOffset? now = null,
        string? description = null) =>
        new(
            id,
            code,
            displayName,
            displayLabel: null,
            ProviderKind.BuiltIn,
            enabled: true,
            sortOrder,
            authenticationMode,
            capacityMode,
            capabilities,
            description,
            now ?? DateTimeOffset.UnixEpoch,
            now ?? DateTimeOffset.UnixEpoch);

    public static ProviderDefinition Custom(
        Guid id,
        string displayName,
        string? displayLabel,
        ProviderAuthenticationMode authenticationMode,
        ProviderCapacityMode capacityMode,
        bool enabled,
        int sortOrder,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        string? description = null) =>
        new(
            id,
            builtInCode: null,
            displayName,
            displayLabel,
            ProviderKind.Custom,
            enabled,
            sortOrder,
            authenticationMode,
            capacityMode,
            ProviderCapabilities.SupportsConfiguration |
                (authenticationMode == ProviderAuthenticationMode.ApiKey
                    ? ProviderCapabilities.SupportsApiKey
                    : ProviderCapabilities.None),
            description,
            createdAt,
            updatedAt);
}
