using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Threat;
using Lifestone.Client.World;
using Lifestone.Data.Magic;
using Lifestone.Data.World;
using DatReaderWriter.Enums;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 4 F6 (the live round 4: the Throwaway was killed by island tuskers on the 609 m run from the Refuge to the Burrow's entry front and respawned at its sanctuary; the leg read it as <c>travel:run:moved</c>): the walking legs' life checks
/// (check, act, check, M5 D141) and the run's fight-back (M5 D88: always fight back an attacker).
/// <list type="bullet">
/// <item><b>Check before</b> (<see cref="ReadLife"/>): the health, alive or not, the deaths property (PropertyInt 43, NumDeaths), the vitae and the death-notice mark, one line. A body already at health 0 does not walk (<c>dead:&lt;leg&gt;:before-walk</c>).</item>
/// <item><b>Check after a move</b> (<see cref="DiedAfterMove"/>): when the server moved the body during a walk, the death evidence is read: the death notice (<see cref="PlayerDied"/>: ACE's 0x01AC "You have been killed by ..."), NumDeaths risen, a vitae
/// enchantment new or deeper (ACE's <c>Player_Death.cs</c> Die: the Dead motion, health 0, NumDeaths, the vitae; @95ef426d), the health at 0. Any of them is a death: the leg ends <c>died:&lt;leg&gt;:respawned=&lt;cell&gt;</c>, logged with
/// the cell it died in (the last cell before the move), the killer when the server names it, and the respawn cell; never <c>travel:run:moved</c>. The corpse stays where the body died (the client sees it no more after the
/// respawn: <c>corpse=at-death-cell</c>). No recovery in item 4: the retreat, the corpse run and the vitae on the island are item 6's. A move with no death keeps its rule (a hop's F5 taken=touch or moved-elsewhere; a run's
/// <c>travel:run:moved</c>; M6 7 N, a run to an entry: a move into the entry's destination is the entry taken on touch, <see cref="WalkTakenFrom"/>).</item>
/// <item><b>Fight back</b> (<see cref="WalkGuarded"/>): during a run on foot, a creature heard attacking the player (<see cref="AttackWatch"/>: a damage or evasion notice or a hostile spell line, within
/// <see cref="FightBackHeardSeconds"/>) that stands within <see cref="FightBackReachMetres"/> (<see cref="HostileReach"/>, the hunt's and the harm-heal station's shared rule and reach) stops the walk; the leg fights it with
/// <see cref="FightBack"/>, then plans the walk again from where the body stands. M6 8 G (<c>m6-hop-walk-fight-back</c>): a portal hop's walks are the same walk (<see cref="HopThrough"/>: the way in, the front, the seek to the portal, the way out). A walk that cannot be planned after a fight-back
/// is planned once more (<c>recovery=replan-after-fight</c>); failing that its own failure ends it by name. An attacker the bolts cannot hurt keeps this rule: the fight-back runs to <see cref="FightBackSeconds"/> (or the health line, or the mana) and the leg retreats.
/// M6 14 run past (D137, the owner's amendment of M5 D88 on travel legs): the walk stops only for an attacker she cannot outpace, one in her way, a ranged one whose hits would reach the line, or one still on her at the door
/// (<c>TravelLegRunPast.cs</c>); the fight-back then takes those alone. M6 14 caught on the run (the owner, 2026-11-15): one that keeps pace and stands in melee has caught her and is fought at once (<c>caught</c>); when the set is spent
/// the run past judges again there and the same fight-back goes on while it names any (<see cref="TravelLeg.RunPastAgain"/>); every creature is read where the client draws it (<see cref="HostileReach"/>); one left beyond the cast range
/// that attacks her again is fought (<see cref="ReleaseLeftThatAttackAgain"/>); a lull is no creature on her, whatever list it is on (<see cref="AttackerOnHer"/>), and each end or lull line names the last attack heard.</item>
/// <item><b>Where the fight-back stands down</b> (<see cref="WalkStandsDown"/>, by design): a body not present (a portal hop in flight: from the Use to the arrival no walk runs, and a teleport under way stops no walk); a death not yet handled (health 0: the walk's failure and
/// <see cref="DiedAfterMove"/> name it); the urgent recall (a cast, <c>AutopilotScenario.BeginUrgentRecall</c>: no guarded walk runs, and the keeper's rule stands down for it) and this leg's own retreat by recall (the cast is not stopped; only the walk back answers an attacker).</item>
/// <item><b>Retreat</b> (M6 6 E, <see cref="Retreat"/>): at <see cref="MaxFightBacks"/> fight-backs (<c>cap</c>), a fight-back without the mana for its bolt (<c>no-mana</c>), the health under the ground's retreat line during a fight-back
/// (<c>health</c>), a fight-back past <see cref="FightBackSeconds"/> (<c>timeout</c>: the same retreat fits, an attacker the bolts cannot kill is left behind) or, M6 6 R1F, no bolt whose components the pack holds (<c>no-components</c>: the pack's check
/// before, or ACE's 0x0400 on every bolt the book holds) the leg leaves by Lifestone Recall, else walks back to its start point, and fails
/// <c>retreat:&lt;leg&gt;:&lt;recall|walked-back&gt;</c>; a failed retreat is <c>retreat-failed:&lt;leg&gt;:...</c>.</item>
/// <item><b>M6 14 retreat attackers</b> (live 2026-11-14 10:59:18 UTC: on the Holding's approach two Infected Assailers killed the climber while the line was the destination's Rampager, 10%, and her mana was spent under the recall's): the
/// health line is the attackers' own (<see cref="RetreatLine"/>: each creature present by its facts across every profile, its damage type and her live resistance to it; the general fallback for one with no facts; the hits this
/// fight-back measures as its floor), and a fight-back keeps the way out's reserve (<see cref="ReserveLine"/>: Lifestone Recall's cost plus a heal's), leaving at it with the recall still paid (<c>mana-line</c>), refusing a bolt that
/// would spend into it, and publishing it for the vitals guard's heals while it lasts (<see cref="JourneyFacts.FightBackReserve"/>).</item>
/// </list>
/// </summary>
internal abstract partial class TravelLeg
{
    /// <summary>The leg word of a portal hop's walk in a death's name (<c>died:hop:respawned=...</c>).</summary>
    internal const string HopLegKind = "hop";

    /// <summary>How recent an attack must be for its creature to stop a walk, session seconds (the hunt's <see cref="HuntScenario.DefendHeardSeconds"/>).</summary>
    protected const double FightBackHeardSeconds = HuntScenario.DefendHeardSeconds;

    /// <summary>The reach inside which an attacker stops a walk (the hunt's <see cref="HuntScenario.DefendReachMetres"/>, <see cref="HostileReach.DefaultMetres"/>).</summary>
    protected const double FightBackReachMetres = HuntScenario.DefendReachMetres;

    /// <summary>Seconds one fight-back may take before the leg retreats (<c>why=timeout</c>, M6 6 E; the skirmish's own bound, <see cref="Skirmish.FightBackSeconds"/>).</summary>
    protected const double FightBackSeconds = Skirmish.FightBackSeconds;

    /// <summary>The fight-backs one walk makes at most before it retreats (<c>why=cap</c>; Lifestone's own bound: a run that is attacked again and again retreats, it does not fight on; the retreat's walk back has no cap).</summary>
    protected const int MaxFightBacks = 8;

    /// <summary>The cast chance the fight-back's bolt level must reach on the current War Magic skill (the skirmish's, <see cref="Skirmish.FightBackChance"/>).</summary>
    protected const double FightBackChance = Skirmish.FightBackChance;

    private AttackWatch? _attackWatch;
    private readonly List<Vitals.WalkStop> _walkStops = [];

    /// <summary>
    /// The attackers the fight-backs left on this leg, by guid (<see cref="Leave"/>): one that stayed beyond the cast range (M6 14 caught on the run: no longer left once its name is heard attacking her again,
    /// <see cref="ReleaseLeftThatAttackAgain"/>), or one given up (the timeout, the walk back's no-mana or no-components), left for good.
    /// </summary>
    private readonly Dictionary<uint, LeftAttacker> _left = [];

    /// <summary>One attacker a fight-back left (<see cref="_left"/>).</summary>
    /// <param name="Name">Its name.</param>
    /// <param name="Why">Why it was left, in words.</param>
    /// <param name="ForRange">True when it was left for standing beyond her reach (it is fought again when it attacks her again from within <paramref name="Reach"/>).</param>
    /// <param name="Attacks">The attacks heard from its name when it was left (<see cref="AttackWatch.AttacksOf"/>).</param>
    /// <param name="At">The session clock it was left at.</param>
    /// <param name="Reach">M6 14 the hovering Zefir: the planar metres within which her spell reaches it (<c>Skirmish.SpellReach</c>); infinite for one left before the reach was read.</param>
    private readonly record struct LeftAttacker(string Name, string Why, bool ForRange, int Attacks, double At, double Reach);

    /// <summary>The fight-backs this leg made (report-only; the skirmish counts them).</summary>
    protected int FoughtBack => Skirmish.FoughtBack;

    /// <summary>The attackers the fight-backs killed (report-only; the skirmish counts them).</summary>
    protected int FightBackKills => Skirmish.Kills;

    /// <summary>What the check before a walk read (<see cref="ReadLife"/>).</summary>
    /// <param name="Known">True when the health was read.</param>
    /// <param name="Health">The current health.</param>
    /// <param name="MaxHealth">The maximum health.</param>
    /// <param name="Deaths">NumDeaths (PropertyInt 43), or -1 when unread.</param>
    /// <param name="Vitae">The vitae's multiplier (1 with none).</param>
    /// <param name="NoticeMark">The death-notice mark.</param>
    protected readonly record struct LifeMark(bool Known, uint Health, uint MaxHealth, int Deaths, float Vitae, int NoticeMark);

    /// <summary>The check before a walking leg (M6 4 F6): the health, alive, the deaths, the vitae, one line; a body at health 0 fails <c>dead:&lt;leg&gt;:before-walk</c>.</summary>
    /// <param name="leg">The leg's word in a failure's name (<c>run</c>, <c>walk-home</c>, <c>hop</c>).</param>
    /// <param name="what">The leg in words (logged).</param>
    protected LifeMark ReadLife(string leg, string what)
    {
        var q = S.State.Self.Qualities;
        var known = q.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var max);
        var deaths = q.TryGetInt(DeathTally.NumDeathsProperty, out var d) ? d : -1;
        var vitae = q.TryGetVitae(out _) ? EffectRules.VitaeValue(q) : 1f;
        var mark = new LifeMark(known, health, max, deaths, vitae, DeathMark);
        Log(string.Create(CultureInfo.InvariantCulture, $"life check before {what}: health={(known ? $"{health}/{max}" : "unread")} alive={(known ? (health > 0 ? "yes" : "no") : "unknown")} deaths={(deaths < 0 ? "unread" : deaths.ToString(CultureInfo.InvariantCulture))} vitae={VitaeWord(vitae)} cell={CellWord(S.State.Self.Position)}"));
        if (known && health == 0)
        {
            Fail($"dead:{leg}:before-walk", $"the health is 0 of {max} before {what}: the body is dead and does not walk");
        }

        return mark;
    }

    /// <summary>
    /// The check after the server moved the body during a walk (M6 4 F6): waits until it is present, then reads the death evidence against <paramref name="before"/>; a death fails the leg <c>died:&lt;leg&gt;:respawned=&lt;cell&gt;</c>, logged
    /// with <paramref name="lastAlive"/>'s cell, the killer and the evidence. No death: nothing (the caller's move rule follows).
    /// </summary>
    protected IEnumerable<ActionHandle?> DiedAfterMove(string leg, string what, LifeMark before, CellFrame? lastAlive)
    {
        foreach (var h in Until(() => S.State.Self.Travel == SelfTravel.Present || Dead(before, out _), PresentSeconds))
        {
            yield return h;
        }

        if (!Dead(before, out var evidence))
        {
            Log($"life check after the move on {what}: no death evidence (no notice, deaths and vitae unchanged, health above 0): the move rule decides");
            yield break;
        }

        foreach (var h in Until(() => S.State.Self.Travel == SelfTravel.Present, PresentSeconds))
        {
            yield return h;
        }

        var notice = DiedSince(before.NoticeMark);
        var killer = notice is { } n ? $"'{n.Text}'{(n.Killer != 0 ? string.Create(CultureInfo.InvariantCulture, $" (killer 0x{n.Killer:X8})") : string.Empty)}" : "not named by the server";
        var heard = _attackWatch is { } watch && watch.HeardWithin(FightBackHeardSeconds * 3) is { Count: > 0 } recent ? string.Join(",", recent.Select(r => "'" + r + "'")) : "none";
        var respawn = S.State.Self.Position;
        Log(string.Create(CultureInfo.InvariantCulture, $"died on {what}: death_cell={CellWord(lastAlive)} killer={killer} attackers_heard={heard} evidence={evidence} respawned={CellWord(respawn)} corpse=at-death-cell; no recovery in item 4 (the retreat, the corpse run and the vitae are item 6's)"));
        Fail(string.Create(CultureInfo.InvariantCulture, $"died:{leg}:respawned={CellWord(respawn)}"), $"the player died on {what} in {CellWord(lastAlive)} ({evidence}; killer {killer}) and respawned in {CellWord(respawn)}");
    }

    /// <summary>True with the evidence when the body died since <paramref name="before"/>: the notice, NumDeaths risen, a vitae new or deeper, the health at 0.</summary>
    private bool Dead(LifeMark before, out string evidence)
    {
        var q = S.State.Self.Qualities;
        var seen = new List<string>();
        if (DiedSince(before.NoticeMark) is not null)
        {
            seen.Add("notice");
        }

        if (before.Deaths >= 0 && q.TryGetInt(DeathTally.NumDeathsProperty, out var deaths) && deaths > before.Deaths)
        {
            seen.Add(string.Create(CultureInfo.InvariantCulture, $"deaths {before.Deaths}->{deaths}"));
        }

        if (q.TryGetVitae(out _) && EffectRules.VitaeValue(q) is var vitae && vitae < before.Vitae - 0.001f)
        {
            seen.Add("vitae " + VitaeWord(vitae));
        }

        if (q.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out _) && health == 0)
        {
            seen.Add("health 0");
        }

        evidence = string.Join("+", seen);
        return seen.Count > 0;
    }

    private static string VitaeWord(float vitae) => vitae >= 1f ? "none" : string.Create(CultureInfo.InvariantCulture, $"{EffectRules.VitaePercentage(vitae)}%");

    private static string CellWord(CellFrame? at) => at is { } p ? string.Create(CultureInfo.InvariantCulture, $"0x{p.CellId:X8}") : "none";

    /// <summary>
    /// A walk on foot to a point with the life checks and the fight-back (M6 4 F6): the check before (<see cref="ReadLife"/>), the walk (<see cref="WalkToPoint"/>, stopped by an attacker in reach), after a move the death check
    /// (<see cref="DiedAfterMove"/>, else the walk's own failure stands: <c>travel:run:moved</c>), after a stop the fight-back (<see cref="FightBack"/>) and the walk planned again, each logged.
    /// </summary>
    /// <param name="leg">The leg's word in a failure's name (<c>run</c>, <c>walk-home</c>).</param>
    /// <param name="name">The station's word for the walk.</param>
    /// <param name="label">The destination's label.</param>
    /// <param name="cell">The point's cell.</param>
    /// <param name="at">The point.</param>
    /// <param name="purpose">The travel purpose.</param>
    /// <param name="why">The purpose in words.</param>
    /// <param name="takenInto">
    /// M6 7 N: the landblock the portal at the walk's end drops the body in, or null: a teleport during the walk into it, with no death evidence, ends the walk (<see cref="WalkTakenFrom"/>; live: the run to the entry's front touched the portal, ACE <c>Portal.OnCollideObject</c>,
    /// Portal.cs:103-106 @95ef426d, and the leg failed <c>travel:run:moved</c> with the body in the ground); a move anywhere else keeps the walk's own failure.
    /// </param>
    /// <param name="keepOff">M6 7 AI: for a walk to a portal, the other portals it keeps clear of, or null.</param>
    /// <param name="way">M6 8 B1: the file's way to the point, or null: planned.</param>
    /// <param name="knownLife">M6 8 G: the life check the caller already read (a hop's), or null: read here.</param>
    /// <param name="moveWhat">M6 8 G: the walk in the death check's words (a hop's <c>hop &lt;key&gt;</c>), or null: the leg's.</param>
    /// <param name="door">M6 14 run past (D137): true for a walk that ends at a portal's door (the run's entry front; a hop's door is its seek, <see cref="AttackerStopsSeek"/>): the attackers still on her there are fought before the portal (<see cref="DoorStops"/>).</param>
    /// <param name="use">The hub walk: the object the walk ends by Using (<see cref="TravelRequest.Use"/>), or null.</param>
    protected IEnumerable<ActionHandle?> WalkGuarded(string leg, string name, string label, uint cell, Vector3 at, TravelPurpose purpose, string why, uint? takenInto = null, PortalKeepOff? keepOff = null, IReadOnlyList<CellFrame>? way = null, LifeMark? knownLife = null, string? moveWhat = null, bool door = false, NavUseTarget? use = null)
    {
        WalkTakenFrom = null;
        StartAttackWatch();
        BeginAwarenessLeg(leg, label); // M6 16 E1f: the walk-awareness shadow's leg (report-only, TravelLegAwarenessShadow.cs)
        var start = S.State.Self.Position; // M6 6 E: the leg's start point (the last hop's arrival: the Refuge, the hub drop), the retreat's walk back
        var life = knownLife ?? ReadLife(leg, $"the {leg} leg to {label}");
        foreach (var h in GuardedWalk(leg, name, label, cell, at, purpose, why, life, retreating: false, takenInto, keepOff, way, moveWhat, door, use))
        {
            yield return h;
        }

        if (_retreatWhy is not null)
        {
            foreach (var h in Retreat(leg, label, start))
            {
                yield return h;
            }
        }

        EndAwarenessLeg(); // M6 16 E1f: the shadow's line for this leg, when it passed a creature inside its own range (a leg that fails is closed by DisposeSafety)
    }

    /// <summary>
    /// The guarded walk's loop (M6 4 F6): the walk, the death check after a move, the fight-back and the walk planned again. Out (<paramref name="retreating"/> false), a stop at <see cref="MaxFightBacks"/> or a fight-back that sets
    /// <see cref="_retreatWhy"/> ends the loop for <see cref="Retreat"/>; the retreat's walk back (true) has no cap and never retreats again (M6 6 E).
    /// </summary>
    private IEnumerable<ActionHandle?> GuardedWalk(string leg, string name, string label, uint cell, Vector3 at, TravelPurpose purpose, string why, LifeMark life, bool retreating, uint? takenInto = null, PortalKeepOff? keepOff = null, IReadOnlyList<CellFrame>? way = null, string? moveWhat = null, bool door = false, NavUseTarget? use = null)
    {
        var replanned = false;
        var (danceStops, afterDance) = (0, false); // M6 the dance before running on (rule b): the walk's stops for an owed dance, and whether the last stop was one
        _walkStops.Clear(); // M6 14 the crowded passage: this walk's stops by attackers, judged by progress (WalkStops)
        for (var walk = 0; ; walk++)
        {
            var travelMark = TravelMark;
            var lastAlive = S.State.Self.Position;
            string? failure = null;
            BeginRunPastWalk(cell, at, retreating); // M6 14 run past: her trail and the walk's goal, read each frame of the walk
            _danceStop = null;
            var reason = walk == 0 ? why
                : afterDance ? string.Create(CultureInfo.InvariantCulture, $"{why}; planned again after the walk dance")
                : string.Create(CultureInfo.InvariantCulture, $"{why}; planned again after fight-back {walk - danceStops}");
            foreach (var h in Catching(WalkToPoint(name, label, cell, at, purpose, reason, AttackerStopsWalk, keepOff, way, use), f => failure = f))
            {
                if (!TeleportSince(travelMark) && S.State.Self.Position is { } p)
                {
                    lastAlive = p;
                }

                yield return h;
            }

            if (TeleportSince(travelMark) || failure is not null)
            {
                foreach (var h in DiedAfterMove(leg, moveWhat ?? $"the {leg} leg to {label}", life, lastAlive))
                {
                    yield return h;
                }
            }

            if (TookOnTheWalk(takenInto, travelMark, lastAlive, leg, label))
            {
                yield break;
            }

            if (failure is not null)
            {
                if (walk > 0 && !replanned && !TeleportSince(travelMark))
                {
                    // M6 8 G: the fight may have carried the body where the walker cannot start from: one more plan from where it stands, then the walk's own failure ends it by name.
                    replanned = true;
                    Log($"recovery=replan-after-fight: the walk to {label} could not resume after fight-back {FoughtBack} ({failure}); planning it once more from {CellWord(S.State.Self.Position)}");
                    continue;
                }

                throw new InvalidOperationException(failure); // no death: the walk's own failure (travel:run:moved for a move by the server)
            }

            if (WalkInterrupted is not { } stopped)
            {
                if (!door || retreating || Context.Facts.RunOnly is not null || WalkStandsDown() || DoorStops() is not { } onHer)
                {
                    yield break; // at the point
                }

                // M6 14 run past (D137): at the door with attackers still on her: they are fought here, before the portal
                Log($"run past: at the door of the walk to {label}: the walk stops: {onHer}");
                stopped = onHer;
            }

            if (_danceStop is { } danceStop && string.Equals(danceStop.Stop, stopped, StringComparison.Ordinal))
            {
                // M6 the dance before running on (rule b): the walk stopped at a lull for the dance it owed: she dances where she stands (the gate read again first), then the walk is planned again from there; a dance is not a fight
                // (D137), so no walk stop is counted for it
                (_danceStop, afterDance) = (null, true);
                danceStops++;
                foreach (var h in DanceInTheLull(leg, label, danceStop.Line, danceStop.Why))
                {
                    yield return h;
                }

                if (_retreatWhy is not null)
                {
                    yield break; // the dance's fight-back or the measure after it set the retreat due: the walk's retreat follows
                }

                Log(string.Create(CultureInfo.InvariantCulture, $"walk dance: the walk to {label} is planned again from {CellWord(S.State.Self.Position)} {(_danceOwed is null ? "after the dance it owed" : "with the dance still owed (the gate closed again before it)")} (the dance before running on)"));
                continue;
            }

            afterDance = false;
            foreach (var h in FightBackOnWalk(leg, label, stopped, life, retreating, takenInto))
            {
                yield return h;
            }

            if (_walkEnded)
            {
                yield break;
            }

            Log(string.Create(CultureInfo.InvariantCulture, $"fight-back {FoughtBack} on the {leg} leg ended; the walk to {label} is planned again from {CellWord(S.State.Self.Position)}"));
        }
    }

    /// <summary>M6 8 G: true when the last <see cref="FightBackOnWalk"/> ended the walk (the retreat is due, or a portal took the body); false: the walk goes on.</summary>
    private bool _walkEnded;

    /// <summary>
    /// The stop of a walk (M6 4 F6, the middle of <see cref="GuardedWalk"/>; M6 8 G: a hop's seek to its portal reads it too): at <see cref="MaxFightBacks"/> the retreat is due; else the fight-back, the death check after a move, the portal's touch. <see cref="_walkEnded"/> says whether the walk goes on.
    /// </summary>
    private IEnumerable<ActionHandle?> FightBackOnWalk(string leg, string label, string stopped, LifeMark life, bool retreating, uint? takenInto)
    {
        _walkEnded = true;
        if (!retreating && _retreatWhy is not null)
        {
            yield break; // M6 14 run past: a safety line on the run set the retreat due: no fight, the retreat follows
        }

        var metres = HerPlanar() is { } herNow && _runGoal is { } goalNow ? (double?)System.Numerics.Vector2.Distance(herNow, goalNow) : null;
        if (!retreating && metres is { } m)
        {
            // M6 14 the crowded passage (live ladder44 22:30:07 UTC: the 8th stop recalled her 95 m short of the portal, gaining every stop, 24 kills in 24 casts): the stops are judged by progress and by the fight, not by their count
            var verdict = Vitals.WalkStops.Judge(_walkStops, m);
            Log($"walk stops: the walk to {label}, stop {_walkStops.Count + 1}: {verdict.Why} (M6 14: progress and the fight, not a count; the safety lines stand)");
            if (verdict.Retreat)
            {
                RetreatDue("cap", $"the walk to {label}: {verdict.Why}: {stopped}");
                yield break;
            }
        }
        else if (!retreating && FoughtBack >= MaxFightBacks)
        {
            RetreatDue("cap", string.Create(CultureInfo.InvariantCulture, $"the walk to {label} was stopped by attackers {FoughtBack} times (at most {MaxFightBacks}; her distance to the goal is not read, so the count stands): {stopped}"));
            yield break;
        }

        var (castsBefore, killsBefore) = (Skirmish.Casts, FightBackKills);
        string? failure = null;
        var fightMark = TravelMark;
        var lastAlive = S.State.Self.Position;
        foreach (var h in Catching(FightBack(leg, retreating), f => failure = f))
        {
            yield return h;
        }

        if (TeleportSince(fightMark) || failure is not null)
        {
            foreach (var h in DiedAfterMove(leg, $"the fight-back on the {leg} leg to {label}", life, lastAlive))
            {
                yield return h;
            }
        }

        if (metres is { } stoppedAt)
        {
            _walkStops.Add(new Vitals.WalkStop(stoppedAt, Skirmish.Casts - castsBefore, FightBackKills - killsBefore));
        }

        if (TookOnTheWalk(takenInto, fightMark, lastAlive, leg, label))
        {
            yield break;
        }

        if (failure is not null)
        {
            throw new InvalidOperationException(failure);
        }

        if (!retreating && _retreatWhy is not null)
        {
            yield break; // a fight-back that cannot go on (no mana, the health under the line, the timeout): the retreat follows
        }

        if (!retreating && CrossingGivenUp() is { } gaveUp)
        {
            // M6 14 approach cost (rule 2): the check after the fight-back, its heals in the mana: the rest of the crossing would end under the reserve, so she leaves now
            RetreatDue(Vitals.ApproachCost.AbandonedPrefix, gaveUp);
            yield break;
        }

        if (!retreating && DanceDueNow() is { } dance)
        {
            // M6 14 walk dance: the fight-back ended by its own end test (the lull): the crossing's measure asks for the dance, or (M6 14 top-off, the owner, 2026-11-15) she is not full: she tops herself off before the walk goes on
            // (mana to her maximum, stamina near full), and the crossing is measured again after. M6 the dance before running on (the owner, 2026-11-21): the gate decides (TravelLegDance.cs): chip hits the race holds from beyond
            // melee do not stop it; a refused dance is owed and the walk reads the lull each frame
            foreach (var h in DanceInTheLull(leg, label, dance.Line, dance.Why))
            {
                yield return h;
            }

            if (_retreatWhy is not null)
            {
                yield break; // the dance's fight-back or the measure after it set the retreat due
            }
        }

        _walkEnded = false;
    }

    /// <summary>M6 7 N: where the body last stood before a portal took it on touch during <see cref="WalkGuarded"/> (its <c>takenInto</c>), or null: the walk was not taken.</summary>
    protected CellFrame? WalkTakenFrom { get; private set; }

    /// <summary>
    /// M6 7 N: the check after a walk with a <c>takenInto</c> (the death check has run and found none): a teleport since <paramref name="mark"/> that left the player present in that landblock is the portal taken on touch, one line; anything else: false, the walk's own reading stands.
    /// </summary>
    private bool TookOnTheWalk(uint? takenInto, int mark, CellFrame? lastAlive, string leg, string label)
    {
        if (takenInto is not { } into || !TeleportSince(mark) || S.State.Self.Travel != SelfTravel.Present || S.State.Self.Position is not { } now || now.CellId >> 16 != into)
        {
            return false;
        }

        WalkTakenFrom = lastAlive ?? now;
        Log(string.Create(CultureInfo.InvariantCulture, $"the {leg} leg to {label}: the server moved the player from {CellWord(lastAlive)} to cell 0x{now.CellId:X8}, the landblock 0x{into:X4} the leg was running to: the portal took the body on touch (taken=touch)"));
        return true;
    }

    private void StartAttackWatch()
    {
        if (_attackWatch is null)
        {
            _attackWatch = new AttackWatch(S, line => Log("fight-back: " + line));
            _attackWatch.Start();
            _ = ThreatBoard.For(S); // M6 16 Q2b: the threat board hears from this watch's start at the latest (it is the session's: it may have heard long before); the leg's attackers are read off it
        }
    }

    private void DisposeSafety()
    {
        EndAwarenessLeg(); // M6 16 E1f: a leg of the walk-awareness shadow left open by a failure
        _attackWatch?.Dispose();
        _attackWatch = null;
    }

    // ------------------------------------------------------------------------------------------------ M6 6 E: the run leg's retreat

    /// <summary>Why the walk must retreat (<c>cap</c>, <c>no-mana</c>, <c>health</c>, <c>timeout</c>, M6 6 R1F <c>no-components</c>, M6 14 retreat attackers <c>mana-line</c>, M6 14 approach cost <c>approach-abandoned</c>), set by the loop or the fight-back; null: no retreat due.</summary>
    private string? _retreatWhy;

    /// <summary>What was seen when <see cref="_retreatWhy"/> was set.</summary>
    private string _retreatDetail = string.Empty;

    /// <summary>The retreats this leg made (report-only; 0 or 1, the retreat ends the leg).</summary>
    protected int Retreats { get; private set; }

    private void RetreatDue(string why, string detail)
    {
        _retreatWhy = why;
        _retreatDetail = detail;
        Log($"retreat due on the walk: why={why}: {detail}");
    }

    /// <summary>
    /// The walk's retreat line in percent and its source in words. M6 14 retreat attackers (live 2026-11-14 10:59:16 UTC: the line was the destination's, the Holding's Rampager in bludgeon under the bot's protection, 10%, while two
    /// Infected Assailers took her from 163 to 15 in eleven seconds): the line from the creatures attacking her or in reach (<see cref="HostileReach.Present"/>: heard within <see cref="FightBackHeardSeconds"/>, within
    /// <see cref="FightBackReachMetres"/>), each by its own facts looked up by weenie class across every profile, its own damage type, her armour and her live resistance to it (<see cref="Vitals.LiveFightLine.ReadAttackers"/>, rule 1);
    /// a creature with no facts lifts it to the general fallback (<see cref="Vitals.FightLines.GeneralFallbackPercent"/>, rule 2, named); the hits this fight-back has measured are its floor (<see cref="AttackWatch.Measured"/>, rule 3).
    /// The destination's profile and hitter are not read: the walk's creatures are the walk's.
    /// </summary>
    private (double Percent, string Source) RetreatLine()
    {
        var c = CultureInfo.InvariantCulture;
        var now = S.Clock.Now;
        if (_attackWatch is not { } watch)
        {
            return (Vitals.FightLines.GeneralFallbackPercent, string.Create(c, $"{Vitals.FightLines.GeneralFallbackPercent}% (the general fallback: no attack watch is running)"));
        }

        HostileReach.Present(S, FightBackReachMetres, watch.HeardWithin(FightBackHeardSeconds), _present, WhereSheStands().At);
        var (perSecond, biggest) = watch.Measured(now);
        _fightLine.Times = Context.AttackTimes ?? Vitals.CreatureAttackTimes.For(Context.Dats); // M6 14 attack timing: each attacker's own timing from the dats
        var line = _fightLine.ReadAttackers(S.State, _present, now, perSecond, biggest, measure: name => watch.Measure(name, now)); // M6 14 the walk's line: a kind heard over the whole window is priced by its measured hits
        if (line.Percent != _fightLineLogged)
        {
            _fightLineLogged = line.Percent;
            Log("fight line on the walk: " + line.Why() + " (M6 14 retreat attackers)");
        }

        var who = _present.Count == 0 ? "none read in reach" : string.Join(", ", _present.Select(p => "'" + p.Name + "'"));
        var lifted = line.Fallback ? $"; the general fallback {Vitals.FightLines.GeneralFallbackPercent}% for {(line.Unknown > 0 ? line.Unknown.ToString(c) + " attacker(s) with no facts" : "no attacker read")}" : string.Empty;
        return (line.Percent, string.Create(c, $"{line.Percent}% (from the attackers present: {who}; their facts and her live state, measured {line.MeasuredPerSecond:0.#}/s and a largest hit of {biggest:0}{lifted})"));
    }

    /// <summary>M6 14 holding safety: the leg's reader of the derived line (<see cref="Vitals.LiveFightLine"/>) and the last line it logged.</summary>
    private readonly Vitals.LiveFightLine _fightLine = new();

    private readonly List<Vitals.PresentAttacker> _present = [];

    private int _fightLineLogged = -1;

    /// <summary>The line in words when the health is read and under the retreat line (a line of 0 never retreats), else null; <paramref name="health"/> the health in words.</summary>
    private string? HealthUnderLine(out string health)
    {
        health = "health " + VitalWord(MageTemplate.MaxHealth);
        var (line, source) = RetreatLine();
        return line > 0 && S.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var cur, out var max) && max > 0 && cur * 100.0 < line * max ? source : null;
    }

    // ------------------------------------------------------------------------------------------------ M6 14 retreat attackers: the retreat's own price (rule 4)

    /// <summary>
    /// M6 14 retreat attackers (rule 4; live 2026-11-14 10:59:16 UTC: the retreat came with the mana at 49, under Lifestone Recall's 50, so it was a walk back she did not live through): the mana a fight-back keeps for its way out, the fight's
    /// way-out rule (<see cref="Vitals.ManaWayOut"/>) with the leg's own prices: Lifestone Recall's cost plus one heal's (the most the highest Heal Self her book knows can take, <see cref="GearCasts.ManaCeiling"/>), so she never spends down to
    /// where she can neither heal nor leave. Null with the reason when the book holds no Lifestone Recall or its row is not in the spell table (the walk back is the way out then, and no reserve buys it).
    /// </summary>
    private Vitals.WayOutLine? ReserveLine(out string why) => ReserveLineFor(S, Context, out why);

    /// <summary>M6 16 Q8: <see cref="ReserveLine"/> for any skirmish's host (the quest steps keep the same reserve in their skirmish), on <paramref name="session"/> and <paramref name="context"/>.</summary>
    /// <param name="session">The session.</param>
    /// <param name="context">The journey's context (its spell table and its facts of the tie).</param>
    /// <param name="why">The line in words, or why none.</param>
    internal static Vitals.WayOutLine? ReserveLineFor(IGameSessionView session, JourneyContext context, out string why)
    {
        var c = CultureInfo.InvariantCulture;
        if (!session.State.Self.Spellbook.Knows(RecallHome.LifestoneRecallSpell) || !context.Spells.TryGetValue(RecallHome.LifestoneRecallSpell, out var recall))
        {
            why = "the book holds no Lifestone Recall (or its row is not in the spell table): no reserve buys the way out, which is the walk back";
            return null;
        }

        if (context.Facts.LifestoneTied == false || !RecallReadiness.Of(session.State, context.Facts, context.Spells).Castable)
        {
            why = "Lifestone Recall cannot take her home (the tie is known false, 0x049E, or the recall is known unavailable or the pack is short for it): no reserve buys the way out, which is the walk back";
            return null;
        }

        // M6 audit (finding 8, the owner's decision of 2026-11-19): the heal the reserve pays for is the one she would cast, the highest Heal Self known at the ladder's plan chance on her
        // current skill (VitalsMeans.Pick, the fight's own pick), else her lowest known level; not the dearest in the book. Live 2026-11-19 00:58:08 and 03:45:13 UTC, run 2 and run 4 at
        // level 10: the reserve priced Heal Self V (60 mana, cast chance 0.05) and the errand's skirmish refused bolts at 119 and 123 of 168 mana.
        var heal = 0u;
        if (Vitals.VitalsMeans.Pick(session.State.Self.Qualities, session.State.Self.Spellbook, context.Spells, Vitals.VitalsLine.HealSelf, Grounds.LadderLoader.WarMage.PlanCastChance, out _) is { } cast)
        {
            heal = GearCasts.ManaCeiling(cast, 0);
        }
        else
        {
            foreach (var id in Vitals.VitalsMeans.HealSelf)
            {
                if (session.State.Self.Spellbook.Knows(id) && context.Spells.TryGetValue(id, out var info))
                {
                    heal = GearCasts.ManaCeiling(info, 0); // the lowest known level: she casts the cheapest heal she has
                    break;
                }
            }
        }

        var line = Vitals.ManaWayOut.LineOf(recall.BaseMana, heal);
        why = string.Create(c, $"line={line.Line} (Lifestone Recall {line.RecallCost} + {(heal == 0 ? "no heal: the book holds no Heal Self" : "Heal Self " + heal.ToString(c))})");
        return line;
    }

    /// <summary>The mana held, or null when unread.</summary>
    private uint? ManaNow() => S.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _) ? mana : null;

    /// <summary>
    /// The retreat (M6 6 E; item 6's notes, "The contract"; map-death.md section 5): the walk cannot go on fighting, so the leg leaves. Check before: the book (Lifestone Recall 1635, <see cref="RecallHome.LifestoneRecallSpell"/>, known or not),
    /// the leg's start point, one line. Act: Lifestone Recall when the book holds it (<see cref="RecallBack"/>), else the walk back to the leg's start point (<see cref="WalkBack"/>). Check after: the arrival. The leg then fails by name
    /// <c>retreat:&lt;leg&gt;:&lt;recall|walked-back&gt;</c> (never <c>died:</c>), logged <c>retreat on &lt;leg&gt;: why= way= from= to=</c>; a retreat that itself fails is the named stop <c>retreat-failed:&lt;leg&gt;:&lt;recall|walk-back&gt;:&lt;what&gt;</c>.
    /// A death during it stays <c>died:&lt;leg&gt;:respawned=</c>.
    /// </summary>
    private IEnumerable<ActionHandle?> Retreat(string leg, string label, CellFrame? start)
    {
        Retreats++;
        var why = _retreatWhy!;
        var from = S.State.Self.Position;
        var cost = Context.Spells.TryGetValue(RecallHome.LifestoneRecallSpell, out var info) ? info.BaseMana : 0u;
        var payable = cost == 0 || !S.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _) || mana >= cost;
        // M6 6 FXD2 (6-D4): the recall is judged as the bolt pick judges a bolt (the pack's components, LevelFloor.ShortOf over HeldForAccount) and on the tie (0x049E known false); either failing is the walk back, with the reason on the line.
        // M6, the recall's components: the one read every way home takes (RecallReadiness): a pack without the Item Enchantment focus is judged on the account's formula when the run knows the account, and a recall ACE refused for a
        // component earlier this run is not cast again under attack.
        var read = RecallReadiness.Of(S.State, Context.Facts, Context.Spells);
        var known = read.Known;
        var shortOf = read.ShortOf;
        var judged = !read.Judged ? "components=not judged (the pack or its focus is unread)" : shortOf is null ? "components=held" : "components=short";
        var tied = Context.Facts.LifestoneTied != false;
        var recall = known && payable && shortOf is null && tied && !read.Unavailable;
        var book = known
            ? payable ? "lifestone_recall=yes" : string.Create(CultureInfo.InvariantCulture, $"lifestone_recall=yes but the mana {VitalWord(MageTemplate.MaxMana)} is under its {cost}")
            : "lifestone_recall=no";
        book += shortOf is not null ? $"; the pack is short for it: {shortOf}" : known ? "; " + judged : string.Empty;
        book += known && read.Unavailable ? "; ACE refused it for a component earlier this run (recall-unavailable:components)" : string.Empty;
        book += tied ? string.Empty : "; the lifestone tie is known false (0x049E)";
        Log(string.Create(
            CultureInfo.InvariantCulture,
            $"retreat on {leg}: check before: why={why} ({_retreatDetail}); health {VitalWord(MageTemplate.MaxHealth)}, mana {VitalWord(MageTemplate.MaxMana)}; {book} from={CellWord(from)} start={CellWord(start)} -> way={(recall ? "recall" : "walk-back")} (the walk to {label} is given up)"));
        var life = ReadLife(leg, $"the retreat on the {leg} leg");
        if (recall)
        {
            foreach (var h in RecallBack(leg, why, from, start, life))
            {
                yield return h;
            }
        }
        else
        {
            foreach (var h in WalkBack(leg, why, from, start, life, "walk-back", known ? "Lifestone Recall is not usable: " + book : "the book holds no Lifestone Recall"))
            {
                yield return h;
            }
        }
    }

    /// <summary>The retreat by Lifestone Recall: magic, the cast (the travel cast rules), the refusal or the teleport, the death check, present in the home lifestone's landblock, peace; then <c>retreat:&lt;leg&gt;:recall</c>.</summary>
    private IEnumerable<ActionHandle?> RecallBack(string leg, string why, CellFrame? from, CellFrame? start, LifeMark life)
    {
        var home = HomeLifestone.Cell >> 16;
        foreach (var h in MagicNow())
        {
            yield return h;
        }

        var cast = new CastResult();
        string? notSent = null;
        foreach (var h in Catching(Cast(RecallHome.LifestoneRecallSpell, 0, cast), f => notSent = f))
        {
            yield return h;
        }

        if (notSent is not null)
        {
            Fail($"retreat-failed:{leg}:recall:not-sent", notSent);
        }

        Log($"retreat on {leg}: cast Lifestone Recall: {Describe(cast.Handle!)} (check after: a teleport to landblock 0x{home:X4})");
        foreach (var h in Until(() => Refusal(this, cast) != 0 || TeleportSince(cast.TravelMark), TeleportStartSeconds))
        {
            yield return h;
        }

        var refusal = Refusal(this, cast);
        if (refusal != 0 && !TeleportSince(cast.TravelMark))
        {
            if (refusal == 0x049E)
            {
                Context.Facts.LifestoneTied = false;
            }

            if (refusal == 0x0400)
            {
                Context.Facts.LifestoneRecallUnavailable = true; // M6, the recall's components (M6 7 AV's fact): the check before could not judge the pack, so the rest of the run leaves the recall out
            }

            if (refusal is 0x0400 or 0x049E)
            {
                // M6 6 FXD2 (6-D4): a recall ACE refuses for a missing component or the failed tie is answered once by the walk back (check after the refusal: no teleport began).
                Log($"retreat on {leg}: Lifestone Recall was refused: {Named(refusal)}; the retreat falls back once to the walk back");
                foreach (var h in WalkBack(leg, why, from, start, life, "walk-back after recall refused", "Lifestone Recall was refused: " + Named(refusal)))
                {
                    yield return h;
                }

                yield break;
            }

            Fail($"retreat-failed:{leg}:recall:refused", "Lifestone Recall was refused: " + Named(refusal));
        }

        if (!TeleportSince(cast.TravelMark))
        {
            Fail($"retreat-failed:{leg}:recall:no-teleport", string.Create(CultureInfo.InvariantCulture, $"no teleport began within {TeleportStartSeconds:0} s of the cast ({Describe(cast.Handle!)})"));
        }

        foreach (var h in DiedAfterMove(leg, $"the retreat by Lifestone Recall on the {leg} leg", life, from))
        {
            yield return h;
        }

        foreach (var h in Until(() => S.State.Self.Travel == SelfTravel.Present, PresentSeconds))
        {
            yield return h;
        }

        if (S.State.Self.Travel != SelfTravel.Present)
        {
            Fail($"retreat-failed:{leg}:recall:not-present", string.Create(CultureInfo.InvariantCulture, $"the player was not present within {PresentSeconds:0} s of the recall's teleport (travel {S.State.Self.Travel})"));
        }

        var to = S.State.Self.Position;
        if (to is not { } there || there.CellId >> 16 != home)
        {
            Fail(string.Create(CultureInfo.InvariantCulture, $"retreat-failed:{leg}:recall:elsewhere:{(to is { } p ? (p.CellId >> 16).ToString("X4", CultureInfo.InvariantCulture) : "none")}"), $"Lifestone Recall landed in {CellWord(to)}, not in the home lifestone's landblock 0x{home:X4}");
        }

        Context.Facts.LifestoneTied = true;
        Context.Facts.Recalls++;
        foreach (var h in Pause(3))
        {
            yield return h;
        }

        foreach (var h in PeaceNow())
        {
            yield return h;
        }

        Log($"retreat on {leg}: why={why} way=recall from={CellWord(from)} to={CellWord(to)}");
        Fail($"retreat:{leg}:recall", $"the {leg} leg retreated ({why}: {_retreatDetail}) by Lifestone Recall from {CellWord(from)} to {CellWord(to)}");
    }

    /// <summary>
    /// The retreat on foot: peace, the guarded walk (<see cref="GuardedWalk"/>, the life checks, no fight-back cap, still answering an attacker that hits, M5 D88) back to the leg's start point; then <c>retreat:&lt;leg&gt;:walked-back</c>.
    /// The walk's own failure is <c>retreat-failed:&lt;leg&gt;:walk-back</c>; a death on it stays <c>died:</c>.
    /// </summary>
    private IEnumerable<ActionHandle?> WalkBack(string leg, string why, CellFrame? from, CellFrame? start, LifeMark life, string way = "walk-back", string reason = "the book holds no Lifestone Recall")
    {
        if (start is null)
        {
            Fail($"retreat-failed:{leg}:walk-back:no-start", $"the leg's start point was not read when the walk began, and Lifestone Recall is not usable ({reason})");
        }

        foreach (var h in PeaceNow())
        {
            yield return h;
        }

        var named = FailName;
        string? failure = null;
        foreach (var h in Catching(GuardedWalk(leg, "retreat", "the leg's start", start!.Value.CellId, start.Value.Frame.Origin, TravelPurpose.Cell, $"retreat ({why}): back to where the {leg} leg began, on foot ({reason})", life, retreating: true), f => failure = f))
        {
            yield return h;
        }

        if (failure is not null)
        {
            if (FailName != named)
            {
                throw new InvalidOperationException(failure); // a named failure on the way back (a death, died:): it stands
            }

            Fail($"retreat-failed:{leg}:walk-back", failure);
        }

        var to = S.State.Self.Position;
        Log($"retreat on {leg}: why={why} way={way} from={CellWord(from)} to={CellWord(to)}");
        Fail($"retreat:{leg}:walked-back", $"the {leg} leg retreated ({why}: {_retreatDetail}) on foot from {CellWord(from)} back to its start {CellWord(to)}");
    }

    /// <summary>
    /// The nearest attacker to fight (heard within <see cref="FightBackHeardSeconds"/>, alive, attackable, within <see cref="FightBackReachMetres"/> where the client draws it and her, <see cref="HostileReach"/>), the ones left skipped (one
    /// left for the cast range that attacked her again is no longer left, <see cref="ReleaseLeftThatAttackAgain"/>) and, on the run, the ones the run past need not fight (<see cref="SkipInFight"/>). M6 16 Q2b (docs/automation-engine.md
    /// E2, <c>TODO(engine-threat-flip)</c>): the session's threat board answers (<see cref="NearestOnHer"/>); the one change: an attack the board heard before this leg's watch began is known for its window.
    /// </summary>
    private bool Attacker(out uint guid, out string name, out double metres)
    {
        guid = 0;
        name = string.Empty;
        metres = double.NaN;
        if (_attackWatch is null)
        {
            return false;
        }

        ReleaseLeftThatAttackAgain();
        return NearestOnHer(_skipInFight ??= SkipInFight, out guid, out name, out metres);
    }

    /// <summary>
    /// M6 14 caught on the run (the owner's ruling, 2026-11-15: she is not in a lull while she is under attack; live ladder25 04:10:54 UTC: a lull was called while a Reedshark Ravager the fight-back had left bit her every second): the nearest creature
    /// on her now, whatever list it is on (left, or outside the run past's set): heard attacking her within <see cref="FightBackHeardSeconds"/>, alive, within <see cref="FightBackReachMetres"/> where the client draws it and her. A lull is
    /// none. False when none stands there. M6 16 Q2b: the session's threat board answers (<see cref="NearestOnHer"/>), as for the fight-back's pick; the leg's watch still gates the read.
    /// </summary>
    private bool OnHer(out uint guid, out string name, out double metres)
    {
        guid = 0;
        name = string.Empty;
        metres = double.NaN;
        return _attackWatch is not null && NearestOnHer(null, out guid, out name, out metres);
    }

    /// <summary>The fight-back's pick's skip (<see cref="SkipInFight"/>), made once: the walk's frame allocates nothing for it.</summary>
    private Func<uint, bool>? _skipInFight;

    /// <summary>
    /// M6 16 Q2b: the nearest creature on her off the session's threat board, asked the leg's own question (<see cref="ThreatQuery.OnHer"/>: <see cref="FightBackReachMetres"/>, <see cref="FightBackHeardSeconds"/>, every attack heard as
    /// what counts, an evade among them, from where the client draws her), <paramref name="skip"/> set aside. Of two at one distance the one met later in the object table, as the leg's old read (<see cref="HostileReach.TryNearest"/>)
    /// took it (<see cref="ThreatQuery.TiesToLast"/>). An attack the board heard before the leg's watch began is known for its window. Allocates nothing.
    /// </summary>
    private bool NearestOnHer(Func<uint, bool>? skip, out uint guid, out string name, out double metres)
    {
        var query = ThreatQuery.OnHer(FightBackReachMetres, FightBackHeardSeconds, WhereSheStands().At) with { Skip = skip, TiesToLast = true };
        var answer = ThreatBoard.For(S).Ask(S, in query, S.Clock.Now);
        (guid, name, metres) = answer.Kind == ThreatAnswerKind.Standing ? (answer.Guid, answer.Name, answer.Metres) : (0u, string.Empty, double.NaN);
        return guid != 0;
    }

    /// <summary>An attacker the fight-back leaves: one left (<see cref="_left"/>) or, M6 14 run past, one the run past need not fight while it fights the ones it must (<see cref="_fightOnly"/>).</summary>
    private bool SkipInFight(uint guid) => _left.ContainsKey(guid) || (_fightOnly is { } only && !only.Contains(guid));

    /// <summary>The fight-back leaves <paramref name="guid"/> (see <see cref="_left"/>), the attacks heard from its name kept so a new one is seen.</summary>
    private void Leave(uint guid, string name, bool forRange, string why, double reach = double.PositiveInfinity) =>
        _left[guid] = new LeftAttacker(name, why, forRange, _attackWatch?.AttacksOf(name).Attacks ?? 0, S.Clock.Now, reach);

    /// <summary>
    /// M6 14 caught on the run (the owner, 2026-11-15; live ladder25 04:10:34 UTC: a Reedshark Ravager was left as 19 m away by its stale report and bit her for the next 24 s, ignored by the run past, the lull and the fight-back alike): a
    /// creature left for the cast range whose name is heard attacking her again after it was left is no longer left: it is on her, so it is fought. One line each. A creature given up (the timeout, the walk back's no-mana or no-components)
    /// stays left.
    /// </summary>
    private void ReleaseLeftThatAttackAgain()
    {
        if (_attackWatch is not { } watch || _left.Count == 0)
        {
            return;
        }

        List<uint>? released = null;
        foreach (var (guid, left) in _left)
        {
            // M6 14 the hovering Zefir (live ladder51 02:56:00 UTC: one left at 38.9 m was taken back at each Harm Other it cast from there, for 180 s with no cast): an attack from beyond her reach does not take it back; one from within it
            // does (the distance unread: it does, as before)
            if (left.ForRange && watch.AttacksOf(left.Name).Attacks > left.Attacks && Skirmish.PlanarMetres(guid, double.NaN) is var d && (double.IsNaN(d) || d <= left.Reach))
            {
                (released ??= []).Add(guid);
            }
        }

        if (released is null)
        {
            return;
        }

        foreach (var guid in released)
        {
            var left = _left[guid];
            _left.Remove(guid);
            var ago = watch.LastHeard(left.Name, out var at) ? string.Create(CultureInfo.InvariantCulture, $"{S.Clock.Now - at:0.0} s ago") : "at an unknown time";
            var where = Skirmish.PlanarMetres(guid, double.NaN) is var d && !double.IsNaN(d) ? string.Create(CultureInfo.InvariantCulture, $", now {d:0.0} m off{(double.IsInfinity(left.Reach) ? string.Empty : $" within her reach {left.Reach:0.#} m")}") : string.Empty;
            Log(string.Create(CultureInfo.InvariantCulture, $"fight-back: '{left.Name}' 0x{guid:X8} was left {S.Clock.Now - left.At:0.0} s ago ({left.Why}) but attacked her again {ago} ({watch.AttacksOf(left.Name).Attacks - left.Attacks} attack(s) heard from its name since{where}): it is no longer left; an attacker on her is fought (M6 14 caught on the run)"));
        }
    }

    /// <summary>M6 14 caught on the run: the last attack heard on her from any creature, in words, so a line that claims a lull or no attacker can be checked.</summary>
    protected string LastAttackWord() =>
        _attackWatch is { } watch && watch.LastAttack(out var who, out var at)
            ? string.Create(CultureInfo.InvariantCulture, $"the last attack heard on her: '{who}' {S.Clock.Now - at:0.0} s ago")
            : "no attack heard on her on this leg";

    /// <summary>M6 14 caught on the run: how long ago <paramref name="name"/> was last heard attacking her, in words (a lull's or an attack's line names it).</summary>
    /// <param name="name">The creature's name.</param>
    protected string HeardAgo(string name) =>
        _attackWatch is { } watch && watch.LastHeard(name, out var at) ? string.Create(CultureInfo.InvariantCulture, $"{S.Clock.Now - at:0.0} s ago") : "at an unknown time";

    /// <summary>
    /// M6 14 caught on the run: why a fight-back ends, truthfully: the creatures still on her (<see cref="HostileReach.Present"/>, none skipped) each with why it is not fought now (left, or the run past's reason), or none; and the last attack
    /// heard (<see cref="LastAttackWord"/>).
    /// </summary>
    private string FightBackEndWhy(bool onTheRun)
    {
        var c = CultureInfo.InvariantCulture;
        if (_attackWatch is not { } watch)
        {
            return "no attack watch is running";
        }

        HostileReach.Present(S, FightBackReachMetres, watch.HeardWithin(FightBackHeardSeconds), _endPresent, WhereSheStands().At);
        if (_endPresent.Count == 0)
        {
            return string.Create(c, $"no creature heard attacking her within {FightBackHeardSeconds:0} s stands alive within {FightBackReachMetres:0} m ({LastAttackWord()})");
        }

        var each = string.Join(", ", _endPresent.Select(p => string.Create(c, $"'{p.Name}' 0x{p.Guid:X8} at {p.Metres:0.0} m ({(_left.TryGetValue(p.Guid, out var left) ? "left: " + left.Why + (left.ForRange ? string.Create(c, $", not heard attacking from within {left.Reach:0.#} m since it was left") : string.Empty) : onTheRun && _runVerdicts.TryGetValue(p.Guid, out var reason) ? "the run past: " + reason : "not judged")})")));
        return string.Create(c, $"none of the {_endPresent.Count} creature(s) still on her is to be fought now: {each}; {LastAttackWord()}{(onTheRun ? ": the walk goes on and she runs from them (M6 14 run past, D137)" : string.Empty)}");
    }

    private readonly List<Vitals.PresentAttacker> _endPresent = [];

    private string? AttackerStopsWalk()
    {
        if (Context.Facts.RunOnly is { } runOn)
        {
            RunOnFrame(runOn); // M6 item 14 (the den run): the run-only road: no walk stops for an attacker (TravelLegRunOnly.cs)
            return null;
        }

        // M6 14 run past (D137): an attacker stops the walk only when she cannot outpace it (TravelLegRunPast.cs); M6 the dance before running on (rule b): with no fight to stop for, an owed dance stops it at the lull's start
        // (TravelLegDance.cs)
        return WalkStandsDown() ? null : RunPastStops() ?? DanceStopsWalk();
    }

    /// <summary>M6 14 run past (D137): the seek to a portal is the walk's door: the attackers still on her are fought there before the Use (<see cref="DoorStops"/>); the run-only road stops for none.</summary>
    private string? AttackerStopsSeek()
    {
        if (Context.Facts.RunOnly is { } runOn)
        {
            RunOnFrame(runOn);
            return null;
        }

        return WalkStandsDown() ? null : DoorStops();
    }

    /// <summary>M6 8 G: true when no attacker stops a walk, by design (see the file's summary): the body is not present (a portal hop in flight, a teleport under way), or its health reads 0 (a death not yet handled).</summary>
    private bool WalkStandsDown() =>
        S.State.Self.Travel != SelfTravel.Present || (S.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out _) && health == 0);

    /// <summary>
    /// The fight-back (M5 D88): the skirmish (<see cref="Skirmish.Run"/>, M6 16 Q7: moved out of the leg unchanged; its rules are in its summary) with this leg as its host (<c>TravelLegSkirmishHost.cs</c>: the attackers it leaves,
    /// the run past judged again, the retreat due, the leg's log). The leg publishes the way out's reserve (<see cref="ReserveLine"/>) for the vitals guard while it lasts and clears it, the target and the run past's set however it ends;
    /// <paramref name="retreating"/> (the walk back) keeps no reserve and never sets the retreat due.
    /// </summary>
    private IEnumerable<ActionHandle?> FightBack(string leg, bool retreating)
    {
        // M6 14 retreat attackers (rule 4): the way out's reserve is the fight-back's, and the autopilot's vitals guard reads it so no heal it casts meanwhile spends the recall's mana; cleared however the fight-back ends
        var reserveWhy = "none: the walk back is under way (it never retreats again)";
        var reserve = retreating ? null : ReserveLine(out reserveWhy);
        Context.Facts.FightBackReserve = reserve;
        try
        {
            foreach (var h in Skirmish.Run(leg, retreating, reserve, reserveWhy))
            {
                yield return h;
            }
        }
        finally
        {
            Context.Facts.FightBackReserve = null;
            Context.Facts.FightBackTarget = null; // M6 14 walk drains: the target's health is published only while its fight-back lasts
            _fightOnly = null; // M6 14 run past: the next stop names its own
        }
    }

    private string VitalWord(uint id) => S.State.Self.Qualities.TryGetVitalLevels(id, out var cur, out var max) ? string.Create(CultureInfo.InvariantCulture, $"{cur}/{max}") : "unread";
}
