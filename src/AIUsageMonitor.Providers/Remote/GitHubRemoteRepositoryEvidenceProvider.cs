using System.Net.Http.Headers;
using System.Text.Json;
using AIUsageMonitor.Application.RemoteEvidence;
using AIUsageMonitor.Application.Security;
using AIUsageMonitor.Application.Time;

namespace AIUsageMonitor.Providers.Remote;

public sealed class GitHubRemoteRepositoryEvidenceProvider : IRemoteRepositoryEvidenceProvider
{
    public const string HttpClientName = "AIUsageMonitor.Remote.GitHub";

    private readonly IClock _clock;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecureCredentialStore _credentials;

    public GitHubRemoteRepositoryEvidenceProvider(
        IClock clock,
        IHttpClientFactory httpClientFactory,
        ISecureCredentialStore credentials)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
    }

    public RemoteRepositoryProvider Provider => RemoteRepositoryProvider.GitHub;

    public async Task<RemoteRepositoryEvidence> InspectAsync(
        RemoteRepositoryEvidenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var draft = new RemoteEvidenceDraft(request.ProjectId, RemoteEvidenceSource.GitHubRest, _clock.UtcNow);
        if (request.RepositoryProvider is null ||
            !request.RepositoryProvider.Replace(" ", string.Empty, StringComparison.Ordinal)
                .Equals("GitHub", StringComparison.OrdinalIgnoreCase) ||
            !RemoteEvidenceUrl.TryGitHub(request.RepositoryUrl, out var target) || target is null)
        {
            return draft.Failure(RemoteEvidenceState.Unsupported,
                "The configured repository is not a supported GitHub identity.");
        }

        try
        {
            var authorization = await ResolveAuthorizationAsync(request, cancellationToken).ConfigureAwait(false);
            if (authorization.State is not RemoteEvidenceState.Available)
            {
                return draft.Failure(authorization.State, authorization.Error!);
            }

            var client = _httpClientFactory.CreateClient(HttpClientName);
            var repositoryResponse = await RemoteEvidenceHttp.GetJsonAsync(
                client,
                target.Api(string.Empty),
                authorization.Value,
                cancellationToken).ConfigureAwait(false);
            if (repositoryResponse.State is not RemoteEvidenceState.Available)
            {
                var failure = GitHubFailure(repositoryResponse);
                return draft.Failure(failure.State, failure.Error);
            }

            using var repositoryDocument = RemoteEvidenceJson.Parse(repositoryResponse.Body, out var repositoryError);
            if (repositoryDocument is null)
            {
                return draft.Failure(RemoteEvidenceState.InvalidResponse, repositoryError!);
            }

            try
            {
                var root = repositoryDocument.RootElement;
                var owner = RemoteEvidenceJson.String(root.GetProperty("owner"), "login") ??
                    throw new ArgumentException("GitHub repository owner was missing.");
                var repositoryName = RemoteEvidenceJson.Required(root, "name");
                var repositoryId = RemoteEvidenceJson.Required(root, "id");
                var defaultBranch = RemoteEvidenceJson.Required(root, "default_branch");
                if (!RemoteEvidenceBranches.TryNormalize(defaultBranch, out defaultBranch))
                {
                    throw new ArgumentException("GitHub returned an invalid default branch.");
                }
                if (!owner.Equals(target.Owner, StringComparison.OrdinalIgnoreCase) ||
                    !repositoryName.Equals(target.Repository, StringComparison.OrdinalIgnoreCase) ||
                    request.RepositoryId is not null && !request.RepositoryId.Equals(repositoryId, StringComparison.OrdinalIgnoreCase))
                {
                    return draft.Failure(RemoteEvidenceState.InvalidResponse,
                        "GitHub returned a repository identity different from the configured project target.");
                }

                draft.Repository = new RemoteRepositoryIdentity(
                    Provider,
                    RemoteEvidenceSource.GitHubRest,
                    repositoryId,
                    $"{owner}/{repositoryName}",
                    owner,
                    repositoryName,
                    defaultBranch,
                    webUrl: target.CanonicalUrl);
                draft.RepositoryState = RemoteEvidenceState.Available;
            }
            catch (ArgumentException)
            {
                return draft.Failure(RemoteEvidenceState.InvalidResponse,
                    "GitHub repository metadata was malformed.");
            }

            if (!RemoteEvidenceBranches.TryNormalize(request.RequestedBranch ?? draft.Repository!.DefaultBranch, out var branch))
            {
                draft.BranchState = RemoteEvidenceState.InvalidResponse;
                draft.Error = "The requested GitHub branch was invalid.";
                return draft.Build();
            }

            var branchResponse = await RemoteEvidenceHttp.GetJsonAsync(
                client,
                target.Api($"branches/{Uri.EscapeDataString(branch)}"),
                authorization.Value,
                cancellationToken).ConfigureAwait(false);
            if (branchResponse.State is not RemoteEvidenceState.Available)
            {
                var failure = GitHubFailure(branchResponse);
                draft.BranchState = failure.State;
                draft.Error ??= failure.Error;
                return draft.Build();
            }

            using var branchDocument = RemoteEvidenceJson.Parse(branchResponse.Body, out var branchError);
            if (branchDocument is null)
            {
                draft.BranchState = RemoteEvidenceState.InvalidResponse;
                draft.Error ??= branchError;
                return draft.Build();
            }

            try
            {
                var branchRoot = branchDocument.RootElement;
                var actualBranch = RemoteEvidenceJson.String(branchRoot, "name") ?? branch;
                if (!RemoteEvidenceBranches.TryNormalize(actualBranch, out actualBranch))
                {
                    throw new ArgumentException("GitHub returned an invalid branch.");
                }
                var commit = RemoteEvidenceJson.Required(branchRoot.GetProperty("commit"), "sha");
                draft.Branch = new RemoteBranchEvidence(
                    actualBranch,
                    commit,
                    actualBranch.Equals(draft.Repository.DefaultBranch, StringComparison.OrdinalIgnoreCase));
                draft.BranchState = RemoteEvidenceState.Available;
            }
            catch (ArgumentException)
            {
                draft.BranchState = RemoteEvidenceState.InvalidResponse;
                draft.Error ??= "GitHub branch metadata was malformed.";
                return draft.Build();
            }

            if (request.PullRequestNumber is { } pullRequestNumber)
            {
                await ReadPullRequestAsync(draft, target, pullRequestNumber, authorization.Value, client, cancellationToken)
                    .ConfigureAwait(false);
            }

            var evidenceCommit = draft.PullRequest?.HeadCommitId ?? draft.Branch.CommitId;
            await ReadStatusesAsync(draft, target, evidenceCommit, authorization.Value, client, cancellationToken).ConfigureAwait(false);
            await ReadChecksAsync(draft, target, evidenceCommit, authorization.Value, client, cancellationToken).ConfigureAwait(false);
            await ReadWorkflowRunsAsync(draft, target, evidenceCommit, branch, authorization.Value, client, cancellationToken).ConfigureAwait(false);
            return draft.Build();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return draft.Failure(RemoteEvidenceState.Cancelled, "Remote GitHub evidence was cancelled by the caller.");
        }
        catch (ArgumentException)
        {
            return draft.Failure(RemoteEvidenceState.InvalidResponse, "GitHub returned an unusable evidence response.");
        }
        catch (KeyNotFoundException)
        {
            return draft.Failure(RemoteEvidenceState.InvalidResponse, "GitHub returned an incomplete evidence response.");
        }
    }

    private async Task ReadPullRequestAsync(
        RemoteEvidenceDraft draft,
        GitHubTarget target,
        int number,
        AuthenticationHeaderValue? authorization,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var response = await RemoteEvidenceHttp.GetJsonAsync(
            client,
            target.Api($"pulls/{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}"),
            authorization,
            cancellationToken).ConfigureAwait(false);
        if (response.State is not RemoteEvidenceState.Available)
        {
            var failure = GitHubFailure(response);
            draft.PullRequestState = failure.State;
            draft.Error ??= failure.Error;
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
            var id = RemoteEvidenceJson.Required(root, "number");
            var state = RemoteEvidenceJson.Required(root, "state");
            var mergedAt = RemoteEvidenceJson.Time(root, "merged_at");
            var mergeability = RemoteEvidenceJson.Boolean(root, "mergeable") switch
            {
                true => RemoteMergeability.Available,
                false => RemoteMergeability.Conflicting,
                _ => RemoteEvidenceJson.String(root, "mergeable_state")?.ToLowerInvariant() switch
                {
                    "unknown" or "checking" => RemoteMergeability.Calculating,
                    _ => RemoteMergeability.Unknown
                }
            };
            draft.PullRequest = new RemotePullRequestEvidence(
                id,
                mergedAt is not null ? "merged" : state,
                RemoteEvidenceJson.Boolean(root, "draft"),
                RemoteEvidenceJson.String(root.GetProperty("head"), "ref"),
                RemoteEvidenceJson.String(root.GetProperty("base"), "ref"),
                RemoteEvidenceJson.String(root.GetProperty("head"), "sha"),
                RemoteEvidenceJson.String(root.GetProperty("base"), "sha"),
                mergeability,
                RemoteEvidenceJson.SafeUri(root, "html_url", "github.com"),
                RemoteEvidenceJson.String(root, "title"),
                RemoteEvidenceJson.String(root, "body"),
                RemoteEvidenceJson.String(root, "merge_commit_sha"),
                RemoteEvidenceJson.String(root, "node_id"));
            draft.PullRequestState = RemoteEvidenceState.Available;
        }
        catch (ArgumentException)
        {
            draft.PullRequestState = RemoteEvidenceState.InvalidResponse;
            draft.Error ??= "GitHub pull-request metadata was malformed.";
            return;
        }

        var requested = await ReadRequestedReviewersAsync(target, number, authorization, client, cancellationToken).ConfigureAwait(false);
        var submitted = await ReadReviewsAsync(
            target,
            number,
            RemoteEvidenceLimits.MaxItems - requested.Values.Count,
            authorization,
            client,
            cancellationToken).ConfigureAwait(false);
        draft.ReviewState = requested.State is RemoteEvidenceState.Available && submitted.State is RemoteEvidenceState.Available
            ? RemoteEvidenceState.Available
            : requested.State is not RemoteEvidenceState.Available ? requested.State : submitted.State;
        var reviewTruncated = RemoteEvidenceCollections.AppendBounded(
            draft.Reviews,
            requested.Values,
            RemoteEvidenceLimits.MaxItems);
        reviewTruncated |= RemoteEvidenceCollections.AppendBounded(
            draft.Reviews,
            submitted.Values,
            RemoteEvidenceLimits.MaxItems);
        if (requested.Truncated || submitted.Truncated || reviewTruncated)
        {
            draft.Limitations.Add("GitHub review evidence was capped by the adapter bound.");
            draft.ReviewState = RemoteEvidenceState.Partial;
        }
        if (draft.ReviewState is not RemoteEvidenceState.Available)
        {
            draft.Error ??= requested.Error ?? submitted.Error;
        }
    }

    private async Task ReadStatusesAsync(
        RemoteEvidenceDraft draft,
        GitHubTarget target,
        string commit,
        AuthenticationHeaderValue? authorization,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var pages = await ReadGitHubPagesAsync(
            client,
            target,
            target.Api($"commits/{Uri.EscapeDataString(commit)}/statuses?per_page={RemoteEvidenceLimits.MaxItems}"),
            authorization,
            cancellationToken).ConfigureAwait(false);
        if (pages.Pages[0].Response.State is not RemoteEvidenceState.Available)
        {
            var failure = GitHubFailure(pages.Pages[0].Response);
            draft.StatusState = failure.State;
            draft.Error ??= failure.Error;
            return;
        }

        var truncated = false;
        foreach (var page in pages.Pages)
        {
            if (page.Response.State is not RemoteEvidenceState.Available)
            {
                draft.StatusState = RemoteEvidenceState.Partial;
                draft.Error ??= page.Response.ErrorMessage;
                draft.Limitations.Add("GitHub commit status pagination stopped before all pages were read.");
                return;
            }

            using var document = RemoteEvidenceJson.Parse(page.Response.Body, out var error);
            if (document is null || document.RootElement.ValueKind is not JsonValueKind.Array)
            {
                draft.StatusState = RemoteEvidenceState.InvalidResponse;
                draft.Error ??= error ?? "GitHub status response was malformed.";
                return;
            }

            var state = ParseStatusArray(
                document.RootElement,
                RemoteStatusKind.CommitStatus,
                draft.Statuses,
                out var pageTruncated);
            if (state is not RemoteEvidenceState.Available)
            {
                draft.StatusState = state;
                draft.Error ??= "GitHub status response was malformed.";
                return;
            }

            truncated |= pageTruncated;
        }

        if (truncated || pages.Incomplete)
        {
            draft.Limitations.Add("GitHub commit statuses were capped by the adapter bound.");
            draft.StatusState = RemoteEvidenceState.Partial;
            return;
        }

        draft.StatusState = RemoteEvidenceState.Available;
    }

    private async Task ReadChecksAsync(
        RemoteEvidenceDraft draft,
        GitHubTarget target,
        string commit,
        AuthenticationHeaderValue? authorization,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var pages = await ReadGitHubPagesAsync(
            client,
            target,
            target.Api($"commits/{Uri.EscapeDataString(commit)}/check-runs?per_page={RemoteEvidenceLimits.MaxItems}"),
            authorization,
            cancellationToken).ConfigureAwait(false);
        if (pages.Pages[0].Response.State is not RemoteEvidenceState.Available)
        {
            var failure = GitHubFailure(pages.Pages[0].Response);
            draft.CheckState = failure.State;
            draft.Error ??= failure.Error;
            return;
        }

        var totalCount = 0;
        var truncated = false;
        foreach (var page in pages.Pages)
        {
            if (page.Response.State is not RemoteEvidenceState.Available)
            {
                draft.CheckState = RemoteEvidenceState.Partial;
                draft.Error ??= page.Response.ErrorMessage;
                draft.Limitations.Add("GitHub check-run pagination stopped before all pages were read.");
                return;
            }

            using var document = RemoteEvidenceJson.Parse(page.Response.Body, out var error);
            if (document is null ||
                !document.RootElement.TryGetProperty("check_runs", out var checks) ||
                checks.ValueKind is not JsonValueKind.Array)
            {
                draft.CheckState = RemoteEvidenceState.InvalidResponse;
                draft.Error ??= error ?? "GitHub check-run response was malformed.";
                return;
            }

            if (document.RootElement.TryGetProperty("total_count", out var total) &&
                total.ValueKind == JsonValueKind.Number && total.TryGetInt32(out var pageTotal))
            {
                totalCount = Math.Max(totalCount, pageTotal);
            }

            var state = ParseStatusArray(checks, RemoteStatusKind.CheckRun, draft.Checks, out var pageTruncated);
            if (state is not RemoteEvidenceState.Available)
            {
                draft.CheckState = state;
                draft.Error ??= "GitHub check-run response was malformed.";
                return;
            }

            truncated |= pageTruncated;
        }

        if (truncated || totalCount > draft.Checks.Count || pages.Incomplete)
        {
            draft.Limitations.Add("GitHub check runs were capped by the adapter bound.");
            draft.CheckState = RemoteEvidenceState.Partial;
            return;
        }

        draft.CheckState = RemoteEvidenceState.Available;
    }

    private async Task ReadWorkflowRunsAsync(
        RemoteEvidenceDraft draft,
        GitHubTarget target,
        string commit,
        string branch,
        AuthenticationHeaderValue? authorization,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var pages = await ReadGitHubPagesAsync(
            client,
            target,
            target.Api($"actions/runs?head_sha={Uri.EscapeDataString(commit)}&per_page={RemoteEvidenceLimits.MaxItems}"),
            authorization,
            cancellationToken,
            uri => HasQueryParameter(uri, "head_sha", commit)).ConfigureAwait(false);
        if (pages.Pages[0].Response.State is not RemoteEvidenceState.Available)
        {
            var failure = GitHubFailure(pages.Pages[0].Response);
            draft.CiState = failure.State;
            draft.CiResult = RemoteCiState.Unknown;
            draft.Error ??= failure.Error;
            return;
        }

        var totalCount = 0;
        var truncated = false;
        foreach (var page in pages.Pages)
        {
            if (page.Response.State is not RemoteEvidenceState.Available)
            {
                draft.CiState = RemoteEvidenceState.Partial;
                draft.CiResult = PartialCiResult(draft.CiRuns);
                draft.Error ??= page.Response.ErrorMessage;
                draft.Limitations.Add("GitHub workflow-run pagination stopped before all pages were read.");
                return;
            }

            using var document = RemoteEvidenceJson.Parse(page.Response.Body, out var error);
            if (document is null ||
                !document.RootElement.TryGetProperty("workflow_runs", out var runs) ||
                runs.ValueKind is not JsonValueKind.Array)
            {
                draft.CiState = RemoteEvidenceState.InvalidResponse;
                draft.CiResult = RemoteCiState.Unknown;
                draft.Error ??= error ?? "GitHub workflow-run response was malformed.";
                return;
            }

            if (document.RootElement.TryGetProperty("total_count", out var total) &&
                total.ValueKind == JsonValueKind.Number && total.TryGetInt32(out var pageTotal))
            {
                totalCount = Math.Max(totalCount, pageTotal);
            }

            try
            {
                foreach (var run in runs.EnumerateArray())
                {
                    if (draft.CiRuns.Count >= RemoteEvidenceLimits.MaxItems)
                    {
                        truncated = true;
                        break;
                    }

                    var id = RemoteEvidenceJson.Required(run, "id");
                    var name = RemoteEvidenceJson.String(run, "name") ?? "workflow run";
                    var status = RemoteEvidenceJson.String(run, "status");
                    var conclusion = RemoteEvidenceJson.String(run, "conclusion");
                    draft.CiRuns.Add(new RemoteCiRunEvidence(
                        RemoteStatusKind.WorkflowRun,
                        id,
                        name,
                        RemoteEvidenceCi.FromStatus(status, conclusion),
                        conclusion,
                        RemoteEvidenceJson.String(run, "head_branch") ?? branch,
                        RemoteEvidenceJson.String(run, "head_sha") ?? commit,
                        RemoteEvidenceJson.Time(run, "created_at"),
                        RemoteEvidenceJson.Time(run, "updated_at"),
                        null,
                        RemoteEvidenceJson.SafeUri(run, "html_url", "github.com")));
                }
            }
            catch (ArgumentException)
            {
                draft.CiState = RemoteEvidenceState.InvalidResponse;
                draft.CiResult = RemoteCiState.Unknown;
                draft.Error ??= "GitHub workflow-run evidence was malformed.";
                return;
            }
        }

        draft.CiResult = truncated || totalCount > draft.CiRuns.Count || pages.Incomplete
            ? PartialCiResult(draft.CiRuns)
            : RemoteEvidenceCi.Aggregate(draft.CiRuns);
        if (truncated || totalCount > draft.CiRuns.Count || pages.Incomplete)
        {
            draft.Limitations.Add("GitHub workflow-run evidence may be truncated by the adapter bound.");
            draft.CiState = RemoteEvidenceState.Partial;
            return;
        }

        draft.CiState = RemoteEvidenceState.Available;
    }

    private async Task<(RemoteEvidenceState State, List<RemoteReviewEvidence> Values, string? Error, bool Truncated)> ReadRequestedReviewersAsync(
        GitHubTarget target,
        int number,
        AuthenticationHeaderValue? authorization,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var response = await RemoteEvidenceHttp.GetJsonAsync(
            client,
            target.Api($"pulls/{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}/requested_reviewers"),
            authorization,
            cancellationToken).ConfigureAwait(false);
        if (response.State is not RemoteEvidenceState.Available)
        {
            var failure = GitHubFailure(response);
            return (failure.State, [], failure.Error, false);
        }

        using var document = RemoteEvidenceJson.Parse(response.Body, out var error);
        if (document is null)
        {
            return (RemoteEvidenceState.InvalidResponse, [], error, false);
        }

        try
        {
            var values = new List<RemoteReviewEvidence>();
            var usersTruncated = ReadRequestedArray(document.RootElement, "users", values, RemoteEvidenceLimits.MaxItems);
            var teamsTruncated = ReadRequestedArray(document.RootElement, "teams", values, RemoteEvidenceLimits.MaxItems);
            var truncated = usersTruncated || teamsTruncated;
            return (truncated ? RemoteEvidenceState.Partial : RemoteEvidenceState.Available, values, null, truncated);
        }
        catch (ArgumentException)
        {
            return (RemoteEvidenceState.InvalidResponse, [], "GitHub requested-reviewer evidence was malformed.", false);
        }
    }

    private async Task<(RemoteEvidenceState State, List<RemoteReviewEvidence> Values, string? Error, bool Truncated)> ReadReviewsAsync(
        GitHubTarget target,
        int number,
        int maxItems,
        AuthenticationHeaderValue? authorization,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var pages = await ReadGitHubPagesAsync(
            client,
            target,
            target.Api($"pulls/{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}/reviews?per_page={RemoteEvidenceLimits.MaxItems}"),
            authorization,
            cancellationToken).ConfigureAwait(false);
        if (pages.Pages[0].Response.State is not RemoteEvidenceState.Available)
        {
            var failure = GitHubFailure(pages.Pages[0].Response);
            return (failure.State, [], failure.Error, false);
        }

        var values = new List<RemoteReviewEvidence>();
        var truncated = pages.Incomplete;
        foreach (var page in pages.Pages)
        {
            if (page.Response.State is not RemoteEvidenceState.Available)
            {
                return (
                    RemoteEvidenceState.Partial,
                    values,
                    page.Response.ErrorMessage,
                    true);
            }

            using var document = RemoteEvidenceJson.Parse(page.Response.Body, out var error);
            if (document is null || document.RootElement.ValueKind is not JsonValueKind.Array)
            {
                return (RemoteEvidenceState.InvalidResponse, [], error ?? "GitHub review response was malformed.", false);
            }

            try
            {
                foreach (var review in document.RootElement.EnumerateArray())
                {
                    if (values.Count >= maxItems)
                    {
                        truncated = true;
                        break;
                    }

                    var reviewer = review.TryGetProperty("user", out var user)
                        ? RemoteEvidenceJson.String(user, "login")
                        : null;
                    if (reviewer is null)
                    {
                        continue;
                    }

                    values.Add(new RemoteReviewEvidence(
                        reviewer,
                        RemoteEvidenceJson.String(review, "state") ?? "unknown",
                        requested: false,
                        RemoteEvidenceJson.Time(review, "submitted_at"),
                        RemoteEvidenceJson.String(review, "id")));
                }
            }
            catch (ArgumentException)
            {
                return (RemoteEvidenceState.InvalidResponse, [], "GitHub submitted-review evidence was malformed.", false);
            }
        }

        return (
            truncated ? RemoteEvidenceState.Partial : RemoteEvidenceState.Available,
            values,
            null,
            truncated);
    }

    private static (RemoteEvidenceState State, string Error) GitHubFailure(RemoteHttpResult response)
    {
        if (response.State is RemoteEvidenceState.RateLimited ||
            response.State is RemoteEvidenceState.PermissionDenied &&
            (response.RateLimitRemaining == 0 || response.RetryAfterSeconds is not null))
        {
            return (RemoteEvidenceState.RateLimited, "The GitHub provider rate-limited the request.");
        }

        return (response.State, response.ErrorMessage ?? "The GitHub provider returned an unusable response.");
    }

    private static RemoteCiState PartialCiResult(IReadOnlyCollection<RemoteCiRunEvidence> runs)
    {
        var aggregate = RemoteEvidenceCi.Aggregate(runs);
        return aggregate is RemoteCiState.NoEvidence or RemoteCiState.Passing
            ? RemoteCiState.Unknown
            : aggregate;
    }

    private static async Task<(List<(Uri Uri, RemoteHttpResult Response)> Pages, bool Incomplete)> ReadGitHubPagesAsync(
        HttpClient client,
        GitHubTarget target,
        Uri firstUri,
        AuthenticationHeaderValue? authorization,
        CancellationToken cancellationToken,
        Func<Uri, bool>? queryGuard = null)
    {
        var pages = new List<(Uri Uri, RemoteHttpResult Response)>();
        var uri = firstUri;
        for (var page = 0; page < RemoteEvidenceLimits.MaxPages; page++)
        {
            var response = await RemoteEvidenceHttp.GetJsonAsync(
                client,
                uri,
                authorization,
                cancellationToken).ConfigureAwait(false);
            pages.Add((uri, response));
            if (response.State is not RemoteEvidenceState.Available)
            {
                return (pages, pages.Count > 1);
            }

            if (!response.HasNextPage)
            {
                return (pages, false);
            }

            if (response.NextPageUri is null ||
                !RemoteEvidenceUrl.IsSafeGitHubNextPage(response.NextPageUri, target, uri) ||
                queryGuard is not null && !queryGuard(response.NextPageUri))
            {
                return (pages, true);
            }

            if (page + 1 >= RemoteEvidenceLimits.MaxPages)
            {
                return (pages, true);
            }

            uri = response.NextPageUri;
        }

        return (pages, true);
    }

    private static bool HasQueryParameter(Uri uri, string name, string expectedValue)
    {
        foreach (var parameter in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = parameter.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var parameterName = Uri.UnescapeDataString(parameter[..separator].Replace('+', ' '));
            var parameterValue = Uri.UnescapeDataString(parameter[(separator + 1)..].Replace('+', ' '));
            if (parameterName.Equals(name, StringComparison.Ordinal) &&
                parameterValue.Equals(expectedValue, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<(RemoteEvidenceState State, AuthenticationHeaderValue? Value, string? Error)> ResolveAuthorizationAsync(
        RemoteRepositoryEvidenceRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CredentialReference))
        {
            return (RemoteEvidenceState.Available, null, null);
        }

        var token = await _credentials.RetrieveAsync(request.CredentialReference, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(token)
            ? (RemoteEvidenceState.AuthenticationRequired, null, "The configured GitHub credential is missing.")
            : (RemoteEvidenceState.Available, new AuthenticationHeaderValue("Bearer", token), null);
    }

    private static RemoteEvidenceState ParseStatusArray(
        JsonElement values,
        RemoteStatusKind kind,
        ICollection<RemoteStatusEvidence> destination,
        out bool truncated)
    {
        truncated = false;
        try
        {
            foreach (var item in values.EnumerateArray())
            {
                if (destination.Count >= RemoteEvidenceLimits.MaxItems)
                {
                    truncated = true;
                    break;
                }

                var context = item.TryGetProperty("context", out var contextElement) ? contextElement : default;
                var name = RemoteEvidenceJson.String(item, "name") ??
                    RemoteEvidenceJson.String(context, "name") ??
                    RemoteEvidenceJson.String(context, "genre") ??
                    $"{kind} evidence";
                destination.Add(new RemoteStatusEvidence(
                    kind,
                    name,
                    RemoteEvidenceJson.String(item, "status") ?? RemoteEvidenceJson.String(item, "state") ?? "unknown",
                    RemoteEvidenceJson.String(item, "conclusion"),
                    RemoteEvidenceJson.String(item, "sha"),
                    RemoteEvidenceJson.Time(item, "created_at") ?? RemoteEvidenceJson.Time(item, "creationDate"),
                    RemoteEvidenceJson.Time(item, "updated_at") ?? RemoteEvidenceJson.Time(item, "updatedDate"),
                    RemoteEvidenceJson.SafeUri(item, "html_url", "github.com") ??
                    RemoteEvidenceJson.SafeUri(context, "targetUrl", "github.com")));
            }

            return RemoteEvidenceState.Available;
        }
        catch (ArgumentException)
        {
            return RemoteEvidenceState.InvalidResponse;
        }
    }

    private static bool ReadRequestedArray(
        JsonElement root,
        string property,
        ICollection<RemoteReviewEvidence> destination,
        int maxItems)
    {
        if (!root.TryGetProperty(property, out var values) || values.ValueKind is not JsonValueKind.Array)
        {
            return false;
        }

        var index = 0;
        foreach (var value in values.EnumerateArray())
        {
            if (index++ >= maxItems || destination.Count >= maxItems)
            {
                return true;
            }

            var name = RemoteEvidenceJson.String(value, "login") ?? RemoteEvidenceJson.String(value, "slug");
            if (name is not null)
            {
                destination.Add(new RemoteReviewEvidence(name, "requested", requested: true));
            }
        }

        return false;
    }

}
