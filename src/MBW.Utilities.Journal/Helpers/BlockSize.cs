using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace MBW.Utilities.Journal.Helpers;

[DebuggerDisplay("BlockSize 2^{Power} => {Size}")]
internal readonly struct BlockSize
{
    public readonly byte Power;
    public readonly uint Size;
    public readonly ulong Mask;

    private BlockSize(byte power)
    {
        Power = power;
        Size = 1U << power;
        Mask = Size - 1;
    }

    public static BlockSize FromPowerOfTwo(byte power)
    {
        if (power > 31)
            throw new ArgumentOutOfRangeException(nameof(power), "Power must be at most 31.");

        return new BlockSize(power);
    }

    public static BlockSize FromSize(ushort size)
    {
        if (BitOperations.PopCount(size) != 1)
            throw new ArgumentOutOfRangeException(nameof(size), size, "Size must be a power of two.");

        byte power = 0;
        ushort tempSize = size;
        while (tempSize > 1)
        {
            tempSize >>= 1;
            power++;
        }

        return new BlockSize(power);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong RoundUpToNearestBlock(ulong value) => (value & Mask) == 0 ? value : RoundDownToNearestBlock(value + Size);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong RoundUpToNearestBlockMinimumOne(ulong value) => value == 0 ? Size : RoundUpToNearestBlock(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong RoundDownToNearestBlock(ulong value) => value & ~Mask;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong RoundDownToNearestBlockMinimumOne(ulong value) => value <= Size ? Size : RoundDownToNearestBlock(value);

    public uint GetBlockCountRoundUp(ulong size) => (uint)(RoundUpToNearestBlock(size) >> Power);
    public uint GetBlockCountRoundDown(ulong size) => (uint)(RoundDownToNearestBlock(size) >> Power);

    public bool IsAligned(ulong size) => (size & Mask) == 0;
}