using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Routing;

namespace AIUsageMonitor.Application.Planning;

public sealed class PlannerInvocationRequest
{
    public PlannerInvocationRequest(
        EffectiveAgentDefinition planner,
        OrchestrationWorkRequest ownerRequest,
        string workspacePath,
        TimeSpan timeout)
    {
        Planner = planner ?? throw new ArgumentNullException(nameof(planner));
        OwnerRequest = ownerRequest ?? throw new ArgumentNullException(nameof(ownerRequest));
        WorkspacePath = Required(workspacePath, nameof(workspacePath), 2_000);
        if (!Path.IsPathFullyQualified(WorkspacePath))
        {
            throw new ArgumentException("The planner workspace must be absolute.", nameof(workspacePath));
        }

        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(30))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        Timeout = timeout;
    }

    public EffectiveAgentDefinition Planner { get; }
    public OrchestrationWorkRequest OwnerRequest { get; }
    public string WorkspacePath { get; }
    public TimeSpan Timeout { get; }

    private static string Required(string value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A bounded planner value is required.", parameterName);
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
    }
}

public enum PlannerInvocationStatus
{
    Succeeded,
    Unsupported,
    AdapterUnavailable,
    AuthenticationRequired,
    Failed,
    Cancelled,
    TimedOut,
    InvalidResult
}

public sealed record PlannerInvocationResult(
    PlannerInvocationStatus Status,
    PlannerPlan? Plan = null,
    string? ErrorMessage = null)
{
    public bool Succeeded => Status == PlannerInvocationStatus.Succeeded && Plan is not null;
}

/// <summary>
/// Provider-independent structured proposal returned by a planner. The constructor is the
/// application validation boundary for provider output; no generated command is represented.
/// </summary>
public sealed class PlannerPlan
{
    public PlannerPlan(
        string normalizedObjective,
        IReadOnlyList<string> includedScope,
        IReadOnlyList<string> acceptanceCriteria,
        IReadOnlyList<string> constraints,
        IReadOnlyList<PlanningValidationRequirement> validationExpectations,
        RoutingTaskClassification classification,
        IReadOnlyList<PlanningStopCondition> stopConditions,
        IReadOnlyList<PlanningExecutionBudget> executionBudgets)
    {
        NormalizedObjective = Required(normalizedObjective, nameof(normalizedObjective), 4_000);
        IncludedScope = TextList(includedScope, nameof(includedScope), required: true);
        AcceptanceCriteria = TextList(acceptanceCriteria, nameof(acceptanceCriteria), required: true);
        Constraints = TextList(constraints, nameof(constraints), required: false);
        ValidationExpectations = CopyList(validationExpectations, nameof(validationExpectations), required: true);
        if (ValidationExpectations.Any(static value => value is null))
        {
            throw new ArgumentException("Planner validation requirements cannot contain null entries.", nameof(validationExpectations));
        }
        Classification = classification ?? throw new ArgumentNullException(nameof(classification));
        StopConditions = stopConditions?.ToArray() ?? throw new ArgumentNullException(nameof(stopConditions));
        ExecutionBudgets = executionBudgets?.ToArray() ?? throw new ArgumentNullException(nameof(executionBudgets));

        if (StopConditions.Count == 0 || StopConditions.Count > 16 || StopConditions.Any(static value => value is null))
        {
            throw new ArgumentException("Planner stop conditions are invalid.", nameof(stopConditions));
        }

        if (new[]
            {
                PlanningStopConditionKind.ImmutableTargetMoved,
                PlanningStopConditionKind.ScopeViolation,
                PlanningStopConditionKind.BudgetExceeded
            }.Any(kind => StopConditions.All(value => value.Kind != kind)))
        {
            throw new ArgumentException("Planner output must include immutable-target, scope, and budget stop conditions.", nameof(stopConditions));
        }

        if (ExecutionBudgets.Count == 0 || ExecutionBudgets.Count > 16 || ExecutionBudgets.Any(static value => value is null))
        {
            throw new ArgumentException("Planner execution budgets are invalid.", nameof(executionBudgets));
        }

        var budgetKinds = ExecutionBudgets.Select(static value => value.Kind).ToArray();
        if (budgetKinds.Distinct().Count() != budgetKinds.Length ||
            ExecutionBudgets.Any(value => value.Limit > 1_000_000) ||
            !ExecutionBudgets.Any(static value => value.Kind == PlanningBudgetKind.Attempts) ||
            !ExecutionBudgets.Any(static value => value.Kind == PlanningBudgetKind.ElapsedMinutes))
        {
            throw new ArgumentException("Planner output must include bounded unique attempts and elapsed-time budgets.", nameof(executionBudgets));
        }

        var elapsed = ExecutionBudgets.Single(value => value.Kind == PlanningBudgetKind.ElapsedMinutes).Limit;
        if (elapsed > 240)
        {
            throw new ArgumentException("Planner elapsed-time budget exceeds the supported bound.", nameof(executionBudgets));
        }
    }

    public string NormalizedObjective { get; }
    public IReadOnlyList<string> IncludedScope { get; }
    public IReadOnlyList<string> AcceptanceCriteria { get; }
    public IReadOnlyList<string> Constraints { get; }
    public IReadOnlyList<PlanningValidationRequirement> ValidationExpectations { get; }
    public RoutingTaskClassification Classification { get; }
    public IReadOnlyList<PlanningStopCondition> StopConditions { get; }
    public IReadOnlyList<PlanningExecutionBudget> ExecutionBudgets { get; }

    private static IReadOnlyList<string> TextList(
        IReadOnlyList<string> values,
        string parameterName,
        bool required)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if (values.Count > 32)
        {
            throw new ArgumentException("Planner text lists cannot exceed 32 entries.", parameterName);
        }

