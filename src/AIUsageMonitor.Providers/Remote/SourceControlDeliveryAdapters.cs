using System.Net.Http.Headers;
using System.Text.Json;
using AIUsageMonitor.Application.Delivery;
using AIUsageMonitor.Application.RemoteEvidence;
using AIUsageMonitor.Application.Security;

namespace AIUsageMonitor.Providers.Remote;

public abstract class RemoteSourceControlDeliveryAdapterBase : IRemoteSourceControlDeliveryAdapter
{
    private readonly IRemoteRepositoryEvidenceProvider _evidence;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecureCredentialStore _credentials;

    protected RemoteSourceControlDeliveryAdapterBase(
        IRemoteRepositoryEvidenceProvider evidence,
        IHttpClientFactory httpClientFactory,
        ISecureCredentialStore credentials)
    {
        _evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
    }

    public abstract RemoteRepositoryProvider Provider { get; }
    protected abstract string ProviderName { get; }
    protected abstract RemoteEvidenceSource EvidenceSource { get; }
    protected abstract string HttpClientName { get; }

    public async Task<SourceControlRemoteEvidence> ReadAsync(SourceControlDeliveryCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        try
        {
            if (command.Target.Provider != Provider) return Failure(command, "The command provider does not match the adapter.");
            var head = await InspectAsync(command, command.Target.HeadRef, null, cancellationToken).ConfigureAwait(false);
            var baseEvidence = string.Equals(command.Target.BaseRef, command.Target.HeadRef, StringComparison.Ordinal)
                ? head
                : await InspectAsync(command, command.Target.BaseRef, null, cancellationToken).ConfigureAwait(false);
            RemoteRepositoryEvidence pullRequestEvidence = baseEvidence;
            if (command.Target.PullRequestId is { } pullRequestId && int.TryParse(pullRequestId, out var pullRequestNumber) && pullRequestNumber > 0)
                pullRequestEvidence = await InspectAsync(command, command.Target.BaseRef, pullRequestNumber, cancellationToken).ConfigureAwait(false);
            return new SourceControlRemoteEvidence(
                pullRequestEvidence.RepositoryState == RemoteEvidenceState.Available ? pullRequestEvidence : head,
                head.Branch,
                baseEvidence.Branch,
                pullRequestEvidence.PullRequest);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
        {
            return Failure(command, "The remote evidence request was invalid or malformed.");
        }
    }

    public async Task<SourceControlRemoteMutationResult> MutateAsync(
        SourceControlDeliveryCommand command,
        SourceControlRemoteEvidence currentEvidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentEvidence);
        if (command.Target.Provider != Provider)
            return new(SourceControlDeliveryStatus.InvalidAuthority, "The command provider does not match the adapter.");
        var authorization = await ResolveAuthorizationAsync(command, cancellationToken).ConfigureAwait(false);
        if (authorization.State is not RemoteEvidenceState.Available)
            return new(SourceControlDeliveryStatus.Blocked, authorization.Error);
        return await MutateCoreAsync(command, currentEvidence, authorization.Value!, cancellationToken).ConfigureAwait(false);
    }

    protected HttpClient Client => _httpClientFactory.CreateClient(HttpClientName);

    protected abstract Task<SourceControlRemoteMutationResult> MutateCoreAsync(
        SourceControlDeliveryCommand command,
        SourceControlRemoteEvidence currentEvidence,
        AuthenticationHeaderValue authorization,
        CancellationToken cancellationToken);

    protected abstract bool TryGetTarget(SourceControlDeliveryCommand command, out Uri? repositoryApi, out string? validationError);

    protected async Task<SourceControlRemoteEvidence> ReadAfterMutationAsync(SourceControlDeliveryCommand command, string? pullRequestId, CancellationToken cancellationToken)
    {
        var rebound = pullRequestId is null ? command : command.WithTarget(command.Target.WithPullRequest(pullRequestId));
        return await ReadAsync(rebound, cancellationToken).ConfigureAwait(false);
    }

    protected async Task<RemoteDeliveryHttpResult> SendAsync(
        HttpMethod method,
        Uri uri,
        AuthenticationHeaderValue authorization,
        object? body,
        CancellationToken cancellationToken) =>
        await RemoteDeliveryHttp.SendJsonAsync(Client, method, uri, authorization, body, cancellationToken).ConfigureAwait(false);

