using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 airborne (FightScenario.Airborne.cs; live 2026-11-13 14:12 UTC, the Tusker Abode's pit): a jump in flight is never cut, and nothing is cast from the air. Over the scripted ACE: an attacker that strikes during a jump leg does
/// not drop the walk, and self-defence takes it the frame after the landing check; a cast waits while the body is in the air and goes out once it stands; ACE's 0x04EB answers are waits, never refusals, so the war pick drops nothing
/// and counts no miss; and the live sequence itself, scripted.
/// </summary>
public sealed partial class FightScenarioTests
{
    private const uint RedeemerGuid = 0x80003B6A;

    /// <summary>The index of the first log line holding <paramref name="text"/>, or -1.</summary>
    private static int FirstLineWith(Rig r, string text)
    {
        var lines = r.Log.Lines;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Contains(text, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Frames until the walk has begun its first leg.</summary>
    private static void ToTheFirstLeg(Rig r, FightScenario scenario, FakeWalk walk)
    {
        for (var i = 0; i < 40 && walk.Legs == 0; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal(1, walk.Legs);
    }

    [Fact]
    public void An_attacker_during_a_jump_leg_does_not_cancel_it_and_self_defence_starts_after_the_landing_check()
    {
        var r = new Rig();
        r.Add(TuskerGuid, TuskerSlave, FloorCell, 40); // behind the wall: no line, so the fight walks
        var virindi = r.Add(VirindiGuid, Virindi, OtherFloorCell, 30, "Virindi"); // avoided by the profile, 10 m off with a line
        var walk = new FakeWalk { BlockedCell = FloorCell, KeepBlockedCell = true, TicksPerLeg = 1000, FlightTicks = 8, Events = [] };
        var keys = r.BindKeys();
        var scenario = ToTheFight(r, r.Scenario(walk));
        ToTheFirstLeg(r, scenario, walk);
        Assert.Empty(r.CastTargets);

        walk.TakeOff();
        keys.InTheAir = true;
        walk.Landed = () => keys.InTheAir = false;
        r.Frame(scenario);
        MeleeHit(r); // the Virindi strikes her in the air
        for (var i = 0; i < 5; i++)
        {
            r.Frame(scenario);
            Assert.True(walk.InFlight);
        }

        Assert.Equal(1, LinesWith(r, "self-defence: Virindi 0x80002001 attacked (damage notice)")); // heard in the air
        Assert.Equal(0, walk.Abandons); // ... and the jump is not cut
        Assert.Empty(r.CastTargets);
        Assert.Equal(0, LinesWith(r, "the walk is dropped"));

        for (var i = 0; i < 12 && r.CastTargets.Count == 0; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal(["landing check", "abandon"], walk.Events!.Where(e => e != "tick in flight"));
        Assert.DoesNotContain("abandon in flight", walk.Events);
        Assert.Equal(VirindiGuid, r.CastTargets[0]);
        var landed = FirstLineWith(r, "airborne: the jump's landing check has run");
        var dropped = FirstLineWith(r, "self-defence: the walk is dropped; 0x80002001 is in range with a clear line");
        Assert.True(landed >= 0 && dropped > landed, $"landing line {landed}, drop line {dropped}");
        Assert.Contains("the attacker Virindi 0x80002001", r.Log.Lines[landed], StringComparison.Ordinal);
        Assert.Equal(1, LinesWith(r, "airborne: the walk's jump is in flight"));
        Assert.Equal(1, scenario.AirHolds);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));
        Assert.True(virindi.Dead);
        Assert.Equal(1, scenario.Defended);
    }

    [Fact]
    public void A_cast_is_not_sent_while_the_body_is_in_the_air_and_goes_out_once_it_stands()
    {
        var r = new Rig();
        var tusker = r.Add(TuskerGuid, TuskerSlave, FloorCell, 40);
        var keys = r.BindKeys();
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));

        keys.InTheAir = true; // off a ledge, with no jump leg: the fight decides and the cast's own check before holds the send
        for (var i = 0; i < 12; i++)
        {
            r.Frame(scenario);
        }

        Assert.Empty(r.CastTargets);
        Assert.Empty(r.Answered);
        Assert.Equal(1, LinesWith(r, "fight bolt: the body is in the air: the cast of"));

        keys.InTheAir = false;
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));
        Assert.True(tusker.Dead);
        Assert.Equal(TuskerGuid, r.CastTargets[0]);
        Assert.Equal(1, LinesWith(r, "held for the air"));
        Assert.Equal(0, scenario.Tally.Misses);
    }

    [Fact]
    public void ACE_s_0x04EB_answers_are_waits_so_the_war_pick_drops_nothing_and_counts_no_miss()
    {
        var r = new Rig();
        var tusker = r.Add(TuskerGuid, TuskerSlave, FloorCell, 40);
        r.Script(Shot.InTheAir, Shot.InTheAir, Shot.InTheAir, Shot.InTheAir); // past M6 7 K's three in a row
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(tusker.Dead);
        Assert.Equal(4, LinesWith(r, "ACE answered 0x04EB YouCantDoThatWhileInTheAir"));
        Assert.Single(r.CastSpells.Distinct()); // every try the bolt's own level
        Assert.True(r.Answered.Count >= 6, $"{r.Answered.Count} answers"); // four in the air, then the two hits
        Assert.Equal(0, LinesWith(r, "is dropped for the fight"));
        Assert.Equal(0, LinesWith(r, "war pick: the bolt goes from"));
        Assert.Equal(0, LinesWith(r, "miss:"));
        Assert.Equal(0, scenario.Tally.Misses);
        Assert.Equal(ActionReason.InTheAir, ActionReasons.FromCode(CastAttempt.InTheAirCode)); // a typed code: the refused-in-a-row rule never counts it (RefusedForGood)
    }

    /// <summary>
    /// The live sequence of 2026-11-13 (<c>cache/m6/14/live/abode5/step3-levelup.log</c> from 14:12:29): no tusker had a line, so leg 24 walked toward one past the pit; the take-off at 14:12:31.117; a Tusker Redeemer swung at
    /// her 1.4 s into the flight, now in line; the jump landed about 2.2 s after the take-off; and the first bolt reached ACE before the landing's position did (0x04EB). Live, self-defence dropped the walk in the air, three
    /// bolts went into the air, and the war pick dropped Flame Bolt VI, then V, IV and III. Here the jump is not cut, nothing is cast in the air, the landing check runs before self-defence takes the attacker, the 0x04EB is a wait,
    /// and the bolt keeps its level.
    /// </summary>
    [Fact]
    public void The_live_jump_over_the_Abode_s_pit_with_a_swing_in_the_air_lands_then_fights_at_the_bolt_s_level()
    {
        var r = new Rig();
        var redeemer = r.Add(RedeemerGuid, TuskerSlave, FloorCell, 32, "Tusker Redeemer");
        var walk = new FakeWalk { BlockedCell = FloorCell, TicksPerLeg = 1000, FlightTicks = 9, Events = [] }; // 9 frames of 0.25 s: the 2.2 s flight
        var keys = r.BindKeys();
        var scenario = ToTheFight(r, r.Scenario(walk));
        ToTheFirstLeg(r, scenario, walk); // "no creature of the ground has a clear line": the leg over the pit
        Assert.Empty(r.CastTargets);

        walk.TakeOff(); // "jump: take-off"
        keys.InTheAir = true;
        walk.Landed = () => keys.InTheAir = false;
        r.Frame(scenario);
        walk.BlockedCell = 0; // over the pit the Redeemer is in line and in range: sight would drop the walk too
        r.Frame(scenario);
        r.Script(Shot.InTheAir); // the first bolt after the landing reaches ACE before the landing's position does
        r.Frame(scenario);
        r.Frame(scenario);
        MeleeHit(r, "Tusker Redeemer"); // 1.4 s into the flight
        for (var i = 0; i < 3; i++)
        {
            r.Frame(scenario);
            Assert.True(walk.InFlight);
        }

        Assert.Equal(0, walk.Abandons);
        Assert.Empty(r.CastTargets); // nothing cast in the air
        Assert.Equal(0, LinesWith(r, "the walk is dropped"));
        Assert.Equal(1, LinesWith(r, "self-defence: Tusker Redeemer 0x80003B6A attacked (damage notice)"));

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(redeemer.Dead);
        Assert.Equal(["landing check", "abandon"], walk.Events!.Where(e => e != "tick in flight").Take(2)); // the landing check ran, then the walk was dropped (the fight's end drops it again)
        Assert.DoesNotContain("abandon in flight", walk.Events);
        var landed = FirstLineWith(r, "airborne: the jump's landing check has run");
        var dropped = FirstLineWith(r, "the walk is dropped; 0x80003B6A is in range with a clear line");
        Assert.True(landed >= 0 && dropped > landed, $"landing line {landed}, drop line {dropped}");
        Assert.All(r.CastTargets, t => Assert.Equal(RedeemerGuid, t));
        Assert.Equal(1, LinesWith(r, "ACE answered 0x04EB YouCantDoThatWhileInTheAir"));
        Assert.Single(r.CastSpells.Distinct()); // Flame Bolt at its level throughout: no drop to the level below
        Assert.Equal(0, LinesWith(r, "is dropped for the fight"));
        Assert.Equal(0, LinesWith(r, "miss:"));
        Assert.Equal(0, LinesWith(r, "the body is in the air: the cast of")); // the fight never asked for a cast while she flew
        Assert.Equal(1, scenario.AirHolds);
        Assert.Equal(1, scenario.Defended);
    }
}
