namespace LiveryGallery.Services;

internal static class AppHttpClient
{
    public static HttpClient Instance { get; } = Create();

    private static HttpClient Create()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(Configuration.AppSettings.UserAgent);
        return client;
    }
}
