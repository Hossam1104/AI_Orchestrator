using System.Collections.ObjectModel;
using System.Windows.Input;
using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Domain.Providers;

namespace AIUsageMonitor.Desktop.ViewModels;

/// <summary>
/// Reusable provider card state. Availability, authentication, and capacity are separate
/// properties so the view never has to infer one from another.
/// </summary>
public sealed class ProviderCapacityCardViewModel : ObservableObject
{
    private readonly IAiUsageProvider? _provider;
    private readonly IProviderConnectionService? _connectionService;
    private readonly ObservableCollection<QuotaWindowViewModel> _quotaWindows = [];
    private ProviderConnection? _connection;
    private ProviderAvailability _availability = ProviderAvailability.Unknown;
    private ProviderAuthenticationState _authenticationState = ProviderAuthenticationState.Unknown;
    private ProviderCapacityState _capacityState;
    private ProviderAuthenticationMode _authenticationMode;
    private ProviderCapacityMode _capacityMode;
    private string _statusText = "Unknown";
    private string _statusDetail = "Provider state has not been checked.";
    private string? _accountDisplayName;
    private string? _subscriptionText;
    private DateTimeOffset? _lastSuccessfulRefresh;
    private bool _isRefreshing;
    private bool _isCheckingSession;
    private bool _isInitialized;
    private ICommand _editCommand;
    private ICommand _removeCommand = new RelayCommand(() => { });

