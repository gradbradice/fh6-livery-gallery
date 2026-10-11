using System.Reflection;
using LiveryGallery.Viewer3D;

namespace LiveryGallery.Services;

/// <summary>Versions of the packages the app is built with, read from their assemblies</summary>
internal static class ComponentVersions
{
    /// <summary>The open ForzaToolkit packages the gallery uses, with the game's package of the profile, by name</summary>
    public static IReadOnlyList<ComponentVersion> OpenPackages(GameProfile profile) =>
    [
        .. new[]
        {
            typeof(global::ForzaToolkit.Core.AppVersion),
            typeof(global::ForzaToolkit.Games.ForzaGame),
            profile.Liveries.GetType(), // the game's package: ForzaToolkit.Games.FH6 for FH6
            typeof(global::ForzaToolkit.Saves.SaveFolderPointers),
            typeof(global::ForzaToolkit.Liveries.LiveryData),
            typeof(global::ForzaToolkit.Liveries.Server.LiveryServerConnection),
            typeof(global::ForzaToolkit.CarDatabase.CarDatabaseService),
            typeof(global::ForzaToolkit.StringTables.StringTable),
            typeof(global::ForzaToolkit.GameInput.GameNavigator),
            typeof(global::ForzaToolkit.UI.WindowExtensions),
        }
        .Select(Of)
        .DistinctBy(c => c.Name, StringComparer.Ordinal)
        .OrderBy(c => c.Name, StringComparer.Ordinal),
    ];

    /// <summary>The package that holds <paramref name="anchor"/>: its assembly name and version</summary>
    public static ComponentVersion Of(Type anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        var assembly = anchor.Assembly;
        return new ComponentVersion(assembly.GetName().Name ?? anchor.Namespace ?? "?", PackageVersion(assembly));
    }

    /// <summary>The package version (informational version without the commit hash), or "?"</summary>
    public static string PackageVersion(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        try
        {
            string? version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString();
            if (version is null) return "?";
            int plus = version.IndexOf('+');
            return plus >= 0 ? version[..plus] : version;
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to read the package version of {assembly.GetName().Name}", ex);
            return "?";
        }
    }
}
