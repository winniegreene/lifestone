using System.Globalization;
using System.Numerics;
using System.Runtime.Versioning;
using System.Text.Json;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.WorldWalk;

/// <summary>
/// The rooms (W5). One physics world for each landblock with rooms. Every doorway is walked both ways, each leg from the first room's standing place to the second's, placed afresh so that one failure
/// cannot colour the next. Every way out of a building is walked out and back through <see cref="IndoorOutdoorPathSource"/>, the source the journeys pick for a room and the land.
/// Doors, levers and barriers are server objects and are not here: a closed door is W9's.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class RoomWalks
{
    /// <summary>A leg's limit in simulated seconds, beyond its length. Not retail: the walk's own.</summary>
    private const double LegSecondsBase = 30;

    /// <summary>How long a leg may take per metre of its straight line, at the walk's slowest fair pace. Not retail: the walk's own.</summary>
    private const double LegSecondsPerMetre = 1.0;

    /// <summary>A walk to or from the land: the limit, as the outdoor walks allow. Not retail: the walk's own.</summary>
    private const double ExitSecondsLimit = 120;

    private const double Fps = 60.0;

    /// <summary>How far outside a doorway's sill the walk's outdoor end lies. Not retail: the walk's own.</summary>
    private const float ExitOutsideMetres = 6f;

    /// <summary>How many nodes from that point a walkable node is looked for.</summary>
    private const int ExitSearchNodes = 6;

    /// <summary>A sill this far over the land outside is no door a body walks to: a window or a balcony. Not retail: the walk's own.</summary>
    private const float HighSillMetres = 1.5f;

    /// <summary>
    /// The directions a body is walked at a door that failed, by the body alone (<see cref="BodyAtDoor"/>): in degrees from the door's outward direction, straight out first;
    /// the sideways ones find a stair or a ramp that runs along the wall. Not retail: the walk's own.
    /// </summary>
    private static readonly int[] DoorFanDegrees = [0, 45, -45, 80, -80];

    /// <summary>How far from the door's outer approach the body's ground end lies, along each of <see cref="DoorFanDegrees"/>. Not retail: the walk's own.</summary>
    private static readonly float[] DoorFanMetres = [2.5f, 4.5f];

    /// <summary>How far inside the sill the body's room end lies. Not retail: the walk's own.</summary>
    private const float DoorInsideMetres = 1f;

    /// <summary>A walk of the body alone at a door: its limit in simulated seconds (the walk is a few metres). Not retail: the walk's own.</summary>
    private const double DoorWalkSeconds = 10;

    private const string BodyWalks = "walks";
    private const string BodyInOnly = "in-only";
    private const string BodyDropOnly = "drop-only";
    private const string BodyRecoveryOnly = "recovery-only";
    private const string BodyNone = "none";

    /// <summary>How far either side of a closed doorway a body is tried, as the room graph's approach. Not retail: the walk's own.</summary>
    private const float CrossingTryMetres = 0.7f;

    /// <summary>Where round a floor's centre a body is tried, in order: the centre first. A room's frame is a goal in the game, where a body comes to rest near it; a start needs a place a body fits. Not retail: the walk's own.</summary>
    private static readonly Vector3[] Nudges =
    [
        Vector3.Zero, new(0, 0, 0.05f), new(0, 0, 0.25f),
        new(0.3f, 0, 0.05f), new(-0.3f, 0, 0.05f), new(0, 0.3f, 0.05f), new(0, -0.3f, 0.05f),
        new(0.75f, 0, 0.05f), new(-0.75f, 0, 0.05f), new(0, 0.75f, 0.05f), new(0, -0.75f, 0.05f),
    ];

    /// <summary>Where a body is tried farther out, only for a room where none of <see cref="Nudges"/> holds a body at all. Not retail: the walk's own.</summary>
    private static readonly Vector3[] FarNudges = [.. Ring(1.25f), .. Ring(2f)];

    /// <summary>How far the route check's ground probe may find the floor from a standing place's own height (<see cref="Stand"/>). Not retail: the walk's own.</summary>
    private const float StandFloorMetres = 0.3f;

    /// <summary>Every landblock the census found rooms in, in id order, so neighbours stay in the collision store.</summary>
    public static IReadOnlyList<LandblockCoord> Pick(string censusRun)
    {
        var found = new List<ushort>();
        foreach (var line in File.ReadLines(Path.Combine(censusRun, "results.jsonl")))
        {
            using var row = JsonDocument.Parse(line);
            var census = row.RootElement.GetProperty("row");
            if (census.GetProperty("EnvCells").GetInt32() > 0)
            {
                found.Add(ushort.Parse(census.GetProperty("Id").GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
        }

        found.Sort();
        return [.. found.Select(id => new LandblockCoord((byte)(id >> 8), (byte)(id & 0xFF)))];
    }

    /// <summary>Walks one landblock's rooms.</summary>
    public static object Run(WalkContext context, OfflineWalkRig rig, LandblockCoord coord)
    {
        var id = Census.IdOf(coord);
        var cells = LandblockCellSet.Load(context.Structures, coord);
        if (cells.Count == 0)
        {
            return new { id, skipped = "no rooms" };
        }

        // The buildings' lean is what the journeys' indoor-outdoor source gives every room outside a named ground. One graph, shared by the legs, leaves no cell out: a refusal is a finding here.
        var graph = new RoomGraph(cells, RoomGraph.BuildingDoorwayLean);
        var rooms = new Dictionary<uint, CellFrame>();
        var noFrame = new List<string>();
        foreach (var cellId in cells.Ids)
        {
            if (graph.RoomFrame(cellId) is { } frame)
            {
                rooms[cellId] = frame;
            }
            else
            {
                noFrame.Add(Short(cellId));
            }
        }

        var doorways = Survey(cells, graph, rooms, out var pairs, out var closed, out var crossBlock);
        var legs = WalkLegs(context, coord, graph, rooms, pairs, closed);
        var (exits, exitBad, exitKinds, exitNatural) = WalkExits(context, rig, coord, cells, graph, legs.Standing);
        var joined = Groups(rooms.Keys, pairs.Select(p => (p.From, p.To)));
        var walked = Groups(rooms.Keys, legs.Arrived);
        return new
        {
            id,
            cells = cells.Count,
            rooms = rooms.Count,
            no_frame = noFrame,
            no_stand = legs.NoStand,
            placed_elsewhere = legs.Elsewhere,
            no_floor_probe = legs.NoFloorProbe,
            doors_short = legs.DoorsShort,
            nudged = legs.Nudged,
            doorways,
            passable = pairs.Count,
            closed = legs.ClosedCauses,
            closed_tried = legs.ClosedTried,
            closed_crossed = legs.ClosedCrossed,

            // Every closed doorway by name, so a known defect can be looked up; the flat portals are left out (they are over half of them, and a portal with no doorway shape says nothing about one room).
            closed_doors = closed.Where(c => c.Cause != "flat-portal").Select(c => new { from = Short(c.Cell.Id), to = Short(c.Portal.OtherCellId), cause = c.Cause }).ToArray(),
            cross_block = crossBlock,
            legs = legs.Walked,
            clean = legs.Clean,
            groups_by_map = joined,
            groups_by_walk = walked,
            exits,
            exit_kinds = exitKinds,
            bad = legs.Bad,
            exit_bad = exitBad,

            // A door with no lane that the body alone cannot walk both ways (only by a drop, only by a recovery move, or not at all): the planner is right to leave it out (docs/world-walk.md "Doors the body cannot walk").
            exit_natural = exitNatural,
        };
    }

    private static string Short(uint cellId) => (cellId & 0xFFFF).ToString("X4", CultureInfo.InvariantCulture);

    /// <summary>Every room-to-room portal: the pairs to walk, those the graph closes, and those into another landblock.</summary>
    private static int Survey(LandblockCellSet cells, RoomGraph graph, Dictionary<uint, CellFrame> rooms, out List<(uint From, uint To)> pairs, out List<(EnvCellData Cell, CellPortalData Portal, string Cause)> closed, out List<string> crossBlock)
    {
        pairs = [];
        closed = [];
        crossBlock = [];
        var seen = new HashSet<(uint, uint)>();
        var doorways = 0;
        foreach (var cellId in cells.Ids)
        {
            var cell = cells.GetCell(cellId)!;
            foreach (var portal in cell.Portals)
            {
                if (portal.LeadsOutside)
                {
                    continue;
                }

                doorways++;
                if (cells.GetCell(portal.OtherCellId) is null)
                {
                    crossBlock.Add($"{Short(cellId)}>{portal.OtherCellId:X8}");
                }
                else if (!graph.IsPassable(cell, portal))
                {
                    var planner = graph.Planner;
                    closed.Add((cell, portal, planner.DoorwayOf(cell, portal) is null ? "flat-portal"
                        : !rooms.ContainsKey(portal.OtherCellId) ? "no-floor-beyond"
                        : planner.IsLedge(cell) || (cells.GetCell(portal.OtherCellId) is { } beyond && planner.IsLedge(beyond)) ? "ledge"
                        : !planner.Fits(cell, portal) ? "too-small"
                        : !planner.HasFloorsAtSill(cell, portal) ? "no-floor-at-sill"
                        : "other"));
                }
                else if (rooms.ContainsKey(cellId) && rooms.ContainsKey(portal.OtherCellId) && seen.Add((cellId, portal.OtherCellId)))
                {
                    pairs.Add((cellId, portal.OtherCellId));
                }
            }
        }

        // Two buildings' doors that open into each other (RoomGraph.PairedDoors) are walked as a doorway between their rooms.
        foreach (var (from, to) in graph.PairedDoors)
        {
            if (rooms.ContainsKey(from.CellId) && rooms.ContainsKey(to.CellId) && seen.Add((from.CellId, to.CellId)))
            {
                pairs.Add((from.CellId, to.CellId));
            }
        }

        return doorways;
    }

    private sealed record LegTally(int Walked, int Clean, List<object> Bad, List<(uint, uint)> Arrived, Dictionary<uint, CellFrame> Standing, List<string> NoStand, List<string> Elsewhere, Dictionary<string, int> Nudged, Dictionary<string, int> ClosedCauses, int ClosedTried, List<object> ClosedCrossed)
    {
        /// <summary>The rooms stood in where the route check's ground probe finds no floor (<see cref="Stand"/>).</summary>
        public List<string> NoFloorProbe { get; init; } = [];

        /// <summary>The rooms whose place does not reach every doorway the room is walked through, with how many it misses (<see cref="Stand"/>).</summary>
        public Dictionary<string, int> DoorsShort { get; init; } = [];
    }

    /// <summary>Whether a body can step through a doorway the graph closes: from just inside it to just past it, the true physics step, ending on the ground in the next room.</summary>
    private static bool Crosses(OfflineWalker walker, EnvCellData cell, CellPortalData portal, Doorway door, float roomZ, out float drop)
    {
        drop = 0f;
        var body = walker.Physics;
        foreach (var z in new[] { roomZ + 0.05f, door.Sill + 0.05f })
        {
            var near = new Vector3(door.Centre.X, door.Centre.Y, z) - (door.Outward * CrossingTryMetres);
            if (body.SetPosition(new CellFrame(cell.Id, new AcFrame(near, Quaternion.Identity)), SetPositionFlags.Placement) != SetPositionError.OK)
            {
                continue;
            }

            var start = body.Position;
            var to = new CellFrame(start.CellId, new AcFrame(start.Origin + (door.Outward * (CrossingTryMetres * 2f)), start.Frame.Orientation));
            if (body.TryTransitionTo(to, carriedContact: true) && walker.OnGround && body.Position.CellId == portal.OtherCellId)
            {
                drop = start.Origin.Z - body.Position.Origin.Z;
                return true;
            }
        }

        return false;
    }

    /// <summary>The place round the room's frame a body fits, stays in the room and can leave it by; null when none, with <paramref name="elsewhere"/> set when a body fitted only by leaving the room.</summary>
    /// <remarks>
    /// Of <see cref="Nudges"/>, the first place where the route check's own ground probe finds the floor too (within <see cref="StandFloorMetres"/>) and from which the ground leads to every doorway
    /// the room is walked through (<see cref="Floods"/>) is taken: a floor's centre can lie in a solid the placement does not catch (a long table, 0x2D5A0105), where the check rightly refuses every walk
    /// to or from it and the body still walks it, and a place beside the centre can lie in a nook the room's doorways cannot be reached from (behind a raised block, 0x80900102 and its 12 copies; between
    /// bookshelves, 0x00D00115). When no place near the centre reaches them all, <see cref="FarNudges"/> are tried too, and the place reaching the most is taken, the first of equals. With no place the
    /// probe finds near the centre, <see cref="FarNudges"/> are tried for one it does (a room round a solid at its centre, 0x89040106, where the body fitted only on top of it); with none there either,
    /// the first place the body fits is taken, and <paramref name="unprobed"/> says so (a sliver of a room a body overhangs, a sloping floor).
    /// <paramref name="short"/> is how many of the room's doorways the place taken does not reach.
    /// </remarks>
    private static (CellFrame Frame, int Nudge)? Stand(OfflineWalker walker, PhysicsWorld world, RoomGraph graph, IReadOnlyDictionary<uint, CellFrame> rooms, in CellFrame room, out bool elsewhere, out bool unprobed, out int @short)
    {
        elsewhere = false;
        unprobed = false;
        @short = 0;
        var foot = walker.Physics.Setup!.Spheres[0];
        var cell = graph.Cells.GetCell(room.CellId)!;
        var doors = Doors(graph, cell, rooms);
        var floods = new Floods(world, foot, graph.Cells, cell, room.Origin, doors);
        (CellFrame Frame, int Nudge)? fits = null;
        (CellFrame Frame, int Nudge, int Reached)? best = null;
        bool Probed(in CellFrame at, out float floor) => world.TryFixedGroundZ(at, foot, out floor, out _) && MathF.Abs(floor - at.Origin.Z) <= StandFloorMetres;
        for (var i = 0; i < Nudges.Length; i++)
        {
            var at = new CellFrame(room.CellId, new AcFrame(room.Origin + Nudges[i], Quaternion.Identity));
            if (walker.Place(at))
            {
                if (walker.Position.CellId == room.CellId)
                {
                    if (Probed(at, out var floor))
                    {
                        var reached = floods.Reach(at.Origin with { Z = floor });
                        if (reached == doors.Count)
                        {
                            return (at, i);
                        }

                        if (best is null || reached > best.Value.Reached)
                        {
                            best = (at, i, reached);
                        }

                        continue;
                    }

                    fits ??= (at, i);
                    continue;
                }

                elsewhere = true;
            }
        }

        (CellFrame Frame, int Nudge)? farFits = null;
        if (best is null || best.Value.Reached < doors.Count)
        {
            for (var i = 0; i < FarNudges.Length; i++)
            {
                var at = new CellFrame(room.CellId, new AcFrame(room.Origin + FarNudges[i], Quaternion.Identity));
                if (!walker.Place(at) || walker.Position.CellId != room.CellId)
                {
                    continue;
                }

                if (!Probed(at, out var floor))
                {
                    farFits ??= (at, Nudges.Length + i);
                    continue;
                }

                if (floods.Reach(at.Origin with { Z = floor }) is var reached && (best is null || reached > best.Value.Reached))
                {
                    best = (at, Nudges.Length + i, reached);
                    if (reached == doors.Count)
                    {
                        break;
                    }
                }
            }
        }

        if (best is { } taken)
        {
            @short = doors.Count - taken.Reached;
            return (taken.Frame, taken.Nudge);
        }

        if (fits is not null)
        {
            unprobed = true;
            return fits;
        }

        if (!elsewhere && farFits is not null)
        {
            unprobed = true;
            return farFits;
        }

        return null;
    }

    /// <summary>How far inside a doorway the walk looks for the ground leading to it. Not retail: the walk's own.</summary>
    private const float DoorwayInsideMetres = 0.5f;

    /// <summary>How near a doorway's inside point (<see cref="DoorwayInsideMetres"/>) the ground must lead, across. Not retail: the walk's own.</summary>
    private const float DoorReachMetres = 0.6f;

    /// <summary>The spacing of <see cref="Floods"/>' grid. Not retail: the walk's own.</summary>
    private const float FloodGridMetres = 0.25f;

    /// <summary>The spacing of the probes along a straight line to a doorway. Not retail: the walk's own.</summary>
    private const float LineSampleMetres = 0.1f;

    /// <summary>The most probes one room's floods make (a floor of 400 m² at <see cref="FloodGridMetres"/>). Not retail: the walk's own.</summary>
    private const int FloodProbeCap = 6400;

    /// <summary>The points just inside each doorway of <paramref name="cell"/> the walk crosses into a room with a place, at the sill's height.</summary>
    private static List<Vector3> Doors(RoomGraph graph, EnvCellData cell, IReadOnlyDictionary<uint, CellFrame> rooms)
    {
        var doors = new List<Vector3>();
        foreach (var portal in cell.Portals)
        {
            if (!portal.LeadsOutside && rooms.ContainsKey(portal.OtherCellId) && graph.Cells.GetCell(portal.OtherCellId) is not null && graph.IsPassable(cell, portal)
                && graph.Planner.DoorwayOf(cell, portal) is { } door)
            {
                var inside = door.Centre - (new Vector3(door.Outward.X, door.Outward.Y, 0f) * DoorwayInsideMetres);
                doors.Add(inside with { Z = door.Sill });
            }
        }

        return doors;
    }

    /// <summary>
    /// Which of a room's doorways the ground leads to from a place: the route check's own ground probe, each step at most <see cref="CellRoutePlanner.StepUpMetres"/> up or down from the one before,
    /// first along a straight line to each doorway, then, when a line fails, over the room's floor on a grid (<see cref="FloodGridMetres"/>), kept in the room. The grid's parts are shared by the places
    /// tried in one room. A gap narrower than the grid can be stepped over: the test only ever moves a place to one that reaches more.
    /// </summary>
    private sealed class Floods(PhysicsWorld world, PhysSphere foot, LandblockCellSet cells, EnvCellData cell, Vector3 anchor, List<Vector3> doors)
    {
        private static readonly (int X, int Y)[] Around = [(0, 0), (1, 0), (-1, 0), (0, 1), (0, -1)];

        private readonly Dictionary<(int X, int Y), (float Z, int Part)> _nodes = [];
        private readonly HashSet<(int X, int Y)> _closed = [];
        private readonly List<HashSet<int>> _reaches = [];
        private int _probes;

        /// <summary>How many of the doorways the ground leads to from <paramref name="from"/> (on its floor).</summary>
        public int Reach(Vector3 from)
        {
            if (doors.TrueForAll(door => Line(from, door)))
            {
                return doors.Count;
            }

            var near = Node(from);
            foreach (var (dx, dy) in Around)
            {
                if (_nodes.TryGetValue((near.X + dx, near.Y + dy), out var node) && MathF.Abs(node.Z - from.Z) <= CellRoutePlanner.StepUpMetres)
                {
                    return _reaches[node.Part].Count;
                }
            }

            foreach (var (dx, dy) in Around)
            {
                var key = (near.X + dx, near.Y + dy);
                if (!_nodes.ContainsKey(key) && Ground(key, from.Z) is { } z)
                {
                    return Flood(key, z);
                }
            }

            return 0;
        }

        private (int X, int Y) Node(Vector3 at) => ((int)MathF.Round((at.X - anchor.X) / FloodGridMetres), (int)MathF.Round((at.Y - anchor.Y) / FloodGridMetres));

        private Vector3 At((int X, int Y) key, float z) => new(anchor.X + (key.X * FloodGridMetres), anchor.Y + (key.Y * FloodGridMetres), z);

        private static bool Near(Vector3 at, Vector3 door) =>
            new Vector2(at.X - door.X, at.Y - door.Y).Length() <= DoorReachMetres && MathF.Abs(at.Z - door.Z) <= CellRoutePlanner.StepUpMetres;

        private float? Probe(Vector3 at)
        {
            _probes++;
            return world.TryFixedGroundZ(new CellFrame(cell.Id, new AcFrame(at, Quaternion.Identity)), foot, out var z, out _) && MathF.Abs(z - at.Z) <= CellRoutePlanner.StepUpMetres ? z : null;
        }

        private bool Line(Vector3 from, Vector3 door)
        {
            var across = new Vector2(door.X - from.X, door.Y - from.Y);
            var length = across.Length();
            var z = from.Z;
            for (var d = 0f; ; d += LineSampleMetres)
            {
                var at = length > 0f ? from + (new Vector3(across.X, across.Y, 0f) * (MathF.Min(d, length) / length)) : from;
                if (Near(at with { Z = z }, door))
                {
                    return true;
                }

                if (d >= length || Probe(at with { Z = z }) is not { } next)
                {
                    return false;
                }

                z = next;
            }
        }

        private float? Ground((int X, int Y) key, float fromZ)
        {
            if (_closed.Contains(key) || _probes >= FloodProbeCap)
            {
                return null;
            }

            var at = At(key, fromZ);
            if (!cells.Contains(cell, at + new Vector3(0f, 0f, 0.5f)) || Probe(at) is not { } z)
            {
                _closed.Add(key);
                return null;
            }

            return z;
        }

        private int Flood((int X, int Y) start, float z)
        {
            var part = _reaches.Count;
            var reach = new HashSet<int>();
            _reaches.Add(reach);
            _nodes[start] = (z, part);
            var queue = new Queue<(int X, int Y)>();
            queue.Enqueue(start);
            while (queue.Count > 0 && reach.Count < doors.Count)
            {
                var key = queue.Dequeue();
                var here = _nodes[key].Z;
                var at = At(key, here);
                for (var k = 0; k < doors.Count; k++)
                {
                    if (Near(at, doors[k]))
                    {
                        reach.Add(k);
                    }
                }

                foreach (var next in new[] { (key.X + 1, key.Y), (key.X - 1, key.Y), (key.X, key.Y + 1), (key.X, key.Y - 1) })
                {
                    if (!_nodes.ContainsKey(next) && Ground(next, here) is { } nz)
                    {
                        _nodes[next] = (nz, part);
                        queue.Enqueue(next);
                    }
                }
            }

            return reach.Count;
        }
    }

    /// <summary>Eight places round a floor's centre at <paramref name="metres"/>, 0.05 m up.</summary>
    private static Vector3[] Ring(float metres) =>
        [.. Enumerable.Range(0, 8).Select(k => new Vector3(metres * MathF.Cos(k * MathF.PI / 4f), metres * MathF.Sin(k * MathF.PI / 4f), 0.05f))];

    private static LegTally WalkLegs(WalkContext context, LandblockCoord coord, RoomGraph graph, Dictionary<uint, CellFrame> rooms, List<(uint From, uint To)> pairs, List<(EnvCellData Cell, CellPortalData Portal, string Cause)> closed)
    {
        var bad = new List<object>();
        var arrived = new List<(uint, uint)>();
        var standing = new Dictionary<uint, CellFrame>();
        var noStand = new List<string>();
        var placedElsewhere = new List<string>();
        var nudged = new Dictionary<string, int>();
        var noFloorProbe = new List<string>();
        var doorsShort = new Dictionary<string, int>();
        var clean = 0;
        var walkedLegs = 0;
        var time = new SimulatedTime();
        using var session = GameSession.Offline(_ => { }, time, NullLogSink.Instance);
        var world = new PhysicsWorld();
        world.Attach(context.Store.Get(coord));
        var now = 0.0;
        using var audit = new MovementAudit(new NullMovementSink(), session.Events, () => now, Census.IdOf(coord));
        var walker = OfflineWalker.Create(context.Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(OfflinePlayerWeenie.PlaceholderRunSkill), context.Geometry, context.Animations, sink: audit);
        session.Actions.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var source = new RoomPathSource(graph);
        foreach (var (cellId, frame) in rooms)
        {
            if (Stand(walker, world, graph, rooms, frame, out var elsewhere, out var unprobed, out var @short) is { } spot)
            {
                standing[cellId] = spot.Frame;
                if (@short > 0)
                {
                    doorsShort[Short(cellId)] = @short;
                }

                if (unprobed)
                {
                    noFloorProbe.Add(Short(cellId));
                }

                if (spot.Nudge != 0)
                {
                    nudged[Short(cellId)] = spot.Nudge;
                }
            }
            else
            {
                (elsewhere ? placedElsewhere : noStand).Add(Short(cellId));
            }
        }

        var causes = new Dictionary<string, int>();
        var crossed = new List<object>();
        var tried = 0;
        foreach (var (cell, portal, cause) in closed)
        {
            causes[cause] = causes.GetValueOrDefault(cause) + 1;
            if (graph.Planner.DoorwayOf(cell, portal) is { } door && standing.TryGetValue(cell.Id, out var near))
            {
                tried++;
                if (Crosses(walker, cell, portal, door, near.Origin.Z, out var drop))
                {
                    crossed.Add(new { from = Short(cell.Id), to = Short(portal.OtherCellId), cause, drop = Math.Round(drop, 2) });
                }
            }
        }

        (bool Done, NavRunState State, string? Reason, int Rungs, int Refusals, List<string> Why, float Route) Leg(CellFrame start, CellFrame goal, uint to, string name, RouteConfirmer? check)
        {
            walker.Place(start);
            using var runner = new NavRunner(session.Actions.Move, new ControllerNavBody(walker.Controller), source, check, audit, new NavRunnerOptions { Name = name, ScarClock = time, PredictiveSteering = context.PredictiveSteering }, NullLogSink.Instance);
            runner.Start(NavGoal.ToPoint(new NavPoint(to & 0xFFFF0000u, goal.Origin), to), now);

            // --trace-leg: this leg, walked where the run walks it (after the landblock's legs before it, from the same standing place and heading), traced step by step.
            using var traceOut = context.TraceLeg == name && context.TraceFolder is { } folder ? new StreamWriter(Path.Combine(folder, $"leg-{Census.IdOf(coord)}-{name}{(check is null ? "-nocheck" : string.Empty)}.txt")) : null;
            if (traceOut is not null)
            {
                NavRunner.PredictTrace = l => traceOut.WriteLine("    P " + l);
            }

            var end = now + LegSecondsBase + (Vector3.Distance(start.Origin, goal.Origin) * LegSecondsPerMetre);
            var why = new List<string>();
            var refusals = 0;
            object? lastRoute = null;
            while (!runner.IsDone && now < end)
            {
                if (traceOut is not null)
                {
                    var q = walker.Physics.Position;
                    var st = runner.SteeringState;
                    if (st.Route is { } r && !ReferenceEquals(r, lastRoute))
                    {
                        lastRoute = r;
                        traceOut.WriteLine("  route " + string.Join(" ", Enumerable.Range(0, r.Vertices.Count).Select(i => FormattableString.Invariant($"{i}:({r.Vertices[i].Global.X:F2},{r.Vertices[i].Global.Y:F2})"))));
                    }

                    traceOut.WriteLine(FormattableString.Invariant($"  S {now:F3} {q.Origin.X:F3} {q.Origin.Y:F3} {RetailFrames.GetHeading(q.Frame):F2} {st.Turn} {st.Forward} {walker.Physics.CollidingWithEnvironment} z={q.Origin.Z:F2} ground={walker.Controller.OnGround} cell={q.CellId & 0xFFFF:X4}"));
                }

                now += 1.0 / Fps;
                time.Advance(1.0 / Fps);
                walker.Controller.Tick(now);
                runner.Step(now);
                session.Tick(now);
                if (runner.ConfirmFailures != refusals)
                {
                    refusals = runner.ConfirmFailures;
                    why.Add(runner.LastConfirmFailure?.ToString() ?? "unknown");
                }
            }

            if (traceOut is not null)
            {
                traceOut.WriteLine(FormattableString.Invariant($"LEG {name} | {runner.State} rungs {runner.Rungs} replans {runner.Replans} route_m {runner.RouteMetres:F1} run_s {runner.RunSeconds:F1} keys {runner.KeyChanges} turns {runner.Turns} | {runner.SteeringFields()} predict_fallbacks={runner.PredictFallbacks} predict_handed={runner.PredictHandedSteps} |"));
                NavRunner.PredictTrace = null;
            }

            return (runner.IsDone, runner.State, runner.IsDone ? runner.FailureReason : null, runner.Rungs, refusals, why, runner.RouteMetres);
        }

        foreach (var (from, to) in pairs)
        {
            if (!standing.TryGetValue(from, out var start) || !standing.TryGetValue(to, out var goal))
            {
                bad.Add(new { from = Short(from), to = Short(to), state = "no-place-to-stand" });
                continue;
            }

            walkedLegs++;
            var name = $"{Short(from)}-{Short(to)}";
            var leg = Leg(start, goal, to, name, confirmer);
            var state = leg.Done ? leg.State.ToString() : "over-time";
            if (leg.Done && leg.State == NavRunState.Arrived)
            {
                arrived.Add((from, to));
                if (leg.Rungs == 0 && leg.Refusals == 0)
                {
                    clean++;
                    continue;
                }
            }

            // A leg the route check stopped never moved: without the check, the body says whether the check was right.
            string? without = null;
            var withoutRungs = 0;
            if (leg.Refusals > 0 && leg.State != NavRunState.Arrived)
            {
                var retry = Leg(start, goal, to, name + "-nocheck", null);
                without = retry.Done ? retry.State.ToString() : "over-time";
                withoutRungs = retry.Rungs;
            }

            bad.Add(new { from = Short(from), to = Short(to), state, reason = leg.Reason, rungs = leg.Rungs, refused = leg.Refusals, why = leg.Why, route_m = Math.Round(leg.Route, 1), without_check = without, without_check_rungs = withoutRungs });
        }

        return new LegTally(walkedLegs, clean, bad, arrived, standing, noStand, placedElsewhere, nudged, causes, tried, crossed) { NoFloorProbe = noFloorProbe, DoorsShort = doorsShort };
    }

    /// <summary>
    /// Each way out of the landblock's rooms, out and back, through the journeys' own source. Returns the number walked, what went wrong, the doors by kind and the doors the
    /// body alone cannot walk. A door that failed is walked by the body alone too (<see cref="BodyAtDoor"/>): a door with no lane that the body cannot walk both ways is the
    /// planner's right answer, not a failure, and goes to the natural list.
    /// </summary>
    private static (int Walks, List<object> Bad, Dictionary<string, int> Kinds, List<object> Natural) WalkExits(WalkContext context, OfflineWalkRig rig, LandblockCoord coord, LandblockCellSet cells, RoomGraph graph, Dictionary<uint, CellFrame> rooms)
    {
        var bad = new List<object>();
        var natural = new List<object>();
        var walks = 0;
        var kinds = new Dictionary<string, int>();
        var exits = graph.ExitsToOutside;
        if (exits.Count == 0)
        {
            return (0, bad, kinds, natural);
        }

        var block = context.Store.Get(coord);

        var tile = context.Tile(coord, out var tileStats);
        foreach (var exit in exits)
        {
            var cell = Short(exit.CellId);
            if (graph.IsPaired(exit))
            {
                // It opens into another building's door, with no ground between: its walk is the rooms' leg between the two (Survey).
                kinds["paired"] = kinds.GetValueOrDefault("paired") + 1;
                continue;
            }

            var from = "own";
            if (!rooms.TryGetValue(exit.CellId, out var room))
            {
                if (NearestStanding(cells, rooms, exit) is not { } near)
                {
                    bad.Add(new { cell, state = "no-standing-place" });
                    continue;
                }

                // The door's own room has no standing place (a vestibule under the door's frame whose spot lies in the next room, or a cell with no floor of its own): the walks start and end at
                // the nearest room through its openings that has one, so the door still gets its verdict.
                room = near;
                from = Short(near.CellId);
            }

            if (OutsidePoint(tile, exit) is not { } outside)
            {
                var aimAt = new Vector2(exit.Sill.X, exit.Sill.Y) + (exit.Outward * ExitOutsideMetres);
                var (ax, ay) = NavTile.NodeAt(aimAt);
                var flags = NavTile.InBounds(ax, ay) ? tile.Flags[NavTile.Index(ax, ay)].ToString() : "out-of-block";
                var below = NavTileBuilder.TerrainZ(block, aimAt) is { } tz ? Math.Round(exit.Sill.Z - tz, 2) : (double?)null;
                var record = new { cell, state = "no-ground-outside", sill = Pos(exit.Sill), outward = new[] { Math.Round(exit.Outward.X, 2), Math.Round(exit.Outward.Y, 2) }, flags, below };
                if (NavTile.InBounds(ax, ay) && ClosedByNature(tile.Flags[NavTile.Index(ax, ay)], below) is { } nature)
                {
                    // Closed by nature, with the reason named (the world walk's verdicts): a roof's or an upper floor's door over the building's own shell, or a door onto water.
                    kinds[nature] = kinds.GetValueOrDefault(nature) + 1;
                    natural.Add(new { cell, kind = nature, gap_m = below, sill = record.sill });
                    continue;
                }

                bad.Add(record);
                continue;
            }

            var lane = graph.TryJoin(tile, exit.CellId, out _);
            var gap = exit.Sill.Z - outside.Local.Z;
            var kind = (lane ? "lane" : "no-lane") + (gap > HighSillMetres ? "/high" : "/level");
            kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
            var inside = NavGoal.ToPoint(new NavPoint(exit.CellId & 0xFFFF0000u, room.Origin), room.CellId);
            var failed = new List<(string Way, OfflineWalkResult Walk)>();
            OfflineWalkResult WalkOne(string way, CellFrame start, NavGoal goal) =>
                rig.Walk(new OfflineWalkRequest($"{cell}-{way}", start, goal, ExitSecondsLimit) { PathSource = map => new IndoorOutdoorPathSource(new PathPlanner(map), cells) });

            // The walk in first, from the nearest outside node the body can be placed at: a node the map holds walkable where no body fits (under a balcony's edge) is no start, the next one is.
            walks++;
            var walkIn = WalkOne("in", NavMap.ToCellFrame(outside), inside);
            foreach (var other in OutsidePoints(tile, exit).Skip(1).Take(OutsideStartTries - 1))
            {
                if (walkIn.State is not null || walkIn.Reason != OfflineWalkRig.StartRefused)
                {
                    break;
                }

                outside = other;
                walkIn = WalkOne("in", NavMap.ToCellFrame(outside), inside);
            }

            walks++;
            var walkOut = WalkOne("out", room, NavGoal.ToPoint(outside));
            foreach (var (way, walk) in new[] { ("out", walkOut), ("in", walkIn) })
            {
                if (!walk.Clean)
                {
                    failed.Add((way, walk));
                }
            }

            if (failed.Count == 0)
            {
                continue;
            }

            var body = BodyAtDoor(rig, block, exit, cell);
            if (!lane && body is not (BodyWalks or BodyInOnly))
            {
                natural.Add(new { cell, kind, gap_m = Math.Round(gap, 2), body, from });
                continue;
            }

            foreach (var (way, walk) in failed)
            {
                bad.Add(new { cell, way, kind, from, lane_skip = lane ? null : string.Join(",", tileStats.PortalSkips.Where(k => k.Cell == exit.CellId).Select(k => k.Why)), body, gap_m = Math.Round(gap, 2), state = walk.State?.ToString(), reason = walk.Reason, rungs = walk.Rungs, refused = walk.ConfirmFailures, why = walk.RefusedWhy, rungs_at = walk.RungsAt, left_m = Math.Round(walk.LeftMetres, 2) });
            }
        }

        return (walks, bad, kinds, natural);
    }

    /// <summary>
    /// The standing place of the room nearest <paramref name="exit"/>'s sill among those reached from its cell through its openings, at most <see cref="StandingSearchRooms"/> rooms away; null when none has one.
    /// </summary>
    private static CellFrame? NearestStanding(LandblockCellSet cells, Dictionary<uint, CellFrame> rooms, RoomExit exit)
    {
        var seen = new HashSet<uint> { exit.CellId };
        var ring = new List<uint> { exit.CellId };
        for (var depth = 0; depth < StandingSearchRooms && ring.Count > 0; depth++)
        {
            var next = new List<uint>();
            foreach (var id in ring)
            {
                foreach (var portal in cells.GetCell(id)?.Portals ?? [])
                {
                    if (!portal.LeadsOutside && seen.Add(portal.OtherCellId))
                    {
                        next.Add(portal.OtherCellId);
                    }
                }
            }

            var found = next.Where(rooms.ContainsKey).Select(id => rooms[id]).OrderBy(f => Vector3.Distance(f.Origin, exit.Sill)).Cast<CellFrame?>().FirstOrDefault();
            if (found is not null)
            {
                return found;
            }

            ring = next;
        }

        return null;
    }

    /// <summary>
    /// Why a door with no walkable land node outside it is closed by nature, or null: <c>over-building</c> when the land before it is under a building's shell and the sill stands more than
    /// <see cref="OverBuildingMetres"/> over it (a roof's or an upper floor's door: the body can only drop), <c>onto-water</c> when the land before it is water (a dock's door).
    /// </summary>
    internal static string? ClosedByNature(NavFlags flags, double? below) =>
        (flags & NavFlags.Building) != 0 && below > OverBuildingMetres ? "over-building" : (flags & NavFlags.Water) != 0 ? "onto-water" : null;

    /// <summary>How far over the land a door's sill must stand for a building's shell before it to close it by nature (<see cref="ClosedByNature"/>), in metres. Not retail: the world walk's own.</summary>
    private const double OverBuildingMetres = 3.0;

    private static double[] Pos(Vector3 v) => [Math.Round(v.X, 2), Math.Round(v.Y, 2), Math.Round(v.Z, 2)];

    /// <summary>How many outside nodes a door's walk in tries as its start before its body's placement is called refused (<see cref="OutsidePoints"/>). Not retail: the world walk's own.</summary>
    private const int OutsideStartTries = 5;

    /// <summary>How many rooms away from a door's own room <see cref="NearestStanding"/> looks. Not retail: the world walk's own.</summary>
    private const int StandingSearchRooms = 3;

    /// <summary>
    /// How the body alone fares at a door, the planner aside: walked straight in from the ground and straight out to it along a fan of directions (<see cref="DoorFanDegrees"/>,
    /// <see cref="DoorFanMetres"/>), through the door's outer approach, with no route check. <c>walks</c> when some walk in and some walk out arrive with no recovery move,
    /// <c>in-only</c> when only a walk in does, <c>drop-only</c> when only a walk out does (a balcony: the body drops), <c>recovery-only</c> when a walk in arrives only with a
    /// recovery move (a jump onto a sill over the step-up), else <c>none</c>. Retail's edge slide keeps a walking body on a ledge over a drop with a steep face
    /// (<c>CTransition::edge_slide</c>, 11.6096 0x0050BEA0), so a door a metre over the ground with no step is jump-only both ways.
    /// </summary>
    private static string BodyAtDoor(OfflineWalkRig rig, LandblockCollision block, RoomExit exit, string cell)
    {
        var sill = new Vector2(exit.Sill.X, exit.Sill.Y);
        var lb = exit.CellId & 0xFFFF0000u;
        var room = new CellFrame(exit.CellId, new AcFrame(new Vector3(sill - (exit.Outward * DoorInsideMetres), exit.Sill.Z + 0.05f), Quaternion.Identity));
        var approach = NavMap.ToCellFrame(new NavPoint(lb, new Vector3(sill + (exit.Outward * CellRoutePlanner.ApproachMetres), exit.Sill.Z)));
        bool walkedIn = false, walkedOut = false, recoveredIn = false;
        foreach (var degrees in DoorFanDegrees)
        {
            var (sin, cos) = MathF.SinCos(degrees * MathF.PI / 180f);
            var direction = new Vector2((exit.Outward.X * cos) - (exit.Outward.Y * sin), (exit.Outward.X * sin) + (exit.Outward.Y * cos));
            foreach (var metres in DoorFanMetres)
            {
                var at = sill + (exit.Outward * CellRoutePlanner.ApproachMetres) + (direction * metres);
                if (NavTileBuilder.TerrainZ(block, at) is not { } ground)
                {
                    continue;
                }

                var land = NavMap.ToCellFrame(new NavPoint(lb, new Vector3(at, ground)));
                if (!walkedIn)
                {
                    var walk = Straight(rig, $"{cell}-body-in", land, room, exit.CellId, [new(approach, true), new(room, true)]);
                    // In means in the physics' cells too: a structure no building lists (no building portal names its cells) is never entered from the land, and a body
                    // walked to its room's point stands there in the land cell (the world walk's exits, W8: 20 doors called walkable that the planner rightly gives no lane).
                    var inside = walk.State == NavRunState.Arrived && !LandDefs.IsOutdoorCell(walk.EndCellId);
                    walkedIn = inside && walk.Rungs == 0;
                    recoveredIn |= inside;
                }

                if (!walkedOut)
                {
                    var walk = Straight(rig, $"{cell}-body-out", room, land, null, [new(approach, true), new(land, false)]);
                    walkedOut = walk.State == NavRunState.Arrived && walk.Rungs == 0 && LandDefs.IsOutdoorCell(walk.EndCellId);
                }

                if (walkedIn && walkedOut)
                {
                    return BodyWalks;
                }
            }
        }

        return walkedIn ? BodyInOnly : walkedOut ? BodyDropOnly : recoveredIn ? BodyRecoveryOnly : BodyNone;
    }

    /// <summary>One straight walk of the body alone (<see cref="BodyAtDoor"/>), with no route check.</summary>
    private static OfflineWalkResult Straight(OfflineWalkRig rig, string name, CellFrame start, CellFrame goal, uint? goalCell, NavWaypoint[] points)
    {
        var source = new StraightSource(points);
        return rig.Walk(new OfflineWalkRequest(name, start, NavGoal.ToPoint(NavMap.FromCellFrame(goal), goalCell), DoorWalkSeconds) { PathSource = _ => source, NoRouteCheck = true });
    }

    /// <summary>A path source that always gives the same straight points: the body's own walk at a door (<see cref="BodyAtDoor"/>).</summary>
    private sealed class StraightSource(IReadOnlyList<NavWaypoint> points) : INavPathSource
    {
        public NavMap? Map => null;

        public PathPlanner? Planner => null;

        public NavPath Plan(in CellFrame from, NavGoal goal, NavScars scars) => new(NavPathStatus.Found, points, null, []);
    }

    /// <summary>A walkable node a few metres out of the doorway, nearest first; null when the land there is closed.</summary>
    internal static NavPoint? OutsidePoint(NavTile tile, RoomExit exit) => OutsidePoints(tile, exit).Cast<NavPoint?>().FirstOrDefault();

    /// <summary>The walkable nodes a few metres out of the doorway, those with room for two nodes first, nearest first within each (<see cref="OutsidePoint"/>).</summary>
    internal static IEnumerable<NavPoint> OutsidePoints(NavTile tile, RoomExit exit)
    {
        var aim = new Vector2(exit.Sill.X, exit.Sill.Y) + (exit.Outward * ExitOutsideMetres);
        var (cx, cy) = NavTile.NodeAt(aim);
        foreach (var room in new byte[] { 2, 1 })
        {
            for (var r = 0; r <= ExitSearchNodes; r++)
            {
                for (var y = cy - r; y <= cy + r; y++)
                {
                    for (var x = cx - r; x <= cx + r; x++)
                    {
                        if (Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)) == r && NavTile.InBounds(x, y) && tile.IsWalkable(x, y) && tile.Clearance[NavTile.Index(x, y)] >= room && (room == 2 || tile.Clearance[NavTile.Index(x, y)] < 2) && (tile.Flags[NavTile.Index(x, y)] & NavFlags.Water) == 0)
                        {
                            yield return WalkContext.PointOf(tile, x, y);
                        }
                    }
                }
            }
        }
    }

    /// <summary>Sizes of the groups of rooms joined by the given steps, largest first.</summary>
    private static int[] Groups(IEnumerable<uint> rooms, IEnumerable<(uint A, uint B)> steps)
    {
        var parent = rooms.ToDictionary(r => r, r => r);
        uint Root(uint r)
        {
            while (parent[r] != r)
            {
                parent[r] = parent[parent[r]];
                r = parent[r];
            }

            return r;
        }

        foreach (var (a, b) in steps)
        {
            parent[Root(a)] = Root(b);
        }

        return [.. parent.Keys.GroupBy(Root).Select(g => g.Count()).OrderDescending()];
    }
}
