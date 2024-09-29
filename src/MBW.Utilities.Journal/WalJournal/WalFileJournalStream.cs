using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using Jamarino.IntervalTree;
using MBW.Utilities.Journal.Extensions;
using MBW.Utilities.Journal.Primitives;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal.WalJournal;

internal sealed class WalFileJournalStream : JournaledStream
{
    private record struct JournalSegment(long InnerOffset, long JournalOffset, ushort Length);

    private readonly Stream _origin;
    private readonly IJournalStream _journalStreamCreator;
    private Stream? _journal;
    private readonly ulong _journalNonceValue;
    private QuickIntervalTree<long, JournalSegment>? _journalSegments;

    private uint _journalWrittenSegments;
    private ushort _journalMaxSegmentDataLength;

    private long _virtualOffset;
    private long _virtualLength;

    public WalFileJournalStream(Stream origin, IJournalStream journalStreamCreator)
    {
        if (origin is { CanWrite: false, CanRead: false })
            throw new ArgumentException("Must be able to write or read from inner", nameof(origin));
        if (origin is { CanSeek: false })
            throw new ArgumentException("Must be able to seek from inner", nameof(origin));

        _origin = origin;
        _journalStreamCreator = journalStreamCreator;

        JournaledUtilities.EnsureJournalCommitted(_origin, _journalStreamCreator);

        _virtualLength = _origin.Length;
        _virtualOffset = 0;

        long nonceValue = Random.Shared.NextInt64();
        _journalNonceValue = Unsafe.As<long, ulong>(ref nonceValue);
    }

    [MemberNotNullWhen(true, nameof(_journal), nameof(_journalSegments))]
    private bool IsJournalOpened(bool openIfClosed)
    {
        if (_journal != null)
        {
            Debug.Assert(_journalSegments != null);
            return true;
        }

        if (!openIfClosed)
            return false;

        _journal = _journalStreamCreator.OpenOrCreate();
        _journalSegments = new QuickIntervalTree<long, JournalSegment>();

        JournalFileHeader value = new JournalFileHeader
        {
            Magic = JournalFileConstants.HeaderMagic,
            Nonce = _journalNonceValue,
            Strategy = JournalStrategy.WalJournalFile
        };
        _journal.Write(value.AsSpan());

        if (_origin.CanRead && _journal is not { CanRead: true, CanSeek: true })
            throw new InvalidOperationException("Journal for a readable stream must be able to read and seek");
        if (_origin.CanWrite && _journal is not { CanWrite: true })
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

        // Close out journal stream
        _journal.Seek(0, SeekOrigin.End);
        WalJournalFooter value = new WalJournalFooter
        {
            Magic = WalJournalFileConstants.WalJournalFooterMagic,
            HeaderNonce = _journalNonceValue,
            Entries = _journalWrittenSegments,
            FinalLength = _virtualLength,
            MaxEntryDataLength = _journalMaxSegmentDataLength
        };
        _journal.Write(value.AsSpan());
        _journal.Flush();

        _journal.Seek(0, SeekOrigin.Begin);
        WalFileJournalHelpers.ApplyJournal(_origin, _journal);

        // The journal has been applied
        DeleteAndResetJournal();
    }

    public override void Rollback()
    {
        DeleteAndResetJournal();

        // Reset stream back to what it was initially
        _virtualLength = _origin.Length;
        _virtualOffset = Math.Clamp(_virtualOffset, 0, _virtualLength);
    }

    private void DeleteAndResetJournal()
    {
        if (!IsJournalOpened(false))
        {
            // No writes have happened
            return;
        }

        // Delete journal
        _journal.Close();
        _journal = null;
        _journalSegments = null;
        _journalWrittenSegments = 0;
        _journalMaxSegmentDataLength = 0;

        _journalStreamCreator.Delete();
    }

