using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.World;
using Lifestone.Net.Types;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>
/// The body of a creature the creature layer does not count (M6 4 W2, <c>m6-npc-in-door-lane</c>): an NPC or a vendor, which the player's physics meets as a solid body
/// (<c>CPhysicsObj::FindObjCollisions</c>, 11.6096 0x0050FB20, as <see cref="Lifestone.Client.Physics.ServerObjectPhysicsHost"/> ports it) but the planner never saw, since the
/// confirmer checks the fixed world only (docs/wayfinding.md "The confirmer").
/// </summary>
/// <param name="Guid">The object's id.</param>
/// <param name="Name">Its name, for the log.</param>
/// <param name="Centre">Its centre in global metres (<see cref="NavPoint.Global"/>).</param>
/// <param name="Radius">Its own radius: its Setup's radius times its scale, as the physics object's (<c>PhysicsObj.Radius</c>).</param>
/// <param name="Z">Its feet's z.</param>
/// <param name="Outdoor">True when it stands in an outdoor cell (on the lattice); a body in a room is not on the plan (<c>TODO(m6-npc-indoor-bodies)</c>).</param>
public readonly record struct NavBody(uint Guid, string Name, Vector2 Centre, float Radius, float Z, bool Outdoor);

/// <summary>A source of the bodies of the creatures the creature layer leaves out (the NPCs and vendors the client sees).</summary>
public interface INavBodyFeed
{
    /// <summary>Clears <paramref name="into"/> and fills it with every body within <paramref name="maxMetres"/> of <paramref name="from"/> (global metres).</summary>
    void Fill(Vector2 from, float maxMetres, List<NavBody> into);

    /// <summary>
    /// M6 sprint (<c>m6-vendor-reach</c>): the live place of the object <paramref name="guid"/>, as the client holds it (its object table's position: what the client draws), whether or not it is a body on the plan (a hostile
    /// creature, a chest), so an object goal's arrival is measured to where the object is now (<c>NavRunner.CheckArrival</c>). False when it is not in view; a feed that does not say returns false and the goal's planned point stands.
    /// </summary>
    /// <param name="guid">The object's id.</param>
    /// <param name="at">Its cell and frame.</param>
    bool TryPlace(uint guid, out CellFrame at)
    {
        at = default;
        return false;
    }
}

/// <summary>
/// The NPCs' bodies from the client's object table (M6 4 W2): every creature (ItemType bit 0x10) that is placed, not the player or another player (0x8), not
/// ethereal (PhysicsState 0x4: the physics lets the body through), not known dead, and that <see cref="HostileCreatureFeed.IsHostile"/> leaves out (not attackable,
/// or a vendor, 0x200): the Scrivener of Item Magic before Holtburg's scriveners' door is one; and every lifestone and bind stone (<see cref="StoneFlags"/>, M6 sprint), which is no creature. Its radius is its Setup's radius (retail <c>CSetup::radius</c>,
/// <see cref="HostileCreatureFeed.SetupRadii"/>) times its scale, and beyond a person's width only what its collision shape blocks (<see cref="BodyRadius"/>, M6 item 14); <see cref="NavCreatures.UnknownCreatureRadiusMetres"/> when there is no lookup or no Setup id (logged once).
/// A creature whose Setup the lookup cannot read (not in the dats, or of no radius) is not a body (M6 4 f3, logged once by name, <c>npc-setup-unread</c>): the player's physics has no shape for it either,
/// so the walker never meets it. Live (M6 4 round 1, Holtburg): the "Bottle" in 0xA9B40155 is a creature to the client (ACE weenie 40526 <c>ace40526-bottle</c>, ItemType 16, WeenieType 10,
/// PropertyBool 83 NpcLooksLikeObject, which the wire does not carry), with Setup 0x02001A57, which the lookup could not read; it was counted a body of 1 m.
/// Allocates nothing once the list has grown.
/// </summary>
/// <param name="objects">The session's object table.</param>
/// <param name="self">The player's id.</param>
/// <param name="log">The log, or null.</param>
/// <param name="setupRadius">A Setup's radius by its id (<see cref="HostileCreatureFeed.SetupRadii"/>); null for none (every body then assumes 1 m, logged once).</param>
public sealed class NpcBodyFeed(WorldObjectTable objects, uint self, ILogSink? log = null, Func<uint, float?>? setupRadius = null) : INavBodyFeed
{
    private readonly WorldObjectTable _objects = objects ?? throw new ArgumentNullException(nameof(objects));
    private bool _radiusLogged;
    private bool _unreadLogged;

