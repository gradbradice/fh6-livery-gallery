using ForzaToolkit.Liveries.History;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Services;

/// <summary>
/// What the statistics (ForzaToolkit.Liveries <c>Statistics</c>) need from the gallery: who a livery is by, the same way
/// for a shown livery and for a journal event, and the authors who share an artwork
/// </summary>
/// <param name="cards">The author cards</param>
/// <param name="own">The shown save's account: its liveries without an id in the journal are its own</param>
internal sealed class StatisticsSource(AuthorCardService cards, Func<OwnAccount?>? own = null)
{
    /// <summary>
    /// One key per author (<see cref="AuthorKeys"/>): the card the user assigned the livery to, else its account's card,
    /// else the account, else the name; null when nothing tells. Merged accounts share their card, so they count as one
    /// author; two accounts of one name are two
    /// </summary>
    public string? AuthorKey(LiveryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.AssignedCardId
            ?? AuthorKeys.Of(null, entry.AuthorIdentityTagHex, entry.Data.IsAuthorUnknown ? null : entry.Data.AuthorRaw, cards.FindCardForIdentityTag);
    }

    /// <summary>The same key for a journal event (its livery's assignment found by file hash)</summary>
    public string? AuthorKey(LiveryHistoryEvent historyEvent)
    {
        ArgumentNullException.ThrowIfNull(historyEvent);
        if (historyEvent.Hash is { } hash && cards.FindAssignment(hash) is { } assignment) return assignment.CardId;
        string? tag = own?.Invoke()?.AccountOf(historyEvent.AuthorTag, historyEvent.Author) ?? historyEvent.AuthorTag;
        return AuthorKeys.Of(null, tag, historyEvent.Author, cards.FindCardForIdentityTag);
    }

    /// <summary>Two authors (cards or accounts) with much the same artwork, closest first: a second account or a re-upload</summary>
    public IReadOnlyList<SharedArtwork> SharedArtworks(IReadOnlyList<LiveryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return ForzaToolkit.Liveries.Authors.ArtworkMatches.AcrossAuthors([.. entries.Select(e => AuthorInsights.ToInput(e, cards))]);
    }
}
