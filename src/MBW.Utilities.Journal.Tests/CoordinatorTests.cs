using MBW.Utilities.Journal.Exceptions;
using MBW.Utilities.Journal.Abstracts;
using MBW.Utilities.Journal.Tests.Helpers;

namespace MBW.Utilities.Journal.Tests;

public class CoordinatorTests
{
    [Fact]
    public async Task CommitAppliesEveryParticipantAndClearsWitness()
    {
        TestStream originA = new();
        TestStream originB = new();
        MemoryJournalStreamFactory journalsA = new();
        MemoryJournalStreamFactory journalsB = new();
        MemoryJournalWitnessStore witness = new();

        await using JournaledStream streamA =
            await JournaledStreamFactory.CreateWalJournal(originA.GetStream(), journalsA);
        await using JournaledStream streamB =
            await JournaledStreamFactory.CreateSparseJournal(originB.GetStream(), journalsB);
        JournaledStreamCoordinator coordinator =
            await JournaledStreamCoordinator.CreateAsync([streamA, streamB], witness);

        streamA.WriteStr("alpha");
        streamB.WriteStr("beta");
        await coordinator.CommitAsync();

        Assert.Equal("alpha", originA.GetStream().ReadFullStr());
        Assert.Equal("beta", originB.GetStream().ReadFullStr());
        Assert.Equal(JournaledStreamState.Ready, streamA.State);
        Assert.Equal(JournaledStreamState.Ready, streamB.State);
        Assert.Null(witness.Value);
    }

    [Fact]
    public async Task RecoveryCommitsPreparedParticipantsBeforeAnyApply()
    {
        TestStream originA = new();
        TestStream originB = new();
        MemoryJournalStreamFactory journalsA = new();
        MemoryJournalStreamFactory journalsB = new();
        MemoryJournalWitnessStore witness = new();
        Guid key = Guid.NewGuid();

        await using (JournaledStream streamA =
                     await JournaledStreamFactory.CreateWalJournal(originA.GetStream(), journalsA))
        await using (JournaledStream streamB =
                     await JournaledStreamFactory.CreateSparseJournal(originB.GetStream(), journalsB))
        {
            streamA.WriteStr("alpha");
            streamB.WriteStr("beta");
            await streamA.Prepare(key);
            await streamB.Prepare(key);
            await witness.StoreAsync(key);
            await streamA.Commit(key);
        }

        await using JournaledStream reopenedA = await JournaledStreamFactory.CreateWalJournal(
            originA.GetStream(), journalsA, JournalOpenMode.Coordinated);
        await using JournaledStream reopenedB = await JournaledStreamFactory.CreateSparseJournal(
            originB.GetStream(), journalsB, JournalOpenMode.Coordinated);

        Assert.Empty(originA.GetStream().ReadFullBytes());
        Assert.Empty(originB.GetStream().ReadFullBytes());
        await JournaledStreamCoordinator.CreateAsync([reopenedA, reopenedB], witness);

        Assert.Equal("alpha", originA.GetStream().ReadFullStr());
        Assert.Equal("beta", originB.GetStream().ReadFullStr());
        Assert.Null(witness.Value);
    }

    [Fact]
    public async Task UnwitnessedPreparedTransactionRollsBackByDefault()
    {
        TestStream originA = new();
        TestStream originB = new();
        MemoryJournalStreamFactory journalsA = new();
        MemoryJournalStreamFactory journalsB = new();
        MemoryJournalWitnessStore witness = new();
        Guid key = Guid.NewGuid();

        await using (JournaledStream streamA =
                     await JournaledStreamFactory.CreateWalJournal(originA.GetStream(), journalsA))
        await using (JournaledStream streamB =
                     await JournaledStreamFactory.CreateSparseJournal(originB.GetStream(), journalsB))
        {
            streamA.WriteStr("alpha");
            streamB.WriteStr("beta");
            await streamA.Prepare(key);
            await streamB.Prepare(key);
        }

        await using JournaledStream reopenedA = await JournaledStreamFactory.CreateWalJournal(
            originA.GetStream(), journalsA, JournalOpenMode.Coordinated);
        await using JournaledStream reopenedB = await JournaledStreamFactory.CreateSparseJournal(
            originB.GetStream(), journalsB, JournalOpenMode.Coordinated);

        await JournaledStreamCoordinator.CreateAsync([reopenedA, reopenedB], witness);

        Assert.Equal(JournaledStreamState.Ready, reopenedA.State);
        Assert.Equal(JournaledStreamState.Ready, reopenedB.State);
        Assert.False(journalsA.HasAnyJournal);
        Assert.False(journalsB.HasAnyJournal);
    }

    [Fact]
    public async Task DisabledRecoveryReportsPreparedTransactionWithoutChangingIt()
    {
        TestStream origin = new();
        MemoryJournalStreamFactory journals = new();
        MemoryJournalWitnessStore witness = new();
        Guid key = Guid.NewGuid();

        await using JournaledStream stream =
            await JournaledStreamFactory.CreateWalJournal(origin.GetStream(), journals);
        stream.WriteStr("pending");
        await stream.Prepare(key);

        await Assert.ThrowsAsync<JournalRecoveryRequiredException>(() =>
            JournaledStreamCoordinator.CreateAsync([stream], witness, JournalCoordinatorRecoveryMode.None));

        Assert.Equal(JournaledStreamState.Prepared, stream.State);
        Assert.True(journals.HasAnyJournal);
        Assert.Empty(origin.GetStream().ReadFullBytes());
    }

