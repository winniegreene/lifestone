using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.World;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Casting;

/// <summary>The answer of a shot check.</summary>
public enum ShotAnswer
{
    /// <summary>The bolt can fly to the target.</summary>
    Clear,

    /// <summary>The world stops the bolt: do not cast.</summary>
    Blocked,

    /// <summary>No answer (<see cref="ShotVerdict.Why"/> names why): the cast goes on and the wasted-cast count is the backstop.</summary>
    NotChecked,
}

/// <summary>A shot check's answer and its reason.</summary>
/// <param name="Answer">The answer.</param>
/// <param name="Why">The reason, for the log.</param>
public readonly record struct ShotVerdict(ShotAnswer Answer, string Why);

/// <summary>M6 8 L: asks, before a bolt is paid for, whether it can reach its target. Both fights ask through <see cref="Actions.ActionApi.Shots"/>.</summary>
public interface IShotCheck
{
    /// <summary>Whether the bolt of <paramref name="spellId"/> can fly from the player to <paramref name="target"/> now.</summary>
    /// <param name="session">The session.</param>
    /// <param name="spellId">The bolt's spell.</param>
    /// <param name="target">The target's guid.</param>
    ShotVerdict Check(IGameSessionView session, uint spellId, uint target);
}

/// <summary>The sizes of a body that a bolt's launch reads.</summary>
/// <param name="Height">The Setup's height times the scale (<c>PhysicsObj.GetHeight</c>).</param>
/// <param name="PhysicsRadius">The first cylinder-sphere's radius, else the first sphere's, times the scale (<c>PhysicsObj.GetPhysicsRadius</c>).</param>
/// <param name="Radius">The Setup's radius times the scale (<c>PhysicsObj.GetRadius</c>).</param>
public readonly record struct ShotBody(float Height, float PhysicsRadius, float Radius);

/// <summary>The sizes of a bolt that its launch reads.</summary>
/// <param name="Sphere">The first collision sphere, scaled (<c>WorldObject.GetProjectileRadius</c> reads its radius).</param>
/// <param name="Height">The Setup's height times the scale.</param>
public readonly record struct BoltBody(PhysSphere Sphere, float Height);

/// <summary>
/// M6 8 L: where ACE starts a straight bolt and where its launch test must reach, as pure sums. <c>CalculateProjectileOrigins</c> and <c>CalculatePreOffset</c> (@95ef426d WorldObjects/WorldObject_Magic.cs:1569, 1528) and <c>IsProjectileVisible</c> (WorldObject.cs:402).
/// A bolt spell's create offset is zero (SELECT create_Offset_Origin_X, _Y, _Z FROM spell in the pinned world: Flame, Frost, Acid, Lightning and Blade). An arc spell starts at the full height, not at 2/3 (<c>TODO(m6-shot-arc-start)</c>): the ladder's bolts are straight.
/// </summary>
public static class BoltLaunch
{
    /// <summary>ACE's <c>ProjHeight</c> (WorldObject_Magic.cs:1526), the share of a body's height a bolt leaves from and aims at.</summary>
    public const float ProjHeight = 2f / 3f;

    /// <summary>ACE's contact distance (WorldObject_Magic.cs:1588): nearer than this the bolt starts one body-and-bolt radius out instead of two.</summary>
    public const float ContactMetres = 0.6f;

    /// <summary>ACE's <c>Position.CylinderDistance</c> (Physics/Common/Position.cs) between the two bodies.</summary>
    /// <param name="a">The first body's feet.</param>
    /// <param name="bodyA">The first body.</param>
    /// <param name="b">The second body's feet.</param>
    /// <param name="bodyB">The second body.</param>
    public static float CylinderDistance(in CellFrame a, in ShotBody bodyA, in CellFrame b, in ShotBody bodyB)
    {
        var offset = a.GetOffset(b);
        var reach = offset.Length() - (bodyA.Radius + bodyB.Radius);
        var diffZ = offset.Z >= 0f ? offset.Z - bodyA.Height : -offset.Z - bodyB.Height;
        if (diffZ > 0 && reach > 0)
        {
            return MathF.Sqrt((diffZ * diffZ) + (reach * reach));
        }

        return diffZ < 0 && reach < 0 ? -MathF.Sqrt((diffZ * diffZ) + (reach * reach)) : reach;
    }