    public override void Flush()
    {
        _journal?.Flush();
        _origin.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (!CanRead)
            throw new ArgumentException("The underlying stream for this TransactedStream is unreadable");

        // Read the original stream first
        int read = 0;
        Span<byte> tmpBuffer = buffer.AsSpan().Slice(offset, count);

        long readFromInner = Math.Clamp(_origin.Length - _virtualOffset, 0, count);
        if (readFromInner > 0)
        {
            _origin.Seek(_virtualOffset, SeekOrigin.Begin);
            read = _origin.Read(tmpBuffer);
        }

        if (!IsJournalOpened(false))
        {
            _virtualOffset += read;
            return read;
        }

        // Path up the read data, with any journaled data
        LongRange thisRead = new LongRange(_virtualOffset, (uint)count);
        foreach (JournalSegment journalSegment in _journalSegments.Query(_virtualOffset, thisRead.End))
        {
            // Calculate area in inner, covered by this segment
            LongRange thisSegment = new LongRange(journalSegment.InnerOffset, journalSegment.Length);
            LongRange thisSegmentIntersect = thisRead.Intersection(thisSegment);

            // Calculate area in journal, covered by this read & segment
            long segmentOffset = thisSegmentIntersect.Start - thisSegment.Start;
            LongRange journalRange = new LongRange(journalSegment.JournalOffset + segmentOffset, thisSegmentIntersect.Length);

            // Prepare a view of buffer, that matches this segment
            long patchBufferStart = thisSegmentIntersect.Start - _virtualOffset;
            Span<byte> patchBuffer = tmpBuffer.Slice((int)patchBufferStart, (int)thisSegmentIntersect.Length);

            _journal.Seek(journalRange.Start, SeekOrigin.Begin);
            _journal.ReadExactly(patchBuffer);
        }

        // Calculate the number of bytes read
        // This will be from the offset we had, until the length of the stream or count of bytes wanted, whatever comes first
        long availableBytes = _virtualLength - thisRead.Start;
        return (int)Math.Min(availableBytes, thisRead.Length);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (!CanWrite)
            throw new ArgumentException("Stream is unwriteable");

        if (!IsJournalOpened(true))
            throw new InvalidOperationException("Unable to open a journal for writing");

        // Trim to 65k blocks to stay within a journal segment
        Span<byte> remainingWrite = buffer.AsSpan(offset, count);
        while (remainingWrite.Length > 0)
        {
            Span<byte> thisWrite = remainingWrite.Slice(0, Math.Min(ushort.MaxValue, remainingWrite.Length));
            remainingWrite = remainingWrite.Slice(thisWrite.Length);

            WriteInternal(thisWrite);
        }
    }

    private void WriteInternal(ReadOnlySpan<byte> buffer)
    {
        Debug.Assert(IsJournalOpened(false));
        Debug.Assert(buffer.Length is > 0 and <= ushort.MaxValue);

        LongRange thisWrite = new LongRange(_virtualOffset, (uint)buffer.Length);

        // Checksum the incoming data
        ulong checksum = XxHash64.HashToUInt64(buffer);

        // Write to the journal
        _journal.Seek(0, SeekOrigin.End);
        WalJournalLocalHeader value = new WalJournalLocalHeader
        {
            Magic = WalJournalFileConstants.WalJournalLocalMagic,
            InnerOffset = thisWrite.Start,
            Length = (ushort)thisWrite.Length,
            XxHashChecksum = checksum
        };
        _journal.Write(value.AsSpan());

        long journalDataOffset = _journal.Position;
        _journal.Write(buffer);
        _journalWrittenSegments++;
        if (buffer.Length > _journalMaxSegmentDataLength)
            _journalMaxSegmentDataLength = (ushort)buffer.Length;

        // Internal tracking
        _virtualOffset += thisWrite.Length;
        _virtualLength = Math.Max(_virtualLength, _virtualOffset);

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
        _journalSegments.Add(thisWrite.Start, thisWrite.End, new JournalSegment(thisWrite.Start, journalDataOffset, (ushort)thisWrite.Length));
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long newOffset = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _virtualOffset + offset,
            SeekOrigin.End => _virtualLength + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null)
        };

        if (newOffset < 0)
            throw new ArgumentException($"Desired offset, {offset} from {origin} placed the offset at {newOffset} which was out of range");

        _virtualOffset = newOffset;
        _virtualLength = Math.Max(_virtualLength, _virtualOffset);
        return _virtualOffset;
    }

    public override void SetLength(long value)
    {
        if (_virtualLength == value)
            return;

        if (!IsJournalOpened(true))
            throw new InvalidOperationException("Unable to open a journal for writing");

        _virtualLength = value;
        _virtualOffset = Math.Min(_virtualLength, _virtualOffset);
    }

    public override bool CanRead => _origin.CanRead;
    public override bool CanSeek => _origin.CanSeek;
    public override bool CanWrite => _origin.CanWrite;
    public override long Length => _virtualLength;

    public override long Position
    {
        get => _virtualOffset;
        set => _virtualOffset = value;
    }
}