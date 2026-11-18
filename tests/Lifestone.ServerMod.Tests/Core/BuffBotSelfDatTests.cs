using DatReaderWriter.Enums;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;

namespace Lifestone.ServerMod.Tests.Core;

/// <summary>
/// The owner (2026-11-17): the bot's own book (<see cref="BuffBotSelf.Book"/>) against the retail dats. Each self line is its line's Self spell at level VII (self-targeted, beneficial, power 300, an hour, base
/// mana 70) in the school it names; the dance's two spells are Life Magic, power 300, base mana 70, Meditative Trance a Transfer and Robustification a Boost, neither lasting (the transfer and boost numbers are the
/// world copy's spell table's, which the dat does not hold: <see cref="BuffBotSelfTests"/> uses them).
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class BuffBotSelfDatTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [RequiresDatsFact]
    public void The_bots_own_book_is_the_dats_level_VII_Self_spells_of_its_lines_and_its_dance()
    {
        var table = SpellTableData.Load(fixture.Dats).Spells;
        foreach (var line in BuffBotSelf.Lines)
        {
            var dat = table[line.SpellId];
            Assert.Equal(line.Spell, dat.Name);
            Assert.True(dat.Flags.HasFlag(SpellFlags.SelfTargeted), line.Spell);
            Assert.True(dat.Flags.HasFlag(SpellFlags.Beneficial), line.Spell);
            Assert.Equal(SpellType.Enchantment, dat.MetaSpellType);
            Assert.Equal((300u, 3600.0, 70u), (dat.Power, dat.Duration, dat.BaseMana));

            // The line's first spell, by the line's name, is of the same category: the level VII spell stands over the line it names.
            var first = Assert.Single(table.Values, s => s.Name == line.Line + " I");
            Assert.Equal(first.Category, dat.Category);
            Assert.True(first.Flags.HasFlag(SpellFlags.SelfTargeted), first.Name);
        }

        (uint Id, string Name, SpellType Type)[] dance =
        [
            (BuffBotSelf.StaminaToManaSpell, BuffBotSelf.StaminaToManaName, SpellType.Transfer),
            (BuffBotSelf.RevitalizeSpell, BuffBotSelf.RevitalizeName, SpellType.Boost),
        ];
        foreach (var (id, name, type) in dance)
        {
            var dat = table[id];
            Assert.Equal((name, type, MagicSchool.LifeMagic), (dat.Name, dat.MetaSpellType, dat.School));
            Assert.True(dat.Flags.HasFlag(SpellFlags.SelfTargeted), name);
            Assert.Equal((300u, 70u), (dat.Power, dat.BaseMana));
        }

        Assert.Equal("Stamina to Mana Self VI", table[1681].Name); // the line's level VI, one below Meditative Trance's
        Assert.Equal(table[1681].Category, table[BuffBotSelf.StaminaToManaSpell].Category);
        Assert.Equal("Revitalize Self VI", table[1182].Name);
        Assert.Equal(table[1182].Category, table[BuffBotSelf.RevitalizeSpell].Category);
    }
}
