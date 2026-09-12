using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Desktop.ViewModels;

namespace AIUsageMonitor.Desktop.Tests;

public sealed class ExecutionViewModelTests
{
    [Fact]
    public void PrepareRemainsUnavailableUntilOwnerProvidesRequiredInput()
    {
        var viewModel = NewViewModel();
        viewModel.SetPersistenceAvailability(true);
        viewModel.ProjectOptions.Add(new MissionControlProjectOption(Project()));
        viewModel.SelectedProject = viewModel.ProjectOptions[0];

        Assert.False(viewModel.PrepareCommand.CanExecute(null));

        viewModel.Title = "Bounded change";
        viewModel.Objective = "Make the requested bounded change.";
        viewModel.AcceptanceCriteria = "Verify the result.";

        Assert.True(viewModel.PrepareCommand.CanExecute(null));
    }

    [Fact]
    public void StartIsGatedUntilCoordinatorReturnsReadyPreparation()
    {
        var viewModel = NewViewModel();
        viewModel.SetPersistenceAvailability(true);

        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.CancelCommand.CanExecute(null));
    }

    private static ExecutionViewModel NewViewModel() =>
        new(null, new StubExecutionCoordinator());

    private static Project Project() => new(
        Guid.NewGuid(),
        "Test project",
        Environment.CurrentDirectory,
        "main",
        ProjectStatus.Active,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private sealed class StubExecutionCoordinator : IExecutionCoordinator
    {
        public Task<ExecutionPreparationResult> PrepareAsync(OrchestrationWorkRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExecutionPreparationResult(ExecutionPreparationStatus.Failed));

        public Task<ExecutionStartResult> StartAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExecutionStartResult(ExecutionCoordinatorState.Draft));

        public Task<ExecutionCancellationResult> CancelAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExecutionCancellationResult(false));

        public Task<ExecutionRunSnapshot> GetCurrentRunAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExecutionRunSnapshot(null, ExecutionCoordinatorState.Draft, null, null));
    }
}
