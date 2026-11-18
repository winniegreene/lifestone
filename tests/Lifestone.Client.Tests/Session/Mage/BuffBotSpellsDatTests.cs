using DatReaderWriter.Enums;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Lifestone.ServerTools;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 14 (m6-plan.md section 3.1, D127): each spell of the buff bot's profiles against the retail dats. The bot casts only what one player can cast on another, so every spell is the Other spell
/// of its line at level VII: not self-targeted, beneficial, never resisted, an hour long, and of the same category as the Self line of the climber's own profile it stands over (ACE stacks by
/// category and power, Managers/EnchantmentManager.cs 134-204 @95ef426d, so the bot's line stands over hers and hers under it). A line she holds no Self line of (Run and Jump: Sprint and Jumping
/// Mastery; and the six elemental protections that the profile <c>all</c> adds to the tuskers' Bludgeoning) is proved against the dat alone: the Self spell one id over the bot's, and the Self line the profile names it
/// by, found in the spell table by that name.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class BuffBotSpellsDatTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    /// <summary>
    /// The bot's lines she holds no Self line of: Run and Jump (the owner, 2026-11-11: the bot buffs them), and the six protections of the profile <c>all</c> (the owner, 2026-11-12: every buff, for a ground where every
    /// damage type is met; her own profile holds the tuskers' protection alone).
    /// </summary>
    private static readonly HashSet<string> NotHers = new(StringComparer.Ordinal)
    {
        "Sprint Self", "Jumping Mastery Self",
        "Blade Protection Self", "Piercing Protection Self", "Fire Protection Self", "Cold Protection Self", "Acid Protection Self", "Lightning Protection Self",
    };

    [RequiresDatsFact]
    public void Every_line_is_the_dats_Other_spell_at_level_VII_in_its_Self_lines_category()
    {
        var table = SpellTableData.Load(fixture.Dats).Spells;
        foreach (var profile in BuffBotWire.Profiles)
        {
            foreach (var line in profile.Lines)
            {
                var dat = table[line.SpellId];
                Assert.Equal(line.Spell, dat.Name);
                Assert.False(dat.Flags.HasFlag(SpellFlags.SelfTargeted), $"{line.Spell} is cast on another");
                Assert.True(dat.Flags.HasFlag(SpellFlags.Beneficial), line.Spell);
                Assert.False(dat.Flags.HasFlag(SpellFlags.Resistable), $"{line.Spell} is never resisted (ACE WorldObjects/WorldObject_Magic.cs 122-124)");
                Assert.Equal(SpellType.Enchantment, dat.MetaSpellType);
                Assert.Equal((300u, 3600.0), (dat.Power, dat.Duration));
                Assert.True(dat.School is MagicSchool.CreatureEnchantment or MagicSchool.LifeMagic, $"{line.Spell}: an item spell at another player takes only a shield (D127)");
                Assert.NotEqual(0u, dat.NonComponentTargetType); // it has a target: ACE's emote cast gives it the teller (Managers/EmoteManager.cs 1889-1901)

                // The Self spell of the line at level VII: her own profile's when she holds the line, else the dat's one id over (the two level VII spells of a line sit side by side: the Other, then the Self).
                var own = ArenaProfile.LineNamed(line.Line);
                Assert.Equal(!NotHers.Contains(line.Line), own is not null); // the lines she holds none of are named here, so a third is a decision and not a silent pass
                var selfId = own?.Spells.Single(s => s.Power == 300).Id ?? line.SpellId + 1;
                Assert.Equal(line.SpellId + 1, selfId);
                var self = table[selfId];
                Assert.True(self.Flags.HasFlag(SpellFlags.SelfTargeted), self.Name);
                Assert.Equal((300u, 3600.0), (self.Power, self.Duration));
                Assert.Equal(self.Category, dat.Category);
                Assert.Equal(self.School, dat.School);

                // The line's name is the Self line's in the dat (her profile's own name for it when it differs: its DatRoot): the line's first spell, by that name, is self-targeted and of the bot's spell's category.
                var root = own?.DatRoot ?? line.Line;
                var first = Assert.Single(table.Values, s => s.Name == root + " I");
                Assert.True(first.Flags.HasFlag(SpellFlags.SelfTargeted), first.Name);
                Assert.Equal((dat.Category, dat.School), (first.Category, first.School));
            }
        }
    }

    [RequiresDatsFact]
    public void Run_and_Jump_are_the_dats_Sprint_and_Jumping_Mastery_lines_one_id_under_their_Self_spells()
    {
        var table = SpellTableData.Load(fixture.Dats).Spells;
        (string Line, uint Other, string OtherName, uint Self, string SelfName)[] wanted =
        [
            ("Sprint Self", 2300, "Saladur's Boon", 2301, "Saladur's Blessing"),
            ("Jumping Mastery Self", 2256, "Jahannan's Boon", 2257, "Jahannan's Blessing"),
        ];
        foreach (var (name, other, otherName, self, selfName) in wanted)
        {
            var line = BuffBotWire.Tusker.Lines.Single(l => l.Line == name);
            Assert.Equal((other, otherName), (line.SpellId, line.Spell));
            Assert.Equal((otherName, selfName), (table[other].Name, table[self].Name));
            Assert.False(table[other].Flags.HasFlag(SpellFlags.SelfTargeted), otherName);
            Assert.True(table[self].Flags.HasFlag(SpellFlags.SelfTargeted), selfName);
            Assert.Equal(table[self].Category, table[other].Category);
            Assert.Equal(MagicSchool.CreatureEnchantment, table[other].School);
            Assert.Null(ArenaProfile.LineNamed(name)); // she casts neither herself: the bot's line stands over nothing of hers
        }

        Assert.NotEqual(table[2300].Category, table[2256].Category); // two lines, not one
    }

    [RequiresDatsFact]
    public void The_nine_lines_all_adds_are_the_dats_six_other_protections_and_three_skill_lines_one_id_under_their_Self_spells()
    {
        var table = SpellTableData.Load(fixture.Dats).Spells;
        (string Line, uint Other, string OtherName, uint Self, string SelfName, MagicSchool School)[] wanted =
        [
            ("Blade Protection Self", 2150, "Boon of the Blade Turner", 2151, "Blessing of the Blade Turner", MagicSchool.LifeMagic),
            ("Piercing Protection Self", 2160, "Boon of the Arrow Turner", 2161, "Blessing of the Arrow Turner", MagicSchool.LifeMagic),
            ("Fire Protection Self", 2156, "Fiery Boon", 2157, "Fiery Blessing", MagicSchool.LifeMagic),
            ("Cold Protection Self", 2154, "Icy Boon", 2155, "Icy Blessing", MagicSchool.LifeMagic),
            ("Acid Protection Self", 2148, "Caustic Boon", 2149, "Caustic Blessing", MagicSchool.LifeMagic),
            ("Lightning Protection Self", 2158, "Storm's Boon", 2159, "Storm's Blessing", MagicSchool.LifeMagic),
            ("Magic Resistance Self", 2280, "Web of Resistance", 2281, "Aura of Resistance", MagicSchool.CreatureEnchantment),
            ("Impregnability Self", 2242, "Web of Deflection", 2243, "Aura of Deflection", MagicSchool.CreatureEnchantment),
            ("Arcane Enlightenment Self", 2194, "Aliester's Boon", 2195, "Aliester's Blessing", MagicSchool.CreatureEnchantment),
        ];
        var added = BuffBotWire.All.Lines.Skip(BuffBotWire.Tusker.Lines.Count).ToList();
        Assert.Equal(wanted.Select(w => w.Line), added.Select(l => l.Line));
        foreach (var (name, other, otherName, self, selfName, school) in wanted)
        {
            var line = added.Single(l => l.Line == name);
            Assert.Equal((other, otherName), (line.SpellId, line.Spell));
            Assert.Equal((otherName, selfName), (table[other].Name, table[self].Name));
            Assert.False(table[other].Flags.HasFlag(SpellFlags.SelfTargeted), otherName);
            Assert.True(table[self].Flags.HasFlag(SpellFlags.SelfTargeted), selfName);
            Assert.Equal(table[self].Category, table[other].Category);
            Assert.Equal(school, table[other].School);
            Assert.Equal((300u, 3600.0), (table[other].Power, table[other].Duration));

            // The three skill lines are lines of her own profile, whose level VII spell is the Self spell here; the six protections are no line of hers.
            Assert.Equal(school == MagicSchool.CreatureEnchantment ? self : (uint?)null, ArenaProfile.LineNamed(name)?.Spells.Single(s => s.Power == 300).Id);
        }

        // Twenty-eight lines, twenty-eight categories: no line of the profile stands over another of it (ACE keeps one spell a category in effect).
        Assert.Equal(BuffBotWire.All.Lines.Count, BuffBotWire.All.Lines.Select(l => table[l.SpellId].Category).Distinct().Count());

        // Every damage type a creature deals has its protection in the profile: the seven elemental protection categories of the dat's Self lines, the tuskers' Bludgeoning among them.
        string[] elements = ["Blade", "Piercing", "Bludgeoning", "Fire", "Cold", "Acid", "Lightning"];
        var protections = elements.Select(e => table.Values.Single(s => s.Name == e + " Protection Self I").Category).ToHashSet();
        Assert.Equal(7, protections.Count);
        Assert.Equal(protections, BuffBotWire.All.Lines.Select(l => table[l.SpellId].Category).Where(protections.Contains).ToHashSet());
        Assert.Single(BuffBotWire.Tusker.Lines, l => protections.Contains(table[l.SpellId].Category)); // the tuskers' profile: Bludgeoning alone
    }
}
