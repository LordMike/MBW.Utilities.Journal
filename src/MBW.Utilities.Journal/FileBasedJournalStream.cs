namespace MBW.Utilities.Journal;

/// <summary>
/// A provider for a journal file based on a filesystem file
/// </summary>
internal sealed class FileBasedJournalStream(string file) : IJournalStream
{
    public bool Exists() => File.Exists(file);
    public void Delete() => File.Delete(file);
    public Stream OpenOrCreate() => File.Open(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete);
}