using System.Diagnostics.CodeAnalysis;
using MBW.Utilities.Journal.Abstracts;
using MBW.Utilities.Journal.SparseJournal;

namespace MBW.Utilities.Journal;

/// <summary>
/// A provider for a journal file based on a filesystem file
/// </summary>
internal sealed class FileBasedJournalStreamFactory(string file) : IJournalStreamFactory
{
    private string GetFileName(string identifier) => identifier == string.Empty ? file : file + identifier;

    public bool Exists(string identifier) => File.Exists(GetFileName(identifier));

    public void Delete(string identifier) => File.Delete(GetFileName(identifier));

    public bool TryOpen(string identifier, bool createIfMissing, [NotNullWhen(true)] out Stream? stream)
    {
        try
        {
            FileStream fsStream = File.Open(GetFileName(identifier), FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.Read | FileShare.Delete);
            SparseStreamHelper.MakeStreamSparse(fsStream);

            stream = fsStream;

            return true;
        }
        catch (FileNotFoundException)
        {
            stream = null;
            return false;
        }
    }

    public Stream OpenOrCreate(string identifier)
    {
        FileStream fsStream = File.Open(GetFileName(identifier), FileMode.OpenOrCreate, FileAccess.ReadWrite,
            FileShare.Read | FileShare.Delete);
        SparseStreamHelper.MakeStreamSparse(fsStream);

        return fsStream;
    }
}