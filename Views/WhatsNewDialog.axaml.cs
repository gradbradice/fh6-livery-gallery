using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LiveryGallery.Controller;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.Views;

internal partial class WhatsNewDialog : Window
{
    private readonly string? _releaseUrl;

    public WhatsNewDialog(string version, string? releaseBody, string? releaseUrl)
    {
        InitializeComponent();
        _releaseUrl = releaseUrl;

        string title = string.Format(Strings.WhatsNewTitleFormat, version);
        Title = title;
        TitleBarText.Text = title;
        TitleText.Text = title;
        CloseDialogButton.Content = Strings.ButtonClose;
        DownloadButton.Content = Strings.ButtonUpdateDownload;
        DownloadButton.IsEnabled = !string.IsNullOrEmpty(releaseUrl);

        string currentHeader = Strings.ChangelogSectionHeader;
        string englishHeader = AppLocalisationService.AppLanguage == AppLanguage.English
            ? currentHeader
            : "English";
        string? section = ChangelogService.Extract(releaseBody, currentHeader, englishHeader);
        MarkdownContent.Markdown = section ?? Strings.ChangelogNotAvailable;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) =>
        this.HandleTitleBarDragOrMaximize(e, MaximizeIcon);

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e) => this.ToggleMaximize(MaximizeIcon);

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

    private void DownloadButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_releaseUrl is not null) TrustedUrlLauncher.TryOpen(_releaseUrl);
    }
}
