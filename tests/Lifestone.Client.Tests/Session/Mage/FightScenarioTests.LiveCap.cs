using Lifestone.Client.Options;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 (the owner, 2026-11-13: the climber casts the highest bolt her live skill lands at the plan chance, not the ground plan's projected-base cap): the level up job's ladder cap reaches the fight as
/// <see cref="FightOptions.LiveCap"/> and governs: the ground's <c>spell_plan</c> row does not lower it and is named in the log line; the opener and the mana dance read the same cap; every fight without the ladder's cap
/// (BSD Mage's den job, a command line's <c>--fight-max-level</c>) keeps the row's cap exactly as before. Nothing here needs the dats or a server.
/// </summary>
public sealed partial class FightScenarioTests
{
    [Fact]
    public void The_ladders_live_cap_governs_and_the_grounds_row_only_names_the_projection()
    {
        var abode = GroundCatalog.Get("tusker-abode");
        var live = new FightOptions { MaxLevel = 6, LiveCap = true };

        Assert.Equal(6, FightScenario.PlanCap(abode, 45, live, out var note)); // the row says V at 45 (the base skill's 254); her buffed War Magic lands VI
        Assert.Equal("the ladder's live cap governs: Flame Bolt up to VI (the highest level at the plan chance on the current War Magic); the ground's plan row, the projection on the base skill, names Flame Bolt V for level 45 and does not lower it", note);
        Assert.Equal(4, FightScenario.PlanCap(abode, 45, live with { MaxLevel = 4 }, out _)); // the live cap is the cap, under the row too (the ladder never sends one under the rung's plan)
        Assert.Equal(6, FightScenario.PlanCap(abode, 70, live, out var none));
        Assert.EndsWith("the ground's plan has no row for level 70", none, StringComparison.Ordinal);

        // Without the flag, the row's rule exactly as before: the same cap is lowered to the row's V.
        Assert.Equal(5, FightScenario.PlanCap(abode, 45, new FightOptions { MaxLevel = 6 }, out var plain));
        Assert.Equal("the ground's plan caps Flame Bolt at V for level 45", plain);
        Assert.Equal(5, FightScenario.PlanCap(abode, 45, 6, out _));

        // BSD Mage's den job: no ladder, no live cap: VI below 100.
        Assert.Equal(6, FightScenario.PlanCap(GroundCatalog.Get(FightOptions.DefaultGround), 80, new FightOptions(), out var den));
        Assert.Equal("the ground's plan caps Flame Bolt at VI for level 80", den);
    }

    [Fact]
    public void The_den_fights_max_level_argument_is_the_live_cap_and_a_command_lines_cap_is_not()
    {
        var spec = new StationSpec("den-fight", StationKinds.DenFight, null, 0, new Dictionary<string, string>(StringComparer.Ordinal) { [JourneyPlans.MaxLevelArgument] = "6" });

        var fromSpec = JourneyStations.DenFightOptions(spec, new FightOptions(), []);
        Assert.Equal((6, true), (fromSpec.MaxLevel, fromSpec.LiveCap));
        Assert.Contains(" max_level=6 live_cap=yes", fromSpec.ToString(), StringComparison.Ordinal);

        var given = CommandLineArgument.ParseAll(["--fight-max-level=4"]);
        Assert.False(FightOptions.FromArguments(given).LiveCap); // not a command-line option
        var fromLine = JourneyStations.DenFightOptions(spec, FightOptions.FromArguments(given), given);
        Assert.Equal((4, false), (fromLine.MaxLevel, fromLine.LiveCap)); // the command line's cap: the row still caps under it
        Assert.DoesNotContain("live_cap", fromLine.ToString(), StringComparison.Ordinal);

        Assert.False(JourneyStations.DenFightOptions(spec with { Args = null }, new FightOptions(), []).LiveCap);
    }

    [Fact]
    public void In_the_fight_the_live_cap_casts_above_the_grounds_row_and_without_it_the_row_holds()
    {
        var bolts = ArenaProfile.LineNamed("Flame Bolt")!.Spells;

        var live = new Rig(characterLevel: 80, fight: new FightOptions { MaxLevel = 7, LiveCap = true });
        live.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var atLive = live.Scenario(new FakeWalk());
        Assert.Equal(ScenarioStatus.Succeeded, live.Run(atLive));
        Assert.Equal("VII", atLive.Level); // the den's row says VI below 100; the live cap does not take it lower
        Assert.Contains(live.Log.Lines, l => l.Contains("the ladder's live cap governs: Flame Bolt up to VII", StringComparison.Ordinal) && l.Contains("names Flame Bolt VI for level 80 and does not lower it", StringComparison.Ordinal));
        Assert.All(live.CastSpells, id => Assert.Equal(bolts[6].Id, id));

        var plain = new Rig(characterLevel: 80, fight: new FightOptions { MaxLevel = 7 });
        plain.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var atPlain = plain.Scenario(new FakeWalk());
        Assert.Equal(ScenarioStatus.Succeeded, plain.Run(atPlain));
        Assert.Equal("VI", atPlain.Level);
        Assert.Contains(plain.Log.Lines, l => l.Contains("the ground's plan caps Flame Bolt at VI for level 80", StringComparison.Ordinal));
    }

    [Fact]
    public void The_opener_and_the_mana_dance_read_the_cap_the_fight_casts_by()
    {
        var abode = GroundCatalog.Get("tusker-abode");
        var r = new Rig(openers: true);
        var live = new FightOptions { MaxLevel = 6, LiveCap = true };
        var plain = new FightOptions { MaxLevel = 6 };

        // The mana dance: the fight's own cap, live or the row's.
        Assert.Equal(FightScenario.PlanCap(abode, 45, live, out _), new ManaDance(r.Table, abode, live, new BuffOptions(), r.Log, "test").CapAt(45));
        Assert.Equal(6, new ManaDance(r.Table, abode, live, new BuffOptions(), r.Log, "test").CapAt(45));
        Assert.Equal(5, new ManaDance(r.Table, abode, plain, new BuffOptions(), r.Log, "test").CapAt(45));

        // The opener: its level is chosen under the cap the fight gives it (LevelCapFor, the bolt's own): VI under the live cap where the row would give V.
        var line = ArenaProfile.ExtraNamed("Fire Vulnerability Other")!;
        var book = r.R.H.State.Self.Spellbook;
        Assert.Equal(5, FightScenario.ChooseOpenerLevel(line, r.Table, book, 450, 0.9, FightScenario.PlanCap(abode, 45, live, out _), null, out _));
        Assert.Equal(4, FightScenario.ChooseOpenerLevel(line, r.Table, book, 450, 0.9, FightScenario.PlanCap(abode, 45, plain, out _), null, out _));

        // In the fight, the opener and the bolt ask the one cap (FightScenario.LevelCapFor): its line is logged once, the live cap's.
        var fight = new Rig(characterLevel: 80, openers: true, fight: new FightOptions { MaxLevel = 7, LiveCap = true });
        fight.Add(TuskerGuid, TuskerSlave, FloorCell, 40);
        var scenario = fight.Scenario(new FakeWalk());
        Assert.Equal(ScenarioStatus.Succeeded, fight.Run(scenario));
        Assert.True(scenario.OpenersLanded >= 1);
        Assert.Single(fight.Log.Lines, l => l.Contains("the ladder's live cap governs", StringComparison.Ordinal));
        Assert.DoesNotContain(fight.Log.Lines, l => l.Contains("the ground's plan caps", StringComparison.Ordinal));
    }
}
