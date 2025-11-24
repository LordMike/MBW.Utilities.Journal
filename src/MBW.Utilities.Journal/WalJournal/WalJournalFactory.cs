using System.Diagnostics;
using MBW.Utilities.Journal.Abstracts;
using MBW.Utilities.Journal.Exceptions;
using MBW.Utilities.Journal.Extensions;
using MBW.Utilities.Journal.Helpers;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal.WalJournal;

public sealed class WalJournalFactory : IJournalFactory
{
    public byte ImplementationId => (byte)JournalImplementation.WalJournal;

    public IJournal Create(Stream origin, Stream journal)
    {
        Debug.Assert(journal.Length == 0);

        JournalFileHeader header = new JournalFileHeader
        {
            Magic = JournalFileHeader.ExpectedMagic,
            Nonce = unchecked((ulong)Random.Shared.NextInt64()),
            ImplementationId = ImplementationId,
            Flags = JournalHeaderFlags.None
        };
        journal.Write(header.AsSpan());

        return new WalJournal(origin, journal, header);
    }

    public IJournal Open(Stream origin, Stream journal)
    {
        if (!JournaledStreamHelpers.TryRead(journal, JournalFileHeader.ExpectedMagic, out JournalFileHeader header))
            throw new InvalidOperationException();

        if (header.Magic != JournalFileHeader.ExpectedMagic)
            throw new JournalCorruptedException("Journal header was corrupted", false);

        if ((header.Flags & JournalHeaderFlags.Committed) == 0)
            throw new JournalCorruptedException("Journal header indicates the journal was not committed", false);

        if (header.ImplementationId != ImplementationId)
            throw new JournalIncorrectImplementationException(header.ImplementationId, ImplementationId);

        journal.Seek(-WalJournalFooter.StructSize, SeekOrigin.End);
        if (!JournaledStreamHelpers.TryRead(journal, WalJournalFooter.ExpectedMagic,
                out WalJournalFooter footer))
            throw new InvalidOperationException();

        if (header.Nonce != footer.HeaderNonce)
            throw new JournalCorruptedException("Journal header was corrupted, footer did not match headers info",
                false);

        return new WalJournal(origin, journal, header, footer);
    }
}