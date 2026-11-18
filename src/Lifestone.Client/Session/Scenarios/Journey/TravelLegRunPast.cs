using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Threat;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 14 run past (D137; the owner, 2026-11-14, amending M5 D88 on travel legs only: a run to a dungeon door is no fight; live <c>cache/m6/14/live/holdtrial1</c> and <c>holdtrial2</c>: on the Tusker island she fought six and more creatures
/// between the hub's drop and the Holding's door, died once and was recalled once): on a travel leg's walk (the rung change's run, a hop's walks to its portal, the walk home, the retreat's walk back) an attacker does not stop her while she
/// can outpace it. Each frame of a walk, check, act, check:
/// <list type="bullet">
/// <item><b>Check</b>: every creature heard attacking her within <see cref="FightBackHeardSeconds"/> that stands within <see cref="FightBackReachMetres"/> (the one world picture: its body as the client draws it), each judged by
/// <see cref="RunPast.Judge"/> against her live run (<see cref="CreaturePace.Her"/>) through the leg's <see cref="RunPastReader"/>: its pace live or from its facts and the dats, the attacks its name lands after the reader first saw it,
/// the farthest it attacked from (a thrower or a caster), and for a ranged one its measured damage a second over the seconds left in its range on her way to the walk's goal. Each verdict is logged when it changes (<c>run past: ...</c>;
/// a creature she outruns is named with both speeds).</item>
/// <item><b>The safety lines stand</b> (M6 6 E, M6 14 retreat attackers): her health under the derived retreat line of the attackers present, or her mana at the way out's reserve, stops the walk for the leg's retreat by recall
/// (<see cref="Retreat"/>), as in a fight-back. The walk back never retreats again.</item>
/// <item><b>Act</b>: she stops only for the attackers the rule must fight (<c>caught</c>, <c>keeps-pace</c>, <c>blocked</c>, <c>ranged-line</c>): the fight-back takes those alone (<see cref="_fightOnly"/>). M6 14 caught on the run
/// (the owner, 2026-11-15; live ladder25 04:07:39 to 04:09:43 UTC: she sheathed, ran, stopped and drew again with six tuskers at 2 to 3 m): when that set is spent the run past judges the attackers present again right there
/// (<see cref="RunPastAgain"/>, the safety lines included), and while it names any to fight the same fight-back goes on, no peace and no new walk; the walk is planned again only when none must be fought, and the slower ones are left
/// behind. At a walk's door (the run's entry front; the seek to a hop's portal) the attackers still on her are fought there before the portal (<c>at-goal</c>, <see cref="DoorStops"/>).</item>
/// <item><b>Check after</b>: the fight-back's own (each cast, each death, the retreat lines), and the walk planned again from where she stands.</item>
/// </list>
/// Inside a hunting ground nothing here runs: the hunt's and the fight's self-defence keep M5 D88.
/// </summary>
internal abstract partial class TravelLeg
{
    private readonly RunPastReader _runPast = new();
    private readonly List<PresentAttacker> _runPresent = [];
    private readonly List<ThreatStander> _runStanders = []; // M6 16 Q2: the board's creatures of the leg's question, reused every frame
    private readonly List<(double At, Vector2 Where)> _herTrail = [];
    private Vector2? _runGoal;
    private bool _runRetreating;
    private CreaturePaces? _paces;

    /// <summary>
    /// M6 14 run past: while set, the fight-back takes only these attackers (the ones the run past must fight); the others are left to the run that follows. Null: every attacker in reach (the door, a rest, the old rule).
    /// </summary>
    private HashSet<uint>? _fightOnly;

    /// <summary>The attackers the run past left behind or passed on this leg (report-only).</summary>
    protected int RanPast => _runPast.RanPast;

    private CreaturePaces Paces => _paces ??= Context.Paces ?? CreaturePaces.For(Context.Dats);

    /// <summary>M6 14 attack timing: the creatures' attack timings (the context's, else the dats').</summary>
    private CreatureAttackTimes AttackTimes => Context.AttackTimes ?? CreatureAttackTimes.For(Context.Dats);

    /// <summary>The start of one walk of a guarded walk: her trail is begun anew (the blocked rule reads it), the goal named for the ranged rule's heading.</summary>
    private void BeginRunPastWalk(uint cell, Vector3 at, bool retreating)
    {
        _herTrail.Clear();
        _fightOnly = null; // a stop names its own; none carries over to the next walk
        _runVerdicts.Clear();
        _runRetreating = retreating;
        var goal = new CellFrame(cell, new AcFrame(at, Quaternion.Identity)).ToGlobal();
        _runGoal = new Vector2((float)goal.X, (float)goal.Y);
    }

    /// <summary>
    /// The run past's frame on a walk (see the class): null while she runs on; the stop's reason when an attacker must be fought, or when a safety line sets the retreat due (<see cref="_retreatWhy"/>). The attackers to fight are
    /// left in <see cref="_fightOnly"/>.
    /// </summary>
    private string? RunPastStops()
    {
        var now = S.Clock.Now;
        if (NoteHer(now) is not { } her || _attackWatch is not { } watch || !Present())
        {
            return null;
        }

        return JudgeRunPast(her, Blocked(now), watch, inFight: false);
    }

    /// <summary>
    /// M6 14 caught on the run (rule 2): the fight-back's own judgement when the set the run past named is spent: the attackers present judged again where she stands (the same judgement as <see cref="RunPastStops"/>, the safety lines
    /// included; standing to fight is not being blocked). The words of the first she must fight (or of the retreat a safety line set due), the set in <see cref="_fightOnly"/>; null when none must be fought.
    /// </summary>
    private string? RunPastAgain()
    {
        _fightOnly = null;
        _runVerdicts.Clear();
        if (HerPlanar() is not { } her || _attackWatch is not { } watch || !Present())
        {
            return null;
        }

        return JudgeRunPast(her, blocked: false, watch, inFight: true);
    }

    /// <summary>
    /// The run past's judgement of the attackers in <see cref="_runPresent"/> (see the class): the safety lines first (not on the walk back), then each attacker by <see cref="RunPast.Judge"/>, each verdict logged when it changes and kept
    /// in <see cref="_runVerdicts"/>; the attackers to fight into <see cref="_fightOnly"/>; the words of the first, or null.
    /// </summary>
    private string? JudgeRunPast(Vector2 her, bool blocked, AttackWatch watch, bool inFight)
    {
        if (!_runRetreating && SafetyStops(inFight) is { } leave)
        {
            return leave;
        }

        var side = HerSide(atGoal: false, blocked: blocked) with { Standing = inFight }; // M6 14 caught on the run: standing to fight, a melee attacker on her is fought before she runs on
        string? stop = null;
        HashSet<uint>? must = null;
        foreach (var p in _runPresent)
        {
            var a = _runPast.Read(S, watch, p, her, _runGoal, side.Speed, Paces, AttackTimes);
            var v = RunPast.Judge(in side, in a);
            LogRunPast(p.Guid, v);
            _runVerdicts[p.Guid] = v.Reason;
            if (v.Call == RunPastCall.Fight)
            {
                (must ??= []).Add(p.Guid);
                stop ??= StopWord(p.Name, p.Guid, a.Metres, v);
            }
        }

        _fightOnly = must;
        return stop;
    }

    /// <summary>M6 14 caught on the run: the run past's last reason for each attacker it judged on this walk (the fight-back's end line names them).</summary>
    private readonly Dictionary<uint, string> _runVerdicts = [];

    /// <summary>
    /// The door's check (see the class): at a walk's goal before a portal, the attackers still on her: within <see cref="RunPast.MeleeReachMetres"/>, closing to it within <see cref="RunPast.DoorSeconds"/> at their pace (one whose pace is not
    /// known: within the metres she runs in them), or a ranged one heard within them. The stop's reason, or null; the fight-back then takes every attacker in reach.
    /// </summary>
    private string? DoorStops()
    {
        var now = S.Clock.Now;
        if (NoteHer(now) is not { } her || _attackWatch is not { } watch || !Present())
        {
            return null;
        }

        var side = HerSide(atGoal: true, blocked: false);
        string? stop = null;
        foreach (var p in _runPresent)
        {
            var a = _runPast.Read(S, watch, p, her, _runGoal, side.Speed, Paces, AttackTimes);
            var closes = a.Pace is { MetresPerSecond: > 0 } pace
                ? (a.Metres - RunPast.MeleeReachMetres) / pace.MetresPerSecond <= RunPast.DoorSeconds
                : a.Metres <= RunPast.MeleeReachMetres + (side.Speed * RunPast.DoorSeconds);
            var throwing = a.Ranged && watch.LastHeard(p.Name, out var heard) && now - heard <= RunPast.DoorSeconds;
            if (a.Metres > RunPast.MeleeReachMetres && !closes && !throwing)
            {
                continue; // not on her: the portal takes her before it does
            }

            var v = RunPast.Judge(in side, in a);
            LogRunPast(p.Guid, v);
            stop ??= StopWord(p.Name, p.Guid, a.Metres, v);
        }

        _fightOnly = null;
        return stop;
    }

    /// <summary>
    /// The attackers present now (heard within <see cref="FightBackHeardSeconds"/>, within <see cref="FightBackReachMetres"/> where the client draws them and her, none the fight-back left; M6 14 caught on the run: one left for the cast
    /// range that attacked her again since is no longer left, <see cref="ReleaseLeftThatAttackAgain"/>) into <see cref="_runPresent"/>; false when there are none. M6 16 Q2 (docs/automation-engine.md E2,
    /// <c>TODO(engine-threat-flip)</c>): the session's threat board answers, asked the leg's own question (<see cref="ThreatQuery.OnHer"/>: its reach, its window, every attack heard as what counts, an evade among them), in the
    /// object table's order as before; the one change: an attack the board heard before this leg's watch began (a rung move, a new leg) is known for its window. The leg's watch (<see cref="_attackWatch"/>) still gates the read
    /// and keeps its own hearing for its other reads.
    /// </summary>
    private bool Present()
    {
        ReleaseLeftThatAttackAgain();
        var query = ThreatQuery.OnHer(FightBackReachMetres, FightBackHeardSeconds, WhereSheStands().At);
        ThreatBoard.For(S).Gather(S, in query, S.Clock.Now, _runStanders);
        _runPresent.Clear();
        for (var i = 0; i < _runStanders.Count; i++)
        {
            var p = _runStanders[i];
            _runPresent.Add(new PresentAttacker(p.Guid, p.Wcid, p.Name, p.Metres));
        }

        NoteAwarenessAttackers(_runPresent); // M6 16 E1f: the walk-awareness shadow reads this read (report-only; the left ones included: they attacked her too)
        for (var i = _runPresent.Count - 1; i >= 0; i--)
        {
            if (_left.ContainsKey(_runPresent[i].Guid))
            {
                _runPresent.RemoveAt(i);
            }
        }

        return _runPresent.Count > 0;
    }

    /// <summary>
    /// The safety lines on the run (M6 6 E, M6 14 retreat attackers): her health under the derived line, or her mana at the way out's reserve, sets the retreat due; the stop's reason, or null. <paramref name="inFight"/> (M6 14 caught on the
    /// run: the fight-back's own judgement, <see cref="RunPastAgain"/>): the mana line is the fight-back's, the dance under fire holding the leaving while she is not losing (<see cref="Skirmish.LeavesAtTheLine"/>).
    /// </summary>
    private string? SafetyStops(bool inFight)
    {
        if (HealthUnderLine(out var healthWord) is { } under)
        {
            RetreatDue("health", $"{healthWord} is under the retreat line {under} on the run past (M6 14 run past, D137: the safety lines stand)");
            return $"retreat due: {healthWord} is under the retreat line";
        }

        if (ReserveLine(out var reserveWhy) is { } line && ManaNow() is { } held
            && ManaWayOut.Decide(new WayOutInput(true, held, line, Attackers: true, KillPending: false, Press: false, Refused: false)) is { Yes: true }
            && (!inFight || Skirmish.LeavesAtTheLine(in line, held, out _)))
        {
            RetreatDue(ManaWayOut.Leave, string.Create(CultureInfo.InvariantCulture, $"mana {held} is at the way out's reserve {reserveWhy} with attackers on her on the run past: Lifestone Recall is still paid, so she leaves (M6 14 run past, D137: the safety lines stand)"));
            return string.Create(CultureInfo.InvariantCulture, $"retreat due: mana {held} is at the way out's reserve");
        }

        return null;
    }

    /// <summary>Her side of the rule now: her run speed (0 when her Run skill is not read: every creature keeps pace), the blocked and door flags, her health over the derived retreat line.</summary>
    private RunPastHer HerSide(bool atGoal, bool blocked)
    {
        var q = S.State.Self.Qualities;
        var speed = CreaturePace.Her(q, out var speedWhy);
        var (percent, source) = RetreatLine();
        var above = q.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var max) ? health - (percent / 100.0 * max) : 0;
        return new RunPastHer(speed ?? 0, speed is null ? speedWhy + ": every creature keeps pace" : speedWhy, blocked, atGoal, above, source);
    }

    /// <summary>Notes where she stands now on the walk's trail (one sample older than <see cref="RunPast.BlockedSeconds"/> kept) and returns her planar global position, or null when unread.</summary>
    private Vector2? NoteHer(double now)
    {
        if (HerPlanar() is not { } here)
        {
            return null;
        }

        _herTrail.Add((now, here));
        while (_herTrail.Count > 2 && _herTrail[1].At <= now - RunPast.BlockedSeconds)
        {
            _herTrail.RemoveAt(0);
        }

        return here;
    }

    /// <summary>Her planar global position where the client draws her (<see cref="WhereSheStands"/>), or null when unread.</summary>
    private Vector2? HerPlanar()
    {
        if (WhereSheStands().At is not { } at)
        {
            return null;
        }

        var g = at.ToGlobal();
        return new Vector2((float)g.X, (float)g.Y);
    }

    /// <summary>True when the walk's trail spans <see cref="RunPast.BlockedSeconds"/> and she moved under <see cref="RunPast.BlockedGainMetres"/> in it.</summary>
    private bool Blocked(double now) =>
        _herTrail.Count >= 2 && _herTrail[0].At <= now - RunPast.BlockedSeconds && Vector2.Distance(_herTrail[0].Where, _herTrail[^1].Where) < RunPast.BlockedGainMetres;

    private void LogRunPast(uint guid, in RunPastVerdict v)
    {
        if (_runPast.Note(guid, in v) is { } line)
        {
            Log(line);
        }
    }

    private static string StopWord(string name, uint guid, double metres, in RunPastVerdict v) =>
        string.Create(CultureInfo.InvariantCulture, $"'{name}' 0x{guid:X8} attacked the player and stands {metres:0.0} m away (within {FightBackReachMetres:0} m): fight back ({v.Reason}; M6 14 run past, D137)");
}