    [Fact]
    public async Task StaleWitnessIsClearedWhenEveryParticipantIsReady()
    {
        TestStream origin = new();
        MemoryJournalStreamFactory journals = new();
        MemoryJournalWitnessStore witness = new();
        Guid key = Guid.NewGuid();
        await witness.StoreAsync(key);

        await using JournaledStream stream =
            await JournaledStreamFactory.CreateWalJournal(origin.GetStream(), journals);
        await JournaledStreamCoordinator.CreateAsync([stream], witness);

        Assert.Null(witness.Value);
    }

    [Theory]
    [InlineData(JournalCommitFailureMode.PreserveForRecovery, JournaledStreamState.Prepared)]
    [InlineData(JournalCommitFailureMode.RollbackIfSafe, JournaledStreamState.Ready)]
    public async Task WitnessStoreFailureHonorsFailurePolicy(
        JournalCommitFailureMode failureMode, JournaledStreamState expectedState)
    {
        TestStream origin = new();
        MemoryJournalStreamFactory journals = new();
        FailingStoreWitness witness = new();
        await using JournaledStream stream =
            await JournaledStreamFactory.CreateWalJournal(origin.GetStream(), journals);
        JournaledStreamCoordinator coordinator =
            await JournaledStreamCoordinator.CreateAsync([stream], witness);
        stream.WriteStr("pending");

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.CommitAsync(failureMode));

        Assert.Equal(expectedState, stream.State);
        Assert.Empty(origin.GetStream().ReadFullBytes());
        Assert.Equal(expectedState != JournaledStreamState.Ready, journals.HasAnyJournal);
    }

    [Theory]
    [InlineData(JournalCommitFailureMode.Unset)]
    [InlineData((JournalCommitFailureMode)99)]
    public async Task UndefinedFailurePolicyIsRejectedWithoutChangingParticipants(
        JournalCommitFailureMode failureMode)
    {
        TestStream origin = new();
        MemoryJournalStreamFactory journals = new();
        MemoryJournalWitnessStore witness = new();
        await using JournaledStream stream =
            await JournaledStreamFactory.CreateWalJournal(origin.GetStream(), journals);
        JournaledStreamCoordinator coordinator =
            await JournaledStreamCoordinator.CreateAsync([stream], witness);
        stream.WriteStr("pending");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => coordinator.CommitAsync(failureMode));

        Assert.Equal(JournaledStreamState.Dirty, stream.State);
        Assert.Null(witness.Value);
        Assert.True(journals.HasAnyJournal);
    }

    [Fact]
    public async Task CommittedMarkerCompletesRecoveryEvenWhenWitnessIsMissing()
    {
        TestStream originA = new();
        TestStream originB = new();
        MemoryJournalStreamFactory journalsA = new();
        MemoryJournalStreamFactory journalsB = new();
        MemoryJournalWitnessStore witness = new();
        Guid key = Guid.NewGuid();

        await using (JournaledStream streamA =
                     await JournaledStreamFactory.CreateWalJournal(originA.GetStream(), journalsA))
        await using (JournaledStream streamB =
                     await JournaledStreamFactory.CreateWalJournal(originB.GetStream(), journalsB))
        {
            streamA.WriteStr("alpha");
            streamB.WriteStr("beta");
            await streamA.Prepare(key);
            await streamB.Prepare(key);
            await streamA.Commit(key);
        }

        await using JournaledStream reopenedA = await JournaledStreamFactory.CreateWalJournal(
            originA.GetStream(), journalsA, JournalOpenMode.Coordinated);
        await using JournaledStream reopenedB = await JournaledStreamFactory.CreateWalJournal(
            originB.GetStream(), journalsB, JournalOpenMode.Coordinated);

        await JournaledStreamCoordinator.CreateAsync(
            [reopenedA, reopenedB], witness, JournalCoordinatorRecoveryMode.None);

        Assert.Equal("alpha", originA.GetStream().ReadFullStr());
        Assert.Equal("beta", originB.GetStream().ReadFullStr());
    }

    [Fact]
    public async Task ApplyWaitsUntilEveryCommitMarkerIsDurable()
    {
        TestStream originA = new();
        TestStream originB = new();
        MemoryJournalStreamFactory journalsA = new();
        FaultingJournalStreamFactory journalsB = new() { FailOnFlush = 5 };
        MemoryJournalWitnessStore witness = new();

        await using JournaledStream streamA =
            await JournaledStreamFactory.CreateWalJournal(originA.GetStream(), journalsA);
        await using JournaledStream streamB =
            await JournaledStreamFactory.CreateWalJournal(originB.GetStream(), journalsB);
        JournaledStreamCoordinator coordinator =
            await JournaledStreamCoordinator.CreateAsync([streamA, streamB], witness);
        streamA.WriteStr("alpha");
        streamB.WriteStr("beta");

        await Assert.ThrowsAsync<IOException>(() => coordinator.CommitAsync());

        Assert.Equal(JournaledStreamState.CommittedButNotApplied, streamA.State);
        Assert.Equal(JournaledStreamState.Prepared, streamB.State);
        Assert.Empty(originA.GetStream().ReadFullBytes());
        Assert.Empty(originB.GetStream().ReadFullBytes());
        Assert.NotNull(witness.Value);

        journalsB.FailOnFlush = null;
        await coordinator.RecoverAsync();
        Assert.Equal("alpha", originA.GetStream().ReadFullStr());
        Assert.Equal("beta", originB.GetStream().ReadFullStr());
        Assert.Null(witness.Value);
    }

    private sealed class FailingStoreWitness : IJournalWitnessStore
    {
        public ValueTask<Guid?> ReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Guid?>(null);

        public ValueTask StoreAsync(Guid preparationKey, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new IOException("Injected witness failure"));

        public ValueTask ClearAsync(Guid preparationKey, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
