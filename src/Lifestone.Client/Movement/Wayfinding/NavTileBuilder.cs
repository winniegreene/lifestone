using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Lifestone.Client.Movement.Routes;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>
/// The constants of a <see cref="NavTile"/> build (P5a, docs/wayfinding.md). Not retail: every number is Lifestone's own
/// (the body radius is the human Setup's sphere radius, the step-up the human Setup's, the slope numbers
/// <c>PhysicsGlobals.FloorZ</c> and a ramp to 0.85).
/// </summary>
public sealed record NavBuildOptions
{
    /// <summary>The defaults the streamer uses.</summary>
    public static NavBuildOptions Default { get; } = new();

    /// <summary>A standing human's radius in metres (the human Setup's sphere radius). Lifestone's.</summary>
    public float BodyRadius { get; init; } = 0.48f;

    /// <summary>The extra keep-off distance added to <see cref="BodyRadius"/> round every static and building. Lifestone's.</summary>
    public float Margin { get; init; } = 0.1f;

    /// <summary>The step-up in metres (the human Setup's). The builder uses it to call a low, small static tiny and a low flat box a step (<see cref="NavBuildStats.StaticNodesStepped"/>); the planner reads the edge rule (a rise over this blocks the edge, a drop to 2 m is costed) from the tile's z. Lifestone's.</summary>
    public float StepUp { get; init; } = NavMap.StepUpMetres;

    /// <summary>The normal z at which the slope ramp ends (no cost above it); the ramp runs from <c>PhysicsGlobals.FloorZ</c> to here. Lifestone's.</summary>
    public float SlopeRampEnd { get; init; } = 0.85f;

    /// <summary>
    /// How near a too-steep land triangle a node costs the top of the slope ramp, in metres from the node's centre. To a body a too-steep triangle is a wall, and a route allowed anywhere in the node beside it brings the foot sphere against it
    /// (the world walk: 79% of the long walks' recovery moves, on about 2% of the walkable ground). The body's radius and half a node's diagonal, rounded up. A cost and not a block, so no passage closes. Lifestone's.
    /// </summary>
    public float SteepLandKeepOff { get; init; } = 1f;

    /// <summary>The largest vertical part of a building portal's normal a lane is cut for (<see cref="RoomGraph.BuildingDoorwayLean"/>; the upright rule's 0.1 before M6 5 w). Lifestone's.</summary>
    public float DoorwayLean { get; init; } = RoomGraph.BuildingDoorwayLean;

    /// <summary>
    /// How far under <see cref="CellRoutePlanner.HumanHeight"/> the lane cutter probes a door's lintel: the room graph's own (<see cref="RoomGraph.DefaultHeadSlackMetres"/>), so that a door the graph
    /// lists as a way out gets its lane (the world walk: a door 2.5 m wide and 1.79 m tall over the dats, walked both ways by the body, had none). Lifestone's.
    /// </summary>
    public float HeadSlack { get; init; } = RoomGraph.DefaultHeadSlackMetres;

    /// <summary>A doorway lane's width in metres. Lifestone's.</summary>
    public float LaneWidth { get; init; } = 1.5f;

    /// <summary>A doorway lane's length in metres, from the sill out. Lifestone's.</summary>
    public float LaneLength { get; init; } = 2.5f;

    /// <summary>How far from the sill a straight lane may go on over its building's own shell to reach the land (<c>NavTileBuilder.ExtendOverShell</c>), in metres. Lifestone's.</summary>
    public float LaneOverShellMetres { get; init; } = 10f;

    /// <summary>
    /// How far out from a doorway's sill the lane cutter follows the building's floors (a porch, its stairs) down to the ground before it refuses the lane as above the ground
    /// (M6 4 w6a: a balcony door over a street). Lifestone's: a 2 m porch's stairs run about 4 m, and twice that is room to spare.
    /// </summary>
    public float LaneClimbLength { get; init; } = 8f;

    /// <summary>
    /// The longest walk from a raised door's sill down its stairs when they turn aside from the lane's line (<see cref="LaneStairs"/>, M6 7 A), in metres. Lifestone's: the Refuge's huts' walk is about 13 m, their decks 18 x 24 m, the longest found over the dats 23.5 m.
    /// </summary>
    public float LaneStairReach { get; init; } = 24f;

    /// <summary>A static whose top is not more than this above the ground at a node does not block it (a decal, a flat paving stone). Lifestone's.</summary>
    public float DecalHeight { get; init; } = 0.05f;

    /// <summary>A static whose underside is at least this above the ground at a node does not block it (an arch, a bridge): a standing human is 1.8 m, plus a 0.2 m margin. Lifestone's.</summary>
    public float OverheadClearance { get; init; } = 2.0f;

    /// <summary>A static is tiny, and skipped (counted), when its footprint is under this in both directions and it is lower than <see cref="StepUp"/>. Lifestone's.</summary>
    public float TinySize { get; init; } = 0.3f;

    /// <summary>
    /// The landblocks round this one, whose <c>ForeignShadows</c> that fall into this landblock's cells are stamped as
    /// "shadow" obstacles (a static of a neighbour whose footprint reaches over the border). Null: none, and only this
    /// landblock's own statics are stamped; the streamer passes the attached neighbours.
    /// </summary>
    public IReadOnlyList<LandblockCollision>? Neighbours { get; init; }
}

/// <summary>What one build did, for the log line: every skip is counted, never silent.</summary>
public sealed class NavBuildStats
{
    /// <summary>Statics stamped (at least one node blocked).</summary>
    public int StaticsStamped { get; set; }

    /// <summary>Foreign shadows (neighbours' statics reaching in) stamped.</summary>
    public int ShadowsStamped { get; set; }

    /// <summary>
    /// Neighbours' statics near the border that cast no shadow into this landblock's cells but whose solid, widened by the body, reaches over the border
    /// (the world walk: a static of 0x521A's reaching 0.07 m into 0x5219 stopped the body there while 0x5219's nodes were open).
    /// </summary>
    public int ReachStamped { get; set; }

    /// <summary>Statics left to the room graph: their cell is an EnvCell, not an outdoor cell.</summary>
    public int StaticsIndoor { get; set; }

    /// <summary>Statics wholly below the ground at their origin (or within <see cref="NavBuildOptions.DecalHeight"/> of it).</summary>
    public int StaticsBelowGround { get; set; }

    /// <summary>Statics under <see cref="NavBuildOptions.TinySize"/> and the step-up: stepped over, not stamped.</summary>
    public int StaticsTiny { get; set; }

    /// <summary>Statics that stamped no node (overhead, or between nodes after the checks).</summary>
    public int StaticsNoNodes { get; set; }

    /// <summary>
    /// Nodes a static's flat top (<c>Prim.FlatTop</c>) would have blocked but whose top stands within <see cref="NavBuildOptions.StepUp"/> of the ground there: a step the body walks up onto (M6 4 f3: the
    /// dais 0x020019E3 under Holtburg's Portal to Town Network, 0.27 m high), left open.
    /// </summary>
    public int StaticNodesStepped { get; set; }

    /// <summary>Buildings blocked.</summary>
    public int BuildingsStamped { get; set; }

    /// <summary>Buildings whose shell has no collision polygons: blocked by their bounding box instead of their footprint.</summary>
    public int BuildingsByBounds { get; set; }

    /// <summary>Nodes under a building's footprint opened again because the body stands there clear of the shell's physics (a passage through a gatehouse).</summary>
    public int BuildingNodesOpen { get; set; }

    /// <summary>Doorway lanes built.</summary>
    public int LanesBuilt { get; set; }

    /// <summary>Of <see cref="LanesBuilt"/>, the lanes whose stairs turn aside from the lane's line (<see cref="LaneStairs"/>, M6 7 A).</summary>
    public int LanesByStairs { get; set; }

    /// <summary>Of <see cref="LanesBuilt"/>, the straight lanes that go on over their building's shell to reach the land (a porch the shell covers).</summary>
    public int LanesOverShell { get; set; }

    /// <summary>Of <see cref="LanesByStairs"/>, the lanes whose steps run straight along the lane's line but meet the ground past <see cref="NavBuildOptions.LaneLength"/> (the world walk, W8).</summary>
    public int LanesRaisedLine { get; set; }

    /// <summary>Building portals skipped: a standing human cannot pass the entry cell's outside portal polygon.</summary>
    public int PortalsImpassable { get; set; }

    /// <summary>Building portals skipped: the entry cell, its outside portal or the polygon is missing.</summary>
    public int PortalsNoGeometry { get; set; }

    /// <summary>
    /// Building portals skipped: no floor steps down from the sill to the ground along the lane's line (M6 4 w6a), and none leads there by a turn (<see cref="LaneStairs"/>, M6 7 A): a balcony door over a street.
    /// </summary>
    public int PortalsAboveGround { get; set; }

    /// <summary>Building portals skipped: the lane lies wholly off this tile.</summary>
    public int PortalsOffTile { get; set; }

    /// <summary>Every building portal skipped, by its entry cell and why (<c>impassable</c>, <c>tilted</c>, <c>above-ground</c>, <c>no-geometry</c>, <c>off-tile</c>): what the counters count, door by door.</summary>
    public List<(uint Cell, string Why)> PortalSkips { get; } = [];

