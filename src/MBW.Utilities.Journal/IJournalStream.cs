namespace MBW.Utilities.Journal;

public interface IJournalStream
{
    bool Exists();
    void Delete();
    Stream OpenOrCreate();
}