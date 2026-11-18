using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.World;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 7 Y (r2b, level 10): a lone Female Tusker one bolt from dead, and the emergency chose Drain Health Other I twice while it read 1.1 a second against about 11. The finishing bolt, the recent hits, a critical bolt's line, the walk that
/// ran up to the tusker, and the <c>blocked:</c> flood. Over the scripted caster; nothing here needs a server.
/// </summary>
public sealed partial class FightScenarioTests
{
    private const string Slave = "Tusker Slave";

    /// <summary>The fight under way at full health with the tuskers alive (a bolt lands for nothing: 400 hits to kill).</summary>
    private static (FightManaTests.Rig Rig, FightScenario Scenario, List<uint> Spells, uint[] Tuskers) Engaged(int tuskers = 1, float y = 40, FightOptions? fight = null, bool knowsDance = true, uint manaPotions = 0, bool attackable = false)
    {
        var r = new FightManaTests.Rig(mana: 400, stamina: 200, manaPotions: manaPotions, knowsDrain: true, fight: fight, knowsDance: knowsDance) { HitsToKill = 400, TuskerFlags = attackable ? Lifestone.Client.Session.Contracts.WorldObjectTable.AttackableFlag : 0 }; // M6 16 E3.0: attackable for the tests that ask the threat board
        r.Caster.HealthPerCast = 18;
        r.Caster.ManaRegenPerSecond = 20;
        var guids = Enumerable.Range(0, tuskers).Select(i => r.AddTusker(y + (i * 10))).ToArray();
        var spells = new List<uint>();
        r.Caster.TargetedCastHook = (spell, _) =>
        {
            spells.Add(spell);
            return false;
        };
        var scenario = r.Scenario();
        r.Run(scenario, 20, 0.1, () => r.BoltAnswers.Any());
        return (r, scenario, spells, guids);
    }

