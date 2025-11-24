namespace MBW.Utilities.Journal.Tests.Helpers;

public sealed class TestStream
{
    private MemoryStream _stream = new();

    public MemoryStream GetStream()
    {
        if (!_stream.CanWrite)
        {
            // Re-open stream
            // A previous variation of this was a Stream which wrapped a MemoryStream, but didn't implement Close/Dispose.
            // It unfortunately had issues with Async, as we didn't implement every method conceivable.
            // This setup abuses that MemoryStram data is never truly gone, and that its a test so new arrays isn't really an issue
            _stream = new MemoryStream(_stream.ToArray());
        }

        return _stream;
    }
}