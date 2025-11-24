namespace MBW.Utilities.Journal.Abstracts;

public interface IJournalFactory
{
    byte ImplementationId { get; }
    
    IJournal Create(Stream origin, Stream journal);
    IJournal Open(Stream origin, Stream journal);
}