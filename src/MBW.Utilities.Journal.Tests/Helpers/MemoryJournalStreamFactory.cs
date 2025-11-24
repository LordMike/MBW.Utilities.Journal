using System.Diagnostics.CodeAnalysis;
using MBW.Utilities.Journal.Abstracts;

namespace MBW.Utilities.Journal.Tests.Helpers;

public sealed class MemoryJournalStreamFactory : IJournalStreamFactory
{
    private readonly Dictionary<string, TestStream> _streams = new(StringComparer.Ordinal);

    public IEnumerable<(string, TestStream)> Streams => _streams.Select(s => (s.Key, s.Value));

    public bool HasAnyJournal => _streams.Count > 0;

    public bool Exists(string identifier) => _streams.ContainsKey(identifier);

    public void Delete(string identifier)
    {
        _streams.Remove(identifier);
    }

    public bool TryOpen(string identifier, bool createIfMissing, [NotNullWhen(true)] out Stream? stream)
    {
        if (_streams.TryGetValue(identifier, out var tmpStream))
        {
            stream = tmpStream.GetStream();
            stream.Seek(0, SeekOrigin.Begin);
            return true;
        }

        if (!createIfMissing)
        {
            stream = null;
            return false;
        }

        tmpStream = new TestStream();
        _streams.Add(identifier, tmpStream);
        stream = tmpStream.GetStream();
        return true;
    }
}