    /// <summary>The bolt's start, in the caster's cell.</summary>
    /// <param name="caster">The caster's feet.</param>
    /// <param name="me">The caster's body.</param>
    /// <param name="target">The target's feet.</param>
    /// <param name="it">The target's body.</param>
    /// <param name="bolt">The bolt.</param>
    public static CellFrame Start(in CellFrame caster, in ShotBody me, in CellFrame target, in ShotBody it, in BoltBody bolt)
    {
        var rise = me.Height * ProjHeight;
        var feet = caster.GetOffset(target);
        var aim = new Vector3(feet.X, feet.Y, feet.Z + (it.Height * ProjHeight) - rise);
        var flat = MathF.Sqrt((aim.X * aim.X) + (aim.Y * aim.Y));
        var slope = new Vector3(0f, flat, aim.Z);
        var localDir = slope.LengthSquared() > 1e-8f ? Vector3.Normalize(slope) : Vector3.UnitY;
        var single = me.PhysicsRadius + bolt.Sphere.Radius;
        var preOffset = new Vector3(0f, 0f, rise) + (localDir * single) - (Vector3.UnitY * single);
        var radsum = CylinderDistance(in caster, in me, in target, in it) < ContactMetres ? single : 2f * single;
        var local = new Vector3(0f, radsum, 0f) + preOffset;
        var heading = flat > 1e-4f ? new Vector2(aim.X / flat, aim.Y / flat) : Vector2.UnitY;
        var origin = caster.Frame.Origin + new Vector3(heading.X * local.Y, heading.Y * local.Y, local.Z);
        return new CellFrame(caster.CellId, caster.Frame with { Origin = origin });
    }

    /// <summary>
    /// Where the launch test's path ends: the target raised to its eye level (<c>IsProjectileVisible</c>: its height less the bolt's), brought back along the path by the two radii, because on the server the path ends on the target's body and a low ceiling over the eye must not read as a wall.
    /// </summary>
    /// <param name="start">The bolt's start.</param>
    /// <param name="target">The target's feet.</param>
    /// <param name="it">The target's body.</param>
    /// <param name="bolt">The bolt.</param>
    public static CellFrame End(in CellFrame start, in CellFrame target, in ShotBody it, in BoltBody bolt)
    {
        var eye = new CellFrame(target.CellId, target.Frame with { Origin = target.Frame.Origin + new Vector3(0f, 0f, it.Height - bolt.Height) });
        var path = start.GetOffset(eye);
        var length = path.Length();
        var back = it.PhysicsRadius + bolt.Sphere.Radius;
        return length <= back ? eye : new CellFrame(eye.CellId, eye.Frame with { Origin = eye.Frame.Origin - (path * (back / length)) });
    }

    /// <summary>The end of the line the bolt really flies: the target at 2/3 of its height (<c>CalculateProjectileVelocity</c>, WorldObject_Magic.cs:1724), brought back along the path by the two radii.</summary>
    /// <param name="start">The bolt's start.</param>
    /// <param name="target">The target's feet.</param>
    /// <param name="it">The target's body.</param>
    /// <param name="bolt">The bolt.</param>
    public static CellFrame FlightEnd(in CellFrame start, in CellFrame target, in ShotBody it, in BoltBody bolt)
    {
        var aim = new CellFrame(target.CellId, target.Frame with { Origin = target.Frame.Origin + new Vector3(0f, 0f, it.Height * ProjHeight) });
        var path = start.GetOffset(aim);
        var length = path.Length();
        var back = it.PhysicsRadius + bolt.Sphere.Radius;
        return length <= back ? aim : new CellFrame(aim.CellId, aim.Frame with { Origin = aim.Frame.Origin - (path * (back / length)) });
    }
}

/// <summary>M6 8 L: the shot check over the player's own physics world and the dats' Setups (<see cref="PhysicsWorld.SweepBolt"/>).</summary>
public sealed class PhysicsShotCheck : IShotCheck
{
    /// <summary>The cylinder distance under which a target is at arm's length and no check is run (Lifestone's own: a biter must always be answered, and nothing fits between the two).</summary>
    public const float TouchingMetres = 1.0f;

