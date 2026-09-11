using System.Windows.Input;
using AIUsageMonitor.Desktop;

namespace AIUsageMonitor.Desktop.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private object _activeWorkspace;
    private bool _isMissionControlSelected;
    private bool _isProjectsSelected;
    private bool _isAiCapacitySelected;
    private bool _persistenceAvailable;

    public MainWindowViewModel()
        : this(new MissionControlViewModel(), new AiCapacityViewModel(), new ProjectsViewModel())
    {
    }

    public MainWindowViewModel(AiCapacityViewModel aiCapacity)
        : this(new MissionControlViewModel(), aiCapacity, new ProjectsViewModel())
    {
    }

    public MainWindowViewModel(
        AiCapacityViewModel aiCapacity,
        ProjectsViewModel projects)
        : this(new MissionControlViewModel(), aiCapacity, projects)
    {
    }

    public MainWindowViewModel(
        MissionControlViewModel missionControl,
        AiCapacityViewModel aiCapacity,
        ProjectsViewModel projects)
    {
        MissionControl = missionControl ?? throw new ArgumentNullException(nameof(missionControl));
        AiCapacity = aiCapacity ?? throw new ArgumentNullException(nameof(aiCapacity));
        Projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _activeWorkspace = MissionControl;
        _isMissionControlSelected = true;
        ShowMissionControlCommand = new RelayCommand(ShowMissionControl);
        ShowProjectsCommand = new RelayCommand(ShowProjects);
        ShowAiCapacityCommand = new RelayCommand(ShowAiCapacity);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
    }

    public MissionControlViewModel MissionControl { get; }

    public AiCapacityViewModel AiCapacity { get; }

    public ProjectsViewModel Projects { get; }

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

    public ICommand ShowMissionControlCommand { get; }

    public ICommand ShowProjectsCommand { get; }

    public ICommand ShowAiCapacityCommand { get; }

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
            Projects.InitializeAsync(cancellationToken)).ConfigureAwait(true);
    }

    public async Task InitializeDegradedAsync(CancellationToken cancellationToken = default)
    {
        MissionControl.SetPersistenceAvailability(false);
        await AiCapacity.InitializeDegradedAsync(cancellationToken).ConfigureAwait(true);
        Projects.SetPersistenceAvailability(false);
        await Projects.InitializeAsync(cancellationToken).ConfigureAwait(true);
    }

    public void SetPersistenceAvailability(bool persistenceAvailable)
    {
        _persistenceAvailable = persistenceAvailable;
        OnPropertyChanged(nameof(GlobalStatusText));
        MissionControl.SetPersistenceAvailability(persistenceAvailable);
        Projects.SetPersistenceAvailability(persistenceAvailable);
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
    }

    private void ShowProjects()
    {
        ActiveWorkspace = Projects;
        IsMissionControlSelected = false;
        IsProjectsSelected = true;
        IsAiCapacitySelected = false;
    }

    private void ShowAiCapacity()
    {
        ActiveWorkspace = AiCapacity;
        IsMissionControlSelected = false;
        IsProjectsSelected = false;
        IsAiCapacitySelected = true;
    }

    private void ToggleTheme()
    {
        ThemeManager.Toggle();
        RefreshThemeState();
    }
}
