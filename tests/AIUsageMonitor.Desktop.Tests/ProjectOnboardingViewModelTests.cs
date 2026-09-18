using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Desktop.ViewModels;

namespace AIUsageMonitor.Desktop.Tests;

public sealed class ProjectOnboardingViewModelTests
{
    [Fact]
    public async Task NewProjectOpensStepOne()
    {
        var service = new FakeOnboardingService();
        var viewModel = CreateProjectsViewModel(service);
        await viewModel.InitializeAsync();

        viewModel.NewProjectCommand.Execute(null);

        Assert.NotNull(viewModel.Onboarding);
        Assert.Equal(ProjectOnboardingStep.Project, viewModel.Onboarding!.CurrentStep);
        Assert.True(viewModel.IsOnboardingVisible);
        Assert.False(viewModel.IsEditorVisible);
    }

    [Fact]
    public void NextIsBlockedUntilRequiredProjectValuesExist()
    {
        var onboarding = CreateOnboarding(new FakeOnboardingService());

        Assert.False(onboarding.NextCommand.CanExecute(null));
        onboarding.Name = "Project";
        Assert.False(onboarding.NextCommand.CanExecute(null));
        onboarding.LocalPath = "C:\\project";
        Assert.True(onboarding.NextCommand.CanExecute(null));
    }

    [Fact]
    public void RepositorySkipAdvancesWithoutFakeConnection()
    {
        var onboarding = CreateOnboarding(new FakeOnboardingService());
        SetProjectValues(onboarding);
        onboarding.NextCommand.Execute(null);

        onboarding.SkipRepositoryCommand.Execute(null);
        onboarding.NextCommand.Execute(null);

        Assert.Equal(ProjectOnboardingStep.Tracker, onboarding.CurrentStep);
        Assert.Equal(RepositoryOnboardingChoice.Skip, onboarding.RepositoryChoice);
    }

    [Fact]
    public void TrackerSkipAdvancesAndDefaultsRemainVisible()
    {
        var onboarding = CreateOnboarding(new FakeOnboardingService());
        SetProjectValues(onboarding);
        onboarding.NextCommand.Execute(null);
        onboarding.SkipRepositoryCommand.Execute(null);
        onboarding.NextCommand.Execute(null);

        Assert.True(onboarding.IsTrackerSkipped);
        onboarding.NextCommand.Execute(null);

        Assert.Equal(ProjectOnboardingStep.Agents, onboarding.CurrentStep);
        Assert.Equal(6, onboarding.AgentOptions.Count);
        Assert.Contains("Planner", onboarding.AgentOptions[0].RolesText + onboarding.AgentOptions[1].RolesText);
    }

    [Fact]
    public void GitHubTrackerIsFirstClassAndRequiresItsBoundedReference()
    {
        var onboarding = CreateOnboarding(new FakeOnboardingService());
        SetProjectValues(onboarding);
        onboarding.NextCommand.Execute(null);
        onboarding.SkipRepositoryCommand.Execute(null);
        onboarding.NextCommand.Execute(null);

        Assert.Contains("GitHub", onboarding.TrackerOptions);
        onboarding.SelectedTrackerOption = "GitHub";

        Assert.False(onboarding.NextCommand.CanExecute(null));
        Assert.Contains("GITHUB", onboarding.TrackerReferenceLabel, StringComparison.Ordinal);
        Assert.Contains("owner/repository", onboarding.TrackerReferenceHelpText, StringComparison.Ordinal);
        Assert.Contains("not tested", onboarding.TrackerStateText, StringComparison.OrdinalIgnoreCase);

        onboarding.TrackerReference = "Hossam1104/AI_Orchestrator";

        Assert.True(onboarding.NextCommand.CanExecute(null));
    }

    [Fact]
    public void BackPreservesInMemoryOnboardingValues()
    {
        var onboarding = CreateOnboarding(new FakeOnboardingService());
        SetProjectValues(onboarding);
        onboarding.NextCommand.Execute(null);
        onboarding.SkipRepositoryCommand.Execute(null);
        onboarding.NextCommand.Execute(null);
        onboarding.NextCommand.Execute(null);
        onboarding.BackCommand.Execute(null);
        onboarding.BackCommand.Execute(null);
        onboarding.BackCommand.Execute(null);

        Assert.Equal(ProjectOnboardingStep.Project, onboarding.CurrentStep);
        Assert.Equal("Project", onboarding.Name);
        Assert.Equal("C:\\project", onboarding.LocalPath);
        Assert.Equal(RepositoryOnboardingChoice.Skip, onboarding.RepositoryChoice);
    }