    private readonly Func<PhysicsWorld?> _world;
    private readonly DatSet _dats;
    private readonly PhysicsGeometryLibrary _geometry;
    private readonly Dictionary<(uint Setup, float Scale), ShotBody?> _bodies = [];
    private readonly Dictionary<uint, BoltBody?> _bolts = [];

    /// <summary>A check over the world <paramref name="world"/> gives (null until the player is built).</summary>
    /// <param name="world">The player's physics world.</param>
    /// <param name="dats">The dats the Setups are read from.</param>
    public PhysicsShotCheck(Func<PhysicsWorld?> world, DatSet dats)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _dats = dats ?? throw new ArgumentNullException(nameof(dats));
        _geometry = new PhysicsGeometryLibrary(dats);
    }

    /// <inheritdoc />
    public ShotVerdict Check(IGameSessionView session, uint spellId, uint target)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_world() is not { } world)
        {
            return new ShotVerdict(ShotAnswer.NotChecked, "no physics world yet");
        }

        var objects = session.State.Objects;
        if (session.State.Self.Position is not { } caster || !objects.TryGet(target, out var victim) || victim.Position is not { } at)
        {
            return new ShotVerdict(ShotAnswer.NotChecked, "a position is unknown");
        }

        if (!objects.TryGet(session.State.Self.Guid, out var self) || BodyOf(self) is not { } me)
        {
            return new ShotVerdict(ShotAnswer.NotChecked, "TODO(m6-shot-no-setup): the player's own Setup is unknown");
        }

        if (BodyOf(victim) is not { } it)
        {
            return new ShotVerdict(ShotAnswer.NotChecked, "TODO(m6-shot-no-setup): the target's Setup is unknown");
        }

        if (BoltOf(spellId) is not { } bolt)
        {
            return new ShotVerdict(ShotAnswer.NotChecked, string.Create(CultureInfo.InvariantCulture, $"TODO(m6-shot-no-setup): spell {spellId} has no projectile Setup in the world extract"));
        }

        var gap = BoltLaunch.CylinderDistance(in caster, in me, in at, in it);
        return gap < TouchingMetres
            ? new ShotVerdict(ShotAnswer.NotChecked, string.Create(CultureInfo.InvariantCulture, $"touching ({gap:0.00} m apart)"))
            : Sweep(world, in caster, in me, in at, in it, in bolt);
    }

    /// <summary>The sweep alone, on known sizes (the dat tests read it too).</summary>
    /// <param name="world">The physics world.</param>
    /// <param name="caster">The caster's feet.</param>
    /// <param name="me">The caster's body.</param>
    /// <param name="at">The target's feet.</param>
    /// <param name="it">The target's body.</param>
    /// <param name="bolt">The bolt.</param>
    public static ShotVerdict Sweep(PhysicsWorld world, in CellFrame caster, in ShotBody me, in CellFrame at, in ShotBody it, in BoltBody bolt)
    {
        ArgumentNullException.ThrowIfNull(world);
        var start = BoltLaunch.Start(in caster, in me, in at, in it, in bolt);
        var end = BoltLaunch.End(in start, in at, in it, in bolt);
        var sphere = bolt.Sphere;
        var swept = world.SweepBolt(in start, in end, in sphere);
        if (swept.Line == BoltLine.Clear)
        {
            // The launch test aims at the eye and the bolt flies at 2/3 of the target's height (CalculateProjectileVelocity, WorldObject_Magic.cs:1724), so a hill can pass the one and stop the other (gate A's bolts that died on a hill were launched: the server's test had passed them).
            var flight = BoltLaunch.FlightEnd(in start, in at, in it, in bolt);
            var flown = world.SweepBolt(in start, in flight, in sphere);
            if (flown.Line == BoltLine.Blocked)
            {
                return new ShotVerdict(ShotAnswer.Blocked, string.Create(CultureInfo.InvariantCulture, $"the launch test passes but the flight line is stopped {start.GetOffset(flown.Reached).Length():0.0} m out, in 0x{flown.Reached.CellId:X8}"));
            }
        }

        var from = string.Create(CultureInfo.InvariantCulture, $"from 0x{start.CellId:X8} [{start.Frame.Origin.X:0.0} {start.Frame.Origin.Y:0.0} {start.Frame.Origin.Z:0.0}] to [{end.Frame.Origin.X:0.0} {end.Frame.Origin.Y:0.0} {end.Frame.Origin.Z:0.0}]");
        return swept.Line switch
        {
            BoltLine.Clear => new ShotVerdict(ShotAnswer.Clear, from),
            BoltLine.Blocked => new ShotVerdict(ShotAnswer.Blocked, string.Create(CultureInfo.InvariantCulture, $"{from}: the world stops it {start.GetOffset(swept.Reached).Length():0.0} m out, in 0x{swept.Reached.CellId:X8}")),
            _ => new ShotVerdict(ShotAnswer.NotChecked, from + ": a landblock of the path is not attached"),
        };
    }

    private ShotBody? BodyOf(WorldObject o)
    {
        if (o.Physics.SetupId is not { } setupId)
        {
            return null;
        }

        var scale = o.Physics.Scale ?? 1f;
        if (_bodies.TryGetValue((setupId, scale), out var known))
        {
            return known;
        }

        ShotBody? body = PhysicsSetup.Load(_dats, setupId, _geometry) is { } setup
            ? new ShotBody(setup.Height * scale, (setup.HasPhysicsBsp ? 0f : setup.CylSpheres.Count > 0 ? setup.CylSpheres[0].Radius : setup.Spheres.Count > 0 ? setup.Spheres[0].Radius : 0f) * scale, setup.Radius * scale)
            : null;
        _bodies[(setupId, scale)] = body;
        return body;
    }

    private BoltBody? BoltOf(uint spellId)
    {
        if (_bolts.TryGetValue(spellId, out var known))
        {
            return known;
        }

        BoltBody? bolt = null;
        if (AceWorldExtract.Load().ProjectilesBySpell.TryGetValue(spellId, out var entry) && PhysicsSetup.Load(_dats, entry.SetupId, _geometry) is { Spheres.Count: > 0 } setup)
        {
            var scale = entry.DefaultScale ?? 1f;
            var first = setup.Spheres[0];
            bolt = new BoltBody(new PhysSphere(first.Center * scale, first.Radius * scale), setup.Height * scale);
        }

        _bolts[spellId] = bolt;
        return bolt;
    }
}

