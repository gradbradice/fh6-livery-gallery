namespace LiveryGallery.Configuration;

/// <summary>
/// Who the program is: its name, folders, User-Agent and where it is published. The gallery (LiveryGallery.Avalonia)
/// has no name of its own, like it knows no game of its own (<c>GameProfile</c>); the host program sets this once
/// at start with <see cref="AppSettings.Initialize"/>.
/// </summary>
internal sealed record AppIdentity
{
    /// <summary>The program's name: window title, About, the diagnostics README (<c>FH6 Livery Gallery</c>)</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The folder of the program's data in %APPDATA% and of its caches in %LOCALAPPDATA%, and the start of the
    /// diagnostics archive's name (<c>FH6LiveryGallery</c>). Changing it moves nothing: the program starts empty
    /// </summary>
    public required string FolderName { get; init; }

    /// <summary>The product token of the User-Agent, before the version (<c>FH6-Livery-Gallery</c>)</summary>
    public required string UserAgentProduct { get; init; }

    /// <summary>The prefix of the environment variables the program reads (<c>FH6_LIVERY_GALLERY</c>)</summary>
    public required string EnvironmentPrefix { get; init; }

    /// <summary>The GitHub repository, <c>owner/name</c>: the link in About and Contacts, and the releases the update check reads</summary>
    public required string GitHubRepository { get; init; }

    /// <summary>The author's page on X shown in Contacts, or null to hide the line</summary>
    public string? XUrl { get; init; }

    /// <summary>How the X link reads (<c>@name</c>)</summary>
    public string? XLabel { get; init; }

    /// <summary>The author's e-mail shown in Contacts, or null to hide the line</summary>
    public string? Email { get; init; }

    /// <summary>The repository's page</summary>
    public string GitHubUrl => $"https://github.com/{GitHubRepository}";

    /// <summary>The latest release in GitHub's API</summary>
    public string LatestReleaseApiUrl => $"https://api.github.com/repos/{GitHubRepository}/releases/latest";
}
