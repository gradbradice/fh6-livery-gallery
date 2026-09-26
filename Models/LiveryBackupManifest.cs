namespace LiveryGallery.Models;

internal sealed record LiveryBackupManifest
{
    public required DateTime CreatedAtUtc { get; init; }
    public required List<LiveryData> Entries { get; init; }
}