    /// <summary>Nodes with no terrain polygon (blocked): the whole tile when the landblock has no LandBlock record.</summary>
    public int NodesWithoutTerrain { get; set; }

    /// <summary>Open nodes within <see cref="NavBuildOptions.SteepLandKeepOff"/> of a too-steep land triangle, costed as the top of the slope ramp.</summary>
    public int NodesBesideSteepLand { get; set; }

    /// <summary>Open nodes whose square overlaps a too-steep land triangle (<see cref="NavFlags.SteepEdge"/>).</summary>
    public int NodesOnSteepEdge { get; set; }

    /// <summary>Stamps that found the obstacle list full (65,534 entries): their nodes have no owner.</summary>
    public int ObstacleOverflow { get; set; }

    /// <summary>The build's wall-clock time (also <see cref="NavTile.BuildMilliseconds"/>).</summary>
    public double Milliseconds { get; set; }

    /// <summary>The build's log line.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"navtile ms={Milliseconds:F1} statics={StaticsStamped} shadows={ShadowsStamped} reach_over={ReachStamped} buildings={BuildingsStamped}(by_bounds={BuildingsByBounds} open_nodes={BuildingNodesOpen}) lanes={LanesBuilt}(by_stairs={LanesByStairs} raised_line={LanesRaisedLine}) "
        + $"skipped(indoor={StaticsIndoor} below_ground={StaticsBelowGround} tiny={StaticsTiny} no_nodes={StaticsNoNodes}) stepped_nodes={StaticNodesStepped} "
        + $"portals_skipped(impassable={PortalsImpassable} above_ground={PortalsAboveGround} no_geometry={PortalsNoGeometry} off_tile={PortalsOffTile}) no_terrain={NodesWithoutTerrain} beside_steep={NodesBesideSteepLand} steep_edge={NodesOnSteepEdge} owner_overflow={ObstacleOverflow}");
}

/// <summary>
/// Builds a <see cref="NavTile"/> from a landblock's immutable collision (P5a, design 3.1). Pure and thread-safe: no shared
/// state, so it runs on a <c>WorkerPool</c> worker when the streamer attaches the landblock. Not retail: the tile is a
/// cheap, conservative proxy and every route over it is confirmed by the physics (the wayfinder's rule: the map proposes,
/// the physics confirms).
/// </summary>
/// <remarks>
/// <para><b>Ground.</b> Each node's z and slope come from the terrain polygon under its centre
/// (<c>CLandCell::find_terrain_poly</c>, 11.6096 0x00533A30, as <c>RouteLibrary.TryStand</c> reads it): z from the plane,
/// the slope from the plane's normal z. Under <c>PhysicsGlobals.FloorZ</c> the node is <see cref="NavFlags.Blocked"/>
/// (<see cref="NavTile.SlopeCost"/> 255); from there up to <see cref="NavBuildOptions.SlopeRampEnd"/> it is
/// <see cref="NavFlags.Slope"/> with a cost ramping 255 to 0. Water is the land cell's (24 m): a cell with any water
/// corner flags all its nodes <see cref="NavFlags.Water"/>, which the planner costs and never blocks (the island's wades of up
/// to 1,083 m to the Holding were all confirmed, M6 4 w3, so the per-cell flag stands). A landblock entirely under water is
/// a wall, and so is the body's reach beside it in a neighbour (<c>StampSea</c>, retail <c>CLandCell::find_env_collisions</c>,
/// 11.6096 0x00533C60). Open nodes near a too-steep land triangle, a neighbour's included, cost the top of the slope ramp (<see cref="NavBuildOptions.SteepLandKeepOff"/>).</para>
/// <para><b>Statics.</b> Retail tests a static's physics-BSP parts when it has any, else its cylinder-spheres, else its
/// spheres (<c>CPhysicsObj::FindObjCollisions</c>, 11.6096 0x0050FB20); the same order picks what is stamped. A part is its
/// bounding box (scaled, in its frame: an oriented rectangle when the part is only turned about the vertical), a
/// cylinder-sphere or sphere a circle; each is inflated by body radius + margin (0.58 m) with rounded corners. A tilted
/// physics-BSP part (its world box can be far larger than the part: the island's rock arches) is asked node by node
/// instead: a stack of body spheres over the walking band, placed against the part's own BSP as retail places a body
/// (<c>Prim.Hits</c>, <c>PhysPart.PlacementCollides</c>). <b>A static's top is walkable only as a step</b>: a flat top (a cylinder-sphere's, or a box turned about the vertical only)
/// within the step-up of the ground at a node does not block it, as the body walks up onto it (retail <c>CCylSphere::step_sphere_up</c>, 11.6096 0x0053C070; M6 4 f3: the
/// dais 0x020019E3 under Holtburg's Portal to Town Network, a cylinder-sphere 2.6 m wide and 0.26 m high, was stamped a wall, so no walk could reach the portal); any other
/// static (a sphere, a tilted part, a top over the step-up) still blocks, a named conservatism, and the physics confirmation decides. A node is not
/// blocked by a static whose top is within <see cref="NavBuildOptions.DecalHeight"/> of the ground there (a decal) or
/// whose underside is <see cref="NavBuildOptions.OverheadClearance"/> or more above it (an arch). A static in an EnvCell
/// (a room's furniture) belongs to the room graph and is counted skipped; one wholly below the ground, or tiny, is
/// counted skipped too.</para>
/// <para><b>Buildings.</b> A building's footprint is blocked ("building"): the union of its shell's collision polygons
/// projected on the ground, inflated like a static (its bounding box, <c>PhysBuilding.Part.Shape.Bounds</c>, only when the
/// shell has no collision polygons, counted): the box over-blocks every terraced or L-shaped building so far that a
/// door ends up an island inside it. A node the projection blocked, and nothing before it, is opened again where the body stands
/// clear of the shell's own physics (the body's spheres over the walking band placed against it, as for a tilted static) and
/// outside the building's rooms: a roofed passage through a gatehouse (M6 4 w6a, Fort Tethana's gate). Then each doorway lane
/// is cut: the building's portal names the entry EnvCell and its outside portal polygon; if a standing human can pass
/// the polygon (the four probes <c>CellRoutePlanner.IsWalkable</c> uses), and the sill steps down to the ground along the
/// lane's line over the shell's floors (<c>LaneClimbs</c>, M6 4 w6a: a balcony door over a street gets none; stairs that turn aside are <see cref="LaneStairs"/>', M6 7 A), a strip <see cref="NavBuildOptions.LaneWidth"/>
/// wide runs from the sill <see cref="NavBuildOptions.LaneLength"/> out along the portal's outward normal; its nodes are
/// unblocked (except where the ground itself is too steep) and flagged <see cref="NavFlags.DoorwayLane"/>.</para>
/// <para><b>Clearance.</b> <see cref="ComputeClearance"/>.</para>
/// </remarks>
public static class NavTileBuilder
{
    // A doorway lane is sampled every 0.25 m across and along (finer than a node, so no node of the strip is missed).
    private const float LaneSample = 0.25f;
    private const ushort OwnerLimit = NavTile.NoOwner;

    // The chamfer's integer weights: 5 for a step along an axis, 7 for a diagonal step (7/5 = 1.4, sqrt2 = 1.414: the
    // classic 5-7 chamfer, a 1% error); a distance in nodes is the sum / 5, rounded.
    private const int ChamferAxis = 5;
    private const int ChamferDiagonal = 7;

    /// <summary>Builds the tile of <paramref name="block"/>.</summary>
    /// <param name="block">The landblock's collision set.</param>
    /// <param name="options">The constants; null for <see cref="NavBuildOptions.Default"/>.</param>
    public static NavTile Build(LandblockCollision block, NavBuildOptions? options = null) => Build(block, options, out _);

