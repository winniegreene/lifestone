using Lifestone.Client.Session.Mage;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 7 S2b: a worn find's mana against the dats. ACE gives a loot item <c>ManaRate = -1 / ceil(1200 / maxBaseMana)</c> (<c>CalculateManaRate</c>, LootGenerationFactory_Magic.cs:105-112) and an <c>ItemMaxMana</c> of
/// <c>ceil(maxSpellMana x workmanship mod x rng)</c> with rng 12 to 20 for jewellery (<c>RollItemMaxMana</c>, :67-100); the heartbeat burns it every 5 s (Player_Tick.cs:624-660). Round 12's Compass casts Major War Magic Aptitude (2534).
/// The defences' formulas the requirement check reads are the SkillTable's.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class ItemManaDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint MajorWarMagicAptitude = 2534;

    [RequiresDatsFact]
    public void The_Compass_with_Major_War_Magic_Aptitude_burns_at_ACEs_rate_from_the_cantrips_base_mana()
    {
        var spell = SpellTableData.Load(fixture.Dats).Spells[MajorWarMagicAptitude];
        Assert.Equal("Major War Magic Aptitude", spell.Name);
        var baseMana = (int)spell.BaseMana;
        var secondsPerPoint = Math.Ceiling(1200.0 / baseMana);
        var rate = -1.0 / secondsPerPoint;
        var maxLow = (int)Math.Ceiling(baseMana * 1.0 * 12);
        var maxHigh = (int)Math.Ceiling(baseMana * 1.0 * 20);
        output.WriteLine($"Major War Magic Aptitude base mana {baseMana}: ManaRate {rate:0.#####}/s (1 point per {secondsPerPoint} s, {3600 / secondsPerPoint:0} an hour); ItemMaxMana {maxLow} to {maxHigh} at workmanship mod 1: {maxLow * secondsPerPoint / 3600:0.0} to {maxHigh * secondsPerPoint / 3600:0.0} h from full to empty");
        Assert.Equal(WornManaWatch.CompassBaseMana, baseMana);
        Assert.Equal(WornManaWatch.CompassSecondsPerPoint, secondsPerPoint);
    }

    [RequiresDatsFact]
    public void The_defences_the_armour_loots_activation_limit_names_have_the_SkillTables_formulas()
    {
        var skills = SkillTableData.Load(fixture.Dats);
        foreach (var id in new uint[] { MageTemplate.MeleeDefense, 7, MageTemplate.ArcaneLore, MageTemplate.WarMagic, MageTemplate.ManaConversion })
        {
            var table = skills.Skills.Single(s => s.Id == id).Formula;
            Assert.Equal(table, QualitiesSkills.Formula(id)!.Value.Formula);
        }
    }
}
