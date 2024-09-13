using MBW.Utilities.Journal.Exceptions;
using MBW.Utilities.Journal.Structures;
using MBW.Utilities.Journal.Tests.Helpers;

namespace MBW.Utilities.Journal.Tests;

public class TransactTests : TestsBase
{
    /// <summary>
    /// Prepare a file &amp; journal, committed - but not applied
    /// </summary>
    private void PrepareCommittedJournalButNotApplied(string initial = "Initial", string transacted = "HeldBack")
    {
        char[] expectedTransacted = new char[Math.Max(initial.Length, transacted.Length)];
        initial.CopyTo(expectedTransacted);
        transacted.CopyTo(expectedTransacted);

        TestFile.WriteStr(initial);

        RunScenario<TestStreamBlockedException>(() =>
        {
            using JournaledStream journaledStream = new JournaledStream(TestFile, JournalFileProvider);

            journaledStream.WriteStr(transacted);
            Assert.Equal(expectedTransacted, journaledStream.ReadFullStr().AsSpan());

            // Trigger an unwriteable file
            TestFile.LockWrites = true;

            journaledStream.Commit();
        });
    }

    [Fact]
    public void RollbackAfterCommitTest()
    {
        TestFile.WriteStr("Initial");

        RunScenario(() =>
        {
            using JournaledStream journaledStream = new JournaledStream(TestFile, JournalFileProvider);

            journaledStream.Seek(0, SeekOrigin.End);

            // Write data and commit
            journaledStream.WriteStr("CommittedData");
            journaledStream.Commit();

            // Write more data without committing
            journaledStream.WriteStr("UncommittedData");

            // Rollback should remove uncommitted data
            journaledStream.Rollback();
        });

        // Check if file only has the committed data
        Assert.Equal("InitialCommittedData", TestFile.ReadFullStr());
    }

    [Fact]
    public void EmptyTransactionCommitTest()
    {
        TestFile.WriteStr("InitialData");

        RunScenario(() =>
        {
            using JournaledStream journaledStream = new JournaledStream(TestFile, JournalFileProvider);

            // Commit without writing
            journaledStream.Commit();
        });

        // Ensure no changes are made to the file
        Assert.Equal("InitialData", TestFile.ReadFullStr());
        Assert.False(JournalFileProvider.Exists());
    }

    [Fact]
    public void BoundaryConditionWritesTest()
    {
        TestFile.WriteStr("Data");

        RunScenario(() =>
        {
            using JournaledStream journaledStream = new JournaledStream(TestFile, JournalFileProvider);

            // Position at the end of the current data
            journaledStream.Seek(0, SeekOrigin.End);

            // Write exactly at the end
            journaledStream.WriteStr("End");
            journaledStream.Commit();

            // Verify if the file has appended the data correctly
            Assert.Equal("DataEnd", journaledStream.ReadFullStr());

            // Now seek beyond the end and write more data
            journaledStream.Seek(10, SeekOrigin.Begin);
            journaledStream.WriteStr("Beyond");
            journaledStream.Commit();
        });

        // Check if the new data starts from position 10, filling with zeros if needed
        Assert.Equal("DataEnd\0\0\0Beyond", TestFile.ReadFullStr());
    }

    [Fact]
    public void NonSequentialWritesTest()
    {
        RunScenario(() =>
        {
            using JournaledStream journaledStream = new JournaledStream(TestFile, JournalFileProvider);

            // Write data at the start
            journaledStream.WriteStr("0123456789");

            journaledStream.Seek(0, SeekOrigin.Begin);
            journaledStream.WriteStr("1");
            Assert.Equal("1123456789", journaledStream.ReadFullStr());

            journaledStream.Seek(5, SeekOrigin.Begin);
            journaledStream.WriteStr("1");
            Assert.Equal("1123416789", journaledStream.ReadFullStr());

            journaledStream.Seek(-1, SeekOrigin.End);
            journaledStream.WriteStr("1");
            Assert.Equal("1123416781", journaledStream.ReadFullStr());

            journaledStream.Seek(0, SeekOrigin.Begin);
            journaledStream.WriteStr("9876543210");
            Assert.Equal("9876543210", journaledStream.ReadFullStr());

            // Move way beyond current file
            for (int i = 0; i < 100; i++)
                journaledStream.WriteStr("9876543210");

            Assert.Equal(100 * 10 + 10, journaledStream.Length);

            // Reset length
            journaledStream.SetLength(5);
            Assert.Equal("98765", journaledStream.ReadFullStr());

            // Make the string longer again
            journaledStream.WriteStr("88774466");
            Assert.Equal("9876588774466", journaledStream.ReadFullStr());

            journaledStream.Commit();
        });

        // Check final result
        Assert.Equal("9876588774466", TestFile.ReadFullStr());
    }