    /// <summary>
    /// The ObjectDescriptionFlags bits of a lifestone (<c>BF_LIFESTONE</c> 0x4000) and of an allegiance bind stone (<c>BF_BINDSTONE</c> 0x8000000), as <see cref="Lifestone.Client.Avatars.WorldContentCensus"/> reads them: solid fixed
    /// objects the server places, which the player's physics meets (<see cref="Lifestone.Client.Physics.ServerObjectPhysicsHost"/>: "creatures stop at each other, the player, doors and lifestones") and which are no creature.
    /// </summary>
    public const uint StoneFlags = 0x4000 | 0x8000000;

    /// <summary>
    /// Whether <paramref name="o"/> is a body as the feed counts one: placed, solid, not the player or another player, and either a living creature that is not hostile, or a lifestone or bind stone
    /// (<see cref="StoneFlags"/>). M6 sprint (run 6, 2026-11-20 03:16:39 UTC, the video's 5:38:25): Lifestone Recall lands on the tied lifestone's own point (ACE WorldObject_Magic.cs 1087-1095 @95ef426d,
    /// <c>LinkedLifestone</c>, which Lifestone Tie sets to the lifestone's location, 974), so every trip home starts inside Holtburg's lifestone 0x7A9B404F; the planner did not know the lifestone (no creature),
    /// planned the walk to Archmage Cindrue straight out of it to the north-east, and the body pressed along the stone (two touches, two stuck rungs) into Alcott, who stands 3.0 m from its centre. The stone touched
    /// the walker on 9 of the run's 27 arrivals there. A start inside a body's disc may leave it, moving outward (<see cref="NavScars.SetBodies"/>), so the walk out of the stone stays planned.
    /// </summary>
    /// <param name="o">The object.</param>
    /// <param name="self">The player's id.</param>
    public static bool IsBody(WorldObject o, uint self)
    {
        ArgumentNullException.ThrowIfNull(o);
        if (o.Guid == self || o.IsSelf || o.Position is null || (o.DescriptionFlags & WorldObjectTable.PlayerFlag) != 0 || (o.State & PhysicsState.Ethereal) != 0)
        {
            return false;
        }

        return (o.ItemType & HostileCreatureFeed.CreatureItemType) != 0
            ? o.Health is not <= 0f && o.Motion is not { Forward: WorldObjectTable.DeadMotionLowWord } && !HostileCreatureFeed.IsHostile(o, self)
            : (o.DescriptionFlags & StoneFlags) != 0;
    }

    /// <inheritdoc/>
    public void Fill(Vector2 from, float maxMetres, List<NavBody> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        float max2 = maxMetres * maxMetres;
        foreach (var o in _objects.All)
        {
            if (!IsBody(o, self))
            {
                continue;
            }

            var at = o.Position!.Value;
            var centre = NavMap.FromCellFrame(at).Global;
            if (Vector2.DistanceSquared(centre, from) > max2)
            {
                continue;
            }

            if (RadiusOf(o) is not { } radius)
            {
                continue;
            }

            into.Add(new NavBody(o.Guid, o.Name, centre, radius, at.Origin.Z, LandDefs.IsOutdoorCell(at.CellId)));
        }
    }

    /// <inheritdoc/>
    public bool TryPlace(uint guid, out CellFrame at)
    {
        if (guid != 0 && _objects.TryGet(guid, out var o) && o.Position is { } p && p.CellId != 0)
        {
            at = p;
            return true;
        }

        at = default;
        return false;
    }

    /// <summary>
    /// A person's body: the human Setup 0x02000001's radius (retail <c>CSetup::radius</c> 0.68 m at scale 1, <c>HoltburgScrivenerWalkDatTests</c> reads it from the dats), the widest a townsperson is. A body no wider is
    /// sized as before; a wider one is not trusted to its model's bounds.
    /// </summary>
    public const float PersonRadiusMetres = 0.68f;

