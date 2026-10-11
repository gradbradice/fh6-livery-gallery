using LiveryGallery.Models;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LiveryGallery.Services.Diagnostics;

/// <summary>
/// The settings as JSON, without paths and IDs. Works by rule, not by a list of known properties, so a setting
/// added later is covered too: a property whose name says Path or ends in Id, and any text value that looks like
/// a path, becomes "&lt;removed&gt;". A null value stays null, so whether it was set is still visible.
/// </summary>
internal static class DiagnosticsSettingsSanitizer
{
    public const string Removed = "<removed>";

    public static JsonObject Sanitize(AppSettingsData settings, JsonSerializerOptions options)
    {
        var json = JsonSerializer.SerializeToNode(settings, options)?.AsObject() ?? new JsonObject();
        foreach (string name in json.Select(p => p.Key).ToList())
        {
            if (json[name] is not JsonValue value) continue; // null stays null
            if (IsSensitiveName(name) || (value.TryGetValue(out string? text) && LooksLikePath(text)))
                json[name] = Removed;
        }
        return json;
    }

    internal static bool IsSensitiveName(string name) =>
        name.Contains("Path", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("Id", StringComparison.Ordinal)
        || name.EndsWith("Ids", StringComparison.Ordinal);

    internal static bool LooksLikePath(string? text) =>
        text is not null && (text.Contains('\\') || text.Contains('/') || (text.Length >= 2 && text[1] == ':'));
}
