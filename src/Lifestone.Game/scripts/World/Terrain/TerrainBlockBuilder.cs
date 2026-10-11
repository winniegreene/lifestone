using System.Diagnostics;
using Lifestone.Data.Dat;
using Lifestone.Data.Terrain;
using Lifestone.Data.Terrain.Blending;

namespace Lifestone.Game.World.Terrain;

/// <summary>
/// The worker-thread half of the terrain: reads a landblock and builds its <see cref="PreparedTerrainBlock"/> (retail
/// split, LandHeightTable heights, <c>CLandBlockStruct::calc_lighting</c> normals, packed TexMerge recipes, Godot axes).
/// It needs only the dats and the atlas <em>manifest</em>, not the decoded atlas, so streaming can prepare landblocks
/// while <see cref="TerrainRenderer.PrepareAtlas"/> is still decoding. Thread-safe.
/// </summary>
public sealed class TerrainBlockBuilder
{
    public TerrainBlockBuilder(DatSet dats, TerrainNormalMode normalMode = TerrainRenderer.DefaultNormalMode)
    {
        ArgumentNullException.ThrowIfNull(dats);
        Dats = dats;
        NormalMode = normalMode;
        TexMerge = new TerrainTexMerge(dats.TerrainAtlas);
    }

    public DatSet Dats { get; }

    /// <summary>Seamless normals (default) or retail's per-block ones (<c>--retail-normals</c>; docs/terrain-renderer.md §8).</summary>
    public TerrainNormalMode NormalMode { get; }

    /// <summary>Shared palette code → recipe cache over <see cref="DatSet.TerrainAtlas"/>.</summary>
    public TerrainTexMerge TexMerge { get; }

    /// <summary>Worker thread: everything a landblock needs before <see cref="TerrainRenderer.AttachBlock"/>; null when it does not exist.</summary>
    public PreparedTerrainBlock? Prepare(LandblockCoord coord, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        if (Dats.GetLandblockTerrain(coord) is not { } terrain)
        {
            return null;
        }

        var region = Dats.TerrainRegion;
        var normals = NormalMode == TerrainNormalMode.SeamlessAcrossBlocks
            ? TerrainSeamlessNormals.Compute(terrain, Dats.GetLandblockTerrain, region)
            : null;
        cancellation.ThrowIfCancellationRequested();
        var mesh = TerrainRenderMesh.Build(terrain, region, TexMerge, normals);
        return TerrainBlockMesh.Prepare(terrain, mesh, timer.Elapsed.TotalMilliseconds);
    }
}
