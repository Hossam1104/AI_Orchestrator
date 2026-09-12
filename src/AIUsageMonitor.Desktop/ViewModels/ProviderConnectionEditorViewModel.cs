using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Domain.Providers;
using AIUsageMonitor.Providers.Copilot;

namespace AIUsageMonitor.Desktop.ViewModels;

public sealed class ProviderConnectionEditorViewModel : ObservableObject
{
    private readonly IProviderConnectionService _service;
    private readonly IProviderRegistry? _registry;
    private ProviderDefinition _definition;
    private ProviderConnection? _connection;
    private ProviderAuthenticationMode _authenticationMode;
    private ProviderCapacityMode _capacityMode;
    private CopilotBillingScope _copilotScope = CopilotBillingScope.PersonalUser;
    private string _providerName = string.Empty;
    private string _displayLabel = string.Empty;
    private string _description = string.Empty;
    private bool _enabled;
    private string _username = string.Empty;
    private string _organization = string.Empty;
    private string _serverAddress = "http://127.0.0.1:58627/";
    private string? _validationMessage;
    private bool _isSaving;

    public ProviderConnectionEditorViewModel(
        ProviderCode code,
        ProviderConnection? connection,
        IProviderConnectionService service)
        : this(CompatibilityDefinition(code), connection, service)
    {
    }

