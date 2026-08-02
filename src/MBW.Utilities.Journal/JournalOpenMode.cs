namespace MBW.Utilities.Journal;

/// <summary>
/// Controls how a journaled stream handles an existing journal while it is opened.
/// </summary>
[Flags]
public enum JournalOpenMode
{
    /// <summary>
    /// Do not apply, discard, or expose an existing journal automatically.
    /// </summary>
    None,
    /// <summary>
    /// Apply a journal whose durable commit marker is present before returning the stream.
    /// </summary>
    ApplyCommittedJournals = 1,
    /// <summary>
    /// Delete dirty or prepared journals whose changes have not been committed.
    /// </summary>
    DiscardUncommittedJournals = 2,
    /// <summary>
    /// Open pending journals without applying or discarding them so a coordinator can make the recovery decision.
    /// </summary>
    OpenPendingJournals = 4,

    /// <summary>
    /// Apply committed journals and require explicit handling for all other pending journals.
    /// </summary>
    Default = ApplyCommittedJournals,
    /// <summary>
    /// Preserve and expose pending journal state for <see cref="JournaledStreamCoordinator"/> recovery.
    /// </summary>
    Coordinated = OpenPendingJournals
}
