using MBW.Utilities.Journal.Helpers;

namespace MBW.Utilities.Journal.Tests;

public class JournalNonceGeneratorTests
{
    [Fact]
    public void GeneratesUniqueNoncesAcrossConcurrentCallers()
    {
        ulong[] nonces = new ulong[100_000];

        Parallel.For(0, nonces.Length, i => nonces[i] = JournalNonceGenerator.Next());

        Assert.Equal(nonces.Length, nonces.Distinct().Count());
    }
}
