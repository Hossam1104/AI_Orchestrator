namespace AIUsageMonitor.Application.Agents;

/// <summary>
/// Projects verified local connection/authentication truth into an agent's persisted global
/// identity, exactly once per still-unverified agent. It never invokes a model, never fabricates
/// availability or entitlement, and never strengthens a project override beyond global truth.
/// </summary>
public interface IAgentConnectionVerificationService
{
    /// <summary>
    /// Returns the agent unchanged when its connection mode is already decided, no exactly-one
    /// matching probe is registered for its provider, or the probe could not reach a definitive
    /// result. Otherwise probes once, persists the verified global truth, and returns the
    /// refreshed effective view.
    /// </summary>
    Task<EffectiveAgentDefinition> VerifyAsync(
        EffectiveAgentDefinition agent,
        string workspacePath,
        CancellationToken cancellationToken = default);
}
