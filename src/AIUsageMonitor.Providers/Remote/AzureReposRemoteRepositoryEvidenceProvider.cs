using System.Net.Http.Headers;
using System.Text.Json;
using AIUsageMonitor.Application.RemoteEvidence;
using AIUsageMonitor.Application.Security;
using AIUsageMonitor.Application.Time;

namespace AIUsageMonitor.Providers.Remote;

public sealed class AzureReposRemoteRepositoryEvidenceProvider : IRemoteRepositoryEvidenceProvider
{
    public const string HttpClientName = "AIUsageMonitor.Remote.AzureRepos";

    private readonly IClock _clock;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecureCredentialStore _credentials;

    public AzureReposRemoteRepositoryEvidenceProvider(
        IClock clock,
        IHttpClientFactory httpClientFactory,
        ISecureCredentialStore credentials)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
    }

    public RemoteRepositoryProvider Provider => RemoteRepositoryProvider.AzureRepos;

    public async Task<RemoteRepositoryEvidence> InspectAsync(
        RemoteRepositoryEvidenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var draft = new RemoteEvidenceDraft(request.ProjectId, RemoteEvidenceSource.AzureDevOpsRest, _clock.UtcNow);
        if (!IsAzureProvider(request.RepositoryProvider) ||
            !RemoteEvidenceUrl.TryAzure(request.RepositoryUrl, out var target) || target is null)
        {
            return draft.Failure(RemoteEvidenceState.Unsupported,
                "The configured repository is not a supported Azure Repos identity.");
        }

        try
        {
            var authorization = await ResolveAuthorizationAsync(request, cancellationToken).ConfigureAwait(false);
            if (authorization.State is not RemoteEvidenceState.Available)
            {
                return draft.Failure(authorization.State, authorization.Error!);
            }

            var auth = authorization.Value ?? throw new InvalidOperationException("Azure authorization was unavailable.");

            var client = _httpClientFactory.CreateClient(HttpClientName);
            var repositoryResponse = await RemoteEvidenceHttp.GetJsonAsync(
                client,
                target.RepositoryApi(),
                auth,
                cancellationToken).ConfigureAwait(false);
            if (repositoryResponse.State is not RemoteEvidenceState.Available)
            {
                return draft.Failure(repositoryResponse.State, repositoryResponse.ErrorMessage!);
            }

            using var repositoryDocument = RemoteEvidenceJson.Parse(repositoryResponse.Body, out var repositoryError);
            if (repositoryDocument is null)
            {
                return draft.Failure(RemoteEvidenceState.InvalidResponse, repositoryError!);
            }

            string repositoryId;
            string repositoryName;
            string defaultBranch;
            string projectName;
            string projectId;
            try
            {
                var root = repositoryDocument.RootElement;
                repositoryId = RemoteEvidenceJson.Required(root, "id");
                repositoryName = RemoteEvidenceJson.Required(root, "name");
                defaultBranch = RemoteEvidenceJson.Required(root, "defaultBranch");
                var project = root.GetProperty("project");
                projectId = RemoteEvidenceJson.Required(project, "id");
                projectName = RemoteEvidenceJson.String(project, "name") ?? target.Project;
                if (!repositoryName.Equals(target.Repository, StringComparison.OrdinalIgnoreCase) ||
                    request.RepositoryId is not null && !request.RepositoryId.Equals(repositoryId, StringComparison.OrdinalIgnoreCase) ||
                    request.RepositoryMetadata.TryGetValue("projectId", out var configuredProjectId) &&
                    !string.IsNullOrWhiteSpace(configuredProjectId) &&
                    !configuredProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase))
                {
                    return draft.Failure(RemoteEvidenceState.InvalidResponse,
                        "Azure returned a repository identity different from the configured project target.");
                }

                if (!RemoteEvidenceBranches.TryNormalize(defaultBranch, out defaultBranch))
                {
                    return draft.Failure(RemoteEvidenceState.InvalidResponse,
                        "Azure returned an invalid default branch.");
                }

                draft.Repository = new RemoteRepositoryIdentity(
                    Provider,
                    RemoteEvidenceSource.AzureDevOpsRest,
                    repositoryId,
                    $"{target.Organization}/{projectName}/{repositoryName}",
                    target.Organization,
                    repositoryName,
                    defaultBranch,
                    projectName,
                    new Uri($"https://dev.azure.com/{Uri.EscapeDataString(target.Organization)}/{Uri.EscapeDataString(projectName)}/_git/{Uri.EscapeDataString(repositoryName)}"));
                draft.RepositoryState = RemoteEvidenceState.Available;
            }
            catch (ArgumentException)
            {
                return draft.Failure(RemoteEvidenceState.InvalidResponse,
                    "Azure repository metadata was malformed.");
            }

            if (!RemoteEvidenceBranches.TryNormalize(request.RequestedBranch ?? draft.Repository!.DefaultBranch, out var branch))
            {
                draft.BranchState = RemoteEvidenceState.InvalidResponse;
                draft.Error = "The requested Azure branch was invalid.";
                return draft.Build();
            }

            var branchTarget = new AzureTarget(target.Organization, target.Project, repositoryId);
            var expectedRef = RemoteEvidenceBranches.Ref(branch);
            var branchUri = branchTarget.Api(
                $"refs?filter={Uri.EscapeDataString(expectedRef)}&%24top={RemoteEvidenceLimits.MaxItems}&api-version=7.1");
            for (var page = 0; page < RemoteEvidenceLimits.MaxPages; page++)
            {
                var branchResponse = await RemoteEvidenceHttp.GetJsonAsync(
                    client,
                    branchUri,
                    authorization.Value,
                    cancellationToken).ConfigureAwait(false);
                if (branchResponse.State is not RemoteEvidenceState.Available)
                {
                    draft.BranchState = page == 0
                        ? branchResponse.State
                        : RemoteEvidenceState.Partial;
                    draft.Error ??= branchResponse.ErrorMessage;
                    if (page > 0)
                    {
                        draft.Limitations.Add("Azure branch pagination stopped before the exact ref was proven.");
                    }

                    return draft.Build();
                }

                using var branchDocument = RemoteEvidenceJson.Parse(branchResponse.Body, out var branchError);
                if (branchDocument is null ||
                    !branchDocument.RootElement.TryGetProperty("value", out var refs) ||
                    refs.ValueKind is not JsonValueKind.Array)
                {
                    draft.BranchState = RemoteEvidenceState.InvalidResponse;
                    draft.Error ??= branchError ?? "Azure branch response was malformed.";
                    return draft.Build();
                }

                try
                {
                    var reference = refs.EnumerateArray()
                        .FirstOrDefault(value => RemoteEvidenceJson.String(value, "name") == expectedRef);
                    if (reference.ValueKind is JsonValueKind.Object)
                    {
                        var commit = RemoteEvidenceJson.Required(reference, "objectId");
                        draft.Branch = new RemoteBranchEvidence(
                            branch,
                            commit,
                            branch.Equals(draft.Repository.DefaultBranch, StringComparison.OrdinalIgnoreCase));
                        draft.BranchState = RemoteEvidenceState.Available;
                        break;
                    }
                }
                catch (ArgumentException)
                {
                    draft.BranchState = RemoteEvidenceState.InvalidResponse;
                    draft.Error ??= "Azure branch metadata was malformed.";
                    return draft.Build();
                }

                if (!branchResponse.ContinuationHeaderPresent)
                {
                    draft.BranchState = RemoteEvidenceState.Unavailable;
                    draft.Error ??= "The requested Azure branch was unavailable.";
                    return draft.Build();
                }

                if (branchResponse.ContinuationToken is null)
                {
                    draft.BranchState = RemoteEvidenceState.Partial;
                    draft.Limitations.Add("Azure branch pagination metadata was rejected before the exact ref was proven.");
                    return draft.Build();
                }

                if (page + 1 >= RemoteEvidenceLimits.MaxPages)
                {
                    draft.BranchState = RemoteEvidenceState.Partial;
                    draft.Limitations.Add("Azure branch evidence was capped before the exact ref was proven.");
                    return draft.Build();
                }

                branchUri = branchTarget.Api(
                    $"refs?filter={Uri.EscapeDataString(expectedRef)}&%24top={RemoteEvidenceLimits.MaxItems}&continuationToken={Uri.EscapeDataString(branchResponse.ContinuationToken)}&api-version=7.1");
            }

            if (draft.Branch is null)
            {
                draft.BranchState = RemoteEvidenceState.Partial;
                draft.Limitations.Add("Azure branch evidence was capped before the exact ref was proven.");
                return draft.Build();
            }

            if (request.PullRequestNumber is { } pullRequestNumber)
            {
                await ReadPullRequestAsync(draft, branchTarget, pullRequestNumber, auth, client, cancellationToken)
                    .ConfigureAwait(false);
            }

            var evidenceCommit = draft.PullRequest is null ? draft.Branch.CommitId : draft.PullRequest.HeadCommitId;
            if (evidenceCommit is not null)
            {
                await ReadCommitStatusesAsync(draft, branchTarget, evidenceCommit, auth, client, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                draft.StatusState = RemoteEvidenceState.Partial;
                draft.Limitations.Add("Azure PR head commit was unavailable; commit status evidence was not correlated.");
            }
            if (draft.PullRequest is not null)
            {
                await ReadPullRequestStatusesAsync(draft, branchTarget, draft.PullRequest.Id, auth, client, cancellationToken)
                    .ConfigureAwait(false);
            }

            var buildBranch = draft.PullRequest?.SourceBranch;
            if (draft.PullRequest is null)
            {
                buildBranch = branch;
            }

            if (evidenceCommit is null || buildBranch is null)
            {
                draft.CiState = RemoteEvidenceState.Partial;
                draft.CiResult = RemoteCiState.Unknown;
                draft.Limitations.Add("Azure PR build evidence was not correlated because its source branch and head commit were not both established.");
            }
            else
            {
                await ReadBuildsAsync(
                    draft,
                    target,
                    repositoryId,
                    buildBranch,
                    evidenceCommit,
                    auth,
                    client,
                    cancellationToken).ConfigureAwait(false);
            }
            return draft.Build();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return draft.Failure(RemoteEvidenceState.Cancelled, "Remote Azure evidence was cancelled by the caller.");
        }
        catch (ArgumentException)
        {
            return draft.Failure(RemoteEvidenceState.InvalidResponse, "Azure returned an unusable evidence response.");
        }
        catch (KeyNotFoundException)
        {
            return draft.Failure(RemoteEvidenceState.InvalidResponse, "Azure returned an incomplete evidence response.");
        }
    }

    private async Task ReadPullRequestAsync(
        RemoteEvidenceDraft draft,
        AzureTarget target,
        int number,
        AuthenticationHeaderValue authorization,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var response = await RemoteEvidenceHttp.GetJsonAsync(
            client,
            target.Api($"pullRequests/{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}?api-version=7.1"),
            authorization,
            cancellationToken).ConfigureAwait(false);
        if (response.State is not RemoteEvidenceState.Available)
        {
            draft.PullRequestState = response.State;
            draft.Error ??= response.ErrorMessage;
            return;
        }

        using var document = RemoteEvidenceJson.Parse(response.Body, out var error);
        if (document is null)
        {
            draft.PullRequestState = RemoteEvidenceState.InvalidResponse;
            draft.Error ??= error;
            return;
        }

        try
        {
            var root = document.RootElement;
            var repository = root.GetProperty("repository");
            if (RemoteEvidenceJson.String(repository, "id") is { } prRepositoryId &&
                draft.Repository is not null &&
                !prRepositoryId.Equals(draft.Repository.ProviderRepositoryId, StringComparison.OrdinalIgnoreCase))
            {
                draft.PullRequestState = RemoteEvidenceState.InvalidResponse;
                draft.Error ??= "Azure pull request repository identity did not match the configured repository.";
                return;
            }

            var lastSource = root.TryGetProperty("lastMergeSourceCommit", out var sourceCommit)
                ? RemoteEvidenceJson.String(sourceCommit, "commitId")
                : null;
            var lastTarget = root.TryGetProperty("lastMergeTargetCommit", out var targetCommit)
                ? RemoteEvidenceJson.String(targetCommit, "commitId")
                : null;
            draft.PullRequest = new RemotePullRequestEvidence(
                RemoteEvidenceJson.Required(root, "pullRequestId"),
                NormalizePullRequestState(RemoteEvidenceJson.Required(root, "status")),
                RemoteEvidenceJson.Boolean(root, "isDraft"),
                NormalizeBranch(RemoteEvidenceJson.String(root, "sourceRefName")),
                NormalizeBranch(RemoteEvidenceJson.String(root, "targetRefName")),
                lastSource,
                lastTarget,
                RemoteEvidenceJson.String(root, "mergeStatus")?.ToLowerInvariant() switch
                {
                    "succeeded" => RemoteMergeability.Available,
                    "conflicts" => RemoteMergeability.Conflicting,
                    "checking" => RemoteMergeability.Calculating,
                    _ => RemoteMergeability.Unknown
                },
                SafeAzureUri(root, "url"),
                RemoteEvidenceJson.String(root, "title"),
                RemoteEvidenceJson.String(root, "description"),
                root.TryGetProperty("lastMergeCommit", out var mergeCommit)
                    ? RemoteEvidenceJson.String(mergeCommit, "commitId")
                    : null,
                RemoteEvidenceJson.String(root, "pullRequestId"));
            draft.PullRequestState = RemoteEvidenceState.Available;

            if (root.TryGetProperty("reviewers", out var reviewers) && reviewers.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                var values = new List<RemoteReviewEvidence>();
                var truncated = false;
                foreach (var reviewer in reviewers.EnumerateArray())
                {
                    if (index++ >= RemoteEvidenceLimits.MaxItems)
                    {
                        truncated = true;
                        break;
                    }

                    var name = RemoteEvidenceJson.String(reviewer, "displayName") ??
                        RemoteEvidenceJson.String(reviewer, "uniqueName") ??
                        RemoteEvidenceJson.String(reviewer, "id");
                    if (name is null)
                    {
                        continue;
                    }

                    var vote = RemoteEvidenceJson.String(reviewer, "vote");
                    values.Add(new RemoteReviewEvidence(
                        name,
                        vote switch
                        {
                            "10" => "approved",
                            "-10" => "changes requested",
                            "0" => "pending",
                            _ => "unknown"
                        },
                        requested: true,
                        reviewId: RemoteEvidenceJson.String(reviewer, "id")));
                }

                truncated |= RemoteEvidenceCollections.AppendBounded(
                    draft.Reviews,
                    values,
                    RemoteEvidenceLimits.MaxItems);
                if (truncated)
                {
                    draft.Limitations.Add("Azure pull-request reviewers were capped by the adapter bound.");
                }

                draft.ReviewState = truncated ? RemoteEvidenceState.Partial : RemoteEvidenceState.Available;
            }
            else
            {
                draft.ReviewState = RemoteEvidenceState.Available;
            }
        }
        catch (ArgumentException)
        {
            draft.PullRequestState = RemoteEvidenceState.InvalidResponse;
            draft.Error ??= "Azure pull-request metadata was malformed.";
        }
    }

    private async Task ReadCommitStatusesAsync(
        RemoteEvidenceDraft draft,
        AzureTarget target,
        string commit,
        AuthenticationHeaderValue authorization,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var response = await RemoteEvidenceHttp.GetJsonAsync(
            client,
            target.Api($"commits/{Uri.EscapeDataString(commit)}/statuses?top={RemoteEvidenceLimits.MaxItems}&skip=0&api-version=7.1"),
            authorization,
            cancellationToken).ConfigureAwait(false);
        var (state, values, truncated) = ParseAzureStatuses(response, RemoteStatusKind.CommitStatus);
        draft.StatusState = state;
        if (state is not RemoteEvidenceState.Available)
        {
            draft.Error ??= response.ErrorMessage;
            return;
        }

        truncated |= RemoteEvidenceCollections.AppendBounded(
            draft.Statuses,
            values,
            RemoteEvidenceLimits.MaxItems);
        if (truncated)
        {
            draft.Limitations.Add("Azure commit statuses were capped by the adapter bound.");
            draft.StatusState = RemoteEvidenceState.Partial;
            return;
        }

        if (values.Count == RemoteEvidenceLimits.MaxItems)
        {
            var lookahead = await RemoteEvidenceHttp.GetJsonAsync(
                client,
                target.Api($"commits/{Uri.EscapeDataString(commit)}/statuses?top=1&skip={RemoteEvidenceLimits.MaxItems}&api-version=7.1"),
                authorization,
                cancellationToken).ConfigureAwait(false);
            if (lookahead.State is not RemoteEvidenceState.Available)
            {
                draft.StatusState = RemoteEvidenceState.Partial;
                draft.Error ??= lookahead.ErrorMessage;
                draft.Limitations.Add("Azure commit-status look-ahead did not prove exhaustive evidence.");
                return;
            }

            var (lookaheadState, lookaheadValues, _) = ParseAzureStatuses(lookahead, RemoteStatusKind.CommitStatus);
            if (lookaheadState is not RemoteEvidenceState.Available)
            {
                draft.StatusState = RemoteEvidenceState.Partial;
                draft.Error ??= "Azure commit-status look-ahead was malformed.";
                draft.Limitations.Add("Azure commit-status look-ahead did not prove exhaustive evidence.");
                return;
            }

            if (lookaheadValues.Count > 0)
            {
                draft.StatusState = RemoteEvidenceState.Partial;
                draft.Limitations.Add("Azure commit statuses continued beyond the retained evidence bound.");
                return;
            }
        }

        draft.Error ??= response.State is not RemoteEvidenceState.Available ? response.ErrorMessage : null;
    }

    private async Task ReadPullRequestStatusesAsync(
        RemoteEvidenceDraft draft,
        AzureTarget target,
        string pullRequestId,
        AuthenticationHeaderValue authorization,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var response = await RemoteEvidenceHttp.GetJsonAsync(
            client,
            target.Api($"pullRequests/{Uri.EscapeDataString(pullRequestId)}/statuses?api-version=7.1"),
            authorization,
            cancellationToken).ConfigureAwait(false);
        var (state, values, truncated) = ParseAzureStatuses(response, RemoteStatusKind.PullRequestStatus);
        if (draft.StatusState == RemoteEvidenceState.Available && state != RemoteEvidenceState.Available)
        {
            draft.StatusState = state;
        }

        var destinationTruncated = RemoteEvidenceCollections.AppendBounded(
            draft.Statuses,
            values,
            RemoteEvidenceLimits.MaxItems);
        if (truncated || destinationTruncated)
        {
            draft.Limitations.Add("Azure pull-request statuses were capped by the adapter bound.");
            if (draft.StatusState is RemoteEvidenceState.Available)
            {
                draft.StatusState = RemoteEvidenceState.Partial;
            }
        }
        draft.Error ??= response.State is not RemoteEvidenceState.Available ? response.ErrorMessage : null;
    }

    private async Task ReadBuildsAsync(
        RemoteEvidenceDraft draft,
        AzureTarget target,
        string repositoryId,
        string branch,
        string commit,
        AuthenticationHeaderValue authorization,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var query =
            $"builds?repositoryId={Uri.EscapeDataString(repositoryId)}&branchName={Uri.EscapeDataString(RemoteEvidenceBranches.Ref(branch))}&queryOrder=finishTimeDescending&%24top={RemoteEvidenceLimits.MaxItems}&api-version=7.1";
        for (var page = 0; page < RemoteEvidenceLimits.MaxPages; page++)
        {
            var response = await RemoteEvidenceHttp.GetJsonAsync(
                client,
                target.BuildApi(query),
                authorization,
                cancellationToken).ConfigureAwait(false);
            if (response.State is not RemoteEvidenceState.Available)
            {
                if (page == 0)
                {
                    draft.CiState = response.State;
                    draft.CiResult = RemoteCiState.Unknown;
                    draft.Error ??= response.ErrorMessage;
                }
                else
                {
                    draft.CiState = RemoteEvidenceState.Partial;
                    draft.CiResult = PartialCiResult(draft.CiRuns);
                    draft.Error ??= response.ErrorMessage;
                    draft.Limitations.Add("Azure build pagination stopped before all pages were read.");
                }

                return;
            }

            using var document = RemoteEvidenceJson.Parse(response.Body, out var error);
            if (document is null || !document.RootElement.TryGetProperty("value", out var builds) || builds.ValueKind is not JsonValueKind.Array)
            {
                draft.CiState = RemoteEvidenceState.InvalidResponse;
                draft.CiResult = RemoteCiState.Unknown;
                draft.Error ??= error ?? "Azure build response was malformed.";
                return;
            }

            try
            {
                var pageTruncated = false;
                var pageRuns = new List<RemoteCiRunEvidence>();
                var index = 0;
                foreach (var build in builds.EnumerateArray())
                {
                    if (index++ >= RemoteEvidenceLimits.MaxItems)
                    {
                        pageTruncated = true;
                        break;
                    }

                    var sourceVersion = RemoteEvidenceJson.String(build, "sourceVersion");
                    if (sourceVersion is null || !sourceVersion.Equals(commit, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var id = RemoteEvidenceJson.Required(build, "id");
                    var name = build.TryGetProperty("definition", out var definition) &&
                        RemoteEvidenceJson.String(definition, "name") is { } definitionName
                        ? definitionName
                        : RemoteEvidenceJson.String(build, "buildNumber") ?? "Azure build";
                    pageRuns.Add(new RemoteCiRunEvidence(
                        RemoteStatusKind.Build,
                        id,
                        name,
                        FromBuild(RemoteEvidenceJson.String(build, "status"), RemoteEvidenceJson.String(build, "result")),
                        RemoteEvidenceJson.String(build, "result"),
                        NormalizeBranch(RemoteEvidenceJson.String(build, "sourceBranch")) ?? branch,
                        sourceVersion,
                        RemoteEvidenceJson.Time(build, "queueTime"),
                        RemoteEvidenceJson.Time(build, "finishTime"),
                        RemoteEvidenceJson.Time(build, "finishTime"),
                        SafeAzureUri(build, "_links", "web", "href")));
                }

                var destinationTruncated = RemoteEvidenceCollections.AppendBounded(
                    draft.CiRuns,
                    pageRuns,
                    RemoteEvidenceLimits.MaxItems);
                if (pageTruncated || destinationTruncated)
                {
                    draft.CiState = RemoteEvidenceState.Partial;
                    draft.CiResult = PartialCiResult(draft.CiRuns);
                    draft.Limitations.Add(destinationTruncated
                        ? "Azure build evidence was capped by the adapter bound."
                        : "Azure builds were capped by the adapter bound.");
                    return;
                }
            }
            catch (ArgumentException)
            {
                draft.CiState = RemoteEvidenceState.InvalidResponse;
                draft.CiResult = RemoteCiState.Unknown;
                draft.Error ??= "Azure build evidence was malformed.";
                return;
            }

            if (!response.ContinuationHeaderPresent)
            {
                draft.CiState = RemoteEvidenceState.Available;
                draft.CiResult = RemoteEvidenceCi.Aggregate(draft.CiRuns);
                return;
            }

            if (response.ContinuationToken is null)
            {
                draft.CiState = RemoteEvidenceState.Partial;
                draft.CiResult = PartialCiResult(draft.CiRuns);
                draft.Limitations.Add("Azure build pagination metadata was rejected before the target commit was exhaustively searched.");
                return;
            }

            if (draft.CiRuns.Count >= RemoteEvidenceLimits.MaxItems)
            {
                draft.CiState = RemoteEvidenceState.Partial;
                draft.CiResult = PartialCiResult(draft.CiRuns);
                draft.Limitations.Add("Azure build evidence was capped before the target commit was exhaustively searched.");
                return;
            }

            if (page + 1 >= RemoteEvidenceLimits.MaxPages)
            {
                draft.CiState = RemoteEvidenceState.Partial;
                draft.CiResult = PartialCiResult(draft.CiRuns);
                draft.Limitations.Add("Azure build evidence was capped before the target commit was exhaustively searched.");
                return;
            }

            query =
                $"builds?repositoryId={Uri.EscapeDataString(repositoryId)}&branchName={Uri.EscapeDataString(RemoteEvidenceBranches.Ref(branch))}&queryOrder=finishTimeDescending&%24top={RemoteEvidenceLimits.MaxItems}&continuationToken={Uri.EscapeDataString(response.ContinuationToken)}&api-version=7.1";
        }
    }

    private async Task<(RemoteEvidenceState State, AuthenticationHeaderValue? Value, string? Error)> ResolveAuthorizationAsync(
        RemoteRepositoryEvidenceRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CredentialReference))
        {
            return (RemoteEvidenceState.AuthenticationRequired, null, "Azure Repos requires a configured credential reference.");
        }

        var token = await _credentials.RetrieveAsync(request.CredentialReference, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(token)
            ? (RemoteEvidenceState.AuthenticationRequired, null, "The configured Azure credential is missing.")
            : (RemoteEvidenceState.Available,
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(":" + token))),
                null);
    }

    private static string NormalizePullRequestState(string status) =>
        status.Trim().ToLowerInvariant() switch
        {
            "active" => "open",
            "completed" => "merged",
            "abandoned" => "closed",
            _ => "unknown"
        };

    private static (RemoteEvidenceState State, List<RemoteStatusEvidence> Values, bool Truncated) ParseAzureStatuses(
        RemoteHttpResult response,
        RemoteStatusKind kind)
    {
        if (response.State is not RemoteEvidenceState.Available)
        {
            return (response.State, [], false);
        }

        using var document = RemoteEvidenceJson.Parse(response.Body, out _);
        if (document is null)
        {
            return (RemoteEvidenceState.InvalidResponse, [], false);
        }

        var root = document.RootElement;
        var values = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("value", out var nested) && nested.ValueKind == JsonValueKind.Array
                ? nested
                : default;
        if (values.ValueKind is not JsonValueKind.Array)
        {
            return (RemoteEvidenceState.InvalidResponse, [], false);
        }

        try
        {
            var result = new List<RemoteStatusEvidence>();
            var index = 0;
            var truncated = false;
            foreach (var value in values.EnumerateArray())
            {
                if (index++ >= RemoteEvidenceLimits.MaxItems)
                {
                    truncated = true;
                    break;
                }

                var context = value.TryGetProperty("context", out var contextElement) ? contextElement : default;
                var name = RemoteEvidenceJson.String(context, "name") ?? "Azure status";
                result.Add(new RemoteStatusEvidence(
                    kind,
                    name,
                    RemoteEvidenceJson.String(value, "state") ?? "unknown",
                    RemoteEvidenceJson.String(value, "description"),
                    RemoteEvidenceJson.String(value, "commitId"),
                    RemoteEvidenceJson.Time(value, "creationDate"),
                    RemoteEvidenceJson.Time(value, "updatedDate"),
                    SafeAzureUri(value, "targetUrl")));
            }

            return (RemoteEvidenceState.Available, result, truncated);
        }
        catch (ArgumentException)
        {
            return (RemoteEvidenceState.InvalidResponse, [], false);
        }
    }

    private static RemoteCiState FromBuild(string? status, string? result) =>
        result?.ToLowerInvariant() switch
        {
            "succeeded" => RemoteCiState.Passing,
            "failed" or "partiallysucceeded" => RemoteCiState.Failing,
            "canceled" => RemoteCiState.Cancelled,
            _ when status?.Equals("completed", StringComparison.OrdinalIgnoreCase) == true => RemoteCiState.Unknown,
            _ => RemoteCiState.Pending
        };

    private static RemoteCiState PartialCiResult(IReadOnlyCollection<RemoteCiRunEvidence> runs)
    {
        var aggregate = RemoteEvidenceCi.Aggregate(runs);
        return aggregate is RemoteCiState.NoEvidence or RemoteCiState.Passing
            ? RemoteCiState.Unknown
            : aggregate;
    }

    private static string? NormalizeBranch(string? value) =>
        RemoteEvidenceBranches.TryNormalize(value, out var branch) ? branch : null;

    private static Uri? SafeAzureUri(JsonElement element, string propertyName, params string[] nestedProperties)
    {
        var value = element;
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(propertyName, out value))
        {
            return null;
        }

        foreach (var property in nestedProperties)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(property, out value))
            {
                return null;
            }
        }

        var raw = value.ValueKind == JsonValueKind.String ? RemoteEvidenceJson.Limit(value.GetString()) : null;

        return raw is not null && Uri.TryCreate(raw, UriKind.Absolute, out var uri) &&
            RemoteEvidenceUrl.IsSafeResponseUri(uri, "dev.azure.com", "*.visualstudio.com")
            ? uri
            : null;
    }

    private static bool IsAzureProvider(string? value)
    {
        var normalized = value?.Replace(" ", string.Empty, StringComparison.Ordinal);
        return normalized is not null &&
            (normalized.Equals("AzureRepos", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("AzureDevOps", StringComparison.OrdinalIgnoreCase));
    }
}
