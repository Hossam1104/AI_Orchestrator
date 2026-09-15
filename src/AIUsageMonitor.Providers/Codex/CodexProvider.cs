using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Domain.Providers;
using AIUsageMonitor.Providers.Common;

namespace AIUsageMonitor.Providers.Codex;

/// <summary>
/// Codex consumer-plan usage is intentionally not inferred from OpenAI API organization usage.
/// The documented Codex subscription status surface is interactive, so this adapter reports the
/// official CLI when installed and leaves automated consumer capacity manual/unsupported.
/// </summary>
public sealed class CodexProvider : ProviderAdapterBase, IProviderSessionDetector
{
    private readonly IExecutableLocator _executableLocator;
    private readonly IProviderProcessRunner? _processRunner;

    public CodexProvider(
        IClock clock,
        IExecutableLocator executableLocator,
        IProviderProcessRunner? processRunner = null)
        : base(clock)
    {
        _executableLocator = executableLocator ?? throw new ArgumentNullException(nameof(executableLocator));
        _processRunner = processRunner;
    }

    public override ProviderCode Code => ProviderCode.Codex;

    public async Task<ProviderDetectionResult> DetectSessionAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var executablePath = _executableLocator.Find("codex");
        if (executablePath is null)
        {
            return new(
                Code,
                false,
                ProviderAuthenticationState.AuthenticationRequired,
                "Codex local tool was not detected.",
                UtcNow);
        }

        if (_processRunner is null || !string.Equals(
                Path.GetExtension(executablePath),
                ".exe",
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                Code,
                true,
                ProviderAuthenticationState.Unknown,
                "Local Codex tool detected — authentication not machine-verifiable.",
                UtcNow);
        }

        var result = await _processRunner.RunAsync(
                new ProviderProcessRequest(
                    executablePath,
                    ["login", "status"],
                    TimeSpan.FromSeconds(8)),
                cancellationToken)
            .ConfigureAwait(false);
        var output = $"{result.StandardOutput}\n{result.StandardError}";
        if (output.Contains("not logged", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("not authenticated", StringComparison.OrdinalIgnoreCase))
        {
            return new(
                Code,
                true,
                ProviderAuthenticationState.AuthenticationRequired,
                "Codex local tool is installed, but no authenticated session was reported.",
                UtcNow);
        }

        if (result.Succeeded && output.Contains("logged in", StringComparison.OrdinalIgnoreCase))
        {
            return new(
                Code,
                true,
                ProviderAuthenticationState.AuthenticatedLocalSession,
                "Connected via local Codex session.",
                UtcNow);
        }

        return new(
            Code,
            true,
            ProviderAuthenticationState.Unknown,
            "Unable to verify the local Codex session.",
            UtcNow);
    }

    public override Task<ProviderDetectionResult> DetectAsync(
        CancellationToken cancellationToken = default) =>
        DetectSessionAsync(cancellationToken);

    public override async Task<ProviderConnectionStatus> GetConnectionStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var detection = await DetectSessionAsync(cancellationToken).ConfigureAwait(false);
        return detection.AuthenticationState switch
        {
            ProviderAuthenticationState.AuthenticatedLocalSession => ProviderConnectionStatus.Connected,
            ProviderAuthenticationState.AuthenticationRequired => ProviderConnectionStatus.AuthenticationRequired,
            _ when detection.IsDetected => ProviderConnectionStatus.LocalDetected,
            _ => ProviderConnectionStatus.Unsupported
        };
    }

    protected override Task<ProviderRefreshResult> RefreshCoreAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(Unsupported());
}
