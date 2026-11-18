using System.Numerics;
using System.Runtime.CompilerServices;
using DatReaderWriter.DBObjs;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Dat;
using Lifestone.Data.Models;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Net.Types;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>
/// A door as the client knows it from the session's object table (M5 item 5, d1): a server object whose CreateObject has the Door description bit
/// (<c>ObjectDescriptionFlag.Door</c> 0x1000, set by <c>Door.cs:39</c> and written into the CreateObject by <c>WorldObject_Networking.cs:69-73</c> @95ef426d),
/// closed while its physics state lacks Ethereal and open while it has it (<c>Door.Open</c> sets Ethereal at once and the reset closes it again, <c>Door.cs:111-130,
/// 150-170,210-228</c>).
/// </summary>
/// <param name="Guid">The object's id.</param>
/// <param name="At">Where it stands (its CreateObject position, landblock-local).</param>
/// <param name="Radius">The door's own extent in metres: the radius of its Setup's selection sphere (<c>CSetup::selection_sphere</c>, which covers the door's leaf; the Setup's physics radius is a 0.14 m dummy) times its scale; <see cref="DoorCrossings.UnknownRadiusMetres"/> when it could not be read.</param>
/// <param name="Open">True while the door's physics state has Ethereal.</param>
/// <param name="Locked">The server's word that the door is locked (ACE <c>PropertyBool.Locked</c> through a property update), or null while it has not said.</param>
/// <param name="Unusable">The CreateObject's Usable is No (1; ACE writes <c>ItemUseable</c> into the weenie header, <c>WorldObject_Networking.cs:103,733</c> @95ef426d): a door the player cannot
/// Use, opened only by a linked lever or switch (M6 4 w6a: Fort Tethana's gate door 0x72581042, wcid 2179 'doorprisonactivated', whose Use text is "This door cannot be activated from here.",
/// opened by its lever 0x72581044 and shut again after its ResetInterval 300 s). The walk does not Use one (<c>TODO(door-lever)</c>).</param>
/// <param name="LockedFrom">Where <paramref name="Locked"/> was read from (<c>appraised</c>, <c>refused-use</c> or <c>property-update</c>), or null while unknown (M6 8 C).</param>
/// <param name="Name">The door's name for the logs, or null.</param>
public readonly record struct NavDoor(uint Guid, CellFrame At, float Radius, bool Open, bool? Locked, bool Unusable = false, string? LockedFrom = null, string? Name = null);

/// <summary>
/// A door matched to the doorway it stands in (<see cref="DoorCrossings.Map(IReadOnlyList{NavDoor}, INavPathSource)"/>): the tile's doorway lane (an outside door) or the room graph's portal crossing,
/// by the nearest sill to the door within the door's own extent.
/// </summary>
/// <param name="Door">The door.</param>
/// <param name="LandblockId">The landblock whose frame <see cref="Sill"/> is in.</param>
/// <param name="CellId">The EnvCell the doorway belongs to (the lane's entry cell).</param>
/// <param name="Sill">The doorway's centre at its lowest point, landblock-local.</param>
/// <param name="Outward">The unit horizontal direction through the doorway to the neighbouring cell (out of the building for an outside door).</param>
/// <param name="Lane">The outdoor tile's doorway lane for the same doorway, or null for a room-to-room doorway or a landblock with no attached tile.</param>
/// <param name="Distance">The 3D distance from the door's position to <see cref="Sill"/>, in metres.</param>
public sealed record DoorCrossing(NavDoor Door, uint LandblockId, uint CellId, Vector3 Sill, Vector2 Outward, NavDoorwayLane? Lane, float Distance)
{
    /// <summary>The sill as a lattice point (global metres through <see cref="NavPoint.Global"/>).</summary>
    public NavPoint SillPoint => new(LandblockId, Sill);
}

