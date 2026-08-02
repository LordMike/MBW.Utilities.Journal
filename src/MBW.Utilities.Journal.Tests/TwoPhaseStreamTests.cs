using MBW.Utilities.Journal.Abstracts;
using MBW.Utilities.Journal.Exceptions;
using MBW.Utilities.Journal.Tests.Helpers;

namespace MBW.Utilities.Journal.Tests;

public class TwoPhaseStreamTests : TestsBase
{
    public delegate Task<JournaledStream> CreateDelegate(Stream origin, IJournalStreamFactory factory,
        JournalOpenMode mode = JournalOpenMode.Default);

    public static IEnumerable<object[]> Implementations()
    {
        yield return [(CreateDelegate)JournaledStreamFactory.CreateWalJournal];
        yield return [(CreateDelegate)JournaledStreamFactory.CreateSparseJournal];
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task PreparedJournalEnforcesKeyedTransitions(CreateDelegate create)
    {
        await using JournaledStream stream = await create(TestFile, JournalFileProvider);
        Assert.Equal(JournaledStreamState.Ready, stream.State);

        stream.WriteStr("prepared");
        Assert.Equal(JournaledStreamState.Dirty, stream.State);

        Guid key = Guid.NewGuid();
        await stream.Prepare(key);
        Assert.Equal(JournaledStreamState.Prepared, stream.State);
        Assert.Equal(key, stream.PreparationKey);
        Assert.Equal("prepared", stream.ReadFullStr());

        Assert.Throws<JournalInInvalidStateException>(() => stream.WriteStr("blocked"));
        await Assert.ThrowsAsync<JournalInInvalidStateException>(() => stream.Commit());
        await Assert.ThrowsAsync<JournalInInvalidStateException>(() => stream.Commit(Guid.NewGuid()));

        await stream.Commit(key);
        Assert.Equal(JournaledStreamState.CommittedButNotApplied, stream.State);
        await Assert.ThrowsAsync<JournalInInvalidStateException>(() => stream.Rollback());
        Assert.Empty(TestFile.ReadFullBytes());

        await stream.Apply();
        Assert.Equal(JournaledStreamState.Ready, stream.State);
        Assert.Null(stream.PreparationKey);
        Assert.Equal("prepared", TestFile.ReadFullStr());
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task ParameterlessCommitPersistsMarkerOnly(CreateDelegate create)
    {
        await using JournaledStream stream = await create(TestFile, JournalFileProvider);
        stream.WriteStr("delayed");

        await stream.Commit();

        Assert.Equal(JournaledStreamState.CommittedButNotApplied, stream.State);
        Assert.NotNull(stream.PreparationKey);
        Assert.Empty(TestFile.ReadFullBytes());

        await stream.Apply();
        Assert.Equal("delayed", TestFile.ReadFullStr());
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task PreparedJournalCanBeReopenedWithoutApplying(CreateDelegate create)
    {
        Guid key = Guid.NewGuid();
        await using (JournaledStream writer = await create(TestFile, JournalFileProvider))
        {
            writer.WriteStr("pending");
            await writer.Prepare(key);
        }

        await using JournaledStream reopened =
            await create(TestFile, JournalFileProvider, JournalOpenMode.Coordinated);

        Assert.Equal(JournaledStreamState.Prepared, reopened.State);
        Assert.Equal(key, reopened.PreparationKey);
        Assert.Empty(TestFile.ReadFullBytes());
        Assert.Equal("pending", reopened.ReadFullStr());

        await reopened.Commit(key);
        await reopened.Apply();
        Assert.Equal("pending", TestFile.ReadFullStr());
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task DirtyCrashRemnantIsRollbackOnly(CreateDelegate create)
    {
        await using (JournaledStream writer = await create(TestFile, JournalFileProvider))
            writer.WriteStr("unfinalized");

        await using JournaledStream reopened =
            await create(TestFile, JournalFileProvider, JournalOpenMode.Coordinated);
        Assert.Equal(JournaledStreamState.Dirty, reopened.State);
        Assert.False(reopened.CanRead);
        Assert.False(reopened.CanWrite);

        await reopened.Rollback();
        Assert.Equal(JournaledStreamState.Ready, reopened.State);
        Assert.False(JournalFileProvider.HasAnyJournal);
    }
}
