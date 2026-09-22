using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Providers.Codex;

namespace AIUsageMonitor.Provider.Tests;

public sealed class CodexAgentConnectionProbeTests
{
    private const string WorkspacePath = @"C:\apo-test";

    [Fact]
    public async Task ProbeAsync_WhenLoggedIn_ReportsAuthenticated()
    {
        var probe = new CodexAgentConnectionProbe(new TestExecutableLocator("codex"), new FakeLoginStatusRunner(Success("Logged in")));

        var result = await probe.ProbeAsync(Agent(), WorkspacePath);

        Assert.Equal(AgentConnectionProbeStatus.Authenticated, result.Status);
        Assert.Equal("OpenAI", probe.Provider);
        Assert.Equal(AgentConnectionMode.Cli, probe.ConnectionMode);
    }

    [Fact]
    public async Task ProbeAsync_WhenNotLoggedIn_ReportsAuthenticationRequired()
    {
        var probe = new CodexAgentConnectionProbe(new TestExecutableLocator("codex"), new FakeLoginStatusRunner(Success("Not logged in")));

        var result = await probe.ProbeAsync(Agent(), WorkspacePath);

        Assert.Equal(AgentConnectionProbeStatus.AuthenticationRequired, result.Status);
    }

    [Fact]
    public async Task ProbeAsync_WhenExecutableIsNotFound_ReportsUnavailable()
    {
        var probe = new CodexAgentConnectionProbe(new TestExecutableLocator(), new FakeLoginStatusRunner(Success("Logged in")));

        var result = await probe.ProbeAsync(Agent(), WorkspacePath);

        Assert.Equal(AgentConnectionProbeStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task ProbeAsync_WhenProcessTimesOut_ReportsTimedOut()
    {
        var probe = new CodexAgentConnectionProbe(new TestExecutableLocator("codex"), new FakeLoginStatusRunner(new ProviderProcessResult(ProviderProcessOutcome.TimedOut, null, string.Empty, string.Empty, false, false)));

        var result = await probe.ProbeAsync(Agent(), WorkspacePath);

        Assert.Equal(AgentConnectionProbeStatus.TimedOut, result.Status);
    }

    [Fact]
    public async Task ProbeAsync_WhenProcessIsCancelled_ReportsCancelled()
    {
        var probe = new CodexAgentConnectionProbe(new TestExecutableLocator("codex"), new FakeLoginStatusRunner(new ProviderProcessResult(ProviderProcessOutcome.Cancelled, null, string.Empty, string.Empty, false, false)));

        var result = await probe.ProbeAsync(Agent(), WorkspacePath);

        Assert.Equal(AgentConnectionProbeStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task ProbeAsync_WhenSessionOutputIsInconclusive_ReportsUnavailable()
    {
        var probe = new CodexAgentConnectionProbe(new TestExecutableLocator("codex"), new FakeLoginStatusRunner(Success(string.Empty)));

        var result = await probe.ProbeAsync(Agent(), WorkspacePath);

        Assert.Equal(AgentConnectionProbeStatus.Unavailable, result.Status);
    }

    private static ProviderProcessResult Success(string standardOutput) =>
        new(ProviderProcessOutcome.ExitedSuccessfully, 0, standardOutput, string.Empty, false, false);

    private static AgentDefinition Agent()
    {
        var now = DateTimeOffset.UtcNow;
        return new AgentDefinition(
            Guid.NewGuid(),
            "GPT-5.6 Sol",
            "Planner",
            AgentConnectionMode.Unknown,
            AgentAvailability.Unknown,
            enabled: true,
            createdAt: now,
            updatedAt: now,
            provider: "OpenAI",
            roleCapabilities: [AgentRole.Planner],
            supportedConnectionModes: [],
            authenticationState: AgentAuthenticationState.Unknown,
            entitlementState: AgentEntitlementState.Unknown,
            modelIdentifier: "gpt-5.6-sol");
    }

    private sealed class FakeLoginStatusRunner(ProviderProcessResult result) : IProviderProcessRunner
    {
        public Task<ProviderProcessResult> RunAsync(ProviderProcessRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}
