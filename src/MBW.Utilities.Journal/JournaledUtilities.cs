using MBW.Utilities.Journal.Exceptions;
using MBW.Utilities.Journal.Helpers;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal;

public static class JournaledUtilities
{
    /// <summary>
    /// If a journal exists for this stream, and it was committed but not yet applied, this function will apply it. If the journal exists, but wasn't committed, it is discarded.
    /// </summary>
    public static void EnsureJournalCommitted(Stream backingStream, string journalFile)
    {
        EnsureJournalCommitted(backingStream, new FileBasedJournalStream(journalFile));
    }

    /// <summary>
    /// If a journal exists for this stream, and it was committed but not yet applied, this function will apply it. If the journal exists, but wasn't committed, it is discarded.
    /// </summary>
    public static void EnsureJournalCommitted(Stream backingStream, IJournalStream journalStream)
    {
        if (!journalStream.Exists())
            return;

        // If this is completed, commit it, else delete it
        using Stream fsJournal = journalStream.OpenOrCreate();

        if (!JournaledStreamHelpers.TryReadHeader(fsJournal, out TransactFileHeader header))
        {
            // Corrupt file. The file exists, but does not have a valid header. This is unlike if the footer is missing (a partially written file)
            throw new JournalCorruptedException("The journal file was corrupted", false);
        }

        fsJournal.Seek(-TransactFileFooter.StructSize, SeekOrigin.End);
        if (!JournaledStreamHelpers.TryReadFooter(fsJournal, out TransactFileFooter footer))
        {
            // Bad file or not committed
            journalStream.Delete();
            return;
        }

        if (header.Nonce != footer.HeaderNonce)
            throw new JournalCorruptedException($"Header & footer does not match. Nonces: {header.Nonce:X8}, footer: {footer.HeaderNonce:X8}", false);

        fsJournal.Seek(0, SeekOrigin.Begin);
        JournaledStreamHelpers.ApplyJournal(backingStream, fsJournal, header, footer);

        // Committed
        journalStream.Delete();
    }
}