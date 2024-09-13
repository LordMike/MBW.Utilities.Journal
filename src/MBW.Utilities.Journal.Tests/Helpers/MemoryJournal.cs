namespace MBW.Utilities.Journal.Tests.Helpers;

public sealed class MemoryJournal(Stream underlyingStream) : IJournalStream
{
    private bool _exists;

    public bool Exists() => _exists;

    public void Delete()
    {
        underlyingStream.SetLength(0);
        _exists = false;
    }

    public Stream OpenOrCreate()
    {
        _exists = true;
        underlyingStream.Seek(0, SeekOrigin.Begin);
        return underlyingStream;
    }
}