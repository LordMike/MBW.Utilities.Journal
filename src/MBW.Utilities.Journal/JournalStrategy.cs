using MBW.Utilities.Journal.SparseJournal;

namespace MBW.Utilities.Journal;

internal enum JournalStrategy : byte
{
    None,

    /// <summary>
    /// WAL Journal file implemented by <see cref="FileBasedJournalStream"/>
    /// </summary>
    WalJournalFile,

    /// <summary>
    /// Sparse file approach implemented by <see cref="SparseFileBackedJournalStream"/>
    /// </summary>
    SparseFile
}