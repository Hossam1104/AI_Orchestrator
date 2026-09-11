using System.IO;
using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Desktop.ViewModels;

namespace AIUsageMonitor.Desktop.Tests;

/// <summary>
/// The workspace supplies the preferred folder root to the host picker without registering or
/// selecting anything on the operator's behalf.
/// </summary>
public sealed class ProjectFolderPickerTests
{
    [Fact]
    public async Task PickerOpensAtThePreferredRoot()
    {
        var preferences = new FakeFolderPreferences { PreferredRoot = "D:\\AI Tools\\Active Projects" };
        var viewModel = CreateProjectsViewModel(new FakeOnboardingService(), preferences);
        var picker = new RecordingPicker("D:\\AI Tools\\Active Projects\\Chosen");
        viewModel.SetPathPicker(picker.Pick);
        await viewModel.InitializeAsync();

        viewModel.NewProjectCommand.Execute(null);
        viewModel.Onboarding!.BrowsePathCommand.Execute(null);

        Assert.Equal(["D:\\AI Tools\\Active Projects"], picker.ReceivedRoots);
        Assert.Equal("D:\\AI Tools\\Active Projects\\Chosen", viewModel.Onboarding.LocalPath);
    }

    [Fact]
    public async Task AbsentPreferredRootStillOpensTheNormalPicker()
    {
        var viewModel = CreateProjectsViewModel(new FakeOnboardingService(), new FakeFolderPreferences());
        var picker = new RecordingPicker("C:\\Elsewhere\\Chosen");
        viewModel.SetPathPicker(picker.Pick);
        await viewModel.InitializeAsync();

        viewModel.NewProjectCommand.Execute(null);
        viewModel.Onboarding!.BrowsePathCommand.Execute(null);

        Assert.Null(viewModel.PreferredPickerRoot);
        Assert.Equal([null], picker.ReceivedRoots);
        Assert.Equal("C:\\Elsewhere\\Chosen", viewModel.Onboarding.LocalPath);
    }

    [Fact]
    public async Task PreferredRootIsNeverAutoRegisteredOrAutoSelected()
    {
        var preferences = new FakeFolderPreferences { PreferredRoot = "D:\\AI Tools\\Active Projects" };
        var service = new FakeOnboardingService();
        var viewModel = CreateProjectsViewModel(service, preferences);
        viewModel.SetPathPicker(new RecordingPicker(null).Pick);
        await viewModel.InitializeAsync();

        viewModel.NewProjectCommand.Execute(null);
        viewModel.Onboarding!.BrowsePathCommand.Execute(null);

        Assert.Equal("D:\\AI Tools\\Active Projects", viewModel.PreferredPickerRoot);
        Assert.Empty(viewModel.Onboarding.LocalPath);
        Assert.Empty(viewModel.Projects);
        Assert.Null(viewModel.SelectedProjectCard);
        Assert.Equal(0, service.CompleteCalls);
    }

    [Fact]
    public async Task PickerRootFollowsTheLastSuccessfulRegistration()
    {
        var preferences = new FakeFolderPreferences { PreferredRoot = "D:\\AI Tools\\Active Projects" };
        var viewModel = CreateProjectsViewModel(new FakeOnboardingService(), preferences);
        var picker = new RecordingPicker("C:\\Code\\Payments");
        viewModel.SetPathPicker(picker.Pick);
        await viewModel.InitializeAsync();

        viewModel.NewProjectCommand.Execute(null);
        var onboarding = viewModel.Onboarding!;
        onboarding.Name = "Payments";
        onboarding.LocalPath = "C:\\Code\\Payments";
        onboarding.NextCommand.Execute(null);
        onboarding.SkipRepositoryCommand.Execute(null);
        onboarding.NextCommand.Execute(null);
        onboarding.NextCommand.Execute(null);

        // The application service owns the recorded folder; the workspace only re-reads it.
        preferences.PreferredRoot = "C:\\Code";
        await onboarding.FinishAsync();

        Assert.Equal("C:\\Code", viewModel.PreferredPickerRoot);
        viewModel.NewProjectCommand.Execute(null);
        viewModel.Onboarding!.BrowsePathCommand.Execute(null);
        Assert.Equal("C:\\Code", picker.ReceivedRoots[^1]);
    }

