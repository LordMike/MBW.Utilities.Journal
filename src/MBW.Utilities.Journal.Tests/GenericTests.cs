using MBW.Utilities.Journal.Abstracts;
using MBW.Utilities.Journal.Exceptions;
using MBW.Utilities.Journal.Tests.Helpers;

namespace MBW.Utilities.Journal.Tests;

public class GenericTests : TestsBase
{
    public delegate Task<JournaledStream> CreateDelegate(Stream origin, IJournalStreamFactory journalStreamFactory, JournalOpenMode openMode = JournalOpenMode.Default);

    public static IEnumerable<object[]> GetTestStreams()
    {
        yield return [(CreateDelegate)JournaledStreamFactory.CreateWalJournal];
        yield return [(CreateDelegate)JournaledStreamFactory.CreateSparseJournal];
    }

    private void ApplyToAllJournals(Action<string, Stream> action)
    {
        foreach ((string? identifier, var journalStream) in JournalFileProvider.Streams)
        {
            journalStream.Seek(0, SeekOrigin.Begin);
            action(identifier, journalStream);
        }
    }

    [Theory]
    [MemberData(nameof(GetTestStreams))]
    public async Task RollbackAfterCommitTest(CreateDelegate createDelegate)
    {
        TestFile.WriteStr("Initial");

        await RunScenarioAsync(async () =>
        {
            using JournaledStream journaledStream = await createDelegate(TestFile, JournalFileProvider);

            journaledStream.Seek(0, SeekOrigin.End);

            // Write data and commit
            journaledStream.WriteStr("CommittedData");
            await journaledStream.Commit();

            // Write more data without committing
            journaledStream.WriteStr("UncommittedData");

            // Rollback should remove uncommitted data
            await journaledStream.Rollback();
        });

        // Check if file only has the committed data
        Assert.Equal("InitialCommittedData", TestFile.ReadFullStr());
    }

    [Theory]
    [MemberData(nameof(GetTestStreams))]
    public async Task EmptyTransactionCommitTest(CreateDelegate createDelegate)
    {
        TestFile.WriteStr("InitialData");

        await RunScenarioAsync(async () =>
        {
            using JournaledStream journaledStream = await createDelegate(TestFile, JournalFileProvider);

            // Commit without writing
            await journaledStream.Commit();
        });

        // Ensure no changes are made to the file
        Assert.Equal("InitialData", TestFile.ReadFullStr());
        Assert.False(JournalFileProvider.HasAnyJournal);
    }

    [Theory]
    [MemberData(nameof(GetTestStreams))]
    public async Task BoundaryConditionWritesTest(CreateDelegate createDelegate)
    {
        TestFile.WriteStr("Data");

        await RunScenarioAsync(async () =>
        {
            using JournaledStream journaledStream = await createDelegate(TestFile, JournalFileProvider);

            // Position at the end of the current data
            journaledStream.Seek(0, SeekOrigin.End);

            // Write exactly at the end
            journaledStream.WriteStr("End");
            await journaledStream.Commit();

            // Verify if the file has appended the data correctly
            Assert.Equal("DataEnd", journaledStream.ReadFullStr());

            // Now seek beyond the end and write more data
            journaledStream.Seek(10, SeekOrigin.Begin);
            journaledStream.WriteStr("Beyond");
            await journaledStream.Commit();
        });

        // Check if the new data starts from position 10, filling with zeros if needed
        Assert.Equal("DataEnd\0\0\0Beyond", TestFile.ReadFullStr());
    }

    [Theory]
    [MemberData(nameof(GetTestStreams))]
    public async Task NonSequentialWritesTest(CreateDelegate createDelegate)
    {
        await RunScenarioAsync(async () =>
        {
            using JournaledStream journaledStream = await createDelegate(TestFile, JournalFileProvider);

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

            await journaledStream.Commit();
        });

        // Check final result
        Assert.Equal("9876588774466", TestFile.ReadFullStr());
    }

