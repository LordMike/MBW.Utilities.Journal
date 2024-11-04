using MBW.Utilities.Journal.SparseJournal;
using MBW.Utilities.Journal.WalJournal;

namespace MBW.Utilities.Journal;

public static class JournaledStreamFactory
{
    public static JournaledStream CreateWalJournal(Stream origin, string journalFile) => CreateWalJournal(origin, new FileBasedJournalStreamFactory(journalFile));
    public static JournaledStream CreateWalJournal(Stream origin, IJournalStreamFactory journalStreamFactory) => new WalFileJournalStream(origin, journalStreamFactory);

    public static JournaledStream CreateSparseJournal(Stream origin, string journalFile) => CreateSparseJournal(origin, new FileBasedJournalStreamFactory(journalFile));
    public static JournaledStream CreateSparseJournal(Stream origin, IJournalStreamFactory journalStreamFactory) => CreateSparseJournal(origin, journalStreamFactory, 12);
    public static JournaledStream CreateSparseJournal(Stream origin, IJournalStreamFactory journalStreamFactory, byte blockSize) => new SparseJournalStream(origin, journalStreamFactory, blockSize);
}