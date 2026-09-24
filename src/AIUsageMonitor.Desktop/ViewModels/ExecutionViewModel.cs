using System.Collections.ObjectModel;
using AIUsageMonitor.Application.MissionControl;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Projects;

namespace AIUsageMonitor.Desktop.ViewModels;

/// <summary>
/// Owner-facing bounded execution surface. It composes application read models into the existing
/// coordinator request without becoming a second execution authority.
/// </summary>
public sealed class ExecutionViewModel : ObservableObject
{
    private readonly IProjectRegistryService? _projects;
    private readonly IExecutionCoordinator? _coordinator;
    private readonly IMissionControlReadModelService? _missionControl;
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
    private string _contextSourceText = "No authoritative current work context is available.";
    private string _currentWorkText = "No current work selected.";
    private string? _titleSourceText;
    private readonly SemaphoreSlim _restoreGate = new(1, 1);
    private long _restoreGeneration;
    private bool _isRestoring;
    private bool _isPreparingExecution;

    public ExecutionViewModel()
        : this(null, null, null)
    {
    }

    public ExecutionViewModel(
        IProjectRegistryService? projects,
        IExecutionCoordinator? coordinator,
        IMissionControlReadModelService? missionControl = null)
    {
        _projects = projects;
        _coordinator = coordinator;
        _missionControl = missionControl;
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
            if ((_isPreparingExecution || State is ExecutionCoordinatorState.Running or ExecutionCoordinatorState.Cancelling) && !ReferenceEquals(value, _selectedProject))
            {
                return;
            }

            if (!SetProperty(ref _selectedProject, value))
            {
                return;
            }

            ResetPreparationForSelection();
            OnPropertyChanged(nameof(SelectedProjectText));
            NotifyCommands();
            _ = TryRestoreAsync(value, ++_restoreGeneration);
        }
    }

    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value))
            {
                _titleSourceText = string.IsNullOrWhiteSpace(value) ? null : "Owner override in Advanced details.";
                OnPropertyChanged(nameof(TitleSourceText));
                NotifyCommands();
            }
        }
    }

    public string Objective
    {
        get => _objective;
        set
        {
            if (SetProperty(ref _objective, value))
            {
                OnPropertyChanged(nameof(TitleSourceText));
                NotifyCommands();
            }
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

    public string ContextSourceText => _contextSourceText;

    public string CurrentWorkText => _currentWorkText;

    public string TitleSourceText => string.IsNullOrWhiteSpace(Title)
        ? "Title is derived from the owner request when Prepare runs."
        : _titleSourceText ?? "Owner-provided title.";

    public string MissingContextText
    {
        get
        {
            if (SelectedProject is null)
            {
                return "Missing before Prepare: registered project.";
            }

            if (string.IsNullOrWhiteSpace(Objective))
            {
                return "Missing before Prepare: owner work request.";
            }

            return ParseLines(AcceptanceCriteria).Count == 0
                ? "Missing before Prepare: acceptance criteria. APO will not invent them."
                : string.Empty;
        }
    }

    public string PlannerText => _prepared is null ? "Not resolved" : _prepared.Planner?.Name ?? "Persisted lineage";

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

    public string PrepareBlockedReason
    {
        get => GetPrepareBlocker() ?? string.Empty;
    }

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

    /// <summary>
    /// Reconciles <see cref="ProjectOptions"/> with current registry truth without app restart.
    /// Unlike <see cref="InitializeAsync"/>, this never clears the collection and never replaces
    /// or removes the currently selected option's instance while it still exists in the registry:
    /// the selector binds <c>SelectedItem</c> TwoWay, so dropping that exact instance out of the
    /// collection — even momentarily — lets WPF push a transient null back through the binding and
    /// silently wipe the owner's in-progress request. It also never auto-selects a project; that
    /// remains an <see cref="InitializeAsync"/>-only, cold-start behavior.
    /// </summary>
    public async Task RefreshProjectsAsync(CancellationToken cancellationToken = default)
    {
        if (_projects is null || !IsPersistenceAvailable)
        {
            return;
        }

        try
        {
            var latest = await _projects.GetProjectsAsync(cancellationToken).ConfigureAwait(true);
            ReconcileProjectOptions(latest);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // A same-process synchronization refresh must not destroy an already-loaded project list.
        }
    }

    private void ReconcileProjectOptions(IReadOnlyList<Project> latest)
    {
        var latestById = latest.ToDictionary(static project => project.Id);
        var existingIds = new HashSet<Guid>();

        for (var index = ProjectOptions.Count - 1; index >= 0; index--)
        {
            var option = ProjectOptions[index];
            if (latestById.TryGetValue(option.Id, out var project))
            {
                existingIds.Add(option.Id);
                option.Update(project);
            }
            else
            {
                ProjectOptions.RemoveAt(index);
            }
        }

        foreach (var project in latest)
        {
            if (!existingIds.Contains(project.Id))
            {
                ProjectOptions.Add(new MissionControlProjectOption(project));
            }
        }

        OnPropertyChanged(nameof(ShowEmptyState));
    }

    public void SetPersistenceAvailability(bool available)
    {
        _persistenceAvailable = available;
        OnPropertyChanged(nameof(IsPersistenceAvailable));
        OnPropertyChanged(nameof(ErrorMessage));
        NotifyCommands();
    }

    private bool CanPrepare() =>
        GetPrepareBlocker() is null;

    private bool CanStart() => IsPersistenceAvailable && _coordinator is not null && IsReady;

    private bool CanCancel() => IsPersistenceAvailable && _coordinator is not null && State == ExecutionCoordinatorState.Running;

    private string? GetPrepareBlocker()
    {
        if (!IsPersistenceAvailable || _coordinator is null)
        {
            return "Execution persistence is unavailable.";
        }

        if (_isRestoring)
        {
            return "Wait for persisted execution recovery to finish.";
        }

        if (_isPreparingExecution || IsBusy)
        {
            return "Another execution operation is already in progress.";
        }

        if (SelectedProject is null)
        {
            return "Select a registered project.";
        }

        if (string.IsNullOrWhiteSpace(Objective))
        {
            return "Enter a work request.";
        }

        if (Objective.Trim().Length > 4_000)
        {
            return "Work request cannot exceed 4,000 characters.";
        }

        var effectiveTitle = string.IsNullOrWhiteSpace(Title)
            ? ExecutionContextPrefillPolicy.DeriveTitle(Objective)
            : Title.Trim();
        if (effectiveTitle.Length > 500)
        {
            return "Title cannot exceed 500 characters.";
        }

        if (!string.IsNullOrWhiteSpace(WorkItemReference) && WorkItemReference.Trim().Length > 200)
        {
            return "Work item reference cannot exceed 200 characters.";
        }

        var acceptanceCriteria = ParseLines(AcceptanceCriteria);
        if (acceptanceCriteria.Count == 0)
        {
            return "Add acceptance criteria in Owner Mode.";
        }

        var lineError = ValidateLines(acceptanceCriteria, "Acceptance criteria", "Acceptance criterion");
        return lineError ?? ValidateLines(ParseLines(Constraints), "Constraints", "Constraint") ?? ValidateLines(ParseLines(ValidationExpectations), "Validation expectations", "Validation expectation");
    }

    private static string? ValidateLines(IReadOnlyList<string> values, string pluralName, string singularName)
    {
        if (values.Count > 32)
        {
            return $"{pluralName} cannot contain more than 32 entries.";
        }

        for (var index = 0; index < values.Count; index++)
        {
            if (values[index].Length > 4_000)
            {
                return $"{singularName} {index + 1} cannot exceed 4,000 characters.";
            }
        }

        return null;
    }

    private async Task PrepareAsync()
    {
        var project = SelectedProject;
        if (!CanPrepare() || _coordinator is null || project is null)
        {
            return;
        }

        try
        {
            var request = new OrchestrationWorkRequest(
                project.Id,
                Environment.UserName,
                string.IsNullOrWhiteSpace(Title) ? ExecutionContextPrefillPolicy.DeriveTitle(Objective) : Title,
                Objective,
                string.IsNullOrWhiteSpace(WorkItemReference) ? null : WorkItemReference,
                ParseLines(AcceptanceCriteria),
                ParseLines(Constraints),
                ParseLines(ValidationExpectations));
            _errorMessage = null;
            OnPropertyChanged(nameof(ErrorMessage));
            _isPreparingExecution = true;
            State = ExecutionCoordinatorState.Preparing;
            var result = await _coordinator.PrepareAsync(request).ConfigureAwait(true);
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
        finally
        {
            _isPreparingExecution = false;
            PublishPreparedState();
        }
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

    private async Task TryRestoreAsync(MissionControlProjectOption? project, long generation)
    {
        if (project is null || _coordinator is null || !IsPersistenceAvailable)
        {
            return;
        }

        await _restoreGate.WaitAsync().ConfigureAwait(true);
        var started = false;
        try
        {
            if (generation != _restoreGeneration || !ReferenceEquals(project, SelectedProject) || State is ExecutionCoordinatorState.Running or ExecutionCoordinatorState.Cancelling)
            {
                return;
            }

            started = true;
            _isRestoring = true;
            State = ExecutionCoordinatorState.Preparing;
            var result = await _coordinator.RestoreAsync(project.Id).ConfigureAwait(true);
            if (generation != _restoreGeneration || !ReferenceEquals(project, SelectedProject))
            {
                return;
            }

            _prepared = result.PreparedExecution;
            if (result.Succeeded && result.PreparedExecution is not null)
            {
                ApplyContextPrefill(ExecutionContextPrefillPolicy.FromPreparedExecution(result.PreparedExecution));
            }
            else
            {
                await TryPrefillFromMissionControlAsync(project.Id, generation).ConfigureAwait(true);
            }

            State = result.Succeeded ? ExecutionCoordinatorState.Ready : ExecutionCoordinatorState.Draft;
            _errorMessage = result.Succeeded || result.Status == ExecutionRehydrationStatus.NotResumable ? null : result.ErrorMessage;
        }
        catch (Exception)
        {
            if (generation == _restoreGeneration && ReferenceEquals(project, SelectedProject))
            {
                _prepared = null;
                State = ExecutionCoordinatorState.Draft;
            }
        }
        finally
        {
            if (started)
            {
                _isRestoring = false;
            }

            _restoreGate.Release();
            if (generation == _restoreGeneration && ReferenceEquals(project, SelectedProject))
            {
                PublishPreparedState();
            }
        }
    }

    public void ApplyContextPrefill(ExecutionContextPrefill prefill)
    {
        ArgumentNullException.ThrowIfNull(prefill);
        if (!prefill.IsAuthoritative)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(prefill.Title))
        {
            _title = prefill.Title;
            _titleSourceText = $"Reused from {prefill.Source}.";
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(TitleSourceText));
        }

        if (!string.IsNullOrWhiteSpace(prefill.Objective))
        {
            _objective = prefill.Objective;
            OnPropertyChanged(nameof(Objective));
        }

        if (!string.IsNullOrWhiteSpace(prefill.WorkItemReference))
        {
            _workItemReference = prefill.WorkItemReference;
            OnPropertyChanged(nameof(WorkItemReference));
        }

        SetPrefilledLines(prefill.AcceptanceCriteria, ref _acceptanceCriteria, nameof(AcceptanceCriteria));
        SetPrefilledLines(prefill.Constraints, ref _constraints, nameof(Constraints));
        SetPrefilledLines(prefill.ValidationExpectations, ref _validationExpectations, nameof(ValidationExpectations));

        _contextSourceText = prefill.Source;
        _currentWorkText = string.IsNullOrWhiteSpace(prefill.Title)
            ? "Current work title is unavailable."
            : string.IsNullOrWhiteSpace(prefill.WorkItemReference)
                ? prefill.Title
                : $"{prefill.WorkItemReference} · {prefill.Title}";
        OnPropertyChanged(nameof(ContextSourceText));
        OnPropertyChanged(nameof(CurrentWorkText));
        NotifyCommands();
    }

    private async Task TryPrefillFromMissionControlAsync(Guid projectId, long generation)
    {
        if (_missionControl is null)
        {
            return;
        }

        try
        {
            var snapshot = await _missionControl.ReadAsync(projectId).ConfigureAwait(true);
            if (generation == _restoreGeneration && SelectedProject?.Id == projectId)
            {
                ApplyContextPrefill(ExecutionContextPrefillPolicy.FromMissionControl(snapshot));
            }
        }
        catch
        {
            // Missing read-model evidence must leave the owner-visible request fields empty.
        }
    }

    private void SetPrefilledLines(IReadOnlyList<string> values, ref string target, string propertyName)
    {
        if (values.Count == 0)
        {
            return;
        }

        target = string.Join(Environment.NewLine, values);
        OnPropertyChanged(propertyName);
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

    private void ResetPreparationForSelection()
    {
        if (State is ExecutionCoordinatorState.Running or ExecutionCoordinatorState.Cancelling)
        {
            return;
        }

        _title = string.Empty;
        _objective = string.Empty;
        _workItemReference = string.Empty;
        _acceptanceCriteria = string.Empty;
        _constraints = string.Empty;
        _validationExpectations = string.Empty;
        _titleSourceText = null;
        _contextSourceText = "Project registry metadata is available; no authoritative current work is selected yet.";
        _currentWorkText = "No current work selected.";
        _prepared = null;
        _lastResult = null;
        _errorMessage = null;
        State = ExecutionCoordinatorState.Draft;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Objective));
        OnPropertyChanged(nameof(WorkItemReference));
        OnPropertyChanged(nameof(AcceptanceCriteria));
        OnPropertyChanged(nameof(Constraints));
        OnPropertyChanged(nameof(ValidationExpectations));
        OnPropertyChanged(nameof(ContextSourceText));
        OnPropertyChanged(nameof(CurrentWorkText));
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
        OnPropertyChanged(nameof(PrepareBlockedReason));
        OnPropertyChanged(nameof(MissingContextText));
        OnPropertyChanged(nameof(TitleSourceText));
    }

    private static IReadOnlyList<string> ParseLines(string value) =>
        value.Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
