using DatReaderWriter.Enums;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>Lifestone Tie (2644) against the dats (M4 P5b J3b, W0): the profile lines carry the SpellTable's own numbers, and the kits carry the components of the recall and the tie.</summary>
[Trait("Tier", "Exhaustive")]
public sealed class J3bTravelDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    [RequiresDatsFact]
    public void Lifestone_Tie_2644_is_an_Item_Enchantment_portal_link_of_power_100_and_the_mage_and_both_fighters_carry_the_line_M4_P5b_J3b()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var tie = spells[2644];
        output.WriteLine($"{tie.Name}: school {tie.School}, power {tie.Power}, duration {tie.Duration}, type {tie.MetaSpellType}, base mana {tie.BaseMana}");
        Assert.Equal(("Lifestone Tie", MagicSchool.ItemEnchantment, 100u, SpellType.PortalLink), (tie.Name, tie.School, tie.Power, tie.MetaSpellType));
        Assert.Equal(0, tie.Duration);

        var line = ArenaProfile.ExtraNamed("Lifestone Tie")!;
        Assert.Equal(2644u, line.Spells.Single().Id);
        Assert.Equal((tie.Name, tie.Power, tie.School), (line.Spells.Single().Name, line.Spells.Single().Power, line.School));
        Assert.Contains(2644u, ArenaProfile.SpellsToLearn);
        foreach (var fighter in FighterProfile.All)
        {
            Assert.Same(line, fighter.LifestoneTie);
            Assert.Contains(2644u, fighter.SpellsToLearn);
            Assert.False(fighter.LifestoneTie.IsBuff);
            Assert.DoesNotContain(fighter.LifestoneTie, fighter.BuffOrder);
        }

        // A skill of 132 casts power 100 at the 0.9 rule, as the recall: the fighters' Item Enchantment (161 and up) reaches it.
        Assert.True(GearCasts.Chance(FighterComponents.ItemEnchantmentSkill, tie.Power) >= FighterComponents.CastChance);
    }

    [RequiresDatsFact]
    public void The_mage_kit_carries_the_components_of_Lifestone_Recall_and_Lifestone_Tie_and_the_fighters_kits_too_M4_P5b_J3b()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        foreach (var (account, fighter) in new[] { ("test", (FighterProfile?)null), ("test-melee", FighterProfile.Brawler), ("test-archer", FighterProfile.Archer) })
        {
            var held = fighter is null
                ? GearKit.Build(spells, account).Items.Where(i => i.Kind == GearKind.Component).Select(i => i.Wcid).ToHashSet()
                : FighterComponents.For(fighter, spells, account).Select(i => i.Item.Wcid).ToHashSet();
            foreach (var spell in new uint[] { 1635, 2644 })
            {
                var info = spells[spell];
                foreach (var id in SpellFormulaRules.CustomizeForAccount(info.Formula, info.FormulaVersion, account).Where(c => c != 0).Distinct())
                {
                    Assert.True(ComponentWeenies.TryGetByComponent(id, out var weenie), $"component {id}");
                    Assert.True(held.Contains(weenie.Wcid), $"{account}: spell {spell} ({info.Name}) needs component {id} (wcid {weenie.Wcid}), which the kit does not carry");
                }
            }
        }
    }
}
