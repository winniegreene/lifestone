using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Threat;
using Lifestone.Client.World;
using Lifestone.Data.Magic;
using DatReaderWriter.Enums;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 16 Q7 (docs/automation-engine.md section 2, "one combat entry": the skirmish, anywhere off a mapped ground): the travel leg's fight-back as an engine of its own, moved out of the leg unchanged so other callers can use it. A
/// coroutine (<see cref="Run"/>) over a small host (<see cref="ISkirmishHost"/>: which creatures to fight and leave, when the run past is judged again, when a retreat is due, where to log, and its hands). It keeps what a fight-back learns
/// for the host's lifetime: the bolts refused for a component, the targets whose bolts came to nothing, the dance's rounds.
/// <para>
/// The fight-back (M5 D88), check, act, check for each cast: the attacker alive and in reach, the bolt (the highest War Magic projectile the book knows whose chance on the current skill is at least <see cref="FightBackChance"/>, else the lowest),
/// the mana for it; the targeted cast through the host's cast policy; the attacker's death within 2 s. M6 14 the hovering Zefir (<c>Skirmish.Reach.cs</c>): the reach is the spell's own, ACE's range from her skill,
/// planar where the client draws both; an attacker beyond the bolt's range gets Life Magic when a Harm Other reaches it, else she runs past it at once, left until it attacks her from within her reach; a target with no cast at it in
/// <see cref="FightBackDecisionSeconds"/> is run past the same way. M6 14 caught on the run (rule 2): on the run, when the attackers the run past stopped her for are spent, it judges again there (<see cref="ISkirmishHost.RunPastAgain"/>)
/// and the fight-back goes on while it names any; the end line names what is still on her and the last attack heard (<see cref="ISkirmishHost.EndWhy"/>).
/// Ends in peace. Named failures: <c>fight-back:&lt;leg&gt;:no-war-spell</c>, and (M6 6 R1F) <c>fight-back:&lt;leg&gt;:no-magic-mode</c> when the check before a cast cannot enter the magic mode. M6 6 R1F: the bolt is one the pack holds the
/// components for (<see cref="Bolt"/>); a bolt ACE refuses for a component (0x0400) is dropped for the host and the next is picked; none left sets the retreat due (<c>no-components</c>). M6 6 E: no mana for the bolt, the health under the
/// retreat line, or the timeout set the retreat due (<see cref="ISkirmishHost.RetreatDue"/>) and end the fight-back; a retreating fight-back (the walk back) leaves such an attacker instead and the walk back goes on.
/// </para>
/// </summary>
/// <param name="host">What only the caller knows.</param>
internal sealed partial class Skirmish(ISkirmishHost host)
{
    /// <summary>Seconds one fight-back may take before the host retreats (<c>why=timeout</c>, M6 6 E; the Academy's kill bound, <see cref="AcademyRun.KillSeconds"/>).</summary>
    internal const double FightBackSeconds = AcademyRun.KillSeconds;

    /// <summary>The cast chance the fight-back's bolt level must reach on the current War Magic skill (Lifestone's own; the buff cycle's and the fight's plan chance are their own).</summary>
    internal const double FightBackChance = WarBoltPick.DefaultMinChance;

    /// <summary>M6 14 bolt wait: the bound of the wait after a bolt for its kill when no line says it resolved (a wall hit, a projectile lost): the old wait.</summary>
    private const double BoltWaitSeconds = 2.0;

    /// <summary>
    /// M6 14 bolt wait: after the bolt's damage or resist line, the seconds the kill notice is given to follow it (ACE sends the death's motion and the kill text in the same tick as the killing blow's damage line or the next; Lifestone's own
    /// grace: a few frames over a tick, far under the two seconds it replaces).
    /// </summary>
    private const double KillNoticeGraceSeconds = 0.3;

    /// <summary>M6 16 Q8: the last bolt's resolving line was a damage or drain line of 0 points.</summary>
    private bool _resolvedZero;

    /// <summary>M6 6 R1F: the bolts ACE refused for a missing component (never cast again by this skirmish's host).</summary>
    private readonly HashSet<uint> _boltsRefused = [];

    private IGameSessionView S => host.Session;

