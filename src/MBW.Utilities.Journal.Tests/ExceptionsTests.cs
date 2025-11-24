using MBW.Utilities.Journal.Exceptions;

namespace MBW.Utilities.Journal.Tests;

public class ExceptionsTests : TestsBase
{
    [Fact]
    public async Task TestOtherImplementation()
    {
        await using (var walJournal = await JournaledStreamFactory.CreateWalJournal(TestFile, JournalFileProvider))
        {
            walJournal.Write("Hello"u8);
            await walJournal.Commit(false);
        }

        await Assert.ThrowsAsync<JournalIncorrectImplementationException>(() =>
            JournaledStreamFactory.CreateSparseJournal(TestFile, JournalFileProvider));
    }
}