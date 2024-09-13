using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using Jamarino.IntervalTree;
using MBW.Utilities.Journal.Exceptions;
using MBW.Utilities.Journal.Extensions;
using MBW.Utilities.Journal.Helpers;
using MBW.Utilities.Journal.Primitives;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal;

public sealed class JournaledStream : Stream
{
    private record struct JournalSegment(long InnerOffset, long JournalOffset, ushort Length);

    private readonly Stream _inner;
    private readonly IJournalStream _journalStreamCreator;
    private Stream? _journal;
    private readonly ulong _journalNonceValue;
    private LightIntervalTree<long, JournalSegment>? _journalSegments;

    private uint _journalWrittenSegments;
    private ushort _journalMaxSegmentDataLength;

    private long _virtualOffset;
    private long _virtualLength;

    public JournaledStream(Stream inner, string journalFilePath) : this(inner, new FileBasedJournalStream(journalFilePath))
    {
    }

    public JournaledStream(Stream inner, IJournalStream journalStreamCreator)
    {
        if (inner is { CanWrite: false, CanRead: false })
            throw new ArgumentException("Must be able to write or read from inner", nameof(inner));
        if (inner is { CanSeek: false })
            throw new ArgumentException("Must be able to seek from inner", nameof(inner));

        _inner = inner;
        _journalStreamCreator = journalStreamCreator;

        if (_journalStreamCreator.Exists())
            ApplyExistingJournalIfCommitted();

        _virtualLength = _inner.Length;
        _virtualOffset = 0;

        long nonceValue = Random.Shared.NextInt64();
        _journalNonceValue = Unsafe.As<long, ulong>(ref nonceValue);
    }

    private void ApplyExistingJournalIfCommitted()
    {
        // If this is completed, commit it, else delete it
        using Stream fsJournal = _journalStreamCreator.OpenOrCreate();

        if (!JournaledStreamHelpers.TryReadHeader(fsJournal, out TransactFileHeader header))
        {
            // Corrupt file. The file exists, but does not have a valid header. This is unlike if the footer is missing (a partially written file)
            throw new JournalCorruptedException("The journal file was corrupted", false);
        }

        fsJournal.Seek(-TransactFileFooter.StructSize, SeekOrigin.End);
        if (!JournaledStreamHelpers.TryReadFooter(fsJournal, out TransactFileFooter footer))
        {
            // Bad file or not committed
            _journalStreamCreator.Delete();
            return;
        }

        if (header.Nonce != footer.HeaderNonce)
            throw new JournalCorruptedException($"Header & footer does not match. Nonces: {header.Nonce:X8}, footer: {footer.HeaderNonce:X8}", false);

        fsJournal.Seek(0, SeekOrigin.Begin);
        JournaledStreamHelpers.ApplyJournal(_inner, fsJournal, header, footer);

        // Committed
        _journalStreamCreator.Delete();
    }

    [MemberNotNullWhen(true, nameof(_journal), nameof(_journalSegments))]
    private bool IsJournalOpened(bool open)
    {
        if (_journal != null)
        {
            Debug.Assert(_journalSegments != null);
            return true;
        }

        if (!open)
            return false;

        _journal = _journalStreamCreator.OpenOrCreate();
        _journalSegments = new LightIntervalTree<long, JournalSegment>();

        _journal.WriteOne(new TransactFileHeader
        {
            Magic = TransactedFileConstants.HeaderMagic,
            Nonce = _journalNonceValue
        }, stackalloc byte[TransactFileHeader.StructSize]);

        if (_inner.CanRead && _journal is not { CanRead: true, CanSeek: true })
            throw new InvalidOperationException("Journal for a readable stream must be able to read and seek");
        if (_inner.CanWrite && _journal is not { CanWrite: true })
            throw new InvalidOperationException("Journal for a writeable stream must be able to write");

        return true;
    }

    public void Commit()
    {
        if (!IsJournalOpened(false))
        {
            // No writes have happened
            return;
        }

        // Close out journal stream
        _journal.Seek(0, SeekOrigin.End);
        _journal.WriteOne(new TransactFileFooter
        {
            Magic = TransactedFileConstants.FooterMagic,
            HeaderNonce = _journalNonceValue,
            Entries = _journalWrittenSegments,
            FinalLength = _virtualLength,
            MaxEntryDataLength = _journalMaxSegmentDataLength
        }, stackalloc byte[TransactFileFooter.StructSize]);
        _journal.Flush();

        _journal.Seek(0, SeekOrigin.Begin);
        JournaledStreamHelpers.ApplyJournal(_inner, _journal);

        // The journal has been applied
        DeleteAndResetJournal();
    }

    public void Rollback()
    {
        DeleteAndResetJournal();

        // Reset stream back to what it was initially
        _virtualLength = _inner.Length;
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
        _inner.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (!CanRead)
            throw new ArgumentException("The underlying stream for this TransactedStream is unreadable");

        // Read the original stream first
        int read = 0;
        Span<byte> tmpBuffer = buffer.AsSpan().Slice(offset, count);

        long readFromInner = Math.Clamp(_inner.Length - _virtualOffset, 0, count);
        if (readFromInner > 0)
        {
            _inner.Seek(_virtualOffset, SeekOrigin.Begin);
            read = _inner.Read(tmpBuffer);
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
            throw new InvalidOperationException();

        // Trim to 65k blocks to stay within a journal segment
        var remainingWrite = buffer.AsSpan(offset, count);
        while (remainingWrite.Length > 0)
        {
            var thisWrite = remainingWrite.Slice(0, Math.Min(ushort.MaxValue, remainingWrite.Length));
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
        Span<byte> tmpXxHash = stackalloc byte[sizeof(ulong)];
        Debug.Assert(XxHash64.Hash(buffer, tmpXxHash) == tmpXxHash.Length);
        ref ulong checksum = ref Unsafe.As<byte, ulong>(ref tmpXxHash[0]);

        // Write to the journal
        _journal.Seek(0, SeekOrigin.End);
        _journal.WriteOne(new TransactLocalHeader
        {
            Magic = TransactedFileConstants.LocalMagic,
            InnerOffset = thisWrite.Start,
            Length = (ushort)thisWrite.Length,
            XxHashChecksum = checksum
        }, stackalloc byte[TransactLocalHeader.StructSize]);

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

        IsJournalOpened(true);
        _virtualLength = value;
        _virtualOffset = Math.Min(_virtualLength, _virtualOffset);
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => _virtualLength;

    public override long Position
    {
        get => _virtualOffset;
        set => _virtualOffset = value;
    }
}