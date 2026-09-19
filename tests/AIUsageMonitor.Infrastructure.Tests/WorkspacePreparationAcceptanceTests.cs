using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Routing;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Application.Workspaces;
using AIUsageMonitor.Infrastructure.Git;
using AIUsageMonitor.Infrastructure.Persistence;
using AIUsageMonitor.Infrastructure.Workspaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageMonitor.Infrastructure.Tests;

[Collection("SystemLocalPathProbe")]
public sealed class WorkspacePreparationAcceptanceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly string BaseSha = new('b', 40);
    private static readonly string AlternateSha = new('c', 40);
    private static readonly string BaseHash = new('a', 64);
    private static readonly string AlternateHash = new('d', 64);

    private readonly string _realRepositoryRoot = Path.Combine(Path.GetTempPath(), "apo-46-acceptance-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RoutingWrongContract_IsRejectedBeforePlanPersistenceOrGitMutation()
    {
        var fixture = CreatePlanningFixture();
        var wrongReference = new PlanningExecutionContractReference(Guid.NewGuid(), 1, 1, BaseHash);
        var planStore = new CountingPlanRepository();
        var repository = new InstrumentedPlanningRepository(fixture.Repository);
        var planning = fixture.CreatePlanningService(planStore, repository, new RoutingDecisionRepositoryStub());

        var result = await planning.CreatePlanAsync(fixture.Request with { ContractReference = wrongReference });

        Assert.Equal(WorkspacePreparationPlanningStatus.ContractMismatch, result.Status);
        Assert.Equal(0, planStore.CreateCount);
        Assert.Equal(0, repository.MutationCount);
    }

    [Fact]
    public async Task RoutingDecisionContractA_RequestContractB_IsRejectedBeforePlanPersistence()
    {
        var fixture = CreatePlanningFixture();
        var contractB = CreateContract(fixture.Project.Id, fixture.Context.ContextId, fixture.Project.LocalPath);
        var decisionA = CreateRoutingDecision(fixture.Project.Id, fixture.Contract.Reference, fixture.Context.ContextId,
            fixture.Context.ContractVersion, fixture.Context.UpdatedAt);
        var planStore = new CountingPlanRepository();
        var repository = new InstrumentedPlanningRepository(fixture.Repository);
        var planning = fixture.CreatePlanningService(planStore, repository, new RoutingDecisionRepositoryStub(decisionA), contractB);

        var result = await planning.CreatePlanAsync(fixture.Request with
        {
            ContractReference = contractB.Reference,
            RoutingDecisionReference = decisionA.Reference
        });

        Assert.Equal(WorkspacePreparationPlanningStatus.ContractMismatch, result.Status);
        Assert.Equal(0, planStore.CreateCount);
        Assert.Equal(0, repository.MutationCount);
    }

    [Fact]
    public async Task RealPersistence_MissingApprovalEvidenceIsNormalBeforePreparation()
    {
        using var store = new TemporaryStore();
        var plan = CreatePlan();
        var plans = CreatePlanRepository(store);
        var receipts = CreateReceiptRepository(store);
        var approvals = CreateApprovalRepository(store);
        Assert.True((await plans.CreateAsync(plan)).Succeeded);
        var service = new WorkspacePreparationService(plans, receipts, new InstrumentedPreparationRepository(plan),
            new NoopPreparationLock(), new TestPathProvider(plan.ProposedWorkspacePath), new HandoffRedactionService(), new FixedClock(Now),
            approvalEvidence: approvals);

        var result = await service.InspectAsync(plan.Reference);

        Assert.Equal(WorkspaceRecoveryState.NotPrepared, result.State);
        Assert.Empty(Directory.EnumerateFiles(store.RootDirectory, "*.bak", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("contextId")]
    [InlineData("contextContractVersion")]
    [InlineData("contextUpdatedAt")]
    public async Task RoutingWrongContextIdentity_IsRejectedBeforePlanPersistence(string changedField)
    {
        var fixture = CreatePlanningFixture();
        var contractVersion = changedField == "contextContractVersion" ? fixture.Context.ContractVersion + 1 : fixture.Context.ContractVersion;
        var updatedAt = changedField == "contextUpdatedAt" ? fixture.Context.UpdatedAt.AddMinutes(-1) : fixture.Context.UpdatedAt;
        var decision = CreateRoutingDecision(fixture.Project.Id, fixture.Contract.Reference, fixture.Context.ContextId,
            contractVersion, updatedAt);
        if (changedField == "contextId")
            decision = CreateRoutingDecision(fixture.Project.Id, fixture.Contract.Reference, Guid.NewGuid(),
                fixture.Context.ContractVersion, fixture.Context.UpdatedAt);
        var planStore = new CountingPlanRepository();
        var repository = new InstrumentedPlanningRepository(fixture.Repository);
        var planning = fixture.CreatePlanningService(planStore, repository, new RoutingDecisionRepositoryStub(decision));

        var result = await planning.CreatePlanAsync(fixture.Request with { RoutingDecisionReference = decision.Reference });

        Assert.Equal(WorkspacePreparationPlanningStatus.ContractMismatch, result.Status);
        Assert.Equal(0, planStore.CreateCount);
        Assert.Equal(0, repository.MutationCount);
    }

    [Fact]
    public async Task RoutingWithoutRecommendation_IsRejectedBeforePlanPersistence()
    {
        var fixture = CreatePlanningFixture();
        var decision = CreateRoutingDecision(fixture.Project.Id, fixture.Contract.Reference, fixture.Context.ContextId,
            fixture.Context.ContractVersion, fixture.Context.UpdatedAt, eligible: false);
        var planStore = new CountingPlanRepository();
        var repository = new InstrumentedPlanningRepository(fixture.Repository);
        var planning = fixture.CreatePlanningService(planStore, repository, new RoutingDecisionRepositoryStub(decision));

        var result = await planning.CreatePlanAsync(fixture.Request with { RoutingDecisionReference = decision.Reference });

        Assert.Equal(WorkspacePreparationPlanningStatus.ContractMismatch, result.Status);
        Assert.Null(decision.Recommendation);
        Assert.Null(decision.SelectedAgentId);
        Assert.Equal(0, planStore.CreateCount);
    }

    [Fact]
    public async Task RoutingIneligibleSelectedCandidate_IsRejectedClosed()
    {
        var fixture = CreatePlanningFixture();
        var decision = CreateRoutingDecision(fixture.Project.Id, fixture.Contract.Reference, fixture.Context.ContextId,
            fixture.Context.ContractVersion, fixture.Context.UpdatedAt, eligible: false);
        var planStore = new CountingPlanRepository();
        var planning = fixture.CreatePlanningService(planStore, new InstrumentedPlanningRepository(fixture.Repository),
            new RoutingDecisionRepositoryStub(decision));

        var result = await planning.CreatePlanAsync(fixture.Request with { RoutingDecisionReference = decision.Reference });

        Assert.Equal(WorkspacePreparationPlanningStatus.ContractMismatch, result.Status);
        Assert.False(WorkspacePreparationPlanningService.IsUsableRoutingDecision(decision, fixture.Project.Id,
            fixture.Contract.Reference, new WorkspaceContextIdentity(fixture.Project.Id, fixture.Context.ContextId,
                fixture.Context.ContractVersion, fixture.Context.UpdatedAt)));
    }

    [Fact]
    public async Task ExactRoutingRecommendation_PermitsPlanningWithoutGitMutation()
    {
        var fixture = CreatePlanningFixture();
        var decision = CreateRoutingDecision(fixture.Project.Id, fixture.Contract.Reference, fixture.Context.ContextId,
            fixture.Context.ContractVersion, fixture.Context.UpdatedAt);
        var planStore = new CountingPlanRepository();
        var repository = new InstrumentedPlanningRepository(fixture.Repository);
        var planning = fixture.CreatePlanningService(planStore, repository, new RoutingDecisionRepositoryStub(decision));

        var result = await planning.CreatePlanAsync(fixture.Request with { RoutingDecisionReference = decision.Reference });

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(1, planStore.CreateCount);
        Assert.Equal(0, repository.MutationCount);
        Assert.Equal(decision.Reference, result.Plan!.RoutingDecisionReference);
    }

    [Theory]
    [InlineData("projectId")]
    [InlineData("workspaceId")]
    [InlineData("planId")]
    [InlineData("correlationId")]
    [InlineData("createdAt")]
    [InlineData("contextProjectId")]
    [InlineData("contextId")]
    [InlineData("contextContractVersion")]
    [InlineData("contextUpdatedAt")]
    [InlineData("contractId")]
    [InlineData("contractRevision")]
    [InlineData("contractSchemaVersion")]
    [InlineData("contractContentHash")]
    [InlineData("repositoryRoot")]
    [InlineData("commonDirectory")]
    [InlineData("sourceHead")]
    [InlineData("sourceBranch")]
    [InlineData("sourceDetached")]
    [InlineData("sourceIsClean")]
    [InlineData("changedFileCount")]
    [InlineData("workingTreeStateFingerprint")]
    [InlineData("divergenceState")]
    [InlineData("divergenceReference")]
    [InlineData("divergenceAhead")]
    [InlineData("divergenceBehind")]
    [InlineData("baseCommitSha")]
    [InlineData("workspaceBranch")]
    [InlineData("proposedWorkspacePath")]
    [InlineData("policy")]
    [InlineData("approvalRequired")]
    [InlineData("workGraphReference")]
    [InlineData("routingDecisionReference")]
    [InlineData("contentHash")]
    public async Task PersistedPlanTamperMatrix_IsIntegrityFailureAndObservational(string field)
    {
        using var store = new TemporaryStore();
        var plan = CreatePlan(includeOptionalReferences: true);
        var repository = CreatePlanRepository(store);
        Assert.True((await repository.CreateAsync(plan)).Succeeded);
        var path = store.Paths.GetWorkspacePreparationPlanFile(plan.ProjectId, plan.PlanId);

        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        TamperPlan(document["payload"]!.AsObject(), field);
        var tamperedBytes = document.ToJsonString(JsonFileStore.SerializerOptions);
        await File.WriteAllTextAsync(path, tamperedBytes);

        var first = await repository.GetAsync(plan.ProjectId, plan.PlanId);
        var bytesAfterFirstRead = await File.ReadAllTextAsync(path);
        var second = await repository.GetAsync(plan.ProjectId, plan.PlanId);
        var bytesAfterSecondRead = await File.ReadAllTextAsync(path);

        Assert.Equal(WorkspacePreparationPlanReadState.IntegrityFailure, first.State);
        Assert.Equal(WorkspacePreparationPlanReadState.IntegrityFailure, second.State);
        Assert.Equal(tamperedBytes, bytesAfterFirstRead);
        Assert.Equal(tamperedBytes, bytesAfterSecondRead);
        Assert.Empty(Directory.EnumerateFiles(store.RootDirectory, "*.bak", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(store.RootDirectory, "*.tmp", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("projectId")]
    [InlineData("workspaceId")]
    [InlineData("correlationId")]
    [InlineData("preparedAt")]
    [InlineData("planProjectId")]
    [InlineData("planId")]
    [InlineData("planSchemaVersion")]
    [InlineData("planContentHash")]
    [InlineData("approvalId")]
    [InlineData("approvalSchemaVersion")]
    [InlineData("approvalContentHash")]
    [InlineData("workspacePath")]
    [InlineData("workspaceBranch")]
    [InlineData("baseCommitSha")]
    [InlineData("actualHeadCommitSha")]
    [InlineData("repositoryIdentity")]
    [InlineData("cleanupOwnerReference")]
    [InlineData("cleanupOwner")]
    [InlineData("cleanupPolicy")]
    [InlineData("automaticCleanupAllowed")]
    [InlineData("limitation")]
    [InlineData("contentHash")]
    public async Task PersistedReceiptTamperMatrix_IsIntegrityFailureAndObservational(string field)
    {
        using var store = new TemporaryStore();
        var plan = CreatePlan();
        var evidence = CreateEvidence(plan);
        var receipt = CreateReceipt(plan, evidence);
        var repository = CreateReceiptRepository(store);
        Assert.Equal(WorkspacePreparationReceiptWriteStatus.Created, (await repository.CreateAsync(receipt)).Status);
        var path = store.Paths.GetWorkspaceReceiptFile(plan.ProjectId, plan.WorkspaceId);

        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        TamperReceipt(document["payload"]!.AsObject(), field);
        var tamperedBytes = document.ToJsonString(JsonFileStore.SerializerOptions);
        await File.WriteAllTextAsync(path, tamperedBytes);

        var first = await repository.GetAsync(plan.ProjectId, plan.WorkspaceId);
        var bytesAfterFirstRead = await File.ReadAllTextAsync(path);
        var second = await repository.GetAsync(plan.ProjectId, plan.WorkspaceId);
        var bytesAfterSecondRead = await File.ReadAllTextAsync(path);

        Assert.Equal(WorkspacePreparationReceiptReadState.IntegrityFailure, first.State);
        Assert.Equal(WorkspacePreparationReceiptReadState.IntegrityFailure, second.State);
        Assert.Equal(tamperedBytes, bytesAfterFirstRead);
        Assert.Equal(tamperedBytes, bytesAfterSecondRead);
        Assert.Empty(Directory.EnumerateFiles(store.RootDirectory, "*.bak", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(store.RootDirectory, "*.tmp", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("projectId")]
    [InlineData("workspaceId")]
    [InlineData("approvalId")]
    [InlineData("planProjectId")]
    [InlineData("planId")]
    [InlineData("planSchemaVersion")]
    [InlineData("planContentHash")]
    [InlineData("actorReference")]
    [InlineData("approvedAt")]
    [InlineData("recordedAt")]
    [InlineData("sanitizedReason")]
    [InlineData("contentHash")]
    public async Task PersistedApprovalEvidenceTamperMatrix_IsIntegrityFailureAndObservational(string field)
    {
        using var store = new TemporaryStore();
        var plan = CreatePlan();
        var evidence = CreateEvidence(plan);
        var repository = CreateApprovalRepository(store);
        Assert.Equal(WorkspacePreparationApprovalEvidenceWriteStatus.Created, (await repository.CreateAsync(evidence)).Status);
        var path = store.Paths.GetWorkspaceApprovalEvidenceFile(plan.ProjectId, plan.WorkspaceId, evidence.ApprovalId);

        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        TamperApprovalEvidence(document["payload"]!.AsObject(), field);
        var tamperedBytes = document.ToJsonString(JsonFileStore.SerializerOptions);
        await File.WriteAllTextAsync(path, tamperedBytes);

        var first = await repository.GetAsync(plan.ProjectId, plan.WorkspaceId, evidence.ApprovalId);
        var bytesAfterFirstRead = await File.ReadAllTextAsync(path);
        var second = await repository.GetAsync(plan.ProjectId, plan.WorkspaceId, evidence.ApprovalId);
        var bytesAfterSecondRead = await File.ReadAllTextAsync(path);

        Assert.Equal(WorkspacePreparationApprovalEvidenceReadState.IntegrityFailure, first.State);
        Assert.Equal(WorkspacePreparationApprovalEvidenceReadState.IntegrityFailure, second.State);
        Assert.Equal(tamperedBytes, bytesAfterFirstRead);
        Assert.Equal(tamperedBytes, bytesAfterSecondRead);
        Assert.Empty(Directory.EnumerateFiles(store.RootDirectory, "*.bak", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(store.RootDirectory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CreateOnce_PlanReceiptAndApprovalEvidencePreserveOriginalBytes()
    {
        using var store = new TemporaryStore();
        var plan = CreatePlan();
        var planRepository = CreatePlanRepository(store);
        var planFirst = await planRepository.CreateAsync(plan);
        var planPath = store.Paths.GetWorkspacePreparationPlanFile(plan.ProjectId, plan.PlanId);
        var planBytes = await File.ReadAllTextAsync(planPath);
        var planSecond = await planRepository.CreateAsync(plan);

        var evidence = CreateEvidence(plan);
        var approvalRepository = CreateApprovalRepository(store);
        var approvalFirst = await approvalRepository.CreateAsync(evidence);
        var approvalPath = store.Paths.GetWorkspaceApprovalEvidenceFile(plan.ProjectId, plan.WorkspaceId, evidence.ApprovalId);
        var indexPath = store.Paths.GetWorkspaceApprovalEvidenceByPlanFile(plan.ProjectId, plan.WorkspaceId, plan.PlanId);
        var approvalBytes = await File.ReadAllTextAsync(approvalPath);
        var indexBytes = await File.ReadAllTextAsync(indexPath);
        var approvalSecond = await approvalRepository.CreateAsync(evidence);

        var receipt = CreateReceipt(plan, evidence);
        var receiptRepository = CreateReceiptRepository(store);
        var receiptFirst = await receiptRepository.CreateAsync(receipt);
        var receiptPath = store.Paths.GetWorkspaceReceiptFile(plan.ProjectId, plan.WorkspaceId);
        var receiptBytes = await File.ReadAllTextAsync(receiptPath);
        var receiptSecond = await receiptRepository.CreateAsync(receipt);

        Assert.Equal(WorkspacePreparationPlanWriteStatus.Created, planFirst.Status);
        Assert.Equal(WorkspacePreparationPlanWriteStatus.PlanConflict, planSecond.Status);
        Assert.Equal(planBytes, await File.ReadAllTextAsync(planPath));
        Assert.Equal(WorkspacePreparationApprovalEvidenceWriteStatus.Created, approvalFirst.Status);
        Assert.Equal(WorkspacePreparationApprovalEvidenceWriteStatus.ApprovalEvidenceConflict, approvalSecond.Status);
        Assert.Equal(approvalBytes, await File.ReadAllTextAsync(approvalPath));
        Assert.Equal(indexBytes, await File.ReadAllTextAsync(indexPath));
        Assert.Equal(WorkspacePreparationReceiptWriteStatus.Created, receiptFirst.Status);
        Assert.Equal(WorkspacePreparationReceiptWriteStatus.ReceiptConflict, receiptSecond.Status);
        Assert.Equal(receiptBytes, await File.ReadAllTextAsync(receiptPath));
    }

    [Fact]
    public async Task ApprovalIndexConflict_DoesNotReplaceOriginalPlanAuthority()
    {
        using var store = new TemporaryStore();
        var plan = CreatePlan();
        var repository = CreateApprovalRepository(store);
        var original = CreateEvidence(plan);
        Assert.Equal(WorkspacePreparationApprovalEvidenceWriteStatus.Created, (await repository.CreateAsync(original)).Status);
        var indexPath = store.Paths.GetWorkspaceApprovalEvidenceByPlanFile(plan.ProjectId, plan.WorkspaceId, plan.PlanId);
        var originalIndex = await File.ReadAllTextAsync(indexPath);

        var conflicting = new WorkspacePreparationApprovalEvidence(plan.ProjectId, plan.WorkspaceId, Guid.NewGuid(), plan.Reference,
            "owner:other", Now, Now, "conflicting approval");
        var result = await repository.CreateAsync(conflicting);

        Assert.Equal(WorkspacePreparationApprovalEvidenceWriteStatus.ApprovalEvidenceConflict, result.Status);
        Assert.Equal(originalIndex, await File.ReadAllTextAsync(indexPath));
        var indexed = await repository.GetForPlanAsync(plan.ProjectId, plan.WorkspaceId, plan.PlanId);
        Assert.True(indexed.IsValid);
        Assert.Equal(original.ApprovalId, indexed.Evidence!.ApprovalId);
    }

    [Fact]
    public async Task ApprovalIndexCrashWindow_RepairsOnlyMissingIndexForSameAuthority()
    {
        using var store = new TemporaryStore();
        var plan = CreatePlan();
        var repository = CreateApprovalRepository(store);
        var evidence = CreateEvidence(plan);
        var indexDirectory = store.Paths.GetWorkspaceApprovalEvidenceByPlanDirectory(plan.ProjectId, plan.WorkspaceId, plan.PlanId);
        Directory.CreateDirectory(Path.GetDirectoryName(indexDirectory)!);
        File.WriteAllText(indexDirectory, "fixture collision");

        var first = await repository.CreateAsync(evidence);
        var evidencePath = store.Paths.GetWorkspaceApprovalEvidenceFile(plan.ProjectId, plan.WorkspaceId, evidence.ApprovalId);
        var originalEvidenceBytes = await File.ReadAllTextAsync(evidencePath);
        Assert.Equal(WorkspacePreparationApprovalEvidenceWriteStatus.Unavailable, first.Status);
        Assert.True(File.Exists(evidencePath));
        Assert.False(File.Exists(store.Paths.GetWorkspaceApprovalEvidenceByPlanFile(plan.ProjectId, plan.WorkspaceId, plan.PlanId)));

        File.Delete(indexDirectory);
        var retry = await repository.CreateAsync(evidence);

        Assert.Equal(WorkspacePreparationApprovalEvidenceWriteStatus.Created, retry.Status);
        Assert.Equal(originalEvidenceBytes, await File.ReadAllTextAsync(evidencePath));
        var indexed = await repository.GetForPlanAsync(plan.ProjectId, plan.WorkspaceId, plan.PlanId);
        Assert.True(indexed.IsValid);
        Assert.Equal(evidence.ContentHash, indexed.Evidence!.ContentHash);
    }

    [Fact]
    public async Task ApprovalIndexCrashWindow_ConflictingAuthorityCannotRepairOrReplaceIndex()
    {
        using var store = new TemporaryStore();
        var plan = CreatePlan();
        var repository = CreateApprovalRepository(store);
        var original = CreateEvidence(plan);
        Assert.Equal(WorkspacePreparationApprovalEvidenceWriteStatus.Created, (await repository.CreateAsync(original)).Status);
        var indexPath = store.Paths.GetWorkspaceApprovalEvidenceByPlanFile(plan.ProjectId, plan.WorkspaceId, plan.PlanId);
        var originalIndex = await File.ReadAllTextAsync(indexPath);
        var conflicting = new WorkspacePreparationApprovalEvidence(plan.ProjectId, plan.WorkspaceId, Guid.NewGuid(), plan.Reference,
            "owner:conflict", Now, Now, "different authority");

        var result = await repository.CreateAsync(conflicting);

        Assert.Equal(WorkspacePreparationApprovalEvidenceWriteStatus.ApprovalEvidenceConflict, result.Status);
        Assert.Equal(originalIndex, await File.ReadAllTextAsync(indexPath));
        var indexed = await repository.GetForPlanAsync(plan.ProjectId, plan.WorkspaceId, plan.PlanId);
        Assert.True(indexed.IsValid);
        Assert.Equal(original.ApprovalId, indexed.Evidence!.ApprovalId);
    }

    [Fact]
    public async Task FinalizeReceiptAfterPreparedWithoutReceipt_PerformsNoGitMutation()
    {
        var plan = CreatePlan();
        var repository = new InstrumentedPreparationRepository(plan);
        var approval = new WorkspacePreparationApproval(Guid.NewGuid(), plan.Reference, "owner:test", Now);
        var approvals = new InMemoryApprovalRepository();
        var receipts = new InMemoryReceiptRepository();
        var service = CreatePreparationService(plan, repository, receipts, approvals);
        await service.PrepareAsync(plan.Reference, approval);
        var mutationsBeforeFinalize = repository.MutationCount;

        var finalized = await service.FinalizeReceiptAsync(plan.Reference, approval);

        Assert.Equal(WorkspacePreparationStatus.AlreadyPrepared, finalized.Status);
        Assert.Equal(mutationsBeforeFinalize, repository.MutationCount);
    }

    [Fact]
    public async Task RoutingAuthorityChangeBeforeMutation_IsPlanStaleWithoutGitMutation()
    {
        var plan = CreatePlan(includeOptionalReferences: true);
        var repository = new InstrumentedPreparationRepository(plan);
        var route = new RoutingDecisionRepositoryStub();
        var contracts = new ContractRepositoryStub(CreateContract(plan.ProjectId, plan.Context.ContextId, plan.Repository.RegisteredPath));
        var contexts = new ContextResolverStub(CreateContext(plan.ProjectId, plan.Context.ContextId, plan.Repository.RegisteredPath));
        var service = new WorkspacePreparationService(new InMemoryPlanRepository(plan), new InMemoryReceiptRepository(), repository,
            new NoopPreparationLock(), new TestPathProvider(plan.ProposedWorkspacePath), new HandoffRedactionService(), new FixedClock(Now),
            contexts, contracts, route, new InMemoryApprovalRepository());
        var approval = new WorkspacePreparationApproval(Guid.NewGuid(), plan.Reference, "owner:test", Now);

        var result = await service.PrepareAsync(plan.Reference, approval);

        Assert.Equal(WorkspacePreparationStatus.PlanStale, result.Status);
        Assert.Equal(0, repository.MutationCount);
    }

    [Fact]
    public async Task RealRepositories_IsolateTwoWorkspacesAndPreserveSourceState()
    {
        if (!GitAvailable()) return;
        InitializeRepository(_realRepositoryRoot);
        var store = new TemporaryStore();
        try
        {
            var git = new GitWorkspaceRepository();
            var paths = new ManagedWorkspacePathProvider(store.Paths);
            var plans = CreatePlanRepository(store);
            var receipts = CreateReceiptRepository(store);
            var approvals = CreateApprovalRepository(store);
            var sourceBefore = await git.DiscoverAsync(_realRepositoryRoot);
            var planA = CreateRealPlan(sourceBefore, paths, Guid.NewGuid(), Guid.NewGuid(), "feature/ap46-a");
            var planB = CreateRealPlan(sourceBefore, paths, Guid.NewGuid(), Guid.NewGuid(), "feature/ap46-b");
            Assert.True((await plans.CreateAsync(planA)).Succeeded);
            Assert.True((await plans.CreateAsync(planB)).Succeeded);
            var service = new WorkspacePreparationService(plans, receipts, git, new RepositoryPreparationFileLock(store.Paths), paths,
                new HandoffRedactionService(), new FixedClock(Now), approvalEvidence: approvals);

            var preparedA = await service.PrepareAsync(planA.Reference, new WorkspacePreparationApproval(Guid.NewGuid(), planA.Reference, "owner:test", Now));
            var preparedB = await service.PrepareAsync(planB.Reference, new WorkspacePreparationApproval(Guid.NewGuid(), planB.Reference, "owner:test", Now));
            var sourceAfter = await git.DiscoverAsync(_realRepositoryRoot);

            Assert.True(preparedA.Status == WorkspacePreparationStatus.Prepared, preparedA.ErrorMessage);
            Assert.True(preparedB.Status == WorkspacePreparationStatus.Prepared, preparedB.ErrorMessage);
            Assert.Equal(sourceBefore.HeadCommitSha, sourceAfter.HeadCommitSha);
            Assert.Equal(sourceBefore.BranchName, sourceAfter.BranchName);
            Assert.Equal(sourceBefore.IsDetached, sourceAfter.IsDetached);
            Assert.Equal(sourceBefore.IsClean, sourceAfter.IsClean);
            Assert.Equal(sourceBefore.ChangedFileCount, sourceAfter.ChangedFileCount);
            Assert.Equal(sourceBefore.WorkingTreeStateFingerprint, sourceAfter.WorkingTreeStateFingerprint);
            Assert.True(HasExactWorktree(sourceAfter, planA.ProposedWorkspacePath, planA.WorkspaceBranch, planA.BaseCommitSha));
            Assert.True(HasExactWorktree(sourceAfter, planB.ProposedWorkspacePath, planB.WorkspaceBranch, planB.BaseCommitSha));
            Assert.Equal(planA.BaseCommitSha, RunGit(planA.ProposedWorkspacePath, "rev-parse", "HEAD"));
            Assert.Equal(planB.BaseCommitSha, RunGit(planB.ProposedWorkspacePath, "rev-parse", "HEAD"));
            Assert.Equal(planA.WorkspaceBranch, RunGit(planA.ProposedWorkspacePath, "branch", "--show-current"));
            Assert.Equal(planB.WorkspaceBranch, RunGit(planB.ProposedWorkspacePath, "branch", "--show-current"));
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public async Task RealRepository_SameBranchConflictsBeforeSecondWorktreeAdd()
    {
        if (!GitAvailable()) return;
        InitializeRepository(_realRepositoryRoot);
        using var store = new TemporaryStore();
        var recorder = new RecordingGitCommandRunner(new SystemGitCommandRunner());
        var git = new GitWorkspaceRepository(recorder);
        var paths = new ManagedWorkspacePathProvider(store.Paths);
        var plans = CreatePlanRepository(store);
        var receipts = CreateReceiptRepository(store);
        var approvals = CreateApprovalRepository(store);
        var initial = await git.DiscoverAsync(_realRepositoryRoot);
        var planA = CreateRealPlan(initial, paths, Guid.NewGuid(), Guid.NewGuid(), "feature/ap46-a");
        Assert.True((await plans.CreateAsync(planA)).Succeeded);
        var service = new WorkspacePreparationService(plans, receipts, git, new RepositoryPreparationFileLock(store.Paths), paths,
            new HandoffRedactionService(), new FixedClock(Now), approvalEvidence: approvals);
        Assert.Equal(WorkspacePreparationStatus.Prepared,
            (await service.PrepareAsync(planA.Reference, new WorkspacePreparationApproval(Guid.NewGuid(), planA.Reference, "owner:test", Now))).Status);

        var afterA = await git.DiscoverAsync(_realRepositoryRoot);
        var planB = CreateRealPlan(afterA, paths, Guid.NewGuid(), Guid.NewGuid(), planA.WorkspaceBranch);
        Assert.True((await plans.CreateAsync(planB)).Succeeded);
        var conflict = await service.PrepareAsync(planB.Reference, new WorkspacePreparationApproval(Guid.NewGuid(), planB.Reference, "owner:test", Now));
        var final = await git.DiscoverAsync(_realRepositoryRoot);

        Assert.Equal(WorkspacePreparationStatus.BranchConflict, conflict.Status);
        Assert.Equal(1, recorder.Commands.Count(command => IsWorktreeAdd(command)));
        Assert.NotNull(Assert.Single(final.Worktrees, value => string.Equals(value.BranchName, planA.WorkspaceBranch, StringComparison.Ordinal)));
        Assert.True(Directory.Exists(planA.ProposedWorkspacePath));
        Assert.False(Directory.Exists(planB.ProposedWorkspacePath));
    }

    [Theory]
    [InlineData("-leading")]
    [InlineData("has space")]
    [InlineData("semi;colon")]
    [InlineData("quote'value")]
    [InlineData("double\"quote")]
    [InlineData("two..dots")]
    [InlineData("brace@{bad")]
    [InlineData("ending.lock")]
    public void InvalidBranchNames_AreRejectedByConservativePlanningPrefilter(string branch)
    {
        Assert.False(WorkspacePreparationPlanningService.IsSafeBranchName(branch));
    }

    [Fact]
    public async Task ActualGitBranchValidation_RejectsInputThatPassesPrefilterButGitRejects()
    {
        if (!GitAvailable()) return;
        InitializeRepository(_realRepositoryRoot);
        var recorder = new RecordingGitCommandRunner(new SystemGitCommandRunner());
        var git = new GitWorkspaceRepository(recorder);
        var discovery = await git.DiscoverAsync(_realRepositoryRoot);
        const string branch = "feature/.hidden";

        Assert.True(WorkspacePreparationPlanningService.IsSafeBranchName(branch));
        var result = await git.ValidateBranchNameAsync(discovery.CommonDirectory!, branch);

        Assert.Equal(WorkspaceBranchValidationStatus.Invalid, result.Status);
        Assert.Contains(recorder.Commands, command => command.Contains("check-ref-format", StringComparer.Ordinal));
    }

    [Fact]
    public async Task LocalDivergence_IsPointInTimeWithExactBoundedCountsAndNoNetwork()
    {
        if (!GitAvailable()) return;
        InitializeRepository(_realRepositoryRoot);
        RunGit(_realRepositoryRoot, "branch", "base");
        File.AppendAllText(Path.Combine(_realRepositoryRoot, "tracked.txt"), "ahead\n");
        RunGit(_realRepositoryRoot, "add", "tracked.txt");
        RunGit(_realRepositoryRoot, "commit", "-m", "ahead");
        RunGit(_realRepositoryRoot, "config", "branch.main.remote", ".");
        RunGit(_realRepositoryRoot, "config", "branch.main.merge", "refs/heads/base");

        var discovery = await new GitWorkspaceRepository().DiscoverAsync(_realRepositoryRoot);

        Assert.Equal(WorkspaceDivergenceState.PointInTime, discovery.Divergence.State);
        Assert.Equal("base", discovery.Divergence.LocalUpstreamReference);
        Assert.Equal(1, discovery.Divergence.AheadCount);
        Assert.Equal(0, discovery.Divergence.BehindCount);
    }

    [Fact]
    public async Task GitCommandAllowlist_ContainsNoNetworkOrForbiddenMutation()
    {
        if (!GitAvailable()) return;
        InitializeRepository(_realRepositoryRoot);
        using var store = new TemporaryStore();
        var recorder = new RecordingGitCommandRunner(new SystemGitCommandRunner());
        var git = new GitWorkspaceRepository(recorder);
        var initial = await git.DiscoverAsync(_realRepositoryRoot);
        await git.ValidateBranchNameAsync(initial.CommonDirectory!, "feature/allowlist");
        await git.QueryLocalBranchAsync(initial.CommonDirectory!, "feature/allowlist");
        var target = new ManagedWorkspacePathProvider(store.Paths).GetWorkspacePath(Guid.NewGuid(), Guid.NewGuid());
        var mutation = await git.AddExactWorktreeAsync(initial.CommonDirectory!, "feature/allowlist", target, initial.HeadCommitSha!);
        Assert.True(mutation.Succeeded, mutation.ErrorMessage);
        await git.DiscoverAsync(_realRepositoryRoot);

        var forbidden = new[] { "fetch", "pull", "push", "reset", "clean", "checkout", "switch", "merge", "rebase", "cherry-pick", "commit", "add", "stash", "remove", "prune" };
        foreach (var command in recorder.Commands)
        {
            var text = string.Join(' ', command).ToLowerInvariant();
            foreach (var token in command)
            {
                if (IsWorktreeAdd(command) && string.Equals(token, "add", StringComparison.Ordinal)) continue;
                Assert.DoesNotContain(token.ToLowerInvariant(), forbidden);
            }
            Assert.True(IsAllowlistedGitCommand(command), $"Unexpected product Git command: {text}");
        }
    }

    [Fact]
    public async Task SameRepositoryPreparation_HasOneMutationCriticalSection()
    {
        var firstPlan = CreatePlan();
        var secondPlan = CreatePlan(repositoryOverride: firstPlan.Repository);
        var probe = new MutationProbe(blockUntilReleased: true);
        var locks = new KeyedPreparationLock();
        var repositoryA = new InstrumentedPreparationRepository(firstPlan, probe);
        var repositoryB = new InstrumentedPreparationRepository(secondPlan, probe);
        var first = CreatePreparationService(firstPlan, repositoryA, new InMemoryReceiptRepository(), new InMemoryApprovalRepository(), locks);
        var second = CreatePreparationService(secondPlan, repositoryB, new InMemoryReceiptRepository(), new InMemoryApprovalRepository(), locks);

        var firstTask = first.PrepareAsync(firstPlan.Reference, new WorkspacePreparationApproval(Guid.NewGuid(), firstPlan.Reference, "owner:a", Now));
        await probe.FirstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var secondTask = second.PrepareAsync(secondPlan.Reference, new WorkspacePreparationApproval(Guid.NewGuid(), secondPlan.Reference, "owner:b", Now));

        // A leaking critical section lets the second preparation enter while the first still holds
        // it, which completes BothEntered immediately. Racing that signal rather than sleeping a
        // fixed interval means a slow machine only makes the healthy path wait longer; it cannot
        // turn a genuine lock failure into a green run.
        var leaked = await Task.WhenAny(probe.BothEntered.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.NotSame(probe.BothEntered.Task, leaked);
        Assert.Equal(1, probe.MutationCount);
        Assert.Equal(1, probe.MaximumActive);
        probe.Release();
        await Task.WhenAll(firstTask, secondTask);
        Assert.Equal(2, probe.MutationCount);
        Assert.Equal(1, probe.MaximumActive);
    }

    [Fact]
    public async Task DifferentRepositoryPreparation_DoesNotShareMutationCriticalSection()
    {
        var firstPlan = CreatePlan();
        var secondPlan = CreatePlan();
        var probe = new MutationProbe(blockUntilBothEntered: true);
        var locks = new KeyedPreparationLock();
        var first = CreatePreparationService(firstPlan, new InstrumentedPreparationRepository(firstPlan, probe), new InMemoryReceiptRepository(), new InMemoryApprovalRepository(), locks);
        var second = CreatePreparationService(secondPlan, new InstrumentedPreparationRepository(secondPlan, probe), new InMemoryReceiptRepository(), new InMemoryApprovalRepository(), locks);

        var firstTask = first.PrepareAsync(firstPlan.Reference, new WorkspacePreparationApproval(Guid.NewGuid(), firstPlan.Reference, "owner:a", Now));
        var secondTask = second.PrepareAsync(secondPlan.Reference, new WorkspacePreparationApproval(Guid.NewGuid(), secondPlan.Reference, "owner:b", Now));
        await probe.BothEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, probe.MaximumActive);
        probe.Release();
        await Task.WhenAll(firstTask, secondTask);
    }

    [Fact]
    public async Task LockCancellation_PropagatesWithoutMutationAndDoesNotPoisonLaterAcquisition()
    {
        var plan = CreatePlan();
        var locks = new KeyedPreparationLock();
        var held = await locks.AcquireAsync(plan.Repository.CommonDirectory!);
        var repository = new InstrumentedPreparationRepository(plan);
        var service = CreatePreparationService(plan, repository, new InMemoryReceiptRepository(), new InMemoryApprovalRepository(), locks);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PrepareAsync(plan.Reference,
            new WorkspacePreparationApproval(Guid.NewGuid(), plan.Reference, "owner:test", Now), cancellation.Token));
        Assert.Equal(0, repository.MutationCount);
        await held.DisposeAsync();
        var recovered = await service.PrepareAsync(plan.Reference, new WorkspacePreparationApproval(Guid.NewGuid(), plan.Reference, "owner:test", Now));
        Assert.Equal(WorkspacePreparationStatus.Prepared, recovered.Status);
    }

    [Fact]
    public async Task ChildGitCancellation_UsesOnlySuppliedProcessTerminationSeam()
    {
        var killCount = 0;
        var output = Task.FromResult(string.Empty);
        var error = Task.FromResult(string.Empty);

        var result = await BoundedProcessWait.HandleTimeoutOrCancellationAsync(
            () => Interlocked.Increment(ref killCount), output, error, TimeSpan.FromMilliseconds(10), wasExternallyCancelled: true);

        Assert.True(result.Cancelled);
        Assert.False(result.TimedOut);
        Assert.Equal(1, killCount);
    }

    [Fact]
    public async Task PlanningUsesZeroGitMutationCount()
    {
        var fixture = CreatePlanningFixture();
        var repository = new InstrumentedPlanningRepository(fixture.Repository);
        var planStore = new CountingPlanRepository();
        var planning = fixture.CreatePlanningService(planStore, repository, new RoutingDecisionRepositoryStub());

        var result = await planning.CreatePlanAsync(fixture.Request);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(0, repository.MutationCount);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_realRepositoryRoot)) Directory.Delete(_realRepositoryRoot, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static WorkspacePreparationPlan CreatePlan(bool includeOptionalReferences = false, WorkspaceRepositoryDiscovery? repositoryOverride = null)
    {
        var projectId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), "apo-46-plan", projectId.ToString("D"), workspaceId.ToString("D"), "repo");
        var contract = new PlanningExecutionContractReference(Guid.NewGuid(), 1, 1, BaseHash);
        var workGraph = includeOptionalReferences ? new WorkGraphReference(Guid.NewGuid(), 1, BaseHash) : null;
        var routing = includeOptionalReferences ? new RoutingDecisionReference(Guid.NewGuid(), 1, BaseHash) : null;
        var repository = repositoryOverride ?? new WorkspaceRepositoryDiscovery(WorkspaceRepositoryDiscoveryStatus.Available, path, path, Path.Combine(path, ".git"),
            headCommitSha: BaseSha, branchName: "main", isClean: true,
            workingTreeStateFingerprint: WorkspacePreparationIntegrity.ComputeWorkingTreeStateFingerprint(string.Empty),
            divergence: new WorkspaceRepositoryDivergence(WorkspaceDivergenceState.NotConfigured));
        return new WorkspacePreparationPlan(projectId, workspaceId, Guid.NewGuid(), Guid.NewGuid(), Now,
            new WorkspaceContextIdentity(projectId, Guid.NewGuid(), 1, Now), contract, workGraph, workGraph is null ? null : Guid.NewGuid(), routing,
            repository, BaseSha, "feature/workspace", path, WorkspacePreparationPolicy.RequireCleanSource, true, "acceptance plan");
    }

    private static WorkspacePreparationApprovalEvidence CreateEvidence(WorkspacePreparationPlan plan) =>
        new(plan.ProjectId, plan.WorkspaceId, Guid.NewGuid(), plan.Reference, "owner:test", Now, Now, "approved");

    private static WorkspacePreparationReceipt CreateReceipt(WorkspacePreparationPlan plan, WorkspacePreparationApprovalEvidence evidence) =>
        new(plan.ProjectId, plan.WorkspaceId, plan.CorrelationId, Now, plan.Reference, plan.ProposedWorkspacePath, plan.WorkspaceBranch,
            plan.BaseCommitSha, plan.BaseCommitSha, plan.Repository.CommonDirectory!, "APO", approvalReference: new WorkspacePreparationApprovalReference(
                evidence.ApprovalId, WorkspacePreparationApprovalEvidenceSchema.CurrentVersion, evidence.ContentHash));

    private static JsonWorkspacePreparationPlanRepository CreatePlanRepository(TemporaryStore store) =>
        new(store.Paths, store.Files, NullLogger<JsonWorkspacePreparationPlanRepository>.Instance);

    private static JsonWorkspacePreparationReceiptRepository CreateReceiptRepository(TemporaryStore store) =>
        new(store.Paths, store.Files, NullLogger<JsonWorkspacePreparationReceiptRepository>.Instance);

    private static JsonWorkspacePreparationApprovalEvidenceRepository CreateApprovalRepository(TemporaryStore store) =>
        new(store.Paths, store.Files, NullLogger<JsonWorkspacePreparationApprovalEvidenceRepository>.Instance);

    private static void TamperPlan(JsonObject payload, string field)
    {
        var context = payload["context"]!.AsObject();
        var contract = payload["contractReference"]!.AsObject();
        var repository = payload["repository"]!.AsObject();
        var divergence = repository["divergence"]!.AsObject();
        switch (field)
        {
            case "projectId": payload["projectId"] = Guid.NewGuid().ToString(); break;
            case "workspaceId": payload["workspaceId"] = Guid.NewGuid().ToString(); break;
            case "planId": payload["planId"] = Guid.NewGuid().ToString(); break;
            case "correlationId": payload["correlationId"] = Guid.NewGuid().ToString(); break;
            case "createdAt": payload["createdAt"] = Now.AddMinutes(1).ToString("O"); break;
            case "contextProjectId": context["projectId"] = Guid.NewGuid().ToString(); break;
            case "contextId": context["contextId"] = Guid.NewGuid().ToString(); break;
            case "contextContractVersion": context["contractVersion"] = 2; break;
            case "contextUpdatedAt": context["updatedAt"] = Now.AddMinutes(1).ToString("O"); break;
            case "contractId": contract["id"] = Guid.NewGuid().ToString(); break;
            case "contractRevision": contract["revision"] = 2; break;
            case "contractSchemaVersion": contract["schemaVersion"] = 2; break;
            case "contractContentHash": contract["contentHash"] = AlternateHash; break;
            case "repositoryRoot": repository["repositoryRoot"] = Path.Combine(Path.GetTempPath(), "tampered-root"); break;
            case "commonDirectory": repository["commonDirectory"] = Path.Combine(Path.GetTempPath(), "tampered-common"); break;
            case "sourceHead": repository["headCommitSha"] = AlternateSha; break;
            case "sourceBranch": repository["branchName"] = "tampered"; break;
            case "sourceDetached": repository["isDetached"] = true; break;
            case "sourceIsClean": repository["isClean"] = false; break;
            case "changedFileCount": repository["changedFileCount"] = 1; break;
            case "workingTreeStateFingerprint": repository["workingTreeStateFingerprint"] = AlternateHash; break;
            case "divergenceState": divergence["state"] = "pointInTime"; break;
            case "divergenceReference": divergence["localUpstreamReference"] = "refs/heads/tampered"; break;
            case "divergenceAhead": divergence["aheadCount"] = 1; break;
            case "divergenceBehind": divergence["behindCount"] = 1; break;
            case "baseCommitSha": payload["baseCommitSha"] = AlternateSha; break;
            case "workspaceBranch": payload["workspaceBranch"] = "feature/tampered"; break;
            case "proposedWorkspacePath": payload["proposedWorkspacePath"] = Path.Combine(Path.GetTempPath(), "tampered-path"); break;
            case "policy": payload["policy"] = "allowDirtySourceWithWarning"; break;
            case "approvalRequired": payload["approvalRequired"] = false; break;
            case "workGraphReference": payload["workGraphReference"]!.AsObject()["id"] = Guid.NewGuid().ToString(); break;
            case "routingDecisionReference": payload["routingDecisionReference"]!.AsObject()["id"] = Guid.NewGuid().ToString(); break;
            case "contentHash": payload["contentHash"] = AlternateHash; break;
            default: throw new ArgumentOutOfRangeException(nameof(field), field, null);
        }
    }

    private static void TamperReceipt(JsonObject payload, string field)
    {
        var plan = payload["planReference"]!.AsObject();
        var approval = payload["approvalReference"]!.AsObject();
        switch (field)
        {
            case "projectId": payload["projectId"] = Guid.NewGuid().ToString(); break;
            case "workspaceId": payload["workspaceId"] = Guid.NewGuid().ToString(); break;
            case "correlationId": payload["correlationId"] = Guid.NewGuid().ToString(); break;
            case "preparedAt": payload["preparedAt"] = Now.AddMinutes(1).ToString("O"); break;
            case "planProjectId": plan["projectId"] = Guid.NewGuid().ToString(); break;
            case "planId": plan["id"] = Guid.NewGuid().ToString(); break;
            case "planSchemaVersion": plan["schemaVersion"] = 2; break;
            case "planContentHash": plan["contentHash"] = AlternateHash; break;
            case "approvalId": approval["approvalId"] = Guid.NewGuid().ToString(); break;
            case "approvalSchemaVersion": approval["schemaVersion"] = 2; break;
            case "approvalContentHash": approval["contentHash"] = AlternateHash; break;
            case "workspacePath": payload["workspacePath"] = Path.Combine(Path.GetTempPath(), "tampered-receipt"); break;
            case "workspaceBranch": payload["workspaceBranch"] = "feature/tampered"; break;
            case "baseCommitSha": payload["baseCommitSha"] = AlternateSha; break;
            case "actualHeadCommitSha": payload["actualHeadCommitSha"] = AlternateSha; break;
            case "repositoryIdentity": payload["repositoryIdentity"] = Path.Combine(Path.GetTempPath(), "tampered-repository"); break;
            case "cleanupOwnerReference": payload["cleanupOwnerReference"] = "owner:tampered"; break;
            case "cleanupOwner": payload["cleanupOwner"] = "external"; break;
            case "cleanupPolicy": payload["cleanupPolicy"] = 1; break;
            case "automaticCleanupAllowed": payload["automaticCleanupAllowed"] = true; break;
            case "limitation": payload["limitation"] = "tampered limitation"; break;
            case "contentHash": payload["contentHash"] = AlternateHash; break;
            default: throw new ArgumentOutOfRangeException(nameof(field), field, null);
        }
    }

    private static void TamperApprovalEvidence(JsonObject payload, string field)
    {
        var plan = payload["planReference"]!.AsObject();
        switch (field)
        {
            case "projectId": payload["projectId"] = Guid.NewGuid().ToString(); break;
            case "workspaceId": payload["workspaceId"] = Guid.NewGuid().ToString(); break;
            case "approvalId": payload["approvalId"] = Guid.NewGuid().ToString(); break;
            case "planProjectId": plan["projectId"] = Guid.NewGuid().ToString(); break;
            case "planId": plan["id"] = Guid.NewGuid().ToString(); break;
            case "planSchemaVersion": plan["schemaVersion"] = 2; break;
            case "planContentHash": plan["contentHash"] = AlternateHash; break;
            case "actorReference": payload["actorReference"] = "owner:tampered"; break;
            case "approvedAt": payload["approvedAt"] = Now.AddMinutes(1).ToString("O"); break;
            case "recordedAt": payload["recordedAt"] = Now.AddMinutes(1).ToString("O"); break;
            case "sanitizedReason": payload["sanitizedReason"] = "tampered reason"; break;
            case "contentHash": payload["contentHash"] = AlternateHash; break;
            default: throw new ArgumentOutOfRangeException(nameof(field), field, null);
        }
    }

    private static PlanningFixture CreatePlanningFixture()
    {
        var projectId = Guid.NewGuid();
        var contextId = Guid.NewGuid();
        var root = Path.Combine(Path.GetTempPath(), "apo-46-planning", projectId.ToString("D"));
        var project = new Project(projectId, "APO-46 planning", root, "main", ProjectStatus.Active, Now, Now);
        var context = CreateContext(projectId, contextId, root);
        var contract = CreateContract(projectId, contextId, root);
        var repository = new WorkspaceRepositoryDiscovery(WorkspaceRepositoryDiscoveryStatus.Available, root, root, Path.Combine(root, ".git"),
            headCommitSha: BaseSha, branchName: "main", isClean: true,
            workingTreeStateFingerprint: WorkspacePreparationIntegrity.ComputeWorkingTreeStateFingerprint(string.Empty),
            divergence: new WorkspaceRepositoryDivergence(WorkspaceDivergenceState.NotConfigured));
        var request = new WorkspacePreparationRequest(projectId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), contract.Reference, BaseSha, "feature/plan");
        return new PlanningFixture(project, context, contract, repository, request);
    }

    private static RoutingDecision CreateRoutingDecision(Guid projectId, PlanningExecutionContractReference contract, Guid contextId,
        int contextVersion, DateTimeOffset updatedAt, bool eligible = true)
    {
        var agentId = Guid.NewGuid();
        var candidate = new RoutingAgentSnapshot(projectId, agentId,
            new AgentIdentity(agentId, "APO Test Agent", "provider", "model"), Now, true, [AgentRole.Executor], eligible ? ["workspace"] : ["review"], [],
            AgentConnectionMode.Api, [AgentConnectionMode.Api], AgentAvailability.Available, AgentAuthenticationState.Authenticated,
            AgentEntitlementState.VerifiedAvailable);
        var input = new RoutingInputSnapshot(projectId, contract, new RoutingContextReference(contextId, contextVersion, updatedAt),
            new RoutingTaskClassification(RoutingScopeScale.Bounded, RoutingTaskRisk.Low, RoutingBlastRadius.Local, RoutingValidationCost.Low,
                AgentRole.Executor, ["workspace"]), new RoutingPolicySnapshot("policy:apo-46", AgentRole.Executor), [candidate],
            [new RoutingCapacityEvidence(agentId, RoutingCapacityState.Sufficient, Now, Now.AddHours(1), "fixture:capacity", source: RoutingCapacityEvidenceSource.Manual)], null, Now);
        var evaluation = new RoutingDecisionEngine().Evaluate(input);
        return new RoutingDecision(projectId, Guid.NewGuid(), RoutingDecisionSchema.CurrentVersion, Now, evaluation);
    }

    private static PlanningExecutionContract CreateContract(Guid projectId, Guid contextId, string root) => new(
        projectId, Guid.NewGuid(), PlanningExecutionContractSchema.CurrentVersion, 1, Now, "planner:test", Guid.NewGuid(),
        new PlanningContextBinding(contextId, 1), new PlanningWorkItem(PlanningWorkItemSource.Jira, "APO-46", "Workspace"),
        new PlanningRepositoryTarget(PlanningRepositoryMode.LocalGit, root, "main", BaseSha),
        [new PlanningScopeClause("include", "workspace")], [new PlanningScopeClause("constraint", "exact")],
        [new PlanningScopeClause("forbid", "destructive")], [new PlanningDeliverable("workspace", "workspace", true)],
        [new PlanningValidationRequirement("tests", PlanningValidationKind.Test, "tests", true)],
        [new PlanningAcceptanceCriterion("accept", "accept", true)], [new PlanningExecutionBudget(PlanningBudgetKind.Attempts, 1)],
        [
            new PlanningStopCondition("target", PlanningStopConditionKind.ImmutableTargetMoved, "target"),
            new PlanningStopCondition("scope", PlanningStopConditionKind.ScopeViolation, "scope"),
            new PlanningStopCondition("budget", PlanningStopConditionKind.BudgetExceeded, "budget")
        ], [], null, null);

    private static ProjectContextReference CreateContext(Guid projectId, Guid contextId, string root) => new(
        projectId, contextId, 1, Now, Now,
        new ProjectRepositoryContextReference(projectId, root, RepositorySelectionState.Inspect, RepositoryVerificationStatus.AvailableClean, root, true, "main", false, [], Now),
        new ProjectTrackerContextReference(TrackerReferenceState.NotConfigured), [], new ProjectCurrentWorkReference(CurrentWorkState.NotSelected), [], null, null,
        ProjectNextSafeAction.ReadyForPlanning);

    private static RoutingDecision CreateValidRouteFor(PlanningFixture fixture) =>
        CreateRoutingDecision(fixture.Project.Id, fixture.Contract.Reference, fixture.Context.ContextId, fixture.Context.ContractVersion, fixture.Context.UpdatedAt);

    private WorkspacePreparationPlan CreateRealPlan(WorkspaceRepositoryDiscovery repository, IManagedWorkspacePathProvider paths,
        Guid projectId, Guid workspaceId, string branch) =>
        new(projectId, workspaceId, Guid.NewGuid(), Guid.NewGuid(), Now, new WorkspaceContextIdentity(projectId, Guid.NewGuid(), 1, Now),
            new PlanningExecutionContractReference(Guid.NewGuid(), 1, 1, BaseHash), null, null, null, repository, repository.HeadCommitSha!, branch,
            paths.GetWorkspacePath(projectId, workspaceId), WorkspacePreparationPolicy.RequireCleanSource, true, "real acceptance plan");

    private static bool HasExactWorktree(WorkspaceRepositoryDiscovery discovery, string path, string branch, string sha) =>
        discovery.Worktrees.Any(value => WorkspacePreparationPlanningService.SamePath(value.Path, path) &&
            string.Equals(value.BranchName, branch, StringComparison.Ordinal) && string.Equals(value.HeadCommitSha, sha, StringComparison.OrdinalIgnoreCase));

    private static void InitializeRepository(string path)
    {
        Directory.CreateDirectory(path);
        RunGit(path, "init", "-q", "-b", "main");
        RunGit(path, "config", "user.email", "apo-test@example.invalid");
        RunGit(path, "config", "user.name", "APO Test");
        File.WriteAllText(Path.Combine(path, "tracked.txt"), "initial\n");
        RunGit(path, "add", "tracked.txt");
        RunGit(path, "commit", "-m", "initial");
    }

    private static string RunGit(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = workingDirectory, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Git fixture command.");
        process.WaitForExit(10_000);
        if (!process.HasExited || process.ExitCode != 0) throw new InvalidOperationException(process.StandardError.ReadToEnd());
        return process.StandardOutput.ReadToEnd().Trim();
    }

    private static bool GitAvailable()
    {
        try { return RunGit(Path.GetTempPath(), "--version").StartsWith("git version", StringComparison.OrdinalIgnoreCase); }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    private static bool IsWorktreeAdd(IReadOnlyList<string> command) =>
        command.Count >= 4 && command.Contains("worktree") && command.Contains("add") && command.Contains("-b");

    private static bool IsAllowlistedGitCommand(IReadOnlyList<string> command)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "rev-parse", "symbolic-ref", "status", "worktree", "for-each-ref", "rev-list", "check-ref-format", "show-ref" };
        if (IsWorktreeAdd(command)) return true;
        return command.Any(value => allowed.Contains(value));
    }

    private static WorkspacePreparationService CreatePreparationService(WorkspacePreparationPlan plan, IWorkspaceRepository repository,
        IWorkspacePreparationReceiptRepository receipts, IWorkspacePreparationApprovalEvidenceRepository approvals,
        IRepositoryPreparationLock? locks = null) =>
        new(new InMemoryPlanRepository(plan), receipts, repository, locks ?? new NoopPreparationLock(), new TestPathProvider(plan.ProposedWorkspacePath),
            new HandoffRedactionService(), new FixedClock(Now), approvalEvidence: approvals);

    private sealed record PlanningFixture(Project Project, ProjectContextReference Context, PlanningExecutionContract Contract,
        WorkspaceRepositoryDiscovery Repository, WorkspacePreparationRequest Request)
    {
        public WorkspacePreparationPlanningService CreatePlanningService(CountingPlanRepository plans, InstrumentedPlanningRepository repository,
            RoutingDecisionRepositoryStub routes, PlanningExecutionContract? contractOverride = null)
        {
            return new WorkspacePreparationPlanningService(new ProjectRepositoryStub(Project), new ContextResolverStub(Context),
                new ContractRepositoryStub(contractOverride ?? Contract), routes, repository, plans, new HandoffRedactionService(), new FixedClock(Now),
                new TestPathProvider(Path.Combine(Path.GetTempPath(), "apo-46-planning", Project.Id.ToString("D"))), graphs: null);
        }
    }

    private sealed class FixedClock(DateTimeOffset value) : IClock { public DateTimeOffset UtcNow => value; }

    private sealed class TestPathProvider(string path) : IManagedWorkspacePathProvider
    {
        public string GetWorkspacePath(Guid projectId, Guid workspaceId) => path;
        public bool IsSafeManagedWorkspacePath(Guid projectId, Guid workspaceId, out string resolved, out string? errorMessage)
        { resolved = path; errorMessage = null; return true; }
    }

    private sealed class CountingPlanRepository : IWorkspacePreparationPlanRepository
    {
        public int CreateCount { get; private set; }
        public WorkspacePreparationPlan? Plan { get; private set; }
        public Task<WorkspacePreparationPlanWriteResult> CreateAsync(WorkspacePreparationPlan plan, CancellationToken cancellationToken = default)
        { CreateCount++; Plan = plan; return Task.FromResult(new WorkspacePreparationPlanWriteResult(WorkspacePreparationPlanWriteStatus.Created)); }
        public Task<WorkspacePreparationPlanReadResult> GetAsync(Guid projectId, Guid planId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Plan is not null && Plan.ProjectId == projectId && Plan.PlanId == planId
                ? new WorkspacePreparationPlanReadResult(WorkspacePreparationPlanReadState.Valid, Plan)
                : new WorkspacePreparationPlanReadResult(WorkspacePreparationPlanReadState.Missing));
    }

    private sealed class InMemoryPlanRepository(WorkspacePreparationPlan plan) : IWorkspacePreparationPlanRepository
    {
        public Task<WorkspacePreparationPlanWriteResult> CreateAsync(WorkspacePreparationPlan value, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkspacePreparationPlanWriteResult(WorkspacePreparationPlanWriteStatus.Created));
        public Task<WorkspacePreparationPlanReadResult> GetAsync(Guid projectId, Guid planId, CancellationToken cancellationToken = default) =>
            Task.FromResult(projectId == plan.ProjectId && planId == plan.PlanId
                ? new WorkspacePreparationPlanReadResult(WorkspacePreparationPlanReadState.Valid, plan)
                : new WorkspacePreparationPlanReadResult(WorkspacePreparationPlanReadState.Missing));
    }

    private sealed class InMemoryReceiptRepository : IWorkspacePreparationReceiptRepository
    {
        public WorkspacePreparationReceipt? Receipt { get; private set; }
        public Task<WorkspacePreparationReceiptWriteResult> CreateAsync(WorkspacePreparationReceipt receipt, CancellationToken cancellationToken = default)
        { Receipt = receipt; return Task.FromResult(new WorkspacePreparationReceiptWriteResult(WorkspacePreparationReceiptWriteStatus.Created)); }
        public Task<WorkspacePreparationReceiptReadResult> GetAsync(Guid projectId, Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Receipt is null ? new WorkspacePreparationReceiptReadResult(WorkspacePreparationReceiptReadState.Missing) : new(WorkspacePreparationReceiptReadState.Valid, Receipt));
    }

    private sealed class InMemoryApprovalRepository : IWorkspacePreparationApprovalEvidenceRepository
    {
        public WorkspacePreparationApprovalEvidence? Evidence { get; private set; }
        public Task<WorkspacePreparationApprovalEvidenceWriteResult> CreateAsync(WorkspacePreparationApprovalEvidence evidence, CancellationToken cancellationToken = default)
        { Evidence = evidence; return Task.FromResult(new WorkspacePreparationApprovalEvidenceWriteResult(WorkspacePreparationApprovalEvidenceWriteStatus.Created)); }
        public Task<WorkspacePreparationApprovalEvidenceReadResult> GetAsync(Guid projectId, Guid workspaceId, Guid approvalId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Evidence is not null && Evidence.ProjectId == projectId && Evidence.WorkspaceId == workspaceId && Evidence.ApprovalId == approvalId
                ? new WorkspacePreparationApprovalEvidenceReadResult(WorkspacePreparationApprovalEvidenceReadState.Valid, Evidence)
                : new WorkspacePreparationApprovalEvidenceReadResult(WorkspacePreparationApprovalEvidenceReadState.Missing));
        public Task<WorkspacePreparationApprovalEvidenceReadResult> GetForPlanAsync(Guid projectId, Guid workspaceId, Guid planId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Evidence is not null && Evidence.ProjectId == projectId && Evidence.WorkspaceId == workspaceId && Evidence.PlanReference.PlanId == planId
                ? new WorkspacePreparationApprovalEvidenceReadResult(WorkspacePreparationApprovalEvidenceReadState.Valid, Evidence)
                : new WorkspacePreparationApprovalEvidenceReadResult(WorkspacePreparationApprovalEvidenceReadState.Missing));
    }

    private sealed class InstrumentedPreparationRepository(WorkspacePreparationPlan plan, MutationProbe? probe = null) : IWorkspaceRepository, IWorkspacePreparedWorkspaceVerifier
    {
        private bool _mutated;
        public int MutationCount { get; private set; }
        public Task<WorkspaceRepositoryDiscovery> DiscoverAsync(string registeredPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(_mutated ? WithWorktree(plan.Repository, plan) : plan.Repository);
        public Task<WorkspacePreparedWorkspaceVerification> VerifyPreparedWorkspaceAsync(string workspacePath, CancellationToken cancellationToken = default)
        {
            if (!_mutated)
                return Task.FromResult(new WorkspacePreparedWorkspaceVerification(WorkspacePreparedWorkspaceVerificationStatus.WorkspaceMissing, workspacePath));

            return Task.FromResult(new WorkspacePreparedWorkspaceVerification(WorkspacePreparedWorkspaceVerificationStatus.Verified, workspacePath, true, workspacePath,
                plan.Repository.CommonDirectory, plan.BaseCommitSha, plan.WorkspaceBranch, false, true, 0,
                WorkspacePreparationIntegrity.ComputeWorkingTreeStateFingerprint(string.Empty)));
        }
        public async Task<WorkspaceRepositoryMutationResult> AddExactWorktreeAsync(string commonDirectory, string workspaceBranch, string managedWorkspacePath,
            string exactBaseCommitSha, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MutationCount++;
            if (probe is not null) await probe.EnterAsync(cancellationToken);
            _mutated = true;
            return new WorkspaceRepositoryMutationResult(true);
        }
        private static WorkspaceRepositoryDiscovery WithWorktree(WorkspaceRepositoryDiscovery source, WorkspacePreparationPlan value) =>
            new(WorkspaceRepositoryDiscoveryStatus.Available, source.RegisteredPath, source.RepositoryRoot, source.CommonDirectory, source.IsBareRepository,
                source.HeadCommitSha, source.BranchName, source.IsDetached, source.IsClean, source.ChangedFileCount,
                [new WorkspaceWorktreeEvidence(value.ProposedWorkspacePath, value.BaseCommitSha, value.WorkspaceBranch, false, false, false)],
                source.LocalBranches, workingTreeStateFingerprint: source.WorkingTreeStateFingerprint, divergence: source.Divergence);
    }

    private sealed class InstrumentedPlanningRepository(WorkspaceRepositoryDiscovery discovery) : IWorkspaceRepository, IWorkspaceBranchSafety, IWorkspacePreparedWorkspaceVerifier
    {
        public int MutationCount { get; private set; }
        public Task<WorkspaceRepositoryDiscovery> DiscoverAsync(string registeredPath, CancellationToken cancellationToken = default) => Task.FromResult(discovery);
        public Task<WorkspacePreparedWorkspaceVerification> VerifyPreparedWorkspaceAsync(string workspacePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkspacePreparedWorkspaceVerification(WorkspacePreparedWorkspaceVerificationStatus.WorkspaceMissing, workspacePath));
        public Task<WorkspaceRepositoryMutationResult> AddExactWorktreeAsync(string commonDirectory, string workspaceBranch, string managedWorkspacePath,
            string exactBaseCommitSha, CancellationToken cancellationToken = default) { MutationCount++; return Task.FromResult(new WorkspaceRepositoryMutationResult(false)); }
        public Task<WorkspaceBranchValidationResult> ValidateBranchNameAsync(string commonDirectory, string branchName, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkspaceBranchValidationResult(WorkspaceBranchValidationStatus.Valid));
        public Task<WorkspaceBranchExistenceResult> QueryLocalBranchAsync(string commonDirectory, string branchName, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkspaceBranchExistenceResult(WorkspaceBranchExistenceStatus.NotFound));
    }

    private sealed class NoopPreparationLock : IRepositoryPreparationLock
    {
        public Task<IAsyncDisposable> AcquireAsync(string repositoryIdentity, CancellationToken cancellationToken = default) => Task.FromResult<IAsyncDisposable>(new NoopAsyncDisposable());
    }
    private sealed class NoopAsyncDisposable : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }

    private sealed class KeyedPreparationLock : IRepositoryPreparationLock
    {
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new(StringComparer.OrdinalIgnoreCase);
        public async Task<IAsyncDisposable> AcquireAsync(string repositoryIdentity, CancellationToken cancellationToken = default)
        {
            var gate = _semaphores.GetOrAdd(repositoryIdentity, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            return new HeldSemaphore(gate);
        }
        private sealed class HeldSemaphore(SemaphoreSlim semaphore) : IAsyncDisposable
        { public ValueTask DisposeAsync() { semaphore.Release(); return ValueTask.CompletedTask; } }
    }

    private sealed class MutationProbe(bool blockUntilReleased = false, bool blockUntilBothEntered = false)
    {
        public TaskCompletionSource<object?> FirstEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<object?> BothEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<object?> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;
        public int MutationCount { get; private set; }
        public int MaximumActive { get; private set; }
        public async Task EnterAsync(CancellationToken cancellationToken)
        {
            MutationCount++;
            var active = Interlocked.Increment(ref _active);
            MaximumActive = Math.Max(MaximumActive, active);
            if (MutationCount == 1) FirstEntered.TrySetResult(null);
            if (MutationCount == 2) BothEntered.TrySetResult(null);
            if (blockUntilBothEntered) await BothEntered.Task.WaitAsync(cancellationToken);
            else if (blockUntilReleased) await _release.Task.WaitAsync(cancellationToken);
            Interlocked.Decrement(ref _active);
        }
        public void Release() => _release.TrySetResult(null);
    }

    private sealed class RecordingGitCommandRunner(IGitCommandRunner inner) : IGitCommandRunner
    {
        public List<IReadOnlyList<string>> Commands { get; } = [];
        public async Task<GitCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
        { Commands.Add(arguments.ToArray()); return await inner.RunAsync(arguments, cancellationToken); }
    }

    private sealed class ProjectRepositoryStub(Project project) : IProjectRepository
    {
        public Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Project>>([project]);
        public Task<Project?> GetByIdAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<Project?>(projectId == project.Id ? project : null);
        public Task UpsertAsync(Project value, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ContextResolverStub(ProjectContextReference context) : IProjectContextResolver
    {
        public Task<ProjectContextResolution> ResolveAsync(Guid projectId, CancellationToken cancellationToken = default)
        {
            var project = new Project(context.ProjectId, "APO-46", context.Repository.RegisteredLocalPath, "main", ProjectStatus.Active, Now, Now);
            return Task.FromResult(new ProjectContextResolution(ProjectContextResolutionState.Ready, new ProjectContextView(project, context, [])));
        }
    }

    private sealed class ContractRepositoryStub(PlanningExecutionContract contract) : IPlanningExecutionContractRepository
    {
        public Task<PlanningContractRepositoryWriteResult> CreateAsync(PlanningExecutionContract value, CancellationToken cancellationToken = default) => Task.FromResult(new PlanningContractRepositoryWriteResult(PlanningContractRepositoryWriteStatus.Created));
        public Task<PlanningContractReadResult> GetAsync(Guid projectId, Guid contractId, int revision, CancellationToken cancellationToken = default) => Task.FromResult(projectId == contract.ProjectId ? new PlanningContractReadResult(PlanningContractReadState.Valid, contract) : new PlanningContractReadResult(PlanningContractReadState.Missing));
        public Task<PlanningContractReadResult> GetLatestAsync(Guid projectId, Guid contractId, CancellationToken cancellationToken = default) => Task.FromResult(new PlanningContractReadResult(PlanningContractReadState.Missing));
        public Task<PlanningContractRevisionListResult> ListRevisionsAsync(Guid projectId, Guid contractId, CancellationToken cancellationToken = default) => Task.FromResult(new PlanningContractRevisionListResult(PlanningContractReadState.Valid, [contract]));
    }

    private sealed class RoutingDecisionRepositoryStub(RoutingDecision? decision = null) : IRoutingDecisionRepository
    {
        public RoutingDecision? Decision { get; } = decision;
        public Task<RoutingDecisionRepositoryWriteResult> CreateAsync(RoutingDecision value, CancellationToken cancellationToken = default) => Task.FromResult(new RoutingDecisionRepositoryWriteResult(RoutingDecisionRepositoryWriteStatus.Created));
        public Task<RoutingDecisionReadResult> GetAsync(Guid projectId, Guid decisionId, CancellationToken cancellationToken = default) => Task.FromResult(Decision is not null && Decision.ProjectId == projectId && Decision.DecisionId == decisionId ? new RoutingDecisionReadResult(RoutingDecisionReadState.Valid, Decision) : new RoutingDecisionReadResult(RoutingDecisionReadState.Missing));
    }
}
