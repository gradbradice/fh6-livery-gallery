using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

/// <summary>What the 3D renderer knows about the game folder: the rows detector looks for menu mods there</summary>
internal readonly record struct GameFolderState(bool IsChecking, string? Directory);

/// <summary>"Find in game": the position badge on cards and moving the in-game My Designs cursor</summary>
internal sealed partial class MainViewModel
{
    private GridLayout? _gridLayout;
    private bool _isNavigating;

    /// <summary>The game's My Designs grid; null when the gallery cannot find liveries in this game</summary>
    private MyDesignsGrid? MyDesigns => savePathService.Profile.MyDesigns;
    private bool _selectionBlocksNavigation;

    public Func<GameFolderState>? GameFolderStateProvider { get; set; }

    public string? NavigateInGameMenuBlockedTooltip =>
        Gallery.SelectedCount > 1 ? Strings.NavigateInGameMultipleSelectedTooltip : null;

    private void InitializeNavigation()
    {
        Gallery.SelectedEntries.CollectionChanged += (_, __) => OnSelectionChangedForNavigation();
    }

    /// <summary>
    /// With several liveries selected the target is ambiguous, so the card button and the menu item are
    /// both disabled. Entries are only touched when the selection crosses that line, not on every click.
    /// </summary>
    private void OnSelectionChangedForNavigation()
    {
        bool blocks = Gallery.SelectedCount > 1;
        if (blocks != _selectionBlocksNavigation)
        {
            _selectionBlocksNavigation = blocks;
            foreach (var entry in Gallery.AllEntries) entry.SelectionBlocksNavigation = blocks;
        }
        NotifyNavigationCommands();
    }

