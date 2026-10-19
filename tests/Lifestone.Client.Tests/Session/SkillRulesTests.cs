using System.Collections.Immutable;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Tests;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared;
using Xunit;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The one current-skill rule (<see cref="SkillRules"/>, M3 2.2 a2): the base and the current value of a skill, the attack and defence groups of a skill enchantment
/// (<c>TODO(g4-enchantment-affects-skills)</c>, closed in <see cref="EnchantmentRules.AffectsKey"/>), the experience a raise costs, and the session's read-only lookups (the spell, the XpTable, the SkillTable).
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class SkillRulesTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint SkillKind = SkillRules.SkillKind;
    private const uint AdditiveSkill = 0x2000000 | EnchantmentRules.Additive | 0x1000 | SkillKind;
    private const uint MeleeDefense = 6;
    private const uint MissileDefense = 7;
    private const uint MagicDefense = 15;
    private const uint Run = 24;
    private const uint WarMagic = 34;

    // Melee Defense's formula: (Coordination + Quickness) / 3 (attributes 4 and 3).
    private static readonly SkillTableFormula MeleeFormula = new(0, 1, 1, 3, 4, 3);

    private static EnchantmentDesc Make(ushort spell, ushort category, uint power, uint type, uint key, float value, EnchantmentBucket bucket, double start = 0, ushort layer = 1) =>
        new(spell, layer, category, 0, power, start, 1800.0, 0x50000001, 0f, 0f, 0.0, type, key, value, 0, bucket);

    private static PlayerQualities Character(ImmutableArray<EnchantmentDesc> enchantments, uint advancementClass = SkillRules.TrainedClass, uint skill = MeleeDefense, ushort ranks = 20, uint init = 10)
    {
        var q = new PlayerQualities { IsKnown = true, Enchantments = enchantments };
        q.Attributes[1] = new AttributeDesc(1, 0, 100, 0);
        q.Attributes[2] = new AttributeDesc(2, 0, 100, 0);
        q.Attributes[3] = new AttributeDesc(3, 0, 80, 0); // Quickness
        q.Attributes[4] = new AttributeDesc(4, 0, 90, 0); // Coordination
        q.Attributes[5] = new AttributeDesc(5, 0, 70, 0);
        q.Attributes[6] = new AttributeDesc(6, 0, 100, 0);
        q.Skills[skill] = new SkillDesc(skill, ranks, 1, advancementClass, 0, init, 0, 0.0);
        return q;
    }

    private static uint Current(PlayerQualities q, uint skill = MeleeDefense, uint minLevel = 2)
    {
        Assert.True(SkillRules.TryCurrent(q, skill, MeleeFormula, minLevel, out var value));
        return value;
    }

    [Fact]
    public void The_base_is_the_formula_over_the_raw_attributes_plus_the_start_level_and_the_ranks()
    {
        // (90 + 80) / 3 = 56.67 rounds to 57; plus the start level 10 and 20 ranks.
        var q = Character([]);
        Assert.True(SkillRules.TryBase(q, MeleeDefense, MeleeFormula, 2, out var baseValue));
        Assert.Equal(87u, baseValue);
        Assert.Equal(87u, Current(q));
    }

    [Fact]
    public void A_skill_below_its_min_level_has_no_formula_and_one_the_description_lacks_counts_as_untrained()
    {
        var below = Character([], advancementClass: SkillRules.UntrainedClass);
        Assert.True(SkillRules.TryBase(below, MeleeDefense, MeleeFormula, 2, out var b));
        Assert.Equal(30u, b); // start level and ranks only (InqSkillBaseLevel returns 0 under the min level)
        Assert.True(SkillRules.TryBase(below, MeleeDefense, MeleeFormula, 1, out b));
        Assert.Equal(87u, b);

        var missing = Character([]); // no Run record
        Assert.True(SkillRules.TryBase(missing, Run, MeleeFormula, 1, out b));
        Assert.Equal(57u, b); // retail's default: advancement class 1, no ranks
        Assert.False(SkillRules.TryBase(new PlayerQualities(), MeleeDefense, MeleeFormula, 1, out _)); // the description has not arrived
    }

    [Fact]
    public void An_aura_the_defence_skill_names_lifts_the_current_value_and_not_the_base()
    {
        // Aura of Defense (spell 2245): additive, skill kind, key 6, +40 (BuffStatMods).
        var row = BuffStatMods.BySpell[2245];
        var q = Character([Make(2245, 40, 100, row.Type | 0x2000000, row.Key, (float)row.Value, EnchantmentBucket.Additive)]);
        Assert.Equal(MeleeDefense, row.Key);
        Assert.Equal(127u, Current(q));
        Assert.True(SkillRules.TryBase(q, MeleeDefense, MeleeFormula, 2, out var baseValue));
        Assert.Equal(87u, baseValue);
    }

    [Fact]
    public void The_defender_aura_is_a_float_enchantment_and_leaves_the_skill_alone()
    {
        // The brief expected the Defender line to lift Melee Defense; its StatMod (1605, type 0x9008, key 169) is a float stat of the item (no skill bit), so no skill moves. The group clauses are tested below.
        var defender = BuffStatMods.BySpell[1605];
        Assert.Equal(0u, defender.Type & SkillKind);
        var q = Character([Make(1605, 70, 100, defender.Type | 0x2000000, defender.Key, (float)defender.Value, EnchantmentBucket.Additive)]);
        Assert.Equal(87u, Current(q));
    }

    [Theory]
    [InlineData(EnchantmentRules.DefenseSkillsKind, MeleeDefense, true)]
    [InlineData(EnchantmentRules.DefenseSkillsKind, MissileDefense, true)]
    [InlineData(EnchantmentRules.DefenseSkillsKind, MagicDefense, true)]
    [InlineData(EnchantmentRules.DefenseSkillsKind, 0x30u, true)]
    [InlineData(EnchantmentRules.DefenseSkillsKind, Run, false)]
    [InlineData(EnchantmentRules.DefenseSkillsKind, WarMagic, false)]
    [InlineData(EnchantmentRules.AttackSkillsKind, WarMagic, true)]
    [InlineData(EnchantmentRules.AttackSkillsKind, 0x21u, true)]
    [InlineData(EnchantmentRules.AttackSkillsKind, 0x31u, true)]
    [InlineData(EnchantmentRules.AttackSkillsKind, MeleeDefense, false)]
    [InlineData(EnchantmentRules.AttackSkillsKind, Run, false)]
    [InlineData(0u, MeleeDefense, false)]
    public void A_group_enchantment_lifts_the_skills_of_its_group_only(uint groupBit, uint skill, bool lifted)
    {
        // Enchantment::AffectsDefenseSkills and AffectsAttackSkills: the group bit in the type, and the skill one of the group's ids; its own key (0) names none of them.
        var q = Character([Make(900, 77, 100, AdditiveSkill | groupBit, 0, 25f, EnchantmentBucket.Additive)], skill: skill, init: 0, ranks: 0);
        Assert.True(SkillRules.TryBase(q, skill, MeleeFormula, 1, out var baseValue));
        Assert.Equal(lifted ? baseValue + 25 : baseValue, Current(q, skill, 1));
    }

    [Fact]
    public void A_group_enchantment_does_not_touch_an_attribute_that_the_cull_asks_for()
    {
        // The cull's kind bit comes first: a skill-kind enchantment never reaches the attributes, whatever its group.
        var group = Make(900, 77, 100, AdditiveSkill | EnchantmentRules.DefenseSkillsKind, 0, 25f, EnchantmentBucket.Additive);
        Assert.Equal(90u, EnchantmentRules.EnchantAttribute([group], 4, 90));
        Assert.True(EnchantmentRules.AffectsKey(in group, MeleeDefense));
        Assert.False(EnchantmentRules.AffectsKey(in group, 4));
    }

    [Fact]
    public void Only_the_strongest_enchantment_of_a_category_counts_and_the_multipliers_and_the_vitae_scale_before_the_additions()
    {
        var weak = Make(901, 30, 50, AdditiveSkill, MeleeDefense, 10f, EnchantmentBucket.Additive, layer: 1);
        var strong = Make(902, 30, 200, AdditiveSkill, MeleeDefense, 40f, EnchantmentBucket.Additive, layer: 2); // same category: beats the weak one
        var other = Make(903, 31, 10, AdditiveSkill, MeleeDefense, 5.9f, EnchantmentBucket.Additive); // another category: truncated to 5
        var times = Make(904, 32, 100, EnchantmentRules.Multiplicative | 0x1000 | SkillKind, MeleeDefense, 1.1f, EnchantmentBucket.Multiplicative);
        var vitae = new EnchantmentDesc(EnchantmentRules.VitaeSpellId, 0, 99, 0, 1, 0.0, -1.0, 0x50000001, 0f, 0f, 0.0, EnchantmentRules.VitaeKind | EnchantmentRules.Multiplicative, 0, 0.95f, 0, EnchantmentBucket.Vitae);
        var q = Character([times, weak, strong, other, vitae]);

        // 87 x 1.1 x 0.95 = 90.9 (the floats as the wire carries them), plus 40 and 5 = 135.9, rounded.
        Assert.Equal(136u, Current(q));
        SkillRules.Modifiers(q.Enchantments, MeleeDefense, out var product, out var additive);
        Assert.Equal(1.1, product, 5);
        Assert.Equal(45, additive);
    }

    [Fact]
    public void The_magic_skills_still_read_the_same_number_through_GearCasts()
    {
        // GearCasts.TryCurrentSkill calls SkillRules with the magic formula (Focus + Self) / 4: Focus 70, Self 100 = 42.5 -> 43, plus 10 + 100 ranks.
        var q = Character([], skill: 31, ranks: 100, init: 10);
        Assert.True(GearCasts.TryCurrentSkill(q, 31, out var value, out _));
        Assert.Equal(153u, value);
        Assert.Equal(value, GearCasts.SkillValue(70, 100, 110, 1.0, null, 0));
    }

    [Fact]
    public void The_augmentation_bonuses_join_the_base_and_the_current_as_ACE_adds_them()
    {
        // m3-skill-aug-bonus: ACE CreatureSkill.GetAugBonus_Base (LumAugAllSkills 365, the melee/missile/magic augmentations 300-302 at +10 a level on their group, Enlightenment 390 on a trained skill) joins the base and is
        // scaled; GetAugBonus_Current (Jack of All Trades 326 at +5 a level, LumAugSkilledSpec 344 at +2 a level on a specialised skill) is added after the vitae. The plain value here is (90 + 80) / 3 = 57, + 10 + 20 ranks = 87.
        const uint LightWeapons = 45;
        var plain = Character([], skill: LightWeapons);
        Assert.Equal(87u, Current(plain, LightWeapons));

        var q = Character([], skill: LightWeapons);
        q.Ints[SkillRules.AugSkilledMeleeProperty] = 2;
        Assert.True(SkillRules.TryBase(q, LightWeapons, MeleeFormula, 2, out var baseValue));
        Assert.Equal(107u, baseValue);
        Assert.Equal(107u, Current(q, LightWeapons));

        // A skill outside the group gets none of it.
        var defence = Character([]);
        defence.Ints[SkillRules.AugSkilledMeleeProperty] = 2;
        Assert.Equal(87u, Current(defence));

        // The credit augmentation counts for every skill; Enlightenment only from Trained up.
        var all = Character([], skill: LightWeapons);
        all.Ints[SkillRules.LumAugAllSkillsProperty] = 3;
        all.Ints[SkillRules.EnlightenmentProperty] = 2;
        Assert.Equal(92u, Current(all, LightWeapons));
        var untrained = Character([], advancementClass: SkillRules.UntrainedClass, skill: LightWeapons);
        untrained.Ints[SkillRules.LumAugAllSkillsProperty] = 3;
        untrained.Ints[SkillRules.EnlightenmentProperty] = 2;
        Assert.Equal(10u + 20u + 3u, Current(untrained, LightWeapons)); // no formula part below Trained, no Enlightenment

        // The current bonuses come after the enchantments' product and add to the additive ones; the base is not touched.
        var late = Character([Make(902, 30, 200, AdditiveSkill, LightWeapons, 40f, EnchantmentBucket.Additive)], advancementClass: SkillRules.SpecializedClass, skill: LightWeapons);
        late.Ints[SkillRules.JackOfAllTradesProperty] = 2;
        late.Ints[SkillRules.LumAugSkilledSpecProperty] = 3;
        Assert.Equal(87u + 40u + 10u + 6u, Current(late, LightWeapons));
        Assert.True(SkillRules.TryBase(late, LightWeapons, MeleeFormula, 2, out var lateBase));
        Assert.Equal(87u, lateBase);
    }

    [Fact]
    public void The_registry_walk_allocates_nothing()
    {
        var q = Character([Make(902, 30, 200, AdditiveSkill, MeleeDefense, 40f, EnchantmentBucket.Additive), Make(904, 32, 100, EnchantmentRules.Multiplicative | SkillKind, MeleeDefense, 1.1f, EnchantmentBucket.Multiplicative)]);
        var registry = q.Enchantments;
        AllocationAssert.Nothing(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                SkillRules.Modifiers(registry, MeleeDefense, out _, out _);
            }
        });

        var plain = Character([]);
        AllocationAssert.Nothing(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                SkillRules.TryCurrent(plain, MeleeDefense, MeleeFormula, 2, out _);
            }
        });
    }

    // ------------------------------------------------------------------ the experience a raise costs (the real XpTable)

    [RequiresLocalDatsFact]
    public void A_raise_costs_the_next_ranks_cumulative_experience_less_what_is_spent_and_nothing_at_the_cap()
    {
        var xp = XpTableData.Load(fixture.Dats);
        var trained = xp.TrainedSkillXpList;
        var skill = new SkillDesc(34, 100, 1, SkillRules.TrainedClass, trained[100] + 5, 10, 0, 0.0);
        Assert.Equal(trained[101] - (trained[100] + 5), SkillRules.CostToRaise(xp, in skill));
        Assert.Equal(trained[110] - (trained[100] + 5), SkillRules.CostToRaise10(xp, in skill));

        var specialized = skill with { AdvancementClass = SkillRules.SpecializedClass, Experience = xp.SpecializedSkillXpList[100] };
        Assert.Equal(xp.SpecializedSkillXpList[101] - xp.SpecializedSkillXpList[100], SkillRules.CostToRaise(xp, in specialized));

        var cap = (ushort)(trained.Count - 1);
        var nearCap = skill with { Ranks = (ushort)(cap - 3), Experience = trained[cap - 3] };
        Assert.Equal(trained[cap] - trained[cap - 3], SkillRules.CostToRaise10(xp, in nearCap)); // fewer than ten ranks left
        var atCap = skill with { Ranks = cap, Experience = trained[cap] };
        Assert.Equal(0u, SkillRules.CostToRaise(xp, in atCap));
        Assert.Equal(0u, SkillRules.CostToRaise10(xp, in atCap));
        var half = skill with { Experience = (uint)(trained[100] + ((trained[101] - trained[100]) / 2)) };
        Assert.InRange(SkillRules.RankProgress(xp, in half), 0.49f, 0.51f); // the footer meter: halfway through rank 100
        Assert.Equal(0f, SkillRules.RankProgress(xp, in atCap));
        var untrained = skill with { AdvancementClass = SkillRules.UntrainedClass };
        Assert.Equal(0u, SkillRules.CostToRaise(xp, in untrained));
    }

    // ------------------------------------------------------------------ the session's lookups

    [RequiresLocalDatsFact]
    public async Task The_session_answers_the_spell_the_XpTable_and_the_SkillTable_once_the_host_gave_it_the_dats()
    {
        using var session = GameSession.Offline(static _ => { }, new ManualTimeProvider(), NullLogSink.Instance);
        IGameSessionView view = session;
        Assert.Null(view.Spell(2)); // before the dats: null, not a throw
        Assert.Null(view.XpTable);
        Assert.Null(view.SkillTable);

        session.UseSpellsFrom(fixture.Dats);
        await session.SpellTableLoaded;
        Assert.True(session.SpellTableReady);
        Assert.NotNull(view.XpTable);
        Assert.True(view.SkillTable!.TryGetSkill(WarMagic, out var warMagic));
        Assert.Equal("War Magic", warMagic.Name);
        var strength = view.Spell(2);
        Assert.NotNull(strength);
        Assert.Contains("Strength", strength.Name, StringComparison.Ordinal);
        Assert.Null(view.Spell(0xFFFFFF)); // no such spell
        AllocationAssert.Nothing(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                _ = view.Spell(2);
            }
        });
    }
}
