namespace MBW.Utilities.Journal.Tests.Helpers;

public class TestStream : Stream
{
    private Stream _streamImplementation;

    public TestStream()
    {
        _streamImplementation = new MemoryStream();
    }

    public TestStream Clone()
    {
        if (_streamImplementation is not MemoryStream mem)
            throw new InvalidOperationException("Can only snapshot memory streams");

        return new TestStream
        {
            _streamImplementation = new MemoryStream(mem.ToArray())
        };
    }

    public override void Flush() => _streamImplementation.Flush();

    public override int Read(byte[] buffer, int offset, int count)
        => _streamImplementation.Read(buffer, offset, count);

    public override long Seek(long offset, SeekOrigin origin) => _streamImplementation.Seek(offset, origin);
    public override void SetLength(long value) => _streamImplementation.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count) => _streamImplementation.Write(buffer, offset, count);

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