    protected static bool TryDocument(string? body, out JsonDocument? document) =>
        (document = RemoteEvidenceJson.Parse(body, out _)) is not null;

    protected static string? Text(JsonElement element, string propertyName) => RemoteEvidenceJson.String(element, propertyName);

    protected static SourceControlRemoteMutationResult MapFailure(RemoteDeliveryHttpResult result) =>
        result.OutcomeUncertain
            ? new(SourceControlDeliveryStatus.ReconciliationRequired, result.ErrorMessage, MutationSent: true, MayHaveModifiedRemote: true)
            : new(result.State == RemoteEvidenceState.Partial ? SourceControlDeliveryStatus.Conflict : SourceControlDeliveryStatus.Failed, result.ErrorMessage, MutationSent: true);

    protected async Task<(RemoteEvidenceState State, AuthenticationHeaderValue? Value, string? Error)> ResolveAuthorizationAsync(
        SourceControlDeliveryCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.CredentialReference))
            return Provider == RemoteRepositoryProvider.GitHub
                ? (RemoteEvidenceState.Available, null, null)
                : (RemoteEvidenceState.AuthenticationRequired, null, "The provider requires a configured credential reference.");
        var token = await _credentials.RetrieveAsync(command.CredentialReference, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token)) return (RemoteEvidenceState.AuthenticationRequired, null, "The configured credential is missing.");
        return Provider == RemoteRepositoryProvider.AzureRepos
            ? (RemoteEvidenceState.Available, new AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(":" + token))), null)
            : (RemoteEvidenceState.Available, new AuthenticationHeaderValue("Bearer", token), null);
    }

    private async Task<RemoteRepositoryEvidence> InspectAsync(SourceControlDeliveryCommand command, string branch, int? pullRequestNumber, CancellationToken cancellationToken)
    {
        var request = new RemoteRepositoryEvidenceRequest(
            command.ProjectId, ProviderName, command.Target.RepositoryUrl, command.Target.ProviderRepositoryId,
            requestedBranch: branch, pullRequestNumber: pullRequestNumber, credentialReference: command.CredentialReference);
        return await _evidence.InspectAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private SourceControlRemoteEvidence Failure(SourceControlDeliveryCommand command, string message)
    {
        var evidence = new RemoteRepositoryEvidence(command.ProjectId, RemoteEvidenceState.InvalidResponse, EvidenceSource, DateTimeOffset.UtcNow, safeErrorMessage: message);
        return new(evidence, null, null);
    }
}

public sealed class GitHubRemoteSourceControlDeliveryAdapter : RemoteSourceControlDeliveryAdapterBase
{
    public GitHubRemoteSourceControlDeliveryAdapter(
        IRemoteRepositoryEvidenceProvider evidence,
        IHttpClientFactory httpClientFactory,
        ISecureCredentialStore credentials)
        : base(evidence, httpClientFactory, credentials)
    {
    }

    public GitHubRemoteSourceControlDeliveryAdapter(
        GitHubRemoteRepositoryEvidenceProvider githubEvidence,
        IHttpClientFactory httpClientFactory,
        ISecureCredentialStore credentials)
        : base(githubEvidence, httpClientFactory, credentials)
    {
    }

    public override RemoteRepositoryProvider Provider => RemoteRepositoryProvider.GitHub;
    protected override string ProviderName => "GitHub";
    protected override RemoteEvidenceSource EvidenceSource => RemoteEvidenceSource.GitHubRest;
    protected override string HttpClientName => GitHubRemoteRepositoryEvidenceProvider.HttpClientName;

    protected override bool TryGetTarget(SourceControlDeliveryCommand command, out Uri? repositoryApi, out string? validationError)
    {
        repositoryApi = null;
        validationError = null;
        if (!RemoteEvidenceUrl.TryGitHub(command.Target.RepositoryUrl, out var target) || target is null)
        {
            validationError = "The repository URL is not a validated GitHub identity.";
            return false;
        }
        repositoryApi = target.Api(string.Empty);
        return true;
    }

