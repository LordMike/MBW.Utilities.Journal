using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;
using Jamarino.IntervalTree;
using MBW.Utilities.Journal.Extensions;
using MBW.Utilities.Journal.Primitives;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal.WalJournal;

internal sealed class WalFileJournalStream : JournaledStream
{
    private record struct JournalSegment(long InnerOffset, long JournalOffset, ushort Length);

    private Stream? _journal;
    private readonly ulong _journalNonceValue;
    private QuickIntervalTree<long, JournalSegment>? _journalSegments;

    private uint _journalWrittenSegments;
    private ushort _journalMaxSegmentDataLength;

    public WalFileJournalStream(Stream origin, IJournalStreamFactory journalStreamFactory) : base(origin, journalStreamFactory)
    {
        _journalNonceValue = unchecked((ulong)Random.Shared.NextInt64());
    }

    [MemberNotNullWhen(true, nameof(_journal), nameof(_journalSegments))]
    protected override bool IsJournalOpened(bool openIfClosed)
    {
        if (_journal != null)
        {
            Debug.Assert(_journalSegments != null);
            return true;
        }

        if (!openIfClosed)
            return false;

        _journal = JournalStreamFactory.OpenOrCreate(string.Empty);
        _journalSegments = new QuickIntervalTree<long, JournalSegment>();

        JournalFileHeader value = new JournalFileHeader
        {
            Magic = JournalFileConstants.HeaderMagic,
            Nonce = _journalNonceValue,
            Strategy = JournalStrategy.WalJournalFile
        };
        _journal.Write(value.AsSpan());

        if (Origin.CanRead && _journal is not { CanRead: true, CanSeek: true })
            throw new InvalidOperationException("Journal for a readable stream must be able to read and seek");
        if (Origin.CanWrite && _journal is not { CanWrite: true })
            throw new InvalidOperationException("Journal for a writeable stream must be able to write");

        return true;
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

        JournalStreamFactory.Delete(string.Empty);
    }
    protected override void FinalizeJournal()
    {
        if (!IsJournalOpened(false))
            throw new InvalidOperationException("Cannot FinalizeJournal, because its not opened");

        _journal.Seek(0, SeekOrigin.End);
        
        WalJournalFooter value = new WalJournalFooter
        {
            Magic = WalJournalFileConstants.WalJournalFooterMagic,
            HeaderNonce = _journalNonceValue,
            Entries = _journalWrittenSegments,
            FinalLength = VirtualLength,
            MaxEntryDataLength = _journalMaxSegmentDataLength
        };
        
        _journal.Write(value.AsSpan());
        _journal.Flush();
    }

    protected override void ApplyFinalizedJournal()
    {
        if (!IsJournalOpened(false))
            throw new InvalidOperationException("Cannot ApplyFinalizedJournal, because its not opened");

        _journal.Seek(0, SeekOrigin.Begin);
        WalFileJournalHelpers.ApplyJournal(Origin, _journal);

        DeleteAndResetJournal();
    }

    protected override void RollbackJournal() => DeleteAndResetJournal();

    public override void Flush()
    {
        _journal?.Flush();
        Origin.Flush();
    }

    public override int Read(Span<byte> buffer)
    {
        if (!CanRead)
            throw new ArgumentException("The underlying stream for this " + nameof(WalFileJournalStream) + " is unreadable");

        // Read the original stream first
        int read = 0;
        long readFromInner = Math.Clamp(Origin.Length - VirtualOffset, 0, buffer.Length);
        if (readFromInner > 0)
        {
            Origin.Seek(VirtualOffset, SeekOrigin.Begin);
            read = Origin.Read(buffer);
        }

        if (!IsJournalOpened(false))
        {
            VirtualOffset += read;
            return read;
        }

        // Patch up the read data, with any journaled data
        LongRange thisRead = new LongRange(VirtualOffset, (uint)buffer.Length);
        foreach (JournalSegment journalSegment in _journalSegments.Query(VirtualOffset, thisRead.End))
        {
            // Calculate area in inner, covered by this segment
            LongRange thisSegment = new LongRange(journalSegment.InnerOffset, journalSegment.Length);
            LongRange thisSegmentIntersect = thisRead.Intersection(thisSegment);

            // Calculate area in journal, covered by this read & segment
            long segmentOffset = thisSegmentIntersect.Start - thisSegment.Start;
            LongRange journalRange = new LongRange(journalSegment.JournalOffset + segmentOffset, thisSegmentIntersect.Length);

            // Prepare a view of buffer, that matches this segment
            long patchBufferStart = thisSegmentIntersect.Start - VirtualOffset;
            Span<byte> patchBuffer = buffer.Slice((int)patchBufferStart, (int)thisSegmentIntersect.Length);

            _journal.Seek(journalRange.Start, SeekOrigin.Begin);
            _journal.ReadExactly(patchBuffer);
        }

        // Calculate the number of bytes read
        // This will be from the offset we had, until the length of the stream or count of bytes wanted, whatever comes first
        long availableBytes = VirtualLength - thisRead.Start;
        if (availableBytes <= 0)
            return 0;

        int result = (int)Math.Min(availableBytes, thisRead.Length);
        VirtualOffset += result;
        return result;
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        EnsureNotFinalized();

        if (!CanWrite)
            throw new ArgumentException("Stream is unwriteable");

        if (!IsJournalOpened(true))
            throw new InvalidOperationException("Unable to open a journal for writing");

        // Trim to 65k blocks to stay within a journal segment
        ReadOnlySpan<byte> remainingWrite = buffer;
        while (remainingWrite.Length > 0)
        {
            ReadOnlySpan<byte> thisWrite = remainingWrite[..Math.Min(ushort.MaxValue, remainingWrite.Length)];
            remainingWrite = remainingWrite[thisWrite.Length..];

            WriteInternal(thisWrite);
        }
    }

    private void WriteInternal(ReadOnlySpan<byte> buffer)
    {
        Debug.Assert(IsJournalOpened(false));
        Debug.Assert(buffer.Length is > 0 and <= ushort.MaxValue);

        LongRange thisWrite = new LongRange(VirtualOffset, (uint)buffer.Length);

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
        VirtualOffset += thisWrite.Length;
        VirtualLength = Math.Max(VirtualLength, VirtualOffset);

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
}
