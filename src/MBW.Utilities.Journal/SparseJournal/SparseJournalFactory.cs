using System.Diagnostics;
using System.Runtime.InteropServices;
using MBW.Utilities.Journal.Abstracts;
using MBW.Utilities.Journal.Exceptions;
using MBW.Utilities.Journal.Extensions;
using MBW.Utilities.Journal.Helpers;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal.SparseJournal;

internal sealed class SparseJournalFactory(byte blockSize = 12) : IJournalFactory
{
    private readonly BlockSize _blockSize = BlockSize.FromPowerOfTwo(blockSize);

    public IJournal Create(Stream origin, Stream journal)
    {
        Debug.Assert(journal.Length == 0);

        JournalFileHeader header = new JournalFileHeader
        {
            Magic = JournalFileConstants.HeaderMagic,
            Nonce = unchecked((ulong)Random.Shared.NextInt64()),
            Strategy = JournalStrategy.SparseFile,
            Flags = JournalHeaderFlags.None
        };
        journal.Write(header.AsSpan());

        return new SparseJournal(origin, journal, _blockSize, header);
    }

    public IJournal Open(Stream origin, Stream journal)
    {
        if (!JournaledStreamHelpers.TryRead(journal, JournalFileConstants.HeaderMagic, out JournalFileHeader header))
            throw new InvalidOperationException();

        if (header.Magic != JournalFileConstants.HeaderMagic)
            throw new JournalCorruptedException("Journal header was corrupted", false);

        if ((header.Flags & JournalHeaderFlags.Committed) == 0)
            throw new JournalCorruptedException("Journal header indicates the journal was not committed", false);

        journal.Seek(-SparseJournalFooter.StructSize, SeekOrigin.End);
        if (!JournaledStreamHelpers.TryRead(journal, SparseJournalFooter.ExpectedMagic,
                out SparseJournalFooter footer))
            throw new InvalidOperationException();

        if (header.Nonce != footer.HeaderNonce)
            throw new JournalCorruptedException("Journal header was corrupted, footer did not match headers info",
                false);

        // Read bitmap
        journal.Seek((long)footer.StartOfBitmap, SeekOrigin.Begin);

        List<ulong> bitmap = new List<ulong>((int)footer.BitmapLengthUlongs);
        
        // Allocate the N ulongs - TODO, find a better way than this
        for (int i = 0; i < footer.BitmapLengthUlongs; i++)
            bitmap.Add(0);
        
        Span<byte> bitmapBytes = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(bitmap));
        journal.ReadExactly(bitmapBytes);

        return new SparseJournal(origin, journal, BlockSize.FromPowerOfTwo(footer.BlockSize), header, footer, bitmap);
    }
}