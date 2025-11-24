using System.Numerics;
using System.Text;
using MBW.Utilities.Journal.Primitives;
using MBW.Utilities.Journal.Structures;
using MBW.Utilities.Journal.Tests.Helpers;

namespace MBW.Utilities.Journal.Tests;

public class SparseTests : TestsBase
{
    [Fact]
    public async Task TooSmallBlockSizeIsRejected()
    {
        var jrnlFilePath = Path.Combine(Path.GetTempPath(), "DUMMY_FILE");

        // The Sparse Journal assumes the BlockSize in use in large enough, that it surpasses the Journal File Header
        uint journalFileHeaderSize = (uint)JournalFileHeader.StructSize;

        BlockSize b = BlockSize.FromPowerOfTwo((byte)BitOperations.Log2(journalFileHeaderSize));
        byte lowerSize = (byte)b.RoundDownToNearestBlockMinimumOne(journalFileHeaderSize);
        byte lowerBlockSize = (byte)BitOperations.Log2(lowerSize);

        byte upperSize = (byte)b.RoundUpToNearestBlockMinimumOne(journalFileHeaderSize);
        byte upperBlockSize = (byte)BitOperations.Log2(upperSize);

        using var ms = new MemoryStream();

        // Using an exponent below or at the minimum size is not ok
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            JournaledStreamFactory.CreateSparseJournal(ms, jrnlFilePath, lowerBlockSize));

        // Using an exponent above the minimum size, is ok
        using (JournaledStreamFactory.CreateSparseJournal(ms, jrnlFilePath, upperBlockSize))
        {
        }
    }

    [Fact]
    public async Task LargerThanBlockSizeSparseJournalStreamTest()
    {
        // Use 512-byte blocks
        BlockSize blockSize = BlockSize.FromSize(512);

        // Ensure we have more than 1 ulong in our bitmap
        byte[] firstBuffer = new byte[blockSize.Size * 8 * sizeof(ulong) + 10];
        Random.Shared.NextBytes(firstBuffer);

        await RunScenarioAsync(async () =>
        {
            await using JournaledStream journaledStream =
                await JournaledStreamFactory.CreateSparseJournal(TestFile, JournalFileProvider, blockSize.Power);

            journaledStream.Write(firstBuffer);
            await journaledStream.Commit();
        });

        byte[] actual = TestFile.ReadFullBytes();
        Assert.Equal(firstBuffer, actual);

        byte[] secondBuffer = new byte[1000];
        Random.Shared.NextBytes(secondBuffer);

        await RunScenarioAsync(async () =>
        {
            await using JournaledStream journaledStream =
                await JournaledStreamFactory.CreateSparseJournal(TestFile, JournalFileProvider, blockSize.Power);

            journaledStream.Write(secondBuffer);
            await journaledStream.Commit();
        });

        byte[] expected = new byte[Math.Max(firstBuffer.Length, secondBuffer.Length)];
        Array.Copy(firstBuffer, 0, expected, 0, firstBuffer.Length);
        Array.Copy(secondBuffer, 0, expected, 0, secondBuffer.Length);

        actual = TestFile.ReadFullBytes();
        Assert.Equal(Encoding.UTF8.GetString(expected), Encoding.UTF8.GetString(actual));
        // Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task LargerFile()
    {
        // Use 4096-byte blocks
        BlockSize blockSize = BlockSize.FromSize(4096);

        // Prepare 800K data, this is at least a few ulong-bitmaps worth of 4k blocks 
        byte[] expected = new byte[800 * 1024];
        Random.Shared.NextBytes(expected);

        await RunScenarioAsync(async () =>
        {
            await using JournaledStream journaledStream =
                await JournaledStreamFactory.CreateSparseJournal(TestFile, JournalFileProvider, blockSize.Power);

            // Write in smaller random increments
            Span<byte> remaining = expected.AsSpan();
            while (remaining.Length > 0)
            {
                int toRead = Math.Min(remaining.Length, Random.Shared.Next(200, 1000));
                Span<byte> buffer = remaining[..toRead];
                remaining = remaining[toRead..];

                journaledStream.Write(buffer);
            }

            await journaledStream.Commit();
        });

        byte[] actual = TestFile.ReadFullBytes();
        Assert.Equal(expected, actual);
    }
}