    protected override async Task<SourceControlRemoteMutationResult> MutateCoreAsync(SourceControlDeliveryCommand command, SourceControlRemoteEvidence currentEvidence, AuthenticationHeaderValue authorization, CancellationToken cancellationToken)
    {
        if (!TryGetTarget(command, out var repositoryApi, out var error) || repositoryApi is null)
            return new(SourceControlDeliveryStatus.InvalidAuthority, error);
        if (!RemoteEvidenceUrl.TryGitHub(command.Target.RepositoryUrl, out var target) || target is null)
            return new(SourceControlDeliveryStatus.InvalidAuthority, "The repository URL is not a validated GitHub identity.");
        var number = command.Target.PullRequestId;
        RemoteDeliveryHttpResult response;
        switch (command.OperationKind)
        {
            case SourceControlDeliveryOperationKind.CreateDraftPullRequest:
                response = await SendAsync(HttpMethod.Post, target.Api("pulls"), authorization, new { title = command.PullRequestTitle, body = command.PullRequestBody ?? string.Empty, head = command.Target.HeadRef, @base = command.Target.BaseRef, draft = true }, cancellationToken).ConfigureAwait(false);
                if (response.State is not RemoteEvidenceState.Available) return MapFailure(response);
                if (!TryParseGitHubPullRequest(response.Body, command, requireDraft: true, out var createdId, out var parseError))
                    return new(SourceControlDeliveryStatus.Failed, parseError, MutationSent: true);
                var createdEvidence = await ReadAfterMutationAsync(command, createdId, cancellationToken).ConfigureAwait(false);
                return VerifyCreated(command, createdEvidence, createdId);
            case SourceControlDeliveryOperationKind.UpdatePullRequestMetadata:
                response = await SendAsync(HttpMethod.Patch, target.Api($"pulls/{number}"), authorization, new { title = command.PullRequestTitle, body = command.PullRequestBody ?? string.Empty }, cancellationToken).ConfigureAwait(false);
                break;
            case SourceControlDeliveryOperationKind.AddDeliveryComment:
                response = await SendAsync(HttpMethod.Post, target.Api($"issues/{number}/comments"), authorization, new { body = command.DeliveryComment }, cancellationToken).ConfigureAwait(false);
                break;
            case SourceControlDeliveryOperationKind.RequestReviewers:
                response = await SendAsync(HttpMethod.Post, target.Api($"pulls/{number}/requested_reviewers"), authorization, new { reviewers = command.Reviewers.ToArray() }, cancellationToken).ConfigureAwait(false);
                break;
            case SourceControlDeliveryOperationKind.MarkReadyForReview:
                response = await SendAsync(HttpMethod.Patch, target.Api($"pulls/{number}"), authorization, new { draft = false }, cancellationToken).ConfigureAwait(false);
                break;
            case SourceControlDeliveryOperationKind.MergePullRequest:
                response = await SendAsync(HttpMethod.Put, target.Api($"pulls/{number}/merge"), authorization, new { sha = command.Target.HeadSha, merge_method = "merge" }, cancellationToken).ConfigureAwait(false);
                if (response.State is not RemoteEvidenceState.Available) return MapFailure(response);
                if (!TryDocument(response.Body, out var mergeDocument) || mergeDocument is null)
                    return new(SourceControlDeliveryStatus.Failed, "GitHub merge response was malformed.", MutationSent: true);
                var mergeRoot = mergeDocument.RootElement;
                if (mergeRoot.TryGetProperty("merged", out var merged) && merged.ValueKind == JsonValueKind.False)
                    return new(SourceControlDeliveryStatus.Conflict, "GitHub did not accept the exact merge.", MutationSent: true);
                var mergeSha = Text(mergeRoot, "sha");
                if (string.IsNullOrWhiteSpace(mergeSha) || mergeSha.Length < 7 || !mergeSha.All(Uri.IsHexDigit))
                    return new(SourceControlDeliveryStatus.ReconciliationRequired, "GitHub did not return a concrete merge commit identity.", PullRequestId: number, MutationSent: true, MayHaveModifiedRemote: true);
                var mergedEvidence = await ReadAfterMutationAsync(command, number, cancellationToken).ConfigureAwait(false);
                return mergedEvidence.PullRequest?.State.Equals("merged", StringComparison.OrdinalIgnoreCase) == true
                    ? new(SourceControlDeliveryStatus.Verified, PullRequestId: number, MergeCommitSha: mergeSha, MutationSent: true, Evidence: mergedEvidence)
                    : new(SourceControlDeliveryStatus.ReconciliationRequired, "GitHub merge was sent but post-state is not verified.", PullRequestId: number, MergeCommitSha: mergeSha, MutationSent: true, MayHaveModifiedRemote: true, Evidence: mergedEvidence);
            default:
                return new(SourceControlDeliveryStatus.Unsupported, "The GitHub delivery operation is not supported.");
        }
        if (response.State is not RemoteEvidenceState.Available) return MapFailure(response);
        var updatedEvidence = await ReadAfterMutationAsync(command, number, cancellationToken).ConfigureAwait(false);
        if (command.OperationKind == SourceControlDeliveryOperationKind.MarkReadyForReview && updatedEvidence.PullRequest?.IsDraft != false)
            return new(SourceControlDeliveryStatus.ReconciliationRequired, "Ready mutation was sent but the pull request remains unverified.", PullRequestId: number, MutationSent: true, MayHaveModifiedRemote: true, Evidence: updatedEvidence);
        return new(SourceControlDeliveryStatus.Verified, PullRequestId: number, MutationSent: true, Evidence: updatedEvidence);
    }

