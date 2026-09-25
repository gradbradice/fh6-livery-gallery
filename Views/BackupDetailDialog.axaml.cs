using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using LiveryGallery.Controller;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

internal partial class BackupDetailDialog : Window
{
    private readonly BackupDetailViewModel _viewModel;

    public BackupDetailDialog(
        LiveryBackupService backupService, SavePathService savePathService, BackupRowViewModel backup,
        IReadOnlyList<LiveryData> currentEntries, Func<Task> onRestored)
    {
        InitializeComponent();
        _viewModel = new BackupDetailViewModel(backupService, savePathService, backup, currentEntries, onRestored);
        DataContext = _viewModel;

        _viewModel.ErrorMessageRequested += async message =>
            await InfoDialog.ShowAsync(this, Strings.BackupsDialogTitle, message);

        string title = string.Format(Strings.BackupDetailTitleFormat, backup.DateText);
        Title = title;
        TitleBarText.Text = title;
        RestoreButton.Content = Strings.ArchiveRestoreButton;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private async void RemovedRow_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        try
        {
            if (sender is not Control control || control.DataContext is not RemovedLiveryRowViewModel row) return;

            var (wasSuperseded, bitmap) = await ThumbnailCacheService.AcquireForAsync(control, row.ThumbnailPath);
            if (!wasSuperseded && ReferenceEquals(control.DataContext, row)) row.Thumbnail = bitmap;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Unhandled exception in RemovedRow_AttachedToVisualTree", ex);
        }
    }

    private void RemovedRow_DetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control control) return;
        if (control.DataContext is RemovedLiveryRowViewModel row) row.Thumbnail = null;
        ThumbnailCacheService.ReleaseFor(control);
    }
}
