namespace MBW.Utilities.Journal.Abstracts;

public interface IJournal
{
    ValueTask FinalizeJournal(long finalLength);
    ValueTask ApplyJournal();

    void Flush();

    ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken);
    ValueTask WriteAsync(long offset, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);
}