    public ProviderCapacityCardViewModel(
        ProviderDefinition definition,
        IAiUsageProvider? provider = null,
        IProviderConnectionService? connectionService = null)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _provider = provider;
        _connectionService = connectionService;
        _authenticationMode = definition.AuthenticationMode;
        _capacityMode = definition.CapacityMode;
        _capacityState = CapacityStateFor(_capacityMode);
        _editCommand = new AsyncCommand(() => EditAsync(), () => CanConfigure && !IsRefreshing);
        SessionCommand = new AsyncCommand(() => CheckSessionAsync(), () => CanCheckSession);
        RefreshCommand = new AsyncCommand(() => RefreshAsync(), () => CanRefresh);
        InitializeText();
    }

    // Compatibility constructor for existing view-model consumers and focused tests.
    public ProviderCapacityCardViewModel(
        ProviderCode code,
        string displayName,
        IAiUsageProvider? provider = null,
        IProviderConnectionService? connectionService = null)
        : this(CompatibilityDefinition(code, displayName), provider, connectionService)
    {
    }

    public ProviderDefinition Definition { get; }

    public Guid ProviderId => Definition.Id;

    public ProviderCode? Code => Definition.BuiltInCode;

    public ProviderCode? BuiltInCode => Definition.BuiltInCode;

    public string DisplayName => Definition.EffectiveDisplayName;

    public ProviderKind Kind => Definition.Kind;

    public bool IsCustom => Definition.Kind == ProviderKind.Custom;

    public bool IsEnabled => Definition.Enabled;

    public ObservableCollection<QuotaWindowViewModel> QuotaWindows => _quotaWindows;

    public ICommand RefreshCommand { get; }

    public ICommand SessionCommand { get; }

    public ICommand EditCommand => _editCommand;

    public ICommand RemoveCommand => _removeCommand;

    public ProviderAvailability Availability
    {
        get => _availability;
        private set
        {
            if (SetProperty(ref _availability, value))
            {
                OnPropertyChanged(nameof(AvailabilityText));
            }
        }
    }

    public ProviderAuthenticationState AuthenticationState
    {
        get => _authenticationState;
        private set
        {
            if (SetProperty(ref _authenticationState, value))
            {
                OnPropertyChanged(nameof(AuthenticationText));
            }
        }
    }

    public ProviderCapacityState CapacityState
    {
        get => _capacityState;
        private set
        {
            if (SetProperty(ref _capacityState, value))
            {
                OnPropertyChanged(nameof(CapacityText));
                OnPropertyChanged(nameof(IsManualOnly));
                OnPropertyChanged(nameof(CanRefresh));
                (RefreshCommand as AsyncCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    public ProviderAuthenticationMode AuthenticationMode
    {
        get => _authenticationMode;
        private set
        {
            if (SetProperty(ref _authenticationMode, value))
            {
                OnPropertyChanged(nameof(AuthenticationText));
                OnPropertyChanged(nameof(CredentialStateText));
                OnPropertyChanged(nameof(CanCheckSession));
                OnPropertyChanged(nameof(CanRefresh));
                (RefreshCommand as AsyncCommand)?.NotifyCanExecuteChanged();
                (SessionCommand as AsyncCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    public string AvailabilityText => Availability switch
    {
        _ when !IsEnabled => "Disabled",
        ProviderAvailability.Detected => "Local tool detected",
        ProviderAvailability.NotDetected => "Local tool not detected",
        _ => "Availability not checked"
    };

    public string AuthenticationText => !IsEnabled
        ? "Disabled"
        : AuthenticationState switch
    {
        ProviderAuthenticationState.AuthenticatedLocalSession => "Connected via local session",
        ProviderAuthenticationState.ApiKeyConfigured => "API key configured",
        ProviderAuthenticationState.AuthenticationRequired => AuthenticationMode == ProviderAuthenticationMode.LocalSession
            ? "Authentication required"
            : "API key required",
        ProviderAuthenticationState.ExternalAuthentication => "External session — not machine-verifiable",
        ProviderAuthenticationState.NotConfigured => "Not configured",
        _ when AuthenticationMode == ProviderAuthenticationMode.LocalSession => "Local session not checked",
        _ => "Authentication unknown"
    };

    public string CapacityText => !IsEnabled
        ? "Disabled"
        : CapacityState switch
    {
        ProviderCapacityState.Available => "Available",
        ProviderCapacityState.AuthenticationRequired => "Authentication required",
        ProviderCapacityState.RefreshFailed => "Refresh failed",
        ProviderCapacityState.Unsupported => "Unavailable",
        ProviderCapacityState.Unavailable => "Unavailable",
        ProviderCapacityState.Manual => "Manual",
        _ => "Capacity not checked"
    };

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

    // Retained as a concise compatibility/status-pill value; the UI binds to the three explicit
    // state properties above instead of using this aggregate value to infer authentication.
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
                (RemoveCommand as AsyncCommand)?.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanRefresh));
                OnPropertyChanged(nameof(CanCheckSession));
                (SessionCommand as AsyncCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsManualOnly =>
        !IsEnabled ||
        CapacityState is ProviderCapacityState.Manual or ProviderCapacityState.Unavailable or ProviderCapacityState.Unsupported ||
        !Definition.HasCapability(ProviderCapabilities.SupportsCapacityRefresh);

    public bool CanRefresh =>
        IsEnabled &&
        _provider is not null &&
        Definition.HasCapability(ProviderCapabilities.SupportsCapacityRefresh) &&
        !IsRefreshing &&
        CapacityModeAllowsRefresh &&
        (AuthenticationMode != ProviderAuthenticationMode.ApiKey ||
         HasCredentialSaved ||
         _connectionService is null);

    public bool CanCheckSession =>
        IsEnabled &&
        Definition.HasCapability(ProviderCapabilities.SupportsLocalSessionDetection) &&
        AuthenticationMode == ProviderAuthenticationMode.LocalSession &&
        !IsRefreshing &&
        !_isCheckingSession;

    public bool CanConfigure =>
        _connectionService is not null &&
        Definition.HasCapability(ProviderCapabilities.SupportsConfiguration);

    // Compatibility alias for the former connection-editor binding.
    public bool CanEditConnection => CanConfigure;

    public bool CanRemove => IsCustom && !IsRefreshing;

    public bool HasCredentialSaved => !string.IsNullOrWhiteSpace(_connection?.CredentialReference);

    public string CredentialStateText => AuthenticationMode == ProviderAuthenticationMode.ApiKey
        ? HasCredentialSaved ? "API key configured securely" : "No API key configured"
        : "No APO-managed credential required";

    public bool IsInitialized => _isInitialized;

    internal ProviderConnection? Connection => _connection;

    internal IAiUsageProvider? Provider => _provider;

    internal void SetEditorCommand(ICommand command)
    {
        _editCommand = command ?? throw new ArgumentNullException(nameof(command));
        OnPropertyChanged(nameof(EditCommand));
    }

    internal void SetRemoveCommand(ICommand command)
    {
        _removeCommand = command ?? throw new ArgumentNullException(nameof(command));
        OnPropertyChanged(nameof(RemoveCommand));
    }

    public void SetConnection(ProviderConnection? connection)
    {
        _connection = connection;
        OnPropertyChanged(nameof(HasCredentialSaved));
        OnPropertyChanged(nameof(CredentialStateText));

        if (connection is not null)
        {
            LastSuccessfulRefresh ??= connection.LastSuccessfulSync;
            AuthenticationMode = AuthenticationModeFor(connection.ConnectionType, AuthenticationMode);
            if (AuthenticationMode == ProviderAuthenticationMode.ApiKey)
            {
                AuthenticationState = HasCredentialSaved
                    ? ProviderAuthenticationState.ApiKeyConfigured
                    : ProviderAuthenticationState.AuthenticationRequired;
                CapacityState = _capacityMode == ProviderCapacityMode.Automatic
                    ? ProviderCapacityState.Unknown
                    : CapacityStateFor(_capacityMode);
                StatusDetail = "API key mode is explicit; local session state is not used as a fallback.";
            }
        }

        OnPropertyChanged(nameof(CanRefresh));
        (RefreshCommand as AsyncCommand)?.NotifyCanExecuteChanged();
    }

    internal void MarkInitialized()
    {
        _isInitialized = true;
        OnPropertyChanged(nameof(IsInitialized));
    }

    public void ApplyDetection(ProviderDetectionResult detection)
    {
        ArgumentNullException.ThrowIfNull(detection);
        if (Code != detection.Code)
        {
            return;
        }

        Availability = detection.IsDetected
            ? ProviderAvailability.Detected
            : ProviderAvailability.NotDetected;
        if (AuthenticationMode == ProviderAuthenticationMode.LocalSession)
        {
            AuthenticationState = detection.AuthenticationState;
            StatusDetail = detection.DetectionMethod ?? "Local session detection completed.";
            StatusText = detection.AuthenticationState == ProviderAuthenticationState.AuthenticatedLocalSession
                ? "Connected"
                : detection.AuthenticationState == ProviderAuthenticationState.AuthenticationRequired
                    ? "Authentication Required"
                    : detection.IsDetected ? "Local Detected" : "Not Detected";
        }
        else if (AuthenticationMode == ProviderAuthenticationMode.ExternalManual)
        {
            AuthenticationState = ProviderAuthenticationState.ExternalAuthentication;
            StatusDetail = detection.DetectionMethod ?? "External authentication is not machine-verifiable.";
            StatusText = detection.IsDetected ? "Detected" : "Not Detected";
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
        StatusDetail = "Refreshing capacity for this provider…";
        try
        {
            var result = await provider.RefreshAsync(cancellationToken).ConfigureAwait(true);
            ApplyResult(result);
            if (_connectionService is not null && BuiltInCode is { } code)
            {
                try
                {
                    await _connectionService.RecordRefreshAsync(result, cancellationToken).ConfigureAwait(true);
                }
                catch
                {
                    StatusDetail = $"{StatusDetail} Capacity refreshed; local connection state could not be saved.";
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RestoreConfiguredState("Capacity refresh cancelled.");
        }
        catch
        {
            CapacityState = ProviderCapacityState.RefreshFailed;
            StatusText = "Error";
            StatusDetail = "Provider capacity refresh failed; authentication state was not changed.";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    public void ApplyResult(ProviderRefreshResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (BuiltInCode != result.Code)
        {
            return;
        }

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

        (CapacityState, StatusText, StatusDetail) = result.Outcome switch
        {
            ProviderRefreshOutcome.Success => (ProviderCapacityState.Available, "Connected", "Capacity refreshed successfully."),
            ProviderRefreshOutcome.Partial => (ProviderCapacityState.Available, "Connected · Partial", result.ErrorMessage ?? "Usage was refreshed; some capacity fields are unavailable."),
            ProviderRefreshOutcome.AuthenticationRequired => (ProviderCapacityState.AuthenticationRequired, "Authentication Required", result.ErrorMessage ?? "Authentication is required for capacity."),
            ProviderRefreshOutcome.Unsupported => (ProviderCapacityState.Manual, "Manual", result.ErrorMessage ?? "Automatic capacity is unavailable for this provider."),
            ProviderRefreshOutcome.Stale => (ProviderCapacityState.RefreshFailed, "Stale", result.ErrorMessage ?? "Showing stale data from the last successful refresh."),
            _ => (ProviderCapacityState.RefreshFailed, "Error", result.ErrorMessage ?? "Provider capacity refresh failed.")
        };

        if (result.Outcome == ProviderRefreshOutcome.AuthenticationRequired &&
            AuthenticationMode == ProviderAuthenticationMode.ApiKey)
        {
            AuthenticationState = ProviderAuthenticationState.AuthenticationRequired;
        }
    }

    private void InitializeText()
    {
        if (!IsEnabled)
        {
            AuthenticationState = ProviderAuthenticationState.Unknown;
            Availability = ProviderAvailability.Unknown;
            StatusText = "Disabled";
            StatusDetail = "This provider registration is disabled.";
            return;
        }

        if (AuthenticationMode == ProviderAuthenticationMode.ExternalManual)
        {
            AuthenticationState = ProviderAuthenticationState.ExternalAuthentication;
        }
        else if (AuthenticationMode == ProviderAuthenticationMode.ApiKey)
        {
            AuthenticationState = ProviderAuthenticationState.NotConfigured;
        }
        else
        {
            AuthenticationState = ProviderAuthenticationState.Unknown;
        }

        Availability = ProviderAvailability.Unknown;
        StatusText = CapacityText;
        StatusDetail = Definition.Description ?? "Provider state has not been checked.";
    }

    private bool CapacityModeAllowsRefresh =>
        _capacityMode == ProviderCapacityMode.Automatic ||
        (Definition.HasCapability(ProviderCapabilities.SupportsCapacityRefresh) &&
         AuthenticationMode == ProviderAuthenticationMode.ApiKey);

    private void RestoreConfiguredState(string message)
    {
        StatusText = CapacityText;
        StatusDetail = message;
    }

    private Task EditAsync() => Task.CompletedTask;

    private async Task CheckSessionAsync()
    {
        if (!CanCheckSession || _provider is null || BuiltInCode is not { } code)
        {
            return;
        }

        _isCheckingSession = true;
        OnPropertyChanged(nameof(CanCheckSession));
        (SessionCommand as AsyncCommand)?.NotifyCanExecuteChanged();
        try
        {
            ApplyDetection(await _provider.DetectAsync().ConfigureAwait(true));
        }
        catch
        {
            ApplyDetection(new ProviderDetectionResult(
                code,
                true,
                ProviderAuthenticationState.Unknown,
                "Unable to verify session.",
                DateTimeOffset.UtcNow));
        }
        finally
        {
            _isCheckingSession = false;
            OnPropertyChanged(nameof(CanCheckSession));
            (SessionCommand as AsyncCommand)?.NotifyCanExecuteChanged();
        }
    }

    private static ProviderAuthenticationMode AuthenticationModeFor(
        ProviderConnectionType connectionType,
        ProviderAuthenticationMode fallback) => connectionType switch
        {
            ProviderConnectionType.LocalSession => ProviderAuthenticationMode.LocalSession,
            ProviderConnectionType.ApiKey or ProviderConnectionType.OfficialApi => ProviderAuthenticationMode.ApiKey,
            ProviderConnectionType.ExternalManual or ProviderConnectionType.Manual => ProviderAuthenticationMode.ExternalManual,
            _ => fallback
        };

    private static ProviderCapacityState CapacityStateFor(ProviderCapacityMode mode) => mode switch
    {
        ProviderCapacityMode.Manual => ProviderCapacityState.Manual,
        ProviderCapacityMode.Automatic => ProviderCapacityState.Unknown,
        ProviderCapacityMode.Unavailable => ProviderCapacityState.Unavailable,
        _ => ProviderCapacityState.Unknown
    };

    private static ProviderDefinition CompatibilityDefinition(ProviderCode code, string displayName)
    {
        var supportsRefresh = code is not (ProviderCode.Codex or ProviderCode.Antigravity);
        return ProviderDefinition.BuiltIn(
            Guid.NewGuid(),
            code,
            displayName,
            code is ProviderCode.Codex or ProviderCode.Claude
                ? ProviderAuthenticationMode.LocalSession
                : ProviderAuthenticationMode.ApiKey,
            supportsRefresh ? ProviderCapacityMode.Automatic : ProviderCapacityMode.Manual,
            supportsRefresh
                ? ProviderCapabilities.SupportsApiKey | ProviderCapabilities.SupportsCapacityRefresh
                : ProviderCapabilities.None,
            (int)code);
    }
}