/// <summary>A closed-or-open door a path crosses: the door's crossing, where along the route (arc length) the route crosses its doorway and where the body stops to open it.</summary>
/// <param name="Crossing">The door and its doorway.</param>
/// <param name="CrossArc">The route's arc length at the doorway.</param>
/// <param name="StopArc">The route's arc length at which the body stops before the doorway to Use the door.</param>
/// <param name="StopStraightMetres">
/// The straight-line metres from the route's point at <see cref="StopArc"/> to where it crosses the doorway (M5 7 P7l): the stand-off, unless the route starts nearer the doorway than that (then the start's own distance).
/// </param>
public readonly record struct DoorStep(DoorCrossing Crossing, float CrossArc, float StopArc, float StopStraightMetres)
{
    /// <summary>The door's id.</summary>
    public uint Guid => Crossing.Door.Guid;

    /// <summary>
    /// How the stop was placed, for the step's log line: <c>along</c> when it lies the stand-off before the doorway along the route (a route straight into the doorway), <c>straight</c> when the route bends
    /// back on itself before the doorway so the stop was moved earlier to stand the stand-off away in a straight line (P7l), <c>start</c> when the route starts within the stand-off.
    /// </summary>
    public string StopRule => StopArc <= 0f ? "start" : CrossArc - StopArc > StopStraightMetres + 0.01f ? "straight" : "along";
}

/// <summary>
/// The doors the client knows and the way to Use one (<see cref="NavRunner.Doors"/>). The session's feed is <see cref="SessionDoorFeed"/>; a test gives its own.
/// </summary>
public interface INavDoorFeed
{
    /// <summary>Replaces <paramref name="into"/> with every door in the object table that has a position.</summary>
    /// <param name="into">The list to fill.</param>
    void Fill(List<NavDoor> into);

    /// <summary>One door by id, with its state now.</summary>
    /// <param name="guid">The door's id.</param>
    /// <param name="door">The door.</param>
    /// <returns>False when the object table does not hold it (or it has no position).</returns>
    bool TryGet(uint guid, out NavDoor door);

    /// <summary>Uses the door (<see cref="IActionApi.Use"/>, the automation source).</summary>
    /// <param name="guid">The door's id.</param>
    /// <returns>The action's handle, or null for a feed that sends none.</returns>
    ActionHandle? Use(uint guid);

    /// <summary>
    /// Asks the server whether the door is locked before the body walks up to it (M6 8 C, D117): the session's appraisal of the door. The answer arrives later and sets <see cref="NavDoor.Locked"/> with
    /// <see cref="NavDoor.LockedFrom"/> <c>appraised</c>. A feed that cannot ask does nothing (the default).
    /// </summary>
    /// <param name="guid">The door's id.</param>
    void ReadLock(uint guid)
    {
    }

    /// <summary>The doorway each of <paramref name="doors"/> stands in on <paramref name="paths"/>' landblocks; overridden by a test that has no tile and no rooms.</summary>
    /// <param name="doors">The doors.</param>
    /// <param name="paths">The run's path source (its tile and room graph).</param>
    IReadOnlyList<DoorCrossing> Crossings(IReadOnlyList<NavDoor> doors, INavPathSource paths) => DoorCrossings.Map(doors, paths);

    /// <summary>
    /// A number that changes when the set of doors the feed knows changes (a door created, deleted or given a position), read every frame by <see cref="NavRunner"/> to know when the doors of the
    /// remaining path are worth checking again (M5 7 F2). Allocates nothing. A feed whose doors never change leaves it 0.
    /// </summary>
    int Version => 0;
}

