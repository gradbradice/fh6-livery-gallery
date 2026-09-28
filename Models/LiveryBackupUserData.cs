namespace LiveryGallery.Models;

internal sealed record LiveryBackupUserData
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public Dictionary<string, LiveryBackupUserDataEntry> Liveries { get; init; } = [];
}

internal sealed record LiveryBackupUserDataEntry
{
    public bool IsFavorite { get; init; }
    public List<string>? Tags { get; init; }
}