/// <summary>What a shot drop was measured by (<see cref="ShotLedger.Drop(uint, string, double, string, ShotDropKind, ShotPlace?)"/>).</summary>
public enum ShotDropKind
{
    /// <summary>The shot check before the cast found the bolt's path blocked: no bolt was sent, nothing was paid. A new check from another place costs nothing.</summary>
    Blocked,

    /// <summary><see cref="ShotLedger.WastedBeforeDrop"/> casts in a row came to nothing: the bolts flew (or were cast) and no damage landed. The check did not see what stopped them.</summary>
    Wasted,
}

/// <summary>
/// M6 fight targeting (run 9): where a shot drop was measured: the caster's feet and the target's place as the fight read them at the drop (her own position; the creature where the fight takes it to stand).
/// </summary>
/// <param name="Caster">Where she stood.</param>
/// <param name="Target">Where the target stood.</param>
public readonly record struct ShotPlace(CellFrame Caster, CellFrame Target)
{
    /// <summary>How far apart the two stood, in metres.</summary>
    public double Metres => Caster.ToGlobal().DistanceTo(Target.ToGlobal());
}

/// <summary>
/// M6 8 L: the one rule both fights share. Before a bolt, the shot check is asked and its answer logged (<c>shots_blocked=</c>); after one, a cast that ended with no bolt or no damage landing is counted per target (<c>wasted_casts=</c>), and at <see cref="WastedBeforeDrop"/> in a row the target is
/// left alone for <see cref="DropSeconds"/>, logged with its name, the count and the reason. A cast the check called clear that the server did not let fly is a disagreement (<c>shot_disagreements=</c>), which measures the check.
/// </summary>
/// <remarks>
/// M6 fight targeting (run 9, 2026-11-20 19:51 to 2026-11-21 04:58 UTC: 12 drops, every one by the check before the cast, measured with the Tusker Slave 3.9 to 21.5 m off; the 102 life-cast decisions at them came with it
/// 1.7 to 11.7 m off, after she or it had moved, while each drop stood its 120 s; docs/session.md section 120). A drop belongs to where it was measured: a drop made with its <see cref="ShotPlace"/> ends before its time
/// (<see cref="IsDropped(uint, double, in CellFrame, in CellFrame, double)"/>) when she has moved since, when the target has moved since (a drop by the check only: a new check costs nothing, where a drop for wasted casts
/// would pay two more casts each time a sticky creature's drawn body shifts, <c>TODO(m6-sticky-walk-back)</c>), or when a target measured farther than <see cref="CloseMetres"/> has come within it.
/// </remarks>
/// <param name="log">The fight's log.</param>
public sealed class ShotLedger(Action<string> log)
{
    private readonly record struct DropRecord(double Until, ShotDropKind Kind, ShotPlace? Place);