/// <summary>
/// Doors on the wayfinder's routes (M5 item 5, d1; closes the wayfinding half of <c>TODO(d25-server-object-host)</c>, docs/milestones/m4-hero-visual-bugs.md section 3):
/// maps each door of the object table to the doorway it stands in and finds where a planned path crosses one. Not retail: Lifestone's own. ACE rules at @95ef426d.
/// </summary>
/// <remarks>
/// <para><b>The doorway.</b> The candidates of a landblock are the outdoor tile's doorway lanes (<see cref="NavTile.DoorwayLanes"/>), the room graph's outside doorways
/// (<see cref="RoomGraph.ExitsToOutside"/>) and the room-to-room portals a standing human fits through (<see cref="CellRoutePlanner.Fits"/>). A door belongs to the nearest
/// candidate sill (3D) within the door's own extent, <see cref="NavDoor.Radius"/> (D46: no town constant); a door with no sill that near is not on any route and is left out.</para>
/// <para><b>The crossing.</b> A path crosses a doorway where a segment of it passes through the plane of the sill (perpendicular to the doorway's outward direction) within the
/// door's extent of the sill's centre. The route's arc length there is the step's <see cref="DoorStep.CrossArc"/>.</para>
/// <para><b>Locked.</b> ACE does not send the lock in the CreateObject (<c>WorldObject_Networking.cs:845-892</c> sets no door-lock bit); the client knows it only from a public
/// property update (<see cref="WorldObject.Locked"/>). A door that was never appraised or updated is read as not locked: <c>TODO(door-locked-unknown)</c>, and the runner's
/// open timeout names a door that stays shut. ACE opens a locked door used from behind it (<c>Door.cs:104-107</c>): <c>TODO(door-locked-behind)</c>, the runner does not use it.</para>
/// </remarks>
public static class DoorCrossings
{
    /// <summary>The ObjectDescriptionFlags bit of a door (<c>ObjectDescriptionFlag.Door</c>, <c>ACE.Entity/Enum/ObjectDescriptionFlag.cs:21</c> @95ef426d).</summary>
    public const uint DoorFlag = 0x1000;

    /// <summary>The extent assumed for a door whose Setup radius cannot be read, in metres (a door is about a metre across; logged once). Not retail: Lifestone's own.</summary>
    public const float UnknownRadiusMetres = 1f;

    /// <summary>The weenie header's Usable value No (ACE <c>Usable.No</c> = 0x01, <c>ACE.Entity/Enum/Usable.cs:9</c> @95ef426d): the object cannot be used.</summary>
    public const uint UsableNo = 0x01;

    /// <summary>
    /// A door Setup's extent by its id from <paramref name="dats"/>: the radius of its selection sphere (<c>CSetup::selection_sphere</c>), else its physics radius (<c>CSetup::radius</c>), or null for a Setup not in
    /// the dats; cached per id. An Aluvian house door Setup (0x020019FF) has a 0.89 m selection sphere round its leaf and a 0.14 m physics radius. For <see cref="SessionDoorFeed"/>'s <c>setupExtent</c>. Game thread only.
    /// </summary>
    /// <param name="dats">The dats.</param>
    public static Func<uint, float?> SetupExtents(DatSet dats)
    {
        ArgumentNullException.ThrowIfNull(dats);
        var cache = new Dictionary<uint, float?>();
        return id =>
        {
            if (!cache.TryGetValue(id, out var extent))
            {
                extent = null;
                if (dats.TryGetPortal<Setup>(id, out var setup))
                {
                    var model = ModelAssembly.FromSetup(id, setup);
                    extent = model.SelectionSphere.Radius > 0f ? model.SelectionSphere.Radius : model.Radius;
                }

                cache[id] = extent;
            }

            return extent;
        };
    }

    private static readonly ConditionalWeakTable<RoomGraph, List<Candidate>> RoomCandidates = [];

    /// <summary>Whether <paramref name="o"/> is a door the client can place: the Door description bit and a position.</summary>
    /// <param name="o">The object.</param>
    public static bool IsDoor(WorldObject o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return (o.DescriptionFlags & DoorFlag) != 0 && o.Position is not null;
    }

