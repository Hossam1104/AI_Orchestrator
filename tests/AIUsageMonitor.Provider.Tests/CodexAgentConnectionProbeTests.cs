using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Providers.Codex;

namespace AIUsageMonitor.Provider.Tests;

public sealed class CodexAgentConnectionProbeTests
{
    private const string WorkspacePath = @"C:\apo-test";
    private static readonly Guid SolAgentId = Guid.Parse("b4c0b0d1-7f2c-4d4d-9f4d-000000000038");
    private static readonly Guid LunaAgentId = Guid.Parse("b4c0b0d1-7f2c-4d4d-9f4d-000000000039");

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

    [Fact]
    public void CanProbe_WhenExactSolIdentity_ReturnsTrue()
    {
        var probe = new CodexAgentConnectionProbe(new TestExecutableLocator("codex"), new FakeLoginStatusRunner(Success("Logged in")));

        Assert.True(probe.CanProbe(SolAgent()));
    }

    [Fact]
    public void CanProbe_WhenExactLunaIdentity_ReturnsTrue()
    {
        var probe = new CodexAgentConnectionProbe(new TestExecutableLocator("codex"), new FakeLoginStatusRunner(Success("Logged in")));

        Assert.True(probe.CanProbe(LunaAgent()));
    }

    [Fact]
    public void CanProbe_WhenUnrelatedCustomOpenAIAgent_ReturnsFalse()
    {
        // Defect characterization (Finding B): an arbitrary custom OpenAI agent, unrelated to Sol
        // or Luna, must never become Codex-probeable merely because it shares provider "OpenAI".
        var probe = new CodexAgentConnectionProbe(new TestExecutableLocator("codex"), new FakeLoginStatusRunner(Success("Logged in")));

        Assert.False(probe.CanProbe(Agent()));
    }

    [Fact]
    public void CanProbe_WhenSolIdButWrongModelIdentifier_ReturnsFalse()
    {
        var probe = new CodexAgentConnectionProbe(new TestExecutableLocator("codex"), new FakeLoginStatusRunner(Success("Logged in")));
        var agent = IdentityAgent(SolAgentId, "OpenAI", "some-other-model", [AgentRole.Planner]);

        Assert.False(probe.CanProbe(agent));
    }

    [Fact]
    public void CanProbe_WhenSolIdButWrongProvider_ReturnsFalse()
    {
        var probe = new CodexAgentConnectionProbe(new TestExecutableLocator("codex"), new FakeLoginStatusRunner(Success("Logged in")));
        var agent = IdentityAgent(SolAgentId, "Anthropic", "gpt-5.6-sol", [AgentRole.Planner]);

        Assert.False(probe.CanProbe(agent));
    }

    [Fact]
    public void CanProbe_WhenSolIdButMissingPlannerRoleCapability_ReturnsFalse()
    {
        var probe = new CodexAgentConnectionProbe(new TestExecutableLocator("codex"), new FakeLoginStatusRunner(Success("Logged in")));
        var agent = IdentityAgent(SolAgentId, "OpenAI", "gpt-5.6-sol", [AgentRole.Executor]);

        Assert.False(probe.CanProbe(agent));
    }

    [Fact]
    public void CanProbe_WhenLunaIdButMissingExecutorRoleCapability_ReturnsFalse()
    {
        var probe = new CodexAgentConnectionProbe(new TestExecutableLocator("codex"), new FakeLoginStatusRunner(Success("Logged in")));
        var agent = IdentityAgent(LunaAgentId, "OpenAI", "gpt-5.6-luna", [AgentRole.Planner]);

        Assert.False(probe.CanProbe(agent));
    }

    [Fact]
    public void CanProbe_WhenExactSolIdentityButDisabled_ReturnsFalse()
    {
        var probe = new CodexAgentConnectionProbe(new TestExecutableLocator("codex"), new FakeLoginStatusRunner(Success("Logged in")));
        var agent = IdentityAgent(SolAgentId, "OpenAI", "gpt-5.6-sol", [AgentRole.Planner], enabled: false);

        Assert.False(probe.CanProbe(agent));
    }

