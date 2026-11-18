using System.Globalization;
using Lifestone.Client.Diagnostics;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>Which awareness range a walk keeps off a hostile creature (<c>--walk-awareness</c>, <see cref="WalkAwareness"/>).</summary>
public enum WalkAwarenessMode
{
    /// <summary>The pinned range of the kind, else ACE's default of 18 m (<see cref="CreatureAwareness.MetresOf"/>): what every walk kept before M6 16 E1f. Still an option (<c>--walk-awareness=pinned</c>).</summary>
    Pinned,

    /// <summary>The kind's own range by the world database (the bestiary's record, <see cref="CreatureAwareness.OwnMetresOf"/>); the pin, else ACE's default, for a kind the tables do not hold. The default since M6 16 Q3.</summary>
    Own,
}

/// <summary>
/// M6 16 E1f (docs/automation-engine.md E1 and section 3, "shadow first"; m6-plan.md D139): which awareness range the session's walks keep off a creature, and the shadow that reports what the other range would have
/// changed. One a session, on the object table every creature feed is built over (<c>WorldObjectTable.WalkAwareness</c>), so no host passes it and a kind is named once whatever runner sees it.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> The walker's disc round a creature is its awareness range plus the two bodies (<see cref="NavCreatures.AggroRadius"/>). The range was one of 74 pinned ranges, else ACE's default of 18 m; of the 47 creature
/// kinds the ground profiles name, 37 have another range of their own in the world database, 30 of them farther (to 30 m) and 7 less far (<c>CreatureBehaviourTests</c>). <see cref="Mode"/> says which of the two the walk uses.
/// The default is the kind's own range (M6 16 Q3; a range is only a cost on a route, never a wall: <see cref="NavCreatures.CostPerMetre"/>); <c>--walk-awareness=pinned</c> restores the pinned ranges. The shadow runs in both modes.</para>
/// <para><b>The kind line</b> (<see cref="Kept"/>, the feed's log, category <c>wayfinding</c>): once a kind a session, for a kind whose two ranges differ:
/// <c>awareness-shadow: &lt;name&gt; (class N): the walk keeps R1 m, its own range gives R2 m</c>, the ranges before the two bodies' radii; under <see cref="WalkAwarenessMode.Own"/> R1 is R2 and the line ends
/// <c>; the pinned read gives R m</c>.</para>
/// <para><b>The leg line</b> (<see cref="BeginLeg"/>, <see cref="Seen"/>, <see cref="Attacked"/>, <see cref="EndLeg"/>; a travel leg's guarded walk opens and closes one): a creature of a kind that notices farther than its
/// pinned range counts once a leg when the feed sees it inside its own disc and outside the pinned one, centre to centre, as the layer measures (the server's last report of the creature, the body's place, every refresh of the
/// layer: twice a second and at each plan). That is ground the pinned radius prices as clear while the creature may already have noticed her. The line names the count, how many of them were then among the attackers the leg
/// read (<see cref="Attacked"/>), the closest such pass with its distance and both discs, and the session's totals: <c>mode= passed= attackers= closest_m= session_legs= session_passed= session_attackers=</c>. A leg with no
/// such pass writes no line. Flat, as the outdoor layer is: indoors a creature on another storey counts.</para>
/// <para>Allocates nothing on the feed's path once its sets have grown; a line allocates when it is written. Not thread-safe: game thread.</para>
/// </remarks>
public sealed class WalkAwareness
{
    /// <summary>The option's name.</summary>
    public const string OptionName = "--walk-awareness";

    /// <summary>The token every line of the shadow starts with.</summary>
    public const string Token = "awareness-shadow: ";

    private const string LogCategory = "wayfinding";

    private readonly HashSet<uint> _differing = [];
    private readonly HashSet<uint> _said = [];
    private readonly Dictionary<uint, Pass> _passes = [];
    private bool _announced;

    /// <summary>One creature seen inside its own disc and outside the pinned one on the open leg: its closest such sample.</summary>
    private readonly record struct Pass(uint Wcid, string Name, float Metres, float PinnedRadius, float OwnRadius, bool Attacked);

    /// <summary>Which range the walks keep (<c>--walk-awareness</c>; the session's bootstrap sets it). <see cref="WalkAwarenessMode.Own"/> by default (M6 16 Q3).</summary>
    public WalkAwarenessMode Mode { get; set; } = WalkAwarenessMode.Own;

    /// <summary>The option's word for <see cref="Mode"/>: <c>pinned</c> or <c>own</c>.</summary>
    public string ModeWord => Mode == WalkAwarenessMode.Own ? "own" : "pinned";

    /// <summary>The kinds this session's feeds saw whose own range is not the pinned one.</summary>
    public int KindsDiffering => _differing.Count;

    /// <summary>True between <see cref="BeginLeg"/> and <see cref="EndLeg"/>.</summary>
    public bool LegOpen { get; private set; }

    /// <summary>The legs closed this session (<c>session_legs=</c>).</summary>
    public int Legs { get; private set; }

    /// <summary>The creatures the closed legs passed inside their own disc and outside the pinned one (<c>session_passed=</c>).</summary>
    public int Passed { get; private set; }

    /// <summary>Of <see cref="Passed"/>, those then among a leg's attackers (<c>session_attackers=</c>).</summary>
    public int Attackers { get; private set; }

