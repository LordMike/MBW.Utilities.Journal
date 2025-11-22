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
        TestFile.Lock = false;

        foreach ((string? _, var testStream) in JournalFileProvider.Streams)
        {
            testStream.Seek(0, SeekOrigin.Begin);
            testStream.Lock = false;
        }
    }

    protected void RunScenario(Action @delegate)
    {
        ResetFileOffsets();
        @delegate();
        ResetFileOffsets();
    }

    protected TException RunScenario<TException>(Action @delegate) where TException : Exception
    {
        ResetFileOffsets();
        TException except = Assert.Throws<TException>(@delegate);
        ResetFileOffsets();

        return except;
    }
}