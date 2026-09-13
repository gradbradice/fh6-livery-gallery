using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ForzaData;
using LiveryGallery.Configuration;
using LiveryGallery.Controller;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.Views;

internal partial class AboutDialog : Window
{
    private const string GithubUrl = "https://github.com/gradbradice/fh6-livery-gallery";

    public AboutDialog()
    {
        InitializeComponent();

        Title = Strings.AboutTitle;
        TitleBarText.Text = Strings.AboutTitle;
        VersionText.Text = $"{Strings.AboutVersionLabel} {AppSettings.Version}";
        DescriptionText.Text = Strings.AboutDescription;
        CloseButton.Content = Strings.ButtonClose;
        HeaderParserVersionText.Text = string.Format(Strings.HeaderParserVersionFormat, NativeHeaderParser.GetVersion());
        GithubLinkButton.Content = Strings.ContactsGithubLabel;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private void GithubLink_Click(object? sender, RoutedEventArgs e) => TrustedUrlLauncher.TryOpen(GithubUrl);

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
