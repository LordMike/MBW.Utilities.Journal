using MBW.Utilities.Journal.Tests.Helpers;
using MBW.Utilities.Journal.Structures;

namespace MBW.Utilities.Journal.Tests;

public class JournalWitnessStoreTests
{
    [Fact]
    public async Task StoreReadAndClearAreKeyCheckedAndIdempotent()
    {
        MemoryJournalWitnessStore store = new();
        Guid key = Guid.NewGuid();

        Assert.Null(await store.ReadAsync());

        await store.StoreAsync(key);
        await store.StoreAsync(key);
        Assert.Equal(key, await store.ReadAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.StoreAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.ClearAsync(Guid.NewGuid()));

        await store.ClearAsync(key);
        await store.ClearAsync(key);
        Assert.Null(await store.ReadAsync());
    }

    [Fact]
    public void WitnessRecordValidatesSerializedData()
    {
        Guid key = Guid.NewGuid();
        byte[] record = JournalWitnessRecord.Create(key);
        Assert.Equal(key, JournalWitnessRecord.Read(record));

        Assert.Throws<ArgumentException>(() => JournalWitnessRecord.Create(Guid.Empty));
        Assert.Throws<InvalidDataException>(() => JournalWitnessRecord.Read([1, 2, 3]));

        byte[] badMagic = record.ToArray();
        badMagic[0] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => JournalWitnessRecord.Read(badMagic));

        byte[] badChecksum = record.ToArray();
        badChecksum[sizeof(ulong)] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => JournalWitnessRecord.Read(badChecksum));
    }
}
