using LiveryGallery.Configuration;

namespace LiveryGallery;

/// <summary>
/// This program, FH6 Livery Gallery: the names the open gallery (LiveryGallery.Avalonia) takes from the host. The game
/// it shows is <see cref="GameProfiles.Fh6"/>; another program (an FH4 gallery) has its own identity here.
/// </summary>
internal static class ProgramIdentity
{
    public static AppIdentity Fh6LiveryGallery { get; } = new()
    {
        Name = "FH6 Livery Gallery",
        // %APPDATA%\FH6LiveryGallery and %LOCALAPPDATA%\FH6LiveryGallery: the folders of every version so far
        FolderName = "FH6LiveryGallery",
        UserAgentProduct = "FH6-Livery-Gallery",
        EnvironmentPrefix = "FH6_LIVERY_GALLERY",
        GitHubRepository = "gradbradice/fh6-livery-gallery",
        XUrl = "https://x.com/bradice_livery",
        XLabel = "@bradice__livery",
        Email = "tbradice@proton.me",
    };
}
