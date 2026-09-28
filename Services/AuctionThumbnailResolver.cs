using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LiveryGallery.Services;

internal sealed partial class AuctionThumbnailResolver
{
    public const string ManifestFileName = ".manifest";
    private const uint SupportedManifestVersion = 2;
    private const uint SupportedRegistryVersion = 1;
    private const int MaxRecords = 1_000_000;
    private const int MaxNameLength = 4096;
    private const int TokenSourceLength = 16;
    private const string CrockfordAlphabet = "0123456789abcdefghjkmnpqrstvwxyz";
    private static readonly TimeSpan DiscoveryRetryInterval = TimeSpan.FromMinutes(5);

    [GeneratedRegex(
        @"^(?<carId>\d{3,6})_(?<instance>[0-9a-f]{16})(?:bm\d+|u(?<token>[0-9a-z]{26}))_bigThumb\.webp$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LogicalNameRegex();

    private static readonly Lock _discoveryLock = new();
    private static string? _cacheDirectory;
    private static DateTime _lastDiscoveryAttemptUtc = DateTime.MinValue;

    private readonly Lock _indexLock = new();
    private Index? _index;
    private (string Path, long Length, DateTime LastWriteUtc)? _indexSignature;

    public static string? FindCacheDirectory()
    {
        lock (_discoveryLock)
        {
            if (_cacheDirectory is not null && Directory.Exists(_cacheDirectory)) return _cacheDirectory;

            _cacheDirectory = null;
            if (DateTime.UtcNow - _lastDiscoveryAttemptUtc < DiscoveryRetryInterval) return null;
            _lastDiscoveryAttemptUtc = DateTime.UtcNow;
            _cacheDirectory = DiscoverCacheDirectory();
            return _cacheDirectory;
        }
    }

    private static string? DiscoverCacheDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(localAppData)) return null;

        var candidates = new List<string>
        {
            Path.Combine(localAppData, "ForzaHorizon6", "LocalStorage_Cache", "CacheThumbnails"),
        };

        string packages = Path.Combine(localAppData, "Packages");
        try
        {
            if (Directory.Exists(packages))
            {
                foreach (string package in Directory.EnumerateDirectories(packages, "Microsoft.*"))
                    candidates.Add(Path.Combine(package, "LocalCache", "Local", "LocalStorage_Cache", "CacheThumbnails"));
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(packages, $"Failed to enumerate '{packages}' while looking for the thumbnail cache", ex);
        }

        string? best = null;
        DateTime bestWriteUtc = DateTime.MinValue;
        foreach (string candidate in candidates)
        {
            try
            {
                var manifest = new FileInfo(Path.Combine(candidate, ManifestFileName));
                if (manifest.Exists && manifest.LastWriteTimeUtc > bestWriteUtc)
                {
                    best = candidate;
                    bestWriteUtc = manifest.LastWriteTimeUtc;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // inaccessible candidate ignore
            }
        }
        return best;
    }

    public Index? LoadIndex()
    {
        string? directory = FindCacheDirectory();
        if (directory is null) return null;

        string manifestPath = Path.Combine(directory, ManifestFileName);
        (string Path, long Length, DateTime LastWriteUtc) signature;
        try
        {
            var info = new FileInfo(manifestPath);
            if (!info.Exists) return StaleIndexFor(manifestPath);
            signature = (manifestPath, info.Length, info.LastWriteTimeUtc);
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(manifestPath, $"Failed to stat '{manifestPath}'", ex);
            return StaleIndexFor(manifestPath);
        }

        lock (_indexLock)
        {
            if (_index is not null && _indexSignature == signature) return _index;
        }

        try
        {
            var parsed = ParseManifest(directory, ReadAllBytesShared(manifestPath));
            lock (_indexLock)
            {
                _index = parsed;
                _indexSignature = signature;
            }
            return parsed;
        }
        catch (Exception ex)
        {
            // The game may be rewriting the file right now. Keep using the previous copy
            AppLogger.LogErrorThrottled(manifestPath, $"Failed to read the thumbnail cache manifest '{manifestPath}'", ex);
            return StaleIndexFor(manifestPath);
        }
    }

    private Index? StaleIndexFor(string manifestPath)
    {
        lock (_indexLock)
        {
            return _indexSignature is { } sig && string.Equals(sig.Path, manifestPath, StringComparison.OrdinalIgnoreCase)
                ? _index
                : null;
        }
    }

    public static string? TryComputeToken(string headerPath)
    {
        try
        {
            byte[] header = ReadAllBytesShared(headerPath);
            return header.Length < TokenSourceLength
                ? null
                : EncodeCrockford(header.AsSpan(header.Length - TokenSourceLength));
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(headerPath, $"Failed to read '{headerPath}' for the auction preview lookup", ex);
            return null;
        }
    }

    internal static string EncodeCrockford(ReadOnlySpan<byte> raw)
    {
        var result = new StringBuilder((raw.Length * 8 + 4) / 5);
        int accumulator = 0;
        int bits = 0;
        foreach (byte value in raw)
        {
            accumulator = (accumulator << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                result.Append(CrockfordAlphabet[(accumulator >> bits) & 0x1F]);
                accumulator &= (1 << bits) - 1;
            }
        }
        if (bits > 0) result.Append(CrockfordAlphabet[(accumulator << (5 - bits)) & 0x1F]);
        return result.ToString();
    }

    private static byte[] ReadAllBytesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream(stream.CanSeek ? (int)Math.Min(stream.Length, int.MaxValue) : 0);
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static Index ParseManifest(string cacheDirectory, byte[] bytes)
    {
        ReadOnlySpan<byte> data = bytes;
        if (data.Length < 8) throw new InvalidDataException("The thumbnail cache manifest is too short");

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(data);
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        if (version != SupportedManifestVersion)
            throw new InvalidDataException($"Unsupported thumbnail cache manifest version {version}");
        if (count > MaxRecords)
            throw new InvalidDataException($"Implausible thumbnail cache manifest record count {count}");

        int offset = 8;
        var entries = new List<Entry>();
        for (uint i = 0; i < count; i++)
        {
            if (!TryReadName(data, ref offset, out string name) || offset + 16 > data.Length)
                throw new InvalidDataException("The thumbnail cache manifest is truncated");

            var guid = new Guid(data.Slice(offset, 16));
            offset += 16;

            if (!TryParseLogicalName(name, out int carId, out string instance, out string? token) || token is null) continue;
            entries.Add(new Entry(carId, instance, token, Path.Combine(cacheDirectory, guid.ToString("D") + ".webp")));
        }

        var registryKeys = new HashSet<(int CarId, string Instance)>();
        if (offset + 16 <= data.Length && BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]) == SupportedRegistryVersion)
        {
            uint registryCount = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 12)..]);
            int registryOffset = offset + 16;
            if (registryCount <= MaxRecords)
            {
                var keys = new HashSet<(int CarId, string Instance)>();
                bool complete = true;
                for (uint i = 0; i < registryCount; i++)
                {
                    if (!TryReadName(data, ref registryOffset, out string name))
                    {
                        complete = false;
                        break;
                    }
                    if (TryParseLogicalName(name, out int carId, out string instance, out _))
                        keys.Add((carId, instance));
                }
                if (complete) registryKeys = keys;
            }
        }

        return new Index(entries, registryKeys);
    }

    private static bool TryReadName(ReadOnlySpan<byte> data, ref int offset, out string name)
    {
        name = string.Empty;
        if (offset + 4 > data.Length) return false;
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        if (length == 0 || length > MaxNameLength || offset + 4L + length > data.Length) return false;
        name = Encoding.UTF8.GetString(data.Slice(offset + 4, (int)length));
        offset += 4 + (int)length;
        return true;
    }

    private static bool TryParseLogicalName(string name, out int carId, out string instance, out string? token)
    {
        carId = 0;
        instance = string.Empty;
        token = null;
        var match = LogicalNameRegex().Match(name);
        if (!match.Success) return false;
        if (!int.TryParse(match.Groups["carId"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out carId)) return false;
        instance = match.Groups["instance"].Value.ToLowerInvariant();
        if (match.Groups["token"].Success) token = match.Groups["token"].Value.ToLowerInvariant();
        return true;
    }

    internal sealed record Entry(int CarId, string Instance, string Token, string Path);

    internal sealed class Index
    {
        private readonly Dictionary<(int CarId, string Token), List<Entry>> _byCarAndToken = [];
        private readonly Dictionary<string, List<Entry>> _byToken = new(StringComparer.Ordinal);
        private readonly HashSet<(int CarId, string Instance)> _registryKeys;

        public Index(IEnumerable<Entry> entries, HashSet<(int CarId, string Instance)> registryKeys)
        {
            _registryKeys = registryKeys;
            foreach (var entry in entries)
            {
                if (!_byCarAndToken.TryGetValue((entry.CarId, entry.Token), out var sameCar))
                    _byCarAndToken[(entry.CarId, entry.Token)] = sameCar = [];
                sameCar.Add(entry);

                if (!_byToken.TryGetValue(entry.Token, out var sameToken))
                    _byToken[entry.Token] = sameToken = [];
                sameToken.Add(entry);
            }
        }

        public string? Resolve(int carId, string? token)
        {
            if (string.IsNullOrEmpty(token)) return null;

            if (_byCarAndToken.TryGetValue((carId, token), out var sameCar))
            {
                var existing = sameCar.Where(e => File.Exists(e.Path)).ToList();
                if (existing.Count == 1) return existing[0].Path;
                if (existing.Count > 1)
                {
                    // One design rendered for several cars in the garage. Prefer the one the game currently uses
                    var registered = existing.Where(e => _registryKeys.Contains((e.CarId, e.Instance))).ToList();
                    if (registered.Count == 1) return registered[0].Path;
                    return existing.MaxBy(e => SafeLastWriteUtc(e.Path))!.Path;
                }
            }

            // Shared design stored under another car. Only accept an unambiguous match
            if (_byToken.TryGetValue(token, out var sameToken))
            {
                var distinct = sameToken
                    .Where(e => File.Exists(e.Path))
                    .Select(e => e.Path)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (distinct.Count == 1) return distinct[0];
            }

            return null;
        }

        private static DateTime SafeLastWriteUtc(string path)
        {
            try { return File.GetLastWriteTimeUtc(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return DateTime.MinValue; }
        }
    }
}
