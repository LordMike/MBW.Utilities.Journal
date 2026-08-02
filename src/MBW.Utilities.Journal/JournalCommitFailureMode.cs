namespace MBW.Utilities.Journal;

public enum JournalCommitFailureMode
{
    PreserveForRecovery,
    RollbackIfSafe
}
