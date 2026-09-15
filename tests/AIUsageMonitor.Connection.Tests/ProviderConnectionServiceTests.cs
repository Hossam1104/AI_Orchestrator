using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Application.Security;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Domain.Providers;
using AIUsageMonitor.Infrastructure.Persistence;
using AIUsageMonitor.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageMonitor.Connection.Tests;

public sealed class ProviderConnectionServiceTests
{
    [Fact]
    public async Task Save_PersistsOnlyOpaqueReference_AndAppliesRuntimeSettings()
    {
        using var store = new TemporaryStore();
        var repository = new JsonProviderConnectionRepository(
            store.Paths,
            store.Files,
            NullLogger<JsonProviderConnectionRepository>.Instance);
        var credentials = new FakeCredentialStore();
        var runtime = new FakeRuntimeSettingsUpdater();
        var service = CreateService(repository, credentials, runtime);

        var saved = await service.SaveAsync(new ProviderConnectionEdit(
            ProviderCode.Copilot,
            ProviderConnectionType.OfficialApi,
            new Dictionary<string, string?>
            {
                [ProviderConnectionConfigurationKeys.CopilotScope] = "PersonalUser",
                [ProviderConnectionConfigurationKeys.CopilotUsername] = "octocat"
            },
            secret: "synthetic-secret"));

        Assert.NotNull(saved.CredentialReference);
        Assert.StartsWith("apo-copilot-", saved.CredentialReference, StringComparison.Ordinal);
        Assert.Equal("synthetic-secret", await credentials.RetrieveAsync(saved.CredentialReference!));
        Assert.Equal(saved.CredentialReference, runtime.LastCredentialReference);
        Assert.Equal("octocat", runtime.LastConfiguration[ProviderConnectionConfigurationKeys.CopilotUsername]);

        var json = await File.ReadAllTextAsync(store.Paths.ConnectionsFile);
        Assert.DoesNotContain("synthetic-secret", json, StringComparison.Ordinal);
        Assert.Contains(saved.CredentialReference, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CustomApiKey_UsesStableIdNamespaceAndNeverPersistsSecretMaterial()
    {
        using var store = new TemporaryStore();
        var repository = new JsonProviderConnectionRepository(
            store.Paths,
            store.Files,
            NullLogger<JsonProviderConnectionRepository>.Instance);
        var credentials = new FakeCredentialStore();
        var service = CreateService(repository, credentials, new FakeRuntimeSettingsUpdater());
        var providerId = Guid.Parse("d9dfed05-6c31-4d16-a4ca-7fc5c36e6df7");

        var saved = await service.SaveAsync(ProviderConnectionEdit.ForProvider(
            providerId,
            ProviderConnectionType.ApiKey,
            new Dictionary<string, string?>
            {
                [ProviderConnectionConfigurationKeys.AuthenticationMode] = ProviderAuthenticationMode.ApiKey.ToString(),
                [ProviderConnectionConfigurationKeys.CapacityMode] = ProviderCapacityMode.Unavailable.ToString()
            },
            secret: "custom-secret-material"));

        Assert.StartsWith($"apo-custom-{providerId:N}-", saved.CredentialReference, StringComparison.Ordinal);
        Assert.Equal("custom-secret-material", await credentials.RetrieveAsync(saved.CredentialReference!));
        var json = await File.ReadAllTextAsync(store.Paths.ConnectionsFile);
        Assert.DoesNotContain("custom-secret-material", json, StringComparison.Ordinal);
        Assert.Contains(providerId.ToString("N"), saved.CredentialReference, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedPersistence_RemovesStagedCredential_AndPreservesPreviousReference()
    {
        var repository = new InMemoryConnectionRepository { FailWrites = true };
        var previous = CreateConnection("old-reference");
        repository.Current = previous;
        var credentials = new FakeCredentialStore();
        await credentials.StoreAsync("old-reference", "old-secret");
        var service = CreateService(repository, credentials, new FakeRuntimeSettingsUpdater());

        await Assert.ThrowsAsync<IOException>(() => service.SaveAsync(new ProviderConnectionEdit(
            ProviderCode.Copilot,
            ProviderConnectionType.OfficialApi,
            new Dictionary<string, string?>(),
            secret: "new-secret")));

        Assert.Equal(previous.CredentialReference, repository.Current!.CredentialReference);
        Assert.Equal("old-secret", await credentials.RetrieveAsync("old-reference"));
        Assert.Single(credentials.RemovedReferences);
        Assert.StartsWith("apo-copilot-", credentials.RemovedReferences[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveCredential_CommitsNullReferenceBeforeSecureCleanup()
    {
        var repository = new InMemoryConnectionRepository { Current = CreateConnection("old-reference") };
        var credentials = new FakeCredentialStore();
        await credentials.StoreAsync("old-reference", "old-secret");
        var service = CreateService(repository, credentials, new FakeRuntimeSettingsUpdater());

        var saved = await service.SaveAsync(new ProviderConnectionEdit(
            ProviderCode.Copilot,
            ProviderConnectionType.OfficialApi,
            new Dictionary<string, string?>(),
            removeCredential: true));

        Assert.Null(saved.CredentialReference);
        Assert.Equal(ProviderConnectionStatus.NotConfigured, saved.Status);
        Assert.Null(repository.Current!.CredentialReference);
        Assert.Equal(ProviderConnectionStatus.NotConfigured, repository.Current.Status);
        Assert.Equal(["old-reference"], credentials.RemovedReferences);
        Assert.Null(await credentials.RetrieveAsync("old-reference"));
    }

    [Fact]
    public async Task ReplacingCredential_DoesNotPreserveConnectedStatusBeforeVerification()
    {
        var repository = new InMemoryConnectionRepository { Current = CreateConnection("old-reference") };
        var credentials = new FakeCredentialStore();
        await credentials.StoreAsync("old-reference", "old-secret");
        var service = CreateService(repository, credentials, new FakeRuntimeSettingsUpdater());

        var saved = await service.SaveAsync(new ProviderConnectionEdit(
            ProviderCode.Copilot,
            ProviderConnectionType.OfficialApi,
            new Dictionary<string, string?>(),
            secret: "new-secret"));

        Assert.Equal(ProviderConnectionStatus.Updating, saved.Status);
        Assert.NotEqual(ProviderConnectionStatus.Connected, repository.Current!.Status);
    }

    [Fact]
    public async Task EditingConfiguration_DoesNotPreserveConnectedStatusBeforeVerification()
    {
        var repository = new InMemoryConnectionRepository { Current = CreateConnection("old-reference") };
        var credentials = new FakeCredentialStore();
        await credentials.StoreAsync("old-reference", "old-secret");
        var service = CreateService(repository, credentials, new FakeRuntimeSettingsUpdater());

        var saved = await service.SaveAsync(new ProviderConnectionEdit(
            ProviderCode.Copilot,
            ProviderConnectionType.OfficialApi,
            new Dictionary<string, string?>
            {
                [ProviderConnectionConfigurationKeys.CopilotScope] = "Organization",
                [ProviderConnectionConfigurationKeys.CopilotOrganization] = "new-org"
            }));

        Assert.Equal(ProviderConnectionStatus.Updating, saved.Status);
        Assert.Equal("old-reference", saved.CredentialReference);
        Assert.Equal("new-org", saved.Configuration[ProviderConnectionConfigurationKeys.CopilotOrganization]);
    }

    [Fact]
    public async Task SuccessRefresh_PersistsConnectedAndBothRefreshTimestamps()
    {
        var repository = new InMemoryConnectionRepository { Current = CreateConnection("opaque-reference") };
        var credentials = new FakeCredentialStore();
        var service = CreateService(repository, credentials, new FakeRuntimeSettingsUpdater());
        var completedAt = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

        var saved = await service.RecordRefreshAsync(ProviderRefreshResult.Success(
            ProviderCode.Copilot,
            CreateAccount("fresh-account"),
            null,
            [],
            completedAt));

        Assert.NotNull(saved);
        Assert.Equal(ProviderConnectionStatus.Connected, saved!.Status);
        Assert.Equal(completedAt, saved.LastAttempt);
        Assert.Equal(completedAt, saved.LastSuccessfulSync);
        Assert.Equal("fresh-account", saved.AccountDisplayName);
    }

    [Fact]
    public async Task AuthenticationFailure_UpdatesAttemptAndPreservesPreviousSuccessfulSync()
    {
        var previousSuccess = new DateTimeOffset(2026, 8, 23, 11, 0, 0, TimeSpan.Zero);
        var repository = new InMemoryConnectionRepository
        {
            Current = CreateConnection("opaque-reference", previousSuccess, previousSuccess)
        };
        var service = CreateService(repository, new FakeCredentialStore(), new FakeRuntimeSettingsUpdater());
        var attemptedAt = previousSuccess.AddMinutes(5);

        var saved = await service.RecordRefreshAsync(
            ProviderRefreshResult.AuthenticationRequired(ProviderCode.Copilot, attemptedAt));

        Assert.NotNull(saved);
        Assert.Equal(ProviderConnectionStatus.AuthenticationRequired, saved!.Status);
        Assert.Equal(attemptedAt, saved.LastAttempt);
        Assert.Equal(previousSuccess, saved.LastSuccessfulSync);
    }

    [Fact]
    public async Task StaleRefresh_UpdatesAttemptAndPreservesPreviousSuccessfulSync()
    {
        var previousSuccess = new DateTimeOffset(2026, 8, 23, 11, 0, 0, TimeSpan.Zero);
        var repository = new InMemoryConnectionRepository
        {
            Current = CreateConnection("opaque-reference", previousSuccess, previousSuccess)
        };
        var service = CreateService(repository, new FakeCredentialStore(), new FakeRuntimeSettingsUpdater());
        var attemptedAt = previousSuccess.AddMinutes(5);

        var saved = await service.RecordRefreshAsync(ProviderRefreshResult.Stale(
            ProviderCode.Copilot,
            null,
            null,
            [],
            attemptedAt));

        Assert.NotNull(saved);
        Assert.Equal(ProviderConnectionStatus.Stale, saved!.Status);
        Assert.Equal(attemptedAt, saved.LastAttempt);
        Assert.Equal(previousSuccess, saved.LastSuccessfulSync);
    }

    [Fact]
    public async Task RefreshMetadata_PreservesCredentialReferenceAndConfiguration()
    {
        var configuration = new Dictionary<string, string?>
        {
            [ProviderConnectionConfigurationKeys.CopilotScope] = "Organization",
            [ProviderConnectionConfigurationKeys.CopilotOrganization] = "preserved-org"
        };
        var repository = new InMemoryConnectionRepository
        {
            Current = CreateConnection("opaque-reference", null, null, configuration)
        };
        var credentials = new FakeCredentialStore();
        var service = CreateService(repository, credentials, new FakeRuntimeSettingsUpdater());

        var saved = await service.RecordRefreshAsync(ProviderRefreshResult.Partial(
            ProviderCode.Copilot,
            null,
            null,
            [],
            "Usage-only.",
            new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero)));

        Assert.NotNull(saved);
        Assert.Equal(ProviderConnectionStatus.Partial, saved!.Status);
        Assert.Equal(new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero), saved.LastAttempt);
        Assert.Equal("opaque-reference", saved!.CredentialReference);
        Assert.Equal(configuration, saved.Configuration);
        Assert.Equal(0, credentials.RetrieveCount);
    }

    [Fact]
    public async Task ProviderErrorRefresh_PersistsErrorMetadataAndPreservesPreviousSuccessfulSync()
    {
        var previousSuccess = new DateTimeOffset(2026, 8, 23, 11, 0, 0, TimeSpan.Zero);
        var repository = new InMemoryConnectionRepository
        {
            Current = CreateConnection("opaque-reference", previousSuccess, previousSuccess)
        };
        var service = CreateService(repository, new FakeCredentialStore(), new FakeRuntimeSettingsUpdater());
        var attemptedAt = previousSuccess.AddMinutes(5);

        var saved = await service.RecordRefreshAsync(ProviderRefreshResult.Failed(
            ProviderCode.Copilot,
            "provider_error",
            "The provider failed.",
            attemptedAt));

        Assert.NotNull(saved);
        Assert.Equal(ProviderConnectionStatus.Error, saved!.Status);
        Assert.Equal(attemptedAt, saved.LastAttempt);
        Assert.Equal(previousSuccess, saved.LastSuccessfulSync);
        Assert.Equal("provider_error", saved.LastErrorCode);
        Assert.Equal("The provider failed.", saved.LastErrorMessage);
    }

    private static ProviderConnectionService CreateService(
        IProviderConnectionRepository repository,
        FakeCredentialStore credentials,
        IProviderRuntimeSettingsUpdater runtime) =>
        new(repository, new FixedIdentityCatalog(), credentials, runtime, new SystemClock());

    private static ProviderConnection CreateConnection(
        string credentialReference,
        DateTimeOffset? lastSuccessfulSync = null,
        DateTimeOffset? lastAttempt = null,
        IReadOnlyDictionary<string, string?>? configuration = null) => new(
        Guid.NewGuid(),
        FixedIdentityCatalog.CopilotId,
        ProviderConnectionType.OfficialApi,
        ProviderConnectionStatus.Connected,
        "test-account",
        lastSuccessfulSync,
        lastAttempt,
        null,
        null,
        credentialReference,
        configuration);

    private static ProviderAccount CreateAccount(string displayName) => new(
        Guid.NewGuid(),
        FixedIdentityCatalog.CopilotId,
        displayName,
        displayName,
        AIUsageMonitor.Domain.Common.DataSource.OfficialApi,
        AIUsageMonitor.Domain.Common.ConfidenceLevel.Official,
        DateTimeOffset.UtcNow);

    private sealed class TemporaryStore : IDisposable
    {
        public TemporaryStore()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "AIUsageMonitorTests", Guid.NewGuid().ToString("N"));
            Paths = new ApplicationDataPaths(RootDirectory);
            Files = new JsonFileStore(NullLogger<JsonFileStore>.Instance);
            Paths.EnsureDirectories();
        }

        public string RootDirectory { get; }

        public ApplicationDataPaths Paths { get; }

        public JsonFileStore Files { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootDirectory))
                {
                    Directory.Delete(RootDirectory, recursive: true);
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

    private sealed class FixedIdentityCatalog : IProviderIdentityCatalog
    {
        public static Guid CopilotId { get; } = Guid.Parse("e6cb6e5a-e4ec-4a53-8d83-cb1f97f51011");

        public Guid GetProviderId(ProviderCode code) => CopilotId;
    }

    private sealed class FakeRuntimeSettingsUpdater : IProviderRuntimeSettingsUpdater
    {
        public string? LastCredentialReference { get; private set; }

        public IReadOnlyDictionary<string, string?> LastConfiguration { get; private set; } =
            new Dictionary<string, string?>();

        public void Apply(
            ProviderCode code,
            string? credentialReference,
            IReadOnlyDictionary<string, string?> configuration)
        {
            LastCredentialReference = credentialReference;
            LastConfiguration = configuration;
        }
    }

    private sealed class FakeCredentialStore : ISecureCredentialStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

        public List<string> RemovedReferences { get; } = [];

        public int RetrieveCount { get; private set; }

        public Task StoreAsync(string credentialReference, string secret, CancellationToken cancellationToken = default)
        {
            _values[credentialReference] = secret;
            return Task.CompletedTask;
        }

        public Task<string?> RetrieveAsync(string credentialReference, CancellationToken cancellationToken = default) =>
            RetrieveAndCount(credentialReference);

        private Task<string?> RetrieveAndCount(string credentialReference)
        {
            RetrieveCount++;
            return Task.FromResult(_values.TryGetValue(credentialReference, out var secret) ? secret : null);
        }

        public Task RemoveAsync(string credentialReference, CancellationToken cancellationToken = default)
        {
            _values.Remove(credentialReference);
            RemovedReferences.Add(credentialReference);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryConnectionRepository : IProviderConnectionRepository
    {
        public ProviderConnection? Current { get; set; }

        public bool FailWrites { get; init; }

        public Task<ProviderConnection?> GetByProviderIdAsync(Guid providerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);

        public Task UpsertAsync(ProviderConnection connection, CancellationToken cancellationToken = default)
        {
            if (FailWrites)
            {
                throw new IOException("synthetic persistence failure");
            }

            Current = connection;
            return Task.CompletedTask;
        }
    }
}
