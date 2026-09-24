using AIUsageMonitor.Application.MissionControl;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Validation;
using AIUsageMonitor.Application.Approvals;
using AIUsageMonitor.Desktop.ViewModels;

namespace AIUsageMonitor.Desktop.Tests;

public sealed class MissionControlViewModelTests
{
    [Fact]
    public async Task MultipleActiveProjects_DoNotCauseAnArbitrarySelection()
    {
        var alpha = CreateProject("Alpha");
        var beta = CreateProject("Beta");
        var viewModel = CreateViewModel(alpha, beta);

        await viewModel.InitializeAsync();

        Assert.Null(viewModel.SelectedProject);
        Assert.True(viewModel.ShowSelectProjectState);
        Assert.Equal("Select project", viewModel.OverallStateText);
    }

    [Fact]
    public async Task SelectingAProject_RefreshesOnlyThatProject()
    {
        var alpha = CreateProject("Alpha");
        var beta = CreateProject("Beta");
        var readModel = new FakeReadModel();
        var viewModel = CreateViewModel(readModel, alpha, beta);

        await viewModel.InitializeAsync();
        await viewModel.SelectProjectAsync(beta.Id);

        Assert.Equal([beta.Id], readModel.RequestedProjectIds);
        Assert.Equal(beta.Id, viewModel.Snapshot!.ProjectId);
    }

    [Fact]
    public async Task RefreshDoesNotOverlap()
    {
        var project = CreateProject("Single");
        var readModel = new FakeReadModel { Delay = TimeSpan.FromMilliseconds(40) };
        var viewModel = CreateViewModel(readModel, project);
        await viewModel.InitializeAsync();

        var first = viewModel.RefreshAsync();
        var second = viewModel.RefreshAsync();
        await Task.WhenAll(first, second);

        Assert.Equal(1, readModel.MaxConcurrentReads);
    }

    [Fact]
    public async Task DegradedPersistence_RendersUnknownAndNeverAccepted()
    {
        var viewModel = new MissionControlViewModel();

        viewModel.SetPersistenceAvailability(false);
        await viewModel.InitializeAsync();

        Assert.True(viewModel.ShowStorageUnavailableState);
        Assert.Equal("Unknown", viewModel.OverallStateText);
        Assert.NotEqual("Accepted", viewModel.OverallStateText);
    }

    [Fact]
    public async Task SupersededSelection_DoesNotDisposeAnInFlightRefreshToken()
    {
        var alpha = CreateProject("Alpha");
        var beta = CreateProject("Beta");
        var readModel = new TokenProbingReadModel();
        var viewModel = new MissionControlViewModel(new FakeProjectRegistry([alpha, beta]), readModel);
        await viewModel.InitializeAsync();

        viewModel.SelectedProject = viewModel.ProjectOptions.Single(option => option.Id == alpha.Id);
        await readModel.FirstReadStarted.Task;

        // Switching projects while the first read is still in flight supersedes it.
        viewModel.SelectedProject = viewModel.ProjectOptions.Single(option => option.Id == beta.Id);
        readModel.ReleaseFirstRead();
        await readModel.FirstReadCompleted.Task;

        Assert.False(readModel.ObservedDisposedToken);
    }

    [Fact]
    public async Task RefreshProjectsAsyncMakesSameProcessRegisteredProjectVisibleWithoutRestart()
    {
        var alpha = CreateProject("Alpha");
        var registry = new MutableProjectRegistry(alpha);
        var viewModel = new MissionControlViewModel(registry, new FakeReadModel());
        await viewModel.InitializeAsync();

        Assert.Single(viewModel.ProjectOptions);

        var beta = await registry.CreateProjectAsync(new ProjectEdit { Name = "Zed", LocalPath = Environment.CurrentDirectory });
        await viewModel.RefreshProjectsAsync();

        Assert.Equal(2, viewModel.ProjectOptions.Count);
        Assert.Contains(viewModel.ProjectOptions, option => option.Id == alpha.Id);
        Assert.Contains(viewModel.ProjectOptions, option => option.Id == beta.Id);
    }

