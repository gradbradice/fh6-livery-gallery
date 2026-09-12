using System.Net;

namespace LiveryGallery.Services;

internal static class ConditionalHttpFetcher
{
    public readonly record struct Result(bool NotModified, string? Json, string? ETag);

    public static async Task<Result> FetchAsync(
        HttpClient http, string url, string? currentETag, TimeSpan timeout, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(currentETag))
            request.Headers.TryAddWithoutValidation("If-None-Match", currentETag);

        using var response = await http.SendAsync(request, timeoutCts.Token);

        if (response.StatusCode == HttpStatusCode.NotModified)
            return new Result(true, null, null);

        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(timeoutCts.Token);
        return new Result(false, json, response.Headers.ETag?.Tag);
    }
}
