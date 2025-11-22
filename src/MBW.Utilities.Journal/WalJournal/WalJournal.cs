using System.Diagnostics;
using System.IO.Hashing;
using Jamarino.IntervalTree;
using MBW.Utilities.Journal.Abstracts;
using MBW.Utilities.Journal.Exceptions;
using MBW.Utilities.Journal.Extensions;
using MBW.Utilities.Journal.Primitives;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal.WalJournal;

internal sealed class WalJournal : IJournal
{
    private readonly Stream _origin;
    private readonly Stream _journal;
    private readonly JournalFileHeader _header;
    private readonly WalJournalFooter? _footer;
    private readonly QuickIntervalTree<long, JournalSegment> _journalSegments;

    private uint _journalWrittenSegments;
    private ushort _journalMaxSegmentDataLength;

    public WalJournal(Stream origin, Stream journal, JournalFileHeader header)
    {
        _origin = origin;
        _journal = journal;
        _header = header;
        _footer = null;
        _journalSegments = [];
    }

    public WalJournal(Stream origin, Stream journal, JournalFileHeader header, WalJournalFooter footer)
    {
        _origin = origin;
        _journal = journal;
        _header = header;
        _footer = footer;
    }

    public async ValueTask FinalizeJournal(long finalLength)
    {
        _journal.Seek(0, SeekOrigin.End);

        WalJournalFooter value = new WalJournalFooter
        {
            Magic = WalJournalFooter.ExpectedMagic,
            HeaderNonce = _header.Nonce,
            Entries = _journalWrittenSegments,
            FinalLength = finalLength,
            MaxEntryDataLength = _journalMaxSegmentDataLength
        };

        _journal.Write(value.AsSpan());
        await _journal.FlushAsync();
    }

    public ValueTask ApplyJournal()
    {
        Debug.Assert(_footer.HasValue);

        // Seek to begin of data
        _journal.Seek(JournalFileHeader.StructSize, SeekOrigin.Begin);

        // Truncate the original to ensure it fits our desired length
        bool targetHasBeenAltered = false;
        if (_origin.Length != _footer.Value.FinalLength)
        {
            _origin.SetLength(_footer.Value.FinalLength);
            targetHasBeenAltered = true;
        }

        // Apply all segments to the original
        Span<byte> tmpLocalHeader = stackalloc byte[WalJournalLocalHeader.StructSize];
        Span<byte> tmpLocalData = new byte[_footer.Value.MaxEntryDataLength];

        for (int i = 0; i < _footer.Value.Entries; i++)
        {
            WalJournalLocalHeader localHeader = _journal.ReadOne<WalJournalLocalHeader>(tmpLocalHeader);
            if (localHeader.Magic != WalJournalLocalHeader.ExpectedMagic)
                throw new JournalCorruptedException(
                    $"Bad segment magic, {localHeader.Magic:X4}, expected: {WalJournalLocalHeader.ExpectedMagic:X4}",
                    targetHasBeenAltered);

            // Read journaled data
            Span<byte> thisJournalData = tmpLocalData.Slice(0, localHeader.Length);
            _journal.ReadExactly(thisJournalData);

            // Checksum
            ulong checksum = XxHash64.HashToUInt64(thisJournalData);
            if (checksum != localHeader.XxHashChecksum)
                throw new JournalCorruptedException("Segment was corrupted, bad checksum", targetHasBeenAltered);

            // Calculate how much to read from the journal.
            // The journal may contain data written _after_ the final file destination, if the file was truncated in a transaction
            long maxToWriteToInner = Math.Max(0, _footer.Value.FinalLength - localHeader.InnerOffset);
            uint toWriteToInner = (uint)Math.Min(thisJournalData.Length, maxToWriteToInner);

            if (toWriteToInner > 0)
            {
                thisJournalData = thisJournalData.Slice(0, (int)toWriteToInner);

                _origin.Seek(localHeader.InnerOffset, SeekOrigin.Begin);
                _origin.Write(thisJournalData);
                targetHasBeenAltered = true;
            }
        }

        _origin.Flush();

        return ValueTask.CompletedTask;
    }

    public void Flush()
    {
        _journal.Flush();
    }

    public async ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        // Read the original stream first
        long readUpTo = 0;
        long readFromInner = Math.Clamp(_origin.Length - offset, 0, buffer.Length);
        if (readFromInner > 0)
        {
            _origin.Seek(offset, SeekOrigin.Begin);
            var read = await _origin.ReadUpToAsync(buffer, cancellationToken);
            readUpTo = offset + read;
        }

