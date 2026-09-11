using AIUsageMonitor.Domain.Providers;

namespace AIUsageMonitor.Application.Providers;

public sealed class ProviderDetectionResult
{
    public ProviderCode Code { get; }
    public bool IsDetected { get; }
    public bool ToolDetected => IsDetected;
    public ProviderAuthenticationState AuthenticationState { get; }
    public string? DetectionMethod { get; }
    public DateTimeOffset DetectedAt { get; }

    public ProviderDetectionResult(ProviderCode code, bool isDetected, string? detectionMethod, DateTimeOffset detectedAt)
        : this(
            code,
            isDetected,
            ProviderAuthenticationState.Unknown,
            detectionMethod,
            detectedAt)
    {
    }

    public ProviderDetectionResult(
        ProviderCode code,
        bool isDetected,
        ProviderAuthenticationState authenticationState,
        string? detectionMethod,
        DateTimeOffset detectedAt)
    {
        Code = code;
        IsDetected = isDetected;
        AuthenticationState = authenticationState;
        DetectionMethod = detectionMethod;
        DetectedAt = detectedAt;
    }
}
