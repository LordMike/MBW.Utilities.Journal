using System.Diagnostics;
using System.Runtime.InteropServices;
using MBW.Utilities.Journal.Abstracts;
using MBW.Utilities.Journal.Extensions;
using MBW.Utilities.Journal.Helpers;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal.SparseJournal;

internal sealed class SparseJournal : IJournal
{
    private readonly Stream _origin;
    private readonly Stream _journal;
    private readonly BlockSize _blockSize;
    private readonly JournalFileHeader _header;
    private readonly List<ulong> _sparseBitmap;
    private SparseJournalFooter? _footer;

    public SparseJournal(Stream origin, Stream journal, BlockSize blockSize, JournalFileHeader header)
    {
        _origin = origin;
        _journal = journal;
        _blockSize = blockSize;
        _header = header;
        _footer = null;
        _sparseBitmap = [];
    }

    public SparseJournal(Stream origin, Stream journal, BlockSize blockSize, JournalFileHeader header,
        SparseJournalFooter footer, List<ulong> bitmap)
    {
        _origin = origin;
        _journal = journal;
        _blockSize = blockSize;
        _header = header;
        _footer = footer;
        _sparseBitmap = bitmap;
    }

    public async ValueTask FinalizeJournal(long finalLength)
    {
        _journal.Seek(0, SeekOrigin.End);
        ulong startOfBitmap = (ulong)_journal.Length;

        Span<ulong> bitmapLongs = CollectionsMarshal.AsSpan(_sparseBitmap);
        Span<byte> readOnlySpan = MemoryMarshal.AsBytes(bitmapLongs);
        _journal.Write(readOnlySpan);

        SparseJournalFooter footer = new SparseJournalFooter
        {
            Magic = SparseJournalFileConstants.SparseJournalFooterMagic,
            HeaderNonce = _header.Nonce,
            FinalLength = finalLength,
            BlockSize = _blockSize.Power,
            BitmapLengthUlongs = (uint)_sparseBitmap.Count,
            StartOfBitmap = startOfBitmap,
        };

        _footer = footer;

        _journal.Write(footer.AsSpan());
        await _journal.FlushAsync();
    }

    public async ValueTask ApplyJournal()
    {
        Debug.Assert(_footer.HasValue);

        byte[] copyBuffer = new byte[4096];

        // Seek to begin of data
        _journal.Seek(_blockSize.Size, SeekOrigin.Begin);

        // Truncate the original to ensure it fits our desired length
        if (_origin.Length != _footer.Value.FinalLength)
            _origin.SetLength(_footer.Value.FinalLength);

        async Task CopyStreams(long blockIndex, long blocks)
        {
            // Copy over data from journal to inner stream for all blocks since the last block with data
            long originOffset = blockIndex * _blockSize.Size;
            long journalOffset = _blockSize.Size + originOffset;

            long finalLength = blocks * _blockSize.Size + originOffset;
            long lengthToCopy = Math.Min(finalLength, _footer.Value.FinalLength) - originOffset;

            Debug.Assert(lengthToCopy > 0);

            _journal.Seek(journalOffset, SeekOrigin.Begin);
            _origin.Seek(originOffset, SeekOrigin.Begin);

            for (long i = 0; i < lengthToCopy; i += copyBuffer.Length)
            {
                long remaining = lengthToCopy - i;
                Memory<byte> tmpBuffer = copyBuffer.AsMemory(0, (int)Math.Min(copyBuffer.Length, remaining));

                await _journal.ReadExactlyAsync(tmpBuffer);
                await _origin.WriteAsync(tmpBuffer);
            }
        }

        // Calculate the number of blocks to copy over. Note that we only have as many bitmap bits as there were blocks written from the start of the stream
        // So it may be that the files length does not correspond to the number of bits in the bitmap.
        long blocksCount = Math.Min(_blockSize.GetBlockCountRoundUp((ulong)_footer.Value.FinalLength),
            _sparseBitmap.Count * 8 * sizeof(ulong));

        // Apply sparse to the original
        long? lastBlockWithData = null;
        for (long blockIndex = 0; blockIndex < blocksCount; blockIndex++)
        {
            int bitmapIndex = (int)(blockIndex / 64);
            int bitPosition = (int)(blockIndex % 64);

            bool hasData = (_sparseBitmap[bitmapIndex] & (1UL << bitPosition)) != 0;

            if (hasData)
            {
                // If this block has data, update the last block with data
                if (!lastBlockWithData.HasValue)
                    lastBlockWithData = blockIndex;
            }
            else if (lastBlockWithData.HasValue)
            {
                // Copy over data from journal to inner stream for all blocks since the last block with data
                long blocksToCopy = blockIndex - lastBlockWithData.Value;
                await CopyStreams(lastBlockWithData.Value, blocksToCopy);

                lastBlockWithData = null;
            }
        }

        // Handle any remaining blocks to be copied
        if (lastBlockWithData.HasValue)
        {
            long blocksToCopy = blocksCount - lastBlockWithData.Value;
            await CopyStreams(lastBlockWithData.Value, blocksToCopy);
        }

        await _origin.FlushAsync();
    }

