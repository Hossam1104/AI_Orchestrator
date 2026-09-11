using AIUsageMonitor.Domain.Providers;
using AIUsageMonitor.Infrastructure.Persistence;
using AIUsageMonitor.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageMonitor.Infrastructure.Tests;

public sealed class ProviderDefinitionPersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "apo-provider-definitions-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CustomDefinition_RoundTripsAndRemovalLeavesRegistrationAbsent()
    {
        var paths = new ApplicationDataPaths(_root);
        paths.EnsureDirectories();
        var repository = new JsonProviderDefinitionRepository(
            paths,
            new JsonFileStore(NullLogger<JsonFileStore>.Instance),
            NullLogger<JsonProviderDefinitionRepository>.Instance);
        var now = DateTimeOffset.UtcNow;
        var definition = ProviderDefinition.Custom(
            Guid.Parse("11b82f6b-8f2c-4c07-9a89-3bb94bd57a4e"),
            "Future Provider",
            "Manual",
            ProviderAuthenticationMode.ExternalManual,
            ProviderCapacityMode.Manual,
            enabled: true,
            sortOrder: 3,
            now,
            now,
            "No arbitrary endpoint or command is stored.");

        await repository.UpsertCustomAsync(definition);
        var loaded = Assert.Single(await repository.GetCustomAsync());
        Assert.Equal(definition.Id, loaded.Id);
        Assert.Equal(definition.DisplayName, loaded.DisplayName);
        Assert.Equal(definition.AuthenticationMode, loaded.AuthenticationMode);
        Assert.DoesNotContain("secret", await File.ReadAllTextAsync(paths.ProviderDefinitionsFile), StringComparison.OrdinalIgnoreCase);

        await repository.RemoveCustomAsync(definition.Id);

        Assert.Empty(await repository.GetCustomAsync());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
