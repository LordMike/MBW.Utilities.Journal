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

    private Span<byte> GetBitmapBytes() => MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(_sparseBitmap));

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
            BitmapLengthUlongs = (uint)_sparseBitmap.Count,
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
        // TODO: Implement ReadOnlySpan read/write overloads
        // TODO: Use bitwise ANDs to align to blocksize
        // TODO: Use input buffers if possible, allow smaller than blocksize?
        // TODO: Calculate ranges of dirty when read, avoid blockwise read; wait till "not dirty" to read, to simplify code
        if (!CanRead)
            throw new ArgumentException("The underlying stream for this " + nameof(SparseJournalStream) + " is unreadable");

        int maxToRead = (int)(Math.Min(VirtualOffset + count, VirtualLength) - VirtualOffset);
        if (maxToRead <= 0)
            return 0;

        // Prepare an aligned buffer to do block-sized reads
        long alignedSize = (int)((maxToRead + _blockSizeBytes - 1) / _blockSizeBytes) * (int)_blockSizeBytes;
        long alignedOffset = VirtualOffset / _blockSizeBytes;
        Span<byte> alignedBuffer = new byte[alignedSize]; // TODO: Use buffer parameter if it matches exactly, to support aligned reads like BufferStream

        Debug.Assert(alignedOffset + alignedBuffer.Length > VirtualOffset + maxToRead, "Ensure we have enough space to cover the virtual read");

        // Read from the original stream and overlay the original
        ReadAlignedBlocks(alignedBuffer, alignedOffset);

        // Copy over to buffer
        Span<byte> alignedView = alignedBuffer.Slice((int)(VirtualOffset - alignedOffset));
        alignedView = alignedView.Slice(0, maxToRead);

        Span<byte> targetBuffer = buffer.AsSpan().Slice(offset, maxToRead);
        alignedView.CopyTo(targetBuffer);

        VirtualOffset += maxToRead;
        return maxToRead;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        // Prepare buffer in multiple of blockSize bytes
        long alignedOffset = (VirtualOffset / _blockSizeBytes) * _blockSizeBytes;
        int alignedSize = (int)((count + _blockSizeBytes - 1) / _blockSizeBytes) * (int)_blockSizeBytes;
        Span<byte> alignedBuffer = new byte[alignedSize];

        // Read in origin and overlay the journaled data
        ReadAlignedBlocks(alignedBuffer, alignedOffset);

        // Copy over source buffer
        buffer.AsSpan(offset, count).CopyTo(alignedBuffer.Slice((int)(VirtualOffset - alignedOffset), count));

        // Write out to journal
        if (IsJournalOpened(true))
        {
            long journalOffset = GetJournalOffset(alignedOffset);
            _sparseJournal.Seek(journalOffset, SeekOrigin.Begin);
            _sparseJournal.Write(alignedBuffer);

            // Mark all affected blocks as dirty
            uint firstBlock = (uint)(alignedOffset / _blockSizeBytes);
            uint lastBlock = (uint)((alignedOffset + alignedSize) / _blockSizeBytes);

            for (uint block = firstBlock; block < lastBlock; block++)
            {
                SetDirty(block);
            }
        }

        VirtualOffset += count;
        VirtualLength = Math.Max(VirtualLength, VirtualOffset);
    }

    private void ReadAlignedBlocks(Span<byte> buffer, long alignedOffset)
    {
        Debug.Assert(alignedOffset % _blockSizeBytes == 0);
        Debug.Assert(buffer.Length % _blockSizeBytes == 0);
        Debug.Assert(_sparseJournal == null || _sparseBitmap!.Count == 0 || _sparseJournal.Length % _blockSizeBytes == 0);

        // Read in origin
        long originToRead = Math.Clamp(Origin.Length - alignedOffset, 0, buffer.Length);

        if (originToRead > 0)
        {
            Origin.Seek(alignedOffset, SeekOrigin.Begin);
            Origin.ReadUpTo(buffer);
        }

        // Copy over already journaled data
        if (IsJournalOpened(false))
        {
            long journalOffset = GetJournalOffset(alignedOffset);
            uint block = (uint)(alignedOffset / _blockSizeBytes);

            // Truncate buffer to what the journal has, so we don't need to worry about reading outside the journal. This may produce 0, that's fine
            Span<byte> journalBuffer = buffer.Slice(0, (int)Math.Clamp(_sparseJournal.Length - journalOffset, 0, buffer.Length));

            for (uint i = 0; i < journalBuffer.Length; i += _blockSizeBytes)
            {
                if (IsDirty(block))
                {
                    _sparseJournal.Seek(journalOffset, SeekOrigin.Begin);
                    _sparseJournal.ReadUpTo(journalBuffer.Slice((int)i, (int)_blockSizeBytes));
                }

                block++;
                journalOffset += _blockSizeBytes;
            }
        }
    }

    private bool IsDirty(uint block)
    {
        Debug.Assert(_sparseBitmap != null);

        if (block >= _sparseBitmap.Count * 64)
            return false;

        int index = (int)(block / 64);
        int bit = (int)(block % 64);

        return (_sparseBitmap[index] & (1UL << bit)) != 0;
    }

    private void SetDirty(uint block)
    {
        Debug.Assert(_sparseBitmap != null);

        int index = (int)(block / 64);
        int bit = (int)(block % 64);

        while (_sparseBitmap.Count <= index)
            _sparseBitmap.Add(0);

        _sparseBitmap[index] |= (1UL << bit);
    }
}