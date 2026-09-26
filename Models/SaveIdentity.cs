namespace LiveryGallery.Models;

internal sealed record SaveIdentity
{
    public required ulong UserId { get; init; }
    public required string GameId { get; init; }
    public required SaveIdentitySource Source { get; init; }
    public required bool ConfirmationFileFound { get; init; }
    public required bool FolderNameAgreesWithManifest { get; init; }
}

internal enum SaveIdentitySource
{
    ManifestJson,
    FolderNameFallback,
}
