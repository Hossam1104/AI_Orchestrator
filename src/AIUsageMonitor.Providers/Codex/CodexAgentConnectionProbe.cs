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
    private readonly IExecutableLocator _locator;
    private readonly IProviderProcessRunner _processes;

    public CodexAgentConnectionProbe(IExecutableLocator locator, IProviderProcessRunner processes)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
    }

    public string Provider => "OpenAI";

    public AgentConnectionMode ConnectionMode => AgentConnectionMode.Cli;

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
