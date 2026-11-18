using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 16, slice E5e4: the facts <see cref="GearLight"/> stands on, read from the retail SpellTable (the tests skip when the dats are missing). The owner's rule is that an item's buff spell is overridden by her
/// own stronger one while a cantrip is not: <see cref="GearLight"/> holds no list of cantrips, it reads each spell's category and power, so the rule holds exactly when a cantrip's category is no buff's.
/// </summary>
public sealed class GearLightDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    [RequiresDatsFact]
    public void No_cantrip_the_pick_reads_shares_a_category_with_a_buff_of_the_same_stat_so_no_buff_of_hers_surpasses_one()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var compared = 0;
        foreach (var cantrip in CantripRows.BySpell.Values)
        {
            foreach (var buff in BuffStatMods.BySpell.Values)
            {
                if (buff.Type != cantrip.Mod.Type || buff.Key != cantrip.Mod.Key || CantripRows.Of(buff.Id) is not null || !spells.TryGetValue(buff.Id, out var buffRow))
                {
                    continue;
                }

                compared++;
                Assert.True(spells[cantrip.Id].Category != buffRow.Category, $"{spells[cantrip.Id].Name} ({cantrip.Id}) and {buffRow.Name} ({buff.Id}) share category {buffRow.Category}");
            }
        }

        output.WriteLine($"{CantripRows.BySpell.Count} cantrips against the buffs of their stats: {compared} pairs, no category shared");
        Assert.True(compared > 0);
    }

    [RequiresDatsFact]
    public void The_levels_of_one_buff_share_a_category_and_rise_in_power_and_only_creature_and_life_spells_are_cast_on_the_wearer()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        SpellInfo Named(string name) => spells.Values.Single(s => s.Name == name);

        var armor = new[] { "Armor Self I", "Armor Self II", "Armor Self III", "Armor Self IV", "Armor Self V", "Armor Self VI" }.Select(Named).ToList();
        output.WriteLine(string.Join(", ", armor.Select(s => $"{s.Name} {s.Id}: category {s.Category}, power {s.Power}")));
        Assert.Single(armor.Select(s => s.Category).Distinct());
        Assert.Equal(armor.Select(s => s.Power).Order(), armor.Select(s => s.Power));
        Assert.Equal(armor.Count, armor.Select(s => s.Power).Distinct().Count());
        Assert.All(armor, s => Assert.True(GearLight.CastOnWearer(s))); // Life Magic: on her (Creature_Magic.cs:117-121 @95ef426d)

        Assert.True(GearLight.CastOnWearer(Named("Minor Focus")));
        Assert.True(GearLight.CastOnWearer(Named("Strength Self I")));
        Assert.False(GearLight.CastOnWearer(Named("Impenetrability V"))); // an Item spell of no aura category: on the piece itself (:128)
        Assert.False(GearLight.CastOnWearer(Named("Piercing Bane V")));
    }
}
