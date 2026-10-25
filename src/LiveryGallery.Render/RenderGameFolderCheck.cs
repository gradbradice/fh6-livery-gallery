using ForzaToolkit.Games.Installation;
using ForzaToolkit.LiveryRender.Assets;
using LiveryGallery.Viewer3D;

namespace LiveryGallery.Render;

/// <summary>
/// <see cref="IGameFolderCheck"/> through LiveryRender's <see cref="RenderGameFolder.Check"/>: the open check of the
/// folder (ForzaToolkit.Games) and, on top, whether a car model can be read
/// </summary>
internal sealed class RenderGameFolderCheck : IGameFolderCheck
{
    public async Task<GameFolderReport> CheckAsync(string? path)
    {
        try
        {
            return await RenderGameFolder.CheckAsync(path);
        }
        catch (Exception ex)
        {
            AppLog.LogError($"Game folder check failed for '{path}'", ex);
            return new GameFolderReport
            {
                InputPath = path,
                Findings = [new GameFolderFinding(GameFolderIssueCode.NotFound, true, path)],
            };
        }
    }
}