    [Fact]
    public async Task RefreshProjectsAsyncPreservesSelectedProjectInstanceById()
    {
        var alpha = CreateProject("Alpha");
        var registry = new MutableProjectRegistry(alpha);
        var viewModel = new MissionControlViewModel(registry, new FakeReadModel());
        await viewModel.InitializeAsync();
        await viewModel.SelectProjectAsync(alpha.Id);
        var selectedBeforeRefresh = viewModel.SelectedProject;

        await registry.CreateProjectAsync(new ProjectEdit { Name = "Zed", LocalPath = Environment.CurrentDirectory });
        await viewModel.RefreshProjectsAsync();

        Assert.Same(selectedBeforeRefresh, viewModel.SelectedProject);
        Assert.Equal(alpha.Id, viewModel.SelectedProject!.Id);
    }

    [Fact]
    public async Task RefreshProjectsAsyncDoesNotDuplicateProjectsAcrossRepeatedCalls()
    {
        var alpha = CreateProject("Alpha");
        var registry = new MutableProjectRegistry(alpha);
        var viewModel = new MissionControlViewModel(registry, new FakeReadModel());
        await viewModel.InitializeAsync();

        await registry.CreateProjectAsync(new ProjectEdit { Name = "Zed", LocalPath = Environment.CurrentDirectory });
        await viewModel.RefreshProjectsAsync();
        await viewModel.RefreshProjectsAsync();
        await viewModel.RefreshProjectsAsync();

        Assert.Equal(2, viewModel.ProjectOptions.Count);
    }

    [Fact]
    public async Task RefreshProjectsAsyncInsertsNewProjectInAlphabeticalOrder()
    {
        var alpha = CreateProject("Alpha");
        var zed = CreateProject("Zed");
        var registry = new MutableProjectRegistry(alpha, zed);
        var viewModel = new MissionControlViewModel(registry, new FakeReadModel());
        await viewModel.InitializeAsync();

        await registry.CreateProjectAsync(new ProjectEdit { Name = "Middle", LocalPath = Environment.CurrentDirectory });
        await viewModel.RefreshProjectsAsync();

        Assert.Equal(["Alpha", "Middle", "Zed"], viewModel.ProjectOptions.Select(option => option.Name));
    }

    [Fact]
    public async Task RefreshProjectsAsyncDoesNotDisturbSnapshotOfSelectedProject()
    {
        var alpha = CreateProject("Alpha");
        var registry = new MutableProjectRegistry(alpha);
        var readModel = new FakeReadModel();
        var viewModel = new MissionControlViewModel(registry, readModel);
        // Alpha is the only Active project, so InitializeAsync auto-selects and reads it once.
        await viewModel.InitializeAsync();
        var snapshotBeforeRefresh = viewModel.Snapshot;

        await registry.CreateProjectAsync(new ProjectEdit { Name = "Zed", LocalPath = Environment.CurrentDirectory });
        await viewModel.RefreshProjectsAsync();

        Assert.Same(snapshotBeforeRefresh, viewModel.Snapshot);
        Assert.Equal([alpha.Id], readModel.RequestedProjectIds);
    }

    [Fact]
    public async Task RefreshProjectsAsyncUpdatesExistingProjectMetadata()
    {
        var alpha = CreateProject("Alpha");
        var registry = new MutableProjectRegistry(alpha);
        var viewModel = new MissionControlViewModel(registry, new FakeReadModel());
        await viewModel.InitializeAsync();
        var option = viewModel.ProjectOptions[0];

        await registry.UpdateProjectAsync(alpha.Id, new ProjectEdit { Name = "Alpha Renamed", LocalPath = alpha.LocalPath, Status = ProjectStatus.Paused });
        await viewModel.RefreshProjectsAsync();

        Assert.Same(option, viewModel.ProjectOptions[0]);
        Assert.Equal("Alpha Renamed", option.Name);
        Assert.Equal(ProjectStatus.Paused.ToString(), option.StatusText);
    }

