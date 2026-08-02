using MBW.Utilities.Journal.Tests.Helpers;

namespace MBW.Utilities.Journal.Tests;

public class OriginDurabilityTests
{
    [Fact]
    public async Task ApplyRetainsJournalWhenDurableOriginFlushFails()
    {
        MemoryStream originData = new();
        FaultingFlushStream origin = new(originData);
        MemoryJournalStreamFactory journals = new();
        await using JournaledStream stream =
            await JournaledStreamFactory.CreateWalJournal(origin, journals);
        stream.Write("pending"u8);
        await stream.Commit();
        origin.FailOnFlushAsync = true;

        await Assert.ThrowsAsync<IOException>(() => stream.Apply());

        Assert.Equal(JournaledStreamState.CommittedButNotApplied, stream.State);
        Assert.True(journals.HasAnyJournal);

        origin.FailOnFlushAsync = false;
        await stream.Apply();
        Assert.Equal(JournaledStreamState.Ready, stream.State);
        Assert.False(journals.HasAnyJournal);
    }

    [Fact]
    public async Task AutomaticRecoveryRetainsJournalWhenDurableOriginFlushFails()
    {
        MemoryStream originData = new();
        FaultingFlushStream origin = new(originData);
        MemoryJournalStreamFactory journals = new();
        await using (JournaledStream writer =
                     await JournaledStreamFactory.CreateWalJournal(origin, journals))
        {
            writer.Write("pending"u8);
            await writer.Commit();
        }

        origin.FailOnFlushAsync = true;
        await Assert.ThrowsAsync<IOException>(() =>
            JournaledStreamFactory.CreateWalJournal(origin, journals));
        Assert.True(journals.HasAnyJournal);

        origin.FailOnFlushAsync = false;
        await using JournaledStream recovered =
            await JournaledStreamFactory.CreateWalJournal(origin, journals);
        Assert.Equal(JournaledStreamState.Ready, recovered.State);
        Assert.False(journals.HasAnyJournal);
    }

    [Fact]
    public async Task CoordinatorClearsWitnessBeforeApplyingOrigins()
    {
        MemoryStream originData = new();
        FaultingFlushStream origin = new(originData);
        MemoryJournalStreamFactory journals = new();
        MemoryJournalWitnessStore witness = new();
        await using JournaledStream stream =
            await JournaledStreamFactory.CreateWalJournal(origin, journals);
        JournaledStreamCoordinator coordinator =
            await JournaledStreamCoordinator.CreateAsync([stream], witness);
        stream.Write("pending"u8);
        origin.FailOnFlushAsync = true;

        await Assert.ThrowsAsync<IOException>(() => coordinator.CommitAsync());

        Assert.Equal(JournaledStreamState.CommittedButNotApplied, stream.State);
        Assert.Null(witness.Value);

        origin.FailOnFlushAsync = false;
        await coordinator.RecoverAsync();
        Assert.Equal(JournaledStreamState.Ready, stream.State);
        Assert.Null(witness.Value);
    }
}