    [Fact]
    public void EOFBehaviorTest()
    {
        TestFile.WriteStr("12345");

        RunScenario(() =>
        {
            using JournaledStream journaledStream = new JournaledStream(TestFile, JournalFileProvider);

            // Attempt to read at EOF
            journaledStream.Seek(0, SeekOrigin.End); // Move to the end of the data

            Span<byte> buffer = stackalloc byte[10];
            int read = journaledStream.Read(buffer);
            Assert.Equal(0, read);

            // Write at EOF
            journaledStream.WriteStr("6789");
            journaledStream.Commit();

            // Verify if the new data is appended correctly
            Assert.Equal("123456789", journaledStream.ReadFullStr());

            // Seek beyond EOF and try to write
            journaledStream.Seek(15, SeekOrigin.Begin);
            journaledStream.WriteStr("End");
            journaledStream.Commit();
        });

        // Check for zero padding and the new data
        Assert.Equal("123456789\0\0\0\0\0\0End", TestFile.ReadFullStr());
    }

    [Fact]
    public void JournalFileCorruptionTest_PartialCorrupt()
    {
        PrepareCommittedJournalButNotApplied("Clean", "Corrupt");

        Assert.True(JournalFileProvider.Exists());
        Assert.Equal("Clean", TestFile.ReadFullStr());

        // Corrupt journal between header & footer
        byte[] buffer = new byte[JournalFile.Length - TransactFileHeader.StructSize - TransactFileFooter.StructSize];
        Random rng = new Random(42);
        rng.NextBytes(buffer);

        JournalFile.Seek(TransactFileHeader.StructSize, SeekOrigin.Begin);
        JournalFile.Write(buffer);

        // Verify the journal is detected as not being valid (corrupt data)
        JournalCorruptedException ex = RunScenario<JournalCorruptedException>(() =>
        {
            using JournaledStream journaledStream = new JournaledStream(TestFile, JournalFileProvider);
        });

        Assert.True(ex.OriginalFileHasBeenAltered);

        // Note: Original has been altered, as the length is applied before any journaled data is read
        // // Verify that the original data is still intact
        // Assert.Equal("Clean", TestFile.ReadFullStr());
    }

    [Fact]
    public void JournalFileCorruptionTest_FullyCorrupt()
    {
        PrepareCommittedJournalButNotApplied("Clean", "Corrupt");

        Assert.True(JournalFileProvider.Exists());
        Assert.Equal("Clean", TestFile.ReadFullStr());

        // Corrupt journal entirely
        byte[] buffer = new byte[JournalFile.Length];
        Random rng = new Random(42);
        rng.NextBytes(buffer);

        JournalFile.Write(buffer);

        // Verify the journal is detected as not being valid (missing header)
        RunScenario<JournalCorruptedException>(() =>
        {
            using JournaledStream journaledStream = new JournaledStream(TestFile, JournalFileProvider);
        });

        // The journal must not be removed. The user must figure out what to do
        RunScenario<JournalCorruptedException>(() =>
        {
            using JournaledStream journaledStream = new JournaledStream(TestFile, JournalFileProvider);
        });

        // Verify that the original data is still intact
        Assert.Equal("Clean", TestFile.ReadFullStr());
    }

    [Fact]
    public void RecoverCommitedTest()
    {
        PrepareCommittedJournalButNotApplied();

        // File does not see "HeldBack"
        Assert.True(JournalFileProvider.Exists());
        Assert.Equal("Initial", TestFile.ReadFullStr());

        // We should commit the journal at this point
        RunScenario(() =>
        {
            using JournaledStream journaledStream = new JournaledStream(TestFile, JournalFileProvider);

            // Original & transacted file should see "HeldBack"
            // The journal should have been replayed
            Assert.Equal("HeldBack", TestFile.ReadFullStr());
            Assert.Equal("HeldBack", journaledStream.ReadFullStr());
            Assert.False(JournalFileProvider.Exists());
        });

        Assert.False(JournalFileProvider.Exists());
        Assert.Equal("HeldBack", TestFile.ReadFullStr());
    }

    [Fact]
    public void RecoverUncommitedTest()
    {
        TestFile.WriteStr("Initially");

        // A journal is made, but is not committed
        RunScenario(() =>
        {
            using JournaledStream journaledStream = new JournaledStream(TestFile, JournalFileProvider);

            journaledStream.WriteStr("NotSeen");

            // Transacted sees the new "NotSeen", plus "ly" from "Initially"
            Assert.Equal("NotSeenly", journaledStream.ReadFullStr());

            // Do not commit
        });

        // Original file still holds "Initially"
        Assert.Equal("Initially", TestFile.ReadFullStr());
        Assert.True(JournalFileProvider.Exists());

        // Once reopened, the journal should be discarded
        RunScenario(() =>
        {
            using JournaledStream journaledStream = new JournaledStream(TestFile, JournalFileProvider);

            Assert.Equal("Initially", journaledStream.ReadFullStr());
            Assert.False(JournalFileProvider.Exists());
        });

        Assert.Equal("Initially", TestFile.ReadFullStr());
        Assert.False(JournalFileProvider.Exists());
    }

