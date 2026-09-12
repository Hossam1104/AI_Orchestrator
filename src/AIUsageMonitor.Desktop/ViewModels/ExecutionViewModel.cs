using System.Collections.ObjectModel;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Projects;

namespace AIUsageMonitor.Desktop.ViewModels;

/// <summary>
/// Owner-facing bounded execution surface. It knows only the coordinator and project registry;
/// all durable authority and runtime truth comes from Application services.
/// </summary>
public sealed class ExecutionViewModel : ObservableObject
{
    private readonly IProjectRegistryService? _projects;
    private readonly IExecutionCoordinator? _coordinator;
    private MissionControlProjectOption? _selectedProject;
    private ExecutionCoordinatorState _state = ExecutionCoordinatorState.Draft;
    private PreparedExecution? _prepared;
    private BoundedExecutionResult? _lastResult;
    private string? _errorMessage;
    private bool _persistenceAvailable;
    private string _title = string.Empty;
    private string _objective = string.Empty;
    private string _workItemReference = string.Empty;
    private string _acceptanceCriteria = string.Empty;
    private string _constraints = string.Empty;
    private string _validationExpectations = string.Empty;

    public ExecutionViewModel()
        : this(null, null)
    {
    }

    public ExecutionViewModel(
        IProjectRegistryService? projects,
        IExecutionCoordinator? coordinator)
    {
        _projects = projects;
        _coordinator = coordinator;
        _persistenceAvailable = projects is not null && coordinator is not null;
        PrepareCommand = new AsyncCommand(PrepareAsync, CanPrepare);
        StartCommand = new AsyncCommand(StartAsync, CanStart);
        CancelCommand = new AsyncCommand(CancelAsync, CanCancel);
    }

    public ObservableCollection<MissionControlProjectOption> ProjectOptions { get; } = [];

    public AsyncCommand PrepareCommand { get; }

    public AsyncCommand StartCommand { get; }

    public AsyncCommand CancelCommand { get; }

    public MissionControlProjectOption? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (!SetProperty(ref _selectedProject, value))
            {
                return;
            }

