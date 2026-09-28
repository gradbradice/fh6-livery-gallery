using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;
using System.Collections.ObjectModel;

namespace LiveryGallery.ViewModels;

internal sealed partial class FilterBarViewModel : ObservableObject
{
    private readonly AppSettingsData _settings;

    public event Action? FiltersChanged;

    public FilterBarViewModel(AppSettingsData settings)
    {
        _settings = settings;
        _sortMode = settings.SortMode;
        _favoriteMode = settings.FavoriteMode;
        _mineMode = settings.MineMode;
        _installedMode = settings.InstalledMode;
        _isInstalledFilterAvailable = settings.ServerConnectionEnabled;
        _duplicatesFilterMode = settings.DuplicatesFilterMode;
        _generatedFilterMode = settings.GeneratedFilterMode;
        _paintFilterMode = settings.PaintFilterMode;
        _auctionFilterMode = settings.AuctionFilterMode;
        _groupingEnabled = settings.GroupingEnabled;
    }

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSortManufacturer))]
    [NotifyPropertyChangedFor(nameof(IsSortAuthor))]
    [NotifyPropertyChangedFor(nameof(IsSortDownloadTime))]
    [NotifyPropertyChangedFor(nameof(SortValueText))]
    [NotifyPropertyChangedFor(nameof(GroupingValueText))]
    [NotifyPropertyChangedFor(nameof(GroupingByKeyText))]
    private SortMode _sortMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFavNone))]
    [NotifyPropertyChangedFor(nameof(IsFavFirst))]
    [NotifyPropertyChangedFor(nameof(IsFavOnly))]
    [NotifyPropertyChangedFor(nameof(IsFavSeparate))]
    [NotifyPropertyChangedFor(nameof(FavoriteValueText))]
    private FavoriteMode _favoriteMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMineNone))]
    [NotifyPropertyChangedFor(nameof(IsMineFirst))]
    [NotifyPropertyChangedFor(nameof(IsMineOnly))]
    [NotifyPropertyChangedFor(nameof(IsMineSeparate))]
    [NotifyPropertyChangedFor(nameof(MineValueText))]
    private MineMode _mineMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstalledNone))]
    [NotifyPropertyChangedFor(nameof(IsInstalledFirst))]
    [NotifyPropertyChangedFor(nameof(IsInstalledOnly))]
    [NotifyPropertyChangedFor(nameof(InstalledValueText))]
    private InstalledMode _installedMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstalledValueText))]
    [NotifyPropertyChangedFor(nameof(InstalledMenuTooltip))]
    private bool _isInstalledFilterAvailable;

    public InstalledMode EffectiveInstalledMode => IsInstalledFilterAvailable ? InstalledMode : InstalledMode.None;

    public string? InstalledMenuTooltip => IsInstalledFilterAvailable ? null : Strings.InstalledFilterNeedsServer;

    public void SetInstalledFilterAvailable(bool available)
    {
        if (IsInstalledFilterAvailable == available) return;
        IsInstalledFilterAvailable = available;
        if (InstalledMode != InstalledMode.None) FiltersChanged?.Invoke();
    }


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDupAll))]
    [NotifyPropertyChangedFor(nameof(IsDupAndPossible))]
    [NotifyPropertyChangedFor(nameof(IsDupOnly))]
    [NotifyPropertyChangedFor(nameof(DuplicatesValueText))]
    private DuplicatesFilterMode _duplicatesFilterMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGenAll))]
    [NotifyPropertyChangedFor(nameof(IsGenOnly))]
    [NotifyPropertyChangedFor(nameof(GeneratedValueText))]
    private GeneratedFilterMode _generatedFilterMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPaintAll))]
    [NotifyPropertyChangedFor(nameof(IsPaintHidden))]
    [NotifyPropertyChangedFor(nameof(IsPaintOnly))]
    [NotifyPropertyChangedFor(nameof(PaintValueText))]
    private PaintFilterMode _paintFilterMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAuctionAll))]
    [NotifyPropertyChangedFor(nameof(IsAuctionSeparate))]
    [NotifyPropertyChangedFor(nameof(IsAuctionOnly))]
    [NotifyPropertyChangedFor(nameof(IsAuctionHidden))]
    [NotifyPropertyChangedFor(nameof(AuctionValueText))]
    private AuctionFilterMode _auctionFilterMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFavSeparateEnabled))]
    [NotifyPropertyChangedFor(nameof(IsMineSeparateEnabled))]
    [NotifyPropertyChangedFor(nameof(IsAuctionSeparateEnabled))]
    [NotifyPropertyChangedFor(nameof(IsGroupingNone))]
    [NotifyPropertyChangedFor(nameof(IsGroupingByKey))]
    [NotifyPropertyChangedFor(nameof(GroupingValueText))]
    private bool _groupingEnabled;

    public ObservableCollection<QuickFilterChipViewModel> QuickFilters { get; } = [];
    public bool HasQuickFilters => QuickFilters.Count > 0;
    public IReadOnlyList<QuickFilter> ActiveQuickFilters => [.. QuickFilters.Select(c => c.Filter)];

    [RelayCommand]
    private void ShowOnlyCar(LiveryEntry? entry)
    {
        if (entry is not null) SetQuickFilter(QuickFilter.ForCar(entry));
    }

    [RelayCommand]
    private void ShowOnlyManufacturer(LiveryEntry? entry)
    {
        if (entry is not null) SetQuickFilter(QuickFilter.ForManufacturer(entry));
    }

    [RelayCommand]
    private void ShowOnlyAuthor(LiveryEntry? entry)
    {
        if (entry is not null) SetQuickFilter(QuickFilter.ForAuthor(entry));
    }

    [RelayCommand]
    private void RemoveQuickFilter(QuickFilterChipViewModel? chip)
    {
        if (chip is null || !QuickFilters.Remove(chip)) return;
        // A car belongs to its manufacturer. Dropping the manufacturer drops the car as well
        if (chip.Filter.Kind == QuickFilterKind.Manufacturer)
            RemoveChips(c => c.Filter.Kind == QuickFilterKind.Car);
        OnQuickFiltersChanged();
    }

    public QuickFilter? ActiveManufacturerFilter =>
        QuickFilters.FirstOrDefault(c => c.Filter.Kind == QuickFilterKind.Manufacturer)?.Filter;

    [RelayCommand]
    private void ClearQuickFilters()
    {
        if (QuickFilters.Count == 0) return;
        QuickFilters.Clear();
        OnQuickFiltersChanged();
    }

    public void ApplyQuickFilter(QuickFilter filter) => SetQuickFilter(filter);

    private void SetQuickFilter(QuickFilter filter)
    {
        switch (filter.Kind)
        {
            case QuickFilterKind.Author:
                RemoveChips(c => c.Filter.Kind == QuickFilterKind.Author);
                QuickFilters.Add(new QuickFilterChipViewModel(filter));
                break;

            case QuickFilterKind.Manufacturer:
                RemoveChips(c => c.Filter.Kind == QuickFilterKind.Manufacturer
                    || (c.Filter.Kind == QuickFilterKind.Car && !QuickFilter.SameManufacturer(c.Filter.Manufacturer, filter)));
                QuickFilters.Insert(0, new QuickFilterChipViewModel(filter));
                break;

            case QuickFilterKind.Car:
                RemoveChips(c => c.Filter.Kind == QuickFilterKind.Car
                    || (c.Filter.Kind == QuickFilterKind.Manufacturer && !QuickFilter.SameManufacturer(c.Filter, filter.Manufacturer)));
                if (filter.Manufacturer is { } manufacturer && ActiveManufacturerFilter is null)
                    QuickFilters.Insert(0, new QuickFilterChipViewModel(manufacturer));
                int afterManufacturer = ActiveManufacturerFilter is null ? 0 : 1;
                QuickFilters.Insert(afterManufacturer, new QuickFilterChipViewModel(filter));
                break;
        }
        OnQuickFiltersChanged();
    }

    private void RemoveChips(Func<QuickFilterChipViewModel, bool> predicate)
    {
        foreach (var chip in QuickFilters.Where(predicate).ToList())
            QuickFilters.Remove(chip);
    }

    private void OnQuickFiltersChanged()
    {
        OnPropertyChanged(nameof(HasQuickFilters));
        FiltersChanged?.Invoke();
    }

    public bool IsSortManufacturer => SortMode == SortMode.Manufacture;
    public bool IsSortAuthor => SortMode == SortMode.Author;
    public bool IsSortDownloadTime => SortMode == SortMode.DownloadTime;

    [RelayCommand]
    private void SetSortMode(SortMode mode) => SortMode = mode;

    public bool IsFavNone => FavoriteMode == FavoriteMode.None;
    public bool IsFavFirst => FavoriteMode == FavoriteMode.FavoritesFirst;
    public bool IsFavOnly => FavoriteMode == FavoriteMode.OnlyFavorites;
    public bool IsFavSeparate => FavoriteMode == FavoriteMode.FavoritesSeparately;

    public bool IsFavSeparateEnabled => GroupingEnabled;

    [RelayCommand]
    private void SetFavoriteMode(FavoriteMode mode) => FavoriteMode = mode;

    public bool IsInstalledNone => InstalledMode == InstalledMode.None;
    public bool IsInstalledFirst => InstalledMode == InstalledMode.InstalledFirst;
    public bool IsInstalledOnly => InstalledMode == InstalledMode.OnlyInstalled;

    [RelayCommand]
    private void SetInstalledMode(InstalledMode mode) => InstalledMode = mode;

    public bool IsMineNone => MineMode == MineMode.None;
    public bool IsMineFirst => MineMode == MineMode.MineFirst;
    public bool IsMineOnly => MineMode == MineMode.OnlyMine;
    public bool IsMineSeparate => MineMode == MineMode.MineSeparately;

    public bool IsMineSeparateEnabled => GroupingEnabled;

    [RelayCommand]
    private void SetMineMode(MineMode mode) => MineMode = mode;

    public bool IsDupAll => DuplicatesFilterMode == DuplicatesFilterMode.All;
    public bool IsDupAndPossible => DuplicatesFilterMode == DuplicatesFilterMode.DuplicatesAndPossible;
    public bool IsDupOnly => DuplicatesFilterMode == DuplicatesFilterMode.DuplicatesOnly;

    [RelayCommand]
    private void SetDuplicatesFilterMode(DuplicatesFilterMode mode) => DuplicatesFilterMode = mode;

    public bool IsGenAll => GeneratedFilterMode == GeneratedFilterMode.All;
    public bool IsGenOnly => GeneratedFilterMode == GeneratedFilterMode.GeneratedOnly;

    [RelayCommand]
    private void SetGeneratedFilterMode(GeneratedFilterMode mode) => GeneratedFilterMode = mode;

    public bool IsPaintAll => PaintFilterMode == PaintFilterMode.All;
    public bool IsPaintHidden => PaintFilterMode == PaintFilterMode.HidePaint;
    public bool IsPaintOnly => PaintFilterMode == PaintFilterMode.PaintOnly;

    [RelayCommand]
    private void SetPaintFilterMode(PaintFilterMode mode) => PaintFilterMode = mode;

    public bool IsAuctionAll => AuctionFilterMode == AuctionFilterMode.All;
    public bool IsAuctionSeparate => AuctionFilterMode == AuctionFilterMode.AuctionSeparately;
    public bool IsAuctionOnly => AuctionFilterMode == AuctionFilterMode.OnlyAuction;
    public bool IsAuctionHidden => AuctionFilterMode == AuctionFilterMode.HideAuction;

    // Like favorites/mine. A separate group only makes sense when the gallery is grouped
    public bool IsAuctionSeparateEnabled => GroupingEnabled;

    [RelayCommand]
    private void SetAuctionFilterMode(AuctionFilterMode mode) => AuctionFilterMode = mode;

    public bool IsGroupingNone => !GroupingEnabled;
    public bool IsGroupingByKey => GroupingEnabled;

    [RelayCommand]
    private void DisableGrouping() => GroupingEnabled = false;

    [RelayCommand]
    private void EnableGrouping() => GroupingEnabled = true;

    public string SortValueText => SortMode switch
    {
        SortMode.Author => Strings.SortOptionAuthor,
        SortMode.DownloadTime => Strings.SortOptionDownloadDate,
        _ => Strings.SortOptionManufacturer
    };

    public string GroupingByKeyText => SortMode switch
    {
        SortMode.Author => Strings.GroupingOptionByAuthor,
        SortMode.DownloadTime => Strings.GroupingOptionByMonth,
        _ => Strings.GroupingOptionByManufacturer
    };

    public string GroupingValueText => GroupingEnabled ? GroupingByKeyText : Strings.GroupingOptionNone;

    public string FavoriteValueText => FavoriteMode switch
    {
        FavoriteMode.FavoritesFirst => Strings.FilterValueFirst,
        FavoriteMode.OnlyFavorites => Strings.FilterValueOnly,
        FavoriteMode.FavoritesSeparately => Strings.FilterValueSeparately,
        _ => Strings.FilterValueAll
    };

    public string InstalledValueText => !IsInstalledFilterAvailable
        ? Strings.FilterValueUnavailable
        : InstalledMode switch
        {
            InstalledMode.InstalledFirst => Strings.FilterValueFirst,
            InstalledMode.OnlyInstalled => Strings.FilterValueOnly,
            _ => Strings.FilterValueAll
        };

    public string MineValueText => MineMode switch
    {
        MineMode.MineFirst => Strings.FilterValueFirst,
        MineMode.OnlyMine => Strings.FilterValueOnly,
        MineMode.MineSeparately => Strings.FilterValueSeparately,
        _ => Strings.FilterValueAll
    };

    public string DuplicatesValueText => DuplicatesFilterMode switch
    {
        DuplicatesFilterMode.DuplicatesAndPossible => Strings.FilterValueWithPossible,
        DuplicatesFilterMode.DuplicatesOnly => Strings.FilterValueOnly,
        _ => Strings.FilterValueAll
    };

    public string GeneratedValueText => GeneratedFilterMode switch
    {
        GeneratedFilterMode.GeneratedOnly => Strings.FilterValueOnly,
        _ => Strings.FilterValueAll
    };

    public string PaintValueText => PaintFilterMode switch
    {
        PaintFilterMode.HidePaint => Strings.FilterValueHide,
        PaintFilterMode.PaintOnly => Strings.FilterValueOnly,
        _ => Strings.FilterValueAll
    };

    public string AuctionValueText => AuctionFilterMode switch
    {
        AuctionFilterMode.AuctionSeparately => Strings.FilterValueSeparately,
        AuctionFilterMode.OnlyAuction => Strings.FilterValueOnly,
        AuctionFilterMode.HideAuction => Strings.FilterValueHide,
        _ => Strings.FilterValueAll
    };

    public void RefreshLocalizedText()
    {
        OnPropertyChanged(nameof(SortValueText));
        OnPropertyChanged(nameof(GroupingByKeyText));
        OnPropertyChanged(nameof(GroupingValueText));
        OnPropertyChanged(nameof(FavoriteValueText));
        OnPropertyChanged(nameof(MineValueText));
        OnPropertyChanged(nameof(InstalledValueText));
        OnPropertyChanged(nameof(InstalledMenuTooltip));
        OnPropertyChanged(nameof(DuplicatesValueText));
        OnPropertyChanged(nameof(GeneratedValueText));
        OnPropertyChanged(nameof(PaintValueText));
        OnPropertyChanged(nameof(AuctionValueText));
        foreach (var chip in QuickFilters) chip.RefreshLocalizedText();
    }

    partial void OnSortModeChanged(SortMode value) => Persist(s => s.SortMode = value);
    partial void OnFavoriteModeChanged(FavoriteMode value) => Persist(s => s.FavoriteMode = value);
    partial void OnMineModeChanged(MineMode value) => Persist(s => s.MineMode = value);
    partial void OnInstalledModeChanged(InstalledMode value) => Persist(s => s.InstalledMode = value);
    partial void OnDuplicatesFilterModeChanged(DuplicatesFilterMode value) => Persist(s => s.DuplicatesFilterMode = value);
    partial void OnGeneratedFilterModeChanged(GeneratedFilterMode value) => Persist(s => s.GeneratedFilterMode = value);
    partial void OnPaintFilterModeChanged(PaintFilterMode value) => Persist(s => s.PaintFilterMode = value);
    partial void OnAuctionFilterModeChanged(AuctionFilterMode value) => Persist(s => s.AuctionFilterMode = value);
    partial void OnGroupingEnabledChanged(bool value) => Persist(s => s.GroupingEnabled = value);

    private void Persist(Action<AppSettingsData> apply)
    {
        apply(_settings);
        AppSettingsService.Save(_settings);
        FiltersChanged?.Invoke();
    }
}
