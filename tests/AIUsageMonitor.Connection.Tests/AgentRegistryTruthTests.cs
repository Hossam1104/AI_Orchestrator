using AIUsageMonitor.Application.Agents;

namespace AIUsageMonitor.Connection.Tests;

public sealed class AgentRegistryTruthTests
{
    [Fact]
    public void AgentDefinition_PreservesLegacyFieldsAndNormalizesNewCapabilities()
    {
        var now = new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);
        var agent = new AgentDefinition(
            Guid.NewGuid(),
            "Luna",
            "implementation",
            AgentConnectionMode.Api,
            AgentAvailability.AuthenticationRequired,
            enabled: true,
            now,
            now,
            provider: "OpenAI",
            roleCapabilities: [AgentRole.Executor, AgentRole.Executor, AgentRole.Reviewer],
            supportedConnectionModes: [AgentConnectionMode.Api, AgentConnectionMode.Cli, AgentConnectionMode.Api],
            authenticationState: AgentAuthenticationState.AuthenticationRequired,
            entitlementState: AgentEntitlementState.Unknown,
            modelIdentifier: "model-luna",
            rolePolicyMetadata: [new AgentRolePolicyMetadata(AgentRole.Executor, "primary implementation executor", "Primary")]);

        Assert.Equal("Luna", agent.Name);
        Assert.Equal("OpenAI", agent.Provider);
        Assert.Equal("model-luna", agent.ModelIdentifier);
        Assert.Equal([AgentRole.Executor, AgentRole.Reviewer], agent.RoleCapabilities);
        Assert.Equal([AgentConnectionMode.Api, AgentConnectionMode.Cli], agent.SupportedConnectionModes);
        Assert.Equal(AgentAuthenticationState.AuthenticationRequired, agent.AuthenticationState);
        Assert.Equal(AgentEntitlementState.Unknown, agent.EntitlementState);
        Assert.Equal("implementation", agent.Role);
    }

    [Fact]
    public void ConnectionResult_SeparatesAuthenticationFromUnverifiedEntitlement()
    {
        var result = new AgentConnectionResult(
            new AgentIdentity(Guid.NewGuid(), "Manual Claude", "Anthropic", "claude-manual"),
            new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero),
            AgentConnectionMode.InteractiveOnly,
            AgentAvailability.AuthenticationRequired,
            AgentAuthenticationState.AuthenticationRequired,
            AgentEntitlementState.Unknown,
            AgentEvidenceSource.InteractiveManualState,
            limitationCode: "AUTH_REQUIRED",
            message: "Interactive verification is required.",
            supportedConnectionModes: [AgentConnectionMode.InteractiveOnly]);

        Assert.Equal(AgentAvailability.AuthenticationRequired, result.Availability);
        Assert.Equal(AgentAuthenticationState.AuthenticationRequired, result.AuthenticationState);
        Assert.Equal(AgentEntitlementState.Unknown, result.EntitlementState);
        Assert.DoesNotContain("token", result.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConnectionResult_RejectsUnsupportedModeAsAvailable()
    {
        Assert.Throws<ArgumentException>(() => new AgentConnectionResult(
            new AgentIdentity(Guid.NewGuid(), "Unknown"),
            DateTimeOffset.UtcNow,
            AgentConnectionMode.Unsupported,
            AgentAvailability.Available,
            AgentAuthenticationState.Unknown,
            AgentEntitlementState.Unknown,
            AgentEvidenceSource.ManualVerification));
    }

    [Fact]
    public void ConnectionResult_RejectsUnknownAndUnsupportedFromSupportedModes()
    {
        Assert.Throws<ArgumentException>(() => new AgentConnectionResult(
            new AgentIdentity(Guid.NewGuid(), "Unknown"),
            DateTimeOffset.UtcNow,
            AgentConnectionMode.Unknown,
            AgentAvailability.Unknown,
            AgentAuthenticationState.Unknown,
            AgentEntitlementState.Unknown,
            AgentEvidenceSource.ManualVerification,
            supportedConnectionModes: [AgentConnectionMode.Unknown]));
        Assert.Throws<ArgumentException>(() => new AgentConnectionResult(
            new AgentIdentity(Guid.NewGuid(), "Unsupported"),
            DateTimeOffset.UtcNow,
            AgentConnectionMode.Unsupported,
            AgentAvailability.Unavailable,
            AgentAuthenticationState.Unknown,
            AgentEntitlementState.Unknown,
            AgentEvidenceSource.ManualVerification,
            supportedConnectionModes: [AgentConnectionMode.Unsupported]));
    }

    [Fact]
    public void DefaultCatalog_ContainsSixUnverifiedOwnerApprovedEntries()
    {
        var defaults = new DefaultAgentCatalog().GetDefaults();

        Assert.Equal(6, defaults.Count);
        Assert.Equal(
            ["GPT-5.6 Sol", "GPT-5.6 Luna xHigh", "Claude Sonnet 5", "Claude Opus 5", "GPT-5.6 Terra HIGH", "Gemini 3.7"],
            defaults.Select(value => value.Name));

        var sol = defaults.Single(value => value.Name == "GPT-5.6 Sol");
        Assert.Equal(
            [AgentRole.Planner, AgentRole.Architect, AgentRole.AcceptanceAuthority],
            sol.RoleCapabilities);
        Assert.Equal("gpt-5.6-sol", sol.ModelIdentifier);
        Assert.Equal("gpt-5.6-luna", defaults.Single(value => value.Name == "GPT-5.6 Luna xHigh").ModelIdentifier);
        Assert.All(defaults, value =>
        {
            Assert.Equal(AgentAvailability.Unknown, value.Availability);
            Assert.Equal(AgentAuthenticationState.Unknown, value.AuthenticationState);
            Assert.Equal(AgentEntitlementState.Unknown, value.EntitlementState);
            Assert.Equal(AgentConnectionMode.Unknown, value.ConnectionMode);
            Assert.Empty(value.SupportedConnectionModes);
        });
        Assert.Null(defaults.Single(value => value.Name == "Claude Sonnet 5").ModelIdentifier);
        Assert.Null(defaults.Single(value => value.Name == "Claude Opus 5").ModelIdentifier);
        Assert.Null(defaults.Single(value => value.Name == "GPT-5.6 Terra HIGH").ModelIdentifier);
        Assert.Null(defaults.Single(value => value.Name == "Gemini 3.7").ModelIdentifier);
    }

    [Fact]
    public void AgentDefinition_ExplicitEmptySupportedModesRemainEmpty()
    {
        var agent = CreateAgent(
            connectionMode: AgentConnectionMode.Unknown,
            supportedConnectionModes: []);

        Assert.Equal(AgentConnectionMode.Unknown, agent.ConnectionMode);
        Assert.Empty(agent.SupportedConnectionModes);
    }

    [Fact]
    public void AgentDefinition_RejectsUnknownOrUnsupportedStructuredSupportedModes()
    {
        Assert.Throws<ArgumentException>(() => CreateAgent(
            connectionMode: AgentConnectionMode.Unknown,
            supportedConnectionModes: [AgentConnectionMode.Unknown]));
        Assert.Throws<ArgumentException>(() => CreateAgent(
            connectionMode: AgentConnectionMode.Manual,
            supportedConnectionModes: [AgentConnectionMode.Manual, AgentConnectionMode.Unsupported]));
    }

    [Fact]
    public void AgentDefinition_PreservesUnknownAndUnsupportedAsDistinctPrimaryTruth()
    {
        var unknown = CreateAgent(
            connectionMode: AgentConnectionMode.Unknown,
            supportedConnectionModes: []);
        var unsupported = CreateAgent(
            connectionMode: AgentConnectionMode.Unsupported,
            supportedConnectionModes: []);

        Assert.Equal(AgentConnectionMode.Unknown, unknown.ConnectionMode);
        Assert.NotEqual(AgentConnectionMode.Unsupported, unknown.ConnectionMode);
        Assert.Equal(AgentConnectionMode.Unsupported, unsupported.ConnectionMode);
        Assert.Empty(unknown.SupportedConnectionModes);
        Assert.Empty(unsupported.SupportedConnectionModes);
    }

    [Fact]
    public void AgentDefinition_RejectsUnsupportedPrimaryAsAvailableWithExplicitEmptySupportedModes()
    {
        Assert.Throws<ArgumentException>(() => CreateAgent(
            connectionMode: AgentConnectionMode.Unsupported,
            availability: AgentAvailability.Available,
            supportedConnectionModes: []));
    }

    [Fact]
    public void AgentDefinition_RejectsRolePolicyMetadataOutsideRoleCapabilities()
    {
        Assert.Throws<ArgumentException>(() => CreateAgent(
            roleCapabilities: [AgentRole.Executor],
            rolePolicyMetadata: [new AgentRolePolicyMetadata(
                AgentRole.Reviewer,
                "review metadata") ]));
    }

    [Fact]
    public void AgentDefinition_RejectsBlankValuesAndUndefinedContractEnums()
    {
        Assert.Throws<ArgumentException>(() => new AgentIdentity(Guid.Empty, "Agent"));
        Assert.Throws<ArgumentException>(() => new AgentIdentity(Guid.NewGuid(), " "));
        Assert.Throws<ArgumentException>(() => CreateAgent(id: Guid.Empty));
        Assert.Throws<ArgumentException>(() => CreateAgent(name: " "));
        Assert.Throws<ArgumentException>(() => CreateAgent(capabilities: [" "]));
        Assert.Throws<ArgumentException>(() => CreateAgent(limitations: [" "]));
        Assert.Throws<ArgumentException>(() => CreateAgent(roleCapabilities: [(AgentRole)99]));
        Assert.Throws<ArgumentException>(() => CreateAgent(
            connectionMode: (AgentConnectionMode)99,
            supportedConnectionModes: []));
        Assert.Throws<ArgumentException>(() => CreateAgent(
            authenticationState: (AgentAuthenticationState)99));
        Assert.Throws<ArgumentException>(() => CreateAgent(
            entitlementState: (AgentEntitlementState)99));
    }

    [Fact]
    public async Task EffectiveRegistry_ProjectOverridesCannotGrantGlobalUnsupportedCapabilities()
    {
        var projectId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var agent = CreateAgent(
            id: agentId,
            roleCapabilities: [AgentRole.Executor],
            supportedConnectionModes: [AgentConnectionMode.Manual]);
        var overrides = new InMemoryAgentOverrideRepository();
        await overrides.UpsertAsync(new AgentProjectOverride(
            projectId,
            agentId,
            permittedRoles: [AgentRole.Executor, AgentRole.Planner],
            permittedConnectionModes: [AgentConnectionMode.Manual, AgentConnectionMode.Api]));

        var service = new AgentRegistryService(
            new InMemoryAgentRepository(agent),
            overrides);
        var result = await service.ResolveAsync(projectId, agentId);

        Assert.True(result.Found);
        Assert.Equal([AgentRole.Executor], result.Agent!.RoleCapabilities);
        Assert.Equal([AgentConnectionMode.Manual], result.Agent.SupportedConnectionModes);
        Assert.DoesNotContain(AgentRole.Planner, result.Agent.RoleCapabilities);
        Assert.DoesNotContain(AgentConnectionMode.Api, result.Agent.SupportedConnectionModes);
    }

    [Fact]
    public async Task EffectiveRegistry_AppliesOnlyProjectConfigurationAndReturnsExplicitMissing()
    {
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var agent = new AgentDefinition(
            agentId,
            "Agent",
            "executor",
            AgentConnectionMode.Manual,
            AgentAvailability.Available,
            enabled: true,
            now,
            now,
            roleCapabilities: [AgentRole.Executor, AgentRole.Reviewer],
            supportedConnectionModes: [AgentConnectionMode.Manual]);
        var agents = new InMemoryAgentRepository(agent);
        var overrides = new InMemoryAgentOverrideRepository();
        await overrides.UpsertAsync(new AgentProjectOverride(
            projectA,
            agentId,
            enabledOverride: false,
            permittedRoles: [AgentRole.Reviewer],
            permittedConnectionModes: [AgentConnectionMode.Manual],
            policyReference: "project-a-policy"));
        var service = new AgentRegistryService(agents, overrides);

        var projectAView = await service.ResolveAsync(projectA, agentId);
        var projectBView = await service.ResolveAsync(projectB, agentId);
        var missing = await service.ResolveAsync(projectA, Guid.NewGuid());

        Assert.True(projectAView.Found);
        Assert.NotNull(projectAView.Agent);
        Assert.False(projectAView.Agent!.Enabled);
        Assert.Equal([AgentRole.Reviewer], projectAView.Agent.RoleCapabilities);
        Assert.Equal("project-a-policy", projectAView.Agent.ProjectOverride!.PolicyReference);
        Assert.True(projectBView.Found);
        Assert.True(projectBView.Agent!.Enabled);
        Assert.Equal([AgentRole.Executor, AgentRole.Reviewer], projectBView.Agent.RoleCapabilities);
        Assert.Null(projectBView.Agent.ProjectOverride);
        Assert.False(missing.Found);
        Assert.Equal(AgentRegistryResolutionStatus.NotFound, missing.Status);
        Assert.True(agent.Enabled);
        Assert.Equal([AgentRole.Executor, AgentRole.Reviewer], agent.RoleCapabilities);
    }

    private sealed class InMemoryAgentRepository : IAgentRepository
    {
        private readonly Dictionary<Guid, AgentDefinition> _agents;

        public InMemoryAgentRepository(AgentDefinition agent) => _agents = new() { [agent.Id] = agent };

        public Task<IReadOnlyList<AgentDefinition>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentDefinition>>(_agents.Values.ToArray());

        public Task<AgentDefinition?> GetByIdAsync(Guid agentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_agents.GetValueOrDefault(agentId));

        public Task UpsertAsync(AgentDefinition agent, CancellationToken cancellationToken = default)
        {
            _agents[agent.Id] = agent;
            return Task.CompletedTask;
        }
    }

    private static AgentDefinition CreateAgent(
        Guid? id = null,
        string name = "Agent",
        AgentConnectionMode connectionMode = AgentConnectionMode.Manual,
        AgentAvailability availability = AgentAvailability.Unknown,
        AgentAuthenticationState authenticationState = AgentAuthenticationState.Unknown,
        AgentEntitlementState entitlementState = AgentEntitlementState.Unknown,
        IReadOnlyList<string>? capabilities = null,
        IReadOnlyList<string>? limitations = null,
        IReadOnlyList<AgentRole>? roleCapabilities = null,
        IReadOnlyList<AgentConnectionMode>? supportedConnectionModes = null,
        IReadOnlyList<AgentRolePolicyMetadata>? rolePolicyMetadata = null) => new(
        id ?? Guid.NewGuid(),
        name,
        "executor",
        connectionMode,
        availability,
        enabled: true,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        capabilities: capabilities,
        limitations: limitations,
        roleCapabilities: roleCapabilities ?? [AgentRole.Executor],
        supportedConnectionModes: supportedConnectionModes ?? [connectionMode],
        authenticationState: authenticationState,
        entitlementState: entitlementState,
        rolePolicyMetadata: rolePolicyMetadata);

    private sealed class InMemoryAgentOverrideRepository : IAgentProjectOverrideRepository
    {
        private readonly Dictionary<(Guid ProjectId, Guid AgentId), AgentProjectOverride> _overrides = [];

        public Task<IReadOnlyList<AgentProjectOverride>> GetAllAsync(
            Guid projectId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentProjectOverride>>(
                _overrides
                    .Where(pair => pair.Key.ProjectId == projectId)
                    .Select(pair => pair.Value)
                    .ToArray());

        public Task<AgentProjectOverride?> GetAsync(
            Guid projectId,
            Guid agentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_overrides.GetValueOrDefault((projectId, agentId)));

        public Task UpsertAsync(
            AgentProjectOverride projectOverride,
            CancellationToken cancellationToken = default)
        {
            _overrides[(projectOverride.ProjectId, projectOverride.AgentId)] = projectOverride;
            return Task.CompletedTask;
        }
    }
}