    [Fact]
    public async Task RefreshProjectsAsyncPreservesSelectedIdentityWhileApplyingNewMetadata()
    {
        var alpha = CreateProject("Alpha");
        var registry = new MutableProjectRegistry(alpha);
        var viewModel = new MissionControlViewModel(registry, new FakeReadModel());
        await viewModel.InitializeAsync();
        await viewModel.SelectProjectAsync(alpha.Id);
        var selectedBeforeRefresh = viewModel.SelectedProject;

        await registry.UpdateProjectAsync(alpha.Id, new ProjectEdit { Name = "Alpha Renamed", LocalPath = alpha.LocalPath, Status = ProjectStatus.Paused });
        await viewModel.RefreshProjectsAsync();

        Assert.Same(selectedBeforeRefresh, viewModel.SelectedProject);
        Assert.Equal("Alpha Renamed", viewModel.SelectedProject!.Name);
        Assert.Equal(ProjectStatus.Paused.ToString(), viewModel.SelectedProject.StatusText);
    }

    [Fact]
    public async Task RefreshProjectsAsyncRenameReordersCollectionWithoutClearing()
    {
        var alpha = CreateProject("Alpha");
        var zed = CreateProject("Zed");
        var registry = new MutableProjectRegistry(alpha, zed);
        var viewModel = new MissionControlViewModel(registry, new FakeReadModel());
        await viewModel.InitializeAsync();
        Assert.Equal(["Alpha", "Zed"], viewModel.ProjectOptions.Select(option => option.Name));

        await registry.UpdateProjectAsync(zed.Id, new ProjectEdit { Name = "Beta", LocalPath = zed.LocalPath, Status = ProjectStatus.Active });
        await viewModel.RefreshProjectsAsync();

        Assert.Equal(["Alpha", "Beta"], viewModel.ProjectOptions.Select(option => option.Name));
    }

    [Fact]
    public async Task RefreshProjectsAsyncPreservesSelectedIdentityAcrossRenameReorder()
    {
        var alpha = CreateProject("Alpha");
        var zed = CreateProject("Zed");
        var registry = new MutableProjectRegistry(alpha, zed);
        var viewModel = new MissionControlViewModel(registry, new FakeReadModel());
        await viewModel.InitializeAsync();
        await viewModel.SelectProjectAsync(zed.Id);
        var selectedBeforeRename = viewModel.SelectedProject;

        await registry.UpdateProjectAsync(zed.Id, new ProjectEdit { Name = "Beta", LocalPath = zed.LocalPath, Status = ProjectStatus.Active });
        await viewModel.RefreshProjectsAsync();

        Assert.Same(selectedBeforeRename, viewModel.SelectedProject);
        Assert.Equal("Beta", viewModel.SelectedProject!.Name);
        Assert.Equal(["Alpha", "Beta"], viewModel.ProjectOptions.Select(option => option.Name));
    }

    [Fact]
    public async Task RefreshProjectsAsyncMetadataUpdateOfSelectedProjectDoesNotRereadReadModel()
    {
        var alpha = CreateProject("Alpha");
        var registry = new MutableProjectRegistry(alpha);
        var readModel = new FakeReadModel();
        var viewModel = new MissionControlViewModel(registry, readModel);
        // Alpha is the only Active project, so InitializeAsync auto-selects and reads it once.
        await viewModel.InitializeAsync();
        var snapshotBeforeRefresh = viewModel.Snapshot;

        await registry.UpdateProjectAsync(alpha.Id, new ProjectEdit { Name = "Alpha Renamed", LocalPath = alpha.LocalPath, Status = ProjectStatus.Active });
        await viewModel.RefreshProjectsAsync();

        Assert.Same(snapshotBeforeRefresh, viewModel.Snapshot);
        Assert.Equal([alpha.Id], readModel.RequestedProjectIds);
        Assert.Equal("Alpha Renamed", viewModel.SelectedProject!.Name);
    }

