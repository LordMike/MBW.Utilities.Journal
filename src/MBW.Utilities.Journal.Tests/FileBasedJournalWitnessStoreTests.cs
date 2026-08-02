namespace MBW.Utilities.Journal.Tests;

public class FileBasedJournalWitnessStoreTests
{
    [Fact]
    public async Task StoreReadAndClearAreKeyCheckedAndIdempotent()
    {
        string directory = Path.Combine(Path.GetTempPath(), "journal-witness-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(directory, "transaction.witness");
        FileBasedJournalWitnessStore store = new(file);
        Guid key = Guid.NewGuid();

        try
        {
            Assert.False(Directory.Exists(directory));
            Assert.Null(await store.ReadAsync());
            Assert.False(Directory.Exists(directory));

            await store.StoreAsync(key);
            await store.StoreAsync(key);
            Assert.Equal(key, await store.ReadAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await store.StoreAsync(Guid.NewGuid()));
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await store.ClearAsync(Guid.NewGuid()));
            Assert.True(File.Exists(file));

            await store.ClearAsync(key);
            await store.ClearAsync(key);
            Assert.Null(await store.ReadAsync());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task CorruptWitnessIsNeverTreatedAsMissing()
    {
        string file = Path.Combine(Path.GetTempPath(), "journal-witness-" + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllBytesAsync(file, [1, 2, 3]);
            FileBasedJournalWitnessStore store = new(file);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await store.ReadAsync());
            await Assert.ThrowsAsync<InvalidDataException>(async () => await store.StoreAsync(Guid.NewGuid()));
            Assert.True(File.Exists(file));
        }
        finally
        {
            File.Delete(file);
        }
    }
}
