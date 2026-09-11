using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Time;

namespace AIUsageMonitor.Connection.Tests;

public sealed class ProjectOnboardingServiceTests
{
    [Fact]
    public async Task BlankRequiredValuesAreRejectedWithoutCreatingProject()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.CompleteAsync(new ProjectOnboardingRequest
        {
            Name = " ",
            LocalPath = "C:\\workspace",
            SkipRepository = true
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ProjectOnboardingCompletionStatus.FailedBeforeProjectCreation, result.Status);
        Assert.Contains("Project name", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fixture.Projects.Items);
    }

    [Fact]
    public async Task ExplicitRepositoryAndTrackerSkipProducesReadyContext()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.CompleteAsync(new ProjectOnboardingRequest
        {
            Name = "Local project",
            LocalPath = "C:\\workspace",
            SkipRepository = true,
            SkipTracker = true
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Project);
        Assert.NotNull(result.Context);
        Assert.Equal(RepositorySelectionState.Skipped, result.Context!.Repository.Selection);
        Assert.Equal(TrackerReferenceState.Skipped, result.Context.Tracker.State);
        Assert.Equal(CurrentWorkState.NotSelected, result.Context.CurrentWork.State);
        Assert.Equal(ProjectNextSafeAction.ReadyForPlanning, result.Context.NextSafeAction);
        Assert.Equal(ProjectContextContract.CurrentVersion, result.Context.ContractVersion);
        Assert.Equal(6, result.Context.ModelRoleReferences.Count);
        Assert.All(result.Context.ModelRoleReferences, model =>
        {
            Assert.True(model.Enabled);
            Assert.Equal(AgentAvailability.Unknown, model.Availability);
            Assert.Equal(AgentAuthenticationState.Unknown, model.Authentication);
            Assert.Equal(AgentEntitlementState.Unknown, model.Entitlement);
        });
        Assert.Equal(6, fixture.Agents.Items.Count);
        Assert.Equal(6, fixture.Agents.UpsertCount);
    }

    [Fact]
    public async Task VerifiedRepositoryAndTrackerAreReferenceOnly()
    {
        var fixture = CreateFixture(new LocalRepositoryInspection(
            RepositoryVerificationStatus.AvailableClean,
            "C:\\workspace\\app",
            repositoryRoot: "C:\\workspace",
            localPathIsRepositoryRoot: false,
            branchName: "feature/onboarding",
            remotes: [new RepositoryRemote("origin", "https://github.com/example/app.git")],
            capturedAt: new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero)));

        var result = await fixture.Service.CompleteAsync(new ProjectOnboardingRequest
        {
            Name = "Git project",
            LocalPath = "C:\\workspace\\app",
            RepositoryInspection = fixture.Inspection,
            RepositoryDefaultBranch = "feature/onboarding",
            SkipTracker = false,
            TrackerType = "Jira",
            TrackerReference = "APO-39"
        });

