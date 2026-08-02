namespace MBW.Utilities.Journal.Tests;

/// <summary>
/// Canonical, executable examples for opening real files safely after either a clean shutdown
/// or an interrupted journal operation.
/// </summary>
public class JournaledStreamExamples
{
    [Fact]
    public async Task CanonicalSingleFileWorkflowReconcilesTheJournalBeforeUse()
    {
        using TemporaryDirectory files = new();
        string originPath = files.PathFor("document.dat");
        string journalPath = originPath + ".jrnl";

        // This application chooses fully automatic single-file recovery:
        // - a committed journal is reapplied because Commit is the durable decision;
        // - an uncommitted journal is discarded because origin was never changed.
        // Omit DiscardUncommittedJournals if an unexpected pending journal should instead
        // stop startup with JournalRecoveryRequiredException for operator inspection.
        const JournalOpenMode recoveryPolicy =
            JournalOpenMode.ApplyCommittedJournals |
            JournalOpenMode.DiscardUncommittedJournals;

        // Arrange an interrupted, uncommitted edit from an earlier process. In real code this
        // state would be left by a crash, rather than deliberately created during startup.
        await using (FileStream origin = OpenOrigin(originPath))
        await using (JournaledStream interrupted = await JournaledStreamFactory.CreateWalJournal(
                         origin, journalPath, recoveryPolicy))
        {
            interrupted.Write("never committed"u8);
        }

        Assert.True(File.Exists(journalPath));

        await using (FileStream origin = OpenOrigin(originPath))
        await using (JournaledStream journal = await JournaledStreamFactory.CreateWalJournal(
                         origin, journalPath, recoveryPolicy))
        {
            // CreateWalJournal finishes the selected recovery before returning. The abandoned
            // edit has therefore been discarded and this stream is immediately safe to read/write.
            Assert.Equal(JournaledStreamState.Ready, journal.State);
            Assert.Equal(0, journal.Length);

            // Always perform application reads and writes through JournaledStream. Accessing
            // origin directly would bypass the journal and its recovery guarantees.
            journal.Write("durable value"u8);

            // Commit records the durable decision but deliberately does not update origin yet.
            // A caller may use Commit(true) when it wants Commit followed by Apply immediately.
            await journal.Commit();
            Assert.Equal(0, origin.Length);
        }

        Assert.True(File.Exists(journalPath));

        await using (FileStream origin = OpenOrigin(originPath))
        await using (JournaledStream journal = await JournaledStreamFactory.CreateWalJournal(
                         origin, journalPath, recoveryPolicy))
        {
            // The same canonical open sequence detects the durable commit and reapplies it before
            // returning. Callers do not need a separate recovery phase before normal work.
            Assert.Equal(JournaledStreamState.Ready, journal.State);
            journal.Position = 0;
            byte[] contents = new byte[journal.Length];
            await journal.ReadExactlyAsync(contents);
            Assert.Equal("durable value", System.Text.Encoding.UTF8.GetString(contents));
        }

        Assert.Equal("durable value", await File.ReadAllTextAsync(originPath));
        Assert.False(File.Exists(journalPath));
    }

    [Fact]
    public async Task CanonicalCoordinatedWorkflowRecoversEveryParticipantBeforeUse()
    {
        using TemporaryDirectory files = new();
        string originAPath = files.PathFor("accounts.dat");
        string originBPath = files.PathFor("ledger.dat");
        string journalAPath = originAPath + ".jrnl";
        string journalBPath = originBPath + ".jrnl";
        string witnessPath = files.PathFor("transaction.witness");

        // Arrange an interrupted transaction whose durable witness was written before the crash.
        // This setup is test-only; normal application code calls coordinator.CommitAsync().
        Guid witnessedKey = Guid.NewGuid();
        await using (FileStream originA = OpenOrigin(originAPath))
        await using (FileStream originB = OpenOrigin(originBPath))
        await using (JournaledStream streamA = await JournaledStreamFactory.CreateWalJournal(
                         originA, journalAPath, JournalOpenMode.Coordinated))
        await using (JournaledStream streamB = await JournaledStreamFactory.CreateSparseJournal(
                         originB, journalBPath, openMode: JournalOpenMode.Coordinated))
        {
            streamA.Write("account recovery"u8);
            streamB.Write("ledger recovery"u8);
            await streamA.Prepare(witnessedKey);
            await streamB.Prepare(witnessedKey);
            await new FileBasedJournalWitnessStore(witnessPath).StoreAsync(witnessedKey);
        }

        await using (FileStream originA = OpenOrigin(originAPath))
        await using (FileStream originB = OpenOrigin(originBPath))
        await using (JournaledStream streamA = await JournaledStreamFactory.CreateWalJournal(
                         originA, journalAPath, JournalOpenMode.Coordinated))
        await using (JournaledStream streamB = await JournaledStreamFactory.CreateSparseJournal(
                         originB, journalBPath, openMode: JournalOpenMode.Coordinated))
        {
            // Canonical coordinated startup has two phases:
            // 1. Open the complete, fixed participant set with Coordinated. This preserves pending
            //    journals instead of making a per-file decision too early.
            // 2. Create the coordinator with the same durable witness path used by prior runs.
            //    Do not expose or use any participant until CreateAsync has completed recovery.
            FileBasedJournalWitnessStore witness = new(witnessPath);
            JournaledStreamCoordinator coordinator = await JournaledStreamCoordinator.CreateAsync(
                [streamA, streamB], witness, JournalCoordinatorRecoveryMode.Default);

            // Default coordinator recovery rolls witnessed work forward and unwitnessed work back.
            // At this point every participant is Ready and safe for ordinary reads and writes.
            Assert.Equal(JournaledStreamState.Ready, streamA.State);
            Assert.Equal(JournaledStreamState.Ready, streamB.State);
            Assert.Equal("account recovery", ReadAllText(streamA));
            Assert.Equal("ledger recovery", ReadAllText(streamB));

            streamA.Position = streamA.Length;
            streamB.Position = streamB.Length;
            streamA.Write(" + current"u8);
            streamB.Write(" + current"u8);

            // CommitAsync prepares and witnesses every participant before applying any origin.
            // The same startup sequence above can finish this operation after an interruption.
            await coordinator.CommitAsync();
        }

        Assert.Equal("account recovery + current", await File.ReadAllTextAsync(originAPath));
        Assert.Equal("ledger recovery + current", await File.ReadAllTextAsync(originBPath));
        Assert.False(File.Exists(journalAPath));
        Assert.False(File.Exists(journalBPath));
        Assert.False(File.Exists(witnessPath));
    }

    private static FileStream OpenOrigin(string path) =>
        new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
            4096, FileOptions.Asynchronous);

    private static string ReadAllText(Stream stream)
    {
        stream.Position = 0;
        using StreamReader reader = new(stream, System.Text.Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        return reader.ReadToEnd();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string _path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "journal-examples-" + Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(_path);

        public string PathFor(string fileName) => System.IO.Path.Combine(_path, fileName);

        public void Dispose() => Directory.Delete(_path, true);
    }
}
