using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Routing;

namespace AIUsageMonitor.Connection.Tests;

public sealed class ExecutableRoutingPolicyResolverTests
{
    [Fact]
    public async Task ResolverUsesProjectTypedPreferenceBeforeGlobalAndDefaultMetadata()
    {
        var projectId = Guid.NewGuid();
        var projectPreferred = Guid.NewGuid();
        var globalPreferred = Guid.NewGuid();
        var repository = new FakePolicyRepository
        {
            Global = new RoutingPolicy(null, true, null, null, null, null, DateTimeOffset.UtcNow, preferredAgentIds: [globalPreferred]),
            Project = new RoutingPolicy(null, null, null, null, null, null, DateTimeOffset.UtcNow, preferredAgentIds: [projectPreferred])
        };
        var resolver = new ExecutableRoutingPolicyResolver(repository, new DefaultAgentCatalog(), new HandoffRedactionService());

        var result = await resolver.ResolveAsync(
            projectId,
            new RoutingTaskClassification(RoutingScopeScale.Bounded, RoutingTaskRisk.High, RoutingBlastRadius.Module, RoutingValidationCost.Moderate, AgentRole.Executor));

        Assert.Equal(ExecutableRoutingPolicyResolutionStatus.Resolved, result.Status);
        Assert.Equal([projectPreferred], result.Policy!.PreferredAgentIds);
        Assert.True(result.Policy.IndependentReviewRequired);
    }

    [Fact]
    public async Task ResolverRejectsSecretShapedPolicyReference()
    {
        var resolver = new ExecutableRoutingPolicyResolver(new FakePolicyRepository(), new DefaultAgentCatalog(), new HandoffRedactionService());

        var result = await resolver.ResolveAsync(
            Guid.NewGuid(),
            new RoutingTaskClassification(RoutingScopeScale.Bounded, RoutingTaskRisk.Low, RoutingBlastRadius.Local, RoutingValidationCost.Low, AgentRole.Executor),
            "api_key=not-a-policy-reference");

        Assert.Equal(ExecutableRoutingPolicyResolutionStatus.InvalidRequest, result.Status);
        Assert.Null(result.Policy);
    }

    private sealed class FakePolicyRepository : IRoutingPolicyRepository
    {
        public RoutingPolicy? Global { get; init; }
        public RoutingPolicy? Project { get; init; }

        public Task<RoutingPolicy?> GetGlobalAsync(CancellationToken cancellationToken = default) => Task.FromResult(Global);
        public Task SaveGlobalAsync(RoutingPolicy policy, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<RoutingPolicy?> GetProjectOverrideAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(Project);
        public Task SaveProjectOverrideAsync(Guid projectId, RoutingPolicy policy, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
