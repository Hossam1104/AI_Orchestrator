using System.Collections.ObjectModel;
using System.Globalization;
using AIUsageMonitor.Application.MissionControl;
using AIUsageMonitor.Application.Projects;

namespace AIUsageMonitor.Desktop.ViewModels;

public sealed class MissionControlProjectOption
{
    public MissionControlProjectOption(Project project)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
    }

    public Project Project { get; }

    public Guid Id => Project.Id;

    public string Name => Project.Name;

    public string StatusText => Project.Status.ToString();

    public string DisplayText => $"{Name} · {StatusText}";
}

/// <summary>
/// Read-only command-center presentation. Project selection is explicit and every displayed fact
/// comes from the Application Mission Control snapshot; this view model never probes external
/// systems or parses persistence.
/// </summary>
public sealed class MissionControlViewModel : ObservableObject
{
    private readonly IProjectRegistryService? _projects;
    private readonly IMissionControlReadModelService? _readModel;
    private MissionControlProjectOption? _selectedProject;
    private MissionControlSnapshot? _snapshot;
    private bool _isStorageAvailable;
    private bool _isRefreshing;
    private bool _isLoadingProjects;
    private string? _errorMessage;
    private string _lastRefreshText = "Not refreshed";
    private long _selectionGeneration;
    private bool _suppressSelectionRefresh;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private CancellationTokenSource? _selectionRefreshCancellation;

    public MissionControlViewModel()
        : this(null, null)
    {
    }

    public MissionControlViewModel(
        IProjectRegistryService? projects,
        IMissionControlReadModelService? readModel)
    {
        _projects = projects;
        _readModel = readModel;
        _isStorageAvailable = projects is not null && readModel is not null;
        RefreshCommand = new AsyncCommand(() => RefreshAsync(), CanRefresh);
    }

    public ObservableCollection<MissionControlProjectOption> ProjectOptions { get; } = [];

    public AsyncCommand RefreshCommand { get; }

    public MissionControlProjectOption? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (!SetProperty(ref _selectedProject, value))
                return;

