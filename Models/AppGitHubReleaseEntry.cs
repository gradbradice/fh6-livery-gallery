using System.Text.Json.Serialization;

namespace LiveryGallery.Models;

internal class AppGitHubReleaseEntry
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }
    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }
    [JsonPropertyName("body")]
    public string? Body { get; set; }
}
