using System.Diagnostics;
using MBW.Utilities.Journal.Abstracts;
using MBW.Utilities.Journal.Exceptions;
using MBW.Utilities.Journal.Extensions;
using MBW.Utilities.Journal.Helpers;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal.WalJournal;

internal sealed class WalJournalFactory : IJournalFactory
{
    public IJournal Create(Stream origin, Stream journal)
    {
        Debug.Assert(journal.Length == 0);

        JournalFileHeader header = new JournalFileHeader
        {
            Magic = JournalFileConstants.HeaderMagic,
            Nonce = unchecked((ulong)Random.Shared.NextInt64()),
            Strategy = JournalStrategy.WalJournalFile,
            Flags = JournalHeaderFlags.None
        };
        journal.Write(header.AsSpan());

        return new WalJournal(origin, journal, header);
    }

    public IJournal Open(Stream origin, Stream journal)
    {
        if (!JournaledStreamHelpers.TryRead(journal, JournalFileConstants.HeaderMagic, out JournalFileHeader header))
            throw new InvalidOperationException();

        if (header.Magic != JournalFileConstants.HeaderMagic)
            throw new JournalCorruptedException("Journal header was corrupted", false);

        if ((header.Flags & JournalHeaderFlags.Committed) == 0)
            throw new JournalCorruptedException("Journal header indicates the journal was not committed", false);

        journal.Seek(-WalJournalFooter.StructSize, SeekOrigin.End);
        if (!JournaledStreamHelpers.TryRead(journal, WalJournalFooter.ExpectedMagic,
                out WalJournalFooter footer))
            throw new InvalidOperationException();

        return new WalJournal(origin, journal, header, footer);
    }
}