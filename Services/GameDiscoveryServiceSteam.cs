using Microsoft.Win32;
using System.Runtime.Versioning;
using System.Text;

namespace LiveryGallery.Services;

internal class GameDiscoveryServiceSteam
{
    private const uint SteamAppId = 2483190;

    [SupportedOSPlatform("windows")]
    public static string? TryFindViaSteam()
    {
        try
        {
            string? steamPath = FindSteamInstallPath();
            if (steamPath is null) return null;

            foreach (string library in EnumerateSteamLibraryFolders(steamPath))
            {
                string manifestPath = Path.Combine(library, "steamapps", $"appmanifest_{SteamAppId}.acf");
                if (!File.Exists(manifestPath)) continue;

                var manifest = ParseVdf(File.ReadAllText(manifestPath));
                if (manifest.TryGetValue("AppState", out var appStateObj)
                    && appStateObj is Dictionary<string, object> appState
                    && appState.TryGetValue("installdir", out var installDirObj)
                    && installDirObj is string installDir)
                {
                    string gamePath = Path.Combine(library, "steamapps", "common", installDir);
                    if (Directory.Exists(gamePath)) return gamePath;
                }
            }
        }
        catch
        {
            
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static string? FindSteamInstallPath()
    {
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string defaultPath = Path.Combine(programFilesX86, "Steam");
        if (IsValidSteamInstallation(defaultPath)) return defaultPath;

        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        string? registryPath = key?.GetValue("SteamPath") as string;
        if (!string.IsNullOrEmpty(registryPath))
        {
            registryPath = registryPath.Replace('/', '\\');
            if (IsValidSteamInstallation(registryPath)) return registryPath;
        }

        return null;
    }

    private static bool IsValidSteamInstallation(string steamPath)
    {
        return Directory.Exists(steamPath)
            && File.Exists(Path.Combine(steamPath, "config", "libraryfolders.vdf"));
    }

    private static IEnumerable<string> EnumerateSteamLibraryFolders(string steamPath)
    {
        yield return steamPath;

        string vdfPath = Path.Combine(steamPath, "config", "libraryfolders.vdf");
        if (!File.Exists(vdfPath)) yield break;

        Dictionary<string, object> root;
        try
        {
            root = ParseVdf(File.ReadAllText(vdfPath));
        }
        catch
        {
            yield break;
        }

        if (!root.TryGetValue("libraryfolders", out var foldersObj) || foldersObj is not Dictionary<string, object> folders)
            yield break;

        foreach (var entry in folders.Values)
        {
            if (entry is Dictionary<string, object> folderInfo
                && folderInfo.TryGetValue("path", out var pathObj)
                && pathObj is string path
                && !string.Equals(path, steamPath, StringComparison.OrdinalIgnoreCase))
            {
                yield return path;
            }
        }
    }

    private static Dictionary<string, object> ParseVdf(string content)
    {
        int pos = 0;
        return ParseVdfObject(content, ref pos);
    }

    private static Dictionary<string, object> ParseVdfObject(string s, ref int pos)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            SkipWhitespaceAndComments(s, ref pos);
            if (pos >= s.Length) break;
            if (s[pos] == '}') { pos++; break; }

            string key = ReadQuotedString(s, ref pos);
            SkipWhitespaceAndComments(s, ref pos);

            if (pos < s.Length && s[pos] == '{')
            {
                pos++;
                result[key] = ParseVdfObject(s, ref pos);
            }
            else
            {
                result[key] = ReadQuotedString(s, ref pos);
            }
        }
        return result;
    }

    private static void SkipWhitespaceAndComments(string s, ref int pos)
    {
        while (pos < s.Length)
        {
            if (char.IsWhiteSpace(s[pos])) { pos++; continue; }
            if (pos + 1 < s.Length && s[pos] == '/' && s[pos + 1] == '/')
            {
                while (pos < s.Length && s[pos] != '\n') pos++;
                continue;
            }
            break;
        }
    }

    private static string ReadQuotedString(string s, ref int pos)
    {
        SkipWhitespaceAndComments(s, ref pos);
        if (pos >= s.Length || s[pos] != '"')
            throw new InvalidDataException($"An opening quote was expected at position {pos} in a VDF file");
        pos++;

        var sb = new StringBuilder();
        while (pos < s.Length && s[pos] != '"')
        {
            if (s[pos] == '\\' && pos + 1 < s.Length)
            {
                pos++;
                sb.Append(s[pos] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    '"' => '"',
                    '\\' => '\\',
                    var other => other,
                });
            }
            else
            {
                sb.Append(s[pos]);
            }
            pos++;
        }
        // closing quotation mark
        pos++;
        return sb.ToString();
    }
}
