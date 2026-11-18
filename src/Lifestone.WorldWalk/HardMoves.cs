using System.Numerics;
using System.Runtime.Versioning;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Grounds;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.WorldWalk;

/// <summary>
/// W7's moves: the awkward single steps (into a wall, up a slope, over a landblock edge, through a doorway the map opens or closes) of a sample of landblocks, each placed and stepped through our physics with ACE's switches on.
/// tools/ace-compare runs the same list through ACE's own physics and lists where the two differ. Physics is shared by the planner, the oracle and the walker, so nothing else here could show a fault in it.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class HardMoves
{
    private const int BlockedEdgeMoves = 8, SteepMoves = 4, BorderMoves = 2, DoorMoves = 8;

    /// <summary>A step's length: about a body's width, the room walks' crossing try either side of a sill. Not retail: the walk's own.</summary>
    private const float StepMetres = 1.4f;

    /// <summary>A body is placed this far over the ground: exactly on a floor polygon it is refused (the room walks' first trial).</summary>
    private const float RaiseMetres = 0.05f;

    /// <summary>How far to one side the off-plane twin of a doorway step starts. Not retail: the walk's own; far more than float noise, far less than anything a body would notice.</summary>
    private const float OffPlaneMetres = 0.013f;

    /// <summary>A repeatable sample: half landblocks with rooms, half of the main landmass, in id order.</summary>
    public static IReadOnlyList<LandblockCoord> Pick(string censusRun, HashSet<(int X, int Y)> land, int seed, int sample)
    {
        var rooms = RoomWalks.Pick(censusRun).ToArray();
        new Random(seed).Shuffle(rooms);
        var ground = LongWalks.Pick(land, seed, sample / 2);
        return [.. rooms.Take(sample / 2).Concat(ground).Distinct().OrderBy(c => c.X).ThenBy(c => c.Y)];
    }

    public static object Run(WalkContext context, LandblockCoord coord, int seed)
    {
        var world = new PhysicsWorld(PhysicsCompat.Ace);
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (coord.Offset(dx, dy) is { } near && LandDefs.IsOnMap(near))
                {
                    world.Attach(context.Store.Get(near));
                }
            }
        }

        var walker = OfflineWalker.Create(context.Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(OfflinePlayerWeenie.PlaceholderRunSkill), context.Geometry, context.Animations);
        world.UseTime(0.0);
        var rng = new Random((seed * 65599) + (coord.X * 256) + coord.Y);
        var moves = new List<object>();

        void Do(string kind, uint cell, Vector3 at, Vector2 way)
        {
            var from = new CellFrame(cell, new AcFrame(at, Quaternion.Identity));
            if (!walker.Place(from))
            {
                moves.Add(new { kind, cell, x = at.X, y = at.Y, z = at.Z, dx = way.X, dy = way.Y, placed = false });
                return;
            }

            var start = walker.Position;
            var step = new Vector3(way * StepMetres, 0f);
            var ok = walker.Physics.TryTransitionTo(new CellFrame(start.CellId, new AcFrame(start.Origin + step, start.Frame.Orientation)));
            var end = walker.Position;
            moves.Add(new
            {
                kind,
                cell,
                x = at.X,
                y = at.Y,
                z = at.Z,
                dx = way.X,
                dy = way.Y,
                placed = true,
                start_cell = start.CellId,
                sx = start.Origin.X,
                sy = start.Origin.Y,
                sz = start.Origin.Z,
                ok,
                end_cell = end.CellId,
                ex = end.Origin.X,
                ey = end.Origin.Y,
                ez = end.Origin.Z,
                ground = walker.OnGround,
            });
        }

        Ground(context.Tile(coord, out _), rng, Do);
        Doors(context, coord, rng, Do);
        return new { id = Census.IdOf(coord), moves };
    }

    private static void Ground(NavTile tile, Random rng, Action<string, uint, Vector3, Vector2> act)
    {
        var edges = new List<(int X, int Y, int Dx, int Dy)>();
        var steep = new List<(int X, int Y)>();
        var border = new List<(int X, int Y, int Dx, int Dy)>();
        foreach (var (x, y) in Nodes(tile))
        {
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var (nx, ny) = (x + dx, y + dy);
                if (!NavTile.InBounds(nx, ny))
                {
                    border.Add((x, y, dx, dy));
                }
                else if (!tile.IsWalkable(nx, ny))
                {
                    edges.Add((x, y, dx, dy));
                }
            }

            if (tile.SlopeCost[NavTile.Index(x, y)] > 0)
            {
                steep.Add((x, y));
            }
        }

        void Act(string kind, int x, int y, int dx, int dy)
        {
            var p = tile.NodePoint(x, y);
            var at = p with { Z = p.Z + RaiseMetres };
            act(kind, GroundDraft.OutdoorCell(tile.LandblockId, at), at, new Vector2(dx, dy));
        }

        foreach (var e in Some(edges, BlockedEdgeMoves, rng))
        {
            Act("wall-edge", e.X, e.Y, e.Dx, e.Dy);
        }

        foreach (var e in Some(border, BorderMoves, rng))
        {
            Act("landblock-edge", e.X, e.Y, e.Dx, e.Dy);
        }

        foreach (var s in Some(steep, SteepMoves, rng))
        {
            var up = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.Where(d => NavTile.InBounds(s.X + d.Item1, s.Y + d.Item2)).OrderByDescending(d => tile.Z[NavTile.Index(s.X + d.Item1, s.Y + d.Item2)]).First();
            Act("slope", s.X, s.Y, up.Item1, up.Item2);
        }
    }

    private static IEnumerable<(int X, int Y)> Nodes(NavTile tile)
    {
        for (var y = 0; y < NavTile.Size; y++)
        {
            for (var x = 0; x < NavTile.Size; x++)
            {
                if (tile.IsWalkable(x, y) && (tile.Flags[NavTile.Index(x, y)] & NavFlags.Water) == 0)
                {
                    yield return (x, y);
                }
            }
        }
    }

    private static void Doors(WalkContext context, LandblockCoord coord, Random rng, Action<string, uint, Vector3, Vector2> act)
    {
        var cells = LandblockCellSet.Load(context.Structures, coord);
        if (cells.Count == 0)
        {
            return;
        }

        var graph = new RoomGraph(cells, RoomGraph.BuildingDoorwayLean);
        var doors = new List<(uint Cell, Vector3 At, Vector2 Way, string Kind)>();
        foreach (var cellId in cells.Ids.Order())
        {
            var cell = cells.GetCell(cellId)!;
            if (graph.RoomFrame(cellId) is not { } room)
            {
                continue;
            }

            foreach (var portal in cell.Portals)
            {
                if (!portal.LeadsOutside && cells.GetCell(portal.OtherCellId) is not null && graph.Planner.DoorwayOf(cell, portal) is { } door)
                {
                    var at = new Vector3(door.Centre.X, door.Centre.Y, room.Origin.Z + RaiseMetres) - (door.Outward * (StepMetres / 2f));
                    doors.Add((cellId, at, new Vector2(door.Outward.X, door.Outward.Y), graph.IsPassable(cell, portal) ? "door-open" : "door-closed"));
                }
            }
        }

        foreach (var d in Some(doors, DoorMoves, rng))
        {
            act(d.Kind, d.Cell, d.At, d.Way);

            // The same step 13 mm to one side, off any plane the doorway's own centre lies exactly on: docs/third-party/physics.md row 19 says ACE and retail part ways only for a centre exactly on a splitting plane, and this tells that case from a real difference.
            act(d.Kind + "-off", d.Cell, d.At + new Vector3(-d.Way.Y * OffPlaneMetres, d.Way.X * OffPlaneMetres, 0f), d.Way);
        }
    }

    private static List<T> Some<T>(List<T> all, int count, Random rng)
    {
        var copy = all.ToArray();
        rng.Shuffle(copy);
        return [.. copy.Take(count)];
    }
}
