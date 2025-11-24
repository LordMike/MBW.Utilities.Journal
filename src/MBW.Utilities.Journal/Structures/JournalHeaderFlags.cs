namespace MBW.Utilities.Journal.Structures;

[Flags]
public enum JournalHeaderFlags : byte
{
    None,

    /// <summary>
    /// This journal file represents a committed journal. The associated footer will describe the journal in more detail
    /// </summary>
    Committed = 1,
}