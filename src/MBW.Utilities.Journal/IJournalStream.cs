namespace MBW.Utilities.Journal;

public interface IJournalStream
{
    bool Exists(string identifier);
    void Delete(string identifier);
    Stream OpenOrCreate(string identifier);
}