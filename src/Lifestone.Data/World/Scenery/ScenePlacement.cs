using System.Numerics;
using Lifestone.Data.Terrain;

namespace Lifestone.Data.World.Scenery;

/// <summary>One procedurally placed scenery object, in landblock-local AC coordinates.</summary>
/// <param name="ObjectId">GfxObj (0x01xxxxxx) or Setup (0x02xxxxxx) to draw.</param>
/// <param name="Origin">
/// Landblock-local position (X east, Y north, Z up; origin at the landblock's south-west corner). Z is the terrain
/// height under (X, Y): retail's <c>Plane::set_height</c> replaces the BaseLoc z.
/// </param>
/// <param name="Orientation">
/// Rotation of the object frame (Hamilton quaternion, same convention as AC's Frame). When neither Align nor
/// MaxRotation applies it is the BaseLoc quaternion exactly as stored, which retail accepts up to
/// |norm^2 - 1| &lt;= 0.001; normalise before building a basis.
/// </param>
/// <param name="Scale">Uniform scale (<c>CPhysicsObj::SetScaleStatic</c>).</param>
/// <param name="CellX">Land cell column 0..7 that contains <paramref name="Origin"/>.</param>
/// <param name="CellY">Land cell row 0..7 that contains <paramref name="Origin"/>.</param>
/// <param name="VertexX">Terrain vertex column 0..8 whose scene slot produced the object.</param>
/// <param name="VertexY">Terrain vertex row 0..8 whose scene slot produced the object.</param>
/// <param name="SceneId">Source Scene (0x12xxxxxx) record.</param>
/// <param name="ObjectIndex">Index of the source ObjectDesc inside the Scene.</param>
public readonly record struct ScenePlacement(
    uint ObjectId,
    Vector3 Origin,
    Quaternion Orientation,
    float Scale,
    int CellX,
    int CellY,
    int VertexX,
    int VertexY,
    uint SceneId,
    int ObjectIndex)
{
    /// <summary>Index of the land cell, <c>CellX * 8 + CellY</c>.</summary>
    public int CellIndex => (CellX * TerrainConstants.CellsPerSide) + CellY;

    /// <summary>The land cell id in <paramref name="landblock"/>: <c>0xXXYY0000 | (CellIndex + 1)</c>.</summary>
    public uint CellId(LandblockCoord landblock) => ((uint)landblock.X << 24) | ((uint)landblock.Y << 16) | (uint)(CellIndex + 1);

    /// <summary>True for a Setup (multi-part) object, false for a single GfxObj.</summary>
    public bool IsSetup => ObjectId >> 24 == 0x02;
}

/// <summary>Where in the retail filter chain a scenery candidate stopped (in evaluation order).</summary>
public enum SceneryStage
{
    /// <summary>Frequency noise &gt;= ObjectDesc.Freq: not a candidate.</summary>
    FrequencyRejected,

    /// <summary>ObjectDesc.WeenieObj != 0 (server-spawned): not a candidate.</summary>
    Weenie,

    /// <summary>Displaced position outside [0, 192) on X or Y.</summary>
    OutOfBlock,

    /// <summary>Within the road band of a road cell (CLandBlock::on_road).</summary>
    OnRoad,

    /// <summary>The land cell holds a building's origin (CSortCell::has_building).</summary>
    Building,

    /// <summary>No terrain triangle contains the point (never happens for in-block points).</summary>
    NoTerrainPolygon,

    /// <summary>Terrain normal z outside [MinSlope, MaxSlope] (ObjectDesc::CheckSlope).</summary>
    Slope,

    /// <summary>CPhysicsObj::makeObject would fail for the ObjectId (missing record or part).</summary>
    ObjectUnavailable,

    /// <summary>CPhysicsObj::obj_within_block rejected the placed object.</summary>
    NotWithinBlock,

    /// <summary>Placed.</summary>
    Placed,
}

/// <summary>One evaluated ObjectDesc at one vertex, with the stage where it stopped (for diagnostics and tests).</summary>
/// <param name="VertexX">Terrain vertex column 0..8.</param>
/// <param name="VertexY">Terrain vertex row 0..8.</param>
/// <param name="SceneId">Scene record.</param>
/// <param name="ObjectIndex">ObjectDesc index inside the Scene.</param>
/// <param name="ObjectId">ObjectDesc.ObjectId.</param>
/// <param name="Position">
/// Landblock-local position once known: displaced X/Y and BaseLoc Z up to the slope test, terrain Z afterwards;
/// zero for frequency/weenie rejections.
/// </param>
/// <param name="Stage">Where it stopped.</param>
public readonly record struct SceneryCandidate(
    int VertexX,
    int VertexY,
    uint SceneId,
    int ObjectIndex,
    uint ObjectId,
    Vector3 Position,
    SceneryStage Stage);

/// <summary>Counts of every stage of the retail filter chain for one landblock.</summary>
public sealed class SceneryFunnel
{
    private readonly int[] _counts = new int[Enum.GetValues<SceneryStage>().Length];

    /// <summary>Vertices whose terrain type and scene slot reach at least one scene.</summary>
    public int VerticesWithScenes { get; internal set; }

    /// <summary>ObjectDescs evaluated (all objects of every picked scene).</summary>
    public int ObjectsEvaluated => _counts.Sum();

    /// <summary>Candidates: passed the frequency test and are not weenies.</summary>
    public int Candidates => ObjectsEvaluated - this[SceneryStage.FrequencyRejected] - this[SceneryStage.Weenie];

    /// <summary>Candidates that survive the block-bounds and road tests (research's "before slope/building" count).</summary>
    public int AfterRoad => Candidates - this[SceneryStage.OutOfBlock] - this[SceneryStage.OnRoad];

    /// <summary>Placed objects.</summary>
    public int Placed => this[SceneryStage.Placed];

    /// <summary>Number of objects that stopped at <paramref name="stage"/>.</summary>
    public int this[SceneryStage stage] => _counts[(int)stage];

    internal void Add(SceneryStage stage) => _counts[(int)stage]++;

    public override string ToString() =>
        $"candidates {Candidates}, out of block {this[SceneryStage.OutOfBlock]}, on road {this[SceneryStage.OnRoad]}, " +
        $"after road {AfterRoad}, building {this[SceneryStage.Building]}, slope {this[SceneryStage.Slope]}, " +
        $"unavailable {this[SceneryStage.ObjectUnavailable]}, not within block {this[SceneryStage.NotWithinBlock]}, placed {Placed}";
}

/// <summary>Scenery of one landblock.</summary>
public sealed class SceneryResult(LandblockCoord coord, IReadOnlyList<ScenePlacement> placements, SceneryFunnel funnel, IReadOnlyList<SceneryCandidate>? candidates)
{
    public LandblockCoord Coord { get; } = coord;

    /// <summary>Placed objects in retail creation order (vertex x-major, then y, then ObjectDesc index).</summary>
    public IReadOnlyList<ScenePlacement> Placements { get; } = placements;

    public SceneryFunnel Funnel { get; } = funnel;

    /// <summary>Every evaluated ObjectDesc, when requested from <see cref="SceneryGenerator.Generate(LandblockCoord, bool)"/>.</summary>
    public IReadOnlyList<SceneryCandidate>? Candidates { get; } = candidates;
}
