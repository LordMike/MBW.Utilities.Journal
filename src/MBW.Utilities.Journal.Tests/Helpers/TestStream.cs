namespace MBW.Utilities.Journal.Tests.Helpers;

public class TestStream : Stream
{
    private Stream _streamImplementation;

    public TestStream()
    {
        _streamImplementation = new MemoryStream();
    }

    public bool LockWrites { get; set; }
    public bool LockReads { get; set; }

    public bool Lock
    {
        set => LockWrites = LockReads = value;
    }

    public override void Flush() => _streamImplementation.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (LockReads)
            throw new TestStreamBlockedException("Reading is prohibited");
        return _streamImplementation.Read(buffer, offset, count);
    }

    public override long Seek(long offset, SeekOrigin origin) => _streamImplementation.Seek(offset, origin);
    public override void SetLength(long value)
    {
        if (LockWrites)
            throw new TestStreamBlockedException("Writing/seeking is prohibited");
        _streamImplementation.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (LockWrites)
            throw new TestStreamBlockedException("Writing is prohibited");
        _streamImplementation.Write(buffer, offset, count);
    }

    public override bool CanRead => _streamImplementation.CanRead;
    public override bool CanSeek => _streamImplementation.CanSeek;
    public override bool CanWrite => _streamImplementation.CanWrite;
    public override long Length => _streamImplementation.Length;

    public override long Position
    {
        get => _streamImplementation.Position;
        set => _streamImplementation.Position = value;
    }
}