    public void Flush()
    {
        _journal.Flush();
    }

    public async ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (buffer.Length == 0)
            throw new ArgumentException("The read cannot be a 0-byte size", nameof(buffer));

        // TODO: Use input buffers if possible, allow smaller than blocksize?
        // TODO: Calculate ranges of dirty when read, avoid blockwise read; wait till "not dirty" to read, to simplify code

        if (buffer.Length <= 0)
            return 0;

        // Prepare an aligned buffer to do block-sized reads
        // The alignedSize must be large enough to cover both ends of an unaligned write
        long alignedOffset = (long)_blockSize.RoundDownToNearestBlock((ulong)offset);
        int alignedSize =
            (int)(_blockSize.RoundUpToNearestBlock((ulong)(offset + buffer.Length)) - (ulong)alignedOffset);

        // TODO: Use buffer parameter if it matches exactly, to support aligned reads like BufferStream
        Memory<byte> alignedBuffer = buffer;
        bool usedNewBuffer = false;
        if (alignedBuffer.Length != alignedSize)
        {
            alignedBuffer = new byte[alignedSize];
            usedNewBuffer = true;
        }

        Debug.Assert(alignedOffset + alignedBuffer.Length >= offset + buffer.Length,
            "Ensure we have enough space to cover the virtual read");

        // Read from the original stream and overlay the original
        await ReadAlignedBlocks(alignedBuffer, alignedOffset);

        // Copy over to buffer
        if (usedNewBuffer)
        {
            Memory<byte> alignedView = alignedBuffer.Slice((int)(offset - alignedOffset));
            alignedView = alignedView.Slice(0, buffer.Length);

            alignedView.CopyTo(buffer);
        }

        return buffer.Length;
    }

    public async ValueTask WriteAsync(long offset, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        if (buffer.Length == 0)
            return;

        // Prepare buffer in multiple of blockSize bytes
        // The alignedSize must be large enough to cover both ends of an unaligned write
        long alignedOffset = (long)_blockSize.RoundDownToNearestBlock((ulong)offset);
        int alignedSize = (int)(_blockSize.RoundUpToNearestBlock((ulong)(offset + buffer.Length)) -
                                (ulong)alignedOffset);
        Memory<byte> alignedBuffer = new byte[alignedSize];

        // Read in origin and overlay the journaled data
        await ReadAlignedBlocks(alignedBuffer, alignedOffset);

        // Copy over source buffer
        buffer.CopyTo(alignedBuffer.Slice((int)(offset - alignedOffset), buffer.Length));

        // Write out to journal
        long journalOffset = GetJournalOffset(alignedOffset);
        _journal.Seek(journalOffset, SeekOrigin.Begin);
        await _journal.WriteAsync(alignedBuffer, cancellationToken);

        // Mark all affected blocks as dirty
        uint firstBlock = _blockSize.GetBlockCountRoundUp((ulong)alignedOffset);
        uint lastBlock = _blockSize.GetBlockCountRoundUp((ulong)(alignedOffset + alignedSize));

        for (uint block = firstBlock; block < lastBlock; block++)
            SetDirty(block);
    }

    private long GetJournalOffset(long originOffset) => originOffset + _blockSize.Size;

    private async Task ReadAlignedBlocks(Memory<byte> buffer, long alignedOffset)
    {
        Debug.Assert(_blockSize.IsAligned((ulong)alignedOffset));
        Debug.Assert(_blockSize.IsAligned((ulong)buffer.Length));
        Debug.Assert(_sparseBitmap.Count == 0 || _blockSize.IsAligned((ulong)_journal.Length));

        // Read in origin
        long originToRead = Math.Clamp(_origin.Length - alignedOffset, 0, buffer.Length);

        if (originToRead > 0)
        {
            _origin.Seek(alignedOffset, SeekOrigin.Begin);
            await _origin.ReadUpToAsync(buffer);
        }

        // Copy over already journaled data
        long journalOffset = GetJournalOffset(alignedOffset);
        uint block = _blockSize.GetBlockCountRoundUp((ulong)alignedOffset);

        // Truncate buffer to what the journal has, so we don't need to worry about reading outside the journal. This may produce 0, that's fine
        Memory<byte> journalBuffer = buffer[..(int)Math.Clamp(_journal.Length - journalOffset, 0, buffer.Length)];
        Debug.Assert(_blockSize.IsAligned((ulong)journalBuffer.Length));

        for (uint i = 0; i < journalBuffer.Length; i += _blockSize.Size)
        {
            if (IsDirty(block))
            {
                _journal.Seek(journalOffset, SeekOrigin.Begin);
                await _journal.ReadUpToAsync(journalBuffer.Slice((int)i, (int)_blockSize.Size));
            }

            block++;
            journalOffset += _blockSize.Size;
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