using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using LiveryGallery.Localisation;

namespace LiveryGallery.Views;

internal readonly record struct InfoTextLine(string Text, string? ColorResourceKey = null);

internal partial class InfoDialog : Window
{
    public InfoDialog(string title, string message, string? okText = null)
        : this(title, [new InfoTextLine(message)], okText)
    {
    }

    public InfoDialog(string title, IReadOnlyList<InfoTextLine> lines, string? okText = null)
    {
        InitializeComponent();
        Title = title;
        TitleBarText.Text = title;
        BuildInlines(lines);
        OkButton.Content = okText ?? Strings.ButtonOk;
    }

    private void BuildInlines(IReadOnlyList<InfoTextLine> lines)
    {
        var inlines = new InlineCollection();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0) inlines.Add(new LineBreak());

            var run = new Run(lines[i].Text);
            if (lines[i].ColorResourceKey is { } key)
                run.Foreground = GetBrush(key);
            inlines.Add(run);
        }
        MessageText.Inlines = inlines;
    }

    private IBrush GetBrush(string key)
    {
        if (Application.Current?.TryGetResource(key, ActualThemeVariant, out var res) == true && res is IBrush brush)
            return brush;
        return Brushes.Gray;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e) => Close();

    public static async Task ShowAsync(Window owner, string title, string message, string? okText = null)
    {
        var dlg = new InfoDialog(title, message, okText);
        await dlg.ShowDialog(owner);
    }

    public static async Task ShowAsync(Window owner, string title, IReadOnlyList<InfoTextLine> lines, string? okText = null)
    {
        var dlg = new InfoDialog(title, lines, okText);
        await dlg.ShowDialog(owner);
    }
}
