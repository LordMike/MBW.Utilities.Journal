using MBW.Utilities.Journal.Primitives;

namespace MBW.Utilities.Journal.Tests;

public class BlockSizeTests
{
    [Fact]
    public void InvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BlockSize.FromPowerOfTwo(32));
        Assert.Throws<ArgumentOutOfRangeException>(() => BlockSize.FromSize(5000));
        Assert.Throws<ArgumentOutOfRangeException>(() => BlockSize.FromSize(65534));
    }

    [Fact]
    public void FromPowerOfTwo_ValidArguments()
    {
        var blockSize = BlockSize.FromPowerOfTwo(10);
        Assert.Equal(10, blockSize.Power);
        Assert.Equal(1024U, blockSize.Size);
        Assert.Equal(0x3FFUL, blockSize.Mask);

        blockSize = BlockSize.FromPowerOfTwo(2);
        Assert.Equal(2, blockSize.Power);
        Assert.Equal(4U, blockSize.Size);
        Assert.Equal(0x3UL, blockSize.Mask);

        blockSize = BlockSize.FromPowerOfTwo(16);
        Assert.Equal(16, blockSize.Power);
        Assert.Equal(65536U, blockSize.Size);
        Assert.Equal(0xFFFFUL, blockSize.Mask);
    }

    [Fact]
    public void FromSize_ValidArguments()
    {
        var blockSize = BlockSize.FromSize(4096);
        Assert.Equal(12, blockSize.Power);
        Assert.Equal(4096U, blockSize.Size);
        Assert.Equal(0xFFFUL, blockSize.Mask);
    }

    [Fact]
    public void RoundUpToNearestBlock()
    {
        var blockSize = BlockSize.FromPowerOfTwo(10);
        Assert.Equal(0UL, blockSize.RoundUpToNearestBlock(0));
        Assert.Equal(1024UL, blockSize.RoundUpToNearestBlock(1));
        Assert.Equal(1024UL, blockSize.RoundUpToNearestBlock(1023));
        Assert.Equal(1024UL, blockSize.RoundUpToNearestBlock(1024));
        Assert.Equal(2048UL, blockSize.RoundUpToNearestBlock(1025));
        Assert.Equal(2048UL, blockSize.RoundUpToNearestBlock(2000));

        // Note: The last # of values in any integer will never constitute a whole block
        Assert.Equal(ulong.MaxValue - 1023, blockSize.RoundUpToNearestBlock(ulong.MaxValue - 1024));

        // Assert.Equal(ulong.MaxValue - 1023, blockSize.RoundUpToNearestBlock(ulong.MaxValue - 1));
    }

    [Fact]
    public void RoundUpToNearestBlockMinimumOne()
    {
        var blockSize = BlockSize.FromPowerOfTwo(10);
        Assert.Equal(1024UL, blockSize.RoundUpToNearestBlockMinimumOne(0));
        Assert.Equal(1024UL, blockSize.RoundUpToNearestBlockMinimumOne(1024));
        Assert.Equal(2048UL, blockSize.RoundUpToNearestBlockMinimumOne(1025));
        Assert.Equal(2048UL, blockSize.RoundUpToNearestBlockMinimumOne(2000));

        // Note: The last # of values in any integer will never constitute a whole block
        Assert.Equal(ulong.MaxValue - 1023, blockSize.RoundUpToNearestBlockMinimumOne(ulong.MaxValue - 1024));

        // Assert.Equal(ulong.MaxValue - 1023, blockSize.RoundUpToNearestBlockMinimumOne(ulong.MaxValue - 1));
    }

    [Fact]
    public void RoundDownToNearestBlock()
    {
        var blockSize = BlockSize.FromPowerOfTwo(10);
        Assert.Equal(0UL, blockSize.RoundDownToNearestBlock(0));
        Assert.Equal(0UL, blockSize.RoundDownToNearestBlock(1));
        Assert.Equal(1024UL, blockSize.RoundDownToNearestBlock(1500));
        Assert.Equal(2048UL, blockSize.RoundDownToNearestBlock(2048));
        Assert.Equal(ulong.MaxValue - 1023, blockSize.RoundDownToNearestBlock(ulong.MaxValue - 1));

        // Note: The last # of values in any integer will never constitute a whole block
        Assert.Equal(ulong.MaxValue - 1023, blockSize.RoundDownToNearestBlock(ulong.MaxValue));
    }

    [Fact]
    public void RoundDownToNearestBlockMinimumOne()
    {
        var blockSize = BlockSize.FromPowerOfTwo(10);
        Assert.Equal(1024UL, blockSize.RoundDownToNearestBlockMinimumOne(0));
        Assert.Equal(1024UL, blockSize.RoundDownToNearestBlockMinimumOne(1));
        Assert.Equal(1024UL, blockSize.RoundDownToNearestBlockMinimumOne(1023));
        Assert.Equal(1024UL, blockSize.RoundDownToNearestBlockMinimumOne(1024));
        Assert.Equal(1024UL, blockSize.RoundDownToNearestBlockMinimumOne(1025));
        Assert.Equal(1024UL, blockSize.RoundDownToNearestBlockMinimumOne(1500));
        Assert.Equal(1024UL, blockSize.RoundDownToNearestBlockMinimumOne(2047));
        Assert.Equal(2048UL, blockSize.RoundDownToNearestBlockMinimumOne(2048));
        Assert.Equal(ulong.MaxValue - 1023, blockSize.RoundDownToNearestBlockMinimumOne(ulong.MaxValue - 1));

        // Note: The last # of values in any integer will never constitute a whole block
        Assert.Equal(ulong.MaxValue - 1023, blockSize.RoundDownToNearestBlockMinimumOne(ulong.MaxValue));
    }
}