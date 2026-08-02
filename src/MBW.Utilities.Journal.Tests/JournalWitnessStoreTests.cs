using MBW.Utilities.Journal.Structures;
using MBW.Utilities.Journal.Tests.Helpers;

namespace MBW.Utilities.Journal.Tests;

public class JournalWitnessStoreTests
{
    [Fact]
    public void WitnessCanonicalizesAndOwnsItsParticipantSet()
    {
        ulong[] source = [30, 10, 20];
        JournalWitness witness = new(Guid.NewGuid(), source);
        source[0] = 99;

        Assert.Equal([10UL, 20UL, 30UL], witness.ParticipantNonces);
        Assert.Throws<ArgumentException>(() => new JournalWitness(Guid.Empty, [1]));
        Assert.Throws<ArgumentException>(() => new JournalWitness(Guid.NewGuid(), []));
        Assert.Throws<ArgumentException>(() => new JournalWitness(Guid.NewGuid(), [1, 1]));
    }

    [Fact]
    public async Task StoreReadAndClearAreExactAndIdempotent()
    {
        MemoryJournalWitnessStore store = new();
        Guid key = Guid.NewGuid();
        JournalWitness witness = new(key, [30, 10, 20]);
        JournalWitness equivalent = new(key, [10, 20, 30]);

        Assert.Null(await store.ReadAsync());

        await store.StoreAsync(witness);
        await store.StoreAsync(equivalent);
        Assert.True((await store.ReadAsync())!.ValueEquals(witness));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.StoreAsync(new JournalWitness(key, [10, 20])));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.StoreAsync(new JournalWitness(Guid.NewGuid(), [10, 20, 30])));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.ClearAsync(new JournalWitness(key, [10, 20])));

        await store.ClearAsync(equivalent);
        await store.ClearAsync(equivalent);
        Assert.Null(await store.ReadAsync());
    }

    [Fact]
    public async Task FileStoreRoundTripsTheCompleteWitness()
    {
        string path = Path.Combine(Path.GetTempPath(), $"journal-witness-{Guid.NewGuid():N}.tmp");
        try
        {
            FileBasedJournalWitnessStore store = new(path);
            JournalWitness witness = new(Guid.NewGuid(), [300, 100, 200]);

            await store.StoreAsync(witness);
            JournalWitness? stored = await store.ReadAsync();

            Assert.NotNull(stored);
            Assert.True(stored.ValueEquals(witness));
            await store.ClearAsync(witness);
            Assert.Null(await store.ReadAsync());
            Assert.False(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WitnessRecordValidatesSerializedData()
    {
        JournalWitness witness = new(Guid.NewGuid(), [30, 10, 20]);
        byte[] record = JournalWitnessRecord.Create(witness);
        JournalWitness roundTrip = JournalWitnessRecord.Read(record);
        Assert.True(roundTrip.ValueEquals(witness));

        Assert.Throws<InvalidDataException>(() => JournalWitnessRecord.Read([1, 2, 3]));

        byte[] oldVersion = record.ToArray();
        oldVersion[7] = (byte)'1';
        Assert.Throws<InvalidDataException>(() => JournalWitnessRecord.Read(oldVersion));

        byte[] badChecksum = record.ToArray();
        badChecksum[sizeof(ulong)] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => JournalWitnessRecord.Read(badChecksum));

        byte[] badLength = record[..^1];
        Assert.Throws<InvalidDataException>(() => JournalWitnessRecord.Read(badLength));
    }
}
