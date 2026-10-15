using Godot;
using Lifestone.Client.Rendering;
using Lifestone.Data.Appearance;
using Lifestone.Data.Models;
using Lifestone.Game.Core;
using Lifestone.Game.Render;

namespace Lifestone.Game.SpellViewer;

/// <summary>
/// GfxObj meshes for actor parts through the shared render path (<see cref="RenderResources.AcquireMesh"/> with the
/// statics' key <c>gfxobj:0x…:Object</c>, so a GfxObj already resident is shared). Every sub-mesh is drawn
/// (<see cref="MeshDrawContext.Object"/>: creatures and projectiles go through the part array). One reference per GfxObj
/// is held until <see cref="Dispose"/>.
/// </summary>
/// <remarks>
/// <para><b>Lighting.</b> A mesh's materials carry the lighting rule they were drawn with (<see cref="SurfaceLighting"/>), so a
/// library is made for one rule and the rule is part of the shared-mesh key: the default <see cref="SurfaceLighting.Object"/>
/// keeps the statics' key (<c>gfxobj:0x…:Object</c>); any other rule appends its name
/// (<c>gfxobj:0x…:Object:CreatureMode</c>), so creature-mode views (the portal tunnel, the character-creation figure) build
/// their own meshes with their own shaders and never share one lit by the world's sun.</para>
/// </remarks>
public sealed class ActorMeshLibrary : IDisposable
{
    private readonly GameServices _services;
    private readonly SurfaceLighting _lighting;
    private readonly Dictionary<uint, ArrayMesh?> _meshes = new();
    private readonly Dictionary<string, ArrayMesh?> _variants = new();
    private readonly bool _fadeTwins;
    private readonly Dictionary<ulong, ArrayMesh> _fades = new();
    private readonly Dictionary<string, PendingFade> _pendingFades = new();
    private readonly Queue<string> _pendingOrder = new();

    /// <param name="services">The shared services.</param>
    /// <param name="lighting">The lighting rule the meshes' materials are drawn with (part of the shared-mesh key).</param>
    /// <param name="fadeTwins">Also build, for each mesh, its blended twin (<see cref="RenderResources.FadeTwin"/>, <see cref="FadeOf"/>), the mesh a part is drawn with while its translucency is strictly between 0 and 1 (the materialise fade, D45 and D63).</param>
    public ActorMeshLibrary(GameServices services, SurfaceLighting lighting = SurfaceLighting.Object, bool fadeTwins = false)
    {
        _services = services;
        _lighting = lighting;
        _fadeTwins = fadeTwins;
    }

    /// <summary>
    /// The blended twin of a mesh this library gave out (same geometry, every opaque or alpha-tested surface drawn blended without a depth write, the way retail draws a part with a
    /// translucency material), or null when the library was made without twins or does not know the mesh.
    /// </summary>
    public ArrayMesh? FadeOf(ArrayMesh? mesh)
    {
        if (mesh is null || !_fadeTwins)
        {
            return null;
        }

        if (_fades.TryGetValue(mesh.GetInstanceId(), out var fade) && GodotObject.IsInstanceValid(fade))
        {
            return fade;
        }

        // Not built yet (twins are built a few a frame, PumpFades): build this one now, which is cheap (a quarter of a millisecond), rather than draw a fading part solid.
        foreach (var (key, pending) in _pendingFades)
        {
            if (pending.Mesh.GetInstanceId() == mesh.GetInstanceId())
            {
                BuildFade(key, pending);
                return _fades.GetValueOrDefault(mesh.GetInstanceId());
            }
        }

        return null;
    }

    /// <summary>True while some mesh's blended twin is still to be built (<see cref="PumpFades"/>).</summary>
    public bool FadesPending => _pendingFades.Count > 0;

    /// <summary>
    /// Main thread: builds up to <paramref name="max"/> of the blended twins still to be built. A character's 34 parts are built in one frame at the arrival (<see cref="Get(uint, PartAppearance?)"/>),
    /// and their twins, which double that, are built over the frames that follow so the arrival's frame does not carry both. Returns how many are left.
    /// </summary>
    public int PumpFades(int max)
    {
        while (max > 0 && _pendingOrder.Count > 0)
        {
            var key = _pendingOrder.Dequeue();
            if (_pendingFades.TryGetValue(key, out var pending))
            {
                BuildFade(key, pending);
                max--;
            }
        }

        return _pendingFades.Count;
    }

    private void BuildFade(string key, PendingFade pending)
    {
        _pendingFades.Remove(key);
        var meshKey = key + RenderResources.FadeKeySuffix;
        _fades[pending.Mesh.GetInstanceId()] = _services.Render.AcquireMesh(meshKey, pending.Prepared, pending.Surfaces);
    }

    /// <summary>What a mesh needs to get its blended twin built later: its converted geometry and its surfaces as the twin draws them (carrying no decoded texture: the mesh uploaded it).</summary>
    private sealed record PendingFade(ArrayMesh Mesh, PreparedMesh Prepared, PreparedSurface[] Surfaces);

    /// <summary>The lighting rule this library's meshes are drawn with.</summary>
    public SurfaceLighting Lighting => _lighting;

    public int Count => _meshes.Count + _variants.Count;

