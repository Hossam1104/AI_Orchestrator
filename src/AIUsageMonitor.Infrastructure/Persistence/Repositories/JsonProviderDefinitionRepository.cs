using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Domain.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageMonitor.Infrastructure.Persistence.Repositories;

public sealed class JsonProviderDefinitionRepository : IProviderDefinitionRepository
{
    private readonly ApplicationDataPaths _paths;
    private readonly VersionedJsonCollectionStore<ProviderDefinitionRecord> _records;
    private readonly ILogger<JsonProviderDefinitionRepository> _logger;

    public JsonProviderDefinitionRepository(
        ApplicationDataPaths paths,
        JsonFileStore files,
        ILogger<JsonProviderDefinitionRepository> logger)
    {
        _paths = paths;
        _records = new VersionedJsonCollectionStore<ProviderDefinitionRecord>(files);
        _logger = logger;
    }

    public async Task<IReadOnlyList<ProviderDefinition>> GetCustomAsync(
        CancellationToken cancellationToken = default)
    {
        var records = await _records.ReadAsync(_paths.ProviderDefinitionsFile, cancellationToken)
            .ConfigureAwait(false);
        var definitions = new List<ProviderDefinition>(records.Count);
        foreach (var record in records)
        {
            try
            {
                var definition = record.ToDomain();
                if (definition.Kind == ProviderKind.Custom)
                {
                    definitions.Add(definition);
                }
            }
            catch (ArgumentException exception)
            {
                _logger.LogWarning(exception, "Skipping invalid custom provider definition {ProviderId}", record.Id);
            }
        }

        return definitions
            .OrderBy(definition => definition.SortOrder)
            .ThenBy(definition => definition.CreatedAt)
            .ToArray();
    }

    public Task UpsertCustomAsync(
        ProviderDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Kind != ProviderKind.Custom)
        {
            throw new ArgumentException("Only custom provider definitions can be persisted.", nameof(definition));
        }

        var record = ProviderDefinitionRecord.FromDomain(definition);
        return _records.UpdateAsync(_paths.ProviderDefinitionsFile, records =>
        {
            var index = records.FindIndex(existing => existing.Id == record.Id);
            if (index >= 0)
            {
                records[index] = record;
            }
            else
            {
                records.Add(record);
            }

            return records;
        }, cancellationToken);
    }

    public Task RemoveCustomAsync(Guid providerId, CancellationToken cancellationToken = default)
    {
        if (providerId == Guid.Empty)
        {
            throw new ArgumentException("Provider id cannot be empty.", nameof(providerId));
        }

        return _records.UpdateAsync(_paths.ProviderDefinitionsFile, records =>
        {
            records.RemoveAll(record => record.Id == providerId && record.Kind == ProviderKind.Custom);
            return records;
        }, cancellationToken);
    }
}
