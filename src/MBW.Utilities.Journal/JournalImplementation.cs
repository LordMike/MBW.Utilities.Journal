using MBW.Utilities.Journal.SparseJournal;
using MBW.Utilities.Journal.WalJournal;

namespace MBW.Utilities.Journal;

internal enum JournalImplementation : byte
{
    /// <summary>
    /// WAL Journal file implemented by <see cref="WalJournalFactory"/>
    /// </summary>
    WalJournal = 1,

    /// <summary>
    /// Sparse file approach implemented by <see cref="SparseJournalFactory"/>
    /// </summary>
    SparseJournal = 2,
    
    // Reserve 1..20 for implementations in this library
    // Reserve 21..250 for third parties
    // Reserve 251..255 for future stuff that might require us to handle stuff differently
}