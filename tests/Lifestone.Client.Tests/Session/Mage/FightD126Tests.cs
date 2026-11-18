using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Tests.Shared.Net;
using Xunit;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// D126 (a), soak round 12's lapses: 15 lines cast together came due together and ran out together at 09:43 UTC (a two-minute walk reached no decision point) and again in the 10:28 trip. The fight renews a batch whole: the lull's lookahead
/// takes every line due within <see cref="FightScenario.BatchGapSeconds"/> of the last line it takes, the hard floor starts a batch early enough for its last line, a walk is dropped for a line under the hard floor, and a stop for a trip
/// waits until every line due within the trip's length is recast. Over the scripted caster; nothing here needs a server.
/// </summary>
public sealed class FightD126Tests(ITestOutputHelper output)
{
    private void Dump(FightManaTests.Rig r)
    {
        foreach (var line in r.Log.Lines.Where(l => l.Contains("buff refresh", StringComparison.Ordinal)).Take(80))
        {
            output.WriteLine(line);
        }
    }

    private static void Hit(FightManaTests.Rig r) =>
        r.R.Server((p, s) => AceM3Writers.DefenderNotification(p, s, "Tusker Slave", 4, 0.1, 5, 4, false, 0));

    private static string? LineOf(uint spell) => ArenaProfile.Lines.FirstOrDefault(l => l.IsBuff && l.Spells.Any(s => s.Id == spell))?.Name;

    private static List<string> BuffCasts(FightManaTests.Rig r) => [.. r.Caster.Answered.Where(a => !a.Fizzled).Select(a => LineOf(a.Spell)).OfType<string>()];

    private static FightManaTests.Rig Rig(Func<ProfileLine, double> left, BuffOptions buff, float tuskerAt, FightOptions? fight = null)
    {
        var r = new FightManaTests.Rig(mana: 400, stamina: 200, fight: fight, knowsDrain: true) { HitsToKill = 400 };
        r.Caster.ManaRegenPerSecond = 40;
        r.Caster.HealthPerCast = 20;
        r.Buff = buff;
        r.AddTusker(tuskerAt);
        FightF24Tests.Prime(r, left);
        return r;
    }

    private static FightScenario Armed(FightManaTests.Rig r, FightManaTests.Walk? walk = null)
    {
        var scenario = r.Scenario(walk);
        scenario.RequestBuffRefresh(300, 20, "a line under the 300 s margin");
        return scenario;
    }

    /// <summary>The lines still active <paramref name="ahead"/> seconds from now, read as the fight reads them.</summary>
    private static List<string> LapsedWithin(FightManaTests.Rig r, double ahead)
    {
        var cycle = new BuffCycle(r.View, r.Caster.Table, r.Buff, r.Log, ArmourBuffLedger.For(r.R.H.State));
        var states = new List<BuffCycle.LineState>();
        cycle.ReadLines(r.Now + ahead, states);
        return [.. states.Where(s => !s.Active).Select(s => s.Line.Name)];
    }

