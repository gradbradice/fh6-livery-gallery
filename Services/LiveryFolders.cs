namespace LiveryGallery.Services;

internal static class LiveryFolders
{
    public const string LiveryPrefix = "Livery_";
    public const string AuctionPrefix = "SoulBoundLivery_";

    public static bool IsAuction(string? folderName) =>
        folderName is not null && folderName.StartsWith(AuctionPrefix, StringComparison.Ordinal);

    public static bool IsLiveryContainerName(string? name) =>
        name is not null && (name.StartsWith(LiveryPrefix, StringComparison.Ordinal) || IsAuction(name));
}
