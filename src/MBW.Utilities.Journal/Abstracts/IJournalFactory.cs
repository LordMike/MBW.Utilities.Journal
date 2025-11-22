namespace MBW.Utilities.Journal;

public interface IJournalFactory
{
    IJournal Create(Stream origin, Stream journal);
    IJournal Open(Stream origin, Stream journal);
}