    [Theory]
    [MemberData(nameof(GetTestStreams))]
    public async Task EOFBehaviorTest(CreateDelegate createDelegate)
    {
        TestFile.WriteStr("12345");

        await RunScenarioAsync(async () =>
        {
            using JournaledStream journaledStream = await createDelegate(TestFile, JournalFileProvider);

            // Attempt to read at EOF
            journaledStream.Seek(0, SeekOrigin.End); // Move to the end of the data

            Span<byte> buffer = stackalloc byte[10];
            int read = journaledStream.Read(buffer);
            Assert.Equal(0, read);

            // Write at EOF
            journaledStream.WriteStr("6789");
            await journaledStream.Commit();

            // Verify if the new data is appended correctly
            Assert.Equal("123456789", journaledStream.ReadFullStr());

            // Seek beyond EOF and try to write
            journaledStream.Seek(15, SeekOrigin.Begin);
            journaledStream.WriteStr("End");
            await journaledStream.Commit();
        });

        // Check for zero padding and the new data
        Assert.Equal("123456789\0\0\0\0\0\0End", TestFile.ReadFullStr());
    }

    [Theory]
    [MemberData(nameof(GetTestStreams))]
    public async Task JournalFileCorruptionTest_FullyCorrupt(CreateDelegate createDelegate)
    {
        char[] expectedTransacted = new char[Math.Max("Clean".Length, "Corrupt".Length)];
        "Clean".CopyTo(expectedTransacted);
        "Corrupt".CopyTo(expectedTransacted);

        TestFile.WriteStr("Clean");

        await RunScenarioAsync(async () =>
        {
            using JournaledStream journaledStream1 = await createDelegate(TestFile, JournalFileProvider);

            journaledStream1.WriteStr("Corrupt");
            Assert.Equal(expectedTransacted, journaledStream1.ReadFullStr().AsSpan());

            await journaledStream1.Commit(false);
        });

        Assert.True(JournalFileProvider.Exists(string.Empty));
        Assert.Equal("Clean", TestFile.ReadFullStr());

        // Corrupt journal entirely
        ApplyToAllJournals(static (identifier, journalStream) =>
        {
            byte[] buffer = new byte[journalStream.Length];
            Random rng = new Random(42);
            rng.NextBytes(buffer);

            journalStream.Write(buffer);
        });

        // Verify the journal is detected as not being valid (missing header)
        await RunScenarioAsync<JournalCorruptedException>(async () =>
        {
            using JournaledStream journaledStream = await createDelegate(TestFile, JournalFileProvider);
        });

        // The journal must not be removed. The user must figure out what to do
        await RunScenarioAsync<JournalCorruptedException>(async () =>
        {
            using JournaledStream journaledStream = await createDelegate(TestFile, JournalFileProvider);
        });

        // Verify that the original data is still intact
        Assert.Equal("Clean", TestFile.ReadFullStr());
    }

    [Theory]
    [MemberData(nameof(GetTestStreams))]
    public async Task RecoverCommitedTest(CreateDelegate createDelegate)
    {
        char[] expectedTransacted = new char[Math.Max("Initial".Length, "HeldBack".Length)];
        "Initial".CopyTo(expectedTransacted);
        "HeldBack".CopyTo(expectedTransacted);

        TestFile.WriteStr("Initial");

        await RunScenarioAsync(async () =>
        {
            using JournaledStream journaledStream1 = await createDelegate(TestFile, JournalFileProvider);

            journaledStream1.WriteStr("HeldBack");
            Assert.Equal(expectedTransacted, journaledStream1.ReadFullStr().AsSpan());

            // Commit, but do not apply yet
            await journaledStream1.Commit(false);
        });

        // File does not see "HeldBack"
        Assert.True(JournalFileProvider.Exists(string.Empty));
        Assert.Equal("Initial", TestFile.ReadFullStr());

        // We should commit the journal at this point
        await RunScenarioAsync(async () =>
        {
            // This should auto-apply the committed stream
            using JournaledStream journaledStream = await createDelegate(TestFile, JournalFileProvider);

            // Original & transacted file should see "HeldBack"
            // The journal should have been replayed
            Assert.Equal("HeldBack", TestFile.ReadFullStr());
            Assert.Equal("HeldBack", journaledStream.ReadFullStr());
            Assert.False(JournalFileProvider.HasAnyJournal);
        });

        Assert.False(JournalFileProvider.HasAnyJournal);
        Assert.Equal("HeldBack", TestFile.ReadFullStr());
    }