    /// <summary>
    /// M6 item 14 (<c>m6-forge-in-smithy-lane</c>, live 2026-11-12 01:02 UTC): a body's radius on the plan. The Setup's radius is the model's bounds: right for a person, far too wide for an object the server types a creature
    /// (Holtburg's Salvaging Forge, ACE weenie 30461: Setup radius 2.07 m at scale 1.4, a disc of 3.38 m that closed the lane to the smithy's door, which the walker's own physics passes 3.3 m from its centre). So: no wider than
    /// its bounds (<paramref name="bounds"/>), and beyond a person's width (<see cref="PersonRadiusMetres"/>) only as wide as what its collision shape blocks at walking height (<paramref name="shape"/>:
    /// <see cref="SetupShapes.ShapeRadius"/> times the scale). With no shape read (a lookup that carries none, or a Setup whose collision is its parts' geometry) the body is a person's width: the walker's physics, which
    /// holds the object's real shape, meets what the disc leaves out, and a disc too wide closes a lane for good where one too narrow costs a re-plan. A standing person is walked round exactly as before: nothing on the wire
    /// tells a fixed object from an NPC who never moves, so every such creature stays a body.
    /// </summary>
    /// <param name="bounds">The Setup's radius times the object's scale.</param>
    /// <param name="shape">The collision shape's radius at walking height times the scale, or null when not read.</param>
    public static float BodyRadius(float bounds, float? shape) => MathF.Min(bounds, MathF.Max(PersonRadiusMetres, shape ?? 0f));

    /// <summary>The body's radius, or null when it is no body: a lookup was given and cannot read the object's Setup (the physics has no shape for it).</summary>
    private float? RadiusOf(WorldObject o)
    {
        var physics = o.Created.Physics;
        if (setupRadius is not null && physics.SetupId is { } id and not 0)
        {
            if (setupRadius(id) is { } radius and > 0f)
            {
                var scale = physics.Scale is { } s and > 0f ? s : 1f;
                return BodyRadius(radius * scale, SetupShapes.Of(setupRadius)?.ShapeRadius(id) * scale);
            }

            if (!_unreadLogged)
            {
                _unreadLogged = true;
                log?.Warn("wayfinding", "npc-setup-unread: a creature's Setup is not in the dats or has no radius; the physics has no shape for it, so it is not a body on the plan (a prop the server types a creature)", new LogField("npc", o.Name), new LogField("guid", o.Guid), new LogField("setup", id));
            }

            return null;
        }

        if (!_radiusLogged)
        {
            _radiusLogged = true;
            log?.Warn("wayfinding", "an NPC's Setup radius could not be read (no lookup, no Setup id, or not in the dats); its body's disc assumes 1 m", new LogField("npc", o.Name), new LogField("setup", physics.SetupId ?? 0), new LogField("assumed_m", NavCreatures.UnknownCreatureRadiusMetres));
        }

        return NavCreatures.UnknownCreatureRadiusMetres;
    }
}

/// <summary>
/// A run's NPC bodies on the plan (M6 4 W2, <c>m6-npc-in-door-lane</c>; docs/wayfinding.md "The creature layer"). At each plan the runner reads the bodies
/// (<see cref="Refresh"/>), stamps those within <see cref="RouteMetres"/> of the way as blocked discs (<see cref="NavScars.SetBodies"/>: the walker's radius
/// plus the body's own, <see cref="DiscRadius"/>), and checks the planned path against every body it read (<see cref="FirstMeeting"/>): a body met by a path planned
/// with it stamped stands where the plan cannot go round (a doorway lane, a gap narrower than the body and the disc). Left out: the request's own object (a walk to an
/// NPC still reaches it), a body whose disc holds the goal point (the walk ends at its stand), a body in a room (the room graph knows no bodies:
/// <c>TODO(m6-npc-indoor-bodies)</c>) and a body more than <see cref="HeightBandMetres"/> over or under the lattice's ground at its feet (a balcony over a
/// street). Not retail: the player walked round an NPC himself; every constant here is Lifestone's own. Game thread only; allocates nothing once warm.
/// </summary>
public sealed class NpcBodies
{
    /// <summary>A body within this of the way (the straight line to the goal, or a planned path) is stamped (the brief's "about 30 m of the route").</summary>
    public const float RouteMetres = 30f;

