using System.Collections.ObjectModel;
using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Domain.Providers;
using AIUsageMonitor.Providers.Common;

namespace AIUsageMonitor.Desktop.ViewModels;

public sealed class AiCapacityViewModel : ObservableObject
{
    private readonly IProviderConnectionService? _connectionService;
    private readonly IExecutableLocator? _executableLocator;
    private readonly bool _isDegraded;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private string _refreshStateText = "Ready to refresh";
    private DateTimeOffset? _lastRefresh;
    private bool _isRefreshing;

    public AiCapacityViewModel()
        : this(new SystemExecutableLocator())
    {
    }

    public AiCapacityViewModel(IExecutableLocator executableLocator)
    {
        _executableLocator = executableLocator ?? throw new ArgumentNullException(nameof(executableLocator));
        _isDegraded = true;
        Cards = new ObservableCollection<ProviderCapacityCardViewModel>(CreateDegradedCards());
        RefreshAllCommand = new AsyncCommand(() => RefreshAllAsync(), () => !_isDegraded && !IsRefreshing && Cards.Count > 0);
    }

    public AiCapacityViewModel(
        IProviderRegistry registry,
        IProviderConnectionService connectionService)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        Cards = new ObservableCollection<ProviderCapacityCardViewModel>(
            registry.GetAll().OrderBy(provider => provider.Code)
                .Select(provider => new ProviderCapacityCardViewModel(
                    provider.Code,
                    DisplayNameFor(provider.Code),
                    provider,
                    connectionService)));
        RefreshAllCommand = new AsyncCommand(() => RefreshAllAsync(), () => !IsRefreshing && Cards.Count > 0);
    }

    public ObservableCollection<ProviderCapacityCardViewModel> Cards { get; }

    internal IProviderConnectionService? ConnectionService => _connectionService;

    public bool IsDegraded => _isDegraded;

    public AsyncCommand RefreshAllCommand { get; }

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
        ? $"Last refresh: {value.ToLocalTime():MMM d, h:mm tt}"
        : "Last refresh: not yet";

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
            {
                RefreshAllCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_connectionService is null)
        {
            return;
        }

        try
        {
            _ = await _connectionService.LoadAllAsync(cancellationToken).ConfigureAwait(true);
            foreach (var card in Cards)
            {
                var connection = await _connectionService.GetAsync(card.Code, cancellationToken).ConfigureAwait(true);
                card.SetConnection(connection);
            }

            await Task.WhenAll(Cards.Select(async card =>
            {
                var actual = card;
                var providerAdapter = GetProvider(actual);
                if (providerAdapter is not null)
                {
                    try
                    {
                        actual.ApplyDetection(await providerAdapter.DetectAsync(cancellationToken).ConfigureAwait(true));
                    }
                    catch
                    {
                        actual.ApplyResult(ProviderRefreshResult.Failed(
                            actual.Code,
                            "detection_failed",
                            "Provider detection failed.",
                            DateTimeOffset.UtcNow));
                    }
                }

                actual.MarkInitialized();
            })).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            RefreshStateText = "Some saved connection state was unavailable; showing safe defaults.";
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
            var executableName = ExecutableNameFor(card.Code);
            var detected = executableName is not null && _executableLocator!.Find(executableName) is not null;
            var detail = detected
                ? $"Official {executableName} CLI detected locally; configured capacity requires persistence."
                : card.Code is ProviderCode.Codex or ProviderCode.Antigravity
                    ? "No documented machine-readable capacity surface detected; local persistence is unavailable."
                    : "Local persistence is unavailable; configured provider state cannot be reconstructed.";

            card.ApplyDetection(new ProviderDetectionResult(
                card.Code,
                detected,
                detail,
                DateTimeOffset.UtcNow));
            card.MarkInitialized();
        }

        return Task.CompletedTask;
    }

    public void SetEditorLauncher(Func<ProviderCapacityCardViewModel, Task> launcher)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        foreach (var card in Cards)
        {
            if (card.CanEditConnection)
            {
                var command = new AsyncCommand(() => launcher(card), () => !card.IsRefreshing);
                card.SetEditorCommand(command);
            }
        }
    }

    public async Task RefreshAllAsync(CancellationToken cancellationToken = default)
    {
        if (_isDegraded)
        {
            RefreshStateText = "Refresh unavailable while local persistence is degraded.";
            return;
        }

        if (!await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(true))
        {
            return;
        }

        IsRefreshing = true;
        RefreshStateText = "Refreshing all providers…";
        try
        {
            var refreshableCards = Cards.Where(static card => card.CanRefresh).ToArray();
            await Task.WhenAll(refreshableCards.Select(card => card.RefreshAsync(cancellationToken))).ConfigureAwait(true);
            LastRefresh = DateTimeOffset.UtcNow;
            RefreshStateText = refreshableCards.Length == 0
                ? "No automatic capacity refresh is available; manual providers were left unchanged."
                : "Refresh complete; each supported provider is shown independently.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RefreshStateText = "Refresh cancelled.";
        }
        finally
        {
            IsRefreshing = false;
            _refreshGate.Release();
        }
    }

    private static IAiUsageProvider? GetProvider(ProviderCapacityCardViewModel card) => card.Provider;

    private static IEnumerable<ProviderCapacityCardViewModel> CreateDegradedCards() =>
        Enum.GetValues<ProviderCode>().OrderBy(code => code)
            .Select(code => new ProviderCapacityCardViewModel(code, DisplayNameFor(code)));

    private static string? ExecutableNameFor(ProviderCode code) => code switch
    {
        ProviderCode.Codex => "codex",
        ProviderCode.Claude => "claude",
        ProviderCode.Kimi => "kimi",
        ProviderCode.Antigravity => "agy",
        ProviderCode.Copilot => null,
        _ => null
    };

    public static string DisplayNameFor(ProviderCode code) => code switch
    {
        ProviderCode.Codex => "Codex",
        ProviderCode.Claude => "Claude / Anthropic",
        ProviderCode.Kimi => "Kimi",
        ProviderCode.Copilot => "GitHub Copilot",
        ProviderCode.Antigravity => "Antigravity",
        _ => code.ToString()
    };
}
