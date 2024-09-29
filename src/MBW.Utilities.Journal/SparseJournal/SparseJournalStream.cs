namespace MBW.Utilities.Journal.SparseJournal;

internal sealed class SparseJournalStream : JournaledStream
{
    public SparseJournalStream(Stream origin, IJournalStreamFactory journalStreamFactory) : base(origin, journalStreamFactory)
    {
    }

    public override void Commit()
    {
        throw new NotImplementedException();
    }

    public override void Rollback()
    {
        throw new NotImplementedException();
    }

    protected override bool IsJournalOpened(bool openIfClosed)
    {
        throw new NotImplementedException();
    }

    public override void Flush()
    {
        Origin.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotImplementedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotImplementedException();
    }
}