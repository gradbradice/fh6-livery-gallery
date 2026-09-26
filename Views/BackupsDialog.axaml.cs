using Avalonia.Controls;
using Avalonia.Input;
using LiveryGallery.Controller;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

internal partial class BackupsDialog : Window
{
    private readonly LiveryBackupService _backupService;
    private readonly SavePathService _savePathService;
    private readonly Func<IReadOnlyList<LiveryData>> _getCurrentEntries;
    private readonly Func<Task> _onRestored;
    private readonly BackupsViewModel _viewModel;

    public BackupsDialog(
        LiveryBackupService backupService, SavePathService savePathService,
        Func<IReadOnlyList<LiveryData>> getCurrentEntries, Func<Task> onRestored)
    {
        InitializeComponent();
        _backupService = backupService;
        _savePathService = savePathService;
        _getCurrentEntries = getCurrentEntries;
        _onRestored = onRestored;
        _viewModel = new BackupsViewModel(backupService, savePathService, getCurrentEntries);
        DataContext = _viewModel;

        _viewModel.ErrorMessageRequested += async message =>
            await InfoDialog.ShowAsync(this, Strings.BackupsDialogTitle, message);
        _viewModel.OpenRequested += row => OpenDetail(row);
        _viewModel.DeleteRequested += async row => await ConfirmDeleteAsync(row);

        Title = Strings.BackupsDialogTitle;
        TitleBarText.Text = Strings.BackupsDialogTitle;
        TitleText.Text = Strings.BackupsDialogTitle;
        CreateBackupButton.Content = Strings.BackupCreateButton;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private async void OpenDetail(BackupRowViewModel row)
    {
        if (row.Manifest is null)
        {
            await InfoDialog.ShowAsync(this, Strings.BackupsDialogTitle, Strings.BackupUnreadableMessage);
            return;
        }

        var dialog = new BackupDetailDialog(
            _backupService, _savePathService, row, _getCurrentEntries, _onRestored);
        await dialog.ShowDialog(this);
    }

    private async Task ConfirmDeleteAsync(BackupRowViewModel row)
    {
        bool confirmed = await ConfirmDialog.AskAsync(
            this, Strings.BackupDeleteConfirmTitle,
            string.Format(Strings.BackupDeleteConfirmMessage, row.DateText, row.SizeText));
        if (!confirmed) return;

        _viewModel.DeleteConfirmed(row);
    }
}
