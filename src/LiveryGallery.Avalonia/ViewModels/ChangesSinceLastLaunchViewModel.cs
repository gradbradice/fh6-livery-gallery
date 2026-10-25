using LiveryGallery.Localisation;

namespace LiveryGallery.ViewModels;

internal sealed class ChangesSinceLastLaunchViewModel
{
    public ChangesSinceLastLaunchViewModel(LiveryChanges changes)
    {
        AddedRows = [.. changes.Added.Select(entry => new ChangedLiveryRowViewModel(entry, entry.ThumbnailPath, isRemoved: false))];
        RemovedRows = [.. changes.Removed.Select(r => new ChangedLiveryRowViewModel(r.Entry, r.PreviewPath, isRemoved: true))];
    }

    public IReadOnlyList<ChangedLiveryRowViewModel> AddedRows { get; }
    public IReadOnlyList<ChangedLiveryRowViewModel> RemovedRows { get; }

    public string SummaryText => string.Format(Strings.ChangesSummaryFormat, AddedRows.Count, RemovedRows.Count);
    public string AddedTabHeader => string.Format(Strings.ChangesAddedTabFormat, AddedRows.Count);
    public string RemovedTabHeader => string.Format(Strings.ChangesRemovedTabFormat, RemovedRows.Count);

    public bool ShowNoAddedHint => AddedRows.Count == 0;
    public bool ShowNoRemovedHint => RemovedRows.Count == 0;

    /// <summary>Opens on the removed tab when nothing was added</summary>
    public int InitialTabIndex => AddedRows.Count == 0 ? 1 : 0;
}
