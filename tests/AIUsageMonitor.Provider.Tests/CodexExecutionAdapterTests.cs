using AIUsageMonitor.Application.Agents;
using System.Text.Json;
using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Routing;
using AIUsageMonitor.Application.Workspaces;
using AIUsageMonitor.Providers.Common;
using AIUsageMonitor.Providers.Codex;

namespace AIUsageMonitor.Provider.Tests;

public sealed class CodexExecutionAdapterTests
{
    [Fact]
    public void PlannerSchemaIsValidJson() => JsonDocument.Parse(CodexLocalInvocation.PlannerSchema).Dispose();

    [Fact]
    public void PlannerAndExecutionSchemasAreStrictStructuredOutputCompatible()
    {
        AssertStrictSchema(CodexLocalInvocation.PlannerSchema, "PlannerSchema");
        AssertStrictSchema(CodexLocalInvocation.ExecutionSchema, "ExecutionSchema");
    }

    [Fact]
    public void StrictSchemasPreserveNullableOptionalSemantics()
    {
        using var planner = JsonDocument.Parse(CodexLocalInvocation.PlannerSchema);
        var validationProperties = planner.RootElement
            .GetProperty("properties")
            .GetProperty("validationExpectations")
            .GetProperty("items")
            .GetProperty("properties");
        AssertNullableType(validationProperties, "commandOrReference");

        using var execution = JsonDocument.Parse(CodexLocalInvocation.ExecutionSchema);
        var executionProperties = execution.RootElement.GetProperty("properties");
        foreach (var name in new[] { "stopReason", "toolInvocations", "modelTurns", "changedFiles", "changedLines" })
        {
            AssertNullableType(executionProperties, name);
        }
    }

