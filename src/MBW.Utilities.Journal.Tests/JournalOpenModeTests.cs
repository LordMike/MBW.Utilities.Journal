using MBW.Utilities.Journal.Exceptions;
using MBW.Utilities.Journal.Tests.Helpers;

namespace MBW.Utilities.Journal.Tests;

public class JournalOpenModeTests : TestsBase
{
    [Fact]
    public async Task CommittedJournal_Applies_WhenFlagEnabled()
    {
        TestFile.WriteStr("Base");

        await RunScenarioAsync(async () =>
        {
            await using JournaledStream writer =
                await JournaledStreamFactory.CreateWalJournal(TestFile, JournalFileProvider, JournalOpenMode.ApplyCommittedJournals);

            writer.WriteStr("Pending");
            await writer.Commit(applyImmediately: false);
        });

        Assert.Equal("Base", TestFile.ReadFullStr());
        Assert.True(JournalFileProvider.HasAnyJournal);

        await RunScenarioAsync(async () =>
        {
            await using JournaledStream reader =
                await JournaledStreamFactory.CreateWalJournal(TestFile, JournalFileProvider, JournalOpenMode.ApplyCommittedJournals);

            Assert.Equal("Pending", reader.ReadFullStr());
            Assert.False(JournalFileProvider.HasAnyJournal);
        });

        Assert.False(JournalFileProvider.HasAnyJournal);
        Assert.Equal("Pending", TestFile.ReadFullStr());
    }

    [Fact]
    public async Task CommittedJournal_Throws_WhenApplyFlagDisabled()
    {
        TestFile.WriteStr("Base");

        await RunScenarioAsync(async () =>
        {
            await using JournaledStream writer =
                await JournaledStreamFactory.CreateWalJournal(TestFile, JournalFileProvider, JournalOpenMode.ApplyCommittedJournals);

            writer.WriteStr("Pending");
            await writer.Commit(applyImmediately: false);
        });

        JournalCommittedButNotAppliedException ex = await RunScenarioAsync<JournalCommittedButNotAppliedException>(async () =>
        {
            await using JournaledStream _ =
                await JournaledStreamFactory.CreateWalJournal(TestFile, JournalFileProvider, JournalOpenMode.None);
        });

        Assert.NotNull(ex);
        Assert.True(JournalFileProvider.HasAnyJournal);
        Assert.Equal("Base", TestFile.ReadFullStr());
    }

    [Fact]
    public async Task UncommittedJournal_Discarded_WhenFlagEnabled()
    {
        TestFile.WriteStr("Original");

        await RunScenarioAsync(async () =>
        {
            await using JournaledStream writer =
                await JournaledStreamFactory.CreateWalJournal(TestFile, JournalFileProvider, JournalOpenMode.ApplyCommittedJournals);

            writer.WriteStr("Uncommitted");
            // No commit
        });

        Assert.True(JournalFileProvider.HasAnyJournal);

        await RunScenarioAsync(async () =>
        {
            await using JournaledStream reader =
                await JournaledStreamFactory.CreateWalJournal(TestFile, JournalFileProvider, JournalOpenMode.DiscardUncommittedJournals);

            Assert.Equal("Original", reader.ReadFullStr());
            Assert.False(JournalFileProvider.HasAnyJournal);
        });

        Assert.Equal("Original", TestFile.ReadFullStr());
        Assert.False(JournalFileProvider.HasAnyJournal);
    }

    [Fact]
    public async Task UncommittedJournal_Throws_WhenDiscardFlagDisabled()
    {
        TestFile.WriteStr("Original");

        await RunScenarioAsync(async () =>
        {
            await using JournaledStream writer =
                await JournaledStreamFactory.CreateWalJournal(TestFile, JournalFileProvider, JournalOpenMode.ApplyCommittedJournals);

            writer.WriteStr("Uncommitted");
            // No commit
        });

        JournalCorruptedException ex = await RunScenarioAsync<JournalCorruptedException>(async () =>
        {
            await using JournaledStream _ =
                await JournaledStreamFactory.CreateWalJournal(TestFile, JournalFileProvider, JournalOpenMode.ApplyCommittedJournals);
        });

        Assert.NotNull(ex);
        Assert.True(JournalFileProvider.HasAnyJournal);
        Assert.Equal("Original", TestFile.ReadFullStr());
    }
}
