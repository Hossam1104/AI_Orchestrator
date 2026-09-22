using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Providers.Common;

namespace AIUsageMonitor.Providers.Codex;

/// <summary>
/// Verifies local OpenAI Codex CLI session truth for orchestration-executor identity. Reuses the
/// same bounded <c>codex login status</c> probe the planner/executor adapters use at invocation
/// time, rather than duplicating it a third time alongside the separate AI-Providers monitoring
/// surface (<see cref="CodexProvider"/>).
/// </summary>
public sealed class CodexAgentConnectionProbe : IAgentConnectionProbe
{
    /// <summary>Stable built-in id of GPT-5.6 Sol in <see cref="DefaultAgentCatalog"/>.</summary>
    private static readonly Guid SolAgentId = Guid.Parse("b4c0b0d1-7f2c-4d4d-9f4d-000000000038");

    /// <summary>Stable built-in id of GPT-5.6 Luna xHigh in <see cref="DefaultAgentCatalog"/>.</summary>
    private static readonly Guid LunaAgentId = Guid.Parse("b4c0b0d1-7f2c-4d4d-9f4d-000000000039");

    private const string SolModelIdentifier = "gpt-5.6-sol";
    private const string LunaModelIdentifier = "gpt-5.6-luna";

    private readonly IExecutableLocator _locator;
    private readonly IProviderProcessRunner _processes;

    public CodexAgentConnectionProbe(IExecutableLocator locator, IProviderProcessRunner processes)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
    }

    public string Provider => "OpenAI";

    public AgentConnectionMode ConnectionMode => AgentConnectionMode.Cli;

    /// <summary>
    /// This probe is authoritative only for the exact built-in Sol and Luna identities: their
    /// stable catalog id, provider, model identifier, and intended role capability must all match,
    /// and the agent must still be enabled. Any other OpenAI-provider agent — including a custom
    /// agent an owner configures with an unrelated model identifier — fails closed and is never
    /// promoted by this probe, even though the local Codex CLI session it observes is shared
    /// infrastructure for every OpenAI-provider agent.
    /// </summary>
    public bool CanProbe(AgentDefinition agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        if (!agent.Enabled || !string.Equals(agent.Provider, Provider, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (agent.Id == SolAgentId)
        {
            return string.Equals(agent.ModelIdentifier, SolModelIdentifier, StringComparison.Ordinal) &&
                agent.RoleCapabilities.Contains(AgentRole.Planner);
        }

        if (agent.Id == LunaAgentId)
        {
            return string.Equals(agent.ModelIdentifier, LunaModelIdentifier, StringComparison.Ordinal) &&
                agent.RoleCapabilities.Contains(AgentRole.Executor);
        }

        return false;
    }

    public async Task<AgentConnectionProbeResult> ProbeAsync(
        AgentDefinition agent,
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        var session = await CodexLocalInvocation.VerifySessionAsync(_locator, _processes, workspacePath, cancellationToken)
            .ConfigureAwait(false);
        return session.Status switch
        {
            CodexSessionStatus.Authenticated => new(AgentConnectionProbeStatus.Authenticated, session.ErrorMessage),
            CodexSessionStatus.AuthenticationRequired => new(AgentConnectionProbeStatus.AuthenticationRequired, session.ErrorMessage),
            CodexSessionStatus.Cancelled => new(AgentConnectionProbeStatus.Cancelled, session.ErrorMessage),
            CodexSessionStatus.TimedOut => new(AgentConnectionProbeStatus.TimedOut, session.ErrorMessage),
            _ => new(AgentConnectionProbeStatus.Unavailable, session.ErrorMessage)
        };
    }
}
