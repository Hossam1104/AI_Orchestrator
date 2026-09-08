using System.Net;
using System.Net.Http.Headers;
using AIUsageMonitor.Application.Common;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.RemoteEvidence;
using AIUsageMonitor.Application.Security;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Providers.Remote;

namespace AIUsageMonitor.Provider.Tests;

public sealed class RemoteRepositoryEvidenceTests
{
    private const string GitHubRepository = "{\"id\":\"repo-1\",\"name\":\"repo\",\"owner\":{\"login\":\"octo\"},\"default_branch\":\"main\"}";
    private const string GitHubBranch = "{\"name\":\"main\",\"commit\":{\"sha\":\"abc123\"}}";
    private const string GitHubEmptyStatuses = "[]";
    private const string GitHubEmptyChecks = "{\"total_count\":0,\"check_runs\":[]}";
    private const string GitHubEmptyRuns = "{\"total_count\":0,\"workflow_runs\":[]}";

    [Fact]
    public async Task GitHub_NormalizesPublicEvidence_AndUsesOnlyReadRequests()
    {
        var credentials = new TestCredentialStore();
        var provider = CreateGitHubProvider(credentials, out var requests, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/repos/octo/repo" => Json(GitHubRepository),
                "/repos/octo/repo/branches/main" => Json(GitHubBranch),
                _ when path.EndsWith("/statuses", StringComparison.Ordinal) => Json("[{\"context\":\"build\",\"state\":\"success\",\"description\":\"ok\",\"sha\":\"abc123\",\"created_at\":\"2026-08-22T11:00:00Z\",\"updated_at\":\"2026-08-22T11:30:00Z\",\"target_url\":\"https://github.com/octo/repo/actions\"}]"),
                _ when path.EndsWith("/check-runs", StringComparison.Ordinal) => Json("{\"total_count\":1,\"check_runs\":[{\"id\":11,\"name\":\"unit\",\"status\":\"completed\",\"conclusion\":\"success\",\"html_url\":\"https://github.com/octo/repo/actions/runs/11\"}]}"),
                _ when path.EndsWith("/actions/runs", StringComparison.Ordinal) => Json("{\"total_count\":1,\"workflow_runs\":[{\"id\":22,\"name\":\"CI\",\"status\":\"completed\",\"conclusion\":\"success\",\"head_branch\":\"main\",\"head_sha\":\"abc123\",\"created_at\":\"2026-08-22T11:00:00Z\",\"updated_at\":\"2026-08-22T11:30:00Z\",\"html_url\":\"https://github.com/octo/repo/actions/runs/22\"}]}"),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(),
            "GitHub",
            "https://github.com/octo/repo.git",
            repositoryId: "repo-1",
            requestedBranch: "main"));

