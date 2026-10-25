using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LiveryGallery.Services;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>The gallery's keys (<see cref="GalleryShortcuts"/>)</summary>
internal partial class MainWindow
{
    /// <summary>
    /// The window sees each key before the control in focus (tunnelling): a button that was clicked keeps the focus,
    /// and the space would press it again instead of opening the preview. A dialog of the gallery is modal, so its keys
    /// never come here, and a menu or flyout that is open gets its keys in its own popup
    /// </summary>
    private void InitializeShortcuts() => AddHandler(KeyDownEvent, OnShortcutKeyDown, RoutingStrategies.Tunnel);

    private void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || !_isLoaded) return;
        var focused = FocusManager?.GetFocusedElement();
        bool typing = focused is TextBox;
        if (GalleryShortcuts.Match(e.Key, e.PhysicalKey, e.KeyModifiers, typing) is { } action && RunShortcut(action, focused))
            e.Handled = true;
    }

    /// <summary>Does what the key asks; false when there is nothing to do, and the key goes on to the control in focus</summary>
    private bool RunShortcut(GalleryShortcut action, object? focused)
    {
        var gallery = _mainViewModel.Gallery;
        LiveryEntry? first = gallery.SelectedEntries.FirstOrDefault();
        LiveryEntry? single = gallery.SelectedCount == 1 ? first : null;
        switch (action)
        {
            case GalleryShortcut.Search:
                SearchBox.Focus(NavigationMethod.Tab);
                SearchBox.SelectAll();
                return true;
            case GalleryShortcut.Escape:
                return Escape(focused);
            case GalleryShortcut.SelectAll:
                gallery.SelectAllCommand.Execute(null);
                return true;
            case GalleryShortcut.Refresh:
                if (!_mainViewModel.Status.CanRefresh || !_mainViewModel.RefreshCommand.CanExecute(null)) return true;
                _mainViewModel.RefreshCommand.Execute(null);
                return true;

            // The context menu's commands: each works on every selected livery
            case GalleryShortcut.Favorite when first is not null:
                gallery.ToggleFavoritesCommand.Execute(first);
                return true;
            case GalleryShortcut.Lock when first is not null:
                gallery.ToggleLocksCommand.Execute(first);
                return true;
            case GalleryShortcut.Tags when first is not null:
                gallery.EditTagsCommand.Execute(first);
                return true;
            case GalleryShortcut.MoveToArchive when first is not null:
                if (_mainViewModel.MoveSelectedToArchiveCommand.CanExecute(null)) _mainViewModel.MoveSelectedToArchiveCommand.Execute(null);
                return true;

            // A card's buttons: one livery
            case GalleryShortcut.Preview when single is not null:
                gallery.ViewPreviewCommand.Execute(single);
                return true;
            case GalleryShortcut.View3D when single is not null:
                if (single.CanView3D) gallery.View3DCommand.Execute(single);
                return true;
            case GalleryShortcut.FindInGame when single is not null:
                if (single.ShowFindInGame && _mainViewModel.NavigateInGameCommand.CanExecute(single))
                    _mainViewModel.NavigateInGameCommand.Execute(single);
                return true;

            case GalleryShortcut.Settings:
                OpenSettingsMenuItem_Click(null, new RoutedEventArgs());
                return true;
            case GalleryShortcut.Archive:
                OpenArchiveMenuItem_Click(null, new RoutedEventArgs());
                return true;
            case GalleryShortcut.Backups:
                OpenBackupsMenuItem_Click(null, new RoutedEventArgs());
                return true;
            case GalleryShortcut.Statistics:
                StatsButton_Click(null, new RoutedEventArgs());
                return true;
            case GalleryShortcut.Authors:
                AuthorsButton_Click(null, new RoutedEventArgs());
                return true;
            case GalleryShortcut.ShortcutList:
                ShortcutsMenuItem_Click(null, new RoutedEventArgs());
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Esc in the search: clears it, and when it is empty, leaves it. Elsewhere it clears the selection. Esc in another
    /// text field is that field's
    /// </summary>
    private bool Escape(object? focused)
    {
        if (ReferenceEquals(focused, SearchBox))
        {
            if (!string.IsNullOrEmpty(SearchBox.Text)) SearchBox.Text = "";
            else GalleryScroll.Focus(); // the list takes the focus: the arrows and Page Down scroll it
            return true;
        }
        if (focused is TextBox || !_mainViewModel.Gallery.HasSelection) return false;
        _mainViewModel.Gallery.ClearSelectionCommand.Execute(null);
        return true;
    }

    private void ShortcutsMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        SettingsButton.Flyout?.Hide();
        RunUiTask(() => new ShortcutsDialog().ShowDialog(this), "keyboard shortcuts dialog");
    }
}
