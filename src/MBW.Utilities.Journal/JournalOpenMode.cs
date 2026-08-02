namespace MBW.Utilities.Journal;

[Flags]
public enum JournalOpenMode
{
    None,
    ApplyCommittedJournals = 1,
    DiscardUncommittedJournals = 2,
    OpenPendingJournals = 4,

    Default = ApplyCommittedJournals,
    Coordinated = OpenPendingJournals
}
