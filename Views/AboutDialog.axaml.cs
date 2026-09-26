using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Forza.Data;
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
        ForzaDataPackageVersionText.Text = string.Format(Strings.ForzaDataPackageVersionFormat, GetForzaDataPackageVersion());
        GithubLinkButton.Content = Strings.ContactsGithubLabel;
    }

    private static string GetForzaDataPackageVersion()
    {
        try
        {
            var assembly = typeof(NativeHeaderParser).Assembly;
            string? version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString();
            if (version is null) return "?";

            int plusIndex = version.IndexOf('+');
            return plusIndex >= 0 ? version[..plusIndex] : version;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to read Forza.Data package version", ex);
            return "?";
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private void GithubLink_Click(object? sender, RoutedEventArgs e) => TrustedUrlLauncher.TryOpen(GithubUrl);

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
