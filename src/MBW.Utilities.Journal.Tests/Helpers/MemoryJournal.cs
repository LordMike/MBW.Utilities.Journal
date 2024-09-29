namespace MBW.Utilities.Journal.Tests.Helpers;

public sealed class MemoryJournal : IJournalStream
{
    private readonly Dictionary<string, TestStream> _streams = new(StringComparer.Ordinal);

    public IEnumerable<(string, TestStream)> Streams => _streams.Select(s => (s.Key, s.Value));

    public bool Exists(string identifier) => _streams.ContainsKey(identifier);

    public void Delete(string identifier)
    {
        if (_streams.Remove(identifier, out var removed))
            removed.Lock = true;
    }

    public Stream OpenOrCreate(string identifier)
    {
        if (_streams.TryGetValue(identifier, out var stream))
        {
            stream.Seek(0, SeekOrigin.Begin);
            return stream;
        }

        stream = new TestStream();
        _streams.Add(identifier, stream);
        return stream;
    }
}