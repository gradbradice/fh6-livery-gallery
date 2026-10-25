using LiveryGallery.Localisation;

namespace LiveryGallery.Services.Accounts;

internal static class AccountLabels
{
    /// <summary>The last four characters of the key: the end of the XUID for an Xbox account</summary>
    public static string ShortId(string key) => AccountKey.ShortId(key);

    public static string Title(string key, string? displayName) =>
        !string.IsNullOrWhiteSpace(displayName) ? displayName.Trim() : string.Format(Strings.AccountUnnamedFormat, ShortId(key));
}

/// <summary>
/// What the archive and backup dialogs need to know about accounts: which one is open now, and how to name the
/// one an item came from (the switcher's current title if that account is known, otherwise what was stored).
/// </summary>
/// <param name="existingKeys">Accounts whose save folder is on this PC now; null means "assume all exist"</param>
internal sealed class AccountContext(
    AccountRef? current, IReadOnlyDictionary<string, string> knownTitles, IReadOnlySet<string>? existingKeys = null)
{
    public static AccountContext None { get; } = new(null, new Dictionary<string, string>());

    public AccountRef? Current => current;

    public string Title(AccountRef account) =>
        knownTitles.TryGetValue(account.Key, out var title) ? title : AccountLabels.Title(account.Key, account.DisplayName);

    public string CurrentTitle => current is null ? "" : Title(current);

    /// <summary>
    /// Known to be another account. An item of unknown origin (from before accounts were recorded) is not:
    /// asking about every old item would only teach the user to click through the question.
    /// </summary>
    public bool IsOther(AccountRef? source) =>
        source is not null && current is not null && !string.Equals(source.Key, current.Key, StringComparison.OrdinalIgnoreCase);

    /// <summary>Another account whose save folder is no longer on this PC</summary>
    public bool IsGone(AccountRef? source) =>
        IsOther(source) && existingKeys is not null && !existingKeys.Contains(source!.Key);

    /// <summary>
    /// What "only this account" hides: other accounts that still exist. Items of an account that is gone stay
    /// visible, or the only way back to them would be hidden behind the filter.
    /// </summary>
    public bool IsHiddenByAccountFilter(AccountRef? source) => IsOther(source) && !IsGone(source);

    /// <summary>The note shown on an item of another account; empty for the open account or unknown origin</summary>
    public string SourceNote(AccountRef? source) =>
        IsGone(source) ? string.Format(Strings.AccountSourceGoneFormat, Title(source!))
        : IsOther(source) ? string.Format(Strings.AccountSourceOtherFormat, Title(source!))
        : "";

    /// <summary>The question to ask before restoring items of other accounts into this one, or null when there are none</summary>
    public string? BuildRestoreIntoCurrentPrompt(IEnumerable<AccountRef?> sources)
    {
        var others = sources.Where(IsOther).Select(s => s!).ToList();
        if (others.Count == 0) return null;

        string names = string.Join(", ", others
            .GroupBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => Title(g.First())));
        return string.Format(Strings.RestoreFromOtherAccountMessage, others.Count, names, CurrentTitle);
    }
}
