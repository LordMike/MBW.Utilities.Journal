namespace MBW.Utilities.Journal;

[Flags]
public enum JournalCoordinatorRecoveryMode
{
    None = 0,
    CommitWitnessed = 1,
    RollbackUnwitnessed = 2,
    Default = CommitWitnessed | RollbackUnwitnessed
}
