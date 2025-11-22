using MBW.Utilities.Journal.Abstracts;
using MBW.Utilities.Journal.Exceptions;
using MBW.Utilities.Journal.Helpers;
using MBW.Utilities.Journal.SparseJournal;
using MBW.Utilities.Journal.Structures;
using MBW.Utilities.Journal.WalJournal;

namespace MBW.Utilities.Journal;

public static class JournaledStreamFactory
{
    private static async ValueTask HandleOpenMode(Stream origin, IJournalStreamFactory streamFactory,
        IJournalFactory journalFactory, JournalOpenMode openMode)
    {
        if (!streamFactory.TryOpen(string.Empty, false, out Stream? journalStream))
            return;

        if (!JournaledStreamHelpers.TryRead(journalStream, JournalFileConstants.HeaderMagic,
                out JournalFileHeader header) ||
            (header.Flags & JournalHeaderFlags.Committed) == 0)
        {
            // Corrupt stream
            if ((openMode & JournalOpenMode.DiscardUncommittedJournals) != 0)
            {
                // Delete it
                await journalStream.DisposeAsync();
                streamFactory.Delete(string.Empty);
                return;
            }

            // Abort
            throw new JournalCorruptedException(
                "There is an journal present for this stream, but it has not been committed. Discarding the journal was also not allowed.",
                false);
        }

        // Handle existing stream
        if ((openMode & JournalOpenMode.ApplyCommittedJournals) != 0)
        {
            await using (journalStream)
            {
                IJournal journal = journalFactory.Open(origin, journalStream);
                await journal.ApplyJournal();
            }

            streamFactory.Delete(string.Empty);
        }
        else
        {
            throw new JournalCommittedButNotAppliedException(
                "The journal for this stream exists, but has not been applied fully. Open the Journal with " +
                nameof(JournalOpenMode.ApplyCommittedJournals) + " to complete the process");
        }
    }

    public static Task<JournaledStream> CreateWalJournal(Stream origin, string journalFile,
        JournalOpenMode openMode = JournalOpenMode.Default) =>
        CreateWalJournal(origin, new FileBasedJournalStreamFactory(journalFile), openMode);

    public static async Task<JournaledStream> CreateWalJournal(Stream origin,
        IJournalStreamFactory journalStreamFactory,
        JournalOpenMode openMode = JournalOpenMode.Default)
    {
        WalJournalFactory journalFactory = new WalJournalFactory();
        await HandleOpenMode(origin, journalStreamFactory, journalFactory, openMode);

        return new JournaledStream(origin, journalStreamFactory, journalFactory);
    }

    public static Task<JournaledStream> CreateSparseJournal(Stream origin, string journalFile,
        JournalOpenMode openMode = JournalOpenMode.Default) =>
        CreateSparseJournal(origin, new FileBasedJournalStreamFactory(journalFile), openMode);

    public static Task<JournaledStream> CreateSparseJournal(Stream origin,
        IJournalStreamFactory journalStreamFactory,
        JournalOpenMode openMode = JournalOpenMode.Default) =>
        CreateSparseJournal(origin, journalStreamFactory, 12, openMode);

    public static async Task<JournaledStream> CreateSparseJournal(Stream origin,
        IJournalStreamFactory journalStreamFactory,
        byte blockSize, JournalOpenMode openMode = JournalOpenMode.Default)
    {
        SparseJournalFactory journalFactory = new SparseJournalFactory(blockSize);
        await HandleOpenMode(origin, journalStreamFactory, journalFactory, openMode);

        return new JournaledStream(origin, journalStreamFactory, journalFactory);
    }
}