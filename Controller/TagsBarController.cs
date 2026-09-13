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
        var allTags = allEntries
            .SelectMany(x => x.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var allTagsSet = new HashSet<string>(allTags, StringComparer.OrdinalIgnoreCase);
        if (_lastTags is not null && _lastTags.SetEquals(allTagsSet)) return;
        _lastTags = allTagsSet;

        selectedTags.RemoveWhere(t => !allTags.Contains(t, StringComparer.OrdinalIgnoreCase));

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
