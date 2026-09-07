using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AIUsageMonitor.Application.RemoteEvidence;

namespace AIUsageMonitor.Providers.Remote;

public sealed record RemoteDeliveryHttpResult(
    RemoteEvidenceState State,
    string? Body = null,
    string? ErrorMessage = null,
    HttpStatusCode? StatusCode = null,
    bool OutcomeUncertain = false);

/// <summary>One-shot HTTP mutation transport. It intentionally has no retry policy.</summary>
internal static class RemoteDeliveryHttp
{
    private const int MaxResponseBytes = 512 * 1024;

    public static async Task<RemoteDeliveryHttpResult> SendJsonAsync(
        HttpClient client,
        HttpMethod method,
        Uri uri,
        AuthenticationHeaderValue? authorization,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (authorization is not null) request.Headers.Authorization = authorization;
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new(Map(response.StatusCode), Error(response.StatusCode), StatusCode: response.StatusCode);
            if (response.Content.Headers.ContentLength > MaxResponseBytes)
                return new(RemoteEvidenceState.InvalidResponse, ErrorMessage: "Remote mutation response exceeded its bounded size.", StatusCode: response.StatusCode);
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (bytes.Length > MaxResponseBytes)
                return new(RemoteEvidenceState.InvalidResponse, ErrorMessage: "Remote mutation response exceeded its bounded size.", StatusCode: response.StatusCode);
            return new(RemoteEvidenceState.Available, Encoding.UTF8.GetString(bytes), StatusCode: response.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new(RemoteEvidenceState.Unavailable, "The remote mutation outcome is uncertain after a timeout.", OutcomeUncertain: true);
        }
        catch (HttpRequestException)
        {
            return new(RemoteEvidenceState.Unavailable, "The remote mutation outcome is uncertain after a transport failure.", OutcomeUncertain: true);
        }
        catch (IOException)
        {
            return new(RemoteEvidenceState.Unavailable, "The remote mutation outcome is uncertain after a response-read failure.", OutcomeUncertain: true);
        }
    }

    private static RemoteEvidenceState Map(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => RemoteEvidenceState.AuthenticationRequired,
        HttpStatusCode.Forbidden => RemoteEvidenceState.PermissionDenied,
        (HttpStatusCode)429 => RemoteEvidenceState.RateLimited,
        HttpStatusCode.NotFound => RemoteEvidenceState.Unavailable,
        HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed => RemoteEvidenceState.Partial,
        >= HttpStatusCode.InternalServerError => RemoteEvidenceState.Unavailable,
        _ => RemoteEvidenceState.InvalidResponse
    };

    private static string Error(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => "Remote authentication was rejected or is required.",
        HttpStatusCode.Forbidden => "Remote permission was denied.",
        (HttpStatusCode)429 => "The remote provider rate-limited the request.",
        HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed => "The remote provider rejected the exact state because it changed or conflicts.",
        _ => "The remote provider rejected the bounded mutation."
    };
}
