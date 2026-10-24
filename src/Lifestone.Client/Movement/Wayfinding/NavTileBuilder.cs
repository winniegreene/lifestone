using System.Diagnostics;
using System.Globalization;
using System.Numerics;
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

    /// <summary>The step-up in metres (the human Setup's). The builder only uses it to call a low, small static tiny; the planner reads the edge rule (a rise over this blocks the edge, a drop to 2 m is costed) from the tile's z. Lifestone's.</summary>
    public float StepUp { get; init; } = NavMap.StepUpMetres;

    /// <summary>The normal z at which the slope ramp ends (no cost above it); the ramp runs from <c>PhysicsGlobals.FloorZ</c> to here. Lifestone's.</summary>
    public float SlopeRampEnd { get; init; } = 0.85f;

    /// <summary>A doorway lane's width in metres. Lifestone's.</summary>
    public float LaneWidth { get; init; } = 1.5f;

    /// <summary>A doorway lane's length in metres, from the sill out. Lifestone's.</summary>
    public float LaneLength { get; init; } = 2.5f;

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

    /// <summary>Statics left to the room graph: their cell is an EnvCell, not an outdoor cell.</summary>
    public int StaticsIndoor { get; set; }

    /// <summary>Statics wholly below the ground at their origin (or within <see cref="NavBuildOptions.DecalHeight"/> of it).</summary>
    public int StaticsBelowGround { get; set; }

    /// <summary>Statics under <see cref="NavBuildOptions.TinySize"/> and the step-up: stepped over, not stamped.</summary>
    public int StaticsTiny { get; set; }

    /// <summary>Statics that stamped no node (overhead, or between nodes after the checks).</summary>
    public int StaticsNoNodes { get; set; }

    /// <summary>Buildings blocked.</summary>
    public int BuildingsStamped { get; set; }

    /// <summary>Buildings whose shell has no collision polygons: blocked by their bounding box instead of their footprint.</summary>
    public int BuildingsByBounds { get; set; }

    /// <summary>Doorway lanes built.</summary>
    public int LanesBuilt { get; set; }

    /// <summary>Building portals skipped: a standing human cannot pass the entry cell's outside portal polygon.</summary>
    public int PortalsImpassable { get; set; }

    /// <summary>Building portals skipped: the entry cell, its outside portal or the polygon is missing.</summary>
    public int PortalsNoGeometry { get; set; }

    /// <summary>Building portals skipped: the lane lies wholly off this tile.</summary>
    public int PortalsOffTile { get; set; }

    /// <summary>Nodes with no terrain polygon (blocked): the whole tile when the landblock has no LandBlock record.</summary>
    public int NodesWithoutTerrain { get; set; }

    /// <summary>Stamps that found the obstacle list full (65,534 entries): their nodes have no owner.</summary>
    public int ObstacleOverflow { get; set; }

    /// <summary>The build's wall-clock time (also <see cref="NavTile.BuildMilliseconds"/>).</summary>
    public double Milliseconds { get; set; }

    /// <summary>The build's log line.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"navtile ms={Milliseconds:F1} statics={StaticsStamped} shadows={ShadowsStamped} buildings={BuildingsStamped}(by_bounds={BuildingsByBounds}) lanes={LanesBuilt} "
        + $"skipped(indoor={StaticsIndoor} below_ground={StaticsBelowGround} tiny={StaticsTiny} no_nodes={StaticsNoNodes}) "
        + $"portals_skipped(impassable={PortalsImpassable} no_geometry={PortalsNoGeometry} off_tile={PortalsOffTile}) no_terrain={NodesWithoutTerrain} owner_overflow={ObstacleOverflow}");
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
/// corner flags all its nodes <see cref="NavFlags.Water"/> (conservative: the finer corner data is private to the
/// collision set, TODO(nav-water-corner)).</para>
/// <para><b>Statics.</b> Retail tests a static's physics-BSP parts when it has any, else its cylinder-spheres, else its
/// spheres (<c>CPhysicsObj::FindObjCollisions</c>, 11.6096 0x0050FB20); the same order picks what is stamped. A part is its
/// bounding box (scaled, in its frame: an oriented rectangle when the part is only turned about the vertical, else the
/// world box), a cylinder-sphere or sphere a circle; each is inflated by body radius + margin (0.58 m) with rounded
/// corners. <b>A static's top is never walkable</b> (a named conservatism): a wall, step or crate lower than the step-up
/// still blocks, though the real body could climb it; the physics confirmation decides the rare case. A node is not
/// blocked by a static whose top is within <see cref="NavBuildOptions.DecalHeight"/> of the ground there (a decal) or
/// whose underside is <see cref="NavBuildOptions.OverheadClearance"/> or more above it (an arch). A static in an EnvCell
/// (a room's furniture) belongs to the room graph and is counted skipped; one wholly below the ground, or tiny, is
/// counted skipped too.</para>
/// <para><b>Buildings.</b> A building's footprint is blocked ("building"): the union of its shell's collision polygons
/// projected on the ground, inflated like a static (its bounding box, <c>PhysBuilding.Part.Shape.Bounds</c>, only when the
/// shell has no collision polygons, counted): the box over-blocks every terraced or L-shaped building so far that a
/// door ends up an island inside it. Then each doorway lane
/// is cut: the building's portal names the entry EnvCell and its outside portal polygon; if a standing human can pass
/// the polygon (the four probes <c>CellRoutePlanner.IsWalkable</c> uses), a strip <see cref="NavBuildOptions.LaneWidth"/>
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
        }

        foreach (var building in block.Buildings)
        {
            StampBuilding(tile, building, infl, o, stats);
        }

        for (var i = 0; i < block.Buildings.Count; i++)
        {
            CutDoorwayLanes(tile, block, i, o, stats);
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
    }

    private static void StampStatic(NavTile tile, PhysStaticObject obj, Vector3 offset, string kind, NavFlags flag, float infl, NavBuildOptions o, List<Prim> prims, NavBuildStats stats)
    {
        if (!LandDefs.IsOutdoorCell(obj.Position.CellId))
        {
            stats.StaticsIndoor++;
            return;
        }

        prims.Clear();
        if ((obj.State & PhysicsState.HasPhysicsBsp) != 0)
        {
            foreach (var part in obj.Parts)
            {
                if (part.Shape.Physics is not null)
                {
                    prims.Add(Prim.Box(part, offset));
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
            stats.StaticsNoNodes++;
            return;
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
            stats.StaticsTiny++;
            return;
        }

        var origin = obj.Position.Frame.Origin + offset;
        if (NavTile.InBounds((int)MathF.Floor(origin.X), (int)MathF.Floor(origin.Y))
            && top <= tile.Z[NavTile.Index((int)MathF.Floor(origin.X), (int)MathF.Floor(origin.Y))] + o.DecalHeight)
        {
            stats.StaticsBelowGround++;
            return;
        }

        var made = MakeOwner(tile, new NavObstacle(obj.ModelId, origin, kind), stats, out var owner);
        var marked = 0;
        foreach (var p in prims)
        {
            marked += Stamp(tile, p, owner, flag, infl, o);
        }

        if (marked == 0)
        {
            if (made)
            {
                tile.Obstacles.RemoveAt(tile.Obstacles.Count - 1);
            }

            stats.StaticsNoNodes++;
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

    private static void StampBuilding(NavTile tile, PhysBuilding building, float infl, NavBuildOptions o, NavBuildStats stats)
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

                marked += StampPolygon(tile, xy[..vertices.Length], top, owner, infl, o);
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
    private static int StampPolygon(NavTile tile, ReadOnlySpan<Vector2> xy, float top, ushort owner, float infl, NavBuildOptions o)
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

                Mark(tile, i, owner, NavFlags.Building);
                marked++;
            }
        }

        return marked;
    }

    private static bool InsidePolygon(ReadOnlySpan<Vector2> xy, float px, float py)
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
    private static int Stamp(NavTile tile, in Prim p, ushort owner, NavFlags kind, float infl, NavBuildOptions o)
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
                if (!p.Reaches(cx, cy, infl2, infl))
                {
                    continue;
                }

                Mark(tile, i, owner, kind);
                marked++;
            }
        }

        return marked;
    }

    private static void CutDoorwayLanes(NavTile tile, LandblockCollision block, int buildingIndex, NavBuildOptions o, NavBuildStats stats)
    {
        var building = block.Buildings[buildingIndex];
        foreach (var portal in building.Portals)
        {
            if (!block.EnvCells.TryGetValue(portal.OtherCellId, out var cell)
                || (uint)portal.OtherPortalIndex >= (uint)cell.Portals.Count)
            {
                stats.PortalsNoGeometry++;
                continue;
            }

            var outside = cell.Portals[portal.OtherPortalIndex];
            if (!outside.LeadsOutside || outside.Vertices is not { Length: >= 3 } vertices)
            {
                stats.PortalsNoGeometry++;
                continue;
            }

            var frame = cell.Position.Frame;
            var normal = frame.LocalToGlobalVec(outside.Plane.Normal);
            if (normal.LengthSquared() < 1e-8f || MathF.Abs(Vector3.Normalize(normal).Z) > 0.1f)
            {
                stats.PortalsImpassable++;
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

            // The passability probes of CellRoutePlanner.IsWalkable (the human's width and height through the polygon).
            var along = Vector3.Normalize(new Vector3(-normal.Y, normal.X, 0f));
            var floor = new Vector3(centre.X, centre.Y, sill);
            if (!(InPortal(corners, along, floor + new Vector3(0, 0, 0.2f))
                && InPortal(corners, along, floor + new Vector3(0, 0, 1.8f))
                && InPortal(corners, along, floor + new Vector3(0, 0, 1f) + (along * o.BodyRadius))
                && InPortal(corners, along, floor + new Vector3(0, 0, 1f) - (along * o.BodyRadius))))
            {
                stats.PortalsImpassable++;
                continue;
            }

            // The neighbour (the outdoors) lies along the normal when the cell is on the negative side (PortalSide).
            var into = outside.PortalSide ? normal : -normal;
            var outward = new Vector2(into.X, into.Y);
            outward = Vector2.Normalize(outward);
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
                continue;
            }

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

            tile.DoorwayLanes.Add(new NavDoorwayLane(buildingIndex, portal.OtherCellId, new Vector3(centre.X, centre.Y, sill), outward, nodes.ToArray()));
            stats.LanesBuilt++;
        }
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

        private Prim(bool circle, bool oriented, Vector3 origin, Quaternion inverse, Vector3 lo, Vector3 hi, float radius, float minX, float maxX, float minY, float maxY, float zLow, float zHigh)
        {
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

        public bool Valid => MinX <= MaxX && MinY <= MaxY && ZLow <= ZHigh && float.IsFinite(MinX + MaxX + MinY + MaxY + ZLow + ZHigh);

        public static Prim Box(PhysPart part, Vector3 offset)
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
            return new Prim(false, yawOnly, origin, Quaternion.Conjugate(q), lo, hi, 0f, minX, maxX, minY, maxY, yawOnly ? origin.Z + lo.Z : minZ, yawOnly ? origin.Z + hi.Z : maxZ);
        }

        public static Prim Cylinder(PhysStaticObject obj, PhysCylSphere cyl, Vector3 offset)
        {
            var frame = obj.Position.Frame;
            var centre = frame.Origin + offset + Vector3.Transform(cyl.LowPoint * obj.Scale, frame.Orientation);
            var r = cyl.Radius * obj.Scale;
            return new Prim(true, false, centre, Quaternion.Identity, default, default, r, centre.X - r, centre.X + r, centre.Y - r, centre.Y + r, centre.Z, centre.Z + (cyl.Height * obj.Scale));
        }

        public static Prim Ball(PhysStaticObject obj, PhysSphere sphere, Vector3 offset)
        {
            var frame = obj.Position.Frame;
            var centre = frame.Origin + offset + Vector3.Transform(sphere.Center * obj.Scale, frame.Orientation);
            var r = sphere.Radius * obj.Scale;
            return new Prim(true, false, centre, Quaternion.Identity, default, default, r, centre.X - r, centre.X + r, centre.Y - r, centre.Y + r, centre.Z - r, centre.Z + r);
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
                // A tilted part: its world box, inflated square (conservative).
                return cx >= MinX - infl && cx <= MaxX + infl && cy >= MinY - infl && cy <= MaxY + infl;
            }

            var q = Vector3.Transform(new Vector3(cx - _origin.X, cy - _origin.Y, 0f), _inverse);
            var ex = MathF.Max(MathF.Max(_lo.X - q.X, q.X - _hi.X), 0f);
            var ey = MathF.Max(MathF.Max(_lo.Y - q.Y, q.Y - _hi.Y), 0f);
            return (ex * ex) + (ey * ey) <= infl2;
        }
    }
}
