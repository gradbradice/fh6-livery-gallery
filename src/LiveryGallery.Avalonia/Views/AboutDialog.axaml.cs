using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LiveryGallery.Configuration;
using LiveryGallery.Controller;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.Viewer3D;

namespace LiveryGallery.Views;

internal partial class AboutDialog : Window
{
    /// <param name="components">Versions of the closed components (the host reads them); the open ones are read here</param>
    /// <param name="profile">The game: its package is listed with the open ones</param>
    public AboutDialog(AboutComponents components, GameProfile profile)
    {
        InitializeComponent();

        string title = string.Format(Strings.AboutTitleFormat, AppSettings.Identity.Name);
        Title = title;
        TitleBarText.Text = title;
        AppNameText.Text = AppSettings.Identity.Name;
        VersionText.Text = $"{Strings.AboutVersionLabel} {AppSettings.Version}";
        DescriptionText.Text = string.Format(Strings.AboutDescriptionFormat, profile.Game.Name);
        CloseButton.Content = Strings.ButtonClose;
        OpenPackagesLabel.Text = Strings.AboutOpenPackagesLabel;
        OpenPackagesList.ItemsSource = ComponentVersions.OpenPackages(profile);
        ClosedComponentsLabel.Text = Strings.AboutClosedComponentsLabel;
        ClosedComponentsList.ItemsSource = components.Closed;
        ClosedComponentsSection.IsVisible = components.Closed.Count > 0;
        GithubLinkButton.Content = Strings.ContactsGithubLabel;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private void GithubLink_Click(object? sender, RoutedEventArgs e) => TrustedUrlLauncher.TryOpen(AppSettings.Identity.GitHubUrl);

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