    /// <summary>The door's state now: closed while the physics state lacks Ethereal (<c>Door.cs:111-130</c> @95ef426d).</summary>
    /// <param name="o">The door object.</param>
    /// <param name="radius">The door's extent.</param>
    public static NavDoor Describe(WorldObject o, float radius)
    {
        ArgumentNullException.ThrowIfNull(o);
        return new NavDoor(o.Guid, o.Position!.Value, radius, (o.State & PhysicsState.Ethereal) != 0, o.Locked, o.Weenie.Usable == UsableNo, o.LockedFrom, o.Name);
    }

    /// <summary>The doorway of each door on the path source's landblocks (see the class remarks); doors with none are left out.</summary>
    /// <param name="doors">The doors.</param>
    /// <param name="paths">The run's path source.</param>
    public static IReadOnlyList<DoorCrossing> Map(IReadOnlyList<NavDoor> doors, INavPathSource paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Map(doors, (paths as INavRoomSource)?.Rooms, paths.Map);
    }

    /// <summary>The doorway of each door, from a room graph and an attached nav map (either may be null).</summary>
    /// <param name="doors">The doors.</param>
    /// <param name="rooms">The landblock's room graph, or null.</param>
    /// <param name="map">The nav map holding the landblock's tile, or null.</param>
    public static IReadOnlyList<DoorCrossing> Map(IReadOnlyList<NavDoor> doors, RoomGraph? rooms, NavMap? map)
    {
        ArgumentNullException.ThrowIfNull(doors);
        var found = new List<DoorCrossing>();
        foreach (var door in doors)
        {
            var landblock = door.At.CellId & 0xFFFF0000u;
            var best = (Candidate?)null;
            var bestDistance = float.MaxValue;
            void Consider(Candidate c)
            {
                var d = Vector3.Distance(door.At.Origin, c.Sill);
                if (d < bestDistance)
                {
                    (best, bestDistance) = (c, d);
                }
            }

            NavTile? tile = null;
            if (map is not null && map.TryGetTile(landblock, out var attached))
            {
                tile = attached;
                foreach (var lane in tile.DoorwayLanes)
                {
                    Consider(new Candidate(lane.EntryCellId, lane.Sill, lane.Outward, lane));
                }
            }

            if (rooms is not null && rooms.LandblockId == landblock)
            {
                foreach (var c in CandidatesOf(rooms))
                {
                    Consider(c);
                }
            }

            if (best is { } pick && bestDistance <= door.Radius)
            {
                found.Add(new DoorCrossing(door, landblock, pick.CellId, pick.Sill, pick.Outward, pick.Lane, bestDistance));
            }
        }

        return found;
    }

    /// <summary>
    /// The doors a path crosses, in route order: for each crossing, the first place a segment of the path passes through the doorway's plane within the door's extent.
    /// </summary>
    /// <param name="path">The planned path.</param>
    /// <param name="crossings">The doors' doorways (<see cref="Map(IReadOnlyList{NavDoor}, INavPathSource)"/>).</param>
    /// <param name="standOffMetres">
    /// How far before the doorway the body stops, in a straight line from where the route crosses the doorway (M5 7 P7l): the last point of the route before the crossing that is this far from it. On a route straight
    /// into the doorway that is the stand-off along the route, as before. Live trip check 3 (2026-10-30 06:13 UTC) showed why it is not measured along the route: the room route out of Archmage Cindrue's entry hall
    /// 0xA9B40117 by the door 0x7A9B400A zigzags inside the door's thin doorway cell 0xA9B40119 (its doorway vertices, each pushed into a cell 0.7 m deep, run up to the outer sill, back 0.45 m and up to it again), so the stop 1.3 m before the
    /// doorway along the route lay 0.4 m from the shut door's panel in a straight line, inside the body's contact with it: the body pressed on the panel 0.9 m short of the stop, never Used the door, and the leg
    /// ended stuck after 3 plans and 12 rungs.
    /// </param>
    public static List<DoorStep> StepsOf(NavPath path, IReadOnlyList<DoorCrossing> crossings, float standOffMetres)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(crossings);
        var steps = new List<DoorStep>();
        var waypoints = path.Waypoints;
        if (waypoints.Count < 2 || crossings.Count == 0)
        {
            return steps;
        }

