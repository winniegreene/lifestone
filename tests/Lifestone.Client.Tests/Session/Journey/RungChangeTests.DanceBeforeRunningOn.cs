using System.Globalization;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 the dance before running on (the owner, 2026-11-21: on a run she refills her stamina and mana before she runs on; docs/session.md section 123). Run 10's case
/// (<c>cache/m6/sprint/run10/step3-levelup.log</c>, 2026-11-21 UTC, the run from the Facility Hub's Barracks drop to the Tusker Armory's door): fight-back 1 ended at 07:29:07.550 with three Stinging Chitticks 25.7 to 28.0 m off passed by
/// the run past; at 07:29:09.515 the dance was due to her maximum, but one had stung her for 1 to 2 points 1.1 s before, so "no lull, no dance"; the vitals guard's race at 07:29:02.916 read winning (health 192 rising 3.1/s, the heal she can
/// sustain 12.6/s against the incoming 3.9/s); then no hit for 36.5 s, and no dance before fight-back 2 at mana 211 of 379. Over the rung rig's walk dance (<see cref="DanceRig"/>: the dance covers her, her stamina at 40 of 275, so a
/// top-off is due after every fight-back): (a) with the race held, the stings from beyond melee do not stop the dance before the walk goes on, nor the dance itself; an attacker at melee reach, or a race that is not held, lets the walk go on
/// without a dance; (b) the dance owed is read each frame of the walk, and a lull (no hit from anyone over the slowest attack of the kinds heard: here every kind's named fallback, 2.75 s, the rig has no dats) stops the walk for it; (c) a
/// creature that catches her in the dance is fought back as before.
/// </summary>
public sealed partial class RungChangeTests
{
    /// <summary>Run 10's chittick that stung her at 07:29:09.515 UTC.</summary>
    private const uint RunOnChittick = 0x800019B5;

    /// <summary>The Stinging Chittick's weenie class (<c>creatures.json</c>, the world database's creature table).</summary>
    private const uint StingingChittickWcid = 4244;

    /// <summary>The Reedshark Ravager's weenie class (the rung tests' own).</summary>
    private const uint ReedsharkRavagerWcid = 22746;

    /// <summary>
    /// Run 10's race as the vitals guard read it at 07:29:02.916 UTC (its line): winning: health 192 of 245 rising 3.1/s over the last 6 s; the heal she can sustain 12.6/s against the incoming 3.9/s (4/s with the dead attackers'); a largest
    /// hit of 7; the next heal 2.8 s and the way out 4.45 s; 4 attackers with 1581 health left at her 28.8 damage a second.
    /// </summary>
    private static (LosingRaceInput Input, LosingRaceAnswer Answer) Run10Race()
    {
        var input = new LosingRaceInput(192, 3.9, 7, 12.6, 2.8, RecallWayOut.Seconds) { TrendPerSecond = 3.1, IncomingAllPerSecond = 4.0, Attackers = 4, AttackersHealth = 1581, KillPerSecond = 28.8 };
        return (input, LosingRace.Decide(in input));
    }

    /// <summary>
    /// A race the heal does not hold (<see cref="LosingRace"/>'s own case, live ladder25 2026-11-15 04:13:09 to 04:13:27 UTC: drains of about 6 health a second against 10 to 20 coming in, from health 121 down): health 121 falling 9/s, the heal
    /// 6/s against the incoming 15/s, a largest hit of 20, the kill side not read.
    /// </summary>
    private static (LosingRaceInput Input, LosingRaceAnswer Answer) LadderRaceLost()
    {
        var input = new LosingRaceInput(121, 15, 20, 6, 2.8, RecallWayOut.Seconds) { TrendPerSecond = -9, IncomingAllPerSecond = 15, Attackers = 6, KillUnknown = "the replay reads no kill side" };
        return (input, LosingRace.Decide(in input));
    }

