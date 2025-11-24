using MBW.Utilities.Journal.Abstracts;
using MBW.Utilities.Journal.Exceptions;
using MBW.Utilities.Journal.Helpers;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal.WalJournal;

public sealed class WalJournalFactory() : JournalFactoryBase((byte)JournalImplementation.WalJournal)
{
    protected override IJournal Create(Stream origin, Stream journal, JournalFileHeader header)
    {
        return new WalJournal(origin, journal, header);
    }

    protected override IJournal Open(Stream origin, Stream journal, JournalFileHeader header)
    {
        journal.Seek(-WalJournalFooter.StructSize, SeekOrigin.End);
        if (!JournaledStreamHelpers.TryRead(journal, WalJournalFooter.ExpectedMagic,
                out WalJournalFooter footer))
            throw new JournalCorruptedException("The journal, which should be committed and complete, did not have the required footer. It is likely corrupt.", false);

        if (header.Nonce != footer.HeaderNonce)
            throw new JournalCorruptedException("Journal header was corrupted, footer did not match headers info", false);

        return new WalJournal(origin, journal, header, footer);
    }
}