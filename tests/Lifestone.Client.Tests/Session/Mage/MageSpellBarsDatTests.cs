using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// The spell ids of the two bars against the SpellTable (0x0E00000E): the dat titles every level VII ("Inferno's Gift" is Fire Vulnerability Other VII), so each id is pinned by its dat name, its power (VII) and the line it
/// shares a category with (level I of the same line).
/// </summary>
public sealed class MageSpellBarsDatTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [RequiresDatsFact]
    public void The_fight_tab_ids_are_the_VII_of_their_lines_in_the_dat()
    {
        var table = SpellTableData.Load(fixture.Dats).Spells;
        (uint Id, string Name, uint Power, uint LineOne)[] pins =
        [
            (MageSpellBars.FireVulnerabilityOtherVii, "Inferno's Gift", 325, 21), // line one (LineOne): Fire Vulnerability Other I
            (MageSpellBars.FlameBoltVii, "Ilservian's Flame", 300, 27), // line one (LineOne): Flame Bolt I
            (MageSpellBars.RevitalizeSelfVii, "Robustification", 300, 1177), // line one (LineOne): Revitalize Self I
            (MageSpellBars.StaminaToManaSelfVii, "Meditative Trance", 300, 1676), // line one (LineOne): Stamina to Mana Self I
            (MageSpellBars.HealSelfVii, "Adja's Intervention", 300, 6), // line one (LineOne): Heal Self I
        ];

        foreach (var (id, name, power, lineOne) in pins)
        {
            Assert.Equal(name, table[id].Name);
            Assert.Equal(power, table[id].Power);
            Assert.Equal(table[lineOne].Category, table[id].Category);
        }

        Assert.Equal("Fire Vulnerability Other I", table[21].Name);
        Assert.Equal("Heal Self I", table[6].Name);
        Assert.Equal("Flame Bolt I", table[27].Name);
        Assert.Equal("Revitalize Self I", table[1177].Name);
        Assert.Equal("Stamina to Mana Self I", table[1676].Name);
        Assert.Equal("Fire Vulnerability Other VI", table[1108].Name); // the id before the VII in the line: VII is a title
        Assert.Equal("Heal Self VI", table[1161].Name);
    }

    [RequiresDatsFact]
    public void The_buff_tab_ids_exist_and_are_the_VI_and_VII_of_the_profile_lines()
    {
        var table = SpellTableData.Load(fixture.Dats).Spells;
        Assert.All(MageSpellBars.BuffTab, id => Assert.True(table.ContainsKey(id)));
        Assert.Equal(["Focus Self VI", "Willpower Self VI", "Creature Enchantment Mastery Self VI"], MageSpellBars.BuffTab.Take(3).Select(id => table[id].Name));
        Assert.Equal(["Inner Calm", "Mind Blossom", "Adja's Blessing"], MageSpellBars.BuffTab.Skip(3).Take(3).Select(id => table[id].Name)); // VII of Focus, Willpower, Creature Enchantment Mastery
    }
}
