namespace LiveryGallery.Services;

internal static class AppHttpClient
{
    public static HttpClient Instance { get; } = Create();

    private static HttpClient Create()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FH6-Livery-Gallery/1.0");
        return client;
    }
}
