using System.Collections.ObjectModel;
using System.Windows.Input;
using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Domain.Providers;

namespace AIUsageMonitor.Desktop.ViewModels;

public sealed class ProviderCapacityCardViewModel : ObservableObject
{
    private readonly IAiUsageProvider? _provider;
    private readonly IProviderConnectionService? _connectionService;
    private readonly ObservableCollection<QuotaWindowViewModel> _quotaWindows = [];
    private ProviderConnection? _connection;
    private string _statusText = "Not Configured";
    private string _statusDetail = "No provider connection is configured.";
    private string? _accountDisplayName;
    private string? _subscriptionText;
    private DateTimeOffset? _lastSuccessfulRefresh;
    private bool _isRefreshing;
    private bool _isInitialized;
    private ICommand _editCommand;

    public ProviderCapacityCardViewModel(
        ProviderCode code,
        string displayName,
        IAiUsageProvider? provider = null,
        IProviderConnectionService? connectionService = null)
    {
        Code = code;
        DisplayName = displayName;
        _provider = provider;
        _connectionService = connectionService;
        RefreshCommand = new AsyncCommand(() => RefreshAsync(), () => CanRefresh);
        _editCommand = new AsyncCommand(() => EditAsync(), () => CanEditConnection && !IsRefreshing);
    }

    public ProviderCode Code { get; }

    public string DisplayName { get; }

    public ObservableCollection<QuotaWindowViewModel> QuotaWindows => _quotaWindows;

    public ICommand RefreshCommand { get; }

