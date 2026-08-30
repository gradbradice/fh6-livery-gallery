using LiveryGallery.Enums;

namespace LiveryGallery.Services;

internal static class LocalFileService
{
    private const string _baseDir = @"C:\XboxGames\GameSave\pgs";

    public static string? FindLocalFilesPath()
    {
        if (!Directory.Exists(_baseDir)) return null;

        try
        {
            var userDirs = Directory.GetDirectories(_baseDir)
                .Where(d => Path.GetFileName(d).StartsWith("u_", StringComparison.Ordinal))
                .OrderByDescending(Directory.GetLastWriteTime)
                .ToList();
            if (userDirs.Count == 0) return null;

            string dir = userDirs.First();
            var numDirs = Directory.GetDirectories(dir)
                .Where(d => Path.GetFileName(d).Length > 0 && Path.GetFileName(d).All(char.IsDigit))
                .OrderByDescending(Directory.GetLastWriteTime)
                .ToList();
            if (numDirs.Count == 0) return null;

            string path = Path.Combine(numDirs.First(), "ContainersRoot");
            return Directory.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    public static List<string> GetListLiveryDirs(string path)
    {
        return GetListDataDirs(path, DataType.Livery);
    }

    private static List<string> GetListDataDirs(string path, DataType dataType)
    {
        var result = new List<string>();
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

        }
        return result;
    }
}
