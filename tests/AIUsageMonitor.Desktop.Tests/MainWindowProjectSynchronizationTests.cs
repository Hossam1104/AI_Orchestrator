using AIUsageMonitor.Application.MissionControl;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Desktop.ViewModels;

namespace AIUsageMonitor.Desktop.Tests;

/// <summary>
/// Reproduces the APO-70 live project-registry synchronization defect at the composition root:
/// a project registered through the registry mid-process (as onboarding does) must become visible
/// to Execution and Mission Control the moment the owner navigates back to that workspace, in the
/// same running process, without restarting the app. Before the RefreshProjectsAsync fix, both
/// workspaces only ever read the registry once, inside MainWindowViewModel.InitializeAsync().
/// </summary>
public sealed class MainWindowProjectSynchronizationTests
{
    [Fact]
    public async Task NavigatingToExecutionAfterSameProcessRegistrationRevealsTheNewProject()
    {
        var alpha = CreateProject("Alpha");
        var registry = new MutableProjectRegistry(alpha);
        var viewModel = new MainWindowViewModel(
            new MissionControlViewModel(registry, new FakeReadModel()),
            new AiCapacityViewModel(),
            new ProjectsViewModel(),
            new ExecutionViewModel(registry, new FakeExecutionCoordinator()));

        await viewModel.InitializeAsync();
        Assert.Single(viewModel.Execution.ProjectOptions);

        var beta = await registry.CreateProjectAsync(new ProjectEdit { Name = "Beta", LocalPath = Environment.CurrentDirectory });
        viewModel.ShowExecutionCommand.Execute(null);
        await WaitUntil(() => viewModel.Execution.ProjectOptions.Count == 2);

        Assert.Contains(viewModel.Execution.ProjectOptions, option => option.Id == alpha.Id);
        Assert.Contains(viewModel.Execution.ProjectOptions, option => option.Id == beta.Id);
    }

    [Fact]
    public async Task NavigatingToMissionControlAfterSameProcessRegistrationRevealsTheNewProject()
    {
        var alpha = CreateProject("Alpha");
        var registry = new MutableProjectRegistry(alpha);
        var viewModel = new MainWindowViewModel(
            new MissionControlViewModel(registry, new FakeReadModel()),
            new AiCapacityViewModel(),
            new ProjectsViewModel(),
            new ExecutionViewModel(registry, new FakeExecutionCoordinator()));

        await viewModel.InitializeAsync();
        Assert.Single(viewModel.MissionControl.ProjectOptions);

        var beta = await registry.CreateProjectAsync(new ProjectEdit { Name = "Beta", LocalPath = Environment.CurrentDirectory });
        viewModel.ShowMissionControlCommand.Execute(null);
        await WaitUntil(() => viewModel.MissionControl.ProjectOptions.Count == 2);

        Assert.Contains(viewModel.MissionControl.ProjectOptions, option => option.Id == alpha.Id);
        Assert.Contains(viewModel.MissionControl.ProjectOptions, option => option.Id == beta.Id);
    }

    [Fact]
    public async Task RepeatedNavigationDoesNotDuplicateProjectsInEitherWorkspace()
    {
        var alpha = CreateProject("Alpha");
        var registry = new MutableProjectRegistry(alpha);
        var viewModel = new MainWindowViewModel(
            new MissionControlViewModel(registry, new FakeReadModel()),
            new AiCapacityViewModel(),
            new ProjectsViewModel(),
            new ExecutionViewModel(registry, new FakeExecutionCoordinator()));

        await viewModel.InitializeAsync();
        await registry.CreateProjectAsync(new ProjectEdit { Name = "Beta", LocalPath = Environment.CurrentDirectory });

        viewModel.ShowExecutionCommand.Execute(null);
        await WaitUntil(() => viewModel.Execution.ProjectOptions.Count == 2);
        viewModel.ShowMissionControlCommand.Execute(null);
        await WaitUntil(() => viewModel.MissionControl.ProjectOptions.Count == 2);
        viewModel.ShowExecutionCommand.Execute(null);
        await Task.Delay(20);
        viewModel.ShowMissionControlCommand.Execute(null);
        await Task.Delay(20);

        Assert.Equal(2, viewModel.Execution.ProjectOptions.Count);
        Assert.Equal(2, viewModel.MissionControl.ProjectOptions.Count);
    }

    private static async Task WaitUntil(Func<bool> predicate)
    {
        for (var i = 0; i < 100 && !predicate(); i++) await Task.Delay(10);
        Assert.True(predicate());
    }

    private static Project CreateProject(string name) =>
        new(Guid.NewGuid(), name, Environment.CurrentDirectory, "main", ProjectStatus.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class FakeReadModel : IMissionControlReadModelService
    {
        public List<Guid> RequestedProjectIds { get; } = [];

        public Task<MissionControlSnapshot> ReadAsync(Guid projectId, CancellationToken cancellationToken = default)
        {
            RequestedProjectIds.Add(projectId);
            return Task.FromResult(CreateSnapshot(projectId));
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

    private sealed class FakeExecutionCoordinator : IExecutionCoordinator
    {
        public Task<ExecutionPreparationResult> PrepareAsync(OrchestrationWorkRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExecutionPreparationResult(ExecutionPreparationStatus.Failed));

        public Task<ExecutionStartResult> StartAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExecutionStartResult(ExecutionCoordinatorState.Completed));

        public Task<ExecutionRehydrationResult> RestoreAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExecutionRehydrationResult(ExecutionRehydrationStatus.NotResumable));

        public Task<ExecutionCancellationResult> CancelAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExecutionCancellationResult(true));

        public Task<ExecutionRunSnapshot> GetCurrentRunAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExecutionRunSnapshot(null, ExecutionCoordinatorState.Draft, null, null));
    }
}