    [Fact]
    public void RollebackTest()
    {
        TestFile.WriteStr("Alpha");

        RunScenario(() =>
        {
            using JournaledStream journaledStream = new JournaledStream(TestFile, JournalFileProvider);

            journaledStream.Seek(0, SeekOrigin.End);

            // Initial commit
            journaledStream.WriteStr("Beta");

            Assert.Equal("AlphaBeta", journaledStream.ReadFullStr());
            Assert.True(JournalFileProvider.Exists());

            journaledStream.Rollback();

            // Post-rollback
            Assert.False(JournalFileProvider.Exists());
            Assert.Equal("Alpha", journaledStream.ReadFullStr());
        });

        Assert.False(JournalFileProvider.Exists());
        Assert.Equal("Alpha", TestFile.ReadFullStr());
    }

    [Fact]
    public void DoubleCommitTest()
    {
        RunScenario(() =>
        {
            using (JournaledStream journaledStream = new JournaledStream(TestFile, JournalFileProvider))
            {
                // Initial commit
                journaledStream.WriteStr("Alpha");

                Assert.Equal("Alpha", journaledStream.ReadFullStr());
                Assert.True(JournalFileProvider.Exists());

                journaledStream.Commit();

                // Post-commit
                Assert.False(JournalFileProvider.Exists());
                Assert.Equal("Alpha", journaledStream.ReadFullStr());

                // Second commit
                journaledStream.WriteStr("Beta");
                Assert.Equal("AlphaBeta", journaledStream.ReadFullStr());
                Assert.True(JournalFileProvider.Exists());

                journaledStream.Commit();

                // Post-commit
                Assert.False(JournalFileProvider.Exists());
                Assert.Equal("AlphaBeta", journaledStream.ReadFullStr());
            }
        });

        Assert.False(JournalFileProvider.Exists());
        Assert.Equal("AlphaBeta", TestFile.ReadFullStr());
    }

    [Fact]
    public void SimpleTest()
    {
        RunScenario(() =>
        {
            using JournaledStream journaledStream = new JournaledStream(TestFile, JournalFileProvider);

            journaledStream.WriteStr("Begin");
            Assert.Equal(5, journaledStream.Length);
            Assert.Equal("Begin", journaledStream.ReadFullStr());

            journaledStream.WriteStr("Mid");
            Assert.Equal(8, journaledStream.Length);
            Assert.Equal("BeginMid", journaledStream.ReadFullStr());

            journaledStream.WriteStr("End");
            Assert.Equal(11, journaledStream.Length);
            Assert.Equal("BeginMidEnd", journaledStream.ReadFullStr());

            journaledStream.Commit();
        });

        Assert.Equal("BeginMidEnd", TestFile.ReadFullStr());
        Assert.False(JournalFileProvider.Exists());

        RunScenario(() =>
        {
            using JournaledStream journaledStream = new JournaledStream(TestFile, JournalFileProvider);

            journaledStream.Seek(3, SeekOrigin.Begin);
            journaledStream.WriteStr("u");
            Assert.Equal("BegunMidEnd", journaledStream.ReadFullStr());

            journaledStream.Commit();
        });

        Assert.Equal("BegunMidEnd", TestFile.ReadFullStr());
        Assert.False(JournalFileProvider.Exists());

        RunScenario(() =>
        {
            using JournaledStream journaledStream = new JournaledStream(TestFile, JournalFileProvider);

            journaledStream.Seek(0, SeekOrigin.End);
            journaledStream.WriteStr("PostStuff");

            Assert.Equal("BegunMidEndPostStuff", journaledStream.ReadFullStr());

            journaledStream.Seek(3, SeekOrigin.Begin);
            journaledStream.WriteStr("a");

            Assert.Equal("BeganMidEndPostStuff", journaledStream.ReadFullStr());

            journaledStream.Commit();
        });

        Assert.Equal("BeganMidEndPostStuff", TestFile.ReadFullStr());
        Assert.False(JournalFileProvider.Exists());

        RunScenario(() =>
        {
            using JournaledStream journaledStream = new JournaledStream(TestFile, JournalFileProvider);

            journaledStream.SetLength(8);

            Assert.Equal("BeganMid", journaledStream.ReadFullStr());

            journaledStream.Commit();
        });

        Assert.Equal("BeganMid", TestFile.ReadFullStr());
        Assert.False(JournalFileProvider.Exists());
    }
}