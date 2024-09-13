using System.IO.Hashing;
using MBW.Utilities.Journal.Exceptions;
using MBW.Utilities.Journal.Extensions;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal.Helpers;

internal static class JournaledStreamHelpers
{
    public static bool TryReadHeader(Stream stream, out TransactFileHeader header)
    {
        header = stream.ReadOneIfEnough<TransactFileHeader>(stackalloc byte[TransactFileHeader.StructSize], out bool success);
        if (!success)
            return false;

        return header.Magic == TransactedFileConstants.HeaderMagic;
    }

    public static bool TryReadFooter(Stream stream, out TransactFileFooter footer)
    {
        footer = stream.ReadOneIfEnough<TransactFileFooter>(stackalloc byte[TransactFileFooter.StructSize], out bool success);

        if (!success)
            return false;

        return footer.Magic == TransactedFileConstants.FooterMagic;
    }

    public static void ApplyJournal(Stream inner, Stream journal)
    {
        if (!TryReadHeader(journal, out TransactFileHeader header))
        {
            // Bad file
            throw new InvalidOperationException();
        }

        journal.Seek(-TransactFileFooter.StructSize, SeekOrigin.End);
        if (!TryReadFooter(journal, out TransactFileFooter footer))
        {
            // Bad file or not committed
            throw new InvalidOperationException();
        }

        if (header.Nonce != footer.HeaderNonce)
            throw new InvalidOperationException($"Header & footer does not match. Nonces: {header.Nonce:X8}, footer: {footer.HeaderNonce:X8}");

        ApplyJournal(inner, journal, header, footer);
    }

    public static void ApplyJournal(Stream inner, Stream journal, TransactFileHeader header, TransactFileFooter footer)
    {
        // Seek to begin of data
        journal.Seek(TransactFileHeader.StructSize, SeekOrigin.Begin);

        // Truncate the original to ensure it fits our desired length
        bool targetHasBeenAltered = false;
        if (inner.Length != footer.FinalLength)
        {
            inner.SetLength(footer.FinalLength);
            targetHasBeenAltered = true;
        }

        // Apply all segments to the original
        Span<byte> tmpLocalHeader = stackalloc byte[TransactLocalHeader.StructSize];
        Span<byte> tmpLocalData = new byte[footer.MaxEntryDataLength];

        for (int i = 0; i < footer.Entries; i++)
        {
            TransactLocalHeader localHeader = journal.ReadOne<TransactLocalHeader>(tmpLocalHeader);
            if (localHeader.Magic != TransactedFileConstants.LocalMagic)
                throw new JournalCorruptedException($"Bad segment magic, {localHeader.Magic:X4}, expected: {TransactedFileConstants.LocalMagic:X4}", targetHasBeenAltered);

            // Read journaled data
            Span<byte> thisJournalData = tmpLocalData.Slice(0, localHeader.Length);
            journal.ReadExactly(thisJournalData);

            // Checksum
            ulong checksum = XxHash64.HashToUInt64(thisJournalData);
            if (checksum != localHeader.XxHashChecksum)
                throw new JournalCorruptedException("Segment was corrupted, bad checksum", targetHasBeenAltered);

            // Calculate how much to read from the journal.
            // The journal may contain data written _after_ the final file destination, if the file was truncated in a transaction
            long maxToWriteToInner = Math.Max(0, footer.FinalLength - localHeader.InnerOffset);
            uint toWriteToInner = (uint)Math.Min(thisJournalData.Length, maxToWriteToInner);

            if (toWriteToInner > 0)
            {
                thisJournalData = thisJournalData.Slice(0, (int)toWriteToInner);

                inner.Seek(localHeader.InnerOffset, SeekOrigin.Begin);
                inner.Write(thisJournalData);
                targetHasBeenAltered = true;
            }
        }
    }
}