    /// <summary>
    /// The awareness range the walk keeps off a creature of weenie class <paramref name="wcid"/>, by <see cref="Mode"/>: <paramref name="pinnedMetres"/> or <paramref name="ownMetres"/>. A kind whose two ranges differ is named
    /// once a session on <paramref name="log"/> (the kind line, see the class); without a log it is counted and named by the first feed that has one.
    /// </summary>
    /// <param name="wcid">The creature's weenie class.</param>
    /// <param name="name">Its name.</param>
    /// <param name="pinnedMetres">Its pinned range, else ACE's default.</param>
    /// <param name="ownMetres">Its own range by the world database.</param>
    /// <param name="log">The feed's log, or null.</param>
    public float Kept(uint wcid, string name, float pinnedMetres, float ownMetres, ILogSink? log)
    {
        var kept = Mode == WalkAwarenessMode.Own ? ownMetres : pinnedMetres;
        if (ownMetres == pinnedMetres)
        {
            return kept;
        }

        _differing.Add(wcid);
        if (log is not null && _said.Add(wcid))
        {
            log.Info(LogCategory, KindLine(wcid, name, pinnedMetres, ownMetres));
        }

        return kept;
    }

    /// <summary>The kind line (see the class).</summary>
    private string KindLine(uint wcid, string name, float pinnedMetres, float ownMetres)
    {
        var c = CultureInfo.InvariantCulture;
        return Mode == WalkAwarenessMode.Own
            ? string.Create(c, $"{Token}{name} (class {wcid}): the walk keeps {ownMetres:0.##} m, its own range gives {ownMetres:0.##} m; the pinned read gives {pinnedMetres:0.##} m")
            : string.Create(c, $"{Token}{name} (class {wcid}): the walk keeps {pinnedMetres:0.##} m, its own range gives {ownMetres:0.##} m");
    }

    /// <summary>
    /// The feed's sample of one hostile creature on the open leg: <paramref name="metres"/> from the body, centre to centre, against the kind's two discs. A sample inside the own disc and outside the pinned one counts the
    /// creature once a leg and keeps its closest such sample. Nothing without an open leg, or for a kind that notices no farther than its pinned range.
    /// </summary>
    /// <param name="guid">The creature.</param>
    /// <param name="wcid">Its weenie class.</param>
    /// <param name="name">Its name.</param>
    /// <param name="metres">Its centre's distance from the body's.</param>
    /// <param name="pinnedRadius">The disc of its pinned range (<see cref="NavCreatures.AggroRadius"/>).</param>
    /// <param name="ownRadius">The disc of its own range.</param>
    public void Seen(uint guid, uint wcid, string name, float metres, float pinnedRadius, float ownRadius)
    {
        if (!LegOpen || !(ownRadius > pinnedRadius) || !(metres >= pinnedRadius && metres < ownRadius))
        {
            return;
        }

        if (!_passes.TryGetValue(guid, out var pass))
        {
            _passes.Add(guid, new Pass(wcid, name, metres, pinnedRadius, ownRadius, Attacked: false));
        }
        else if (metres < pass.Metres)
        {
            _passes[guid] = pass with { Metres = metres, PinnedRadius = pinnedRadius, OwnRadius = ownRadius };
        }
    }

    /// <summary>Opens a leg: the passes of the leg before (never closed) are dropped.</summary>
    public void BeginLeg()
    {
        _passes.Clear();
        LegOpen = true;
    }

    /// <summary>
    /// The open leg read <paramref name="guid"/> among the creatures attacking her (the travel leg's own read: a creature of a name its attack watch heard, standing in its reach). Marks the creature when the leg has already
    /// counted it (<see cref="Seen"/>); nothing otherwise. No detector of its own.
    /// </summary>
    /// <param name="guid">The attacker.</param>
    public void Attacked(uint guid)
    {
        if (LegOpen && _passes.TryGetValue(guid, out var pass) && !pass.Attacked)
        {
            _passes[guid] = pass with { Attacked = true };
        }
    }

    /// <summary>Closes the open leg and returns its line (see the class), or null: no leg was open, or it passed no creature inside its own disc and outside the pinned one. The session's totals take the leg either way.</summary>
    /// <param name="what">The leg in words (<c>the run leg to entry-front</c>).</param>
    public string? EndLeg(string what)
    {
        if (!LegOpen)
        {
            return null;
        }

        LegOpen = false;
        Legs++;
        if (_passes.Count == 0)
        {
            return null;
        }

        var attackers = 0;
        uint closestGuid = 0;
        Pass closest = default;
        foreach (var (guid, pass) in _passes)
        {
            attackers += pass.Attacked ? 1 : 0;
            if (closestGuid == 0 || pass.Metres < closest.Metres)
            {
                (closestGuid, closest) = (guid, pass);
            }
        }

        var passed = _passes.Count;
        _passes.Clear();
        Passed += passed;
        Attackers += attackers;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Token}{what}: {passed} creature(s) passed inside their own range and outside the pinned radius, {attackers} of them among her attackers; closest '{closest.Name}' (class {closest.Wcid}) 0x{closestGuid:X8} at {closest.Metres:0.0} m (pinned radius {closest.PinnedRadius:0.0} m, own {closest.OwnRadius:0.0} m); mode={ModeWord} passed={passed} attackers={attackers} closest_m={closest.Metres:0.0} session_legs={Legs} session_passed={Passed} session_attackers={Attackers}");
    }

    /// <summary>The line that says the shadow watches, once a session (the first leg writes it); null after that.</summary>
    public string? Announce()
    {
        if (_announced)
        {
            return null;
        }

        _announced = true;
        return $"{Token}watching ({OptionName}={ModeWord}): the walks keep {(Mode == WalkAwarenessMode.Own ? "each kind's own range" : "the pinned ranges")} off a creature; a kind whose own range is another is named once a session, and a leg names once the creatures it passed inside their own range and outside the pinned radius";
    }
}
