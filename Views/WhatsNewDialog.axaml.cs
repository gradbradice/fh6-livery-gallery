using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (e.ClickCount == 2)
            ToggleMaximize();
        else
            BeginMoveDrag(e);
    }

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e) => ToggleMaximize();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        MaximizeIcon.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

    private void DownloadButton_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_releaseUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo(_releaseUrl) { UseShellExecute = true });
        }
        catch
        {

        }
    }
}
