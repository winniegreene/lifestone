using System.Globalization;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 item 0, measure <c>m5-mage-80-skill</c> (m5-plan.md section 9; den-loop-audit.md section 3.4 estimated 330-360): the buffed War Magic of a level-80, 100, 120 and 126 mage,
/// computed (not measured) from the spending rule (<see cref="MageSpendingPlanner.Compute"/> over the XpTable's experience for the level) and the buff chain the cycle casts
/// (the owner's order, each line at the highest level whose cast chance reaches <see cref="BuffOptions.DefaultCastChance"/> from the skill the earlier casts left; the same arithmetic as <c>BuffChainDatTests</c>),
/// and the Flame Bolt level <see cref="FightScenario.ChooseLevel(uint, Lifestone.Client.Session.Contracts.Spellbook?, double, out string)"/> would pick from it with every level known. It prints one
/// <c>[measure] level80-skill ...</c> line per level and asserts only that it computed (and that the level-126 row agrees with the pinned chain, which is a check of this test's own arithmetic, not a judgement of the plan).
/// It is a plain unit test that needs the dats (skipped without them, as the repository's dat tests are); it reads no server.
/// </summary>
public sealed class MageLevel80SkillTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>The levels M5 asks about (the owner's 80 to 120 span, and the level the build reaches).</summary>
    private static readonly int[] Levels = [80, 100, 120, 126];

    private sealed record Row(int Level, ulong Xp, uint FocusBase, uint SelfBase, uint WarBase, uint WarBuffed, uint CreatureBuffed, int Chosen, string ChosenName, double Chance, uint NeedFor90, string Lines);

    [RequiresDatsFact]
    public void The_buffed_war_magic_of_a_level_80_to_126_mage_is_computed_from_the_plan_and_the_chain()
    {
        var xp = XpTableData.Load(fixture.Dats);
        var rows = Levels.Select(level => Compute(xp, level)).ToList();
        foreach (var r in rows)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[measure] level80-skill level={r.Level} xp={r.Xp} focus_base={r.FocusBase} self_base={r.SelfBase} war_magic_base={r.WarBase} war_magic_buffed={r.WarBuffed} creature_buffed={r.CreatureBuffed} flame_bolt_level={(r.Chosen < 0 ? "none" : r.ChosenName)} chance={r.Chance:0.000} skill_for_flame_bolt_vii_at_0.9={r.NeedFor90} lines_below_0.9={r.Lines}"));
        }

        Assert.Equal(Levels.Length, rows.Count);
        Assert.All(rows, r => Assert.True(r.WarBuffed > 0));

        // This test's own arithmetic against the pinned chain (BuffChainDatTests: 402 at level 126 from the plan's ranks, no worn-item bonus).
        Assert.Equal(402u, rows[^1].WarBuffed);
        Assert.Equal(MageTemplate.ExpectedMagicSkillBase(MageTemplate.WarMagic), rows[^1].WarBase);
    }

    private static Row Compute(XpTableData xp, int level)
    {
        var total = xp.XpForLevel(level);
        var plan = MageSpendingPlanner.Compute(xp, total);
        StatTarget Target(MageStatKind kind, uint id) => plan.Targets.Single(t => t.Kind == kind && t.Id == id);
        var focusBase = MageTemplate.HighAttribute + (uint)Target(MageStatKind.Attribute, MageTemplate.Focus).Ranks;
        var selfBase = MageTemplate.HighAttribute + (uint)Target(MageStatKind.Attribute, MageTemplate.Self).Ranks;
        uint InitPlusRanks(uint skillId)
        {
            var t = Target(MageStatKind.Skill, skillId);
            return (t.Specialized ? 10u : 0u) + (uint)t.Ranks;
        }

        var focusAdd = 0;
        var selfAdd = 0;
        var skillAdd = new Dictionary<uint, int> { [MageTemplate.CreatureEnchantment] = 0, [MageTemplate.ItemEnchantment] = 0, [MageTemplate.LifeMagic] = 0, [MageTemplate.WarMagic] = 0 };

        uint Current(uint skillId) => GearCasts.SkillValue(focusBase + (uint)focusAdd, selfBase + (uint)selfAdd, InitPlusRanks(skillId), 1.0, null, skillAdd[skillId]);

        var below = new List<string>();
        foreach (var line in OwnersOrder())
        {
            var skill = Current(GearCasts.SkillOf(line.School));
            ProfileSpell? best = null;
            foreach (var spell in line.Spells)
            {
                if (GearCasts.Chance(skill, spell.Power) >= BuffOptions.DefaultCastChance && (best is null || spell.Power > best.Power))
                {
                    best = spell;
                }
            }

            if (best is null)
            {
                below.Add(line.Name.Replace(' ', '_'));
                continue;
            }

            var mod = BuffStatMods.BySpell[best.Id];
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
        }

        var warBuffed = Current(MageTemplate.WarMagic);
        var warBase = GearCasts.SkillValue(focusBase, selfBase, InitPlusRanks(MageTemplate.WarMagic), 1.0, null, 0);
        var chosen = FightScenario.ChooseLevel(warBuffed, null, BuffOptions.DefaultCastChance, out _);
        var line2 = ArenaProfile.LineNamed(ArenaProfile.WarLineName)!;
        var name = chosen < 0 ? "none" : $"{line2.Spells[chosen].Name}(index {chosen + 1})";
        var chance = chosen < 0 ? 0.0 : GearCasts.Chance(warBuffed, line2.Spells[chosen].Power);
        var needed = 0u; // the least skill whose chance for Flame Bolt VII reaches 0.9 (den-loop-audit.md section 3.4: power + 31.4)
        while (GearCasts.Chance(needed, line2.Spells[FightScenario.Levels - 1].Power) < BuffOptions.DefaultCastChance)
        {
            needed++;
        }

        return new Row(level, total, focusBase, selfBase, warBase, warBuffed, Current(MageTemplate.CreatureEnchantment), chosen, name, chance, needed, below.Count == 0 ? "none" : string.Join('+', below));
    }

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
}