    public ProviderConnectionEditorViewModel(
        ProviderDefinition definition,
        ProviderConnection? connection,
        IProviderConnectionService service,
        IProviderRegistry? registry = null)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _connection = connection;
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _registry = registry;
        _providerName = definition.DisplayName;
        _displayLabel = definition.DisplayLabel ?? string.Empty;
        _description = definition.Description ?? string.Empty;
        _enabled = definition.Enabled;
        _authenticationMode = definition.AuthenticationMode;
        _capacityMode = definition.CapacityMode;
        Hydrate(connection);
    }

    public Guid ProviderId => _definition.Id;

    public ProviderCode? Code => _definition.BuiltInCode;

    public bool IsCustom => _definition.Kind == ProviderKind.Custom;

    public bool IsBuiltIn => !IsCustom;

    public string Title => IsCustom
        ? string.IsNullOrWhiteSpace(_providerName) ? "Add AI Provider" : $"Configure {_providerName}"
        : $"{_definition.DisplayName} settings";

    public string ChannelLabel => IsCustom
        ? "Generic provider registration. APO does not call arbitrary URLs or commands."
        : _definition.BuiltInCode switch
        {
            ProviderCode.Codex => "Existing local Codex session first; API key fallback is optional.",
            ProviderCode.Claude => "Existing local Claude session first; organization API usage is an optional fallback.",
            ProviderCode.Antigravity => "External/manual provider state; capacity is not machine-readable.",
            ProviderCode.Copilot => "Legacy provider settings retained for compatibility only.",
            ProviderCode.Kimi => "Legacy provider settings retained for compatibility only.",
            _ => "Provider authentication and capacity settings."
        };

    public bool IsCopilot => Code == ProviderCode.Copilot;
    public bool IsClaude => Code == ProviderCode.Claude;
    public bool IsKimi => Code == ProviderCode.Kimi;

    public IReadOnlyList<ProviderAuthenticationMode> AuthenticationModeOptions { get; } =
        Enum.GetValues<ProviderAuthenticationMode>();

    public IReadOnlyList<ProviderCapacityMode> CapacityModeOptions =>
        SupportsAutomaticCapacity
            ? [ProviderCapacityMode.Automatic, ProviderCapacityMode.Manual, ProviderCapacityMode.Unavailable]
            : [ProviderCapacityMode.Manual, ProviderCapacityMode.Unavailable];

    public string CapacityModeReason => SupportsAutomaticCapacity
        ? Code == ProviderCode.Claude
            ? "Automatic reads organization API usage only; it does not represent consumer subscription capacity."
            : "Automatic is available through this provider's declared typed adapter."
        : "Automatic capacity is unavailable for the selected authentication channel; use Manual or Unavailable.";

    public string ProviderName
    {
        get => _providerName;
        set
        {
            if (SetProperty(ref _providerName, value))
            {
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public string DisplayLabel
    {
        get => _displayLabel;
        set => SetProperty(ref _displayLabel, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public ProviderAuthenticationMode AuthenticationMode
    {
        get => _authenticationMode;
        set
        {
            if (SetProperty(ref _authenticationMode, value))
            {
                if (!SupportsAutomaticCapacity && _capacityMode == ProviderCapacityMode.Automatic)
                {
                    _capacityMode = ProviderCapacityMode.Manual;
                    OnPropertyChanged(nameof(CapacityMode));
                }

                OnPropertyChanged(nameof(IsLocalSession));
                OnPropertyChanged(nameof(IsApiKey));
                OnPropertyChanged(nameof(IsExternalManual));
                OnPropertyChanged(nameof(CredentialStateText));
                OnPropertyChanged(nameof(CapacityModeOptions));
                OnPropertyChanged(nameof(CapacityModeReason));
            }
        }
    }

    public ProviderCapacityMode CapacityMode
    {
        get => _capacityMode;
        set => SetProperty(ref _capacityMode, value == ProviderCapacityMode.Automatic && !SupportsAutomaticCapacity
            ? ProviderCapacityMode.Manual
            : value);
    }

    public bool IsLocalSession => AuthenticationMode == ProviderAuthenticationMode.LocalSession;

    public bool IsApiKey => AuthenticationMode == ProviderAuthenticationMode.ApiKey;

    public bool IsExternalManual => AuthenticationMode == ProviderAuthenticationMode.ExternalManual;

    private bool SupportsAutomaticCapacity =>
        _definition.HasCapability(ProviderCapabilities.SupportsCapacityRefresh) &&
        AuthenticationMode != ProviderAuthenticationMode.ExternalManual &&
        (AuthenticationMode == ProviderAuthenticationMode.ApiKey || Code != ProviderCode.Claude);

    public CopilotBillingScope CopilotScope
    {
        get => _copilotScope;
        set => SetProperty(ref _copilotScope, value);
    }

    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    public string Organization
    {
        get => _organization;
        set => SetProperty(ref _organization, value);
    }

    public string ServerAddress
    {
        get => _serverAddress;
        set => SetProperty(ref _serverAddress, value);
    }

    public bool CredentialSaved => !string.IsNullOrWhiteSpace(_connection?.CredentialReference);

    public string CredentialStateText => IsApiKey
        ? CredentialSaved
            ? "API key configured securely. The saved secret is never loaded into this editor."
            : "No API key configured."
        : "No APO-managed credential required.";

    public string? ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set => SetProperty(ref _isSaving, value);
    }

    public ProviderDefinition Definition => _definition;

    public async Task<bool> SaveAsync(
        string? newSecret,
        CancellationToken cancellationToken = default)
    {
        ValidationMessage = Validate(newSecret, removing: false);
        if (ValidationMessage is not null)
        {
            return false;
        }

        IsSaving = true;
        try
        {
            if (IsCustom)
            {
                if (_registry is null)
                {
                    ValidationMessage = "Custom provider registration is unavailable.";
                    return false;
                }

                _definition = await _registry.SaveCustomAsync(
                        new ProviderDefinitionEdit(
                            ProviderName,
                            string.IsNullOrWhiteSpace(DisplayLabel) ? null : DisplayLabel,
                            AuthenticationMode,
                            CapacityMode,
                            enabled: Enabled,
                            string.IsNullOrWhiteSpace(Description) ? null : Description,
                            _definition.Id),
                        cancellationToken)
                    .ConfigureAwait(true);
            }

            var enteredSecret = string.IsNullOrWhiteSpace(newSecret) ? null : newSecret;
            var edit = _definition.BuiltInCode is { } code
                ? new ProviderConnectionEdit(
                    code,
                    ConnectionTypeFor(AuthenticationMode),
                    BuildConfiguration(),
                    enteredSecret)
                : ProviderConnectionEdit.ForProvider(
                    _definition.Id,
                    ConnectionTypeFor(AuthenticationMode),
                    BuildConfiguration(),
                    enteredSecret);

            _connection = await _service.SaveAsync(edit, cancellationToken)
                .ConfigureAwait(true);
            OnPropertyChanged(nameof(CredentialSaved));
            OnPropertyChanged(nameof(CredentialStateText));
            OnPropertyChanged(nameof(Definition));
            ValidationMessage = null;
            return true;
        }
        catch (ArgumentException exception)
        {
            ValidationMessage = exception.Message;
            return false;
        }
        catch
        {
            ValidationMessage = "The provider settings could not be saved. The previous saved state was preserved.";
            return false;
        }
        finally
        {
            IsSaving = false;
        }
    }

    public async Task<bool> RemoveCredentialAsync(CancellationToken cancellationToken = default)
    {
        ValidationMessage = Validate(null, removing: true);
        if (ValidationMessage is not null)
        {
            return false;
        }

        IsSaving = true;
        try
        {
            _connection = await _service.SaveAsync(
                    IsCustom
                        ? ProviderConnectionEdit.ForProvider(
                            _definition.Id,
                            ConnectionTypeFor(AuthenticationMode),
                            BuildConfiguration(),
                            removeCredential: true)
                        : new ProviderConnectionEdit(
                            _definition.BuiltInCode!.Value,
                            ConnectionTypeFor(AuthenticationMode),
                            BuildConfiguration(),
                            removeCredential: true),
                    cancellationToken)
                .ConfigureAwait(true);
            OnPropertyChanged(nameof(CredentialSaved));
            OnPropertyChanged(nameof(CredentialStateText));
            ValidationMessage = null;
            return true;
        }
        catch
        {
            ValidationMessage = "The credential could not be removed. The previous saved state was preserved.";
            return false;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private string? Validate(string? newSecret, bool removing)
    {
        if (IsCustom && string.IsNullOrWhiteSpace(ProviderName))
        {
            return "Provider name is required.";
        }

        if (IsCustom && CapacityMode == ProviderCapacityMode.Automatic)
        {
            return "Automatic capacity requires a registered typed adapter; use Manual or Unavailable.";
        }

        if (CapacityMode == ProviderCapacityMode.Automatic && !SupportsAutomaticCapacity)
        {
            return "Automatic capacity is unavailable for the selected authentication channel.";
        }

        if (!IsApiKey && !string.IsNullOrWhiteSpace(newSecret))
        {
            return "API key can only be saved when API key authentication mode is selected.";
        }

        if (IsApiKey && !removing && string.IsNullOrWhiteSpace(newSecret) && !CredentialSaved)
        {
            return "Enter an API key or choose another authentication mode.";
        }

        if (removing && !CredentialSaved)
        {
            return "There is no saved API key to remove.";
        }

        return null;
    }

    private IReadOnlyDictionary<string, string?> BuildConfiguration()
    {
        var configuration = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [ProviderConnectionConfigurationKeys.AuthenticationMode] = AuthenticationMode.ToString(),
            [ProviderConnectionConfigurationKeys.CapacityMode] = CapacityMode.ToString()
        };

        if (IsCopilot)
        {
            configuration[ProviderConnectionConfigurationKeys.CopilotScope] = CopilotScope.ToString();
            configuration[ProviderConnectionConfigurationKeys.CopilotUsername] =
                string.IsNullOrWhiteSpace(Username) ? null : Username.Trim();
            configuration[ProviderConnectionConfigurationKeys.CopilotOrganization] =
                string.IsNullOrWhiteSpace(Organization) ? null : Organization.Trim();
        }
        else if (IsClaude)
        {
            configuration[ProviderConnectionConfigurationKeys.AnthropicChannel] =
                AuthenticationMode == ProviderAuthenticationMode.ApiKey
                    ? "organization-api"
                    : "local-session";
        }
        else if (IsKimi)
        {
            configuration[ProviderConnectionConfigurationKeys.KimiServerAddress] = ServerAddress.Trim();
        }

        return configuration;
    }

    private void Hydrate(ProviderConnection? connection)
    {
        if (connection is null)
        {
            return;
        }

        AuthenticationMode = connection.ConnectionType switch
        {
            ProviderConnectionType.LocalSession => ProviderAuthenticationMode.LocalSession,
            ProviderConnectionType.ApiKey or ProviderConnectionType.OfficialApi => ProviderAuthenticationMode.ApiKey,
            ProviderConnectionType.ExternalManual or ProviderConnectionType.Manual => ProviderAuthenticationMode.ExternalManual,
            _ => AuthenticationMode
        };

        if (connection.Configuration.TryGetValue(ProviderConnectionConfigurationKeys.CapacityMode, out var capacityMode) &&
            Enum.TryParse<ProviderCapacityMode>(capacityMode, ignoreCase: true, out var parsedCapacityMode))
        {
            CapacityMode = parsedCapacityMode;
        }

        if (connection.Configuration.TryGetValue(ProviderConnectionConfigurationKeys.CopilotScope, out var scope) &&
            Enum.TryParse<CopilotBillingScope>(scope, ignoreCase: true, out var parsedScope))
        {
            CopilotScope = parsedScope;
        }

        if (connection.Configuration.TryGetValue(ProviderConnectionConfigurationKeys.CopilotUsername, out var username))
        {
            Username = username ?? string.Empty;
        }

        if (connection.Configuration.TryGetValue(ProviderConnectionConfigurationKeys.CopilotOrganization, out var organization))
        {
            Organization = organization ?? string.Empty;
        }

        if (connection.Configuration.TryGetValue(ProviderConnectionConfigurationKeys.KimiServerAddress, out var serverAddress) &&
            !string.IsNullOrWhiteSpace(serverAddress))
        {
            ServerAddress = serverAddress;
        }
    }

    private static ProviderConnectionType ConnectionTypeFor(ProviderAuthenticationMode mode) => mode switch
    {
        ProviderAuthenticationMode.LocalSession => ProviderConnectionType.LocalSession,
        ProviderAuthenticationMode.ApiKey => ProviderConnectionType.ApiKey,
        ProviderAuthenticationMode.ExternalManual => ProviderConnectionType.ExternalManual,
        _ => ProviderConnectionType.Unknown
    };

    private static ProviderDefinition CompatibilityDefinition(ProviderCode code) =>
        ProviderDefinition.BuiltIn(
            Guid.NewGuid(),
            code,
            code switch
            {
                ProviderCode.Copilot => "GitHub Copilot",
                ProviderCode.Claude => "Claude",
                ProviderCode.Kimi => "Kimi",
                _ => code.ToString()
            },
            code is ProviderCode.Codex or ProviderCode.Claude
                ? ProviderAuthenticationMode.LocalSession
                : ProviderAuthenticationMode.ApiKey,
            ProviderCapacityMode.Manual,
            ProviderCapabilities.SupportsApiKey | ProviderCapabilities.SupportsConfiguration,
            (int)code);
}
