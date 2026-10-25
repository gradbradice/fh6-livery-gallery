using Avalonia.Interactivity;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>The room in the save: the counter, "free up room" and the gallery's automatic backups</summary>
internal partial class MainWindow
{
    private void InitializeRoom()
    {
        _mainViewModel.AutoBackups = new AutoBackups(_backupService, _settings);
        _mainViewModel.ConfirmArchiveWithoutBackupAsync = () => ConfirmDialog.AskAsync(
            this, Strings.AutoBackupFailedTitle, Strings.AutoBackupFailedMessage, Strings.AutoBackupFailedContinueButton, Strings.ButtonCancel);
        _mainViewModel.FewFreeSlotsFound += (free, limit) => RunUiTask(() => WarnFewFreeSlotsAsync(free, limit), "few free slots");
    }

    private void SlotsCounter_Click(object? sender, RoutedEventArgs e) => RunUiTask(OpenFreeSpaceAsync, "free up room");

    private void FreeSpaceMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        SettingsButton.Flyout?.Hide();
        RunUiTask(OpenFreeSpaceAsync, "free up room");
    }

    /// <summary>
    /// Once a launch, when the first scan finds little room: what is left, and a way to free some. "Do not show again"
    /// turns it off (<see cref="Models.AppSettingsData.WarnFewFreeSlots"/>, back in Settings)
    /// </summary>
    private async Task WarnFewFreeSlotsAsync(int free, int limit)
    {
        var culture = AppLocalisationService.Culture;
        string message = free == 0
            ? string.Format(Strings.FewFreeSlotsFullMessageFormat, limit.ToString("N0", culture))
            : string.Format(Strings.FewFreeSlotsMessageFormat, free.ToString("N0", culture), limit.ToString("N0", culture));
        var (freeUp, dontShowAgain) = await ConfirmDialog.AskWithOptionAsync(
            this, Strings.StatsSlotsTitle, message, Strings.DontShowAgainCheckBox, Strings.FreeSpaceMenuItem, Strings.ButtonClose);
        // Ticked with either button: the window does not come at start any more; the counter and "Free up room" stay
        if (dontShowAgain) _mainViewModel.TurnOffFewFreeSlotsWarning();
        if (freeUp) await OpenFreeSpaceAsync();
    }

    /// <summary>"Free up room": the copies of each duplicated livery but the best go to the archive, as the user ticks them</summary>
    private async Task OpenFreeSpaceAsync()
    {
        var gallery = _mainViewModel.Gallery;
        var all = gallery.AllEntries.ToList();
        var plan = FreeSpacePlanner.Plan(all, gallery.IsInstalledDataKnown);
        var dialog = new FreeSpaceDialog(new FreeSpaceViewModel(plan, SaveRoom.Counted(all), _profile.MaxLiveries));
        var chosen = await dialog.ShowDialog<IReadOnlyList<LiveryEntry>?>(this);
        if (_isClosing || chosen is not { Count: > 0 }) return;
        await _mainViewModel.MoveToArchiveAsync(chosen);
    }
}
