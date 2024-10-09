using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using MBW.Utilities.Journal.Extensions;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal.SparseJournal;

internal sealed class SparseJournalStream : JournaledStream
{
    private Stream? _sparseJournal;
    private List<ulong>? _sparseBitmap;

    private readonly byte _blockSize;
    private readonly uint _blockSizeBytes;
    private readonly ulong _journalNonceValue;

    public SparseJournalStream(Stream origin, IJournalStreamFactory journalStreamFactory, byte blockSize = 12) : base(origin, journalStreamFactory)
    {
        if (blockSize is < 10 or > 24)
            throw new ArgumentOutOfRangeException(nameof(blockSize), blockSize, "The blocksize should be in the range 10..24");

        _blockSize = blockSize;
        _blockSizeBytes = (uint)(1 << blockSize);
        _journalNonceValue = unchecked((ulong)Random.Shared.NextInt64());
    }

    private long GetJournalOffset(long originOffset) => originOffset + _blockSizeBytes;

    private Span<byte> GetBitmapBytes()
    {
        Span<ulong> bitmapSpan = CollectionsMarshal.AsSpan(_sparseBitmap);
        return MemoryMarshal.AsBytes(bitmapSpan);
    }

    [MemberNotNullWhen(true, nameof(_sparseJournal), nameof(_sparseBitmap))]
    protected override bool IsJournalOpened(bool openIfClosed)
    {
        if (_sparseJournal != null)
        {
            Debug.Assert(_sparseBitmap != null);
            return true;
        }

        if (!openIfClosed)
            return false;

        _sparseJournal = JournalStreamFactory.OpenOrCreate(string.Empty);
        _sparseBitmap = new List<ulong>();

        JournalFileHeader value = new JournalFileHeader
        {
            Magic = JournalFileConstants.HeaderMagic,
            Nonce = _journalNonceValue,
            Strategy = JournalStrategy.SparseFile
        };
        _sparseJournal.Write(value.AsSpan());

        if (Origin.CanRead && _sparseJournal is not { CanRead: true, CanSeek: true })
            throw new InvalidOperationException("Journal for a readable stream must be able to read and seek");
        if (Origin.CanWrite && _sparseJournal is not { CanWrite: true })
            throw new InvalidOperationException("Journal for a writeable stream must be able to write");

        return true;
    }

    public override void Commit()
    {
        if (!IsJournalOpened(false))
        {
            // No writes have happened
            return;
        }

        // Dump bitmap to journal
        _sparseJournal.Seek(0, SeekOrigin.End);
        ulong startOfBitmap = (ulong)_sparseJournal.Length;

        _sparseJournal.Write(GetBitmapBytes());

        // Dump footer
        SparseJournalFooter value = new SparseJournalFooter
        {
            Magic = SparseJournalFileConstants.SparseJournalFooterMagic,
            HeaderNonce = _journalNonceValue,
            FinalLength = VirtualLength,
            BlockSize = _blockSize,
            BlockCount = (uint)_sparseBitmap.Count,
            StartOfBitmap = startOfBitmap,
        };

        _sparseJournal.Write(value.AsSpan());
        _sparseJournal.Flush();

        _sparseJournal.Seek(0, SeekOrigin.Begin);
        SparseJournalHelpers.ApplyJournal(Origin, _sparseJournal);

        // The journal has been applied
        DeleteAndResetJournal();
    }

    public override void Rollback()
    {
        DeleteAndResetJournal();

        // Reset stream back to what it was initially
        VirtualLength = Origin.Length;
        VirtualOffset = Math.Clamp(VirtualOffset, 0, VirtualLength);
    }

    private void DeleteAndResetJournal()
    {
        if (!IsJournalOpened(false))
        {
            // No writes have happened
            return;
        }

        // Delete journal
        _sparseJournal.Close();
        _sparseJournal = null;
        _sparseBitmap = null;

        JournalStreamFactory.Delete(string.Empty);
    }

    public override void Flush()
    {
        Origin.Flush();

        if (IsJournalOpened(false))
            _sparseJournal.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (!CanRead)
            throw new ArgumentException("The underlying stream for this " + nameof(SparseJournalStream) + " is unreadable");

        // Read the original stream first
        int read = 0;
        Span<byte> tmpBuffer = buffer.AsSpan().Slice(offset, count);

        long readFromInner = Math.Clamp(Origin.Length - VirtualOffset, 0, count);
        if (readFromInner > 0)
        {
            Origin.Seek(VirtualOffset, SeekOrigin.Begin);
            read = Origin.Read(tmpBuffer);
        }

        if (!IsJournalOpened(false))
        {
            VirtualOffset += read;
            return read;
        }

        // Prepare buffer in multiple of blockSize bytes
        // Read in origin
        // Copy over already journaled data

        // Copy buffer to target


        var readFirstBlock = VirtualOffset / _blockSizeBytes;

        // Patch up the read data, with any journaled data
        var bitmapBytes = GetBitmapBytes();

        // Calculate the number of bytes read
        // This will be from the offset we had, until the length of the stream or count of bytes wanted, whatever comes first
        // long availableBytes = VirtualLength - thisRead.Start;
        // return (int)Math.Min(availableBytes, thisRead.Length);

        throw new NotImplementedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        // Prepare buffer in multiple of blockSize bytes
        var alignedOffset = (VirtualOffset / _blockSizeBytes) * _blockSizeBytes;
        var blockSizeAlignedSize = (count + _blockSizeBytes - 1) / _blockSizeBytes;
        byte[] tmpBuffer = new byte[blockSizeAlignedSize];

        // Read in origin
        var originToRead = Math.Clamp(Origin.Length - VirtualOffset, 0, tmpBuffer.Length);

        if (originToRead > 0)
        {
            Origin.Seek(alignedOffset, SeekOrigin.Begin);
            Origin.ReadUpTo(tmpBuffer);
        }

        // Copy over already journaled data
        var journalToRead = Math.Clamp(Jourstre.Length - VirtualOffset, 0, tmpBuffer.Length);

        // Copy over source buffer
        // Write out
        // Mark bitmap
    }

    private void ReadJournaledBlocks(Span<byte> buffer, long alignedOffset)
    {
        Debug.Assert(alignedOffset % _blockSizeBytes == 0);
        Debug.Assert(buffer.Length % _blockSizeBytes == 0);

        // Read in origin
        var originToRead = Math.Clamp(Origin.Length - alignedOffset, 0, buffer.Length);

        if (originToRead > 0)
        {
            Origin.Seek(alignedOffset, SeekOrigin.Begin);
            Origin.ReadUpTo(buffer);
        }

        // Copy over already journaled data
        if (IsJournalOpened(false))
        {
            // TODO: Check if any blocks are modified before reading
            
            var journalOffset = GetJournalOffset(alignedOffset);
            var journalToRead = Math.Clamp(_sparseJournal.Length - journalOffset, 0, buffer.Length);
            if (journalToRead > 0)
            {
                Span<byte> journalBuffer = new byte[journalToRead];
                _sparseJournal.Seek(journalOffset, SeekOrigin.Begin);
                var read = _sparseJournal.ReadUpTo(journalBuffer);
                
                // Copy over 
            }
        }

        // Copy over source buffer
        // Write out
        // Mark bitmap
    }
}