using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;
using Lifestone.Tests.Shared.Net;
using Xunit;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 upkeep mana, in the fight rig (the pure rule is <c>VitalsPolicyUpkeepTests</c>). The live fault (2026-11-14 01:29 to 02:15 UTC, the Tusker Quarters at level 58): a hostile always in reach, the mana at 150 to 200 of 377, the
/// way out armed, and every recast of Impenetrability and Bludgeon Bane refused by its guard until they lapsed under fire. What these prove, over the scripted caster: in that shape the climber's policy fills toward the guard's number
/// before the lines are due and both are recast before they lapse; a drink fills it when no press can; a drain that did not land holds the rule aside, which the policy reads.
/// </summary>
public sealed class FightUpkeepManaTests(ITestOutputHelper output)
{
    private static readonly WayOutLine WayOut = ManaWayOut.LineOf(50, 10);

    private static readonly string[] Armour = ["Impenetrability", "Bludgeon Bane"];

    private void Dump(FightManaTests.Rig r)
    {
        foreach (var line in r.Log.Lines.Where(l => l.Contains("buff refresh", StringComparison.Ordinal) || l.Contains("(upkeep: ", StringComparison.Ordinal) || l.Contains("mana: upkeep", StringComparison.Ordinal) || l.Contains("way out", StringComparison.Ordinal) || l.Contains("landed: read to", StringComparison.Ordinal) || l.Contains("Potion brought", StringComparison.Ordinal)).Take(80))
        {
            output.WriteLine(line);
        }
    }

    private static string? LineOf(uint spell) => ArenaProfile.Lines.FirstOrDefault(l => l.IsBuff && l.Spells.Any(s => s.Id == spell))?.Name;

    /// <summary>The way out's number for <paramref name="line"/>: the dearest level the book holds at its ceiling on her worn pieces, plus the line (the guard's own sum, <c>FightScenario.BuffGuardCost</c>).</summary>
    private static uint GuardNeed(FightManaTests.Rig r, string line)
    {
        var self = r.R.H.State.Self;
        var cost = ArenaProfile.LineNamed(line)!.Spells.Where(s => self.Spellbook.Knows(s.Id) && r.Caster.Table.ContainsKey(s.Id)).Max(s => GearCasts.ManaCeiling(r.Caster.Table[s.Id], self.Inventory.Equipped.Count));
        return cost + WayOut.Line;
    }

    /// <summary>The Quarters' shape: the climber, eight tuskers in reach and a hit every second, the two armour lines due in <paramref name="left"/> s, the way out armed, the mana under the guard's number.</summary>
    private (FightManaTests.Rig Rig, FightScenario Scenario, uint Need) Quarters(double left, uint stamina, uint manaPotions, bool knowsDance = true)
    {
        // The bolt is held to its first level (MaxLevel 1): a few mana a bolt after Mana Conversion, as the live Frost Bolt VI's 9 (the rig's level VII costs 70), so the mana sits where the old rules leave it; no Revitalize Self,
        // so the stamina is only what she regenerates and drains.
        var r = new FightManaTests.Rig(mana: 400, stamina: stamina, manaPotions: manaPotions, knowsRevitalize: false, knowsDrain: true, knowsStaminaDrain: true, knowsDance: knowsDance) { HitsToKill = 3, Respawn = true, Fight = new FightOptions { Kills = 200, MaxLevel = 1, Seconds = 3600 } };
        r.R.H.State.Self.Name = MageTemplate.ClimberName;
        r.Caster.ManaRegenPerSecond = 0.43; // the live regeneration (Battlemage's Boon on ACE's human row)
        r.Caster.StaminaRegenPerSecond = 0.645;
        r.Caster.CastSeconds = 2; // a cast's wind-up, so a kill takes seconds as it does live (the rig answers at once otherwise)
        r.Caster.HealthPerCast = 20;
        for (var i = 0; i < 8; i++)
        {
            r.AddTusker(12 + (i * 0.5f));
        }

        FightF24Tests.Prime(r, l => Armour.Contains(l.Name) ? left : 1500);
        var need = Armour.Max(a => GuardNeed(r, a));
        r.Caster.SetVitals(need - 60, stamina); // under the guard's number, as the live 150 to 200 of 377 were under its 290
        r.R.Tick();
        var scenario = r.Scenario();
        scenario.RequestBuffRefresh(300, 20, "a line under the 300 s margin");
        scenario.ArmWayOut(WayOut);
        return (r, scenario, need);
    }

    private static void Hit(FightManaTests.Rig r, uint damage = 1) =>
        r.R.Server((p, s) => AceM3Writers.DefenderNotification(p, s, "Tusker Slave", 4, 0.1, damage, 4, false, 0));

    private static void RunUnderFire(FightManaTests.Rig r, FightScenario scenario, double seconds, Func<bool> until)
    {
        var last = double.NegativeInfinity;
        r.Run(scenario, seconds, 0.1, () =>
        {
            if (r.Now - last >= 1)
            {
                last = r.Now;
                Hit(r);
            }

            return until();
        });
    }

