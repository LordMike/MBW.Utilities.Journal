using System.Diagnostics.CodeAnalysis;

namespace MBW.Utilities.Journal.Abstracts;

public interface IJournalStreamFactory
{
    bool Exists(string identifier);
    void Delete(string identifier);

    bool TryOpen(string identifier, bool createIfMissing, [NotNullWhen(true)] out Stream? stream);

    Stream OpenOrCreate(string identifier)
    {
        if (!TryOpen(identifier, true, out var strm))
            throw new InvalidOperationException();

        return strm;
    }
}