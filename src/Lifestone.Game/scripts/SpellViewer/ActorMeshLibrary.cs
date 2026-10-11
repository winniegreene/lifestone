using Godot;
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
public sealed class ActorMeshLibrary : IDisposable
{
    private readonly GameServices _services;
    private readonly Dictionary<uint, ArrayMesh?> _meshes = new();

    public ActorMeshLibrary(GameServices services) => _services = services;

    public int Count => _meshes.Count;

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

    private ArrayMesh? Acquire(uint gfxObjId)
    {
        if (_services.GfxObjMeshes.Get(gfxObjId) is not { } gfxObj || gfxObj.Mesh.SubMeshes.Count == 0)
        {
            Log.Warn("spellviewer", "part GfxObj has no drawing geometry", ("gfxobj", $"0x{gfxObjId:X8}"));
            return null;
        }

        var prepared = MeshFactory.Prepare(gfxObj.Mesh);
        var surfaces = prepared.SubMeshes.Select(s => _services.Render.PrepareSurface(SurfaceRequest.For(s.Source))).ToArray();
        try
        {
            return _services.Render.AcquireMesh(Key(gfxObjId), prepared, surfaces);
        }
        finally
        {
            foreach (var surface in surfaces)
            {
                surface.Dispose();
            }
        }
    }

    private static string Key(uint gfxObjId) => $"gfxobj:0x{gfxObjId:X8}:{MeshDrawContext.Object}";

    /// <summary>Main thread: releases every mesh reference.</summary>
    public void Dispose()
    {
        foreach (var (id, mesh) in _meshes)
        {
            if (mesh is not null)
            {
                _services.Render.ReleaseMesh(Key(id));
            }
        }

        _meshes.Clear();
    }
}
