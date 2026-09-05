using System.Text;
using System.Xml.Linq;

namespace LiveryGallery.Services;

internal class GameDiscoveryServiceXbox
{
    private const string GameDisplayNameFragment = "Forza Horizon 6";
    // idk which of these options actually works or if the correct one is among them
    private static readonly string[] XboxIdentityNamePrefix =
    [
        "Microsoft.FH6",
        "Microsoft.ForzaHorizon6",
        "ForzaHorizon6",
    ];

    public static string? TryFindViaXbox()
    {
        try
        {
            foreach (string appFolder in EnumerateXboxAppFolders())
            {
                if (!Directory.Exists(appFolder)) continue;

                foreach (string subDir in Directory.EnumerateDirectories(appFolder))
                {
                    string manifestPath = Path.Combine(subDir, "appxmanifest.xml");
                    string installPath = subDir;

                    if (!File.Exists(manifestPath))
                    {
                        string contentManifest = Path.Combine(subDir, "Content", "appxmanifest.xml");
                        if (!File.Exists(contentManifest)) continue;
                        manifestPath = contentManifest;
                        installPath = Path.Combine(subDir, "Content");
                    }

                    var (displayName, identityName) = TryReadAppxIdentity(manifestPath);

                    bool matchesByDisplayName = displayName is not null
                        && displayName.Contains(GameDisplayNameFragment, StringComparison.OrdinalIgnoreCase);

                    bool matchesById = identityName is not null
                        && XboxIdentityNamePrefix.Any(prefix =>
                            identityName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

                    if (matchesByDisplayName || matchesById)
                        return installPath;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static IEnumerable<string> EnumerateXboxAppFolders()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            string root = drive.RootDirectory.FullName;

            string modifiableWindowsApps = Path.Combine(root, "Program Files", "ModifiableWindowsApps");
            if (Directory.Exists(modifiableWindowsApps))
                yield return modifiableWindowsApps;

            string gamingRootFile = Path.Combine(root, ".GamingRoot");
            if (!File.Exists(gamingRootFile)) continue;

            List<string> extra;
            try
            {
                extra = ParseGamingRootFile(gamingRootFile, root);
            }
            catch
            {
                continue;
            }

            foreach (var folder in extra) yield return folder;
        }
    }

    private static List<string> ParseGamingRootFile(string filePath, string driveRoot)
    {
        const uint expectedMagic = 0x58424752;

        using var stream = File.OpenRead(filePath);
        using var reader = new BinaryReader(stream, Encoding.Unicode);

        uint magic = reader.ReadUInt32();
        if (magic != expectedMagic)
            throw new InvalidDataException($"Invalid .GamingRoot signature: 0x{magic:x8}, expected 0x{expectedMagic:x8}");

        uint folderCount = reader.ReadUInt32();
        if (folderCount >= byte.MaxValue)
            throw new InvalidDataException($"Unexpectedly large number of folders in .GamingRoot: {folderCount}");

        var folders = new List<string>((int)folderCount);
        for (int i = 0; i < folderCount; i++)
        {
            var sb = new StringBuilder();
            char c = reader.ReadChar();
            while (c != '\0')
            {
                sb.Append(c);
                c = reader.ReadChar();
            }
            folders.Add(Path.Combine(driveRoot, sb.ToString()));
        }

        return folders;
    }

    /// <summary>
    /// DisplayName is a readable name, which may be localized for the system language
    /// IdentityName is a stable technical identifier, part of the Package Family Name
    /// independent of the language
    /// </summary>
    private static (string? DisplayName, string? IdentityName) TryReadAppxIdentity(string manifestPath)
    {
        try
        {
            var doc = XDocument.Load(manifestPath);
            // The namespace from the root element itself. Different versions
            // of appxmanifest.xml use different schema URIs
            XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;

            string? displayName = doc.Root?.Element(ns + "Properties")?.Element(ns + "DisplayName")?.Value;
            string? identityName = doc.Root?.Element(ns + "Identity")?.Attribute("Name")?.Value;

            return (displayName, identityName);
        }
        catch
        {
            return (null, null);
        }
    }
}
