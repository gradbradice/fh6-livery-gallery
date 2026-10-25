using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

/// <summary>The room in the save: the counter of the status bar, and the warning at the first scan of a launch</summary>
internal sealed partial class MainViewModel
{
    private bool _roomChecked;

    /// <summary>The game has a limit: the counter shows</summary>
    [ObservableProperty]
    private bool _hasSlotsLimit;

    /// <summary>"238 / 1000"</summary>
    [ObservableProperty]
    private string _slotsText = "";

    [ObservableProperty]
    private string _slotsTooltip = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSlotsWarning), nameof(IsSlotsFull))]
    private SlotsLevel _slotsLevel;

    public bool IsSlotsWarning => SlotsLevel == SlotsLevel.Warning;
    public bool IsSlotsFull => SlotsLevel == SlotsLevel.Full;

    /// <summary>The first scan of the launch found fewer than <see cref="SaveRoom.FewFreeSlots"/> free slots: (free, limit)</summary>
    public event Action<int, int>? FewFreeSlotsFound;

    /// <summary>"Do not show again" in the "Room in the save" window: it is not shown at start any more (Settings bring it back)</summary>
    public void TurnOffFewFreeSlotsWarning()
    {
        if (!settings.WarnFewFreeSlots) return;
        settings.WarnFewFreeSlots = false;
        AppSettingsService.Save(settings);
    }

    /// <summary>The counter after the gallery changed (a scan, an archive, a restore)</summary>
    private void UpdateSlots()
    {
        if (savePathService.Profile.MaxLiveries is not { } limit || savePathService.SavePath is null)
        {
            HasSlotsLimit = false;
            return;
        }
        int counted = SaveRoom.Counted(Gallery.AllEntries);
        int free = Math.Max(0, limit - counted);
        var culture = AppLocalisationService.Culture;
        SlotsText = $"{counted.ToString("N0", culture)} / {limit.ToString("N0", culture)}";
        SlotsLevel = SaveRoom.Level(counted, limit);
        string state = SlotsLevel switch
        {
            SlotsLevel.Full => Strings.StatsSlotsFull,
            SlotsLevel.Warning => string.Format(Strings.StatsSlotsAlmostFormat, free.ToString("N0", culture)),
            _ => string.Format(Strings.StatsSlotsFreeFormat, free.ToString("N0", culture)),
        };
        SlotsTooltip = $"{Strings.StatsSlotsTitle}: {SlotsText}. {state}.{Environment.NewLine}{Strings.StatsSlotsNote}"
            + $"{Environment.NewLine}{Environment.NewLine}{Strings.SlotsCounterClickHint}";
        HasSlotsLimit = true;
    }

    /// <summary>Once a launch, after its first scan: tells the user when little room is left</summary>
    private void CheckRoomOnce()
    {
        if (_roomChecked || savePathService.Profile.MaxLiveries is not { } limit) return;
        _roomChecked = true;
        if (!settings.WarnFewFreeSlots) return;
        int counted = SaveRoom.Counted(Gallery.AllEntries);
        if (SaveRoom.IsFew(counted, limit)) FewFreeSlotsFound?.Invoke(Math.Max(0, limit - counted), limit);
    }
}
