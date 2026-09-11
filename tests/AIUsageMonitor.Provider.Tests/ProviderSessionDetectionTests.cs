using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Domain.Providers;
using AIUsageMonitor.Providers.Claude;
using AIUsageMonitor.Providers.Codex;
using AIUsageMonitor.Providers.Common;
using AIUsageMonitor.Providers.Copilot;
using AIUsageMonitor.Providers.Kimi;

namespace AIUsageMonitor.Provider.Tests;

public sealed class ProviderSessionDetectionTests
{
    [Fact]
    public async Task Codex_MissingTool_IsAuthenticationRequiredWithoutProcessInvocation()
    {
        var runner = new RecordingProcessRunner();
        var provider = new CodexProvider(new TestClock(), new TestExecutableLocator(), runner);

        var result = await provider.DetectSessionAsync();

        Assert.False(result.ToolDetected);
        Assert.Equal(ProviderAuthenticationState.AuthenticationRequired, result.AuthenticationState);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task Codex_AuthenticatedSession_UsesFixedReadOnlyStatusCommand()
    {
        var runner = new RecordingProcessRunner(new ProviderProcessResult(
            ProviderProcessOutcome.ExitedSuccessfully,
            0,
            "Logged in using ChatGPT",
            string.Empty,
            false,
            false));
        var provider = new CodexProvider(
            new TestClock(),
            new TestExecutableLocator("codex"),
            runner);

        var result = await provider.DetectSessionAsync();

        Assert.True(result.ToolDetected);
        Assert.Equal(ProviderAuthenticationState.AuthenticatedLocalSession, result.AuthenticationState);
        var request = Assert.Single(runner.Requests);
        Assert.Equal("C:\\test-tools\\codex.exe", request.ExecutablePath);
        Assert.Equal(["login", "status"], request.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(8), request.Timeout);
    }

    [Fact]
    public async Task Codex_Timeout_LeavesAuthenticationUnknownAndDoesNotExposeOutput()
    {
        var runner = new RecordingProcessRunner(new ProviderProcessResult(
            ProviderProcessOutcome.TimedOut,
            null,
            "access-token: SHOULD-NOT-REACH-UI",
            string.Empty,
            false,
            false));
        var provider = new CodexProvider(
            new TestClock(),
            new TestExecutableLocator("codex"),
            runner);

        var result = await provider.DetectSessionAsync();

        Assert.Equal(ProviderAuthenticationState.Unknown, result.AuthenticationState);
        Assert.Contains("Unable to verify", result.DetectionMethod, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SHOULD-NOT-REACH-UI", result.DetectionMethod, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Codex_NotLoggedInIsNotMisclassifiedAsAuthenticated()
    {
        var runner = new RecordingProcessRunner(new ProviderProcessResult(
            ProviderProcessOutcome.ExitedSuccessfully,
            0,
            "Not logged in",
            string.Empty,
            false,
            false));
        var provider = new CodexProvider(
            new TestClock(),
            new TestExecutableLocator("codex"),
            runner);

        var result = await provider.DetectSessionAsync();

        Assert.Equal(ProviderAuthenticationState.AuthenticationRequired, result.AuthenticationState);
    }

    [Fact]
    public async Task Claude_AuthenticatedSession_ParsesOnlyTheBoundedBooleanStatus()
    {
        var runner = new RecordingProcessRunner(new ProviderProcessResult(
            ProviderProcessOutcome.ExitedSuccessfully,
            0,
            "{\"loggedIn\":true,\"oauthToken\":\"secret\"}",
            string.Empty,
            false,
            false));
        var provider = new ClaudeProvider(
            new TestClock(),
            new TestHttpClientFactory(DelegateHttpMessageHandler.Json("{}")),
            new TestCredentialStore(),
            new TestExecutableLocator("claude"),
            new ProviderRuntimeSettingsAccessor(),
            runner);

        var result = await provider.DetectSessionAsync();

        Assert.Equal(ProviderAuthenticationState.AuthenticatedLocalSession, result.AuthenticationState);
        Assert.DoesNotContain("secret", result.DetectionMethod, StringComparison.OrdinalIgnoreCase);
        var request = Assert.Single(runner.Requests);
        Assert.Equal(["auth", "status", "--json"], request.Arguments);
    }

    [Fact]
    public async Task Claude_ExplicitApiKeyMode_DoesNotInvokeLocalSessionProbe()
    {
        var runner = new RecordingProcessRunner();
        var settings = new ProviderRuntimeSettingsAccessor(
            new CopilotOptions(),
            new AnthropicOptions
            {
                AuthenticationMode = ProviderAuthenticationMode.ApiKey,
                CredentialReference = "opaque-anthropic-reference"
            },
            new KimiOptions());
        var provider = new ClaudeProvider(
            new TestClock(),
            new TestHttpClientFactory(DelegateHttpMessageHandler.Json("{}")),
            new TestCredentialStore(),
            new TestExecutableLocator("claude"),
            settings,
            runner);

        var result = await provider.DetectSessionAsync();

        Assert.Equal(ProviderAuthenticationState.ApiKeyConfigured, result.AuthenticationState);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task Claude_MalformedStatus_IsUnknownRatherThanProviderUnavailable()
    {
        var runner = new RecordingProcessRunner(new ProviderProcessResult(
            ProviderProcessOutcome.ExitedSuccessfully,
            0,
            "not-json",
            string.Empty,
            false,
            false));
        var provider = new ClaudeProvider(
            new TestClock(),
            new TestHttpClientFactory(DelegateHttpMessageHandler.Json("{}")),
            new TestCredentialStore(),
            new TestExecutableLocator("claude"),
            new ProviderRuntimeSettingsAccessor(),
            runner);

        var result = await provider.DetectSessionAsync();

        Assert.True(result.ToolDetected);
        Assert.Equal(ProviderAuthenticationState.Unknown, result.AuthenticationState);
        Assert.Contains("not machine-verifiable", result.DetectionMethod, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecordingProcessRunner : IProviderProcessRunner
    {
        private readonly ProviderProcessResult? _result;

        public RecordingProcessRunner(ProviderProcessResult? result = null) => _result = result;

        public List<ProviderProcessRequest> Requests { get; } = [];

        public Task<ProviderProcessResult> RunAsync(
            ProviderProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_result ?? new ProviderProcessResult(
                ProviderProcessOutcome.StartFailed,
                null,
                string.Empty,
                string.Empty,
                false,
                false,
                "not configured"));
        }
    }
}
