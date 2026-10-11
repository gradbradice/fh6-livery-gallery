using ForzaToolkit.Liveries.Scanning;

namespace LiveryGallery.Services;

/// <summary>The game's WebP preview as the gallery's PNG (<see cref="ThumbnailService"/>)</summary>
internal sealed class ThumbnailPreviewWriter : ILiveryPreviewWriter
{
    public string FileName(string key, LiveryFiles files, string? sourceHash) =>
        ThumbnailService.ComputeDestinationFileName(key, sourceHash);

    public bool Write(string sourcePath, string destinationPath) =>
        ThumbnailService.GenerateAndSave(sourcePath, destinationPath);
}
