using LiveryGallery.Enums;
using LiveryGallery.Models;
using LiveryGallery.Configuration;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LiveryGallery.Services;

internal static partial class LocalSaveService
{
    private const string _baseDir = @"C:\XboxGames\GameSave\pgs";

    [GeneratedRegex(@"^u_(?<userId>\d+)_(?<gameId>[0-9a-fA-F]+)$")]
    private static partial Regex UserFolderRegex();

    public static (ulong UserId, string GameId)? TryParseFolderName(string folderPathOrName)
    {
        string name = Path.GetFileName(folderPathOrName.TrimEnd('\\', '/'));
        var match = UserFolderRegex().Match(name);
        if (!match.Success || !ulong.TryParse(match.Groups["userId"].Value, out ulong id)) return null;
        return (id, match.Groups["gameId"].Value.ToUpperInvariant());
    }

    public static ulong? TryParseUserId(string folderPathOrName) => TryParseFolderName(folderPathOrName)?.UserId;

    public static (ulong UserId, string GameId)? TryReadManifestIdentity(string uFolderPath, int containerId)
    {
        string manifestPath = Path.Combine(uFolderPath, $"{containerId}.json");
        try
        {
            if (!File.Exists(manifestPath)) return null;
            string json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<SaveManifestFile>(json, JsonSettings.GitHubDeserializeOptions);
            string? userIdStr = manifest?.Manifest?.UserId;
            string? gameId = manifest?.Manifest?.GameId;
            if (string.IsNullOrEmpty(userIdStr) || string.IsNullOrEmpty(gameId)) return null;
            if (!ulong.TryParse(userIdStr, out ulong userId)) return null;
            return (userId, gameId.ToUpperInvariant());
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to read/parse save manifest '{manifestPath}'", ex);
            return null;
        }
    }

    public static bool HasIdentityConfirmationFile(string uFolderPath, ulong userId, string gameId) =>
        File.Exists(Path.Combine(uFolderPath, $"{userId}_{gameId.ToUpperInvariant()}.json"));

    public static SaveIdentity? ResolveSaveIdentity(string uFolderPath, int? containerId)
    {
        var fromFolder = TryParseFolderName(uFolderPath);
        var fromManifest = containerId is { } id ? TryReadManifestIdentity(uFolderPath, id) : null;

        (ulong UserId, string GameId)? resolved = fromManifest ?? fromFolder;
        if (resolved is null) return null;

        bool agree = fromFolder is not null && fromManifest is not null
            && fromFolder.Value.UserId == fromManifest.Value.UserId
            && string.Equals(fromFolder.Value.GameId, fromManifest.Value.GameId, StringComparison.Ordinal);

        return new SaveIdentity
        {
            UserId = resolved.Value.UserId,
            GameId = resolved.Value.GameId,
            Source = fromManifest is not null ? SaveIdentitySource.ManifestJson : SaveIdentitySource.FolderNameFallback,
            ConfirmationFileFound = HasIdentityConfirmationFile(uFolderPath, resolved.Value.UserId, resolved.Value.GameId),
            FolderNameAgreesWithManifest = agree,
        };
    }

    public static string? FindLocalSavePath()
    {
        if (!Directory.Exists(_baseDir)) return null;

        try
        {
            var userDirs = Directory.GetDirectories(_baseDir)
                .Where(d => Path.GetFileName(d).StartsWith("u_", StringComparison.Ordinal))
                .OrderByDescending(Directory.GetLastWriteTime);

            foreach (var dir in userDirs)
            {
                if (IsSavePathValid(dir)) return dir;
            }
            return null;
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to find the save path in '{_baseDir}'", ex);
            return null;
        }
    }

    public static bool IsSavePathValid(string path)
    {
        return GetSaveDataPath(path) != null;
    }

    public static List<string> GetListLiveryDirs(string path)
    {
        return GetListDataDirs(path, DataType.Livery);
    }

    public static string? GetSaveDataPath(string savePath) => GetSaveDataPathWithId(savePath, null).Path;

    public static (string? Path, int? ContainerId) GetSaveDataPathWithId(string savePath, int? lastKnownContainerId)
    {
        try
        {
            if (!Directory.Exists(savePath)) return (null, null);

            if (lastKnownContainerId is { } lastId)
            {
                string candidateDir = Path.Combine(savePath, (lastId + 1).ToString());
                string candidate = Path.Combine(candidateDir, "ContainersRoot");
                if (Directory.Exists(candidate)) return (candidate, lastId + 1);
            }

            var candidates = Directory.GetDirectories(savePath)
                .Where(d => Path.GetFileName(d).Length > 0 && Path.GetFileName(d).All(char.IsDigit))
                .Select(d => (Dir: d, Root: Path.Combine(d, "ContainersRoot")))
                .Where(x => Directory.Exists(x.Root))
                .OrderByDescending(x => Directory.GetLastWriteTime(x.Root));

            var best = candidates.FirstOrDefault();
            if (best.Root is null) return (null, null);

            int? foundId = int.TryParse(Path.GetFileName(best.Dir), out int id) ? id : null;
            return (best.Root, foundId);
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to determine the save data path in '{savePath}'", ex);
            return (null, null);
        }
    }

    private static List<string> GetListDataDirs(string path, DataType dataType)
    {
        var result = new List<string>();
        if (!Directory.Exists(path)) return result;

        string dataDirName;
        string dataFileName;
        if (dataType == DataType.Livery)
        {
            dataDirName = "Livery_";
            dataFileName = "C_livery";
        }
        else
        {
            return result;
        }

        foreach (var dir in Directory.GetDirectories(path))
        {
            string name = Path.GetFileName(dir);
            if (!name.StartsWith(dataDirName, StringComparison.Ordinal)) continue;
            if (!File.Exists(Path.Combine(dir, "header"))) continue;
            if (!File.Exists(Path.Combine(dir, dataFileName))) continue;
            result.Add(name);
        }
        return result;
    }
}
