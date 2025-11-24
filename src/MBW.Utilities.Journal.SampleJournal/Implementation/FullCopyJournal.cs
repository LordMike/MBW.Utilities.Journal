using System.Runtime.InteropServices;
using MBW.Utilities.Journal.Abstracts;

namespace MBW.Utilities.Journal.SampleJournal.Implementation;

internal sealed class FullCopyJournal(
    Stream origin,
    Stream journal,
    long originalJournalPosition,
    long knownFinalLength) : IJournal
{
    private long _finalLength = knownFinalLength;

    public ValueTask FinalizeJournal(long finalLength)
    {
        // Assumes the journal already contains the full copy; we just append the footer with the desired final length.
        _finalLength = finalLength;
        journal.Seek(0, SeekOrigin.End);

        FullCopyJournalFooter footer = new()
        {
            Magic = FullCopyJournalFooter.ExpectedMagic,
            FinalLength = finalLength
        };

        journal.Write(SpanFrom(ref footer));
        journal.Flush();
        return ValueTask.CompletedTask;
    }

    public ValueTask ApplyJournal()
    {
        // Assumes finalize wrote the footer and the journal contains a full copy that should replace the origin.
        journal.Seek(originalJournalPosition, SeekOrigin.Begin);
        origin.Seek(0, SeekOrigin.Begin);
        origin.SetLength(_finalLength);

        CopyBytes(journal, origin, _finalLength);
        origin.Flush();

        return ValueTask.CompletedTask;
    }

    public void Flush() => journal.Flush();

    public ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        journal.Seek(originalJournalPosition + offset, SeekOrigin.Begin);
        return journal.ReadAsync(buffer, cancellationToken);
    }

    public ValueTask WriteAsync(long offset, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        journal.Seek(originalJournalPosition + offset, SeekOrigin.Begin);
        return journal.WriteAsync(buffer, cancellationToken);
    }

    private static Span<byte> SpanFrom<T>(ref T value) where T : unmanaged =>
        MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref value, 1));

    private static void CopyBytes(Stream source, Stream destination, long bytesToCopy)
    {
        Span<byte> buffer = stackalloc byte[8192];
        long remaining = bytesToCopy;

        while (remaining > 0)
        {
            int toRead = (int)Math.Min(buffer.Length, remaining);
            int read = source.Read(buffer[..toRead]);
            if (read == 0)
                break;

            destination.Write(buffer[..read]);
            remaining -= read;
        }
    }
}