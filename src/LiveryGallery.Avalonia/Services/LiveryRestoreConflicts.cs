using ForzaToolkit.Liveries.Scanning;
using LiveryGallery.Localisation;

namespace LiveryGallery.Services;

internal static class LiveryRestoreConflicts
{
    private const int MaxListedNames = 10;

    public static string FormatLiveryId(ulong liveryId) => liveryId > 0 ? $"#{liveryId}" : "";

    public sealed record Result(
        HashSet<string> SameFolder,
        IReadOnlyDictionary<string, LiveryData> Duplicates);

    /// <param name="candidates">The liveries to restore</param>
    /// <param name="currentEntries">The liveries the gallery shows</param>
    /// <param name="savePath">The save folder the store lists</param>
    /// <param name="store">The game's livery store: whether the save has a livery depends on its layout (FH4: files, not a folder)</param>
    public static Result Find(
        IEnumerable<LiveryData> candidates, IReadOnlyList<LiveryData> currentEntries, string savePath, ILiveryStore store)
    {
        var currentContent = currentEntries
            .Where(e => !string.IsNullOrEmpty(e.CLiveryHash))
            .ToLookup(e => (e.CarId, e.CLiveryHash!));

        var sameFolder = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new Dictionary<string, LiveryData>(StringComparer.OrdinalIgnoreCase);

        foreach (var data in candidates)
        {
            if (IsInSave(store, savePath, data.FolderName))
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

    /// <summary>Which of the selected liveries to restore, and why the others are left out</summary>
    /// <param name="Conflicts">Liveries already in the save and duplicates of liveries in it</param>
    /// <param name="ToRestore">What to restore</param>
    /// <param name="Selected">What the user chose</param>
    public sealed record Plan(Result Conflicts, IReadOnlyList<LiveryData> ToRestore, IReadOnlyList<LiveryData> Selected)
    {
        /// <summary>The report after restoring: what was already in the save and what failed; null when nothing to say</summary>
        /// <param name="restored">The folders restored</param>
        public string? Report(IReadOnlySet<string> restored) => BuildReport(
            [.. Selected.Where(d => Conflicts.SameFolder.Contains(d.FolderName))],
            [.. ToRestore.Where(d => !restored.Contains(d.FolderName))]);
    }

    /// <summary>
    /// What to restore of <paramref name="selected"/> (from the archive or a backup): never a livery the save already has;
    /// a duplicate of one in the save only when <paramref name="confirmDuplicates"/> says so
    /// </summary>
    public static async Task<Plan> PlanAsync(
        IReadOnlyList<LiveryData> selected, IReadOnlyList<LiveryData> currentEntries, string savePath, ILiveryStore store,
        Func<string, Task<bool>>? confirmDuplicates)
    {
        var conflicts = Find(selected, currentEntries, savePath, store);
        var duplicates = selected.Where(d => conflicts.Duplicates.ContainsKey(d.FolderName)).ToList();
        bool restoreDuplicates = false;
        if (duplicates.Count > 0 && confirmDuplicates is not null)
        {
            bool hasOtherLiveries = selected.Any(d =>
                !conflicts.SameFolder.Contains(d.FolderName) && !conflicts.Duplicates.ContainsKey(d.FolderName));
            string prompt = BuildDuplicatePrompt(
                [.. duplicates.Select(d => (d, conflicts.Duplicates[d.FolderName]))], hasOtherLiveries);
            restoreDuplicates = await confirmDuplicates(prompt);
        }

        var toRestore = selected
            .Where(d => !conflicts.SameFolder.Contains(d.FolderName)
                && (restoreDuplicates || !conflicts.Duplicates.ContainsKey(d.FolderName)))
            .ToList();
        return new Plan(conflicts, toRestore, selected);
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
        data.LiveryId > 0 ? $"{data.DisplayLiveryName} ({FormatLiveryId(data.LiveryId)})" : data.DisplayLiveryName;

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

    // The key comes from an archive index or a backup manifest: only one the store accepts is looked up
    private static bool IsInSave(ILiveryStore store, string savePath, string folderName)
    {
        try
        {
            return store.IsLiveryKey(folderName) && LiveryFileMover.IsInSave(store, savePath, folderName);
        }
        catch (Exception ex)
        {
            AppLog.LogError($"Failed to check whether '{folderName}' exists in the save folder", ex);
            return false;
        }
    }
}
