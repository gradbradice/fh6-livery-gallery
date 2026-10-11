namespace LiveryGallery.Services.Accounts;

/// <summary>What the settings dialog may do with the stored account data, without knowing the services behind it</summary>
internal sealed record AccountDataActions(Func<Task<List<StoredAccount>>> List, Func<string, Task<bool>> Forget);
