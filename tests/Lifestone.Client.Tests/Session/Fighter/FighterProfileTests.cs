using DatReaderWriter.Enums;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Magic;
using Lifestone.Data.Motion;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>The fighters' spell profile as data (M4 P5a lane C1, <see cref="FighterProfile"/>): its lines, roles and ids, with the contract the buff cycle, the keeper and the harm-heal station read.</summary>
public sealed class FighterProfileTests
{
    private static readonly string[] SharedBuffs =
    [
        "Strength Self", "Endurance Self", "Coordination Self", "Quickness Self", "Focus Self", "Willpower Self",
        "Invulnerability Self", "Impregnability Self", "Armor Self", "Regeneration Self", "Rejuvenation Self",
    ];

    [Theory]
    [InlineData("Holtburg Brawler", "Heavy Weapon Mastery Self")]
    [InlineData("Holtburg Archer", "Missile Weapon Mastery Self")]
    public void A_fighter_has_twelve_buff_lines_the_weapon_mastery_among_them_and_five_utility_lines_M4_P5a_C1c(string name, string mastery)
    {
        var profile = FighterProfile.ForName(name)!;
        Assert.Equal(name, profile.CharacterName);
        Assert.Equal(18, profile.Lines.Count); // M4 P5b J3b: Lifestone Tie
        Assert.Equal(12, profile.Lines.Count(l => l.IsBuff));
        Assert.Equal(SharedBuffs.Append(mastery).Order(), profile.Lines.Where(l => l.IsBuff).Select(l => l.Name).Order());
        Assert.Equal(mastery, profile.WeaponMastery.Name);
        Assert.Equal(["Stamina to Mana Self", "Revitalize Self", "Harm Self", "Heal Self", "Lifestone Recall", "Lifestone Tie"], profile.UtilityLines.Select(l => l.Name));
        Assert.Equal(1635u, profile.LifestoneRecall.Spells.Single().Id); // M4 P5a C1c (D38): the fighters learn Lifestone Recall
        Assert.Equal(ProfileRole.Travel, profile.UtilityLine("Lifestone Recall")!.Role);
        Assert.Contains(1635u, profile.SpellsToLearn);
        Assert.All(profile.UtilityLines, l => Assert.False(l.IsBuff));
        Assert.Equal(["Stamina to Mana Self", "Revitalize Self"], profile.Lines.Where(l => l.IsManaLine).Select(l => l.Name));
        Assert.All(profile.Lines, l => Assert.Equal(Enumerable.Range(1, l.Spells.Count), l.Spells.Select(s => s.Position)));
        Assert.Equal(profile.Lines.SelectMany(l => l.Spells).Select(s => s.Id), profile.SpellsToLearn);
        Assert.Equal(profile.SpellsToLearn.Count, profile.SpellsToLearn.Distinct().Count());
    }

    [Fact]
    public void The_roles_and_schools_are_creature_buffs_life_buffs_the_two_mana_lines_and_harm_and_heal_M4_P5a()
    {
        foreach (var p in FighterProfile.All)
        {
            Assert.Equal(9, p.Lines.Count(l => l.Role == ProfileRole.CreatureBuff)); // six attributes, the weapon mastery, Invulnerability, Impregnability
            Assert.Equal(["Armor Self", "Regeneration Self", "Rejuvenation Self"], p.Lines.Where(l => l.Role == ProfileRole.LifeBuff).Select(l => l.Name));
            Assert.Equal(["Stamina to Mana Self"], p.Lines.Where(l => l.Role == ProfileRole.ManaTransfer).Select(l => l.Name));
            Assert.Equal(["Revitalize Self"], p.Lines.Where(l => l.Role == ProfileRole.StaminaRestore).Select(l => l.Name));
            Assert.Equal(["Harm Self"], p.Lines.Where(l => l.Role == ProfileRole.HarmSelf).Select(l => l.Name));
            Assert.Equal(["Heal Self"], p.Lines.Where(l => l.Role == ProfileRole.HealSelf).Select(l => l.Name));
            Assert.All(p.Lines.Where(l => l.Role is ProfileRole.CreatureBuff or ProfileRole.LifeBuff), l => Assert.True(l.IsBuff));
            Assert.Equal(["Lifestone Recall", "Lifestone Tie"], p.Lines.Where(l => l.Role == ProfileRole.Travel).Select(l => l.Name));
            Assert.DoesNotContain(p.Lines, l => l.Role is ProfileRole.ItemBuffArmor or ProfileRole.ItemBuffWand or ProfileRole.WarBolt or ProfileRole.WarOption); // no Item Enchantment buff and no War Magic: Lifestone Recall is a Travel line
        }
    }

