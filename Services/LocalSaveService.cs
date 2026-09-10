using LiveryGallery.Enums;

namespace LiveryGallery.Services;

internal static class LocalSaveService
{
    private const string _baseDir = @"C:\XboxGames\GameSave\pgs";

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

    public static string? GetSaveDataPath(string savePath)
    {
        try
        {
            if (!Directory.Exists(savePath)) return null;

            var numDirs = Directory.GetDirectories(savePath)
                    .Where(d => Path.GetFileName(d).Length > 0 && Path.GetFileName(d).All(char.IsDigit))
                    .OrderByDescending(Directory.GetLastWriteTime);

            foreach (var dir in numDirs)
            {
                string candidate = Path.Combine(dir, "ContainersRoot");
                if (Directory.Exists(candidate)) return candidate;
            }
            return null;
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to determine the save data path in '{savePath}'", ex);
            return null;
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

        try
        {
            foreach(var dir in Directory.GetDirectories(path))
            {
                string name = Path.GetFileName(dir);
                if (!name.StartsWith(dataDirName, StringComparison.Ordinal)) continue;
                if (!File.Exists(Path.Combine(dir, "header"))) continue;
                if (!File.Exists(Path.Combine(dir, dataFileName))) continue;
                result.Add(name);
            }
        }
        catch
        {
            // do not log. this method is called on every automatic scan
            // once per 5 seconds
        }
        return result;
    }
}
