using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.Views;

/// <summary>The list of the gallery's keys (F1), made from <see cref="GalleryShortcuts"/></summary>
internal partial class ShortcutsDialog : Window
{
    public ShortcutsDialog()
    {
        InitializeComponent();

        Title = Strings.ShortcutsTitle;
        TitleBarText.Text = Strings.ShortcutsTitle;
        TitleText.Text = Strings.ShortcutsTitle;
        NoteText.Text = string.Format(Strings.ShortcutsNoteFormat, Strings.KeySpace, Strings.KeyDelete);
        CloseDialogButton.Content = Strings.ButtonClose;

        Grid.SetIsSharedSizeScope(LeftColumn, true);
        Grid.SetIsSharedSizeScope(RightColumn, true);
        AddGroup(LeftColumn, GalleryShortcutGroup.SearchAndSelection);
        AddGroup(LeftColumn, GalleryShortcutGroup.Windows);
        AddGroup(RightColumn, GalleryShortcutGroup.SelectedLiveries);
    }

    private static void AddGroup(StackPanel column, GalleryShortcutGroup group)
    {
        var title = new TextBlock
        {
            Text = GalleryShortcuts.GroupTitle(group),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Margin = new(0, 18, 0, 8),
        };
        title.Bind(TextBlock.ForegroundProperty, title.GetResourceObservable("TextSecondaryBrush"));
        column.Children.Add(title);
        foreach (var shortcut in GalleryShortcuts.All.Where(s => s.Group == group))
            column.Children.Add(Row(shortcut.Action));
    }

    /// <summary>One key: its keys as caps ("Ctrl" "Shift" "A"), and what it does</summary>
    private static Grid Row(GalleryShortcut action)
    {
        var keys = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new(0, 0, 16, 0) };
        foreach (string key in GalleryShortcuts.GestureText(action).Split('+'))
            keys.Children.Add(new Border { Classes = { "keyCap" }, Child = new TextBlock { Text = key } });
        var text = new TextBlock
        {
            Text = GalleryShortcuts.Description(action),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Grid.SetColumn(text, 1);
        return new Grid
        {
            // The keys of a column line up, as wide as its longest ("Strg+Umschalt+A" in German)
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "Keys" }, new ColumnDefinition(GridLength.Star) },
            Margin = new(0, 0, 0, 8),
            Children = { keys, text },
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key is not (Key.Escape or Key.F1)) return;
        e.Handled = true;
        Close();
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
