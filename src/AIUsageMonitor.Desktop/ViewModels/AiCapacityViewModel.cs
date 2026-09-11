using System.Collections.ObjectModel;
using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Domain.Providers;
using AIUsageMonitor.Providers.Common;

namespace AIUsageMonitor.Desktop.ViewModels;

public sealed class AiCapacityViewModel : ObservableObject
{
    private readonly IProviderRegistry? _registry;
    private readonly IProviderConnectionService? _connectionService;
    private readonly IExecutableLocator? _executableLocator;
    private readonly bool _isDegraded;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private Func<ProviderCapacityCardViewModel, Task>? _editorLauncher;
    private Func<Task>? _addProviderLauncher;
    private Func<ProviderCapacityCardViewModel, Task>? _removeProviderLauncher;
    private string _refreshStateText = "Ready";
    private DateTimeOffset? _lastRefresh;
    private bool _isRefreshing;
    private bool _isDetectingSessions;

    public AiCapacityViewModel()
        : this(new SystemExecutableLocator())
    {
    }

    public AiCapacityViewModel(IExecutableLocator executableLocator)
    {
        _executableLocator = executableLocator ?? throw new ArgumentNullException(nameof(executableLocator));
        _isDegraded = true;
        Cards = new ObservableCollection<ProviderCapacityCardViewModel>(
            CreateDefaultDefinitions().Select(definition => new ProviderCapacityCardViewModel(definition)));
        CreateCommands();
    }