        var xy = new Vector2[waypoints.Count];
        var z = new float[waypoints.Count];
        var arc = new float[waypoints.Count];
        for (var i = 0; i < waypoints.Count; i++)
        {
            var p = waypoints[i].Point;
            xy[i] = p.Global;
            z[i] = p.Z;
            arc[i] = i == 0 ? 0f : arc[i - 1] + Vector2.Distance(xy[i - 1], xy[i]);
        }

        foreach (var crossing in crossings)
        {
            for (var i = 0; i + 1 < waypoints.Count; i++)
            {
                if (Crosses(crossing, xy[i], z[i], xy[i + 1], z[i + 1], checkHeight: true, out var t))
                {
                    var at = arc[i] + (t * (arc[i + 1] - arc[i]));
                    var cross = Vector2.Lerp(xy[i], xy[i + 1], t);
                    var (stop, straight) = StopBefore(xy, arc, i, cross, at, standOffMetres);
                    steps.Add(new DoorStep(crossing, at, stop, straight));
                    break;
                }
            }
        }

        steps.Sort((a, b) => a.CrossArc.CompareTo(b.CrossArc));
        return steps;
    }

    /// <summary>
    /// Where the body stops before a doorway the route crosses at <paramref name="cross"/> (arc <paramref name="crossArc"/>, on the segment from vertex <paramref name="segment"/>): walking back along the route from the
    /// crossing, the first point that is <paramref name="standOff"/> from the crossing in a straight line (the route's arc there, and that distance); the route's start (arc 0, its own distance) when no point before
    /// the crossing is that far. On a straight route into the doorway the arc is the crossing's less the stand-off.
    /// </summary>
    private static (float Arc, float Straight) StopBefore(Vector2[] xy, float[] arc, int segment, Vector2 cross, float crossArc, float standOff)
    {
        var q = cross;
        var qArc = crossArc;
        for (var k = segment; k >= 0; k--)
        {
            var p = xy[k];
            var from = p - cross;
            if (from.Length() >= standOff)
            {
                // Between p (at least the stand-off out) and q (within it): the point at exactly the stand-off, the smaller root of |from + u (q - p)|^2 = standOff^2 on [0, 1].
                var d = q - p;
                var a = Vector2.Dot(d, d);
                if (a <= 1e-8f)
                {
                    return (arc[k], from.Length());
                }

                var b = 2f * Vector2.Dot(from, d);
                var c = Vector2.Dot(from, from) - (standOff * standOff);
                var root = (-b - MathF.Sqrt(MathF.Max(0f, (b * b) - (4f * a * c)))) / (2f * a);
                var u = Math.Clamp(root, 0f, 1f);
                return (arc[k] + (u * (qArc - arc[k])), standOff);
            }

            q = p;
            qArc = arc[k];
        }

        return (0f, Vector2.Distance(xy[0], cross));
    }

    /// <summary>
    /// Whether the segment from (<paramref name="a"/>, <paramref name="za"/>) to (<paramref name="b"/>, <paramref name="zb"/>), global metres, passes through the doorway's
    /// plane within the door's extent of the sill's centre (horizontally; and, with <paramref name="checkHeight"/>, within it vertically too: the sight line's eye height is
    /// not the floor's, so it is judged by the door's height instead).
    /// </summary>
    /// <param name="crossing">The doorway.</param>
    /// <param name="a">The segment's start.</param>
    /// <param name="za">Its height.</param>
    /// <param name="b">The segment's end.</param>
    /// <param name="zb">Its height.</param>
    /// <param name="checkHeight">True for a path on the floor (the height must be within the door's extent of the sill), false for a line of sight.</param>
    /// <param name="t">Where along the segment it crosses, 0 to 1.</param>
    public static bool Crosses(DoorCrossing crossing, Vector2 a, float za, Vector2 b, float zb, bool checkHeight, out float t)
    {
        ArgumentNullException.ThrowIfNull(crossing);
        t = 0f;
        var sill = crossing.SillPoint.Global;
        var da = Vector2.Dot(a - sill, crossing.Outward);
        var db = Vector2.Dot(b - sill, crossing.Outward);
        if (!((da < 0f && db >= 0f) || (da > 0f && db <= 0f)))
        {
            return false;
        }

        t = da / (da - db);
        var at = Vector2.Lerp(a, b, t);
        var height = za + (t * (zb - za));
        var reach = crossing.Door.Radius;
        if (Vector2.Distance(at, sill) > reach)
        {
            return false;
        }

        return checkHeight ? MathF.Abs(height - crossing.Sill.Z) <= reach : height >= crossing.Sill.Z - reach && height <= crossing.Sill.Z + (2f * reach);
    }

    private static List<Candidate> CandidatesOf(RoomGraph rooms) => RoomCandidates.GetValue(rooms, Build);

    private static List<Candidate> Build(RoomGraph rooms)
    {
        var list = new List<Candidate>();
        foreach (var exit in rooms.ExitsToOutside)
        {
            list.Add(new Candidate(exit.CellId, exit.Sill, exit.Outward, null));
        }

        foreach (var id in rooms.Cells.Ids)
        {
            var cell = rooms.Cells.GetCell(id)!;
            foreach (var portal in cell.Portals)
            {
                if (!portal.LeadsOutside && rooms.Cells.GetCell(portal.OtherCellId) is not null && rooms.Planner.Fits(cell, portal) && rooms.Planner.DoorwayOf(cell, portal) is { } doorway)
                {
                    list.Add(new Candidate(id, new Vector3(doorway.Centre.X, doorway.Centre.Y, doorway.Sill), new Vector2(doorway.Outward.X, doorway.Outward.Y), null));
                }
            }
        }

        return list;
    }

    private readonly record struct Candidate(uint CellId, Vector3 Sill, Vector2 Outward, NavDoorwayLane? Lane);
}

