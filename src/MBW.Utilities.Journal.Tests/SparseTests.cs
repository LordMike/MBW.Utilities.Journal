﻿using MBW.Utilities.Journal.Helpers;
using MBW.Utilities.Journal.Tests.Helpers;

namespace MBW.Utilities.Journal.Tests;

public class SparseTests : TestsBase
{
    [Fact]
    public void LargerThanBlockSizeSparseJournalStreamTest()
    {
        // Use 512-byte blocks
        BlockSize blockSize = BlockSize.FromSize(512);

        // Ensure we have more than 1 ulong in our bitmap
        byte[] firstBuffer = new byte[blockSize.Size * 8 * sizeof(ulong) + 10];
        Random.Shared.NextBytes(firstBuffer);

        RunScenario(() =>
        {
            using JournaledStream journaledStream = JournaledStreamFactory.CreateSparseJournal(TestFile, JournalFileProvider, blockSize.Power);

            journaledStream.Write(firstBuffer);
            journaledStream.Commit();
        });

        byte[] actual = TestFile.ReadFullBytes();
        Assert.Equal(firstBuffer, actual);

        byte[] seconBuffer = new byte[1000];
        Random.Shared.NextBytes(seconBuffer);

        RunScenario(() =>
        {
            using JournaledStream journaledStream = JournaledStreamFactory.CreateSparseJournal(TestFile, JournalFileProvider, blockSize.Power);

            journaledStream.Write(seconBuffer);
            journaledStream.Commit();
        });

        byte[] expected = new byte[Math.Max(firstBuffer.Length, seconBuffer.Length)];
        Array.Copy(firstBuffer, 0, expected, 0, firstBuffer.Length);
        Array.Copy(seconBuffer, 0, expected, 0, seconBuffer.Length);

        actual = TestFile.ReadFullBytes();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void LargerFile()
    {
        // Use 4096-byte blocks
        BlockSize blockSize = BlockSize.FromSize(4096);

        // Prepare 800K data, this is at least a few ulong-bitmaps worth of 4k blocks 
        byte[] expected = new byte[800 * 1024];
        Random.Shared.NextBytes(expected);

        RunScenario(() =>
        {
            using JournaledStream journaledStream = JournaledStreamFactory.CreateSparseJournal(TestFile, JournalFileProvider, blockSize.Power);

            // Write in smaller random increments
            Span<byte> remaining = expected.AsSpan();
            while (remaining.Length > 0)
            {
                int toRead = Math.Min(remaining.Length, Random.Shared.Next(200, 1000));
                Span<byte> buffer = remaining[..toRead];
                remaining = remaining[toRead..];

                journaledStream.Write(buffer);
            }

            journaledStream.Commit();
        });

        byte[] actual = TestFile.ReadFullBytes();
        Assert.Equal(expected, actual);
    }
}