using AIUsageMonitor.Application.Common;

namespace AIUsageMonitor.Application.Routing;

/// <summary>
/// Persisted routing/safety policy values. A project override may leave values unset so a future
/// routing engine can resolve them against the global policy; APO-27 does not execute the policy.
/// </summary>
public sealed class RoutingPolicy
{
    public const int MaximumPreferredAgents = 64;
    public const int MaximumProhibitedAgents = 64;

    public RoutingPolicy(
        bool? qualityRiskFirst,
        bool? requireIndependentReviewForHighRisk,
        bool? requireHumanApprovalForHighRisk,
        int? maxConcurrentRuns,
        int? maxRetries,
        int? maxReviewRemediationCycles,
        DateTimeOffset updatedAt,
        IReadOnlyDictionary<string, string?>? rules = null,
        IReadOnlyList<Guid>? preferredAgentIds = null,
        IReadOnlyList<Guid>? prohibitedAgentIds = null)
    {
        ValidateNonNegative(maxConcurrentRuns, nameof(maxConcurrentRuns));
        ValidateNonNegative(maxRetries, nameof(maxRetries));
        ValidateNonNegative(maxReviewRemediationCycles, nameof(maxReviewRemediationCycles));

        QualityRiskFirst = qualityRiskFirst;
        RequireIndependentReviewForHighRisk = requireIndependentReviewForHighRisk;
        RequireHumanApprovalForHighRisk = requireHumanApprovalForHighRisk;
        MaxConcurrentRuns = maxConcurrentRuns;
        MaxRetries = maxRetries;
        MaxReviewRemediationCycles = maxReviewRemediationCycles;
        Rules = MetadataValidation.Copy(rules);
        PreferredAgentIds = NormalizeAgentIds(preferredAgentIds, MaximumPreferredAgents, nameof(preferredAgentIds), preserveOrder: true);
        ProhibitedAgentIds = NormalizeAgentIds(prohibitedAgentIds, MaximumProhibitedAgents, nameof(prohibitedAgentIds), preserveOrder: false);
        if (PreferredAgentIds is not null && ProhibitedAgentIds is not null &&
            PreferredAgentIds.Any(ProhibitedAgentIds.Contains))
        {
            throw new ArgumentException("An agent cannot be both preferred and prohibited.", nameof(preferredAgentIds));
        }
        UpdatedAt = updatedAt;
    }

    public bool? QualityRiskFirst { get; }

    public bool? RequireIndependentReviewForHighRisk { get; }

    public bool? RequireHumanApprovalForHighRisk { get; }

    public int? MaxConcurrentRuns { get; }

    public int? MaxRetries { get; }

    public int? MaxReviewRemediationCycles { get; }

    public IReadOnlyDictionary<string, string?> Rules { get; }

    /// <summary>Typed policy authority; null means inherit the global value for project overrides.</summary>
    public IReadOnlyList<Guid>? PreferredAgentIds { get; }

    /// <summary>Typed policy authority; null means inherit the global value for project overrides.</summary>
    public IReadOnlyList<Guid>? ProhibitedAgentIds { get; }

    public DateTimeOffset UpdatedAt { get; }

    private static void ValidateNonNegative(int? value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Policy limits cannot be negative.");
        }
    }

    private static IReadOnlyList<Guid>? NormalizeAgentIds(
        IReadOnlyList<Guid>? values,
        int maximum,
        string parameterName,
        bool preserveOrder)
    {
        if (values is null)
        {
            return null;
        }

        if (values.Count > maximum)
        {
            throw new ArgumentException("Routing policy contains too many agent ids.", parameterName);
        }

        var result = new List<Guid>(values.Count);
        foreach (var value in values)
        {
            if (value == Guid.Empty || result.Contains(value))
            {
                throw new ArgumentException("Routing policy agent ids must be non-empty and unique.", parameterName);
            }

            result.Add(value);
        }

        if (!preserveOrder)
        {
            result.Sort();
        }

        return result.AsReadOnly();
    }
}
