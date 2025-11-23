using MBW.Utilities.Journal.Abstracts;
using MBW.Utilities.Journal.Exceptions;
using MBW.Utilities.Journal.Helpers;
using MBW.Utilities.Journal.SparseJournal;
using MBW.Utilities.Journal.Structures;
using MBW.Utilities.Journal.WalJournal;

namespace MBW.Utilities.Journal;

/// <summary>
/// Entry point for creating journaled streams with different strategies and open modes.
/// </summary>
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

    /// <summary>
    /// Creates a journaled stream using the provided journal strategy and storage, honoring the specified open mode for existing journals.
    /// </summary>
    /// <remarks>This is an advanced method that allows you to create your own journal providers. It is recommended to use on of the other methods which are preconfigured for easy usage</remarks>
    /// <param name="origin">Underlying stream to be journaled.</param>
    /// <param name="journalStreamFactory">Factory that provides the journal backing stream.</param>
    /// <param name="journalFactory">Factory that creates/opens the concrete journal implementation.</param>
    /// <param name="openMode">Controls whether to apply committed journals or discard uncommitted ones when present.</param>
    /// <returns>A journaled stream wrapping the origin.</returns>
    /// <exception cref="JournalCorruptedException">Thrown when an uncommitted or corrupt journal is present and the open mode does not allow discarding.</exception>
    /// <exception cref="JournalCommittedButNotAppliedException">Thrown when a committed journal is present and the open mode does not allow applying it.</exception>
    public static async Task<JournaledStream> CreateJournal(Stream origin,
        IJournalStreamFactory journalStreamFactory, IJournalFactory journalFactory,
        JournalOpenMode openMode = JournalOpenMode.Default)
    {
        await HandleOpenMode(origin, journalStreamFactory, journalFactory, openMode);

        return new JournaledStream(origin, journalStreamFactory, journalFactory);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="origin">Underlying stream to be journaled.</param>
    /// <param name="journalFile">A file path to the journal, usually for files, this could be FILENAME.jrnl.</param>
    /// <param name="openMode">Controls whether to apply committed journals or discard uncommitted ones when present.</param>
    /// <returns>A journaled stream, configured with the WAL strategy</returns>
    /// <exception cref="JournalCorruptedException">Thrown when an uncommitted or corrupt journal is present and the open mode does not allow discarding.</exception>
    /// <exception cref="JournalCommittedButNotAppliedException">Thrown when a committed journal is present and the open mode does not allow applying it.</exception>
    public static Task<JournaledStream> CreateWalJournal(Stream origin, string journalFile,
        JournalOpenMode openMode = JournalOpenMode.Default) =>
        CreateWalJournal(origin, new FileBasedJournalStreamFactory(journalFile), openMode);

    /// <summary>
    /// <inheritdoc cref="CreateWalJournal(System.IO.Stream,string,MBW.Utilities.Journal.JournalOpenMode)"/>
    /// </summary>
    /// <param name="origin">Underlying stream to be journaled.</param>
    /// <param name="journalStreamFactory">A producer for journaled streams.</param>
    /// <param name="openMode">Controls whether to apply committed journals or discard uncommitted ones when present.</param>
    /// <returns><inheritdoc cref="CreateWalJournal(System.IO.Stream,string,MBW.Utilities.Journal.JournalOpenMode)"/></returns>
    /// <exception cref="JournalCorruptedException">Thrown when an uncommitted or corrupt journal is present and the open mode does not allow discarding.</exception>
    /// <exception cref="JournalCommittedButNotAppliedException">Thrown when a committed journal is present and the open mode does not allow applying it.</exception>
    public static Task<JournaledStream> CreateWalJournal(Stream origin,
        IJournalStreamFactory journalStreamFactory,
        JournalOpenMode openMode = JournalOpenMode.Default)
    {
        WalJournalFactory journalFactory = new WalJournalFactory();
        return CreateJournal(origin, journalStreamFactory, journalFactory, openMode);
    }

    public static Task<JournaledStream> CreateSparseJournal(Stream origin, string journalFile,
        JournalOpenMode openMode = JournalOpenMode.Default) =>
        CreateSparseJournal(origin, new FileBasedJournalStreamFactory(journalFile), openMode);

    public static Task<JournaledStream> CreateSparseJournal(Stream origin,
        IJournalStreamFactory journalStreamFactory,
        JournalOpenMode openMode = JournalOpenMode.Default) =>
        CreateSparseJournal(origin, journalStreamFactory, 12, openMode);

    public static Task<JournaledStream> CreateSparseJournal(Stream origin,
        IJournalStreamFactory journalStreamFactory,
        byte blockSize, JournalOpenMode openMode = JournalOpenMode.Default)
    {
        SparseJournalFactory journalFactory = new SparseJournalFactory(blockSize);
        return CreateJournal(origin, journalStreamFactory, journalFactory, openMode);
    }
}