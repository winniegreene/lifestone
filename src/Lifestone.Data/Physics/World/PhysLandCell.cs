// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Common/LandCell.cs (FindCollisions,
// FindEnvCollisions, find_terrain_poly, point_in_cell, find_transit_cells) and SortCell.cs (AGPL-3.0)
// with changes: the two terrain polygons come from the scenery track's retail-exact TerrainSampler (its point-in-triangle
// test and planes), not ACE's LandblockStruct; checked against the retail functions cited below.

using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.Transitions;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Scenery;

namespace Lifestone.Data.Physics.World;

/// <summary>
/// An outdoor land cell (retail <c>CLandCell</c> with its <c>CSortCell</c> base): its two terrain triangles, its water,
/// at most one building, and the static objects that reach into it.
/// </summary>
public sealed class PhysLandCell : PhysCell
{
    private readonly TerrainSampler _terrain;
    private readonly int _firstPolygon;
    private readonly PhysPolygon[] _polygons;

    internal PhysLandCell(uint id, TerrainSampler terrain, int cellX, int cellY, WaterType waterType)
        : base(id, new CellFrame(id, new AcFrame(CenterOf(cellX, cellY), Quaternion.Identity)))
    {
        _terrain = terrain;
        CellX = cellX;
        CellY = cellY;
        WaterType = waterType;
        _firstPolygon = 2 * ((cellX * TerrainConstants.CellsPerSide) + cellY);
        _polygons = new PhysPolygon[2];
        Span<Vector3> corners = stackalloc Vector3[3];
        for (var t = 0; t < 2; t++)
        {
            var polygon = _firstPolygon + t;
            for (var k = 0; k < 3; k++)
            {
                corners[k] = terrain.Vertices[terrain.PolygonVertexIndices[(polygon * 3) + k]];
            }

            _polygons[t] = new PhysPolygon(polygon, corners);
        }
    }

    /// <summary>The cell's column (0-7, east) in its landblock.</summary>
    public int CellX { get; }

    /// <summary>The cell's row (0-7, north) in its landblock.</summary>
    public int CellY { get; }

    /// <inheritdoc/>
    public override WaterType WaterType { get; }

    /// <summary>The cell's two terrain triangles (retail <c>CLandCell::polygons</c>), in landblock space.</summary>
    public IReadOnlyList<PhysPolygon> Polygons => _polygons;

    /// <summary>The building whose origin is in this cell (retail <c>CSortCell::building</c>: the first one), or null.</summary>
    public PhysBuilding? Building { get; internal set; }

    /// <summary>
    /// Retail <c>CLandCell::find_terrain_poly</c> (11.4186 0x00532CF0, 11.6096 0x00533A30): the first of the two
    /// triangles whose footprint holds <paramref name="point"/> (landblock-local; retail <c>point_in_poly2D</c>, as the
    /// scenery track's <see cref="TerrainSampler"/> evaluates it), or null.
    /// </summary>
    public PhysPolygon? FindTerrainPoly(Vector3 point)
    {
        for (var t = 0; t < 2; t++)
        {
            if (_terrain.PointInPolygon2D(_firstPolygon + t, point.X, point.Y))
            {
                return _polygons[t];
            }
        }

        return null;
    }

    /// <summary>Retail <c>CLandCell::point_in_cell</c> (11.4186 0x00532D40, 11.6096 0x00533A80): over one of the two triangles.</summary>
    public override bool PointInCell(Vector3 point) => FindTerrainPoly(point) is not null;

    /// <summary>
    /// Retail <c>CLandCell::find_collisions</c> (11.4186 0x00532D60, 11.6096 0x00533AA0): the terrain, then the building
    /// (<c>CSortCell::find_collisions</c>, 0x005340A0, 0x00534DE0), then the static objects.
    /// </summary>
    internal override TransitionState FindCollisions(Transition transition)
    {
        var state = FindEnvCollisions(transition);
        if (state != TransitionState.OK)
        {
            return state;
        }

        if (Building is not null)
        {
            state = Building.FindBuildingCollisions(transition);
            if (state != TransitionState.OK)
            {
                return state;
            }
        }

        return FindObjCollisions(transition);
    }

    /// <summary>
    /// Retail <c>CLandCell::find_env_collisions</c> (11.4186 0x00532F20, 11.6096 0x00533C60): the terrain triangle under the
    /// path's low point (moved into this cell's landblock). A landblock entirely under water stops everything but the
    /// camera and missiles; otherwise the sphere is checked against the triangle's plane
    /// (<see cref="ObjectInfo.ValidateWalkable"/>), sinking by the water depth there.
    /// </summary>
    internal override TransitionState FindEnvCollisions(Transition transition)
    {
        var state = CheckEntryRestrictions(transition);
        if (state != TransitionState.OK)
        {
            return state;
        }

        var path = transition.SpherePath;
        var blockOffset = LandDefs.GetBlockOffset(path.CheckPos.CellId, Id);
        var localPoint = path.GlobalLowPoint - blockOffset;
        var walkable = FindTerrainPoly(localPoint);
        if (walkable is null)
        {
            return state;
        }

        var info = transition.ObjectInfo;
        if (BlockWaterType == WaterType.EntirelyWater
            && (info.State & ObjectInfoState.IsViewer) == 0
            && (info.ObjectState & PhysicsState.Missile) == 0)
        {
            return TransitionState.Collided;
        }

        var waterDepth = GetWaterDepth(localPoint);
        ref readonly var sphere = ref path.GlobalSphere[0];
        var checkPos = new PhysSphere(sphere.Center - blockOffset, sphere.Radius);
        return info.ValidateWalkable(checkPos, walkable.Plane, WaterType != WaterType.NotWater, waterDepth, transition, Id);
    }

    /// <summary>
    /// Retail <c>CLandCell::find_transit_cells(p, num_sphere, sphere, cell_array, path)</c> (11.4186 0x00533800, 11.6096
    /// 0x00534540): the outdoor cells around the spheres, then the EnvCells behind the building's doorways
    /// (<c>CSortCell::find_transit_cells</c>, 0x00534060, 0x00534DA0).
    /// </summary>
    internal override void FindTransitCells(IPhysicsCellSource cells, in CellFrame position, ReadOnlySpan<PhysSphere> spheres, CellArray cellArray, SpherePath? path)
    {
        CellLists.AddAllOutsideCells(cells, position, spheres, cellArray);
        Building?.FindBuildingTransitCells(cells, position, spheres, cellArray, path);
    }

    /// <summary>
    /// Retail <c>CLandCell::find_transit_cells(num_parts, parts, cell_array)</c> (11.4186 0x00533840, 11.6096 0x00534580):
    /// the same for a static object's parts.
    /// </summary>
    internal override void FindTransitCells(IPhysicsCellSource cells, ReadOnlySpan<PhysPart> parts, CellArray cellArray)
    {
        CellLists.AddAllOutsideCells(cells, parts, cellArray);
        Building?.FindBuildingTransitCells(cells, parts, cellArray);
    }

    /// <summary>
    /// The cell's centre at height 0 (retail <c>CLandBlockStruct::ConstructPolygons</c>, 11.4186 0x00531D10, 11.6096
    /// 0x00532A50, sets each land cell's position to <c>((2i + 1) * 12, (2j + 1) * 12, 0)</c>).
    /// </summary>
    private static Vector3 CenterOf(int cellX, int cellY) =>
        new((float)((2 * cellX) + 1) * 12.0f, (float)((2 * cellY) + 1) * 12.0f, 0f);
}