    [Fact]
    public void The_cycles_order_is_Focus_and_Willpower_then_the_mastery_then_the_rest_in_profile_order_M4_P5a()
    {
        Assert.Equal(
            ["Focus Self", "Willpower Self", "Heavy Weapon Mastery Self", "Strength Self", "Endurance Self", "Coordination Self", "Quickness Self", "Invulnerability Self", "Impregnability Self", "Armor Self", "Regeneration Self", "Rejuvenation Self"],
            FighterProfile.Brawler.BuffOrder.Select(l => l.Name));
        Assert.Equal("Missile Weapon Mastery Self", FighterProfile.Archer.BuffOrder[2].Name);
        // The mage's order is unchanged by the abstraction: Focus, Willpower, Creature Enchantment Mastery first, then the other masteries.
        Assert.Equal(["Focus Self", "Willpower Self", "Creature Enchantment Mastery Self", "War Magic Mastery Self"], ArenaProfile.Buffs.BuffOrder.Take(4).Select(l => l.Name));
        Assert.Equal(BuffCycle.OrderedLines, ArenaProfile.Buffs.BuffOrder);
    }

    [Fact]
    public void The_utility_spells_are_reachable_by_line_name_with_the_ids_the_keeper_and_the_harm_heal_station_use_M4_P5a()
    {
        foreach (var p in FighterProfile.All)
        {
            Assert.Equal([8u, 1167u, 1168u, 1169u, 1170u, 1171u, 2071u, 4309u], p.UtilityLine("Harm Self")!.Spells.Select(s => s.Id));
            Assert.Equal([6u, 1157u, 1158u, 1159u, 1160u, 1161u, 2073u, 4311u], p.UtilityLine("Heal Self")!.Spells.Select(s => s.Id));
            Assert.Equal([1676u, 1677u, 1678u, 1679u, 1680u, 1681u, 2345u, 4981u], p.UtilityLine("Stamina to Mana Self")!.Spells.Select(s => s.Id));
            Assert.Equal([1177u, 1178u, 1179u, 1180u, 1181u, 1182u, 2083u, 4321u], p.UtilityLine("Revitalize Self")!.Spells.Select(s => s.Id));
            Assert.Same(p.HarmSelf, p.LineNamed("Harm Self"));
            Assert.Same(p.StaminaToMana, p.ManaTransferLine);
            Assert.Same(p.Revitalize, p.StaminaRestoreLine);
            Assert.Equal(ProfileRole.HarmSelf, p.HarmSelf.Role);
            Assert.Equal(ProfileRole.HealSelf, p.HealSelf.Role);
            Assert.Null(p.UtilityLine("Strength Self"));
        }
    }

    [Fact]
    public void The_pinned_ids_of_the_new_lines_are_the_dats_first_family_and_the_Hunter_has_no_profile_M4_P5a()
    {
        Assert.Equal([418u, 419u, 420u, 421u, 422u, 423u, 2309u, 4624u], FighterProfile.Brawler.WeaponMastery.Spells.Select(s => s.Id));
        Assert.Equal([467u, 468u, 469u, 470u, 471u, 472u, 2207u, 4522u], FighterProfile.Archer.WeaponMastery.Spells.Select(s => s.Id));
        Assert.Equal(420u, FighterProfile.Brawler.WeaponMastery.Spells[2].Id); // level III
        Assert.Equal(421u, FighterProfile.Brawler.WeaponMastery.Spells[3].Id); // level IV
        Assert.Null(FighterProfile.ForName("Holtburg Hunter"));
        Assert.Null(FighterProfile.For(FighterTemplate.Hunter));
        Assert.Same(FighterProfile.Brawler, FighterProfile.ForName(" holtburg brawler "));
    }
}

