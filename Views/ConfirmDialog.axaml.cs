using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using LiveryGallery.Controller;
using LiveryGallery.Localisation;

namespace LiveryGallery.Views;

internal partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string? yesText = null, string? noText = null, string iconKey = "IconFlag")
    {
        InitializeComponent();
        Title = title;
        TitleBarText.Text = title;
        MessageText.Text = message;
        YesButton.Content = yesText ?? Strings.ButtonYes;
        NoButton.Content = noText ?? Strings.ButtonNo;

        if (Application.Current?.TryGetResource(iconKey, ActualThemeVariant, out var res) == true && res is Geometry geometry)
            TitleBarIcon.Data = geometry;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private void YesButton_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void NoButton_Click(object? sender, RoutedEventArgs e) => Close(false);

    public static async Task<bool> AskAsync(Window owner, string title, string message, string? yesText = null, string? noText = null, string iconKey = "IconFlag")
    {
        var dlg = new ConfirmDialog(title, message, yesText, noText, iconKey);
        return await dlg.ShowDialog<bool>(owner);
    }
}