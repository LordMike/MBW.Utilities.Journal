namespace MBW.Utilities.Journal;

[Flags]
public enum JournalOpenMode
{
    None,
    ApplyCommittedJournals = 1,
    DiscardUncommittedJournals = 2,

    Default = ApplyCommittedJournals
}