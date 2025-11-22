using MBW.Utilities.Journal.Exceptions;
using MBW.Utilities.Journal.Helpers;
using Metalama.Patterns.Contracts;

namespace MBW.Utilities.Journal;

public abstract class JournaledStream : Stream
{
    protected readonly Stream Origin;
    protected readonly IJournalStreamFactory JournalStreamFactory;

    protected long VirtualOffset;
    protected long VirtualLength;
    private bool _journalFinalized;

    protected JournaledStream(Stream origin, IJournalStreamFactory journalStreamFactory)
    {
        JournaledStreamHelpers.CheckOriginStreamRequirements(origin);

        Origin = origin;
        JournalStreamFactory = journalStreamFactory;

        JournaledUtilities.EnsureJournalCommitted(Origin, JournalStreamFactory);

        VirtualLength = Origin.Length;
        VirtualOffset = 0;
    }

    [Invariant]
    private void A()
    {
        if (Origin == null)
        {
            
        }
    }
    
    public void Commit(bool applyImmediately = true)
    {
        if (!IsJournalOpened(false))
            return;

        EnsureJournalFinalized();

        if (!applyImmediately)
            return;

        ApplyFinalizedJournal();
        ClearFinalizationState();
    }

    public void Rollback()
    {
        RollbackJournal();

        Position = Math.Clamp(VirtualOffset, 0, Origin.Length);
        VirtualLength = Origin.Length;

        ClearFinalizationState();
    }

    public abstract override int Read(Span<byte> buffer);
    public abstract override void Write(ReadOnlySpan<byte> buffer);

    /// <summary>
    /// Note: Sealed to force overriding the Span&lt;&gt; overloads instead
    /// </summary>
    public sealed override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan().Slice(offset, count));

    /// <summary>
    /// Note: Sealed to force overriding the Span&lt;&gt; overloads instead
    /// </summary>
    public sealed override void Write(byte[] buffer, int offset, int count)
    {
        EnsureNotFinalized();
        
        Write(buffer.AsSpan().Slice(offset, count));
    }

    protected abstract bool IsJournalOpened(bool openIfClosed);

    public override void SetLength(long value)
    {
        EnsureNotFinalized();

        if (VirtualLength == value)
            return;

        if (!IsJournalOpened(true))
            throw new InvalidOperationException("Unable to open a journal for writing");

        VirtualLength = value;
        VirtualOffset = Math.Clamp(VirtualOffset, 0, VirtualLength);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long newOffset = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => VirtualOffset + offset,
            SeekOrigin.End => VirtualLength + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null)
        };

        if (newOffset < 0)
            throw new ArgumentException(
                $"Desired offset, {offset} from {origin} placed the offset at {newOffset} which was out of range");

        return Position = newOffset;
    }

    public override bool CanRead => Origin.CanRead;
    public override bool CanSeek => Origin.CanSeek;
    public override bool CanWrite => Origin.CanWrite;

    public override long Length => VirtualLength;

    public override long Position
    {
        get => VirtualOffset;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Position must be non-negative");

            if (value > VirtualLength)
                EnsureNotFinalized();
            
            VirtualOffset = value;
            VirtualLength = Math.Max(VirtualLength, VirtualOffset);
        }
    }

    private void ClearFinalizationState() => _journalFinalized = false;

    protected void EnsureNotFinalized()
    {
        if (_journalFinalized)
            throw new JournalCommittedButNotAppliedException(
                "The journal has been prepared for commit and cannot be modified until it is applied or discarded");
    }

    private void EnsureJournalFinalized()
    {
        if (_journalFinalized)
            return;

        FinalizeJournal();
        _journalFinalized = true;
    }

    protected abstract void FinalizeJournal();
    protected abstract void ApplyFinalizedJournal();
    protected abstract void RollbackJournal();
}