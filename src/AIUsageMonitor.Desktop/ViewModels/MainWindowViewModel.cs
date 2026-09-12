using System.Windows.Input;
using AIUsageMonitor.Desktop;

namespace AIUsageMonitor.Desktop.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private object _activeWorkspace;
    private bool _isMissionControlSelected;
    private bool _isProjectsSelected;
    private bool _isAiCapacitySelected;
    private bool _isExecutionSelected;
    private bool _persistenceAvailable;

    public MainWindowViewModel()
        : this(new MissionControlViewModel(), new AiCapacityViewModel(), new ProjectsViewModel(), new ExecutionViewModel())
    {
    }

    public MainWindowViewModel(AiCapacityViewModel aiCapacity)
        : this(new MissionControlViewModel(), aiCapacity, new ProjectsViewModel(), new ExecutionViewModel())
    {
    }

    public MainWindowViewModel(
        AiCapacityViewModel aiCapacity,
        ProjectsViewModel projects)
        : this(new MissionControlViewModel(), aiCapacity, projects, new ExecutionViewModel())
    {
    }

    public MainWindowViewModel(
        MissionControlViewModel missionControl,
        AiCapacityViewModel aiCapacity,
        ProjectsViewModel projects)
        : this(missionControl, aiCapacity, projects, new ExecutionViewModel())
    {
    }

    public MainWindowViewModel(
        MissionControlViewModel missionControl,
        AiCapacityViewModel aiCapacity,
        ProjectsViewModel projects,
        ExecutionViewModel execution)
    {
        MissionControl = missionControl ?? throw new ArgumentNullException(nameof(missionControl));
        AiCapacity = aiCapacity ?? throw new ArgumentNullException(nameof(aiCapacity));
        Projects = projects ?? throw new ArgumentNullException(nameof(projects));
        Execution = execution ?? throw new ArgumentNullException(nameof(execution));
        _activeWorkspace = MissionControl;
        _isMissionControlSelected = true;
        ShowMissionControlCommand = new RelayCommand(ShowMissionControl);
        ShowProjectsCommand = new RelayCommand(ShowProjects);
        ShowAiCapacityCommand = new RelayCommand(ShowAiCapacity);
        ShowExecutionCommand = new RelayCommand(ShowExecution);
        Projects.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ProjectsViewModel.CanAddExistingProject) or nameof(ProjectsViewModel.AddExistingProjectStateText))
            {
                OnPropertyChanged(nameof(CanAddExistingProject));
                OnPropertyChanged(nameof(AddExistingProjectStateText));
                (AddExistingProjectCommand as RelayCommand)?.NotifyCanExecuteChanged();
            }
        };
        AddExistingProjectCommand = new RelayCommand(OpenAddExistingProject, () => CanAddExistingProject);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
    }

    public MissionControlViewModel MissionControl { get; }

    public AiCapacityViewModel AiCapacity { get; }

    public ProjectsViewModel Projects { get; }

    public ExecutionViewModel Execution { get; }

    public object ActiveWorkspace
    {
        get => _activeWorkspace;
        private set => SetProperty(ref _activeWorkspace, value);
    }

    public bool IsMissionControlSelected
    {
        get => _isMissionControlSelected;
        private set => SetProperty(ref _isMissionControlSelected, value);
    }

    public bool IsAiCapacitySelected
    {
        get => _isAiCapacitySelected;
        private set => SetProperty(ref _isAiCapacitySelected, value);
    }

    public bool IsProjectsSelected
    {
        get => _isProjectsSelected;
        private set => SetProperty(ref _isProjectsSelected, value);
    }

    public bool IsExecutionSelected
    {
        get => _isExecutionSelected;
        private set => SetProperty(ref _isExecutionSelected, value);
    }

    public ICommand ShowMissionControlCommand { get; }

    public ICommand ShowProjectsCommand { get; }

    public ICommand ShowAiCapacityCommand { get; }

    public ICommand ShowExecutionCommand { get; }

    public ICommand AddExistingProjectCommand { get; }

    public bool CanAddExistingProject => Projects.CanAddExistingProject;

    public string AddExistingProjectStateText => Projects.AddExistingProjectStateText;

    public ICommand ToggleThemeCommand { get; }

    public bool IsDarkTheme => ThemeManager.CurrentTheme == ThemeVariant.Dark;

    public string ThemeToggleText => IsDarkTheme ? "Light" : "Dark";

    public string ThemeToggleIcon => IsDarkTheme ? "☼" : "◐";

    public string ThemeToggleToolTip => IsDarkTheme ? "Switch to light theme" : "Switch to dark theme";

    public string GlobalStatusText => _persistenceAvailable ? "LOCAL READY" : "SETUP REQUIRED";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await Task.WhenAll(
            MissionControl.InitializeAsync(cancellationToken),
            AiCapacity.InitializeAsync(cancellationToken),
            Projects.InitializeAsync(cancellationToken),
            Execution.InitializeAsync(cancellationToken)).ConfigureAwait(true);
        OnPropertyChanged(nameof(CanAddExistingProject));
        OnPropertyChanged(nameof(AddExistingProjectStateText));
        (AddExistingProjectCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    public async Task InitializeDegradedAsync(CancellationToken cancellationToken = default)
    {
        MissionControl.SetPersistenceAvailability(false);
        await AiCapacity.InitializeDegradedAsync(cancellationToken).ConfigureAwait(true);
        Projects.SetPersistenceAvailability(false);
        await Projects.InitializeAsync(cancellationToken).ConfigureAwait(true);
        Execution.SetPersistenceAvailability(false);
        await Execution.InitializeDegradedAsync(cancellationToken).ConfigureAwait(true);
    }

    public void SetPersistenceAvailability(bool persistenceAvailable)
    {
        _persistenceAvailable = persistenceAvailable;
        OnPropertyChanged(nameof(GlobalStatusText));
        MissionControl.SetPersistenceAvailability(persistenceAvailable);
        Projects.SetPersistenceAvailability(persistenceAvailable);
        Execution.SetPersistenceAvailability(persistenceAvailable);
    }

    public void RefreshThemeState()
    {
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(ThemeToggleText));
        OnPropertyChanged(nameof(ThemeToggleIcon));
        OnPropertyChanged(nameof(ThemeToggleToolTip));
    }

    private void ShowMissionControl()
    {
        ActiveWorkspace = MissionControl;
        IsMissionControlSelected = true;
        IsProjectsSelected = false;
        IsAiCapacitySelected = false;
        IsExecutionSelected = false;
    }

    private void ShowProjects()
    {
        ActiveWorkspace = Projects;
        IsMissionControlSelected = false;
        IsProjectsSelected = true;
        IsAiCapacitySelected = false;
        IsExecutionSelected = false;
    }

    private void ShowAiCapacity()
    {
        ActiveWorkspace = AiCapacity;
        IsMissionControlSelected = false;
        IsProjectsSelected = false;
        IsAiCapacitySelected = true;
        IsExecutionSelected = false;
    }

    private void ShowExecution()
    {
        ActiveWorkspace = Execution;
        IsMissionControlSelected = false;
        IsProjectsSelected = false;
        IsAiCapacitySelected = false;
        IsExecutionSelected = true;
    }

    private void OpenAddExistingProject()
    {
        if (!CanAddExistingProject)
        {
            return;
        }

        ShowProjects();
        Projects.AddExistingProjectCommand.Execute(null);
    }

    private void ToggleTheme()
    {
        ThemeManager.Toggle();
        RefreshThemeState();
    }
}
