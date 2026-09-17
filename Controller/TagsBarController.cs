using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using LiveryGallery.Models;

namespace LiveryGallery.Controller;

internal sealed class TagsBarController(WrapPanel tagsBar, Control tagsFilterRow, HashSet<string> selectedTags)
{
    private HashSet<string>? _lastTags;

    public void Rebuild(IEnumerable<LiveryEntry> allEntries, Action onSelectionChanged)
    {
        var allTagsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in allEntries)
            foreach (var tag in entry.Tags)
                allTagsSet.Add(tag);

        if (_lastTags is not null && _lastTags.SetEquals(allTagsSet)) return;
        _lastTags = allTagsSet;

        var allTags = allTagsSet.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();

        selectedTags.RemoveWhere(t => !allTagsSet.Contains(t));

        tagsBar.Children.Clear();
        tagsFilterRow.IsVisible = allTags.Count > 0;

        foreach (var tag in allTags)
        {
            var button = new ToggleButton
            {
                Content = tag,
                IsChecked = selectedTags.Contains(tag),
                Margin = new Thickness(0, 0, 8, 8)
            };
            button.Classes.Add("tagChip");
            button.IsCheckedChanged += (_, _) =>
            {
                if (button.IsChecked == true) selectedTags.Add(tag);
                else selectedTags.Remove(tag);
                onSelectionChanged();
            };
            tagsBar.Children.Add(button);
        }
    }
}
