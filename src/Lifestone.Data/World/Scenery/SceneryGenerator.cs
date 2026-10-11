// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Common/Landblock.cs (get_land_scenes,
// init_buildings) and Source/ACE.Server/Physics/Common/ObjectDesc.cs (Displace, RotateObj, ObjAlign, ScaleObj,
// CheckSlope) (AGPL-3.0)
// with changes: re-checked line by line against retail CLandBlock::get_land_scenes / ObjectDesc::* (acclient 11.4186
// decompile) and the x87 code of the user's acclient.exe 11.6096; the ACE deviations listed in
// docs/third-party/scenery.md are fixed (2^-32 scale, double arithmetic, set_heading pitch axis, scale quotient,
// missing-object handling); landblock inputs come from ISceneryDataSource.

using System.Numerics;
using Lifestone.Data.Dat;
using Lifestone.Data.Terrain;

namespace Lifestone.Data.World.Scenery;

/// <summary>
/// Retail procedural scenery for a landblock (<c>CLandBlock::get_land_scenes</c>): for every terrain vertex, the
/// terrain type and scene slot pick a SceneType, a hash picks one Scene, and each ObjectDesc of that Scene is placed
/// when it passes the retail filters.
/// </summary>
/// <remarks>
/// <para>Filter chain, in retail order (each rejection is a <see cref="SceneryStage"/>):</para>
/// <list type="number">
/// <item>frequency noise &lt; Freq and WeenieObj == 0;</item>
/// <item>ObjectDesc::Place: BaseLoc + random displacement, rotated by a per-vertex quadrant; the position is
/// vertex * 24 + that offset and must lie in [0, 192) on X and Y;</item>
/// <item>not CLandBlock::on_road;</item>
/// <item>the land cell (floor(x/24), floor(y/24)) holds no building origin (CSortCell::has_building, buildings are
/// added by CLandBlock::init_buildings before scenery);</item>
/// <item>CLandCell::find_terrain_poly finds the triangle under the point, and MinSlope &lt;= N.z &lt;= MaxSlope;</item>
/// <item>z = Plane::set_height on that triangle (the BaseLoc z is discarded);</item>
/// <item>orientation: Align ? heading of -N (ObjAlign) : (MaxRotation &gt; 0 ? random heading : BaseLoc);</item>
/// <item>CPhysicsObj::makeObject succeeds and CPhysicsObj::obj_within_block accepts the unscaled object;</item>
/// <item>scale = MinScale * (MaxScale / MinScale)^noise (MaxScale when equal).</item>
/// </list>
/// <para>Thread safety: <see cref="Generate(LandblockCoord, bool)"/> may be called concurrently when the data source is thread-safe.</para>
/// </remarks>
public sealed class SceneryGenerator
{
    /// <summary><c>LandDefs::adjust_to_outside</c> snaps coordinates closer than this (float 0.0002) to zero.</summary>
    private const float AdjustToOutsideEpsilon = 0.0002f;

    private readonly ISceneryDataSource _data;

