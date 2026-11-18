using Lifestone.Client.Options;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 item 7 F16 (D91, the owner): the fight never retreats on the den and the emergency drain holds the health instead. The line (FightEmergency) is live; under it the mage casts Drain Health Other I (one gesture, no wind-up) at the
/// living tusker in reach with the most health left, again after each cast, until the safe line; the autopilot's upkeep health stop is a profile value (0 on the den). Over the scripted caster; nothing here needs a server.
/// </summary>
public sealed class FightF16Tests
{
    private const uint DrainOne = 1237;

    private static FightManaTests.Rig Rig(int healthPercent, int tuskers, out uint[] guids, FightOptions? fight = null, double castSeconds = 0)
    {
        var r = new FightManaTests.Rig(mana: 400, stamina: 200, knowsDrain: true, fight: fight) { HitsToKill = 400 };
        r.Caster.HealthPerCast = 20;
        r.Caster.ManaRegenPerSecond = 20;
        r.Caster.CastSeconds = castSeconds;
        guids = [.. Enumerable.Range(0, tuskers).Select(i => r.AddTusker(40 + i))];
        r.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max);
        r.Caster.SetHealth(max * (uint)healthPercent / 100);
        return r;
    }

    private static void SetTuskerHealth(FightManaTests.Rig r, uint guid, float fraction)
    {
        var h = r.R.H;
        h.R.Reducer.OnUpdateHealth(h.R.Context, h.Header, new UpdateHealthEvent(guid, fraction));
        r.R.Tick();
    }

    private static uint MaxHealth(FightManaTests.Rig r)
    {
        r.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max);
        return max;
    }

    [Fact]
    public void The_line_is_the_floor_at_the_first_rounds_hit_rate_and_moves_up_with_the_incoming_rate_and_is_cut_to_the_safe_line()
    {
        // Round 1 (15 fight legs): 1,433 hits in 2,489 s and 5,614 damage = 0.576 hits a second at 3.92 a hit = 2.26 a second; three one-second casts and a 10% margin are 26 of 190: the floor (114) holds.
        Assert.Equal(114, FightEmergency.Line(2.26, 1.0, 190, 60, 10, 85), 6);
        Assert.Equal(114, FightEmergency.Line(0, 1.0, 190, 60, 10, 85), 6); // nothing measured yet: the seed (2.3 a second), still the floor
        Assert.Equal((40 * 1.0 * 3) + 19, FightEmergency.Line(40, 1.0, 190, 60, 10, 85), 6); // four tuskers at 10 a second each: the expected damage of three casts plus the margin
        Assert.Equal(190 * 0.85, FightEmergency.Line(500, 1.0, 190, 60, 10, 85), 6); // never over the safe line
        Assert.True(FightEmergency.Line(40, 2.0, 190, 60, 10, 85) > FightEmergency.Line(40, 1.0, 190, 60, 10, 85)); // a slower cast raises it
    }

    [Fact]
    public void The_emergency_starts_under_the_line_holds_to_the_safe_line_and_logs_the_line_only_when_it_moves_over_five_points()
    {
        var e = new FightEmergency(60, 85, 10);
        Assert.False(e.Wanted(120, 190, 2.3, 0)); // 63%: over the line
        Assert.True(e.Wanted(110, 190, 2.3, 1)); // 58%: starts
        Assert.True(e.Wanted(150, 190, 2.3, 2)); // 79%: holds up to the safe line
        Assert.False(e.Wanted(162, 190, 2.3, 3)); // 85%: ends
        Assert.False(e.Wanted(120, 190, 2.3, 4)); // 63%: over the line again, not started

        var note = e.LineNote(190, 2.3);
        Assert.NotNull(note);
        Assert.Contains("the line is 114 of 190 (60%)", note, StringComparison.Ordinal);
        Assert.Null(e.LineNote(190, 2.3)); // unchanged: no line
        e.Wanted(190, 190, 20, 5); // 20 a second: 60 + 19 = 79 of 190 is under the floor, still 60%
        Assert.Null(e.LineNote(190, 20));
        e.Wanted(190, 190, 40, 6); // 139 of 190 = 73%: 13 points up
        Assert.Contains("the line is 139 of 190 (73%)", e.LineNote(190, 40), StringComparison.Ordinal);
        Assert.False(new FightEmergency(0, 85, 10).Wanted(1, 190, 2.3, 0)); // floor 0 is off
    }

    [Fact]
    public void Set_aside_after_failed_casts_the_emergency_still_says_it_holds_so_a_health_drink_can_go_M6_8()
    {
        var e = new FightEmergency(60, 85, 10);
        Assert.True(e.Wanted(50, 190, 2.3, 0)); // under the line
        Assert.False(e.AsideNow(0));
        var aside = false;
        for (var i = 0; i < FightEmergency.MaxFailsInARow; i++)
        {
            aside = e.Ended(landed: false, resisted: false, 2, 1);
        }

        Assert.True(aside);
        Assert.False(e.Wanted(50, 190, 2.3, 2)); // no cast is tried
        Assert.True(e.AsideNow(2)); // but it holds: the fight asks for a health drink
        Assert.False(e.AsideNow(1 + FightEmergency.AsideSeconds)); // the set-aside is over: the casts again
        Assert.True(e.Wanted(190, 190, 2.3, 3) is false && !e.AsideNow(3)); // healed over the safe line: nothing holds
    }

    [Fact]
    public void The_fullest_tusker_is_picked_and_the_nearest_among_equals_and_a_blocked_one_is_skipped()
    {
        float[] health = [0.3f, 1.0f, 0.6f, 1.0f];
        float[] distance = [5, 9, 3, 6];
        Assert.Equal(3, FightEmergency.Fullest(health, distance, [], 4)); // two are full: the nearer (6 m) of them
        Assert.Equal(1, FightEmergency.Fullest(health, distance, [false, false, false, true], 4)); // the nearer is blocked
        Assert.Equal(-1, FightEmergency.Fullest(health, distance, [true, true, true, true], 4));
        Assert.Equal(-1, FightEmergency.Fullest(health, distance, [], 0));
    }

    [Fact]
    public void The_options_default_to_60_85_10_and_are_bounded_and_registered()
    {
        var d = new FightOptions();
        Assert.Equal((60, 85, 10), (d.EmergencyFloorPercent, d.EmergencySafePercent, d.EmergencyMarginPercent));
        var o = FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-emergency-floor=70", "--fight-emergency-safe=95", "--fight-emergency-margin=5"]));
        Assert.Equal((70, 95, 5), (o.EmergencyFloorPercent, o.EmergencySafePercent, o.EmergencyMarginPercent));
        Assert.Equal(90, FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-emergency-floor=90"])).EmergencySafePercent); // the safe line is pulled up with the floor
        Assert.Equal(60, FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-emergency-safe=10"])).EmergencySafePercent); // never under the floor
        Assert.Equal(0, FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-emergency-floor=0"])).EmergencyFloorPercent);
        Assert.Equal(60, FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-emergency-floor=101"])).EmergencyFloorPercent); // a bad value keeps the default
        foreach (var name in new[] { "--fight-emergency-floor", "--fight-emergency-safe", "--fight-emergency-margin" })
        {
            Assert.Contains(OptionCatalog.Fight().Options, x => x.Name == name);
        }
    }

    [Fact]
    public void The_den_profile_never_retreats_for_health_and_the_upkeep_floor_is_the_profiles_where_it_has_one_and_the_derived_line_elsewhere()
    {
        var options = new AutopilotOptions();
        var den = GroundCatalog.Get("black-spawn-den-aluvian");
        Assert.Equal(0, den.FightRetreatHealthPercent);
        Assert.Equal(0, AutopilotScenario.HealthFloorFor(den, options)); // the upkeep never cuts the fight for health on the den
        Assert.Equal(30, options.HealthFloorPercent);
        foreach (var key in new[] { "low-b-belt", "low-north-forest-mix", "tusker-temple-fort-tethana" })
        {
            var other = GroundCatalog.Get(key);
            Assert.Null(other.FightRetreatHealthPercent);
            Assert.Equal(options.HealthFloorPercent, AutopilotScenario.HealthFloorFor(other, options)); // another ground still does
        }
    }

    [Fact]
    public void The_den_carries_the_scarab_of_level_I_and_buys_it_from_Cindrue_on_a_trip()
    {
        var den = GroundCatalog.Get("black-spawn-den-aluvian");
        var lead = den.Supplies.StockOf(691);
        Assert.NotNull(lead);
        Assert.Equal("Lead Scarab", lead!.Name);
        Assert.Equal(2304u, den.VendorSelling(691)!.Wcid); // Archmage Cindrue
        Assert.False(den.IsCarried(691)); // bought on trips
        Assert.Equal(14, den.PriceOf(691));
    }

    [Fact]
    public void Under_the_line_the_next_cast_is_Drain_Health_Other_I_at_the_fullest_tusker_in_reach_within_one_gesture_and_it_goes_on_to_the_safe_line_and_never_retreats()
    {
        var r = Rig(healthPercent: 55, tuskers: 3, out var t, castSeconds: 1.0); // 55%: over D88's old 50% drain line, under the emergency line
        var targets = new List<(uint Spell, uint Target)>();
        r.Caster.TargetedCastHook = (spell, guid) =>
        {
            targets.Add((spell, guid));
            return false;
        };
        SetTuskerHealth(r, t[0], 0.3f);
        SetTuskerHealth(r, t[1], 1.0f);
        SetTuskerHealth(r, t[2], 0.6f);
        var scenario = r.Scenario();
        var started = r.Now;
        var max = MaxHealth(r);

        r.Run(scenario, 60, 0.1, () => targets.Any(x => !DrainPicker.Ids.Contains(x.Spell)));

        Assert.Contains(targets[0].Spell, DrainPicker.Ids); // M5 7 F19 (D92): the level the measured heal and the target's real health choose (D91's "always I" is superseded)
        Assert.Equal(t[1], targets[0].Target); // the fullest
        Assert.True(r.Caster.Answered[0].At - started <= 1.6, "the first cast lands within one gesture, not after a wind-up or a retreat");
        var drains = targets.TakeWhile(x => DrainPicker.Ids.Contains(x.Spell)).ToList();
        Assert.True(drains.Count >= 2, "again at once while the health is under the safe line");
        Assert.All(drains, x => Assert.Contains(x.Spell, DrainPicker.Ids)); // a drain each time, at the level that heals most a second
        r.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out _);
        Assert.True(health * 100 >= max * 85, $"health {health} of {max} is at the safe line when the normal choices begin again");
        Assert.True(scenario.EmergencyDrains >= 2);
        Assert.Contains(r.Log.Lines, l => l.Contains("emergency: Drain Health Other ", StringComparison.Ordinal) && l.Contains($" on Tusker Slave 0x{t[1]:X8} at health ", StringComparison.Ordinal));
        Assert.Contains(r.Log.Lines, l => l.Contains("emergency: the line is ", StringComparison.Ordinal));
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("stop=health-floor", StringComparison.Ordinal));
    }

    [Fact]
    public void After_each_cast_the_fullest_tusker_in_reach_is_chosen_again()
    {
        var r = Rig(healthPercent: 30, tuskers: 2, out var t);
        var targets = new List<uint>();
        r.Caster.TargetedCastHook = (spell, guid) =>
        {
            if (DrainPicker.Ids.Contains(spell))
            {
                targets.Add(guid);
            }

            return false;
        };
        SetTuskerHealth(r, t[0], 1.0f);
        SetTuskerHealth(r, t[1], 0.5f);
        var scenario = r.Scenario();
        r.Run(scenario, 20, 0.1, () => targets.Count >= 1);
        Assert.Equal(t[0], targets[0]);

        SetTuskerHealth(r, t[0], 0.2f); // the first was drained down; the other is now the fullest
        SetTuskerHealth(r, t[1], 0.9f);
        r.Run(scenario, 20, 0.1, () => targets.Count >= 2);
        Assert.Equal(t[1], targets[1]);
        Assert.Contains(r.Log.Lines, l => l.Contains($"target 0x{t[1]:X8}", StringComparison.Ordinal) && l.Contains("the emergency drain's fullest tusker in reach", StringComparison.Ordinal));
    }

    [Fact]
    public void The_emergency_outranks_the_stamina_refill_and_the_bolt_and_with_the_floor_at_0_it_is_off()
    {
        var r = Rig(healthPercent: 55, tuskers: 1, out _);
        r.Caster.StaminaRegenPerSecond = 0;
        r.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxStamina, out _, out var staminaMax);
        r.Caster.Stamina = staminaMax / 3; // far under the refill share
        var scenario = r.Scenario();
        r.Run(scenario, 30, 0.1, () => r.Caster.Answered.Any());
        Assert.Contains(r.Caster.Answered[0].Spell, DrainPicker.Ids); // before the Revitalize Self of the stamina order and before any bolt
        Assert.DoesNotContain(r.RevitalizeAnswers, a => a.At <= r.Caster.Answered[0].At);

        var off = Rig(healthPercent: 55, tuskers: 1, out _, fight: new FightOptions { EmergencyFloorPercent = 0 });
        var offScenario = off.Scenario();
        off.Run(offScenario, 10, 0.1, () => off.Caster.Answered.Count >= 3);
        Assert.DoesNotContain(off.Caster.Answered, a => a.Spell == DrainOne); // 55% is over D88's own 50% line: no drain at all
        Assert.Equal(0, offScenario.EmergencyDrains);
    }

    [Fact]
    public void With_no_tusker_in_reach_the_emergency_casts_Heal_Self()
    {
        var r = Rig(healthPercent: 55, tuskers: 0, out _);
        var scenario = r.Scenario();
        r.Run(scenario, 30, 0.1, () => r.Caster.Answered.Count(a => VitalsMeans.HealSelf.Contains(a.Spell)) >= 1);
        Assert.Contains(r.Caster.Answered, a => VitalsMeans.HealSelf.Contains(a.Spell));
        Assert.DoesNotContain(r.Caster.Answered, a => DrainPicker.Ids.Contains(a.Spell));
        Assert.Contains(r.Log.Lines, l => l.Contains("emergency: no tusker in reach; ", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_hit_is_logged_with_its_amount_and_the_biggest_is_kept_for_the_verdict_and_the_autopilot()
    {
        var r = Rig(healthPercent: 100, tuskers: 1, out _);
        var scenario = r.Scenario();
        r.Run(scenario, 2, 0.1);
        foreach (var damage in new uint[] { 7, 31, 12 })
        {
            r.R.Server((p, s) => AceM3Writers.DefenderNotification(p, s, "Tusker Slave", 4, 0.1, damage, 4, false, 0));
            r.Run(scenario, 0.3, 0.1);
        }

        Assert.Equal(31, scenario.Tally.HitMax);
        Assert.Equal(3, r.Log.Lines.Count(l => l.Contains("hit: Tusker Slave hit the player for ", StringComparison.Ordinal)));
        Assert.Contains(r.Log.Lines, l => l.Contains("hit: Tusker Slave hit the player for 31 ", StringComparison.Ordinal) && l.Contains("biggest 31", StringComparison.Ordinal));
        Assert.Contains(" fizzles=0 hit_max=31 target_died_mid_cast=0 doomed_skips=0 openers=", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(31, ((IStoppableFight)scenario).HitMax);
    }

    [Fact]
    public void Every_decision_of_the_picker_is_one_line_with_every_score_the_choice_and_the_emergency_line()
    {
        var r = Rig(healthPercent: 100, tuskers: 1, out _);
        var scenario = r.Scenario();
        r.Run(scenario, 20, 0.1, () => r.BoltAnswers.Count() >= 4);
        var decisions = r.Log.Lines.Where(l => l.Contains("] decide: ", StringComparison.Ordinal)).ToList();
        Assert.True(decisions.Count >= 3, "a line every decision, not a line a change of pick");
        Assert.All(decisions, l =>
        {
            Assert.Contains("scores (all-in s):", l, StringComparison.Ordinal);
            Assert.Contains("| chosen ", l, StringComparison.Ordinal);
            Assert.Contains("| why ", l, StringComparison.Ordinal);
            Assert.Contains("emergency line ", l, StringComparison.Ordinal);
        });
        Assert.Contains(r.Log.Lines, l => l.Contains(", was 0x00000000: the first pick", StringComparison.Ordinal) && l.Contains("target 0x", StringComparison.Ordinal));
    }
}
