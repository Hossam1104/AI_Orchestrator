using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Handoffs;

namespace AIUsageMonitor.Application.Routing;

public enum ExecutableRoutingPolicyResolutionStatus
{
    Resolved,
    InvalidRequest,
    PersistenceUnavailable,
    InvalidPolicy
}

public sealed record ExecutableRoutingPolicyResolution(
    ExecutableRoutingPolicyResolutionStatus Status,
    RoutingPolicySnapshot? Policy = null,
    string? ErrorMessage = null)
{
    public bool Succeeded => Status == ExecutableRoutingPolicyResolutionStatus.Resolved && Policy is not null;
}

public interface IExecutableRoutingPolicyResolver
{
    Task<ExecutableRoutingPolicyResolution> ResolveAsync(
        Guid projectId,
        RoutingTaskClassification classification,
        string? contextPolicyReference = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The only application service that turns persisted routing policy plus planner classification
/// into executable policy. ExecutionCoordinator consumes this authority but does not assemble it.
/// </summary>
public sealed class ExecutableRoutingPolicyResolver : IExecutableRoutingPolicyResolver
{
    private readonly IRoutingPolicyRepository _policies;
    private readonly IDefaultAgentCatalog _catalog;
    private readonly IHandoffRedactionService _redaction;

    public ExecutableRoutingPolicyResolver(
        IRoutingPolicyRepository policies,
        IDefaultAgentCatalog catalog,
        IHandoffRedactionService redaction)
    {
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _redaction = redaction ?? throw new ArgumentNullException(nameof(redaction));
    }

    public async Task<ExecutableRoutingPolicyResolution> ResolveAsync(
        Guid projectId,
        RoutingTaskClassification classification,
        string? contextPolicyReference = null,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty || classification is null)
        {
            return new(ExecutableRoutingPolicyResolutionStatus.InvalidRequest, ErrorMessage: "A project and planner classification are required.");
        }

        if (contextPolicyReference is not null && _redaction.ValidateIdentityText(contextPolicyReference).RequiresRedaction)
        {
            return new(ExecutableRoutingPolicyResolutionStatus.InvalidRequest, ErrorMessage: "The routing policy reference crossed the redaction boundary.");
        }

        try
        {
            var global = await _policies.GetGlobalAsync(cancellationToken).ConfigureAwait(false);
            var projectOverride = await _policies.GetProjectOverrideAsync(projectId, cancellationToken).ConfigureAwait(false);
            var preferred = projectOverride?.PreferredAgentIds ?? global?.PreferredAgentIds ?? DefaultPreferredAgents(classification.RequiredRole);
            var prohibited = projectOverride?.ProhibitedAgentIds ?? global?.ProhibitedAgentIds ?? Array.Empty<Guid>();
            var highRisk = classification.Risk is RoutingTaskRisk.High or RoutingTaskRisk.Critical;
            var policyId = string.IsNullOrWhiteSpace(contextPolicyReference)
                ? $"runtime-routing:{projectId:D}"
                : contextPolicyReference.Trim();
            var policy = new RoutingPolicySnapshot(
                policyId,
                classification.RequiredRole,
                preferred,
                prohibited,
                classification.CapacityRequirement,
                independentReviewRequired: classification.IndependentReviewRequired ||
                    (highRisk && Effective(projectOverride, global, static value => value.RequireIndependentReviewForHighRisk) == true),
                securityReviewRequired: classification.SecurityReviewRequired,
                ownerApprovalRequired: classification.OwnerApprovalRequired ||
                    (highRisk && Effective(projectOverride, global, static value => value.RequireHumanApprovalForHighRisk) == true),
                requireSupportedConnection: classification.RequiresSupportedConnection,
                requireVerifiedAvailability: classification.RequiresVerifiedAvailability,
                requireAuthenticatedAccess: classification.RequiresAuthenticatedAccess,
                requireVerifiedEntitlement: classification.RequiresVerifiedEntitlement,
                policyReference: contextPolicyReference,
                reason: "Resolved from planner classification, global routing policy, and project override.");
            return new(ExecutableRoutingPolicyResolutionStatus.Resolved, policy);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return new(ExecutableRoutingPolicyResolutionStatus.InvalidPolicy, ErrorMessage: exception.Message);
        }
        catch (IOException exception)
        {
            return new(ExecutableRoutingPolicyResolutionStatus.PersistenceUnavailable, ErrorMessage: exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return new(ExecutableRoutingPolicyResolutionStatus.PersistenceUnavailable, ErrorMessage: exception.Message);
        }
    }

    private IReadOnlyList<Guid> DefaultPreferredAgents(AgentRole requiredRole) =>
        _catalog.GetDefaults()
            .Where(agent => agent.RoleCapabilities.Contains(requiredRole))
            .OrderBy(agent => agent.RolePolicyMetadata.Any(value => value.PreferenceLabel is "Primary") ? 0 : 1)
            .ThenBy(agent => agent.Name, StringComparer.Ordinal)
            .Select(agent => agent.Id)
            .Take(1)
            .ToArray();

    private static T? Effective<T>(
        RoutingPolicy? projectOverride,
        RoutingPolicy? global,
        Func<RoutingPolicy, T?> selector)
        where T : struct =>
        projectOverride is not null && selector(projectOverride).HasValue
            ? selector(projectOverride)
            : global is not null
                ? selector(global)
                : null;
}
