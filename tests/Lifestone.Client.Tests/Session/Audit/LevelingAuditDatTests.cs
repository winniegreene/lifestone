using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Messages;
using Lifestone.Client.Session.Quests;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Tests;
using Xunit;

namespace Lifestone.Client.Tests.Session.Audit;

/// <summary>
/// The leveling audit of 2026-11-19 (cache/m6/sprint/leveling-audit.md): the findings that need the dats' XpTable. Each assertion marked
/// "a fix:" flips when the finding is fixed; the owner chooses the fix.
/// </summary>
public sealed class LevelingAuditDatTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    /// <summary>The Minor Smoldering Atlan Wand's wield rule: base War Magic 290 (SELECT type, value FROM weenie_properties_int WHERE
    /// object_Id = 46128 AND type IN (158, 159, 160): 2 (raw skill), 34 (War Magic), 290; read 2026-11-19).</summary>
    private const uint WandWieldDifficulty = 290;

    /// <summary>
    /// Finding 4 (with 10), FIXED on m6/audit-fixes (the owner's decision of 2026-11-19). Before: the Atlan wand errand opens at 75 (atlan-wand.json ladder from_level; its own source: "the ladder's
    /// from_level 75 is about where she reaches it"), while the ladder's own projection of her base War Magic reaches the wand's 290 at 65
    /// (LadderTests: "from 65, base 290"). The earlier climber's live rate on the Quarters rose from 3.9-5.2 M XP/h at level 79 to 5.8-7.9 M
    /// XP/h at 82-83 with the wand (cache/m6/14/live, the [ladder] lines of 2026-11-17/18).
    /// </summary>
    [RequiresDatsFact]
    public void Audit_fixed_the_wand_errand_opens_where_the_projection_can_wield_it()
    {
        var table = XpTableData.Load(fixture.Dats);
        var first = Enumerable.Range(1, 126).First(level => SpellPlan.ProjectedWarBase(table, level) >= WandWieldDifficulty);
        var window = QuestCatalog.Get("atlan-wand").Ladder!;

        Assert.Equal(65, first);
        Assert.Equal(first, window.FromLevel); // fixed (the owner's decision of 2026-11-19): the window opens where the projection can wield it; the roll's floor (0.80) gates the start
    }

    /// <summary>
    /// Fix 10 (the owner's decision of 2026-11-19: the wand's roll only at 80% or more): the floor's skill against the Atlan recipe's difficulty 150 (the 60-80 helper's read of recipe 789, the quest file's step), the
    /// spend's push to it when in reach of this allocation and the next two level-ups', and none when the chance already stands at the floor.
    /// </summary>
    [RequiresDatsFact]
    public void Fix_10_the_spend_buys_the_rolls_skill_to_its_80_percent_floor_in_reach()
    {
        var table = XpTableData.Load(fixture.Dats);
        const uint Alchemy = MageTemplate.Alchemy; // ACE's Skill.Alchemy (38)
        const uint Difficulty = 150;
        var floorSkill = RecipeChance.SkillFor(Difficulty, RecipeChance.RollFloor);
        Assert.InRange(RecipeChance.Of(floorSkill, Difficulty), 0.80, 0.81);

        PlayerQualities Her(uint ranks, long unassigned, int level)
        {
            var q = new PlayerQualities { IsKnown = true };
            q.Ints[PlayerQualities.LevelProperty] = level;
            q.Int64s[PlayerQualities.TotalExperienceProperty] = (long)table.CharacterLevelXpList[level];
            q.Int64s[PlayerQualities.AvailableExperienceProperty] = unassigned;
            q.Skills[Alchemy] = new SkillDesc(Alchemy, (ushort)ranks, 1, SkillRules.TrainedClass, 0, 0u, 0, 0);
            return q;
        }

        var short20 = new RecipeSpendFacts(Alchemy, floorSkill - 20, Difficulty, "atlan-wand");
        var push = RecipeSpend.Push(table, Her(10, 50_000_000, 75), short20, 50_000_000, 0, out var line);
        Assert.NotNull(push);
        Assert.Equal((Alchemy, 30), (push!.Skill, push.Ranks)); // 20 ranks over the 10 held
        Assert.Contains("buy first: ranks 10 -> 30", line, StringComparison.Ordinal);

        Assert.Null(RecipeSpend.Push(table, Her(10, 0, 75), new RecipeSpendFacts(Alchemy, floorSkill, Difficulty, "atlan-wand"), 0, 0, out var at));
        Assert.Contains("at or over the 0.80 floor", at, StringComparison.Ordinal);
    }

    /// <summary>
    /// Finding 7, FIXED on m6/audit-fixes (the owner's decision of 2026-11-19). Before: e1f2e1b2 was undone by the every-spell rule for Heal Self: the plan's sustain line now stops at the level she casts at 0.95,
    /// but once that level is in her book the plan's need is Known, and EverySpellPlan.Evaluate decides a Known line again itself unless it is
    /// the dance or Revitalize (EverySpellPlan.cs:606). Heal Self is Beneficial in the dats' spell table, so it is decided at the buff bar,
    /// 0.75: at Life Magic 270 standing (her projected base 205 to 210 in the late 20s plus the buff bot's 60 to 65) the plan's level is V
    /// (power 200: 0.95 needs 243) and the rule buys VI (power 250, ACE's chance 0.80 at 270) for 50,000 at a Grand Master, a Town Network
    /// trip, a heal the fight never casts below its 0.95 (VitalsMeans.Pick).
    /// </summary>
    [RequiresDatsFact]
    public void Audit_fixed_the_every_spell_rule_keeps_the_plans_Heal_Self_V_once_it_is_known()
    {
        var spells = Lifestone.Data.Magic.SpellTableData.Load(fixture.Dats).Spells;
        var catalog = Lifestone.Client.Session.Grounds.ScrivenerCatalog.Default.WithStock(Lifestone.Client.Session.Grounds.ScrivenerStock.Of(Lifestone.Client.Session.Grounds.ScrivenerStock.Default.Spells.Where(s => s.Line == "Heal Self")));
        var five = catalog.Stock.LineSpells("Heal Self").Single(s => s.Level == 5);
        Assert.True((spells[five.SpellId].Flags & Lifestone.Data.Magic.SpellFlags.Beneficial) != 0); // the dats: Heal Self is a beneficial spell

        var input = new EverySpellInput
        {
            Knows = id => id == five.SpellId,
            Fact = id => spells.TryGetValue(id, out var info) ? new EverySpellFact(info.Name, info.School, info.Power, (info.Flags & Lifestone.Data.Magic.SpellFlags.Beneficial) != 0) : null, // as EverySpellKeep builds it
            Skill = _ => 270,
            Catalog = catalog,
            AtHome = _ => false,
            Purse = 1_000_000,
            PlanLines = new Dictionary<string, EverySpellPlanLine>(StringComparer.Ordinal) { ["Heal Self"] = new(LearnNeedKind.Known, Lifestone.Client.Session.Grounds.ScrivenerLineKind.Sustain, 5) },
        };

        var pick = EverySpellPlan.Judge(input, "Heal Self");

        Assert.Equal(EverySpellVerdict.PlanLine, pick.Verdict); // fixed (the owner, 2026-11-19): the plan's answer, V, stands; before the fix the rule bought VI
        var six = catalog.Stock.LineSpells("Heal Self").Single(s => s.Level == 6);
        Assert.InRange(SpellPlan.Chance(270, spells[six.SpellId].Power), 0.75, 0.95); // VI at 270: over the buff bar, under the fight's 0.95
    }
}
