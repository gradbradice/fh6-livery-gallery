using Avalonia.Controls;
using Avalonia.Input;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

internal partial class BackupDetailDialog : Window
{
    private readonly BackupDetailViewModel _viewModel;
    private readonly DialogWorkScope _work;

    public BackupDetailDialog(
        LiveryBackups backupService, SavePathService savePathService, BackupRowViewModel backup,
        Func<IReadOnlyList<LiveryData>> getCurrentEntries, Func<Task> onRestored)
    {
        InitializeComponent();
        _viewModel = new BackupDetailViewModel(backupService, savePathService, backup, getCurrentEntries, onRestored);
        DataContext = _viewModel;
        _work = new DialogWorkScope(this, () => _viewModel.RestoreSelectedCommand.ExecutionTask);

        _viewModel.ErrorMessageRequested += message => _work.Run(
            () => InfoDialog.ShowAsync(this, Strings.BackupsDialogTitle, message), "backup detail: error message");
        _viewModel.ConfirmRestoreDuplicatesAsync = message => ConfirmDialog.AskAsync(
            this, Strings.RestoreDuplicateTitle, message, Strings.RestoreAnywayButton, Strings.RestoreSkipDuplicatesButton);
        _viewModel.ConfirmRestoreIntoCurrentAccountAsync = message => ConfirmDialog.AskAsync(
            this, Strings.RestoreFromOtherAccountTitle, message, Strings.RestoreIntoCurrentAccountButton, Strings.ButtonCancel);
        _viewModel.ConfirmWhileGameRunningAsync = () => ConfirmDialog.AskAsync(
            this, string.Format(Strings.GameRunningTitleFormat, savePathService.Profile.Game.Name), Strings.GameRunningFileChangeMessage, Strings.GameRunningContinueButton, Strings.ButtonCancel);

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

}
