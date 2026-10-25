using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LiveryGallery.Configuration;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.Views;

internal partial class ContactsDialog : Window
{
    public ContactsDialog()
    {
        InitializeComponent();

        Title = Strings.ContactsTitle;
        TitleBarText.Text = Strings.ContactsTitle;
        TitleText.Text = Strings.ContactsTitle;
        GithubLinkButton.Content = Strings.ContactsGithubLabel;
        TwitterRow.IsVisible = AppSettings.Identity.XUrl is not null;
        TwitterLinkButton.Content = AppSettings.Identity.XLabel ?? AppSettings.Identity.XUrl;
        EmailRow.IsVisible = AppSettings.Identity.Email is not null;
        EmailText.Text = AppSettings.Identity.Email;
        CloseDialogButton.Content = Strings.ButtonClose;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private void GithubLink_Click(object? sender, RoutedEventArgs e) => TrustedUrlLauncher.TryOpen(AppSettings.Identity.GitHubUrl);

    private void TwitterLink_Click(object? sender, RoutedEventArgs e)
    {
        if (AppSettings.Identity.XUrl is { } url) TrustedUrlLauncher.TryOpen(url);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
