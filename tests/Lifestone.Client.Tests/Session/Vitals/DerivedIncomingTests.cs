using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// M6 14 the derived incoming (live ladder57 2026-11-17 10:42:05 UTC, the Crater Caves: five Nubilous Golems derived at 21.7 a second, 4.3 each, against fight-sim's ACE at 0.8 to 1.5 a golem and her measured 3.6 for the pack): the creature
/// table's derivation counted every strike and every spell as landing. ACE rolls both against her (SkillCheck.GetSkillChance at 0.03, SkillCheck.cs:7-12 @95ef426d): a spell lands when the creature's skill in its school beats her Magic
/// Defense (TryResistSpell, WorldObject_Magic.cs:104-168), a strike when its attack skill beats her Melee Defense (DamageEvent.GetEvadeChance, DamageEvent.cs:386-397). The skills come from the world database (creature-skills.json) and the dats'
/// SkillTable; fight-sim (tools/fight-sim.ps1 matchup 23082 and 7126 --explain, on her sheet as last saved on 2026-11-17: Melee Defense 112, Missile Defense 85, Magic Defense 142) prints ACE's own working, the numbers pinned here.
/// </summary>
public sealed class DerivedIncomingTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint Golem = 23082, Wisp = 7126;

    private static readonly DefenceRead Sheet = new(112, 85, 142); // fight-sim's "her ... Melee Defense 112, Missile Defense 85, Magic Defense 142"

    [Fact]
    public void The_world_database_s_skill_rows_and_ACE_s_weapon_skill_choice()
    {
        Assert.True(CreatureSkills.Count > 5000);
        Assert.True(CreatureSkills.TryRow(Golem, CreatureSkills.WarMagic, out var init, out var ranks, out var sac));
        Assert.Equal((50, 0, 3), (init, ranks, sac)); // weenie_properties_skill: War Magic 50, specialized
        Assert.False(CreatureSkills.TryRow(Golem, CreatureSkills.UnarmedCombat, out _, out _, out _)); // no Unarmed Combat row: ACE converts it
        Assert.Equal(CreatureSkills.LightWeapons, CreatureSkills.AttackSkillId(Golem, 0, ranged: false)); // GetCurrentWeaponSkill: no weapon, Unarmed with init 0, so Light Weapons
        Assert.Equal(CreatureSkills.LightWeapons, CreatureSkills.AttackSkillId(Wisp, 0, ranged: false));
        Assert.Equal(CreatureSkills.MissileWeapons, CreatureSkills.AttackSkillId(Golem, 999_999_999, ranged: true)); // a ranged weapon with no row of its skill: Missile Weapons

        // ACE's GetSkillChance at 0.03: the golem's War Magic 125 against her 142 lands 0.375; its attack 183 against her 112 is evaded 0.106 (fight-sim: "evade chance 0.106")
        Assert.Equal(0.375, CreatureSkills.Chance(125, 142), 3);
        Assert.Equal(0.106, 1 - CreatureSkills.Chance(183, 112), 3);
        Assert.Equal(0.869, CreatureSkills.Chance(205, 142), 3); // the wisp's War Magic 205: fight-sim's Frost Bolt IV 81 %, Lightning Bolt IV 85 %
        Assert.Equal(0.012, 1 - CreatureSkills.Chance(260, 112), 3); // fight-sim: "attack skill 260 vs her defence 112: evade chance 0.012"
    }

    [RequiresDatsFact]
    public void The_golem_and_the_wisp_derive_fight_sim_s_skills_and_landing_shares_and_their_rate_on_her_sheet()
    {
        var times = CreatureAttackTimes.For(fixture.Dats);
        var golem = times.Of(Golem, "Nubilous Golem");
        var wisp = times.Of(Wisp, "Cursed Wisp");
        output.WriteLine(golem.Why);
        output.WriteLine(wisp.Why);

        // the skills, as fight-sim's working names them: the golem's attack skill 183 (Light Weapons 100 + (Strength 160 + Coordination 90) / 3), War Magic 125 (50 + (Focus 150 + Self 150) / 4); the wisp's 260 and 205
        Assert.Equal(183u, golem.AttackSkill);
        Assert.Equal(260u, wisp.AttackSkill);
        Assert.All(golem.Spells.Where(s => s.MeanDamage > 0), s => Assert.Equal(125u, s.CasterSkill));
        Assert.All(wisp.Spells.Where(s => s.MeanDamage > 0), s => Assert.Equal(205u, s.CasterSkill));
        Assert.Equal(0.894, golem.StrikesLand(Sheet.Melee), 3); // fight-sim: 10.5 % evaded or void
        Assert.Equal(0.375, golem.Spells.First(s => s.Name == "Lightning Bolt IV").LandsOn(Sheet.Magic), 3); // fight-sim: 35 % (40 % on the tactics run's sheet)

        // the rate on her sheet, each term at fight-sim's mean landed hit (its armour per body part and her protections: a golem's strike 5.2, a Lightning Bolt IV 24.2, a Shock Wave IV 28.2; a wisp's Frost Bolt IV 28.7, Lightning Bolt
        // IV 25.9, its melee 1.4): our attack rates and ACE's landing shares against fight-sim's totals, 1.53 a second a golem and 1.43 a wisp (of which its Harm Other IV's 0.17 is unpriced here, TODO(m6-creature-life-spells))
        var golemRate = OnSheet(golem, 5.2, new() { ["Lightning Bolt IV"] = 24.2, ["Shock Wave IV"] = 28.2 }, landed: true);
        var wispRate = OnSheet(wisp, 1.4, new() { ["Frost Bolt IV"] = 28.7, ["Lightning Bolt IV"] = 25.9 }, landed: true);
        output.WriteLine($"golem {golemRate:0.###}/s (fight-sim 1.53), wisp {wispRate:0.###}/s (fight-sim 1.43 over a minute, less Harm Other IV's 0.17 = 1.26; 3.4 in a fight's opening)");
        Assert.InRange(golemRate, 1.53 * 0.8, 1.53 * 1.2); // 1.46: within 5 %

        // a wisp's spells thin out as its own mana runs dry (ACE's Monster_Magic.UseMana; fight-sim: 3.4 a second in the opening, 1.43 over a minute): the derivation times them with its mana never running out, the opening's rate
        // (TODO(m6-creature-mana)), so it lies within that spread: over the minute's mean of the terms it prices and under the opening's
        Assert.InRange(wispRate, 1.26, 3.4);
        Assert.Contains(AttackTimings.ManaTodo, wisp.Todo, StringComparison.Ordinal);

        // before the fix every strike and spell landed: the golem's spells at 1 / 0.375 of ACE's, its strikes at 1 / 0.894
        var golemWhole = OnSheet(golem, 5.2, new() { ["Lightning Bolt IV"] = 24.2, ["Shock Wave IV"] = 28.2 }, landed: false);
        Assert.True(golemWhole > golemRate * 1.15, $"{golemWhole} against {golemRate}");
    }

    /// <summary>
    /// The race of 10:42:05.349 UTC replayed on her sheet (live: "the race is lost at health 162/199: her health 162 falls 2.2/s; the heal she can sustain, 11.9/s, is under the incoming 21.7/s (her hits measure 3.6/s over the last 10 s)
    /// ('Nubilous Golem' ... 1 hit(s) for 36 over 2.4 s ..., derived 21.7/s while new (5 x ...)); 5 attacker(s) with 1235 health left at her 30.6 damage a second: killed in 40.4 s"). The five golems priced by the dats' derivation
    /// through her sheet's factors (fight-sim's working: her armour against bludgeon about 0.145 by body part and the bot's protection 0.35, so 0.0507 a strike; the bot's lightning protection 0.35): every strike and spell landing, as the
    /// guard read them, the incoming is over her heal and the race is lost; with ACE's rolls on her sheet's defences it holds. Live her bot buffs had lapsed at 10:40:43 to 10:41:18 (the golems' derived rate rose from 1.77 a golem at
    /// 10:31:51 to 4.33 at 10:41:24), which the sheet does not show: the replay is her buffed state.
    /// </summary>
    [RequiresDatsFact]
    public void The_1042_five_golems_with_ACE_s_rolls_hold_against_her_heal_on_her_sheet()
    {
        var golem = CreatureAttackTimes.For(fixture.Dats).Of(Golem, "Nubilous Golem");
        var meanHit = FightLines.MeanHit(CreatureFacts.All.Find(Golem)!.Hitter);
        const double Bludgeon = 0.145 * 0.35, Lightning = 0.35;
        var whole = golem.PerSecondOn(meanHit, Bludgeon, _ => Lightning, null); // every strike and spell lands
        var rolled = golem.PerSecondOn(meanHit, Bludgeon, _ => Lightning, Sheet);
        output.WriteLine($"a golem on her sheet: {whole:0.###}/s every strike and spell landing, {rolled:0.###}/s with ACE's rolls (fight-sim 1.53)");
        Assert.InRange(rolled, 1.53 * 0.8, 1.53 * 1.2);

        // the guard's read of five new golems heard 2.4 s: the window's 36 over 10 s and the derived rate for the 7.6 s not heard, at least the derived (race2's rule)
        static double Incoming(double each) => Math.Max(5 * each, 3.6 + (5 * each * (1 - (2.4 / RecentHits.AverageSeconds))));
        LosingRaceInput At(double incoming) => new(162, incoming, 36, 11.9, 2.4, RecallWayOut.Seconds) { TrendPerSecond = -2.2, IncomingAllPerSecond = 3.6, Attackers = 5, AttackersHealth = 1235, KillPerSecond = 30.6 };
        var before = LosingRace.Decide(At(Incoming(whole)));
        var after = LosingRace.Decide(At(Incoming(rolled)));
        output.WriteLine($"incoming {Incoming(whole):0.#}/s: {LosingRace.Verdict(before)}; with the rolls {Incoming(rolled):0.#}/s: {LosingRace.Verdict(after)}");
        Assert.True(before.Lost); // every strike and spell landing: over the heal's 11.9, and five golems are not killed first
        Assert.False(after.Lost || after.Losing);
        Assert.Equal("holding", LosingRace.Verdict(after)); // falling 2.2 a second, the heal holds: she heals and stays
    }

    /// <summary>The creature's damage a second on her sheet: its strikes a second (landing at ACE's share when <paramref name="landed"/>) at <paramref name="strike"/>, and each priced spell's casts a second (landing likewise) at fight-sim's mean on her.</summary>
    private static double OnSheet(AttackTiming t, double strike, Dictionary<string, double> spellMeans, bool landed)
    {
        var rate = t.HitsPerSecond * (landed ? t.StrikesLand(Sheet.Melee) : 1) * strike;
        foreach (var s in t.Spells.Where(s => s.MeanDamage > 0))
        {
            Assert.True(spellMeans.TryGetValue(s.Name, out var mean), $"{s.Name} has no fight-sim mean pinned");
            rate += s.Share / t.Seconds * (landed ? s.LandsOn(Sheet.Magic) : 1) * mean;
        }

        return rate;
    }
}
