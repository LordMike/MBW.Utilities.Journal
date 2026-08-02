using System.Diagnostics.CodeAnalysis;
using MBW.Utilities.Journal.Abstracts;

namespace MBW.Utilities.Journal.Tests.Helpers;

internal sealed class FaultingJournalStreamFactory : IJournalStreamFactory
{
    private readonly MemoryStream _stream = new();
    private bool _exists;

    public int? FailOnFlush { get; set; }
    public int FlushCount { get; private set; }

    public bool Exists(string identifier) => _exists;

    public void Delete(string identifier)
    {
        _exists = false;
        _stream.SetLength(0);
        _stream.Position = 0;
    }

    public bool TryOpen(string identifier, bool createIfMissing, [NotNullWhen(true)] out Stream? stream)
    {
        if (!_exists && !createIfMissing)
        {
            stream = null;
            return false;
        }

        _exists = true;
        _stream.Position = 0;
        stream = new NonClosingFaultingStream(this, _stream);
        return true;
    }

    private sealed class NonClosingFaultingStream(
        FaultingJournalStreamFactory owner, MemoryStream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            owner.FlushCount++;
            if (owner.FailOnFlush == owner.FlushCount)
                return Task.FromException(new IOException("Injected journal flush failure"));
            return inner.FlushAsync(cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) => inner.WriteAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
        }
    }
}
