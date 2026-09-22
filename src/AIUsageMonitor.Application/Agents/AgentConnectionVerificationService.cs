using AIUsageMonitor.Application.Time;

namespace AIUsageMonitor.Application.Agents;

/// <summary>
/// Bounded implementation of <see cref="IAgentConnectionVerificationService"/>. It never guesses:
/// an agent is probed only when its connection mode is still <see cref="AgentConnectionMode.Unknown"/>
/// and exactly one registered probe matches its provider; a probe result that is not a definitive
/// authentication outcome leaves the agent unverified for the next attempt rather than persisting a
/// fabricated fact.
/// </summary>
public sealed class AgentConnectionVerificationService : IAgentConnectionVerificationService
{
    private readonly IReadOnlyList<IAgentConnectionProbe> _probes;
    private readonly IAgentRepository _agents;
    private readonly IClock _clock;

    public AgentConnectionVerificationService(
        IEnumerable<IAgentConnectionProbe> probes,
        IAgentRepository agents,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(probes);
        _probes = probes.Where(static probe => probe is not null).ToArray();
        _agents = agents ?? throw new ArgumentNullException(nameof(agents));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<EffectiveAgentDefinition> VerifyAsync(
        EffectiveAgentDefinition agent,
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        if (agent.GlobalDefinition.ConnectionMode != AgentConnectionMode.Unknown ||
            string.IsNullOrWhiteSpace(agent.Provider))
        {
            return agent;
        }

        var matches = _probes
            .Where(probe => string.Equals(probe.Provider, agent.Provider, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
        {
            return agent;
        }

        AgentConnectionProbeResult probeResult;
        try
        {
            probeResult = await matches[0].ProbeAsync(agent.GlobalDefinition, workspacePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return agent;
        }

        if (probeResult.Status is not (AgentConnectionProbeStatus.Authenticated or AgentConnectionProbeStatus.AuthenticationRequired))
        {
            return agent;
        }

        var verifiedMode = matches[0].ConnectionMode;
        var availability = probeResult.Status == AgentConnectionProbeStatus.Authenticated
            ? AgentAvailability.Available
            : AgentAvailability.AuthenticationRequired;
        var authenticationState = probeResult.Status == AgentConnectionProbeStatus.Authenticated
            ? AgentAuthenticationState.Authenticated
            : AgentAuthenticationState.AuthenticationRequired;
        var now = _clock.UtcNow;

        var connectionResult = new AgentConnectionResult(
            agent.GlobalDefinition.Identity,
            now,
            verifiedMode,
            availability,
            authenticationState,
            AgentEntitlementState.Unknown,
            AgentEvidenceSource.OfficialCli,
            message: probeResult.Message,
            supportedConnectionModes: [verifiedMode]);

        var updatedGlobal = new AgentDefinition(
            agent.GlobalDefinition.Id,
            agent.GlobalDefinition.Name,
            agent.GlobalDefinition.Role,
            verifiedMode,
            availability,
            agent.GlobalDefinition.Enabled,
            agent.GlobalDefinition.CreatedAt,
            now,
            provider: agent.GlobalDefinition.Provider,
            capabilities: agent.GlobalDefinition.Capabilities,
            limitations: agent.GlobalDefinition.Limitations,
            costAndQuotaMetadata: agent.GlobalDefinition.CostAndQuotaMetadata,
            roleCapabilities: agent.GlobalDefinition.RoleCapabilities,
            supportedConnectionModes: agent.GlobalDefinition.SupportedConnectionModes.Contains(verifiedMode)
                ? agent.GlobalDefinition.SupportedConnectionModes
                : agent.GlobalDefinition.SupportedConnectionModes.Concat([verifiedMode]).ToArray(),
            authenticationState: authenticationState,
            entitlementState: agent.GlobalDefinition.EntitlementState,
            modelIdentifier: agent.GlobalDefinition.ModelIdentifier,
            rolePolicyMetadata: agent.GlobalDefinition.RolePolicyMetadata,
            lastConnectionResult: connectionResult);

        await _agents.UpsertAsync(updatedGlobal, cancellationToken).ConfigureAwait(false);
        return new EffectiveAgentDefinition(agent.ProjectId, updatedGlobal, agent.ProjectOverride);
    }
}
