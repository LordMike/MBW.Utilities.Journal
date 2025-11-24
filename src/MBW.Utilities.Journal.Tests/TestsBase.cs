using MBW.Utilities.Journal.Tests.Helpers;

namespace MBW.Utilities.Journal.Tests;

public abstract class TestsBase
{
    protected TestStream TestFileWrapper { get; } = new();
    protected Stream TestFile => TestFileWrapper.GetStream();
    protected MemoryJournalStreamFactory JournalFileProvider { get; } = new();

    private void ResetFileOffsets()
    {
        TestFile.Seek(0, SeekOrigin.Begin);
        foreach ((string? _, var testStream) in JournalFileProvider.Streams)
            testStream.GetStream().Seek(0, SeekOrigin.Begin);
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