/// <summary>
/// The doors from the session's object table (<see cref="INavDoorFeed"/>): every object with the Door description bit and a position, its extent from its Setup's radius times its scale
/// (<see cref="DoorCrossings.SetupExtents"/>), <see cref="IActionApi.Use"/> by the automation source (the source of the runner's other actions: the kill switch and the dry run apply).
/// </summary>
/// <param name="session">The session (its object table and its actions).</param>
/// <param name="log">The log (the first door whose extent falls back), or null.</param>
/// <param name="setupExtent">A door Setup's extent by its id (<see cref="DoorCrossings.SetupExtents"/>), or null for no lookup (every door then assumes <see cref="DoorCrossings.UnknownRadiusMetres"/>, logged once).</param>
public sealed class SessionDoorFeed(IGameSessionView session, ILogSink? log = null, Func<uint, float?>? setupExtent = null) : INavDoorFeed
{
    private bool _logged;
    private int _version;
    private int _doorCount = -1;
    private uint _guidSum;

    /// <inheritdoc/>
    /// <remarks>Counts the door objects and sums their ids (one pass over the object table, nothing allocated); the number moves when either changes.</remarks>
    public int Version
    {
        get
        {
            var count = 0;
            var sum = 0u;
            foreach (var o in session.State.Objects.All)
            {
                if (DoorCrossings.IsDoor(o))
                {
                    count++;
                    sum += o.Guid;
                }
            }

            if (count != _doorCount || sum != _guidSum)
            {
                (_doorCount, _guidSum) = (count, sum);
                _version++;
            }

            return _version;
        }
    }

    /// <inheritdoc/>
    public void Fill(List<NavDoor> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        foreach (var o in session.State.Objects.All)
        {
            if (DoorCrossings.IsDoor(o))
            {
                into.Add(DoorCrossings.Describe(o, RadiusOf(o)));
            }
        }
    }