            ResetPreparation();
            OnPropertyChanged(nameof(SelectedProjectText));
            NotifyCommands();
        }
    }

    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value)) NotifyCommands();
        }
    }

    public string Objective
    {
        get => _objective;
        set
        {
            if (SetProperty(ref _objective, value)) NotifyCommands();
        }
    }

    public string WorkItemReference
    {
        get => _workItemReference;
        set => SetProperty(ref _workItemReference, value);
    }

    public string AcceptanceCriteria
    {
        get => _acceptanceCriteria;
        set
        {
            if (SetProperty(ref _acceptanceCriteria, value)) NotifyCommands();
        }
    }

    public string Constraints
    {
        get => _constraints;
        set => SetProperty(ref _constraints, value);
    }

    public string ValidationExpectations
    {
        get => _validationExpectations;
        set => SetProperty(ref _validationExpectations, value);
    }

    public ExecutionCoordinatorState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(IsReady));
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(ValidationStateText));
                NotifyCommands();
            }
        }
    }

    public bool IsPersistenceAvailable => _persistenceAvailable;

    public bool IsBusy => State is ExecutionCoordinatorState.Preparing or ExecutionCoordinatorState.Running or ExecutionCoordinatorState.Cancelling;

    public bool IsReady => State == ExecutionCoordinatorState.Ready && _prepared is not null;

    public bool ShowEmptyState => ProjectOptions.Count == 0;

    public string StateText => State switch
    {
        ExecutionCoordinatorState.Draft => "DRAFT",
        ExecutionCoordinatorState.Preparing => "PREPARING AUTHORITIES",
        ExecutionCoordinatorState.Ready => "READY",
        ExecutionCoordinatorState.Running => "RUNNING",
        ExecutionCoordinatorState.Waiting => "WAITING / RECOVERY",
        ExecutionCoordinatorState.Cancelling => "CANCELLING",
        ExecutionCoordinatorState.Cancelled => "CANCELLED",
        ExecutionCoordinatorState.Failed => "FAILED",
        ExecutionCoordinatorState.Completed => "COMPLETED",
        ExecutionCoordinatorState.ValidationPending => "VALIDATION PENDING",
        ExecutionCoordinatorState.HumanActionRequired => "HUMAN ACTION REQUIRED",
        _ => State.ToString().ToUpperInvariant()
    };

    public string SelectedProjectText => SelectedProject?.Name ?? "Select a registered project";

    public string PlannerText => _prepared?.Planner.Name ?? "Not resolved";

    public string ExecutorText => _prepared?.Executor.Name ?? "Not resolved";

    public string RouteReasonText => _prepared?.RoutingDecision.ReasonCodes.Count > 0
        ? string.Join(", ", _prepared.RoutingDecision.ReasonCodes)
        : "No routing result yet";

    public string RunIdText => _prepared is null ? "Not created" : _prepared.RunId.ToString("D");

    public string WorkspaceText => _prepared?.WorkspaceReceipt.WorkspacePath ?? "Not prepared";

    public string CheckpointText => _prepared is null
        ? "Not created"
        : $"{_prepared.Checkpoint.LifecycleState} · {_prepared.Checkpoint.Reference.ContentHash[..12]}";

    public string TerminalResultText => _lastResult is null
        ? "No terminal result"
        : _lastResult.Status.ToString();

    public string AdapterSummaryText => _lastResult?.AdapterResult?.Summary ?? "No adapter evidence available.";

    public string ValidationStateText => State == ExecutionCoordinatorState.Completed
        ? "Owner validation and acceptance are still pending."
        : "No validation result has been recorded.";

    public string ErrorMessage => _errorMessage ?? (IsPersistenceAvailable ? string.Empty : "Execution is unavailable in degraded no-persistence mode.");

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_projects is null || _coordinator is null)
        {
            return;
        }

        _persistenceAvailable = true;
        ProjectOptions.Clear();
        foreach (var project in await _projects.GetProjectsAsync(cancellationToken).ConfigureAwait(true))
        {
            ProjectOptions.Add(new MissionControlProjectOption(project));
        }

        OnPropertyChanged(nameof(ShowEmptyState));
        if (ProjectOptions.Count == 1)
        {
            SelectedProject = ProjectOptions[0];
        }
    }

    public Task InitializeDegradedAsync(CancellationToken cancellationToken = default)
    {
        _errorMessage = "Execution requires local persisted orchestration authority.";
        OnPropertyChanged(nameof(ErrorMessage));
        return Task.CompletedTask;
    }

    public void SetPersistenceAvailability(bool available)
    {
        _persistenceAvailable = available;
        OnPropertyChanged(nameof(IsPersistenceAvailable));
        OnPropertyChanged(nameof(ErrorMessage));
        NotifyCommands();
    }

    private bool CanPrepare() =>
        IsPersistenceAvailable &&
        _coordinator is not null &&
        !IsBusy &&
        SelectedProject is not null &&
        !string.IsNullOrWhiteSpace(Title) &&
        !string.IsNullOrWhiteSpace(Objective) &&
        ParseLines(AcceptanceCriteria).Count > 0;

    private bool CanStart() => IsPersistenceAvailable && _coordinator is not null && IsReady;

    private bool CanCancel() => IsPersistenceAvailable && _coordinator is not null && State == ExecutionCoordinatorState.Running;

    private async Task PrepareAsync()
    {
        if (!CanPrepare() || _coordinator is null || SelectedProject is null)
        {
            return;
        }

        _errorMessage = null;
        OnPropertyChanged(nameof(ErrorMessage));
        State = ExecutionCoordinatorState.Preparing;
        try
        {
            var result = await _coordinator.PrepareAsync(new OrchestrationWorkRequest(
                SelectedProject.Id,
                Environment.UserName,
                Title,
                Objective,
                string.IsNullOrWhiteSpace(WorkItemReference) ? null : WorkItemReference,
                ParseLines(AcceptanceCriteria),
                ParseLines(Constraints),
                ParseLines(ValidationExpectations))).ConfigureAwait(true);
            _prepared = result.PreparedExecution;
            State = result.Succeeded ? ExecutionCoordinatorState.Ready : result.Status == ExecutionPreparationStatus.Cancelled ? ExecutionCoordinatorState.Cancelled : ExecutionCoordinatorState.Failed;
            _errorMessage = result.ErrorMessage;
        }
        catch (Exception)
        {
            _prepared = null;
            State = ExecutionCoordinatorState.Failed;
            _errorMessage = "The request could not be prepared safely.";
        }

        PublishPreparedState();
    }

    private async Task StartAsync()
    {
        if (!CanStart() || _coordinator is null)
        {
            return;
        }

        _errorMessage = null;
        OnPropertyChanged(nameof(ErrorMessage));
        State = ExecutionCoordinatorState.Running;
        try
        {
            var result = await _coordinator.StartAsync().ConfigureAwait(true);
            State = result.State;
            _lastResult = result.Result;
            _errorMessage = result.ErrorMessage;
        }
        catch (Exception)
        {
            State = ExecutionCoordinatorState.Failed;
            _errorMessage = "The bounded execution service failed safely.";
        }

        PublishPreparedState();
    }

    private async Task CancelAsync()
    {
        if (!CanCancel() || _coordinator is null)
        {
            return;
        }

        var result = await _coordinator.CancelAsync().ConfigureAwait(true);
        if (result.Requested)
        {
            State = ExecutionCoordinatorState.Cancelling;
        }
        else
        {
            _errorMessage = result.ErrorMessage;
            OnPropertyChanged(nameof(ErrorMessage));
        }

        NotifyCommands();
    }

    private void ResetPreparation()
    {
        if (IsBusy)
        {
            return;
        }

        _prepared = null;
        _lastResult = null;
        _errorMessage = null;
        State = ExecutionCoordinatorState.Draft;
        PublishPreparedState();
    }

    private void PublishPreparedState()
    {
        OnPropertyChanged(nameof(PlannerText));
        OnPropertyChanged(nameof(ExecutorText));
        OnPropertyChanged(nameof(RouteReasonText));
        OnPropertyChanged(nameof(RunIdText));
        OnPropertyChanged(nameof(WorkspaceText));
        OnPropertyChanged(nameof(CheckpointText));
        OnPropertyChanged(nameof(TerminalResultText));
        OnPropertyChanged(nameof(AdapterSummaryText));
        OnPropertyChanged(nameof(ValidationStateText));
        OnPropertyChanged(nameof(ErrorMessage));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        PrepareCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private static IReadOnlyList<string> ParseLines(string value) =>
        value.Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