    [Fact]
    public async Task WorkspaceWithoutAPreferenceServiceKeepsTheWindowsDefault()
    {
        var viewModel = CreateProjectsViewModel(new FakeOnboardingService(), folderPreferences: null);
        var picker = new RecordingPicker("C:\\Code\\Payments");
        viewModel.SetPathPicker(picker.Pick);
        await viewModel.InitializeAsync();

        viewModel.NewProjectCommand.Execute(null);
        viewModel.Onboarding!.BrowsePathCommand.Execute(null);

        Assert.Null(viewModel.PreferredPickerRoot);
        Assert.Equal([null], picker.ReceivedRoots);
    }

    [Fact]
    public async Task FailingPreferenceLookupLeavesThePickerUsable()
    {
        var preferences = new FakeFolderPreferences
        {
            Failure = new IOException("simulated preference read failure")
        };
        var viewModel = CreateProjectsViewModel(new FakeOnboardingService(), preferences);
        var picker = new RecordingPicker("C:\\Code\\Payments");
        viewModel.SetPathPicker(picker.Pick);
        await viewModel.InitializeAsync();

        viewModel.NewProjectCommand.Execute(null);
        viewModel.Onboarding!.BrowsePathCommand.Execute(null);

        Assert.Null(viewModel.PreferredPickerRoot);
        Assert.Equal("C:\\Code\\Payments", viewModel.Onboarding.LocalPath);
    }

    private static ProjectsViewModel CreateProjectsViewModel(
        FakeOnboardingService service,
        IProjectFolderPreferenceService? folderPreferences) =>
        new(
            new ProjectRegistryService(new MemoryProjectRepository(), new FixedClock()),
            null,
            service,
            new DefaultAgentCatalog(),
            folderPreferences);

    private sealed class RecordingPicker(string? selection)
    {
        public List<string?> ReceivedRoots { get; } = [];

        public string? Pick(string? preferredRoot)
        {
            ReceivedRoots.Add(preferredRoot);
            return selection;
        }
    }

    private sealed class FakeFolderPreferences : IProjectFolderPreferenceService
    {
        public string? PreferredRoot { get; set; }

        public Exception? Failure { get; init; }

        public Task<string?> GetPreferredPickerRootAsync(CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(PreferredRoot);
        }

        public Task RecordSuccessfulProjectFolderAsync(
            string projectLocalPath,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class MemoryProjectRepository : IProjectRepository
    {
        private readonly List<Project> _projects = [];

        public Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Project>>(_projects.ToArray());

        public Task<Project?> GetByIdAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_projects.SingleOrDefault(project => project.Id == projectId));

        public Task UpsertAsync(Project project, CancellationToken cancellationToken = default)
        {
            _projects.Add(project);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOnboardingService : IProjectOnboardingService
    {
        public int CompleteCalls { get; private set; }

        public Project CreatedProject { get; } = new(
            Guid.NewGuid(),
            "Payments",
            "C:\\Code\\Payments",
            null,
            ProjectStatus.Active,
            new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero));

        public Task<LocalRepositoryInspection> InspectRepositoryAsync(
            string localPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalRepositoryInspection(
                RepositoryVerificationStatus.NotGitRepository,
                localPath));

        public Task<ProjectOnboardingResult> CompleteAsync(
            ProjectOnboardingRequest request,
            CancellationToken cancellationToken = default)
        {
            CompleteCalls++;
            return Task.FromResult(ProjectOnboardingResult.Success(
                CreatedProject,
                new ProjectContextReference(
                    CreatedProject.Id,
                    Guid.NewGuid(),
                    ProjectContextContract.CurrentVersion,
                    CreatedProject.CreatedAt,
                    CreatedProject.UpdatedAt,
                    ProjectRepositoryContextReference.Skipped(CreatedProject.Id, CreatedProject.LocalPath),
                    new ProjectTrackerContextReference(TrackerReferenceState.Skipped),
                    [],
                    new ProjectCurrentWorkReference(CurrentWorkState.NotSelected),
                    [],
                    null,
                    null,
                    ProjectNextSafeAction.ReadyForPlanning)));
        }
    }
}
