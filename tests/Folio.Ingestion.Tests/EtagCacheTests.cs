using Folio.Ingestion.GitHub;

namespace Folio.Ingestion.Tests;

public sealed class EtagCacheTests
{
    [Test]
    public async Task Concurrent_Updates_And_Evictions_Never_Exceed_The_Capacity()
    {
        EtagCache cache = new(capacity: 64);

        // Interleave fresh keys (drive eviction) with re-Sets of stable keys (the update path that raced).
        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (int i = 0; i < 5000; i++)
            {
                cache.Set($"fresh-{worker}-{i}", new CachedResponse("e", null, "t"));
                cache.Set($"stable-{i % 16}", new CachedResponse("e", null, "t"));
            }
        })));

        await Assert.That(cache.Count).IsLessThanOrEqualTo(64);
    }
}