        var normalized = values.Select(value => Required(value, parameterName, 4_000)).ToArray();
        if (required && normalized.Length == 0)
        {
            throw new ArgumentException("The planner must provide required bounded text.", parameterName);
        }

        return normalized;
    }

    private static IReadOnlyList<T> CopyList<T>(
        IReadOnlyList<T> values,
        string parameterName,
        bool required)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if (values.Count > 32)
        {
            throw new ArgumentException("Planner text lists cannot exceed 32 entries.", parameterName);
        }

        var normalized = values.ToArray();
        if (required && normalized.Length == 0)
        {
            throw new ArgumentException("The planner must provide required bounded text.", parameterName);
        }

        return normalized;
    }

    private static string Required(string value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A bounded planner value is required.", parameterName);
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
    }
}

public static class PlannerPlanValidator
{
    public static string? Validate(
        PlannerPlan plan,
        OrchestrationWorkRequest ownerRequest,
        EffectiveAgentDefinition planner)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(ownerRequest);
        ArgumentNullException.ThrowIfNull(planner);

        if (!planner.Enabled || !planner.RoleCapabilities.Contains(AgentRole.Planner))
        {
            return "The planner is not enabled with Planner capability.";
        }

        if (ownerRequest.AcceptanceCriteria.Any(criterion => !plan.AcceptanceCriteria.Contains(criterion, StringComparer.Ordinal)))
        {
            return "Planner output must preserve every owner acceptance criterion exactly.";
        }

        if (ownerRequest.Constraints.Any(constraint => !plan.Constraints.Contains(constraint, StringComparer.Ordinal)))
        {
            return "Planner output must preserve every owner constraint exactly.";
        }

        return null;
    }
}

public sealed class PlannerAdapterDescriptor
{
    public PlannerAdapterDescriptor(
        string adapterIdentifier,
        IReadOnlyList<AgentConnectionMode> supportedConnectionModes,
        IReadOnlyList<string>? supportedProviders = null,
        IReadOnlyList<string>? supportedModels = null)
    {
        AdapterIdentifier = Required(adapterIdentifier, nameof(adapterIdentifier), 200);
        ArgumentNullException.ThrowIfNull(supportedConnectionModes);
        if (supportedConnectionModes.Count == 0 || supportedConnectionModes.Any(value => !Enum.IsDefined(value)))
        {
            throw new ArgumentException("At least one planner connection mode is required.", nameof(supportedConnectionModes));
        }

        SupportedConnectionModes = supportedConnectionModes.Distinct().ToArray();
        SupportedProviders = Values(supportedProviders, nameof(supportedProviders));
        SupportedModels = Values(supportedModels, nameof(supportedModels));
    }

    public string AdapterIdentifier { get; }
    public IReadOnlyList<AgentConnectionMode> SupportedConnectionModes { get; }
    public IReadOnlyList<string> SupportedProviders { get; }
    public IReadOnlyList<string> SupportedModels { get; }

    public bool Matches(EffectiveAgentDefinition agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return SupportedConnectionModes.Contains(agent.ConnectionMode) &&
            (SupportedProviders.Count == 0 || (agent.Provider is not null && SupportedProviders.Contains(agent.Provider, StringComparer.OrdinalIgnoreCase))) &&
            (SupportedModels.Count == 0 || (agent.ModelIdentifier is not null && SupportedModels.Contains(agent.ModelIdentifier, StringComparer.Ordinal)));
    }

    private static IReadOnlyList<string> Values(IReadOnlyList<string>? values, string parameterName) =>
        (values ?? Array.Empty<string>()).Select(value => Required(value, parameterName, 300)).Distinct(StringComparer.Ordinal).ToArray();

    private static string Required(string value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A bounded planner value is required.", parameterName);
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
    }
}

public interface IPlannerAdapter
{
    PlannerAdapterDescriptor Descriptor { get; }

    Task<PlannerInvocationResult> PlanAsync(
        PlannerInvocationRequest request,
        CancellationToken cancellationToken = default);
}

public enum PlannerAdapterResolutionStatus
{
    Resolved,
    Unsupported,
    ConfigurationConflict
}

public sealed record PlannerAdapterResolution(
    PlannerAdapterResolutionStatus Status,
    IPlannerAdapter? Adapter = null,
    string? ErrorMessage = null)
{
    public bool Succeeded => Status == PlannerAdapterResolutionStatus.Resolved && Adapter is not null;
}

public interface IPlannerAdapterResolver
{
    PlannerAdapterResolution Resolve(EffectiveAgentDefinition planner);
}

public sealed class PlannerAdapterResolver : IPlannerAdapterResolver
{
    private readonly IReadOnlyList<IPlannerAdapter> _adapters;

    public PlannerAdapterResolver(IEnumerable<IPlannerAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        _adapters = adapters.ToArray();
    }

    public PlannerAdapterResolution Resolve(EffectiveAgentDefinition planner)
    {
        ArgumentNullException.ThrowIfNull(planner);
        var matches = _adapters
            .Where(adapter => adapter is not null && adapter.Descriptor.Matches(planner))
            .ToArray();
        return matches.Length switch
        {
            0 => new(PlannerAdapterResolutionStatus.Unsupported, ErrorMessage: "No exact bounded planner adapter is available."),
            1 => new(PlannerAdapterResolutionStatus.Resolved, matches[0]),
            _ => new(PlannerAdapterResolutionStatus.ConfigurationConflict, ErrorMessage: "More than one exact planner adapter matches the selected planner.")
        };
    }
}
