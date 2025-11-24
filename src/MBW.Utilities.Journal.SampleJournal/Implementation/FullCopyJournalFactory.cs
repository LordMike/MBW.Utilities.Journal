using MBW.Utilities.Journal.Abstracts;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal.SampleJournal.Implementation;

/// <summary>
/// Extremely simple journal that snapshots the entire origin into the journal stream once, then reads/writes against that copy.
/// Inefficient on purpose, but demonstrates how to plug in a custom provider.
/// </summary>
public sealed class FullCopyJournalFactory() : JournalFactoryBase(21)
{
    protected override IJournal Create(Stream origin, Stream journal, JournalFileHeader header)
    {
        var originalJournalPosition = journal.Position;

        // This method assumes the Journal header has already been written, and that we're at the correct position
        // Our FullCopy journal begins by copying the entire Origin stream into our journal
        origin.Seek(0, SeekOrigin.Begin);
        origin.CopyTo(journal);

        // Now the journal stream is ready, future edits will go to the Journal in our FullCopyJournal implementation
        return new FullCopyJournal(origin, journal, originalJournalPosition, origin.Length);
    }

    protected override IJournal Open(Stream origin, Stream journal, JournalFileHeader header)
    {
        var originalJournalPosition = journal.Position;

        // This method assumes the journal exists and is committed
        // We read the footer, to get the info our FullCopyJournal needs
        var footer = ReadFooterStruct<FullCopyJournalFooter, ulong>(journal);

        return new FullCopyJournal(origin, journal, originalJournalPosition, footer.FinalLength);
    }
}