using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIUsageMonitor.Application.Approvals;
using AIUsageMonitor.Application.Delivery;
using AIUsageMonitor.Application.RemoteEvidence;
using AIUsageMonitor.Application.Security;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.Validation;
using AIUsageMonitor.Providers.Remote;
using Microsoft.Extensions.Http;

namespace AIUsageMonitor.Provider.Tests;

public sealed class ControlledDeliveryAdapterTests
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string BaseSha = "1111111111111111111111111111111111111111";
    private const string HeadSha = "2222222222222222222222222222222222222222";
    private const string MergeSha = "3333333333333333333333333333333333333333";

    [Fact]
    public async Task GitHubFlow_UsesBoundedRoutesAndMergeCommit()
    {
        var handler = new DeliveryHandler { Provider = RemoteRepositoryProvider.GitHub };
        var adapter = new GitHubRemoteSourceControlDeliveryAdapter(new FakeEvidenceProvider(RemoteRepositoryProvider.GitHub, handler), new SingleClientFactory(handler), new EmptyCredentials());

        var create = await adapter.MutateAsync(Command(RemoteRepositoryProvider.GitHub, "https://github.com/owner/repo.git", null, SourceControlDeliveryOperationKind.CreateDraftPullRequest, handler), defaultEvidence(handler));
        Assert.Equal(SourceControlDeliveryStatus.Verified, create.Status);
        Assert.Equal("42", create.PullRequestId);

        var target = Target(RemoteRepositoryProvider.GitHub, "https://github.com/owner/repo.git", "42");
        var update = await adapter.MutateAsync(Command(target, SourceControlDeliveryOperationKind.UpdatePullRequestMetadata, handler), defaultEvidence(handler));
        var reviewers = await adapter.MutateAsync(Command(target, SourceControlDeliveryOperationKind.RequestReviewers, handler, reviewers: ["octocat"]), defaultEvidence(handler));
        var comment = await adapter.MutateAsync(Command(target, SourceControlDeliveryOperationKind.AddDeliveryComment, handler, comment: "APO-63 delivery evidence"), defaultEvidence(handler));
        var ready = await adapter.MutateAsync(Command(target, SourceControlDeliveryOperationKind.MarkReadyForReview, handler, highRisk: true), defaultEvidence(handler));
        var merge = await adapter.MutateAsync(Command(target, SourceControlDeliveryOperationKind.MergePullRequest, handler, highRisk: true), defaultEvidence(handler));

        Assert.Equal(SourceControlDeliveryStatus.Verified, update.Status);
        Assert.Equal(SourceControlDeliveryStatus.Verified, reviewers.Status);
        Assert.Equal(SourceControlDeliveryStatus.Verified, comment.Status);
        Assert.Equal(SourceControlDeliveryStatus.Verified, ready.Status);
        Assert.Equal(SourceControlDeliveryStatus.Verified, merge.Status);
        Assert.Equal(MergeSha, merge.MergeCommitSha);
        Assert.Contains(handler.Requests, request => request.Method == HttpMethod.Put && request.Path.EndsWith("/pulls/42/merge", StringComparison.Ordinal));
        var mergeRequest = handler.Requests.Single(request => request.Path.EndsWith("/pulls/42/merge", StringComparison.Ordinal));
        Assert.Contains("\"merge_method\":\"merge\"", mergeRequest.Body, StringComparison.Ordinal);
        Assert.Contains($"\"sha\":\"{HeadSha}\"", mergeRequest.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("force", string.Join("\n", handler.Requests.Select(request => request.Body)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AzureCompletion_UsesNoFastForwardAndNeverBypassesPolicy()
    {
        var handler = new DeliveryHandler { Provider = RemoteRepositoryProvider.AzureRepos };
        var adapter = new AzureReposRemoteSourceControlDeliveryAdapter(new FakeEvidenceProvider(RemoteRepositoryProvider.AzureRepos, handler), new SingleClientFactory(handler), new StaticCredentials());
        var target = Target(RemoteRepositoryProvider.AzureRepos, "https://dev.azure.com/org/project/_git/repository", "7");

        var result = await adapter.MutateAsync(Command(target, SourceControlDeliveryOperationKind.MergePullRequest, handler, highRisk: true, credentialReference: "azure:test"), defaultEvidence(handler));

        Assert.Equal(SourceControlDeliveryStatus.Verified, result.Status);
        Assert.Equal(MergeSha, result.MergeCommitSha);
        var request = Assert.Single(handler.Requests, value => value.Path.Contains("pullRequests/7", StringComparison.Ordinal) && value.Method == HttpMethod.Patch);
        Assert.Contains("\"mergeStrategy\":\"noFastForward\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"deleteSourceBranch\":false", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"bypassPolicy\":false", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("autoComplete", request.Body, StringComparison.OrdinalIgnoreCase);
    }

    private static SourceControlDeliveryCommand Command(RemoteRepositoryProvider provider, string url, string? pullRequestId, SourceControlDeliveryOperationKind operation, DeliveryHandler handler) =>
        Command(Target(provider, url, pullRequestId), operation, handler, highRisk: operation is SourceControlDeliveryOperationKind.MarkReadyForReview or SourceControlDeliveryOperationKind.MergePullRequest);

    private static SourceControlDeliveryCommand Command(SourceControlDeliveryTarget target, SourceControlDeliveryOperationKind operation, DeliveryHandler handler, IReadOnlyList<string>? reviewers = null, string? comment = null, bool highRisk = false, string? credentialReference = null)
    {
        var evidence = new SourceControlDeliveryEvidence(defaultEvidence(handler).Fingerprint, HeadSha, BaseSha,
            highRisk ? new ValidationGateDecisionReference(Guid.NewGuid(), 1, new string('a', 64)) : null,
            highRisk ? Guid.NewGuid() : null, highRisk ? Guid.NewGuid() : null, highRisk ? Guid.NewGuid() : null,
            highRisk ? new HumanApprovalEvidenceRevision([new HumanApprovalEvidenceReference("validation", "validation:test")]) : null,
            highRisk ? "policy:v1" : null);
        return new(Guid.NewGuid(), ProjectId, "APO-63", new PlanningExecutionContractReference(Guid.NewGuid(), 1, 1, new string('b', 64)), operation, target,
            "test", new string('c', 64), "audit:test", evidence, credentialReference,
            pullRequestTitle: "APO-63 test PR", pullRequestBody: "bounded body", deliveryComment: comment, reviewers: reviewers ?? []);
    }

    private static SourceControlDeliveryTarget Target(RemoteRepositoryProvider provider, string url, string? pullRequestId) =>
        new(provider, url, provider == RemoteRepositoryProvider.GitHub ? "1" : "repo-guid", provider == RemoteRepositoryProvider.GitHub ? "owner/repo" : "org/project/repository", "main", BaseSha, "task", HeadSha, pullRequestId);

    private static SourceControlRemoteEvidence defaultEvidence(DeliveryHandler handler) =>
        new(new RemoteRepositoryEvidence(ProjectId, RemoteEvidenceState.Available,
            handler.Provider == RemoteRepositoryProvider.GitHub ? RemoteEvidenceSource.GitHubRest : RemoteEvidenceSource.AzureDevOpsRest,
            DateTimeOffset.UtcNow,
            new RemoteRepositoryIdentity(handler.Provider, handler.Provider == RemoteRepositoryProvider.GitHub ? RemoteEvidenceSource.GitHubRest : RemoteEvidenceSource.AzureDevOpsRest,
                handler.Provider == RemoteRepositoryProvider.GitHub ? "1" : "repo-guid", handler.Provider == RemoteRepositoryProvider.GitHub ? "owner/repo" : "org/project/repository", handler.Provider == RemoteRepositoryProvider.GitHub ? "owner" : "org", handler.Provider == RemoteRepositoryProvider.GitHub ? "repo" : "repository", "main"),
            RemoteEvidenceState.Available,
            new RemoteBranchEvidence("task", HeadSha, false), RemoteEvidenceState.Available,
            handler.PullRequestId is null ? null : new RemotePullRequestEvidence(handler.PullRequestId, handler.PullRequestState, handler.IsDraft, "task", "main", HeadSha, BaseSha, RemoteMergeability.Available),
            handler.PullRequestId is null ? RemoteEvidenceState.NotConfigured : RemoteEvidenceState.Available),
            new RemoteBranchEvidence("task", HeadSha, false), new RemoteBranchEvidence("main", BaseSha, true),
            handler.PullRequestId is null ? null : new RemotePullRequestEvidence(handler.PullRequestId, handler.PullRequestState, handler.IsDraft, "task", "main", HeadSha, BaseSha, RemoteMergeability.Available));

    private sealed class FakeEvidenceProvider(RemoteRepositoryProvider provider, DeliveryHandler handler) : IRemoteRepositoryEvidenceProvider
    {
        public RemoteRepositoryProvider Provider => provider;

        public Task<RemoteRepositoryEvidence> InspectAsync(RemoteRepositoryEvidenceRequest request, CancellationToken cancellationToken = default)
        {
            var source = provider == RemoteRepositoryProvider.GitHub ? RemoteEvidenceSource.GitHubRest : RemoteEvidenceSource.AzureDevOpsRest;
            var branch = request.RequestedBranch?.Equals("main", StringComparison.Ordinal) == true ? new RemoteBranchEvidence("main", BaseSha, true) : new RemoteBranchEvidence("task", HeadSha, false);
            var hasPr = request.PullRequestNumber.HasValue;
            var pr = hasPr ? new RemotePullRequestEvidence(request.PullRequestNumber!.Value.ToString(), handler.PullRequestState, handler.IsDraft, "task", "main", HeadSha, BaseSha, RemoteMergeability.Available) : null;
            return Task.FromResult(new RemoteRepositoryEvidence(ProjectId, RemoteEvidenceState.Available, source, DateTimeOffset.UtcNow,
                new RemoteRepositoryIdentity(provider, source, provider == RemoteRepositoryProvider.GitHub ? "1" : "repo-guid", provider == RemoteRepositoryProvider.GitHub ? "owner/repo" : "org/project/repository", provider == RemoteRepositoryProvider.GitHub ? "owner" : "org", provider == RemoteRepositoryProvider.GitHub ? "repo" : "repository", "main"),
                RemoteEvidenceState.Available, branch, RemoteEvidenceState.Available, pr, hasPr ? RemoteEvidenceState.Available : RemoteEvidenceState.NotConfigured,
                reviewState: hasPr ? RemoteEvidenceState.Available : RemoteEvidenceState.NotConfigured));
        }
    }

    private sealed class DeliveryHandler : HttpMessageHandler
    {
        public RemoteRepositoryProvider Provider { get; init; }
        public string? PullRequestId { get; private set; }
        public string PullRequestState { get; private set; } = "open";
        public bool IsDraft { get; private set; } = true;
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new(request.Method, request.RequestUri!.AbsolutePath + request.RequestUri.Query, body));
            if (Provider == RemoteRepositoryProvider.GitHub)
            {
                if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath.EndsWith("/pulls", StringComparison.Ordinal))
                {
                    PullRequestId = "42";
                    return Json("{\"number\":42,\"draft\":true,\"base\":{\"ref\":\"main\"},\"head\":{\"ref\":\"task\",\"sha\":\"" + HeadSha + "\"}}");
                }
                if (request.Method == HttpMethod.Patch && body.Contains("\"draft\":false", StringComparison.Ordinal)) IsDraft = false;
                if (request.Method == HttpMethod.Put && request.RequestUri.AbsolutePath.EndsWith("/merge", StringComparison.Ordinal)) { PullRequestState = "merged"; return Json("{\"merged\":true,\"sha\":\"" + MergeSha + "\"}"); }
                return Json("{}");
            }
            if (request.Method == HttpMethod.Patch && body.Contains("\"status\":\"completed\"", StringComparison.Ordinal)) { PullRequestId ??= "7"; PullRequestState = "completed"; return Json("{\"pullRequestId\":7,\"status\":\"completed\",\"lastMergeCommit\":{\"commitId\":\"" + MergeSha + "\"}}"); }
            return Json("{}");
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string Body);

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class EmptyCredentials : ISecureCredentialStore
    {
        public Task StoreAsync(string credentialReference, string secret, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> RetrieveAsync(string credentialReference, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task RemoveAsync(string credentialReference, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StaticCredentials : ISecureCredentialStore
    {
        public Task StoreAsync(string credentialReference, string secret, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> RetrieveAsync(string credentialReference, CancellationToken cancellationToken = default) => Task.FromResult<string?>("token");
        public Task RemoveAsync(string credentialReference, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