    private JourneyContext Context => host.Context;

    /// <summary>The fight-backs this skirmish made (report-only).</summary>
    internal int FoughtBack { get; private set; }

    /// <summary>The attackers the fight-backs killed (report-only).</summary>
    internal int Kills { get; private set; }

    /// <summary>The casts the fight-backs made at attackers (the walk's stops read them).</summary>
    internal int Casts { get; private set; }

    private void Log(string text) => host.Log(text);

    /// <summary>One fight-back (see the class), with the way out's <paramref name="reserve"/> (null: none buys it, or the walk back) and its words.</summary>
    /// <param name="leg">The host's word in a failure's name and its lines.</param>
    /// <param name="retreating">True on the walk back: the retreat is never set due again; an attacker that cannot be fought is left.</param>
    /// <param name="reserve">The mana kept for the way out.</param>
    /// <param name="reserveWhy">The reserve in words.</param>
    internal IEnumerable<ActionHandle?> Run(string leg, bool retreating, Vitals.WayOutLine? reserve, string reserveWhy)
    {
        FoughtBack++;
        FireDanceBegin(); // M6 14 fire dance: a round under fire starts again with the fight-back
        var started = S.Clock.Now;
        var casts = 0;
        var kills = 0;
        host.Watch?.BeginTally(); // M6 14 retreat attackers (rule 3): the hits this fight-back sees are the line's floor
        Log(string.Create(CultureInfo.InvariantCulture, $"fight-back {FoughtBack} on the {leg} leg{(retreating ? " (retreating)" : string.Empty)}: the walk is stopped; health {VitalWord(MageTemplate.MaxHealth)}, mana {VitalWord(MageTemplate.MaxMana)}; the way out's reserve {reserveWhy} (M5 D88: always fight back an attacker; M6 14 retreat attackers: no bolt takes the mana under the reserve)"));
        foreach (var h in host.MagicNow())
        {
            yield return h;
        }

        uint target = 0;
        var tallies = new Dictionary<uint, (double Since, double Fire, int Casts)>(); // M6 14 the hovering Zefir: each target's first frame, the dance's seconds then, and the casts at it (the decision window)
        var onTheRun = host.OnTheRun; // M6 14 caught on the run (rule 2): the run past named the set; when it is spent, the run past judges again here
        var judgedAt = double.NaN;
        while (true)
        {
            if (!host.NextAttacker(out var guid, out var name, out var metres))
            {
                if (!onTheRun || judgedAt == S.Clock.Now)
                {
                    break; // none to fight (a judgement that named a set no pick then finds, in the same frame, ends it too: never a loop without a frame)
                }

                // M6 14 caught on the run (rule 2; the owner's ruling, 2026-11-15: under attack on the run she either fights or runs, never stands between): the set is spent: the run past judges the attackers present again right here, the safety lines included; while it names any to
                // fight, this fight-back goes on in the same combat mode (no peace, no new walk)
                judgedAt = S.Clock.Now;
                if (host.RunPastAgain() is not { } again)
                {
                    break;
                }

                if (host.RetreatIsDue && !retreating)
                {
                    yield break; // a safety line on the judgement set the retreat due: the walk's retreat follows
                }

                Log(string.Create(CultureInfo.InvariantCulture, $"fight-back {FoughtBack} on the {leg} leg: the attackers it was stopped for are spent, but the run past judges again and must fight on: {again}; the fight-back goes on, no peace and no new walk ({casts} cast(s), {kills} kill(s) so far; M6 14 caught on the run)"));
                continue;
            }

            if (S.Clock.Now - started - _fireSeconds > FightBackSeconds) // M6 14 fire dance: the dance's seconds are not the attacker's
            {
                var timedOut = string.Create(CultureInfo.InvariantCulture, $"'{name}' 0x{guid:X8} still attacks after {FightBackSeconds:0} s of fight-back ({casts} cast(s), {kills} kill(s))");
                host.Leave(guid, name, forRange: false, "given up: " + timedOut, double.PositiveInfinity); // M6 6 E: the walk back (or the walk on, retreating) does not stop for it again
                if (!retreating)
                {
                    host.RetreatDue("timeout", timedOut);
                    yield break;
                }

                Log($"fight-back (retreating): {timedOut}: left; the walk back goes on");
                started = S.Clock.Now;
                continue;
            }

            if (!retreating && host.HealthUnderLine(out var healthWord) is { } under)
            {
                host.RetreatDue("health", $"{healthWord} is under the retreat line {under} with '{name}' 0x{guid:X8} attacking");
                yield break;
            }

            if (!retreating && reserve is { } line && ManaNow() is { } held && Vitals.ManaWayOut.Decide(new Vitals.WayOutInput(true, held, line, Attackers: true, KillPending: false, Press: false, Refused: false)) is { Yes: true }
                && LeavesAtTheLine(in line, held, out var fireWhy))
            {
                // M6 14 retreat attackers (rule 4): at the reserve, with the recall still paid, she leaves now; under the recall's own cost the old rules stand (no-mana, the walk back). M6 14 fire dance: a round of the dance under
                // fire holds the leaving while she is not losing (the bolt's refusal below then dances)
                host.RetreatDue(Vitals.ManaWayOut.Leave, string.Create(CultureInfo.InvariantCulture, $"mana {held} is at the way out's reserve {reserveWhy} with '{name}' 0x{guid:X8} attacking: Lifestone Recall is still paid, so she leaves while she can heal and leave (the dance under fire: {fireWhy})"));
                yield break;
            }

            if (guid != target)
            {
                target = guid;
                Log(string.Create(CultureInfo.InvariantCulture, $"fight-back: target '{name}' 0x{guid:X8} at {metres:0.0} m; {host.NewTarget(guid, name)}"));
            }

            if (!tallies.TryGetValue(guid, out var tally))
            {
                tallies[guid] = tally = (S.Clock.Now, _fireSeconds, 0);
            }

            var planar = PlanarMetres(guid, metres); // M6 14 the hovering Zefir: ACE measures a spell's range in two dimensions (Player.VerifySpellRange)
            if (tally.Casts == 0 && S.Clock.Now - tally.Since - (_fireSeconds - tally.Fire) > FightBackDecisionSeconds)
            {
                // M6 14 the hovering Zefir (rule 3): no cast at it in one decision window: never a fight-back that waits on an attacker it does not hit; the next attacker, or the walk goes on. It is fought again when it catches her
                // (attacks from melee reach), not for attacking from where it stood
                RunPastOutOfReach(guid, name, Vitals.RunPast.MeleeReachMetres, string.Create(CultureInfo.InvariantCulture, $"no cast at it in {FightBackDecisionSeconds:0} s of this fight-back ({planar:0.0} m off)"));
                continue;
            }

            var wcid = S.State.Objects.TryGet(guid, out var targetObject) ? targetObject.Weenie.WeenieClassId : 0u;
            var lifeStrike = _boltBlocked.Contains(guid); // M6 14 the Zefir: its bolts came to nothing: Harm Other, which no wall stops (Vitals/LifeStrike.cs)
            SpellInfo? spell;
            string bolt;
            BoltLack lack;
            if (lifeStrike)
            {
                var spent = LifeSpent(guid);
                if (spent || LifeStrikeAt(guid, wcid, planar) is not { } pick)
                {
                    var none = spent
                        ? $"its bolts came to nothing {FightScenario.WallHitsBeforeBlock} times in a row and its life casts {FightScenario.LifeThroughMissesBeforeAside} times"
                        : $"its bolts came to nothing {FightScenario.WallHitsBeforeBlock} times in a row and no life spell answers it ({_lifeWhy})";
                    if (!spent && LifeReach() is var lifeReach && lifeReach > 0 && planar > lifeReach)
                    {
                        RunPastOutOfReach(guid, name, lifeReach, none); // M6 14 the hovering Zefir: beyond every Harm Other's range: fought again with Life Magic if it attacks her from within it
                        continue;
                    }

                    host.Leave(guid, name, forRange: false, "given up: " + none, double.PositiveInfinity);
                    Log(string.Create(CultureInfo.InvariantCulture, $"fight-back: '{name}' 0x{guid:X8}: {none}: left; the walk goes on (its hits are still read by the safety lines; {host.LastAttackWord()})"));
                    continue;
                }

                (spell, bolt, lack) = (pick.Spell, pick.Why, BoltLack.None);
                Log($"fight-back: Life Magic at '{name}' 0x{guid:X8} ({planar:0.0} m), its bolts having come to nothing: {pick.Why}");
            }
            else
            {
                spell = Bolt(wcid, out bolt, out lack);
                if (spell is not null && SpellReach(spell) is var reach && planar > reach)
                {
                    // M6 14 the hovering Zefir (rule 1): ACE refuses a targeted spell beyond its range (Player.VerifySpellRange, Player_Magic.cs:481-506 @95ef426d): the bolt is not cast. Life Magic when a Harm Other reaches it (rule 2),
                    // else she runs past it (rule 3)
                    var beyond = string.Create(CultureInfo.InvariantCulture, $"it stands {planar:0.0} m off (planar, as ACE measures), beyond {spell.Name}'s range {reach:0.#} m");
                    if (!LifeSpent(guid) && LifeStrikeAt(guid, wcid, planar) is { } far)
                    {
                        (spell, bolt, lifeStrike) = (far.Spell, far.Why, true);
                        Log($"fight-back: Life Magic at '{name}' 0x{guid:X8}: {beyond}; {far.Why}");
                    }
                    else
                    {
                        // fought again from within the bolt's range, or a Harm Other's when that reaches farther and only the distance kept it from her
                        var lifeReach = LifeSpent(guid) ? 0f : LifeReach();
                        RunPastOutOfReach(guid, name, planar > lifeReach ? Math.Max(reach, lifeReach) : reach, string.Create(CultureInfo.InvariantCulture, $"{beyond}, and no life spell answers it ({(LifeSpent(guid) ? $"its life casts came to nothing {FightScenario.LifeThroughMissesBeforeAside} times" : _lifeWhy)})"));
                        continue;
                    }
                }
            }

            if (spell is null && lack == BoltLack.NoComponents)
            {
                // M6 6 R1F: every bolt the book holds lacks a component (the pack's check before, or ACE's 0x0400 on this leg): the fifth why, retreat at once; the walk back leaves such an attacker.
                var lacking = $"no War Magic bolt can be cast at '{name}' 0x{guid:X8}: {bolt}";
                if (!retreating)
                {
                    host.RetreatDue("no-components", lacking);
                    yield break;
                }

                host.Leave(guid, name, forRange: false, "given up: " + lacking, double.PositiveInfinity);
                Log($"fight-back (retreating): {lacking}: left; the walk back goes on");
                continue;
            }

            if (spell is null)
            {
                // M6 6 R1F (E's TODO(m6-fight-back-war-spell) reworded as the rule): a book without a War Magic projectile cannot answer an attacker (M5 D88), so the leg stops by name rather than walk on under attack.
                host.Fail($"fight-back:{leg}:no-war-spell", "the spellbook holds no War Magic projectile to fight back with: the leg stops by name (the rule: no bolt, no fight-back, no walk on under attack)");
            }

            if (S.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _) && mana < spell!.BaseMana)
            {
                var lacking = string.Create(CultureInfo.InvariantCulture, $"mana {mana} is under {spell.Name}'s {spell.BaseMana} with '{name}' 0x{guid:X8} attacking");
                if (!retreating)
                {
                    host.RetreatDue("no-mana", lacking);
                    yield break;
                }

                host.Leave(guid, name, forRange: false, "given up: " + lacking, double.PositiveInfinity);
                Log($"fight-back (retreating): {lacking}: left; the walk back goes on");
                continue;
            }

            if (!retreating && reserve is { } kept && ManaNow() is { } have
                && Vitals.ManaWayOut.Spend(new Vitals.WayOutSpend(Vitals.WayOutSpendKind.Bolt, have, GearCasts.ManaCeiling(spell!, 0), kept, Attackers: true)) is { Yes: false } refused)
            {
                // M6 14 retreat attackers (rule 4): the bolt would take the mana under the way out's reserve: refused. M6 14 fire dance: she dances under fire instead of leaving while the dance covers her, she is not losing and it
                // still pays (FireDance), one cast, checked after it lands, then the loop reads everything again and she bolts on; else she leaves with the recall and a heal still paid, the dance's answer named
                var boltCost = GearCasts.ManaCeiling(spell!, 0);
                var fire = FireDanceNow(in kept, boltCost, out var fireLevels);
                if (fire.Move == FireDanceMove.Leave)
                {
                    host.RetreatDue(Vitals.ManaWayOut.Leave, string.Create(CultureInfo.InvariantCulture, $"{spell!.Name} ({boltCost} mana) would take the mana {have} under the way out's reserve {reserveWhy} with '{name}' 0x{guid:X8} attacking ({refused.Why}); the dance under fire: {fire.Token}: {fire.Why}"));
                    yield break;
                }

                Log(string.Create(CultureInfo.InvariantCulture, $"fire dance: {spell!.Name} ({boltCost} mana) would take the mana {have} under the way out's reserve {reserveWhy} with '{name}' 0x{guid:X8} attacking ({refused.Why}): she dances instead of leaving: {fire.Token}: {fire.Why}; health {VitalWord(MageTemplate.MaxHealth)}, stamina {VitalWord(MageTemplate.MaxStamina)}"));
                foreach (var h in FireDanceCast(fire, fireLevels, name, guid))
                {
                    yield return h;
                }

                continue; // check, act, check: the attacker, the lines and the bolt are read again
            }

            // M6 6 R1F: the check before each cast: the magic mode (check, act, check; a refused cast left the climber out of it in live round 1); not reached is the named stop.
            foreach (var h in WarBoltPick.MagicBeforeCast(S, TravelLeg.ModeSeconds, Log))
            {
                yield return h;
            }

            if (S.State.Self.Qualities.CombatMode != CombatModes.Magic)
            {
                host.Fail($"fight-back:{leg}:no-magic-mode", string.Create(CultureInfo.InvariantCulture, $"the magic mode was not reached within {TravelLeg.ModeSeconds:0} s before a bolt at '{name}' 0x{guid:X8} (the mode is {S.State.Self.Qualities.CombatMode})"));
            }

            var cast = new TravelLeg.CastResult();
            var chatMark = S.State.Chat.Total; // M6 14 bolt wait: the bolt's own lines are the ones after this
            var picture = ThreatBoard.For(S).Health;
            picture.Aim(guid); // M6 16 Q5: a line naming its name, until the bolt's wait ends, is this target's in the one health picture (not the nearest of the name)
            foreach (var h in host.Cast(spell!.Id, guid, cast))
            {
                yield return h;
            }

            casts++;
            tallies[guid] = tallies[guid] with { Casts = tallies[guid].Casts + 1 };
            Casts++; // M6 14 the crowded passage: the walk's stops read each fight-back's casts and kills
            host.Counted(SkirmishTally.Cast); // M6 14 approach cost (rule 2): the crossing's casts
            Log(string.Create(CultureInfo.InvariantCulture, $"fight-back: cast {spell.Name} ({bolt}) at '{name}' 0x{guid:X8} ({metres:0.0} m): {ScriptLeg.Describe(cast.Handle!)}; health {VitalWord(MageTemplate.MaxHealth)}, mana {VitalWord(MageTemplate.MaxMana)}"));
            if (WarBoltPick.IsComponentRefusal(cast.Handle, host.RefusalSince(cast.Mark)))
            {
                _boltsRefused.Add(spell.Id); // M6 6 R1F: never cast again on this leg; the next pick follows at once
                picture.Release(guid); // M6 16 Q5: nothing flew
                Log($"fight-back: {spell.Name} was refused for a missing component (0x0400): dropped for this leg; the next bolt is picked");
                continue;
            }

            // M6 14 bolt wait (the owner, 2026-11-15; live ladder23 02:50:34.006 landed, 02:50:36.021 the next try: two seconds of nothing after every bolt that does not kill): the wait ends when this bolt has resolved (its damage line
            // or the target's resist, BoltLine) and the kill notice's grace has passed, or at the kill; the bound stands when no line comes (a wall hit, a projectile lost)
            var dying = guid;
            var waitFrom = S.Clock.Now;
            var lifeCast = lifeStrike;
            var (resolvedAt, resolvedBy) = (double.NaN, string.Empty);
            foreach (var h in host.Until(() => BoltResolved(dying, chatMark, ref resolvedAt, ref resolvedBy), BoltWaitSeconds))
            {
                yield return h;
            }

            picture.Release(dying); // M6 16 Q5: the bolt has resolved (or its wait ended): a later line of the name is the nearest's again

            if (Alive(dying))
            {
                Log(double.IsNaN(resolvedAt)
                    ? string.Create(CultureInfo.InvariantCulture, $"fight-back: no damage or resist line for {spell.Name} at '{name}' 0x{dying:X8} within {BoltWaitSeconds:0.#} s (a wall hit or a projectile lost): the next cast")
                    : string.Create(CultureInfo.InvariantCulture, $"fight-back: {spell.Name} resolved at '{name}' 0x{dying:X8} ({resolvedBy}) {resolvedAt - waitFrom:0.0} s after the cast's end; no kill notice within the grace {KillNoticeGraceSeconds:0.#} s: the next cast at once"));
                var zeroHit = !double.IsNaN(resolvedAt) && host.ZeroPointsComeToNothing && _resolvedZero; // M6 16 Q8: a host may count a line of 0 points as nothing (the quest steps' rule; the travel leg: any line resolves); the line shows it flew
                NoteNoLine(dying, name, spell.Name, lifeCast, resolved: !double.IsNaN(resolvedAt) && !zeroHit, flew: zeroHit || cast.Handle?.Outcome is { Kind: ActionOutcomeKind.Confirmed }); // M6 14 the Zefir: bolts that come to nothing switch to Life Magic; life casts that come to nothing leave it
            }

            if (!Alive(dying))
            {
                kills++;
                Kills++;
                host.Counted(SkirmishTally.Kill);
                Log($"fight-back: '{name}' 0x{dying:X8} is dead ({casts} cast(s) in this fight-back)");
                if (!retreating && host.GivesUp() is { } gaveUp)
                {
                    // M6 14 approach cost (rule 2): the measured crossing will not reach the portal above the reserve: she leaves now, before the mana line forces it, and the crossing is held for the rest of the run
                    host.RetreatDue(Vitals.ApproachCost.AbandonedPrefix, gaveUp);
                    yield break;
                }
            }
        }

