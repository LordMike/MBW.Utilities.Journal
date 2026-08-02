using System.Buffers.Binary;
using System.IO.Hashing;
using MBW.Utilities.Journal.Abstracts;

namespace MBW.Utilities.Journal;

/// <summary>
/// A durable, versioned file witness for coordinated journal commits.
/// </summary>
public sealed class FileBasedJournalWitnessStore : IJournalWitnessStore
{
    private const ulong Magic = 0x315449574C4E524A; // "JRNLWIT1"
    private const int PayloadSize = sizeof(ulong) + 16;
    private const int RecordSize = PayloadSize + sizeof(ulong);
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
            if (stream.Length != RecordSize)
                throw new InvalidDataException("The journal witness has an invalid length.");

            byte[] record = new byte[RecordSize];
            await stream.ReadExactlyAsync(record, cancellationToken);

            if (BinaryPrimitives.ReadUInt64LittleEndian(record) != Magic)
                throw new InvalidDataException("The journal witness has an unknown format version.");

            ulong expectedChecksum = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(PayloadSize));
            ulong actualChecksum = XxHash64.HashToUInt64(record.AsSpan(0, PayloadSize));
            if (expectedChecksum != actualChecksum)
                throw new InvalidDataException("The journal witness checksum is invalid.");

            Guid key = new Guid(record.AsSpan(sizeof(ulong), 16));
            if (key == Guid.Empty)
                throw new InvalidDataException("The journal witness contains an empty preparation key.");

            return key;
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
            byte[] record = CreateRecord(preparationKey);
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

    private static byte[] CreateRecord(Guid preparationKey)
    {
        byte[] record = new byte[RecordSize];
        BinaryPrimitives.WriteUInt64LittleEndian(record, Magic);
        preparationKey.TryWriteBytes(record.AsSpan(sizeof(ulong), 16));
        ulong checksum = XxHash64.HashToUInt64(record.AsSpan(0, PayloadSize));
        BinaryPrimitives.WriteUInt64LittleEndian(record.AsSpan(PayloadSize), checksum);
        return record;
    }
}
