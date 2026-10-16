using System.Globalization;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// The predicted buff chain of BSD Mage (M3 2.1 s0a; plan section 9, the owner's question of item 1.1): the 24 buff lines cast in the owner's
/// dependency order (Focus Self and Willpower Self, then Creature Enchantment Mastery Self, then the other masteries, then the rest), each at the
/// highest level whose ACE cast chance (<see cref="GearCasts.Chance"/>, decision D10's rule at 0.9) is at least 0.9 from the skill the earlier
/// casts left, using the StatMods of <see cref="BuffStatMods"/> and the skill arithmetic of <see cref="GearCasts.SkillValue"/>. It prints one line
/// per cast and the cycle's mana, and pins them: the cycle itself (s0b) reads the buffed skill from the read model after every cast.
/// </summary>
public sealed class BuffChainDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>The cast chance D10 demands (<c>--buff-cast-chance</c>'s default).</summary>
    private const double MinChance = 0.9;

    /// <summary>The items BSD Mage wears and wields (the wand and the five pieces of the leather set): the worn-item count of ACE's armour-spell surcharge (<see cref="GearCasts.ManaCeiling"/>).</summary>
    private const int WornItems = 6;

    private sealed record Cast(ProfileLine Line, ProfileSpell Spell, uint Skill, double Chance, string SkillsAfter, uint BaseMana, uint ManaCeiling);

    /// <summary>The owner's order: Focus and Willpower, Creature Enchantment Mastery, the other masteries, then the rest in profile order.</summary>
    private static List<ProfileLine> OwnersOrder()
    {
        var order = new List<ProfileLine>();
        foreach (var name in new[] { "Focus Self", "Willpower Self", "Creature Enchantment Mastery Self" })
        {
            order.Add(ArenaProfile.LineNamed(name)!);
        }

        order.AddRange(ArenaProfile.Lines.Where(l => l.IsBuff && l.Name.EndsWith("Mastery Self", StringComparison.Ordinal) && !order.Contains(l)).ToArray());
        order.AddRange(ArenaProfile.Lines.Where(l => l.IsBuff && !order.Contains(l)).ToArray());
        return order;
    }

    /// <summary>The chain from the template's own values: the attribute and skill ranks the plan gives, no buff active.</summary>
    private List<Cast> Chain(out Dictionary<uint, uint> finalSkills)
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var focusAdd = 0;
        var selfAdd = 0;
        var skillAdd = new Dictionary<uint, int> { [MageTemplate.CreatureEnchantment] = 0, [MageTemplate.ItemEnchantment] = 0, [MageTemplate.LifeMagic] = 0, [MageTemplate.WarMagic] = 0 };
        var focusRaw = MageTemplate.HighAttribute + (uint)MageTemplate.TargetOf(MageStatKind.Attribute, MageTemplate.Focus)!.Ranks;
        var selfRaw = MageTemplate.HighAttribute + (uint)MageTemplate.TargetOf(MageStatKind.Attribute, MageTemplate.Self)!.Ranks;

        uint Current(uint skillId)
        {
            var t = MageTemplate.TargetOf(MageStatKind.Skill, skillId)!;
            return GearCasts.SkillValue(focusRaw + (uint)focusAdd, selfRaw + (uint)selfAdd, (t.Specialized ? 10u : 0u) + (uint)t.Ranks, 1.0, null, skillAdd[skillId]);
        }

        var casts = new List<Cast>();
        foreach (var line in OwnersOrder())
        {
            var skillId = GearCasts.SkillOf(line.School);
            var skill = Current(skillId);
            ProfileSpell? best = null;
            foreach (var spell in line.Spells)
            {
                if (GearCasts.Chance(skill, spell.Power) >= MinChance && (best is null || spell.Power > best.Power))
                {
                    best = spell;
                }
            }

            Assert.True(best is not null, $"{line.Name}: no level reaches {MinChance} at {line.School} skill {skill}");
            var mod = BuffStatMods.BySpell[best!.Id];
            if (mod.IsAttribute && mod.IsAdditive)
            {
                if (mod.Key == MageTemplate.Focus)
                {
                    focusAdd += (int)mod.Value;
                }
                else if (mod.Key == MageTemplate.Self)
                {
                    selfAdd += (int)mod.Value;
                }
            }
            else if (mod.IsSkill && mod.IsAdditive && skillAdd.ContainsKey(mod.Key))
            {
                skillAdd[mod.Key] += (int)mod.Value;
            }

            casts.Add(new Cast(
                line,
                best,
                skill,
                GearCasts.Chance(skill, best.Power),
                string.Create(CultureInfo.InvariantCulture, $"Creature {Current(MageTemplate.CreatureEnchantment)} Item {Current(MageTemplate.ItemEnchantment)} Life {Current(MageTemplate.LifeMagic)} War {Current(MageTemplate.WarMagic)}"),
                spells[best.Id].BaseMana,
                GearCasts.ManaCeiling(spells[best.Id], WornItems)));
        }

        finalSkills = skillAdd.Keys.ToDictionary(k => k, Current);
        return casts;
    }

    [RequiresDatsFact]
    public void The_chain_casts_the_owners_order_at_the_levels_the_chances_allow_and_prints_the_cycles_mana()
    {
        var casts = Chain(out var finalSkills);
        foreach (var c in casts)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{c.Line.Name}: {c.Spell.Name} (position {c.Spell.Position}, power {c.Spell.Power}), chance {c.Chance:0.0000} from {c.Line.School} skill {c.Skill}, base mana {c.BaseMana} (ceiling {c.ManaCeiling}); skills after: {c.SkillsAfter}"));
        }

        var baseMana = casts.Sum(c => (long)c.BaseMana);
        var ceiling = casts.Sum(c => (long)c.ManaCeiling);
        output.WriteLine($"cycle: {casts.Count} casts, base mana {baseMana}, ceiling with the armour surcharge at {WornItems} worn items {ceiling}; final skills: " + string.Join(", ", finalSkills.Select(kv => $"{kv.Key}={kv.Value}")));

        // The owner's order: Focus and Willpower, Creature Enchantment Mastery, the other masteries, then the rest; every one of the 24 buff lines once.
        Assert.Equal(24, casts.Count);
        Assert.Equal(["Focus Self", "Willpower Self", "Creature Enchantment Mastery Self"], casts.Take(3).Select(c => c.Line.Name));
        Assert.Equal(["War Magic Mastery Self", "Item Enchantment Mastery Self", "Life Magic Mastery Self", "Mana Mastery Self"], casts.Skip(3).Take(4).Select(c => c.Line.Name));
        Assert.Equal(ArenaProfile.Lines.Where(l => l.IsBuff).Select(l => l.Name).Order(), casts.Select(c => c.Line.Name).Order());
        Assert.All(casts, c => Assert.True(c.Chance >= MinChance));

        // The starting point: 316 in the three enchantment schools (Focus 267 and Self 267 give 134, plus 182 ranks).
        Assert.Equal(316u, casts[0].Skill);

        // What the numbers give (the plan's row 1.1 expected the three first casts at VI and VII at 91% in every school): Focus and Willpower are VI (VII is 75% and 84% from 316 and 324),
        // and Creature Enchantment Mastery already reaches VII, at 91%, from 333 (Adja's Blessing, +40), so the skill is 373 and not 368 when the masteries come; every other line is VII at 99%.
        Assert.Equal([6, 6], casts.Take(2).Select(c => c.Spell.Position));
        Assert.Equal(Enumerable.Repeat(7, 22), casts.Skip(2).Select(c => c.Spell.Position));
        Assert.Equal(333u, casts[2].Skill);
        Assert.InRange(casts[2].Chance, 0.909, 0.910);
        Assert.Equal(1.0, casts.Skip(3).Min(c => c.Chance), 0.01);

        // Every school reaches VII at 0.9 or better after the first three casts, and VIII stays below 0.9 even at the end (Creature, Item and Life 373: about 13%; War 402: about 53%).
        Assert.Equal(373u, finalSkills[MageTemplate.CreatureEnchantment]);
        Assert.Equal(373u, finalSkills[MageTemplate.ItemEnchantment]);
        Assert.Equal(373u, finalSkills[MageTemplate.LifeMagic]);
        Assert.Equal(402u, finalSkills[MageTemplate.WarMagic]);
        foreach (var (skillId, skill) in finalSkills)
        {
            var vii = GearCasts.Chance(skill, 300);
            var viii = GearCasts.Chance(skill, 400);
            output.WriteLine($"skill {skillId} = {skill}: VII {vii:0.0000}, VIII {viii:0.0000}");
            Assert.True(vii >= MinChance);
            Assert.True(viii < MinChance);
        }

        // The cycle's mana: every buff lands at VI or VII, whose base mana is 70 each at VII and 70 at VI (the dat), 24 casts, the armour lines once each (D23: a cast at the caster covers every worn piece).
        Assert.Equal(1680, baseMana);

        // Impenetrability and Bludgeon Bane add ManaMod (14) for each worn item in ACE (Creature_Magic.cs CalculateManaUsage): 154 each at six worn items, so the ceiling is 1,848.
        Assert.Equal(1848, ceiling);

        // The kit's ManaCastsPerCycle (GearKit): the cycle needs (mana - maximum 426) over a Stamina to Mana Self VI's yield from a full stamina (275 gives 207), rounded up, both lines, times two for fizzles.
        // Both the base mana and the ceiling give the same seven transfers.
        BuffStatMods.Transfer(BuffStatMods.TransferOf(1681)!.Value, 275, 426, out _, out var yield);
        foreach (var mana in new[] { baseMana, ceiling })
        {
            var transfers = (int)Math.Ceiling((mana - 426) / (double)yield);
            output.WriteLine($"cycle mana {mana}: {transfers} Stamina to Mana Self VI at {yield} each (+ as many Revitalize casts), x2 = {2 * (transfers + transfers)} mana casts a cycle");
            Assert.Equal(7, transfers);
            Assert.Equal(GearKit.ManaCastsPerCycle, 2 * (transfers + transfers));
        }
    }
}