    [Fact]
    public async Task RefreshProjectsAsyncIsNoOpWithoutPersistence()
    {
        var viewModel = new MissionControlViewModel();
        viewModel.SetPersistenceAvailability(false);

        await viewModel.RefreshProjectsAsync();

        Assert.Empty(viewModel.ProjectOptions);
    }

    private static MissionControlViewModel CreateViewModel(params Project[] projects) =>
        CreateViewModel(new FakeReadModel(), projects);

    private static MissionControlViewModel CreateViewModel(FakeReadModel readModel, params Project[] projects) =>
        new(new FakeProjectRegistry(projects), readModel);

    private static Project CreateProject(string name) =>
        new(Guid.NewGuid(), name, $"C:\\{name.ToLowerInvariant()}", null, ProjectStatus.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class FakeProjectRegistry(IReadOnlyList<Project> projects) : IProjectRegistryService
    {
        public Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken cancellationToken = default) => Task.FromResult(projects);
        public Task<Project> CreateProjectAsync(ProjectEdit edit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Project> UpdateProjectAsync(Guid projectId, ProjectEdit edit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeReadModel : IMissionControlReadModelService
    {
        public List<Guid> RequestedProjectIds { get; } = [];
        public TimeSpan Delay { get; init; }
        public int ActiveReads { get; private set; }
        public int MaxConcurrentReads { get; private set; }

        public async Task<MissionControlSnapshot> ReadAsync(Guid projectId, CancellationToken cancellationToken = default)
        {
            RequestedProjectIds.Add(projectId);
            ActiveReads++;
            MaxConcurrentReads = Math.Max(MaxConcurrentReads, ActiveReads);
            try
            {
                if (Delay > TimeSpan.Zero)
                    await Task.Delay(Delay, cancellationToken);
                return CreateSnapshot(projectId);
            }
            finally
            {
                ActiveReads--;
            }
        }
    }

    /// <summary>
    /// Proves a superseded refresh still owns a usable cancellation token: a newer selection may
    /// cancel it, but must not dispose it while the older read is still running.
    /// </summary>
    private sealed class TokenProbingReadModel : IMissionControlReadModelService
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public TaskCompletionSource FirstReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource FirstReadCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ObservedDisposedToken { get; private set; }

        public void ReleaseFirstRead() => _release.TrySetResult();

        public async Task<MissionControlSnapshot> ReadAsync(
            Guid projectId,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                FirstReadStarted.TrySetResult();
                await _release.Task.ConfigureAwait(false);
                try
                {
                    _ = cancellationToken.WaitHandle;
                }
                catch (ObjectDisposedException)
                {
                    ObservedDisposedToken = true;
                }

                FirstReadCompleted.TrySetResult();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return CreateSnapshot(projectId);
        }
    }

    private static MissionControlSnapshot CreateSnapshot(Guid projectId) => new(
            projectId,
            DateTimeOffset.UtcNow,
            MissionControlState.Unknown,
            "No current evidence",
            new(projectId, "Selected", ProjectStatus.Active, "C:\\selected", DateTimeOffset.UtcNow),
            new(false, "No evidence", "No current work selected", "No current step", MissionControlState.Unknown, null, null, "Unknown"),
            new([], "No roles assigned"),
            new(RepositorySelectionState.Skipped, RepositoryVerificationStatus.NotInspected, "Not inspected", "Not available", "Not available", "Not available", "Not available", "Not available", null),
            new(TrackerReferenceState.NotConfigured, "Not configured", "Not configured", "Not available", "Not configured"),
            new(null, "No validation evidence", "Not available", null),
            new(false, "No review evidence", "Not available", "Not available", 0, 0, false, "", "Unknown", null),
            new(false, 0, "No approval evidence", "Not available", "Unknown", null),
            new(false, null, "No runtime evidence", "No process evidence", null, null),
            limitations: [new("Overall", MissionControlLimitationKind.NoEvidence, "No evidence", "Test")]);
}
