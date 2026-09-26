namespace LiveryGallery.Models;

internal sealed record ArchivedLiveryEntry
{
    public required LiveryData Data { get; init; }
    public required DateTime ArchivedAtUtc { get; init; }
}
