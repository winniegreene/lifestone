using DatReaderWriter.Enums;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>The BSD profile as data: its shape, and, with the dats, every id, name, power, duration and school against the SpellTable.</summary>
public sealed class ArenaProfileTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [Fact]
    public void The_profile_has_31_lines_247_spells_and_every_id_once_and_the_build_learns_twelve_more()
    {
        Assert.Equal(31, ArenaProfile.Lines.Count);
        Assert.Equal(247, ArenaProfile.Lines.Sum(l => l.Spells.Count));
        // The lines, the fight tab's Fire Vulnerability Other VII and Heal Self VII (M3 4.2 p1), and (M4 P5a, D33 and D36) Harm Self I-VIII and the three travel spells: Lifestone Recall, Primary Portal Tie, Primary Portal Recall.
        Assert.Equal(247 + 2 + 8 + 3, ArenaProfile.SpellsToLearn.Count);
        Assert.Equal(ArenaProfile.SpellsToLearn.Count, ArenaProfile.SpellsToLearn.Distinct().Count());
        Assert.Equal(ArenaProfile.SpellsToLearn, ArenaProfile.Buffs.SpellsToLearn);
        Assert.Equal(30, ArenaProfile.Lines.Count(l => l.Spells.Count == 8));
        Assert.Equal(7, ArenaProfile.LineNamed("Flame Blast")!.Spells.Count);
        Assert.Equal(ArenaProfile.Lines.SelectMany(l => l.Spells).Select(s => s.Id).Concat(ArenaProfile.FightExtras), ArenaProfile.SpellsToLearn);
        Assert.All(ArenaProfile.Lines, l => Assert.Equal(Enumerable.Range(1, l.Spells.Count), l.Spells.Select(s => s.Position)));
    }

    [Fact]
    public void The_lines_have_their_roles_and_the_oddities_stay_as_pinned()
    {
        Assert.Equal(15, ArenaProfile.LinesOf(ProfileRole.CreatureBuff).Count());
        Assert.Equal(5, ArenaProfile.LinesOf(ProfileRole.LifeBuff).Count());
        Assert.Equal(["Impenetrability", "Bludgeon Bane"], ArenaProfile.LinesOf(ProfileRole.ItemBuffArmor).Select(l => l.Name));
        Assert.Equal(["Hermetic Link", "Defender"], ArenaProfile.LinesOf(ProfileRole.ItemBuffWand).Select(l => l.Name));
        Assert.Equal(["Stamina to Mana Self"], ArenaProfile.LinesOf(ProfileRole.ManaTransfer).Select(l => l.Name));
        Assert.Equal(["Revitalize Self"], ArenaProfile.LinesOf(ProfileRole.StaminaRestore).Select(l => l.Name));
        Assert.Equal(["Flame Bolt"], ArenaProfile.LinesOf(ProfileRole.WarBolt).Select(l => l.Name));
        Assert.Equal(["Flame Streak", "Flame Volley", "Flame Blast", "Flame Arc"], ArenaProfile.LinesOf(ProfileRole.WarOption).Select(l => l.Name));
        Assert.Equal(ArenaProfile.WarLineName, ArenaProfile.LinesOf(ProfileRole.WarBolt).Single().Name);

        // IsBuff is true for the 24 buff lines only (the cycle's lines); the two mana lines are IsManaLine and the five war lines neither.
        Assert.Equal(24, ArenaProfile.Lines.Count(l => l.IsBuff));
        Assert.Equal(["Stamina to Mana Self", "Revitalize Self"], ArenaProfile.Lines.Where(l => l.IsManaLine).Select(l => l.Name));
        Assert.All(ArenaProfile.Lines.Where(l => l.IsManaLine), l => Assert.False(l.IsBuff));
        Assert.Equal(1676u, ArenaProfile.LineNamed("Stamina to Mana Self")!.Spells[0].Id);
        Assert.Equal(2345u, ArenaProfile.LineNamed("Stamina to Mana Self")!.Spells[6].Id);
        Assert.Equal(4981u, ArenaProfile.LineNamed("Stamina to Mana Self")!.Spells[7].Id);
        Assert.Equal(1177u, ArenaProfile.LineNamed("Revitalize Self")!.Spells[0].Id);
        Assert.Equal(2083u, ArenaProfile.LineNamed("Revitalize Self")!.Spells[6].Id);
        Assert.Equal(4321u, ArenaProfile.LineNamed("Revitalize Self")!.Spells[7].Id);

        // The dat's own names, not the spec's.
        Assert.Equal("Mana Conversion Mastery Self", ArenaProfile.LineNamed("Mana Mastery Self")!.DatRoot);
        Assert.Equal("Bludgeoning Protection Self", ArenaProfile.LineNamed("Bludgeon Protection Self")!.DatRoot);
        Assert.Equal("Aura of Hermetic Link Self I", ArenaProfile.Spell(1475)!.Name);
        Assert.Equal("Aura of Defender Self I", ArenaProfile.Spell(1599)!.Name);
        Assert.Equal("Mana Conversion Mastery Self I", ArenaProfile.Spell(653)!.Name);
        Assert.Equal("Bludgeoning Protection Self I", ArenaProfile.Spell(1018)!.Name);

        // Volley: the dat's III and IV twice (ids 3663, 3664 then 143, 144); Blast has no level II; Defender VIII is 350.
        Assert.Equal([125u, 175u, 125u, 175u, 225u, 275u, 325u, 400u], ArenaProfile.LineNamed("Flame Volley")!.Spells.Select(s => s.Power));
        Assert.Equal([75u, 125u, 175u, 225u, 275u, 325u, 400u], ArenaProfile.LineNamed("Flame Blast")!.Spells.Select(s => s.Power));
        Assert.Equal(350u, ArenaProfile.Spell(4400)!.Power);
        Assert.Equal(25u, ArenaProfile.Spell(212)!.Power);
        Assert.Equal(25u, ArenaProfile.Spell(1475)!.Power);

        // Every buff has a duration, every war spell none; the shortest buff lives 1800 s.
        Assert.All(ArenaProfile.Lines, l => Assert.All(l.Spells, s => Assert.Equal(l.IsBuff, s.DurationSeconds > 0)));
        Assert.Equal(1800, ArenaProfile.Lines.Where(l => l.IsBuff).SelectMany(l => l.Spells).Min(s => s.DurationSeconds));
        Assert.Equal(400u, ArenaProfile.LineNamed("Strength Self")!.HighestAtMostPower(10_000)!.Power);
        Assert.Equal(2087u, ArenaProfile.LineNamed("Strength Self")!.HighestAtMostPower(300)!.Id);
        Assert.Null(ArenaProfile.LineNamed("Armor Self")!.HighestAtMostPower(0));
    }

    [Fact]
    public void The_extras_are_Harm_Self_and_the_three_travel_spells_the_mage_learns_but_the_cycle_never_casts_M4_P5a()
    {
        Assert.Equal(["Harm Self", "Lifestone Recall", "Primary Portal Tie", "Primary Portal Recall"], ArenaProfile.Extras.Select(l => l.Name));
        Assert.Equal([ProfileRole.HarmSelf, ProfileRole.Travel, ProfileRole.Travel, ProfileRole.Travel], ArenaProfile.Extras.Select(l => l.Role));
        Assert.Equal([8u, 1167u, 1168u, 1169u, 1170u, 1171u, 2071u, 4309u], ArenaProfile.ExtraNamed("Harm Self")!.Spells.Select(s => s.Id));
        Assert.Equal(2071u, ArenaProfile.ExtraNamed("Harm Self")!.Spells[6].Id); // VII, the level the mage casts at the 0.9 rule
        Assert.Equal(1635u, ArenaProfile.ExtraNamed("Lifestone Recall")!.Spells.Single().Id);
        Assert.Equal(47u, ArenaProfile.ExtraNamed("Primary Portal Tie")!.Spells.Single().Id);
        Assert.Equal(48u, ArenaProfile.ExtraNamed("Primary Portal Recall")!.Spells.Single().Id);
        Assert.All(ArenaProfile.Extras, l => Assert.False(l.IsBuff || l.IsManaLine));
        Assert.All(ArenaProfile.Extras.SelectMany(l => l.Spells), s => Assert.Contains(s.Id, ArenaProfile.FightExtras));
        Assert.DoesNotContain(ArenaProfile.Extras.SelectMany(l => l.Spells).Select(s => s.Id), id => ArenaProfile.Spell(id) is not null); // none is a line of Lines
        Assert.Equal(ArenaProfile.Lines.Count, ArenaProfile.Buffs.Lines.Count);
        Assert.Equal("BSD Mage", ArenaProfile.Buffs.CharacterName);
    }

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void The_extras_equal_the_spell_table_entries_in_name_power_school_and_type_M4_P5a()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        foreach (var line in ArenaProfile.Extras)
        {
            foreach (var s in line.Spells)
            {
                Assert.True(spells.TryGetValue(s.Id, out var dat), $"{line.Name}: id {s.Id} is not in the SpellTable");
                Assert.True(dat.Name == s.Name, $"{line.Name}: id {s.Id} is '{dat.Name}', profile '{s.Name}'");
                Assert.True(dat.Power == s.Power, $"{s.Name} ({s.Id}): Power {dat.Power}, profile {s.Power}");
                Assert.True(dat.School == line.School, $"{s.Name} ({s.Id}): school {dat.School}, line {line.School}");
                Assert.Equal(0, dat.Duration);
                var expectedType = line.Name switch
                {
                    "Harm Self" => SpellType.Boost,
                    "Primary Portal Tie" => SpellType.PortalLink,
                    _ => SpellType.PortalRecall,
                };
                Assert.True(dat.MetaSpellType == expectedType, $"{s.Name} ({s.Id}): type {dat.MetaSpellType}, line {expectedType}");
            }
        }

        // The pinned numbers a reader needs: the recall and the tie are Item Enchantment, powers 100, 150 and 100; Harm Self VII is power 325.
        Assert.Equal((MagicSchool.ItemEnchantment, 100u), (spells[1635].School, spells[1635].Power));
        Assert.Equal((MagicSchool.ItemEnchantment, 150u), (spells[47].School, spells[47].Power));
        Assert.Equal((MagicSchool.ItemEnchantment, 100u), (spells[48].School, spells[48].Power));
        Assert.Equal(325u, spells[2071].Power);
    }

    [Fact]
    public void The_arena_and_the_targets_are_the_aluvian_den_and_the_tuskers()
    {
        Assert.Equal([1628u, 1629u], ArenaProfile.TargetWcids);
        Assert.Equal(7242u, ArenaProfile.ArenaPortalWcid);
        Assert.Equal(0x01050375u, ArenaProfile.ArenaCell);
    }

    [RequiresDatsFact]
    public void Every_profile_spell_equals_the_spell_table_entry()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        foreach (var line in ArenaProfile.Lines)
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
                    ProfileRole.StaminaRestore => SpellType.Boost,
                    _ => line.IsBuff ? SpellType.Enchantment : SpellType.Projectile,
                };
                Assert.True(dat.MetaSpellType == expectedType, $"{s.Name} ({s.Id}): type {dat.MetaSpellType}, line {expectedType}");
            }

            // The dat's root name is in every spell of the line but level VII's own title (Flame Arc's VII is "Flame Arc VII").
            var withoutRoot = line.Spells.Where(s => !s.Name.Contains(line.DatRoot, StringComparison.Ordinal)).Select(s => s.Position).ToArray();
            Assert.True(
                (line.Name == "Flame Arc" ? Array.Empty<int>() : [line.Spells.Count - 1]).SequenceEqual(withoutRoot),
                $"{line.Name}: spells without the root '{line.DatRoot}' at {string.Join(",", withoutRoot)}");
        }
    }
}
