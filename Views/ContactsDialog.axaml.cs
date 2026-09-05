using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LiveryGallery.Localisation;

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

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void GithubLink_Click(object? sender, RoutedEventArgs e) => OpenUrl(GithubUrl);

    private void TwitterLink_Click(object? sender, RoutedEventArgs e) => OpenUrl(TwitterUrl);

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {

        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
