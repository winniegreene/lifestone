using System.Collections;
using Godot;
using Lifestone.Data.Dat;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Scenery;

namespace Lifestone.Game.World;

/// <summary>
/// PLACEHOLDER terrain layer, only with <c>--placeholder-terrain</c>: one vertex-coloured <see cref="MeshInstance3D"/>
/// per landblock from <see cref="TerrainMeshBuilder"/>. The game draws the retail TexMerge terrain
/// (<see cref="Terrain.TerrainLandblockLayer"/>, docs/terrain-renderer.md) by default; this layer stays as a cheap
/// comparison and fallback for streaming measurements.
/// </summary>
/// <remarks>Also registers each landblock's exact <see cref="TerrainSampler"/> with <see cref="GroundHeights"/> (walk mode).</remarks>
public sealed class TerrainLayer : ILandblockLayer
{
    private readonly DatSet _dats;
    private readonly GroundHeights _ground;
    private readonly Dictionary<LandblockCoord, (MeshInstance3D Instance, ArrayMesh Mesh)> _instances = new();
    private readonly Material _material;

    /// <summary>Creates the layer (main thread, before streaming: the shared material's shader compiles here, not in an attach step).</summary>
    public TerrainLayer(DatSet dats, GroundHeights ground)
    {
        _dats = dats;
        _ground = ground;
        _material = TerrainMeshFactory.CreateMaterial();

        // The first build pays ~14 ms of one-time JIT and interop set-up (measured); pay it here, not in an attach step.
        TerrainMeshFactory.Build(
            new PreparedTerrainMesh([Vector3.Zero, Vector3.Right, Vector3.Back], [Vector3.Up, Vector3.Up, Vector3.Up], [Colors.White, Colors.White, Colors.White], [0, 1, 2], new Aabb(Vector3.Zero, Vector3.One)),
            _material).Dispose();
    }

    public string Name => "terrain (placeholder)";

    /// <summary>Attached landblocks.</summary>
    public int Count => _instances.Count;

    public Task<object?> PrepareAsync(LandblockCoord coord, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (_dats.GetLandblockTerrain(coord) is not { } terrain)
        {
            return Task.FromResult<object?>(null);
        }

        var region = _dats.TerrainRegion;
        var mesh = TerrainMeshBuilder.Build(terrain, region);
        cancellation.ThrowIfCancellationRequested();
        return Task.FromResult<object?>(new Prepared(
            TerrainMeshFactory.Prepare(mesh, region),
            new TerrainSampler(terrain, region.LandHeightTable)));
    }

    public IEnumerable? Attach(LandblockCoord coord, object prepared, Node3D landblockRoot)
    {
        var (mesh, sampler) = (Prepared)prepared;
        _ground.Register(sampler);
        var arrayMesh = TerrainMeshFactory.Build(mesh, _material);
        var instance = new MeshInstance3D
        {
            Name = "Terrain",
            Mesh = arrayMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        landblockRoot.AddChild(instance);
        _instances[coord] = (instance, arrayMesh);
        return null; // one step: a single mesh upload (~0.2 ms)
    }

    public void Detach(LandblockCoord coord)
    {
        if (_instances.Remove(coord, out var attached))
        {
            // Free the vertex buffers now rather than when the GC finalises the C# wrapper (the root is hidden already).
            if (GodotObject.IsInstanceValid(attached.Instance))
            {
                attached.Instance.Mesh = null;
            }

            attached.Mesh.Dispose();
        }

        _ground.Unregister(coord);
    }

    private sealed record Prepared(PreparedTerrainMesh Mesh, TerrainSampler Sampler);
}