/// <summary>The fighters' profile against the SpellTable, as <c>ArenaProfileTests.Every_profile_spell_equals_the_spell_table_entry</c> pins the mage's: id, name, power, duration, school and type of every spell.</summary>
[Trait("Tier", "Exhaustive")]
public sealed class FighterProfileDatTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [RequiresDatsTheory]
    [InlineData("Holtburg Brawler")]
    [InlineData("Holtburg Archer")]
    public void Every_fighter_profile_spell_equals_the_spell_table_entry(string name)
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var profile = FighterProfile.ForName(name)!;
        foreach (var line in profile.Lines)
        {
            foreach (var s in line.Spells)
            {
                Assert.True(spells.TryGetValue(s.Id, out var dat), $"{line.Name}: id {s.Id} is not in the SpellTable");
                Assert.True(dat.Name == s.Name, $"{line.Name}: id {s.Id} is '{dat.Name}', profile '{s.Name}'");
                Assert.True(dat.Power == s.Power, $"{s.Name} ({s.Id}): Power {dat.Power}, profile {s.Power}");
                Assert.True(dat.Duration == s.DurationSeconds, $"{s.Name} ({s.Id}): Duration {dat.Duration}, profile {s.DurationSeconds}");
                Assert.True(dat.School == line.School, $"{s.Name} ({s.Id}): school {dat.School}, line {line.School}");
                var expectedType = line.Role switch
                {
                    ProfileRole.ManaTransfer => SpellType.Transfer,
                    ProfileRole.StaminaRestore or ProfileRole.HarmSelf or ProfileRole.HealSelf => SpellType.Boost,
                    ProfileRole.Travel => line.Name == FighterProfile.LifestoneTieLineName ? SpellType.PortalLink : SpellType.PortalRecall,
                    _ => SpellType.Enchantment,
                };
                Assert.True(dat.MetaSpellType == expectedType, $"{s.Name} ({s.Id}): type {dat.MetaSpellType}, line {expectedType}");
                Assert.True(line.Role == ProfileRole.Travel || (dat.Flags & SpellFlags.SelfTargeted) != 0, $"{s.Name} ({s.Id}) is not self-targeted");
            }

            // Within a line the dat's category is one (the stacking family).
            Assert.Single(line.Spells.Select(s => spells[s.Id].Category).Distinct());
        }
    }

    [RequiresDatsFact]
    public void The_three_missile_mastery_level_VII_rows_are_one_family_and_the_profile_pins_the_first_M4_P5a()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        foreach (var id in new uint[] { 2207, 2219, 2313 })
        {
            Assert.Equal("Missile Weapon Mastery Self VII", spells[id].Name);
            Assert.Equal(300u, spells[id].Power);
            Assert.Equal(spells[472].Category, spells[id].Category);
        }

        Assert.Equal(2207u, FighterProfile.Archer.WeaponMastery.Spells[6].Id);
    }

    [RequiresDatsFact]
    public void The_mage_shares_its_attribute_defence_and_life_lines_with_the_fighters_and_the_costs_fit_their_mana_M4_P5a()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        // Level IV costs 50 mana (Heal Self and Harm Self IV too), level III 40, the transfer 40: the fighters' pool (Self 116 at the build) casts two IV spells before a transfer.
        foreach (var line in FighterProfile.Brawler.Lines.Where(l => l.Spells.Count > 1))
        {
            Assert.Equal(line.Name == "Stamina to Mana Self" ? 40u : 50u, spells[line.Spells[3].Id].BaseMana);
        }
    }
}
