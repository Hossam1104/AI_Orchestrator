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

    [Fact]
    public void ValidatorRejectsPlannerRoleThatCannotReachTheRequestedExecutorBoundary()
    {
        var request = new OrchestrationWorkRequest(
            Guid.NewGuid(), "owner:test", "Bounded work", "Do bounded work", acceptanceCriteria: ["Criterion"]);
        var reviewerPlan = new PlannerPlan(
            "Do bounded work", ["Scope"], ["Criterion"], [],
            [new PlanningValidationRequirement("test", PlanningValidationKind.Test, "Run tests", true)],
            new RoutingTaskClassification(RoutingScopeScale.Bounded, RoutingTaskRisk.Low, RoutingBlastRadius.Local, RoutingValidationCost.Low, AgentRole.Reviewer),
            [new PlanningStopCondition("target", PlanningStopConditionKind.ImmutableTargetMoved, "Stop if target changes"), new PlanningStopCondition("scope", PlanningStopConditionKind.ScopeViolation, "Stop if scope grows"), new PlanningStopCondition("budget", PlanningStopConditionKind.BudgetExceeded, "Stop if budget ends")],
            [new PlanningExecutionBudget(PlanningBudgetKind.Attempts, 1), new PlanningExecutionBudget(PlanningBudgetKind.ElapsedMinutes, 10)]);

        Assert.Contains("role", PlannerPlanValidator.Validate(reviewerPlan, request, Agent()), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatorAcceptsPlannerPlanThatEchoesTheCompleteCallerClassificationExactly()
    {
        var classification = new RoutingTaskClassification(
            RoutingScopeScale.Bounded, RoutingTaskRisk.Moderate, RoutingBlastRadius.Module, RoutingValidationCost.Moderate,
            AgentRole.Executor, requiredCapabilities: [], policyTags: [], capacityRequirement: RoutingCapacityRequirement.Optional);
        var request = new OrchestrationWorkRequest(
            Guid.NewGuid(), "owner:test", "Bounded work", "Do bounded work",
            acceptanceCriteria: ["Criterion"], classification: classification);

        var echoedClassification = new RoutingTaskClassification(
            classification.ScopeScale, classification.Risk, classification.BlastRadius, classification.ValidationCost,
            classification.RequiredRole, classification.RequiredCapabilities, classification.PolicyTags, classification.CapacityRequirement,
            classification.IndependentReviewRequired, classification.SecurityReviewRequired, classification.OwnerApprovalRequired,
            classification.RequiresSupportedConnection, classification.RequiresVerifiedAvailability, classification.RequiresAuthenticatedAccess,
            classification.RequiresVerifiedEntitlement);
        var plan = PlanWithClassification(["Criterion"], [], echoedClassification);

        var error = PlannerPlanValidator.Validate(plan, request, Agent());

        Assert.Null(error);
        Assert.Empty(plan.Classification.RequiredCapabilities);
        Assert.Equal(RoutingCapacityRequirement.Optional, plan.Classification.CapacityRequirement);
    }

    [Theory]
    [InlineData(new string[0], new[] { "bounded workspace file editing" })]
    [InlineData(new[] { "required-capability" }, new string[0])]
    public void ValidatorRejectsPlannerCapabilityDrift(string[] ownerCapabilities, string[] plannerCapabilities)
    {
        var classification = new RoutingTaskClassification(
            RoutingScopeScale.Bounded, RoutingTaskRisk.Moderate, RoutingBlastRadius.Module, RoutingValidationCost.Moderate,
            AgentRole.Executor, requiredCapabilities: ownerCapabilities, capacityRequirement: RoutingCapacityRequirement.Optional);
        var request = new OrchestrationWorkRequest(
            Guid.NewGuid(), "owner:test", "Bounded work", "Do bounded work",
            acceptanceCriteria: ["Criterion"], classification: classification);

        var driftedClassification = new RoutingTaskClassification(
            classification.ScopeScale, classification.Risk, classification.BlastRadius, classification.ValidationCost,
            classification.RequiredRole, requiredCapabilities: plannerCapabilities, capacityRequirement: classification.CapacityRequirement);
        var plan = PlanWithClassification(["Criterion"], [], driftedClassification);

        var error = PlannerPlanValidator.Validate(plan, request, Agent());

        Assert.Contains("preserve the caller-authoritative routing classification", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(RoutingCapacityRequirement.Optional, RoutingCapacityRequirement.Required)]
    [InlineData(RoutingCapacityRequirement.NotApplicable, RoutingCapacityRequirement.Required)]
    [InlineData(RoutingCapacityRequirement.Required, RoutingCapacityRequirement.Optional)]
    public void ValidatorRejectsPlannerCapacityDrift(RoutingCapacityRequirement ownerCapacity, RoutingCapacityRequirement plannerCapacity)
    {
        var classification = new RoutingTaskClassification(
            RoutingScopeScale.Bounded, RoutingTaskRisk.Moderate, RoutingBlastRadius.Module, RoutingValidationCost.Moderate,
            AgentRole.Executor, capacityRequirement: ownerCapacity);
        var request = new OrchestrationWorkRequest(
            Guid.NewGuid(), "owner:test", "Bounded work", "Do bounded work",
            acceptanceCriteria: ["Criterion"], classification: classification);

        var driftedClassification = new RoutingTaskClassification(
            classification.ScopeScale, classification.Risk, classification.BlastRadius, classification.ValidationCost,
            classification.RequiredRole, capacityRequirement: plannerCapacity);
        var plan = PlanWithClassification(["Criterion"], [], driftedClassification);

        var error = PlannerPlanValidator.Validate(plan, request, Agent());

        Assert.Contains("preserve the caller-authoritative routing classification", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, false, true)]
    public void ValidatorRejectsPlannerTrustGateDrift(bool ownerSupportedConnection, bool plannerSupportedConnection, bool ownerAuthenticated, bool plannerAuthenticated)
    {
        var classification = new RoutingTaskClassification(
            RoutingScopeScale.Bounded, RoutingTaskRisk.Moderate, RoutingBlastRadius.Module, RoutingValidationCost.Moderate,
            AgentRole.Executor, capacityRequirement: RoutingCapacityRequirement.Optional,
            requiresSupportedConnection: ownerSupportedConnection, requiresAuthenticatedAccess: ownerAuthenticated);
        var request = new OrchestrationWorkRequest(
            Guid.NewGuid(), "owner:test", "Bounded work", "Do bounded work",
            acceptanceCriteria: ["Criterion"], classification: classification);

        var driftedClassification = new RoutingTaskClassification(
            classification.ScopeScale, classification.Risk, classification.BlastRadius, classification.ValidationCost,
            classification.RequiredRole, capacityRequirement: classification.CapacityRequirement,
            requiresSupportedConnection: plannerSupportedConnection, requiresAuthenticatedAccess: plannerAuthenticated);
        var plan = PlanWithClassification(["Criterion"], [], driftedClassification);

        var error = PlannerPlanValidator.Validate(plan, request, Agent());

        Assert.Contains("preserve the caller-authoritative routing classification", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("scopeScale")]
    [InlineData("risk")]
    [InlineData("blastRadius")]
    [InlineData("validationCost")]
    [InlineData("policyTags")]
    [InlineData("independentReviewRequired")]
    [InlineData("securityReviewRequired")]
    [InlineData("ownerApprovalRequired")]
    public void ValidatorRejectsPlannerGovernanceFieldDrift(string field)
    {
        var classification = new RoutingTaskClassification(
            RoutingScopeScale.Bounded, RoutingTaskRisk.Moderate, RoutingBlastRadius.Module, RoutingValidationCost.Moderate,
            AgentRole.Executor, policyTags: ["owner-tag"], capacityRequirement: RoutingCapacityRequirement.Optional,
            independentReviewRequired: false, securityReviewRequired: false, ownerApprovalRequired: false);
        var request = new OrchestrationWorkRequest(
            Guid.NewGuid(), "owner:test", "Bounded work", "Do bounded work",
            acceptanceCriteria: ["Criterion"], classification: classification);

        var driftedClassification = field switch
        {
            "scopeScale" => new RoutingTaskClassification(RoutingScopeScale.CrossCutting, classification.Risk, classification.BlastRadius, classification.ValidationCost, classification.RequiredRole, policyTags: classification.PolicyTags, capacityRequirement: classification.CapacityRequirement),
            "risk" => new RoutingTaskClassification(classification.ScopeScale, RoutingTaskRisk.Critical, classification.BlastRadius, classification.ValidationCost, classification.RequiredRole, policyTags: classification.PolicyTags, capacityRequirement: classification.CapacityRequirement),
            "blastRadius" => new RoutingTaskClassification(classification.ScopeScale, classification.Risk, RoutingBlastRadius.ExternalSystem, classification.ValidationCost, classification.RequiredRole, policyTags: classification.PolicyTags, capacityRequirement: classification.CapacityRequirement),
            "validationCost" => new RoutingTaskClassification(classification.ScopeScale, classification.Risk, classification.BlastRadius, RoutingValidationCost.High, classification.RequiredRole, policyTags: classification.PolicyTags, capacityRequirement: classification.CapacityRequirement),
            "policyTags" => new RoutingTaskClassification(classification.ScopeScale, classification.Risk, classification.BlastRadius, classification.ValidationCost, classification.RequiredRole, policyTags: ["different-tag"], capacityRequirement: classification.CapacityRequirement),
            "independentReviewRequired" => new RoutingTaskClassification(classification.ScopeScale, classification.Risk, classification.BlastRadius, classification.ValidationCost, classification.RequiredRole, policyTags: classification.PolicyTags, capacityRequirement: classification.CapacityRequirement, independentReviewRequired: true),
            "securityReviewRequired" => new RoutingTaskClassification(classification.ScopeScale, classification.Risk, classification.BlastRadius, classification.ValidationCost, classification.RequiredRole, policyTags: classification.PolicyTags, capacityRequirement: classification.CapacityRequirement, securityReviewRequired: true),
            "ownerApprovalRequired" => new RoutingTaskClassification(classification.ScopeScale, classification.Risk, classification.BlastRadius, classification.ValidationCost, classification.RequiredRole, policyTags: classification.PolicyTags, capacityRequirement: classification.CapacityRequirement, ownerApprovalRequired: true),
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        var plan = PlanWithClassification(["Criterion"], [], driftedClassification);

        var error = PlannerPlanValidator.Validate(plan, request, Agent());

        Assert.Contains("preserve the caller-authoritative routing classification", error, StringComparison.OrdinalIgnoreCase);
    }

    private static PlannerPlan Plan(IReadOnlyList<string> criteria, IReadOnlyList<string> constraints) => PlanWithClassification(
        criteria,
        constraints,
        new RoutingTaskClassification(RoutingScopeScale.Bounded, RoutingTaskRisk.Low, RoutingBlastRadius.Local, RoutingValidationCost.Low, AgentRole.Executor));

    private static PlannerPlan PlanWithClassification(IReadOnlyList<string> criteria, IReadOnlyList<string> constraints, RoutingTaskClassification classification) => new(
        "Do bounded work",
        ["Scope"],
        criteria,
        constraints,
        [new PlanningValidationRequirement("test", PlanningValidationKind.Test, "Run tests", true)],
        classification,
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
