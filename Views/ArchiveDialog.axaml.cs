using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using LiveryGallery.Controller;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

internal partial class ArchiveDialog : Window
{
    private readonly ArchiveViewModel _viewModel;

    public ArchiveDialog(LiveryArchiveService archiveService, SavePathService savePathService, Func<Task> onRestored)
    {
        InitializeComponent();
        _viewModel = new ArchiveViewModel(archiveService, savePathService, onRestored);
        DataContext = _viewModel;

        _viewModel.ErrorMessageRequested += async message =>
            await InfoDialog.ShowAsync(this, Strings.ArchiveDialogTitle, message);
        _viewModel.DeleteRequested += async rows => await ConfirmDeleteAsync(rows);

        Title = Strings.ArchiveDialogTitle;
        TitleBarText.Text = Strings.ArchiveDialogTitle;
        TitleText.Text = Strings.ArchiveDialogTitle;
        RestoreButton.Content = Strings.ArchiveRestoreButton;
        DeleteButton.Content = Strings.ArchiveDeleteButton;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private async Task ConfirmDeleteAsync(List<ArchivedLiveryRowViewModel> rows)
    {
        string names = string.Join(", ", rows.Select(r => r.LiveryName));
        bool confirmed = await ConfirmDialog.AskAsync(
            this, Strings.ArchiveDeleteConfirmTitle, string.Format(Strings.ArchiveDeleteConfirmMessage, names));
        if (!confirmed) return;

        await _viewModel.DeleteConfirmedAsync(rows);
    }

    private async void Row_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        try
        {
            if (sender is not Control control || control.DataContext is not ArchivedLiveryRowViewModel row) return;

            var (wasSuperseded, bitmap) = await ThumbnailCacheService.AcquireForAsync(control, row.ThumbnailPath);
            if (!wasSuperseded && ReferenceEquals(control.DataContext, row)) row.Thumbnail = bitmap;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Unhandled exception in Row_AttachedToVisualTree", ex);
        }
    }

    private void Row_DetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control control) return;
        if (control.DataContext is ArchivedLiveryRowViewModel row) row.Thumbnail = null;
        ThumbnailCacheService.ReleaseFor(control);
    }
}
