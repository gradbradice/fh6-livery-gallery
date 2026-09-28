using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ForzaToolkit.Formats;
using ForzaToolkit.LiveryRender;
using ForzaToolkit.LiveryRender.Avalonia;
using LiveryGallery.Configuration;
using LiveryGallery.Controller;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using System.Reflection;

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
        ForzaDataPackageVersionText.Text = string.Format(Strings.ForzaDataPackageVersionFormat,
            GetPackageVersion(typeof(NativeHeaderParser).Assembly));
        LiveryRenderPackageVersionText.Text = string.Format(Strings.LiveryRenderPackageVersionFormat,
            GetPackageVersion(typeof(LiveryRenderer).Assembly));
        LiveryRenderAvaloniaPackageVersionText.Text = string.Format(Strings.LiveryRenderAvaloniaPackageVersionFormat,
            GetPackageVersion(typeof(LiveryViewer).Assembly));
        GithubLinkButton.Content = Strings.ContactsGithubLabel;
    }

    private static string GetPackageVersion(Assembly assembly)
    {
        try
        {
            string? version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString();
            if (version is null) return "?";

            int plusIndex = version.IndexOf('+');
            return plusIndex >= 0 ? version[..plusIndex] : version;
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to read the package version of {assembly.GetName().Name}", ex);
            return "?";
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private void GithubLink_Click(object? sender, RoutedEventArgs e) => TrustedUrlLauncher.TryOpen(GithubUrl);

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
