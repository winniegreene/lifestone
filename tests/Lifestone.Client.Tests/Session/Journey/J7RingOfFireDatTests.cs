using DatReaderWriter.Enums;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>Cassius' Ring of Fire (1785) against the dats (M4 P5b J7): what the SpellTable says, and that the mage's kit carries its components.</summary>
[Trait("Tier", "Exhaustive")]
public sealed class J7RingOfFireDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    [RequiresDatsFact]
    public void Ring_of_Fire_1785_is_a_War_Magic_projectile_of_power_200_the_profile_line_matches_and_the_mage_kit_carries_its_components_M4_P5b_J7()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var ring = spells[1785];
        output.WriteLine($"{ring.Name}: school {ring.School}, power {ring.Power}, type {ring.MetaSpellType}, base mana {ring.BaseMana}, base formula {string.Join(",", ring.Formula)}");
        Assert.Equal(("Cassius' Ring of Fire", MagicSchool.WarMagic, 200u, SpellType.Projectile), (ring.Name, ring.School, ring.Power, ring.MetaSpellType));

        var line = ArenaProfile.ExtraNamed("Cassius' Ring of Fire")!;
        Assert.Equal((ring.Name, ring.Power, ring.School), (line.Spells.Single().Name, line.Spells.Single().Power, line.School));

        foreach (var account in new[] { "test", "someone-else" })
        {
            // M5 3 m1: the hero mage casts the ring on the foci formula (the Diamond Scarab, which no profile carries yet: a named gap, MageKitTests), so the ground here holds a carried Diamond Scarab line.
            var den = Lifestone.Client.Session.Grounds.GroundCatalog.Get(Lifestone.Client.Session.Scenarios.FightOptions.DefaultGround);
            var ground = den.Supplies.StockOf(7299) is not null ? den : den with { Supplies = den.Supplies with { Stock = [.. den.Supplies.Stock, new Lifestone.Client.Session.Grounds.StockLine(7299, "Diamond Scarab", 10, 2, 2)] } }; // M5 7 F10: the den carries it now
            var held = GearKit.Build(spells, account, heroRing: true, ground: ground).Items.Where(i => i.Kind == GearKind.Component).Select(i => i.Wcid).ToHashSet();
            foreach (var id in GearKit.FociFormulaCounts(ring.Formula).Keys)
            {
                Assert.True(ComponentWeenies.TryGetByComponent(id, out var weenie), $"component {id}");
                Assert.True(held.Contains(weenie.Wcid), $"{account}: the Ring of Fire needs component {id} (wcid {weenie.Wcid}), which the kit does not carry");
            }
        }
    }
}