    private static bool BothRecast(FightManaTests.Rig r) => Armour.All(a => r.Caster.Answered.Any(x => LineOf(x.Spell) == a && !x.Fizzled));

    [Fact]
    public void Under_constant_fire_with_the_mana_under_the_guards_number_the_policy_fills_before_the_lines_are_due_and_both_are_recast_before_they_lapse()
    {
        var (r, scenario, need) = Quarters(left: 90, stamina: 150, manaPotions: 0);
        var start = r.Now;
        Assert.True(r.Mana < need, $"mana {r.Mana} under the guard's {need}");

        RunUnderFire(r, scenario, 120, () => BothRecast(r));
        Dump(r);

        Assert.True(BothRecast(r), "Impenetrability and Bludgeon Bane were recast");
        var recasts = r.Caster.Answered.Where(x => Armour.Contains(LineOf(x.Spell))).Select(x => x.At).ToList();
        Assert.True(recasts.Max() < start + 90, $"the last armour recast at {recasts.Max() - start:0.#} s, before the lapse at 90 s");
        Assert.Equal(0, scenario.BuffsExpired);
        Assert.DoesNotContain(r.Log.Entries, e => e.Severity == LogSeverity.Error && e.Line.Contains("EXPIRED", StringComparison.Ordinal));
        Assert.True(scenario.UpkeepFills >= 1, $"upkeep fills {scenario.UpkeepFills}");
        var lines = r.Log.Lines.ToList();
        var firstFill = lines.FindIndex(l => l.Contains("vitals policy: ", StringComparison.Ordinal) && l.Contains("(upkeep: ", StringComparison.Ordinal));
        var firstRecast = lines.FindIndex(l => l.Contains("buff refresh: ", StringComparison.Ordinal) && (l.Contains(": Impenetrability has", StringComparison.Ordinal) || l.Contains(": Bludgeon Bane has", StringComparison.Ordinal)));
        Assert.InRange(firstFill, 0, firstRecast - 1); // the fill came first, toward the target the log names
        Assert.Contains("upkeep target ", lines[firstFill], StringComparison.Ordinal);
        Assert.Null(scenario.StopReason);
    }

    [Fact]
    public void A_drain_that_did_not_land_holds_the_rule_aside_for_its_seconds_and_the_policy_reads_it()
    {
        // Live 2026-11-14 02:04:32 UTC: Drain Health Other I out of range (0x0550) set the rule aside 30 s, and the policy chose it again every 50 ms for 1.3 s; it now offers no health drain while the rule is aside.
        var drain = new FightDrain(new Dictionary<uint, SpellInfo>(), NullLogSink.Instance, 50, 75);
        Assert.False(drain.AsideAt(10));
        drain.Ended(CastEnd.OutOfRange, false, 120, 190, "out of range (0x0550)", 10);
        Assert.True(drain.AsideAt(10));
        Assert.True(drain.AsideAt(10 + FightDrain.AsideSeconds - 0.01));
        Assert.False(drain.AsideAt(10 + FightDrain.AsideSeconds));

        var emergency = new FightDrain(new Dictionary<uint, SpellInfo>(), NullLogSink.Instance, 50, 75);
        emergency.Ended(CastEnd.OutOfRange, false, 60, 190, "out of range (0x0550)", 10, emergency: true);
        emergency.Ended(CastEnd.Resisted, false, 60, 190, "resisted", 11);
        Assert.False(emergency.AsideAt(11)); // the emergency's misses and a resist are the next pick's, never a hold
    }

    [Fact]
    public void With_no_press_to_be_had_the_policy_drinks_toward_the_target_and_the_lines_are_recast_before_they_lapse()
    {
        // Live 02:14 to 02:18 UTC: stamina 16 to 40 of 267, no stamina source read, 109 Mana Potions held.
        var (r, scenario, need) = Quarters(left: 90, stamina: 0, manaPotions: 20, knowsDance: false);
        var start = r.Now;
        var drunk = r.Caster.PotionsDrunk;

        RunUnderFire(r, scenario, 120, () => BothRecast(r));
        Dump(r);

        Assert.True(BothRecast(r), "Impenetrability and Bludgeon Bane were recast");
        Assert.True(r.Caster.Answered.Where(x => Armour.Contains(LineOf(x.Spell))).Max(x => x.At) < start + 90, "before the lapse");
        Assert.True(r.Caster.PotionsDrunk > drunk, "a drink filled toward the target");
        Assert.Contains(r.Log.Lines, l => l.Contains("mana: upkeep: Mana Potion 0x", StringComparison.Ordinal) && l.Contains("is drunk toward the upkeep's target ", StringComparison.Ordinal));
        Assert.Equal(0, scenario.BuffsExpired);
        _ = need;
    }
}
