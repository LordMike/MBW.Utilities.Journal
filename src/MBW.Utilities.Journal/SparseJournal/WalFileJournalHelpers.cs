using MBW.Utilities.Journal.Helpers;
using MBW.Utilities.Journal.Structures;
using MBW.Utilities.Journal.WalJournal;

namespace MBW.Utilities.Journal.SparseJournal;

internal static class SparseJournalHelpers
{
    internal static void ApplyJournal(Stream inner, Stream journal)
    {
        if (!JournaledStreamHelpers.TryRead(journal, JournalFileConstants.HeaderMagic, out JournalFileHeader header))
        {
            // Bad file
            throw new InvalidOperationException();
        }

        journal.Seek(-SparseJournalFooter.StructSize, SeekOrigin.End);
        if (!JournaledStreamHelpers.TryRead(journal, SparseJournalFileConstants.SparseJournalFooterMagic, out SparseJournalFooter footer))
        {
            // Bad file or not committed
            throw new InvalidOperationException();
        }

        if (header.Nonce != footer.HeaderNonce)
            throw new InvalidOperationException($"Header & footer does not match. Nonces: {header.Nonce:X8}, footer: {footer.HeaderNonce:X8}");

        ApplyJournal(inner, journal, header, footer);
    }

    internal static void ApplyJournal(Stream inner, Stream journal, JournalFileHeader header, SparseJournalFooter footer)
    {
        // Seek to begin of data
        journal.Seek(JournalFileHeader.StructSize, SeekOrigin.Begin);

        // Truncate the original to ensure it fits our desired length
        bool targetHasBeenAltered = false;
        if (inner.Length != footer.FinalLength)
        {
            inner.SetLength(footer.FinalLength);
            targetHasBeenAltered = true;
        }

        // Apply sparse to the original
        throw new NotImplementedException();
    }
}