using System.Buffers.Binary;
using System.Security.Cryptography;

namespace MBW.Utilities.Journal.Helpers;

internal static class JournalNonceGenerator
{
    private static long _next = CreateSeed();

    internal static ulong Next() => unchecked((ulong)Interlocked.Increment(ref _next));

    private static long CreateSeed()
    {
        Span<byte> seed = stackalloc byte[sizeof(long)];
        RandomNumberGenerator.Fill(seed);
        return BinaryPrimitives.ReadInt64LittleEndian(seed);
    }
}