    /// <summary>Main thread: the mesh of a GfxObj (built or shared on first use), or null when it has no geometry.</summary>
    public ArrayMesh? Get(uint gfxObjId)
    {
        if (_meshes.TryGetValue(gfxObjId, out var mesh))
        {
            return mesh;
        }

        mesh = Acquire(gfxObjId);
        _meshes.Add(gfxObjId, mesh);
        return mesh;
    }

    /// <summary>
    /// Main thread: the mesh of a GfxObj as an ObjDesc dresses it (<paramref name="appearance"/>: the part's texture swaps and its modified palette), built
    /// or shared on first use under the key <see cref="Key(uint, SurfaceLighting)"/> plus the part's <see cref="PartAppearance.VariantKey"/>; a part with no
    /// swap and no palette is the plain mesh of <see cref="Get(uint)"/>. A variant stays until <see cref="ReleaseVariantsExcept"/> or <see cref="Dispose"/>.
    /// </summary>
    public ArrayMesh? Get(uint gfxObjId, PartAppearance? appearance)
    {
        if (appearance is null || appearance.VariantKey.Length == 0)
        {
            return Get(gfxObjId);
        }

        var key = Key(gfxObjId, _lighting) + appearance.VariantKey;
        if (_variants.TryGetValue(key, out var mesh))
        {
            return mesh;
        }

        mesh = Acquire(gfxObjId, key, appearance);
        _variants.Add(key, mesh);
        return mesh;
    }

    /// <summary>The key a variant's mesh is held under (checks and <see cref="ReleaseVariantsExcept"/>).</summary>
    public string VariantKey(uint gfxObjId, PartAppearance appearance) =>
        appearance.VariantKey.Length == 0 ? Key(gfxObjId, _lighting) : Key(gfxObjId, _lighting) + appearance.VariantKey;

    /// <summary>Main thread: releases every variant mesh whose key is not in <paramref name="keep"/> (a view that dresses a character again drops the dresses it no longer shows).</summary>
    public void ReleaseVariantsExcept(IReadOnlySet<string> keep)
    {
        List<string>? drop = null;
        foreach (var (key, mesh) in _variants)
        {
            if (!keep.Contains(key))
            {
                (drop ??= []).Add(key);
                if (mesh is not null)
                {
                    ReleaseKey(key, mesh);
                }
            }
        }

        if (drop is not null)
        {
            foreach (var key in drop)
            {
                _variants.Remove(key);
            }
        }
    }

    private ArrayMesh? Acquire(uint gfxObjId, string? key = null, PartAppearance? appearance = null)
    {
        if (_services.GfxObjMeshes.Get(gfxObjId) is not { } gfxObj || gfxObj.Mesh.SubMeshes.Count == 0)
        {
            Log.Warn("spellviewer", "part GfxObj has no drawing geometry", ("gfxobj", $"0x{gfxObjId:X8}"));
            return null;
        }

        var prepared = MeshFactory.Prepare(gfxObj.Mesh);
        var surfaces = prepared.SubMeshes.Select(s => _services.Render.PrepareSurface(SurfaceRequest.For(s.Source) with
        {
            Lighting = _lighting,
            Palette = appearance?.Palette,
            SurfaceTextureOverride = appearance?.TextureOverrideFor(s.Source.SurfaceId),
        })).ToArray();
        try
        {
            var meshKey = key ?? Key(gfxObjId, _lighting);
            var mesh = _services.Render.AcquireMesh(meshKey, prepared, surfaces);
            if (_fadeTwins && !_fades.ContainsKey(mesh.GetInstanceId()) && !_pendingFades.ContainsKey(meshKey))
            {
                // The twin is built after the mesh (its materials share the textures the mesh's upload), a few a frame (PumpFades).
                _pendingFades[meshKey] = new PendingFade(mesh, prepared, surfaces.Select(RenderResources.FadeTwin).ToArray());
                _pendingOrder.Enqueue(meshKey);
            }

            return mesh;
        }
        finally
        {
            foreach (var surface in surfaces)
            {
                surface.Dispose();
            }
        }
    }

    private void ReleaseKey(string key, ArrayMesh mesh)
    {
        _services.Render.ReleaseMesh(key);
        _pendingFades.Remove(key);
        if (_fadeTwins && _fades.Remove(mesh.GetInstanceId()))
        {
            _services.Render.ReleaseMesh(key + RenderResources.FadeKeySuffix);
        }
    }

    /// <summary>The shared-mesh key: the statics' form for object lighting, with the rule's name appended for any other.</summary>
    public static string Key(uint gfxObjId, SurfaceLighting lighting = SurfaceLighting.Object) =>
        lighting == SurfaceLighting.Object
            ? $"gfxobj:0x{gfxObjId:X8}:{MeshDrawContext.Object}"
            : $"gfxobj:0x{gfxObjId:X8}:{MeshDrawContext.Object}:{lighting}";

    /// <summary>Main thread: releases every mesh reference.</summary>
    public void Dispose()
    {
        foreach (var (id, mesh) in _meshes)
        {
            if (mesh is not null)
            {
                ReleaseKey(Key(id, _lighting), mesh);
            }
        }

        _meshes.Clear();
        foreach (var (key, mesh) in _variants)
        {
            if (mesh is not null)
            {
                ReleaseKey(key, mesh);
            }
        }

        _variants.Clear();
        _fades.Clear();
        _pendingFades.Clear();
        _pendingOrder.Clear();
    }
}