    private static SourceControlRemoteMutationResult VerifyCreated(SourceControlDeliveryCommand command, SourceControlRemoteEvidence evidence, string id)
    {
        var pr = evidence.PullRequest;
        return pr is not null && pr.Id == id && pr.IsDraft == true && pr.SourceBranch == command.Target.HeadRef && pr.TargetBranch == command.Target.BaseRef && string.Equals(pr.HeadCommitId, command.Target.HeadSha, StringComparison.OrdinalIgnoreCase)
            ? new(SourceControlDeliveryStatus.Verified, PullRequestId: id, MutationSent: true, Evidence: evidence)
            : new(SourceControlDeliveryStatus.ReconciliationRequired, "Draft pull-request creation was not independently verified.", PullRequestId: id, MutationSent: true, MayHaveModifiedRemote: true, Evidence: evidence);
    }

    private static bool TryParseGitHubPullRequest(string? body, SourceControlDeliveryCommand command, bool requireDraft, out string id, out string error)
    {
        id = string.Empty;
        error = "GitHub pull-request response was malformed.";
        if (!TryDocument(body, out var document) || document is null) return false;
        try
        {
            var root = document.RootElement;
            id = RemoteEvidenceJson.Required(root, "number");
            var draft = RemoteEvidenceJson.Boolean(root, "draft");
            var baseRef = RemoteEvidenceJson.String(root.GetProperty("base"), "ref");
            var head = root.GetProperty("head");
            var headRef = RemoteEvidenceJson.String(head, "ref");
            var headSha = RemoteEvidenceJson.String(head, "sha");
            if (requireDraft && draft != true || baseRef != command.Target.BaseRef || headRef != command.Target.HeadRef || !string.Equals(headSha, command.Target.HeadSha, StringComparison.OrdinalIgnoreCase))
                return false;
            error = string.Empty;
            return int.TryParse(id, out var number) && number > 0;
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }
}

public sealed class AzureReposRemoteSourceControlDeliveryAdapter : RemoteSourceControlDeliveryAdapterBase
{
    public AzureReposRemoteSourceControlDeliveryAdapter(
        IRemoteRepositoryEvidenceProvider evidence,
        IHttpClientFactory httpClientFactory,
        ISecureCredentialStore credentials)
        : base(evidence, httpClientFactory, credentials)
    {
    }

    public AzureReposRemoteSourceControlDeliveryAdapter(
        AzureReposRemoteRepositoryEvidenceProvider azureEvidence,
        IHttpClientFactory httpClientFactory,
        ISecureCredentialStore credentials)
        : base(azureEvidence, httpClientFactory, credentials)
    {
    }

    public override RemoteRepositoryProvider Provider => RemoteRepositoryProvider.AzureRepos;
    protected override string ProviderName => "AzureRepos";
    protected override RemoteEvidenceSource EvidenceSource => RemoteEvidenceSource.AzureDevOpsRest;
    protected override string HttpClientName => AzureReposRemoteRepositoryEvidenceProvider.HttpClientName;

