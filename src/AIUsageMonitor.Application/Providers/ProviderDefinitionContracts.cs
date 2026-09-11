using AIUsageMonitor.Domain.Providers;

namespace AIUsageMonitor.Application.Providers;

public sealed class ProviderDefinitionEdit
{
    public ProviderDefinitionEdit(
        string displayName,
        string? displayLabel,
        ProviderAuthenticationMode authenticationMode,
        ProviderCapacityMode capacityMode,
        bool enabled,
        string? description = null,
        Guid? id = null)
    {
        DisplayName = displayName;
        DisplayLabel = displayLabel;
        AuthenticationMode = authenticationMode;
        CapacityMode = capacityMode;
        Enabled = enabled;
        Description = description;
        Id = id;
    }

    public Guid? Id { get; }
    public string DisplayName { get; }
    public string? DisplayLabel { get; }
    public ProviderAuthenticationMode AuthenticationMode { get; }
    public ProviderCapacityMode CapacityMode { get; }
    public bool Enabled { get; }
    public string? Description { get; }
}

public interface IProviderDefinitionRepository
{
    Task<IReadOnlyList<ProviderDefinition>> GetCustomAsync(CancellationToken cancellationToken = default);

    Task UpsertCustomAsync(ProviderDefinition definition, CancellationToken cancellationToken = default);

    Task RemoveCustomAsync(Guid providerId, CancellationToken cancellationToken = default);
}

public interface IProviderSessionDetector
{
    ProviderCode Code { get; }

    Task<ProviderDetectionResult> DetectSessionAsync(CancellationToken cancellationToken = default);
}

public enum ProviderProcessOutcome
{
    ExitedSuccessfully,
    NonZeroExit,
    TimedOut,
    Cancelled,
    StartFailed,
    TerminationFailure
}

public sealed record ProviderProcessRequest(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout);

public sealed record ProviderProcessResult(
    ProviderProcessOutcome Outcome,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool StandardOutputTruncated,
    bool StandardErrorTruncated,
    string? ErrorMessage = null)
{
    public bool Succeeded => Outcome == ProviderProcessOutcome.ExitedSuccessfully;
}

/// <summary>Provider adapters receive only fixed, typed process requests.</summary>
public interface IProviderProcessRunner
{
    Task<ProviderProcessResult> RunAsync(
        ProviderProcessRequest request,
        CancellationToken cancellationToken = default);
}
