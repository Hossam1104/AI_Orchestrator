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

    Task<AgentConnectionProbeResult> ProbeAsync(
        AgentDefinition agent,
        string workspacePath,
        CancellationToken cancellationToken = default);
}
