using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Time;

namespace AIUsageMonitor.Connection.Tests;

public sealed class AgentConnectionVerificationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);
    private const string WorkspacePath = @"C:\apo-test";

    [Fact]
    public async Task VerifyAsync_WhenAuthenticated_PromotesConnectionModeAndPersists()
    {
        var repository = new RecordingAgentRepository();
        var probe = new FakeProbe(AgentConnectionProbeStatus.Authenticated);
        var service = new AgentConnectionVerificationService([probe], repository, new FixedClock(Now));
        var agent = UnverifiedAgent();

        var result = await service.VerifyAsync(agent, WorkspacePath);

        Assert.Equal(1, probe.Calls);
        Assert.Equal(AgentConnectionMode.Cli, result.ConnectionMode);
        Assert.Equal(AgentAvailability.Available, result.Availability);
        Assert.Equal(AgentAuthenticationState.Authenticated, result.AuthenticationState);
        Assert.NotNull(repository.LastUpserted);
        Assert.Equal(AgentConnectionMode.Cli, repository.LastUpserted!.ConnectionMode);
        Assert.NotNull(result.LastConnectionResult);
        Assert.Equal(AgentEvidenceSource.OfficialCli, result.LastConnectionResult!.EvidenceSource);
    }

    [Fact]
    public async Task VerifyAsync_WhenAuthenticationRequired_ConfirmsInvocationModeButBlocksAvailability()
    {
        var repository = new RecordingAgentRepository();
        var probe = new FakeProbe(AgentConnectionProbeStatus.AuthenticationRequired);
        var service = new AgentConnectionVerificationService([probe], repository, new FixedClock(Now));
        var agent = UnverifiedAgent();

        var result = await service.VerifyAsync(agent, WorkspacePath);

        Assert.Equal(AgentConnectionMode.Cli, result.ConnectionMode);
        Assert.Equal(AgentAvailability.AuthenticationRequired, result.Availability);
        Assert.Equal(AgentAuthenticationState.AuthenticationRequired, result.AuthenticationState);
        Assert.NotNull(repository.LastUpserted);
    }

    [Theory]
    [InlineData(AgentConnectionProbeStatus.Unavailable)]
    [InlineData(AgentConnectionProbeStatus.Cancelled)]
    [InlineData(AgentConnectionProbeStatus.TimedOut)]
    public async Task VerifyAsync_WhenProbeIsNotDefinitive_LeavesAgentUnknownAndDoesNotPersist(AgentConnectionProbeStatus status)
    {
        var repository = new RecordingAgentRepository();
        var probe = new FakeProbe(status);
        var service = new AgentConnectionVerificationService([probe], repository, new FixedClock(Now));
        var agent = UnverifiedAgent();

        var result = await service.VerifyAsync(agent, WorkspacePath);

        Assert.Equal(AgentConnectionMode.Unknown, result.ConnectionMode);
        Assert.Same(agent, result);
        Assert.Null(repository.LastUpserted);
    }

    [Fact]
    public async Task VerifyAsync_NeverFabricatesEntitlement()
    {
        var repository = new RecordingAgentRepository();
        var probe = new FakeProbe(AgentConnectionProbeStatus.Authenticated);
        var service = new AgentConnectionVerificationService([probe], repository, new FixedClock(Now));
        var agent = UnverifiedAgent();

        var result = await service.VerifyAsync(agent, WorkspacePath);

        Assert.Equal(AgentEntitlementState.Unknown, result.EntitlementState);
    }

    [Fact]
    public async Task VerifyAsync_WhenAlreadyDecided_SkipsTheProbeEntirely()
    {
        var repository = new RecordingAgentRepository();
        var probe = new FakeProbe(AgentConnectionProbeStatus.Authenticated);
        var service = new AgentConnectionVerificationService([probe], repository, new FixedClock(Now));
        var agent = UnverifiedAgent();

        var first = await service.VerifyAsync(agent, WorkspacePath);
        repository.LastUpserted = null;
        var second = await service.VerifyAsync(first, WorkspacePath);

        Assert.Equal(1, probe.Calls);
        Assert.Same(first, second);
        Assert.Null(repository.LastUpserted);
    }

    [Fact]
    public async Task VerifyAsync_WhenNoProbeMatchesProvider_ReturnsAgentUnchanged()
    {
        var repository = new RecordingAgentRepository();
        var probe = new FakeProbe(AgentConnectionProbeStatus.Authenticated, provider: "Anthropic");
        var service = new AgentConnectionVerificationService([probe], repository, new FixedClock(Now));
        var agent = UnverifiedAgent();

        var result = await service.VerifyAsync(agent, WorkspacePath);

        Assert.Equal(0, probe.Calls);
        Assert.Same(agent, result);
        Assert.Null(repository.LastUpserted);
    }

    [Fact]
    public async Task VerifyAsync_WhenMultipleProbesMatchProvider_DoesNotGuessAndLeavesAgentUnverified()
    {
        var repository = new RecordingAgentRepository();
        var first = new FakeProbe(AgentConnectionProbeStatus.Authenticated);
        var second = new FakeProbe(AgentConnectionProbeStatus.Authenticated);
        var service = new AgentConnectionVerificationService([first, second], repository, new FixedClock(Now));
        var agent = UnverifiedAgent();

        var result = await service.VerifyAsync(agent, WorkspacePath);

        Assert.Equal(0, first.Calls);
        Assert.Equal(0, second.Calls);
        Assert.Same(agent, result);
        Assert.Null(repository.LastUpserted);
    }

    [Fact]
    public async Task VerifyAsync_WhenProbeSharesProviderButDeclaresItselfNotApplicable_ReturnsAgentUnchanged()
    {
        // Finding B regression: sharing a provider is not sufficient. A probe that explicitly
        // reports it is not authoritative for this specific agent identity must never be invoked,
        // even though its Provider string matches.
        var repository = new RecordingAgentRepository();
        var probe = new FakeProbe(AgentConnectionProbeStatus.Authenticated, canProbe: static _ => false);
        var service = new AgentConnectionVerificationService([probe], repository, new FixedClock(Now));
        var agent = UnverifiedAgent();

        var result = await service.VerifyAsync(agent, WorkspacePath);

        Assert.Equal(0, probe.Calls);
        Assert.Equal(AgentConnectionMode.Unknown, result.ConnectionMode);
        Assert.Same(agent, result);
        Assert.Null(repository.LastUpserted);
    }

    [Fact]
    public async Task VerifyAsync_EvaluatesApplicabilityAgainstGlobalDefinitionNotProjectOverride()
    {
        // A project override must never broaden what a probe is willing to verify: applicability is
        // decided from agent.GlobalDefinition, which is exactly what CanProbe receives here.
        var repository = new RecordingAgentRepository();
        AgentDefinition? observed = null;
        var probe = new FakeProbe(AgentConnectionProbeStatus.Authenticated, canProbe: agent =>
        {
            observed = agent;
            return true;
        });
        var service = new AgentConnectionVerificationService([probe], repository, new FixedClock(Now));
        var agent = UnverifiedAgent();

        await service.VerifyAsync(agent, WorkspacePath);

        Assert.NotNull(observed);
        Assert.Same(agent.GlobalDefinition, observed);
    }

    private static EffectiveAgentDefinition UnverifiedAgent()
    {
        var global = new AgentDefinition(
            Guid.NewGuid(),
            "GPT-5.6 Sol",
            "Planner",
            AgentConnectionMode.Unknown,
            AgentAvailability.Unknown,
            enabled: true,
            createdAt: Now,
            updatedAt: Now,
            provider: "OpenAI",
            roleCapabilities: [AgentRole.Planner],
            supportedConnectionModes: [],
            authenticationState: AgentAuthenticationState.Unknown,
            entitlementState: AgentEntitlementState.Unknown,
            modelIdentifier: "gpt-5.6-sol");
        return new EffectiveAgentDefinition(Guid.NewGuid(), global, null);
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

    private sealed class FakeProbe(AgentConnectionProbeStatus status, string provider = "OpenAI", Func<AgentDefinition, bool>? canProbe = null) : IAgentConnectionProbe
    {
        internal int Calls;
        public string Provider { get; } = provider;
        public AgentConnectionMode ConnectionMode => AgentConnectionMode.Cli;
        public bool CanProbe(AgentDefinition agent) =>
            canProbe?.Invoke(agent) ?? string.Equals(Provider, agent.Provider, StringComparison.OrdinalIgnoreCase);
        public Task<AgentConnectionProbeResult> ProbeAsync(AgentDefinition agent, string workspacePath, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new AgentConnectionProbeResult(status, "test-evidence"));
        }
    }
}