    public AiCapacityViewModel(
        IProviderRegistry registry,
        IProviderConnectionService connectionService)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        Cards = new ObservableCollection<ProviderCapacityCardViewModel>(
            registry.GetDefinitions().Select(CreateCard));
        CreateCommands();
    }

    public ObservableCollection<ProviderCapacityCardViewModel> Cards { get; }

    internal IProviderConnectionService? ConnectionService => _connectionService;

    internal IProviderRegistry? Registry => _registry;

    public bool IsDegraded => _isDegraded;

    public AsyncCommand DetectSessionsCommand { get; private set; } = null!;

    public AsyncCommand RefreshCapacityCommand { get; private set; } = null!;

    // Compatibility alias for the former page-level action. It now refreshes only registered
    // typed capacity adapters and never claims that manual providers were refreshed.
    public AsyncCommand RefreshAllCommand => RefreshCapacityCommand;

    public AsyncCommand AddProviderCommand { get; private set; } = null!;

    public string RefreshStateText
    {
        get => _refreshStateText;
        private set => SetProperty(ref _refreshStateText, value);
    }

    public DateTimeOffset? LastRefresh
    {
        get => _lastRefresh;
        private set
        {
            if (SetProperty(ref _lastRefresh, value))
            {
                OnPropertyChanged(nameof(LastRefreshText));
            }
        }
    }

    public string LastRefreshText => LastRefresh is { } value
        ? $"Last capacity refresh: {value.ToLocalTime():MMM d, h:mm tt}"
        : "Capacity not refreshed";

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
            {
                DetectSessionsCommand.NotifyCanExecuteChanged();
                RefreshCapacityCommand.NotifyCanExecuteChanged();
                AddProviderCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsDetectingSessions
    {
        get => _isDetectingSessions;
        private set
        {
            if (SetProperty(ref _isDetectingSessions, value))
            {
                DetectSessionsCommand.NotifyCanExecuteChanged();
                RefreshCapacityCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_connectionService is null || _registry is null)
        {
            return;
        }

        try
        {
            await _registry.InitializeAsync(cancellationToken).ConfigureAwait(true);
            SyncDefinitions();
            _ = await _connectionService.LoadAllAsync(cancellationToken).ConfigureAwait(true);
            foreach (var card in Cards)
            {
                var connection = card.BuiltInCode is { } code
                    ? await _connectionService.GetAsync(code, cancellationToken).ConfigureAwait(true)
                    : await _connectionService.GetAsync(card.ProviderId, cancellationToken).ConfigureAwait(true);
                card.SetConnection(connection);
            }

            await DetectSessionsAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            RefreshStateText = "Some saved provider state was unavailable; showing safe defaults.";
        }
    }

    public Task InitializeDegradedAsync(CancellationToken cancellationToken = default)
    {
        if (!_isDegraded)
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        foreach (var card in Cards)
        {
            var executableName = ExecutableNameFor(card.BuiltInCode);
            var detected = executableName is not null && _executableLocator!.Find(executableName) is not null;
            var message = detected
                ? $"Local {card.DisplayName} tool detected — authentication not machine-verifiable."
                : card.BuiltInCode == ProviderCode.Antigravity
                    ? "External/manual provider state; capacity is unavailable."
                    : "Local tool was not detected.";

            card.ApplyDetection(new ProviderDetectionResult(
                card.BuiltInCode ?? ProviderCode.Codex,
                detected,
                ProviderAuthenticationState.Unknown,
                message,
                DateTimeOffset.UtcNow));
            card.MarkInitialized();
        }

        return Task.CompletedTask;
    }

    public void SetEditorLauncher(Func<ProviderCapacityCardViewModel, Task> launcher)
    {
        _editorLauncher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        foreach (var card in Cards)
        {
            AttachEditor(card);
        }
    }

    public void SetAddProviderLauncher(Func<Task> launcher) =>
        _addProviderLauncher = launcher ?? throw new ArgumentNullException(nameof(launcher));

    public void SetRemoveProviderLauncher(Func<ProviderCapacityCardViewModel, Task> launcher)
    {
        _removeProviderLauncher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        foreach (var card in Cards)
        {
            AttachRemove(card);
        }
    }

    public async Task DetectSessionsAsync(CancellationToken cancellationToken = default)
    {
        if (_isDegraded || !await _sessionGate.WaitAsync(0, cancellationToken).ConfigureAwait(true))
        {
            return;
        }

        IsDetectingSessions = true;
        RefreshStateText = "Detecting supported local sessions…";
        try
        {
            var detectable = Cards.Where(static card => card.CanCheckSession).ToArray();
            await Task.WhenAll(detectable.Select(async card =>
            {
                var provider = card.Provider;
                if (provider is null)
                {
                    return;
                }

                try
                {
                    card.ApplyDetection(await provider.DetectAsync(cancellationToken).ConfigureAwait(true));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    card.ApplyDetection(new ProviderDetectionResult(
                        card.BuiltInCode!.Value,
                        true,
                        ProviderAuthenticationState.Unknown,
                        "Unable to verify session.",
                        DateTimeOffset.UtcNow));
                }
            })).ConfigureAwait(true);
            RefreshStateText = detectable.Length == 0
                ? "No registered local-session detectors are available."
                : "Session detection complete; authentication and capacity remain separate.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RefreshStateText = "Session detection cancelled.";
        }
        finally
        {
            IsDetectingSessions = false;
            _sessionGate.Release();
        }
    }

    public async Task RefreshCapacityAsync(CancellationToken cancellationToken = default)
    {
        if (_isDegraded || !await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(true))
        {
            return;
        }

        IsRefreshing = true;
        RefreshStateText = "Refreshing supported capacity…";
        try
        {
            var refreshable = Cards.Where(static card => card.CanRefresh).ToArray();
            await Task.WhenAll(refreshable.Select(card => card.RefreshAsync(cancellationToken))).ConfigureAwait(true);
            LastRefresh = DateTimeOffset.UtcNow;
            RefreshStateText = refreshable.Length == 0
                ? "No automatic capacity refresh is available; manual providers were left unchanged."
                : "Supported capacity refresh complete.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RefreshStateText = "Capacity refresh cancelled.";
        }
        finally
        {
            IsRefreshing = false;
            _refreshGate.Release();
        }
    }

    public Task AddProviderAsync() => _addProviderLauncher?.Invoke() ?? Task.CompletedTask;

    // Compatibility alias for callers of the former fixed-provider page API.
    public Task RefreshAllAsync(CancellationToken cancellationToken = default) =>
        RefreshCapacityAsync(cancellationToken);

    public async Task RemoveCustomProviderAsync(ProviderCapacityCardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (!card.IsCustom || _registry is null)
        {
            return;
        }

        await _registry.RemoveCustomAsync(card.ProviderId).ConfigureAwait(true);
        Cards.Remove(card);
    }

    internal void ReplaceCustomCard(ProviderDefinition definition)
    {
        var existing = Cards.FirstOrDefault(card => card.ProviderId == definition.Id);
        if (existing is not null)
        {
            var index = Cards.IndexOf(existing);
            Cards[index] = CreateCard(definition);
            AttachEditor(Cards[index]);
            AttachRemove(Cards[index]);
            return;
        }

        Cards.Add(CreateCard(definition));
        SyncCardOrder();
        AttachEditor(Cards.Single(card => card.ProviderId == definition.Id));
        AttachRemove(Cards.Single(card => card.ProviderId == definition.Id));
    }

    private ProviderCapacityCardViewModel CreateCard(ProviderDefinition definition)
    {
        var provider = definition.BuiltInCode is { } code ? _registry?.Find(code) : null;
        return new ProviderCapacityCardViewModel(definition, provider, _connectionService);
    }

    private void SyncDefinitions()
    {
        if (_registry is null)
        {
            return;
        }

        var definitions = _registry.GetDefinitions();
        var definitionIds = definitions.Select(definition => definition.Id).ToHashSet();
        foreach (var card in Cards.Where(card => !definitionIds.Contains(card.ProviderId)).ToArray())
        {
            Cards.Remove(card);
        }

        foreach (var definition in definitions)
        {
            var existing = Cards.FirstOrDefault(card => card.ProviderId == definition.Id);
            if (existing is null)
            {
                Cards.Add(CreateCard(definition));
                existing = Cards[^1];
            }

            AttachEditor(existing);
            AttachRemove(existing);
        }

        SyncCardOrder();
    }

    private void SyncCardOrder()
    {
        var ordered = Cards
            .OrderBy(card => card.Kind == ProviderKind.Custom ? 1 : 0)
            .ThenBy(card => card.Definition.SortOrder)
            .ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            if (Cards[index] != ordered[index])
            {
                Cards.Move(Cards.IndexOf(ordered[index]), index);
            }
        }
    }

    private void AttachEditor(ProviderCapacityCardViewModel card)
    {
        if (_editorLauncher is not null && card.CanConfigure)
        {
            card.SetEditorCommand(new AsyncCommand(
                () => _editorLauncher(card),
                () => !card.IsRefreshing));
        }
    }

    private void AttachRemove(ProviderCapacityCardViewModel card)
    {
        if (_removeProviderLauncher is not null && card.CanRemove)
        {
            card.SetRemoveCommand(new AsyncCommand(
                () => _removeProviderLauncher(card),
                () => !card.IsRefreshing));
        }
    }

    private void CreateCommands()
    {
        DetectSessionsCommand = new AsyncCommand(() => DetectSessionsAsync(), () =>
            !_isDegraded && !IsRefreshing && !IsDetectingSessions);
        RefreshCapacityCommand = new AsyncCommand(() => RefreshCapacityAsync(), () =>
            !_isDegraded && !IsRefreshing && !IsDetectingSessions && Cards.Any(static card => card.CanRefresh));
        AddProviderCommand = new AsyncCommand(() => AddProviderAsync(), () => !_isDegraded && !IsRefreshing);
    }

    private static IEnumerable<ProviderDefinition> CreateDefaultDefinitions()
    {
        var now = DateTimeOffset.UnixEpoch;
        return
        [
            ProviderDefinition.BuiltIn(
                Guid.Parse("1cf3c94e-9bcb-4fe4-9b2c-24a0b4f3a901"),
                ProviderCode.Codex,
                "Codex",
                ProviderAuthenticationMode.LocalSession,
                ProviderCapacityMode.Manual,
                ProviderCapabilities.SupportsLocalSessionDetection |
                    ProviderCapabilities.SupportsApiKey |
                    ProviderCapabilities.SupportsConfiguration,
                0,
                now,
                "Existing local Codex session first; API key is optional."),
            ProviderDefinition.BuiltIn(
                Guid.Parse("2d6f54fa-2c0e-4cc5-8bf6-6debf48b3f02"),
                ProviderCode.Claude,
                "Claude",
                ProviderAuthenticationMode.LocalSession,
                ProviderCapacityMode.Manual,
                ProviderCapabilities.SupportsLocalSessionDetection |
                    ProviderCapabilities.SupportsApiKey |
                    ProviderCapabilities.SupportsCapacityRefresh |
                    ProviderCapabilities.SupportsConfiguration,
                1,
                now,
                "Existing local Claude session first; API key is optional."),
            ProviderDefinition.BuiltIn(
                Guid.Parse("5b544ceb-0ac4-43b6-8c9e-9e27c9f0c505"),
                ProviderCode.Antigravity,
                "Antigravity",
                ProviderAuthenticationMode.ExternalManual,
                ProviderCapacityMode.Manual,
                ProviderCapabilities.None,
                2,
                now,
                "External/manual provider state; capacity remains unavailable.")
        ];
    }

    private static string? ExecutableNameFor(ProviderCode? code) => code switch
    {
        ProviderCode.Codex => "codex",
        ProviderCode.Claude => "claude",
        ProviderCode.Antigravity => "agy",
        _ => null
    };

    public static string DisplayNameFor(ProviderCode code) => code switch
    {
        ProviderCode.Codex => "Codex",
        ProviderCode.Claude => "Claude",
        ProviderCode.Kimi => "Kimi",
        ProviderCode.Copilot => "GitHub Copilot",
        ProviderCode.Antigravity => "Antigravity",
        _ => code.ToString()
    };
}
