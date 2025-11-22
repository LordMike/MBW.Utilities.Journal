using System.Diagnostics.CodeAnalysis;

namespace MBW.Utilities.Journal;

public interface IJournalStreamFactory
{
    bool Exists(string identifier);
    void Delete(string identifier);

    [Obsolete]
    Stream OpenOrCreate(string identifier);

    bool TryOpen(string identifier, bool createIfMissing, [NotNullWhen(true)] out Stream? stream);
}