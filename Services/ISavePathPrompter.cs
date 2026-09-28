namespace LiveryGallery.Services;

internal interface ISavePathPrompter
{
    Task<string?> PromptForSavePathAsync(bool initial, CancellationToken ct = default);
}