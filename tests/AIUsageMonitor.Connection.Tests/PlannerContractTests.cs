using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.Routing;

namespace AIUsageMonitor.Connection.Tests;

public sealed class PlannerContractTests
{
    [Fact]
    public void ValidatorRejectsPlannerThatDropsOwnerAcceptanceOrConstraint()
    {
        var request = new OrchestrationWorkRequest(
            Guid.NewGuid(),
            "owner:test",
            "Bounded work",
            "Do bounded work",
            acceptanceCriteria: ["Keep this criterion"],
            constraints: ["Keep this constraint"]);

        var plan = Plan(["Different criterion"], ["Different constraint"]);

        var error = PlannerPlanValidator.Validate(plan, request, Agent());

        Assert.Contains("preserve", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlannerPlanRequiresImmutableScopeAndBudgetStopsAndBoundedBudgets()
    {
        Assert.Throws<ArgumentException>(() => new PlannerPlan(
            "Objective",
            ["Scope"],
            ["Criterion"],
            [],
            [new PlanningValidationRequirement("test", PlanningValidationKind.Test, "Run tests", true)],
            new RoutingTaskClassification(RoutingScopeScale.Bounded, RoutingTaskRisk.Low, RoutingBlastRadius.Local, RoutingValidationCost.Low, AgentRole.Executor),
            [new PlanningStopCondition("scope", PlanningStopConditionKind.ScopeViolation, "Stop")],
            [new PlanningExecutionBudget(PlanningBudgetKind.Attempts, 1), new PlanningExecutionBudget(PlanningBudgetKind.ElapsedMinutes, 10)]));
    }

    private static PlannerPlan Plan(IReadOnlyList<string> criteria, IReadOnlyList<string> constraints) => new(
        "Do bounded work",
        ["Scope"],
        criteria,
        constraints,
        [new PlanningValidationRequirement("test", PlanningValidationKind.Test, "Run tests", true)],
        new RoutingTaskClassification(RoutingScopeScale.Bounded, RoutingTaskRisk.Low, RoutingBlastRadius.Local, RoutingValidationCost.Low, AgentRole.Executor),
        [
            new PlanningStopCondition("target", PlanningStopConditionKind.ImmutableTargetMoved, "Stop if target changes"),
            new PlanningStopCondition("scope", PlanningStopConditionKind.ScopeViolation, "Stop if scope grows"),
            new PlanningStopCondition("budget", PlanningStopConditionKind.BudgetExceeded, "Stop if budget ends")
        ],
        [new PlanningExecutionBudget(PlanningBudgetKind.Attempts, 1), new PlanningExecutionBudget(PlanningBudgetKind.ElapsedMinutes, 10)]);

    private static EffectiveAgentDefinition Agent()
    {
        var now = DateTimeOffset.UtcNow;
        return new EffectiveAgentDefinition(
            Guid.NewGuid(),
            new AgentDefinition(
                Guid.NewGuid(),
                "Planner",
                "Planner",
                AgentConnectionMode.Cli,
                AgentAvailability.Available,
                enabled: true,
                now,
                now,
                provider: "OpenAI",
                roleCapabilities: [AgentRole.Planner],
                supportedConnectionModes: [AgentConnectionMode.Cli],
                modelIdentifier: "gpt-test"),
            null);
    }
}
