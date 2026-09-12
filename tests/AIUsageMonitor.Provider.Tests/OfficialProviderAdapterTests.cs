using System.Net;
using System.Net.Sockets;
using System.Text;
using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Application.Security;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Domain.Common;
using AIUsageMonitor.Domain.Providers;
using AIUsageMonitor.Domain.Quotas;
using AIUsageMonitor.Providers.Antigravity;
using AIUsageMonitor.Providers.Claude;
using AIUsageMonitor.Providers.Codex;
using AIUsageMonitor.Providers;
using AIUsageMonitor.Providers.Common;
using AIUsageMonitor.Providers.Copilot;
using AIUsageMonitor.Providers.Kimi;
using Microsoft.Extensions.DependencyInjection;

namespace AIUsageMonitor.Provider.Tests;

public sealed class OfficialProviderAdapterTests
{
    private const string TestSecret = "apo-test-secret-do-not-log-123";

    [Fact]
    public async Task Copilot_MapsOfficialUsageWithoutFabricatingAllowance()
    {
        var handler = DelegateHttpMessageHandler.Json(
            """
            {
              "usageItems": [
                {
                  "product": "Copilot",
                  "sku": "Copilot AI Credits",
                  "model": "GPT-5",
                  "unitType": "ai-credits",
                  "grossQuantity": 123,
                  "netQuantity": 123
                }
              ]
            }
            """);
        var credentials = new TestCredentialStore();
        credentials.Add("github-copilot", TestSecret);
        var provider = new CopilotProvider(
            new TestClock(),
            new TestHttpClientFactory(handler),
            credentials,
            new CopilotOptions
            {
                CredentialReference = "github-copilot",
                Username = "octocat"
            });

        var result = await provider.RefreshAsync();

        Assert.Equal(ProviderRefreshOutcome.Partial, result.Outcome);
        var quota = Assert.Single(result.QuotaWindows);
        Assert.Equal(123, quota.UsedValue);
        Assert.Null(quota.LimitValue);
        Assert.Null(quota.RemainingValue);
        Assert.Equal(QuotaUnit.Credits, quota.Unit);
        Assert.Equal(DataSource.OfficialApi, quota.Source);
        Assert.DoesNotContain(TestSecret, result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Copilot_SameProviderInstanceUsesUpdatedLiveSettingsOnTheNextRefresh()
    {
        var requestedPaths = new List<string>();
        var handler = new DelegateHttpMessageHandler((request, _) =>
        {
            requestedPaths.Add(request.RequestUri!.AbsolutePath.TrimStart('/'));
            return Task.FromResult(DelegateHttpMessageHandler.JsonResponse(
                "{\"usageItems\":[{\"sku\":\"credits\",\"unitType\":\"credits\",\"grossQuantity\":10}]}"));
        });
        var credentials = new TestCredentialStore();
        credentials.Add("credential-a", TestSecret);
        credentials.Add("credential-b", "apo-test-secret-b");
        var settings = new ProviderRuntimeSettingsAccessor();
        var provider = new CopilotProvider(
            new TestClock(),
            new TestHttpClientFactory(handler),
            credentials,
            settings);
        var sameProviderInstance = provider;

        settings.Apply(
            ProviderCode.Copilot,
            "credential-a",
            new Dictionary<string, string?>
            {
                [ProviderConnectionConfigurationKeys.CopilotScope] = CopilotBillingScope.Organization.ToString(),
                [ProviderConnectionConfigurationKeys.CopilotOrganization] = "org-a"
            });
        await provider.RefreshAsync();

        settings.Apply(
            ProviderCode.Copilot,
            "credential-b",
            new Dictionary<string, string?>
            {
                [ProviderConnectionConfigurationKeys.CopilotScope] = CopilotBillingScope.Organization.ToString(),
                [ProviderConnectionConfigurationKeys.CopilotOrganization] = "org-b"
            });
        await provider.RefreshAsync();

        Assert.Same(sameProviderInstance, provider);
        Assert.Equal(
            "organizations/org-a/settings/billing/ai_credit/usage",
            requestedPaths[0]);
        Assert.Equal(
            "organizations/org-b/settings/billing/ai_credit/usage",
            requestedPaths[1]);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task Copilot_401_IsAuthenticationRequiredWithoutSecretInResult()
    {
        var credentials = new TestCredentialStore();
        credentials.Add("github-copilot", TestSecret);
        var provider = new CopilotProvider(
            new TestClock(),
            new TestHttpClientFactory(DelegateHttpMessageHandler.Json("{}", HttpStatusCode.Unauthorized)),
            credentials,
            new CopilotOptions { CredentialReference = "github-copilot", Username = "octocat" });

        var result = await provider.RefreshAsync();

        Assert.Equal(ProviderRefreshOutcome.AuthenticationRequired, result.Outcome);
        Assert.DoesNotContain(TestSecret, result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(TestSecret, result.ErrorCode ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Copilot_403_IsPermissionAwarePartial()
    {
        var credentials = new TestCredentialStore();
        credentials.Add("github-copilot", TestSecret);
        var provider = new CopilotProvider(
            new TestClock(),
            new TestHttpClientFactory(DelegateHttpMessageHandler.Json("{}", HttpStatusCode.Forbidden)),
            credentials,
            new CopilotOptions { CredentialReference = "github-copilot", Username = "octocat" });

        var result = await provider.RefreshAsync();

        Assert.Equal(ProviderRefreshOutcome.Partial, result.Outcome);
        Assert.Contains("permission", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TestSecret, result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Copilot_RateLimitAfterSuccess_RetainsLastKnownUsageAsStale()
    {
        var responseNumber = 0;
        var handler = new DelegateHttpMessageHandler((_, _) =>
        {
            responseNumber++;
            return Task.FromResult(responseNumber == 1
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"usageItems\":[{\"sku\":\"credits\",\"unitType\":\"credits\",\"grossQuantity\":10}]}",
                        System.Text.Encoding.UTF8,
                        "application/json")
                }
                : new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        });
        var credentials = new TestCredentialStore();
        credentials.Add("github-copilot", TestSecret);
        var provider = new CopilotProvider(
            new TestClock(),
            new TestHttpClientFactory(handler),
            credentials,
            new CopilotOptions { CredentialReference = "github-copilot", Username = "octocat" });

        var first = await provider.RefreshAsync();
        var second = await provider.RefreshAsync();

        Assert.Equal(ProviderRefreshOutcome.Partial, first.Outcome);
        Assert.Equal(ProviderRefreshOutcome.Stale, second.Outcome);
        Assert.Equal("rate_limited", second.ErrorCode);
        Assert.Equal(10, Assert.Single(second.QuotaWindows).UsedValue);
        Assert.DoesNotContain(TestSecret, second.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Copilot_MalformedResponse_IsTypedAndDoesNotLeakCredential()
    {
        var credentials = new TestCredentialStore();
        credentials.Add("github-copilot", TestSecret);
        var provider = new CopilotProvider(
            new TestClock(),
            new TestHttpClientFactory(DelegateHttpMessageHandler.Json("{\"usageItems\":\"not-an-array\"}")),
            credentials,
            new CopilotOptions { CredentialReference = "github-copilot", Username = "octocat" });

        var result = await provider.RefreshAsync();

        Assert.Equal(ProviderRefreshOutcome.ProviderError, result.Outcome);
        Assert.Equal("malformed_response", result.ErrorCode);
        Assert.DoesNotContain(TestSecret, result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Copilot_MixedValidAndMalformedUsageItemsFailsWithoutUndercounting()
    {
        var credentials = new TestCredentialStore();
        credentials.Add("github-copilot", TestSecret);
        var provider = new CopilotProvider(
            new TestClock(),
            new TestHttpClientFactory(DelegateHttpMessageHandler.Json(
                """
                {
                  "usageItems": [
                    { "sku": "valid", "unitType": "credits", "grossQuantity": 10, "netQuantity": 10 },
                    { "sku": "malformed", "unitType": "credits", "grossQuantity": "not-a-number", "netQuantity": 5 }
                  ]
                }
                """)),
            credentials,
            new CopilotOptions { CredentialReference = "github-copilot", Username = "octocat" });

        var result = await provider.RefreshAsync();

        Assert.Equal(ProviderRefreshOutcome.ProviderError, result.Outcome);
        Assert.Equal("malformed_response", result.ErrorCode);
        Assert.Empty(result.QuotaWindows);
        Assert.DoesNotContain(TestSecret, result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Copilot_MalformedMixedRefreshRetainsPreviousCompleteUsageAsStale()
    {
        var responseNumber = 0;
        var handler = new DelegateHttpMessageHandler((_, _) =>
        {
            responseNumber++;
            var json = responseNumber == 1
                ? "{\"usageItems\":[{\"sku\":\"valid\",\"unitType\":\"credits\",\"grossQuantity\":10}]}"
                : "{\"usageItems\":[{\"sku\":\"valid\",\"unitType\":\"credits\",\"grossQuantity\":20},{\"sku\":\"malformed\",\"unitType\":\"credits\",\"grossQuantity\":{}}]}";
            return Task.FromResult(DelegateHttpMessageHandler.JsonResponse(json));
        });
        var credentials = new TestCredentialStore();
        credentials.Add("github-copilot", TestSecret);
        var provider = new CopilotProvider(
            new TestClock(),
            new TestHttpClientFactory(handler),
            credentials,
            new CopilotOptions { CredentialReference = "github-copilot", Username = "octocat" });

        var first = await provider.RefreshAsync();
        var second = await provider.RefreshAsync();

        Assert.Equal(ProviderRefreshOutcome.Partial, first.Outcome);
        Assert.Equal(10, Assert.Single(first.QuotaWindows).UsedValue);
        Assert.Equal(ProviderRefreshOutcome.Stale, second.Outcome);
        Assert.Equal("malformed_response", second.ErrorCode);
        Assert.Equal(10, Assert.Single(second.QuotaWindows).UsedValue);
        Assert.DoesNotContain(TestSecret, second.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, null, ProviderRefreshOutcome.AuthenticationRequired)]
    [InlineData(HttpStatusCode.Forbidden, null, ProviderRefreshOutcome.Partial)]
    [InlineData(HttpStatusCode.NotFound, null, ProviderRefreshOutcome.Unsupported)]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limited", ProviderRefreshOutcome.ProviderError)]
    [InlineData(HttpStatusCode.InternalServerError, "provider_server_error", ProviderRefreshOutcome.ProviderError)]
    public async Task Copilot_SubjectLookupPreservesTypedHttpFailure(
        HttpStatusCode statusCode,
        string? expectedErrorCode,
        ProviderRefreshOutcome expectedOutcome)
    {
        var credentials = new TestCredentialStore();
        credentials.Add("github-copilot", TestSecret);
        var handler = DelegateHttpMessageHandler.Json("{}", statusCode);
        var provider = new CopilotProvider(
            new TestClock(),
            new TestHttpClientFactory(handler),
            credentials,
            new CopilotOptions { CredentialReference = "github-copilot" });

        var result = await provider.RefreshAsync();

        Assert.Equal(expectedOutcome, result.Outcome);
        Assert.Equal(expectedErrorCode, result.ErrorCode);
        Assert.Equal(1, handler.RequestCount);
        Assert.DoesNotContain(TestSecret, result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Copilot_SuccessfulSubjectResponseWithoutLoginRemainsTruthfulPartial()
    {
        var credentials = new TestCredentialStore();
        credentials.Add("github-copilot", TestSecret);
        var provider = new CopilotProvider(
            new TestClock(),
            new TestHttpClientFactory(DelegateHttpMessageHandler.Json("{\"id\":123}")),
            credentials,
            new CopilotOptions { CredentialReference = "github-copilot" });

        var result = await provider.RefreshAsync();

        Assert.Equal(ProviderRefreshOutcome.Partial, result.Outcome);
        Assert.Contains("identity was unavailable", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TestSecret, result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Copilot_SubjectRateLimitAfterSuccessRetainsLastKnownUsageAsStale()
    {
        var requestNumber = 0;
        var handler = new DelegateHttpMessageHandler((request, _) =>
        {
            requestNumber++;
            if (requestNumber == 1)
            {
                return Task.FromResult(DelegateHttpMessageHandler.JsonResponse("{\"login\":\"octocat\"}"));
            }

            if (requestNumber == 2)
            {
                return Task.FromResult(DelegateHttpMessageHandler.JsonResponse(
                    "{\"usageItems\":[{\"sku\":\"credits\",\"unitType\":\"credits\",\"grossQuantity\":10}]}"));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        });
        var credentials = new TestCredentialStore();
        credentials.Add("github-copilot", TestSecret);
        var provider = new CopilotProvider(
            new TestClock(),
            new TestHttpClientFactory(handler),
            credentials,
            new CopilotOptions { CredentialReference = "github-copilot" });

        var first = await provider.RefreshAsync();
        var second = await provider.RefreshAsync();

        Assert.Equal(ProviderRefreshOutcome.Partial, first.Outcome);
        Assert.Equal(ProviderRefreshOutcome.Stale, second.Outcome);
        Assert.Equal("rate_limited", second.ErrorCode);
        Assert.Equal(10, Assert.Single(second.QuotaWindows).UsedValue);
        Assert.DoesNotContain(TestSecret, second.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Copilot_CancellationIsPropagated()
    {
        var handler = new DelegateHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var credentials = new TestCredentialStore();
        credentials.Add("github-copilot", TestSecret);
        var provider = new CopilotProvider(
            new TestClock(),
            new TestHttpClientFactory(handler),
            credentials,
            new CopilotOptions { CredentialReference = "github-copilot", Username = "octocat" });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.RefreshAsync(cancellation.Token));
    }

    [Fact]
    public async Task Claude_AdminApiUsageIsSeparateFromConsumerSubscription()
    {
        var handler = new DelegateHttpMessageHandler((request, _) =>
        {
            Assert.Equal("/v1/organizations/usage_report/messages", request.RequestUri!.AbsolutePath);
            return Task.FromResult(DelegateHttpMessageHandler.JsonResponse(
                """
                {
                  "data": [
                    {
                      "starting_at": "2026-08-21T00:00:00Z",
                      "ending_at": "2026-08-22T00:00:00Z",
                      "results": [
                        {
                          "uncached_input_tokens": 100,
                          "cache_read_input_tokens": 10,
                          "output_tokens": 20,
                          "cache_creation": {
                            "ephemeral_1h_input_tokens": 5,
                            "ephemeral_5m_input_tokens": 2
                          }
                        }
                      ]
                    }
                  ]
                }
                """));
        });
        var credentials = new TestCredentialStore();
        credentials.Add("anthropic-admin", TestSecret);
        var provider = new ClaudeProvider(
            new TestClock(),
            new TestHttpClientFactory(handler),
            credentials,
            new TestExecutableLocator(),
            new AnthropicOptions { CredentialReference = "anthropic-admin" });

        var result = await provider.RefreshAsync();

        Assert.Equal(ProviderRefreshOutcome.Partial, result.Outcome);
        Assert.Null(result.Subscription);
        var quota = Assert.Single(result.QuotaWindows);
        Assert.Equal(137, quota.UsedValue);
        Assert.Equal(QuotaUnit.Tokens, quota.Unit);
        Assert.Null(quota.LimitValue);
        Assert.StartsWith("anthropic-api:", quota.ExternalKey, StringComparison.Ordinal);
        Assert.DoesNotContain(TestSecret, result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Claude_AdminApiUsageAggregatesAllPagesAndPreservesQueryAcrossPages()
    {
        var requestCount = 0;
        var requestUris = new List<Uri>();
        var clock = new TestClock();
        var initialClockTime = clock.UtcNow;
        var handler = new DelegateHttpMessageHandler((request, _) =>
        {
            requestCount++;
            requestUris.Add(request.RequestUri!);
            if (requestCount == 1)
            {
                clock.UtcNow = initialClockTime.AddHours(1);
                return Task.FromResult(DelegateHttpMessageHandler.JsonResponse(
                    """
                    {
                      "data": [{ "results": [{ "output_tokens": 100 }] }],
                      "has_more": true,
                      "next_page": "cursor/a?b c"
                    }
                """));
            }

            return Task.FromResult(DelegateHttpMessageHandler.JsonResponse(
                """
                {
                  "data": [{ "results": [{ "output_tokens": 200 }] }],
                  "has_more": false
                }
                """));
        });
        var credentials = new TestCredentialStore();
        credentials.Add("anthropic-admin", TestSecret);
        var provider = new ClaudeProvider(
            clock,
            new TestHttpClientFactory(handler),
            credentials,
            new TestExecutableLocator(),
            new AnthropicOptions { CredentialReference = "anthropic-admin" });

        var result = await provider.RefreshAsync();

        Assert.Equal(ProviderRefreshOutcome.Partial, result.Outcome);
        Assert.Equal(2, requestCount);
        var firstQuery = requestUris[0];
        var secondQuery = requestUris[1];
        var firstStartingAt = GetQueryParameter(firstQuery, "starting_at");
        var secondStartingAt = GetQueryParameter(secondQuery, "starting_at");

        Assert.Equal(
            initialClockTime.AddDays(-1).ToUniversalTime().ToString("O"),
            firstStartingAt);
        Assert.NotNull(firstStartingAt);
        Assert.Equal(firstStartingAt, secondStartingAt);
        Assert.Null(GetQueryParameter(firstQuery, "limit"));
        Assert.Null(GetQueryParameter(secondQuery, "limit"));
        Assert.Null(GetQueryParameter(firstQuery, "page"));
        Assert.Equal("cursor/a?b c", GetQueryParameter(secondQuery, "page"));
        Assert.Contains("page=cursor%2Fa%3Fb%20c", secondQuery.Query, StringComparison.Ordinal);
        Assert.Equal(300, Assert.Single(result.QuotaWindows).UsedValue);
        Assert.DoesNotContain(TestSecret, result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Claude_MixedValidAndMalformedTokenFieldsFailsWithoutUndercounting()
    {
        var credentials = new TestCredentialStore();
        credentials.Add("anthropic-admin", TestSecret);
        var provider = new ClaudeProvider(
            new TestClock(),
            new TestHttpClientFactory(DelegateHttpMessageHandler.Json(
                """
                {
                  "data": [{
                    "results": [
                      { "output_tokens": 100 },
                      { "output_tokens": 200, "uncached_input_tokens": "not-a-number" }
                    ]
                  }]
                }
                """)),
            credentials,
            new TestExecutableLocator(),
            new AnthropicOptions { CredentialReference = "anthropic-admin" });

        var result = await provider.RefreshAsync();

        Assert.Equal(ProviderRefreshOutcome.ProviderError, result.Outcome);
        Assert.Equal("malformed_response", result.ErrorCode);
        Assert.Empty(result.QuotaWindows);
        Assert.DoesNotContain(TestSecret, result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Claude_MalformedMixedRefreshRetainsPreviousCompleteUsageAsStale()
    {
        var responseNumber = 0;
        var handler = new DelegateHttpMessageHandler((_, _) =>
        {
            responseNumber++;
            var json = responseNumber == 1
                ? "{\"data\":[{\"results\":[{\"output_tokens\":100}]}]}"
                : "{\"data\":[{\"results\":[{\"output_tokens\":200,\"cache_read_input_tokens\":[]}]}]}";
            return Task.FromResult(DelegateHttpMessageHandler.JsonResponse(json));
        });
        var credentials = new TestCredentialStore();
        credentials.Add("anthropic-admin", TestSecret);
        var provider = new ClaudeProvider(
            new TestClock(),
            new TestHttpClientFactory(handler),
            credentials,
            new TestExecutableLocator(),
            new AnthropicOptions { CredentialReference = "anthropic-admin" });

        var first = await provider.RefreshAsync();
        var second = await provider.RefreshAsync();

        Assert.Equal(ProviderRefreshOutcome.Partial, first.Outcome);
        Assert.Equal(100, Assert.Single(first.QuotaWindows).UsedValue);
        Assert.Equal(ProviderRefreshOutcome.Stale, second.Outcome);
        Assert.Equal("malformed_response", second.ErrorCode);
        Assert.Equal(100, Assert.Single(second.QuotaWindows).UsedValue);
        Assert.DoesNotContain(TestSecret, second.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Claude_AdminApiRedirectDoesNotForwardAdminKeyToRedirectDestination()
    {
        await using var redirectProbe = new LoopbackRedirectProbe();
        var credentials = new TestCredentialStore();
        credentials.Add("anthropic-admin", TestSecret);
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(new TestClock());
        services.AddSingleton<ISecureCredentialStore>(credentials);
        services.AddSingleton(new AnthropicOptions
        {
            CredentialReference = "anthropic-admin",
            AuthenticationMode = ProviderAuthenticationMode.ApiKey
        });
        services.AddProviders();
        services.AddHttpClient(ClaudeProvider.HttpClientName, client => client.BaseAddress = redirectProbe.OriginUri);

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ClaudeProvider>();

        var result = await provider.RefreshAsync();

        Assert.Equal(ProviderRefreshOutcome.ProviderError, result.Outcome);
        Assert.Equal("provider_error", result.ErrorCode);
        Assert.False(redirectProbe.DestinationRequest.IsCompleted);
        Assert.DoesNotContain(TestSecret, result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Claude_HasMoreWithoutCursorIsMalformedAndDoesNotPublishFirstPage()
    {
        var handler = DelegateHttpMessageHandler.Json(
            """
            {
              "data": [{ "results": [{ "output_tokens": 100 }] }],
              "has_more": true,
              "next_page": " "
            }
            """);
        var credentials = new TestCredentialStore();
        credentials.Add("anthropic-admin", TestSecret);
        var provider = new ClaudeProvider(
            new TestClock(),
            new TestHttpClientFactory(handler),
            credentials,
            new TestExecutableLocator(),
            new AnthropicOptions { CredentialReference = "anthropic-admin" });

        var result = await provider.RefreshAsync();

        Assert.Equal(ProviderRefreshOutcome.ProviderError, result.Outcome);
        Assert.Equal("malformed_response", result.ErrorCode);
        Assert.Empty(result.QuotaWindows);
        Assert.DoesNotContain(TestSecret, result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Claude_RepeatedCursorIsMalformedWithoutRequestingDuplicatePage()
    {
        var requestCount = 0;
        var handler = new DelegateHttpMessageHandler((request, _) =>
        {
            requestCount++;
            return Task.FromResult(DelegateHttpMessageHandler.JsonResponse(
                requestCount == 1
                    ? """
                      {
                        "data": [{ "results": [{ "output_tokens": 100 }] }],
                        "has_more": true,
                        "next_page": "same-cursor"
                      }
                      """
                    : """
                      {
                        "data": [{ "results": [{ "output_tokens": 200 }] }],
                        "has_more": true,
                        "next_page": "same-cursor"
                      }
                      """));
        });
        var credentials = new TestCredentialStore();
        credentials.Add("anthropic-admin", TestSecret);
        var provider = new ClaudeProvider(
            new TestClock(),
            new TestHttpClientFactory(handler),
            credentials,
            new TestExecutableLocator(),
            new AnthropicOptions { CredentialReference = "anthropic-admin" });

        var result = await provider.RefreshAsync();

        Assert.Equal(ProviderRefreshOutcome.ProviderError, result.Outcome);
        Assert.Equal("malformed_response", result.ErrorCode);
        Assert.Equal(2, requestCount);
        Assert.Empty(result.QuotaWindows);
        Assert.DoesNotContain(TestSecret, result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limited")]
    [InlineData(HttpStatusCode.InternalServerError, "provider_server_error")]
    public async Task Claude_LaterPageFailureDoesNotPublishIncompleteUsage(
        HttpStatusCode laterStatus,
        string expectedErrorCode)
    {
        var requestCount = 0;
        var handler = new DelegateHttpMessageHandler((_, _) =>
        {
            requestCount++;
            return Task.FromResult(requestCount == 1
                ? DelegateHttpMessageHandler.JsonResponse(
                    """
                    {
                      "data": [{ "results": [{ "output_tokens": 100 }] }],
                      "has_more": true,
                      "next_page": "page-2"
                    }
                    """
                  )
                : new HttpResponseMessage(laterStatus));
        });
        var credentials = new TestCredentialStore();
        credentials.Add("anthropic-admin", TestSecret);
        var provider = new ClaudeProvider(
            new TestClock(),
            new TestHttpClientFactory(handler),
            credentials,
            new TestExecutableLocator(),
            new AnthropicOptions { CredentialReference = "anthropic-admin" });

        var result = await provider.RefreshAsync();

        Assert.Equal(ProviderRefreshOutcome.ProviderError, result.Outcome);
        Assert.Equal(expectedErrorCode, result.ErrorCode);
        Assert.Empty(result.QuotaWindows);
        Assert.DoesNotContain(TestSecret, result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Claude_CancellationDuringPaginationIsPropagated()
    {
        var secondPageRequested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateHttpMessageHandler(async (request, cancellationToken) =>
        {
            if (!request.RequestUri!.Query.Contains("page=", StringComparison.Ordinal))
            {
                return DelegateHttpMessageHandler.JsonResponse(
                    """
                    {
                      "data": [{ "results": [{ "output_tokens": 100 }] }],
                      "has_more": true,
                      "next_page": "page-2"
                    }
                    """);
            }

            secondPageRequested.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var credentials = new TestCredentialStore();
        credentials.Add("anthropic-admin", TestSecret);
        var provider = new ClaudeProvider(
            new TestClock(),
            new TestHttpClientFactory(handler),
            credentials,
            new TestExecutableLocator(),
            new AnthropicOptions { CredentialReference = "anthropic-admin" });
        using var cancellation = new CancellationTokenSource();
        var refresh = provider.RefreshAsync(cancellation.Token);

        await secondPageRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
    }

    [Fact]
    public async Task Claude_PreviousCompleteSnapshotIsStaleWhenLaterRefreshPageFails()
    {
        var requestCount = 0;
        var handler = new DelegateHttpMessageHandler((_, _) =>
        {
            requestCount++;
            return requestCount switch
            {
                1 or 3 => Task.FromResult(DelegateHttpMessageHandler.JsonResponse(
                    """
                    {
                      "data": [{ "results": [{ "output_tokens": 100 }] }],
                      "has_more": true,
                      "next_page": "page-2"
                    }
                    """)),
                2 => Task.FromResult(DelegateHttpMessageHandler.JsonResponse(
                    """
                    {
                      "data": [{ "results": [{ "output_tokens": 200 }] }],
                      "has_more": false
                    }
                    """)),
                _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))
            };
        });
        var credentials = new TestCredentialStore();
        credentials.Add("anthropic-admin", TestSecret);
        var provider = new ClaudeProvider(
            new TestClock(),
            new TestHttpClientFactory(handler),
            credentials,
            new TestExecutableLocator(),
            new AnthropicOptions { CredentialReference = "anthropic-admin" });

        var first = await provider.RefreshAsync();
        var second = await provider.RefreshAsync();

        Assert.Equal(ProviderRefreshOutcome.Partial, first.Outcome);
        Assert.Equal(300, Assert.Single(first.QuotaWindows).UsedValue);
        Assert.Equal(ProviderRefreshOutcome.Stale, second.Outcome);
        Assert.Equal("provider_server_error", second.ErrorCode);
        Assert.Equal(300, Assert.Single(second.QuotaWindows).UsedValue);
        Assert.DoesNotContain(TestSecret, second.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Claude_OfficialCliDetectionLeavesSubscriptionCapacityUnsupported()
    {
        var provider = new ClaudeProvider(
            new TestClock(),
            new TestHttpClientFactory(DelegateHttpMessageHandler.Json("{}")),
            new TestCredentialStore(),
            new TestExecutableLocator("claude"),
            new AnthropicOptions());

        var detection = await provider.DetectAsync();
        var status = await provider.GetConnectionStatusAsync();
        var result = await provider.RefreshAsync();

        Assert.True(detection.IsDetected);
        Assert.Equal(ProviderConnectionStatus.LocalDetected, status);
        Assert.Equal(ProviderRefreshOutcome.Unsupported, result.Outcome);
    }

    [Fact]
    public async Task Kimi_DocumentedLocalUsageApiMapsLimitsResetAndAccountWithoutUnprovenSubscription()
    {
        var handler = new DelegateHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/oauth/usage", StringComparison.Ordinal))
            {
                return Task.FromResult(DelegateHttpMessageHandler.JsonResponse(
                    """
                    {
                      "code": 0,
                      "data": {
                        "kind": "ok",
                        "summary": {
                          "name": "weekly",
                          "window": { "duration": 1, "unit": "week" },
                          "used": "20",
                          "limit": "100",
                          "reset_at": "2026-08-29T12:00:00Z"
                        },
                        "limits": [],
                        "extra_usage": {
                          "monthly_charge_limit_enabled": true,
                          "monthly_charge_limit_cents": 1000,
                          "monthly_used_cents": 250,
                          "currency": "USD"
                        }
                      }
                    }
                    """));
            }

            return Task.FromResult(DelegateHttpMessageHandler.JsonResponse(
                """
                {
                  "code": 0,
                  "data": {
                    "kind": "ok",
                    "userInfo": {
                      "userId": "kimi-user",
                      "nickname": "Kimi User",
                       "userLevelName": "Allegretto"
                    }
                  }
                }
                """));
        });
        var credentials = new TestCredentialStore();
        credentials.Add("kimi-server", TestSecret);
        var provider = new KimiProvider(
            new TestClock(),
            new TestHttpClientFactory(handler),
            credentials,
            new TestExecutableLocator("kimi"),
            new KimiOptions { CredentialReference = "kimi-server" });

        var result = await provider.RefreshAsync();

        Assert.Equal(ProviderRefreshOutcome.Success, result.Outcome);
        Assert.Null(result.Subscription);
        Assert.Equal("Kimi User", result.Account!.DisplayName);
        var weekly = Assert.Single(result.QuotaWindows, window => window.Type == QuotaType.Weekly);
        Assert.Equal(20, weekly.UsedValue);
        Assert.Null(weekly.RemainingValue);
        Assert.Equal(80, weekly.RemainingPercentage);
        Assert.Equal(100, weekly.LimitValue);
        Assert.Equal(new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero), weekly.ResetAt);
        Assert.DoesNotContain(result.QuotaWindows, window => window.Type == QuotaType.ExtraUsage);
        Assert.DoesNotContain(TestSecret, result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://127.0.0.1:58627/")]
    [InlineData("http://localhost:58627/")]
    [InlineData("http://[::1]:58627/")]
    public async Task Kimi_LoopbackServerAddressesAreAcceptedBeforeCredentialLookup(string serverAddress)
    {
        var credentials = new TestCredentialStore();
        var handler = DelegateHttpMessageHandler.Json("{}");
        var provider = new KimiProvider(
            new TestClock(),
            new TestHttpClientFactory(handler),
            credentials,
            new TestExecutableLocator(),
            new KimiOptions
            {
                CredentialReference = "kimi-server",
                ServerAddress = new Uri(serverAddress)
            });

        var result = await provider.RefreshAsync();

        Assert.Equal(ProviderRefreshOutcome.AuthenticationRequired, result.Outcome);
        Assert.Equal(1, credentials.RetrieveCount);
        Assert.Equal(0, handler.RequestCount);
    }

    [Theory]
    [InlineData("http://192.168.1.10:58627/")]
    [InlineData("http://example.com:58627/")]
    [InlineData("https://example.com/")]
    public async Task Kimi_RemoteServerAddressIsRejectedBeforeCredentialOrHttpUse(string serverAddress)
    {
        var credentials = new TestCredentialStore();
        credentials.Add("kimi-server", TestSecret);
        var handler = DelegateHttpMessageHandler.Json("{}");
        var provider = new KimiProvider(
            new TestClock(),
            new TestHttpClientFactory(handler),
            credentials,
            new TestExecutableLocator(),
            new KimiOptions
            {
                CredentialReference = "kimi-server",
                ServerAddress = new Uri(serverAddress)
            });

        var result = await provider.RefreshAsync();

        Assert.Equal(ProviderRefreshOutcome.ProviderError, result.Outcome);
        Assert.Equal("invalid_configuration", result.ErrorCode);
        Assert.Equal(0, credentials.RetrieveCount);
        Assert.Equal(0, handler.RequestCount);
        Assert.DoesNotContain(TestSecret, result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(serverAddress, result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(ProviderConnectionStatus.Error, await provider.GetConnectionStatusAsync());
    }

    [Fact]
    public async Task Kimi_MissingLocalServerCredentialIsAuthenticationRequired()
    {
        var provider = new KimiProvider(
            new TestClock(),
            new TestHttpClientFactory(DelegateHttpMessageHandler.Json("{}")),
            new TestCredentialStore(),
            new TestExecutableLocator("kimi"),
            new KimiOptions { CredentialReference = "kimi-server" });

        var result = await provider.RefreshAsync();

        Assert.Equal(ProviderRefreshOutcome.AuthenticationRequired, result.Outcome);
        Assert.Equal(ProviderConnectionStatus.AuthenticationRequired, await provider.GetConnectionStatusAsync());
    }

    [Fact]
    public async Task Kimi_MalformedUsageResponseIsTyped()
    {
        var credentials = new TestCredentialStore();
        credentials.Add("kimi-server", TestSecret);
        var provider = new KimiProvider(
            new TestClock(),
            new TestHttpClientFactory(DelegateHttpMessageHandler.Json("not-json")),
            credentials,
            new TestExecutableLocator("kimi"),
            new KimiOptions { CredentialReference = "kimi-server" });

        var result = await provider.RefreshAsync();

        Assert.Equal(ProviderRefreshOutcome.ProviderError, result.Outcome);
        Assert.Equal("malformed_response", result.ErrorCode);
        Assert.DoesNotContain(TestSecret, result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Codex_And_Antigravity_DetectCliButDoNotScrapeInteractiveUsage()
    {
        var locator = new TestExecutableLocator("codex", "agy");
        var clock = new TestClock();
        var codex = new CodexProvider(clock, locator);
        var antigravity = new AntigravityProvider(clock, locator);

        Assert.True((await codex.DetectAsync()).IsDetected);
        Assert.Equal(ProviderConnectionStatus.LocalDetected, await codex.GetConnectionStatusAsync());
        Assert.Equal(ProviderRefreshOutcome.Unsupported, (await codex.RefreshAsync()).Outcome);

        Assert.True((await antigravity.DetectAsync()).IsDetected);
        Assert.Equal(ProviderConnectionStatus.LocalDetected, await antigravity.GetConnectionStatusAsync());
        Assert.Equal(ProviderRefreshOutcome.Unsupported, (await antigravity.RefreshAsync()).Outcome);
    }

    private static string? GetQueryParameter(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var encodedName = separator >= 0 ? pair[..separator] : pair;
            if (!string.Equals(Uri.UnescapeDataString(encodedName), name, StringComparison.Ordinal))
            {
                continue;
            }

            return separator >= 0
                ? Uri.UnescapeDataString(pair[(separator + 1)..])
                : string.Empty;
        }

        return null;
    }
}

internal sealed class LoopbackRedirectProbe : IAsyncDisposable
{
    private readonly TcpListener _originListener = new(IPAddress.Loopback, 0);
    private readonly TcpListener _destinationListener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _originTask;
    private readonly Task _destinationTask;
    private readonly TaskCompletionSource<string?> _destinationRequest =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public LoopbackRedirectProbe()
    {
        _originListener.Start();
        _destinationListener.Start();
        var originPort = ((IPEndPoint)_originListener.LocalEndpoint).Port;
        var destinationPort = ((IPEndPoint)_destinationListener.LocalEndpoint).Port;
        OriginUri = new Uri($"http://127.0.0.1:{originPort}/", UriKind.Absolute);
        DestinationUri = new Uri($"http://127.0.0.1:{destinationPort}/redirected", UriKind.Absolute);
        _originTask = ServeOriginAsync();
        _destinationTask = ServeDestinationAsync();
    }

    public Uri OriginUri { get; }

    private Uri DestinationUri { get; }

    public Task<string?> DestinationRequest => _destinationRequest.Task;

    private async Task ServeOriginAsync()
    {
        try
        {
            using var client = await _originListener.AcceptTcpClientAsync(_shutdown.Token);
            await ReadHeadersAsync(client.GetStream(), _shutdown.Token);
            await WriteResponseAsync(
                client.GetStream(),
                $"HTTP/1.1 302 Found\r\nLocation: {DestinationUri}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
                _shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested)
        {
        }
    }

    private async Task ServeDestinationAsync()
    {
        try
        {
            using var client = await _destinationListener.AcceptTcpClientAsync(_shutdown.Token);
            var headers = await ReadHeadersAsync(client.GetStream(), _shutdown.Token);
            _destinationRequest.TrySetResult(GetHeader(headers, "x-api-key"));
            await WriteResponseAsync(
                client.GetStream(),
                "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}",
                _shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested)
        {
        }
    }

    private static async Task<string> ReadHeadersAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        while (buffer.Length < 16 * 1024)
        {
            var count = await stream.ReadAsync(chunk.AsMemory(), cancellationToken);
            if (count == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, count);
            var text = Encoding.ASCII.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
            if (text.Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                return text;
            }
        }

        return Encoding.ASCII.GetString(buffer.ToArray());
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        string response,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.ASCII.GetBytes(response);
        await stream.WriteAsync(bytes.AsMemory(), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static string? GetHeader(string headers, string name)
    {
        foreach (var line in headers.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':');
            if (separator > 0 && string.Equals(line[..separator], name, StringComparison.OrdinalIgnoreCase))
            {
                return line[(separator + 1)..].Trim();
            }
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _originListener.Stop();
        _destinationListener.Stop();
        await Task.WhenAll(_originTask, _destinationTask);
        _shutdown.Dispose();
    }
}
