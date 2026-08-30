using System.Text.Json;

namespace LiveryGallery.Configuration;

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions DefaultDeserializeOptions = new() 
    { 
        WriteIndented = true
    };
    public static readonly JsonSerializerOptions GitHubDeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
