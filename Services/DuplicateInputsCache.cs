using ForzaToolkit.Formats;
using LiveryGallery.Configuration;

namespace LiveryGallery.Services;

internal static class DuplicateInputsCache
{
    private const uint Magic = 0x49444846; // "FHDI"
    private const int FormatVersion = 1;
    private const int MaxSectionCount = 4096;
    private const int MaxShapeCount = 1_000_000;
    private const long MaxFileBytes = 256L * 1024 * 1024;

    private static readonly string _directory = Path.Combine(AppSettings.BaseCachePath, "duplicate-inputs");
    private static readonly bool _isSupported = BlittableArray.IsSupported<LiveryShapeFingerprint>();
    private static readonly string _signature = string.Join("|",
        LiveryReader.ParserVersion,
        LiveryDuplicateDetector.AlgorithmVersion,
        typeof(LiveryShapeFingerprint).Module.ModuleVersionId,
        BlittableArray.ElementSize<LiveryShapeFingerprint>());
    private static int _unsupportedLogged;

    public static bool TryLoad(string? cLiveryHash, out uint[]? sectionCounts, out IReadOnlyList<LiveryShapeFingerprint>? shapes)
    {
        sectionCounts = null;
        shapes = null;
        if (!IsUsable(cLiveryHash)) return false;

        string path = PathFor(cLiveryHash!);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileBytes) return false;

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != FormatVersion) return false;
            if (reader.ReadString() != _signature) return false;

            sectionCounts = reader.ReadBoolean() ? BlittableArray.Read<uint>(reader, MaxSectionCount) : null;
            shapes = reader.ReadBoolean() ? BlittableArray.Read<LiveryShapeFingerprint>(reader, MaxShapeCount) : null;
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(path, $"Failed to read cached duplicate inputs '{path}' — re-reading the livery", ex);
            TryDelete(path);
            sectionCounts = null;
            shapes = null;
            return false;
        }
    }

    public static void Save(string? cLiveryHash, uint[]? sectionCounts, IReadOnlyList<LiveryShapeFingerprint>? shapes)
    {
        if (!IsUsable(cLiveryHash)) return;

        string path = PathFor(cLiveryHash!);
        try
        {
            Directory.CreateDirectory(_directory);
            AtomicFile.WriteViaStream(path, stream =>
            {
                using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
                writer.Write(Magic);
                writer.Write(FormatVersion);
                writer.Write(_signature);
                writer.Write(sectionCounts is not null);
                if (sectionCounts is not null) BlittableArray.Write(writer, sectionCounts);
                writer.Write(shapes is not null);
                if (shapes is not null) BlittableArray.Write(writer, shapes);
            });
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(_directory, $"Failed to cache duplicate inputs '{path}'", ex);
        }
    }

    public static void RemoveUnreferenced(IEnumerable<string?> referencedHashes)
    {
        if (!_isSupported || !Directory.Exists(_directory)) return;
        try
        {
            var referenced = referencedHashes.OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.EnumerateFiles(_directory))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (file.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) && referenced.Contains(name)) continue;
                TryDelete(file);
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(_directory, "Failed to clean up cached duplicate inputs", ex);
        }
    }

    private static bool IsUsable(string? cLiveryHash)
    {
        if (!_isSupported)
        {
            if (Interlocked.Exchange(ref _unsupportedLogged, 1) == 0)
                AppLogger.LogError("Duplicate inputs cache disabled: the shape fingerprint type contains references",
                    new NotSupportedException(typeof(LiveryShapeFingerprint).FullName));
            return false;
        }
        return cLiveryHash is { Length: 64 } && cLiveryHash.All(char.IsAsciiHexDigit);
    }

    private static string PathFor(string cLiveryHash) =>
        Path.Combine(_directory, cLiveryHash.ToLowerInvariant() + ".bin");

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {  }
    }
}