        Assert.Equal(RemoteEvidenceState.Available, evidence.State);
        Assert.Equal(RemoteEvidenceSource.GitHubRest, evidence.Source);
        Assert.Equal("repo-1", evidence.Repository!.ProviderRepositoryId);
        Assert.Equal("octo/repo", evidence.Repository.CanonicalName);
        Assert.Equal("main", evidence.Branch!.BranchName);
        Assert.Equal("abc123", evidence.Branch.CommitId);
        Assert.Single(evidence.Statuses);
        Assert.Single(evidence.Checks);
        Assert.Equal(RemoteCiState.Passing, evidence.CiResult);
        Assert.Equal(RemoteEvidenceState.NotConfigured, evidence.PullRequestState);
        AssertReadOnly(requests, "api.github.com");
    }

    [Theory]
    [InlineData("open", "null", false, "true", "available", RemoteMergeability.Available)]
    [InlineData("closed", "\"2026-08-22T12:00:00Z\"", false, "false", "dirty", RemoteMergeability.Conflicting)]
    [InlineData("open", "null", true, "null", "checking", RemoteMergeability.Calculating)]
    [InlineData("closed", "null", false, "null", "unknown", RemoteMergeability.Calculating)]
    public async Task GitHub_PullRequestStateAndReviewEvidenceRemainFactual(
        string state,
        string mergedAt,
        bool draft,
        string mergeable,
        string mergeableState,
        RemoteMergeability expectedMergeability)
    {
        var credentials = new TestCredentialStore();
        var provider = CreateGitHubProvider(credentials, out var requests, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/repos/octo/repo" => Json(GitHubRepository),
                "/repos/octo/repo/branches/main" => Json(GitHubBranch),
                "/repos/octo/repo/pulls/7" => Json($"{{\"number\":7,\"state\":\"{state}\",\"draft\":{draft.ToString().ToLowerInvariant()},\"merged_at\":{mergedAt},\"mergeable\":{mergeable},\"mergeable_state\":\"{mergeableState}\",\"head\":{{\"ref\":\"feature\",\"sha\":\"def456\"}},\"base\":{{\"ref\":\"main\",\"sha\":\"abc123\"}},\"html_url\":\"https://github.com/octo/repo/pull/7\"}}"),
                "/repos/octo/repo/pulls/7/requested_reviewers" => Json("{\"users\":[{\"login\":\"reviewer\"}],\"teams\":[{\"slug\":\"maintainers\"}]}"),
                "/repos/octo/repo/pulls/7/reviews" => Json("[{\"id\":31,\"user\":{\"login\":\"alice\"},\"state\":\"APPROVED\",\"submitted_at\":\"2026-08-22T11:00:00Z\"},{\"id\":32,\"user\":{\"login\":\"bob\"},\"state\":\"CHANGES_REQUESTED\",\"submitted_at\":\"2026-08-22T11:10:00Z\"}]"),
                _ when path.EndsWith("/statuses", StringComparison.Ordinal) => Json(GitHubEmptyStatuses),
                _ when path.EndsWith("/check-runs", StringComparison.Ordinal) => Json(GitHubEmptyChecks),
                _ when path.EndsWith("/actions/runs", StringComparison.Ordinal) => Json(GitHubEmptyRuns),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "GitHub", "git@github.com:octo/repo.git", pullRequestNumber: 7));

        Assert.Equal(RemoteEvidenceState.Available, evidence.State);
        Assert.Equal(state == "closed" && mergedAt != "null" ? "merged" : state, evidence.PullRequest!.State);
        Assert.Equal(draft, evidence.PullRequest.IsDraft);
        Assert.Equal(expectedMergeability, evidence.PullRequest.Mergeability);
        Assert.Equal("def456", evidence.PullRequest.HeadCommitId);
        Assert.Equal(4, evidence.Reviews.Count);
        Assert.Contains(evidence.Reviews, review => review.Reviewer == "reviewer" && review.Requested);
        Assert.Contains(evidence.Reviews, review => review.State == "APPROVED" && !review.Requested);
        Assert.Contains(evidence.Reviews, review => review.State == "CHANGES_REQUESTED" && !review.Requested);
        AssertReadOnly(requests, "api.github.com");
    }

    [Fact]
    public async Task GitHub_NoWorkflowRuns_IsNoEvidence_NotPassing()
    {
        var provider = CreateGitHubProvider(new TestCredentialStore(), out _, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/repos/octo/repo" => Json(GitHubRepository),
                "/repos/octo/repo/branches/main" => Json(GitHubBranch),
                _ when path.EndsWith("/statuses", StringComparison.Ordinal) => Json(GitHubEmptyStatuses),
                _ when path.EndsWith("/check-runs", StringComparison.Ordinal) => Json(GitHubEmptyChecks),
                _ when path.EndsWith("/actions/runs", StringComparison.Ordinal) => Json(GitHubEmptyRuns),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(Request("GitHub", "https://github.com/octo/repo"));

        Assert.Equal(RemoteEvidenceState.Available, evidence.State);
        Assert.Equal(RemoteEvidenceState.Available, evidence.CiState);
        Assert.Equal(RemoteCiState.NoEvidence, evidence.CiResult);
    }

    [Fact]
    public async Task GitHub_ResolvesOpaqueCredentialOnlyAtTheHttpBoundary()
    {
        var credentials = new TestCredentialStore();
        credentials.Add("github-ref", "fixture-token");
        var provider = CreateGitHubProvider(credentials, out var requests, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/repos/octo/repo" => Json(GitHubRepository),
                "/repos/octo/repo/branches/main" => Json(GitHubBranch),
                _ when path.EndsWith("/statuses", StringComparison.Ordinal) => Json(GitHubEmptyStatuses),
                _ when path.EndsWith("/check-runs", StringComparison.Ordinal) => Json(GitHubEmptyChecks),
                _ when path.EndsWith("/actions/runs", StringComparison.Ordinal) => Json(GitHubEmptyRuns),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "GitHub", "https://github.com/octo/repo",
            repositoryMetadata: new Dictionary<string, string?> { ["credentialReference"] = "github-ref" }));

        Assert.Equal(RemoteEvidenceState.Available, evidence.State);
        Assert.All(requests, request => Assert.Equal("Bearer", request.Headers.Authorization?.Scheme));
        Assert.All(requests, request => Assert.DoesNotContain("fixture-token", request.RequestUri!.ToString(), StringComparison.Ordinal));
        Assert.DoesNotContain("fixture-token", evidence.SafeErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GitHub_ForbiddenChecks_ProducesPartialEvidence()
    {
        var provider = CreateGitHubProvider(new TestCredentialStore(), out _, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/repos/octo/repo" => Json(GitHubRepository),
                "/repos/octo/repo/branches/main" => Json(GitHubBranch),
                _ when path.EndsWith("/statuses", StringComparison.Ordinal) => Json(GitHubEmptyStatuses),
                _ when path.EndsWith("/check-runs", StringComparison.Ordinal) => Status(HttpStatusCode.Forbidden),
                _ when path.EndsWith("/actions/runs", StringComparison.Ordinal) => Json(GitHubEmptyRuns),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(Request("GitHub", "https://github.com/octo/repo"));

        Assert.Equal(RemoteEvidenceState.Partial, evidence.State);
        Assert.NotNull(evidence.Repository);
        Assert.NotNull(evidence.Branch);
        Assert.Equal(RemoteEvidenceState.PermissionDenied, evidence.CheckState);
        Assert.DoesNotContain("secret", evidence.SafeErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GitHub_MissingCredential_IsAuthenticationRequired_WithoutHttp()
    {
        var credentials = new TestCredentialStore();
        var provider = CreateGitHubProvider(credentials, out var requests, _ => Status(HttpStatusCode.OK));

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "GitHub", "https://github.com/octo/repo", credentialReference: "missing-ref"));

        Assert.Equal(RemoteEvidenceState.AuthenticationRequired, evidence.State);
        Assert.Empty(requests);
        Assert.DoesNotContain("missing-ref", evidence.SafeErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, RemoteEvidenceState.AuthenticationRequired)]
    [InlineData(HttpStatusCode.NotFound, RemoteEvidenceState.Unavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, RemoteEvidenceState.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, RemoteEvidenceState.Unavailable)]
    public async Task GitHub_RemoteHttpFailures_AreTypedAndSafe(HttpStatusCode status, RemoteEvidenceState expected)
    {
        var provider = CreateGitHubProvider(new TestCredentialStore(), out var requests, _ => Status(status));

        var evidence = await provider.InspectAsync(Request("GitHub", "https://github.com/octo/repo"));

        Assert.Equal(expected, evidence.State);
        Assert.NotEmpty(requests);
        Assert.DoesNotContain("does not exist", evidence.SafeErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GitHub_MalformedResponse_IsInvalidResponse_WithoutThrowing()
    {
        var provider = CreateGitHubProvider(new TestCredentialStore(), out _, request =>
            request.RequestUri!.AbsolutePath == "/repos/octo/repo"
                ? Json("{\"id\":")
                : Status(HttpStatusCode.NotFound));

        var evidence = await provider.InspectAsync(Request("GitHub", "https://github.com/octo/repo"));

        Assert.Equal(RemoteEvidenceState.InvalidResponse, evidence.State);
        Assert.Contains("malformed", evidence.SafeErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GitHub_UnsafeRepositoryUrl_IsUnsupported_AndNeverReceivesCredential()
    {
        var credentials = new TestCredentialStore();
        credentials.Add("ref", "fixture-token");
        var provider = CreateGitHubProvider(credentials, out var requests, _ => Status(HttpStatusCode.OK));

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "GitHub", "https://user:fixture-token@evil.example/octo/repo.git", credentialReference: "ref"));

        Assert.Equal(RemoteEvidenceState.Unsupported, evidence.State);
        Assert.Empty(requests);
        Assert.DoesNotContain("fixture-token", evidence.SafeErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GitHub_WorkflowEvidenceIsBoundedAndMarkedPartial()
    {
        var runs = string.Join(',', Enumerable.Range(1, 101).Select(index =>
            $"{{\"id\":{index},\"name\":\"CI {index}\",\"status\":\"completed\",\"conclusion\":\"success\",\"head_branch\":\"main\",\"head_sha\":\"abc123\"}}"));
        var provider = CreateGitHubProvider(new TestCredentialStore(), out _, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/repos/octo/repo" => Json(GitHubRepository),
                "/repos/octo/repo/branches/main" => Json(GitHubBranch),
                _ when path.EndsWith("/statuses", StringComparison.Ordinal) => Json(GitHubEmptyStatuses),
                _ when path.EndsWith("/check-runs", StringComparison.Ordinal) => Json(GitHubEmptyChecks),
                _ when path.EndsWith("/actions/runs", StringComparison.Ordinal) => Json($"{{\"total_count\":101,\"workflow_runs\":[{runs}]}}"),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(Request("GitHub", "https://github.com/octo/repo"));

        Assert.Equal(RemoteEvidenceState.Partial, evidence.State);
        Assert.Equal(100, evidence.CiRuns.Count);
        Assert.Equal(RemoteCiState.Unknown, evidence.CiResult);
        Assert.NotEmpty(evidence.Limitations);
    }

    [Fact]
    public async Task GitHub_PartialWorkflowEvidencePreservesKnownFailure()
    {
        var runs = string.Join(',', Enumerable.Range(1, 101).Select(index =>
            $"{{\"id\":{index},\"name\":\"CI {index}\",\"status\":\"completed\",\"conclusion\":\"{(index == 1 ? "failure" : "success")}\",\"head_branch\":\"main\",\"head_sha\":\"abc123\"}}"));
        var provider = CreateGitHubProvider(new TestCredentialStore(), out _, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/repos/octo/repo" => Json(GitHubRepository),
                "/repos/octo/repo/branches/main" => Json(GitHubBranch),
                _ when path.EndsWith("/statuses", StringComparison.Ordinal) => Json(GitHubEmptyStatuses),
                _ when path.EndsWith("/check-runs", StringComparison.Ordinal) => Json(GitHubEmptyChecks),
                _ when path.EndsWith("/actions/runs", StringComparison.Ordinal) => Json($"{{\"total_count\":101,\"workflow_runs\":[{runs}]}}"),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(Request("GitHub", "https://github.com/octo/repo"));

        Assert.Equal(RemoteEvidenceState.Partial, evidence.CiState);
        Assert.Equal(RemoteCiState.Failing, evidence.CiResult);
    }

    [Fact]
    public async Task GitHub_FailedAndPendingWorkflowRuns_AreNotPassing()
    {
        var provider = CreateGitHubProvider(new TestCredentialStore(), out _, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/repos/octo/repo" => Json(GitHubRepository),
                "/repos/octo/repo/branches/main" => Json(GitHubBranch),
                _ when path.EndsWith("/statuses", StringComparison.Ordinal) => Json(GitHubEmptyStatuses),
                _ when path.EndsWith("/check-runs", StringComparison.Ordinal) => Json(GitHubEmptyChecks),
                _ when path.EndsWith("/actions/runs", StringComparison.Ordinal) => Json("{\"total_count\":2,\"workflow_runs\":[{\"id\":1,\"name\":\"failed\",\"status\":\"completed\",\"conclusion\":\"failure\",\"head_sha\":\"abc123\"},{\"id\":2,\"name\":\"running\",\"status\":\"in_progress\",\"conclusion\":null,\"head_sha\":\"abc123\"}]}"),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(Request("GitHub", "https://github.com/octo/repo"));

        Assert.Equal(RemoteCiState.Failing, evidence.CiResult);
        Assert.Equal(2, evidence.CiRuns.Count);
    }

    [Fact]
    public async Task GitHub_StatusesFollowRealNextPageAndCaptureLaterEvidence()
    {
        var provider = CreateGitHubProvider(new TestCredentialStore(), out var requests, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/statuses", StringComparison.Ordinal))
            {
                return request.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal)
                    ? Json("[{\"context\":\"build\",\"state\":\"success\",\"sha\":\"abc123\"}]")
                    : JsonWithHeaders("[]", ("Link", "<https://api.github.com/repos/octo/repo/commits/abc123/statuses?per_page=100&page=2>; rel=\"next\""));
            }

            return path switch
            {
                "/repos/octo/repo" => Json(GitHubRepository),
                "/repos/octo/repo/branches/main" => Json(GitHubBranch),
                _ when path.EndsWith("/check-runs", StringComparison.Ordinal) => Json(GitHubEmptyChecks),
                _ when path.EndsWith("/actions/runs", StringComparison.Ordinal) => Json(GitHubEmptyRuns),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(Request("GitHub", "https://github.com/octo/repo"));

        Assert.Equal(RemoteEvidenceState.Available, evidence.StatusState);
        Assert.Single(evidence.Statuses);
        Assert.Equal(2, requests.Count(request => request.RequestUri!.AbsolutePath.EndsWith("/statuses", StringComparison.Ordinal)));
        Assert.All(requests.Where(request => request.RequestUri!.AbsolutePath.EndsWith("/statuses", StringComparison.Ordinal)),
            request => Assert.Contains("per_page=100", request.RequestUri!.Query, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GitHub_ReviewsFollowRealNextPage()
    {
        var provider = CreateGitHubProvider(new TestCredentialStore(), out var requests, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/repos/octo/repo" => Json(GitHubRepository),
                "/repos/octo/repo/branches/main" => Json(GitHubBranch),
                "/repos/octo/repo/pulls/7" => Json("{\"number\":7,\"state\":\"open\",\"head\":{\"ref\":\"feature\",\"sha\":\"def456\"},\"base\":{\"ref\":\"main\",\"sha\":\"abc123\"}}"),
                "/repos/octo/repo/pulls/7/requested_reviewers" => Json("{\"users\":[],\"teams\":[]}"),
                "/repos/octo/repo/pulls/7/reviews" => request.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal)
                    ? Json("[{\"id\":32,\"user\":{\"login\":\"later\"},\"state\":\"APPROVED\"}]")
                    : JsonWithHeaders("[{\"id\":31,\"user\":{\"login\":\"first\"},\"state\":\"COMMENTED\"}]", ("Link", "<https://api.github.com/repos/octo/repo/pulls/7/reviews?per_page=100&page=2>; rel=\"next\"")),
                _ when path.EndsWith("/statuses", StringComparison.Ordinal) => Json(GitHubEmptyStatuses),
                _ when path.EndsWith("/check-runs", StringComparison.Ordinal) => Json(GitHubEmptyChecks),
                _ when path.EndsWith("/actions/runs", StringComparison.Ordinal) => Json(GitHubEmptyRuns),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "GitHub", "https://github.com/octo/repo", pullRequestNumber: 7));

        Assert.Contains(evidence.Reviews, review => review.Reviewer == "first");
        Assert.Contains(evidence.Reviews, review => review.Reviewer == "later");
        Assert.Equal(RemoteEvidenceState.Available, evidence.ReviewState);
        Assert.Equal(2, requests.Count(request => request.RequestUri!.AbsolutePath.EndsWith("/reviews", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task GitHub_CheckTotalCountUsesLaterPageBeforeDeclaringExhaustive()
    {
        var provider = CreateGitHubProvider(new TestCredentialStore(), out _, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/repos/octo/repo" => Json(GitHubRepository),
                "/repos/octo/repo/branches/main" => Json(GitHubBranch),
                _ when path.EndsWith("/statuses", StringComparison.Ordinal) => Json(GitHubEmptyStatuses),
                _ when path.EndsWith("/check-runs", StringComparison.Ordinal) => request.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal)
                    ? Json("{\"total_count\":2,\"check_runs\":[{\"id\":12,\"name\":\"later\",\"status\":\"completed\",\"conclusion\":\"success\"}]}")
                    : JsonWithHeaders("{\"total_count\":2,\"check_runs\":[{\"id\":11,\"name\":\"first\",\"status\":\"completed\",\"conclusion\":\"success\"}]}", ("Link", "<https://api.github.com/repos/octo/repo/commits/abc123/check-runs?per_page=100&page=2>; rel=\"next\"")),
                _ when path.EndsWith("/actions/runs", StringComparison.Ordinal) => Json(GitHubEmptyRuns),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(Request("GitHub", "https://github.com/octo/repo"));

        Assert.Equal(RemoteEvidenceState.Available, evidence.CheckState);
        Assert.Equal(2, evidence.Checks.Count);
        Assert.DoesNotContain(evidence.Limitations, value => value.Contains("check", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GitHub_NextPageBeyondBoundIsPartial()
    {
        var provider = CreateGitHubProvider(new TestCredentialStore(), out var requests, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/statuses", StringComparison.Ordinal))
            {
                var page = request.RequestUri!.Query.Contains("page=", StringComparison.Ordinal)
                    ? int.Parse(request.RequestUri!.Query.Split("page=", StringSplitOptions.None)[1].Split('&')[0])
                    : 1;
                var next = $"<https://api.github.com/repos/octo/repo/commits/abc123/statuses?per_page=100&page={page + 1}>; rel=\"next\"";
                return JsonWithHeaders($"[{{\"context\":\"page-{page}\",\"state\":\"success\"}}]", ("Link", next));
            }

            return path switch
            {
                "/repos/octo/repo" => Json(GitHubRepository),
                "/repos/octo/repo/branches/main" => Json(GitHubBranch),
                _ when path.EndsWith("/check-runs", StringComparison.Ordinal) => Json(GitHubEmptyChecks),
                _ when path.EndsWith("/actions/runs", StringComparison.Ordinal) => Json(GitHubEmptyRuns),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(Request("GitHub", "https://github.com/octo/repo"));

        Assert.Equal(RemoteEvidenceState.Partial, evidence.StatusState);
        Assert.Equal(3, evidence.Statuses.Count);
        Assert.Equal(3, requests.Count(request => request.RequestUri!.AbsolutePath.EndsWith("/statuses", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(false, false, HttpStatusCode.Forbidden, RemoteEvidenceState.PermissionDenied)]
    [InlineData(true, false, HttpStatusCode.Forbidden, RemoteEvidenceState.RateLimited)]
    [InlineData(false, true, HttpStatusCode.Forbidden, RemoteEvidenceState.RateLimited)]
    [InlineData(false, false, (HttpStatusCode)429, RemoteEvidenceState.RateLimited)]
    public async Task GitHub_RateLimitSignalsAreProviderSpecific(
        bool primaryLimit,
        bool retryAfter,
        HttpStatusCode status,
        RemoteEvidenceState expectedState)
    {
        var provider = CreateGitHubProvider(new TestCredentialStore(), out _, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/repos/octo/repo" => Json(GitHubRepository),
                "/repos/octo/repo/branches/main" => Json(GitHubBranch),
                _ when path.EndsWith("/statuses", StringComparison.Ordinal) => Json(GitHubEmptyStatuses),
                _ when path.EndsWith("/check-runs", StringComparison.Ordinal) =>
                    StatusWithHeaders(status,
                        primaryLimit ? ("x-ratelimit-remaining", "0") : retryAfter ? ("retry-after", "60") : null),
                _ when path.EndsWith("/actions/runs", StringComparison.Ordinal) => Json(GitHubEmptyRuns),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(Request("GitHub", "https://github.com/octo/repo"));

        Assert.Equal(expectedState, evidence.CheckState);
        var message = evidence.SafeErrorMessage ?? string.Empty;
        if (expectedState is RemoteEvidenceState.RateLimited)
        {
            Assert.Contains("rate-limited", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("permission", message, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Contains("permission", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("rate-limit", message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(false, false, HttpStatusCode.Forbidden, RemoteEvidenceState.PermissionDenied)]
    [InlineData(true, false, HttpStatusCode.Forbidden, RemoteEvidenceState.RateLimited)]
    [InlineData(false, true, HttpStatusCode.Forbidden, RemoteEvidenceState.RateLimited)]
    [InlineData(false, false, (HttpStatusCode)429, RemoteEvidenceState.RateLimited)]
    public async Task GitHub_RepositoryFailuresHaveMatchingSafeMessages(
        bool primaryLimit,
        bool retryAfter,
        HttpStatusCode status,
        RemoteEvidenceState expectedState)
    {
        var provider = CreateGitHubProvider(new TestCredentialStore(), out _, _ =>
            StatusWithHeaders(status,
                primaryLimit ? ("x-ratelimit-remaining", "0") : retryAfter ? ("retry-after", "60") : null));

        var evidence = await provider.InspectAsync(Request("GitHub", "https://github.com/octo/repo"));

        Assert.Equal(expectedState, evidence.State);
        var message = evidence.SafeErrorMessage ?? string.Empty;
        if (expectedState is RemoteEvidenceState.RateLimited)
        {
            Assert.Contains("rate-limited", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("permission", message, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Contains("permission", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("rate-limit", message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task GitHub_UntrustedNextPageIsNotFollowed()
    {
        var provider = CreateGitHubProvider(new TestCredentialStore(), out var requests, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/repos/octo/repo" => Json(GitHubRepository),
                "/repos/octo/repo/branches/main" => Json(GitHubBranch),
                _ when path.EndsWith("/statuses", StringComparison.Ordinal) => JsonWithHeaders(
                    "[]", ("Link", "<https://evil.example/repos/octo/repo/commits/abc123/statuses?page=2>; rel=\"next\"")),
                _ when path.EndsWith("/check-runs", StringComparison.Ordinal) => Json(GitHubEmptyChecks),
                _ when path.EndsWith("/actions/runs", StringComparison.Ordinal) => Json(GitHubEmptyRuns),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(Request("GitHub", "https://github.com/octo/repo"));

        Assert.Equal(RemoteEvidenceState.Partial, evidence.StatusState);
        Assert.Equal(1, requests.Count(request => request.RequestUri!.AbsolutePath.EndsWith("/statuses", StringComparison.Ordinal)));
        Assert.DoesNotContain(requests, request => request.RequestUri!.Host == "evil.example");
    }

    [Fact]
    public async Task GitHub_Timeout_IsUnavailable_AndCancellation_IsCancelled()
    {
        var timeoutHandler = new DelegateHttpMessageHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new OperationCanceledException()));
        var timeoutProvider = new GitHubRemoteRepositoryEvidenceProvider(
            new TestClock(), new TestHttpClientFactory(timeoutHandler), new TestCredentialStore());

        var timeoutEvidence = await timeoutProvider.InspectAsync(Request("GitHub", "https://github.com/octo/repo"));
        Assert.Equal(RemoteEvidenceState.Unavailable, timeoutEvidence.State);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelledEvidence = await timeoutProvider.InspectAsync(
            Request("GitHub", "https://github.com/octo/repo"), cancellation.Token);
        Assert.Equal(RemoteEvidenceState.Cancelled, cancelledEvidence.State);
    }

    [Fact]
    public async Task Azure_NormalizesRepositoryPullRequestStatusesAndBuilds_UsingBasicCredential()
    {
        var credentials = new TestCredentialStore();
        credentials.Add("azure-ref", "fixture-token");
        var provider = CreateAzureProvider(credentials, out var requests, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/org/Project/_apis/git/repositories/repo" => Json("{\"id\":\"repo-guid\",\"name\":\"repo\",\"defaultBranch\":\"refs/heads/main\",\"project\":{\"id\":\"project-guid\",\"name\":\"Project\"},\"url\":\"https://dev.azure.com/org/Project/_git/repo\"}"),
                "/org/Project/_apis/git/repositories/repo-guid/refs" => Json("{\"value\":[{\"name\":\"refs/heads/main\",\"objectId\":\"abc123\"}]}"),
                "/org/Project/_apis/git/repositories/repo-guid/pullRequests/7" => Json("{\"pullRequestId\":7,\"status\":\"active\",\"isDraft\":false,\"sourceRefName\":\"refs/heads/feature\",\"targetRefName\":\"refs/heads/main\",\"lastMergeSourceCommit\":{\"commitId\":\"def456\"},\"lastMergeTargetCommit\":{\"commitId\":\"abc123\"},\"mergeStatus\":\"succeeded\",\"repository\":{\"id\":\"repo-guid\"},\"url\":\"https://dev.azure.com/org/Project/_git/repo/pullrequest/7\",\"reviewers\":[{\"id\":\"alice\",\"displayName\":\"Alice\",\"vote\":10},{\"id\":\"bob\",\"displayName\":\"Bob\",\"vote\":-10}]}"),
                "/org/Project/_apis/git/repositories/repo-guid/commits/def456/statuses" => Json("{\"value\":[{\"context\":{\"name\":\"commit-check\"},\"state\":\"succeeded\",\"description\":\"ok\",\"commitId\":\"def456\",\"creationDate\":\"2026-08-22T11:00:00Z\",\"targetUrl\":\"https://dev.azure.com/org/Project/_build/results\"}]}"),
                "/org/Project/_apis/git/repositories/repo-guid/pullRequests/7/statuses" => Json("{\"value\":[{\"context\":{\"name\":\"policy\"},\"state\":\"succeeded\",\"description\":\"ok\",\"creationDate\":\"2026-08-22T11:00:00Z\",\"targetUrl\":\"https://dev.azure.com/org/Project/_build/results\"}]}"),
                "/org/Project/_apis/build/builds" => Json("{\"value\":[{\"id\":99,\"buildNumber\":\"2026.08.22.1\",\"definition\":{\"name\":\"CI\"},\"status\":\"completed\",\"result\":\"succeeded\",\"sourceBranch\":\"refs/heads/feature\",\"sourceVersion\":\"def456\",\"queueTime\":\"2026-08-22T11:00:00Z\",\"finishTime\":\"2026-08-22T11:30:00Z\",\"_links\":{\"web\":{\"href\":\"https://dev.azure.com/org/Project/_build/results/99\"}}}]}"),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(),
            "AzureRepos",
            "ssh://git@ssh.dev.azure.com/v3/org/Project/repo.git",
            repositoryId: "repo-guid",
            repositoryMetadata: new Dictionary<string, string?> { ["credentialReference"] = "azure-ref" },
            requestedBranch: "main",
            pullRequestNumber: 7));

        Assert.Equal(RemoteEvidenceState.Available, evidence.State);
        Assert.Equal(RemoteEvidenceSource.AzureDevOpsRest, evidence.Source);
        Assert.Equal("repo-guid", evidence.Repository!.ProviderRepositoryId);
        Assert.Equal("org/Project/repo", evidence.Repository.CanonicalName);
        Assert.Equal("open", evidence.PullRequest!.State);
        Assert.Equal("def456", evidence.PullRequest!.HeadCommitId);
        Assert.Equal(RemoteMergeability.Available, evidence.PullRequest.Mergeability);
        Assert.Equal(2, evidence.Reviews.Count);
        Assert.Contains(evidence.Reviews, review => review.State == "approved");
        Assert.Equal(2, evidence.Statuses.Count);
        Assert.Contains(evidence.Statuses, status => status.Kind == RemoteStatusKind.PullRequestStatus);
        Assert.Equal(RemoteCiState.Passing, evidence.CiResult);
        Assert.All(requests, request => Assert.Equal(HttpMethod.Get, request.Method));
        Assert.All(requests, request => Assert.Equal("Basic", request.Headers.Authorization?.Scheme));
        Assert.DoesNotContain(requests, request => request.RequestUri!.AbsolutePath.Contains("/items", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("fixture-token", evidence.SafeErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var commitStatusRequest = requests.Single(request => request.RequestUri!.AbsolutePath.EndsWith(
            "/commits/def456/statuses",
            StringComparison.Ordinal));
        Assert.Contains("top=100", Uri.UnescapeDataString(commitStatusRequest.RequestUri!.Query), StringComparison.Ordinal);
        Assert.DoesNotContain("$top=", Uri.UnescapeDataString(commitStatusRequest.RequestUri!.Query), StringComparison.Ordinal);
        Assert.Equal("https://dev.azure.com/org/Project/_build/results/99", evidence.CiRuns.Single().WebUrl!.ToString());
    }

    [Theory]
    [InlineData("active", "open")]
    [InlineData("completed", "merged")]
    [InlineData("abandoned", "closed")]
    [InlineData("notSet", "unknown")]
    [InlineData("unexpected", "unknown")]
    public async Task Azure_PullRequestStatusIsNormalizedAtEvidenceBoundary(string rawStatus, string expectedState)
    {
        var provider = CreateAzurePullRequestStateProvider(rawStatus, out _);
        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "AzureRepos", "https://dev.azure.com/org/Project/_git/repo", repositoryId: "repo-guid",
            repositoryMetadata: new Dictionary<string, string?> { ["credentialReference"] = "azure-ref" },
            requestedBranch: "main", pullRequestNumber: 7));

        Assert.Equal(RemoteEvidenceState.Available, evidence.PullRequestState);
        Assert.Equal(expectedState, evidence.PullRequest!.State);
    }

    [Theory]
    [InlineData(99, false, false, 1)]
    [InlineData(100, false, false, 2)]
    [InlineData(100, true, true, 2)]
    public async Task Azure_CommitStatusesProveExhaustivenessWithBoundedLookAhead(
        int firstPageCount,
        bool lookAheadHasStatus,
        bool expectedPartial,
        int expectedStatusRequests)
    {
        var provider = CreateAzureProvider(AzureCredentials(), out var requests, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/commits/abc123/statuses", StringComparison.Ordinal))
            {
                return request.RequestUri.Query.Contains("skip=100", StringComparison.Ordinal)
                    ? Json($"{{\"value\":[{(lookAheadHasStatus ? AzureStatusValues(101, 1) : string.Empty)}]}}")
                    : Json($"{{\"value\":[{AzureStatusValues(1, firstPageCount)}]}}");
            }

            return path switch
            {
                "/org/Project/_apis/git/repositories/repo" => Json("{\"id\":\"repo-guid\",\"name\":\"repo\",\"defaultBranch\":\"refs/heads/main\",\"project\":{\"id\":\"project-guid\",\"name\":\"Project\"}}"),
                "/org/Project/_apis/git/repositories/repo-guid/refs" => Json("{\"value\":[{\"name\":\"refs/heads/main\",\"objectId\":\"abc123\"}]}"),
                "/org/Project/_apis/build/builds" => Json("{\"value\":[]}"),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "AzureRepos", "https://dev.azure.com/org/Project/_git/repo",
            repositoryId: "repo-guid", credentialReference: "azure-ref"));

        Assert.Equal(expectedPartial ? RemoteEvidenceState.Partial : RemoteEvidenceState.Available, evidence.StatusState);
        Assert.Equal(Math.Min(firstPageCount, 100), evidence.Statuses.Count);
        var statusRequests = requests.Where(request => request.RequestUri!.AbsolutePath.EndsWith(
            "/commits/abc123/statuses", StringComparison.Ordinal)).ToArray();
        Assert.Equal(expectedStatusRequests, statusRequests.Length);
        var firstQuery = Uri.UnescapeDataString(statusRequests[0].RequestUri!.Query);
        Assert.Contains("top=100", firstQuery, StringComparison.Ordinal);
        Assert.Contains("skip=0", firstQuery, StringComparison.Ordinal);
        if (firstPageCount == 100)
        {
            var lookAheadQuery = Uri.UnescapeDataString(statusRequests[1].RequestUri!.Query);
            Assert.Contains("top=1", lookAheadQuery, StringComparison.Ordinal);
            Assert.Contains("skip=100", lookAheadQuery, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task GitHub_ReviewsShareOneGlobalBoundAcrossSources()
    {
        var users = string.Join(',', Enumerable.Range(1, 40).Select(index => $"{{\"login\":\"user-{index}\"}}"));
        var teams = string.Join(',', Enumerable.Range(41, 40).Select(index => $"{{\"slug\":\"team-{index}\"}}"));
        var submitted = string.Join(',', Enumerable.Range(81, 30).Select(index => $"{{\"id\":{index},\"user\":{{\"login\":\"reviewer-{index}\"}},\"state\":\"APPROVED\"}}"));
        var provider = CreateGitHubProvider(new TestCredentialStore(), out _, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/repos/octo/repo" => Json(GitHubRepository),
                "/repos/octo/repo/branches/main" => Json(GitHubBranch),
                "/repos/octo/repo/pulls/7" => Json("{\"number\":7,\"state\":\"open\",\"head\":{\"ref\":\"feature\",\"sha\":\"def456\"},\"base\":{\"ref\":\"main\",\"sha\":\"abc123\"}}"),
                "/repos/octo/repo/pulls/7/requested_reviewers" => Json($"{{\"users\":[{users}],\"teams\":[{teams}]}}"),
                "/repos/octo/repo/pulls/7/reviews" => Json($"[{submitted}]"),
                _ when path.EndsWith("/statuses", StringComparison.Ordinal) => Json(GitHubEmptyStatuses),
                _ when path.EndsWith("/check-runs", StringComparison.Ordinal) => Json(GitHubEmptyChecks),
                _ when path.EndsWith("/actions/runs", StringComparison.Ordinal) => Json(GitHubEmptyRuns),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "GitHub", "https://github.com/octo/repo", pullRequestNumber: 7));

        Assert.Equal(100, evidence.Reviews.Count);
        Assert.Equal(RemoteEvidenceState.Partial, evidence.ReviewState);
        Assert.Contains(evidence.Limitations, limitation => limitation.Contains("review", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Azure_StatusesShareOneGlobalBoundAcrossCommitAndPullRequestSources()
    {
        var provider = CreateAzureProvider(AzureCredentials(), out _, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/org/Project/_apis/git/repositories/repo" => Json("{\"id\":\"repo-guid\",\"name\":\"repo\",\"defaultBranch\":\"refs/heads/main\",\"project\":{\"id\":\"project-guid\",\"name\":\"Project\"}}"),
                "/org/Project/_apis/git/repositories/repo-guid/refs" => Json("{\"value\":[{\"name\":\"refs/heads/main\",\"objectId\":\"abc123\"}]}"),
                "/org/Project/_apis/git/repositories/repo-guid/pullRequests/7" => Json("{\"pullRequestId\":7,\"status\":\"active\",\"sourceRefName\":\"refs/heads/feature\",\"targetRefName\":\"refs/heads/main\",\"lastMergeSourceCommit\":{\"commitId\":\"def456\"},\"repository\":{\"id\":\"repo-guid\"}}"),
                "/org/Project/_apis/git/repositories/repo-guid/commits/def456/statuses" => Json($"{{\"value\":[{AzureStatusValues(1, 80)}]}}"),
                "/org/Project/_apis/git/repositories/repo-guid/pullRequests/7/statuses" => Json($"{{\"value\":[{AzureStatusValues(81, 30)}]}}"),
                "/org/Project/_apis/build/builds" => Json("{\"value\":[]}"),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "AzureRepos", "https://dev.azure.com/org/Project/_git/repo",
            repositoryId: "repo-guid", pullRequestNumber: 7, credentialReference: "azure-ref"));

        Assert.Equal(100, evidence.Statuses.Count);
        Assert.Equal(RemoteEvidenceState.Partial, evidence.StatusState);
        Assert.Contains(evidence.Limitations, limitation => limitation.Contains("status", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Azure_ReviewersUseGlobalBoundAndBecomePartialWhenTruncated()
    {
        var reviewers = string.Join(',', Enumerable.Range(1, 101).Select(index => $"{{\"id\":\"reviewer-{index}\",\"displayName\":\"Reviewer {index}\",\"vote\":0}}"));
        var provider = CreateAzureProvider(AzureCredentials(), out _, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/org/Project/_apis/git/repositories/repo" => Json("{\"id\":\"repo-guid\",\"name\":\"repo\",\"defaultBranch\":\"refs/heads/main\",\"project\":{\"id\":\"project-guid\",\"name\":\"Project\"}}"),
                "/org/Project/_apis/git/repositories/repo-guid/refs" => Json("{\"value\":[{\"name\":\"refs/heads/main\",\"objectId\":\"abc123\"}]}"),
                "/org/Project/_apis/git/repositories/repo-guid/pullRequests/7" => Json($"{{\"pullRequestId\":7,\"status\":\"active\",\"sourceRefName\":\"refs/heads/feature\",\"targetRefName\":\"refs/heads/main\",\"lastMergeSourceCommit\":{{\"commitId\":\"def456\"}},\"repository\":{{\"id\":\"repo-guid\"}},\"reviewers\":[{reviewers}]}}"),
                "/org/Project/_apis/git/repositories/repo-guid/commits/def456/statuses" => Json("{\"value\":[]}"),
                "/org/Project/_apis/git/repositories/repo-guid/pullRequests/7/statuses" => Json("{\"value\":[]}"),
                "/org/Project/_apis/build/builds" => Json("{\"value\":[]}"),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "AzureRepos", "https://dev.azure.com/org/Project/_git/repo",
            repositoryId: "repo-guid", pullRequestNumber: 7, credentialReference: "azure-ref"));

        Assert.Equal(100, evidence.Reviews.Count);
        Assert.Equal(RemoteEvidenceState.Partial, evidence.ReviewState);
        Assert.Contains(evidence.Limitations, limitation => limitation.Contains("reviewer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Azure_OversizedRefContinuationIsPartialWithoutFollowingOrExposingToken()
    {
        var token = new string('x', 1_025);
        var provider = CreateAzureProvider(AzureCredentials(), out var requests, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/org/Project/_apis/git/repositories/repo" => Json("{\"id\":\"repo-guid\",\"name\":\"repo\",\"defaultBranch\":\"refs/heads/main\",\"project\":{\"id\":\"project-guid\",\"name\":\"Project\"}}"),
                "/org/Project/_apis/git/repositories/repo-guid/refs" => JsonWithHeaders("{\"value\":[]}", ("x-ms-continuationtoken", token)),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "AzureRepos", "https://dev.azure.com/org/Project/_git/repo",
            repositoryId: "repo-guid", credentialReference: "azure-ref"));

        Assert.Equal(RemoteEvidenceState.Partial, evidence.BranchState);
        Assert.Single(requests, request => request.RequestUri!.AbsolutePath.EndsWith("/refs", StringComparison.Ordinal));
        Assert.DoesNotContain(token, string.Join(' ', evidence.Limitations), StringComparison.Ordinal);
        Assert.DoesNotContain(token, evidence.SafeErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Azure_OversizedBuildContinuationIsPartialWithoutFollowingOrExposingToken()
    {
        var token = new string('x', 1_025);
        var provider = CreateAzureProvider(AzureCredentials(), out var requests, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/org/Project/_apis/git/repositories/repo" => Json("{\"id\":\"repo-guid\",\"name\":\"repo\",\"defaultBranch\":\"refs/heads/main\",\"project\":{\"id\":\"project-guid\",\"name\":\"Project\"}}"),
                "/org/Project/_apis/git/repositories/repo-guid/refs" => Json("{\"value\":[{\"name\":\"refs/heads/main\",\"objectId\":\"abc123\"}]}"),
                "/org/Project/_apis/git/repositories/repo-guid/commits/abc123/statuses" => Json("{\"value\":[]}"),
                "/org/Project/_apis/build/builds" => JsonWithHeaders("{\"value\":[]}", ("x-ms-continuationtoken", token)),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "AzureRepos", "https://dev.azure.com/org/Project/_git/repo",
            repositoryId: "repo-guid", credentialReference: "azure-ref"));

        Assert.Equal(RemoteEvidenceState.Partial, evidence.CiState);
        Assert.Equal(RemoteCiState.Unknown, evidence.CiResult);
        Assert.Single(requests, request => request.RequestUri!.AbsolutePath.EndsWith("/build/builds", StringComparison.Ordinal));
        Assert.DoesNotContain(token, string.Join(' ', evidence.Limitations), StringComparison.Ordinal);
        Assert.DoesNotContain(token, evidence.SafeErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Azure_UnsafeBuildWebUrlIsRejected()
    {
        var provider = CreateAzureProvider(AzureCredentials(), out _, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/org/Project/_apis/git/repositories/repo" => Json("{\"id\":\"repo-guid\",\"name\":\"repo\",\"defaultBranch\":\"refs/heads/main\",\"project\":{\"id\":\"project-guid\",\"name\":\"Project\"}}"),
                "/org/Project/_apis/git/repositories/repo-guid/refs" => Json("{\"value\":[{\"name\":\"refs/heads/main\",\"objectId\":\"abc123\"}]}"),
                "/org/Project/_apis/git/repositories/repo-guid/commits/abc123/statuses" => Json("{\"value\":[]}"),
                "/org/Project/_apis/build/builds" => Json("{\"value\":[{\"id\":99,\"sourceVersion\":\"abc123\",\"status\":\"completed\",\"result\":\"succeeded\",\"_links\":{\"web\":{\"href\":\"https://evil.example/build/99\"}}}]}"),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "AzureRepos", "https://dev.azure.com/org/Project/_git/repo", repositoryId: "repo-guid",
            credentialReference: "azure-ref"));

        Assert.Equal(RemoteCiState.Passing, evidence.CiResult);
        Assert.Null(evidence.CiRuns.Single().WebUrl);
    }

    [Fact]
    public async Task Azure_BranchSelectionRequiresExactRefIdentity()
    {
        var provider = CreateAzureProvider(AzureCredentials(), out var requests, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/org/Project/_apis/git/repositories/repo" => Json("{\"id\":\"repo-guid\",\"name\":\"repo\",\"defaultBranch\":\"refs/heads/main\",\"project\":{\"id\":\"project-guid\",\"name\":\"Project\"}}"),
                "/org/Project/_apis/git/repositories/repo-guid/refs" => Json("{\"value\":[{\"name\":\"refs/heads/main-other\",\"objectId\":\"wrong\"},{\"name\":\"refs/heads/main\",\"objectId\":\"abc123\"}]}"),
                "/org/Project/_apis/git/repositories/repo-guid/commits/abc123/statuses" => Json("{\"value\":[]}"),
                "/org/Project/_apis/build/builds" => Json("{\"value\":[]}"),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "AzureRepos", "https://dev.azure.com/org/Project/_git/repo", repositoryId: "repo-guid", credentialReference: "azure-ref"));

        Assert.Equal(RemoteEvidenceState.Available, evidence.BranchState);
        Assert.Equal("main", evidence.Branch!.BranchName);
        Assert.Equal("abc123", evidence.Branch.CommitId);
        var refsRequest = requests.Single(request => request.RequestUri!.AbsolutePath.EndsWith("/refs", StringComparison.Ordinal));
        Assert.Contains("filter=refs%2Fheads%2Fmain", refsRequest.RequestUri!.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Azure_BranchPrefixCollisionOnlyIsUnavailable()
    {
        var provider = CreateAzureProvider(AzureCredentials(), out _, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/org/Project/_apis/git/repositories/repo" => Json("{\"id\":\"repo-guid\",\"name\":\"repo\",\"defaultBranch\":\"refs/heads/main\",\"project\":{\"id\":\"project-guid\",\"name\":\"Project\"}}"),
                "/org/Project/_apis/git/repositories/repo-guid/refs" => Json("{\"value\":[{\"name\":\"refs/heads/main-other\",\"objectId\":\"wrong\"}]}"),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "AzureRepos", "https://dev.azure.com/org/Project/_git/repo", repositoryId: "repo-guid", credentialReference: "azure-ref"));

        Assert.Equal(RemoteEvidenceState.Unavailable, evidence.BranchState);
        Assert.Null(evidence.Branch);
        Assert.Equal(RemoteEvidenceState.Partial, evidence.State);
    }

    [Fact]
    public async Task Azure_RefCaseDifferenceIsNotAnExactMatch()
    {
        var provider = CreateAzureProvider(AzureCredentials(), out _, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/org/Project/_apis/git/repositories/repo" => Json("{\"id\":\"repo-guid\",\"name\":\"repo\",\"defaultBranch\":\"refs/heads/main\",\"project\":{\"id\":\"project-guid\",\"name\":\"Project\"}}"),
                "/org/Project/_apis/git/repositories/repo-guid/refs" => Json("{\"value\":[{\"name\":\"refs/heads/Main\",\"objectId\":\"wrong\"}]}"),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "AzureRepos", "https://dev.azure.com/org/Project/_git/repo", repositoryId: "repo-guid", requestedBranch: "main", credentialReference: "azure-ref"));

        Assert.Equal(RemoteEvidenceState.Unavailable, evidence.BranchState);
        Assert.Null(evidence.Branch);
    }

    [Fact]
    public async Task Azure_EmptyRefsAreTruthfullyUnavailable()
    {
        var provider = CreateAzureProvider(AzureCredentials(), out _, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/org/Project/_apis/git/repositories/repo" => Json("{\"id\":\"repo-guid\",\"name\":\"repo\",\"defaultBranch\":\"refs/heads/main\",\"project\":{\"id\":\"project-guid\",\"name\":\"Project\"}}"),
                "/org/Project/_apis/git/repositories/repo-guid/refs" => Json("{\"value\":[]}"),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "AzureRepos", "https://dev.azure.com/org/Project/_git/repo", repositoryId: "repo-guid", credentialReference: "azure-ref"));

        Assert.Equal(RemoteEvidenceState.Unavailable, evidence.BranchState);
        Assert.Null(evidence.Branch);
    }

    [Fact]
    public async Task Azure_BuildSearchUsesPrSourceBranchAndHeadCommit()
    {
        var provider = CreateAzureProvider(AzureCredentials(), out var requests, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/org/Project/_apis/git/repositories/repo" => Json("{\"id\":\"repo-guid\",\"name\":\"repo\",\"defaultBranch\":\"refs/heads/main\",\"project\":{\"id\":\"project-guid\",\"name\":\"Project\"}}"),
                "/org/Project/_apis/git/repositories/repo-guid/refs" => Json("{\"value\":[{\"name\":\"refs/heads/main\",\"objectId\":\"abc123\"}]}"),
                "/org/Project/_apis/git/repositories/repo-guid/pullRequests/7" => Json("{\"pullRequestId\":7,\"status\":\"active\",\"sourceRefName\":\"refs/heads/feature\",\"targetRefName\":\"refs/heads/main\",\"lastMergeSourceCommit\":{\"commitId\":\"def456\"},\"repository\":{\"id\":\"repo-guid\"}}"),
                "/org/Project/_apis/git/repositories/repo-guid/commits/def456/statuses" => Json("{\"value\":[]}"),
                "/org/Project/_apis/git/repositories/repo-guid/pullRequests/7/statuses" => Json("{\"value\":[]}"),
                "/org/Project/_apis/build/builds" => Json("{\"value\":[{\"id\":99,\"sourceBranch\":\"refs/heads/feature\",\"sourceVersion\":\"def456\",\"status\":\"completed\",\"result\":\"succeeded\"}]}"),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "AzureRepos", "https://dev.azure.com/org/Project/_git/repo", repositoryId: "repo-guid",
            pullRequestNumber: 7, credentialReference: "azure-ref"));

        var buildRequest = requests.Single(request => request.RequestUri!.AbsolutePath.EndsWith("/_apis/build/builds", StringComparison.Ordinal));
        var query = Uri.UnescapeDataString(buildRequest.RequestUri!.Query);
        Assert.Contains("branchName=refs/heads/feature", query, StringComparison.Ordinal);
        Assert.Equal("def456", evidence.CiRuns.Single().HeadCommitId);
    }

    [Fact]
    public async Task Azure_NonPrBuildSearchUsesRequestedBranchHeadPair()
    {
        var provider = CreateAzureProvider(AzureCredentials(), out var requests, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/org/Project/_apis/git/repositories/repo" => Json("{\"id\":\"repo-guid\",\"name\":\"repo\",\"defaultBranch\":\"refs/heads/main\",\"project\":{\"id\":\"project-guid\",\"name\":\"Project\"}}"),
                "/org/Project/_apis/git/repositories/repo-guid/refs" => Json("{\"value\":[{\"name\":\"refs/heads/main\",\"objectId\":\"abc123\"}]}"),
                "/org/Project/_apis/git/repositories/repo-guid/commits/abc123/statuses" => Json("{\"value\":[]}"),
                "/org/Project/_apis/build/builds" => Json("{\"value\":[]}"),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "AzureRepos", "https://dev.azure.com/org/Project/_git/repo", repositoryId: "repo-guid", requestedBranch: "main", credentialReference: "azure-ref"));

        var buildRequest = requests.Single(request => request.RequestUri!.AbsolutePath.EndsWith("/_apis/build/builds", StringComparison.Ordinal));
        var query = Uri.UnescapeDataString(buildRequest.RequestUri!.Query);
        Assert.Contains("branchName=refs/heads/main", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Azure_BuildContinuationFindsCommitOnLaterPage()
    {
        var provider = CreateAzureProvider(AzureCredentials(), out var requests, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/build/builds", StringComparison.Ordinal))
            {
                return request.RequestUri!.Query.Contains("continuationToken=page-2", StringComparison.Ordinal)
                    ? Json("{\"value\":[{\"id\":2,\"sourceVersion\":\"abc123\",\"status\":\"completed\",\"result\":\"succeeded\"}]}" )
                    : JsonWithHeaders("{\"value\":[{\"id\":1,\"sourceVersion\":\"other\",\"status\":\"completed\",\"result\":\"succeeded\"}]}", ("x-ms-continuationtoken", "page-2"));
            }

            return path switch
            {
                "/org/Project/_apis/git/repositories/repo" => Json("{\"id\":\"repo-guid\",\"name\":\"repo\",\"defaultBranch\":\"refs/heads/main\",\"project\":{\"id\":\"project-guid\",\"name\":\"Project\"}}"),
                "/org/Project/_apis/git/repositories/repo-guid/refs" => Json("{\"value\":[{\"name\":\"refs/heads/main\",\"objectId\":\"abc123\"}]}"),
                "/org/Project/_apis/git/repositories/repo-guid/commits/abc123/statuses" => Json("{\"value\":[]}"),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "AzureRepos", "https://dev.azure.com/org/Project/_git/repo", repositoryId: "repo-guid", credentialReference: "azure-ref"));

        Assert.Equal(RemoteCiState.Passing, evidence.CiResult);
        Assert.Equal("2", evidence.CiRuns.Single().Id);
        Assert.Equal(2, requests.Count(request => request.RequestUri!.AbsolutePath.EndsWith("/build/builds", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Azure_BuildContinuationBeyondBoundIsPartialNotNoEvidence()
    {
        var pageNumber = 0;
        var provider = CreateAzureProvider(AzureCredentials(), out var requests, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/build/builds", StringComparison.Ordinal))
            {
                return JsonWithHeaders("{\"value\":[]}", ("x-ms-continuationtoken", $"next-{++pageNumber}"));
            }

            return path switch
            {
                "/org/Project/_apis/git/repositories/repo" => Json("{\"id\":\"repo-guid\",\"name\":\"repo\",\"defaultBranch\":\"refs/heads/main\",\"project\":{\"id\":\"project-guid\",\"name\":\"Project\"}}"),
                "/org/Project/_apis/git/repositories/repo-guid/refs" => Json("{\"value\":[{\"name\":\"refs/heads/main\",\"objectId\":\"abc123\"}]}"),
                "/org/Project/_apis/git/repositories/repo-guid/commits/abc123/statuses" => Json("{\"value\":[]}"),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "AzureRepos", "https://dev.azure.com/org/Project/_git/repo", repositoryId: "repo-guid", credentialReference: "azure-ref"));

        Assert.Equal(RemoteEvidenceState.Partial, evidence.CiState);
        Assert.Equal(RemoteCiState.Unknown, evidence.CiResult);
        Assert.NotEqual(RemoteCiState.NoEvidence, evidence.CiResult);
        Assert.Equal(3, requests.Count(request => request.RequestUri!.AbsolutePath.EndsWith("/build/builds", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Azure_NoBuildEvidence_IsNoEvidence_NotPassing()
    {
        var credentials = new TestCredentialStore();
        credentials.Add("azure-ref", "fixture-token");
        var provider = CreateAzureProvider(credentials, out _, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/org/Project/_apis/git/repositories/repo" => Json("{\"id\":\"repo-guid\",\"name\":\"repo\",\"defaultBranch\":\"refs/heads/main\",\"project\":{\"id\":\"project-guid\",\"name\":\"Project\"}}"),
                "/org/Project/_apis/git/repositories/repo-guid/refs" => Json("{\"value\":[{\"name\":\"refs/heads/main\",\"objectId\":\"abc123\"}]}"),
                "/org/Project/_apis/git/repositories/repo-guid/commits/abc123/statuses" => Json("{\"value\":[]}"),
                "/org/Project/_apis/build/builds" => Json("{\"value\":[]}"),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "AzureRepos", "https://dev.azure.com/org/Project/_git/repo", repositoryId: "repo-guid",
            repositoryMetadata: new Dictionary<string, string?> { ["credentialReference"] = "azure-ref" }));

        Assert.Equal(RemoteEvidenceState.Available, evidence.State);
        Assert.Equal(RemoteEvidenceState.Available, evidence.CiState);
        Assert.Equal(RemoteCiState.NoEvidence, evidence.CiResult);
    }

    [Fact]
    public async Task Azure_ForbiddenBuilds_ProducesPartialEvidence()
    {
        var credentials = new TestCredentialStore();
        credentials.Add("azure-ref", "fixture-token");
        var provider = CreateAzureProvider(credentials, out _, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/org/Project/_apis/git/repositories/repo" => Json("{\"id\":\"repo-guid\",\"name\":\"repo\",\"defaultBranch\":\"refs/heads/main\",\"project\":{\"id\":\"project-guid\",\"name\":\"Project\"}}"),
                "/org/Project/_apis/git/repositories/repo-guid/refs" => Json("{\"value\":[{\"name\":\"refs/heads/main\",\"objectId\":\"abc123\"}]}"),
                "/org/Project/_apis/git/repositories/repo-guid/commits/abc123/statuses" => Json("{\"value\":[]}"),
                "/org/Project/_apis/build/builds" => Status(HttpStatusCode.Forbidden),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "AzureRepos", "https://dev.azure.com/org/Project/_git/repo", repositoryId: "repo-guid",
            repositoryMetadata: new Dictionary<string, string?> { ["credentialReference"] = "azure-ref" }));

        Assert.Equal(RemoteEvidenceState.Partial, evidence.State);
        Assert.Equal(RemoteEvidenceState.PermissionDenied, evidence.CiState);
        Assert.Equal(RemoteCiState.Unknown, evidence.CiResult);
    }

    [Fact]
    public async Task Azure_FailedOrRunningBuildEvidence_IsNotPassing()
    {
        var credentials = new TestCredentialStore();
        credentials.Add("azure-ref", "fixture-token");
        var provider = CreateAzureProvider(credentials, out _, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/org/Project/_apis/git/repositories/repo" => Json("{\"id\":\"repo-guid\",\"name\":\"repo\",\"defaultBranch\":\"refs/heads/main\",\"project\":{\"id\":\"project-guid\",\"name\":\"Project\"}}"),
                "/org/Project/_apis/git/repositories/repo-guid/refs" => Json("{\"value\":[{\"name\":\"refs/heads/main\",\"objectId\":\"abc123\"}]}"),
                "/org/Project/_apis/git/repositories/repo-guid/commits/abc123/statuses" => Json("{\"value\":[]}"),
                "/org/Project/_apis/build/builds" => Json("{\"value\":[{\"id\":1,\"buildNumber\":\"failed\",\"status\":\"completed\",\"result\":\"failed\",\"sourceVersion\":\"abc123\"},{\"id\":2,\"buildNumber\":\"running\",\"status\":\"inProgress\",\"sourceVersion\":\"abc123\"}]}"),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "AzureRepos", "https://dev.azure.com/org/Project/_git/repo", repositoryId: "repo-guid",
            repositoryMetadata: new Dictionary<string, string?> { ["credentialReference"] = "azure-ref" }));

        Assert.Equal(RemoteEvidenceState.Available, evidence.State);
        Assert.Equal(RemoteCiState.Failing, evidence.CiResult);
        Assert.Equal(2, evidence.CiRuns.Count);
    }

    [Fact]
    public async Task Azure_MissingCredential_IsAuthenticationRequired_WithoutHttp()
    {
        var provider = CreateAzureProvider(new TestCredentialStore(), out var requests, _ => Status(HttpStatusCode.OK));

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "AzureRepos", "https://dev.azure.com/org/Project/_git/repo"));

        Assert.Equal(RemoteEvidenceState.AuthenticationRequired, evidence.State);
        Assert.Empty(requests);
    }

    [Fact]
    public async Task Azure_WrongProjectIdentity_IsRejectedBeforeBranchInspection()
    {
        var credentials = new TestCredentialStore();
        credentials.Add("azure-ref", "fixture-token");
        var provider = CreateAzureProvider(credentials, out var requests, request =>
            request.RequestUri!.AbsolutePath == "/org/Project/_apis/git/repositories/repo"
                ? Json("{\"id\":\"repo-guid\",\"name\":\"repo\",\"defaultBranch\":\"refs/heads/main\",\"project\":{\"id\":\"other-project\",\"name\":\"Project\"}}")
                : Status(HttpStatusCode.NotFound));

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "AzureRepos", "https://dev.azure.com/org/Project/_git/repo", repositoryId: "repo-guid",
            repositoryMetadata: new Dictionary<string, string?> { ["credentialReference"] = "azure-ref", ["projectId"] = "expected-project" }));

        Assert.Equal(RemoteEvidenceState.InvalidResponse, evidence.State);
        Assert.Single(requests);
    }

    [Fact]
    public async Task Azure_UnsafeRepositoryUrl_IsUnsupported_WithoutOutboundRequest()
    {
        var credentials = new TestCredentialStore();
        credentials.Add("azure-ref", "fixture-token");
        var provider = CreateAzureProvider(credentials, out var requests, _ => Status(HttpStatusCode.OK));

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "AzureRepos", "https://user:fixture-token@evil.example/org/Project/_git/repo",
            repositoryMetadata: new Dictionary<string, string?> { ["credentialReference"] = "azure-ref" }));

        Assert.Equal(RemoteEvidenceState.Unsupported, evidence.State);
        Assert.Empty(requests);
    }

    [Fact]
    public async Task Azure_BuildsAreGloballyBoundedAcrossPages_WhenLaterEvidenceIsDropped()
    {
        var provider = CreateAzureBuildProvider(out var requests, request =>
        {
            var query = Uri.UnescapeDataString(request.RequestUri!.Query);
            return query.Contains("continuationToken=page-2", StringComparison.Ordinal)
                ? Json(AzureBuildResponse(AzureBuildValues(61, 100)))
                : JsonWithHeaders(AzureBuildResponse(AzureBuildValues(1, 60)), ("x-ms-continuationtoken", "page-2"));
        });

        var evidence = await InspectAzureBuildsAsync(provider);
        var buildRequests = requests.Where(request => request.RequestUri!.AbsolutePath.EndsWith("/_apis/build/builds", StringComparison.Ordinal)).ToArray();

        Assert.Equal(2, buildRequests.Length);
        Assert.False(Uri.UnescapeDataString(buildRequests[0].RequestUri!.Query).Contains("continuationToken=", StringComparison.Ordinal));
        Assert.Contains("continuationToken=page-2", Uri.UnescapeDataString(buildRequests[1].RequestUri!.Query), StringComparison.Ordinal);
        Assert.Equal(100, evidence.CiRuns.Count);
        Assert.True(evidence.CiRuns.Count <= 100);
        Assert.Equal(RemoteEvidenceState.Partial, evidence.CiState);
        Assert.Equal(RemoteCiState.Unknown, evidence.CiResult);
        Assert.Contains(evidence.Limitations, limitation => limitation.Contains("capped", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Azure_ExactlyFullGlobalBuildBound_IsAvailableWhenPaginationIsExhaustive()
    {
        var provider = CreateAzureBuildProvider(out var requests, request =>
        {
            var query = Uri.UnescapeDataString(request.RequestUri!.Query);
            return query.Contains("continuationToken=page-2", StringComparison.Ordinal)
                ? Json(AzureBuildResponse(AzureBuildValues(61, 40)))
                : JsonWithHeaders(AzureBuildResponse(AzureBuildValues(1, 60)), ("x-ms-continuationtoken", "page-2"));
        });

        var evidence = await InspectAzureBuildsAsync(provider);
        var buildRequests = requests.Count(request => request.RequestUri!.AbsolutePath.EndsWith("/_apis/build/builds", StringComparison.Ordinal));

        Assert.Equal(2, buildRequests);
        Assert.Equal(100, evidence.CiRuns.Count);
        Assert.True(evidence.CiRuns.Count <= 100);
        Assert.Equal(RemoteEvidenceState.Available, evidence.CiState);
        Assert.Equal(RemoteCiState.Passing, evidence.CiResult);
    }

    [Fact]
    public async Task Azure_BuildsExceedingGlobalBoundWithinLaterPage_ArePartialAndUnknown()
    {
        var provider = CreateAzureBuildProvider(out var requests, request =>
        {
            var query = Uri.UnescapeDataString(request.RequestUri!.Query);
            return query.Contains("continuationToken=page-2", StringComparison.Ordinal)
                ? Json(AzureBuildResponse(AzureBuildValues(100, 2)))
                : JsonWithHeaders(AzureBuildResponse(AzureBuildValues(1, 99)), ("x-ms-continuationtoken", "page-2"));
        });

        var evidence = await InspectAzureBuildsAsync(provider);

        Assert.Equal(100, evidence.CiRuns.Count);
        Assert.True(evidence.CiRuns.Count <= 100);
        Assert.Equal(RemoteEvidenceState.Partial, evidence.CiState);
        Assert.Equal(RemoteCiState.Unknown, evidence.CiResult);
        Assert.Equal(2, requests.Count(request => request.RequestUri!.AbsolutePath.EndsWith("/_apis/build/builds", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Azure_BuildCapacityWithContinuation_StopsWithoutFollowingAnotherPage()
    {
        var provider = CreateAzureBuildProvider(out var requests, _ =>
            JsonWithHeaders(AzureBuildResponse(AzureBuildValues(1, 100)), ("x-ms-continuationtoken", "page-2")));

        var evidence = await InspectAzureBuildsAsync(provider);

        Assert.Equal(100, evidence.CiRuns.Count);
        Assert.True(evidence.CiRuns.Count <= 100);
        Assert.Equal(RemoteEvidenceState.Partial, evidence.CiState);
        Assert.Equal(RemoteCiState.Unknown, evidence.CiResult);
        Assert.Equal(1, requests.Count(request => request.RequestUri!.AbsolutePath.EndsWith("/_apis/build/builds", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Azure_PartialBuildEvidence_PreservesKnownRetainedFailure()
    {
        var provider = CreateAzureBuildProvider(out var requests, _ =>
            JsonWithHeaders(
                AzureBuildResponse(AzureBuildValues(1, 100, result: index => index == 1 ? "failed" : "succeeded")),
                ("x-ms-continuationtoken", "page-2")));

        var evidence = await InspectAzureBuildsAsync(provider);

        Assert.Equal(100, evidence.CiRuns.Count);
        Assert.True(evidence.CiRuns.Count <= 100);
        Assert.Equal(RemoteEvidenceState.Partial, evidence.CiState);
        Assert.Equal(RemoteCiState.Failing, evidence.CiResult);
        Assert.Single(requests, request => request.RequestUri!.AbsolutePath.EndsWith("/_apis/build/builds", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Azure_NonmatchingBuildsDoNotConsumeGlobalCiRunCapacity()
    {
        var provider = CreateAzureBuildProvider(out _, request =>
        {
            var query = Uri.UnescapeDataString(request.RequestUri!.Query);
            return query.Contains("continuationToken=page-2", StringComparison.Ordinal)
                ? Json(AzureBuildResponse(AzureBuildValues(1, 2)))
                : JsonWithHeaders(AzureBuildResponse(AzureBuildValues(1, 100, "other-commit")), ("x-ms-continuationtoken", "page-2"));
        });

        var evidence = await InspectAzureBuildsAsync(provider);

        Assert.Equal(2, evidence.CiRuns.Count);
        Assert.True(evidence.CiRuns.Count <= 100);
        Assert.Equal(RemoteEvidenceState.Available, evidence.CiState);
        Assert.Equal(RemoteCiState.Passing, evidence.CiResult);
        Assert.All(evidence.CiRuns, run => Assert.Equal("abc123", run.HeadCommitId));
    }

    [Fact]
    public async Task Azure_BuildEvidenceIsBoundedAndMarkedPartial()
    {
        var builds = string.Join(',', Enumerable.Range(1, 101).Select(index =>
            $"{{\"id\":{index},\"buildNumber\":\"{index}\",\"status\":\"completed\",\"result\":\"succeeded\",\"sourceVersion\":\"abc123\"}}"));
        var credentials = new TestCredentialStore();
        credentials.Add("azure-ref", "fixture-token");
        var provider = CreateAzureProvider(credentials, out _, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/org/Project/_apis/git/repositories/repo" => Json("{\"id\":\"repo-guid\",\"name\":\"repo\",\"defaultBranch\":\"refs/heads/main\",\"project\":{\"id\":\"project-guid\",\"name\":\"Project\"}}"),
                "/org/Project/_apis/git/repositories/repo-guid/refs" => Json("{\"value\":[{\"name\":\"refs/heads/main\",\"objectId\":\"abc123\"}]}"),
                "/org/Project/_apis/git/repositories/repo-guid/commits/abc123/statuses" => Json("{\"value\":[]}"),
                "/org/Project/_apis/build/builds" => Json($"{{\"value\":[{builds}]}}"),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "AzureRepos", "https://dev.azure.com/org/Project/_git/repo", repositoryId: "repo-guid",
            repositoryMetadata: new Dictionary<string, string?> { ["credentialReference"] = "azure-ref" }));

        Assert.Equal(RemoteEvidenceState.Partial, evidence.State);
        Assert.Equal(100, evidence.CiRuns.Count);
        Assert.NotEmpty(evidence.Limitations);
    }

    [Fact]
    public async Task Azure_Timeout_IsUnavailable()
    {
        var handler = new DelegateHttpMessageHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new OperationCanceledException()));
        var credentials = new TestCredentialStore();
        credentials.Add("azure-ref", "fixture-token");
        var provider = new AzureReposRemoteRepositoryEvidenceProvider(
            new TestClock(), new TestHttpClientFactory(handler), credentials);

        var evidence = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "AzureRepos", "https://dev.azure.com/org/Project/_git/repo",
            repositoryMetadata: new Dictionary<string, string?> { ["credentialReference"] = "azure-ref" }));

        Assert.Equal(RemoteEvidenceState.Unavailable, evidence.State);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "AzureRepos", "https://dev.azure.com/org/Project/_git/repo",
            repositoryMetadata: new Dictionary<string, string?> { ["credentialReference"] = "azure-ref" }), cancellation.Token);
        Assert.Equal(RemoteEvidenceState.Cancelled, cancelled.State);
    }

    [Fact]
    public async Task ApplicationService_SelectsConfiguredProvider_AndAnchorsProjectId()
    {
        var projectId = Guid.NewGuid();
        var adapter = new FakeRemoteProvider(RemoteRepositoryProvider.GitHub);
        var service = new RemoteRepositoryEvidenceService([adapter]);
        var project = ProjectWithRepository(projectId, "GitHub", "https://github.com/octo/repo");

        var evidence = await service.InspectAsync(project, "main");

        Assert.Equal(projectId, evidence.ProjectId);
        Assert.Equal(projectId, adapter.LastRequest!.ProjectId);
        Assert.Equal("main", adapter.LastRequest.RequestedBranch);
    }

    [Fact]
    public async Task ApplicationService_ReportsNotConfiguredAndUnsupportedWithoutThrowing()
    {
        var service = new RemoteRepositoryEvidenceService([]);
        var missing = await service.InspectAsync(ProjectWithRepository(Guid.NewGuid(), null, null));
        var unsupported = await service.InspectAsync(ProjectWithRepository(Guid.NewGuid(), "Bitbucket", "https://example.invalid/repo"));

        Assert.Equal(RemoteEvidenceState.NotConfigured, missing.State);
        Assert.Equal(RemoteEvidenceState.Unsupported, unsupported.State);
        Assert.Equal(RemoteEvidenceSource.Unknown, unsupported.Source);
    }

    private static RemoteRepositoryEvidenceRequest Request(string provider, string url) =>
        new(Guid.NewGuid(), provider, url);

    private static Project ProjectWithRepository(Guid id, string? provider, string? url) =>
        new(id, "Test", @"C:\work", "main", ProjectStatus.Active,
            new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero),
            repositoryProvider: provider,
            repositoryUrl: url);

    private static GitHubRemoteRepositoryEvidenceProvider CreateGitHubProvider(
        TestCredentialStore credentials,
        out List<HttpRequestMessage> requests,
        Func<HttpRequestMessage, HttpResponseMessage> route)
    {
        var capturedRequests = new List<HttpRequestMessage>();
        requests = capturedRequests;
        var handler = new DelegateHttpMessageHandler((request, _) =>
        {
            capturedRequests.Add(request);
            return Task.FromResult(route(request));
        });
        return new GitHubRemoteRepositoryEvidenceProvider(
            new TestClock(), new TestHttpClientFactory(handler), credentials);
    }

    private static AzureReposRemoteRepositoryEvidenceProvider CreateAzureProvider(
        TestCredentialStore credentials,
        out List<HttpRequestMessage> requests,
        Func<HttpRequestMessage, HttpResponseMessage> route)
    {
        var capturedRequests = new List<HttpRequestMessage>();
        requests = capturedRequests;
        var handler = new DelegateHttpMessageHandler((request, _) =>
        {
            capturedRequests.Add(request);
            return Task.FromResult(route(request));
        });
        return new AzureReposRemoteRepositoryEvidenceProvider(
            new TestClock(), new TestHttpClientFactory(handler), credentials);
    }

    private static AzureReposRemoteRepositoryEvidenceProvider CreateAzureBuildProvider(
        out List<HttpRequestMessage> requests,
        Func<HttpRequestMessage, HttpResponseMessage> buildRoute) =>
        CreateAzureProvider(AzureCredentials(), out requests, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/org/Project/_apis/git/repositories/repo" => Json("{\"id\":\"repo-guid\",\"name\":\"repo\",\"defaultBranch\":\"refs/heads/main\",\"project\":{\"id\":\"project-guid\",\"name\":\"Project\"}}"),
                "/org/Project/_apis/git/repositories/repo-guid/refs" => Json("{\"value\":[{\"name\":\"refs/heads/main\",\"objectId\":\"abc123\"}]}"),
                "/org/Project/_apis/git/repositories/repo-guid/commits/abc123/statuses" => Json("{\"value\":[]}"),
                _ when path.EndsWith("/_apis/build/builds", StringComparison.Ordinal) => buildRoute(request),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

    private static AzureReposRemoteRepositoryEvidenceProvider CreateAzurePullRequestStateProvider(
        string rawStatus,
        out List<HttpRequestMessage> requests) =>
        CreateAzureProvider(AzureCredentials(), out requests, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/org/Project/_apis/git/repositories/repo" => Json("{\"id\":\"repo-guid\",\"name\":\"repo\",\"defaultBranch\":\"refs/heads/main\",\"project\":{\"id\":\"project-guid\",\"name\":\"Project\"},\"url\":\"https://dev.azure.com/org/Project/_git/repo\"}"),
                "/org/Project/_apis/git/repositories/repo-guid/refs" => Json("{\"value\":[{\"name\":\"refs/heads/main\",\"objectId\":\"abc123\"}]}"),
                "/org/Project/_apis/git/repositories/repo-guid/pullRequests/7" => Json($"{{\"pullRequestId\":7,\"status\":\"{rawStatus}\",\"isDraft\":false,\"sourceRefName\":\"refs/heads/feature\",\"targetRefName\":\"refs/heads/main\",\"lastMergeSourceCommit\":{{\"commitId\":\"def456\"}},\"lastMergeTargetCommit\":{{\"commitId\":\"abc123\"}},\"mergeStatus\":\"succeeded\",\"repository\":{{\"id\":\"repo-guid\"}},\"reviewers\":[]}}"),
                "/org/Project/_apis/git/repositories/repo-guid/commits/def456/statuses" => Json("{\"value\":[]}"),
                "/org/Project/_apis/git/repositories/repo-guid/pullRequests/7/statuses" => Json("{\"value\":[]}"),
                "/org/Project/_apis/build/builds" => Json("{\"value\":[]}"),
                _ => Status(HttpStatusCode.NotFound)
            };
        });

    private static async Task<RemoteRepositoryEvidence> InspectAzureBuildsAsync(
        AzureReposRemoteRepositoryEvidenceProvider provider) =>
        await provider.InspectAsync(new RemoteRepositoryEvidenceRequest(
            Guid.NewGuid(), "AzureRepos", "https://dev.azure.com/org/Project/_git/repo", repositoryId: "repo-guid",
            repositoryMetadata: new Dictionary<string, string?> { ["credentialReference"] = "azure-ref" }));

    private static string AzureBuildResponse(string builds) => $"{{\"value\":[{builds}]}}";

    private static string AzureBuildValues(
        int start,
        int count,
        string sourceVersion = "abc123",
        Func<int, string>? result = null) =>
        string.Join(',', Enumerable.Range(start, count).Select(index =>
            $"{{\"id\":{index},\"buildNumber\":\"{index}\",\"status\":\"completed\",\"result\":\"{result?.Invoke(index) ?? "succeeded"}\",\"sourceVersion\":\"{sourceVersion}\"}}"));

    private static void AssertReadOnly(IEnumerable<HttpRequestMessage> requests, string host)
    {
        Assert.All(requests, request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(host, request.RequestUri!.Host);
            Assert.DoesNotContain("/contents", request.RequestUri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/items", request.RequestUri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("dispatch", request.RequestUri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("queue", request.RequestUri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        DelegateHttpMessageHandler.JsonResponse(json, status);

    private static HttpResponseMessage JsonWithHeaders(string json, params (string Name, string Value)[] headers)
    {
        var response = Json(json);
        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }

    private static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    private static HttpResponseMessage StatusWithHeaders(
        HttpStatusCode status,
        (string Name, string Value)? header)
    {
        var response = Status(status);
        if (header is { } value)
        {
            response.Headers.TryAddWithoutValidation(value.Name, value.Value);
        }

        return response;
    }

    private static TestCredentialStore AzureCredentials()
    {
        var credentials = new TestCredentialStore();
        credentials.Add("azure-ref", "fixture-token");
        return credentials;
    }

    private static string AzureStatusValues(int start, int count) =>
        string.Join(',', Enumerable.Range(start, count).Select(index =>
            $"{{\"context\":{{\"name\":\"status-{index}\"}},\"state\":\"succeeded\",\"description\":\"ok\",\"commitId\":\"abc123\"}}"));

    private sealed class FakeRemoteProvider(RemoteRepositoryProvider provider) : IRemoteRepositoryEvidenceProvider
    {
        public RemoteRepositoryProvider Provider { get; } = provider;

        public RemoteRepositoryEvidenceRequest? LastRequest { get; private set; }

        public Task<RemoteRepositoryEvidence> InspectAsync(
            RemoteRepositoryEvidenceRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new RemoteRepositoryEvidence(
                request.ProjectId,
                RemoteEvidenceState.Available,
                RemoteEvidenceSource.GitHubRest,
                new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero)));
        }
    }
}
