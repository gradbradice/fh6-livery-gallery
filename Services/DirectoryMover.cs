using LiveryGallery.Enums;

namespace LiveryGallery.Services;

internal static class DirectoryMover
{
    public static MoveOutcome Move(string source, string destination, out string? leftoverPath)
    {
        leftoverPath = null;
        if (!Directory.Exists(source)) return MoveOutcome.Failed;
        if (Directory.Exists(destination))
        {
            AppLogger.LogError($"Cannot move '{source}' to '{destination}' — destination already exists",
                new IOException("Destination already exists"));
            return MoveOutcome.Failed;
        }

        try
        {
            Directory.Move(source, destination);
            return MoveOutcome.Moved;
        }
        catch (IOException)
        {
            // Different volume or a file inside is in use. Copy + delete instead
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to move '{source}' to '{destination}'", ex);
            return MoveOutcome.Failed;
        }

        try
        {
            CopyDirectoryRecursive(source, destination);
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to copy '{source}' to '{destination}'", ex);
            BestEffortIo.TryDeleteDirectory(destination, "Rolling back an incomplete copy");
            return MoveOutcome.Failed;
        }

        try
        {
            Directory.Delete(source, recursive: true);
            return MoveOutcome.Moved;
        }
        catch (Exception deleteError)
        {
            return ResolveFailedSourceDelete(source, destination, deleteError, out leftoverPath);
        }
    }

    private static MoveOutcome ResolveFailedSourceDelete(
        string source, string destination, Exception deleteError, out string? leftoverPath)
    {
        if (!HasAllFilesOf(folder: source, reference: destination))
        {
            AppLogger.LogError(
                $"Copied '{source}' to '{destination}', but deleting the source failed halfway — the copy is " +
                "complete and is used from now on; the rest of the source remains on disk", deleteError);
            leftoverPath = source;
            return MoveOutcome.CopiedButSourceRemains;
        }

        if (BestEffortIo.TryDeleteDirectory(destination, "Undoing a copy whose source could not be removed"))
        {
            AppLogger.LogError(
                $"Copied '{source}' to '{destination}', but could not delete the source (still complete) — " +
                "undid the copy, the folder stays where it was", deleteError);
            leftoverPath = null;
            return MoveOutcome.Failed;
        }

        if (HaveSameContent(source, destination))
        {
            AppLogger.LogError(
                $"'{source}' and '{destination}' could both not be removed — two identical copies exist; " +
                "the new one is used, the old one remains on disk", deleteError);
            leftoverPath = source;
            return MoveOutcome.CopiedButSourceRemains;
        }

        AppLogger.LogError(
            $"Could not delete '{source}', and undoing the copy left '{destination}' behind, which differs from the " +
            "source — the folder stays where it was; the copy remains on disk", deleteError);
        leftoverPath = destination;
        return MoveOutcome.Failed;
    }

    internal static bool HasAllFilesOf(string folder, string reference)
    {
        try
        {
            if (!Directory.Exists(folder)) return false;
            foreach (string file in Directory.EnumerateFiles(reference, "*", SearchOption.AllDirectories))
            {
                if (!File.Exists(Path.Combine(folder, Path.GetRelativePath(reference, file)))) return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(folder, $"Failed to compare '{folder}' with '{reference}'", ex);
            return false; // unknown treat the source as damaged, so the complete copy is kept
        }
    }

    internal static bool HaveSameContent(string first, string second)
    {
        try
        {
            if (!Directory.Exists(first) || !Directory.Exists(second)) return false;
            var firstFiles = Directory.EnumerateFiles(first, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(first, f))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var secondFiles = Directory.EnumerateFiles(second, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(second, f))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!firstFiles.SetEquals(secondFiles)) return false;

            foreach (string relative in firstFiles)
            {
                if (!FilesEqual(Path.Combine(first, relative), Path.Combine(second, relative))) return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(first, $"Failed to compare '{first}' with '{second}'", ex);
            return false; // unknown nnot identical
        }
    }

    private static bool FilesEqual(string a, string b)
    {
        using var streamA = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var streamB = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (streamA.Length != streamB.Length) return false;

        var bufferA = new byte[81920];
        var bufferB = new byte[81920];
        while (true)
        {
            int readA = streamA.ReadAtLeast(bufferA, bufferA.Length, throwOnEndOfStream: false);
            int readB = streamB.ReadAtLeast(bufferB, bufferB.Length, throwOnEndOfStream: false);
            if (readA != readB) return false;
            if (readA == 0) return true;
            if (!bufferA.AsSpan(0, readA).SequenceEqual(bufferB.AsSpan(0, readB))) return false;
        }
    }

    private static void CopyDirectoryRecursive(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (string dir in Directory.GetDirectories(source))
            CopyDirectoryRecursive(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }
}
