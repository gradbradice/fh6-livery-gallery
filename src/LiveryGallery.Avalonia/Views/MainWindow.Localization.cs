using Avalonia.Controls;
using LiveryGallery.Configuration;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

internal partial class MainWindow
{
    private void ApplyLocalizedTexts()
    {
        Title = AppSettings.Identity.Name;
        CustomTitleBarText.Text = AppSettings.Identity.Name;
        MinimizeButtonEl.SetValue(ToolTip.TipProperty, Strings.MinimizeTooltip);
        MaximizeButtonEl.SetValue(ToolTip.TipProperty, Strings.MaximizeTooltip);
        CloseButtonEl.SetValue(ToolTip.TipProperty, Strings.CloseTooltip);

        RefreshButton.SetValue(ToolTip.TipProperty, Strings.RefreshTooltip);
        StatsButton.SetValue(ToolTip.TipProperty, Strings.StatsToggleTooltip);
        SettingsButton.SetValue(ToolTip.TipProperty, Strings.SettingsToggleTooltip);
        OpenSettingsMenuItem.Header = Strings.SettingsDialogTitle;
        OpenArchiveMenuItem.Header = Strings.MenuOpenArchive;
        OpenBackupsMenuItem.Header = Strings.MenuOpenBackups;
        ContactsMenuItem.Header = Strings.SettingsMenuContacts;
        AboutMenuItem.Header = string.Format(Strings.AboutTitleFormat, AppSettings.Identity.Name);
        SearchBox.PlaceholderText = Strings.SearchPlaceholder;

        DisplayFilterButton.SetValue(ToolTip.TipProperty, Strings.DisplayFilterTooltip);
        AuthorsButton.SetValue(ToolTip.TipProperty, Strings.AuthorsButtonTooltip);
        FilterMenuTitleText.Text = Strings.FilterMenuTitle;
        SortLabelText.Text = Strings.FilterSortLabel;
        GroupingLabelText.Text = Strings.FilterGroupingLabel;
        FavLabelText.Text = Strings.FilterFavoritesLabel;
        InstalledLabelText.Text = Strings.FilterInstalledLabel;
        MineLabelText.Text = Strings.FilterMineLabel;
        DupLabelText.Text = Strings.FilterDuplicatesLabel;
        GenLabelText.Text = Strings.FilterGeneratedLabel;
        PaintLabelText.Text = Strings.FilterPaintLabel;
        AuctionLabelText.Text = Strings.FilterAuctionLabel;
        SortManufacturerItem.Header = Strings.SortOptionManufacturer;
        SortAuthorItem.Header = Strings.SortOptionAuthor;
        SortDownloadTimeItem.Header = Strings.SortOptionDownloadDate;
        SortGameOrderItem.Header = Strings.SortOptionGameOrder;
        SortCountryItem.Header = Strings.SortOptionCountry;
        SortCarTypeItem.Header = Strings.SortOptionCarType;
        GroupingNoneItem.Header = Strings.GroupingOptionNone;
        FavNoneItem.Header = Strings.NormalOrderToggle;
        FavFirstItemText.Text = Strings.FavoritesFirstToggle;
        FavOnlyItemText.Text = Strings.OnlyFavoritesToggle;
        FavSeparateItemText.Text = Strings.SeparateFavoritesToggle;
        InstalledNoneItem.Header = Strings.InstalledAllToggle;
        InstalledFirstItemText.Text = Strings.InstalledFirstToggle;
        InstalledOnlyItemText.Text = Strings.OnlyInstalledToggle;
        MineNoneItem.Header = Strings.NormalOrderToggle;
        MineFirstItemText.Text = Strings.MineFirstToggle;
        MineOnlyItemText.Text = Strings.OnlyMineToggle;
        MineSeparateItemText.Text = Strings.SeparateMineToggle;
        DupAllItem.Header = Strings.DuplicatesFilterAll;
        DupAndPossibleItem.Header = Strings.DuplicatesFilterAndPossible;
        DupOnlyItem.Header = Strings.DuplicatesFilterOnly;
        GenAllItem.Header = Strings.GeneratedFilterAll;
        GenOnlyItem.Header = Strings.GeneratedFilterOnly;
        PaintAllItem.Header = Strings.PaintFilterAll;
        PaintOnlyItem.Header = Strings.PaintFilterOnly;
        PaintHideItem.Header = Strings.PaintFilterHide;
        AuctionAllItem.Header = Strings.AuctionFilterAll;
        AuctionOnlyItem.Header = Strings.AuctionFilterOnly;
        AuctionSeparateItem.Header = Strings.AuctionFilterSeparately;
        AuctionHideItem.Header = Strings.AuctionFilterHide;

        TagsFilterLabel.Text = Strings.TagsFilterLabel;
        QuickFiltersLabel.Text = Strings.QuickFiltersLabel;
        AddQuickFilterLabel.Text = Strings.QuickFilterAddButton;
    }

    internal void OnLanguageChanged()
    {
        if (!_isLoaded) return;
        ApplyLocalizedTexts();
        _mainViewModel.Status.RenderScanStatus(_mainViewModel.LastScanResult);
        _mainViewModel.Gallery.RefreshCountsOnly();
        _mainViewModel.FilterBar.RefreshLocalizedText();
        _mainViewModel.ServerStatus.RefreshLocalizedText();
        _ = _mainViewModel.RefreshAccountsAsync(); // the switcher's "Account …1234" and livery counts
        foreach (var entry in _mainViewModel.Gallery.AllEntries)
            entry.RefreshLocalizedText();

        foreach (var group in _mainViewModel.Gallery.DisplayedGroups)
        {
            string oldKey = group.Key;
            group.Key = group.SpecialKind switch
            {
                LiveryGroupSpecialKind.DownloadMonth when group.SpecialMonth is { } month =>
                    month.ToString(AppLocalisationService.MonthYearFormat, AppLocalisationService.Culture),
                LiveryGroupSpecialKind.UnknownDownloadDate => Strings.UnknownDownloadDate,
                LiveryGroupSpecialKind.UnknownManufacturer => Strings.UnknownManufacturer,
                LiveryGroupSpecialKind.UnknownAuthor => Strings.UnknownAuthor,
                LiveryGroupSpecialKind.UnknownCountry => Strings.UnknownCountry,
                LiveryGroupSpecialKind.UnknownCarType => Strings.UnknownCarType,
                LiveryGroupSpecialKind.AllLiveries => Strings.AllLiveriesGroupName,
                LiveryGroupSpecialKind.NotInMyDesigns => Strings.GroupNotInMyDesigns,
                _ when group.IsFavoritesGroup => Strings.SeparateFavoritesGroupName,
                _ when group.IsMineGroup => Strings.SeparateMineGroupName,
                _ when group.IsAuctionGroup => Strings.SeparateAuctionGroupName,
                _ => group.Key
            };

            if (oldKey != group.Key && _collapsedGroupKeys.Remove(oldKey))
                _collapsedGroupKeys.Add(group.Key);
        }

        _mainViewModel.Update.RefreshLocalizedBannerText();
        // Car texts "as in the interface" follow it: the country and car type groups are named again
        _mainViewModel.RefreshCarTextsIfLanguageChanged();
    }
}