    [Fact]
    public async Task FinishInvokesOnboardingServiceOnce()
    {
        var service = new FakeOnboardingService();
        var onboarding = CreateOnboarding(service);
        SetProjectValues(onboarding);
        onboarding.NextCommand.Execute(null);
        onboarding.SkipRepositoryCommand.Execute(null);
        onboarding.NextCommand.Execute(null);
        onboarding.NextCommand.Execute(null);

        await onboarding.FinishAsync();

        Assert.Equal(1, service.CompleteCalls);
        Assert.False(onboarding.HasError);
    }

    [Fact]
    public async Task SuccessfulFinishSelectsCreatedProjectAndFailedFinishShowsNoSuccess()
    {
        var successService = new FakeOnboardingService();
        var viewModel = CreateProjectsViewModel(successService);
        await viewModel.InitializeAsync();
        viewModel.NewProjectCommand.Execute(null);
        SetProjectValues(viewModel.Onboarding!);
        viewModel.Onboarding!.NextCommand.Execute(null);
        viewModel.Onboarding.SkipRepositoryCommand.Execute(null);
        viewModel.Onboarding.NextCommand.Execute(null);
        viewModel.Onboarding.NextCommand.Execute(null);
        await viewModel.Onboarding.FinishAsync();

        Assert.False(viewModel.IsOnboardingVisible);
        Assert.Equal(successService.CreatedProject.Id, viewModel.SelectedProject!.Id);

        var failureService = new FakeOnboardingService { Result = ProjectOnboardingResult.Failure(null, "safe failure") };
        var failedOnboarding = CreateOnboarding(failureService);
        SetProjectValues(failedOnboarding);
        failedOnboarding.NextCommand.Execute(null);
        failedOnboarding.SkipRepositoryCommand.Execute(null);
        failedOnboarding.NextCommand.Execute(null);
        failedOnboarding.NextCommand.Execute(null);
        await failedOnboarding.FinishAsync();

        Assert.True(failedOnboarding.HasError);
        Assert.Equal("safe failure", failedOnboarding.ErrorMessage);
    }

    [Fact]
    public async Task PartialFinishSelectsExistingProjectAndCannotRetryCreation()
    {
        var service = new FakeOnboardingService
        {
            Result = ProjectOnboardingResult.PartialProjectCreated(
                new Project(
                    Guid.NewGuid(),
                    "Partial project",
                    "C:\\partial",
                    null,
                    ProjectStatus.Active,
                    new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero)),
                "The project was created, but onboarding could not be completed.")
        };
        var projects = CreateProjectsViewModel(service);
        await projects.InitializeAsync();
        projects.NewProjectCommand.Execute(null);
        var onboarding = projects.Onboarding!;
        SetProjectValues(onboarding);
        onboarding.NextCommand.Execute(null);
        onboarding.SkipRepositoryCommand.Execute(null);
        onboarding.NextCommand.Execute(null);
        onboarding.NextCommand.Execute(null);

        await onboarding.FinishAsync();
        await onboarding.FinishAsync();

        Assert.Equal(1, service.CompleteCalls);
        Assert.True(onboarding.IsCompletionTerminal);
        Assert.False(onboarding.FinishCommand.CanExecute(null));
        Assert.False(projects.IsOnboardingVisible);
        Assert.Equal(service.Result!.Project!.Id, projects.SelectedProject!.Id);
        Assert.Contains("onboarding could not be completed", projects.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelReturnsToProjectsWithoutCreatingProject()
    {
        var service = new FakeOnboardingService();
        var viewModel = CreateProjectsViewModel(service);
        await viewModel.InitializeAsync();
        viewModel.NewProjectCommand.Execute(null);

        viewModel.Onboarding!.CancelCommand.Execute(null);

        Assert.False(viewModel.IsOnboardingVisible);
        Assert.Empty(viewModel.Projects);
        Assert.Equal(0, service.CompleteCalls);
    }

    private static ProjectsViewModel CreateProjectsViewModel(FakeOnboardingService service) =>
        new(
            new ProjectRegistryService(new MemoryProjectRepository(), new FixedClock()),
            null,
            service,
            new DefaultAgentCatalog());

    private static ProjectOnboardingViewModel CreateOnboarding(FakeOnboardingService service) =>
        new(service, new DefaultAgentCatalog());

    private static void SetProjectValues(ProjectOnboardingViewModel onboarding)
    {
        onboarding.Name = "Project";
        onboarding.LocalPath = "C:\\project";
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
            "Created project",
            "C:\\created",
            null,
            ProjectStatus.Active,
            new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero));

        public ProjectOnboardingResult? Result { get; set; }

        public Task<LocalRepositoryInspection> InspectRepositoryAsync(string localPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalRepositoryInspection(RepositoryVerificationStatus.NotGitRepository, localPath));

        public Task<ProjectOnboardingResult> CompleteAsync(ProjectOnboardingRequest request, CancellationToken cancellationToken = default)
        {
            CompleteCalls++;
            return Task.FromResult(Result ?? ProjectOnboardingResult.Success(
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
