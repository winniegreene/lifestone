using DatReaderWriter.Enums;
using Lifestone.Client.Session.Fighter;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>The fighters' magic gear against the dats (M4 P5a C6): Arcane Lore's formula, and every pinned spell in the SpellTable with the pinned name and a school the gear's spell check can read.</summary>
[Trait("Tier", "Exhaustive")]
public sealed class FighterGearDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    [RequiresDatsFact]
    public void Arcane_Lore_is_Focus_over_3_as_the_gear_check_computes_it()
    {
        var skills = SkillTableData.Load(fixture.Dats);
        var f = skills.Skills.Single(s => s.Id == FighterGearCheck.ArcaneLoreSkillId).Formula;
        Assert.Equal(FighterGearCheck.ArcaneLoreFormula, f);
    }

    [RequiresDatsFact]
    public void Every_pinned_spell_is_in_the_SpellTable_by_id_and_name_and_most_pieces_carry_a_spell_that_goes_on_the_wearer()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        foreach (var set in new[] { FighterGearSet.Brawler, FighterGearSet.Archer })
        {
            var onWearer = 0;
            foreach (var piece in set.Pieces)
            {
                foreach (var spell in piece.Spells)
                {
                    Assert.True(spells.TryGetValue(spell.Id, out var info), $"{piece.Name}: spell {spell.Id} {spell.Name} is not in the dat");
                    Assert.Equal(spell.Name, info!.Name);
                }

                var wearer = FighterGearSet.WearerSpells(piece, id => spells[id].School).ToArray();
                onWearer += wearer.Length > 0 ? 1 : 0;
                output.WriteLine($"{set.Key}: {piece.Name} ({piece.Wcid}) diff {piece.ItemDifficulty}, {piece.Spells.Count} spells, on the wearer: {string.Join(", ", wearer.Select(w => w.Name))}");
                Assert.All(wearer, w => Assert.Contains(spells[w.Id].School, new[] { MagicSchool.CreatureEnchantment, MagicSchool.LifeMagic }));
            }

            // Armour's Impenetrability and the damage auras go on the item itself; the attribute, skill, ward and regeneration lines go on the wearer, which is what gear_magic reads.
            Assert.True(onWearer >= set.Pieces.Count - 3, $"{set.Key}: {onWearer} of {set.Pieces.Count} pieces carry a wearer spell");
        }
    }
}