        Assert.True(result.Succeeded);
        Assert.Equal("GitHub", result.Project!.RepositoryProvider);
        Assert.Equal("feature/onboarding", result.Project.DefaultBranch);
        Assert.Equal("https://github.com/example/app.git", result.Project.RepositoryUrl);
        Assert.Equal("VerifiedLocal", result.Project.RepositoryMetadata["integrationState"]);
        Assert.Equal(TrackerReferenceState.ConfiguredUnverified, result.Context!.Tracker.State);
        Assert.Equal("Jira", result.Context.Tracker.Type);
        Assert.Equal("APO-39", result.Context.Tracker.Reference);
    }

    [Fact]
    public async Task CanonicalDuplicateLocalRootIsRejectedBeforeAnyRegistrationSideEffects()
    {
        var fixture = CreateFixture();
        var existing = new Project(
            Guid.NewGuid(),
            "Existing workspace",
            "C:\\Workspace",
            null,
            ProjectStatus.Active,
            new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero));
        fixture.Projects.Items.Add(existing);

        var result = await fixture.Service.CompleteAsync(new ProjectOnboardingRequest
        {
            Name = "Duplicate workspace",
            LocalPath = "C:\\workspace\\",
            SkipRepository = true
        });

        Assert.Equal(ProjectOnboardingCompletionStatus.AlreadyRegistered, result.Status);
        Assert.False(result.Succeeded);
        Assert.Same(existing, result.ExistingProject);
        Assert.Single(fixture.Projects.Items);
        Assert.Empty(fixture.Agents.Items);
    }

    [Fact]
    public async Task DetachedOrUnbornRepositoryCannotInventDefaultBranch()
    {
        var fixture = CreateFixture(new LocalRepositoryInspection(
            RepositoryVerificationStatus.AvailableClean,
            "C:\\workspace",
            repositoryRoot: "C:\\workspace",
            branchName: null,
            isDetachedHead: true));

        var result = await fixture.Service.CompleteAsync(new ProjectOnboardingRequest
        {
            Name = "Detached project",
            LocalPath = "C:\\workspace",
            RepositoryInspection = fixture.Inspection,
            SkipTracker = true
        });

        Assert.False(result.Succeeded);
        Assert.Contains("usable repository branch", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fixture.Projects.Items);
    }

    [Fact]
    public async Task DeselectingAgentCreatesOnlyProjectScopedRestriction()
    {
        var fixture = CreateFixture();
        var defaults = fixture.Catalog.GetDefaults();

        var first = await fixture.Service.CompleteAsync(new ProjectOnboardingRequest
        {
            Name = "Project A",
            LocalPath = "C:\\a",
            SkipRepository = true,
            EnabledAgentIds = defaults.Skip(1).Select(agent => agent.Id).ToArray()
        });
        var second = await fixture.Service.CompleteAsync(new ProjectOnboardingRequest
        {
            Name = "Project B",
            LocalPath = "C:\\b",
            SkipRepository = true
        });

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.NotNull(first.Project);
        Assert.NotNull(second.Project);
        Assert.Single(fixture.Overrides.Items);
        Assert.Equal(first.Project!.Id, fixture.Overrides.Items[0].ProjectId);
        Assert.Equal(defaults[0].Id, fixture.Overrides.Items[0].AgentId);
        Assert.False(first.Context!.ModelRoleReferences.Single(model => model.AgentId == defaults[0].Id).Enabled);
        Assert.True(second.Context!.ModelRoleReferences.Single(model => model.AgentId == defaults[0].Id).Enabled);
    }

    [Fact]
    public async Task ExistingGlobalDefaultIsPreservedAndBootstrapIsIdempotent()
    {
        var fixture = CreateFixture();
        var existing = fixture.Catalog.GetDefaults()[0];
        var preserved = new AgentDefinition(
            existing.Id,
            existing.Name,
            existing.Role,
            AgentConnectionMode.Manual,
            AgentAvailability.AuthenticationRequired,
            enabled: true,
            existing.CreatedAt,
            existing.UpdatedAt,
            provider: existing.Provider,
            roleCapabilities: existing.RoleCapabilities,
            authenticationState: AgentAuthenticationState.AuthenticationRequired,
            entitlementState: AgentEntitlementState.Unknown,
            rolePolicyMetadata: existing.RolePolicyMetadata);
        await fixture.Agents.UpsertAsync(preserved);

        var result = await fixture.Service.CompleteAsync(new ProjectOnboardingRequest
        {
            Name = "Preserved project",
            LocalPath = "C:\\preserved",
            SkipRepository = true
        });

        Assert.True(result.Succeeded);
        Assert.Equal(6, fixture.Agents.Items.Count);
        Assert.Equal(6, fixture.Agents.UpsertCount); // one pre-seed plus five absent defaults
        var actual = fixture.Agents.Items.Single(agent => agent.Id == existing.Id);
        Assert.Equal(AgentConnectionMode.Manual, actual.ConnectionMode);
        Assert.Equal(AgentAvailability.AuthenticationRequired, actual.Availability);
        Assert.Equal(AgentAuthenticationState.AuthenticationRequired, actual.AuthenticationState);
    }

    [Fact]
    public async Task ContextResolverReturnsReadyOnlyForPersistedMatchingContext()
    {
        var fixture = CreateFixture();
        var created = await fixture.Service.CompleteAsync(new ProjectOnboardingRequest
        {
            Name = "Resolvable project",
            LocalPath = "C:\\resolvable",
            SkipRepository = true
        });
        var resolver = new ProjectContextResolver(fixture.Projects, fixture.Contexts, fixture.AgentRegistry);

        var ready = await resolver.ResolveAsync(created.Project!.Id);
        var missing = await resolver.ResolveAsync(Guid.NewGuid());

        Assert.Equal(ProjectContextResolutionState.Ready, ready.State);
        Assert.NotNull(ready.View);
        Assert.Equal(created.Context!.ContextId, ready.View!.Context.ContextId);
        Assert.Equal(ProjectContextResolutionState.ProjectNotFound, missing.State);
    }

    [Fact]
    public async Task ContextPersistenceFailureAfterProjectCreationReturnsPartialProject()
    {
        var fixture = CreateFixture();
        fixture.Contexts.Failure = new IOException("simulated context write failure");

        var result = await fixture.Service.CompleteAsync(new ProjectOnboardingRequest
        {
            Name = "Partial context project",
            LocalPath = "C:\\partial-context",
            SkipRepository = true
        });

        Assert.Equal(ProjectOnboardingCompletionStatus.PartialProjectCreated, result.Status);
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Project);
        Assert.Single(fixture.Projects.Items);
        var resolver = new ProjectContextResolver(fixture.Projects, fixture.Contexts, fixture.AgentRegistry);
        var resolution = await resolver.ResolveAsync(result.Project!.Id);
        Assert.Equal(ProjectContextResolutionState.ContextMissing, resolution.State);
    }

    [Fact]
    public async Task OverrideFailureAfterProjectCreationReturnsPartialProject()
    {
        var fixture = CreateFixture();
        fixture.Overrides.Failure = new IOException("simulated override write failure");
        var disabledAgent = fixture.Catalog.GetDefaults()[0].Id;

        var result = await fixture.Service.CompleteAsync(new ProjectOnboardingRequest
        {
            Name = "Partial override project",
            LocalPath = "C:\\partial-override",
            SkipRepository = true,
            EnabledAgentIds = fixture.Catalog.GetDefaults().Skip(1).Select(agent => agent.Id).ToArray()
        });

        Assert.Equal(ProjectOnboardingCompletionStatus.PartialProjectCreated, result.Status);
        Assert.NotNull(result.Project);
        Assert.Single(fixture.Projects.Items);
        Assert.Equal(disabledAgent, fixture.Catalog.GetDefaults()[0].Id);
    }

    [Fact]
    public async Task CancellationAfterProjectCreationReturnsPartialProject()
    {
        var fixture = CreateFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Overrides.CancelSource = cancellation;

        var result = await fixture.Service.CompleteAsync(new ProjectOnboardingRequest
        {
            Name = "Cancelled partial project",
            LocalPath = "C:\\cancelled-partial",
            SkipRepository = true,
            EnabledAgentIds = fixture.Catalog.GetDefaults().Skip(1).Select(agent => agent.Id).ToArray()
        }, cancellation.Token);

        Assert.Equal(ProjectOnboardingCompletionStatus.PartialProjectCreated, result.Status);
        Assert.Contains("cancelled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.Project);
        Assert.Single(fixture.Projects.Items);
    }

    [Fact]
    public async Task ResolverDoesNotReturnReadyForUnacceptedInspectedRepositoryEvidence()
    {
        var fixture = CreateFixture();
        var created = await fixture.Service.CompleteAsync(new ProjectOnboardingRequest
        {
            Name = "Unaccepted repository evidence",
            LocalPath = "C:\\unaccepted-evidence",
            SkipRepository = true
        });
        var invalidForReady = new ProjectContextReference(
            created.Project!.Id,
            Guid.NewGuid(),
            ProjectContextContract.CurrentVersion,
            created.Project.CreatedAt,
            created.Project.UpdatedAt,
            new ProjectRepositoryContextReference(
                created.Project.Id,
                created.Project.LocalPath,
                RepositorySelectionState.Inspect,
                RepositoryVerificationStatus.NotGitRepository),
            new ProjectTrackerContextReference(TrackerReferenceState.Skipped),
            created.Context!.ModelRoleReferences,
            created.Context.CurrentWork,
            created.Context.GovernanceReferences,
            created.Context.RoutingPolicyReference,
            created.Context.SafetyPolicyReference,
            ProjectNextSafeAction.ReadyForPlanning);
        await fixture.Contexts.UpsertAsync(invalidForReady);

        var resolver = new ProjectContextResolver(fixture.Projects, fixture.Contexts, fixture.AgentRegistry);
        var result = await resolver.ResolveAsync(created.Project.Id);

        Assert.Equal(ProjectContextResolutionState.Incomplete, result.State);
    }

    [Fact]
    public async Task SuccessfulRegistrationRecordsTheProjectFolderPreference()
    {
        var preferences = new RecordingFolderPreferences();
        var fixture = CreateFixture(folderPreferences: preferences);

        var result = await fixture.Service.CompleteAsync(new ProjectOnboardingRequest
        {
            Name = "Recorded project",
            LocalPath = "C:\\workspace\\recorded",
            SkipRepository = true,
            SkipTracker = true
        });

        Assert.True(result.Succeeded);
        Assert.Equal(["C:\\workspace\\recorded"], preferences.Recorded);
    }

    [Fact]
    public async Task UnsuccessfulOnboardingNeverMovesTheProjectFolderPreference()
    {
        var preferences = new RecordingFolderPreferences();
        var fixture = CreateFixture(folderPreferences: preferences);
        fixture.Projects.Items.Add(new Project(
            Guid.NewGuid(),
            "Existing workspace",
            "C:\\Workspace",
            null,
            ProjectStatus.Active,
            new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero)));

        var duplicate = await fixture.Service.CompleteAsync(new ProjectOnboardingRequest
        {
            Name = "Duplicate workspace",
            LocalPath = "C:\\workspace\\",
            SkipRepository = true
        });
        var invalid = await fixture.Service.CompleteAsync(new ProjectOnboardingRequest
        {
            Name = " ",
            LocalPath = "C:\\workspace\\other",
            SkipRepository = true
        });

        Assert.Equal(ProjectOnboardingCompletionStatus.AlreadyRegistered, duplicate.Status);
        Assert.False(invalid.Succeeded);
        Assert.Empty(preferences.Recorded);
    }

    [Fact]
    public async Task FolderPreferenceFailureDoesNotDowngradeAProvenRegistration()
    {
        var preferences = new RecordingFolderPreferences
        {
            Failure = new IOException("simulated settings write failure")
        };
        var fixture = CreateFixture(folderPreferences: preferences);

        var result = await fixture.Service.CompleteAsync(new ProjectOnboardingRequest
        {
            Name = "Resilient project",
            LocalPath = "C:\\workspace\\resilient",
            SkipRepository = true,
            SkipTracker = true
        });

        Assert.True(result.Succeeded);
        Assert.Equal(ProjectOnboardingCompletionStatus.Succeeded, result.Status);
        Assert.False(result.IsPartialProjectCreated);
        Assert.Single(fixture.Projects.Items);
    }

    private sealed class RecordingFolderPreferences : IProjectFolderPreferenceService
    {
        public List<string> Recorded { get; } = [];

        public Exception? Failure { get; init; }

        public Task<string?> GetPreferredPickerRootAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task RecordSuccessfulProjectFolderAsync(
            string projectLocalPath,
            CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Recorded.Add(projectLocalPath);
            return Task.CompletedTask;
        }
    }

    private static Fixture CreateFixture(
        LocalRepositoryInspection? inspection = null,
        IProjectFolderPreferenceService? folderPreferences = null)
    {
        var projects = new MemoryProjectRepository();
        var registry = new ProjectRegistryService(projects, new FixedClock());
        var agents = new MemoryAgentRepository();
        var overrides = new MemoryOverrideRepository();
        var catalog = new DefaultAgentCatalog();
        var agentRegistry = new AgentRegistryService(agents, overrides);
        var context = new MemoryContextRepository();
        var inspector = new MemoryInspector(inspection ?? new LocalRepositoryInspection(
            RepositoryVerificationStatus.NotGitRepository,
            "C:\\workspace"));
        var service = new ProjectOnboardingService(
            registry,
            inspector,
            agents,
            catalog,
            overrides,
            agentRegistry,
            context,
            new FixedClock(),
            folderPreferences);
        return new Fixture(service, projects, agents, overrides, catalog, inspector.Inspection, context, agentRegistry);
    }

    private sealed record Fixture(
        ProjectOnboardingService Service,
        MemoryProjectRepository Projects,
        MemoryAgentRepository Agents,
        MemoryOverrideRepository Overrides,
        DefaultAgentCatalog Catalog,
        LocalRepositoryInspection Inspection,
        MemoryContextRepository Contexts,
        AgentRegistryService AgentRegistry);

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class MemoryInspector(LocalRepositoryInspection inspection) : ILocalRepositoryInspector
    {
        public LocalRepositoryInspection Inspection { get; } = inspection;

        public Task<LocalRepositoryInspection> InspectAsync(
            string registeredLocalPath,
            string? registeredRepositoryUrl = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Inspection);
    }

    private sealed class MemoryProjectRepository : IProjectRepository
    {
        public List<Project> Items { get; } = [];

        public Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Project>>(Items.ToArray());

        public Task<Project?> GetByIdAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.SingleOrDefault(project => project.Id == projectId));

        public Task UpsertAsync(Project project, CancellationToken cancellationToken = default)
        {
            var index = Items.FindIndex(existing => existing.Id == project.Id);
            if (index >= 0) Items[index] = project;
            else Items.Add(project);
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryAgentRepository : IAgentRepository
    {
        public List<AgentDefinition> Items { get; } = [];
        public int UpsertCount { get; private set; }

        public Task<IReadOnlyList<AgentDefinition>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentDefinition>>(Items.ToArray());

        public Task<AgentDefinition?> GetByIdAsync(Guid agentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.SingleOrDefault(agent => agent.Id == agentId));

        public Task UpsertAsync(AgentDefinition agent, CancellationToken cancellationToken = default)
        {
            UpsertCount++;
            var index = Items.FindIndex(existing => existing.Id == agent.Id);
            if (index >= 0) Items[index] = agent;
            else Items.Add(agent);
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryOverrideRepository : IAgentProjectOverrideRepository
    {
        public List<AgentProjectOverride> Items { get; } = [];
        public Exception? Failure { get; set; }
        public CancellationTokenSource? CancelSource { get; set; }

        public Task<IReadOnlyList<AgentProjectOverride>> GetAllAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentProjectOverride>>(Items.Where(value => value.ProjectId == projectId).ToArray());

        public Task<AgentProjectOverride?> GetAsync(Guid projectId, Guid agentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.SingleOrDefault(value => value.ProjectId == projectId && value.AgentId == agentId));

        public Task UpsertAsync(AgentProjectOverride projectOverride, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Items.RemoveAll(value => value.ProjectId == projectOverride.ProjectId && value.AgentId == projectOverride.AgentId);
            Items.Add(projectOverride);
            CancelSource?.Cancel();
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryContextRepository : IProjectContextReferenceRepository
    {
        private readonly Dictionary<Guid, ProjectContextReference> _items = [];
        public Exception? Failure { get; set; }

        public Task<ProjectContextReadResult> GetAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.TryGetValue(projectId, out var value)
                ? new ProjectContextReadResult(ProjectContextReadState.Valid, value)
                : new ProjectContextReadResult(ProjectContextReadState.Missing));

        public Task UpsertAsync(ProjectContextReference context, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            _items[context.ProjectId] = context;
            return Task.CompletedTask;
        }
    }
}
