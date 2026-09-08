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
                return Snapshot(projectId);
            }
            finally
            {
                ActiveReads--;
            }
        }

        private static MissionControlSnapshot Snapshot(Guid projectId) => new(
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
}
