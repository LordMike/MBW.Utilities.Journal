using MBW.Utilities.Journal.Tests.Helpers;

namespace MBW.Utilities.Journal.Tests;

public abstract class TestsBase
{
    protected TestStream TestFile { get; }
    protected TestStream JournalFile { get; }
    protected MemoryJournal JournalFileProvider { get; }

    protected TestsBase()
    {
        TestFile = new TestStream();
        JournalFile = new TestStream();
        JournalFileProvider = new MemoryJournal(JournalFile);
    }

    private void ResetFileOffsets()
    {
        TestFile.Seek(0, SeekOrigin.Begin);
        JournalFile.Seek(0, SeekOrigin.Begin);

        TestFile.Lock = false;
        JournalFile.Lock = false;
    }

    protected void RunScenario(Action @delegate, Type? expectedException = null)
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