using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Infrastructure.Execution;

namespace AIUsageMonitor.Infrastructure.Tests;

public sealed class ProviderProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_RedactsSecretLikeOutputAndPreservesTypedRequest()
    {
        var host = new RecordingHost(new BoundedProcessResult(
            BoundedProcessOutcome.ExitedSuccessfully,
            0,
            "bearer TOP-SECRET access-token=SECOND-SECRET",
            "api-key: THIRD-SECRET",
            false,
            false,
            false,
            true,
            TimeSpan.FromMilliseconds(2)));
        var runner = new BoundedProviderProcessRunner(host);
        var request = new ProviderProcessRequest(
            "C:\\tools\\known.exe",
            ["auth", "status"],
            TimeSpan.FromSeconds(8));

        var result = await runner.RunAsync(request);

        Assert.DoesNotContain("TOP-SECRET", result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("SECOND-SECRET", result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("THIRD-SECRET", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("[redacted]", result.StandardOutput, StringComparison.Ordinal);
        var bounded = Assert.Single(host.Requests);
        Assert.Equal(request.ExecutablePath, bounded.ExecutablePath);
        Assert.Equal(request.Arguments, bounded.Arguments);
        Assert.Equal(request.Timeout, bounded.Timeout);
    }

    private sealed class RecordingHost : IBoundedProcessHost
    {
        private readonly BoundedProcessResult _result;

        public RecordingHost(BoundedProcessResult result) => _result = result;

        public List<BoundedProcessRequest> Requests { get; } = [];

        public Task<BoundedProcessResult> RunAsync(
            BoundedProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_result);
        }
    }
}