    /// <summary>The wasted casts in a row on one target before it is dropped (two: each costs about 5 mana and 4 s, and the first can be a target caught at a corner).</summary>
    public const int WastedBeforeDrop = 2;

    /// <summary>How long a dropped target stays out, in seconds (the climb's own wall block, <c>FightScenario.WallBlockSeconds</c>).</summary>
    public const double DropSeconds = 120.0;

    /// <summary>An attacker within this many metres is back from its drop (Lifestone's own: the long round's biter stood at 1.2 to 1.6 m, the ledge cases at 6 m).</summary>
    public const double CloseMetres = 3.0;

    private readonly Dictionary<uint, int> _wasted = [];
    private readonly Dictionary<uint, DropRecord> _dropped = [];
    private uint _calledClear;
    private bool _noCheckLogged;

    /// <summary>The casts that ended with no bolt or no damage landing.</summary>
    public int WastedCasts { get; private set; }

    /// <summary>The casts not sent because the check found the shot blocked.</summary>
    public int ShotsBlocked { get; private set; }

    /// <summary>The casts the check called clear that the server did not let fly.</summary>
    public int Disagreements { get; private set; }

    /// <summary>The three fields a round reads.</summary>
    public string Fields => string.Create(CultureInfo.InvariantCulture, $"wasted_casts={WastedCasts} shots_blocked={ShotsBlocked} shot_disagreements={Disagreements}");

    /// <summary>Asks <paramref name="check"/> about the shot and logs the answer with its reason.</summary>
    /// <param name="check">The host's check, or null.</param>
    /// <param name="session">The session.</param>
    /// <param name="spellId">The bolt's spell.</param>
    /// <param name="target">The target's guid.</param>
    /// <param name="name">The target's name, for the log.</param>
    public ShotVerdict Before(IShotCheck? check, IGameSessionView session, uint spellId, uint target, string name)
    {
        _calledClear = 0;
        if (check is null)
        {
            if (!_noCheckLogged)
            {
                _noCheckLogged = true;
                log("shot check: this host has no shot check: every shot goes unchecked; the wasted-cast count is the only guard");
            }

            return new ShotVerdict(ShotAnswer.NotChecked, "no shot check on this host");
        }

        var verdict = check.Check(session, spellId, target);
        if (verdict.Answer == ShotAnswer.Clear)
        {
            _calledClear = target;
        }
        else if (verdict.Answer == ShotAnswer.Blocked)
        {
            ShotsBlocked++;
        }

        log(string.Create(CultureInfo.InvariantCulture, $"shot check: {verdict.Answer} at 0x{target:X8} ({name}), spell {spellId}: {verdict.Why} (shots_blocked {ShotsBlocked})"));
        return verdict;
    }

    /// <summary>Counts a disagreement when the check called this shot clear, once a shot.</summary>
    /// <param name="target">The target's guid.</param>
    /// <param name="name">The target's name.</param>
    /// <param name="why">What the server did.</param>
    public void NoteDisagreement(uint target, string name, string why)
    {
        if (target == 0 || _calledClear != target)
        {
            return;
        }

        _calledClear = 0;
        Disagreements++;
        log($"shot check: DISAGREEMENT at 0x{target:X8} ({name}): the check said clear and the bolt did not reach it: {why} (shot_disagreements {Disagreements})");
    }

    /// <summary>Counts a wasted cast; true when this one dropped the target.</summary>
    /// <param name="target">The target's guid.</param>
    /// <param name="name">The target's name.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="why">How the cast ended.</param>
    public bool NoteWasted(uint target, string name, double clock, string why) => NoteWasted(target, name, clock, why, null);

