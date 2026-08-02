using System.Buffers.Binary;
using System.IO.Hashing;

namespace MBW.Utilities.Journal.Structures;

internal static class JournalWitnessRecord
{
    private const ulong Magic = 0x325449574C4E524A; // "JRNLWIT2"
    private const int PreparationKeyOffset = sizeof(ulong);
    private const int ParticipantCountOffset = PreparationKeyOffset + 16;
    private const int ParticipantNoncesOffset = ParticipantCountOffset + sizeof(uint);
    private const int ChecksumSize = sizeof(ulong);
    internal const int MinimumSize = ParticipantNoncesOffset + sizeof(ulong) + ChecksumSize;

    internal static byte[] Create(JournalWitness witness)
    {
        ArgumentNullException.ThrowIfNull(witness);

        int payloadSize = checked(ParticipantNoncesOffset +
                                  witness.ParticipantNonces.Count * sizeof(ulong));
        byte[] record = new byte[checked(payloadSize + ChecksumSize)];
        BinaryPrimitives.WriteUInt64LittleEndian(record, Magic);
        witness.PreparationKey.TryWriteBytes(record.AsSpan(PreparationKeyOffset, 16));
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(ParticipantCountOffset),
            checked((uint)witness.ParticipantNonces.Count));

        for (int i = 0; i < witness.ParticipantNonces.Count; i++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(
                record.AsSpan(ParticipantNoncesOffset + i * sizeof(ulong)),
                witness.ParticipantNonces[i]);
        }

        ulong checksum = XxHash64.HashToUInt64(record.AsSpan(0, payloadSize));
        BinaryPrimitives.WriteUInt64LittleEndian(record.AsSpan(payloadSize), checksum);
        return record;
    }

    internal static JournalWitness Read(ReadOnlySpan<byte> record)
    {
        if (record.Length < MinimumSize)
            throw new InvalidDataException("The journal witness has an invalid length.");
        if (BinaryPrimitives.ReadUInt64LittleEndian(record) != Magic)
            throw new InvalidDataException("The journal witness has an unknown format version.");

        uint participantCount = BinaryPrimitives.ReadUInt32LittleEndian(record[ParticipantCountOffset..]);
        if (participantCount == 0 || participantCount > int.MaxValue)
            throw new InvalidDataException("The journal witness contains an invalid participant count.");

        int payloadSize;
        int expectedSize;
        try
        {
            payloadSize = checked(ParticipantNoncesOffset + (int)participantCount * sizeof(ulong));
            expectedSize = checked(payloadSize + ChecksumSize);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("The journal witness participant count is too large.", exception);
        }

        if (record.Length != expectedSize)
            throw new InvalidDataException("The journal witness length does not match its participant count.");

        ulong expectedChecksum = BinaryPrimitives.ReadUInt64LittleEndian(record[payloadSize..]);
        ulong actualChecksum = XxHash64.HashToUInt64(record[..payloadSize]);
        if (expectedChecksum != actualChecksum)
            throw new InvalidDataException("The journal witness checksum is invalid.");

        Guid preparationKey = new(record.Slice(PreparationKeyOffset, 16));
        if (preparationKey == Guid.Empty)
            throw new InvalidDataException("The journal witness contains an empty preparation key.");

        ulong[] participantNonces = new ulong[participantCount];
        for (int i = 0; i < participantNonces.Length; i++)
        {
            participantNonces[i] = BinaryPrimitives.ReadUInt64LittleEndian(
                record[(ParticipantNoncesOffset + i * sizeof(ulong))..]);
            if (i > 0 && participantNonces[i - 1] >= participantNonces[i])
                throw new InvalidDataException("The journal witness participant nonces are not canonical.");
        }

        return new JournalWitness(preparationKey, participantNonces);
    }
}
