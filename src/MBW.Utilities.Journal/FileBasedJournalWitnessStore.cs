using MBW.Utilities.Journal.Abstracts;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal;

/// <summary>
/// A durable, versioned file witness for coordinated journal commits.
/// </summary>
public sealed class FileBasedJournalWitnessStore : IJournalWitnessStore
{
    private readonly string _file;

    public FileBasedJournalWitnessStore(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        _file = file;
    }

    public async ValueTask<Guid?> ReadAsync(CancellationToken cancellationToken = default)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(_file, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }

        await using (stream)
        {
            if (stream.Length != JournalWitnessRecord.Size)
                throw new InvalidDataException("The journal witness has an invalid length.");

            byte[] record = new byte[JournalWitnessRecord.Size];
            await stream.ReadExactlyAsync(record, cancellationToken);
            return JournalWitnessRecord.Read(record);
        }
    }

    public async ValueTask StoreAsync(Guid preparationKey, CancellationToken cancellationToken = default)
    {
        if (preparationKey == Guid.Empty)
            throw new ArgumentException("The preparation key must not be empty", nameof(preparationKey));

        Guid? existing = await ReadAsync(cancellationToken);
        if (existing.HasValue)
        {
            if (existing.Value == preparationKey)
                return;

            throw new InvalidOperationException("The witness belongs to another prepared transaction.");
        }

        string? directory = Path.GetDirectoryName(_file);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string temporaryFile = _file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            byte[] record = JournalWitnessRecord.Create(preparationKey);
            await using (FileStream stream = new FileStream(temporaryFile, FileMode.CreateNew,
                             FileAccess.Write, FileShare.None, 4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(record, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(true);
            }

            try
            {
                File.Move(temporaryFile, _file, false);
            }
            catch (IOException)
            {
                Guid? racedValue = await ReadAsync(cancellationToken);
                if (racedValue != preparationKey)
                    throw;
            }
        }
        finally
        {
            try
            {
                File.Delete(temporaryFile);
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }

    public async ValueTask ClearAsync(Guid preparationKey, CancellationToken cancellationToken = default)
    {
        if (preparationKey == Guid.Empty)
            throw new ArgumentException("The preparation key must not be empty", nameof(preparationKey));

        Guid? existing = await ReadAsync(cancellationToken);
        if (!existing.HasValue)
            return;
        if (existing.Value != preparationKey)
            throw new InvalidOperationException("The witness belongs to another prepared transaction.");

        File.Delete(_file);
    }
}
