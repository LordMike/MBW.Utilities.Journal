using MBW.Utilities.Journal.Tests.Helpers;

namespace MBW.Utilities.Journal.Tests;

public class JournalWitnessStoreTests
{
    [Fact]
    public async Task StoreReadAndClearAreKeyCheckedAndIdempotent()
    {
        MemoryJournalWitnessStore store = new();
        Guid key = Guid.NewGuid();

        Assert.Null(await store.ReadAsync());
        Assert.Null(store.FileContents);

        await store.StoreAsync(key);
        await store.StoreAsync(key);
        Assert.Equal(key, await store.ReadAsync());
        Assert.NotEmpty(store.FileContents!);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.StoreAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.ClearAsync(Guid.NewGuid()));

        await store.ClearAsync(key);
        await store.ClearAsync(key);
        Assert.Null(await store.ReadAsync());
        Assert.Null(store.FileContents);
    }

    [Fact]
    public async Task CorruptWitnessIsNeverTreatedAsMissing()
    {
        MemoryJournalWitnessStore store = new() { FileContents = [1, 2, 3] };

        await Assert.ThrowsAsync<InvalidDataException>(async () => await store.ReadAsync());
        await Assert.ThrowsAsync<InvalidDataException>(async () => await store.StoreAsync(Guid.NewGuid()));
        Assert.Equal([1, 2, 3], store.FileContents);
    }
}
