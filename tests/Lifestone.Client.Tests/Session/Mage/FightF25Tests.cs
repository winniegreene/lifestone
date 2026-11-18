using Lifestone.Client.Options;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Tests.Shared.Net;
using Xunit;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 7 F25 (D93, the owner): rebuff on purpose after a fight, looking ahead; keep the stance. At the lull (no hostile in reach after a kill) the fight recasts every line that would run out before the next lull is likely (the fight's mean
/// length plus a margin); under fire only a line under the 30 s hard floor or lapsed; the autopilot's own fight ends in the magic mode. Over the scripted caster; nothing here needs a server.
/// </summary>
public sealed class FightF25Tests(ITestOutputHelper output)
{
    private void Dump(FightManaTests.Rig r)
    {
        foreach (var line in r.Log.Lines.Where(l => l.Contains("buff refresh", StringComparison.Ordinal) || l.Contains("stance", StringComparison.Ordinal)).Take(60))
        {
            output.WriteLine(line);
        }
    }

    private static FightManaTests.Rig Rig(Func<ProfileLine, double> left, BuffOptions buff, float tuskerAt)
    {
        var r = new FightManaTests.Rig(mana: 400, stamina: 200, knowsDrain: true) { HitsToKill = 400 };
        r.Caster.ManaRegenPerSecond = 40;
        r.Caster.HealthPerCast = 20;
        r.Buff = buff;
        r.AddTusker(tuskerAt);
        FightF24Tests.Prime(r, left);
        return r;
    }

    private static void Hit(FightManaTests.Rig r) =>
        r.R.Server((p, s) => AceM3Writers.DefenderNotification(p, s, "Tusker Slave", 4, 0.1, 5, 4, false, 0));

    private static string? LineOf(uint spell) => ArenaProfile.Lines.FirstOrDefault(l => l.IsBuff && l.Spells.Any(s => s.Id == spell))?.Name;

    private static List<string> BuffCasts(FightManaTests.Rig r) => [.. r.Caster.Answered.Select(a => LineOf(a.Spell)).OfType<string>()];

    private static FightScenario Armed(FightManaTests.Rig r)
    {
        var scenario = r.Scenario();
        scenario.RequestBuffRefresh(300, 20, "a line under the 300 s margin"); // the autopilot's first ask arms the fight's own upkeep
        return scenario;
    }

    [Fact]
    public void At_the_lull_a_line_that_runs_out_before_the_next_lull_is_cast_and_one_with_400_s_left_is_not()
    {
        // a tusker 12 m away (in the 20 m reach) is killed; the next, 60 m away, is out of it: 10 s after the last hit the fight is in a lull
        var r = new FightManaTests.Rig(mana: 400, stamina: 200, fight: new FightOptions { Kills = 3 }) { HitsToKill = 2 };
        r.Caster.ManaRegenPerSecond = 40;
        r.Buff = new BuffOptions { FightSeedSeconds = 120 };
        r.AddTusker(12);
        r.AddTusker(60);
        FightF24Tests.Prime(r, l => l.Name switch { "Quickness Self" => 50, "Strength Self" => 400, _ => 1500 });
        var scenario = Armed(r);
        Assert.True(scenario.BuffsKeptAhead);
        Assert.Equal(120, scenario.FightMeanSeconds);
        r.Run(scenario, 30, 0.1, () => scenario.Stage == "fight"); // the fight's tally hears the hits from its start
        Hit(r); // the fight's first hit
        r.Run(scenario, 2, 0.1);
        Assert.Empty(BuffCasts(r)); // a hostile in reach: the lookahead waits for the lull

        r.Run(scenario, 60, 0.1, () => BuffCasts(r).Contains("Quickness Self"));
        r.Run(scenario, 20, 0.1);
        Dump(r);
        Assert.Equal(["Quickness Self"], BuffCasts(r).Distinct().ToList()); // 50 s < the 60 s horizon (M6 7 AZ: the margin alone); Strength Self's 400 s is not
        Assert.Equal(1, scenario.FightsMeasured);
        Assert.InRange(scenario.FightMeanSeconds, 84, 110); // 0.7 x 120 + 0.3 x the measured fight (its first hit to 10 s after its last)
        Assert.Contains(r.Log.Lines, l => l.Contains("buff refresh: lull: the fight lasted ", StringComparison.Ordinal) && l.Contains(" s from its first hit; the fight mean is now ", StringComparison.Ordinal));
        var lookahead = r.Log.Lines.Single(l => l.Contains("buff refresh: lookahead: Quickness Self ", StringComparison.Ordinal));
        Assert.Matches(@"buff refresh: lookahead: Quickness Self \d+ s left < next lull 60 s \(margin 60; the fight\'s mean \d+ s is read, not added", lookahead);
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("lookahead: Strength Self", StringComparison.Ordinal));
        Assert.Null(scenario.StopReason);
    }

    [Fact]
    public void The_horizon_is_the_margin_alone_a_line_at_50_s_is_inside_it_and_one_at_400_s_is_outside()
    {
        var r = Rig(l => l.Name switch { "Quickness Self" => 50, "Strength Self" => 400, _ => 1500 }, new BuffOptions { FightSeedSeconds = 120 }, 60);
        var scenario = Armed(r); // no hit: the lull is now, the horizon is the margin's 60 s, the seed's 120 s is not added
        r.Run(scenario, 30, 0.1, () => BuffCasts(r).Contains("Quickness Self"));
        r.Run(scenario, 20, 0.1);
        Dump(r);
        Assert.Equal(["Quickness Self"], BuffCasts(r).Distinct().ToList());
        Assert.Contains(r.Log.Lines, l => l.Contains("buff refresh: lookahead: Quickness Self 5", StringComparison.Ordinal) && l.Contains("< next lull 60 s (margin 60; the fight's mean 120 s is read, not added", StringComparison.Ordinal));
        Assert.Equal(0, scenario.FightsMeasured);
    }

    [Fact]
    public void Under_fire_only_a_line_under_the_30_s_hard_floor_is_recast_and_a_line_at_100_s_waits_for_the_lull()
    {
        var r = Rig(l => l.Name switch { "Quickness Self" => 100, "Strength Self" => 25, _ => 1500 }, new BuffOptions { FightSeedSeconds = 120 }, 12); // the tusker 12 m away: in reach the whole test
        var scenario = Armed(r);
        for (var i = 0; i < 3; i++)
        {
            Hit(r);
        }

        r.Run(scenario, 20, 0.1);
        Dump(r);
        Assert.Equal(["Strength Self"], BuffCasts(r).Distinct().ToList());
        Assert.Contains(r.Log.Lines, l => l.Contains("buff refresh: hard: Strength Self has 2", StringComparison.Ordinal) && l.Contains("under the 30 s hard floor", StringComparison.Ordinal));
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("buff refresh: lookahead:", StringComparison.Ordinal));
        Assert.True(r.BoltAnswers.Any(), "the fight went on casting at the tusker");
    }

    [Fact]
    public void A_lapsed_line_is_recast_under_fire_as_the_emergency_case()
    {
        var r = Rig(l => l.Name == "Quickness Self" ? 2 : 1500, new BuffOptions(), 12);
        var scenario = Armed(r);
        r.Run(scenario, 5, 0.1); // the line is seen active, then lapses
        Hit(r);
        r.Run(scenario, 30, 0.1, () => BuffCasts(r).Contains("Quickness Self"));
        Dump(r);
        Assert.Contains("Quickness Self", BuffCasts(r));
        Assert.Contains(r.Log.Lines, l => l.Contains("buff refresh: hard: Quickness Self ", StringComparison.Ordinal));
    }

    [Fact]
    public void The_lookahead_margin_and_the_fight_seed_are_registered_options()
    {
        Assert.Equal(60, new BuffOptions().LookaheadMarginSeconds);
        Assert.Equal(90, new BuffOptions().FightSeedSeconds); // soak round 3's den: 39 engagements, a mean of 90 s
        var parsed = BuffOptions.FromArguments(CommandLineArgument.ParseAll(["--buff-lookahead-margin-s=45", "--buff-fight-seed-s=150"]));
        Assert.Empty(parsed.Errors);
        Assert.Equal(45, parsed.LookaheadMarginSeconds);
        Assert.Equal(150, parsed.FightSeedSeconds);
        Assert.NotEmpty(BuffOptions.FromArguments(CommandLineArgument.ParseAll(["--buff-fight-seed-s=0"])).Errors);
        Assert.NotEmpty(BuffOptions.FromArguments(CommandLineArgument.ParseAll(["--buff-lookahead-margin-s=-1"])).Errors);
        Assert.Contains(OptionCatalog.Buff().Options, x => x.Name == "--buff-lookahead-margin-s");
        Assert.Contains(OptionCatalog.Buff().Options, x => x.Name == "--buff-fight-seed-s");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_autopilots_own_fight_ends_in_the_magic_mode_and_any_other_fight_in_peace(bool hold)
    {
        var r = new FightManaTests.Rig(mana: 400, stamina: 200, fight: new FightOptions { Kills = 1, HoldStance = hold }) { HitsToKill = 2 };
        r.Caster.ManaRegenPerSecond = 40;
        r.AddTusker(12);
        var scenario = r.Scenario();
        var status = r.Run(scenario, 120);
        Dump(r);
        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(1, ((IStoppableFight)scenario).Kills);
        if (hold)
        {
            Assert.DoesNotContain(CombatModes.NonCombat, r.Caster.ModesSent); // no peace at the fight's end: the next station wants the magic mode again
            Assert.Contains(r.Log.Lines, l => l.Contains("the stance holds: the magic mode stays up for the autopilot's next station (F25)", StringComparison.Ordinal));
        }
        else
        {
            Assert.Equal(CombatModes.NonCombat, r.Caster.ModesSent[^1]);
        }
    }
}