    /// <summary>Counts a wasted cast; true when this one dropped the target, the drop kept with <paramref name="place"/> (where she and the target stood: null when a position is not known).</summary>
    /// <param name="target">The target's guid.</param>
    /// <param name="name">The target's name.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="why">How the cast ended.</param>
    /// <param name="place">Where she and the target stand now.</param>
    public bool NoteWasted(uint target, string name, double clock, string why, ShotPlace? place)
    {
        NoteDisagreement(target, name, why);
        WastedCasts++;
        var count = _wasted.GetValueOrDefault(target) + 1;
        _wasted[target] = count;
        log(string.Create(CultureInfo.InvariantCulture, $"wasted cast {count} of {WastedBeforeDrop} on 0x{target:X8} ({name}): {why} (wasted_casts {WastedCasts})"));
        if (count < WastedBeforeDrop)
        {
            return false;
        }

        Drop(target, name, clock, string.Create(CultureInfo.InvariantCulture, $"{count} casts in a row wasted, the last: {why}"), ShotDropKind.Wasted, place);
        return true;
    }

    /// <summary>A bolt landed on <paramref name="target"/>: it is kept, and its count starts again.</summary>
    /// <param name="target">The target's guid.</param>
    public void NoteDamage(uint target) => _wasted.Remove(target);

    /// <summary>Leaves <paramref name="target"/> alone for <see cref="DropSeconds"/>.</summary>
    /// <param name="target">The target's guid.</param>
    /// <param name="name">The target's name.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="why">The reason.</param>
    public void Drop(uint target, string name, double clock, string why) => Drop(target, name, clock, why, ShotDropKind.Blocked, null);

    /// <summary>
    /// Leaves <paramref name="target"/> alone for <see cref="DropSeconds"/>, or (with <paramref name="place"/>) until she or it has moved from where the drop was measured (<see cref="IsDropped(uint, double, in CellFrame, in CellFrame, double)"/>).
    /// </summary>
    /// <param name="target">The target's guid.</param>
    /// <param name="name">The target's name.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="why">The reason.</param>
    /// <param name="kind">What measured it.</param>
    /// <param name="place">Where she and the target stood, or null when a position is not known (the drop then holds by its time and the close rule alone).</param>
    public void Drop(uint target, string name, double clock, string why, ShotDropKind kind, ShotPlace? place)
    {
        _wasted.Remove(target);
        _dropped[target] = new DropRecord(clock + DropSeconds, kind, place);
        var where = place is { } p
            ? string.Create(CultureInfo.InvariantCulture, $"; measured with her at 0x{p.Caster.CellId:X8} [{p.Caster.Origin.X:0.0} {p.Caster.Origin.Y:0.0} {p.Caster.Origin.Z:0.0}] and it at 0x{p.Target.CellId:X8} [{p.Target.Origin.X:0.0} {p.Target.Origin.Y:0.0} {p.Target.Origin.Z:0.0}], {p.Metres:0.0} m apart: it ends sooner when she{(kind == ShotDropKind.Blocked ? " or it" : string.Empty)} moves from there{(p.Metres > CloseMetres ? string.Create(CultureInfo.InvariantCulture, $" or it comes within {CloseMetres:0} m") : string.Empty)} (m6-shot-drop-place)")
            : string.Empty;
        log(string.Create(CultureInfo.InvariantCulture, $"shot drop: 0x{target:X8} ({name}) is left alone for {DropSeconds:0} s: {why}{where}"));
    }

    /// <summary>True while <paramref name="target"/> is dropped.</summary>
    /// <param name="target">The target's guid.</param>
    /// <param name="clock">The clock.</param>
    public bool IsDropped(uint target, double clock)
    {
        if (!_dropped.TryGetValue(target, out var drop))
        {
            return false;
        }

        if (clock < drop.Until)
        {
            return true;
        }

        _dropped.Remove(target);
        log($"shot drop: 0x{target:X8} is back: its {DropSeconds:0} s are up");
        return false;
    }

    /// <summary>What measured the drop that stands on <paramref name="target"/>; false when none stands (its time is not read: ask <see cref="IsDropped(uint, double)"/> first).</summary>
    /// <param name="target">The target's guid.</param>
    /// <param name="kind">What measured it.</param>
    public bool DropKindOf(uint target, out ShotDropKind kind)
    {
        kind = ShotDropKind.Blocked;
        if (!_dropped.TryGetValue(target, out var drop))
        {
            return false;
        }

        kind = drop.Kind;
        return true;
    }

