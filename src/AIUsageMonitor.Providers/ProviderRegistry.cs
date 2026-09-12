using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Domain.Providers;
using AIUsageMonitor.Providers.Common;

namespace AIUsageMonitor.Providers;

/// <summary>
/// Dynamic provider registry. The built-in adapter set is deliberately limited to the V1
/// defaults; custom entries are metadata-only until a typed adapter is registered.
/// </summary>
public sealed class ProviderRegistry : IProviderRegistry
{
    private static readonly ProviderCode[] DefaultCodes =
    [
        ProviderCode.Codex,
        ProviderCode.Claude,
        ProviderCode.Antigravity
    ];

    private readonly IReadOnlyList<IAiUsageProvider> _providers;
    private readonly Dictionary<Guid, ProviderDefinition> _definitions;
    private readonly IProviderDefinitionRepository? _repository;
    private readonly object _sync = new();

    public ProviderRegistry(
        IEnumerable<IAiUsageProvider> providers,
        IProviderDefinitionRepository? repository = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _repository = repository;

        var materialized = providers
            .Where(provider => DefaultCodes.Contains(provider.Code))
            .GroupBy(provider => provider.Code)
            .Select(group => group.Single())
            .ToArray();
        _providers = materialized
            .OrderBy(provider => Array.IndexOf(DefaultCodes, provider.Code))
            .ToArray();

        _definitions = _providers
            .Select(CreateBuiltInDefinition)
            .ToDictionary(definition => definition.Id);
    }

    public IReadOnlyList<IAiUsageProvider> GetAll() => _providers;

    public IAiUsageProvider? Find(ProviderCode code) =>
        _providers.FirstOrDefault(provider => provider.Code == code);

    public IReadOnlyList<ProviderDefinition> GetDefinitions()
    {
        lock (_sync)
        {
            return _definitions.Values
                .OrderBy(definition => definition.Kind == ProviderKind.Custom ? 1 : 0)
                .ThenBy(definition => definition.SortOrder)
                .ThenBy(definition => definition.CreatedAt)
                .ToArray();
        }
    }

    public ProviderDefinition? FindDefinition(Guid providerId)
    {
        lock (_sync)
        {
            return _definitions.GetValueOrDefault(providerId);
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_repository is null)
        {
            return;
        }

        var persisted = await _repository.GetCustomAsync(cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            foreach (var definition in persisted)
            {
                if (definition.Kind != ProviderKind.Custom ||
                    _definitions.ContainsKey(definition.Id) ||
                    HasDuplicateDisplayName(definition.DisplayName, definition.DisplayLabel, definition.Id))
                {
                    continue;
                }

                _definitions.Add(definition.Id, definition);
            }
        }
    }

    public async Task<ProviderDefinition> SaveCustomAsync(
        ProviderDefinitionEdit edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (_repository is null)
        {
            throw new NotSupportedException("Custom provider persistence is unavailable.");
        }

        var displayName = NormalizeName(edit.DisplayName);
        var now = DateTimeOffset.UtcNow;
        ProviderDefinition definition;
        ProviderDefinition? previous = null;
        lock (_sync)
        {
            var existing = edit.Id is { } id ? _definitions.GetValueOrDefault(id) : null;
            if (existing is not null && existing.Kind != ProviderKind.Custom)
            {
                throw new InvalidOperationException("Built-in providers cannot be replaced by custom registration.");
            }

            if (HasDuplicateDisplayName(displayName, edit.DisplayLabel, edit.Id))
            {
                throw new ArgumentException("A provider with this display name is already registered.", nameof(edit));
            }

            previous = existing;
            definition = ProviderDefinition.Custom(
                existing?.Id ?? edit.Id.GetValueOrDefault(Guid.NewGuid()),
                displayName,
                edit.DisplayLabel,
                edit.AuthenticationMode,
                edit.CapacityMode,
                edit.Enabled,
                existing?.SortOrder ?? NextCustomSortOrder(),
                existing?.CreatedAt ?? now,
                now,
                edit.Description);
            _definitions[definition.Id] = definition;
        }

        try
        {
            await _repository.UpsertCustomAsync(definition, cancellationToken).ConfigureAwait(false);
            return definition;
        }
        catch
        {
            lock (_sync)
            {
                if (previous is not null)
                {
                    _definitions[previous.Id] = previous;
                }
                else
                {
                    _definitions.Remove(definition.Id);
                }
            }

            throw;
        }
    }

