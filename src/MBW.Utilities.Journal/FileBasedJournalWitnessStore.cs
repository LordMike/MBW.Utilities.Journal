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

    public async ValueTask<JournalWitness?> ReadAsync(CancellationToken cancellationToken = default)
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
            if (stream.Length < JournalWitnessRecord.MinimumSize || stream.Length > int.MaxValue)
                throw new InvalidDataException("The journal witness has an invalid length.");

            byte[] record = new byte[(int)stream.Length];
            await stream.ReadExactlyAsync(record, cancellationToken);
            return JournalWitnessRecord.Read(record);
        }
    }

    public async ValueTask StoreAsync(JournalWitness witness, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(witness);

        JournalWitness? existing = await ReadAsync(cancellationToken);
        if (existing is not null)
        {
            if (existing.ValueEquals(witness))
                return;

            throw new InvalidOperationException("A different journal witness already exists.");
        }

        string? directory = Path.GetDirectoryName(_file);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string temporaryFile = _file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            byte[] record = JournalWitnessRecord.Create(witness);
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
                JournalWitness? racedValue = await ReadAsync(cancellationToken);
                if (racedValue is null || !racedValue.ValueEquals(witness))
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

    public async ValueTask ClearAsync(JournalWitness witness, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(witness);

        JournalWitness? existing = await ReadAsync(cancellationToken);
        if (existing is null)
            return;
        if (!existing.ValueEquals(witness))
            throw new InvalidOperationException("The stored journal witness does not match.");

        File.Delete(_file);
    }
}
