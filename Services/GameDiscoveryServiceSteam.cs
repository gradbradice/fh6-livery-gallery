using Gameloop.Vdf;
using Gameloop.Vdf.Linq;
using Microsoft.Win32;
using System.Runtime.Versioning;

namespace LiveryGallery.Services;

internal class GameDiscoveryServiceSteam
{
    private const uint SteamAppId = 2483190;

    [SupportedOSPlatform("windows")]
    public static string? TryFindViaSteam()
    {
        string? steamPath;
        try
        {
            steamPath = FindSteamInstallPath();
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Steam discovery: Failed to determine the Steam installation path", ex);
            return null;
        }
        if (steamPath is null) return null;

        foreach (string library in EnumerateSteamLibraryFolders(steamPath))
        {
            try
            {
                string manifestPath = Path.Combine(library, "steamapps", $"appmanifest_{SteamAppId}.acf");
                if (!File.Exists(manifestPath)) continue;
                VProperty manifest = VdfConvert.Deserialize(File.ReadAllText(manifestPath));
                if (manifest.Value is VObject appState
                    && appState.TryGetValue("installdir", out var installDirToken))
                {
                    string gamePath = Path.Combine(library, "steamapps", "common", installDirToken.ToString());
                    if (Directory.Exists(gamePath)) return gamePath;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"Steam discovery: Failed to parse the manifest in '{library}'", ex);
            }
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

        VProperty root;
        try
        {
            root = VdfConvert.Deserialize(File.ReadAllText(vdfPath));
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Steam discovery: Failed to parse '{vdfPath}'", ex);
            yield break;
        }

        if (root.Value is not VObject folders) yield break;

        foreach (var entry in folders)
        {
            if (entry.Value is VObject folderInfo
                && folderInfo.TryGetValue("path", out var pathToken))
            {
                string path = pathToken.ToString();
                if (!string.Equals(path, steamPath, StringComparison.OrdinalIgnoreCase))
                    yield return path;
            }
        }
    }
}
