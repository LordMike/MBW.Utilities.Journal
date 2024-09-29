namespace MBW.Utilities.Journal.SparseJournal;

internal sealed class SparseFileBackedJournalStream : JournaledStream
{
    private Stream _origin;

    public SparseFileBackedJournalStream(Stream origin, IJournalStreamFactory sparseFile)
    {
    }

    public override void Commit()
    {
        throw new NotImplementedException();
    }

    public override void Rollback()
    {
        throw new NotImplementedException();
    }

    public override void Flush()
    {
        _origin.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return _origin.Read(buffer, offset, count);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _origin.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        _origin.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _origin.Write(buffer, offset, count);
    }

    public override bool CanRead => _origin.CanRead;

    public override bool CanSeek => _origin.CanSeek;

    public override bool CanWrite => _origin.CanWrite;

    public override long Length => _origin.Length;

    public override long Position
    {
        get => _origin.Position;
        set => _origin.Position = value;
    }
}