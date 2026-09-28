using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LiveryGallery.Services;

internal static class BlittableArray
{
    public static bool IsSupported<T>() => !RuntimeHelpers.IsReferenceOrContainsReferences<T>();

    public static int ElementSize<T>() => Unsafe.SizeOf<T>();

    public static void Write<T>(BinaryWriter writer, IReadOnlyList<T> items)
    {
        if (!IsSupported<T>()) throw new NotSupportedException($"{typeof(T)} contains references");
        T[] array = items as T[] ?? [.. items];
        writer.Write(array.Length);
        if (array.Length == 0) return;
        writer.Write(AsBytes(array));
    }

    public static T[] Read<T>(BinaryReader reader, int maxCount)
    {
        if (!IsSupported<T>()) throw new NotSupportedException($"{typeof(T)} contains references");
        int count = reader.ReadInt32();
        if (count < 0 || count > maxCount) throw new InvalidDataException($"Implausible element count {count}");
        var array = new T[count];
        if (count == 0) return array;
        var bytes = AsBytes(array);
        reader.BaseStream.ReadExactly(bytes);
        return array;
    }

    private static Span<byte> AsBytes<T>(T[] array) =>
        MemoryMarshal.CreateSpan(
            ref Unsafe.As<T, byte>(ref MemoryMarshal.GetArrayDataReference(array)),
            checked(array.Length * Unsafe.SizeOf<T>()));
}
