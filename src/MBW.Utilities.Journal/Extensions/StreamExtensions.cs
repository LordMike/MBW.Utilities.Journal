using System.Runtime.InteropServices;

namespace MBW.Utilities.Journal.Extensions;

internal static class StreamExtensions
{
    public static ref T ReadOne<T>(this Stream stream, Span<byte> buffer) where T : unmanaged
    {
        stream.ReadExactly(buffer);
        return ref MemoryMarshal.AsRef<T>(buffer);
    }

    public static ref T ReadOneIfEnough<T>(this Stream stream, Span<byte> buffer, out bool success) where T : unmanaged
    {
        Span<byte> remaining = buffer;
        while (remaining.Length > 0)
        {
            int read = stream.Read(remaining);
            if (read == 0)
                break;

            remaining = remaining[read..];
        }

        // If we read all through, we report it
        success = remaining.Length <= 0;

        return ref MemoryMarshal.AsRef<T>(buffer);
    }
}