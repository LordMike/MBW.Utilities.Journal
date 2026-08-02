namespace MBW.Utilities.Journal;

/// <summary>
/// Controls what the coordinator does when a commit attempt fails before a durable decision is known.
/// </summary>
public enum JournalCommitFailureMode
{
    /// <summary>
    /// No failure policy was selected. This value is invalid for commit operations.
    /// </summary>
    Unset = 0,

    /// <summary>
    /// Preserve pending journals so the operation can be inspected or resumed.
    /// </summary>
    PreserveForRecovery = 1,

    /// <summary>
    /// Roll back participants only when the coordinator can confirm that no durable commit decision exists.
    /// </summary>
    RollbackIfSafe = 2
}