    [Fact]
    public void At_the_lull_a_batch_is_renewed_whole_past_the_horizon_and_a_line_apart_from_it_is_not()
    {
        // The horizon is 60 s (M6 7 AZ: the margin alone): Quickness (40 s) is inside it; Strength (90 s), Endurance (140 s) and Coordination (190 s) follow it 50 s apart, the same batch; Willpower (400 s) is 210 s past the batch.
        var left = new Dictionary<string, double>(StringComparer.Ordinal) { ["Quickness Self"] = 40, ["Strength Self"] = 90, ["Endurance Self"] = 140, ["Coordination Self"] = 190, ["Willpower Self"] = 400 };
        var r = Rig(l => left.GetValueOrDefault(l.Name, 1500), new BuffOptions { FightSeedSeconds = 120 }, 60);
        var scenario = Armed(r); // no hit: the lull is now
        r.Run(scenario, 60, 0.1, () => left.Keys.Take(4).All(BuffCasts(r).Contains));
        r.Run(scenario, 10, 0.1);
        Dump(r);
        Assert.Equal(["Coordination Self", "Endurance Self", "Quickness Self", "Strength Self"], BuffCasts(r).Distinct().Order(StringComparer.Ordinal).ToList());
        Assert.Contains(r.Log.Lines, l => l.Contains("buff refresh: lookahead: ", StringComparison.Ordinal) && l.Contains("(batch: the lines under 190 s go together)", StringComparison.Ordinal));
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("lookahead: Willpower Self", StringComparison.Ordinal));
    }

    [Fact]
    public void Under_fire_a_batch_starts_early_enough_for_its_last_line_and_none_lapses()
    {
        // Ten lines with 31 to 40 s left, a tusker in reach and hitting: one at a time at the 30 s floor, the last would be reached after it ran out; as a batch the k-th line is due under 30 + 2.0 k s, so the batch starts at once.
        string[] batch = ["Strength Self", "Endurance Self", "Coordination Self", "Quickness Self", "Focus Self", "Willpower Self", "Creature Enchantment Mastery Self", "War Magic Mastery Self", "Item Enchantment Mastery Self", "Life Magic Mastery Self"];
        var r = Rig(l => Array.IndexOf(batch, l.Name) is var i and >= 0 ? 31 + i : 1500, new BuffOptions { FightSeedSeconds = 120 }, 12);
        var scenario = Armed(r);
        Hit(r);
        r.Run(scenario, 1, 0.1);
        Assert.Contains(r.Log.Lines, l => l.Contains("buff refresh: hard: ", StringComparison.Ordinal) && l.Contains("under the 30 s hard floor (batch: the lines under", StringComparison.Ordinal));
        for (var s = 0; s < 60 && !batch.All(BuffCasts(r).Contains); s++)
        {
            Hit(r);
            r.Run(scenario, 1, 0.1);
        }

        Dump(r);
        Assert.True(batch.All(BuffCasts(r).Contains), "every line of the batch was recast");
        Assert.Equal(0, scenario.BuffsExpired);
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("EXPIRED", StringComparison.Ordinal));
    }

    [Fact]
    public void A_walk_is_dropped_for_a_line_that_falls_under_the_hard_floor_and_the_line_is_recast()
    {
        // The tusker is 60 m away (out of the bolt's 30 m): the fight walks, and the walk never arrives (round 12's 491 m replan). Quickness has 45 s left: outside the lull's 1 s horizon, so it falls under the 30 s floor mid-walk.
        var walk = new FightManaTests.Walk { TicksPerLeg = 1_000_000 };
        var r = Rig(l => l.Name == "Quickness Self" ? 45 : 1500, new BuffOptions { FightSeedSeconds = 1, LookaheadMarginSeconds = 0 }, 60);
        var scenario = Armed(r, walk);
        r.Run(scenario, 10, 0.1);
        Assert.True(walk.Legs >= 1, "the fight walks toward the tusker");
        Assert.Empty(BuffCasts(r));
        r.Run(scenario, 40, 0.1, () => BuffCasts(r).Contains("Quickness Self"));
        Dump(r);
        Assert.Contains("Quickness Self", BuffCasts(r));
        Assert.True(scenario.WalksDroppedForBuffs >= 1);
        Assert.Contains(r.Log.Lines, l => l.Contains("buff refresh: the walk is dropped: Quickness Self has ", StringComparison.Ordinal) && l.Contains("under the 30 s hard floor", StringComparison.Ordinal));
        Assert.Equal(0, scenario.BuffsExpired);
    }

    [Fact]
    public void A_stop_for_a_trip_first_recasts_every_line_due_within_the_trip_and_no_line_lapses_across_it()
    {
        // Round 12's 10:27 trip: lines with 23 to 48 s left when the fight stopped for it, gone 2 minutes later. Here Quickness has 60 s, Strength 100 s, Endurance 150 s (the same batch), the rest 1,500 s; the trip takes 180 s.
        var left = new Dictionary<string, double>(StringComparer.Ordinal) { ["Quickness Self"] = 60, ["Strength Self"] = 100, ["Endurance Self"] = 150 };
        var r = Rig(l => left.GetValueOrDefault(l.Name, 1500), new BuffOptions { FightSeedSeconds = 120 }, 12, new FightOptions { Kills = FightOptions.UnlimitedKills, Seconds = 3600 });
        var scenario = r.Scenario(); // not armed: the trip's ask arms it
        r.Run(scenario, 30, 0.1, () => scenario.Stage == "fight"); // a stop before the first pick ends the fight at once (F30): the trip comes in the fight
        Assert.Equal(["Endurance Self", "Quickness Self", "Strength Self"], LapsedWithin(r, 180).Order(StringComparer.Ordinal).ToList()); // without the refresh they run out in the trip
        scenario.RequestLeaveRefresh(180, 20, "logistics-trip: the trip's mean 120 s + margin 60 s");
        ((IStoppableFight)scenario).RequestStop(FightStopReason.LogisticsTrip);
        Assert.True(scenario.LeaveRefreshAsked);
        Hit(r); // under fire: the refresh goes on between the casts
        var status = r.Run(scenario, 120, 0.1);
        Dump(r);
        Assert.NotEqual(ScenarioStatus.Running, status);
        Assert.True(left.Keys.All(BuffCasts(r).Contains), "every line due within the trip was recast before the fight ended");
        Assert.Equal(1, scenario.LeaveRefreshes);
        Assert.False(scenario.LeaveRefreshAsked);
        Assert.Contains(r.Log.Lines, l => l.Contains("buff refresh: leave: done in ", StringComparison.Ordinal));
        Assert.Empty(LapsedWithin(r, 180)); // the simulated trip: no line runs out across it
    }

    [Fact]
    public void A_stop_with_no_line_due_within_the_trip_ends_at_once()
    {
        var r = Rig(_ => 1500, new BuffOptions(), 12, new FightOptions { Kills = FightOptions.UnlimitedKills, Seconds = 3600 });
        var scenario = r.Scenario();
        r.Run(scenario, 30, 0.1, () => scenario.Stage == "fight");
        scenario.RequestLeaveRefresh(180, 20, "logistics-trip");
        ((IStoppableFight)scenario).RequestStop(FightStopReason.LogisticsTrip);
        var status = r.Run(scenario, 20, 0.1);
        Dump(r);
        Assert.NotEqual(ScenarioStatus.Running, status);
        Assert.Empty(BuffCasts(r));
        Assert.Equal(1, scenario.LeaveRefreshes);
    }

    [Fact]
    public void The_leave_refresh_bound_is_the_larger_of_the_floor_and_the_batchs_own_plan_M6_1_fix_1_R6()
    {
        Assert.Equal(FightScenario.LeaveRefreshBoundSeconds, FightScenario.LeaveBoundFor(3, 0)); // a small batch: the long run's floor
        Assert.Equal(FightScenario.LeaveRefreshBoundSeconds, FightScenario.LeaveBoundFor(0, 30));
        Assert.Equal((25 * FightScenario.BatchLineSeconds) + 60, FightScenario.LeaveBoundFor(25, 60)); // a full same-age batch of 25 lines: 50 s of passes plus the margin, never the flat 45
        Assert.True(FightScenario.LeaveBoundFor(25, BuffOptions.DefaultLookaheadMarginSeconds) > 25 * FightScenario.BatchLineSeconds);
    }

    [Fact]
    public void A_stop_for_a_trip_with_a_full_same_age_batch_is_not_cut_at_45_seconds_M6_1_fix_1_R6()
    {
        var count = Math.Min(25, ArenaProfile.Lines.Count(l => l.IsBuff));
        var r = Rig(_ => 100, new BuffOptions { FightSeedSeconds = 120 }, 12, new FightOptions { Kills = FightOptions.UnlimitedKills, Seconds = 3600 });
        r.Caster.CastSeconds = 2.0; // the long run's median pass is 1.94 s a line: 25 lines take more than 45 s
        var scenario = r.Scenario();
        r.Run(scenario, 30, 0.1, () => scenario.Stage == "fight");
        scenario.RequestLeaveRefresh(180, 20, "logistics-trip: the whole batch is the same age");
        ((IStoppableFight)scenario).RequestStop(FightStopReason.LogisticsTrip);
        Hit(r);
        var status = r.Run(scenario, 300, 0.1);
        Dump(r);

        Assert.NotEqual(ScenarioStatus.Running, status);
        Assert.Equal(1, scenario.LeaveRefreshes);
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("passed with", StringComparison.Ordinal) && l.Contains("TODO(m5-leave-refresh-bound)", StringComparison.Ordinal)); // not cut
        Assert.Contains(r.Log.Lines, l => l.Contains($"buff refresh: leave: {count} line(s) in the batch: the bound is ", StringComparison.Ordinal));
        Assert.Equal(count, BuffCasts(r).Distinct().Count()); // every line of the batch was recast
    }
}
