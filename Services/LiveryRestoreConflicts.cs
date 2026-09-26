using LiveryGallery.Localisation;
using LiveryGallery.Models;

namespace LiveryGallery.Services;

internal static class LiveryRestoreConflicts
{
    private const int MaxListedNames = 10;

    public static string FormatLiveryId(ulong liveryId) => liveryId > 0 ? $"#{liveryId}" : "";

    public sealed record Result(
        HashSet<string> SameFolder,
        IReadOnlyDictionary<string, LiveryData> Duplicates);

    public static Result Find(
        IEnumerable<LiveryData> candidates, IReadOnlyList<LiveryData> currentEntries, string savePath)
    {
        var currentContent = currentEntries
            .Where(e => !string.IsNullOrEmpty(e.CLiveryHash))
            .ToLookup(e => (e.CarId, e.CLiveryHash!));

        var sameFolder = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new Dictionary<string, LiveryData>(StringComparer.OrdinalIgnoreCase);

        foreach (var data in candidates)
        {
            if (FolderExists(savePath, data.FolderName))
            {
                sameFolder.Add(data.FolderName);
                continue;
            }

            if (string.IsNullOrEmpty(data.CLiveryHash)) continue;

            var existing = currentContent[(data.CarId, data.CLiveryHash)]
                .FirstOrDefault(e => !e.FolderName.Equals(data.FolderName, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) duplicates[data.FolderName] = existing;
        }

        return new Result(sameFolder, duplicates);
    }

    public static string BuildDuplicatePrompt(
        IReadOnlyList<(LiveryData Restoring, LiveryData Existing)> duplicates, bool hasOtherLiveries)
    {
        var lines = duplicates
            .Take(MaxListedNames)
            .Select(d => string.Format(Strings.RestoreDuplicateLineFormat, FormatName(d.Restoring), FormatName(d.Existing)))
            .ToList();
        if (duplicates.Count > MaxListedNames)
            lines.Add(string.Format(Strings.RestoreListMoreFormat, duplicates.Count - MaxListedNames));

        string message = string.Format(Strings.RestoreDuplicateMessage, string.Join(Environment.NewLine, lines));
        return hasOtherLiveries
            ? message + Environment.NewLine + Environment.NewLine + Strings.RestoreDuplicateOthersNote
            : message;
    }

    public static string? BuildReport(IReadOnlyCollection<LiveryData> sameFolder, IReadOnlyCollection<LiveryData> failed)
    {
        var parts = new List<string>();
        if (sameFolder.Count > 0)
            parts.Add(string.Format(Strings.RestoreAlreadyExistsMessage, FormatList(sameFolder)));
        if (failed.Count > 0)
            parts.Add(string.Format(Strings.RestoreFailedMessage, FormatList(failed)));
        return parts.Count > 0 ? string.Join(Environment.NewLine + Environment.NewLine, parts) : null;
    }

    private static string FormatName(LiveryData data) =>
        data.LiveryId > 0 ? $"{data.LiveryName} ({FormatLiveryId(data.LiveryId)})" : data.LiveryName;

    private static string FormatList(IReadOnlyCollection<LiveryData> entries)
    {
        var lines = entries
            .Take(MaxListedNames)
            .Select(e => $"• {FormatName(e)}")
            .ToList();
        if (entries.Count > MaxListedNames)
            lines.Add(string.Format(Strings.RestoreListMoreFormat, entries.Count - MaxListedNames));
        return string.Join(Environment.NewLine, lines);
    }

    private static bool FolderExists(string savePath, string folderName)
    {
        try
        {
            return Directory.Exists(Path.Combine(savePath, folderName));
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to check whether '{folderName}' exists in the save folder", ex);
            return false;
        }
    }
}
