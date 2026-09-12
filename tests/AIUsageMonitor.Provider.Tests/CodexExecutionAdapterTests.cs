using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Providers.Common;
using AIUsageMonitor.Providers.Codex;

namespace AIUsageMonitor.Provider.Tests;

public sealed class CodexExecutionAdapterTests
{
    [Fact]
    public async Task Planner_UsesDirectExecutableExplicitModelAndBoundedReadOnlyInvocation()
    {
        var workspace = Directory.CreateTempSubdirectory("apo-planner-test-");
        try
        {
        var runner = new FakeProcessRunner(PlannerJson());
        var adapter = new CodexPlannerAdapter(new FakeLocator("C:\\tools\\codex.exe"), runner, new AIUsageMonitor.Application.Handoffs.HandoffRedactionService());
        var request = new AIUsageMonitor.Application.Planning.PlannerInvocationRequest(
            Agent(AgentRole.Planner),
            new OrchestrationWorkRequest(
                Guid.NewGuid(),
                "owner:test",
                "Bounded planner request",
                "Inspect the bounded workspace",
                acceptanceCriteria: ["Preserve the exact criterion"],
                constraints: ["Do not leave the workspace"],
                validationExpectations: ["Run focused tests"]),
            workspace.FullName,
            TimeSpan.FromSeconds(30));

        var result = await adapter.PlanAsync(request);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var invocation = Assert.Single(runner.Requests, value => value.Arguments.Contains("exec"));
        Assert.Equal("C:\\tools\\codex.exe", invocation.ExecutablePath);
        Assert.Equal(workspace.FullName, invocation.WorkingDirectory);
        Assert.Equal("gpt-test", invocation.Arguments[Array.IndexOf(invocation.Arguments.ToArray(), "-m") + 1]);
        Assert.Contains("read-only", invocation.Arguments);
        Assert.Contains("--output-schema", invocation.Arguments);
        Assert.Contains("-o", invocation.Arguments);
        Assert.DoesNotContain("cmd.exe", invocation.Arguments, StringComparer.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(workspace.FullName, recursive: true); }
    }

    [Fact]
    public async Task Planner_RejectsUnexpectedOutputProperties()
    {
        var workspace = Directory.CreateTempSubdirectory("apo-planner-test-");
        try
        {
        var runner = new FakeProcessRunner(PlannerJson(withExtra: true));
        var adapter = new CodexPlannerAdapter(new FakeLocator("C:\\tools\\codex.exe"), runner, new AIUsageMonitor.Application.Handoffs.HandoffRedactionService());

        var result = await adapter.PlanAsync(new AIUsageMonitor.Application.Planning.PlannerInvocationRequest(
            Agent(AgentRole.Planner),
            new OrchestrationWorkRequest(Guid.NewGuid(), "owner:test", "Title", "Objective", acceptanceCriteria: ["Criterion"]),
            workspace.FullName,
            TimeSpan.FromSeconds(30)));

        Assert.Equal(AIUsageMonitor.Application.Planning.PlannerInvocationStatus.InvalidResult, result.Status);
        Assert.Null(result.Plan);
        }
        finally { Directory.Delete(workspace.FullName, recursive: true); }
    }

    [Fact]
    public async Task Planner_DoesNotInvokeWrapperExecutable()
    {
        var workspace = Directory.CreateTempSubdirectory("apo-planner-test-");
        try
        {
        var runner = new FakeProcessRunner(PlannerJson());
        var adapter = new CodexPlannerAdapter(new FakeLocator("C:\\tools\\codex.cmd"), runner, new AIUsageMonitor.Application.Handoffs.HandoffRedactionService());

        var result = await adapter.PlanAsync(new AIUsageMonitor.Application.Planning.PlannerInvocationRequest(
            Agent(AgentRole.Planner),
            new OrchestrationWorkRequest(Guid.NewGuid(), "owner:test", "Title", "Objective", acceptanceCriteria: ["Criterion"]),
            workspace.FullName,
            TimeSpan.FromSeconds(30)));

        Assert.Equal(AIUsageMonitor.Application.Planning.PlannerInvocationStatus.AdapterUnavailable, result.Status);
        Assert.Empty(runner.Requests);
        }
        finally { Directory.Delete(workspace.FullName, recursive: true); }
    }

    private static EffectiveAgentDefinition Agent(AgentRole role)
    {
        var now = DateTimeOffset.UtcNow;
        var definition = new AgentDefinition(
            Guid.NewGuid(),
            "Configured Codex",
            role.ToString(),
            AgentConnectionMode.Cli,
            AgentAvailability.Available,
            enabled: true,
            now,
            now,
            provider: "OpenAI",
            roleCapabilities: [role],
            supportedConnectionModes: [AgentConnectionMode.Cli],
            authenticationState: AgentAuthenticationState.Authenticated,
            entitlementState: AgentEntitlementState.VerifiedAvailable,
            modelIdentifier: "gpt-test");
        return new EffectiveAgentDefinition(Guid.NewGuid(), definition, null);
    }

    private static string PlannerJson(bool withExtra = false) =>
        "{" +
        "\"normalizedObjective\":\"Inspect the bounded workspace\",\"includedScope\":[\"the prepared workspace\"]," +
        "\"acceptanceCriteria\":[\"Preserve the exact criterion\"],\"constraints\":[\"Do not leave the workspace\"]," +
        "\"validationExpectations\":[{\"kind\":\"test\",\"description\":\"Run focused tests\",\"required\":true}]," +
        "\"classification\":{" +
        "\"scopeScale\":\"bounded\",\"risk\":\"moderate\",\"blastRadius\":\"module\",\"validationCost\":\"moderate\",\"requiredRole\":\"executor\",\"requiredCapabilities\":[],\"policyTags\":[],\"capacityRequirement\":\"optional\",\"independentReviewRequired\":false,\"securityReviewRequired\":false,\"ownerApprovalRequired\":false,\"requiresSupportedConnection\":true,\"requiresVerifiedAvailability\":false,\"requiresAuthenticatedAccess\":true,\"requiresVerifiedEntitlement\":false}," +
        "\"stopConditions\":[{\"conditionId\":\"target\",\"kind\":\"immutableTargetMoved\",\"description\":\"Stop if target moves\"},{\"conditionId\":\"scope\",\"kind\":\"scopeViolation\",\"description\":\"Stop if scope grows\"},{\"conditionId\":\"budget\",\"kind\":\"budgetExceeded\",\"description\":\"Stop if budget ends\"}]," +
        "\"executionBudgets\":[{\"kind\":\"attempts\",\"limit\":1},{\"kind\":\"elapsedMinutes\",\"limit\":10}]" +
        (withExtra ? ",\"extra\":true" : "") + "}";

    private sealed class FakeLocator(string path) : IExecutableLocator
    {
        public string? Find(string commandName) => path;
    }

    private sealed class FakeProcessRunner(string output) : IProviderProcessRunner
    {
        public List<ProviderProcessRequest> Requests { get; } = [];

        public Task<ProviderProcessResult> RunAsync(ProviderProcessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (request.Arguments.SequenceEqual(["login", "status"]))
            {
                return Task.FromResult(Success("Logged in"));
            }

            var outputPath = request.Arguments[Array.IndexOf(request.Arguments.ToArray(), "-o") + 1];
            File.WriteAllText(outputPath, output);
            return Task.FromResult(Success(string.Empty));
        }

        private static ProviderProcessResult Success(string standardOutput) => new(
            ProviderProcessOutcome.ExitedSuccessfully,
            0,
            standardOutput,
            string.Empty,
            false,
            false);
    }
}
