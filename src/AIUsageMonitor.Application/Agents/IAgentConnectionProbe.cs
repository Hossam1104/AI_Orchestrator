namespace AIUsageMonitor.Application.Agents;

/// <summary>
/// Bounded, provider-independent outcome of one local connection probe. Probing verifies
/// authentication/session truth only; it never invokes a model.
/// </summary>
public enum AgentConnectionProbeStatus
{
    Authenticated,
    AuthenticationRequired,
    Unavailable,
    Cancelled,
    TimedOut
}

public sealed record AgentConnectionProbeResult(AgentConnectionProbeStatus Status, string? Message = null);

/// <summary>
/// A provider-specific, bounded local probe that verifies whether an agent's declared invocation
/// mode is genuinely reachable and authenticated. A probe never invokes a model and never fabricates
/// entitlement; it only reports the connection/authentication facts it can directly observe.
/// </summary>
public interface IAgentConnectionProbe
{
    /// <summary>The exact provider this probe verifies, matched case-insensitively against <see cref="AgentDefinition.Provider"/>.</summary>
    string Provider { get; }

    /// <summary>The connection mode this probe can confirm when authentication succeeds or is required.</summary>
    AgentConnectionMode ConnectionMode { get; }

    /// <summary>
    /// Returns whether this probe is authoritative for <paramref name="agent"/>'s identity. Sharing
    /// a provider is not sufficient: a probe owns a specific, deterministic scope (for example a
    /// stable built-in agent id plus its model identifier and intended role) and must fail closed
    /// for any agent outside that scope, including other agents that merely share its provider.
    /// Callers evaluate this against an agent's global (non-project-overridden) definition, so a
    /// project override can never broaden what a probe is willing to verify.
    /// </summary>
    bool CanProbe(AgentDefinition agent);

    Task<AgentConnectionProbeResult> ProbeAsync(
        AgentDefinition agent,
        string workspacePath,
        CancellationToken cancellationToken = default);
}
