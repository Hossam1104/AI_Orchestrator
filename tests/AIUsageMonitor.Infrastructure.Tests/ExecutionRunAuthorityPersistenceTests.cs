using System.Text.Json;
using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.Routing;
using AIUsageMonitor.Application.Workspaces;
using AIUsageMonitor.Infrastructure.Persistence;
using AIUsageMonitor.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageMonitor.Infrastructure.Tests;

public sealed class ExecutionRunAuthorityPersistenceTests
{
    [Fact]
    public async Task CreateAndRead_RoundTripsExactAuthority()
    {
        using var store = new TemporaryStore();
        var repository = CreateRepository(store);
        var authority = CreateAuthority();

        var write = await repository.CreateAsync(authority);
        var read = await repository.GetAsync(authority.ProjectId, authority.RunId);

        Assert.Equal(ExecutionRunAuthorityRepositoryWriteStatus.Created, write.Status);
        Assert.True(read.IsValid);
        Assert.Equal(authority.ContentHash, read.Authority!.ContentHash);
        Assert.Equal(authority.Reference.ToString(), read.Authority.Reference.ToString());
        Assert.Equal(authority.WorkspacePath, read.Authority.WorkspacePath);
        Assert.Equal(authority.WorkspaceReceiptContentHash, read.Authority.WorkspaceReceiptContentHash);
        Assert.Equal(authority.InputRecoveryCheckpointReference.ContentHash, read.Authority.InputRecoveryCheckpointReference.ContentHash);
    }

    [Fact]
    public async Task SameRunId_RejectsDuplicateWithoutChangingOriginalBytes()
    {
        using var store = new TemporaryStore();
        var repository = CreateRepository(store);
        var authority = CreateAuthority();
        await repository.CreateAsync(authority);
        var path = store.Paths.GetExecutionRunAuthorityFile(authority.ProjectId, authority.RunId);
        var original = await File.ReadAllBytesAsync(path);

        var duplicate = await repository.CreateAsync(authority);
        var after = await File.ReadAllBytesAsync(path);

        Assert.Equal(ExecutionRunAuthorityRepositoryWriteStatus.InputCheckpointConflict, duplicate.Status);
        Assert.Equal(original, after);
    }

    [Fact]
    public async Task SameRunId_DifferentAuthority_FailsClosed()
    {
        using var store = new TemporaryStore();
        var repository = CreateRepository(store);
        var original = CreateAuthority();
        var conflicting = CreateAuthority(original.ProjectId, original.RunId, "different-adapter");
        await repository.CreateAsync(original);

        var write = await repository.CreateAsync(conflicting);
        var read = await repository.GetAsync(original.ProjectId, original.RunId);

        Assert.Equal(ExecutionRunAuthorityRepositoryWriteStatus.RunConflict, write.Status);
        Assert.True(read.IsValid);
        Assert.Equal(original.ContentHash, read.Authority!.ContentHash);
        Assert.NotEqual(conflicting.ContentHash, read.Authority.ContentHash);
    }

    [Fact]
    public async Task TamperedContent_IsIntegrityFailure_AndIsNotQuarantined()
    {
        using var store = new TemporaryStore();
        var repository = CreateRepository(store);
        var authority = CreateAuthority();
        await repository.CreateAsync(authority);
        var path = store.Paths.GetExecutionRunAuthorityFile(authority.ProjectId, authority.RunId);
        var json = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, json.Replace(authority.ContentHash, new string('f', 64), StringComparison.Ordinal));

        var read = await repository.GetAsync(authority.ProjectId, authority.RunId);

