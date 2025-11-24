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

        if (!JournaledStreamHelpers.TryRead(journalStream, JournalFileHeader.ExpectedMagic,
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
            journalStream.Seek(0, SeekOrigin.Begin);

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
    /// Creates a journaled stream backed by a write-ahead log (WAL) journal. WAL appends change segments, making small or localized edits fast to stage and commit,
    /// while many dispersed edits will grow the log and can slow apply time. Use when you want simple append-only journaling with strong recovery semantics.
    /// Example:
    /// <code>
    /// await using var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite);
    /// await using var js = await JournaledStreamFactory.CreateWalJournal(file, path + ".jrnl");
    /// js.Write(Encoding.UTF8.GetBytes("Hello"));
    /// await js.Commit();
    /// </code>
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

    /// <summary>
    /// Creates a journaled stream backed by a sparse block journal. Sparse journals track dirty blocks via bitmap and write full blocks,
    /// making them efficient for larger block-aligned writes and stable apply times even with many edits; small scattered writes may incur more overhead.
    /// Example:
    /// <code>
    /// await using var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite);
    /// await using var js = await JournaledStreamFactory.CreateSparseJournal(file, path + ".jrnl");
    /// js.Write(Encoding.UTF8.GetBytes("Hello"));
    /// await js.Commit();
    /// </code>
    /// </summary>
    /// <param name="origin">Underlying stream to be journaled.</param>
    /// <param name="journalFile">A file path to the journal, usually for files, this could be FILENAME.jrnl.</param>
    /// <param name="openMode">Controls whether to apply committed journals or discard uncommitted ones when present.</param>
    /// <returns>A journaled stream, configured with the Sparse file strategy</returns>
    /// <exception cref="JournalCorruptedException">Thrown when an uncommitted or corrupt journal is present and the open mode does not allow discarding.</exception>
    /// <exception cref="JournalCommittedButNotAppliedException">Thrown when a committed journal is present and the open mode does not allow applying it.</exception>
    public static Task<JournaledStream> CreateSparseJournal(Stream origin, string journalFile,
        JournalOpenMode openMode = JournalOpenMode.Default) =>
        CreateSparseJournal(origin, new FileBasedJournalStreamFactory(journalFile), openMode);

    /// <summary>
    /// <inheritdoc cref="CreateSparseJournal(System.IO.Stream,string,MBW.Utilities.Journal.JournalOpenMode)"/>
    /// </summary>
    /// <param name="origin">Underlying stream to be journaled.</param>
    /// <param name="journalStreamFactory">A producer for journal streams.</param>
    /// <param name="openMode">Controls whether to apply committed journals or discard uncommitted ones when present.</param>
    /// <returns><inheritdoc cref="CreateSparseJournal(System.IO.Stream,string,MBW.Utilities.Journal.JournalOpenMode)"/></returns>
    /// <exception cref="JournalCorruptedException">Thrown when an uncommitted or corrupt journal is present and the open mode does not allow discarding.</exception>
    /// <exception cref="JournalCommittedButNotAppliedException">Thrown when a committed journal is present and the open mode does not allow applying it.</exception>
    public static Task<JournaledStream> CreateSparseJournal(Stream origin,
        IJournalStreamFactory journalStreamFactory,
        JournalOpenMode openMode = JournalOpenMode.Default) =>
        CreateSparseJournal(origin, journalStreamFactory, 12, openMode);

    /// <summary>
    /// <inheritdoc cref="CreateSparseJournal(System.IO.Stream,string,MBW.Utilities.Journal.JournalOpenMode)"/>
    /// </summary>
    /// <param name="origin">Underlying stream to be journaled.</param>
    /// <param name="journalStreamFactory">A producer for journal streams.</param>
    /// <param name="blockSize">The block size to use for aligned writes. All writes must be aligned internally. Smaller block sizes favor smaller edits, while larger are more suited for large edits. Block size is expressed in a power of two, like 12 for 1024 bytes.</param>
    /// <param name="openMode">Controls whether to apply committed journals or discard uncommitted ones when present.</param>
    /// <returns><inheritdoc cref="CreateSparseJournal(System.IO.Stream,string,MBW.Utilities.Journal.JournalOpenMode)"/></returns>
    /// <exception cref="JournalCorruptedException">Thrown when an uncommitted or corrupt journal is present and the open mode does not allow discarding.</exception>
    /// <exception cref="JournalCommittedButNotAppliedException">Thrown when a committed journal is present and the open mode does not allow applying it.</exception>
    public static Task<JournaledStream> CreateSparseJournal(Stream origin,
        IJournalStreamFactory journalStreamFactory,
        byte blockSize, JournalOpenMode openMode = JournalOpenMode.Default)
    {
        SparseJournalFactory journalFactory = new SparseJournalFactory(blockSize);
        return CreateJournal(origin, journalStreamFactory, journalFactory, openMode);
    }
}