        // Patch up the read data, with any journaled data
        LongRange thisRead = new LongRange(offset, (uint)buffer.Length);
        foreach (JournalSegment journalSegment in _journalSegments.Query(offset, thisRead.End))
        {
            // Calculate area in inner, covered by this segment
            LongRange thisSegment = new LongRange(journalSegment.InnerOffset, journalSegment.Length);
            LongRange thisSegmentIntersect = thisRead.Intersection(thisSegment);

            // Calculate area in journal, covered by this read & segment
            long segmentOffset = thisSegmentIntersect.Start - thisSegment.Start;
            LongRange journalRange =
                new LongRange(journalSegment.JournalOffset + segmentOffset, thisSegmentIntersect.Length);

            // Prepare a view of buffer, that matches this segment
            long patchBufferStart = thisSegmentIntersect.Start - offset;
            Memory<byte> patchBuffer = buffer.Slice((int)patchBufferStart, (int)thisSegmentIntersect.Length);

            _journal.Seek(journalRange.Start, SeekOrigin.Begin);
            await _journal.ReadExactlyAsync(patchBuffer, cancellationToken);

            // Track how far we've read, this may be beyond the original streams length
            long end = journalSegment.InnerOffset + patchBuffer.Length;
            readUpTo = Math.Max(readUpTo, end);
        }

        // Calculate the number of bytes read
        // This will be from the offset we had, until the length of the stream or count of bytes wanted, whatever comes first
        return (int)(readUpTo - offset);
    }

    public async ValueTask WriteAsync(long offset, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        // Trim to 65k blocks to stay within a journal segment
        ReadOnlyMemory<byte> remainingWrite = buffer;
        while (remainingWrite.Length > 0)
        {
            ReadOnlyMemory<byte> thisWrite = remainingWrite[..Math.Min(ushort.MaxValue, remainingWrite.Length)];
            remainingWrite = remainingWrite[thisWrite.Length..];

            await WriteInternal(offset, thisWrite, cancellationToken);
            offset += thisWrite.Length;
        }
    }

    private async Task WriteInternal(long offset, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        Debug.Assert(buffer.Length is > 0 and <= ushort.MaxValue);

        LongRange thisWrite = new LongRange(offset, (uint)buffer.Length);

        // Checksum the incoming data
        ulong checksum = XxHash64.HashToUInt64(buffer.Span);

        // Write to the journal
        _journal.Seek(0, SeekOrigin.End);
        WalJournalLocalHeader value = new WalJournalLocalHeader
        {
            Magic = WalJournalLocalHeader.ExpectedMagic,
            InnerOffset = thisWrite.Start,
            Length = (ushort)thisWrite.Length,
            XxHashChecksum = checksum
        };
        _journal.Write(value.AsSpan());

        long journalDataOffset = _journal.Position;
        await _journal.WriteAsync(buffer, cancellationToken);

        _journalWrittenSegments++;
        if (buffer.Length > _journalMaxSegmentDataLength)
            _journalMaxSegmentDataLength = (ushort)buffer.Length;

        // Track in the segment list
        foreach (JournalSegment journalSegment in _journalSegments.Query(thisWrite.Start, thisWrite.End))
        {
            LongRange thisSegment = new LongRange(journalSegment.InnerOffset, journalSegment.Length);

            // Edge case, the tree uses exact bounds matching, while we need only overlaps
            if (thisSegment.End == thisWrite.Start ||
                thisWrite.End == thisSegment.Start)
                continue;

            // Determine overlap
            if (thisWrite.Contains(thisSegment))
            {
                // Remove this entirely
                _journalSegments.Remove(journalSegment);
            }
            else
            {
                // Split this
                _journalSegments.Remove(journalSegment);

                if (thisSegment < thisWrite)
                {
                    // Make a new segment before thisWrite
                    ushort newLength = (ushort)(thisWrite.Start - thisSegment.Start);
                    _journalSegments.Add(thisSegment.Start, thisWrite.Start,
                        new JournalSegment(thisSegment.Start, journalSegment.JournalOffset, newLength));
                }

                if (thisWrite.End < thisSegment.End)
                {
                    // Make a new segment after thisWrite
                    ushort newLength = (ushort)(thisSegment.End - thisWrite.End);
                    long newJournalOffset = journalSegment.JournalOffset + thisSegment.Length - newLength;

                    _journalSegments.Add(thisWrite.End, thisSegment.End,
                        new JournalSegment(thisWrite.End, newJournalOffset, newLength));
                }
            }
        }

        // Add this new segment
        _journalSegments.Add(thisWrite.Start, thisWrite.End,
            new JournalSegment(thisWrite.Start, journalDataOffset, (ushort)thisWrite.Length));
    }

    private record struct JournalSegment(long InnerOffset, long JournalOffset, ushort Length);
}