        Assert.Equal(ExecutionRunAuthorityReadState.IntegrityFailure, read.State);
        Assert.True(File.Exists(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.bak"));
    }

    [Fact]
    public async Task WrongProject_IsolatedFromOtherProjectPath()
    {
        using var store = new TemporaryStore();
        var repository = CreateRepository(store);
        var authority = CreateAuthority();
        await repository.CreateAsync(authority);

        var read = await repository.GetAsync(Guid.NewGuid(), authority.RunId);

        Assert.Equal(ExecutionRunAuthorityReadState.Missing, read.State);
    }

    [Fact]
    public async Task FutureAndOlderAuthoritySchemas_AreClassifiedWithoutRepair()
    {
        using var store = new TemporaryStore();
        var repository = CreateRepository(store);
        var authority = CreateAuthority();
        await repository.CreateAsync(authority);
        var path = store.Paths.GetExecutionRunAuthorityFile(authority.ProjectId, authority.RunId);

        var future = ExecutionRunAuthorityRecord.FromApplication(authority);
        future.SchemaVersion = ExecutionRunAuthoritySchema.CurrentVersion + 1;
        await store.Files.WriteAsync(path, future);
        var futureRead = await repository.GetAsync(authority.ProjectId, authority.RunId);
        Assert.Equal(ExecutionRunAuthorityReadState.UnsupportedFutureVersion, futureRead.State);

        var older = ExecutionRunAuthorityRecord.FromApplication(authority);
        older.SchemaVersion = ExecutionRunAuthoritySchema.CurrentVersion - 1;
        await store.Files.WriteAsync(path, older);
        var olderRead = await repository.GetAsync(authority.ProjectId, authority.RunId);
        Assert.Equal(ExecutionRunAuthorityReadState.MigrationRequired, olderRead.State);
    }

    [Fact]
    public async Task CorruptJson_IsInvalid_AndRemainsInPlace()
    {
        using var store = new TemporaryStore();
        var repository = CreateRepository(store);
        var authority = CreateAuthority();
        await repository.CreateAsync(authority);
        var path = store.Paths.GetExecutionRunAuthorityFile(authority.ProjectId, authority.RunId);
        await File.WriteAllTextAsync(path, "{ not valid json");

        var read = await repository.GetAsync(authority.ProjectId, authority.RunId);

        Assert.Equal(ExecutionRunAuthorityReadState.Invalid, read.State);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task ReadIoFailure_IsUnavailable_AndDoesNotMutateAuthority()
    {
        using var store = new TemporaryStore();
        var repository = CreateRepository(store);
        var authority = CreateAuthority();
        await repository.CreateAsync(authority);
        var path = store.Paths.GetExecutionRunAuthorityFile(authority.ProjectId, authority.RunId);
        var original = await File.ReadAllBytesAsync(path);
        store.Files.ReadFailureInjector = _ => new IOException("synthetic authority read failure");

        var read = await repository.GetAsync(authority.ProjectId, authority.RunId);

        Assert.Equal(ExecutionRunAuthorityReadState.Unavailable, read.State);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task ConcurrentCreate_IsCreateOnce()
    {
        using var store = new TemporaryStore();
        var repository = CreateRepository(store);
        var authority = CreateAuthority();
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => repository.CreateAsync(authority)));

        Assert.Equal(1, results.Count(value => value.Status == ExecutionRunAuthorityRepositoryWriteStatus.Created));
        Assert.Equal(11, results.Count(value => value.Status == ExecutionRunAuthorityRepositoryWriteStatus.InputCheckpointConflict));
        Assert.All(results, value => Assert.NotEqual(ExecutionRunAuthorityRepositoryWriteStatus.Unavailable, value.Status));
    }

    [Fact]
    public async Task SameReadyCheckpoint_DifferentRunId_IsDurablyClaimedOnce()
    {
        using var store = new TemporaryStore();
        var repository = CreateRepository(store);
        var first = CreateAuthority();
        var second = CreateAuthority(first.ProjectId, Guid.NewGuid(), inputCheckpoint: first.InputRecoveryCheckpointReference);

        Assert.Equal(ExecutionRunAuthorityRepositoryWriteStatus.Created, (await repository.CreateAsync(first)).Status);
        Assert.Equal(ExecutionRunAuthorityRepositoryWriteStatus.InputCheckpointConflict, (await repository.CreateAsync(second)).Status);
        var claim = await repository.GetByInputCheckpointAsync(first.ProjectId, first.InputRecoveryCheckpointReference);
        Assert.True(claim.IsValid);
        Assert.Equal(first.RunId, claim.Authority!.RunId);
    }

    [Fact]
    public void AuthorityRecord_ContainsNoOutputOrCredentialFields()
    {
        var authority = CreateAuthority();
        var json = JsonSerializer.Serialize(ExecutionRunAuthorityRecord.FromApplication(authority), JsonFileStore.SerializerOptions);

        Assert.DoesNotContain("prompt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("output", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonExecutionRunAuthorityRepository CreateRepository(TemporaryStore store) =>
        new(store.Paths, store.Files, NullLogger<JsonExecutionRunAuthorityRepository>.Instance);

    internal static ExecutionRunAuthority CreateAuthority(
        Guid? projectId = null,
        Guid? runId = null,
        string adapterIdentifier = "test-adapter",
        RecoveryCheckpointReference? inputCheckpoint = null)
    {
        var project = projectId ?? Guid.NewGuid();
        var contract = new PlanningExecutionContractReference(Guid.NewGuid(), 1, 1, new string('1', 64));
        var graph = new WorkGraphReference(Guid.NewGuid(), 1, new string('2', 64));
        var handoff = new HandoffPackageReference(Guid.NewGuid(), 1, new string('3', 64));
        var routing = new RoutingDecisionReference(Guid.NewGuid(), 1, new string('4', 64));
        var plan = new WorkspacePreparationPlanReference(Guid.NewGuid(), 1, new string('5', 64), project);
        return new(
            project,
            runId ?? Guid.NewGuid(),
            DateTimeOffset.Parse("2026-08-28T10:00:00+00:00"),
            contract,
            graph,
            Guid.NewGuid(),
            handoff,
            routing,
            plan,
            Guid.NewGuid(),
            @"C:\APO-managed\workspace",
            new string('6', 64),
            inputCheckpoint ?? new RecoveryCheckpointReference(Guid.NewGuid(), 1, new string('7', 64)),
            Guid.NewGuid(),
            "TestProvider",
            "TestModel",
            AgentConnectionMode.Cli,
            adapterIdentifier,
            new ExecutionBudgetEnvelope(1, 1, toolInvocations: 10, modelTurns: 2));
    }
}
