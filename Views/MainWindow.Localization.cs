using Avalonia.Controls;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;

namespace LiveryGallery.Views;

internal partial class MainWindow
{
    private void ApplyLocalizedTexts()
    {
        CustomTitleBarText.Text = Strings.AppTitle;
        MinimizeButtonEl.SetValue(ToolTip.TipProperty, Strings.MinimizeTooltip);
        MaximizeButtonEl.SetValue(ToolTip.TipProperty, Strings.MaximizeTooltip);
        CloseButtonEl.SetValue(ToolTip.TipProperty, Strings.CloseTooltip);

        RefreshButton.SetValue(ToolTip.TipProperty, Strings.RefreshTooltip);
        StatsButton.SetValue(ToolTip.TipProperty, Strings.StatsToggleTooltip);
        SettingsButton.SetValue(ToolTip.TipProperty, Strings.SettingsToggleTooltip);
        OpenSettingsMenuItem.Header = Strings.SettingsDialogTitle;
        ContactsMenuItem.Header = Strings.SettingsMenuContacts;
        AboutMenuItem.Header = Strings.AboutTitle;
        SearchBox.PlaceholderText = Strings.SearchPlaceholder;

        DisplayFilterButton.SetValue(ToolTip.TipProperty, Strings.DisplayFilterTooltip);
        AuthorsButton.SetValue(ToolTip.TipProperty, Strings.AuthorsButtonTooltip);
        GroupingToggleItem.Header = Strings.GroupingToggleLabel;
        SortManufacturerItem.Header = Strings.SortManufacturer;
        SortAuthorItem.Header = Strings.SortAuthor;
        SortDownloadTimeItem.Header = Strings.SortDownloadDate;
        FavNoneItem.Header = Strings.NormalOrderToggle;
        FavFirstItemText.Text = Strings.FavoritesFirstToggle;
        FavOnlyItemText.Text = Strings.OnlyFavoritesToggle;
        FavSeparateItemText.Text = Strings.SeparateFavoritesToggle;
        MineNoneItem.Header = Strings.NormalOrderToggle;
        MineFirstItemText.Text = Strings.MineFirstToggle;
        MineOnlyItemText.Text = Strings.OnlyMineToggle;
        MineSeparateItemText.Text = Strings.SeparateMineToggle;
        DupAllItem.Header = Strings.DuplicatesFilterAll;
        DupAndPossibleItem.Header = Strings.DuplicatesFilterAndPossible;
        DupOnlyItem.Header = Strings.DuplicatesFilterOnly;
        GenAllItem.Header = Strings.GeneratedFilterAll;
        GenOnlyItem.Header = Strings.GeneratedFilterOnly;

        TagsFilterLabel.Text = Strings.TagsFilterLabel;
    }

    internal void OnLanguageChanged()
    {
        if (!_isLoaded) return;
        ApplyLocalizedTexts();
        RenderStatus();
        UpdateCountsAndEmptyState(GetFilteredEntries());
        foreach (var entry in _galleryController.AllEntries)
            entry.RefreshLocalizedText();

        if (GroupsHost.ItemsSource is IEnumerable<LiveryGroup> currentGroups)
        {
            foreach (var group in currentGroups)
            {
                group.Key = group.SpecialKind switch
                {
                    LiveryGroupSpecialKind.DownloadMonth when group.SpecialMonth is { } month =>
                        month.ToString(AppLocalisationService.MonthYearFormat, AppLocalisationService.Culture),
                    LiveryGroupSpecialKind.UnknownDownloadDate => Strings.UnknownDownloadDate,
                    LiveryGroupSpecialKind.UnknownManufacturer => Strings.UnknownManufacturer,
                    LiveryGroupSpecialKind.AllLiveries => Strings.AllLiveriesGroupName,
                    _ when group.IsFavoritesGroup => Strings.SeparateFavoritesGroupName,
                    _ when group.IsMineGroup => Strings.SeparateMineGroupName,
                    _ => group.Key
                };
            }
        }

        if (_updateController.LatestVersion is not null)
            UpdateBannerText.Text = string.Format(Strings.UpdateAvailableFormat, _updateController.LatestVersion);
    }
}
