using DatReaderWriter.Enums;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 14 (<c>m6-banes</c>) against the retail dats: the eight armour lines of the climber's profile (Impenetrability, Bludgeon Bane and the six other banes of <see cref="ArmourBanes"/>) as the SpellTable (0x0E00000E) holds
/// them: every id's name, power and duration, the school and the form (an Item Enchantment whose target type is Vestements, not self-targeted, so the cycle casts it at her own guid and ACE gives it to every worn piece),
/// the category inside the armour surcharge's range, the base mana and the mana mod (what one cast pays for each worn piece), and what a level burns on the foci formula.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class ArmourBanesDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private static readonly uint[] BaseMana = [10, 20, 30, 40, 50, 60, 70, 80];
    private static readonly uint[] ManaMod = [2, 4, 6, 8, 10, 12, 14, 14];

    /// <summary>The SpellTable's category of each line (ACE's SpellCategory: 160 ArmorValueRaising, then the resistance-raising categories), each inside 160 to 189, the range <c>Creature.CalculateManaUsage</c> adds the mana mod for.</summary>
    private static readonly Dictionary<string, uint> Category = new()
    {
        ["Impenetrability"] = 160, ["Acid Bane"] = 162, ["Bludgeon Bane"] = 164, ["Frost Bane"] = 166, ["Lightning Bane"] = 168, ["Flame Bane"] = 170, ["Piercing Bane"] = 172, ["Blade Bane"] = 174,
    };

    [RequiresDatsFact]
    public void Every_armour_line_is_the_SpellTables_in_name_power_duration_form_category_and_mana()
    {
        var table = SpellTableData.Load(fixture.Dats).Spells;
        foreach (var name in ArmourBanes.AllArmourLines)
        {
            var line = BuffProfiles.Climber.LineNamed(name)!;
            Assert.Equal(8, line.Spells.Count);
            for (var i = 0; i < line.Spells.Count; i++)
            {
                var spell = line.Spells[i];
                var info = table[spell.Id];
                output.WriteLine($"{spell.Id} {info.Name}: power {info.Power}, {info.Duration} s, base mana {info.BaseMana}, mana mod {info.ManaMod}, category {info.Category}, target 0x{info.NonComponentTargetType:X}, formula {string.Join(",", info.Formula)}");
                Assert.Equal((spell.Name, spell.Power, spell.DurationSeconds), (info.Name, info.Power, info.Duration));
                Assert.Equal((MagicSchool.ItemEnchantment, SpellType.Enchantment, 0x6u), (info.School, info.MetaSpellType, info.NonComponentTargetType)); // Vestements
                Assert.True((info.Flags & SpellFlags.Beneficial) != 0 && (info.Flags & SpellFlags.SelfTargeted) == 0, info.Name);
                Assert.Equal(CastForm.AtItem, GearCasts.FormOf(info));
                Assert.Equal(Category[name], info.Category);
                Assert.Equal((BaseMana[i], ManaMod[i]), (info.BaseMana, info.ManaMod));
                Assert.Equal(BaseMana[i] + (ManaMod[i] * 10), GearCasts.ManaCeiling(info, 10)); // ten enchantable pieces worn (the live count of 2026-11-12): IV 120, V 150 before Mana Conversion

                // The foci formula (she holds the Foci of Artifice): the level's scarab and its Prismatic Tapers (188): 1, 2, 3, 3, 4, 4 at I to VI.
                if (i < 6)
                {
                    var foci = GearKit.FociFormulaCounts(info.Formula);
                    Assert.Equal(new Dictionary<uint, int> { [(uint)(i + 1)] = 1, [188] = new[] { 1, 2, 3, 3, 4, 4 }[i] }, foci);
                }
            }
        }

        var four = table[1572]; // Piercing Bane IV
        Assert.Equal((120u, 150u), (GearCasts.ManaCeiling(four, 10), GearCasts.ManaCeiling(table[1573], 10)));
        Assert.Equal(8 * 120u, (uint)ArmourBanes.AllArmourLines.Sum(n => GearCasts.ManaCeiling(table[BuffProfiles.Climber.LineNamed(n)!.Spells[3].Id], 10))); // a pass of the eight at IV: 960 mana before Mana Conversion
    }
}
