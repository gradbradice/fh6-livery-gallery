using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LiveryGallery.Localisation;

namespace LiveryGallery.Views;

internal partial class InfoDialog : Window
{
    public InfoDialog(string title, string message, string? okText = null)
    {
        InitializeComponent();
        Title = title;
        TitleBarText.Text = title;
        MessageText.Text = message;
        OkButton.Content = okText ?? Strings.ButtonOk;
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
}