    /// <inheritdoc/>
    public bool TryGet(uint guid, out NavDoor door)
    {
        if (session.State.Objects.TryGet(guid, out var o) && DoorCrossings.IsDoor(o))
        {
            door = DoorCrossings.Describe(o, RadiusOf(o));
            return true;
        }

        door = default;
        return false;
    }

    /// <inheritdoc/>
    public void ReadLock(uint guid) => _ = session.Actions.Identify(guid, ActionSource.Automation);

    /// <inheritdoc/>
    public ActionHandle? Use(uint guid)
    {
        DoorRefusals.Attach(session); // a locked door answers the Use with its sound 0x94 (Door.cs:108-116 @95ef426d)
        var handle = session.Actions.Use(guid, ActionSource.Automation);
        if (handle is not null)
        {
            DoorUseLedger.For(session.ActionLog).Record(guid); // M5 5 F2 (5-R2): the walk's own Use, by guid; DoorWatch counts an opening only of a door in here
        }

        return handle;
    }

    private float RadiusOf(WorldObject o)
    {
        var physics = o.Created.Physics;
        if (setupExtent is not null && physics.SetupId is { } id and not 0 && setupExtent(id) is { } radius and > 0f)
        {
            return radius * (physics.Scale is { } scale and > 0f ? scale : 1f);
        }

        if (!_logged)
        {
            _logged = true;
            log?.Warn(
                "wayfinding",
                setupExtent is null
                    ? "unread door extent: the host gave the door feed no Setup lookup (DoorCrossings.SetupExtents), so every door assumes its fallback extent"
                    : "a door's Setup radius could not be read (no Setup id, or not in the dats); it assumes the fallback extent",
                new LogField("door", o.Name),
                new LogField("assumed_m", DoorCrossings.UnknownRadiusMetres));
        }

        return DoorCrossings.UnknownRadiusMetres;
    }
}

/// <summary>
/// The refusal of a locked door (M6 8 C, D117). ACE answers a Use of a locked door from the front with the transient line "The door is locked!" and the sound 0x94 on the door, and no error code
/// (<c>Door.cs:108-116</c> @95ef426d); this reads the sound and marks the door locked, read from <c>refused-use</c>. One subscription per session, made by the feed's first Use.
/// </summary>
internal static class DoorRefusals
{
    private static readonly ConditionalWeakTable<ActionLog, IDisposable> Attached = [];

    /// <summary>Starts reading the session's door refusals (once).</summary>
    public static void Attach(IGameSessionView session)
    {
        Attached.GetValue(session.ActionLog, _ => session.Events.Subscribe<ObjectSoundPlayed>((in ObjectSoundPlayed e) =>
        {
            if (e.SoundId == Lifestone.Client.Session.Actions.Trackers.OpenContainerTracker.LockedSound && session.State.Objects.TryGet(e.Guid, out var o) && (o.DescriptionFlags & DoorCrossings.DoorFlag) != 0)
            {
                o.Locked = true;
                o.LockedFrom = "refused-use";
            }
        }));
    }
}

/// <summary>
/// The doors the walk itself Used, by guid, for one session (M5 item 5, F2, review 5-R2): <see cref="SessionDoorFeed.Use"/> records the guid here when it sends a Use, and <see cref="DoorWatch"/> reads it. The
/// runner's Use of a door goes through the feed, so this is exactly the walk's Uses; the vendor's Use, another lane's door and ACE opening a door never reach it. Keyed by the session's action log (one per session).
/// </summary>
/// <remarks>Game thread only.</remarks>
public sealed class DoorUseLedger
{
    private static readonly ConditionalWeakTable<ActionLog, DoorUseLedger> Ledgers = [];
    private readonly Dictionary<uint, long> _lastUse = [];

    private DoorUseLedger()
    {
    }

