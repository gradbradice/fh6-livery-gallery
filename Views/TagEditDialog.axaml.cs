using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LiveryGallery.Localisation;

namespace LiveryGallery.Views;

internal partial class TagEditDialog : Window
{
    public List<string> ResultTags { get; private set; } = new();

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

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        ResultTags = [.. (TagsBox.Text ?? "")
            .Split(',')
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())];

        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(false);

    private void TagsBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SaveButton_Click(sender, e);
        else if (e.Key == Key.Escape) CancelButton_Click(sender, e);
    }
}
