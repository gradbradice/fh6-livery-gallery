namespace LiveryGallery.Models;

internal record AuthorCard
{
    public required string Id { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public string? CustomName { get; init; }
    public string? Description { get; init; }
    public string? TwitterUrl { get; init; }
    public string? YouTubeUrl { get; init; }

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(CustomName) ? CustomName
        : Aliases.Count > 0 ? Aliases[0]
        : "?";
}
