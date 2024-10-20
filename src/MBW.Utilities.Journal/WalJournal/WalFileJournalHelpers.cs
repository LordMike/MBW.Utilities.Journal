using System.IO.Hashing;
using MBW.Utilities.Journal.Exceptions;
using MBW.Utilities.Journal.Extensions;
using MBW.Utilities.Journal.Helpers;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal.WalJournal;

internal static class WalFileJournalHelpers
{
    internal static void ApplyJournal(Stream origin, Stream journal)
    {
        if (!JournaledStreamHelpers.TryRead(journal, JournalFileConstants.HeaderMagic, out JournalFileHeader header))
        {
            // Bad file
            throw new InvalidOperationException();
        }

        journal.Seek(-WalJournalFooter.StructSize, SeekOrigin.End);
        if (!JournaledStreamHelpers.TryRead(journal, WalJournalFileConstants.WalJournalFooterMagic, out WalJournalFooter footer))
        {
            // Bad file or not committed
            throw new InvalidOperationException();
        }

        if (header.Nonce != footer.HeaderNonce)
            throw new InvalidOperationException($"Header & footer does not match. Nonces: {header.Nonce:X8}, footer: {footer.HeaderNonce:X8}");

        ApplyJournal(origin, journal, header, footer);
    }

    internal static void ApplyJournal(Stream origin, Stream journal, JournalFileHeader header, WalJournalFooter footer)
    {
        // Seek to begin of data
        journal.Seek(JournalFileHeader.StructSize, SeekOrigin.Begin);

        // Truncate the original to ensure it fits our desired length
        bool targetHasBeenAltered = false;
        if (origin.Length != footer.FinalLength)
        {
            origin.SetLength(footer.FinalLength);
            targetHasBeenAltered = true;
        }

        // Apply all segments to the original
        Span<byte> tmpLocalHeader = stackalloc byte[WalJournalLocalHeader.StructSize];
        Span<byte> tmpLocalData = new byte[footer.MaxEntryDataLength];

        for (int i = 0; i < footer.Entries; i++)
        {
            WalJournalLocalHeader localHeader = journal.ReadOne<WalJournalLocalHeader>(tmpLocalHeader);
            if (localHeader.Magic != WalJournalFileConstants.WalJournalLocalMagic)
                throw new JournalCorruptedException($"Bad segment magic, {localHeader.Magic:X4}, expected: {WalJournalFileConstants.WalJournalLocalMagic:X4}", targetHasBeenAltered);

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

                origin.Seek(localHeader.InnerOffset, SeekOrigin.Begin);
                origin.Write(thisJournalData);
                targetHasBeenAltered = true;
            }
        }
        
        origin.Flush();
    }
}