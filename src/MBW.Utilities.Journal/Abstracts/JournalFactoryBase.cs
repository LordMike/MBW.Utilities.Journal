using System.Diagnostics;
using MBW.Utilities.Journal.Exceptions;
using MBW.Utilities.Journal.Extensions;
using MBW.Utilities.Journal.Helpers;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal.Abstracts;

public abstract class JournalFactoryBase(byte implementationId) : IJournalFactory
{
    protected abstract IJournal Create(Stream origin, Stream journal, JournalFileHeader header);
    protected abstract IJournal Open(Stream origin, Stream journal, JournalFileHeader header);

    public IJournal Create(Stream origin, Stream journal)
    {
        Debug.Assert(journal.Length == 0);

        JournalFileHeader header = new JournalFileHeader
        {
            Magic = JournalFileHeader.ExpectedMagic,
            Nonce = unchecked((ulong)Random.Shared.NextInt64()),
            ImplementationId = implementationId,
            Flags = JournalHeaderFlags.None
        };
        journal.Write(header.AsSpan());

        return Create(origin, journal, header);
    }

    public IJournal Open(Stream origin, Stream journal)
    {
        if (!JournaledStreamHelpers.TryRead(journal, JournalFileHeader.ExpectedMagic, out JournalFileHeader header))
            throw new JournalCorruptedException("The existing journal was not identified as a valid journal", false);

        if (header.Magic != JournalFileHeader.ExpectedMagic)
            throw new JournalCorruptedException("Journal header was corrupted", false);

        if ((header.Flags & JournalHeaderFlags.Committed) == 0)
            throw new JournalCorruptedException("Journal header indicates the journal was not committed", false);

        if (header.ImplementationId != implementationId)
            throw new JournalIncorrectImplementationException(header.ImplementationId, implementationId);

        return Open(origin, journal, header);
    }
}