using Lifestone.Client.Rendering;
using Lifestone.Data.Geometry;

namespace Lifestone.Client.Tests;

public sealed class RenderResourceCachesTests
{
    private static readonly SurfaceShaderVariant Variant = new(SurfaceBlend.Opaque, false, true, SurfaceCull.Back, TextureAddressMode.Wrap, true, true);

    private static TextureKey TexKey(int surface) => new((uint)(0x06000000 + surface), TextureKey.DefaultPalette, false, false);

    private static MaterialKey MatKey(int surface) => new((uint)(0x08000000 + surface), TexKey(surface), Variant);

    [Fact]
    public void Releasing_the_last_mesh_reference_unbinds_and_releases_the_whole_chain()
    {
        var host = new FakeHost();
        var caches = new RenderResourceCaches<FakeTexture, FakeMaterial, FakeMesh>(host, textureBudgetBytes: 0, materialBudget: 100, meshBudgetBytes: 1_000_000);
        var mesh = AcquireMesh(caches, host, "m", [1, 2]);
        Assert.Equal(2, mesh.Bound!.Length);
        Assert.All(mesh.Bound, m => Assert.NotNull(m.Bound));

        caches.ReleaseMesh("m");

        // The mesh stays cached (under its budget) but pins nothing: its materials and their textures are unbound and
        // released, and the textures (budget 0) are evicted and freed.
        Assert.Null(mesh.Bound);
        Assert.False(mesh.Freed);
        Assert.Equal(1, caches.Meshes.IdleCount);
        Assert.Equal(2, caches.Materials.IdleCount);
        Assert.All(host.Materials, m => Assert.Null(m.Bound));
        Assert.Equal(0, caches.Textures.Count);
        Assert.Equal(2, caches.Textures.Evictions);
        Assert.All(host.Textures, t => Assert.True(t.Freed));
    }

    [Fact]
    public void Reacquiring_an_idle_mesh_rebinds_materials_and_textures_from_the_callbacks()
    {
        var host = new FakeHost();
        var caches = new RenderResourceCaches<FakeTexture, FakeMaterial, FakeMesh>(host, textureBudgetBytes: 1_000_000, materialBudget: 100, meshBudgetBytes: 1_000_000);
        var mesh = AcquireMesh(caches, host, "m", [1, 2]);
        var materials = mesh.Bound!.ToArray();
        caches.ReleaseMesh("m");
        Assert.Null(mesh.Bound);

        var materialCalls = 0;
        Assert.True(caches.TryAcquireMesh(
            "m",
            () =>
            {
                materialCalls++;
                return [AcquireMaterial(caches, host, 1), AcquireMaterial(caches, host, 2)];
            },
            out var again));
        Assert.Same(mesh, again);
        Assert.Equal(1, materialCalls);
        Assert.Equal(materials, mesh.Bound); // same cached materials, rebound with their (still cached) textures
        Assert.All(mesh.Bound!, m => Assert.NotNull(m.Bound));
        Assert.Equal(2, host.TexturesCreated); // nothing re-uploaded

        // A second holder of an in-use mesh does not ask for materials.
        Assert.True(caches.TryAcquireMesh("m", () => throw new InvalidOperationException("not idle"), out _));
    }

    [Fact]
    public void A_failing_texture_callback_leaves_the_material_unheld_and_unbound()
    {
        var host = new FakeHost();
        var caches = new RenderResourceCaches<FakeTexture, FakeMaterial, FakeMesh>(host, 0, 100, 1_000_000);
        var handle = AcquireMaterial(caches, host, 7);
        caches.ReleaseMaterial(handle);
        Assert.Null(handle.Material.Bound);
        Assert.Throws<InvalidDataException>(() => caches.TryAcquireMaterial(MatKey(7), _ => throw new InvalidDataException("decode failed"), out _));
        Assert.Equal(1, caches.Materials.IdleCount);
        Assert.Null(handle.Material.Bound);
    }