    /// <summary>
    /// M6 fight targeting (run 9): true while <paramref name="target"/> stays dropped from where she stands now. A drop made with its <see cref="ShotPlace"/> belongs to that place: it ends, logged with the
    /// numbers, when she has moved more than <paramref name="movedMetres"/> from where she stood, when the target has moved more than that from where it stood (a drop by the check before the cast only: the next check is
    /// free, see the class remarks), or when a target measured farther than <see cref="CloseMetres"/> has come within it. A target measured within it stays dropped while neither moves: the check was made from this very
    /// place, and taking the drop back each frame would ask the same blocked check again and cast nothing. A drop made with no place keeps the older rule (<see cref="IsDropped(uint, double, double)"/>).
    /// </summary>
    /// <param name="target">The target's guid.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="caster">Where she stands now.</param>
    /// <param name="at">Where the target stands now.</param>
    /// <param name="movedMetres">The least move that is a new place, in metres (the caller's measured threshold).</param>
    public bool IsDropped(uint target, double clock, in CellFrame caster, in CellFrame at, double movedMetres)
    {
        if (!IsDropped(target, clock))
        {
            return false;
        }

        var metres = caster.ToGlobal().DistanceTo(at.ToGlobal());
        if (_dropped[target].Place is not { } place)
        {
            return IsDropped(target, clock, metres);
        }

        var kind = _dropped[target].Kind;
        var herMove = place.Caster.ToGlobal().DistanceTo(caster.ToGlobal());
        var itsMove = place.Target.ToGlobal().DistanceTo(at.ToGlobal());
        string? why = herMove > movedMetres ? string.Create(CultureInfo.InvariantCulture, $"she has moved {herMove:0.0} m from where it was measured (more than {movedMetres:0.0} m)")
            : kind == ShotDropKind.Blocked && itsMove > movedMetres ? string.Create(CultureInfo.InvariantCulture, $"it has moved {itsMove:0.0} m from where it was measured (more than {movedMetres:0.0} m)")
            : metres <= CloseMetres && place.Metres > CloseMetres ? string.Create(CultureInfo.InvariantCulture, $"it has come within {CloseMetres:0} m (measured {place.Metres:0.0} m off): an attacker this close is fought (M5 D88)")
            : null;
        if (why is null)
        {
            return true;
        }

        _dropped.Remove(target);
        log(string.Create(CultureInfo.InvariantCulture, $"shot drop: 0x{target:X8} is back: {why}; it stands {metres:0.0} m away now, the {(kind == ShotDropKind.Blocked ? "check" : "wasted casts")} belonged to the place it was measured at (m6-shot-drop-place)"));
        return false;
    }

    /// <summary>
    /// True while <paramref name="target"/>, an attacker <paramref name="metres"/> away, stays dropped. One that has come within <see cref="CloseMetres"/> is back at once, its drop ended and logged: what kept the bolts off it (a ledge, a corner) is no reason to stand and be bitten (M5 D88; M6 8's long round: a dropped drudge at 1.6 m stopped the walk nine times while the fight refused it).
    /// </summary>
    /// <param name="target">The attacker's guid.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="metres">How far away it stands.</param>
    public bool IsDropped(uint target, double clock, double metres)
    {
        if (!IsDropped(target, clock))
        {
            return false;
        }

        if (metres > CloseMetres)
        {
            return true;
        }

        _dropped.Remove(target);
        log(string.Create(CultureInfo.InvariantCulture, $"shot drop: 0x{target:X8} is back: it stands {metres:0.0} m away, within {CloseMetres:0} m: an attacker this close is fought (M5 D88)"));
        return false;
    }

    /// <summary>Adds the targets dropped now to a pick's exclusions.</summary>
    /// <param name="excluded">The pick's exclusions.</param>
    /// <param name="clock">The clock.</param>
    public void AddDroppedTo(List<uint> excluded, double clock)
    {
        ArgumentNullException.ThrowIfNull(excluded);
        if (_dropped.Count == 0)
        {
            return; // the pick runs every frame it waits: nothing to do allocates nothing
        }

        foreach (var guid in _dropped.Keys.ToList())
        {
            if (IsDropped(guid, clock))
            {
                excluded.Add(guid);
            }
        }
    }
}
