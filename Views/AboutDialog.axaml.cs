using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ForzaData;
using LiveryGallery.Configuration;
using LiveryGallery.Localisation;

namespace LiveryGallery.Views;

internal partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();

        Title = Strings.AboutTitle;
        TitleBarText.Text = Strings.AboutTitle;
        VersionText.Text = $"{Strings.AboutVersionLabel} {AppSettings.Version}";
        DescriptionText.Text = Strings.AboutDescription;
        CloseButton.Content = Strings.ButtonClose;
        HeaderParserVersionText.Text = string.Format(Strings.HeaderParserVersionFormat, NativeHeaderParser.GetVersion());
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