    [Fact]
    public void Budgets_hold_across_a_long_simulated_flight_and_nothing_is_freed_while_bound()
    {
        // 600 landblocks in a straight line, a 9-landblock window streaming along it (unloads one ring later). Each
        // landblock has 8 meshes: 2 shared by the whole world (trees), 3 regional (shared by 10 consecutive
        // landblocks) and 3 of its own; each mesh has 2 materials; each surface has its own texture (8-64 KB).
        // Budgets are far below what the flight touches.
        const long textureBudget = 4L * 1024 * 1024;
        const int materialBudget = 150;
        const long meshBudget = 64 * 1024;
        var host = new FakeHost();
        var caches = new RenderResourceCaches<FakeTexture, FakeMaterial, FakeMesh>(host, textureBudget, materialBudget, meshBudget);
        var held = new Dictionary<int, List<string>>();
        long maxTextureBytes = 0;
        for (var focus = 0; focus < 600; focus++)
        {
            for (var lb = focus - 4; lb <= focus + 4; lb++)
            {
                if (lb >= 0 && !held.ContainsKey(lb))
                {
                    held[lb] = Attach(caches, host, lb);
                }
            }

            foreach (var lb in held.Keys.Where(k => k < focus - 5).ToList())
            {
                foreach (var key in held[lb])
                {
                    caches.ReleaseMesh(key);
                }

                held.Remove(lb);
            }

            var heldTextureBytes = host.Textures.Where(t => !t.Freed && t.BoundBy > 0).Sum(t => t.Bytes);
            maxTextureBytes = Math.Max(maxTextureBytes, caches.Textures.TotalBytes);

            // Each cache exceeds its budget only by what is in use: idle entries are trimmed and pin nothing.
            Assert.True(caches.Textures.TotalBytes <= Math.Max(textureBudget, heldTextureBytes), $"textures {caches.Textures.TotalBytes} at {focus}");
            Assert.True(caches.Meshes.TotalBytes <= meshBudget || caches.Meshes.IdleCount == 0);
            Assert.True(caches.Materials.TotalBytes <= materialBudget || caches.Materials.IdleCount == 0);
            Assert.Equal(host.Textures.Count(t => !t.Freed), caches.Textures.Count);
            Assert.Equal(host.Materials.Count(m => !m.Freed), caches.Materials.Count);
            Assert.Equal(host.Meshes.Count(m => !m.Freed), caches.Meshes.Count);
        }

        Assert.True(host.TexturesCreated > 3000, $"the flight should touch many textures ({host.TexturesCreated})");
        Assert.True(caches.Textures.Evictions > 3000, $"texture evictions {caches.Textures.Evictions}");
        Assert.True(caches.Materials.Evictions > 3000, $"material evictions {caches.Materials.Evictions}");
        Assert.True(caches.Meshes.Evictions > 1500, $"mesh evictions {caches.Meshes.Evictions}");
        Assert.True(maxTextureBytes < host.TexturesCreatedBytes / 10, $"max resident {maxTextureBytes} of {host.TexturesCreatedBytes} created");

        // Every mesh still held is fully bound; nothing bound was freed.
        foreach (var mesh in host.Meshes.Where(m => !m.Freed && m.Bound is not null))
        {
            Assert.All(mesh.Bound!, m => Assert.False(m.Freed));
            Assert.All(mesh.Bound!, m => Assert.True(m.Bound is { Freed: false }));
        }

        // Land: release everything and clear; every resource is freed exactly once (FakeHost asserts "once").
        foreach (var key in held.Values.SelectMany(k => k))
        {
            caches.ReleaseMesh(key);
        }

        caches.ClearIdle();
        Assert.Equal(0, caches.Textures.Count);
        Assert.Equal(0, caches.Materials.Count);
        Assert.Equal(0, caches.Meshes.Count);
        Assert.All(host.Textures, t => Assert.True(t.Freed));
        Assert.All(host.Materials, m => Assert.True(m.Freed));
        Assert.All(host.Meshes, m => Assert.True(m.Freed));
    }

    private static List<string> Attach(RenderResourceCaches<FakeTexture, FakeMaterial, FakeMesh> caches, FakeHost host, int landblock)
    {
        var region = landblock / 10;
        var keys = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            var (key, surfaces) = i switch
            {
                < 2 => ($"tree{i}", new[] { 1_000_000 + (2 * i), 1_000_001 + (2 * i) }),
                < 5 => ($"r{region}m{i}", new[] { 500_000 + (region * 12) + ((i * 5) % 12), 500_000 + (region * 12) + ((i * 5 + 7) % 12) }),
                _ => ($"lb{landblock}m{i}", new[] { (landblock * 6) + ((i - 5) * 2), (landblock * 6) + ((i - 5) * 2) + 1 }),
            };
            AcquireMesh(caches, host, key, surfaces);
            keys.Add(key);
        }