    public SceneryGenerator(ISceneryDataSource data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <summary>A generator over the user's dats, with a fresh <see cref="DatSceneryDataSource"/> cache.</summary>
    public static SceneryGenerator FromDats(DatSet dats) => new(new DatSceneryDataSource(dats));

    /// <summary>The data source (scene tables, Scenes, object bounds) this generator reads.</summary>
    public ISceneryDataSource Data => _data;

    /// <summary>Generates the scenery of <paramref name="coord"/>; empty when the landblock does not exist.</summary>
    /// <param name="coord">Landblock.</param>
    /// <param name="includeCandidates">Also return every evaluated ObjectDesc with the stage where it stopped.</param>
    public SceneryResult Generate(LandblockCoord coord, bool includeCandidates = false)
    {
        var landblock = _data.GetLandblock(coord);
        return landblock is null
            ? new SceneryResult(coord, [], new SceneryFunnel(), includeCandidates ? [] : null)
            : Generate(landblock, includeCandidates);
    }

    /// <summary>Generates the scenery of an explicit landblock (terrain + buildings).</summary>
    public SceneryResult Generate(SceneryLandblock landblock, bool includeCandidates = false)
    {
        ArgumentNullException.ThrowIfNull(landblock);
        var terrain = landblock.Terrain;
        var coord = terrain.Coord;
        var region = _data.Region;
        var sampler = new TerrainSampler(terrain, region.LandHeightTable);
        var buildingCells = BuildingCells(landblock);

        var placements = new List<ScenePlacement>();
        var funnel = new SceneryFunnel();
        var candidates = includeCandidates ? new List<SceneryCandidate>() : null;

        // CLandBlock::get_land_scenes: block_coord is the landblock's global vertex/cell offset (X * 8, Y * 8).
        var blockX = (uint)coord.GlobalCellX;
        var blockY = (uint)coord.GlobalCellY;

        for (var vx = 0; vx < TerrainConstants.VerticesPerSide; vx++)
        {
            for (var vy = 0; vy < TerrainConstants.VerticesPerSide; vy++)
            {
                var word = terrain.TerrainWords[TerrainConstants.VertexIndex(vx, vy)];
                var terrainType = (word >> 2) & 0x1F;
                var slot = word >> 11;
                if (slot >= region.NumSceneTypes(terrainType))
                {
                    continue;
                }

                var sceneCount = region.SceneCount(terrainType, slot);
                if (sceneCount == 0)
                {
                    continue;
                }

                funnel.VerticesWithScenes++;
                var gx = blockX + (uint)vx;
                var gy = blockY + (uint)vy;
                var sceneIndex = SceneryRandom.PickSceneIndex(gx, gy, sceneCount);
                var sceneId = region.ScenesOf(region.SceneTypeOf(terrainType, slot))[sceneIndex];
                var scene = sceneId == 0 ? null : _data.GetScene(sceneId);
                if (scene is null)
                {
                    continue;
                }

                for (var k = 0; k < scene.Objects.Count; k++)
                {
                    var obj = scene.Objects[k];
                    var (stage, placement, position) = Evaluate(obj, (uint)k, gx, gy, vx, vy, terrain, sampler, buildingCells);
                    funnel.Add(stage);
                    candidates?.Add(new SceneryCandidate(vx, vy, sceneId, k, obj.ObjectId, position, stage));
                    if (stage == SceneryStage.Placed)
                    {
                        placements.Add(placement with { SceneId = sceneId, ObjectIndex = k });
                    }
                }
            }
        }

        return new SceneryResult(coord, placements, funnel, candidates);
    }

    /// <summary>
    /// Land cells (index cellX * 8 + cellY) that hold a building, as retail <c>CLandBlock::init_buildings</c> assigns
    /// them: the cell of the building frame origin after <c>LandDefs::adjust_to_outside</c>; buildings whose origin
    /// falls outside this landblock, or whose model cannot be created, are dropped.
    /// </summary>
    public bool[] BuildingCells(SceneryLandblock landblock)
    {
        ArgumentNullException.ThrowIfNull(landblock);
        var cells = new bool[TerrainConstants.CellCount];
        foreach (var building in landblock.Buildings)
        {
            if (_data.GetObjectBounds(building.ModelId) is null)
            {
                continue; // CBuildingObj::makeBuilding failed
            }

            // adjust_to_outside snaps |x|, |y| < 0.0002 to 0 before taking floor(v / 24).
            var x = Math.Abs(building.Origin.X) < AdjustToOutsideEpsilon ? 0f : building.Origin.X;
            var y = Math.Abs(building.Origin.Y) < AdjustToOutsideEpsilon ? 0f : building.Origin.Y;
            var cx = TerrainSampler.CellIndexOf(x);
            var cy = TerrainSampler.CellIndexOf(y);
            if (cx >= 0 && cy >= 0)
            {
                cells[(cx * TerrainConstants.CellsPerSide) + cy] = true;
            }
        }

        return cells;
    }

    /// <summary>
    /// Retail <c>ObjectDesc::Place</c>: BaseLoc origin plus the random displacement, rotated by the vertex's quadrant.
    /// X is rounded to float before the rotation (it is spilled), Y stays double until stored; both end as float.
    /// </summary>
    public static Vector3 Place(in SceneObjectDesc obj, uint gx, uint gy, uint k)
    {
        var x = obj.DisplaceX <= 0f
            ? obj.BaseOrigin.X
            : (float)((SceneryRandom.ObjectNoise(gx, gy, k, SceneryRandom.DisplaceXSalt) * obj.DisplaceX) + obj.BaseOrigin.X);
        var y = obj.DisplaceY <= 0f
            ? obj.BaseOrigin.Y
            : (float)((SceneryRandom.ObjectNoise(gx, gy, k, SceneryRandom.DisplaceYSalt) * obj.DisplaceY) + obj.BaseOrigin.Y);
        var z = obj.BaseOrigin.Z;

        var quadrant = SceneryRandom.QuadrantNoise(gx, gy);
        if (quadrant >= 0.75)
        {
            return new Vector3(y, -x, z);
        }

        if (quadrant >= 0.5)
        {
            return new Vector3(-x, -y, z);
        }

        return quadrant >= 0.25 ? new Vector3(-y, x, z) : new Vector3(x, y, z);
    }

    /// <summary>
    /// Retail <c>ObjectDesc::ScaleObj</c>: <c>pow(max / min, noise) * min</c> in double (the quotient is not rounded to
    /// float, unlike ACE), returned to <c>SetScaleStatic</c> as float.
    /// </summary>
    public static float Scale(in SceneObjectDesc obj, uint gx, uint gy, uint k)
    {
        if (obj.MinScale == obj.MaxScale)
        {
            return obj.MaxScale;
        }

        var noise = SceneryRandom.ObjectNoise(gx, gy, k, SceneryRandom.ScaleSalt);
        return (float)(Math.Pow((double)obj.MaxScale / obj.MinScale, noise) * obj.MinScale);
    }

    /// <summary>Retail <c>ObjectDesc::CheckSlope</c>.</summary>
    public static bool CheckSlope(in SceneObjectDesc obj, float normalZ) => normalZ >= obj.MinSlope && normalZ <= obj.MaxSlope;

    /// <summary>
    /// Retail <c>ObjectDesc::GetObjFrame</c> / <c>ObjectDesc::ObjAlign</c>: the object frame at <paramref name="origin"/>.
    /// </summary>
    internal static RetailFrame ObjectFrame(in SceneObjectDesc obj, uint gx, uint gy, uint k, Vector3 origin, in TerrainPlane plane)
    {
        var frame = RetailFrame.FromStored(origin, obj.BaseOrientation);
        if (obj.Align != 0)
        {
            // ObjAlign: face the downhill direction, i.e. the heading of the negated plane normal.
            var down = new Vector3(-plane.Normal.X, -plane.Normal.Y, -plane.Normal.Z);
            frame.SetHeading((float)RetailFrame.GetHeading(down));
        }
        else if (obj.MaxRotation > 0f)
        {
            var degrees = (float)(SceneryRandom.ObjectNoise(gx, gy, k, SceneryRandom.RotationSalt) * obj.MaxRotation);
            frame.SetHeading(degrees);
        }

        return frame;
    }

    private (SceneryStage Stage, ScenePlacement Placement, Vector3 Position) Evaluate(
        in SceneObjectDesc obj, uint k, uint gx, uint gy, int vx, int vy, LandblockTerrain terrain, TerrainSampler sampler, bool[] buildingCells)
    {
        if (!(SceneryRandom.FrequencyNoise(gx, gy, k) < obj.Frequency))
        {
            return (SceneryStage.FrequencyRejected, default, default);
        }

        if (obj.WeenieObj != 0)
        {
            return (SceneryStage.Weenie, default, default);
        }

        var offset = Place(obj, gx, gy, k);

        // obj_vector.x = (double)iq * 24.0 + obj_vector.x, stored as float.
        var x = (float)((vx * (double)TerrainConstants.CellSize) + offset.X);
        var y = (float)((vy * (double)TerrainConstants.CellSize) + offset.Y);
        var position = new Vector3(x, y, offset.Z);
        if (!(x >= 0f && y >= 0f && x < TerrainConstants.LandblockSize && y < TerrainConstants.LandblockSize))
        {
            return (SceneryStage.OutOfBlock, default, position);
        }

        if (SceneryRoad.OnRoad(terrain, x, y))
        {
            return (SceneryStage.OnRoad, default, position);
        }

        var cellX = TerrainSampler.CellIndexOf(x);
        var cellY = TerrainSampler.CellIndexOf(y);
        if (buildingCells[(cellX * TerrainConstants.CellsPerSide) + cellY])
        {
            return (SceneryStage.Building, default, position);
        }

        var polygon = sampler.FindTerrainPolygon(cellX, cellY, x, y);
        if (polygon < 0)
        {
            return (SceneryStage.NoTerrainPolygon, default, position);
        }

        var plane = sampler.Planes[polygon];
        if (!CheckSlope(obj, plane.Normal.Z))
        {
            return (SceneryStage.Slope, default, position);
        }

        TerrainSampler.TrySetHeight(plane, x, y, position.Z, out var z);
        position = new Vector3(x, y, z);

        var frame = ObjectFrame(obj, gx, gy, k, position, plane);
        var bounds = _data.GetObjectBounds(obj.ObjectId);
        if (bounds is null)
        {
            return (SceneryStage.ObjectUnavailable, default, position);
        }

        if (!bounds.IsWithinBlock(frame))
        {
            return (SceneryStage.NotWithinBlock, default, position);
        }

        var placement = new ScenePlacement(
            obj.ObjectId,
            frame.Origin,
            frame.Orientation,
            Scale(obj, gx, gy, k),
            cellX,
            cellY,
            vx,
            vy,
            SceneId: 0,
            ObjectIndex: 0);
        return (SceneryStage.Placed, placement, position);
    }
}
