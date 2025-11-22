namespace MBW.Utilities.Journal.Structures;

[Flags]
internal enum JournalHeaderFlags : byte
{
    None,

    /// <summary>
    /// This journal file represents a committed journal. The associated footer will describe the journal in more detail
    /// </summary>
    Committed = 1,
}