    /// <summary>
    /// The walk dance rig on a run to the entry's front (<see cref="DanceRig"/>), the vitals guard's race given by <paramref name="race"/>, the tuskers' motion 3 m/s (an Armored Tusker keeps pace: caught) and every other 0.5 m/s (outrun).
    /// The first walk runs 20 s at 4 m/s, an Armored Tusker at 2 m (caught: fight-back 1) and, with <paramref name="chittick"/>, run 10's Stinging Chittick 25 m off attacking her at its first step; the second walk runs
    /// <paramref name="secondRun"/> s, or with <paramref name="secondStands"/> keeps running where she stands until it is stopped; the walks after it arrive at once. A bolt kills its target; the Armored Tusker's leaves her mana at run 10's
    /// share after fight-back 1.
    /// </summary>
    private static (RungRig Rig, JourneyContext Context) RunOnRig(Func<(LosingRaceInput Input, LosingRaceAnswer Answer)> race, bool chittick, double secondRun = 20, bool secondStands = false)
    {
        var (t, ground) = DanceRig();
        var context = t.Context(ground, new CreaturePaces(table => table == 0x0900000C ? 3.0 : 0.5));
        context.Facts.Race = (_, _) => race();
        int EntryWalks() => t.Travel.Requests.Count(r => r.Destination.Name == "entry-front"); // this request's number (the request is listed before its leg is made)
        t.Travel.WalkingOn = r => r.Destination.Name == "entry-front" && secondStands && EntryWalks() == 2;
        t.Travel.RunsFor = r => r.Destination.Name != "entry-front" ? null : EntryWalks() switch { 1 => 20, 2 when !secondStands => secondRun, _ => null };
        var placed = false;
        t.Travel.OnRun = (r, ran, arrived) =>
        {
            if (placed || ran != 0)
            {
                return;
            }

            placed = true;
            t.Attacker(Runner, "Armored Tusker", 2f, wcid: 11540);
            if (chittick)
            {
                t.Attacker(RunOnChittick, "Stinging Chittick", 25f, wcid: StingingChittickWcid);
            }
        };
        t.CastAt = (_, target) =>
        {
            if (target == Runner)
            {
                t.Caster.SetVitals(220, t.Caster.Stamina); // run 10's share after fight-back 1 (196 of 379, 51.7%) of the rig's 426: the dance's press is due
            }

            t.Kill(target);
        };
        return (t, context);
    }

    /// <summary>The log as a list, the index of a line holding <paramref name="needle"/> (or -1), and a trace of the walk's and the dance's lines.</summary>
    private static (List<string> Lines, Func<string, int> At, string Trace) DanceLog(RungRig t)
    {
        var lines = t.R.H.Log.Lines.ToList();
        var trace = string.Join(" || ", lines.Where(l => l.Contains("walk dance", StringComparison.Ordinal) || l.Contains("walk_dance", StringComparison.Ordinal) || l.Contains("fight-back", StringComparison.Ordinal) || l.Contains("walk check before", StringComparison.Ordinal)).Take(60));
        return (lines, needle => lines.FindIndex(l => l.Contains(needle, StringComparison.Ordinal)), trace);
    }

