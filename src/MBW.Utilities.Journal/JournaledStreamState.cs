namespace MBW.Utilities.Journal;

public enum JournaledStreamState
{
    Unset,

    /// <summary>
    /// Open, ready for read/write
    /// </summary>
    Clean,

    /// <summary>
    /// Open, ready for read/write, has changes
    /// </summary>
    JournalOpened,

    /// <summary>
    /// Committed, but not yet applied. Can only read
    /// </summary>
    JournalFinalized,

    /// <summary>
    /// The stream is closed, no further action is possible
    /// </summary>
    Closed
}