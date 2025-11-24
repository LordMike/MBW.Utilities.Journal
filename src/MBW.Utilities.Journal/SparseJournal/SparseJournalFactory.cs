using System.Diagnostics;
using System.Runtime.InteropServices;
using MBW.Utilities.Journal.Abstracts;
using MBW.Utilities.Journal.Exceptions;
using MBW.Utilities.Journal.Extensions;
using MBW.Utilities.Journal.Helpers;
using MBW.Utilities.Journal.Primitives;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal.SparseJournal;

public sealed class SparseJournalFactory(byte blockSize = 12) : IJournalFactory
{
    private readonly BlockSize _blockSize = BlockSize.FromPowerOfTwo(blockSize);

    public byte ImplementationId => (byte)JournalImplementation.SparseJournal;

    public IJournal Create(Stream origin, Stream journal)
    {
        Debug.Assert(journal.Length == 0);

        // For Windows, try making this stream a sparse file. If the original stream is not a File, we're still
        // writing in distinct locations, so if the underlying Stream supports that, we're still "sparse".
        TryMakeStreamSparse(journal);

        JournalFileHeader header = new JournalFileHeader
        {
            Magic = JournalFileHeader.ExpectedMagic,
            Nonce = unchecked((ulong)Random.Shared.NextInt64()),
            ImplementationId = ImplementationId,
            Flags = JournalHeaderFlags.None
        };
        journal.Write(header.AsSpan());

        return new SparseJournal(origin, journal, _blockSize, header);
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