using DatReaderWriter.Types;
using Lifestone.Data.Models;

namespace Lifestone.Data.Tests.Models;

public sealed class GfxObjMeshCacheTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [Fact]
    public void Concurrent_requests_for_one_id_build_once_and_share_the_mesh()
    {
        var builds = 0;
        var cache = new GfxObjMeshCache(id =>
        {
            Interlocked.Increment(ref builds);
            Thread.Sleep(20);
            return Pentagon(id);
        });

        var results = new GfxObjMesh?[32];
        Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i => results[i] = cache.Get(0x01000001));
        Assert.Equal(1, builds);
        Assert.All(results, r => Assert.Same(results[0], r));
        Assert.Equal(31, cache.Hits);
        Assert.Equal(1, cache.Misses);
    }

    [Fact]
    public void Least_recently_used_entries_are_evicted_by_count()
    {
        var cache = new GfxObjMeshCache(Pentagon, maxEntries: 3);
        var first = cache.Get(1);
        cache.Get(2);
        cache.Get(3);
        Assert.Same(first, cache.Get(1)); // 1 becomes most recent
        cache.Get(4);                     // evicts 2
        Assert.Equal(3, cache.Count);
        Assert.Equal(1, cache.Evictions);

        var misses = cache.Misses;
        cache.Get(1);
        cache.Get(3);
        Assert.Equal(misses, cache.Misses);
        cache.Get(2);
        Assert.Equal(misses + 1, cache.Misses);
    }

    [Fact]
    public void Vertex_budget_bounds_the_cache()
    {
        // Each synthetic pentagon has 5 vertices.
        var cache = new GfxObjMeshCache(Pentagon, maxEntries: 100, maxVertices: 12);
        cache.Get(1);
        cache.Get(2);
        Assert.Equal(10, cache.CachedVertexCount);
        cache.Get(3);
        Assert.Equal(2, cache.Count);
        Assert.Equal(10, cache.CachedVertexCount);
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.CachedVertexCount);
    }

    [Fact]
    public void Missing_gfxobjs_are_cached_as_null_and_failures_are_not_cached()
    {
        var calls = 0;
        var failNext7 = true;
        var cache = new GfxObjMeshCache(id =>
        {
            calls++;
            if (id == 7 && failNext7)
            {
                failNext7 = false;
                throw new InvalidDataException("boom");
            }

            return id == 9 ? null : Pentagon(id);
        });

        Assert.Null(cache.Get(9));
        Assert.Null(cache.Get(9));
        Assert.Equal(1, calls);

        Assert.Throws<InvalidDataException>(() => cache.Get(7));
        Assert.NotNull(cache.Get(7));
        Assert.Equal(3, calls);
    }

    [RequiresDatsFact]
    public void Dat_backed_cache_returns_the_same_meshes_as_direct_builds_under_parallel_load()
    {
        var cache = new GfxObjMeshCache(fixture.Dats, maxEntries: 64);
        uint[] ids = [0x01000C1E, 0x01000BC3, 0x010037A1, 0x0100004E, 0x010001EC, 0x010008AA, 0x0100007B, 0x01004E29];
        var direct = ids.ToDictionary(id => id, id => GfxObjMeshBuilder.Load(fixture.Dats, id));
        Parallel.For(0, 400, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            var id = ids[i % ids.Length];
            var mesh = cache.Get(id);
            var expected = direct[id];
            if (expected is null)
            {
                Assert.Null(mesh);
                return;
            }

            Assert.NotNull(mesh);
            Assert.Equal(expected.Mesh.TriangleCount, mesh.Mesh.TriangleCount);
            Assert.Equal(expected.Mesh.SubMeshes.Select(s => (s.SurfaceId, s.Side)), mesh.Mesh.SubMeshes.Select(s => (s.SurfaceId, s.Side)));
        });

        Assert.Equal(ids.Length, cache.Misses);
        Assert.Same(cache.Get(0x01000C1E), cache.Get(0x01000C1E));
    }

    private static GfxObjMesh Pentagon(uint id) =>
        GfxObjMeshBuilder.Build(
            id,
            GfxObjMeshBuilderTests.Synthetic(
                [0x08000001],
                GfxObjMeshBuilderTests.Pentagon(),
                new Polygon { PosSurface = 0, VertexIds = [0, 1, 2, 3, 4], PosUVIndices = [0, 0, 0, 0, 0] }));
}
