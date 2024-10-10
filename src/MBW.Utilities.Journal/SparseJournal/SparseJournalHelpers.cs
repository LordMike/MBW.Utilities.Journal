using System.Diagnostics;
using System.Runtime.InteropServices;
using MBW.Utilities.Journal.Helpers;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal.SparseJournal;

internal static class SparseJournalHelpers
{
    internal static void ApplyJournal(Stream origin, Stream journal)
    {
        if (!JournaledStreamHelpers.TryRead(journal, JournalFileConstants.HeaderMagic, out JournalFileHeader header))
        {
            // Bad file
            throw new InvalidOperationException();
        }

        journal.Seek(-SparseJournalFooter.StructSize, SeekOrigin.End);
        if (!JournaledStreamHelpers.TryRead(journal, SparseJournalFileConstants.SparseJournalFooterMagic, out SparseJournalFooter footer))
        {
            // Bad file or not committed
            throw new InvalidOperationException();
        }

        if (header.Nonce != footer.HeaderNonce)
            throw new InvalidOperationException($"Header & footer does not match. Nonces: {header.Nonce:X8}, footer: {footer.HeaderNonce:X8}");

        ApplyJournal(origin, journal, header, footer);
    }

    internal static void ApplyJournal(Stream origin, Stream journal, JournalFileHeader header, SparseJournalFooter footer)
    {
        // Read bitmap
        journal.Seek((long)footer.StartOfBitmap, SeekOrigin.Begin);

        ulong[] bitmap = new ulong[footer.BitmapLengthUlongs];
        var bitmapBytes = MemoryMarshal.AsBytes<ulong>(bitmap);
        journal.ReadExactly(bitmapBytes);

        uint blockSizeBytes = (uint)(1 << footer.BlockSize);

        // Seek to begin of data
        journal.Seek(blockSizeBytes, SeekOrigin.Begin);

        // Truncate the original to ensure it fits our desired length
        bool targetHasBeenAltered = false;
        if (origin.Length != footer.FinalLength)
        {
            origin.SetLength(footer.FinalLength);
            targetHasBeenAltered = true;
        }

        void CopyStreams(long blockIndex, long blocks)
        {
            // Copy over data from journal to inner stream for all blocks since the last block with data
            long originOffset = blockIndex * blockSizeBytes;
            long journalOffset = blockSizeBytes + originOffset;

            long finalLength = blocks * blockSizeBytes + originOffset;
            long lengthToCopy = Math.Min(finalLength, footer.FinalLength) - originOffset;

            Debug.Assert(lengthToCopy > 0);

            journal.Seek(journalOffset, SeekOrigin.Begin);
            origin.Seek(originOffset, SeekOrigin.Begin);

            Span<byte> copyBuffer = stackalloc byte[4096];

            for (int i = 0; i < lengthToCopy; i += copyBuffer.Length)
            {
                long remaining = lengthToCopy - i;
                Span<byte> tmpBuffer = copyBuffer.Slice(0, (int)Math.Min(copyBuffer.Length, remaining));

                journal.ReadExactly(tmpBuffer);
                origin.Write(tmpBuffer);
            }

            targetHasBeenAltered = true;
        }

        // Calculate the number of blocks to copy over. Note that we only have as many bitmap bits as there were blocks written from the start of the stream
        // So it may be that the files length does not correspond to the number of bits in the bitmap.
        long blocksCount = Math.Min((footer.FinalLength + blockSizeBytes - 1) / blockSizeBytes, bitmap.Length * 8 * sizeof(ulong));
        
        // Apply sparse to the original
        long? lastBlockWithData = null;
        for (long blockIndex = 0; blockIndex < blocksCount; blockIndex++)
        {
            int bitmapIndex = (int)(blockIndex / 64);
            int bitPosition = (int)(blockIndex % 64);

            bool hasData = (bitmap[bitmapIndex] & (1UL << bitPosition)) != 0;

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
                CopyStreams(lastBlockWithData.Value, blocksToCopy);

                lastBlockWithData = null;
            }
        }

        // Handle any remaining blocks to be copied
        if (lastBlockWithData.HasValue)
        {
            long blocksToCopy = blocksCount - lastBlockWithData.Value;
            CopyStreams(lastBlockWithData.Value, blocksToCopy);
        }
    }
}