    protected override bool TryGetTarget(SourceControlDeliveryCommand command, out Uri? repositoryApi, out string? validationError)
    {
        repositoryApi = null;
        validationError = null;
        if (!RemoteEvidenceUrl.TryAzure(command.Target.RepositoryUrl, out var target) || target is null)
        {
            validationError = "The repository URL is not a validated Azure Repos identity.";
            return false;
        }
        repositoryApi = target.RepositoryApi();
        return true;
    }

    protected override async Task<SourceControlRemoteMutationResult> MutateCoreAsync(SourceControlDeliveryCommand command, SourceControlRemoteEvidence currentEvidence, AuthenticationHeaderValue authorization, CancellationToken cancellationToken)
    {
        if (!RemoteEvidenceUrl.TryAzure(command.Target.RepositoryUrl, out var target) || target is null)
            return new(SourceControlDeliveryStatus.InvalidAuthority, "The repository URL is not a validated Azure Repos identity.");
        var number = command.Target.PullRequestId;
        RemoteDeliveryHttpResult response;
        switch (command.OperationKind)
        {
            case SourceControlDeliveryOperationKind.CreateDraftPullRequest:
                response = await SendAsync(HttpMethod.Post, target.Api("pullrequests?api-version=7.1"), authorization, new { sourceRefName = $"refs/heads/{command.Target.HeadRef}", targetRefName = $"refs/heads/{command.Target.BaseRef}", title = command.PullRequestTitle, description = command.PullRequestBody ?? string.Empty, isDraft = true }, cancellationToken).ConfigureAwait(false);
                if (response.State is not RemoteEvidenceState.Available) return MapFailure(response);
                if (!TryParseAzurePullRequest(response.Body, command, requireDraft: true, out var createdId, out var parseError))
                    return new(SourceControlDeliveryStatus.Failed, parseError, MutationSent: true);
                var createdEvidence = await ReadAfterMutationAsync(command, createdId, cancellationToken).ConfigureAwait(false);
                return VerifyAzureCreated(command, createdEvidence, createdId);
            case SourceControlDeliveryOperationKind.UpdatePullRequestMetadata:
                response = await SendAsync(HttpMethod.Patch, target.Api($"pullRequests/{number}?api-version=7.1"), authorization, new { title = command.PullRequestTitle, description = command.PullRequestBody ?? string.Empty }, cancellationToken).ConfigureAwait(false);
                break;
            case SourceControlDeliveryOperationKind.AddDeliveryComment:
                response = await SendAsync(HttpMethod.Post, target.Api($"pullRequests/{number}/threads?api-version=7.1"), authorization, new { comments = new[] { new { content = command.DeliveryComment, commentType = 1 } }, status = 1 }, cancellationToken).ConfigureAwait(false);
                break;
            case SourceControlDeliveryOperationKind.RequestReviewers:
                foreach (var reviewer in command.Reviewers)
                {
                    response = await SendAsync(HttpMethod.Put, target.Api($"pullRequests/{number}/reviewers/{Uri.EscapeDataString(reviewer)}?api-version=7.1"), authorization, new { }, cancellationToken).ConfigureAwait(false);
                    if (response.State is not RemoteEvidenceState.Available) return MapFailure(response);
                }
                var reviewerEvidence = await ReadAfterMutationAsync(command, number, cancellationToken).ConfigureAwait(false);
                return new(SourceControlDeliveryStatus.Verified, PullRequestId: number, MutationSent: true, Evidence: reviewerEvidence);
            case SourceControlDeliveryOperationKind.MarkReadyForReview:
                response = await SendAsync(HttpMethod.Patch, target.Api($"pullRequests/{number}?api-version=7.1"), authorization, new { isDraft = false }, cancellationToken).ConfigureAwait(false);
                break;
            case SourceControlDeliveryOperationKind.MergePullRequest:
                response = await SendAsync(HttpMethod.Patch, target.Api($"pullRequests/{number}?api-version=7.1"), authorization, new
                {
                    status = "completed",
                    lastMergeSourceCommit = new { commitId = command.Target.HeadSha },
                    completionOptions = new { deleteSourceBranch = false, bypassPolicy = false, mergeStrategy = "noFastForward" }
                }, cancellationToken).ConfigureAwait(false);
                if (response.State is not RemoteEvidenceState.Available) return MapFailure(response);
                var mergeSha = ParseAzureMergeSha(response.Body);
                if (string.IsNullOrWhiteSpace(mergeSha) || mergeSha.Length < 7 || !mergeSha.All(Uri.IsHexDigit))
                    return new(SourceControlDeliveryStatus.ReconciliationRequired, "Azure did not return a concrete merge commit identity.", PullRequestId: number, MutationSent: true, MayHaveModifiedRemote: true);
                var mergedEvidence = await ReadAfterMutationAsync(command, number, cancellationToken).ConfigureAwait(false);
                return mergedEvidence.PullRequest?.State.Equals("completed", StringComparison.OrdinalIgnoreCase) == true
                    ? new(SourceControlDeliveryStatus.Verified, PullRequestId: number, MergeCommitSha: mergeSha, MutationSent: true, Evidence: mergedEvidence)
                    : new(SourceControlDeliveryStatus.ReconciliationRequired, "Azure completion was sent but post-state is not verified.", PullRequestId: number, MergeCommitSha: mergeSha, MutationSent: true, MayHaveModifiedRemote: true, Evidence: mergedEvidence);
            default:
                return new(SourceControlDeliveryStatus.Unsupported, "The Azure Repos delivery operation is not supported.");
        }
        if (response.State is not RemoteEvidenceState.Available) return MapFailure(response);
        var updatedEvidence = await ReadAfterMutationAsync(command, number, cancellationToken).ConfigureAwait(false);
        if (command.OperationKind == SourceControlDeliveryOperationKind.MarkReadyForReview && updatedEvidence.PullRequest?.IsDraft != false)
            return new(SourceControlDeliveryStatus.ReconciliationRequired, "Ready mutation was sent but the pull request remains unverified.", PullRequestId: number, MutationSent: true, MayHaveModifiedRemote: true, Evidence: updatedEvidence);
        return new(SourceControlDeliveryStatus.Verified, PullRequestId: number, MutationSent: true, Evidence: updatedEvidence);
    }

