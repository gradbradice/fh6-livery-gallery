using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LiveryGallery.Controller;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.Views;

internal partial class ContactsDialog : Window
{
    private const string GithubUrl = "https://github.com/gradbradice/fh6-livery-gallery";
    private const string TwitterUrl = "https://x.com/bradice_livery";

    public ContactsDialog()
    {
        InitializeComponent();

        Title = Strings.ContactsTitle;
        TitleBarText.Text = Strings.ContactsTitle;
        TitleText.Text = Strings.ContactsTitle;
        GithubLinkButton.Content = Strings.ContactsGithubLabel;
        CloseDialogButton.Content = Strings.ButtonClose;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private void GithubLink_Click(object? sender, RoutedEventArgs e) => TrustedUrlLauncher.TryOpen(GithubUrl);

    private void TwitterLink_Click(object? sender, RoutedEventArgs e) => TrustedUrlLauncher.TryOpen(TwitterUrl);

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
