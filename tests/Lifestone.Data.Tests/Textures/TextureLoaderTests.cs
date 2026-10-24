using System.Security.Cryptography;
using DatReaderWriter.DBObjs;
using Lifestone.Data.Dat;
using Lifestone.Data.Textures;

namespace Lifestone.Data.Tests.Textures;

/// <summary>Loader plumbing: worker-thread safety and the palette LRU.</summary>
public sealed class TextureLoaderTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private DatSet Dats => fixture.Dats;

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Parallel_decodes_match_sequential_decodes()
    {
        var ids = Dats.Read(d => d.Portal.GetAllIdsOfType<Surface>().Where((_, i) => i % 25 == 0).ToArray());
        var sequential = ids.Select(id => Hash(new TextureLoader(Dats, 4).DecodeSurface(id, TextureDecodeMode.Rgba8))).ToArray();

        var shared = new TextureLoader(Dats, paletteCacheCapacity: 4); // small: forces LRU churn across threads
        var parallel = new string[ids.Length];
        Parallel.For(0, ids.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
            parallel[i] = Hash(shared.DecodeSurface(ids[i], TextureDecodeMode.Rgba8)));

        Assert.Equal(sequential, parallel);
    }

    [Fact]
    public void Lru_cache_evicts_the_least_recently_used_entry()
    {
        var lru = new LruCache<int, string>(2);
        lru.Add(1, "a");
        lru.Add(2, "b");
        Assert.True(lru.TryGet(1, out _)); // 1 becomes most recent
        lru.Add(3, "c");                    // evicts 2
        Assert.False(lru.TryGet(2, out _));
        Assert.True(lru.TryGet(1, out var one));
        Assert.Equal("a", one);
        Assert.True(lru.TryGet(3, out _));
        Assert.Equal(2, lru.Count);
    }

    private static string Hash(DecodedSurface? d) =>
        d?.Texture is null ? "solid" : Convert.ToHexString(SHA256.HashData(d.Texture.Data));
}
