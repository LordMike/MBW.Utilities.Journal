using MBW.Utilities.Journal.Helpers;

namespace MBW.Utilities.Journal;

public abstract class JournaledStream : Stream
{
    protected readonly Stream Origin;
    protected readonly IJournalStreamFactory JournalStreamFactory;

    protected long VirtualOffset;
    protected long VirtualLength;

    protected JournaledStream(Stream origin, IJournalStreamFactory journalStreamFactory)
    {
        JournaledStreamHelpers.CheckOriginStreamRequirements(origin);

        Origin = origin;
        JournalStreamFactory = journalStreamFactory;
        
        JournaledUtilities.EnsureJournalCommitted(Origin, JournalStreamFactory);
        
        VirtualLength = Origin.Length;
        VirtualOffset = 0;
    }
    
    public abstract void Commit();
    public abstract void Rollback();

    protected abstract bool IsJournalOpened(bool openIfClosed);

    public override void SetLength(long value)
    {
        if (VirtualLength == value)
            return;

        if (!IsJournalOpened(true))
            throw new InvalidOperationException("Unable to open a journal for writing");

        VirtualLength = value;
        VirtualOffset = Math.Min(VirtualLength, VirtualOffset);
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
            throw new ArgumentException($"Desired offset, {offset} from {origin} placed the offset at {newOffset} which was out of range");

        VirtualOffset = newOffset;
        VirtualLength = Math.Max(VirtualLength, VirtualOffset);
        return VirtualOffset;
    }

    public override bool CanRead => Origin.CanRead;
    public override bool CanSeek => Origin.CanSeek;
    public override bool CanWrite => Origin.CanWrite;

    public override long Length => VirtualLength;

    public override long Position
    {
        get => VirtualOffset;
        set => VirtualOffset = value;
    }
}