        return keys;
    }

    private static FakeMesh AcquireMesh(RenderResourceCaches<FakeTexture, FakeMaterial, FakeMesh> caches, FakeHost host, string key, int[] surfaces)
    {
        if (caches.TryAcquireMesh(key, () => surfaces.Select(s => AcquireMaterial(caches, host, s)).ToArray(), out var mesh))
        {
            return mesh;
        }

        var materials = surfaces.Select(s => AcquireMaterial(caches, host, s)).ToArray();
        return caches.AddMesh(key, host.CreateMesh(key, surfaces.Length), materials, 4096);
    }

    private static MaterialHandle<FakeMaterial> AcquireMaterial(RenderResourceCaches<FakeTexture, FakeMaterial, FakeMesh> caches, FakeHost host, int surface)
    {
        var key = MatKey(surface);
        if (caches.TryAcquireMaterial(key, textureKey => AcquireTexture(caches, host, textureKey, surface), out var material))
        {
            return new(key, material);
        }

        var texture = AcquireTexture(caches, host, key.Texture!.Value, surface);
        return new(key, caches.AddMaterial(key, host.CreateMaterial(key), texture));
    }

    private static FakeTexture AcquireTexture(RenderResourceCaches<FakeTexture, FakeMaterial, FakeMesh> caches, FakeHost host, TextureKey key, int surface)
    {
        if (caches.TryAcquireTexture(key, out var texture))
        {
            return texture;
        }

        var bytes = 8192L << (surface % 4);
        return caches.AddTexture(key, host.CreateTexture(key, bytes), bytes);
    }

    private sealed class FakeTexture(TextureKey key, long bytes)
    {
        public TextureKey Key { get; } = key;

        public long Bytes { get; } = bytes;

        public int BoundBy { get; set; }

        public bool Freed { get; set; }
    }

    private sealed class FakeMaterial(MaterialKey key)
    {
        public MaterialKey Key { get; } = key;

        public FakeTexture? Bound { get; set; }

        public int BoundBy { get; set; }

        public bool Freed { get; set; }
    }

    private sealed class FakeMesh(string key, int surfaces)
    {
        public string Key { get; } = key;

        public int Surfaces { get; } = surfaces;

        public FakeMaterial[]? Bound { get; set; }

        public bool Freed { get; set; }
    }

    /// <summary>Tracks bindings and asserts the engine-side invariants: never bind or free a freed resource, never free a bound one, free once.</summary>
    private sealed class FakeHost : IRenderResourceHost<FakeTexture, FakeMaterial, FakeMesh>
    {
        public List<FakeTexture> Textures { get; } = new();

        public List<FakeMaterial> Materials { get; } = new();

        public List<FakeMesh> Meshes { get; } = new();

        public int TexturesCreated => Textures.Count;

        public long TexturesCreatedBytes => Textures.Sum(t => t.Bytes);

        public FakeTexture CreateTexture(TextureKey key, long bytes)
        {
            var t = new FakeTexture(key, bytes);
            Textures.Add(t);
            return t;
        }

        public FakeMaterial CreateMaterial(MaterialKey key)
        {
            var m = new FakeMaterial(key);
            Materials.Add(m);
            return m;
        }

        public FakeMesh CreateMesh(string key, int surfaces)
        {
            var m = new FakeMesh(key, surfaces);
            Meshes.Add(m);
            return m;
        }

        public void BindTexture(FakeMaterial material, FakeTexture? texture)
        {
            Assert.False(material.Freed);
            Assert.False(texture?.Freed ?? false);
            if (material.Bound is { } old)
            {
                old.BoundBy--;
            }

            material.Bound = texture;
            if (texture is not null)
            {
                Assert.Equal(material.Key.Texture, texture.Key);
                texture.BoundBy++;
            }
        }

        public void BindMaterials(FakeMesh mesh, IReadOnlyList<FakeMaterial>? materials)
        {
            Assert.False(mesh.Freed);
            foreach (var old in mesh.Bound ?? [])
            {
                old.BoundBy--;
            }

            if (materials is not null)
            {
                Assert.Equal(mesh.Surfaces, materials.Count);
                Assert.All(materials, m => Assert.False(m.Freed));
                Assert.All(materials, m => Assert.NotNull(m.Bound)); // a bound material always has its texture
                foreach (var m in materials)
                {
                    m.BoundBy++;
                }
            }

            mesh.Bound = materials?.ToArray();
        }

        public void FreeTexture(TextureKey key, FakeTexture texture)
        {
            Assert.Equal(key, texture.Key);
            Assert.False(texture.Freed);
            Assert.Equal(0, texture.BoundBy);
            texture.Freed = true;
        }

        public void FreeMaterial(MaterialKey key, FakeMaterial material)
        {
            Assert.Equal(key, material.Key);
            Assert.False(material.Freed);
            Assert.Equal(0, material.BoundBy);
            Assert.Null(material.Bound);
            material.Freed = true;
        }

        public void FreeMesh(string key, FakeMesh mesh)
        {
            Assert.Equal(key, mesh.Key);
            Assert.False(mesh.Freed);
            Assert.Null(mesh.Bound);
            mesh.Freed = true;
        }
    }
}
