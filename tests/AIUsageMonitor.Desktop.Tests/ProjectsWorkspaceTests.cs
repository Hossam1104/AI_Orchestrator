using System.IO;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Desktop.ViewModels;

namespace AIUsageMonitor.Desktop.Tests;

public sealed class ProjectsWorkspaceTests
{
    [Fact]
    public async Task RegistryService_CreateGeneratesIdentityAndUsesOneTimestamp()
    {
        var now = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        var repository = new FakeProjectRepository();
        var service = new ProjectRegistryService(repository, new FixedClock(now));

        var created = await service.CreateProjectAsync(new ProjectEdit
        {
            Name = "  APO workspace  ",
            LocalPath = " C:\\workspaces\\apo ",
            Status = ProjectStatus.Active,
            GovernanceReferences = [" BRD.md ", "", "AGENTS.md"]
        });

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal(now, created.CreatedAt);
        Assert.Equal(now, created.UpdatedAt);
        Assert.Equal("APO workspace", created.Name);
        Assert.Equal("C:\\workspaces\\apo", created.LocalPath);
        Assert.Equal(["BRD.md", "AGENTS.md"], created.GovernanceReferences);
        Assert.Single(repository.Projects);
    }

    [Fact]
    public async Task RegistryService_RepositoryConfigurationRequiresDefaultBranch()
    {
        var repository = new FakeProjectRepository();
        var service = new ProjectRegistryService(
            repository,
            new FixedClock(DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateProjectAsync(new ProjectEdit
        {
            Name = "APO",
            LocalPath = "C:\\apo",
            RepositoryUrl = "https://example.invalid/apo"
        }));

        Assert.Empty(repository.Projects);
    }

    [Fact]
    public async Task RegistryService_EditPreservesIdentityTimesAndHiddenMetadata()
    {
        var createdAt = new DateTimeOffset(2026, 8, 23, 9, 0, 0, TimeSpan.Zero);
        var updatedAt = new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);
        var editAt = new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);
        var projectId = Guid.NewGuid();
        var original = new Project(
            projectId,
            "APO",
            "C:\\apo",
            "main",
            ProjectStatus.Active,
            createdAt,
            updatedAt,
            repositoryProvider: "GitHub",
            repositoryUrl: "https://example.invalid/apo",
            repositoryId: "repo-1",
            repositoryMetadata: new Dictionary<string, string?>
            {
                ["connection"] = "opaque-reference",
                ["owner"] = "team"
            },
            trackerType: "Jira",
            trackerId: "APO-35",
            trackerMetadata: new Dictionary<string, string?>
            {
                ["project"] = "APO"
            },
            governanceReferences: ["BRD.md"],
            routingPolicyReference: "routing/default",
            safetyPolicyReference: "safety/default");
        var repository = new FakeProjectRepository(original);
        var service = new ProjectRegistryService(repository, new FixedClock(editAt));

        var updated = await service.UpdateProjectAsync(projectId, new ProjectEdit
        {
            Name = "APO renamed",
            LocalPath = "C:\\apo-renamed",
            Status = ProjectStatus.Paused,
            RepositoryProvider = original.RepositoryProvider,
            RepositoryUrl = original.RepositoryUrl,
            RepositoryId = original.RepositoryId,
            DefaultBranch = original.DefaultBranch,
            TrackerType = original.TrackerType,
            TrackerId = original.TrackerId,
            GovernanceReferences = [" BRD.md ", "", "TASK.md"],
            RoutingPolicyReference = original.RoutingPolicyReference,
            SafetyPolicyReference = original.SafetyPolicyReference
        });

