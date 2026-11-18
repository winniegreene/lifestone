using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Tools;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 7 AM (gate A, stretch 3: a Flame Bolt stopped short of an outdoor target 11 times in 20 kills, and each time the fight took the tour's next cell and left a live target): outdoors, a bolt that hits the world keeps the
/// fight on its target. The M5 7 F23 rule, "a bolt that hits a wall walks on" (<see cref="StartWalk"/> to the tour), is the right answer in a dungeon, where the next room is the way to a line; outdoors the tour's cell is
/// 50 or more metres off and the target stands where it stood. Here the mage walks toward the target, to a spot with a better line (closer; the second time to the side as well), and the pick shoots again. After
/// <see cref="WallHitsBeforeBlock"/> wall hits on one target it is blocked for <see cref="WallBlockSeconds"/> and the pick takes the next. A dungeon keeps the old rule.
/// </summary>
public sealed partial class FightScenario
{
    /// <summary>The wall hits on one outdoor target before the pick gives it up for <see cref="WallBlockSeconds"/> (Lifestone's own bound: two tries from nearer, one from the side, then the next target).</summary>
    public const int WallHitsBeforeBlock = 3;

    /// <summary>How long a target that hit the bolt into the world three times stays out of the pick, in seconds.</summary>
    public const double WallBlockSeconds = 120.0;

    /// <summary>The nearest the stay rule walks to a target, in metres: close enough for any bolt to find it, far enough not to be in a melee creature's arms before the first bolt.</summary>
    public const double WallStayStandOffMetres = 10.0;

    /// <summary>The fewest metres a stay step closes (a bolt that died 15 m out has a wall in those 15 m; half the way, at least this).</summary>
    public const double WallStayMinStepMetres = 8.0;

    /// <summary>The metres the second step goes to the side (a tree or a rock is passed by a step across the line, not along it).</summary>
    public const double WallStaySideMetres = 6.0;

    private readonly Dictionary<uint, int> _wallHitsOn = [];
    private readonly Dictionary<uint, double> _wallBlockedUntil = [];

    /// <summary>The creatures the pick holds out now for their wall hits (guid to the clock they are let back at), for the tests.</summary>
    internal IReadOnlyDictionary<uint, double> WallBlocked => _wallBlockedUntil;

    /// <summary>
    /// The spot the stay rule's <paramref name="hit"/>th step walks to, for a mage at <paramref name="here"/> and a target at <paramref name="at"/> (both outdoors): toward the target by half the way (at least
    /// <see cref="WallStayMinStepMetres"/>, never past <see cref="WallStayStandOffMetres"/> from it), and from the second hit <see cref="WallStaySideMetres"/> across the line, the side alternating. Null when the two stand
    /// in no outdoor cell, or the mage is already within the stand-off and the side step is the only move (hit 1).
    /// </summary>
    internal static CellFrame? WallStaySpot(in CellFrame here, in CellFrame at, int hit)
    {
        if (!here.IsOutdoor || !at.IsOutdoor)
        {
            return null;
        }

        var a = here.ToGlobal();
        var b = at.ToGlobal();
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var flat = Math.Sqrt((dx * dx) + (dy * dy));
        if (flat < 1.0)
        {
            return null;
        }

        var room = Math.Max(0.0, flat - WallStayStandOffMetres);
        var step = Math.Min(room, Math.Max(flat * 0.5, WallStayMinStepMetres));
        var side = hit >= 2 ? (hit % 2 == 0 ? WallStaySideMetres : -WallStaySideMetres) : 0.0;
        if (step <= 0.0 && side == 0.0)
        {
            return null;
        }

        var (ux, uy) = (dx / flat, dy / flat);
        var spot = new GlobalPosition(a.X + (ux * step) - (uy * side), a.Y + (uy * step) + (ux * side), 0);
        return AcCoordinates.TryGetOutdoorCell(spot, out var cell)
            ? new CellFrame(cell.CellId, new AcFrame(new System.Numerics.Vector3(cell.Local.X, cell.Local.Y, float.NaN), here.Frame.Orientation)) // the height is the walk's to read: NaN asks for the terrain's (IFightWalk.BeginLegTo)
            : null;
    }

    /// <summary>
    /// The wall-hit branch of the hit wait outdoors (see the class remarks). True when it took the frame: a leg toward a better line began (the Walk phase runs it, then the pick shoots again), or the target is blocked and the
    /// pick goes on. False for a dungeon, or when no spot can be planned (then the old rule, the tour's next leg, runs and the log names why).
    /// </summary>
    private bool WallStay(IGameSessionView session, double clock)
    {
        if (!_ground.IsOutdoorFight() || _walk is null || _target == 0)
        {
            return false;
        }

        var target = _target;
        var hit = _wallHitsOn.GetValueOrDefault(target) + 1;
        var name = session.State.Objects.TryGet(target, out var victim) ? victim.Name : "unknown";
        if (hit >= WallHitsBeforeBlock)
        {
            _wallHitsOn.Remove(target);
            _wallBlockedUntil[target] = clock + WallBlockSeconds;
            _missesInARow = 0;
            _phase = Phase.Choose;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"wall stay: wall hit {hit} of {WallHitsBeforeBlock} on 0x{target:X8} ({name}): it is blocked for {WallBlockSeconds:0} s and the pick takes the next target"));
            return true;
        }

        if (session.State.Self.Position is not { } here || StandsAt(session, victim) is not { } at)
        {
            _log.Warn(LogCategory, $"wall stay: TODO(m6-wall-stay-no-position): the mage or 0x{target:X8} has no position; the tour's next leg runs");
            return false;
        }

        if (WallStaySpot(in here, in at, hit) is not { } spot)
        {
            _log.Warn(LogCategory, $"wall stay: TODO(m6-wall-stay-no-spot): no outdoor spot toward 0x{target:X8} ({name}) from 0x{here.CellId:X8}; the tour's next leg runs");
            return false;
        }

        var away = PositionAgreement.DistanceMetres(in here, in at);
        if (!_walk.BeginLegTo(session, clock, in spot, out var leg))
        {
            _log.Warn(LogCategory, $"wall stay: TODO(m6-wall-stay-no-leg): the walk plans no leg to the spot ({leg}); the tour's next leg runs");
            return false;
        }

        _wallHitsOn[target] = hit;
        _walks++;
        NoteLegFrom(session);
        _missesInARow = 0;
        _holdingLogged = false;
        (_legPurpose, _legNote, _legGuid) = (TourPurpose.None, $"a better line to 0x{target:X8} ({name}) after a wall hit", 0);
        _walkBackCorpse = 0;
        _lootWalkCorpse = 0;
        _walkSight = false;
        _log.Info(LogCategory, string.Create(
            CultureInfo.InvariantCulture,
            $"wall stay: wall hit {hit} of {WallHitsBeforeBlock} on 0x{target:X8} ({name}) {away:0.#} m off: walking toward it ({(hit >= 2 ? "closer and to the side" : "closer")}) to 0x{spot.CellId:X8} [{spot.Origin.X:0.#} {spot.Origin.Y:0.#}] on the terrain; the pick shoots again from there; {leg}"));
        _phase = Phase.Walk;
        return true;
    }

    /// <summary>Adds the targets whose wall hits blocked them to <see cref="_pickExcluded"/> and forgets the ones whose time is up or that are dead.</summary>
    private void ExcludeWallBlocked(IGameSessionView session, double clock)
    {
        Shots.AddDroppedTo(_pickExcluded, clock); // M6 8 L: the targets the shared shot rule dropped (FightScenario.ShotCheck.cs)
        if (_wallBlockedUntil.Count == 0)
        {
            return;
        }

        List<uint>? done = null;
        foreach (var (guid, until) in _wallBlockedUntil)
        {
            if (clock >= until || TargetGone(session.State.Objects, guid))
            {
                (done ??= []).Add(guid);
            }
            else
            {
                _pickExcluded.Add(guid);
            }
        }

        if (done is not null)
        {
            foreach (var guid in done)
            {
                _wallBlockedUntil.Remove(guid);
                _log.Info(LogCategory, $"wall stay: 0x{guid:X8} is back in the pick (its {WallBlockSeconds:0} s are up or it is gone)");
            }
        }
    }
}
