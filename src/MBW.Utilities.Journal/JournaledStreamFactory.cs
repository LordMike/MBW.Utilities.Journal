using MBW.Utilities.Journal.SparseJournal;
using MBW.Utilities.Journal.WalJournal;

namespace MBW.Utilities.Journal;

public static class JournaledStreamFactory
{
    public static JournaledStream CreateWalJournal(Stream origin, string journalFile) => CreateWalJournal(origin, new FileBasedJournalStream(journalFile));
    public static JournaledStream CreateWalJournal(Stream origin, IJournalStream journalStream) => new WalFileJournalStream(origin, journalStream);

    public static JournaledStream CreateSparseJournal(Stream origin, string journalFile) => CreateSparseJournal(origin, new FileBasedJournalStream(journalFile));
    public static JournaledStream CreateSparseJournal(Stream origin, IJournalStream journalStream) => new SparseFileBackedJournalStream(origin, journalStream);
}