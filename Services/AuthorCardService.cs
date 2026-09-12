using LiveryGallery.Configuration;
using LiveryGallery.Models;
using System.Text.Json;

namespace LiveryGallery.Services;

internal class AuthorCardService
{
    private readonly SaveService _saveService;
    private static readonly string _path = Path.Combine(AppSettings.BaseCachePath, "author_cards.json");
    private readonly Lock _lock = new();
    private List<AuthorCard> _cards = [];
    private Dictionary<string, AuthorCard> _aliasIndex = new(StringComparer.OrdinalIgnoreCase);

    public AuthorCardService()
    {
        _saveService = new();
        Load();
    }

    public void Flush() => _saveService.Flush();

    public IReadOnlyList<AuthorCard> GetAll()
    {
        lock (_lock) return [.. _cards];
    }

    public string ResolveDisplayName(string rawAuthor)
    {
        lock (_lock)
            return _aliasIndex.TryGetValue(rawAuthor, out var card) ? card.DisplayName : rawAuthor;
    }

    public AuthorCard? FindCardForAlias(string rawAuthor)
    {
        lock (_lock)
            return _aliasIndex.TryGetValue(rawAuthor, out var card) ? card : null;
    }

    public string? FindConflictingAlias(string? excludeCardId, IEnumerable<string> aliases)
    {
        lock (_lock)
        {
            foreach (var alias in aliases)
            {
                if (_aliasIndex.TryGetValue(alias, out var existing) && existing.Id != excludeCardId)
                    return alias;
            }
        }
        return null;
    }

    public AuthorCard? FindCardWithDisplayName(string? excludeCardId, string displayName)
    {
        lock (_lock)
            return _cards.FirstOrDefault(c =>
                c.Id != excludeCardId && string.Equals(c.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));
    }

    public bool TrySave(AuthorCard card)
    {
        lock (_lock)
        {
            if (FindConflictingAliasNoLock(card.Id, card.Aliases) is not null)
                return false;
            if (_cards.Any(c => c.Id != card.Id
                && string.Equals(c.DisplayName, card.DisplayName, StringComparison.OrdinalIgnoreCase)))
                return false;

            _cards.RemoveAll(c => c.Id == card.Id);
            _cards.Add(card);
            RebuildIndexNoLock();
        }
        Save();
        return true;
    }

    public void Delete(string cardId)
    {
        lock (_lock)
        {
            _cards.RemoveAll(c => c.Id == cardId);
            RebuildIndexNoLock();
        }
        Save();
    }

    private string? FindConflictingAliasNoLock(string? excludeCardId, IEnumerable<string> aliases)
    {
        foreach (var alias in aliases)
        {
            if (_aliasIndex.TryGetValue(alias, out var existing) && existing.Id != excludeCardId)
                return alias;
        }
        return null;
    }

    private void RebuildIndexNoLock()
    {
        var usedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validCards = new List<AuthorCard>(_cards.Count);
        var seenDisplayNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool anyConflictRemoved = false;

        foreach (var card in _cards)
        {
            List<string>? deduped = null;
            foreach (var alias in card.Aliases)
            {
                if (usedAliases.Contains(alias))
                {
                    anyConflictRemoved = true;
                    continue;
                }
                usedAliases.Add(alias);
                (deduped ??= []).Add(alias);
            }

            if (deduped is null || deduped.Count == 0)
            {
                anyConflictRemoved = true;
                continue;
            }

            var resolved = deduped.Count == card.Aliases.Count ? card : card with { Aliases = deduped };
            if (!seenDisplayNames.Add(resolved.DisplayName))
            {
                anyConflictRemoved = true;
                resolved = resolved with { CustomName = null };
                if (!seenDisplayNames.Add(resolved.DisplayName))
                {
                    continue;
                }
            }

            validCards.Add(resolved);
        }

        if (anyConflictRemoved)
        {
            AppLogger.LogError(
                "Author cards file had conflicting nicknames across cards, auto-resolved on load",
                new InvalidDataException("Duplicate alias across author cards"));
            _cards = validCards;
            Save();
        }

        var index = new Dictionary<string, AuthorCard>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in validCards)
            foreach (var alias in card.Aliases)
                index[alias] = card;
        _aliasIndex = index;
    }

    private void Save()
    {
        try
        {
            List<AuthorCard> snapshot;
            lock (_lock) snapshot = _cards;
            string json = JsonSerializer.Serialize(snapshot, JsonSettings.DefaultOptions);
            _saveService.ScheduleSave(json, _path);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to serialise author cards", ex);
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                string json = File.ReadAllText(_path);
                var data = JsonSerializer.Deserialize<List<AuthorCard>>(json, JsonSettings.DefaultOptions);
                if (data != null)
                {
                    _cards = data;
                    RebuildIndexNoLock();
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to load author cards", ex);
            AtomicFile.TryBackupCorruptedFile(_path);
        }
    }
}
