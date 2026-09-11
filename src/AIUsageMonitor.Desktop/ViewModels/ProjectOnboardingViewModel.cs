using System.Collections.ObjectModel;
using System.IO;
using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Projects;

namespace AIUsageMonitor.Desktop.ViewModels;

public enum ProjectOnboardingStep
{
    Project = 1,
    Repository = 2,
    Tracker = 3,
    Agents = 4
}

public enum RepositoryOnboardingChoice
{
    NotSelected,
    AcceptDetected,
    Skip
}

public sealed class ProjectOnboardingAgentOptionViewModel : ObservableObject
{
    private bool _isEnabled = true;

    public ProjectOnboardingAgentOptionViewModel(AgentDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        RolesText = string.Join(", ", definition.RoleCapabilities);
    }

    public AgentDefinition Definition { get; }
    public Guid AgentId => Definition.Id;
    public string DisplayName => Definition.Name;
    public string RolesText { get; }
    public string AccessTruthText =>
        $"Access: {Definition.Availability}; authentication: {Definition.AuthenticationState}; entitlement: {Definition.EntitlementState}.";

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }
}

/// <summary>
/// Four-step New Project onboarding state. It owns no persistence and never performs tracker,
/// remote SCM, provider, routing, or model calls; the application service owns completion truth.
/// </summary>
public sealed class ProjectOnboardingViewModel : ObservableObject
{
    private readonly IProjectOnboardingService _service;
    private readonly Func<ProjectOnboardingResult, Task>? _onFinished;
    private readonly Action? _onCanceled;
    private ProjectOnboardingStep _currentStep = ProjectOnboardingStep.Project;
    private RepositoryOnboardingChoice _repositoryChoice;
    private LocalRepositoryInspection? _repositoryInspection;
    private string _name = string.Empty;
    private string _localPath = string.Empty;
    private string _repositoryDefaultBranch = string.Empty;
    private string _selectedTrackerOption = TrackerOptionsList[0];
    private string _trackerReference = string.Empty;
    private bool _isBusy;
    private bool _isCompletionTerminal;
    private string? _errorMessage;
    private Func<string?>? _pathPicker;

    private static readonly IReadOnlyList<string> TrackerOptionsList =
        ["No tracker / Skip", "Jira", "Azure Boards", "Other / Manual reference"];

    public ProjectOnboardingViewModel(
        IProjectOnboardingService service,
        IDefaultAgentCatalog catalog,
        Func<ProjectOnboardingResult, Task>? onFinished = null,
        Action? onCanceled = null,
        Func<string?>? pathPicker = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        ArgumentNullException.ThrowIfNull(catalog);
        _onFinished = onFinished;
        _onCanceled = onCanceled;
        _pathPicker = pathPicker;
        AgentOptions = new ObservableCollection<ProjectOnboardingAgentOptionViewModel>(
            catalog.GetDefaults().Select(static definition => new ProjectOnboardingAgentOptionViewModel(definition)));

        NextCommand = new RelayCommand(Next, CanNext);
        BackCommand = new RelayCommand(Back, () => !IsBusy && CurrentStep != ProjectOnboardingStep.Project);
        InspectRepositoryCommand = new AsyncCommand(InspectRepositoryAsync, CanInspectRepository);
        AcceptRepositoryCommand = new RelayCommand(
            () => RepositoryChoice = RepositoryOnboardingChoice.AcceptDetected,
            () => CanAcceptRepository);
        SkipRepositoryCommand = new RelayCommand(
            () => RepositoryChoice = RepositoryOnboardingChoice.Skip,
            () => !IsBusy && IsRepositoryStep);
        FinishCommand = new AsyncCommand(FinishAsync, CanFinish);
        CancelCommand = new RelayCommand(Cancel, () => !IsBusy && !IsCompletionTerminal);
        BrowsePathCommand = new RelayCommand(BrowsePath, CanBrowsePath);
    }

    public ObservableCollection<ProjectOnboardingAgentOptionViewModel> AgentOptions { get; }

    public IReadOnlyList<string> TrackerOptions => TrackerOptionsList;

    public ProjectOnboardingStep CurrentStep
    {
        get => _currentStep;
        private set
        {
            if (SetProperty(ref _currentStep, value))
            {
                OnPropertyChanged(nameof(StepNumber));
                OnPropertyChanged(nameof(StepTitle));
                OnPropertyChanged(nameof(IsProjectStep));
                OnPropertyChanged(nameof(IsRepositoryStep));
                OnPropertyChanged(nameof(IsTrackerStep));
                OnPropertyChanged(nameof(IsAgentsStep));
                NotifyCommands();
            }
        }
    }

    public int StepNumber => (int)CurrentStep;

