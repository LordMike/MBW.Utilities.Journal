namespace MBW.Utilities.Journal;

public enum JournaledStreamState
{
    /// <summary>
    /// Open, ready for read/write
    /// </summary>
    Ready,

    /// <summary>
    /// Open, ready for read/write, has changes
    /// </summary>
    Dirty,

    /// <summary>
    /// Finalized with a coordination key. Reads and rollback are allowed, mutations are frozen.
    /// </summary>
    Prepared,

    /// <summary>
    /// Committed, but not yet applied. Can only read
    /// </summary>
    CommittedButNotApplied,

    /// <summary>
    /// The stream is closed, no further action is possible
    /// </summary>
    Closed
}
