using MBW.Utilities.Journal.Exceptions;
using MBW.Utilities.Journal.Structures;
using MBW.Utilities.Journal.Tests.Helpers;
using MBW.Utilities.Journal.WalJournal;

namespace MBW.Utilities.Journal.Tests;

public class WalTests : TestsBase
{
    [Fact]
    public void JournalFileCorruptionTest_PartialCorrupt()
    {
        char[] expectedTransacted = new char[Math.Max("Clean".Length, "Corrupt".Length)];
        "Clean".CopyTo(expectedTransacted);
        "Corrupt".CopyTo(expectedTransacted);

        TestFile.WriteStr("Clean");

        RunScenario<TestStreamBlockedException>(() =>
        {
            using JournaledStream journaledStream1 = JournaledStreamFactory.CreateWalJournal(TestFile, JournalFileProvider);

            journaledStream1.WriteStr("Corrupt");
            Assert.Equal(expectedTransacted, journaledStream1.ReadFullStr().AsSpan());

            // Trigger an unwriteable file
            TestFile.LockWrites = true;

            journaledStream1.Commit();
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
        JournalCorruptedException ex = RunScenario<JournalCorruptedException>(() =>
        {
            using JournaledStream journaledStream = JournaledStreamFactory.CreateWalJournal(TestFile, JournalFileProvider);
        });

        Assert.True(ex.OriginalFileHasBeenAltered);

        // Note: Original has been altered, as the length is applied before any journaled data is read
        // // Verify that the original data is still intact
        // Assert.Equal("Clean", TestFile.ReadFullStr());
    }
}