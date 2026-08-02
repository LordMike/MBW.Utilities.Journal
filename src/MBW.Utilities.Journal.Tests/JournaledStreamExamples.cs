namespace MBW.Utilities.Journal.Tests;

/// <summary>
/// Executable examples of the most common real-file journaling workflows.
/// </summary>
public class JournaledStreamExamples
{
    [Fact]
    public async Task CommitAndApplyChangesToOneFile()
    {
        using TemporaryDirectory files = new();
        string originPath = files.PathFor("document.txt");
        string journalPath = files.PathFor("document.wal");

        await using (FileStream origin = OpenOrigin(originPath))
        await using (JournaledStream journal =
                     await JournaledStreamFactory.CreateWalJournal(origin, journalPath))
        {
            // All reads and writes go through JournaledStream; writing directly to origin
            // would bypass the journal and its recovery guarantees.
            journal.Write("Hello, journal!"u8);

            // Commit makes the decision durable but deliberately leaves origin untouched.
            await journal.Commit();
            Assert.Equal(0, origin.Length);

            // Apply replays the committed journal onto origin and removes the journal file.
            await journal.Apply();
            Assert.Equal(JournaledStreamState.Ready, journal.State);
        }

        Assert.Equal("Hello, journal!", await File.ReadAllTextAsync(originPath));
        Assert.False(File.Exists(journalPath));
    }

    [Fact]
    public async Task RollBackChangesToOneFile()
    {
        using TemporaryDirectory files = new();
        string originPath = files.PathFor("document.txt");
        string journalPath = files.PathFor("document.sparse");
        await File.WriteAllTextAsync(originPath, "original");

        await using (FileStream origin = OpenOrigin(originPath))
        await using (JournaledStream journal =
                     await JournaledStreamFactory.CreateSparseJournal(origin, journalPath))
        {
            // The overlay exposes the pending edit while the physical origin stays unchanged.
            journal.Write("changed!"u8);
            journal.Position = 0;
            byte[] pending = new byte[journal.Length];
            await journal.ReadExactlyAsync(pending);
            Assert.Equal("changed!", System.Text.Encoding.UTF8.GetString(pending));

            // Rollback discards the pending journal and restores the stream to Ready.
            await journal.Rollback();
            Assert.Equal(JournaledStreamState.Ready, journal.State);
        }

        Assert.Equal("original", await File.ReadAllTextAsync(originPath));
        Assert.False(File.Exists(journalPath));
    }

    [Fact]
    public async Task ReopeningAppliesACommittedJournalAfterRestart()
    {
        using TemporaryDirectory files = new();
        string originPath = files.PathFor("document.txt");
        string journalPath = files.PathFor("document.wal");

        await using (FileStream origin = OpenOrigin(originPath))
        await using (JournaledStream journal =
                     await JournaledStreamFactory.CreateWalJournal(origin, journalPath))
        {
            journal.Write("recover me"u8);

            // Simulate a process stopping after the durable decision but before Apply.
            await journal.Commit();
            Assert.Equal(0, origin.Length);
        }

        Assert.True(File.Exists(journalPath));

        await using (FileStream origin = OpenOrigin(originPath))
        await using (JournaledStream journal =
                     await JournaledStreamFactory.CreateWalJournal(origin, journalPath))
        {
            // The default open mode recognizes the committed journal and applies it before
            // returning, so callers see the recovered origin immediately.
            Assert.Equal(JournaledStreamState.Ready, journal.State);
        }

        Assert.Equal("recover me", await File.ReadAllTextAsync(originPath));
        Assert.False(File.Exists(journalPath));
    }

    [Fact]
    public async Task CommitTwoFilesAsOneCoordinatedOperation()
    {
        using TemporaryDirectory files = new();
        string originAPath = files.PathFor("accounts.dat");
        string originBPath = files.PathFor("ledger.dat");
        string journalAPath = files.PathFor("accounts.wal");
        string journalBPath = files.PathFor("ledger.sparse");
        string witnessPath = files.PathFor("transaction.witness");

        await using (FileStream originA = OpenOrigin(originAPath))
        await using (FileStream originB = OpenOrigin(originBPath))
        await using (JournaledStream streamA = await JournaledStreamFactory.CreateWalJournal(
                         originA, journalAPath, JournalOpenMode.Coordinated))
        await using (JournaledStream streamB = await JournaledStreamFactory.CreateSparseJournal(
                         originB, journalBPath, openMode: JournalOpenMode.Coordinated))
        {
            // The production witness is a small durable file recording the group's commit
            // decision. Reuse this same path when recovering after a restart.
            FileBasedJournalWitnessStore witness = new(witnessPath);
            JournaledStreamCoordinator coordinator =
                await JournaledStreamCoordinator.CreateAsync([streamA, streamB], witness);

            streamA.Write("account update"u8);
            streamB.Write("ledger update"u8);

            // The coordinator prepares every participant, durably witnesses the decision,
            // commits every journal, and only then applies either origin.
            await coordinator.CommitAsync();
            Assert.Equal(JournaledStreamState.Ready, streamA.State);
            Assert.Equal(JournaledStreamState.Ready, streamB.State);
        }

        Assert.Equal("account update", await File.ReadAllTextAsync(originAPath));
        Assert.Equal("ledger update", await File.ReadAllTextAsync(originBPath));
        Assert.False(File.Exists(journalAPath));
        Assert.False(File.Exists(journalBPath));
        Assert.False(File.Exists(witnessPath));
    }

    private static FileStream OpenOrigin(string path) =>
        new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
            4096, FileOptions.Asynchronous);

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string _path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "journal-examples-" + Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(_path);

        public string PathFor(string fileName) => System.IO.Path.Combine(_path, fileName);

        public void Dispose() => Directory.Delete(_path, true);
    }
}
