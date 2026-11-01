using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.World;
using Lifestone.Net.Messages;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 item 7 F19 (D92, the owner): the emergency heals with what WORKS (the measured heals, the target's real health, never an unknown taken as full), the mana is danced for instead of waited for, and the corpse trip's path geometry.
/// Engine-free, over the scripted caster where a fight runs; nothing here needs a server.
/// </summary>
public sealed class FightF19Tests
{
    [Fact]
    public void ACEs_drain_and_self_heal_lines_are_parsed_and_measured()
    {
        Assert.True(MeasuredHeals.TryParseDrain("You gain 0 points of health due to casting Drain Health Other I on Tusker Guard", out var gain, out var spell, out var target));
        Assert.Equal((0, "Drain Health Other I", "Tusker Guard"), (gain, spell, target));
        Assert.True(MeasuredHeals.TryParseSelfHeal("You cast Heal Self VI and restore 87 points of your Health.", out gain, out spell));
        Assert.Equal((87, "Heal Self VI"), (gain, spell));
        Assert.False(MeasuredHeals.TryParseSelfHeal("You cast Revitalize Self VI and restore 80 points of your Stamina.", out _, out _));
        Assert.False(MeasuredHeals.TryParseDrain("Tusker Guard grazes your foot for 2 points of bludgeoning damage!", out _, out _, out _));

        var heals = new MeasuredHeals();
        Assert.True(heals.Read("You cast Heal Self VI and restore 80 points of your Health."));
        Assert.True(heals.TryMeasured("Heal Self VI", null, out var mean, out var count));
        Assert.Equal((80.0, 1), (mean, count));
        Assert.Equal(80, heals.Expected("Heal Self VI", null, 87.5)); // the measured mean, never more than the formula
        Assert.Equal(87.5, MeasuredHeals.SelfSeed(6)); // ACE's boost 55 plus half its variance 65 (world SQL spell 1161)
    }

    [Fact]
    public void The_drain_rows_are_the_pinned_world_SQLs_and_level_I_gives_60_on_a_full_Slave_and_Guard_and_VI_112_and_78()
    {
        // World SQL v0.9.296 table spell (proportion, transfer_Cap, loss_Percent), the coordinator's read of 2026-10-28 and session.md section 56.
        var expected = new (uint Id, float Proportion, int Cap, float Loss)[] { (1237, 0.25f, 60, -1f), (1238, 0.25f, 80, -0.6f), (1239, 0.25f, 100, -0.33f), (1240, 0.25f, 100, 0f), (1241, 0.3f, 120, 0f), (1242, 0.4f, 150, 0.25f) };
        foreach (var (id, proportion, cap, loss) in expected)
        {
            var row = DrainPicker.RowOf(id)!.Value;
            Assert.Equal((proportion, cap, loss), (row.Proportion, row.TransferCap, row.LossPercent));
        }

        Assert.Equal(60u, Given(1237, 400, 192));
        Assert.Equal(112u, Given(1242, 400, 192));
        Assert.Equal(60u, Given(1237, 260, 192));
        Assert.Equal(78u, Given(1242, 260, 192));
        Assert.Equal(0u, Given(1237, 1, 192)); // the rehearsal's zeros: a guard with a point or two left gives nothing
        Assert.Equal(2u, Given(1237, 4, 192));
    }

    private static uint Given(uint id, uint targetHealth, uint missing)
    {
        var row = DrainPicker.RowOf(id)!.Value;
        BuffStatMods.Transfer(in row, targetHealth, missing, out _, out var given);
        return given;
    }

    [Fact]
    public void A_measured_zero_for_level_I_moves_the_choice_to_the_next_level_or_Heal_Self()
    {
        // the rehearsal's 0, 0, 2, 0, 0: level I's ratio falls and the next level or Heal Self wins (F21: the choice itself is HealPick's, FightF21Tests)
        var heals = new MeasuredHeals();
        for (var i = 0; i < 5; i++)
        {
            heals.NoteRatio("Drain Health Other I", 0, 60);
        }

        var one = heals.ExpectedFromRatio("Drain Health Other I", 60);
        Assert.True(one < 10, $"level I's expected heal is {one} after five zeros");
        Assert.Equal(78, heals.ExpectedFromRatio("Drain Health Other VI", 78)); // a level never measured keeps the formula
        var self = new HealCandidate("Heal Self VI", 0, 87.5, 0, 0.9, 45, 3.0);
        HealCandidate[] drains = [new("Drain Health Other I", 1, one, 0, 0.9, 20, 1.6), new("Drain Health Other VI", 1, 78, 20, 0.9, 45, 3.0)];
        Assert.Equal(1, HealPick.Choose(in self, false, drains, 0.09, 0.25, out _, out _, out _)); // no Heal Self paid for: VI, not the level that measured zero
        Assert.Equal(HealPick.HealSelf, HealPick.Choose(in self, true, drains.AsSpan(0, 1), 0.09, 0.25, out _, out _, out _));
    }

    [Fact]
    public void Under_the_line_a_tusker_the_server_says_is_nearly_dead_is_not_drained_Heal_Self_is_cast()
    {
        var r = new FightManaTests.Rig(mana: 400, stamina: 200, knowsDrain: true) { HitsToKill = 400 };
        r.Caster.HealthPerCast = 20;
        r.Caster.ManaRegenPerSecond = 20;
        var tusker = r.AddTusker(40);
        r.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max);
        r.Caster.SetHealth(max * 55 / 100);
        var h = r.R.H;
        h.R.Reducer.OnUpdateHealth(h.R.Context, h.Header, new UpdateHealthEvent(tusker, 0.01f)); // the answer: four points of a 400-health Slave
        r.R.Tick();
        var scenario = r.Scenario();

        r.Run(scenario, 30, 0.1, () => r.Caster.Answered.Any(a => VitalsMeans.HealSelf.Contains(a.Spell) || DrainPicker.Ids.Contains(a.Spell)));

        Assert.Contains(r.Caster.Answered, a => VitalsMeans.HealSelf.Contains(a.Spell));
        Assert.DoesNotContain(r.Caster.Answered, a => DrainPicker.Ids.Contains(a.Spell));
        Assert.Contains(r.Log.Lines, l => l.Contains("emergency: heal pick: Heal Self", StringComparison.Ordinal) && l.Contains("best drain Drain Health Other", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unknown_health_is_asked_of_the_server_before_the_choice_and_never_read_as_full()
    {
        var r = new FightManaTests.Rig(mana: 400, stamina: 200, knowsDrain: true) { HitsToKill = 400 };
        r.Caster.ManaRegenPerSecond = 20;
        var tusker = r.AddTusker(40); // no health answer yet
        r.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max);
        r.Caster.SetHealth(max * 55 / 100);
        var scenario = r.Scenario();

        r.Run(scenario, 0.5, 0.1);

        Assert.Contains(r.Log.Lines, l => l.Contains($"emergency: the health of Tusker Slave 0x{tusker:X8} is asked of the server first (unknown: never taken as full)", StringComparison.Ordinal));
        Assert.DoesNotContain(r.Caster.Answered, a => DrainPicker.Ids.Contains(a.Spell)); // nothing drained while the answer is awaited
    }

    [Theory]
    [InlineData(10u, 100u, 200u, 275u, 0u, true, 70u, 0, 1)] // the mana short, the stamina there: the transfer, never a wait
    [InlineData(10u, 100u, 40u, 275u, 0u, true, 70u, 3, 3)] // the stamina too low for any transfer, no Revitalize paid: a potion
    [InlineData(80u, 100u, 40u, 275u, 0u, true, 70u, 3, 2)] // the stamina too low, the mana pays a Revitalize: it first
    [InlineData(10u, 100u, 40u, 275u, 0u, true, 70u, 0, 4)] // nothing at all: the named last resort
    [InlineData(100u, 100u, 200u, 275u, 260u, true, 70u, 0, 2)] // the top-up: mana at the goal, the stamina under it
    [InlineData(100u, 100u, 270u, 275u, 260u, true, 70u, 0, 0)]
    [InlineData(10u, 100u, 200u, 275u, 0u, false, 0u, 0, 4)] // no transfer known, nothing paid
    public void Every_former_mana_wait_dances_first_then_a_potion_and_waits_only_as_the_last_resort(uint mana, uint goal, uint stamina, uint staminaMax, uint staminaGoal, bool transfer, uint revitalize, int potions, int step) =>
        Assert.Equal((DanceStep)step, ManaDance.Decide(mana, goal, stamina, staminaMax, staminaGoal, transfer, revitalize, potions));

    [Fact]
    public void The_path_ahead_is_the_line_toward_the_corpse_cut_to_the_look_ahead()
    {
        var self = new GlobalPosition(0, 0, 0);
        var corpse = new GlobalPosition(100, 0, 0);
        Assert.Equal(10, DeathTripPath.DistanceToPathAhead(self, corpse, new GlobalPosition(30, 10, 0), 60), 3); // beside the path
        Assert.Equal(25, DeathTripPath.DistanceToPathAhead(self, corpse, new GlobalPosition(85, 0, 0), 60), 3); // past the look-ahead
        Assert.Equal(5, DeathTripPath.DistanceToPathAhead(self, corpse, new GlobalPosition(-5, 0, 0), 60), 3); // behind the mage: its own reach
        Assert.Equal(5, DeathTripPath.DistanceToPathAhead(self, null, new GlobalPosition(3, 4, 0), 60), 3);
    }
}
