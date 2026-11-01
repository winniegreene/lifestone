using Lifestone.Client.Diagnostics;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Autopilot;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;
using Xunit;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 7 F24, "refresh the buffs before they expire, even under attack" (soak round 3's death: a Tusker Guard was on the mage for the whole run, F23's lull never came, and Impenetrability, Bludgeon Bane and the masteries ran out). Two
/// thresholds: the SOFT margin (300 s, in a lull) and the HARD margin (120 s, between the fight's casts whatever is in reach); the order protections, regenerations, masteries and attributes, wand; an expiry counted. Also the vitals guard's
/// magic-mode entry (the corpse trip's refused-cast flood). Over the scripted caster; nothing here needs a server.
/// </summary>
public sealed class FightF24Tests(ITestOutputHelper output)
{
    private void Dump(FightManaTests.Rig r)
    {
        foreach (var line in r.Log.Lines.Where(l => l.Contains("refresh", StringComparison.Ordinal) || l.Contains("guard", StringComparison.Ordinal) || l.Contains("buff", StringComparison.Ordinal)).Take(60))
        {
            output.WriteLine(line);
        }
    }

    private static FightManaTests.Rig Rig(Func<ProfileLine, double> left, BuffOptions? buff = null, bool tusker = true, int healthPercent = 100)
    {
        var r = new FightManaTests.Rig(mana: 400, stamina: 200, knowsDrain: true) { HitsToKill = 400 };
        r.Caster.ManaRegenPerSecond = 40;
        r.Caster.HealthPerCast = 20;
        if (buff is not null)
        {
            r.Buff = buff;
        }

        if (tusker)
        {
            r.AddTusker(12); // inside the 20 m reach, alive for the whole test
        }

        Prime(r, left);

        r.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max);
        r.Caster.SetHealth(max * (uint)healthPercent / 100);
        r.R.Tick();
        return r;
    }

    /// <summary>Gives the rig's mage every buff line with the time <paramref name="left"/> names left (the armour memory seeded, both item lines on every worn piece): only the lines a test names are due.</summary>
    internal static void Prime(FightManaTests.Rig r, Func<ProfileLine, double> left)
    {
        foreach (var line in ArenaProfile.Buffs.BuffOrder.Where(l => l.IsBuff && l.Role != ProfileRole.ItemBuffArmor))
        {
            Enchant(r, line, left(line));
        }

        // the armour memory is seeded and every worn piece holds both item lines with a long time left: only the lines the test names are due
        var ledger = ArmourBuffLedger.For(r.R.H.State);
        ledger.MarkSeeded();
        var equipped = r.R.H.State.Self.Inventory.Equipped;
        foreach (var item in ArenaProfile.Buffs.BuffOrder.Where(l => l.Role == ProfileRole.ItemBuffArmor))
        {
            for (var i = 0; i < equipped.Count; i++)
            {
                ledger.Record(equipped[i].Guid, item.Name, item.Spells[0].Id, r.Now, left(item));
            }
        }

        r.R.Tick();
    }

    /// <summary>A buff of the line with <paramref name="seconds"/> left, as ACE's 0x02C2 carries it (the line's first level; the cycle reads a line by any of its spells).</summary>
    private static void Enchant(FightManaTests.Rig r, ProfileLine line, double seconds)
    {
        var info = r.Caster.Table[line.Spells[0].Id];
        var mod = BuffStatMods.BySpell.TryGetValue(info.Id, out var known) ? known : new BuffStatMod(info.Id, 0x9001, 1, 0.0);
        r.R.Server((p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, new AceEnchantment((ushort)info.Id, 1, (ushort)info.Category, 0, info.Power, 0.0, seconds, WorldActionRig.Player, 0f, 0f, 0.0, mod.Type, mod.Key, (float)mod.Value, 0)));
    }

    private static void Hit(FightManaTests.Rig r, uint damage = 5) =>
        r.R.Server((p, s) => AceM3Writers.DefenderNotification(p, s, "Tusker Slave", 4, 0.1, damage, 4, false, 0));

    private static string? LineOf(uint spell) => ArenaProfile.Lines.FirstOrDefault(l => l.IsBuff && l.Spells.Any(s => s.Id == spell))?.Name;

    private static List<string> BuffCasts(FightManaTests.Rig r) => [.. r.Caster.Answered.Select(a => LineOf(a.Spell)).OfType<string>()];

    private static FightScenario Armed(FightManaTests.Rig r)
    {
        var scenario = r.Scenario();
        scenario.RequestBuffRefresh(300, 20, "a line under the 300 s margin"); // the autopilot's ask: it arms the hard refresh as well
        return scenario;
    }

    [Fact]
    public void Under_constant_hits_a_line_under_the_hard_floor_is_recast_before_the_next_attack()
    {
        var r = Rig(l => l.Name == "Quickness Self" ? 29 : 1500); // M5 7 F25: the hard floor is 30 s now (was F24's 120 s margin)
        var scenario = Armed(r);
        for (var i = 0; i < 3; i++)
        {
            Hit(r);
        }

        r.Run(scenario, 60, 0.1, () => BuffCasts(r).Contains("Quickness Self"));
        Dump(r);
        Assert.Equal(["Quickness Self"], BuffCasts(r));
        Assert.Contains(r.Log.Lines, l => l.Contains("buff refresh: hard: Quickness Self has 2", StringComparison.Ordinal) && l.Contains("under the 30 s hard floor", StringComparison.Ordinal));
        var firstBolt = r.Caster.Answered.ToList().FindIndex(a => FightManaTests.Rig.IsBolt(a.Spell));
        var recast = r.Caster.Answered.ToList().FindIndex(a => LineOf(a.Spell) == "Quickness Self");
        Assert.True(firstBolt < 0 || recast < firstBolt, "the due line was cast instead of the next attack");
        Assert.Null(scenario.StopReason);
    }

    [Fact]
    public void At_200_s_left_with_a_tusker_in_reach_the_soft_margin_waits_for_a_lull()
    {
        var r = Rig(l => l.Name == "Quickness Self" ? 200 : 1500);
        var scenario = Armed(r);
        r.Run(scenario, 20, 0.1);
        Assert.Empty(BuffCasts(r));
        Assert.Contains(r.Log.Lines, l => l.Contains("buff refresh: waits: Tusker Slave 0x", StringComparison.Ordinal));
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("buff refresh: hard:", StringComparison.Ordinal));
        Assert.True(scenario.BuffRefreshAsked);
        Assert.True(r.BoltAnswers.Any(), "the fight went on casting at the tusker");
    }

    [Fact]
    public void The_emergency_heal_preempts_a_hard_refresh()
    {
        var r = Rig(l => l.Name == "Quickness Self" ? 25 : 1500, healthPercent: 30); // M5 7 F25: under the 30 s hard floor
        var scenario = Armed(r);
        r.Run(scenario, 120, 0.1, () => BuffCasts(r).Contains("Quickness Self"));
        var answered = r.Caster.Answered.ToList();
        Dump(r);
        var heal = answered.FindIndex(a => VitalsMeans.HealSelf.Contains(a.Spell) || DrainPicker.Ids.Contains(a.Spell));
        var recast = answered.FindIndex(a => LineOf(a.Spell) == "Quickness Self");
        Assert.True(heal >= 0, "the emergency healed");
        Assert.True(recast > heal, "the refresh came after the heal, not before it");
    }

    [Fact]
    public void The_protections_come_before_the_regenerations_and_the_masteries_and_the_soonest_first_within_a_group()
    {
        var r = Rig(l => l.Name switch
        {
            "War Magic Mastery Self" => 50, // the soonest of all, but a mastery
            "Regeneration Self" => 60,
            "Invulnerability Self" => 70,
            "Magic Resistance Self" => 100,
            _ => 1500,
        }, new BuffOptions { HardMarginSeconds = 120 }); // M5 7 F25: F24's 120 s margin, so the four are due under fire and the order is what is proved
        var scenario = Armed(r);
        r.Run(scenario, 120, 0.1, () => BuffCasts(r).Distinct().Count() >= 4);
        Dump(r);
        Assert.Equal(["Invulnerability Self", "Magic Resistance Self", "Regeneration Self", "War Magic Mastery Self"], BuffCasts(r).Distinct().ToList());
        Assert.Equal(RefreshGroup.Protection, ArenaProfile.RefreshGroupOf(ArenaProfile.LineNamed("Impenetrability")!));
        Assert.Equal(RefreshGroup.Protection, ArenaProfile.RefreshGroupOf(ArenaProfile.LineNamed("Bludgeon Bane")!));
        Assert.Equal(RefreshGroup.Protection, ArenaProfile.RefreshGroupOf(ArenaProfile.LineNamed("Armor Self")!));
        Assert.Equal(RefreshGroup.Regeneration, ArenaProfile.RefreshGroupOf(ArenaProfile.LineNamed("Mana Renewal Self")!));
        Assert.Equal(RefreshGroup.Attribute, ArenaProfile.RefreshGroupOf(ArenaProfile.LineNamed("Item Enchantment Mastery Self")!));
        Assert.Equal(RefreshGroup.Wand, ArenaProfile.RefreshGroupOf(ArenaProfile.LineNamed("Defender")!));
    }

    [Fact]
    public void A_line_that_expires_anyway_is_logged_once_at_error_level_and_counted()
    {
        var r = Rig(l => l.Name == "Quickness Self" ? 4 : 1500, new BuffOptions { HardMarginSeconds = 0 }); // the hard refresh off: the soft one waits for a lull that never comes
        var scenario = Armed(r);
        r.Run(scenario, 12, 0.1);
        Dump(r);
        Assert.Equal(1, scenario.BuffsExpired);
        var errors = r.Log.Entries.Where(e => e.Severity == LogSeverity.Error && e.Line.Contains("buff refresh: Quickness Self EXPIRED under fire: ", StringComparison.Ordinal)).ToList();
        Assert.Single(errors);
        r.Run(scenario, 5, 0.1);
        Assert.Equal(1, scenario.BuffsExpired);
        Assert.Contains(" buffs_expired=1", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_hard_margin_is_a_registered_option_defaulting_to_30_s()
    {
        Assert.Equal(30, new BuffOptions().HardMarginSeconds); // M5 7 F25 (D93): lowered from 120 s, the emergency case's floor
        Assert.Equal(90, BuffOptions.FromArguments(CommandLineArgument.ParseAll(["--buff-hard-margin-s=90"])).HardMarginSeconds);
        Assert.NotEmpty(BuffOptions.FromArguments(CommandLineArgument.ParseAll(["--buff-hard-margin-s=-1"])).Errors);
        Assert.Contains(OptionCatalog.Buff().Options, x => x.Name == "--buff-hard-margin-s");
    }

    [Fact]
    public void The_guard_enters_the_magic_mode_first_asks_once_and_casts_once_the_mode_has_landed()
    {
        var rig = new FightManaTests.Rig(mana: 400, stamina: 200, knowsDrain: true);
        AutopilotF30Tests.Settle(rig); // M5 7 F30: the guard waits for the login to settle
        rig.Caster.HealthPerCast = 20;
        rig.R.CombatModeIs(CombatModes.NonCombat);
        rig.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max);
        rig.Caster.SetHealth(max * 30 / 100);
        rig.R.Tick();
        var ground = rig.Fight.GroundProfile;
        using var watch = new HostileWatch(rig.View, ground, 20);
        var guard = new AutopilotVitalsGuard(rig.Caster.Table, ground, new AutopilotOptions(), 0.9, watch, rig.Log);
        for (var i = 0; i < 400 && !rig.Log.Lines.Any(l => l.Contains("guard: trip: entering the magic mode for ", StringComparison.Ordinal)); i++)
        {
            guard.Step(rig.View, rig.Now, "trip");
            rig.R.Tick(0.1);
            rig.Caster.Pump();
            rig.R.Tick();
        }

        for (var i = 0; i < 5; i++)
        {
            Assert.True(guard.Step(rig.View, rig.Now, "trip")); // five more steps in one frame
        }

        Dump(rig);

        Assert.True(guard.Casts <= 1, "five steps in one frame start at most one cast, never a flood"); // the mode was asked first; the cast follows once it has landed
        Assert.Single(rig.Log.Lines, l => l.Contains("guard: trip: entering the magic mode for ", StringComparison.Ordinal));
        var end = rig.Now + 30;
        while (rig.Now < end && !rig.Caster.Answered.Any(a => VitalsMeans.HealSelf.Contains(a.Spell)))
        {
            rig.R.Tick(0.1);
            rig.Caster.Pump();
            rig.R.Tick();
            guard.Step(rig.View, rig.Now, "trip");
        }

        Assert.Contains(rig.Caster.Answered, a => VitalsMeans.HealSelf.Contains(a.Spell));
        Assert.Equal(CombatModes.Magic, rig.R.H.State.Self.Qualities.CombatMode);
        Assert.Single(rig.Log.Lines, l => l.Contains("guard: trip: entering the magic mode for ", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Log.Lines, l => l.Contains("not in the magic combat mode", StringComparison.Ordinal) && l.Contains("did not land", StringComparison.Ordinal));
    }
}
