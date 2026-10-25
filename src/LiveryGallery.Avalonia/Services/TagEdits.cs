namespace LiveryGallery.Services;

/// <summary>Tags changed on several liveries at once</summary>
internal static class TagEdits
{
    /// <summary>
    /// <paramref name="current"/> without <paramref name="remove"/> and with <paramref name="add"/> at the end, ignoring
    /// case: a tag the livery has keeps its spelling, a tag both added and removed is added
    /// </summary>
    public static List<string> Change(IReadOnlyList<string> current, IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(remove);
        var removed = new HashSet<string>(remove, StringComparer.OrdinalIgnoreCase);
        removed.ExceptWith(add);
        var result = new List<string>(current.Count + add.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string tag in current)
            if (!removed.Contains(tag) && seen.Add(tag)) result.Add(tag);
        foreach (string tag in add)
        {
            string trimmed = tag.Trim();
            if (trimmed.Length > 0 && seen.Add(trimmed)) result.Add(trimmed);
        }
        return result;
    }
}