    private void NotifyNavigationCommands()
    {
        NavigateInGameCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(NavigateInGameMenuBlockedTooltip));
    }

    private bool CanNavigateInGame(LiveryEntry? entry) => !_isNavigating && entry is { CanNavigateInGame: true };

    [RelayCommand(CanExecute = nameof(CanNavigateInGame))]
    private Task NavigateInGameAsync(LiveryEntry? entry) => RunNavigationAsync(entry);

    /// <summary>Number of rows to use, or null when it cannot be told (the user has to choose in Settings)</summary>
    private (int? Rows, GridRows? Detection, bool GameFolderChecking) ResolveRows()
    {
        if (MyDesigns is not { } grid) return (null, null, false);
        switch (settings.MyDesignsRowsMode)
        {
            case MyDesignsRowsMode.Two: return (2, null, false);
            case MyDesignsRowsMode.Three: return (3, null, false);
        }

        var folder = GameFolderStateProvider?.Invoke() ?? new GameFolderState(false, null);
        if (folder.IsChecking) return (null, null, true);

        var detection = grid.DetectRows(folder.Directory);
        // Without a game folder a menu mod cannot be seen: the game's own menu is assumed
        int? rows = detection.Source == GridRowsSource.GameFolderUnknown ? grid.DefaultRows : detection.Rows;
        return (rows, detection, false);
    }

    /// <summary>Recomputes the badges: after a scan, a settings change or a game folder change</summary>
    /// <param name="refreshGallery">False when the caller rebuilds the gallery right after anyway</param>
    public void RefreshGamePositions(bool refreshGallery = true)
    {
        GridLayout? layout = null;
        string? saveDataPath = savePathService.ResolveSaveDataPath();
        var (rows, _, _) = ResolveRows();
        if (MyDesigns is { } grid && saveDataPath is not null && rows is { } rowCount)
        {
            try
            {
                layout = grid.Layout(saveDataPath, rowCount);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLogger.LogErrorThrottled(saveDataPath + "|grid", $"Failed to list '{saveDataPath}' for the My Designs grid", ex);
            }
        }
        ApplyGamePositions(layout, refreshGallery);
    }

    private void ApplyGamePositions(GridLayout? layout, bool refreshGallery)
    {
        bool changed = _gridLayout?.Signature != layout?.Signature;
        _gridLayout = layout;
        foreach (var entry in Gallery.AllEntries)
        {
            entry.SelectionBlocksNavigation = _selectionBlocksNavigation; // entries may be new after a scan
            entry.GamePosition = layout is not null && !entry.IsAuction && layout.TryGetSlot(entry.FolderName, out var slot)
                ? slot
                : null;
        }
        NotifyNavigationCommands();
        if (refreshGallery && changed && FilterBar.SortMode == SortMode.GameOrder) RefreshGallery();
    }

    private async Task RunNavigationAsync(LiveryEntry? entry)
    {
        if (entry is null || _isNavigating) return;
        _isNavigating = true;
        NotifyNavigationCommands();
        try
        {
            await NavigateCoreAsync(entry);
        }
        catch (OperationCanceledException) when (ShutdownToken.IsCancellationRequested)
        {
            // Shutting down
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"In-game navigation to '{entry.FolderName}' failed", ex);
            ErrorMessageRequested?.Invoke(Strings.NavigateFailedMessage);
        }
        finally
        {
            _isNavigating = false;
            NotifyNavigationCommands();
        }
    }

    private async Task NavigateCoreAsync(LiveryEntry entry)
    {
        if (MyDesigns is not { } grid) return; // the game has no My Designs grid: nothing offers this
        if (entry.IsAuction)
        {
            ErrorMessageRequested?.Invoke(Strings.NavigateInGameAuctionTooltip);
            return;
        }

        string? saveDataPath = savePathService.ResolveSaveDataPath();
        if (saveDataPath is null)
        {
            ErrorMessageRequested?.Invoke(Strings.NavigateNoSavePathMessage);
            return;
        }

        var (rows, detection, gameFolderChecking) = ResolveRows();
        if (gameFolderChecking)
        {
            ErrorMessageRequested?.Invoke(Strings.NavigateGameFolderCheckingMessage);
            return;
        }
        if (rows is not { } rowCount)
        {
            ErrorMessageRequested?.Invoke(Strings.NavigateRowsUnknownMessage);
            return;
        }

        // A fresh listing: the game may have saved a download or a deletion since the last scan
        GridLayout layout;
        try
        {
            layout = await Task.Run(() => grid.Layout(saveDataPath, rowCount), ShutdownToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.LogError($"In-game navigation: failed to list '{saveDataPath}'", ex);
            ErrorMessageRequested?.Invoke(Strings.NavigateSaveReadFailedMessage);
            return;
        }
        if (_gridLayout?.Signature != layout.Signature) ApplyGamePositions(layout, refreshGallery: true);

        if (!layout.TryGetSlot(entry.FolderName, out var slot))
        {
            ErrorMessageRequested?.Invoke(Strings.NavigateLiveryNotInSaveMessage);
            return;
        }

        var plan = NavigationPlanner.FromOrigin(slot);
        var request = new NavigationRequest(
            plan,
            NavigationSettings.TimingFrom(settings),
            GameWindowTarget.For(savePathService.Profile.Game),
            detection is { Source: GridRowsSource.Mod } ? detection.ModWrittenUtc : null,
            settings.NavigationReopenMenu);

        string target = slot.Label;
        bool finished = false;
        Status.SetStatusText(string.Format(Strings.NavigateProgressFormat, target, 0, plan.TotalPresses));
        var progress = new Progress<int>(sent =>
        {
            // Progress is posted to the UI thread; a late report must not overwrite the final text
            if (!finished) Status.SetStatusText(string.Format(Strings.NavigateProgressFormat, target, sent, plan.TotalPresses));
        });

        NavigationResult result;
        try
        {
            result = await GameNavigator.NavigateAsync(request, progress, ShutdownToken);
        }
        finally
        {
            finished = true;
        }
        ReportNavigationResult(result, target);
    }

    private void ReportNavigationResult(NavigationResult result, string target)
    {
        string? status = result.Outcome switch
        {
            NavigationOutcome.Completed => result.CursorParked
                ? string.Format(Strings.NavigateCompletedFormat, target) + " " + Strings.NavigateCursorParkedNote
                : string.Format(Strings.NavigateCompletedFormat, target),
            NavigationOutcome.CancelledByUser => string.Format(Strings.NavigateCancelledByUserFormat, result.KeysSent, result.KeysPlanned),
            NavigationOutcome.FocusLost => string.Format(Strings.NavigateFocusLostFormat, result.KeysSent, result.KeysPlanned),
            NavigationOutcome.MouseMoved => string.Format(Strings.NavigateMouseMovedFormat, result.KeysSent, result.KeysPlanned),
            _ => null,
        };
        if (status is not null)
        {
            Status.SetStatusText(status);
            return;
        }

        string? error = result.Outcome switch
        {
            NavigationOutcome.GameNotFound => string.Format(Strings.NavigateGameNotFoundFormat, savePathService.Profile.Game.Name),
            NavigationOutcome.MultipleGameWindows => string.Format(Strings.NavigateMultipleWindowsFormat, savePathService.Profile.Game.Name),
            NavigationOutcome.ActivationFailed => Strings.NavigateActivationFailedMessage,
            NavigationOutcome.CursorParkFailed => Strings.NavigateCursorParkFailedMessage,
            NavigationOutcome.InputRejected => Strings.NavigateInputRejectedMessage,
            NavigationOutcome.GameElevated => string.Format(Strings.NavigateGameElevatedFormat, savePathService.Profile.Game.Name),
            NavigationOutcome.LayoutChangedAfterGameStart => Strings.NavigateSceneChangedMessage,
            _ => null, // Cancelled: the app is closing
        };
        Status.SetStatusText(string.Empty);
        if (error is not null) ErrorMessageRequested?.Invoke(error);
    }
}