    private static uint MaxHealthOf(FightManaTests.Rig r)
    {
        r.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max);
        return max;
    }

    private static void TuskerHealth(FightManaTests.Rig r, uint guid, float fraction)
    {
        r.R.H.R.Reducer.OnUpdateHealth(r.R.H.R.Context, r.R.H.Header, new UpdateHealthEvent(guid, fraction));
        r.R.Tick();
    }

    private static void Hit(FightManaTests.Rig r, FightScenario scenario, uint damage, double thenSeconds = 0.3)
    {
        r.R.Server((p, s) => AceM3Writers.DefenderNotification(p, s, Slave, 4, 0.1, damage, 4, false, 0));
        r.Run(scenario, thenSeconds, 0.1);
    }

    private static uint FirstAfter(FightManaTests.Rig r, FightScenario scenario, List<uint> spells, int from)
    {
        r.Run(scenario, 15, 0.1, () => spells.Count > from);
        Assert.True(spells.Count > from, "a cast began");
        return spells[from];
    }

    [Fact]
    public void A_bolt_that_kills_the_only_attacker_beats_the_emergency_drain_when_the_mage_lives_through_the_cast_M6_7_Y()
    {
        var (r, scenario, spells, t) = Engaged();
        var max = MaxHealthOf(r);
        Hit(r, scenario, 3);
        TuskerHealth(r, t[0], 0.02f); // the push: well under one bolt
        r.Caster.SetHealth(max * 40 / 100); // under the line, a long way from the next hit
        var from = spells.Count;

        Hit(r, scenario, 3, 0);
        var first = FirstAfter(r, scenario, spells, from);

        Assert.True(FightManaTests.Rig.IsBolt(first), $"the bolt, not spell {first}");
        Assert.Equal(1, scenario.EmergencyFinishes);
        Assert.Contains(r.Log.Lines, l => l.Contains("emergency: finishing bolt: ", StringComparison.Ordinal) && l.Contains("survives yes", StringComparison.Ordinal) && l.Contains("the bolt kills the only attacker and the mage lives through the cast", StringComparison.Ordinal));
        Assert.Equal(0, scenario.EmergencyDrains);
    }

    [Fact]
    public void With_the_next_hit_killing_and_no_heal_that_saves_the_bolt_still_wins_and_the_case_is_named_M6_7_Y()
    {
        var (r, scenario, spells, t) = Engaged();
        var max = MaxHealthOf(r);
        var health = Math.Max(4, max / 20);
        Hit(r, scenario, health * 2);
        TuskerHealth(r, t[0], 0.02f);
        r.Caster.SetHealth(health);
        var from = spells.Count;

        Hit(r, scenario, health * 2, 0);
        r.Caster.SetHealth(health); // the scripted caster's health is the test's: the blow above read, not lived through
        var first = FirstAfter(r, scenario, spells, from);

        Assert.True(FightManaTests.Rig.IsBolt(first), $"the bolt, not spell {first}");
        Assert.Contains(r.Log.Lines, l => l.Contains("emergency: the next hit kills: the biggest hit seen is ", StringComparison.Ordinal));
        Assert.Contains(r.Log.Lines, l => l.Contains("emergency: finishing bolt: ", StringComparison.Ordinal) && l.Contains("survives no", StringComparison.Ordinal) && l.Contains("no heal can save the mage: a dead attacker stops the incoming", StringComparison.Ordinal));
    }

    [Fact]
    public void A_target_one_bolt_does_not_finish_leaves_the_emergency_heal_as_it_was_M6_7_Y()
    {
        var (r, scenario, spells, t) = Engaged();
        var max = MaxHealthOf(r);
        Hit(r, scenario, 3);
        TuskerHealth(r, t[0], 0.9f);
        r.Caster.SetHealth(max * 40 / 100);
        var from = spells.Count;

        Hit(r, scenario, 3, 0);
        var first = FirstAfter(r, scenario, spells, from);

        Assert.False(FightManaTests.Rig.IsBolt(first));
        Assert.Equal(0, scenario.EmergencyFinishes);
        Assert.Contains(r.Log.Lines, l => l.Contains("one bolt is not expected to finish it; the heal stands", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(22, false)] // 2 m from the mage: at arm's reach, not yet heard
    [InlineData(32, true)]  // 12 m away and it hit: a second attacker
    public void A_second_tusker_at_the_mage_leaves_the_emergency_heal_as_it_was_the_den_fight_M6_7_Y(float y, bool hits)
    {
        var (r, scenario, spells, t) = Engaged();
        var max = MaxHealthOf(r);
        Hit(r, scenario, 3);
        TuskerHealth(r, t[0], 0.02f);
        var extra = r.AddTusker(y);
        TuskerHealth(r, extra, 0.9f);
        r.Caster.SetHealth(max * 40 / 100);
        var from = spells.Count;
        if (hits)
        {
            Hit(r, scenario, 3, 0);
        }

        var first = FirstAfter(r, scenario, spells, from);

        Assert.False(FightManaTests.Rig.IsBolt(first));
        Assert.Equal(0, scenario.EmergencyFinishes);
    }

    [Fact]
    public void The_emergency_line_reads_the_recent_hits_not_the_fights_long_mean_M6_7_Y()
    {
        var (r, scenario, _, _) = Engaged();
        Hit(r, scenario, 18, 1.2);
        Hit(r, scenario, 16, 0);
        var read = scenario.Picker.Rates.Read(r.Now);

        Assert.Equal(2, read.Hits);
        Assert.Equal(18, read.BiggestHit);
        Assert.True(read.PerSecond > 8, $"two blows 1.5 s apart read {read.PerSecond:0.0} a second");
        Assert.True(scenario.Picker.Rates.IncomingPerSecond < 3, "the long mean stays the picker's");
    }

    [Theory]
    [InlineData("Critical hit! You incinerate Female Tusker for 60 points with Flame Bolt II.")]
    [InlineData("You incinerate Female Tusker for 40 points with Flame Bolt II.")]
    [InlineData("Critical hit! Overpower! You incinerate Female Tusker for 60 points with Flame Bolt II.")]
    public void A_damage_lines_target_is_read_past_ACEs_prefixes_M6_7_Y(string line) =>
        Assert.Equal("Female Tusker", FightScenario.DealtTarget(line));

    [Fact]
    public void A_critical_bolts_line_ends_its_flight_so_the_next_cast_is_not_held_back_M6_7_Y()
    {
        var (r, scenario, _, t) = Engaged();
        r.R.Tick();
        var position = r.View.State.Objects.TryGet(t[0], out var tusker) ? tusker.Position!.Value.ToGlobal() : default;
        var spell = r.BoltAnswers.Last().Spell;
        scenario.NoteInFlight(r.View, r.Now, spell, t[0], r.Now);
        var before = scenario.InFlightDamageOn(t[0], position, r.Now);
        Assert.True(before > 0);

        var name = r.Caster.Table[spell].Name;
        r.R.Raw(AceWireWriters.ServerMessage($"Critical hit! You incinerate Tusker Slave for 60 points with {name}.", 7));
        r.R.Tick();

        Assert.Equal(before - scenario.InFlightDamageOn(t[0], position, r.Now), before, 6);
    }

    [Fact]
    public void A_target_that_comes_into_range_and_line_drops_the_tours_leg_and_is_cast_at_from_there_M6_7_Y()
    {
        var r = new Rig();
        r.Add(TuskerGuid, TuskerSlave, 0x01050303, 80); // out of range: a leg of the tour
        var walk = new FakeWalk { TicksPerLeg = 100_000 };
        var scenario = ToTheFight(r, r.Scenario(walk));
        for (var i = 0; i < 20 && walk.Legs == 0; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal(1, walk.Legs);
        var casts = scenario.Tally.Casts;
        const uint Near = 0x80001234;
        r.Add(Near, TuskerSlave, FloorCell, 40); // a tusker walks into range and line
        for (var i = 0; i < 60 && scenario.Tally.Casts == casts; i++)
        {
            r.Frame(scenario);
        }

        Assert.True(scenario.Tally.Casts > casts, "the mage stops and casts, no hit needed");
        Assert.Equal(1, walk.Legs);
        Assert.Equal(1, LinesWith(r, $"sight: the walk is dropped; 0x{Near:X8} is in range with a clear line"));
    }

    [Fact]
    public void A_target_in_range_with_no_line_leaves_the_tours_leg_alone_M6_7_Y()
    {
        var r = new Rig();
        r.Add(TuskerGuid, TuskerSlave, 0x01050303, 40);
        var walk = new FakeWalk { BlockedCell = 0x01050303, TicksPerLeg = 100_000 };
        var scenario = ToTheFight(r, r.Scenario(walk));
        for (var i = 0; i < 120; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal(1, walk.Legs);
        Assert.Equal(0, LinesWith(r, "sight: the walk is dropped"));
    }

    [Fact]
    public void The_blocked_line_is_written_once_every_five_seconds_and_every_pick_is_still_counted_M6_7_Y()
    {
        var r = new Rig();
        r.Add(TuskerGuid, TuskerSlave, 0x01050303, 40);
        var walk = new FakeWalk { BlockedCell = 0x01050303, KeepBlockedCell = true, TicksPerLeg = 1 }; // the pick runs again every few frames
        var scenario = ToTheFight(r, r.Scenario(walk));
        var started = r.View.Clock.Now;
        for (var i = 0; i < 60; i++)
        {
            r.Frame(scenario);
        }

        var lines = LinesWith(r, "blocked: no clear line to");
        Assert.True(lines <= 1 + (int)((r.View.Clock.Now - started) / FightScenario.BlockedLogSeconds), $"{lines} lines");
        Assert.True(scenario.Tally.BlockedPicks >= lines * 3, $"{scenario.Tally.BlockedPicks} picks counted against {lines} lines");
        Assert.True(LinesWith(r, " more not logged)") >= 1, "the next line says how many it held back");
    }
}