    [Fact]
    public async Task VerificationService_WithRealCodexProbe_DoesNotPromoteUnrelatedCustomOpenAIAgent()
    {
        // Reproduces the pre-fix defect end-to-end with the real production probe wired into the
        // verification service: a custom OpenAI agent with an unrelated model identifier and a
        // random (non-catalog) id must receive zero probe invocations and remain unpersisted.
        var repository = new RecordingAgentRepository();
        var probe = new CodexAgentConnectionProbe(new TestExecutableLocator("codex"), new FakeLoginStatusRunner(Success("Logged in")));
        var service = new AgentConnectionVerificationService([probe], repository, new FixedClock(DateTimeOffset.UtcNow));
        var effective = new EffectiveAgentDefinition(Guid.NewGuid(), Agent(), null);

        var result = await service.VerifyAsync(effective, WorkspacePath);

        Assert.Equal(AgentConnectionMode.Unknown, result.ConnectionMode);
        Assert.Same(effective, result);
        Assert.Null(repository.LastUpserted);
    }

    [Fact]
    public async Task VerificationService_WithRealCodexProbe_PromotesExactSolIdentity()
    {
        var repository = new RecordingAgentRepository();
        var probe = new CodexAgentConnectionProbe(new TestExecutableLocator("codex"), new FakeLoginStatusRunner(Success("Logged in")));
        var service = new AgentConnectionVerificationService([probe], repository, new FixedClock(DateTimeOffset.UtcNow));
        var effective = new EffectiveAgentDefinition(Guid.NewGuid(), SolAgent(), null);

        var result = await service.VerifyAsync(effective, WorkspacePath);

        Assert.Equal(AgentConnectionMode.Cli, result.ConnectionMode);
        Assert.Equal(AgentAvailability.Available, result.Availability);
        Assert.NotNull(repository.LastUpserted);
        Assert.Equal(SolAgentId, repository.LastUpserted!.Id);
    }

    [Fact]
    public async Task VerificationService_WithRealCodexProbe_PromotesExactLunaIdentity()
    {
        var repository = new RecordingAgentRepository();
        var probe = new CodexAgentConnectionProbe(new TestExecutableLocator("codex"), new FakeLoginStatusRunner(Success("Logged in")));
        var service = new AgentConnectionVerificationService([probe], repository, new FixedClock(DateTimeOffset.UtcNow));
        var effective = new EffectiveAgentDefinition(Guid.NewGuid(), LunaAgent(), null);

        var result = await service.VerifyAsync(effective, WorkspacePath);

        Assert.Equal(AgentConnectionMode.Cli, result.ConnectionMode);
        Assert.Equal(AgentAvailability.Available, result.Availability);
        Assert.NotNull(repository.LastUpserted);
        Assert.Equal(LunaAgentId, repository.LastUpserted!.Id);
    }

    private sealed class FixedClock(DateTimeOffset value) : IClock { public DateTimeOffset UtcNow { get; } = value; }

    private sealed class RecordingAgentRepository : IAgentRepository
    {
        internal AgentDefinition? LastUpserted;
        public Task<IReadOnlyList<AgentDefinition>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AgentDefinition>>([]);
        public Task<AgentDefinition?> GetByIdAsync(Guid agentId, CancellationToken cancellationToken = default) => Task.FromResult<AgentDefinition?>(null);
        public Task UpsertAsync(AgentDefinition agent, CancellationToken cancellationToken = default)
        {
            LastUpserted = agent;
            return Task.CompletedTask;
        }
    }

    private static AgentDefinition SolAgent() => IdentityAgent(SolAgentId, "OpenAI", "gpt-5.6-sol", [AgentRole.Planner]);

    private static AgentDefinition LunaAgent() => IdentityAgent(LunaAgentId, "OpenAI", "gpt-5.6-luna", [AgentRole.Executor]);

    private static AgentDefinition IdentityAgent(Guid id, string provider, string? modelIdentifier, IReadOnlyList<AgentRole> roleCapabilities, bool enabled = true)
    {
        // Fixed in the past (not DateTimeOffset.UtcNow) so it always precedes whatever "now" the
        // verification service's clock observes when it promotes and persists this identity.
        var now = DateTimeOffset.UnixEpoch;
        return new AgentDefinition(
            id,
            "Identity Test Agent",
            roleCapabilities.Count > 0 ? roleCapabilities[0].ToString() : "Planner",
            AgentConnectionMode.Unknown,
            AgentAvailability.Unknown,
            enabled: enabled,
            createdAt: now,
            updatedAt: now,
            provider: provider,
            roleCapabilities: roleCapabilities,
            supportedConnectionModes: [],
            authenticationState: AgentAuthenticationState.Unknown,
            entitlementState: AgentEntitlementState.Unknown,
            modelIdentifier: modelIdentifier);
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
