using System.Runtime.InteropServices;
using MBW.Utilities.Journal.Abstracts;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal.SampleJournal.Implementation;

internal sealed class FullCopyJournal(Stream origin, Stream journal, JournalFileHeader header, long knownFinalLength) : IJournal
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
        origin.SetLength(_finalLength);

        journal.Seek(JournalFileHeader.StructSize, SeekOrigin.Begin);
        origin.Seek(0, SeekOrigin.Begin);

        CopyBytes(journal, origin, _finalLength);
        origin.Flush();

        return ValueTask.CompletedTask;
    }

    public void Flush() => journal.Flush();

    public ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        journal.Seek(JournalFileHeader.StructSize + offset, SeekOrigin.Begin);
        return journal.ReadAsync(buffer, cancellationToken);
    }

    public ValueTask WriteAsync(long offset, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        journal.Seek(JournalFileHeader.StructSize + offset, SeekOrigin.Begin);
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

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct FullCopyJournalFooter
{
    internal const ulong ExpectedMagic = 0x46434A4C464E4C31; // "FCJLFNL1"
    internal static int StructSize => Marshal.SizeOf<FullCopyJournalFooter>();

    public ulong Magic;
    public long FinalLength;
}