    public string StepTitle => CurrentStep switch
    {
        ProjectOnboardingStep.Project => "Project",
        ProjectOnboardingStep.Repository => "Preview",
        ProjectOnboardingStep.Tracker => "Tracker",
        ProjectOnboardingStep.Agents => "AI roles",
        _ => "Project"
    };

    public bool IsProjectStep => CurrentStep == ProjectOnboardingStep.Project;
    public bool IsRepositoryStep => CurrentStep == ProjectOnboardingStep.Repository;
    public bool IsTrackerStep => CurrentStep == ProjectOnboardingStep.Tracker;
    public bool IsAgentsStep => CurrentStep == ProjectOnboardingStep.Agents;
    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value ?? string.Empty))
            {
                NotifyCommands();
            }
        }
    }

    public string LocalPath
    {
        get => _localPath;
        set
        {
            if (!SetProperty(ref _localPath, value ?? string.Empty))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(_localPath))
            {
                var proposed = Path.GetFileName(
                    _localPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!string.IsNullOrWhiteSpace(proposed))
                {
                    Name = proposed;
                }
            }

            NotifyCommands();
        }
    }

    public RepositoryOnboardingChoice RepositoryChoice
    {
        get => _repositoryChoice;
        private set
        {
            if (SetProperty(ref _repositoryChoice, value))
            {
                OnPropertyChanged(nameof(RepositoryDecisionText));
                NotifyCommands();
            }
        }
    }

    public LocalRepositoryInspection? RepositoryInspection
    {
        get => _repositoryInspection;
        private set
        {
            if (SetProperty(ref _repositoryInspection, value))
            {
                OnPropertyChanged(nameof(HasRepositoryInspection));
                OnPropertyChanged(nameof(RepositoryStatusText));
                OnPropertyChanged(nameof(RepositoryRootText));
                OnPropertyChanged(nameof(RepositoryBranchText));
                OnPropertyChanged(nameof(RepositoryRemoteText));
                OnPropertyChanged(nameof(RepositoryHeadText));
                OnPropertyChanged(nameof(RemoteProviderText));
                OnPropertyChanged(nameof(RemoteUrlText));
                OnPropertyChanged(nameof(GovernanceFilesText));
                OnPropertyChanged(nameof(ProjectFilesText));
                OnPropertyChanged(nameof(WorkspaceReadinessText));
                OnPropertyChanged(nameof(RepositoryCapturedAtText));
                OnPropertyChanged(nameof(CanAcceptRepository));
                NotifyCommands();
            }
        }
    }

    public bool HasRepositoryInspection => RepositoryInspection is not null;

    public string RepositoryStatusText => RepositoryInspection?.Status switch
    {
        null => "Not inspected",
        RepositoryVerificationStatus.PathMissing => "Local path missing",
        RepositoryVerificationStatus.PathUnavailable => "Local path unavailable",
        RepositoryVerificationStatus.GitUnavailable => "Git unavailable",
        RepositoryVerificationStatus.NotGitRepository => "Not a Git repository",
        RepositoryVerificationStatus.AvailableClean => "Verified local repository — clean",
        RepositoryVerificationStatus.AvailableDirty => "Verified local repository — changes present",
        RepositoryVerificationStatus.Failed => "Inspection failed",
        _ => "Not inspected"
    };

    public string RepositoryRootText => RepositoryInspection?.RepositoryRoot ?? "Not available";

    public string RepositoryBranchText => RepositoryInspection is { IsDetachedHead: true }
        ? "Detached HEAD — no branch proposed"
        : RepositoryInspection?.BranchName ?? "No usable current branch";

    public string RepositoryRemoteText => RepositoryInspection is null
        ? "Not inspected"
        : RepositoryInspection.Remotes.Count == 0
            ? "No configured local remotes"
            : "Detected from local Git configuration — connectivity not verified";

    public string RepositoryHeadText => RepositoryInspection?.HeadShortSha
        ?? (RepositoryInspection?.HeadSha is { Length: > 0 } head ? head[..Math.Min(7, head.Length)] : "Not available");

    public string RemoteProviderText => RepositoryInspection?.Workspace?.RemoteProvider ??
        (RepositoryInspection is null ? "Not inspected" : "No remote provider identified");

    public string RemoteUrlText => RepositoryInspection?.Remotes.FirstOrDefault(remote =>
        string.Equals(remote.Name, "origin", StringComparison.OrdinalIgnoreCase))?.SanitizedUrl
        ?? "No origin remote";

    public string GovernanceFilesText => RepositoryInspection?.Workspace is not { } workspace
        ? "Not inspected"
        : string.Join(", ", workspace.GovernanceFiles.Select(static item => $"{item.Key}: {item.Value}"));

    public string ProjectFilesText => RepositoryInspection?.Workspace is not { } workspace
        ? "Not inspected"
        : workspace.ProjectFiles.Count == 0
            ? "No solution/build files detected at the selected root"
            : string.Join(", ", workspace.ProjectFiles);

    public string WorkspaceReadinessText => RepositoryInspection?.Workspace is not { } workspace
        ? "Select a folder and inspect it to build the preview."
        : !workspace.Exists
            ? "The selected folder no longer exists. Choose another workspace."
            : !workspace.IsReadable
                ? "The selected folder could not be read safely."
                : "Read-only discovery complete. APO will persist only its local registry metadata.";

    public string RepositoryCapturedAtText => RepositoryInspection is null
        ? "Not inspected"
        : RepositoryInspection.CapturedAt.ToLocalTime().ToString("MMM d, yyyy h:mm tt");

    public string RepositoryDecisionText => RepositoryChoice switch
    {
        RepositoryOnboardingChoice.AcceptDetected => "Local repository metadata will be retained as historical evidence.",
        RepositoryOnboardingChoice.Skip => "This project will continue without repository integration.",
        _ => "Choose whether to accept the local evidence or continue without repository integration."
    };

    public string RepositoryDefaultBranch
    {
        get => _repositoryDefaultBranch;
        set
        {
            if (SetProperty(ref _repositoryDefaultBranch, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanAcceptRepository));
                NotifyCommands();
            }
        }
    }

    public bool CanAcceptRepository =>
        !IsBusy &&
        RepositoryInspection is { Status: RepositoryVerificationStatus.AvailableClean or RepositoryVerificationStatus.AvailableDirty } &&
        (!string.IsNullOrWhiteSpace(RepositoryInspection.BranchName) || !string.IsNullOrWhiteSpace(RepositoryDefaultBranch));

    public string SelectedTrackerOption
    {
        get => _selectedTrackerOption;
        set
        {
            if (SetProperty(ref _selectedTrackerOption, value ?? TrackerOptionsList[0]))
            {
                OnPropertyChanged(nameof(IsTrackerSkipped));
                OnPropertyChanged(nameof(TrackerStateText));
                NotifyCommands();
            }
        }
    }

    public bool IsTrackerSkipped => string.Equals(SelectedTrackerOption, TrackerOptionsList[0], StringComparison.Ordinal);

    public string TrackerReference
    {
        get => _trackerReference;
        set
        {
            if (SetProperty(ref _trackerReference, value ?? string.Empty))
            {
                NotifyCommands();
            }
        }
    }

    public string TrackerStateText => IsTrackerSkipped
        ? "Skipped — no tracker connectivity is checked."
        : "Configured / connectivity not verified. Enter only a bounded project or reference ID.";

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    /// <summary>
    /// A successful or partial completion ends this wizard instance. A partial project is durable
    /// and must not be created again by pressing Finish or retrying the same wizard.
    /// </summary>
    public bool IsCompletionTerminal => _isCompletionTerminal;

    public RelayCommand NextCommand { get; }
    public RelayCommand BackCommand { get; }
    public AsyncCommand InspectRepositoryCommand { get; }
    public RelayCommand AcceptRepositoryCommand { get; }
    public RelayCommand SkipRepositoryCommand { get; }
    public AsyncCommand FinishCommand { get; }
    public RelayCommand CancelCommand { get; }

    public RelayCommand BrowsePathCommand { get; }

    public void SetPathPicker(Func<string?> pathPicker)
    {
        _pathPicker = pathPicker ?? throw new ArgumentNullException(nameof(pathPicker));
        BrowsePathCommand.NotifyCanExecuteChanged();
    }

    private bool CanNext() => !IsBusy && CurrentStep != ProjectOnboardingStep.Agents && IsCurrentStepValid();

    private void Next()
    {
        ErrorMessage = null;
        if (!IsCurrentStepValid())
        {
            return;
        }

        CurrentStep = (ProjectOnboardingStep)((int)CurrentStep + 1);
    }

    private void Back()
    {
        ErrorMessage = null;
        if (CurrentStep != ProjectOnboardingStep.Project)
        {
            CurrentStep = (ProjectOnboardingStep)((int)CurrentStep - 1);
        }
    }

    private bool IsCurrentStepValid()
    {
        switch (CurrentStep)
        {
            case ProjectOnboardingStep.Project when string.IsNullOrWhiteSpace(Name):
                ErrorMessage = "Project name is required.";
                return false;
            case ProjectOnboardingStep.Project when string.IsNullOrWhiteSpace(LocalPath):
                ErrorMessage = "Local workspace path is required.";
                return false;
            case ProjectOnboardingStep.Repository when RepositoryChoice == RepositoryOnboardingChoice.NotSelected:
                ErrorMessage = "Confirm the preview evidence or choose to continue without repository integration.";
                return false;
            case ProjectOnboardingStep.Repository when RepositoryChoice == RepositoryOnboardingChoice.AcceptDetected && !CanAcceptRepository:
                ErrorMessage = "A verified repository with a usable branch is required; otherwise choose skip.";
                return false;
            case ProjectOnboardingStep.Tracker when !IsTrackerSkipped && string.IsNullOrWhiteSpace(TrackerReference):
                ErrorMessage = "A bounded tracker reference is required, or choose No tracker / Skip.";
                return false;
            default:
                return true;
        }
    }

    private bool CanInspectRepository() =>
        !IsBusy && IsRepositoryStep && !string.IsNullOrWhiteSpace(LocalPath);

    private bool CanBrowsePath() => !IsBusy && IsProjectStep && _pathPicker is not null;

    private void BrowsePath()
    {
        var selectedPath = _pathPicker?.Invoke();
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            LocalPath = selectedPath;
        }
    }

    private async Task InspectRepositoryAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            RepositoryInspection = await _service
                .InspectRepositoryAsync(LocalPath.Trim())
                .ConfigureAwait(true);
            RepositoryChoice = RepositoryOnboardingChoice.NotSelected;
            if (!string.IsNullOrWhiteSpace(RepositoryInspection.BranchName))
            {
                RepositoryDefaultBranch = RepositoryInspection.BranchName;
            }
        }
        catch (ArgumentException exception)
        {
            ErrorMessage = exception.Message;
        }
        catch
        {
            ErrorMessage = "Repository inspection could not be completed.";
        }
        finally
        {
            IsBusy = false;
            NotifyCommands();
        }
    }

    private bool CanFinish() => !IsBusy && IsAgentsStep && !IsCompletionTerminal;

    public async Task FinishAsync()
    {
        if (!CanFinish())
        {
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var result = await _service.CompleteAsync(
                    new ProjectOnboardingRequest
                    {
                        Name = Name,
                        LocalPath = LocalPath,
                        SkipRepository = RepositoryChoice == RepositoryOnboardingChoice.Skip,
                        RepositoryInspection = RepositoryInspection,
                        RepositoryDefaultBranch = RepositoryDefaultBranch,
                        SkipTracker = IsTrackerSkipped,
                        TrackerType = IsTrackerSkipped ? null : SelectedTrackerOption,
                        TrackerReference = IsTrackerSkipped ? null : TrackerReference,
                        EnabledAgentIds = AgentOptions
                            .Where(static option => option.IsEnabled)
                            .Select(static option => option.AgentId)
                            .ToArray()
                    })
                .ConfigureAwait(true);

            if (!result.Succeeded)
            {
                if (result.Status == ProjectOnboardingCompletionStatus.AlreadyRegistered)
                {
                    _isCompletionTerminal = true;
                    OnPropertyChanged(nameof(IsCompletionTerminal));
                    ErrorMessage = result.ErrorMessage ?? "Project already registered.";
                    NotifyCommands();
                    if (_onFinished is not null)
                    {
                        await _onFinished(result).ConfigureAwait(true);
                    }

                    return;
                }

                if (result.IsPartialProjectCreated && result.Project is not null)
                {
                    _isCompletionTerminal = true;
                    OnPropertyChanged(nameof(IsCompletionTerminal));
                    NotifyCommands();
                    ErrorMessage = result.ErrorMessage ??
                        "The project was created, but onboarding could not be completed. Its context is incomplete and the project is not ready for planning.";
                    if (_onFinished is not null)
                    {
                        await _onFinished(result).ConfigureAwait(true);
                    }

                    return;
                }

                ErrorMessage = result.ErrorMessage ?? "Project onboarding could not be completed.";
                return;
            }

            _isCompletionTerminal = true;
            OnPropertyChanged(nameof(IsCompletionTerminal));
            NotifyCommands();
            if (_onFinished is not null)
            {
                await _onFinished(result).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Project onboarding was cancelled.";
        }
        catch
        {
            ErrorMessage = "Project onboarding could not be completed.";
        }
        finally
        {
            IsBusy = false;
            NotifyCommands();
        }
    }

    private void Cancel() => _onCanceled?.Invoke();

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyCommands();
            }
        }
    }

    private void NotifyCommands()
    {
        NextCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        InspectRepositoryCommand.NotifyCanExecuteChanged();
        AcceptRepositoryCommand.NotifyCanExecuteChanged();
        SkipRepositoryCommand.NotifyCanExecuteChanged();
        FinishCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        BrowsePathCommand.NotifyCanExecuteChanged();
    }
}
