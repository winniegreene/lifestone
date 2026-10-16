using DatReaderWriter.Enums;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>The BSD profile as data: its shape, and, with the dats, every id, name, power, duration and school against the SpellTable.</summary>
public sealed class ArenaProfileTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [Fact]
    public void The_profile_has_31_lines_247_spells_and_every_id_once()
    {
        Assert.Equal(31, ArenaProfile.Lines.Count);
        Assert.Equal(247, ArenaProfile.Lines.Sum(l => l.Spells.Count));
        Assert.Equal(247, ArenaProfile.SpellsToLearn.Count);
        Assert.Equal(247, ArenaProfile.SpellsToLearn.Distinct().Count());
        Assert.Equal(30, ArenaProfile.Lines.Count(l => l.Spells.Count == 8));
        Assert.Equal(7, ArenaProfile.LineNamed("Flame Blast")!.Spells.Count);
        Assert.Equal(ArenaProfile.Lines.SelectMany(l => l.Spells).Select(s => s.Id), ArenaProfile.SpellsToLearn);
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