    public async Task RemoveCustomAsync(
        Guid providerId,
        CancellationToken cancellationToken = default)
    {
        if (_repository is null)
        {
            throw new NotSupportedException("Custom provider persistence is unavailable.");
        }

        ProviderDefinition removed;
        lock (_sync)
        {
            removed = _definitions.GetValueOrDefault(providerId)
                ?? throw new KeyNotFoundException("The provider registration was not found.");
            if (removed.Kind != ProviderKind.Custom)
            {
                throw new InvalidOperationException("Built-in providers cannot be removed.");
            }

            _definitions.Remove(providerId);
        }

        try
        {
            await _repository.RemoveCustomAsync(providerId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            lock (_sync)
            {
                _definitions[removed.Id] = removed;
            }

            throw;
        }
    }

    private ProviderDefinition CreateBuiltInDefinition(IAiUsageProvider provider)
    {
        var (name, authMode, capacityMode, capabilities, description, sortOrder) = provider.Code switch
        {
            ProviderCode.Codex => (
                "Codex",
                ProviderAuthenticationMode.LocalSession,
                ProviderCapacityMode.Manual,
                ProviderCapabilities.SupportsLocalSessionDetection |
                    ProviderCapabilities.SupportsApiKey |
                    ProviderCapabilities.SupportsConfiguration,
                "Uses the existing authenticated local Codex session when available; API key is an optional fallback.",
                0),
            ProviderCode.Claude => (
                "Claude",
                ProviderAuthenticationMode.LocalSession,
                ProviderCapacityMode.Manual,
                ProviderCapabilities.SupportsLocalSessionDetection |
                    ProviderCapabilities.SupportsApiKey |
                    ProviderCapabilities.SupportsCapacityRefresh |
                    ProviderCapabilities.SupportsConfiguration,
                "Uses the existing authenticated local Claude session; organization API usage is available only through explicit API-key fallback.",
                1),
            ProviderCode.Antigravity => (
                "Antigravity",
                ProviderAuthenticationMode.ExternalManual,
                ProviderCapacityMode.Manual,
                ProviderCapabilities.None,
                "External/manual provider state is shown without inventing machine-readable capacity.",
                2),
            _ => throw new ArgumentOutOfRangeException()
        };

        return ProviderDefinition.BuiltIn(
            ProviderIdentity.ForProvider(provider.Code),
            provider.Code,
            name,
            authMode,
            capacityMode,
            capabilities,
            sortOrder,
            description: description);
    }

    /// <summary>
    /// Compares the label an operator actually sees on both sides. A display label overrides the
    /// display name in every provider surface, so checking only the raw name would let two
    /// registrations render under one identical title.
    /// </summary>
    private bool HasDuplicateDisplayName(string displayName, string? displayLabel, Guid? exceptId)
    {
        var candidate = NormalizeName(EffectiveName(displayName, displayLabel));
        return _definitions.Values.Any(definition =>
            definition.Id != exceptId &&
            string.Equals(
                NormalizeName(definition.EffectiveDisplayName),
                candidate,
                StringComparison.OrdinalIgnoreCase));
    }

    private static string EffectiveName(string displayName, string? displayLabel) =>
        string.IsNullOrWhiteSpace(displayLabel) ? displayName : displayLabel;

    private int NextCustomSortOrder() =>
        _definitions.Values
            .Where(definition => definition.Kind == ProviderKind.Custom)
            .Select(definition => definition.SortOrder)
            .DefaultIfEmpty(2)
            .Max() + 1;

    private static string NormalizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Provider name is required.", nameof(value));
        }

        return string.Join(
            ' ',
            value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