    [Fact]
    public void PlannerPrompt_RepresentsTheAuthoritativeCallerClassificationAndInstructsPreservation()
    {
        var classification = new RoutingTaskClassification(
            RoutingScopeScale.Bounded, RoutingTaskRisk.Moderate, RoutingBlastRadius.Module, RoutingValidationCost.Moderate,
            AgentRole.Executor, requiredCapabilities: ["repository-read"], capacityRequirement: RoutingCapacityRequirement.Optional,
            requiresAuthenticatedAccess: true, requiresVerifiedEntitlement: true);
        var request = new OrchestrationWorkRequest(
            Guid.NewGuid(), "owner:test", "Bounded work", "Do bounded work",
            acceptanceCriteria: ["Criterion"], classification: classification);

        var prompt = CodexPromptBuilder.BuildPlannerPrompt(request, @"C:\apo-test", new HandoffRedactionService());

        Assert.Contains("\"requiredRole\":\"Executor\"", prompt);
        Assert.Contains("\"repository-read\"", prompt);
        Assert.Contains("\"capacityRequirement\":\"Optional\"", prompt);
        Assert.Contains("\"requiresAuthenticatedAccess\":true", prompt);
        Assert.Contains("\"requiresVerifiedEntitlement\":true", prompt);
        Assert.Contains("unchanged", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("caller/control-plane authority", prompt, StringComparison.OrdinalIgnoreCase);
    }

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
            Assert.Equal("exec", invocation.Arguments[2]);
            Assert.Equal("never", invocation.Arguments[Array.IndexOf(invocation.Arguments.ToArray(), "-a") + 1]);
            Assert.Contains("--output-schema", invocation.Arguments);
            Assert.False(runner.OutputSchemaBytes!.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
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

    [Theory]
    [InlineData(ProviderProcessOutcome.StartFailed, PlannerInvocationStatus.AdapterUnavailable, false)]
    [InlineData(ProviderProcessOutcome.NonZeroExit, PlannerInvocationStatus.Failed, true)]
    [InlineData(ProviderProcessOutcome.TimedOut, PlannerInvocationStatus.TimedOut, true)]
    [InlineData(ProviderProcessOutcome.Cancelled, PlannerInvocationStatus.Cancelled, true)]
    [InlineData(ProviderProcessOutcome.TerminationFailure, PlannerInvocationStatus.AdapterUnavailable, false)]
    public async Task Planner_PreservesTypedProcessFailureDiagnostics(ProviderProcessOutcome outcome, PlannerInvocationStatus expected, bool terminationConfirmed)
    {
        var workspace = Directory.CreateTempSubdirectory("apo-planner-test-");
        try
        {
            var process = new ProviderProcessResult(outcome, 73, string.Empty, "planner failure", false, false, ProcessTerminationConfirmed: terminationConfirmed);
            var result = await new CodexPlannerAdapter(new FakeLocator("C:\\tools\\codex.exe"), new FakeProcessRunner(PlannerJson(), executionResult: process), new HandoffRedactionService())
                .PlanAsync(new PlannerInvocationRequest(Agent(AgentRole.Planner), new OrchestrationWorkRequest(Guid.NewGuid(), "owner:test", "Title", "Objective", acceptanceCriteria: ["Criterion"]), workspace.FullName, TimeSpan.FromSeconds(30)));

            Assert.Equal(expected, result.Status);
            Assert.Equal(outcome, result.Diagnostic!.ProcessOutcome);
            Assert.Equal(73, result.Diagnostic.ExitCode);
            Assert.Equal(terminationConfirmed, result.Diagnostic.ProcessTerminationConfirmed);
            Assert.Equal(outcome == ProviderProcessOutcome.TimedOut, result.Diagnostic.TimedOut);
            Assert.Equal(outcome == ProviderProcessOutcome.Cancelled, result.Diagnostic.Cancelled);
        }
        finally { Directory.Delete(workspace.FullName, recursive: true); }
    }

    [Fact]
    public async Task Planner_DistinguishesMissingOutputFromMalformedOutputAndRedactsDiagnostics()
    {
        var workspace = Directory.CreateTempSubdirectory("apo-planner-test-");
        try
        {
            var request = new PlannerInvocationRequest(Agent(AgentRole.Planner), new OrchestrationWorkRequest(Guid.NewGuid(), "owner:test", "Title", "Objective", acceptanceCriteria: ["Criterion"]), workspace.FullName, TimeSpan.FromSeconds(30));
            var missing = await new CodexPlannerAdapter(new FakeLocator("C:\\tools\\codex.exe"), new FakeProcessRunner(PlannerJson(), writeOutput: false), new HandoffRedactionService()).PlanAsync(request);
            var malformed = await new CodexPlannerAdapter(new FakeLocator("C:\\tools\\codex.exe"), new FakeProcessRunner("{", executionResult: new ProviderProcessResult(ProviderProcessOutcome.ExitedSuccessfully, 0, string.Empty, "api_key=secret-value", false, false)), new HandoffRedactionService()).PlanAsync(request);

            Assert.False(missing.Diagnostic!.OutputFileExists);
            Assert.False(missing.Diagnostic.OutputParsingFailed);
            Assert.True(malformed.Diagnostic!.OutputFileExists);
            Assert.True(malformed.Diagnostic.OutputParsingFailed);
            Assert.Contains("[REDACTED]", malformed.Diagnostic.StandardErrorSummary);
            Assert.DoesNotContain("secret-value", malformed.Diagnostic.StandardErrorSummary, StringComparison.Ordinal);
            Assert.True(malformed.Diagnostic.StandardErrorSummary!.Length <= 1_000);
        }
        finally { Directory.Delete(workspace.FullName, recursive: true); }
    }

    [Fact]
    public async Task ExecutorPolicy_UsesWorkspaceWriteWithoutEscalationOrHostShell()
    {
        var workspace = Directory.CreateTempSubdirectory("apo-executor-test-");
        try
        {
            var runner = new FakeProcessRunner("{\"summary\":\"done\"}");
            await CodexLocalInvocation.RunAsync(
                new FakeLocator("C:\\tools\\codex.exe"),
                runner,
                "gpt-test",
                workspace.FullName,
                "{\"type\":\"object\"}",
                "Return JSON.",
                TimeSpan.FromSeconds(30),
                1024,
                CodexInvocationPolicy.Executor,
                CancellationToken.None);

            var invocation = Assert.Single(runner.Requests);
            Assert.Equal("C:\\tools\\codex.exe", invocation.ExecutablePath);
            Assert.Equal(workspace.FullName, invocation.WorkingDirectory);
            Assert.Equal("gpt-test", invocation.Arguments[Array.IndexOf(invocation.Arguments.ToArray(), "-m") + 1]);
            Assert.Contains("workspace-write", invocation.Arguments);
            Assert.DoesNotContain("read-only", invocation.Arguments);
            Assert.Equal("never", invocation.Arguments[Array.IndexOf(invocation.Arguments.ToArray(), "-a") + 1]);
            Assert.Contains("--output-schema", invocation.Arguments);
            Assert.DoesNotContain("cmd.exe", invocation.Arguments, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("powershell", invocation.Arguments, StringComparer.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(workspace.FullName, recursive: true); }
    }

    [Fact]
    public async Task Executor_MapsStructuredResultUsageAndEvidence()
    {
        var workspace = Directory.CreateTempSubdirectory("apo-executor-test-");
        try
        {
            var runner = new FakeProcessRunner("{\"summary\":\"done\",\"stopReason\":\"completed\",\"toolInvocations\":3,\"modelTurns\":2,\"changedFiles\":1,\"changedLines\":8}");
            var result = await new CodexExecutionAdapter(new FakeLocator("C:\\tools\\codex.exe"), runner, new HandoffRedactionService())
                .ExecuteAsync(CreateExecutionRequest(workspace.FullName));

            Assert.Equal(ExecutionAdapterOutcome.Succeeded, result.Outcome);
            Assert.Equal("done", result.Summary);
            Assert.Equal(3, result.Usage?.ToolInvocations);
            Assert.Equal(2, result.Usage?.ModelTurns);
            Assert.True(result.MayHaveModifiedWorkspace);
            Assert.Contains("codex-execution:gpt-test", result.EvidenceReferences);
            Assert.Contains("workspace-receipt:", result.EvidenceReferences[1]);
        }
        finally { Directory.Delete(workspace.FullName, recursive: true); }
    }

    [Fact]
    public async Task Executor_MapsAuthenticationRequiredWithoutClaimingWorkspaceMutation()
    {
        var workspace = Directory.CreateTempSubdirectory("apo-executor-test-");
        try
        {
            var runner = new FakeProcessRunner("{\"summary\":\"done\"}", Success("Not logged in"));
            var result = await new CodexExecutionAdapter(new FakeLocator("C:\\tools\\codex.exe"), runner, new HandoffRedactionService())
                .ExecuteAsync(CreateExecutionRequest(workspace.FullName));

            Assert.Equal(ExecutionAdapterOutcome.AuthenticationRequired, result.Outcome);
            Assert.False(result.MayHaveModifiedWorkspace);
            Assert.Single(runner.Requests);
        }
        finally { Directory.Delete(workspace.FullName, recursive: true); }
    }

    [Theory]
    [InlineData(ProviderProcessOutcome.StartFailed, ExecutionAdapterOutcome.AdapterUnavailable, false)]
    [InlineData(ProviderProcessOutcome.NonZeroExit, ExecutionAdapterOutcome.Failed, true)]
    [InlineData(ProviderProcessOutcome.TimedOut, ExecutionAdapterOutcome.TimedOut, true)]
    [InlineData(ProviderProcessOutcome.Cancelled, ExecutionAdapterOutcome.Cancelled, true)]
    [InlineData(ProviderProcessOutcome.TerminationFailure, ExecutionAdapterOutcome.TerminationUnconfirmed, true)]
    public async Task Executor_ClassifiesProcessOutcomesAndModificationRisk(ProviderProcessOutcome outcome, ExecutionAdapterOutcome expected, bool mayHaveModified)
    {
        var workspace = Directory.CreateTempSubdirectory("apo-executor-test-");
        try
        {
            var runner = new FakeProcessRunner("{\"summary\":\"done\"}", executionResult: new ProviderProcessResult(outcome, 1, string.Empty, string.Empty, false, false));
            var result = await new CodexExecutionAdapter(new FakeLocator("C:\\tools\\codex.exe"), runner, new HandoffRedactionService())
                .ExecuteAsync(CreateExecutionRequest(workspace.FullName));

            Assert.Equal(expected, result.Outcome);
            Assert.Equal(mayHaveModified, result.MayHaveModifiedWorkspace);
        }
        finally { Directory.Delete(workspace.FullName, recursive: true); }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"summary\":\"api_key=abc123456789\"}")]
    public async Task Executor_RejectsMissingOrSecretShapedOutput(string output)
    {
        var workspace = Directory.CreateTempSubdirectory("apo-executor-test-");
        try
        {
            var runner = new FakeProcessRunner(output);
            var result = await new CodexExecutionAdapter(new FakeLocator("C:\\tools\\codex.exe"), runner, new HandoffRedactionService())
                .ExecuteAsync(CreateExecutionRequest(workspace.FullName));

            Assert.Equal(ExecutionAdapterOutcome.InvalidResult, result.Outcome);
            Assert.True(result.MayHaveModifiedWorkspace);
            Assert.Empty(result.EvidenceReferences);
        }
        finally { Directory.Delete(workspace.FullName, recursive: true); }
    }

    private static ExecutionAdapterRequest CreateExecutionRequest(string workspacePath)
    {
        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.NewGuid();
        var contextId = Guid.NewGuid();
        var agent = Agent(AgentRole.Executor, projectId);
        var contract = CreateContract(projectId, contextId, agent.Id, workspacePath, now);
        var node = new WorkGraphNode(Guid.NewGuid(), contract.Reference);
        var graph = new WorkGraph(projectId, Guid.NewGuid(), WorkGraphSchema.CurrentVersion, now, [node], []);
        var routing = CreateRouting(projectId, contextId, contract, agent, now);
        var handoff = CreateHandoff(projectId, contextId, contract, graph, node, now);
        var plan = new WorkspacePreparationPlan(projectId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now,
            new WorkspaceContextIdentity(projectId, contextId, ProjectContextContract.CurrentVersion, now), contract.Reference, graph.Reference, node.NodeId, routing.Reference,
            new WorkspaceRepositoryDiscovery(WorkspaceRepositoryDiscoveryStatus.Available, workspacePath, workspacePath, workspacePath, headCommitSha: new string('a', 40), branchName: "main", isClean: true),
            new string('a', 40), "apo-test", workspacePath, WorkspacePreparationPolicy.RequireCleanSource, true, "prepared");
        var receipt = new WorkspacePreparationReceipt(projectId, plan.WorkspaceId, plan.CorrelationId, now, plan.Reference, workspacePath, plan.WorkspaceBranch, new string('a', 40), new string('a', 40), workspacePath, "owner:test");
        var checkpoint = new RecoveryCheckpoint(projectId, Guid.NewGuid(), RecoveryCheckpointSchema.CurrentVersion, now, RecoveryCheckpointLifecycleState.Ready,
            new RecoveryContextReference(contextId, ProjectContextContract.CurrentVersion, now), contract.Reference, graph.Reference, node.NodeId, handoff.Reference,
            selectedAgentRoleReferences: [new RecoveryAgentRoleReference(agent.Id, AgentRole.Executor)]);
        var authority = new ExecutionRunAuthority(projectId, Guid.NewGuid(), now, contract.Reference, graph.Reference, node.NodeId, handoff.Reference, routing.Reference, plan.Reference, plan.WorkspaceId, workspacePath, receipt.ContentHash, checkpoint.Reference, agent.Id, "OpenAI", "gpt-test", AgentConnectionMode.Cli, "codex-local-executor", new ExecutionBudgetEnvelope(1, 1, toolInvocations: 10, modelTurns: 2));
        return new ExecutionAdapterRequest(authority, agent, contract, handoff, receipt, CancellationToken.None);
    }

    private static EffectiveAgentDefinition Agent(AgentRole role, Guid projectId)
    {
        var now = DateTimeOffset.UtcNow;
        var definition = new AgentDefinition(Guid.NewGuid(), "Configured Codex", role.ToString(), AgentConnectionMode.Cli, AgentAvailability.Available, true, now, now, provider: "OpenAI", roleCapabilities: [role], supportedConnectionModes: [AgentConnectionMode.Cli], authenticationState: AgentAuthenticationState.Authenticated, entitlementState: AgentEntitlementState.VerifiedAvailable, modelIdentifier: "gpt-test");
        return new EffectiveAgentDefinition(projectId, definition, null);
    }

    private static PlanningExecutionContract CreateContract(Guid projectId, Guid contextId, Guid agentId, string workspacePath, DateTimeOffset now) => new(
        projectId, Guid.NewGuid(), PlanningExecutionContractSchema.CurrentVersion, 1, now, "owner:test", agentId,
        new PlanningContextBinding(contextId, ProjectContextContract.CurrentVersion), new PlanningWorkItem(PlanningWorkItemSource.Manual, "desktop:test", "Bounded execution"),
        new PlanningRepositoryTarget(PlanningRepositoryMode.LocalGit, workspacePath, "main", new string('a', 40)), [new PlanningScopeClause("include", "one bounded step")], [], [new PlanningScopeClause("forbid", "unrelated work")], [new PlanningDeliverable("result", "bounded result", true)], [new PlanningValidationRequirement("test", PlanningValidationKind.Test, "focused test", true)], [new PlanningAcceptanceCriterion("criterion", "done", true)], [new PlanningExecutionBudget(PlanningBudgetKind.Attempts, 1), new PlanningExecutionBudget(PlanningBudgetKind.ElapsedMinutes, 1), new PlanningExecutionBudget(PlanningBudgetKind.ToolInvocations, 10), new PlanningExecutionBudget(PlanningBudgetKind.ModelTurns, 2)], [new PlanningStopCondition("target", PlanningStopConditionKind.ImmutableTargetMoved, "target"), new PlanningStopCondition("scope", PlanningStopConditionKind.ScopeViolation, "scope"), new PlanningStopCondition("budget", PlanningStopConditionKind.BudgetExceeded, "budget")], [], null, null);

    private static RoutingDecision CreateRouting(Guid projectId, Guid contextId, PlanningExecutionContract contract, EffectiveAgentDefinition agent, DateTimeOffset now)
    {
        var classification = new RoutingTaskClassification(RoutingScopeScale.Bounded, RoutingTaskRisk.Low, RoutingBlastRadius.Local, RoutingValidationCost.Low, AgentRole.Executor, capacityRequirement: RoutingCapacityRequirement.NotApplicable, requiresAuthenticatedAccess: true, requiresVerifiedAvailability: true, requiresVerifiedEntitlement: true);
        var policy = new RoutingPolicySnapshot("test-policy", AgentRole.Executor, [agent.Id], capacityRequirement: RoutingCapacityRequirement.NotApplicable, requireAuthenticatedAccess: true, requireVerifiedAvailability: true, requireVerifiedEntitlement: true);
        var input = new RoutingInputSnapshot(projectId, contract.Reference, new RoutingContextReference(contextId, 1, now), classification, policy, [RoutingAgentSnapshot.FromEffective(agent)], [], null, now);
        return new RoutingDecision(projectId, Guid.NewGuid(), RoutingDecisionSchema.CurrentVersion, now, new RoutingDecisionEngine().Evaluate(input));
    }

    private static HandoffPackage CreateHandoff(Guid projectId, Guid contextId, PlanningExecutionContract contract, WorkGraph graph, WorkGraphNode node, DateTimeOffset now)
    {
        var budgets = new[] { new PlanningExecutionBudget(PlanningBudgetKind.Attempts, 1), new PlanningExecutionBudget(PlanningBudgetKind.ElapsedMinutes, 1), new PlanningExecutionBudget(PlanningBudgetKind.ToolInvocations, 10), new PlanningExecutionBudget(PlanningBudgetKind.ModelTurns, 2) };
        var scope = new HandoffExecutionScope([new PlanningScopeClause("include", "one bounded step")], [], [new PlanningScopeClause("forbid", "unrelated work")], [new PlanningDeliverable("result", "bounded result", true)], [new PlanningValidationRequirement("test", PlanningValidationKind.Test, "focused test", true)], budgets, [new PlanningStopCondition("target", PlanningStopConditionKind.ImmutableTargetMoved, "target"), new PlanningStopCondition("scope", PlanningStopConditionKind.ScopeViolation, "scope"), new PlanningStopCondition("budget", PlanningStopConditionKind.BudgetExceeded, "budget")], [], null, null);
        return new HandoffPackage(projectId, Guid.NewGuid(), HandoffPackageSchema.CurrentVersion, now, HandoffTransition.PlannerToExecutor, HandoffRole.Planner, HandoffRole.Executor, contract.Reference, contract.WorkItem, new HandoffContextReference(contextId, 1, now, now), new PlanningRepositoryTarget(PlanningRepositoryMode.None), graph.Reference, node.NodeId, null, scope, null, null, null, [], [], [], null, [], "Execute bounded work", new HandoffRedactionMetadata(false, 0, []), new HandoffPackageSizeMetadata(HandoffPackageLimits.MaxCanonicalPayloadBytes, 0, 0, 0, 0, 0, 11));
    }

    private static ProviderProcessResult Success(string standardOutput) => new(ProviderProcessOutcome.ExitedSuccessfully, 0, standardOutput, string.Empty, false, false);

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
        "\"validationExpectations\":[{\"kind\":\"test\",\"description\":\"Run focused tests\",\"required\":true,\"commandOrReference\":null}]," +
        "\"classification\":{" +
        "\"scopeScale\":\"bounded\",\"risk\":\"moderate\",\"blastRadius\":\"module\",\"validationCost\":\"moderate\",\"requiredRole\":\"executor\",\"requiredCapabilities\":[],\"policyTags\":[],\"capacityRequirement\":\"optional\",\"independentReviewRequired\":false,\"securityReviewRequired\":false,\"ownerApprovalRequired\":false,\"requiresSupportedConnection\":true,\"requiresVerifiedAvailability\":false,\"requiresAuthenticatedAccess\":true,\"requiresVerifiedEntitlement\":false}," +
        "\"stopConditions\":[{\"conditionId\":\"target\",\"kind\":\"immutableTargetMoved\",\"description\":\"Stop if target moves\"},{\"conditionId\":\"scope\",\"kind\":\"scopeViolation\",\"description\":\"Stop if scope grows\"},{\"conditionId\":\"budget\",\"kind\":\"budgetExceeded\",\"description\":\"Stop if budget ends\"}]," +
        "\"executionBudgets\":[{\"kind\":\"attempts\",\"limit\":1},{\"kind\":\"elapsedMinutes\",\"limit\":10}]" +
        (withExtra ? ",\"extra\":true" : "") + "}";

    private static void AssertStrictSchema(string schema, string schemaName)
    {
        using var document = JsonDocument.Parse(schema);
        AssertStrictSchemaNode(document.RootElement, schemaName);
    }

    private static void AssertStrictSchemaNode(JsonElement node, string path)
    {
        if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty("properties", out var properties))
        {
            Assert.True(properties.ValueKind == JsonValueKind.Object, $"{path}.properties must be an object.");
            Assert.True(
                node.TryGetProperty("additionalProperties", out var additionalProperties) && additionalProperties.ValueKind == JsonValueKind.False,
                $"{path}.additionalProperties must be explicitly false.");
            Assert.True(
                node.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array,
                $"{path}.required must be an array.");

            var propertyNames = properties.EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var requiredNames = required.EnumerateArray()
                .Select(value =>
                {
                    Assert.Equal(JsonValueKind.String, value.ValueKind);
                    return value.GetString()!;
                })
                .ToArray();

            Assert.Equal(requiredNames.Length, requiredNames.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(propertyNames, requiredNames.OrderBy(name => name, StringComparer.Ordinal).ToArray());
        }

        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in node.EnumerateObject())
            {
                AssertStrictSchemaNode(property.Value, $"{path}.{property.Name}");
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in node.EnumerateArray())
            {
                AssertStrictSchemaNode(item, $"{path}[{index++}]");
            }
        }
    }

    private static void AssertNullableType(JsonElement properties, string propertyName)
    {
        var types = properties.GetProperty(propertyName).GetProperty("type");
        Assert.Equal(JsonValueKind.Array, types.ValueKind);
        Assert.Contains(types.EnumerateArray(), value => value.ValueKind == JsonValueKind.String && value.GetString() == "null");
    }

    private sealed class FakeLocator(string path) : IExecutableLocator
    {
        public string? Find(string commandName) => path;
    }

    private sealed class FakeProcessRunner(
        string output,
        ProviderProcessResult? sessionResult = null,
        ProviderProcessResult? executionResult = null,
        bool writeOutput = true) : IProviderProcessRunner
    {
        public List<ProviderProcessRequest> Requests { get; } = [];
        public byte[]? OutputSchemaBytes { get; private set; }

        public Task<ProviderProcessResult> RunAsync(ProviderProcessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (request.Arguments.SequenceEqual(["login", "status"]))
            {
                return Task.FromResult(sessionResult ?? Success("Logged in"));
            }

            if (writeOutput)
            {
                var schemaPath = request.Arguments[Array.IndexOf(request.Arguments.ToArray(), "--output-schema") + 1];
                OutputSchemaBytes = File.ReadAllBytes(schemaPath);
                var outputPath = request.Arguments[Array.IndexOf(request.Arguments.ToArray(), "-o") + 1];
                File.WriteAllText(outputPath, output);
            }
            return Task.FromResult(executionResult ?? Success(string.Empty));
        }

    }
}
