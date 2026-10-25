using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Localisation;

namespace LiveryGallery.Views;

/// <summary>A tag in the dialog for several liveries: on all of them (ticked), some (a square) or none</summary>
internal sealed partial class TagCheckRow : ObservableObject
{
    public required string Tag { get; init; }

    /// <summary>The square only when some of the liveries have the tag: "leave it as it is"</summary>
    public bool IsThreeState { get; init; }

    public string? Tooltip { get; init; }

    [ObservableProperty]
    private bool? _isChecked;
}

/// <summary>
/// The tags of one livery, typed as a list; or of several liveries: every tag of the gallery ticked on all, some or
/// none of them, and new tags for all of them
/// </summary>
internal partial class TagEditDialog : Window
{
    private readonly List<TagCheckRow>? _rows;

    /// <summary>One livery: its tags after editing</summary>
    public List<string> ResultTags { get; private set; } = new();

    /// <summary>Several liveries: the tags to put on every one of them</summary>
    public List<string> TagsToAdd { get; private set; } = new();

    /// <summary>Several liveries: the tags to take off every one of them</summary>
    public List<string> TagsToRemove { get; private set; } = new();

    /// <summary>Several liveries: a tag on some of them stays as it is unless the user ticks or clears it</summary>
    /// <param name="tagsOfEach">The tags of each livery</param>
    /// <param name="knownTags">Every tag of the gallery</param>
    public TagEditDialog(IReadOnlyList<IReadOnlyList<string>> tagsOfEach, IEnumerable<string> knownTags)
        : this([])
    {
        int count = tagsOfEach.Count;
        TitleText.Text = string.Format(Strings.TagsDialogManyTitleFormat, count);
        HintText.Text = Strings.TagsDialogManyHint;
        NewTagsLabel.Text = Strings.TagsDialogNewTagsLabel;
        NewTagsLabel.IsVisible = true;
        TagsBox.PlaceholderText = Strings.TagsDialogHint;

        var having = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var tags in tagsOfEach)
            foreach (string tag in tags.Distinct(StringComparer.OrdinalIgnoreCase))
                having[tag] = having.GetValueOrDefault(tag) + 1;

        _rows = [.. knownTags.Concat(having.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(tag =>
            {
                int n = having.GetValueOrDefault(tag);
                bool some = n > 0 && n < count;
                return new TagCheckRow
                {
                    Tag = tag,
                    IsThreeState = some,
                    IsChecked = some ? null : n == count,
                    Tooltip = some ? string.Format(Strings.TagsDialogPartialTooltipFormat, n, count) : null,
                };
            })];
        ManyTagsList.ItemsSource = _rows;
        ManyTagsScroll.IsVisible = _rows.Count > 0;
    }

    public TagEditDialog(IEnumerable<string> existingTags)
    {
        InitializeComponent();

        Title = Strings.TagsDialogTitle;
        TitleBarText.Text = Strings.TagsDialogTitle;
        TitleText.Text = Strings.TagsDialogTitle;
        HintText.Text = Strings.TagsDialogHint;
        SaveButton.Content = Strings.ButtonSave;
        CancelButton.Content = Strings.ButtonCancel;
        TagsBox.Text = string.Join(", ", existingTags);

        Loaded += (_, _) =>
        {
            TagsBox.Focus();
            TagsBox.CaretIndex = TagsBox.Text?.Length ?? 0;
        };
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        ResultTags = [.. (TagsBox.Text ?? "")
            .Split(',')
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())];

        if (_rows is not null)
        {
            TagsToAdd = [.. _rows.Where(r => r.IsChecked == true).Select(r => r.Tag).Concat(ResultTags)];
            TagsToRemove = [.. _rows.Where(r => r.IsChecked == false).Select(r => r.Tag)];
        }

        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(false);

    private void TagsBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SaveButton_Click(sender, e);
        else if (e.Key == Key.Escape) CancelButton_Click(sender, e);
    }
}
