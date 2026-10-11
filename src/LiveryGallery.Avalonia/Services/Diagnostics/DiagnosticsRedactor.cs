using LiveryGallery.Configuration;
using LiveryGallery.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LiveryGallery.Services.Diagnostics;

/// <summary>
/// Replaces known personal values (folders that contain the Windows user name or the player ID, the player ID
/// itself) with placeholders in any text that goes into the diagnostics ZIP. Longer values are replaced first,
/// so the save folder becomes &lt;save-folder&gt; rather than &lt;user-profile&gt;\... with the ID still in it.
/// Player IDs of other accounts are found by their shape (the gallery keeps data of every account it has seen):
/// each becomes &lt;other-user-N&gt;, the same N for the same player in every file of one export.
/// Best effort: paths are only replaced when they are given.
/// </summary>
internal sealed partial class DiagnosticsRedactor
{
    // Shorter values (a drive root, a short number) would also hit unrelated text
    private const int MinSecretLength = 4;

    private readonly (string Secret, string Placeholder)[] _rules;
    private readonly ulong? _currentUserId;
    private readonly Dictionary<ulong, string> _otherUsers = [];
    private readonly Lock _otherUsersLock = new();

    /// <param name="rules">Exact values and what replaces them</param>
    /// <param name="currentUserId">The player ID of the open save: found by shape, it becomes &lt;user-id&gt;</param>
    public DiagnosticsRedactor(IEnumerable<(string? Secret, string Placeholder)> rules, ulong? currentUserId = null)
    {
        _currentUserId = currentUserId;
        var expanded = new List<(string Secret, string Placeholder)>();
        foreach (var (secret, placeholder) in rules)
        {
            if (string.IsNullOrWhiteSpace(secret)) continue;
            // Both separators, whatever the OS: the paths are Windows paths, but the tests also run on Linux
            string trimmed = secret.Trim().TrimEnd('\\', '/');
            if (trimmed.Length < MinSecretLength) continue;
            expanded.Add((trimmed, placeholder));
            string forwardSlashes = trimmed.Replace('\\', '/');
            if (forwardSlashes != trimmed) expanded.Add((forwardSlashes, placeholder));
        }

        _rules = [.. expanded
            .DistinctBy(r => r.Secret, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(r => r.Secret.Length)];
    }

    public string Redact(string text)
    {
        foreach (var (secret, placeholder) in _rules)
            text = text.Replace(secret, placeholder, StringComparison.OrdinalIgnoreCase);
        return PlayerIdPattern().Replace(text, ReplacePlayerId);
    }

    // Xbox player IDs (XUID) are 0x0009_xxxx_xxxx_xxxx: 16 decimal digits from 2533274790395904 to 2814749767106559.
    // They appear in decimal (u_2535412345678901_16D460, 2535412345678901Meta), as 16 hex digits (account keys)
    // and as hex without leading zeros after User_ and SaveFolderMetadata_ (User_901F1B0392C35)
    [GeneratedRegex(
        @"(?<dec>(?<![0-9])2[5-8][0-9]{14}(?![0-9]))" +
        @"|(?<=\b(?:User|SaveFolderMetadata)_)(?<hex>[0-9A-Fa-f]{13,16})(?![0-9A-Za-z])" +
        @"|(?<hex>(?<![0-9A-Za-z])[0-9A-Fa-f]{16}(?![0-9A-Za-z]))",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant)]
    private static partial Regex PlayerIdPattern();

    private const ulong PlayerIdTag = 0x0009;

    private string ReplacePlayerId(Match match)
    {
        bool parsed = match.Groups["dec"].Success
            ? ulong.TryParse(match.ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id)
            : ulong.TryParse(match.ValueSpan, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out id);
        if (!parsed || id >> 48 != PlayerIdTag) return match.Value; // a hash or another number of the same length
        if (id == _currentUserId) return "<user-id>";
        lock (_otherUsersLock)
        {
            if (!_otherUsers.TryGetValue(id, out string? placeholder))
            {
                placeholder = $"<other-user-{_otherUsers.Count + 1}>";
                _otherUsers[id] = placeholder;
            }
            return placeholder;
        }
    }

    public static DiagnosticsRedactor ForCurrentUser(DiagnosticsSources sources)
    {
        var settings = sources.Settings;
        ulong? userId = sources.UserId;
        return new DiagnosticsRedactor(
        [
            (sources.SavePath, "<save-folder>"),
            (settings.SavePath, "<save-folder>"),
            (settings.LastKnownContainerSavePath, "<save-folder>"),
            (sources.GameDirectory, "<game-folder>"),
            (settings.GameInstallPath, "<game-folder>"),
            (AppSettings.BaseCachePath, "<gallery-data>"),
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "<local-app-data>"),
            (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "<app-data>"),
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "<user-profile>"),
            (userId?.ToString(CultureInfo.InvariantCulture), "<user-id>"),
            (userId?.ToString("X16", CultureInfo.InvariantCulture), "<user-id>"),
            (userId?.ToString("X", CultureInfo.InvariantCulture), "<user-id>"),
        ], userId);
    }
}

/// <param name="SavePath">The save folder in use, which may be auto-discovered rather than in the settings</param>
/// <param name="GameDirectory">The game folder in use, which may be auto-discovered rather than in the settings</param>
/// <param name="UserId">The player ID of the current save, if known</param>
/// <param name="AccountKey">Whose scan cache the parse issues come from; null when no save folder is open</param>
internal sealed record DiagnosticsSources(
    AppSettingsData Settings, string? SavePath, string? GameDirectory, ulong? UserId, string? AccountKey);
