using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using LiveryGallery.Controller;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.Views;

internal partial class LiveryPreviewDialog : Window
{
    public LiveryPreviewDialog(string webpPath)
    {
        InitializeComponent();

        Title = Strings.PreviewDialogTitle;
        TitleBarText.Text = Strings.PreviewDialogTitle;

        try
        {
            using var stream = File.OpenRead(webpPath);
            PreviewImage.Source = new Bitmap(stream);
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to load preview image '{webpPath}'", ex);
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