    [Theory]
    [MemberData(nameof(GetTestStreams))]
    public async Task RecoverUncommitedTest(CreateDelegate createDelegate)
    {
        TestFile.WriteStr("Initially");

        // A journal is made, but is not committed
        await RunScenarioAsync(async () =>
        {
            using JournaledStream journaledStream = await createDelegate(TestFile, JournalFileProvider);

            journaledStream.WriteStr("NotSeen");

            // Transacted sees the new "NotSeen", plus "ly" from "Initially"
            Assert.Equal("NotSeenly", journaledStream.ReadFullStr());

            // Do not commit
        });

        // Original file still holds "Initially"
        Assert.Equal("Initially", TestFile.ReadFullStr());
        Assert.True(JournalFileProvider.HasAnyJournal);

        // Once reopened, the journal should be discarded
        await RunScenarioAsync(async () =>
        {
            using JournaledStream journaledStream = await createDelegate(TestFile, JournalFileProvider);

            Assert.Equal("Initially", journaledStream.ReadFullStr());
            Assert.False(JournalFileProvider.HasAnyJournal);
        });

        Assert.Equal("Initially", TestFile.ReadFullStr());
        Assert.False(JournalFileProvider.HasAnyJournal);
    }

    [Theory]
    [MemberData(nameof(GetTestStreams))]
    public async Task RollbackTest(CreateDelegate createDelegate)
    {
        TestFile.WriteStr("Alpha");

        await RunScenarioAsync(async () =>
        {
            using JournaledStream journaledStream = await createDelegate(TestFile, JournalFileProvider);

            journaledStream.Seek(0, SeekOrigin.End);

            // Initial commit
            journaledStream.WriteStr("Beta");

            Assert.Equal("AlphaBeta", journaledStream.ReadFullStr());
            Assert.True(JournalFileProvider.HasAnyJournal);

            await journaledStream.Rollback();

            // Post-rollback
            Assert.Equal("Alpha", journaledStream.ReadFullStr());
            Assert.False(JournalFileProvider.HasAnyJournal);
        });

        Assert.Equal("Alpha", TestFile.ReadFullStr());
        Assert.False(JournalFileProvider.HasAnyJournal);
    }

    [Theory]
    [MemberData(nameof(GetTestStreams))]
    public async Task DoubleCommitTest(CreateDelegate createDelegate)
    {
        await RunScenarioAsync(async () =>
        {
            using JournaledStream journaledStream = await createDelegate(TestFile, JournalFileProvider);

            // Initial commit
            journaledStream.WriteStr("Alpha");

            Assert.Equal("Alpha", journaledStream.ReadFullStr());
            Assert.True(JournalFileProvider.HasAnyJournal);

            await journaledStream.Commit();

            // Post-commit
            Assert.Equal("Alpha", journaledStream.ReadFullStr());
            Assert.False(JournalFileProvider.HasAnyJournal);

            // Second commit
            journaledStream.WriteStr("Beta");
            Assert.Equal("AlphaBeta", journaledStream.ReadFullStr());
            Assert.True(JournalFileProvider.HasAnyJournal);

            await journaledStream.Commit();

            // Post-commit
            Assert.Equal("AlphaBeta", journaledStream.ReadFullStr());
            Assert.False(JournalFileProvider.HasAnyJournal);
        });

        Assert.False(JournalFileProvider.HasAnyJournal);
        Assert.Equal("AlphaBeta", TestFile.ReadFullStr());
    }