        // M6 14 caught on the run: the end line says truthfully why it ended: the creatures still on her and why none is fought now, and the last attack heard
        Log(string.Create(CultureInfo.InvariantCulture, $"fight-back {FoughtBack} on the {leg} leg: done: {host.EndWhy(onTheRun)}; casts={casts} kills={kills} s={S.Clock.Now - started:0.0}; health {VitalWord(MageTemplate.MaxHealth)}, mana {VitalWord(MageTemplate.MaxMana)}"));
        foreach (var h in host.PeaceNow())
        {
            yield return h;
        }
    }

    /// <summary>The fight-back's bolt now (<see cref="Bolt"/>: castable, its components held) and its base mana, or 0 with the reason: a term of a recharge's floor.</summary>
    /// <param name="why">The pick in words.</param>
    internal uint BoltMana(out string why)
    {
        var spell = Bolt(0, out why, out _); // no target: the old pick (the highest castable)
        return spell?.BaseMana ?? 0;
    }

    /// <summary>M6 16 Q8: the bolt this skirmish would cast at a creature of <paramref name="wcid"/> now (<see cref="Bolt"/>: castable, its components held, the bolts refused for its host left out; 0: no target), for a host's check before it.</summary>
    /// <param name="wcid">The creature's weenie class, or 0.</param>
    /// <param name="why">The pick in words.</param>
    /// <param name="lack">Why a null pick is null.</param>
    internal SpellInfo? BoltFor(uint wcid, out string why, out BoltLack lack) => Bolt(wcid, out why, out lack);

    private bool Alive(uint guid) =>
        guid != 0 && S.State.Objects.TryGet(guid, out var o) && o.Health is not <= 0f && o.Motion is not { Forward: WorldObjectTable.DeadMotionLowWord };

    /// <summary>The wait after a bolt ends: <paramref name="target"/> is dead, or this bolt's line (<see cref="BoltLine"/>) has come and <see cref="KillNoticeGraceSeconds"/> have passed since; the line's time and words are kept.</summary>
    private bool BoltResolved(uint target, long chatMark, ref double resolvedAt, ref string resolvedBy)
    {
        if (!Alive(target))
        {
            return true;
        }

        if (double.IsNaN(resolvedAt) && BoltLine(chatMark, out var zero) is { } line)
        {
            (resolvedAt, resolvedBy, _resolvedZero) = (S.Clock.Now, line, zero);
        }

        return !double.IsNaN(resolvedAt) && S.Clock.Now - resolvedAt >= KillNoticeGraceSeconds;
    }

    /// <summary>
    /// M6 14 bolt wait: the first line since <paramref name="mark"/> (<c>S.State.Chat.Total</c> before the cast) that says her bolt resolved, or null: a System line of the Magic type (<see cref="FightTally.MagicChatType"/>) that
    /// <see cref="FightTally.Classify"/> reads as her damage dealt ("You scorch ... for 82 points with Ilservian's Flame."), or as the target's resist of her spell ("... resists your spell"; not her own resist of a creature's spell). The
    /// ground fight ends its hit wait the same way (<c>FightScenario.HitWait</c>, its damage lines).
    /// </summary>
    private string? BoltLine(long mark, out bool zero)
    {
        zero = false;
        var chat = S.State.Chat;
        var fresh = (int)Math.Min(chat.Count, chat.Total - mark);
        for (var i = chat.Count - fresh; i < chat.Count; i++)
        {
            var line = chat[i];
            if (line.Kind != ChatKind.System || line.ChatType != FightTally.MagicChatType)
            {
                continue;
            }

            var kind = FightTally.Classify(line.Text, out var amount, out _);
            if (kind is MagicLineKind.DamageDealt or MagicLineKind.DrainDealt || (kind == MagicLineKind.Resist && line.Text.EndsWith(" resists your spell", StringComparison.Ordinal)))
            {
                zero = kind != MagicLineKind.Resist && amount <= 0; // M6 16 Q8: a damage or drain line of 0 points (read by a host that counts it as nothing)
                return kind == MagicLineKind.DamageDealt ? "its damage line" : kind == MagicLineKind.DrainDealt ? "its drain line (Harm Other's)" : "the target resisted it";
            }
        }

        return null;
    }

    private string VitalWord(uint id) => S.State.Self.Qualities.TryGetVitalLevels(id, out var cur, out var max) ? string.Create(CultureInfo.InvariantCulture, $"{cur}/{max}") : "unread";

    /// <summary>The mana held, or null when unread.</summary>
    private uint? ManaNow() => S.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _) ? mana : null;

    /// <summary>
    /// The fight-back's bolt: <see cref="WarBoltPick.ChooseCastableFor"/> over the book (the SpellTable's <see cref="JourneyContext.Spells"/>) at <see cref="FightBackChance"/>, the bolts refused for a component and the ones the pack is short of
    /// left out (M6 6 R1F); the choice in words. The pack is judged on the foci formula when it holds the War Magic focus (<see cref="LevelFloor.HeldForAccount"/> with no account name: the journey's context carries the account's key, not the
    /// name the server hashes, so a pack without the focus is not judged before the cast and the refusal rule drops the bolt).
    /// </summary>
    private SpellInfo? Bolt(uint wcid, out string why, out BoltLack lack)
    {
        var skill = GearCasts.TryCurrentSkill(S.State.Self.Qualities, MageTemplate.WarMagic, out var current, out var skillWhy) ? current : (uint?)null;
        var held = LevelFloor.HeldForAccount(S.State, MagicSchool.WarMagic, host.AccountName); // M6 16 Q8: the host's account (null for the travel leg and the quest steps, as before)
        var spell = WarBoltPick.ChooseCastableFor(Context.Spells, S.State.Self.Spellbook.Spells.Keys, skill, FightBackChance, held, host.AccountName, _boltsRefused, wcid, out why, out lack); // M6 14: the line that hits this creature hardest (BoltElement), as the hunting fight's
        if (skill is null && spell is not null)
        {
            why += " (" + skillWhy + ")";
        }

        return spell;
    }
}
