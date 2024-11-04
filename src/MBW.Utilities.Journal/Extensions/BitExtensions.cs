using System.Runtime.InteropServices;

namespace MBW.Utilities.Journal.Extensions;

internal static class BitExtensions
{
    public static Span<byte> AsSpan<T>(this ref T val) where T : unmanaged
    {
        Span<T> valSpan = MemoryMarshal.CreateSpan(ref val, 1);
        return MemoryMarshal.Cast<T, byte>(valSpan);
    }
}