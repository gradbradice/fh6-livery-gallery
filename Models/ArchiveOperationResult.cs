namespace LiveryGallery.Models;

internal sealed record ArchiveOperationResult(IReadOnlyList<string> Completed, IReadOnlyList<string> LeftoverPaths)
{
    public static ArchiveOperationResult Empty { get; } = new([], []);
}
