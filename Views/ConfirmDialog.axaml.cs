using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LiveryGallery.Localisation;

namespace LiveryGallery.Views;

internal partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string? yesText = null, string? noText = null)
    {
        InitializeComponent();
        Title = title;
        TitleBarText.Text = title;
        MessageText.Text = message;
        YesButton.Content = yesText ?? Strings.ButtonYes;
        NoButton.Content = noText ?? Strings.ButtonNo;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void YesButton_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void NoButton_Click(object? sender, RoutedEventArgs e) => Close(false);

    public static async Task<bool> AskAsync(Window owner, string title, string message, string? yesText = null, string? noText = null)
    {
        var dlg = new ConfirmDialog(title, message, yesText, noText);
        return await dlg.ShowDialog<bool>(owner);
    }
}