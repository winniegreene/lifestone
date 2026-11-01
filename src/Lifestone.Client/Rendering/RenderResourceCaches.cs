using System.Diagnostics.CodeAnalysis;
using Lifestone.Client.Caching;

namespace Lifestone.Client.Rendering;

/// <summary>
/// The engine operations <see cref="RenderResourceCaches{TTexture, TMaterial, TMesh}"/> needs: binding a material's
/// texture and a mesh's materials, and freeing evicted resources. Called on the engine main thread.
/// </summary>
public interface IRenderResourceHost<TTexture, TMaterial, TMesh>
{
    /// <summary>Sets (or, with null, clears) the texture a material samples.</summary>
    void BindTexture(TMaterial material, TTexture? texture);

    /// <summary>Sets the material of every mesh surface (one per surface, in order), or clears them all (null).</summary>
    void BindMaterials(TMesh mesh, IReadOnlyList<TMaterial>? materials);

    /// <summary>An idle texture left the cache: free it (nothing the caches know of references it any more).</summary>
    void FreeTexture(TextureKey key, TTexture texture);

    /// <summary>An idle material left the cache: free it. Its texture was unbound when it went idle.</summary>
    void FreeMaterial(MaterialKey key, TMaterial material);

    /// <summary>An idle mesh left the cache: free it. Its materials were unbound when it went idle.</summary>
    void FreeMesh(string key, TMesh mesh);
}

/// <summary>A mark of the three render caches' use generations (<see cref="RenderResourceCaches{TTexture, TMaterial, TMesh}.Mark"/>).</summary>
public readonly record struct RenderCacheMark(long Textures, long Materials, long Meshes);

/// <summary>One cache's entries and bytes before and after a trim (materials count themselves as bytes).</summary>
public readonly record struct CacheTrimCounts(int CountBefore, long BytesBefore, int CountAfter, long BytesAfter)
{
    public int Evicted => CountBefore - CountAfter;

    public long BytesFreed => BytesBefore - BytesAfter;
}

/// <summary>What <see cref="IRenderCacheTrim.TrimUnusedSince"/> did to each cache.</summary>
public readonly record struct RenderCacheTrimResult(CacheTrimCounts Textures, CacheTrimCounts Materials, CacheTrimCounts Meshes);

/// <summary>The trim half of the render caches (M5 7 P7d): what <see cref="ArrivalCacheTrim"/> drives, so it is tested without an engine.</summary>
public interface IRenderCacheTrim
{
    /// <summary>Starts a new use generation in every cache and returns it.</summary>
    RenderCacheMark Mark();

    /// <summary>Evicts (and frees) every idle mesh, material and texture not used since <paramref name="mark"/>; entries in use stay.</summary>
    RenderCacheTrimResult TrimUnusedSince(RenderCacheMark mark);
}

/// <summary>A material reference: <see cref="Key"/> is null for an uncounted material (the loud error material).</summary>
public readonly record struct MaterialHandle<TMaterial>(MaterialKey? Key, TMaterial Material)
    where TMaterial : class;

