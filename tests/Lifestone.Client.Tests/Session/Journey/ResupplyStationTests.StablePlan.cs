using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 14 T (live, 2026-11-12 05:23 to 05:24 UTC, a trip from the ground at level 15): the visit's plan read Stamina to Mana Self II as the dance line's target (Life Magic 206, a stamina of 89) and bought nothing; the visit then spent
/// 69,280 experience, and 80 s later the counters read level III (210, 92) and named a trip the visit had not served. The two reads are replayed here: the learn plan reads a line's level on her raw numbers with the lines she keeps
/// standing, never on the enchantments that happen to be on her, so the trip's read in town and the counters' read in the field give one answer; and a spend, which does change it, comes before the plan
/// (<c>LogisticsStationTests.M6_14_T_every_trip_spends_the_experience_before_the_buys_so_the_plan_reads_what_the_spend_leaves</c>).
/// </summary>
public sealed partial class ResupplyStationTests
{
    private const uint StaminaToManaI = 1676;
    private const uint StaminaToManaII = 1677;

    /// <summary>A beneficial additive enchantment of an attribute (<paramref name="skill"/> false) or a skill, each in a category of its own.</summary>
    private static EnchantmentDesc Line(ushort spell, ushort category, uint key, float points, bool skill = false) =>
        new(spell, 1, category, 0, 300, 0.0, 3600.0, 1, 0f, 0f, 0.0, EnchantmentRules.BeneficialKind | EnchantmentRules.Additive | 0x1000 | (skill ? 0x10u : EnchantmentRules.AttributeKind), key, points, 0, EnchantmentBucket.Additive);

    /// <summary>The buff bot's level VII lines the plan's skills and stamina read: Focus, Willpower, Endurance and the Life Magic and War Magic masteries, 40 points each.</summary>
    private static ImmutableArray<EnchantmentDesc> BotLines() =>
    [
        Line(2067, 40, MageTemplate.Focus, BuffBotCover.Boost),
        Line(2091, 41, MageTemplate.Self, BuffBotCover.Boost),
        Line(2061, 42, MageTemplate.Endurance, BuffBotCover.Boost),
        Line(2267, 50, MageTemplate.LifeMagic, BuffBotCover.Boost, skill: true),
        Line(2323, 51, MageTemplate.WarMagic, BuffBotCover.Boost, skill: true),
    ];

    /// <summary>
    /// The climber of the two reads, scaled to the plan's untrained Mana Conversion (the press of level II passes level I at a stamina of 110): level 15, Focus and Self 100, Endurance 40, a Max Stamina of 35 before the Endurance (75 with
    /// it, 115 with the bot's Endurance line), War and Life Magic trained, the book holding Flame Bolt I to III and the dance's level I. <paramref name="moreStamina"/> are Max Stamina ranks a spend buys.
    /// </summary>
    private static PlayerDescriptionMsg DanceClimber(ImmutableArray<EnchantmentDesc> on, uint moreStamina = 0) => new()
    {
        IntProperties = ImmutableDictionary<uint, int>.Empty.Add(PlayerQualities.LevelProperty, 15),
        Attributes = [new AttributeDesc(1, 0, 100, 0), new AttributeDesc(MageTemplate.Endurance, 30, 10, 0), new AttributeDesc(MageTemplate.Focus, 0, 100, 0), new AttributeDesc(MageTemplate.Self, 0, 100, 0)],
        Vitals = [new VitalDesc(MageTemplate.MaxStamina, 35 + moreStamina, 0, 0, 20)], // 20 held: what she holds never enters
        Skills = [new SkillDesc(MageTemplate.WarMagic, 68, 1, 2u, 0, 0u, 0, 0), new SkillDesc(MageTemplate.LifeMagic, 65, 1, 2u, 0, 0u, 0, 0)],
        Spells = ImmutableDictionary<uint, float>.Empty.Add(FlameBoltI, 2f).Add(FlameBoltII, 2f).Add(82, 2f).Add(StaminaToManaI, 2f),
        Enchantments = on,
    };

    private static Home DanceHome(ImmutableArray<EnchantmentDesc> on, bool bot, uint moreStamina = 0)
    {
        var home = new Home(Outskirts, 2_000_000, notes: false, description: DanceClimber(on, moreStamina));
        BuffBotCover.For(home.Vendor.State).Set(bot, "the test's");
        return home;
    }

    /// <summary>The plan as both callers ask it (<see cref="ScrivenerLearnPlan.Needs"/> with the ladder and the bot's cover): every line's answer in words.</summary>
    private static List<(string Line, LearnNeedKind Kind, int Target, string Why)> PlanOf(Home home)
    {
        var state = home.Vendor.State;
        var ladder = LadderLoader.WarMage;
        var rung = ScrivenerLearnPlan.RungOf(ladder, home.Ground.Key, state.Self.Qualities.Level)!;
        return [.. ScrivenerLearnPlan.Needs(home.Ground, rung, ladder.PlanCastChance, state.Self.Qualities, state.Self.Spellbook, ScrivenerCatalog.Default, ladder, BuffBotCover.For(state).Covering).Select(n => (n.Line.Name, n.Kind, n.Target, n.Why))];
    }