    /// <summary>Builds the tile and reports what the build did (its log line is <c>stats.ToString()</c>).</summary>
    public static NavTile Build(LandblockCollision block, NavBuildOptions? options, out NavBuildStats stats)
    {
        ArgumentNullException.ThrowIfNull(block);
        var start = Stopwatch.GetTimestamp();
        var o = options ?? NavBuildOptions.Default;
        stats = new NavBuildStats();
        var tile = new NavTile(((uint)block.Coord.X << 24) | ((uint)block.Coord.Y << 16));

        StampGround(tile, block, o, stats);
        StampBesideSteepLand(tile, block, Vector2.Zero, o, stats);
        if (o.Neighbours is { } around)
        {
            foreach (var other in around)
            {
                int dx = other.Coord.X - block.Coord.X, dy = other.Coord.Y - block.Coord.Y;
                if ((dx != 0 || dy != 0) && Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1)
                {
                    StampBesideSteepLand(tile, other, new Vector2(dx * (float)LandDefs.BlockLength, dy * (float)LandDefs.BlockLength), o, stats);
                }
            }
        }

        var prims = new List<Prim>(8);
        var infl = o.BodyRadius + o.Margin;
        var shadowed = new HashSet<PhysStaticObject>(ReferenceEqualityComparer.Instance);

        foreach (var obj in block.StaticObjects)
        {
            StampStatic(tile, obj, Vector3.Zero, "static", NavFlags.Static, infl, o, prims, stats);
        }

        if (o.Neighbours is { } neighbours)
        {
            foreach (var other in neighbours)
            {
                if (other.Coord == block.Coord)
                {
                    continue;
                }

                var offset = new Vector3((other.Coord.X - block.Coord.X) * (float)LandDefs.BlockLength, (other.Coord.Y - block.Coord.Y) * (float)LandDefs.BlockLength, 0f);
                foreach (var shadow in other.ForeignShadows)
                {
                    if (LandblockCoord.FromId(shadow.CellId) == block.Coord && shadowed.Add(shadow.Object))
                    {
                        StampStatic(tile, shadow.Object, offset, "shadow", NavFlags.Static, infl, o, prims, stats);
                    }
                }
            }

            // A neighbour's static whose solid reaches over the border without casting a shadow into a cell here still stops the body: the body's sphere
            // reaches the neighbour's cell before its centre does. The stamp clips to this tile, so only the part within the body's reach of the border marks.
            foreach (var other in neighbours)
            {
                if (other.Coord == block.Coord || Math.Abs(other.Coord.X - block.Coord.X) > 1 || Math.Abs(other.Coord.Y - block.Coord.Y) > 1)
                {
                    continue;
                }

                var offset = new Vector3((other.Coord.X - block.Coord.X) * (float)LandDefs.BlockLength, (other.Coord.Y - block.Coord.Y) * (float)LandDefs.BlockLength, 0f);
                foreach (var obj in other.StaticObjects)
                {
                    var at = obj.Position.Frame.Origin + offset;
                    if (at.X < -NeighbourStaticReach || at.Y < -NeighbourStaticReach || at.X > LandDefs.BlockLength + NeighbourStaticReach || at.Y > LandDefs.BlockLength + NeighbourStaticReach
                        || shadowed.Contains(obj))
                    {
                        continue;
                    }

                    shadowed.Add(obj);
                    StampStatic(tile, obj, offset, "shadow", NavFlags.Static, infl, o, prims, stats, reachOnly: true);
                }
            }
        }

        foreach (var building in block.Buildings)
        {
            StampBuilding(tile, block, building, infl, o, stats);
        }

        for (var i = 0; i < block.Buildings.Count; i++)
        {
            CutDoorwayLanes(tile, block, i, infl, o, stats);
        }

        ComputeClearance(tile);
        stats.Milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        tile.BuildMilliseconds = stats.Milliseconds;
        return tile;
    }