    private static SourceControlRemoteMutationResult VerifyAzureCreated(SourceControlDeliveryCommand command, SourceControlRemoteEvidence evidence, string id)
    {
        var pr = evidence.PullRequest;
        return pr is not null && pr.Id == id && pr.IsDraft == true && pr.SourceBranch == command.Target.HeadRef && pr.TargetBranch == command.Target.BaseRef && string.Equals(pr.HeadCommitId, command.Target.HeadSha, StringComparison.OrdinalIgnoreCase)
            ? new(SourceControlDeliveryStatus.Verified, PullRequestId: id, MutationSent: true, Evidence: evidence)
            : new(SourceControlDeliveryStatus.ReconciliationRequired, "Draft pull-request creation was not independently verified.", PullRequestId: id, MutationSent: true, MayHaveModifiedRemote: true, Evidence: evidence);
    }

    private static bool TryParseAzurePullRequest(string? body, SourceControlDeliveryCommand command, bool requireDraft, out string id, out string error)
    {
        id = string.Empty;
        error = "Azure pull-request response was malformed.";
        if (!TryDocument(body, out var document) || document is null) return false;
        try
        {
            var root = document.RootElement;
            id = RemoteEvidenceJson.Required(root, "pullRequestId");
            var draft = RemoteEvidenceJson.Boolean(root, "isDraft");
            var source = RemoteEvidenceJson.String(root, "sourceRefName")?.Replace("refs/heads/", string.Empty, StringComparison.Ordinal);
            var target = RemoteEvidenceJson.String(root, "targetRefName")?.Replace("refs/heads/", string.Empty, StringComparison.Ordinal);
            var sourceCommit = root.TryGetProperty("lastMergeSourceCommit", out var sourceElement) ? RemoteEvidenceJson.String(sourceElement, "commitId") : null;
            if (requireDraft && draft != true || source != command.Target.HeadRef || target != command.Target.BaseRef || !string.Equals(sourceCommit, command.Target.HeadSha, StringComparison.OrdinalIgnoreCase))
                return false;
            error = string.Empty;
            return int.TryParse(id, out var number) && number > 0;
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private static string? ParseAzureMergeSha(string? body)
    {
        if (!TryDocument(body, out var document) || document is null) return null;
        return document.RootElement.TryGetProperty("lastMergeCommit", out var merge) ? RemoteEvidenceJson.String(merge, "commitId") : null;
    }
}
