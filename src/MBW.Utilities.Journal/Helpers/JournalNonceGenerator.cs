using System.Buffers.Binary;
using System.Security.Cryptography;

namespace MBW.Utilities.Journal.Helpers;

/// <summary>
/// Generates journal header nonces from one process-wide sequence so a nonce is never reused within the same
/// application instance.
/// </summary>
/// <remarks>
/// The sequence starts at a cryptographically random value to make overlap with journals created by another process
/// extremely unlikely. Atomic increments make allocation safe across concurrent callers; the entire 64-bit sequence
/// would have to wrap before an in-process value could be reused.
/// </remarks>
internal static class JournalNonceGenerator
{
    private static long _next = CreateSeed();

    /// <summary>
    /// Allocates the next process-unique journal nonce.
    /// </summary>
    /// <returns>A nonce that has not previously been returned within this application instance.</returns>
    internal static ulong Next() => unchecked((ulong)Interlocked.Increment(ref _next));

    /// <summary>
    /// Creates the random starting point for the process-wide sequence.
    /// </summary>
    private static long CreateSeed()
    {
        Span<byte> seed = stackalloc byte[sizeof(long)];
        RandomNumberGenerator.Fill(seed);
        return BinaryPrimitives.ReadInt64LittleEndian(seed);
    }
}
