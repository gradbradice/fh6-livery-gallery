using System.Collections.Concurrent;
using System.Globalization;
using LiveryGallery.Localisation;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Services;

/// <summary>What one author is in the shown save: the nicknames, how many liveries, the earliest one</summary>
/// <param name="Names">The nicknames seen, oldest first (the card's ones too)</param>
/// <param name="LiveryCount">How many liveries of the save are by it</param>
/// <param name="FirstSeen">When its earliest livery was downloaded; null when unknown</param>
internal sealed record AuthorDetails(IReadOnlyList<string> Names, int LiveryCount, DateTime? FirstSeen);

/// <summary>An account the card editor offers, as the user sees it</summary>
/// <param name="Option">The account</param>
/// <param name="Label">Its name, and what tells it from another account of the same name</param>
/// <param name="Details">How many liveries, and the earliest</param>
internal sealed record AuthorAccountChoice(AuthorAccountOption Option, string Label, string Details);

/// <summary>
/// Who the liveries of one save are by (<see cref="AuthorKeys"/>), built once per scan: the account whose save it is
/// (its liveries without an author id are its own, <see cref="OwnAccount"/>), each author's key, and a label for each
/// author that shares its name with another one (<see cref="AuthorLabels"/>)
/// </summary>
internal sealed class AuthorContext
{
    private readonly IReadOnlyDictionary<string, AuthorLabel> _labels;
    private readonly IReadOnlyDictionary<string, AuthorDetails> _details;

    public AuthorContext(OwnAccount? own, IReadOnlyDictionary<string, AuthorLabel> labels, IReadOnlyDictionary<string, AuthorDetails> details)
    {
        Own = own;
        _labels = labels;
        _details = details;
    }

    // One set of numbers per cards folder, shared by the gallery and the card editor
    private static readonly ConcurrentDictionary<string, AuthorNameNumbers> Numbers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where "Name (2)" is kept for the cards in <paramref name="cards"/>' folder</summary>
    public static AuthorNameNumbers NumbersFor(AuthorCardService cards) =>
        Numbers.GetOrAdd(cards.FolderPath, folder => new AuthorNameNumbers(folder));

    /// <summary>
    /// The accounts the card editor offers, named as the gallery names them: an account whose name another one shares
    /// gets the same label ("(formerly X)", "(2)", "(you)")
    /// </summary>
    /// <param name="options">The accounts (<see cref="AuthorCardService.BuildAccountIndex"/>)</param>
    /// <param name="entries">The shown liveries: the user's own account is the one of the MINE ones</param>
    /// <param name="cards">The author cards</param>
    public static IReadOnlyList<AuthorAccountChoice> LabelAccounts(
        IReadOnlyList<AuthorAccountOption> options, IEnumerable<LiveryEntry> entries, AuthorCardService cards)
    {
        var ownTags = entries.Where(e => e.IsMine).Select(e => e.AuthorAccountTag)
            .Where(AuthorAccounts.IsAccount).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        string NameOf(AuthorAccountOption o) =>
            o.Names.Count > 0 ? o.Names[^1] : o.Card?.KnownNames.LastOrDefault(n => !AuthorAccounts.IsBlankName(n)) ?? Strings.AuthorCardAccountWithoutName;
        var labels = AuthorLabels.Build(options.Select(o => new AuthorLabelSubject(
            AuthorKeys.ForAccount(o.IdentityTag), NameOf(o), o.Names, ownTags.Contains(o.IdentityTag), HasAccount: true, o.FirstSeen,
            o.Card is { IsImplicit: false } card ? [card.Id] : null)), NumbersFor(cards));
        return [.. options.Select(o =>
        {
            string key = AuthorKeys.ForAccount(o.IdentityTag);
            string label = labels.TryGetValue(key, out var l) ? Format(l) : NameOf(o);
            var parts = new List<string> { string.Format(AppLocalisationService.Culture, Strings.AuthorDetailsLiveriesFormat, o.LiveryCount) };
            if (o.FirstSeen is { } first)
                parts.Add(string.Format(AppLocalisationService.Culture, Strings.AuthorDetailsFirstFormat, first.ToLocalTime().ToString("d", AppLocalisationService.Culture)));
            if (o.Names.Count > 1)
                parts.Add(string.Format(AppLocalisationService.Culture, Strings.AuthorDetailsNamesFormat, string.Join(", ", o.Names)));
            return new AuthorAccountChoice(o, label, string.Join(" · ", parts));
        })];
    }

    /// <summary>No account and no labels: names are shown as they are</summary>
    public static AuthorContext Empty { get; } = new(null,
        new Dictionary<string, AuthorLabel>(), new Dictionary<string, AuthorDetails>());

    /// <summary>The account whose save it is; null when unknown</summary>
    public OwnAccount? Own { get; }

    /// <summary>What the duplicate check depends on besides the cards: which liveries without an id are the account's</summary>
    public string Signature => Own is null
        ? "authors2"
        : "authors2|" + Own.IdentityTag + "|" + string.Join(",", Own.Names.Select(n => n.ToLowerInvariant()).Order(StringComparer.Ordinal));

    /// <summary>The label of the author with this key; null when the author is not in the save</summary>
    public AuthorLabel? LabelOf(string? key) => key is not null && _labels.TryGetValue(key, out var label) ? label : null;

    /// <summary>The details of the author with this key; null when the author is not in the save</summary>
    public AuthorDetails? DetailsOf(string? key) => key is not null && _details.TryGetValue(key, out var details) ? details : null;

    /// <summary>How an author is shown: the name, and what tells it from another author of the same name</summary>
    public static string Format(AuthorLabel label) => label.Kind switch
    {
        AuthorLabelKind.Own => string.Format(AppLocalisationService.Culture, Strings.AuthorLabelOwnFormat, label.Name),
        AuthorLabelKind.FormerName => string.Format(AppLocalisationService.Culture, Strings.AuthorLabelFormerNameFormat, label.Name, label.FormerName),
        AuthorLabelKind.NoAccount => string.Format(AppLocalisationService.Culture, Strings.AuthorLabelNoAccountFormat, label.Name),
        AuthorLabelKind.Number => string.Format(AppLocalisationService.Culture, Strings.AuthorLabelNumberFormat, label.Name, label.Number),
        _ => label.Name,
    };

    /// <summary>The lines of a tooltip about an author: its nicknames, how many liveries, the earliest one</summary>
    public static string DescribeDetails(AuthorDetails details)
    {
        var lines = new List<string>();
        if (details.Names.Count > 0)
            lines.Add(string.Format(AppLocalisationService.Culture, Strings.AuthorDetailsNamesFormat, string.Join(", ", details.Names)));
        lines.Add(string.Format(AppLocalisationService.Culture, Strings.AuthorDetailsLiveriesFormat, details.LiveryCount));
        if (details.FirstSeen is { } first)
            lines.Add(string.Format(AppLocalisationService.Culture, Strings.AuthorDetailsFirstFormat, first.ToLocalTime().ToString("d", AppLocalisationService.Culture)));
        return string.Join("\n", lines);
    }
}
