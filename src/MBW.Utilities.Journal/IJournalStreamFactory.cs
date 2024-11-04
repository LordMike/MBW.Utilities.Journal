namespace MBW.Utilities.Journal;

public interface IJournalStreamFactory
{
    bool Exists(string identifier);
    void Delete(string identifier);
    Stream OpenOrCreate(string identifier);
}