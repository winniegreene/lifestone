using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Threat;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 14 walk dance (the owner, 2026-11-15: at her level the stamina dance replaces the potions; with Mana Conversion trained, Revitalize Self and then Stamina to Mana Self restores her mana fast; live
/// <c>cache/m6/14/live/ladder22</c>, 01:54:50 UTC: on the run to the Tusker Armory's entry front the crossing measure gave the crossing up at 200 mana, "7.2 kills, 232 mana, against 136 over the reserve", and she recalled; the walk never
/// danced between fights). The walk dances in a lull:
/// <list type="bullet">
/// <item><b>When</b>: on a run leg whose crossing is measured (<see cref="BeginCrossing"/>), after a fight-back has ended by its own end test, in a lull (M6 14 caught on the run: no creature heard attacking her within
/// <see cref="FightBackHeardSeconds"/> stands alive within <see cref="FightBackReachMetres"/> where the client draws it, whatever list it is on, <see cref="AttackerOnHer"/>; read again before the dance starts, the line naming the
/// last attack heard), when the crossing's measure says the dance is due (the dance covers her, she can still dance, and her mana is under its line,
/// <see cref="Vitals.ApproachCost.Measured"/>), or (M6 14 top-off) on any leg that dances when she is not full (<see cref="TopOffDue"/>): the dance tops her off, mana to her maximum and stamina near full.</item>
/// <item><b>The dance</b>: the leg's own machinery (<see cref="DanceOnTheWalk"/>; the rung change's recharge, its walk-dance mode): each round Revitalize Self at the level the dance reads (<see cref="StaminaDance.Round"/>) and then the
/// best-net Stamina to Mana Self press, every cast checked after it lands; an attacker interrupts it and is fought back first (<see cref="FightBackInDance"/>: the walk's own fight-back, its kills measured, the retreat left to the walk), then
/// the dance resumes toward the line the crossing measures then, or the walk goes on.</item>
/// <item><b>Check after</b>: the crossing is measured again (<see cref="CrossingGivenUp"/>): given up when the dance stopped by its bounds and the rest falls short, by name.</item>
/// </list>
/// <para>
/// M6 the dance before running on (the owner, 2026-11-21: on a run she refills her stamina and mana before she runs on; live run 10, <c>cache/m6/sprint/run10/step3-levelup.log</c>, 07:29:09.515 UTC: on the run from
/// the Facility Hub's Barracks drop to the Tusker Armory's door the dance was due to 379 at mana 196, a Stinging Chittick 25.5 m away had stung her for 1 to 2 points 1.1 s before, so "no lull, no dance"; then no hit landed for 36.5 s and
/// no dance came before fight-back 2 at mana 211; docs/session.md section 123):
/// <list type="bullet">
/// <item><b>a. The gate</b> (<see cref="DanceGate"/>): she dances before the walk goes on when no attacker is on her (the lull above), or when every attacker on her stands beyond the run past's melee reach
/// (<see cref="RunPast.MeleeReachMetres"/>) and the vitals guard's own race reads held (winning or holding: <see cref="JourneyFacts.Race"/>, <see cref="LosingRace"/>): chip hits her sustain covers no longer stop the dance. An attacker
/// within melee reach, a race that is not held, or a race not read keeps the old answer: the walk goes on.</item>
/// <item><b>b. The owed dance</b> (<see cref="DanceStopsWalk"/>): a dance the gate refused is owed; each frame of the walk the lull is read (<see cref="ThreatBoard.Lull"/>: no hit from anyone, from the damage lines, over the slowest
/// attack of the kinds heard, <see cref="LullWindow"/>); when a lull begins and the dance is still due and the gate opens, the walk stops at once for the dance, and is planned again from where she stands.</item>
/// <item><b>c. Run past stands</b> (D137): a dance is not a fight; a creature that catches her during the dance (within melee reach, or a race that is no longer held) is fought back as before (the recharge's walk-dance mode,
/// <c>RungChangeStation.AttackNow</c>).</item>
/// </list>
/// </para>
/// </summary>
internal abstract partial class TravelLeg
{
    /// <summary>What the walk asks its dance (<see cref="DanceOnTheWalk"/>): the leg's word and where the walk goes; the line is the crossing's (<see cref="CrossingDanceDue"/>), read again at each step of the dance.</summary>
    /// <param name="Leg">The leg's word (<c>run</c>).</param>
    /// <param name="Label">The walk's destination's label.</param>
    protected readonly record struct WalkDanceAsk(string Leg, string Label);

    /// <summary>M6 the dance before running on: what the dance's gate read (<see cref="DanceGate"/>).</summary>
    /// <param name="Open">True when she may dance now.</param>
    /// <param name="UnderFire">True when an attacker is on her (heard within <see cref="FightBackHeardSeconds"/>, within <see cref="FightBackReachMetres"/>); false: a lull.</param>
    /// <param name="Who">The nearest attacker on her in words, or empty in a lull.</param>
    /// <param name="Why">The gate's reading in words (the lull, the melee reach, or the race with its numbers).</param>
    protected readonly record struct DanceGateRead(bool Open, bool UnderFire, string Who, string Why);

    /// <summary>True when a retreat is due on the walk (a safety line, a fight-back that cannot go on, or a crossing given up): the walk ends and the retreat follows.</summary>
    protected bool RetreatIsDue => _retreatWhy is not null;

    /// <summary>M6 14 top-off: true on a leg whose dance has machinery (<see cref="DanceOnTheWalk"/> overridden): only such a leg tops her off after a fight-back.</summary>
    protected virtual bool DancesOnTheWalk => false;

    /// <summary>M6 the dance before running on (rule b): the dance the gate refused (its line and why), owed until it runs or is no longer due; null: none owed.</summary>
    private (uint Line, string Why)? _danceOwed;

    /// <summary>M6 the dance before running on (rule b): the dance the walk was stopped for (<see cref="DanceStopsWalk"/>) and the stop's words, read once by <see cref="GuardedWalk"/>; null: none.</summary>
    private (uint Line, string Why, string Stop)? _danceStop;

    /// <summary>M6 the dance before running on (rule b): the last hit (the board's session time) the lull's window was read after; NaN: none read yet.</summary>
    private double _lullAfter = double.NaN;

    /// <summary>M6 the dance before running on (rule b): the lull's window after <see cref="_lullAfter"/> and its words (<see cref="LullWindow"/>).</summary>
    private (double Seconds, string Why) _lull;

    /// <summary>
    /// M6 the dance before running on (rule b): the session time the gate is read next in this quiet stretch: the lull's start, then (the gate closed) just past the old lull (no attack from anyone for <see cref="FightBackHeardSeconds"/>,
    /// where no creature is on her by the lull test, so the gate is open by definition).
    /// </summary>
    private double _lullNextRead = double.NegativeInfinity;

    /// <summary>
    /// M6 14 top-off (the owner, 2026-11-15: after a fight-back, a top-off before the walk resumes): the dance due in the lull when the crossing's measure asks for none: the leg dances, the dance covers her, and its next
    /// cast toward her full bars (<see cref="StaminaDance.TopOff"/>) is a cast, not done. Its line (her maximum) and why, or null.
    /// </summary>
    private (uint Line, string Why)? TopOffDue()
    {
        if (!DancesOnTheWalk || StaminaDance.Round(S.State, Context.Spells, out _) is not { } round
            || !S.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax) || !S.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax))
        {
            return null;
        }

        var step = StaminaDance.TopOff(in round, StaminaPress.Levels(S.State, Context.Spells), mana, manaMax, stamina, staminaMax, afterRevitalize: false);
        return step.Move is WalkDanceMove.Revitalize or WalkDanceMove.Press
            ? (manaMax, string.Create(CultureInfo.InvariantCulture, $"the top-off before the walk goes on: mana {mana} of {manaMax}, stamina {stamina} of {staminaMax}: {step.Move.ToString().ToLowerInvariant()} first: {step.Why}"))
            : null;
    }

    /// <summary>
    /// The dance due now on the walk (the crossing's own decision, <see cref="CrossingDanceDue"/>, while her mana is under its line; else M6 14 top-off, <see cref="TopOffDue"/>): its line and why, or null.
    /// </summary>
    private (uint Line, string Why)? DanceDueNow() =>
        CrossingDanceDue() is { } due && ManaNow() is { } held && held < due.Line ? (due.Line, due.Why) : TopOffDue();

    /// <summary>
    /// The leg's dance in a lull (the class's summary), cast toward the crossing's line (<see cref="CrossingDanceDue"/>). Every cast through the cast policy as automation; peace again at its end. This base has no dance machinery: the
    /// line says so and the dance is named as one that cannot run on this leg, so the measure gives a crossing that falls short up as before.
    /// </summary>
    /// <param name="ask">The walk's ask.</param>
    protected virtual IEnumerable<ActionHandle?> DanceOnTheWalk(WalkDanceAsk ask)
    {
        Log($"walk dance: the {ask.Leg} leg to {ask.Label} has no dance machinery (TODO(m6-walk-dance-leg)): the dance cannot run here, and the walk goes on");
        CrossingDanceEnded("this leg has no dance machinery");
        yield break;
    }

    /// <summary>
    /// M6 the dance before running on (rule a; the class's summary): whether she may dance now. Open in a lull (no creature on her, <see cref="AttackerOnHer"/>); under fire, open only when the nearest attacker on her stands beyond the run
    /// past's melee reach (<see cref="RunPast.MeleeReachMetres"/>; the nearest, so none is nearer) and the vitals guard's race reads held (<see cref="RaceHeld"/>). Closed otherwise, with why: the walk goes on.
    /// </summary>
    protected DanceGateRead DanceGate()
    {
        var c = CultureInfo.InvariantCulture;
        if (!AttackerOnHer(out var guid, out var name, out var metres))
        {
            return new(true, false, string.Empty, string.Create(c, $"no creature heard attacking her within {FightBackHeardSeconds:0} s stands alive within {FightBackReachMetres:0} m, whatever list it is on; {LastAttackWord()}"));
        }

        var who = string.Create(c, $"'{name}' 0x{guid:X8} attacked her {HeardAgo(name)} and stands {metres:0.0} m away where it is drawn");
        if (metres <= RunPast.MeleeReachMetres)
        {
            return new(false, true, who, string.Create(c, $"it stands within the {RunPast.MeleeReachMetres:0} m of melee (the run past's reach, D137): it is on her, so no dance"));
        }

        var held = RaceHeld(out var race);
        return new(held, true, who, string.Create(c, $"it stands beyond the {RunPast.MeleeReachMetres:0} m of melee (the run past's reach, D137){(held ? " and " : ", but ")}{race}"));
    }

    /// <summary>
    /// M6 the dance before running on (rule a): the vitals guard's own race now (<see cref="JourneyFacts.Race"/>): held when it reads winning or holding (<see cref="LosingRaceAnswer.Losing"/> false: her health is not falling, or the
    /// heal she can sustain holds the incoming). A race not read is not held.
    /// </summary>
    /// <param name="words">The race in words with its numbers (<see cref="LosingRace.Words"/>), or why it is not read.</param>
    protected bool RaceHeld(out string words)
    {
        if (Context.Facts.Race?.Invoke(S, S.Clock.Now) is not { } read)
        {
            words = "the vitals guard's race is not read on this run (no guard reads it here): not held, so the lull rule stands alone";
            return false;
        }

        var held = !read.Answer.Losing;
        words = $"the vitals guard's race is {(held ? "held" : "not held")}: {LosingRace.Words(in read.Input, in read.Answer)}";
        return held;
    }

    /// <summary>
    /// The dance in the lull after a fight-back on the walk (the class's summary): the gate read again (the check before, <see cref="DanceGate"/>), the leg's dance, the crossing measured again after; <see cref="_retreatWhy"/> set when it
    /// is given up. A gate that is closed owes the dance (rule b): the walk goes on and reads the lull each frame.
    /// </summary>
    private IEnumerable<ActionHandle?> DanceInTheLull(string leg, string label, uint line, string why)
    {
        // M6 14 caught on the run (the owner's ruling, 2026-11-15: she is not in a lull while she is under attack): a lull is no creature on her, whatever list it is on (left as beyond the cast range, one the run past runs from); M6 the
        // dance before running on (the owner, 2026-11-21): under chip hits her sustain covers, from beyond melee, she dances too; else the walk goes on and the dance is owed
        var gate = DanceGate();
        if (!gate.Open)
        {
            (_danceOwed, _lullAfter) = ((line, why), double.NaN); // owed: the walk reads the lull afresh from its next frame
            Log($"walk dance: due to {line}, but she is under attack: {gate.Who}: no lull, no dance; the walk goes on and the dance waits for the next lull, read each frame of the walk ({gate.Why}) ({why})");
            yield break;
        }

        _danceOwed = null;
        Log(gate.UnderFire
            ? $"walk dance: under fire on the {leg} leg to {label}, but she dances before the walk goes on (the dance before running on): {gate.Who}; {gate.Why}: mana {VitalWord(MageTemplate.MaxMana)}, stamina {VitalWord(MageTemplate.MaxStamina)}, health {VitalWord(MageTemplate.MaxHealth)}: she stands and dances to {line} ({why})"
            : $"walk dance: a lull on the {leg} leg to {label} ({gate.Why}): mana {VitalWord(MageTemplate.MaxMana)}, stamina {VitalWord(MageTemplate.MaxStamina)}, health {VitalWord(MageTemplate.MaxHealth)}: she stands and dances to {line} ({why})");
        foreach (var h in DanceOnTheWalk(new WalkDanceAsk(leg, label)))
        {
            yield return h;
        }

        if (_retreatWhy is not null)
        {
            yield break; // a fight-back in the dance set the retreat due (its safety lines, or the crossing given up after its kills): the walk's retreat follows
        }

        // check after: the crossing measured again with the dance's mana in it
        if (CrossingGivenUp() is { } gaveUp)
        {
            RetreatDue(Vitals.ApproachCost.AbandonedPrefix, gaveUp);
            yield break;
        }

        Log(string.Create(CultureInfo.InvariantCulture, $"walk dance: ended on the {leg} leg to {label}: mana {VitalWord(MageTemplate.MaxMana)}, stamina {VitalWord(MageTemplate.MaxStamina)}: the walk goes on"));
    }

    /// <summary>
    /// M6 the dance before running on (rule b; the class's summary): each frame of a walk with a dance owed, the lull is read from the damage lines (<see cref="ThreatBoard.Lull"/>: no hit from anyone over <see cref="LullWindow"/>). At the
    /// lull's start, and again just past the old lull (no attack from anyone for <see cref="FightBackHeardSeconds"/>, where the gate is open by definition), the dance is read again (<see cref="DanceDueNow"/>: none due, nothing is owed) and
    /// the gate (<see cref="DanceGate"/>); open, the stop's words (the walk stops at once and <see cref="GuardedWalk"/> dances), else null: the walk goes on. Each reading is one line; a new hit starts the lull again. The walk back never
    /// dances. Between the readings a frame reads two numbers off the board and allocates nothing.
    /// </summary>
    private string? DanceStopsWalk()
    {
        if (_danceOwed is not { } owed || _runRetreating)
        {
            return null;
        }

        var c = CultureInfo.InvariantCulture;
        var board = ThreatBoard.For(S);
        var now = S.Clock.Now;
        if (!(board.LastHitAt == _lullAfter))
        {
            // a new hit (or a dance newly owed): the lull's window is read after the last hit, and the gate is read at the lull's start
            (_lullAfter, _lull) = (board.LastHitAt, LullWindow(board));
            _lullNextRead = board.LastHitAt + _lull.Seconds;
        }

        var lull = board.Lull(now, _lull.Seconds);
        if (now < _lullNextRead || !lull.Quiet)
        {
            return null;
        }

        var quiet = now - lull.LastAt;
        _lullNextRead = Math.BitIncrement(Math.Max(board.LastAttackAt + FightBackHeardSeconds, now)); // the gate closed: read again just past the old lull (the lull test counts an attack heard exactly that long ago)
        var quietWord = double.IsInfinity(quiet) ? "no hit heard on her this session" : string.Create(c, $"no hit from anyone for {quiet:0.0} s (the last: '{lull.LastBy}')");
        if (DanceDueNow() is not { } due)
        {
            _danceOwed = null;
            Log($"walk dance: a lull on the walk ({quietWord}; {_lull.Why}), and the dance owed to {owed.Line} is no longer due (her bars, or the crossing's measure since): nothing is owed, the walk goes on");
            return null;
        }

        var gate = DanceGate();
        if (!gate.Open)
        {
            Log($"walk dance: a lull on the walk ({quietWord}; {_lull.Why}), the dance due to {due.Line}, but she is under attack: {gate.Who}: {gate.Why}: the walk goes on and the dance stays owed ({due.Why})");
            return null;
        }

        var stop = $"walk dance: a lull on the walk ({quietWord}; {_lull.Why}) and the dance is due to {due.Line} ({gate.Why}): she stops at once and dances, then the walk is planned again from where she stands (the dance before running on)";
        _danceStop = (due.Line, due.Why, stop);
        return stop;
    }

    /// <summary>
    /// M6 the dance before running on (rule b): the lull's window after the board's last hit: the slowest attack of the kinds heard attacking her within <see cref="FightBackHeardSeconds"/> before it (each kind's own seconds from one attack
    /// to the next, ACE's attack loop over the dats, <see cref="AttackTiming.Seconds"/>; the named fallback where it is not derived), so every attacker still on her would have attacked again in it; at most
    /// <see cref="FightBackHeardSeconds"/> (the old lull). With no kind read, the old lull.
    /// </summary>
    private (double Seconds, string Why) LullWindow(ThreatBoard board)
    {
        var c = CultureInfo.InvariantCulture;
        var last = board.LastHitAt;
        if (double.IsNegativeInfinity(last))
        {
            return (FightBackHeardSeconds, "no hit heard on her this session");
        }

        var seconds = 0.0;
        var kinds = new List<string>();
        for (var i = 0; i < board.AttackerCount; i++)
        {
            var a = board.AttackerAt(i);
            if (!(a.LastAttackAt >= last - FightBackHeardSeconds))
            {
                continue;
            }

            var timing = AttackTimes.Of(board.WcidOf(a.Name), a.Name);
            if (!(timing.Seconds > 0) || !double.IsFinite(timing.Seconds))
            {
                continue;
            }

            seconds = Math.Max(seconds, timing.Seconds);
            kinds.Add(string.Create(c, $"'{a.Name}' every {timing.Seconds:0.##} s{(timing.Derived ? string.Empty : " (" + AttackTimings.UnderivedTodo + ": not derived, the fallback)")}"));
        }

        if (kinds.Count == 0)
        {
            return (FightBackHeardSeconds, string.Create(c, $"no attacker's timing read: the old lull of {FightBackHeardSeconds:0} s"));
        }

        var window = Math.Min(seconds, FightBackHeardSeconds);
        return (window, string.Create(c, $"the lull is {window:0.##} s, the slowest attack of the kinds heard: {string.Join(", ", kinds)}{(seconds > FightBackHeardSeconds ? string.Create(c, $", at most the old lull of {FightBackHeardSeconds:0} s") : string.Empty)}"));
    }

    /// <summary>
    /// The fight-back of an attacker that interrupts the walk's dance: the life check before, <see cref="FightBack"/> (the walk's own: its reserve, its safety lines, its kills measured for the crossing), the death check after a move, and
    /// the crossing's check after (<see cref="CrossingCheckAfterFight"/>). The retreat, when one is due, is the walk's (<see cref="RetreatIsDue"/>): the dance stops and the walk ends for it.
    /// </summary>
    /// <param name="leg">The leg's word.</param>
    /// <param name="label">Where she stands, in words.</param>
    protected IEnumerable<ActionHandle?> FightBackInDance(string leg, string label)
    {
        StartAttackWatch();
        var life = ReadLife(leg, $"the fight-back in the dance {label}");
        string? failure = null;
        var mark = TravelMark;
        var lastAlive = S.State.Self.Position;
        _fightOnly = null; // standing still, every attacker in reach is fought (M5 D88)
        foreach (var h in Catching(FightBack(leg, retreating: false), f => failure = f))
        {
            yield return h;
        }

        if (TeleportSince(mark) || failure is not null)
        {
            foreach (var h in DiedAfterMove(leg, $"the fight-back in the dance {label}", life, lastAlive))
            {
                yield return h;
            }
        }

        if (failure is not null)
        {
            throw new InvalidOperationException(failure);
        }

        CrossingCheckAfterFight();
    }
}