    public ICommand EditCommand => _editCommand;

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetProperty(ref _statusText, value))
            {
                OnPropertyChanged(nameof(IsManualOnly));
                OnPropertyChanged(nameof(CanRefresh));
            }
        }
    }

    public string StatusDetail
    {
        get => _statusDetail;
        private set => SetProperty(ref _statusDetail, value);
    }

    public string? AccountDisplayName
    {
        get => _accountDisplayName;
        private set => SetProperty(ref _accountDisplayName, value);
    }

    public string? SubscriptionText
    {
        get => _subscriptionText;
        private set => SetProperty(ref _subscriptionText, value);
    }

    public DateTimeOffset? LastSuccessfulRefresh
    {
        get => _lastSuccessfulRefresh;
        private set
        {
            if (SetProperty(ref _lastSuccessfulRefresh, value))
            {
                OnPropertyChanged(nameof(LastSuccessfulRefreshText));
            }
        }
    }

    public string LastSuccessfulRefreshText => LastSuccessfulRefresh is { } value
        ? $"Last successful refresh: {value.ToLocalTime():MMM d, h:mm tt}"
        : "Last successful refresh: not yet";

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
            {
                (RefreshCommand as AsyncCommand)?.NotifyCanExecuteChanged();
                (EditCommand as AsyncCommand)?.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanRefresh));
            }
        }
    }

    public bool IsManualOnly =>
        Code is ProviderCode.Codex or ProviderCode.Antigravity ||
        StatusText.Contains("Manual", StringComparison.OrdinalIgnoreCase) ||
        StatusText.Equals("Unsupported", StringComparison.OrdinalIgnoreCase);

    public bool CanRefresh => _provider is not null && !IsManualOnly && !IsRefreshing;

    public bool CanEditConnection => _connectionService is not null &&
        Code is ProviderCode.Copilot or ProviderCode.Claude or ProviderCode.Kimi;

    public bool HasCredentialSaved => !string.IsNullOrWhiteSpace(_connection?.CredentialReference);

    public string CredentialStateText => HasCredentialSaved ? "Credential saved" : "No credential saved";

    public bool IsInitialized => _isInitialized;

    internal ProviderConnection? Connection => _connection;

    internal IAiUsageProvider? Provider => _provider;

    internal void SetEditorCommand(ICommand command)
    {
        _editCommand = command ?? throw new ArgumentNullException(nameof(command));
        OnPropertyChanged(nameof(EditCommand));
    }

    public void SetConnection(ProviderConnection? connection)
    {
        _connection = connection;
        OnPropertyChanged(nameof(HasCredentialSaved));
        OnPropertyChanged(nameof(CredentialStateText));

        if (connection is not null)
        {
            LastSuccessfulRefresh ??= connection.LastSuccessfulSync;
            if (connection.CredentialReference is not null)
            {
                StatusText = "Configured — refresh to verify";
                StatusDetail = "The connection reference is saved securely. No secret was read back.";
            }
        }
    }

    internal void MarkInitialized()
    {
        _isInitialized = true;
        OnPropertyChanged(nameof(IsInitialized));
    }

    public void ApplyDetection(ProviderDetectionResult detection)
    {
        if (StatusText == "Configured — refresh to verify")
        {
            return;
        }

        if (detection.IsDetected)
        {
            StatusText = "Local Detected";
            StatusDetail = detection.DetectionMethod ?? "An official local provider executable was detected.";
        }
        else if (Code is ProviderCode.Codex or ProviderCode.Antigravity)
        {
            StatusText = "Unsupported / Manual";
            StatusDetail = detection.DetectionMethod ?? "Automatic consumer capacity is unavailable.";
        }
        else
        {
            StatusText = "Not Configured";
            StatusDetail = detection.DetectionMethod ?? "Configure a supported provider connection to refresh capacity.";
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var provider = _provider;
        if (!CanRefresh || provider is null)
        {
            return;
        }

        IsRefreshing = true;
        StatusText = "Refreshing";
        StatusDetail = "Refreshing this provider…";
        try
        {
            var result = await provider.RefreshAsync(cancellationToken).ConfigureAwait(true);
            ApplyResult(result);
            if (_connectionService is not null)
            {
                try
                {
                    await _connectionService.RecordRefreshAsync(result, cancellationToken).ConfigureAwait(true);
                }
                catch
                {
                    StatusDetail = $"{StatusDetail} Provider refreshed; local connection state could not be saved.";
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RestoreConfiguredState("Refresh cancelled.");
        }
        catch
        {
            StatusText = "Error";
            StatusDetail = "Provider refresh failed.";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    public void ApplyResult(ProviderRefreshResult result)
    {
        _quotaWindows.Clear();
        foreach (var quota in result.QuotaWindows)
        {
            _quotaWindows.Add(new QuotaWindowViewModel(quota));
        }

        AccountDisplayName = result.Account?.DisplayName;
        SubscriptionText = result.Subscription is null
            ? null
            : result.Subscription.PlanName ?? "Subscription details reported";

        if (result.Outcome is ProviderRefreshOutcome.Success or ProviderRefreshOutcome.Partial)
        {
            LastSuccessfulRefresh = result.CompletedAt;
        }

        (StatusText, StatusDetail) = result.Outcome switch
        {
            ProviderRefreshOutcome.Success => ("Connected", "Capacity refreshed successfully."),
            ProviderRefreshOutcome.Partial => ("Connected · Partial", result.ErrorMessage ?? "Usage was refreshed; some capacity fields are unavailable."),
            ProviderRefreshOutcome.AuthenticationRequired => ("Authentication Required", result.ErrorMessage ?? "Authentication is required."),
            ProviderRefreshOutcome.Unsupported => ("Unsupported / Manual", result.ErrorMessage ?? "Automatic capacity is unavailable for this provider."),
            ProviderRefreshOutcome.Stale => ("Stale", result.ErrorMessage ?? "Showing stale data from the last successful refresh."),
            _ => ("Error", result.ErrorMessage ?? "Provider refresh failed.")
        };
    }

    private void RestoreConfiguredState(string message)
    {
        if (HasCredentialSaved)
        {
            StatusText = "Configured — refresh to verify";
        }
        StatusDetail = message;
    }

    private Task EditAsync() => Task.CompletedTask;
}