/// <summary>
/// The shared texture, material and mesh caches of the render path, chained so that every budget holds: an entry
/// references its dependencies (a mesh its materials, a material its texture) only while someone holds it.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> A mesh that kept its materials while idle would keep their textures alive too, so the texture
/// budget could never evict anything until meshes did, and idle meshes are tiny. Here, when the last holder releases
/// a mesh, its materials are unbound from it and released; a material that goes idle unbinds and releases its texture;
/// an idle texture is evicted by the texture cache's least-recently-used byte budget and freed. Idle entries therefore
/// pin nothing beyond their own size, and each cache's budget bounds its own resources.</para>
/// <para><b>Reuse.</b> Re-acquiring an idle mesh calls back for material references (the caller has them from the
/// landblock's worker-prepared surfaces), and re-acquiring an idle material calls back for a texture reference; both are
/// bound again. Entries in use are never evicted.</para>
/// <para>Engine-agnostic: the engine supplies <see cref="IRenderResourceHost{TTexture, TMaterial, TMesh}"/>. Main thread
/// only.</para>
/// </remarks>
public sealed class RenderResourceCaches<TTexture, TMaterial, TMesh> : IRenderCacheTrim
    where TTexture : class
    where TMaterial : class
    where TMesh : class
{
    private readonly IRenderResourceHost<TTexture, TMaterial, TMesh> _host;
    private readonly ResourceCache<TextureKey, TTexture> _textures;
    private readonly ResourceCache<MaterialKey, MaterialEntry> _materials;
    private readonly ResourceCache<string, MeshEntry> _meshes;

    /// <summary>Creates the caches.</summary>
    /// <param name="host">Engine operations.</param>
    /// <param name="textureBudgetBytes">GPU bytes of textures (held and idle) before idle ones are evicted.</param>
    /// <param name="materialBudget">Materials (held and idle) before idle ones are evicted.</param>
    /// <param name="meshBudgetBytes">Geometry bytes of meshes (held and idle) before idle ones are evicted.</param>
    public RenderResourceCaches(IRenderResourceHost<TTexture, TMaterial, TMesh> host, long textureBudgetBytes, int materialBudget, long meshBudgetBytes)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        _textures = new ResourceCache<TextureKey, TTexture>("textures", textureBudgetBytes, (key, texture) => _host.FreeTexture(key, texture));
        _materials = new ResourceCache<MaterialKey, MaterialEntry>(
            "materials",
            materialBudget,
            (key, entry) => _host.FreeMaterial(key, entry.Material),
            OnMaterialIdle);
        _meshes = new ResourceCache<string, MeshEntry>("meshes", meshBudgetBytes, (key, entry) => _host.FreeMesh(key, entry.Mesh), OnMeshIdle);
    }

    public IResourceCacheStats Textures => _textures;

    /// <summary>Material statistics; "bytes" are material counts.</summary>
    public IResourceCacheStats Materials => _materials;

    public IResourceCacheStats Meshes => _meshes;

    public long TextureBudgetBytes
    {
        get => _textures.BudgetBytes;
        set
        {
            _textures.BudgetBytes = value;
            _textures.Trim();
        }
    }

    public long MeshBudgetBytes
    {
        get => _meshes.BudgetBytes;
        set
        {
            _meshes.BudgetBytes = value;
            _meshes.Trim();
        }
    }

    // ---- Textures ----

    /// <summary>True when the texture is cached (held or idle).</summary>
    public bool ContainsTexture(TextureKey key) => _textures.Contains(key);

    /// <summary>Takes a reference to a cached texture.</summary>
    public bool TryAcquireTexture(TextureKey key, [MaybeNullWhen(false)] out TTexture texture) => _textures.TryAcquire(key, out texture);

    /// <summary>Adds a new texture with one reference taken.</summary>
    public TTexture AddTexture(TextureKey key, TTexture texture, long bytes) => _textures.Add(key, texture, bytes);

    /// <summary>Drops a texture reference.</summary>
    public void ReleaseTexture(TextureKey key) => _textures.Release(key);

    // ---- Materials ----

    /// <summary>
    /// Takes a reference to a cached material. If it has no texture bound (it was idle), <paramref name="acquireTexture"/>
    /// is called for a reference to <see cref="MaterialKey.Texture"/>, which the material binds and holds while in use.
    /// If that callback throws, the material reference is dropped again and the exception propagates.
    /// </summary>
    public bool TryAcquireMaterial(MaterialKey key, Func<TextureKey, TTexture> acquireTexture, [MaybeNullWhen(false)] out TMaterial material)
    {
        ArgumentNullException.ThrowIfNull(acquireTexture);
        if (!_materials.TryAcquire(key, out var entry))
        {
            material = null;
            return false;
        }

        if (key.Texture is { } textureKey && !entry.TextureBound)
        {
            TTexture texture;
            try
            {
                texture = acquireTexture(textureKey);
            }
            catch
            {
                _materials.Release(key);
                throw;
            }

            _host.BindTexture(entry.Material, texture);
            entry.TextureBound = true;
        }

        material = entry.Material;
        return true;
    }

    /// <summary>
    /// Adds a new material with one reference taken. <paramref name="texture"/> is a reference the caller took for
    /// <see cref="MaterialKey.Texture"/> (null when the key has none); the cache binds it and releases it when the
    /// material goes idle.
    /// </summary>
    public TMaterial AddMaterial(MaterialKey key, TMaterial material, TTexture? texture)
    {
        ArgumentNullException.ThrowIfNull(material);
        if ((key.Texture is null) != (texture is null))
        {
            throw new ArgumentException("A texture reference must be given exactly when the key names a texture.", nameof(texture));
        }

        var entry = new MaterialEntry(material);
        if (texture is not null)
        {
            _host.BindTexture(material, texture);
            entry.TextureBound = true;
        }

        return _materials.Add(key, entry, 1).Material;
    }

    /// <summary>Drops a material reference.</summary>
    public void ReleaseMaterial(MaterialKey key) => _materials.Release(key);

    /// <summary>Drops a material reference (uncounted handles are ignored).</summary>
    public void ReleaseMaterial(MaterialHandle<TMaterial> handle)
    {
        if (handle.Key is { } key)
        {
            _materials.Release(key);
        }
    }

    // ---- Meshes ----

    /// <summary>
    /// Takes a reference to a cached mesh. If it has no materials bound (it was idle), <paramref name="acquireMaterials"/>
    /// is called for one material reference per surface, which the mesh binds and holds while in use.
    /// </summary>
    public bool TryAcquireMesh(string key, Func<IReadOnlyList<MaterialHandle<TMaterial>>> acquireMaterials, [MaybeNullWhen(false)] out TMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(acquireMaterials);
        if (!_meshes.TryAcquire(key, out var entry))
        {
            mesh = null;
            return false;
        }

        if (entry.Materials is null)
        {
            IReadOnlyList<MaterialHandle<TMaterial>> materials;
            try
            {
                materials = acquireMaterials();
            }
            catch
            {
                _meshes.Release(key);
                throw;
            }

            Bind(entry, materials);
        }

        mesh = entry.Mesh;
        return true;
    }

    /// <summary>
    /// Adds a new mesh with one reference taken. The cache takes over <paramref name="materials"/> (references the
    /// caller took, one per surface), binds them, and releases them when the mesh goes idle.
    /// </summary>
    public TMesh AddMesh(string key, TMesh mesh, IReadOnlyList<MaterialHandle<TMaterial>> materials, long bytes)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(materials);
        var entry = new MeshEntry(mesh);
        Bind(entry, materials);
        return _meshes.Add(key, entry, bytes).Mesh;
    }

    /// <summary>Drops a mesh reference.</summary>
    public void ReleaseMesh(string key) => _meshes.Release(key);

    /// <summary>True when the mesh is cached (held or idle).</summary>
    public bool ContainsMesh(string key) => _meshes.Contains(key);

    /// <inheritdoc/>
    public RenderCacheMark Mark() => new(_textures.Mark(), _materials.Mark(), _meshes.Mark());

    /// <inheritdoc/>
    /// <remarks>Meshes first, then materials, then textures: an idle entry already dropped what it held when it went idle, so the order only
    /// keeps each cache's numbers in step with what the engine still holds.</remarks>
    public RenderCacheTrimResult TrimUnusedSince(RenderCacheMark mark)
    {
        var meshes = Trim(_meshes, mark.Meshes);
        var materials = Trim(_materials, mark.Materials);
        var textures = Trim(_textures, mark.Textures);
        return new RenderCacheTrimResult(textures, materials, meshes);

        static CacheTrimCounts Trim<TKey, TValue>(ResourceCache<TKey, TValue> cache, long since)
            where TKey : notnull
            where TValue : class
        {
            var (count, bytes) = (cache.Count, cache.TotalBytes);
            cache.TrimUnusedSince(since);
            return new CacheTrimCounts(count, bytes, cache.Count, cache.TotalBytes);
        }
    }

    /// <summary>Evicts (and frees) every idle mesh, material and texture.</summary>
    public void ClearIdle()
    {
        _meshes.ClearIdle();
        _materials.ClearIdle();
        _textures.ClearIdle();
    }

    private void Bind(MeshEntry entry, IReadOnlyList<MaterialHandle<TMaterial>> materials)
    {
        var copy = materials.ToArray();
        _host.BindMaterials(entry.Mesh, copy.Select(m => m.Material).ToArray());
        entry.Materials = copy;
    }

    private void OnMeshIdle(string key, MeshEntry entry)
    {
        if (entry.Materials is not { } materials)
        {
            return;
        }

        _host.BindMaterials(entry.Mesh, null);
        entry.Materials = null;
        foreach (var material in materials)
        {
            ReleaseMaterial(material);
        }
    }

    private void OnMaterialIdle(MaterialKey key, MaterialEntry entry)
    {
        if (!entry.TextureBound || key.Texture is not { } textureKey)
        {
            return;
        }

        _host.BindTexture(entry.Material, null);
        entry.TextureBound = false;
        _textures.Release(textureKey);
    }

    private sealed class MaterialEntry(TMaterial material)
    {
        public TMaterial Material { get; } = material;

        public bool TextureBound { get; set; }
    }

    private sealed class MeshEntry(TMesh mesh)
    {
        public TMesh Mesh { get; } = mesh;

        public MaterialHandle<TMaterial>[]? Materials { get; set; }
    }
}