    /// <summary>A body whose feet are more than this over or under the lattice's ground at its centre stands on another level (a balcony): not on the lattice.</summary>
    public const float HeightBandMetres = 1.5f;

    /// <summary>
    /// How much of a disc's edge a path may graze before it meets the body: the Setup's bounding radius (0.68 m for the human Setup 0x02000001) is larger than
    /// the human cylinder the physics collides (0.48 m), so a path 0.2 m inside the disc still clears the body.
    /// </summary>
    public const float MeetSlackMetres = 0.2f;

    /// <summary>The wait before the one replan when a body stands in the way the plan cannot go round (the runner's <c>npc-in-lane</c>).</summary>
    public const double LaneWaitSeconds = 3.0;

    /// <summary>Plans a runner's plan may make for the bodies (the first, then with the bodies near the planned path, then once more).</summary>
    public const int MaxPlans = 3;

    private readonly List<NavBody> _fill = [];
    private readonly List<NavBody> _seen = [];
    private readonly List<NavBody> _stamped = [];
    private readonly List<(Vector2 Centre, float Radius)> _discs = [];
    private readonly List<(NavBody Body, float Gap)> _offLevel = [];

    /// <summary>The bodies the last <see cref="Refresh"/> kept (outdoor, on the lattice's level, neither the goal's object nor holding the goal).</summary>
    public IReadOnlyList<NavBody> Seen => _seen;

    /// <summary>The bodies stamped on the plan.</summary>
    public IReadOnlyList<NavBody> Stamped => _stamped;

    /// <summary>The bodies in rooms the last refresh left out (<c>TODO(m6-npc-indoor-bodies)</c>).</summary>
    public int Indoor { get; private set; }

    /// <summary>The bodies the last refresh left out because their disc holds the goal point.</summary>
    public int AtGoal { get; private set; }

    /// <summary>The bodies the last refresh left out because they stand on another level than the lattice's ground.</summary>
    public int OffLevel { get; private set; }

    /// <summary>M6 sprint (live 2026-11-19 01:52 UTC, run 3: one body was left out as off its level on every plan of the walk that stood against the Scrivener of Item Magic, and the log could not say which): the bodies the last refresh left out as off level, with the gap in metres between their feet and the lattice's ground at their centre.</summary>
    public IReadOnlyList<(NavBody Body, float Gap)> OffLevelBodies => _offLevel;

    /// <summary>A body's disc: the walker's radius (<see cref="NavCreatures.PlayerRadiusMetres"/>) plus the body's own.</summary>
    /// <param name="bodyRadius">The body's own radius.</param>
    public static float DiscRadius(float bodyRadius) => NavCreatures.PlayerRadiusMetres + bodyRadius;

