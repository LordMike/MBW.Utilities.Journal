using System.Runtime.InteropServices;
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
        // Assumes the base class already wrote the header, journal is empty otherwise, and the origin is ready for reading from start.
        journal.Seek(JournalFileHeader.StructSize, SeekOrigin.Begin);
        origin.Seek(0, SeekOrigin.Begin);
        origin.CopyTo(journal);

        return new FullCopyJournal(origin, journal, header, origin.Length);
    }

    protected override IJournal Open(Stream origin, Stream journal, JournalFileHeader header)
    {
        // Assumes the journal exists, is committed, and ends with our footer; no extra validation to keep it small.
        Span<byte> footerBytes = stackalloc byte[FullCopyJournalFooter.StructSize];
        journal.Seek(-FullCopyJournalFooter.StructSize, SeekOrigin.End);
        journal.ReadExactly(footerBytes);
        FullCopyJournalFooter footer = MemoryMarshal.AsRef<FullCopyJournalFooter>(footerBytes);

        return new FullCopyJournal(origin, journal, header, footer.FinalLength);
    }
}
