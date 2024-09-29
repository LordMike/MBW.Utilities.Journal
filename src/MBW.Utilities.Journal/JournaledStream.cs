namespace MBW.Utilities.Journal;

public abstract class JournaledStream : Stream
{
    public abstract void Commit();
    public abstract void Rollback();
}