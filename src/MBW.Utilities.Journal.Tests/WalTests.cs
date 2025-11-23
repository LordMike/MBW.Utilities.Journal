using MBW.Utilities.Journal.Exceptions;
using MBW.Utilities.Journal.Structures;
using MBW.Utilities.Journal.Tests.Helpers;
using MBW.Utilities.Journal.WalJournal;

namespace MBW.Utilities.Journal.Tests;

public class WalTests : TestsBase
{
    [Fact]
    public async Task ReadAdvancesPositionWhenJournalHasUncommittedData()
    {
        await RunScenarioAsync(async () =>
        {
            using JournaledStream journaledStream = await JournaledStreamFactory.CreateWalJournal(TestFile, JournalFileProvider);

            const string data = "ABCDEF";
            journaledStream.WriteStr(data);
            Assert.Equal(data.Length, journaledStream.Length);
            journaledStream.Seek(0, SeekOrigin.Begin);
            Assert.Equal(0, journaledStream.Position);

            Span<byte> buffer = stackalloc byte[2];
            int read = journaledStream.Read(buffer);

            Assert.Equal(2, read);
            Assert.Equal(2, journaledStream.Position);
        });
    }

    [Fact]
    public async Task PositionSetterRejectsNegativeValues()
    {
        await RunScenarioAsync(async () =>
        {
            using JournaledStream journaledStream = await JournaledStreamFactory.CreateWalJournal(TestFile, JournalFileProvider);

            Assert.Throws<ArgumentOutOfRangeException>(() => journaledStream.Position = -1);
        });
    }

    [Fact]
    public async Task JournalFileCorruptionTest_PartialCorrupt()
    {
        char[] expectedTransacted = new char[Math.Max("Clean".Length, "Corrupt".Length)];
        "Clean".CopyTo(expectedTransacted);
        "Corrupt".CopyTo(expectedTransacted);

        TestFile.WriteStr("Clean");

        await RunScenarioAsync(async () =>
        {
            using JournaledStream journaledStream1 = await JournaledStreamFactory.CreateWalJournal(TestFile, JournalFileProvider);

            journaledStream1.WriteStr("Corrupt");
            Assert.Equal(expectedTransacted, journaledStream1.ReadFullStr().AsSpan());

            // Simulate crash by snapshotting journal mid-commit
            await journaledStream1.Commit(applyImmediately: false);
        });

        Assert.True(JournalFileProvider.Exists(string.Empty));
        Assert.Equal("Clean", TestFile.ReadFullStr());

        // Corrupt journal between header & footer
        var journalFile = JournalFileProvider.OpenOrCreate(string.Empty);

        byte[] buffer = new byte[journalFile.Length - JournalFileHeader.StructSize - WalJournalFooter.StructSize];
        Random rng = new Random(42);
        rng.NextBytes(buffer);

        journalFile.Seek(JournalFileHeader.StructSize, SeekOrigin.Begin);
        journalFile.Write(buffer);

        // Verify the journal is detected as not being valid (corrupt data)
        JournalCorruptedException ex = await RunScenarioAsync<JournalCorruptedException>(async () =>
        {
            using JournaledStream journaledStream = await JournaledStreamFactory.CreateWalJournal(TestFile, JournalFileProvider);
        });

        Assert.True(ex.OriginalFileHasBeenAltered);

        // Note: Original has been altered, as the length is applied before any journaled data is read
        // // Verify that the original data is still intact
        // Assert.Equal("Clean", TestFile.ReadFullStr());
    }
}
