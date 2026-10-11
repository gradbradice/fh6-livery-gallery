namespace LiveryGallery.Services;

/// <summary>
/// Finds the profile save (C_ProfileData) of a save folder: what the livery server reads the installed liveries from.
/// Open: it only looks in the save, whichever client sends the file.
/// </summary>
/// <param name="saves">The game's save folders: whose save a folder is</param>
internal sealed class ProfileDataLocator(PcGameSaveStore saves)
{
    public const string ProfileDataFileName = "C_ProfileData";
    public const string ProfileUserFolderPrefix = "User_";

    // saveDataPath = ...\u_<decUserId>_<gameId>\<containerId>\ContainersRoot
    // Profile lives in ContainersRoot\User_<hexUserId>\C_ProfileData
    public string? FindProfileData(string? saveDataPath)
    {
        if (string.IsNullOrEmpty(saveDataPath) || !Directory.Exists(saveDataPath)) return null;
        try
        {
            if (ResolveUserId(saveDataPath) is not { } userId)
            {
                AppLogger.LogErrorThrottled($"{saveDataPath}|ProfileUserId",
                    $"Cannot determine the UserId for '{saveDataPath}', so {ProfileUserFolderPrefix}<hex> cannot be located",
                    new InvalidOperationException("ResolveSaveIdentity returned null"));
                return null;
            }

            string? userDir = PcGameSaveStore.FindUserFolder(saveDataPath, userId);
            if (userDir is null)
            {
                AppLogger.LogErrorThrottled($"{saveDataPath}|ProfileUserDir",
                    $"Folder '{ProfileUserFolderPrefix}{userId:X}' (UserId {userId}) was not found in '{saveDataPath}'",
                    new DirectoryNotFoundException(Path.Combine(saveDataPath, $"{ProfileUserFolderPrefix}{userId:X}")));
                return null;
            }

            var file = new FileInfo(Path.Combine(userDir, ProfileDataFileName));
            return file.Exists ? file.FullName : null;
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(saveDataPath, $"Failed to look for {ProfileDataFileName} in '{saveDataPath}'", ex);
            return null;
        }
    }

    private ulong? ResolveUserId(string saveDataPath)
    {
        var containerDir = Directory.GetParent(Path.TrimEndingDirectorySeparator(saveDataPath));
        var uFolder = containerDir?.Parent;
        if (containerDir is null || uFolder is null) return null;
        int? containerId = int.TryParse(containerDir.Name, out int id) ? id : null;
        return saves.ResolveIdentity(uFolder.FullName, containerId)?.UserId;
    }
}
