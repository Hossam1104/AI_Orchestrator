using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Application.Routing;
using AIUsageMonitor.Providers.Common;

namespace AIUsageMonitor.Providers.Codex;

public sealed class CodexPlannerAdapter : IPlannerAdapter
{
    private readonly IExecutableLocator _locator;
    private readonly IProviderProcessRunner _processes;
    private readonly IHandoffRedactionService _redaction;

    public CodexPlannerAdapter(
        IExecutableLocator locator,
        IProviderProcessRunner processes,
        IHandoffRedactionService redaction)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        _redaction = redaction ?? throw new ArgumentNullException(nameof(redaction));
    }

    public PlannerAdapterDescriptor Descriptor { get; } = new(
        "codex-local-planner",
        [AgentConnectionMode.Cli],
        ["OpenAI"]);

    public async Task<PlannerInvocationResult> PlanAsync(
        PlannerInvocationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsExactCodexAgent(request.Planner))
        {
            return new(PlannerInvocationStatus.Unsupported, ErrorMessage: "The selected planner is not an exact local OpenAI Codex configuration.");
        }

        try
        {
            var session = await CodexLocalInvocation.VerifySessionAsync(
                    _locator,
                    _processes,
                    request.WorkspacePath,
                    cancellationToken)
                .ConfigureAwait(false);
            if (session.Status != CodexSessionStatus.Authenticated)
            {
                return new(session.Status switch
                {
                    CodexSessionStatus.AuthenticationRequired => PlannerInvocationStatus.AuthenticationRequired,
                    CodexSessionStatus.Cancelled => PlannerInvocationStatus.Cancelled,
                    CodexSessionStatus.TimedOut => PlannerInvocationStatus.TimedOut,
                    _ => PlannerInvocationStatus.AdapterUnavailable
                }, ErrorMessage: session.ErrorMessage);
            }

            var prompt = CodexPromptBuilder.BuildPlannerPrompt(request.OwnerRequest, request.WorkspacePath, _redaction);
            if (prompt.Length > CodexLocalInvocation.MaxPromptLength)
            {
                return new(PlannerInvocationStatus.InvalidResult, ErrorMessage: "The bounded planner prompt exceeds the adapter limit.");
            }

            var invocation = await CodexLocalInvocation.RunAsync(
                    _locator,
                    _processes,
                    request.Planner.ModelIdentifier!,
                    request.WorkspacePath,
                    CodexLocalInvocation.PlannerSchema,
                    prompt,
                    request.Timeout,
                    CodexLocalInvocation.MaxPlannerOutputBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            if (invocation.Process.Outcome != ProviderProcessOutcome.ExitedSuccessfully)
            {
                return new(MapPlannerProcessOutcome(invocation.Process), ErrorMessage: "The local Codex planner process did not complete successfully.");
            }

            if (invocation.Output is null || _redaction.ValidateIdentityText(invocation.Output).RequiresRedaction)
            {
                return new(PlannerInvocationStatus.InvalidResult, ErrorMessage: "The planner output was missing, oversized, or crossed the redaction boundary.");
            }

            try
            {
                var response = JsonSerializer.Deserialize<CodexPlannerResponse>(invocation.Output, CodexLocalInvocation.JsonOptions);
                return new(PlannerInvocationStatus.Succeeded, CodexPlanMapper.Map(response));
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
            {
                return new(PlannerInvocationStatus.InvalidResult, ErrorMessage: "The planner output did not match the bounded APO plan schema.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(PlannerInvocationStatus.Cancelled, ErrorMessage: "Planner cancellation was requested.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(PlannerInvocationStatus.AdapterUnavailable, ErrorMessage: "The local Codex planner could not be invoked safely.");
        }
    }

    private static bool IsExactCodexAgent(EffectiveAgentDefinition agent) =>
        agent.ConnectionMode == AgentConnectionMode.Cli &&
        string.Equals(agent.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(agent.ModelIdentifier);

    private static PlannerInvocationStatus MapPlannerProcessOutcome(ProviderProcessResult result) =>
        result.Outcome switch
        {
            ProviderProcessOutcome.Cancelled => PlannerInvocationStatus.Cancelled,
            ProviderProcessOutcome.TimedOut => PlannerInvocationStatus.TimedOut,
            ProviderProcessOutcome.StartFailed or ProviderProcessOutcome.TerminationFailure => PlannerInvocationStatus.AdapterUnavailable,
            _ => PlannerInvocationStatus.Failed
        };
}

public sealed class CodexExecutionAdapter : IExecutionAdapter
{
    private readonly IExecutableLocator _locator;
    private readonly IProviderProcessRunner _processes;
    private readonly IHandoffRedactionService _redaction;

    public CodexExecutionAdapter(
        IExecutableLocator locator,
        IProviderProcessRunner processes,
        IHandoffRedactionService redaction)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        _redaction = redaction ?? throw new ArgumentNullException(nameof(redaction));
    }

    public ExecutionAdapterDescriptor Descriptor { get; } = new(
        "codex-local-executor",
        [AgentConnectionMode.Cli],
        supportedProviders: ["OpenAI"]);

    public async Task<ExecutionAdapterResult> ExecuteAsync(
        ExecutionAdapterRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsExactCodexAgent(request.SelectedAgent) ||
            !Path.IsPathFullyQualified(request.WorkspacePath) ||
            !Directory.Exists(request.WorkspacePath))
        {
            return new(ExecutionAdapterOutcome.Unsupported, stopReason: "The selected Codex identity or prepared workspace is not exact.");
        }

        try
        {
            var session = await CodexLocalInvocation.VerifySessionAsync(
                    _locator,
                    _processes,
                    request.WorkspacePath,
                    cancellationToken)
                .ConfigureAwait(false);
            if (session.Status != CodexSessionStatus.Authenticated)
            {
                return new(session.Status switch
                {
                    CodexSessionStatus.AuthenticationRequired => ExecutionAdapterOutcome.AuthenticationRequired,
                    CodexSessionStatus.Cancelled => ExecutionAdapterOutcome.Cancelled,
                    CodexSessionStatus.TimedOut => ExecutionAdapterOutcome.TimedOut,
                    _ => ExecutionAdapterOutcome.AdapterUnavailable
                }, stopReason: session.ErrorMessage);
            }

            var prompt = CodexPromptBuilder.BuildExecutionPrompt(request, _redaction);
            if (prompt.Length > CodexLocalInvocation.MaxPromptLength)
            {
                return new(ExecutionAdapterOutcome.InvalidResult, stopReason: "The bounded executor prompt exceeds the adapter limit.");
            }

            var invocation = await CodexLocalInvocation.RunAsync(
                    _locator,
                    _processes,
                    request.SelectedAgent.ModelIdentifier!,
                    request.WorkspacePath,
                    CodexLocalInvocation.ExecutionSchema,
                    prompt,
                    TimeSpan.FromMinutes(Math.Min(request.Budgets.ElapsedMinutes, 240)),
                    CodexLocalInvocation.MaxExecutionOutputBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            var mayHaveModified = invocation.Process.Outcome != ProviderProcessOutcome.StartFailed;
            if (invocation.Process.Outcome != ProviderProcessOutcome.ExitedSuccessfully)
            {
                return new(
                    MapExecutionProcessOutcome(invocation.Process),
                    stopReason: "The local Codex executor process did not complete successfully.",
                    mayHaveModifiedWorkspace: mayHaveModified);
            }

            if (invocation.Output is null || _redaction.ValidateIdentityText(invocation.Output).RequiresRedaction)
            {
                return new(ExecutionAdapterOutcome.InvalidResult, stopReason: "The executor output was missing, oversized, or crossed the redaction boundary.", mayHaveModifiedWorkspace: true);
            }

            try
            {
                var response = JsonSerializer.Deserialize<CodexExecutionResponse>(invocation.Output, CodexLocalInvocation.JsonOptions)
                    ?? throw new JsonException();
                if (string.IsNullOrWhiteSpace(response.Summary) || response.Summary.Length > 2_000 ||
                    (response.StopReason is not null && response.StopReason.Length > 1_000))
                {
                    throw new ArgumentException();
                }

                var usage = new ExecutionAdapterUsageMetrics(
                    response.ToolInvocations,
                    response.ModelTurns,
                    response.ChangedFiles,
                    response.ChangedLines);
                return new(
                    ExecutionAdapterOutcome.Succeeded,
                    response.Summary,
                    response.StopReason,
                    usage,
                    mayHaveModifiedWorkspace: true,
                    evidenceReferences: [$"codex-execution:{request.SelectedAgent.ModelIdentifier}", $"workspace-receipt:{request.WorkspaceReceipt.ContentHash}"]);
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
            {
                return new(ExecutionAdapterOutcome.InvalidResult, stopReason: "The executor output did not match the bounded APO result schema.", mayHaveModifiedWorkspace: true);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(ExecutionAdapterOutcome.Cancelled, stopReason: "Executor cancellation was requested.", mayHaveModifiedWorkspace: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(ExecutionAdapterOutcome.AdapterUnavailable, stopReason: "The local Codex executor could not be invoked safely.");
        }
    }

    private static bool IsExactCodexAgent(EffectiveAgentDefinition agent) =>
        agent.ConnectionMode == AgentConnectionMode.Cli &&
        string.Equals(agent.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(agent.ModelIdentifier);

    private static ExecutionAdapterOutcome MapExecutionProcessOutcome(ProviderProcessResult result) =>
        result.Outcome switch
        {
            ProviderProcessOutcome.Cancelled => ExecutionAdapterOutcome.Cancelled,
            ProviderProcessOutcome.TimedOut => ExecutionAdapterOutcome.TimedOut,
            ProviderProcessOutcome.TerminationFailure => ExecutionAdapterOutcome.TerminationUnconfirmed,
            ProviderProcessOutcome.StartFailed => ExecutionAdapterOutcome.AdapterUnavailable,
            _ => ExecutionAdapterOutcome.Failed
        };
}

internal enum CodexSessionStatus
{
    Authenticated,
    AuthenticationRequired,
    Unavailable,
    Cancelled,
    TimedOut
}

internal sealed record CodexSessionResult(CodexSessionStatus Status, string ErrorMessage);

internal sealed record CodexInvocationResult(ProviderProcessResult Process, string? Output);

internal static class CodexLocalInvocation
{
    public const int MaxPromptLength = 7_500;
    public const int MaxPlannerOutputBytes = 128 * 1024;
    public const int MaxExecutionOutputBytes = 32 * 1024;

    public static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public const string PlannerSchema = """
        {"type":"object","additionalProperties":false,"required":["normalizedObjective","includedScope","acceptanceCriteria","constraints","validationExpectations","classification","stopConditions","executionBudgets"],"properties":{"normalizedObjective":{"type":"string","minLength":1,"maxLength":4000},"includedScope":{"type":"array","minItems":1,"maxItems":32,"items":{"type":"string","minLength":1,"maxLength":4000}},"acceptanceCriteria":{"type":"array","minItems":1,"maxItems":32,"items":{"type":"string","minLength":1,"maxLength":4000}},"constraints":{"type":"array","maxItems":32,"items":{"type":"string","minLength":1,"maxLength":4000}},"validationExpectations":{"type":"array","minItems":1,"maxItems":32,"items":{"type":"object","additionalProperties":false,"required":["kind","description","required"],"properties":{"kind":{"type":"string","enum":["build","test","staticCheck","securityCheck","manualInspection","custom"]},"description":{"type":"string","minLength":1,"maxLength":4000},"required":{"type":"boolean"},"commandOrReference":{"type":["string","null"],"maxLength":1000}}}},"classification":{"type":"object","additionalProperties":false,"required":["scopeScale","risk","blastRadius","validationCost","requiredRole","requiredCapabilities","policyTags","capacityRequirement","independentReviewRequired","securityReviewRequired","ownerApprovalRequired","requiresSupportedConnection","requiresVerifiedAvailability","requiresAuthenticatedAccess","requiresVerifiedEntitlement"],"properties":{"scopeScale":{"type":"string","enum":["bounded","multiFile","crossCutting","projectWide"]},"risk":{"type":"string","enum":["low","moderate","high","critical"]},"blastRadius":{"type":"string","enum":["local","module","project","crossProject","externalSystem"]},"validationCost":{"type":"string","enum":["low","moderate","high"]},"requiredRole":{"type":"string","enum":["planner","architect","acceptanceAuthority","executor","reviewer","securitySpecialist","auxiliaryExecutor"]},"requiredCapabilities":{"type":"array","maxItems":64,"items":{"type":"string","minLength":1,"maxLength":160}},"policyTags":{"type":"array","maxItems":64,"items":{"type":"string","minLength":1,"maxLength":160}},"capacityRequirement":{"type":"string","enum":["required","optional","notApplicable"]},"independentReviewRequired":{"type":"boolean"},"securityReviewRequired":{"type":"boolean"},"ownerApprovalRequired":{"type":"boolean"},"requiresSupportedConnection":{"type":"boolean"},"requiresVerifiedAvailability":{"type":"boolean"},"requiresAuthenticatedAccess":{"type":"boolean"},"requiresVerifiedEntitlement":{"type":"boolean"}}},"stopConditions":{"type":"array","minItems":3,"maxItems":16,"items":{"type":"object","additionalProperties":false,"required":["conditionId","kind","description"],"properties":{"conditionId":{"type":"string","minLength":1,"maxLength":120},"kind":{"type":"string","enum":["immutableTargetMoved","scopeViolation","validationFailure","budgetExceeded","credentialRequired","ownerApprovalRequired","externalDependencyUnavailable","contextInsufficient","unresolvedAmbiguity","securityBoundaryReached"]},"description":{"type":"string","minLength":1,"maxLength":4000}}}},"executionBudgets":{"type":"array","minItems":2,"maxItems":16,"items":{"type":"object","additionalProperties":false,"required":["kind","limit"],"properties":{"kind":{"type":"string","enum":["attempts","elapsedMinutes","changedFiles","changedLines","toolInvocations","modelTurns"]},"limit":{"type":"integer","minimum":1,"maximum":1000000}}}}}
        """;

    public const string ExecutionSchema = """
        {"type":"object","additionalProperties":false,"required":["summary"],"properties":{"summary":{"type":"string","minLength":1,"maxLength":2000},"stopReason":{"type":["string","null"],"maxLength":1000},"toolInvocations":{"type":["integer","null"],"minimum":0},"modelTurns":{"type":["integer","null"],"minimum":0},"changedFiles":{"type":["integer","null"],"minimum":0},"changedLines":{"type":["integer","null"],"minimum":0}}}
        """;

    public static async Task<CodexSessionResult> VerifySessionAsync(
        IExecutableLocator locator,
        IProviderProcessRunner processes,
        string workspacePath,
        CancellationToken cancellationToken)
    {
        var executable = FindDirectExecutable(locator);
        if (executable is null)
        {
            return new(CodexSessionStatus.Unavailable, "The direct Codex executable was not detected.");
        }

        var result = await processes.RunAsync(
                new ProviderProcessRequest(executable, ["login", "status"], TimeSpan.FromSeconds(8), workspacePath),
                cancellationToken)
            .ConfigureAwait(false);
        var output = $"{result.StandardOutput}\n{result.StandardError}";
        if (result.Outcome == ProviderProcessOutcome.Cancelled)
        {
            return new(CodexSessionStatus.Cancelled, "Codex session verification was cancelled.");
        }

        if (result.Outcome == ProviderProcessOutcome.TimedOut)
        {
            return new(CodexSessionStatus.TimedOut, "Codex session verification timed out.");
        }

        if (output.Contains("not logged", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("not authenticated", StringComparison.OrdinalIgnoreCase))
        {
            return new(CodexSessionStatus.AuthenticationRequired, "The local Codex session is not authenticated.");
        }

        return result.Succeeded && output.Contains("logged in", StringComparison.OrdinalIgnoreCase)
            ? new(CodexSessionStatus.Authenticated, "Authenticated local Codex session verified.")
            : new(CodexSessionStatus.Unavailable, "The local Codex session could not be verified.");
    }

    public static async Task<CodexInvocationResult> RunAsync(
        IExecutableLocator locator,
        IProviderProcessRunner processes,
        string model,
        string workspacePath,
        string schema,
        string prompt,
        TimeSpan timeout,
        int maxOutputBytes,
        CancellationToken cancellationToken)
    {
        var executable = FindDirectExecutable(locator) ?? throw new FileNotFoundException("The direct Codex executable was not detected.");
        var temp = Directory.CreateTempSubdirectory("apo-codex-");
        try
        {
            var schemaPath = Path.Combine(temp.FullName, "output-schema.json");
            var outputPath = Path.Combine(temp.FullName, "last-message.json");
            await File.WriteAllTextAsync(schemaPath, schema, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            var result = await processes.RunAsync(
                    new ProviderProcessRequest(
                        executable,
                        ["exec", "--ephemeral", "--color", "never", "-m", model, "-C", workspacePath, "-s", "read-only", "-a", "never", "--output-schema", schemaPath, "-o", outputPath, prompt],
                        timeout,
                        workspacePath),
                    cancellationToken)
                .ConfigureAwait(false);
            return new(result, await ReadBoundedOutputAsync(outputPath, maxOutputBytes, cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            try { Directory.Delete(temp.FullName, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static string? FindDirectExecutable(IExecutableLocator locator)
    {
        var path = locator.Find("codex");
        return path is not null && string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)
            ? path
            : null;
    }

    private static async Task<string?> ReadBoundedOutputAsync(string path, int maximumBytes, CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > maximumBytes)
            {
                return null;
            }

            return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

internal static class CodexPromptBuilder
{
    public static string BuildPlannerPrompt(
        OrchestrationWorkRequest request,
        string workspacePath,
        IHandoffRedactionService redaction)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Return only JSON matching the supplied output schema. You are the planner for one bounded APO request.");
        builder.AppendLine("Classify the task, preserve every owner acceptance criterion and constraint exactly, and include explicit stop conditions and budgets.");
        builder.AppendLine("Do not propose shell commands, credentials, transcripts, source files, commits, pushes, merges, deployments, or work outside this workspace.");
        builder.AppendLine($"Workspace: {workspacePath}");
        Append(builder, "Title", request.Title, redaction);
        Append(builder, "Objective", request.Objective, redaction);
        AppendList(builder, "Acceptance criteria", request.AcceptanceCriteria, redaction);
        AppendList(builder, "Constraints", request.Constraints, redaction);
        AppendList(builder, "Validation expectations", request.ValidationExpectations, redaction);
        return builder.ToString().Trim();
    }

    public static string BuildExecutionPrompt(ExecutionAdapterRequest request, IHandoffRedactionService redaction)
    {
        var scope = request.ExecutionScope;
        var payload = new
        {
            workspacePath = request.WorkspacePath,
            workItem = request.Contract.WorkItem.Title,
            includedScope = scope.IncludedScope.Select(value => new { value.Id, value.Statement }),
            constraints = scope.Constraints.Select(value => new { value.Id, value.Statement }),
            forbiddenScope = scope.ForbiddenScope.Select(value => new { value.Id, value.Statement }),
            deliverables = scope.Deliverables.Select(value => new { value.DeliverableId, value.Description, value.Required }),
            validations = scope.ValidationRequirements.Select(value => new { value.ValidationId, value.Kind, value.Description, value.Required }),
            acceptanceCriteria = request.Contract.AcceptanceCriteria.Select(value => new { value.CriterionId, value.Statement, value.Required }),
            executionBudgets = scope.ExecutionBudgets.Select(value => new { value.Kind, value.Limit }),
            stopConditions = scope.StopConditions.Select(value => new { value.ConditionId, value.Kind, value.Description })
        };
        var json = JsonSerializer.Serialize(payload, CodexLocalInvocation.JsonOptions);
        var prompt = "Return only JSON matching the supplied output schema. Execute only the exact bounded work in the prepared workspace. Stop on any authority, scope, validation, budget, credential, or security boundary. Do not commit, push, merge, deploy, delete unrelated files, or invoke a shell. Report truthful bounded evidence in summary.\n" + json;
        return redaction.Redact(prompt).Value;
    }

    private static void Append(StringBuilder builder, string label, string value, IHandoffRedactionService redaction) =>
        builder.Append(label).Append(": ").AppendLine(redaction.Redact(value).Value);

    private static void AppendList(StringBuilder builder, string label, IReadOnlyList<string> values, IHandoffRedactionService redaction)
    {
        builder.AppendLine(label + ":");
        foreach (var value in values)
        {
            builder.Append("- ").AppendLine(redaction.Redact(value).Value);
        }
    }
}

internal sealed class CodexPlannerResponse
{
    [JsonPropertyName("normalizedObjective")] public string? NormalizedObjective { get; set; }
    [JsonPropertyName("includedScope")] public List<string>? IncludedScope { get; set; }
    [JsonPropertyName("acceptanceCriteria")] public List<string>? AcceptanceCriteria { get; set; }
    [JsonPropertyName("constraints")] public List<string>? Constraints { get; set; }
    [JsonPropertyName("validationExpectations")] public List<CodexValidationResponse>? ValidationExpectations { get; set; }
    [JsonPropertyName("classification")] public CodexClassificationResponse? Classification { get; set; }
    [JsonPropertyName("stopConditions")] public List<CodexStopResponse>? StopConditions { get; set; }
    [JsonPropertyName("executionBudgets")] public List<CodexBudgetResponse>? ExecutionBudgets { get; set; }
}

internal sealed class CodexValidationResponse
{
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("required")] public bool? Required { get; set; }
    [JsonPropertyName("commandOrReference")] public string? CommandOrReference { get; set; }
}

internal sealed class CodexClassificationResponse
{
    [JsonPropertyName("scopeScale")] public string? ScopeScale { get; set; }
    [JsonPropertyName("risk")] public string? Risk { get; set; }
    [JsonPropertyName("blastRadius")] public string? BlastRadius { get; set; }
    [JsonPropertyName("validationCost")] public string? ValidationCost { get; set; }
    [JsonPropertyName("requiredRole")] public string? RequiredRole { get; set; }
    [JsonPropertyName("requiredCapabilities")] public List<string>? RequiredCapabilities { get; set; }
    [JsonPropertyName("policyTags")] public List<string>? PolicyTags { get; set; }
    [JsonPropertyName("capacityRequirement")] public string? CapacityRequirement { get; set; }
    [JsonPropertyName("independentReviewRequired")] public bool? IndependentReviewRequired { get; set; }
    [JsonPropertyName("securityReviewRequired")] public bool? SecurityReviewRequired { get; set; }
    [JsonPropertyName("ownerApprovalRequired")] public bool? OwnerApprovalRequired { get; set; }
    [JsonPropertyName("requiresSupportedConnection")] public bool? RequiresSupportedConnection { get; set; }
    [JsonPropertyName("requiresVerifiedAvailability")] public bool? RequiresVerifiedAvailability { get; set; }
    [JsonPropertyName("requiresAuthenticatedAccess")] public bool? RequiresAuthenticatedAccess { get; set; }
    [JsonPropertyName("requiresVerifiedEntitlement")] public bool? RequiresVerifiedEntitlement { get; set; }
}

internal sealed class CodexStopResponse
{
    [JsonPropertyName("conditionId")] public string? ConditionId { get; set; }
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
}

internal sealed class CodexBudgetResponse
{
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("limit")] public long? Limit { get; set; }
}

internal sealed class CodexExecutionResponse
{
    [JsonPropertyName("summary")] public string? Summary { get; set; }
    [JsonPropertyName("stopReason")] public string? StopReason { get; set; }
    [JsonPropertyName("toolInvocations")] public long? ToolInvocations { get; set; }
    [JsonPropertyName("modelTurns")] public long? ModelTurns { get; set; }
    [JsonPropertyName("changedFiles")] public long? ChangedFiles { get; set; }
    [JsonPropertyName("changedLines")] public long? ChangedLines { get; set; }
}

internal static class CodexPlanMapper
{
    public static PlannerPlan Map(CodexPlannerResponse? response)
    {
        if (response is null || response.NormalizedObjective is null || response.IncludedScope is null ||
            response.AcceptanceCriteria is null || response.Constraints is null || response.ValidationExpectations is null ||
            response.Classification is null || response.StopConditions is null || response.ExecutionBudgets is null)
        {
            throw new ArgumentException("Planner output is incomplete.");
        }

        var classification = response.Classification;
        var routing = new RoutingTaskClassification(
            Parse<RoutingScopeScale>(classification.ScopeScale),
            Parse<RoutingTaskRisk>(classification.Risk),
            Parse<RoutingBlastRadius>(classification.BlastRadius),
            Parse<RoutingValidationCost>(classification.ValidationCost),
            Parse<AgentRole>(classification.RequiredRole),
            classification.RequiredCapabilities ?? throw new ArgumentException(),
            classification.PolicyTags ?? throw new ArgumentException(),
            Parse<RoutingCapacityRequirement>(classification.CapacityRequirement),
            classification.IndependentReviewRequired ?? throw new ArgumentException(),
            classification.SecurityReviewRequired ?? throw new ArgumentException(),
            classification.OwnerApprovalRequired ?? throw new ArgumentException(),
            classification.RequiresSupportedConnection ?? throw new ArgumentException(),
            classification.RequiresVerifiedAvailability ?? throw new ArgumentException(),
            classification.RequiresAuthenticatedAccess ?? throw new ArgumentException(),
            classification.RequiresVerifiedEntitlement ?? throw new ArgumentException());

        var validations = response.ValidationExpectations.Select((value, index) =>
            new PlanningValidationRequirement(
                $"planner-validation-{index + 1}",
                Parse<PlanningValidationKind>(value.Kind),
                value.Description ?? throw new ArgumentException(),
                value.Required ?? throw new ArgumentException(),
                value.CommandOrReference)).ToArray();
        var stops = response.StopConditions.Select((value, index) =>
            new PlanningStopCondition(
                value.ConditionId ?? $"planner-stop-{index + 1}",
                Parse<PlanningStopConditionKind>(value.Kind),
                value.Description ?? throw new ArgumentException())).ToArray();
        var budgets = response.ExecutionBudgets.Select(value =>
            new PlanningExecutionBudget(Parse<PlanningBudgetKind>(value.Kind), value.Limit ?? throw new ArgumentException())).ToArray();

        return new(
            response.NormalizedObjective,
            response.IncludedScope,
            response.AcceptanceCriteria,
            response.Constraints,
            validations,
            routing,
            stops,
            budgets);
    }

    private static T Parse<T>(string? value)
        where T : struct, Enum
    {
        var name = Enum.GetNames<T>().FirstOrDefault(candidate => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase));
        return name is null ? throw new ArgumentException("Planner output contains an undefined enum value.") : Enum.Parse<T>(name);
    }
}
