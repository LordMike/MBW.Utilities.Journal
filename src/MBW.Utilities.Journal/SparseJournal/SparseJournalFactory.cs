using System.Diagnostics;
using System.Runtime.InteropServices;
using MBW.Utilities.Journal.Abstracts;
using MBW.Utilities.Journal.Exceptions;
using MBW.Utilities.Journal.Helpers;
using MBW.Utilities.Journal.Primitives;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal.SparseJournal;

public sealed class SparseJournalFactory : JournalFactoryBase
{
    private readonly BlockSize _blockSize;

    public SparseJournalFactory(byte blockSize = 12) : base((byte)JournalImplementation.SparseJournal)
    {
        // The minimum size we allow, is 5 (2^5 = 32 bytes), as this is larger than our JournalFileHeader
        // I don't know what a max should be, we will potentially use a few times 2^blockSize memory, so it will likely fail with OOMs if the blockSize is too high
        if (blockSize >= 63 || (1UL << blockSize) < (ulong)JournalFileHeader.StructSize)
            throw new ArgumentOutOfRangeException(nameof(blockSize),
                $"The block size must fit the {JournalFileHeader.StructSize}-byte journal header.");
        
        _blockSize = BlockSize.FromPowerOfTwo(blockSize);
    }

    protected override IJournal Create(Stream origin, Stream journal, JournalFileHeader header)
    {
        // For Windows, try making this stream a sparse file. If the original stream is not a File, we're still
        // writing in distinct locations, so if the underlying Stream supports that, we're still "sparse".
        TryMakeStreamSparse(journal);

        return new SparseJournal(origin, journal, _blockSize, header);
    }

    protected override IJournal Open(Stream origin, Stream journal, JournalFileHeader header)
    {
        journal.Seek(-SparseJournalFooter.StructSize, SeekOrigin.End);
        if (!JournaledStreamHelpers.TryRead(journal, SparseJournalFooter.ExpectedMagic,
                out SparseJournalFooter footer))
            throw new JournalCorruptedException("The journal, which should be committed and complete, did not have the required footer. It is likely corrupt.", false);

        if (header.Nonce != footer.HeaderNonce)
            throw new JournalCorruptedException("Journal header was corrupted, footer did not match headers info",
                false);
        ulong persistedBlockSize = footer.BlockSize < 63 ? 1UL << footer.BlockSize : 0;
        ulong journalPayloadEnd = (ulong)(journal.Length - SparseJournalFooter.StructSize);
        bool invalidBounds = header.FinalLength != footer.FinalLength ||
                             persistedBlockSize < (ulong)JournalFileHeader.StructSize ||
                             footer.StartOfBitmap > journalPayloadEnd ||
                             footer.BitmapLengthUlongs > int.MaxValue;

        if (!invalidBounds && footer.BitmapLengthUlongs == 0)
        {
            invalidBounds = footer.StartOfBitmap != (ulong)JournalFileHeader.StructSize;
        }
        else if (!invalidBounds)
        {
            invalidBounds = footer.StartOfBitmap < persistedBlockSize * 2 ||
                            footer.StartOfBitmap % persistedBlockSize != 0;
            if (!invalidBounds)
            {
                ulong journalDataBlocks = footer.StartOfBitmap / persistedBlockSize - 1;
                ulong expectedBitmapLength = (journalDataBlocks + 63) / 64;
                invalidBounds = expectedBitmapLength != footer.BitmapLengthUlongs;
            }
        }

        invalidBounds |= footer.StartOfBitmap + (ulong)footer.BitmapLengthUlongs * sizeof(ulong) !=
                         journalPayloadEnd;
        if (invalidBounds)
            throw new JournalCorruptedException("The sparse journal footer contains invalid bounds", false);

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

    private static unsafe void TryMakeStreamSparse(Stream journal)
    {
        if (journal is not FileStream asFileStream || !OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
            return;

        int bytesReturned = 0;
        bool result;
        result = Windows.Win32.PInvoke.DeviceIoControl(
            asFileStream.SafeFileHandle,
            Windows.Win32.PInvoke.FSCTL_SET_SPARSE,
            null,
            0,
            null,
            0,
            (uint*)&bytesReturned,
            null);

        Debug.Assert(result);
    }
}
