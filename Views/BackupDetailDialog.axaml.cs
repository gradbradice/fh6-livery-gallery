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
        Func<IReadOnlyList<LiveryData>> getCurrentEntries, Func<Task> onRestored)
    {
        InitializeComponent();
        _viewModel = new BackupDetailViewModel(backupService, savePathService, backup, getCurrentEntries, onRestored);
        DataContext = _viewModel;

        _viewModel.ErrorMessageRequested += async message =>
            await InfoDialog.ShowAsync(this, Strings.BackupsDialogTitle, message);
        _viewModel.ConfirmRestoreDuplicatesAsync = message => ConfirmDialog.AskAsync(
            this, Strings.RestoreDuplicateTitle, message, Strings.RestoreAnywayButton, Strings.RestoreSkipDuplicatesButton);

        string title = string.Format(Strings.BackupDetailTitleFormat, backup.DateText);
        Title = title;
        TitleBarText.Text = title;
        RestoreButton.Content = Strings.ArchiveRestoreButton;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.CancelBackgroundWork();
        base.OnClosed(e);
    }

    private async void LiveryRow_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        try
        {
            if (sender is Control control) await ThumbnailLifecycleController.OnAttachedAsync(control);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Unhandled exception in LiveryRow_AttachedToVisualTree", ex);
        }
    }

    private void LiveryRow_DetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control) ThumbnailLifecycleController.OnDetached(control);
    }

    private async void LiveryRow_DataContextChanged(object? sender, EventArgs e)
    {
        try
        {
            if (sender is Control control) await ThumbnailLifecycleController.OnDataContextChangedAsync(control);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Unhandled exception in LiveryRow_DataContextChanged", ex);
        }
    }
}
