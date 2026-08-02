namespace MBW.Utilities.Journal;

/// <summary>
/// Selects which automatic recovery actions a <see cref="JournaledStreamCoordinator"/> may perform.
/// </summary>
[Flags]
public enum JournalCoordinatorRecoveryMode
{
    /// <summary>
    /// Do not automatically commit or roll back prepared transactions; report that recovery is required instead.
    /// </summary>
    None = 0,

    /// <summary>
    /// Commit the exact participant nonce set recorded by the durable witness, clear the witness after every marker is
    /// committed, and then apply the journals independently.
    /// </summary>
    CommitWitnessed = 1,

    /// <summary>
    /// Roll back dirty or prepared participants only after confirming that no witness exists.
    /// </summary>
    RollbackUnwitnessed = 2,

    /// <summary>
    /// Roll witnessed transactions forward and unwitnessed transactions back.
    /// </summary>
    Default = CommitWitnessed | RollbackUnwitnessed
}
