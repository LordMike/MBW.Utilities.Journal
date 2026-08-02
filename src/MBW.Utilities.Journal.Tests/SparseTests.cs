using System.Numerics;
using System.Text;
using MBW.Utilities.Journal.Exceptions;
using MBW.Utilities.Journal.Extensions;
using MBW.Utilities.Journal.SparseJournal;
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
            await journaledStream.Commit(true);
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
            await journaledStream.Commit(true);
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

            await journaledStream.Commit(true);
        });

        byte[] actual = TestFile.ReadFullBytes();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task EmptyBitmapCannotHidePersistedBlocks()
    {
        TestFile.WriteStr("original");
        await using (JournaledStream writer =
                     await JournaledStreamFactory.CreateSparseJournal(TestFile, JournalFileProvider))
        {
            writer.WriteStr("updated");
            await writer.Commit();
        }

        Assert.True(JournalFileProvider.TryOpen(string.Empty, false, out Stream? journal));
        using (journal)
        {
            journal.Seek(-SparseJournalFooter.StructSize, SeekOrigin.End);
            Span<byte> footerBuffer = stackalloc byte[SparseJournalFooter.StructSize];
            SparseJournalFooter footer = journal.ReadOne<SparseJournalFooter>(footerBuffer);
            footer.BitmapLengthUlongs = 0;
            footer.StartOfBitmap = (ulong)(journal.Length - SparseJournalFooter.StructSize);
            journal.Seek(-SparseJournalFooter.StructSize, SeekOrigin.End);
            journal.Write(footer.AsSpan());
        }

        await Assert.ThrowsAsync<JournalCorruptedException>(() =>
            JournaledStreamFactory.CreateSparseJournal(TestFile, JournalFileProvider));
        Assert.Equal("original", TestFile.ReadFullStr());
        Assert.True(JournalFileProvider.HasAnyJournal);
    }

    [Fact]
    public async Task EmptyPreparedJournalHasAValidEmptyBitmap()
    {
        Guid key = Guid.NewGuid();
        await using (JournaledStream writer =
                     await JournaledStreamFactory.CreateSparseJournal(TestFile, JournalFileProvider))
        {
            await writer.Prepare(key);
            await writer.Commit(key);
        }

        await using JournaledStream recovered =
            await JournaledStreamFactory.CreateSparseJournal(TestFile, JournalFileProvider);
        Assert.Equal(JournaledStreamState.Ready, recovered.State);
        Assert.False(JournalFileProvider.HasAnyJournal);
    }
}