    [Theory]
    [MemberData(nameof(GetTestStreams))]
    public async Task DeferredCommitApplyTest(CreateDelegate createDelegate)
    {
        TestFile.WriteStr("Original");

        await RunScenarioAsync(async () =>
        {
            using JournaledStream journaledStream = await createDelegate(TestFile, JournalFileProvider);

            journaledStream.Seek(0, SeekOrigin.End);
            journaledStream.WriteStr("Delayed");

            Assert.Equal("OriginalDelayed", journaledStream.ReadFullStr());

            await journaledStream.Commit(applyImmediately: false);

            Assert.Throws<JournalCommittedButNotAppliedException>(() => journaledStream.WriteStr("Again"));

            Assert.Equal("Original", TestFile.ReadFullStr());
            Assert.True(JournalFileProvider.HasAnyJournal);

            await journaledStream.Commit();
        });

        Assert.False(JournalFileProvider.HasAnyJournal);
        Assert.Equal("OriginalDelayed", TestFile.ReadFullStr());
    }

    [Theory]
    [MemberData(nameof(GetTestStreams))]
    public async Task SimpleTest(CreateDelegate createDelegate)
    {
        await RunScenarioAsync(async () =>
        {
            using JournaledStream journaledStream = await createDelegate(TestFile, JournalFileProvider);

            journaledStream.WriteStr("Begin");
            Assert.Equal(5, journaledStream.Length);
            Assert.Equal("Begin", journaledStream.ReadFullStr());

            journaledStream.WriteStr("Mid");
            Assert.Equal(8, journaledStream.Length);
            Assert.Equal("BeginMid", journaledStream.ReadFullStr());

            journaledStream.WriteStr("End");
            Assert.Equal(11, journaledStream.Length);
            Assert.Equal("BeginMidEnd", journaledStream.ReadFullStr());

            await journaledStream.Commit();
        });

        Assert.Equal("BeginMidEnd", TestFile.ReadFullStr());
        Assert.False(JournalFileProvider.HasAnyJournal);

        await RunScenarioAsync(async () =>
        {
            using JournaledStream journaledStream = await createDelegate(TestFile, JournalFileProvider);

            journaledStream.Seek(3, SeekOrigin.Begin);
            journaledStream.WriteStr("u");
            Assert.Equal("BegunMidEnd", journaledStream.ReadFullStr());

            await journaledStream.Commit();
        });

        Assert.Equal("BegunMidEnd", TestFile.ReadFullStr());
        Assert.False(JournalFileProvider.HasAnyJournal);

        await RunScenarioAsync(async () =>
        {
            using JournaledStream journaledStream = await createDelegate(TestFile, JournalFileProvider);

            journaledStream.Seek(0, SeekOrigin.End);
            journaledStream.WriteStr("PostStuff");

            Assert.Equal("BegunMidEndPostStuff", journaledStream.ReadFullStr());

            journaledStream.Seek(3, SeekOrigin.Begin);
            journaledStream.WriteStr("a");

            Assert.Equal("BeganMidEndPostStuff", journaledStream.ReadFullStr());

            await journaledStream.Commit();
        });

        Assert.Equal("BeganMidEndPostStuff", TestFile.ReadFullStr());
        Assert.False(JournalFileProvider.HasAnyJournal);

        await RunScenarioAsync(async () =>
        {
            using JournaledStream journaledStream = await createDelegate(TestFile, JournalFileProvider);

            journaledStream.SetLength(8);

            Assert.Equal("BeganMid", journaledStream.ReadFullStr());

            await journaledStream.Commit();
        });

        Assert.Equal("BeganMid", TestFile.ReadFullStr());
        Assert.False(JournalFileProvider.HasAnyJournal);
    }
}
