using AIUsageMonitor.Application.Providers;

namespace AIUsageMonitor.Infrastructure.Execution;

/// <summary>
/// Adapts the existing no-shell bounded process host for provider-owned, fixed command probes.
/// Providers never receive a shell command string and this adapter never expands one.
/// </summary>
public sealed class BoundedProviderProcessRunner : IProviderProcessRunner
{
    private readonly IBoundedProcessHost _host;

    public BoundedProviderProcessRunner(IBoundedProcessHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public async Task<ProviderProcessResult> RunAsync(
        ProviderProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var workingDirectory = request.WorkingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Path.IsPathFullyQualified(workingDirectory))
        {
            workingDirectory = Environment.CurrentDirectory;
        }

        var result = await _host.RunAsync(
                new BoundedProcessRequest(
                    request.ExecutablePath,
                    request.Arguments,
                    workingDirectory,
                    request.Timeout),
                cancellationToken)
            .ConfigureAwait(false);

        return new ProviderProcessResult(
            result.Outcome switch
            {
                BoundedProcessOutcome.ExitedSuccessfully => ProviderProcessOutcome.ExitedSuccessfully,
                BoundedProcessOutcome.NonZeroExit => ProviderProcessOutcome.NonZeroExit,
                BoundedProcessOutcome.TimedOut => ProviderProcessOutcome.TimedOut,
                BoundedProcessOutcome.Cancelled => ProviderProcessOutcome.Cancelled,
                BoundedProcessOutcome.StartFailed => ProviderProcessOutcome.StartFailed,
                BoundedProcessOutcome.TerminationFailure => ProviderProcessOutcome.TerminationFailure,
                _ => ProviderProcessOutcome.StartFailed
            },
            result.ExitCode,
            Redact(result.StandardOutput),
            Redact(result.StandardError),
            result.StandardOutputTruncated,
            result.StandardErrorTruncated,
            result.ErrorMessage);
    }

    private static string Redact(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var redacted = System.Text.RegularExpressions.Regex.Replace(
            value,
            "(?im)(bearer\\s+|(?:api[_-]?key|access[_-]?token|refresh[_-]?token|oauth[_-]?token|token)\\s*[\\\"']?\\s*[:=]\\s*[\\\"']?)[^\\s,}\\\"']+",
            "$1[redacted]");
        return System.Text.RegularExpressions.Regex.Replace(
            redacted,
            "(?<![A-Za-z0-9])[A-Za-z0-9_-]{24,}\\.[A-Za-z0-9_-]{8,}\\.[A-Za-z0-9_-]{8,}(?![A-Za-z0-9])",
            "[redacted]");
    }
}
