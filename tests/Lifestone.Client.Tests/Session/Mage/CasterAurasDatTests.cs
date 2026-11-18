using DatReaderWriter.Enums;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 (<c>m6-caster</c>) against the retail dats: the three caster auras of the climber's profile (Spirit Drinker, Hermetic Link, Defender, I to VI) as the SpellTable (0x0E00000E) holds them: name, power, Item Enchantment, a self
/// cast (the proof is her own enchantment: they land on her, WorldObject_Magic.cs:2180-2184 @95ef426d), and what a level burns on the foci formula (the level's scarab, so the banes' scarab rule, <see cref="ScrivenerLearnPlan.ArmorLevelInReach"/>,
/// holds for them); and the caster score's bolt, Flame Bolt V and VI, by their base mana and power.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class CasterAurasDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    [RequiresDatsFact]
    public void The_caster_auras_are_the_SpellTables_self_casts_and_each_level_burns_its_own_scarab()
    {
        var table = SpellTableData.Load(fixture.Dats).Spells;
        foreach (var name in CasterAuras.LineNames)
        {
            var line = ArenaProfile.LineNamed(name)!;
            for (var i = 0; i < 6; i++)
            {
                var spell = line.Spells[i];
                var info = table[spell.Id];
                output.WriteLine($"{spell.Id} {info.Name}: power {info.Power}, {info.Duration} s, base mana {info.BaseMana}, flags {info.Flags}, target 0x{info.NonComponentTargetType:X}, formula {string.Join(",", info.Formula)}");
                Assert.Equal((spell.Name, spell.Power, spell.DurationSeconds), (info.Name, info.Power, info.Duration));
                Assert.Equal((MagicSchool.ItemEnchantment, SpellType.Enchantment), (info.School, info.MetaSpellType));
                Assert.Equal(CastForm.Self, GearCasts.FormOf(info));
                var foci = GearKit.FociFormulaCounts(info.Formula);
                Assert.Equal(1, foci[(uint)(i + 1)]); // the level's scarab: Lead, Iron, Copper, Silver, Gold, Pyreal
            }
        }

        var five = table[84];
        var six = table[85];
        output.WriteLine($"{five.Name}: base mana {five.BaseMana}, power {five.Power}; {six.Name}: base mana {six.BaseMana}, power {six.Power}");
        Assert.Equal(("Flame Bolt V", 25u, 200u), (five.Name, five.BaseMana, five.Power));
        Assert.Equal(("Flame Bolt VI", 250u), (six.Name, six.Power));
    }
}