    /// <summary>
    /// Fills <see cref="NavTile.Clearance"/>: each node's distance in nodes to the nearest blocked node, capped at
    /// <see cref="NavTile.MaxClearance"/>, 0 on a blocked node. A two-pass chamfer (forward, then backward) with the integer
    /// weights 5 along an axis and 7 on a diagonal (distance = sum / 5, rounded; a neighbour of a blocked node reads 1).
    /// The tile's border is treated as open: nothing outside the tile counts as blocked (the planner joins neighbouring
    /// tiles). Re-runnable after a hand stamp.
    /// </summary>
    public static void ComputeClearance(NavTile tile)
    {
        ArgumentNullException.ThrowIfNull(tile);
        const int size = NavTile.Size;
        const int cap = NavTile.MaxClearance * ChamferAxis;
        var d = new ushort[size * size];
        for (var i = 0; i < d.Length; i++)
        {
            d[i] = (tile.Flags[i] & NavFlags.Blocked) != 0 ? (ushort)0 : (ushort)cap;
        }

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var i = (y * size) + x;
                var v = (int)d[i];
                if (v == 0)
                {
                    continue;
                }

                if (x > 0)
                {
                    v = Math.Min(v, d[i - 1] + ChamferAxis);
                }

                if (y > 0)
                {
                    v = Math.Min(v, d[i - size] + ChamferAxis);
                    if (x > 0)
                    {
                        v = Math.Min(v, d[i - size - 1] + ChamferDiagonal);
                    }

                    if (x < size - 1)
                    {
                        v = Math.Min(v, d[i - size + 1] + ChamferDiagonal);
                    }
                }

                d[i] = (ushort)v;
            }
        }

        for (var y = size - 1; y >= 0; y--)
        {
            for (var x = size - 1; x >= 0; x--)
            {
                var i = (y * size) + x;
                var v = (int)d[i];
                if (v == 0)
                {
                    continue;
                }

                if (x < size - 1)
                {
                    v = Math.Min(v, d[i + 1] + ChamferAxis);
                }

                if (y < size - 1)
                {
                    v = Math.Min(v, d[i + size] + ChamferAxis);
                    if (x < size - 1)
                    {
                        v = Math.Min(v, d[i + size + 1] + ChamferDiagonal);
                    }

                    if (x > 0)
                    {
                        v = Math.Min(v, d[i + size - 1] + ChamferDiagonal);
                    }
                }

                d[i] = (ushort)v;
                tile.Clearance[i] = (byte)Math.Min((v + (ChamferAxis / 2)) / ChamferAxis, NavTile.MaxClearance);
            }
        }

        for (var i = 0; i < d.Length; i++)
        {
            if (d[i] == 0)
            {
                tile.Clearance[i] = 0;
            }
        }
    }

    private static void StampGround(NavTile tile, LandblockCollision block, NavBuildOptions o, NavBuildStats stats)
    {
        const int cellNodes = (int)(LandDefs.CellLength / NavTile.NodeMetres);
        var cells = block.LandCells;
        if (cells.Count < TerrainConstants.CellCount)
        {
            // A landblock without a LandBlock record has no cells: nothing to stand on.
            for (var i = 0; i < tile.Flags.Length; i++)
            {
                tile.Flags[i] = NavFlags.Blocked;
                tile.Clearance[i] = 0;
            }

            stats.NodesWithoutTerrain = tile.Flags.Length;
            return;
        }

        var floor = PhysicsGlobals.FloorZ;
        var rampEnd = o.SlopeRampEnd;
        for (var cx = 0; cx < TerrainConstants.CellsPerSide; cx++)
        {
            for (var cy = 0; cy < TerrainConstants.CellsPerSide; cy++)
            {
                var cell = cells[(cx * TerrainConstants.CellsPerSide) + cy];
                var water = cell.WaterType != WaterType.NotWater ? NavFlags.Water : NavFlags.None;
                for (var x = cx * cellNodes; x < (cx + 1) * cellNodes; x++)
                {
                    for (var y = cy * cellNodes; y < (cy + 1) * cellNodes; y++)
                    {
                        var i = NavTile.Index(x, y);
                        var px = (x + 0.5f) * NavTile.NodeMetres;
                        var py = (y + 0.5f) * NavTile.NodeMetres;
                        if (cell.FindTerrainPoly(new Vector3(px, py, 0f)) is not { } polygon || MathF.Abs(polygon.Plane.Normal.Z) < 1e-4f)
                        {
                            tile.Flags[i] = NavFlags.Blocked | water;
                            tile.Clearance[i] = 0;
                            stats.NodesWithoutTerrain++;
                            continue;
                        }

                        var plane = polygon.Plane;
                        tile.Z[i] = -((plane.Normal.X * px) + (plane.Normal.Y * py) + plane.D) / plane.Normal.Z;
                        var flags = water;
                        var nz = plane.Normal.Z;
                        if (nz < floor)
                        {
                            flags |= NavFlags.Blocked;
                            tile.SlopeCost[i] = 255;
                            tile.Clearance[i] = 0;
                        }
                        else if (nz < rampEnd)
                        {
                            flags |= NavFlags.Slope;
                            tile.SlopeCost[i] = (byte)Math.Clamp((int)MathF.Round(255f * (rampEnd - nz) / (rampEnd - floor)), 1, 255);
                        }

                        tile.Flags[i] = flags;
                    }
                }
            }
        }

        StampSea(tile, block, o);
    }

    /// <summary>
    /// The sea wall (M6 4 w3's rule, moved here by w5 so every tile has it, the Godot host's too): a landblock entirely under water blocks every node
    /// (Blocked and Water), and a neighbour entirely under water blocks this tile's nodes within the body's reach (<see cref="NavBuildOptions.BodyRadius"/>
    /// plus <see cref="NavBuildOptions.Margin"/>, the statics' inflation) of the shared edge or corner. Retail's <c>CLandCell::find_env_collisions</c>
    /// (11.6096 0x00533C60, <c>PhysLandCell.FindEnvCollisions</c>) answers Collided to every body but the camera and missiles in such a landblock, so
    /// the open sea is a wall, not a costly wade; a partly flooded landblock only costs (its cells sink the body 0.9, 0.45 or 0.1 m,
    /// <c>CalcWaterDepth</c>). The neighbours are <see cref="NavBuildOptions.Neighbours"/> (null: only this landblock's own water).
    /// </summary>
    private static void StampSea(NavTile tile, LandblockCollision block, NavBuildOptions o)
    {
        if (block.WaterType == WaterType.EntirelyWater)
        {
            for (var i = 0; i < tile.Flags.Length; i++)
            {
                tile.Flags[i] |= NavFlags.Blocked | NavFlags.Water;
                tile.Clearance[i] = 0;
            }

            return;
        }

        if (o.Neighbours is not { } neighbours)
        {
            return;
        }

        const float side = NavTile.Size * NavTile.NodeMetres;
        var reach = o.BodyRadius + o.Margin;
        var rows = Math.Min((int)MathF.Ceiling(reach / NavTile.NodeMetres), NavTile.Size);
        foreach (var other in neighbours)
        {
            int dx = other.Coord.X - block.Coord.X, dy = other.Coord.Y - block.Coord.Y;
            if (other.WaterType != WaterType.EntirelyWater || (dx == 0 && dy == 0) || Math.Abs(dx) > 1 || Math.Abs(dy) > 1)
            {
                continue;
            }

            // The neighbour's square in this tile's metres, and the nodes that could lie within reach of it.
            float sx0 = dx * side, sy0 = dy * side;
            int x0 = dx > 0 ? NavTile.Size - rows : 0, x1 = dx < 0 ? rows - 1 : NavTile.Size - 1;
            int y0 = dy > 0 ? NavTile.Size - rows : 0, y1 = dy < 0 ? rows - 1 : NavTile.Size - 1;
            for (var y = y0; y <= y1; y++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    var c = NavTile.NodeCentre(x, y);
                    var ex = MathF.Max(MathF.Max(sx0 - c.X, c.X - (sx0 + side)), 0f);
                    var ey = MathF.Max(MathF.Max(sy0 - c.Y, c.Y - (sy0 + side)), 0f);
                    if ((ex * ex) + (ey * ey) <= reach * reach)
                    {
                        tile.Block(x, y, NavFlags.Water);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Costs the open nodes within <see cref="NavBuildOptions.SteepLandKeepOff"/> of each too-steep land triangle of <paramref name="source"/>, which lies <paramref name="offset"/> metres from this tile,
    /// and marks those whose square overlaps one (<see cref="NavFlags.SteepEdge"/>, sampled every quarter metre).
    /// </summary>
    private static void StampBesideSteepLand(NavTile tile, LandblockCollision source, Vector2 offset, NavBuildOptions o, NavBuildStats stats)
    {
        var reach = o.SteepLandKeepOff;
        foreach (var cell in source.LandCells)
        {
            foreach (var polygon in cell.Polygons)
            {
                var normalZ = polygon.Plane.Normal.Z;
                if (normalZ >= PhysicsGlobals.FloorZ || MathF.Abs(normalZ) < 1e-4f)
                {
                    continue;
                }

                var v = polygon.Vertices;
                Vector2 a = new Vector2(v[0].X, v[0].Y) + offset, b = new Vector2(v[1].X, v[1].Y) + offset, c = new Vector2(v[2].X, v[2].Y) + offset;
                var low = Vector2.Min(a, Vector2.Min(b, c)) - new Vector2(reach);
                var high = Vector2.Max(a, Vector2.Max(b, c)) + new Vector2(reach);
                int x0 = Math.Max((int)MathF.Floor(low.X / NavTile.NodeMetres), 0), x1 = Math.Min((int)MathF.Ceiling(high.X / NavTile.NodeMetres), NavTile.Size - 1);
                int y0 = Math.Max((int)MathF.Floor(low.Y / NavTile.NodeMetres), 0), y1 = Math.Min((int)MathF.Ceiling(high.Y / NavTile.NodeMetres), NavTile.Size - 1);
                for (var x = x0; x <= x1; x++)
                {
                    for (var y = y0; y <= y1; y++)
                    {
                        var i = NavTile.Index(x, y);
                        if ((tile.Flags[i] & NavFlags.Blocked) != 0)
                        {
                            continue;
                        }

                        var away = DistanceToTriangle(new Vector2((x + 0.5f) * NavTile.NodeMetres, (y + 0.5f) * NavTile.NodeMetres), a, b, c);
                        if (away > reach)
                        {
                            continue;
                        }

                        if ((tile.Flags[i] & NavFlags.SteepEdge) == 0 && away < NavTile.NodeMetres * 0.71f && SquareOverlaps(x, y, a, b, c))
                        {
                            tile.Flags[i] |= NavFlags.SteepEdge;
                            stats.NodesOnSteepEdge++;
                        }

                        if ((tile.Flags[i] & NavFlags.Slope) != 0 && tile.SlopeCost[i] == 255)
                        {
                            continue;
                        }

                        tile.Flags[i] |= NavFlags.Slope;
                        tile.SlopeCost[i] = 255;
                        stats.NodesBesideSteepLand++;
                    }
                }
            }
        }
    }

    /// <summary>Whether any of 16 points spread over node (x, y)'s square, a quarter metre apart, lies on the triangle.</summary>
    private static bool SquareOverlaps(int x, int y, Vector2 a, Vector2 b, Vector2 c)
    {
        for (var sx = 0; sx < 4; sx++)
        {
            for (var sy = 0; sy < 4; sy++)
            {
                var p = new Vector2((x + ((sx + 0.5f) * 0.25f)) * NavTile.NodeMetres, (y + ((sy + 0.5f) * 0.25f)) * NavTile.NodeMetres);
                if (DistanceToTriangle(p, a, b, c) == 0f)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The distance on the map from a point to a triangle: 0 inside it.</summary>
    private static float DistanceToTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        static float Side(Vector2 from, Vector2 to, Vector2 p) => ((to.X - from.X) * (p.Y - from.Y)) - ((to.Y - from.Y) * (p.X - from.X));

        static float ToEdge(Vector2 p, Vector2 from, Vector2 to)
        {
            var along = to - from;
            var t = Math.Clamp(Vector2.Dot(p - from, along) / MathF.Max(along.LengthSquared(), 1e-6f), 0f, 1f);
            return Vector2.Distance(p, from + (along * t));
        }

        float ab = Side(a, b, p), bc = Side(b, c, p), ca = Side(c, a, p);
        return (ab >= 0f && bc >= 0f && ca >= 0f) || (ab <= 0f && bc <= 0f && ca <= 0f) ? 0f : MathF.Min(ToEdge(p, a, b), MathF.Min(ToEdge(p, b, c), ToEdge(p, c, a)));
    }

    /// <summary>How far past the border a neighbour's static may stand and still be looked at for reaching over it, in metres: the widest outdoor statics. Lifestone's.</summary>
    private const float NeighbourStaticReach = 40f;

    // reachOnly: a neighbour's static looked at only for reaching over the border, counted when it marks and silent when it does not.
    private static void StampStatic(NavTile tile, PhysStaticObject obj, Vector3 offset, string kind, NavFlags flag, float infl, NavBuildOptions o, List<Prim> prims, NavBuildStats stats, bool reachOnly = false)
    {
        if (!LandDefs.IsOutdoorCell(obj.Position.CellId))
        {
            if (!reachOnly)
            {
                stats.StaticsIndoor++;
            }

            return;
        }

        prims.Clear();
        if ((obj.State & PhysicsState.HasPhysicsBsp) != 0)
        {
            foreach (var part in obj.Parts)
            {
                if (part.Shape.Physics is not null)
                {
                    prims.Add(Prim.Box(part, offset, tiltedByPhysics: true));
                }
            }
        }
        else if (obj.CylSpheres.Count > 0)
        {
            foreach (var cyl in obj.CylSpheres)
            {
                prims.Add(Prim.Cylinder(obj, cyl, offset));
            }
        }
        else
        {
            foreach (var sphere in obj.Spheres)
            {
                prims.Add(Prim.Ball(obj, sphere, offset));
            }
        }

        prims.RemoveAll(static p => !p.Valid);
        if (prims.Count == 0)
        {
            if (!reachOnly)
            {
                stats.StaticsNoNodes++;
            }

            return;
        }

        if (reachOnly)
        {
            float lowX = float.MaxValue, highX = float.MinValue, lowY = float.MaxValue, highY = float.MinValue;
            foreach (var p in prims)
            {
                lowX = MathF.Min(lowX, p.MinX);
                highX = MathF.Max(highX, p.MaxX);
                lowY = MathF.Min(lowY, p.MinY);
                highY = MathF.Max(highY, p.MaxY);
            }

            if (highX + infl < 0f || highY + infl < 0f || lowX - infl > LandDefs.BlockLength || lowY - infl > LandDefs.BlockLength)
            {
                return;
            }
        }

        float top = float.MinValue, minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var p in prims)
        {
            top = MathF.Max(top, p.ZHigh);
            minX = MathF.Min(minX, p.MinX);
            maxX = MathF.Max(maxX, p.MaxX);
            minY = MathF.Min(minY, p.MinY);
            maxY = MathF.Max(maxY, p.MaxY);
        }

        var low = float.MaxValue;
        foreach (var p in prims)
        {
            low = MathF.Min(low, p.ZLow);
        }

        if (maxX - minX < o.TinySize && maxY - minY < o.TinySize && top - low < o.StepUp)
        {
            if (!reachOnly)
            {
                stats.StaticsTiny++;
            }

            return;
        }

        var origin = obj.Position.Frame.Origin + offset;
        if (NavTile.InBounds((int)MathF.Floor(origin.X), (int)MathF.Floor(origin.Y))
            && top <= tile.Z[NavTile.Index((int)MathF.Floor(origin.X), (int)MathF.Floor(origin.Y))] + o.DecalHeight)
        {
            if (!reachOnly)
            {
                stats.StaticsBelowGround++;
            }

            return;
        }

        var made = MakeOwner(tile, new NavObstacle(obj.ModelId, origin, kind), stats, out var owner);
        var marked = 0;
        foreach (var p in prims)
        {
            marked += Stamp(tile, p, owner, flag, infl, o, stats);
        }

        if (marked == 0)
        {
            if (made)
            {
                tile.Obstacles.RemoveAt(tile.Obstacles.Count - 1);
            }

            if (!reachOnly)
            {
                stats.StaticsNoNodes++;
            }
        }
        else if (reachOnly)
        {
            stats.ReachStamped++;
        }
        else if (kind == "shadow")
        {
            stats.ShadowsStamped++;
        }
        else
        {
            stats.StaticsStamped++;
        }
    }

    private static void StampBuilding(NavTile tile, LandblockCollision block, PhysBuilding building, float infl, NavBuildOptions o, NavBuildStats stats)
    {
        var made = MakeOwner(tile, new NavObstacle(building.ModelId, building.Position.Frame.Origin, "building"), stats, out var owner);
        var marked = 0;
        if (building.Part.Shape.Physics is { Polygons.Count: > 0 } physics)
        {
            // The footprint is the union of the shell's collision polygons projected on the ground (walls, roofs and eaves
            // alike, so the inside of the shell is blocked too): a building's bounding box is far larger than its footprint
            // wherever it has a terrace, a stair or an L (Holtburg's Renald's shop: a 20 m box round a 6 m room).
            var frame = building.Part.Position.Frame;
            var scale = building.Part.GfxObjScale;
            Span<Vector2> xy = stackalloc Vector2[16];
            var fresh = new List<int>();
            foreach (var polygon in physics.Polygons)
            {
                var vertices = polygon.Vertices;
                if (vertices.Length < 3 || vertices.Length > xy.Length)
                {
                    continue;
                }

                var top = float.MinValue;
                for (var i = 0; i < vertices.Length; i++)
                {
                    var w = frame.Origin + Vector3.Transform(vertices[i] * scale, frame.Orientation);
                    xy[i] = new Vector2(w.X, w.Y);
                    top = MathF.Max(top, w.Z);
                }

                marked += StampPolygon(tile, xy[..vertices.Length], top, owner, infl, o, fresh);
            }

            // M6 4 w6a (TODO(m6-tethana-grand-masters)): the projection blocks every node under the shell, a roofed passage through it too (Fort Tethana's gatehouse
            // 0x01001030 over the fort's gate, 0x2581 x 173, y 83 and 84). A node this building blocked, and nothing before it, that the body physically stands on
            // outdoors is opened again: the body's spheres over the walking band are placed against the shell's own physics as retail places a body outdoors
            // (BodyCollides, the test W5 uses for a tilted part), and the body's middle is in none of the building's rooms (a shell is not solid inside every room:
            // the room graph owns the rooms, and the doorway lanes are the only way in).
            List<PhysEnvCell>? rooms = null;
            foreach (var i in fresh)
            {
                var (x, y) = (i % NavTile.Size, i / NavTile.Size);
                var (cx, cy) = ((x + 0.5f) * NavTile.NodeMetres, (y + 0.5f) * NavTile.NodeMetres);
                if (BodyCollides(building.Part, Vector3.Zero, cx, cy, tile.Z[i], infl, o))
                {
                    continue;
                }

                rooms ??= RoomsOf(block, building);
                var middle = new Vector3(cx, cy, tile.Z[i] + ((o.DecalHeight + o.OverheadClearance) * 0.5f));
                if (rooms.Exists(r => r.PointInCell(middle)))
                {
                    continue;
                }

                tile.Flags[i] &= ~(NavFlags.Blocked | NavFlags.Building);
                tile.Owner[i] = NavTile.NoOwner;
                marked--;
                stats.BuildingNodesOpen++;
            }
        }
        else
        {
            marked = Stamp(tile, Prim.Box(building.Part, Vector3.Zero), owner, NavFlags.Building, infl, o);
            stats.BuildingsByBounds++;
        }

        if (marked == 0)
        {
            if (made)
            {
                tile.Obstacles.RemoveAt(tile.Obstacles.Count - 1);
            }

            return;
        }

        stats.BuildingsStamped++;
    }

    /// <summary>
    /// Blocks the nodes within <paramref name="infl"/> of the polygon <paramref name="xy"/> (inside it, or near an edge),
    /// except where its highest corner is within the decal height of the ground (a floor slab).
    /// </summary>
    private static int StampPolygon(NavTile tile, ReadOnlySpan<Vector2> xy, float top, ushort owner, float infl, NavBuildOptions o, List<int> fresh)
    {
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var p in xy)
        {
            minX = MathF.Min(minX, p.X);
            maxX = MathF.Max(maxX, p.X);
            minY = MathF.Min(minY, p.Y);
            maxY = MathF.Max(maxY, p.Y);
        }

        if (!float.IsFinite(minX + maxX + minY + maxY))
        {
            return 0;
        }

        var x0 = Math.Max((int)MathF.Floor(minX - infl), 0);
        var x1 = Math.Min((int)MathF.Floor(maxX + infl), NavTile.Size - 1);
        var y0 = Math.Max((int)MathF.Floor(minY - infl), 0);
        var y1 = Math.Min((int)MathF.Floor(maxY + infl), NavTile.Size - 1);
        var infl2 = infl * infl;
        var marked = 0;
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var i = NavTile.Index(x, y);
                if ((tile.Flags[i] & NavFlags.Building) != 0 && tile.Owner[i] == owner && owner != NavTile.NoOwner)
                {
                    continue;
                }

                if (top <= tile.Z[i] + o.DecalHeight)
                {
                    continue;
                }

                var cx = (x + 0.5f) * NavTile.NodeMetres;
                var cy = (y + 0.5f) * NavTile.NodeMetres;
                if (!InsidePolygon(xy, cx, cy) && EdgeDistanceSquared(xy, cx, cy) > infl2)
                {
                    continue;
                }

                if ((tile.Flags[i] & NavFlags.Blocked) == 0)
                {
                    fresh.Add(i);
                }

                Mark(tile, i, owner, NavFlags.Building);
                marked++;
            }
        }

        return marked;
    }

    /// <summary>
    /// Whether a body of the inflation radius <paramref name="infl"/> standing on <paramref name="ground"/> at landblock-local (cx, cy) touches <paramref name="part"/>
    /// (placed <paramref name="offset"/> from this landblock): a stack of spheres of the inflation radius whose union spans the walking band, from
    /// <see cref="NavBuildOptions.DecalHeight"/> over the ground to <see cref="NavBuildOptions.OverheadClearance"/>, spaced at most one radius apart, each placed against
    /// the part with <see cref="PhysPart.PlacementCollides"/> (the placement test of retail's <c>BSPTREE::find_collisions</c>, 11.6096 0x0053B1A0, with the centre check).
    /// </summary>
    private static bool BodyCollides(PhysPart part, Vector3 offset, float cx, float cy, float ground, float infl, NavBuildOptions o)
    {
        var low = ground + o.DecalHeight + infl;
        var high = MathF.Max(ground + o.OverheadClearance - infl, low);
        var steps = (int)MathF.Ceiling((high - low) / infl);
        var local = new Vector3(cx, cy, 0f) - offset;
        for (var k = 0; k <= steps; k++)
        {
            local.Z = steps == 0 ? low : low + ((high - low) * k / steps);
            if (part.PlacementCollides(local, infl))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The building's rooms: the entry cells of its doorways and every cell of this landblock reached from them through the cells' own portals.</summary>
    private static List<PhysEnvCell> RoomsOf(LandblockCollision block, PhysBuilding building)
    {
        var rooms = new List<PhysEnvCell>();
        var seen = new HashSet<uint>();
        var queue = new Queue<uint>();
        foreach (var portal in building.Portals)
        {
            if (seen.Add(portal.OtherCellId))
            {
                queue.Enqueue(portal.OtherCellId);
            }
        }

        while (queue.Count > 0)
        {
            if (!block.EnvCells.TryGetValue(queue.Dequeue(), out var cell))
            {
                continue;
            }

            rooms.Add(cell);
            foreach (var portal in cell.Portals)
            {
                if (!portal.LeadsOutside && seen.Add(portal.OtherCellId))
                {
                    queue.Enqueue(portal.OtherCellId);
                }
            }
        }

        return rooms;
    }

    internal static bool InsidePolygon(ReadOnlySpan<Vector2> xy, float px, float py)
    {
        var inside = false;
        for (int i = 0, j = xy.Length - 1; i < xy.Length; j = i++)
        {
            if ((xy[i].Y > py) != (xy[j].Y > py) && px < ((xy[j].X - xy[i].X) * (py - xy[i].Y) / (xy[j].Y - xy[i].Y)) + xy[i].X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static float EdgeDistanceSquared(ReadOnlySpan<Vector2> xy, float px, float py)
    {
        var best = float.MaxValue;
        var p = new Vector2(px, py);
        for (int i = 0, j = xy.Length - 1; i < xy.Length; j = i++)
        {
            var a = xy[j];
            var edge = xy[i] - a;
            var length2 = edge.LengthSquared();
            var t = length2 < 1e-12f ? 0f : Math.Clamp(Vector2.Dot(p - a, edge) / length2, 0f, 1f);
            best = MathF.Min(best, Vector2.DistanceSquared(p, a + (edge * t)));
        }

        return best;
    }

    private static void Mark(NavTile tile, int i, ushort owner, NavFlags kind)
    {
        tile.Flags[i] |= NavFlags.Blocked | kind;
        tile.Clearance[i] = 0;
        if (owner != NavTile.NoOwner && tile.Owner[i] == NavTile.NoOwner)
        {
            tile.Owner[i] = owner;
        }
    }

    private static bool MakeOwner(NavTile tile, NavObstacle obstacle, NavBuildStats stats, out ushort owner)
    {
        if (tile.Obstacles.Count >= OwnerLimit)
        {
            stats.ObstacleOverflow++;
            owner = NavTile.NoOwner;
            return false;
        }

        owner = (ushort)tile.Obstacles.Count;
        tile.Obstacles.Add(obstacle);
        return true;
    }

    /// <summary>Blocks the nodes <paramref name="p"/> inflated by <paramref name="infl"/> reaches; returns how many it marked.</summary>
    private static int Stamp(NavTile tile, in Prim p, ushort owner, NavFlags kind, float infl, NavBuildOptions o, NavBuildStats? stats = null)
    {
        var x0 = Math.Max((int)MathF.Floor(p.MinX - infl), 0);
        var x1 = Math.Min((int)MathF.Floor(p.MaxX + infl), NavTile.Size - 1);
        var y0 = Math.Max((int)MathF.Floor(p.MinY - infl), 0);
        var y1 = Math.Min((int)MathF.Floor(p.MaxY + infl), NavTile.Size - 1);
        var infl2 = infl * infl;
        var marked = 0;
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var i = NavTile.Index(x, y);
                var ground = tile.Z[i];
                if (p.ZHigh <= ground + o.DecalHeight || p.ZLow >= ground + o.OverheadClearance)
                {
                    continue;
                }

                var cx = (x + 0.5f) * NavTile.NodeMetres;
                var cy = (y + 0.5f) * NavTile.NodeMetres;

                // M6 4 f3: a static's flat top (a cylinder-sphere's, a level box's) within the step-up of the ground here is a step the body walks up onto, not a wall.
                if (kind == NavFlags.Static && p.FlatTop && p.ZHigh <= ground + o.StepUp)
                {
                    if (stats is not null && p.Hits(cx, cy, ground, infl2, infl, o))
                    {
                        stats.StaticNodesStepped++;
                    }

                    continue;
                }

                if (!p.Hits(cx, cy, ground, infl2, infl, o))
                {
                    continue;
                }

                Mark(tile, i, owner, kind);
                marked++;
            }
        }

        return marked;
    }

    /// <summary>
    /// Extends a straight doorway strip whose next row lies under <paramref name="building"/>'s shell along its line, row by row up to <see cref="NavBuildOptions.LaneOverShellMetres"/> from the sill, while every node
    /// of the row is open or the shell's and the body at <paramref name="sill"/>'s height stands clear of the shell's physics at the row's middle (<see cref="BodyCollides"/>); true when a row opens onto the land,
    /// the strip then reaching it. Nothing is added otherwise.
    /// </summary>
    private static bool ExtendOverShell(NavTile tile, PhysBuilding building, Vector2 origin, Vector2 outward, Vector2 side, int lengthSteps, int acrossSteps, float sill, float infl, NavBuildOptions o, List<int> nodes, HashSet<int> seen)
    {
        var added = new List<int>();
        var maxSteps = (int)MathF.Round(o.LaneOverShellMetres / LaneSample);
        for (var k = lengthSteps + 1; k <= maxSteps; k++)
        {
            var row = new List<int>();
            var underShell = false;
            for (var j = -acrossSteps; j <= acrossSteps; j++)
            {
                var point = origin + (outward * (k * LaneSample)) + (side * (j * LaneSample));
                var nx = (int)MathF.Floor(point.X / NavTile.NodeMetres);
                var ny = (int)MathF.Floor(point.Y / NavTile.NodeMetres);
                if (!NavTile.InBounds(nx, ny))
                {
                    return false;
                }

                var i = NavTile.Index(nx, ny);
                var flags = tile.Flags[i];
                if ((flags & NavFlags.Blocked) != 0)
                {
                    if ((flags & NavFlags.Building) == 0)
                    {
                        return false; // something else stands there
                    }

                    underShell = true;
                }

                row.Add(i);
            }

            if (!underShell)
            {
                if (k == lengthSteps + 1)
                {
                    return false; // the strip already ends on the land
                }

                foreach (var i in row.Where(i => seen.Add(i)))
                {
                    added.Add(i);
                }

                nodes.AddRange(added);
                return true;
            }

            var middle = origin + (outward * (k * LaneSample));
            if (BodyCollides(building.Part, Vector3.Zero, middle.X, middle.Y, sill, infl, o))
            {
                return false;
            }

            foreach (var i in row.Where(i => seen.Add(i)))
            {
                added.Add(i);
            }
        }

        return false;
    }

    private static void CutDoorwayLanes(NavTile tile, LandblockCollision block, int buildingIndex, float infl, NavBuildOptions o, NavBuildStats stats)
    {
        var building = block.Buildings[buildingIndex];
        List<(Vector3[] Corners, Plane Plane, Vector4 Box)>? floors = null;
        LaneStairs? lanesStairs = null;
        foreach (var portal in building.Portals)
        {
            if (!block.EnvCells.TryGetValue(portal.OtherCellId, out var cell)
                || (uint)portal.OtherPortalIndex >= (uint)cell.Portals.Count)
            {
                stats.PortalsNoGeometry++;
                stats.PortalSkips.Add((portal.OtherCellId, "no-geometry"));
                continue;
            }

            var outside = cell.Portals[portal.OtherPortalIndex];
            if (!outside.LeadsOutside || outside.Vertices is not { Length: >= 3 } vertices)
            {
                stats.PortalsNoGeometry++;
                stats.PortalSkips.Add((portal.OtherCellId, "no-geometry"));
                continue;
            }

            var frame = cell.Position.Frame;
            var normal = frame.LocalToGlobalVec(outside.Plane.Normal);
            if (normal.LengthSquared() < 1e-8f || MathF.Abs(Vector3.Normalize(normal).Z) > MathF.Max(CellRoutePlanner.UprightNormalZ, o.DoorwayLean))
            {
                stats.PortalsImpassable++;
                stats.PortalSkips.Add((portal.OtherCellId, "tilted"));
                continue;
            }

            normal = Vector3.Normalize(normal);
            var corners = new Vector3[vertices.Length];
            var centre = Vector3.Zero;
            var sill = float.MaxValue;
            for (var i = 0; i < corners.Length; i++)
            {
                corners[i] = frame.LocalToGlobal(vertices[i]);
                centre += corners[i];
                sill = MathF.Min(sill, corners[i].Z);
            }

            centre /= corners.Length;

            // The passability probes of CellRoutePlanner.IsWalkable (the human's width and height through the polygon, the head as the room graph's: NavBuildOptions.HeadSlack).
            var along = Vector3.Normalize(new Vector3(-normal.Y, normal.X, 0f));
            var floor = new Vector3(centre.X, centre.Y, sill);
            if (!(InPortal(corners, along, floor + new Vector3(0, 0, 0.2f))
                && InPortal(corners, along, floor + new Vector3(0, 0, CellRoutePlanner.HumanHeight - o.HeadSlack))
                && InPortal(corners, along, floor + new Vector3(0, 0, 1f) + (along * o.BodyRadius))
                && InPortal(corners, along, floor + new Vector3(0, 0, 1f) - (along * o.BodyRadius))))
            {
                stats.PortalsImpassable++;
                stats.PortalSkips.Add((portal.OtherCellId, "impassable"));
                continue;
            }

            // The neighbour (the outdoors) lies along the normal when the cell is on the negative side (PortalSide).
            var into = outside.PortalSide ? normal : -normal;
            var outward = new Vector2(into.X, into.Y);
            outward = Vector2.Normalize(outward);

            // M6 4 w6a (TODO(m6-holtburg-upper-floor), the lane half): the body comes to the lane's outer end on the ground and walks its centre line in to the sill, so
            // the sill must be reached from that ground by steps: a door onto a balcony over a street (Holtburg 0xA9B30106 and 0xA9B30110, sills at 119.0 over the
            // street at 116.0) gets no lane, and the plan stops climbing the wall to it.
            // M6 7 A (D62): a door the straight rule refuses keeps its lane if a walk turns aside down to the ground (the Refuge's raised huts, LaneStairs); only then is it above the ground.
            // The world walk (W8): steps along the lane's line that meet the ground past the strip leave the strip on a deck, its points at the ground's height under the body
            // and its outer end among the deck's blocked nodes (a door 2 m up onto a deck, 77 doors over the dats). Such a door is walked as raised: down its floors, cut on the ground past their foot.
            var climbs = LaneClimbs(block, building, new Vector2(centre.X, centre.Y), sill, outward, o, ref floors, out var groundAt);
            var raised = climbs && groundAt + NavTile.NodeMetres > o.LaneLength;
            if (raised)
            {
                lanesStairs ??= new LaneStairs(block, tile, building, o);
                if (lanesStairs.Find(new Vector3(centre.X, centre.Y, sill), outward) is { } down)
                {
                    OpenLane(tile, down.Nodes);
                    tile.DoorwayLanes.Add(new NavDoorwayLane(buildingIndex, portal.OtherCellId, new Vector3(centre.X, centre.Y, sill), outward, down.Nodes, down.Points));
                    stats.LanesBuilt++;
                    stats.LanesByStairs++;
                    stats.LanesRaisedLine++;
                    continue;
                }
            }

            if (!climbs)
            {
                lanesStairs ??= new LaneStairs(block, tile, building, o);
                if (lanesStairs.Find(new Vector3(centre.X, centre.Y, sill), outward) is not { } stairs)
                {
                    stats.PortalsAboveGround++;
                    stats.PortalSkips.Add((portal.OtherCellId, "above-ground"));
                    continue;
                }

                OpenLane(tile, stairs.Nodes);
                tile.DoorwayLanes.Add(new NavDoorwayLane(buildingIndex, portal.OtherCellId, new Vector3(centre.X, centre.Y, sill), outward, stairs.Nodes, stairs.Points));
                stats.LanesBuilt++;
                stats.LanesByStairs++;
                continue;
            }

            var side = new Vector2(-outward.Y, outward.X);
            var nodes = new List<int>();
            var seen = new HashSet<int>();
            var origin = new Vector2(centre.X, centre.Y);
            var lengthSteps = (int)MathF.Round(o.LaneLength / LaneSample);
            var acrossSteps = (int)MathF.Floor((o.LaneWidth * 0.5f / LaneSample) + 1e-4f);
            for (var k = 0; k <= lengthSteps; k++)
            {
                for (var j = -acrossSteps; j <= acrossSteps; j++)
                {
                    var point = origin + (outward * (k * LaneSample)) + (side * (j * LaneSample));
                    var nx = (int)MathF.Floor(point.X / NavTile.NodeMetres);
                    var ny = (int)MathF.Floor(point.Y / NavTile.NodeMetres);
                    if (NavTile.InBounds(nx, ny) && seen.Add(NavTile.Index(nx, ny)))
                    {
                        nodes.Add(NavTile.Index(nx, ny));
                    }
                }
            }

            if (nodes.Count == 0)
            {
                stats.PortalsOffTile++;
                stats.PortalSkips.Add((portal.OtherCellId, "off-tile"));
                continue;
            }

            // The world walk (W8): a porch the shell covers past the strip (a hut's 0x0100110A, its floor 0.4 m over the land, 6 m of it before the door) leaves the strip's outer end
            // among the shell's blocked nodes, and no plan reaches the door, though a body walks straight out. The strip goes on along its line over this shell while the body,
            // at the sill's height, stands clear of the shell's physics, until a row opens onto the land; the shell's own nodes are the only ones it opens.
            if (ExtendOverShell(tile, building, origin, outward, side, lengthSteps, acrossSteps, sill, infl, o, nodes, seen))
            {
                stats.LanesOverShell++;
            }

            OpenLane(tile, nodes);
            tile.DoorwayLanes.Add(new NavDoorwayLane(buildingIndex, portal.OtherCellId, new Vector3(centre.X, centre.Y, sill), outward, nodes.ToArray()));
            stats.LanesBuilt++;
        }
    }

    /// <summary>Unblocks a lane's nodes and flags them (except where the ground itself is too steep).</summary>
    private static void OpenLane(NavTile tile, IReadOnlyList<int> nodes)
    {
        foreach (var i in nodes)
        {
            var terrainSteep = tile.SlopeCost[i] == 255 && (tile.Flags[i] & NavFlags.Slope) == 0;
            if (terrainSteep)
            {
                continue;
            }

            tile.Flags[i] = (tile.Flags[i] & ~(NavFlags.Blocked | NavFlags.Static | NavFlags.Building)) | NavFlags.DoorwayLane;
            tile.Owner[i] = NavTile.NoOwner;
        }
    }

    /// <summary>
    /// Whether the sill is reached from the ground by steps along the lane's line: from the sill outward along <paramref name="outward"/>, every <c>LaneSample</c>, the floors
    /// under the line are the terrain and the walkable polygons of the building's shell (a porch, its stairs, a balcony: normal z at least <c>PhysicsGlobals.FloorZ</c>, either
    /// face); the body keeps to the floor nearest its last height within <see cref="NavBuildOptions.StepUp"/> (up or down). True once the terrain is within a step of it (a
    /// ground-floor door at once, a porch at the foot of its stairs), within <see cref="NavBuildOptions.LaneClimbLength"/> of the sill; false where no floor is within a step
    /// (a balcony's edge over the street). Lifestone's own rule (no retail planner exists); the physics confirms the walk. Stairs that turn aside from the lane's line are <see cref="LaneStairs"/>' (M6 7 A).
    /// <paramref name="groundAt"/> is how far out from the sill the terrain was met (0 when false).
    /// </summary>
    private static bool LaneClimbs(LandblockCollision block, PhysBuilding building, Vector2 origin, float sill, Vector2 outward, NavBuildOptions o, ref List<(Vector3[] Corners, Plane Plane, Vector4 Box)>? floors, out float groundAt)
    {
        groundAt = 0f;
        var steps = (int)MathF.Round(o.LaneClimbLength / LaneSample);
        var height = sill;
        Span<Vector2> xy = stackalloc Vector2[16];
        for (var k = 0; k <= steps; k++)
        {
            var point = origin + (outward * (k * LaneSample));
            if (TerrainZ(block, point) is { } ground && MathF.Abs(ground - height) <= o.StepUp + 1e-3f)
            {
                groundAt = k * LaneSample;
                return true;
            }

            floors ??= FloorsOf(building);
            var best = float.NaN;
            foreach (var (corners, plane, box) in floors)
            {
                if (point.X < box.X || point.X > box.Z || point.Y < box.Y || point.Y > box.W)
                {
                    continue;
                }

                for (var i = 0; i < corners.Length; i++)
                {
                    xy[i] = new Vector2(corners[i].X, corners[i].Y);
                }

                if (!InsidePolygon(xy[..corners.Length], point.X, point.Y))
                {
                    continue;
                }

                var z = -((plane.Normal.X * point.X) + (plane.Normal.Y * point.Y) + plane.D) / plane.Normal.Z;
                if (MathF.Abs(z - height) <= o.StepUp + 1e-3f && (float.IsNaN(best) || MathF.Abs(z - height) < MathF.Abs(best - height)))
                {
                    best = z;
                }
            }

            if (float.IsNaN(best))
            {
                // The doorway itself (k 0) may stand over no shell floor (the room's floor is the EnvCell's): the sill's height carries on.
                if (k > 0)
                {
                    return false;
                }

                continue;
            }

            height = best;
        }

        return false;
    }

    /// <summary>The walkable polygons of a building's shell (normal z at least <c>PhysicsGlobals.FloorZ</c>, either face), in landblock-local space with their planes and xy boxes.</summary>
    private static List<(Vector3[] Corners, Plane Plane, Vector4 Box)> FloorsOf(PhysBuilding building)
    {
        var floors = new List<(Vector3[] Corners, Plane Plane, Vector4 Box)>();
        if (building.Part.Shape.Physics is not { Polygons.Count: > 0 } physics)
        {
            return floors;
        }

        var frame = building.Part.Position.Frame;
        var scale = building.Part.GfxObjScale;
        foreach (var polygon in physics.Polygons)
        {
            var vertices = polygon.Vertices;
            if (vertices.Length < 3 || vertices.Length > 16)
            {
                continue;
            }

            var corners = new Vector3[vertices.Length];
            for (var i = 0; i < corners.Length; i++)
            {
                corners[i] = frame.Origin + Vector3.Transform(vertices[i] * scale, frame.Orientation);
            }

            var plane = PhysPolygon.MakePlane(corners);
            if (MathF.Abs(plane.Normal.Z) < PhysicsGlobals.FloorZ)
            {
                continue;
            }

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var c in corners)
            {
                (minX, maxX, minY, maxY) = (MathF.Min(minX, c.X), MathF.Max(maxX, c.X), MathF.Min(minY, c.Y), MathF.Max(maxY, c.Y));
            }

            floors.Add((corners, plane, new Vector4(minX, minY, maxX, maxY)));
        }

        return floors;
    }

    /// <summary>The terrain's height at a landblock-local point (the terrain polygon's plane, <c>CLandCell::find_terrain_poly</c>), or null off the landblock or with no polygon.</summary>
    internal static float? TerrainZ(LandblockCollision block, Vector2 point)
    {
        var cx = (int)MathF.Floor(point.X / (float)LandDefs.CellLength);
        var cy = (int)MathF.Floor(point.Y / (float)LandDefs.CellLength);
        if (block.LandCells.Count < TerrainConstants.CellCount || cx < 0 || cy < 0 || cx >= TerrainConstants.CellsPerSide || cy >= TerrainConstants.CellsPerSide)
        {
            return null;
        }

        if (block.LandCells[(cx * TerrainConstants.CellsPerSide) + cy].FindTerrainPoly(new Vector3(point, 0f)) is not { } polygon || MathF.Abs(polygon.Plane.Normal.Z) < 1e-4f)
        {
            return null;
        }

        var plane = polygon.Plane;
        return -((plane.Normal.X * point.X) + (plane.Normal.Y * point.Y) + plane.D) / plane.Normal.Z;
    }

    /// <summary>A crossing test of <paramref name="point"/> against the upright polygon <paramref name="corners"/> on the plane's horizontal axis <paramref name="along"/> and z (as <c>CellRoutePlanner</c>'s).</summary>
    private static bool InPortal(Vector3[] corners, Vector3 along, Vector3 point)
    {
        var inside = false;
        var px = Vector3.Dot(point, along);
        var py = point.Z;
        for (int i = 0, j = corners.Length - 1; i < corners.Length; j = i++)
        {
            var xi = Vector3.Dot(corners[i], along);
            var yi = corners[i].Z;
            var xj = Vector3.Dot(corners[j], along);
            var yj = corners[j].Z;
            if ((yi > py) != (yj > py) && px < ((xj - xi) * (py - yi) / (yj - yi)) + xi)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>One stampable shape in landblock-local space: an oriented rectangle (a part's box), a world box, or a circle.</summary>
    private readonly struct Prim
    {
        private readonly bool _circle;
        private readonly bool _oriented;
        private readonly Vector3 _origin;
        private readonly Quaternion _inverse;
        private readonly Vector3 _lo;
        private readonly Vector3 _hi;
        private readonly float _radius;
        private readonly PhysPart? _part;
        private readonly Vector3 _offset;
        private readonly bool _flatTop;

        private Prim(bool circle, bool oriented, Vector3 origin, Quaternion inverse, Vector3 lo, Vector3 hi, float radius, float minX, float maxX, float minY, float maxY, float zLow, float zHigh, PhysPart? part = null, Vector3 offset = default, bool flatTop = false)
        {
            _flatTop = flatTop;
            _part = part;
            _offset = offset;
            _circle = circle;
            _oriented = oriented;
            _origin = origin;
            _inverse = inverse;
            _lo = lo;
            _hi = hi;
            _radius = radius;
            MinX = minX;
            MaxX = maxX;
            MinY = minY;
            MaxY = maxY;
            ZLow = zLow;
            ZHigh = zHigh;
        }

        /// <summary>A shape that stamps nothing (a part with an empty bounding box).</summary>
        public static Prim Empty { get; } = new(false, false, default, Quaternion.Identity, default, default, 0f, 1f, 0f, 1f, 0f, 1f, 0f);

        public float MinX { get; }

        public float MaxX { get; }

        public float MinY { get; }

        public float MaxY { get; }

        public float ZLow { get; }

        public float ZHigh { get; }

        /// <summary>
        /// A shape with a flat top a body steps up onto: a cylinder-sphere (retail <c>CCylSphere::step_sphere_up</c>, 11.6096 0x0053C070: a walker steps onto its top when the rise is within
        /// its step-up) or a box turned about the vertical only and not asked by its physics. A static's low one is a step, not a wall (M6 4 f3). A sphere is not.
        /// </summary>
        public bool FlatTop => _flatTop;

        /// <summary>A tilted physics-BSP part, stamped by its own physics at walking height (<see cref="Hits"/>) instead of its box.</summary>
        public bool ByPhysics => _part is not null;

        public bool Valid => MinX <= MaxX && MinY <= MaxY && ZLow <= ZHigh && float.IsFinite(MinX + MaxX + MinY + MaxY + ZLow + ZHigh);

        public static Prim Box(PhysPart part, Vector3 offset, bool tiltedByPhysics = false)
        {
            var bounds = part.Shape.Bounds;
            if (bounds.Min.X > bounds.Max.X || bounds.Min.Y > bounds.Max.Y || bounds.Min.Z > bounds.Max.Z)
            {
                return Empty;
            }

            var frame = part.Position.Frame;
            var scale = part.GfxObjScale;
            var lo = Vector3.Min(bounds.Min * scale, bounds.Max * scale);
            var hi = Vector3.Max(bounds.Min * scale, bounds.Max * scale);
            var origin = frame.Origin + offset;
            var q = frame.Orientation;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (var c = 0; c < 8; c++)
            {
                var corner = new Vector3((c & 1) == 0 ? lo.X : hi.X, (c & 2) == 0 ? lo.Y : hi.Y, (c & 4) == 0 ? lo.Z : hi.Z);
                var w = origin + Vector3.Transform(corner, q);
                minX = MathF.Min(minX, w.X);
                maxX = MathF.Max(maxX, w.X);
                minY = MathF.Min(minY, w.Y);
                maxY = MathF.Max(maxY, w.Y);
                minZ = MathF.Min(minZ, w.Z);
                maxZ = MathF.Max(maxZ, w.Z);
            }

            // Turned about the vertical only (x and y of the quaternion are 0): the rectangle is exact and so is its z span.
            var yawOnly = MathF.Abs(q.X) + MathF.Abs(q.Y) < 1e-4f;

            // Tilted with a physics shape (M6 4 w5, TODO(nav-static-footprint)): the world box of a tilted rock is far larger than the rock at walking height (the island's
            // arches, model 0x020007D9: a 20 x 20 m box round the portal they frame), so the part is asked node by node, as the physics would place the body (Hits).
            var byPhysics = tiltedByPhysics && !yawOnly && part.Shape.Physics is { Polygons.Count: > 0 };
            return new Prim(false, yawOnly, origin, Quaternion.Conjugate(q), lo, hi, 0f, minX, maxX, minY, maxY, yawOnly ? origin.Z + lo.Z : minZ, yawOnly ? origin.Z + hi.Z : maxZ, byPhysics ? part : null, offset, flatTop: yawOnly && !byPhysics);
        }

        public static Prim Cylinder(PhysStaticObject obj, PhysCylSphere cyl, Vector3 offset)
        {
            var frame = obj.Position.Frame;
            var centre = frame.Origin + offset + Vector3.Transform(cyl.LowPoint * obj.Scale, frame.Orientation);
            var r = cyl.Radius * obj.Scale;
            return new Prim(true, false, centre, Quaternion.Identity, default, default, r, centre.X - r, centre.X + r, centre.Y - r, centre.Y + r, centre.Z, centre.Z + (cyl.Height * obj.Scale), flatTop: true);
        }

        public static Prim Ball(PhysStaticObject obj, PhysSphere sphere, Vector3 offset)
        {
            var frame = obj.Position.Frame;
            var centre = frame.Origin + offset + Vector3.Transform(sphere.Center * obj.Scale, frame.Orientation);
            var r = sphere.Radius * obj.Scale;
            return new Prim(true, false, centre, Quaternion.Identity, default, default, r, centre.X - r, centre.X + r, centre.Y - r, centre.Y + r, centre.Z - r, centre.Z + r);
        }

        /// <summary>
        /// Whether a body of the inflation radius standing on the ground (<paramref name="ground"/>) at (cx, cy) touches the shape: by the part's physics for a
        /// tilted physics-BSP part (<see cref="ByPhysics"/>), else by <see cref="Reaches"/>.
        /// </summary>
        /// <remarks>
        /// The body is a stack of spheres of the inflation radius whose union spans the walking band, from <see cref="NavBuildOptions.DecalHeight"/> over the
        /// ground to <see cref="NavBuildOptions.OverheadClearance"/> (the band the box stamp keeps: a decal under it, an arch over it), spaced at most one
        /// radius apart; each is placed against the part with <see cref="PhysPart.PlacementCollides"/> (the placement test of retail's
        /// <c>BSPTREE::find_collisions</c>, 11.6096 0x0053B1A0): a sphere that touches a physics polygon, or whose centre is inside the solid, blocks the node.
        /// So a node beside the rock's foot, under its overhang above the band, or between its legs stays open, and one inside the rock is blocked.
        /// </remarks>
        public bool Hits(float cx, float cy, float ground, float infl2, float infl, NavBuildOptions o)
        {
            if (_part is not { } part)
            {
                return Reaches(cx, cy, infl2, infl);
            }

            // The cheap reject first: outside the world box inflated, nothing to ask.
            if (cx < MinX - infl || cx > MaxX + infl || cy < MinY - infl || cy > MaxY + infl)
            {
                return false;
            }

            return BodyCollides(part, _offset, cx, cy, ground, infl, o);
        }

        /// <summary>Whether a body of the inflation radius (<paramref name="infl"/>, squared <paramref name="infl2"/>) at (cx, cy) touches the shape.</summary>
        public bool Reaches(float cx, float cy, float infl2, float infl)
        {
            if (_circle)
            {
                var dx = cx - _origin.X;
                var dy = cy - _origin.Y;
                var reach = _radius + infl;
                return (dx * dx) + (dy * dy) <= reach * reach;
            }

            if (!_oriented)
            {
                // A tilted part without a physics shape (a building's bounds): its world box, inflated square (conservative).
                return cx >= MinX - infl && cx <= MaxX + infl && cy >= MinY - infl && cy <= MaxY + infl;
            }

            var q = Vector3.Transform(new Vector3(cx - _origin.X, cy - _origin.Y, 0f), _inverse);
            var ex = MathF.Max(MathF.Max(_lo.X - q.X, q.X - _hi.X), 0f);
            var ey = MathF.Max(MathF.Max(_lo.Y - q.Y, q.Y - _hi.Y), 0f);
            return (ex * ex) + (ey * ey) <= infl2;
        }
    }
}