        Assert.Equal(projectId, updated.Id);
        Assert.Equal(createdAt, updated.CreatedAt);
        Assert.Equal(editAt, updated.UpdatedAt);
        Assert.Equal(ProjectStatus.Paused, updated.Status);
        Assert.Equal(original.RepositoryMetadata, updated.RepositoryMetadata);
        Assert.Equal(original.TrackerMetadata, updated.TrackerMetadata);
        Assert.Equal(["BRD.md", "TASK.md"], updated.GovernanceReferences);
    }

    [Fact]
    public async Task ProjectsWorkspace_InitializeLoadsProjectsAndExposesSelection()
    {
        var repository = new FakeProjectRepository(
            CreateProject("Alpha", "C:\\alpha"),
            CreateProject("Beta", "C:\\beta"));
        var viewModel = CreateViewModel(repository);

        await viewModel.InitializeAsync();

        Assert.False(viewModel.IsLoading);
        Assert.True(viewModel.HasProjects);
        Assert.Equal(2, viewModel.FilteredProjects.Count);
        Assert.Equal("Alpha", viewModel.SelectedProject!.Name);
        Assert.False(viewModel.ShowEmptyRegistryState);
    }

    [Fact]
    public async Task ProjectsWorkspace_EmptyRegistryHasDedicatedEmptyState()
    {
        var viewModel = CreateViewModel(new FakeProjectRepository());

        await viewModel.InitializeAsync();

        Assert.False(viewModel.HasProjects);
        Assert.True(viewModel.ShowEmptyRegistryState);
        Assert.False(viewModel.ShowNoMatchState);
    }

    [Fact]
    public async Task ProjectsWorkspace_SearchAndStatusFilterStayInMemory()
    {
        var repository = new FakeProjectRepository(
            CreateProject("Alpha", "C:\\work\\alpha", ProjectStatus.Active),
            CreateProject("Beta", "D:\\work\\paused", ProjectStatus.Paused),
            CreateProject("Archive", "E:\\old", ProjectStatus.Archived));
        var viewModel = CreateViewModel(repository);
        await viewModel.InitializeAsync();

        viewModel.SearchText = "paused";
        Assert.Equal("Beta", Assert.Single(viewModel.FilteredProjects).Name);
        Assert.Equal(1, repository.GetAllCount);

        viewModel.SearchText = string.Empty;
        viewModel.SelectedStatusFilter = ProjectStatusFilter.Archived;
        Assert.Equal("Archive", Assert.Single(viewModel.FilteredProjects).Name);
        Assert.Equal(ProjectStatusFilter.Archived, viewModel.SelectedStatusFilter);
    }

    [Fact]
    public async Task ProjectsWorkspace_NoMatchStateDiffersFromEmptyRegistry()
    {
        var viewModel = CreateViewModel(new FakeProjectRepository(CreateProject("Alpha", "C:\\alpha")));
        await viewModel.InitializeAsync();

        viewModel.SearchText = "does-not-exist";

        Assert.True(viewModel.ShowNoMatchState);
        Assert.False(viewModel.ShowEmptyRegistryState);
    }

    [Fact]
    public async Task ProjectsWorkspace_SelectingProjectsKeepsDetailsIsolated()
    {
        var alpha = CreateProject("Alpha", "C:\\alpha");
        var beta = CreateProject("Beta", "C:\\beta");
        var viewModel = CreateViewModel(new FakeProjectRepository(alpha, beta));
        await viewModel.InitializeAsync();

        viewModel.SelectProject(alpha.Id);
        Assert.Equal(alpha.Id, viewModel.SelectedProject!.Id);
        Assert.Equal("C:\\alpha", viewModel.SelectedProject.LocalPath);

        viewModel.SelectProject(beta.Id);
        Assert.Equal(beta.Id, viewModel.SelectedProject!.Id);
        Assert.Equal("C:\\beta", viewModel.SelectedProject.LocalPath);
    }

    [Fact]
    public async Task ProjectsWorkspace_NewProjectOpensCleanEditor()
    {
        var viewModel = CreateViewModel(new FakeProjectRepository(CreateProject("Alpha", "C:\\alpha")));
        await viewModel.InitializeAsync();

        viewModel.NewProjectCommand.Execute(null);

        Assert.True(viewModel.IsEditing);
        Assert.True(viewModel.IsCreating);
        Assert.Equal(string.Empty, viewModel.EditorName);
        Assert.Equal(string.Empty, viewModel.EditorLocalPath);
        Assert.Equal(ProjectStatus.Active, viewModel.EditorStatus);
    }

    [Fact]
    public async Task ProjectsWorkspace_EditPopulatesVisibleFieldsAndPreservesHiddenMetadataOnSave()
    {
        var project = new Project(
            Guid.NewGuid(),
            "Alpha",
            "C:\\alpha",
            "main",
            ProjectStatus.Active,
            EditorViewModelFixedClock.AddDays(-2),
            EditorViewModelFixedClock.AddDays(-1),
            repositoryProvider: "GitHub",
            repositoryUrl: "https://example.invalid/alpha",
            repositoryId: "alpha-1",
            repositoryMetadata: new Dictionary<string, string?> { ["opaque"] = "keep" },
            trackerType: "Jira",
            trackerId: "APO-1",
            trackerMetadata: new Dictionary<string, string?> { ["opaque"] = "also-keep" });
        var repository = new FakeProjectRepository(project);
        var viewModel = CreateViewModel(repository);
        await viewModel.InitializeAsync();

        viewModel.EditProjectCommand.Execute(null);
        viewModel.EditorName = "Alpha updated";
        await viewModel.SaveAsync();

        var saved = Assert.Single(repository.Projects);
        Assert.Equal("Alpha updated", saved.Name);
        Assert.Equal(project.Id, saved.Id);
        Assert.Equal(project.CreatedAt, saved.CreatedAt);
        Assert.Equal(project.RepositoryMetadata, saved.RepositoryMetadata);
        Assert.Equal(project.TrackerMetadata, saved.TrackerMetadata);
        Assert.False(viewModel.IsEditing);
    }

    [Fact]
    public async Task ProjectsWorkspace_CreateSuccessUpdatesCollectionAndSelection()
    {
        var repository = new FakeProjectRepository();
        var viewModel = CreateViewModel(repository);
        await viewModel.InitializeAsync();
        viewModel.NewProjectCommand.Execute(null);
        viewModel.EditorName = "New project";
        viewModel.EditorLocalPath = "C:\\new-project";
        viewModel.EditorGovernanceReferencesText = "BRD.md\r\n\r\nTASK.md";

        await viewModel.SaveAsync();

        var saved = Assert.Single(repository.Projects);
        Assert.Single(viewModel.Projects);
        Assert.Equal(saved.Id, viewModel.SelectedProject!.Id);
        Assert.Equal(["BRD.md", "TASK.md"], saved.GovernanceReferences);
    }

    [Fact]
    public async Task ProjectsWorkspace_ArchiveAndRestoreUseLifecycleStatus()
    {
        var project = CreateProject("Alpha", "C:\\alpha");
        var repository = new FakeProjectRepository(project);
        var viewModel = CreateViewModel(repository);
        await viewModel.InitializeAsync();

        viewModel.EditProjectCommand.Execute(null);
        viewModel.EditorStatus = ProjectStatus.Archived;
        await viewModel.SaveAsync();
        Assert.Equal(ProjectStatus.Archived, Assert.Single(repository.Projects).Status);

        viewModel.EditProjectCommand.Execute(null);
        viewModel.EditorStatus = ProjectStatus.Active;
        await viewModel.SaveAsync();
        Assert.Equal(ProjectStatus.Active, Assert.Single(repository.Projects).Status);
    }

    [Fact]
    public async Task ProjectsWorkspace_ValidationBlocksBlankNameLocalPathAndRepositoryBranch()
    {
        var viewModel = CreateViewModel(new FakeProjectRepository());
        await viewModel.InitializeAsync();
        viewModel.NewProjectCommand.Execute(null);

        await viewModel.SaveAsync();
        Assert.Equal("Project name is required.", viewModel.ValidationMessage);

        viewModel.EditorName = "APO";
        await viewModel.SaveAsync();
        Assert.Equal("Local path is required.", viewModel.ValidationMessage);

        viewModel.EditorLocalPath = "C:\\apo";
        viewModel.EditorRepositoryUrl = "https://example.invalid/apo";
        await viewModel.SaveAsync();
        Assert.Equal(
            "Default branch is required when repository information is configured.",
            viewModel.ValidationMessage);
    }

    [Fact]
    public async Task ProjectsWorkspace_WriteFailureRetainsEditorAndShowsBoundedError()
    {
        var repository = new FakeProjectRepository { WriteException = new IOException("private path") };
        var viewModel = CreateViewModel(repository);
        await viewModel.InitializeAsync();
        viewModel.NewProjectCommand.Execute(null);
        viewModel.EditorName = "Keep this value";
        viewModel.EditorLocalPath = "C:\\keep-this-value";

        await viewModel.SaveAsync();

        Assert.True(viewModel.IsEditing);
        Assert.Equal("Keep this value", viewModel.EditorName);
        Assert.Equal("C:\\keep-this-value", viewModel.EditorLocalPath);
        Assert.Equal("Project could not be saved.", viewModel.ErrorMessage);
        Assert.DoesNotContain("private path", viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.Empty(viewModel.Projects);
    }

    [Fact]
    public async Task ProjectsWorkspace_LoadFailureIsRetryableAndDoesNotExposeException()
    {
        var repository = new FakeProjectRepository { ReadException = new IOException("private path") };
        var viewModel = CreateViewModel(repository);

        await viewModel.InitializeAsync();
        Assert.True(viewModel.HasLoadError);
        Assert.Equal("Projects could not be loaded.", viewModel.ErrorMessage);
        Assert.DoesNotContain("private path", viewModel.ErrorMessage, StringComparison.Ordinal);

        repository.ReadException = null;
        repository.Projects.Add(CreateProject("Recovered", "C:\\recovered"));
        await viewModel.RefreshAsync();
        Assert.False(viewModel.HasLoadError);
        Assert.Equal("Recovered", Assert.Single(viewModel.Projects).Name);
    }

    [Fact]
    public async Task ProjectsWorkspace_DuplicateSaveIsPreventedWhileSaving()
    {
        var repository = new FakeProjectRepository { WriteDelay = TimeSpan.FromMilliseconds(60) };
        var viewModel = CreateViewModel(repository);
        await viewModel.InitializeAsync();
        viewModel.NewProjectCommand.Execute(null);
        viewModel.EditorName = "One save";
        viewModel.EditorLocalPath = "C:\\one-save";

        var first = viewModel.SaveAsync();
        var second = viewModel.SaveAsync();
        await Task.WhenAll(first, second);

        Assert.Equal(1, repository.UpsertCount);
        Assert.Single(viewModel.Projects);
    }

    [Fact]
    public async Task ProjectsWorkspace_DegradedModeDoesNotCreateAlternateRegistry()
    {
        var viewModel = new ProjectsViewModel();

        await viewModel.InitializeAsync();

        Assert.False(viewModel.IsStorageAvailable);
        Assert.True(viewModel.ShowStorageUnavailableState);
        Assert.Equal("Project storage unavailable.", viewModel.ErrorMessage);
        Assert.False(viewModel.NewProjectCommand.CanExecute(null));
    }

    [Fact]
    public async Task EditTarget_DoesNotFollowSelectionChange()
    {
        var alpha = CreateProject("Alpha", "C:\\alpha");
        var beta = CreateProject("Beta", "C:\\beta");
        var repository = new FakeProjectRepository(alpha, beta);
        var viewModel = CreateViewModel(repository);
        await viewModel.InitializeAsync();

        viewModel.SelectProject(alpha.Id);
        viewModel.EditProjectCommand.Execute(null);
        viewModel.EditorName = "Alpha edited";

        viewModel.SelectedProjectCard = viewModel.FilteredProjects.Single(card => card.Project.Id == beta.Id);

        await viewModel.SaveAsync();

        Assert.Equal("Alpha edited", repository.Projects.Single(project => project.Id == alpha.Id).Name);
        Assert.Equal("Beta", repository.Projects.Single(project => project.Id == beta.Id).Name);
    }

    [Fact]
    public async Task EditTarget_DoesNotFollowFilterSelectionLoss()
    {
        var alpha = CreateProject("Alpha", "C:\\alpha");
        var beta = CreateProject("Beta", "C:\\beta");
        var repository = new FakeProjectRepository(alpha, beta);
        var viewModel = CreateViewModel(repository);
        await viewModel.InitializeAsync();

        viewModel.SelectProject(alpha.Id);
        viewModel.EditProjectCommand.Execute(null);
        viewModel.EditorName = "Alpha edited";

        viewModel.SearchText = "beta";
        Assert.Null(viewModel.SelectedProjectCard);

        await viewModel.SaveAsync();

        Assert.Equal("Alpha edited", repository.Projects.Single(project => project.Id == alpha.Id).Name);
        Assert.Equal("Beta", repository.Projects.Single(project => project.Id == beta.Id).Name);
    }

    [Fact]
    public async Task FailedSave_RetryUsesOriginalEditTarget()
    {
        var alpha = CreateProject("Alpha", "C:\\alpha");
        var beta = CreateProject("Beta", "C:\\beta");
        var repository = new FakeProjectRepository(alpha, beta) { WriteException = new IOException("private path") };
        var viewModel = CreateViewModel(repository);
        await viewModel.InitializeAsync();

        viewModel.SelectProject(alpha.Id);
        viewModel.EditProjectCommand.Execute(null);
        viewModel.EditorName = "Alpha edited";

        await viewModel.SaveAsync();

        Assert.True(viewModel.IsEditing);
        Assert.Equal("Alpha edited", viewModel.EditorName);
        Assert.Equal("Alpha", repository.Projects.Single(project => project.Id == alpha.Id).Name);

        repository.WriteException = null;
        viewModel.SelectedProjectCard = viewModel.FilteredProjects.Single(card => card.Project.Id == beta.Id);

        await viewModel.SaveAsync();

        Assert.False(viewModel.IsEditing);
        Assert.Equal("Alpha edited", repository.Projects.Single(project => project.Id == alpha.Id).Name);
        Assert.Equal("Beta", repository.Projects.Single(project => project.Id == beta.Id).Name);
    }

    [Fact]
    public async Task NewAndRefreshCommandsDisabledWhileEditing()
    {
        var viewModel = CreateViewModel(new FakeProjectRepository(CreateProject("Alpha", "C:\\alpha")));
        await viewModel.InitializeAsync();

        Assert.True(viewModel.RefreshCommand.CanExecute(null));
        Assert.True(viewModel.NewProjectCommand.CanExecute(null));

        viewModel.EditProjectCommand.Execute(null);

        Assert.False(viewModel.RefreshCommand.CanExecute(null));
        Assert.False(viewModel.NewProjectCommand.CanExecute(null));
        Assert.False(viewModel.IsRegistryInteractionEnabled);
    }

    [Fact]
    public async Task CancelClearsEditTargetAndRestoresRegistryInteraction()
    {
        var alpha = CreateProject("Alpha", "C:\\alpha");
        var beta = CreateProject("Beta", "C:\\beta");
        var repository = new FakeProjectRepository(alpha, beta);
        var viewModel = CreateViewModel(repository);
        await viewModel.InitializeAsync();

        viewModel.SelectProject(alpha.Id);
        viewModel.EditProjectCommand.Execute(null);
        viewModel.EditorName = "Alpha edited";

        viewModel.CancelEditCommand.Execute(null);

        Assert.False(viewModel.IsEditing);
        Assert.True(viewModel.RefreshCommand.CanExecute(null));
        Assert.True(viewModel.NewProjectCommand.CanExecute(null));
        Assert.True(viewModel.IsRegistryInteractionEnabled);

        viewModel.SelectProject(beta.Id);
        viewModel.EditProjectCommand.Execute(null);
        viewModel.EditorName = "Beta edited";
        await viewModel.SaveAsync();

        Assert.Equal("Alpha", repository.Projects.Single(project => project.Id == alpha.Id).Name);
        Assert.Equal("Beta edited", repository.Projects.Single(project => project.Id == beta.Id).Name);
    }

    [Fact]
    public async Task SuccessfulSaveClearsEditTargetAndRestoresRegistryInteraction()
    {
        var viewModel = CreateViewModel(new FakeProjectRepository(CreateProject("Alpha", "C:\\alpha")));
        await viewModel.InitializeAsync();

        viewModel.EditProjectCommand.Execute(null);
        viewModel.EditorName = "Alpha edited";

        Assert.False(viewModel.RefreshCommand.CanExecute(null));
        Assert.False(viewModel.NewProjectCommand.CanExecute(null));

        await viewModel.SaveAsync();

        Assert.False(viewModel.IsEditing);
        Assert.True(viewModel.RefreshCommand.CanExecute(null));
        Assert.True(viewModel.NewProjectCommand.CanExecute(null));
        Assert.True(viewModel.IsRegistryInteractionEnabled);
    }

    [Fact]
    public async Task SearchPreservesSelectionWhenSelectedProjectStillMatches()
    {
        var alpha = CreateProject("Alpha", "C:\\alpha");
        var beta = CreateProject("Beta", "C:\\beta");
        var repository = new FakeProjectRepository(alpha, beta);
        var viewModel = CreateViewModel(repository);
        await viewModel.InitializeAsync();

        viewModel.SelectProject(alpha.Id);

        viewModel.SearchText = "alp";

        var selected = viewModel.SelectedProjectCard;
        Assert.NotNull(selected);
        Assert.Equal(alpha.Id, selected!.Project.Id);
        Assert.Same(selected, Assert.Single(viewModel.FilteredProjects));
    }

    [Fact]
    public async Task StatusFilterPreservesSelectionWhenProjectStillMatches()
    {
        var alpha = CreateProject("Alpha", "C:\\alpha", ProjectStatus.Active);
        var beta = CreateProject("Beta", "C:\\beta", ProjectStatus.Paused);
        var repository = new FakeProjectRepository(alpha, beta);
        var viewModel = CreateViewModel(repository);
        await viewModel.InitializeAsync();

        viewModel.SelectProject(alpha.Id);

        viewModel.SelectedStatusFilter = ProjectStatusFilter.Active;

        var selected = viewModel.SelectedProjectCard;
        Assert.NotNull(selected);
        Assert.Equal(alpha.Id, selected!.Project.Id);
        Assert.Same(selected, Assert.Single(viewModel.FilteredProjects));
    }

    [Fact]
    public async Task SearchClearsSelectionWhenProjectNoLongerMatches()
    {
        var alpha = CreateProject("Alpha", "C:\\alpha");
        var repository = new FakeProjectRepository(alpha);
        var viewModel = CreateViewModel(repository);
        await viewModel.InitializeAsync();

        viewModel.SelectProject(alpha.Id);

        viewModel.SearchText = "does-not-match";

        Assert.Null(viewModel.SelectedProjectCard);
    }

    [Fact]
    public async Task StatusFilterClearsSelectionWhenProjectNoLongerMatches()
    {
        var alpha = CreateProject("Alpha", "C:\\alpha", ProjectStatus.Active);
        var repository = new FakeProjectRepository(alpha);
        var viewModel = CreateViewModel(repository);
        await viewModel.InitializeAsync();

        viewModel.SelectProject(alpha.Id);

        viewModel.SelectedStatusFilter = ProjectStatusFilter.Archived;

        Assert.Null(viewModel.SelectedProjectCard);
    }

    [Fact]
    public async Task SavedProjectSelectionUsesFilteredCollectionInstance()
    {
        var alpha = CreateProject("Alpha", "C:\\alpha");
        var repository = new FakeProjectRepository(alpha);
        var viewModel = CreateViewModel(repository);
        await viewModel.InitializeAsync();

        viewModel.SelectProject(alpha.Id);
        viewModel.EditProjectCommand.Execute(null);
        viewModel.EditorName = "Alpha edited";
        await viewModel.SaveAsync();

        var selected = viewModel.SelectedProjectCard;
        Assert.NotNull(selected);
        Assert.Same(selected, Assert.Single(viewModel.FilteredProjects));
        Assert.Equal("Alpha edited", selected!.Name);
    }

    [Fact]
    public async Task ChangingProjectStatusSoItLeavesCurrentFilterClearsSelection()
    {
        var alpha = CreateProject("Alpha", "C:\\alpha", ProjectStatus.Active);
        var repository = new FakeProjectRepository(alpha);
        var viewModel = CreateViewModel(repository);
        await viewModel.InitializeAsync();

        viewModel.SelectedStatusFilter = ProjectStatusFilter.Active;
        viewModel.SelectProject(alpha.Id);
        viewModel.EditProjectCommand.Execute(null);
        viewModel.EditorStatus = ProjectStatus.Archived;

        await viewModel.SaveAsync();

        Assert.Null(viewModel.SelectedProjectCard);
        Assert.Equal(ProjectStatus.Archived, repository.Projects.Single(project => project.Id == alpha.Id).Status);
    }

    [Fact]
    public void MainWindowNavigation_ProjectsAndExistingWorkspacesRemainIndependent()
    {
        var viewModel = new MainWindowViewModel(new AiCapacityViewModel(), new ProjectsViewModel());

        viewModel.ShowMissionControlCommand.Execute(null);
        Assert.Same(viewModel.MissionControl, viewModel.ActiveWorkspace);
        Assert.True(viewModel.IsMissionControlSelected);
        Assert.False(viewModel.IsProjectsSelected);
        Assert.False(viewModel.IsAiCapacitySelected);

        viewModel.ShowProjectsCommand.Execute(null);
        Assert.Same(viewModel.Projects, viewModel.ActiveWorkspace);
        Assert.False(viewModel.IsMissionControlSelected);
        Assert.True(viewModel.IsProjectsSelected);
        Assert.False(viewModel.IsAiCapacitySelected);
        Assert.True(viewModel.ShowProjectsCommand.CanExecute(null));

        viewModel.ShowAiCapacityCommand.Execute(null);
        Assert.Same(viewModel.AiCapacity, viewModel.ActiveWorkspace);
        Assert.False(viewModel.IsMissionControlSelected);
        Assert.False(viewModel.IsProjectsSelected);
        Assert.True(viewModel.IsAiCapacitySelected);
    }

    private static readonly DateTimeOffset EditorViewModelFixedClock =
        new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    private static ProjectsViewModel CreateViewModel(FakeProjectRepository repository) =>
        new(new ProjectRegistryService(repository, new FixedClock(EditorViewModelFixedClock)));

    private static Project CreateProject(
        string name,
        string localPath,
        ProjectStatus status = ProjectStatus.Active) =>
        new(
            Guid.NewGuid(),
            name,
            localPath,
            null,
            status,
            new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero));

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class FakeProjectRepository : IProjectRepository
    {
        public FakeProjectRepository(params Project[] projects) => Projects.AddRange(projects);

        public List<Project> Projects { get; } = [];

        public Exception? ReadException { get; set; }

        public Exception? WriteException { get; set; }

        public TimeSpan WriteDelay { get; set; }

        public int GetAllCount { get; private set; }

        public int UpsertCount { get; private set; }

        public Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            GetAllCount++;
            if (ReadException is not null)
            {
                return Task.FromException<IReadOnlyList<Project>>(ReadException);
            }

            return Task.FromResult<IReadOnlyList<Project>>(Projects.ToArray());
        }

        public Task<Project?> GetByIdAsync(Guid projectId, CancellationToken cancellationToken = default)
        {
            if (ReadException is not null)
            {
                return Task.FromException<Project?>(ReadException);
            }

            return Task.FromResult(Projects.FirstOrDefault(project => project.Id == projectId));
        }

        public async Task UpsertAsync(Project project, CancellationToken cancellationToken = default)
        {
            UpsertCount++;
            if (WriteDelay > TimeSpan.Zero)
            {
                await Task.Delay(WriteDelay, cancellationToken);
            }

            if (WriteException is not null)
            {
                throw WriteException;
            }

            var index = Projects.FindIndex(existing => existing.Id == project.Id);
            if (index >= 0)
            {
                Projects[index] = project;
            }
            else
            {
                Projects.Add(project);
            }
        }
    }
}
