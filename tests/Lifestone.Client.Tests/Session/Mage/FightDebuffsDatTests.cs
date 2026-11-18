using DatReaderWriter.Enums;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// The debuff and drain table of <see cref="FightDebuffs"/> against the retail dats (M5 item 7 F11, D90): every id's name, school, base mana, power and duration are the SpellTable's, the meta-spell type is the kind's, and the
/// three skill formulas the value model divides by (Light Weapons, Magic Defense, Melee Defense) are the SkillTable's. The effect numbers are ACE's world database and not in the dat, so they are pinned in
/// <see cref="FightDebuffs.Spells"/> and read back here only for their shape.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class FightDebuffsDatTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [RequiresDatsFact]
    public void Every_row_is_the_dats_spell_in_name_school_mana_power_duration_and_meta_type()
    {
        var table = SpellTableData.Load(fixture.Dats).Spells;
        foreach (var row in FightDebuffs.Spells)
        {
            var dat = table[row.Id];
            Assert.Equal(row.Name, dat.Name);
            Assert.Equal(row.School == DebuffSchool.LifeMagic ? MagicSchool.LifeMagic : MagicSchool.CreatureEnchantment, dat.School);
            Assert.Equal((row.BaseMana, row.Power, row.DurationSeconds), (dat.BaseMana, dat.Power, dat.Duration));
            Assert.True(dat.Flags.HasFlag(SpellFlags.Resistable), row.Name);
            var meta = row.Kind switch
            {
                DebuffKind.HarmOther => SpellType.Boost,
                DebuffKind.DrainHealthOther or DebuffKind.DrainStaminaOther => SpellType.Transfer,
                _ => SpellType.Enchantment,
            };
            Assert.Equal(meta, dat.MetaSpellType);
        }
    }

    [RequiresDatsFact]
    public void Level_VI_is_the_dats_line_and_level_VII_is_a_title_of_power_325_in_the_same_category()
    {
        var table = SpellTableData.Load(fixture.Dats).Spells;
        foreach (var kind in Enum.GetValues<DebuffKind>())
        {
            var six = FightDebuffs.Find(kind, 6)!.Value;
            var seven = FightDebuffs.Find(kind, 7)!.Value;
            Assert.EndsWith(" VI", six.Name, StringComparison.Ordinal);
            Assert.Equal(325u, seven.Power);
            Assert.True(seven.Power > six.Power);
            Assert.True(seven.Level > six.Level);
            if (kind is not (DebuffKind.LightWeaponsIneptitude)) // the dat has five Light Weapon Ineptitude Other VI/VII spells for the quest items; the first of each is the plain one
            {
                Assert.Equal(table[six.Id].Category, table[seven.Id].Category);
            }
        }

        Assert.Equal(1108u, FightDebuffs.Find(DebuffKind.FireVulnerability, 6)!.Value.Id);
        Assert.Equal(MageSpellBars.FireVulnerabilityOtherVii, FightDebuffs.Find(DebuffKind.FireVulnerability, 7)!.Value.Id);
    }

    [RequiresDatsFact]
    public void The_skill_formulas_the_model_divides_by_are_the_dats()
    {
        var skills = SkillTableData.Load(fixture.Dats);
        const uint strength = 1, quickness = 3, coordination = 4, focus = 5, self = 6;

        var lightWeapons = skills.Skills.Single(s => s.Id == 45).Formula;
        Assert.Equal((0, 1, 1, (int)FightDebuffs.LightWeaponsDivisor), (lightWeapons.AdditiveBonus, lightWeapons.Attribute1Multiplier, lightWeapons.Attribute2Multiplier, lightWeapons.Divisor));
        Assert.Equal(new[] { strength, coordination }, new[] { lightWeapons.Attribute1, lightWeapons.Attribute2 }.Order());

        var magicDefense = skills.Skills.Single(s => s.Id == 15).Formula;
        Assert.Equal((0, 1, 1, (int)FightDebuffs.MagicDefenseDivisor), (magicDefense.AdditiveBonus, magicDefense.Attribute1Multiplier, magicDefense.Attribute2Multiplier, magicDefense.Divisor));
        Assert.Equal(new[] { focus, self }, new[] { magicDefense.Attribute1, magicDefense.Attribute2 }.Order());

        // The mage's Melee Defense (untrained, usable: min level 1) is (Quickness + Coordination) / 3.
        var meleeDefense = skills.Skills.Single(s => s.Id == 6);
        Assert.Equal(1u, meleeDefense.MinLevel);
        Assert.Equal(3, meleeDefense.Formula.Divisor);
        Assert.Equal(new[] { quickness, coordination }, new[] { meleeDefense.Formula.Attribute1, meleeDefense.Formula.Attribute2 }.Order());

        // The tusker Slave of the world SQL: Light Weapons 150 + (210 + 200) / 3, Magic Defense 200 + (70 + 60) / 7.
        Assert.True(lightWeapons.TryCalculate(210, 200, out var attack));
        Assert.Equal(137u, attack);
        Assert.True(magicDefense.TryCalculate(60, 70, out var defence));
        Assert.Equal(19u, defence);
    }

    [RequiresDatsFact]
    public void The_rows_below_VI_resolve_from_the_dats_SpellTable_with_no_gap_M6_1_f()
    {
        var table = SpellTableData.Load(fixture.Dats).Spells;
        var rows = DebuffRows.FromSpellTable(table, out var missing);
        Assert.Empty(missing);
        Assert.Equal(Enum.GetValues<DebuffKind>().Length * 7, rows.All.Count);
        Assert.Equal(ArenaProfile.ExtraNamed("Fire Vulnerability Other")!.Spells.Take(5).Select(s => s.Id), Enumerable.Range(1, 5).Select(l => rows.Find(DebuffKind.FireVulnerability, l)!.Value.Id));
        Assert.Equal(ArenaProfile.ExtraNamed("Drain Health Other")!.Spells.Take(5).Select(s => s.Id), Enumerable.Range(1, 5).Select(l => rows.Find(DebuffKind.DrainHealthOther, l)!.Value.Id));
        Assert.Equal(ArenaProfile.ExtraNamed("Drain Stamina Other")!.Spells.Take(5).Select(s => s.Id), Enumerable.Range(1, 5).Select(l => rows.Find(DebuffKind.DrainStaminaOther, l)!.Value.Id));
        Assert.Equal(304u, rows.Find(DebuffKind.LightWeaponsIneptitude, 1)!.Value.Id); // the plain line, the world SQL's first
        Assert.Equal(25u, rows.Find(DebuffKind.Imperil, 1)!.Value.Id);
        foreach (var row in rows.All.Where(r => r.Level <= 5))
        {
            var dat = table[row.Id];
            Assert.True(dat.Flags.HasFlag(SpellFlags.Resistable), row.Name);
            Assert.True(row.Power < FightDebuffs.Find(row.Kind, 6)!.Value.Power, row.Name);
            var meta = row.Kind switch
            {
                DebuffKind.HarmOther => SpellType.Boost,
                DebuffKind.DrainHealthOther or DebuffKind.DrainStaminaOther => SpellType.Transfer,
                _ => SpellType.Enchantment,
            };
            Assert.Equal(meta, dat.MetaSpellType);
        }
    }
}
