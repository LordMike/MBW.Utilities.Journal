using MBW.Utilities.Journal.Tests.Helpers;

namespace MBW.Utilities.Journal.Tests;

public abstract class TestsBase
{
    protected TestStream TestFile { get; }
    protected MemoryJournalStreamFactory JournalFileProvider { get; }

    protected TestsBase()
    {
        TestFile = new TestStream();
        JournalFileProvider = new MemoryJournalStreamFactory();
    }

    private void ResetFileOffsets()
    {
        TestFile.Seek(0, SeekOrigin.Begin);
        foreach ((string? _, var testStream) in JournalFileProvider.Streams)
        {
            testStream.Seek(0, SeekOrigin.Begin);
        }
    }

    protected Task RunScenarioAsync(Func<Task> @delegate)
    {
        ResetFileOffsets();
        Task task = @delegate();
        ResetFileOffsets();
        return task;
    }

    protected async Task<TException> RunScenarioAsync<TException>(Func<Task> @delegate) where TException : Exception
    {
        ResetFileOffsets();
        TException except = await Assert.ThrowsAsync<TException>(async () => await @delegate());
        ResetFileOffsets();

        return except;
    }
}
