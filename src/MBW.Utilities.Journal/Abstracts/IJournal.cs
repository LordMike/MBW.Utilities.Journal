namespace MBW.Utilities.Journal;

public interface IJournal : IDisposable
{
    ValueTask FinalizeJournal();
    ValueTask ApplyJournal();
    ValueTask RollbackJournal();

    void Seek(long position);

    void Flush();

    int Read(Span<byte> buffer);
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);
    void Write(ReadOnlySpan<byte> buffer);
    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);

    void Dispose();
}