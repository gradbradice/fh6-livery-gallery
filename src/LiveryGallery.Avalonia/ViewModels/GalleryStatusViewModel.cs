using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

internal sealed partial class GalleryStatusViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _loadingText;

    [ObservableProperty]
    private string _countText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFavorites))]
    private int _favoritesCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDuplicates))]
    private int _duplicatesCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPossibleDuplicates))]
    private int _possibleDuplicatesCount;

    [ObservableProperty]
    private string _emptyStateText = string.Empty;

    [ObservableProperty]
    private bool _isEmptyStateVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRefresh))]
    private bool _isScanRunning;

    public bool CanRefresh => !IsScanRunning;

    public bool HasFavorites => FavoritesCount > 0;
    public bool HasDuplicates => DuplicatesCount > 0;
    public bool HasPossibleDuplicates => PossibleDuplicatesCount > 0;

    public void RenderScanStatus(LiveryScanEntry? lastScanResult)
    {
        if (lastScanResult is null) return;

        string text = string.Format(Strings.StatusProcessed, lastScanResult.Parsed, lastScanResult.ReusedFromCache);
        if (lastScanResult.Errors > 0) text += string.Format(Strings.StatusErrors, lastScanResult.Errors);
        if (lastScanResult.Removed > 0) text += string.Format(Strings.StatusRemoved, lastScanResult.Removed);
        StatusText = text;
    }

    public void SetStatusText(string text) => StatusText = text;

    public void ShowEmptyState(string text)
    {
        EmptyStateText = text;
        IsEmptyStateVisible = true;
    }

    public void SetLoading(bool loading, string? text = null)
    {
        IsLoading = loading;
        if (text is not null) LoadingText = text;
    }

    public void UpdateCountsAndEmptyState(List<LiveryEntry> filtered, int allEntriesCount, string? searchText)
    {
        string search = searchText?.Trim() ?? string.Empty;
        var stats = GalleryStatisticsService.Calculate(filtered);

        CountText = allEntriesCount == 0 ? string.Empty : string.Format(Strings.CountShowing, filtered.Count, allEntriesCount);
        FavoritesCount = stats.FavoritesShown;
        DuplicatesCount = stats.DuplicatesShown;
        PossibleDuplicatesCount = stats.PossibleDuplicatesShown;

        if (allEntriesCount == 0)
        {
            EmptyStateText = Strings.EmptyNoLiveries;
            IsEmptyStateVisible = true;
        }
        else if (filtered.Count == 0)
        {
            EmptyStateText = string.Format(Strings.EmptyNoResults, search);
            IsEmptyStateVisible = true;
        }
        else
        {
            IsEmptyStateVisible = false;
        }
    }
}