    /// <summary>
    /// Rule a, run 10 replayed: fight-back 1 kills the Armored Tusker that caught her; the Stinging Chittick 25 m off, passed by the run past, stings her for 2 and 1 points about every 0.75 s from the fight-back's end (run 10: 13 hits for 26
    /// over the 10 s before 07:29:02.916) through the dance; the guard's race reads winning (its line at 07:29:02.916). The dance is due and she dances before the walk goes on (the line names the chittick beyond the 5 m of melee and the race
    /// with its numbers); no sting stops the dance, the chittick is not bolted in it, and the walk is planned again only after it.
    /// </summary>
    [Fact]
    public void When_a_fight_back_ends_under_chip_hits_the_race_holds_she_dances_before_the_walk_goes_on()
    {
        var (t, context) = RunOnRig(Run10Race, chittick: true, secondRun: 4);
        var (stings, nextSting) = (0, double.NaN);
        var chittickBoltsInDance = -1;
        var hex = "@" + RunOnChittick.ToString("X8", CultureInfo.InvariantCulture);
        var (run, status) = t.RunStation(new RungChangeStation(), context, maxSeconds: 1200, each: () =>
        {
            if (double.IsNaN(nextSting) && t.Lines("fight-back 1 on the run leg: done: ").Count > 0)
            {
                nextSting = t.Now;
            }

            if (!double.IsNaN(nextSting) && t.Now >= nextSting && t.Lines("walk_dance_check: after:").Count == 0)
            {
                t.Hit("Stinging Chittick", stings % 2 == 0 ? 2u : 1u); // acid (the rig's default type), as run 10's "blisters your ... for 2 points of acid damage"
                (stings, nextSting) = (stings + 1, t.Now + 0.75);
            }

            if (chittickBoltsInDance < 0 && t.Lines("walk dance: ended on the run leg").Count > 0)
            {
                chittickBoltsInDance = t.Events.Count(e => e.StartsWith("cast:", StringComparison.Ordinal) && e.EndsWith(hex, StringComparison.Ordinal));
            }
        });
        var (lines, at, trace) = DanceLog(t);
        Assert.True(status == ScenarioStatus.Succeeded, trace);
        Assert.Contains(" fail=none ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal("winning", LosingRace.Verdict(Run10Race().Answer));
        var done = at("fight-back 1 on the run leg: done: ");
        var dance = at("walk dance: under fire on the run leg to entry-front, but she dances before the walk goes on (the dance before running on): 'Stinging Chittick' 0x800019B5 attacked her ");
        Assert.True(done >= 0 && dance > done, trace);
        Assert.Contains("(the run past: passing)", lines[done], StringComparison.Ordinal);
        Assert.Contains("it stands beyond the 5 m of melee (the run past's reach, D137) and the vitals guard's race is held: winning: her health 192 rises 3.1/s over the last 6 s; the heal she can sustain, 12.6/s, holds the incoming 3.9/s", lines[dance], StringComparison.Ordinal);
        Assert.Contains("she stands and dances to 426 (", lines[dance], StringComparison.Ordinal); // her maximum
        Assert.Empty(t.Lines("no lull, no dance"));
        var (revitalize, press, ended, replanned) = (at("walk_dance: revitalize 1: Revitalize Self VI (landed)"), at("walk_dance: press 1:"), at("walk dance: ended on the run leg to entry-front"), at("fight-back 1 on the run leg ended; the walk to entry-front is planned again"));
        Assert.True(dance < revitalize && revitalize < press && press < ended && ended < replanned, $"{dance} {revitalize} {press} {ended} {replanned}: {trace}"); // the dance before the walk goes on
        Assert.True(stings >= 3, $"{stings} stings: {trace}");
        var held = Assert.Single(t.Lines("walk_dance: under fire, the dance goes on (the dance before running on: a dance is not a fight, D137): 'Stinging Chittick' 0x800019B5 attacked her ")); // said once, while the same attacker stings on
        Assert.Contains("and the vitals guard's race is held: winning:", held, StringComparison.Ordinal);
        Assert.Empty(t.Lines("walk_dance: attack 1 of at most 3")); // no sting stopped the dance
        Assert.Equal(0, chittickBoltsInDance); // the chittick was not fought in the dance (a dance is not a fight)
        Assert.NotEmpty(t.Lines("walk_dance: done: topped off: mana ")); // topped off (the stamina's last Revitalize costs a few points of the top)
        Assert.NotEmpty(t.Lines("walk_dance: press 1: Stamina to Mana level 6 (landed): mana 217 -> ")); // from run 10's share, the mana came back by the dance
        Assert.Empty(t.Lines("walk-dance:")); // no dance stopped by its bounds
        Assert.Empty(t.Lines("retreat due"));
    }

    /// <summary>
    /// Rule a: the same run with the race held (run 10's), but as fight-back 1 ends a Reedshark Ravager bites her from 3 m: an attacker within the run past's 5 m of melee keeps the old answer: no dance, the walk goes on (planned again before
    /// any dance), and the dance is owed.
    /// </summary>
    [Fact]
    public void An_attacker_at_melee_reach_when_the_fight_back_ends_lets_the_walk_go_on_without_a_dance()
    {
        var (t, _, lines, at, trace) = MeleeBiteAtTheEnd();
        var refused = at("walk dance: due to 426, but she is under attack: 'Reedshark Ravager' 0x80001962 attacked her ");
        Assert.True(refused > at("fight-back 1 on the run leg: done: "), trace);
        Assert.Contains("stands 3.0 m away where it is drawn: no lull, no dance; the walk goes on and the dance waits for the next lull, read each frame of the walk (it stands within the 5 m of melee (the run past's reach, D137): it is on her, so no dance)", lines[refused], StringComparison.Ordinal);
        var replanned = at("fight-back 1 on the run leg ended; the walk to entry-front is planned again");
        var firstDance = at("walk_dance_check: ");
        Assert.True(refused < replanned && (firstDance < 0 || replanned < firstDance), trace); // the walk went on first
        var underFire = at("walk dance: under fire on the run leg");
        Assert.True(underFire < 0 || underFire > replanned, trace); // no dance before the walk went on
        Assert.True(t.Travel.Requests.Count(r => r.Destination.Name == "entry-front") >= 2, trace);
    }

    /// <summary>
    /// Rule b: after the walk went on without a dance (the Ravager's bite at 3 m), she runs on and it is left behind (slower than her); no hit lands after its bite. Each frame of the walk the lull is read: 2.75 s after the bite (the slowest
    /// attack of the kinds heard, the named fallback for each: the rig has no dats) the lull begins, the dance is still due, the Ravager stands beyond melee and the race is held: the walk stops at once there (about 3 s after the last hit),
    /// she dances, and the walk is planned again from where she stands, then arrives.
    /// </summary>
    [Fact]
    public void A_lull_3_s_after_the_last_hit_stops_the_walk_for_the_dance_it_owed()
    {
        var (t, run, lines, at, trace) = MeleeBiteAtTheEnd();
        var stop = at("walk dance: a lull on the walk (no hit from anyone for ");
        Assert.True(stop > at("walk dance: due to 426, but she is under attack: 'Reedshark Ravager'"), trace);
        var quiet = System.Text.RegularExpressions.Regex.Match(lines[stop], @"no hit from anyone for (\d+\.\d) s \(the last: 'Reedshark Ravager'\)");
        Assert.True(quiet.Success, lines[stop]);
        var seconds = double.Parse(quiet.Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.True(seconds >= 2.7 && seconds <= 3.3, lines[stop]); // the lull's start, at the frame's 0.25 s
        Assert.Contains("the lull is 2.75 s, the slowest attack of the kinds heard: ", lines[stop], StringComparison.Ordinal);
        Assert.Contains("'Reedshark Ravager' every 2.75 s (TODO(f11-swing-seconds): not derived, the fallback)", lines[stop], StringComparison.Ordinal);
        Assert.Contains(") and the dance is due to 426 (it stands beyond the 5 m of melee (the run past's reach, D137) and the vitals guard's race is held: winning:", lines[stop], StringComparison.Ordinal);
        Assert.Contains("she stops at once and dances, then the walk is planned again from where she stands (the dance before running on)", lines[stop], StringComparison.Ordinal);
        var stopped = at("the walk is stopped: walk dance: a lull on the walk");
        var (revitalize, ended, again) = (at("walk_dance: revitalize 1: Revitalize Self VI (landed)"), at("walk dance: ended on the run leg to entry-front"), at("walk dance: the walk to entry-front is planned again from 0x"));
        Assert.True(stop <= stopped && stopped < revitalize && revitalize < ended && ended < again, $"{stop} {stopped} {revitalize} {ended} {again}: {trace}");
        Assert.Contains(t.Lines("walk check before: purpose: "), l => l.Contains("planned again after the walk dance", StringComparison.Ordinal));
        Assert.Equal(3, t.Travel.Requests.Count(r => r.Destination.Name == "entry-front")); // the first, the one the lull stopped, the one after the dance
        Assert.Contains(" fail=none ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Empty(t.Lines("fight-back 2 on the run leg")); // a dance is not a fight: the Ravager left behind is not fought
    }

    /// <summary>The melee case of rules a and b: run 10's race, no chittick; as fight-back 1 ends a Reedshark Ravager bites her once from 3 m; the second walk runs 20 s.</summary>
    private static (RungRig Rig, IStationRun Run, List<string> Lines, Func<string, int> At, string Trace) MeleeBiteAtTheEnd()
    {
        var (t, context) = RunOnRig(Run10Race, chittick: false);
        const uint ravager = Runner + 2;
        var bit = false;
        var (run, status) = t.RunStation(new RungChangeStation(), context, maxSeconds: 1200, each: () =>
        {
            if (!bit && t.Lines("fight-back 1 on the run leg: done: ").Count > 0)
            {
                bit = true;
                t.Attacker(ravager, "Reedshark Ravager", 3f, wcid: ReedsharkRavagerWcid); // its bite: the defender notice for 3
            }
        });
        var (lines, at, trace) = DanceLog(t);
        Assert.True(status == ScenarioStatus.Succeeded, trace);
        return (t, run, lines, at, trace);
    }

    /// <summary>
    /// Rule a: the race not held (a lost race: the heal 6/s against the incoming 15/s, her health falling): as fight-back 1 ends the chittick stings her from 25 m for 2 points three times; no dance, the walk goes on, and the dance is owed.
    /// The second walk keeps her where she stands (the chittick stays 25 m off). At the lull's start, 2.75 s after the last sting, the chittick is still on her by the lull test and the race is still not held: the dance stays owed. Just past
    /// the old lull, no attack from anyone for 10 s, no creature is on her: the walk stops and she dances.
    /// </summary>
    [Fact]
    public void A_race_that_is_not_held_lets_the_walk_go_on_without_a_dance_until_the_old_lull()
    {
        var (t, context) = RunOnRig(LadderRaceLost, chittick: true, secondStands: true);
        var (stings, nextSting) = (0, double.NaN);
        var lastSting = double.NaN;
        var (_, status) = t.RunStation(new RungChangeStation(), context, maxSeconds: 1200, each: () =>
        {
            if (double.IsNaN(nextSting) && t.Lines("fight-back 1 on the run leg: done: ").Count > 0)
            {
                nextSting = t.Now;
            }

            if (stings < 3 && !double.IsNaN(nextSting) && t.Now >= nextSting)
            {
                t.Hit("Stinging Chittick", 2);
                (stings, nextSting, lastSting) = (stings + 1, t.Now + 0.75, t.Now);
            }
        });
        var (lines, at, trace) = DanceLog(t);
        Assert.True(status == ScenarioStatus.Succeeded, trace);
        Assert.Equal("lost", LosingRace.Verdict(LadderRaceLost().Answer));
        var refused = at("walk dance: due to 426, but she is under attack: 'Stinging Chittick' 0x800019B5 attacked her ");
        Assert.True(refused > at("fight-back 1 on the run leg: done: "), trace);
        Assert.Contains("no lull, no dance; the walk goes on and the dance waits for the next lull, read each frame of the walk (it stands beyond the 5 m of melee (the run past's reach, D137), but the vitals guard's race is not held: lost: her health 121 falls 9/s", lines[refused], StringComparison.Ordinal);
        var replanned = at("fight-back 1 on the run leg ended; the walk to entry-front is planned again");
        var firstDance = at("walk_dance_check: ");
        Assert.True(refused < replanned && replanned < firstDance, trace); // the walk went on first
        var owed = at("walk dance: a lull on the walk (no hit from anyone for ");
        Assert.True(owed > replanned, trace);
        Assert.Contains(", the dance due to 426, but she is under attack: 'Stinging Chittick' 0x800019B5 attacked her ", lines[owed], StringComparison.Ordinal);
        Assert.Contains("but the vitals guard's race is not held: lost:", lines[owed], StringComparison.Ordinal);
        Assert.Contains(": the walk goes on and the dance stays owed (", lines[owed], StringComparison.Ordinal);
        var stop = lines.FindIndex(owed + 1, l => l.Contains("walk dance: a lull on the walk (no hit from anyone for ", StringComparison.Ordinal) && l.Contains("she stops at once and dances", StringComparison.Ordinal));
        Assert.True(stop > owed && stop < firstDance, trace);
        var quiet = System.Text.RegularExpressions.Regex.Match(lines[stop], @"no hit from anyone for (\d+\.\d) s");
        Assert.True(quiet.Success && double.Parse(quiet.Groups[1].Value, CultureInfo.InvariantCulture) >= 10, lines[stop]); // just past the old lull
        Assert.Contains("(no creature heard attacking her within 10 s stands alive within 40 m, whatever list it is on; ", lines[stop], StringComparison.Ordinal);
        Assert.True(stings == 3 && !double.IsNaN(lastSting), trace);
        Assert.NotEmpty(t.Lines("walk dance: ended on the run leg to entry-front"));
    }

    /// <summary>
    /// Rule c: in the dance before running on (run 10's race and chittick, its stings through the dance), a Tusker Slave that reaches her at 3 m and attacks has caught her: it is fought back as before (the dance's own fight-back), and the
    /// dance resumes and ends; the chittick's stings from 25 m never stop it.
    /// </summary>
    [Fact]
    public void In_the_dance_a_creature_that_catches_her_at_melee_reach_is_fought_back_and_the_dance_resumes()
    {
        var (t, context) = RunOnRig(Run10Race, chittick: true, secondRun: 4);
        var (stings, nextSting) = (0, double.NaN);
        var caught = false;
        var (_, status) = t.RunStation(new RungChangeStation(), context, maxSeconds: 1200, each: () =>
        {
            if (double.IsNaN(nextSting) && t.Lines("fight-back 1 on the run leg: done: ").Count > 0)
            {
                nextSting = t.Now;
            }

            if (!double.IsNaN(nextSting) && t.Now >= nextSting && t.Lines("walk_dance_check: after:").Count == 0)
            {
                t.Hit("Stinging Chittick", stings % 2 == 0 ? 2u : 1u);
                (stings, nextSting) = (stings + 1, t.Now + 0.75);
            }

            if (!caught && t.Lines("walk_dance revitalize: try 1").Count > 0)
            {
                caught = true;
                t.Attacker(0x80004000, "Tusker Slave", 3f); // it reaches her while Revitalize Self is cast, and attacks
            }
        });
        var (lines, at, trace) = DanceLog(t);
        Assert.True(status == ScenarioStatus.Succeeded, trace);
        var attack = at("walk_dance: attack 1 of at most 3: 'Tusker Slave' 0x80004000 attacked her ");
        Assert.True(attack > at("walk_dance: revitalize 1:"), trace);
        Assert.Contains("stands 3.0 m away (it stands within the 5 m of melee (the run past's reach, D137): it is on her, so no dance)", lines[attack], StringComparison.Ordinal);
        Assert.Contains(t.Events, e => e.StartsWith("cast:", StringComparison.Ordinal) && e.EndsWith("@80004000", StringComparison.Ordinal)); // fought back
        var (ended, press, after) = (at("walk_dance: fight-back 1 ended:"), at("walk_dance: press 1:"), at("walk_dance_check: after:"));
        Assert.True(attack < ended && ended < press && press < after, $"{attack} {ended} {press} {after}: {trace}"); // the dance resumed after the fight-back
        Assert.Empty(t.Lines("walk_dance: attack 2 of at most 3")); // the chittick's stings never stopped it
        Assert.Contains("the walk goes on", lines[after], StringComparison.Ordinal);
    }
}
