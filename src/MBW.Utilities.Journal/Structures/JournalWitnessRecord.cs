using System.Buffers.Binary;
using System.IO.Hashing;

namespace MBW.Utilities.Journal.Structures;

internal static class JournalWitnessRecord
{
    private const ulong Magic = 0x315449574C4E524A; // "JRNLWIT1"
    private const int PayloadSize = sizeof(ulong) + 16;
    internal const int Size = PayloadSize + sizeof(ulong);

    internal static byte[] Create(Guid preparationKey)
    {
        if (preparationKey == Guid.Empty)
            throw new ArgumentException("The preparation key must not be empty", nameof(preparationKey));

        byte[] record = new byte[Size];
        BinaryPrimitives.WriteUInt64LittleEndian(record, Magic);
        preparationKey.TryWriteBytes(record.AsSpan(sizeof(ulong), 16));
        ulong checksum = XxHash64.HashToUInt64(record.AsSpan(0, PayloadSize));
        BinaryPrimitives.WriteUInt64LittleEndian(record.AsSpan(PayloadSize), checksum);
        return record;
    }

    internal static Guid Read(ReadOnlySpan<byte> record)
    {
        if (record.Length != Size)
            throw new InvalidDataException("The journal witness has an invalid length.");

        if (BinaryPrimitives.ReadUInt64LittleEndian(record) != Magic)
            throw new InvalidDataException("The journal witness has an unknown format version.");

        ulong expectedChecksum = BinaryPrimitives.ReadUInt64LittleEndian(record[PayloadSize..]);
        ulong actualChecksum = XxHash64.HashToUInt64(record[..PayloadSize]);
        if (expectedChecksum != actualChecksum)
            throw new InvalidDataException("The journal witness checksum is invalid.");

        Guid key = new(record.Slice(sizeof(ulong), 16));
        if (key == Guid.Empty)
            throw new InvalidDataException("The journal witness contains an empty preparation key.");

        return key;
    }
}