    private static void SamePlan(List<(string Line, LearnNeedKind Kind, int Target, string Why)> one, List<(string Line, LearnNeedKind Kind, int Target, string Why)> other)
    {
        Assert.Equal(one.Count, other.Count);
        for (var i = 0; i < one.Count; i++)
        {
            Assert.True(one[i] == other[i], $"line {i}: {one[i]} | {other[i]}");
        }
    }

    private static (LearnNeedKind Kind, int Target, string Why) DanceOf(Home home)
    {
        var need = PlanOf(home).Single(n => n.Line == "Stamina to Mana Self");
        return (need.Kind, need.Target, need.Why);
    }

    [Fact]
    public void M6_14_T_the_towns_read_and_the_fields_read_of_the_same_character_give_one_answer_whatever_is_on_her()
    {
        // In town at a trip's start the bot's lines may have lapsed (none on her); in the field they are up. Before: the maximum with what was on her, 75 against 115, and her current skills: the dance's level I "known" in town and level II
        // "due" in the field, a trip the visit then did not serve.
        var town = DanceHome([], bot: true);
        var field = DanceHome(BotLines(), bot: true);
        Assert.True(town.Vendor.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxStamina, out _, out var bare));
        Assert.True(field.Vendor.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxStamina, out _, out var buffed));
        Assert.Equal((75u, 115u), (bare, buffed)); // the old input differed between the two reads

        SamePlan(PlanOf(field), PlanOf(town)); // every line: the same kind, the same level, the same words
        var dance = DanceOf(town);
        Assert.Equal((LearnNeedKind.Due, 2), (dance.Kind, dance.Target));
        Assert.Contains("(standing), plan chance 0.95", dance.Why, StringComparison.Ordinal);
        Assert.Contains("at stamina 115 (the standing maximum) level II nets the most", dance.Why, StringComparison.Ordinal);
        Assert.DoesNotContain(PlanOf(town), n => n.Why.Contains("(current)", StringComparison.Ordinal)); // the bolt, the drains, Heal Self and the debuffs read the same standing skill

        // The counters (the field's caller) and the trip's plan (the town's) name the same scroll.
        var inTown = new LogisticsCounters(town.Ground, learnHolds: new LearnHolds()).LearnDue(town.Vendor.State);
        var inField = new LogisticsCounters(field.Ground, learnHolds: new LearnHolds()).LearnDue(field.Vendor.State);
        Assert.NotNull(inTown);
        Assert.Equal(inTown!.Spell!.SpellId, inField!.Spell!.SpellId);
        Assert.Equal(inTown.Why, inField.Why);
    }

    [Fact]
    public void M6_14_T_without_the_bot_the_lines_her_own_book_holds_are_what_stands_and_a_buff_on_her_from_elsewhere_changes_nothing()
    {
        // Her book holds no Focus, Willpower, mastery or Endurance line: nothing of hers stands, so the plan reads her raw numbers (a stamina of 75: level I, held) with or without the same five enchantments on her.
        var bare = DanceHome([], bot: false);
        var buffed = DanceHome(BotLines(), bot: false);

        SamePlan(PlanOf(bare), PlanOf(buffed));
        var dance = DanceOf(buffed);
        Assert.Equal((LearnNeedKind.Known, 1), (dance.Kind, dance.Target));
        Assert.Contains("at stamina 75 (the standing maximum) level I nets the most", dance.Why, StringComparison.Ordinal);
        Assert.True(ScrivenerLearnPlan.TryStandingStamina(buffed.Vendor.State.Self.Qualities, 0, out var standing));
        Assert.Equal(75u, standing);
    }

    [Fact]
    public void M6_14_T_a_spend_does_move_the_answer_which_is_why_the_visit_spends_before_it_plans()
    {
        // The live flip was real: 26 raises between the two reads. Replayed at the dance's own threshold: 35 more Max Stamina ranks (75 to 110 unbuffed) make level II the target, on the lines she holds alone.
        var before = DanceHome([], bot: false);
        var after = DanceHome([], bot: false, moreStamina: 40);

        Assert.Equal((LearnNeedKind.Known, 1), (DanceOf(before).Kind, DanceOf(before).Target));
        Assert.Equal((LearnNeedKind.Due, 2), (DanceOf(after).Kind, DanceOf(after).Target));

        // The counters see the new ranks without any other change (the Endurance and the vital's ranks are in their key): the same instance, the two states.
        var counters = new LogisticsCounters(before.Ground, learnHolds: new LearnHolds());
        var first = counters.LearnDue(before.Vendor.State);
        Assert.NotEqual(StaminaToManaII, first?.Spell?.SpellId ?? 0);
        Assert.Contains(PlanOf(after), n => n.Line == "Stamina to Mana Self" && n.Why.Contains("Stamina to Mana Self II", StringComparison.Ordinal));
    }
}