    /// <summary>How many walk Uses were recorded so far: a watch takes this as its mark.</summary>
    public long Sequence { get; private set; }

    /// <summary>The ledger of the session whose action log is <paramref name="log"/> (made on first use).</summary>
    /// <param name="log">The session's action log.</param>
    /// <returns>The ledger.</returns>
    public static DoorUseLedger For(ActionLog log) => Ledgers.GetValue(log ?? throw new ArgumentNullException(nameof(log)), static _ => new DoorUseLedger());

    /// <summary>Records that the walk sent a Use of <paramref name="guid"/>.</summary>
    /// <param name="guid">The door's id.</param>
    public void Record(uint guid) => _lastUse[guid] = ++Sequence;

    /// <summary>True when the walk Used <paramref name="guid"/> after the ledger stood at <paramref name="mark"/>.</summary>
    /// <param name="mark">A <see cref="Sequence"/> taken earlier.</param>
    /// <param name="guid">The door's id.</param>
    /// <returns>True when it did.</returns>
    public bool UsedSince(long mark, uint guid) => _lastUse.TryGetValue(guid, out var at) && at > mark;

    /// <summary>The number of distinct doors the walk Used after the ledger stood at <paramref name="mark"/>.</summary>
    /// <param name="mark">A <see cref="Sequence"/> taken earlier.</param>
    /// <returns>The count.</returns>
    public int DoorsUsedSince(long mark)
    {
        var n = 0;
        foreach (var at in _lastUse.Values)
        {
            if (at > mark)
            {
                n++;
            }
        }

        return n;
    }
}

/// <summary>
/// Watches the doors a station's walk opens (the journey vendor station's <c>door_opened=</c>, report-only): a door the walk itself Used (<see cref="DoorUseLedger"/>, written by <see cref="SessionDoorFeed.Use"/>)
/// whose physics state gained Ethereal while the watch is live. <c>door_opened=yes</c> is that and nothing else (5-R2): another lane's door, a door ACE opened, a door found open and the vendor's own Use never count.
/// </summary>
public sealed class DoorWatch : IDisposable
{
    private readonly IGameSessionView _session;
    private readonly DoorUseLedger _ledger;
    private readonly IDisposable _subscription;
    private readonly long _mark;
    private readonly HashSet<uint> _gained = [];

    /// <summary>Starts watching <paramref name="session"/>.</summary>
    /// <param name="session">The session.</param>
    public DoorWatch(IGameSessionView session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _ledger = DoorUseLedger.For(session.ActionLog);
        _mark = _ledger.Sequence;
        _subscription = session.Events.Subscribe<ObjectStateChanged>(OnState);
    }

    /// <summary>The doors the walk Used that were seen to open (Ethereal gained) since the watch began.</summary>
    public int Opened
    {
        get
        {
            var n = 0;
            foreach (var guid in _gained)
            {
                if (_ledger.UsedSince(_mark, guid))
                {
                    n++;
                }
            }

            return n;
        }
    }

    /// <summary>The distinct doors the walk Used (through <see cref="SessionDoorFeed.Use"/>) since the watch began.</summary>
    public int WalkUses => _ledger.DoorsUsedSince(_mark);

    /// <summary>True when a door the walk Used turned Ethereal meanwhile.</summary>
    public bool DoorOpened => Opened > 0;

    /// <summary><c>yes</c> or <c>no</c>, for the station's line.</summary>
    public string Field => DoorOpened ? "yes" : "no";

    /// <inheritdoc/>
    public void Dispose() => _subscription.Dispose();

    private void OnState(in ObjectStateChanged e)
    {
        if ((e.Previous & PhysicsState.Ethereal) == 0 && (e.State & PhysicsState.Ethereal) != 0
            && _session.State.Objects.TryGet(e.Guid, out var o) && (o.DescriptionFlags & DoorCrossings.DoorFlag) != 0)
        {
            _gained.Add(e.Guid);
        }
    }
}
