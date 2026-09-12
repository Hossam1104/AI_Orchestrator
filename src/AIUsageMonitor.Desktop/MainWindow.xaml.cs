using AIUsageMonitor.Desktop.ViewModels;
using AIUsageMonitor.Domain.Providers;
using System.Windows;
using Forms = global::System.Windows.Forms;

namespace AIUsageMonitor.Desktop;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private bool _persistenceAvailable;

    public MainWindow()
        : this(new MainWindowViewModel())
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
        InitializeComponent();
        Loaded += OnLoaded;

        // ThemeManager.ThemeChanged is static, so a window that never unsubscribes keeps itself
        // and its whole view-model graph alive for the life of the process.
        Closed += OnClosed;
    }

    public void SetPersistenceAvailability(bool persistenceAvailable)
    {
        _persistenceAvailable = persistenceAvailable;
        _viewModel.SetPersistenceAvailability(persistenceAvailable);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        ThemeManager.ThemeChanged += OnThemeChanged;
        _viewModel.Projects.SetPathPicker(PickLocalPath);
        if (_persistenceAvailable)
        {
            _viewModel.AiCapacity.SetEditorLauncher(OpenConnectionEditorAsync);
            _viewModel.AiCapacity.SetAddProviderLauncher(OpenAddProviderAsync);
            _viewModel.AiCapacity.SetRemoveProviderLauncher(RemoveProviderAsync);
            await _viewModel.InitializeAsync();
        }
        else
        {
            await _viewModel.InitializeDegradedAsync();
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => _viewModel.RefreshThemeState();

    /// <summary>
    /// Opens the folder picker at the preferred root when one was proven to exist, otherwise at
    /// the normal Windows default. A preferred root only positions the dialog; the operator still
    /// chooses the folder and nothing is registered or selected on their behalf.
    /// </summary>
    private static string? PickLocalPath(string? preferredRoot)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Choose a local project workspace",
            ShowNewFolderButton = false,
            UseDescriptionForTitle = true
        };

        if (!string.IsNullOrWhiteSpace(preferredRoot))
        {
            dialog.SelectedPath = preferredRoot;
        }

        return dialog.ShowDialog() == Forms.DialogResult.OK
            ? dialog.SelectedPath
            : null;
    }

    private void OnClosed(object? sender, EventArgs e) =>
        ThemeManager.ThemeChanged -= OnThemeChanged;

    private async Task OpenConnectionEditorAsync(ProviderCapacityCardViewModel card)
    {
        if (!card.CanConfigure ||
            _viewModel.AiCapacity.ConnectionService is not { } service)
        {
            return;
        }

        var connection = card.BuiltInCode is { } code
            ? await service.GetAsync(code)
            : await service.GetAsync(card.ProviderId);
        var editor = new ProviderConnectionEditorWindow(
            new ProviderConnectionEditorViewModel(
                card.Definition,
                connection,
                service,
                _viewModel.AiCapacity.Registry))
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
        {
            if (card.IsCustom &&
                _viewModel.AiCapacity.Registry?.FindDefinition(card.ProviderId) is { } updatedDefinition)
            {
                _viewModel.AiCapacity.ReplaceCustomCard(updatedDefinition);
                card = _viewModel.AiCapacity.Cards.Single(candidate => candidate.ProviderId == updatedDefinition.Id);
            }

            card.SetConnection(card.BuiltInCode is { } builtInCode
                ? await service.GetAsync(builtInCode)
                : await service.GetAsync(card.ProviderId));
        }
    }

    private async Task OpenAddProviderAsync()
    {
        if (_viewModel.AiCapacity.ConnectionService is not { } service ||
            _viewModel.AiCapacity.Registry is not { } registry)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var definition = ProviderDefinition.Custom(
            Guid.NewGuid(),
            "New provider",
            displayLabel: null,
            ProviderAuthenticationMode.ExternalManual,
            ProviderCapacityMode.Manual,
            enabled: true,
            sortOrder: 0,
            now,
            now,
            "Custom registration; automatic capacity requires a future typed adapter.");
        var editor = new ProviderConnectionEditorWindow(
            new ProviderConnectionEditorViewModel(definition, null, service, registry))
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
        {
            _viewModel.AiCapacity.ReplaceCustomCard(editor.DataContext is ProviderConnectionEditorViewModel saved
                ? saved.Definition
                : definition);
        }
    }

    private async Task RemoveProviderAsync(ProviderCapacityCardViewModel card)
    {
        if (!card.IsCustom)
        {
            return;
        }

        var answer = System.Windows.MessageBox.Show(
            this,
            $"Remove the {card.DisplayName} provider registration? Stored credentials are not deleted by this action.",
            "Remove AI provider",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer == MessageBoxResult.Yes)
        {
            await _viewModel.AiCapacity.RemoveCustomProviderAsync(card);
        }
    }
}