    /// <summary>The distance from <paramref name="p"/> to the segment <paramref name="a"/>-<paramref name="b"/>.</summary>
    public static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var len2 = ab.LengthSquared();
        var t = len2 <= 1e-8f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        return Vector2.Distance(p, a + (ab * t));
    }

    /// <summary>
    /// Reads the bodies within <paramref name="maxMetres"/> of <paramref name="from"/> and keeps those on the plan: not <paramref name="ignore"/> (the goal's object),
    /// outdoors, not holding <paramref name="goal"/> in their disc, and, over <paramref name="map"/>, within <see cref="HeightBandMetres"/> of the ground at their
    /// centre. Clears the stamped list.
    /// </summary>
    public void Refresh(INavBodyFeed feed, Vector2 from, float maxMetres, uint ignore, Vector2 goal, NavMap? map)
    {
        ArgumentNullException.ThrowIfNull(feed);
        feed.Fill(from, maxMetres, _fill);
        _seen.Clear();
        _stamped.Clear();
        _offLevel.Clear();
        Indoor = 0;
        AtGoal = 0;
        OffLevel = 0;
        foreach (var b in _fill)
        {
            if (b.Guid == ignore && ignore != 0)
            {
                continue;
            }

            if (!b.Outdoor)
            {
                Indoor++;
                continue;
            }

            if (Vector2.Distance(goal, b.Centre) <= DiscRadius(b.Radius))
            {
                AtGoal++;
                continue;
            }

            if (map is not null && map.TryGetNode(NavPoint.FromGlobal(b.Centre, b.Z), out var node) && MathF.Abs(node.Z - b.Z) > HeightBandMetres)
            {
                OffLevel++;
                _offLevel.Add((b, b.Z - node.Z));
                continue;
            }

            _seen.Add(b);
        }
    }

    /// <summary>Stamps the seen bodies within <see cref="RouteMetres"/> of the line <paramref name="a"/>-<paramref name="b"/>; returns how many it added.</summary>
    public int SelectNear(Vector2 a, Vector2 b)
    {
        var added = 0;
        foreach (var body in _seen)
        {
            if (!IsStamped(body.Guid) && SegmentDistance(body.Centre, a, b) <= RouteMetres)
            {
                _stamped.Add(body);
                added++;
            }
        }

        return added;
    }

    /// <summary>Stamps the seen bodies within <see cref="RouteMetres"/> of a segment of <paramref name="path"/>; returns how many it added.</summary>
    public int SelectNear(IReadOnlyList<NavWaypoint> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var added = 0;
        foreach (var body in _seen)
        {
            if (IsStamped(body.Guid))
            {
                continue;
            }

            for (var i = 0; i + 1 < path.Count; i++)
            {
                if (SegmentDistance(body.Centre, NavMap.FromCellFrame(path[i].At).Global, NavMap.FromCellFrame(path[i + 1].At).Global) <= RouteMetres)
                {
                    _stamped.Add(body);
                    added++;
                    break;
                }
            }
        }

        return added;
    }

    /// <summary>Whether the body <paramref name="guid"/> is stamped.</summary>
    public bool IsStamped(uint guid)
    {
        foreach (var b in _stamped)
        {
            if (b.Guid == guid)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gives <paramref name="scars"/> the stamped bodies' discs (<see cref="NavScars.SetBodies"/>).</summary>
    public void Stamp(NavScars scars)
    {
        ArgumentNullException.ThrowIfNull(scars);
        _discs.Clear();
        foreach (var b in _stamped)
        {
            _discs.Add((b.Centre, DiscRadius(b.Radius)));
        }

        scars.SetBodies(_discs);
    }

    /// <summary>Forgets the bodies (a run without a feed, or a new run).</summary>
    public void Clear()
    {
        _fill.Clear();
        _seen.Clear();
        _stamped.Clear();
        Indoor = 0;
        AtGoal = 0;
        OffLevel = 0;
    }

    /// <summary>
    /// The first seen body, in the order of the path, whose disc (less <see cref="MeetSlackMetres"/>) a segment of <paramref name="path"/> that starts outdoors
    /// passes through at the body's level, or null. A body whose disc holds <paramref name="start"/> is not met: the body stands beside it already, and the plan
    /// lets the route leave a disc the start is in.
    /// </summary>
    public NavBody? FirstMeeting(IReadOnlyList<NavWaypoint> path, Vector2 start)
    {
        ArgumentNullException.ThrowIfNull(path);
        for (var i = 0; i + 1 < path.Count; i++)
        {
            if (path[i].Indoor)
            {
                continue;
            }

            var a = NavMap.FromCellFrame(path[i].At).Global;
            var b = NavMap.FromCellFrame(path[i + 1].At).Global;
            var za = path[i].At.Origin.Z;
            var zb = path[i + 1].At.Origin.Z;
            foreach (var body in _seen)
            {
                var disc = DiscRadius(body.Radius);
                if (Vector2.Distance(start, body.Centre) <= disc)
                {
                    continue;
                }

                if (SegmentDistance(body.Centre, a, b) < disc - MeetSlackMetres && MathF.Abs(body.Z - MathF.Min(za, zb)) <= HeightBandMetres + MathF.Abs(zb - za))
                {
                    return body;
                }
            }
        }

        return null;
    }
}
