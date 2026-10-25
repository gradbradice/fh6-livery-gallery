using Microsoft.Extensions.Logging;
using LiveryGallery.Configuration;

namespace LiveryGallery.Services;

/// <summary>
/// The scan cache of ForzaToolkit.Liveries with the gallery's folders: one cache per game account in
/// <c>accounts\&lt;key&gt;\cache.json</c>, the shared preview folder, and the cache from before account support.
/// </summary>
/// <param name="accountsRoot">Where the per-account folders go; tests pass a temporary folder</param>
/// <param name="legacyDirectory">Where the cache from before account support lives</param>
/// <param name="thumbsDirectory">The shared preview folder</param>
internal sealed class AppCacheService(string? accountsRoot = null, string? legacyDirectory = null, string? thumbsDirectory = null)
    : LiveryCacheStore(
        accountsRoot ?? AppSettings.AccountsPath,
        thumbsDirectory ?? AppSettings.ThumbsPath,
        legacyDirectory ?? AppSettings.BaseCachePath,
        logger: AppLog.LoggerFactory.CreateLogger<AppCacheService>());