            _selectionGeneration++;
            Snapshot = null;
            ErrorMessage = null;
            OnSelectionChanged();
            if (!_suppressSelectionRefresh && value is not null)
            {
                _ = StartSelectionRefreshAsync(value.Id, _selectionGeneration);
            }
        }
    }

    public MissionControlSnapshot? Snapshot
    {
        get => _snapshot;
        private set
        {
            if (SetProperty(ref _snapshot, value))
            {
                OnPropertyChanged(nameof(OverallStateText));
                OnPropertyChanged(nameof(StateReasonText));
                OnPropertyChanged(nameof(CurrentWorkText));
                OnPropertyChanged(nameof(CurrentStepText));
                OnPropertyChanged(nameof(NextSafeActionText));
                OnPropertyChanged(nameof(RepositoryText));
                OnPropertyChanged(nameof(TrackerText));
                OnPropertyChanged(nameof(ValidationText));
                OnPropertyChanged(nameof(ReviewText));
                OnPropertyChanged(nameof(ApprovalText));
                OnPropertyChanged(nameof(RuntimeText));
                OnPropertyChanged(nameof(LastEvidenceText));
                OnPropertyChanged(nameof(AttentionItems));
                OnPropertyChanged(nameof(Limitations));
                OnPropertyChanged(nameof(HasAttention));
                OnPropertyChanged(nameof(HasLimitations));
                OnPropertyChanged(nameof(HasSnapshot));
            }
        }
    }

    public bool IsStorageAvailable
    {
        get => _isStorageAvailable;
        private set
        {
            if (SetProperty(ref _isStorageAvailable, value))
            {
                OnPropertyChanged(nameof(ShowStorageUnavailableState));
                OnPropertyChanged(nameof(ShowSelectProjectState));
                OnPropertyChanged(nameof(OverallStateText));
                RefreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
            {
                OnPropertyChanged(nameof(RefreshStateText));
                RefreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsLoadingProjects
    {
        get => _isLoadingProjects;
        private set
        {
            if (SetProperty(ref _isLoadingProjects, value))
            {
                OnPropertyChanged(nameof(ShowLoadingState));
                OnPropertyChanged(nameof(ShowSelectProjectState));
                RefreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(RefreshStateText));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasProjects => ProjectOptions.Count > 0;

    public bool HasSelectedProject => SelectedProject is not null;

    public bool HasSnapshot => Snapshot is not null;

    public bool HasAttention => Snapshot?.AttentionItems.Count > 0;

    public bool HasLimitations => Snapshot?.Limitations.Count > 0;

    public bool ShowLoadingState => IsLoadingProjects;

    public bool ShowStorageUnavailableState => !IsStorageAvailable && !IsLoadingProjects;

    public bool ShowSelectProjectState =>
        IsStorageAvailable && !IsLoadingProjects && !HasSelectedProject;

    public string SelectedProjectText => SelectedProject?.Name ?? "Select a project";

    public string OverallStateText => Snapshot?.State.ToString() ??
        (ShowStorageUnavailableState ? "Unknown" : HasSelectedProject ? "Unknown" : "Select project");

    public string StateReasonText => Snapshot?.StateReason ??
        (ShowStorageUnavailableState
            ? "Persisted orchestration truth cannot be loaded in degraded mode."
            : HasSelectedProject
                ? "Select Refresh to read the available project evidence."
                : "No project is selected; Mission Control will not guess one.");

    public string CurrentWorkText => Snapshot?.CurrentWork.Title ?? "No current work selected";

    public string CurrentStepText => Snapshot?.CurrentWork.CurrentStep ?? "No authoritative current-step evidence";

    public string NextSafeActionText => Snapshot?.CurrentWork.NextSafeAction ?? "Unknown";

    public string RolesText => Snapshot?.Roles.StatusText ?? "Role assignments unavailable";

    public string RepositoryText => Snapshot?.Repository.StatusText ?? "Repository evidence unavailable";

    public string TrackerText => Snapshot?.Tracker.StatusText ?? "Tracker evidence unavailable";

    public string ValidationText => Snapshot?.Validation.StatusText ?? "No validation-gate evidence";

    public string ReviewText => Snapshot?.Review.HasEvidence == true
        ? $"{Snapshot.Review.WorkflowState} · {Snapshot.Review.Verdict}"
        : "No review evidence";

    public string ApprovalText => Snapshot?.Approval.StatusText ?? "No current approval evidence";

    public string RuntimeText => Snapshot?.Runtime.StatusText ?? "No runtime evidence";

    public string LastEvidenceText => Snapshot is null
        ? _lastRefreshText
        : $"Read {Snapshot.ReadAt.ToLocalTime().ToString("MMM d, yyyy h:mm tt", CultureInfo.CurrentCulture)}";

    public string RefreshStateText => IsRefreshing
        ? "Refreshing Mission Control read model…"
        : ErrorMessage ?? (Snapshot is null ? _lastRefreshText : "Read model ready");

    public IReadOnlyList<MissionControlAttentionItem> AttentionItems =>
        Snapshot?.AttentionItems ?? Array.Empty<MissionControlAttentionItem>();

    public IReadOnlyList<MissionControlLimitation> Limitations =>
        Snapshot?.Limitations ?? Array.Empty<MissionControlLimitation>();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_projects is null || _readModel is null)
        {
            SetPersistenceAvailability(false);
            return;
        }

        await LoadProjectsAsync(cancellationToken).ConfigureAwait(true);
        if (SelectedProject is not null)
        {
            await RefreshSelectedProjectAsync(SelectedProject.Id, _selectionGeneration, cancellationToken).ConfigureAwait(true);
        }
    }

    public void SetPersistenceAvailability(bool persistenceAvailable)
    {
        IsStorageAvailable = persistenceAvailable && _projects is not null && _readModel is not null;
        if (!IsStorageAvailable)
        {
            Snapshot = null;
            ErrorMessage = "Mission Control persistence unavailable.";
            _lastRefreshText = "No persisted evidence available";
            OnPropertyChanged(nameof(LastEvidenceText));
            OnPropertyChanged(nameof(RefreshStateText));
        }
        else if (ErrorMessage == "Mission Control persistence unavailable.")
        {
            ErrorMessage = null;
        }
    }

    public async Task SelectProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var option = ProjectOptions.FirstOrDefault(value => value.Id == projectId);
        if (option is null)
            throw new KeyNotFoundException("The selected project was not found.");

        _suppressSelectionRefresh = true;
        try
        {
            SelectedProject = option;
        }
        finally
        {
            _suppressSelectionRefresh = false;
        }

        await StartSelectionRefreshAsync(option.Id, _selectionGeneration, cancellationToken).ConfigureAwait(true);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedProject is null)
            return;

        await RefreshSelectedProjectAsync(SelectedProject.Id, _selectionGeneration, cancellationToken).ConfigureAwait(true);
    }

    private async Task LoadProjectsAsync(CancellationToken cancellationToken)
    {
        if (_projects is null)
        {
            SetPersistenceAvailability(false);
            return;
        }

        if (IsLoadingProjects)
            return;

        IsLoadingProjects = true;
        ErrorMessage = null;
        try
        {
            var projects = await _projects.GetProjectsAsync(cancellationToken).ConfigureAwait(true);
            ProjectOptions.Clear();
            foreach (var project in projects.OrderBy(value => value.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                ProjectOptions.Add(new MissionControlProjectOption(project));
            }

            OnPropertyChanged(nameof(HasProjects));
            IsStorageAvailable = true;

            var active = ProjectOptions.Where(value => value.Project.Status == ProjectStatus.Active).ToArray();
            _suppressSelectionRefresh = true;
            try
            {
                SelectedProject = active.Length == 1 ? active[0] : null;
            }
            finally
            {
                _suppressSelectionRefresh = false;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            IsStorageAvailable = false;
            ErrorMessage = "Mission Control projects could not be loaded.";
        }
        finally
        {
            IsLoadingProjects = false;
        }
    }

    private async Task RefreshSelectedProjectAsync(
        Guid projectId,
        long generation,
        CancellationToken cancellationToken = default)
    {
        if (_readModel is null || !IsStorageAvailable)
            return;

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (generation != _selectionGeneration || SelectedProject?.Id != projectId)
                return;

            IsRefreshing = true;
            ErrorMessage = null;
            var snapshot = await _readModel.ReadAsync(projectId, cancellationToken).ConfigureAwait(true);
            if (generation != _selectionGeneration || SelectedProject?.Id != projectId)
                return;

            Snapshot = snapshot;
            _lastRefreshText = $"Last refreshed {snapshot.ReadAt.ToLocalTime().ToString("MMM d, yyyy h:mm tt", CultureInfo.CurrentCulture)}";
            OnPropertyChanged(nameof(LastEvidenceText));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            if (generation == _selectionGeneration && SelectedProject?.Id == projectId)
            {
                Snapshot = null;
                ErrorMessage = "Mission Control could not read this project's evidence.";
            }
        }
        finally
        {
            IsRefreshing = false;
            _refreshGate.Release();
        }
    }

    private async Task StartSelectionRefreshAsync(
        Guid projectId,
        long generation,
        CancellationToken cancellationToken = default)
    {
        // Cancel only. The superseded refresh still owns that source and disposes it in its own
        // finally; disposing it here races an in-flight operation that may still register on its
        // token, which surfaces as an ObjectDisposedException instead of a clean cancellation.
        _selectionRefreshCancellation?.Cancel();
        var selectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _selectionRefreshCancellation = selectionCancellation;
        try
        {
            await RefreshSelectedProjectAsync(projectId, generation, selectionCancellation.Token).ConfigureAwait(true);
        }
        finally
        {
            if (ReferenceEquals(_selectionRefreshCancellation, selectionCancellation))
            {
                _selectionRefreshCancellation = null;
            }
            selectionCancellation.Dispose();
        }
    }

    private bool CanRefresh() =>
        IsStorageAvailable && HasSelectedProject && !IsLoadingProjects && !IsRefreshing;

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelectedProject));
        OnPropertyChanged(nameof(ShowSelectProjectState));
        OnPropertyChanged(nameof(SelectedProjectText));
        OnPropertyChanged(nameof(OverallStateText));
        OnPropertyChanged(nameof(StateReasonText));
        OnPropertyChanged(nameof(CurrentWorkText));
        OnPropertyChanged(nameof(CurrentStepText));
        OnPropertyChanged(nameof(NextSafeActionText));
        OnPropertyChanged(nameof(LastEvidenceText));
        RefreshCommand.NotifyCanExecuteChanged();
    }
}
