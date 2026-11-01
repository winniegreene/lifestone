using System.Collections.Immutable;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests.Session;
using Lifestone.Client.Tests.Session.Fighter;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 7 F30 (the owner): the wands and cantrip pieces the level-80 mage keeps aside and never sells (<see cref="KeepAsideRule"/>): a caster with a War Magic bonus of +3% or more and a wearable with a major cantrip it wants are kept
/// whatever the value, burden and payout floors say; a +2% wand and a minor cantrip are not; a full pack still stops the keep.
/// </summary>
public sealed class KeepAsideRuleTests
{
    private const uint Caster = 0x8000;
    private const uint Jewelry = 0x8;
    private const uint Melee = 0x1;
    private const uint MajorFocus = 3001;
    private const uint MinorFocus = 3002;
    private const uint MajorHeavyAptitude = 3003;
    private const uint MajorArmor = 3004;
    private const uint EpicWillpower = 3005;
    private const uint MajorWillpower = 3006;

    private static readonly Dictionary<uint, SpellInfo> Spells = new()
    {
        [MajorFocus] = Named(MajorFocus, "Major Focus"),
        [MinorFocus] = Named(MinorFocus, "Minor Focus"),
        [MajorHeavyAptitude] = Named(MajorHeavyAptitude, "Major Heavy Weapon Aptitude"),
        [MajorArmor] = Named(MajorArmor, "Major Armor"),
        [EpicWillpower] = Named(EpicWillpower, "Epic Willpower"),
        [MajorWillpower] = Named(MajorWillpower, "Major Willpower"),
    };

    private static SpellInfo Named(uint id, string name) => WorldActionRig.Spell(id, SpellType.Enchantment) with { Name = name };

    private static MageLootThresholds Den() => new FightOptions { Loot = true }.EffectiveLootThresholds;

    private static MageLootRules DenRules() => new(Den(), Spells);

    internal static Appraisal Appraised(uint guid, double? elementalMod = null, params uint[] spells) => new(Answer(guid, elementalMod, spells), 1, 0);

    internal static IdentifyResponseMsg Answer(uint guid, double? elementalMod = null, params uint[] spells)
    {
        var floats = ImmutableDictionary<uint, double>.Empty;
        if (elementalMod is { } mod)
        {
            floats = floats.Add(AppraisalIds.ElementalDamageMod, mod);
        }

        return new IdentifyResponseMsg(
                guid,
                IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.FloatStatsTable | IdentifyResponseFlags.SpellBook,
                true,
                ImmutableDictionary<uint, int>.Empty,
                ImmutableDictionary<uint, long>.Empty,
                ImmutableDictionary<uint, bool>.Empty,
                floats,
                ImmutableDictionary<uint, string>.Empty,
                ImmutableDictionary<uint, uint>.Empty,
                [.. spells],
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
    }

    private static MageLootItem Item(MageLootRules rules, uint guid, string name, uint type, long value, long burden, Appraisal? appraisal) =>
        new MageLootItem(guid, name, type, value, burden) with { KeepAside = rules.Thresholds.KeepAside!.Match(type, appraisal, rules.SpellName) };

    [Fact]
    public void The_dens_rule_is_the_profiles_block()
    {
        var rule = Den().KeepAside!;

        Assert.Equal(3.0, rule.Setting.MinWarBonusPercent);
        Assert.Contains("Major Focus", rule.SpellNames);
        Assert.Contains("Major Magic Resistance", rule.SpellNames); // ACE's name for the Magic Defense cantrip
        Assert.Contains("Major Invulnerability", rule.SpellNames); // Melee Defense
        Assert.Contains("Major Impregnability", rule.SpellNames); // Missile Defense
        Assert.Contains("Major War Magic Aptitude", rule.SpellNames);
        Assert.DoesNotContain("Minor Focus", rule.SpellNames);
        Assert.Null(new FightOptions { Loot = true, Ground = "low-b-belt" }.EffectiveLootThresholds.KeepAside); // a ground without the block keeps nothing aside
    }

    [Fact]
    public void A_plus_3_percent_wand_is_kept_below_every_floor_and_a_plus_2_percent_wand_is_not()
    {
        var rules = DenRules();
        var plus3 = Item(rules, 2, "Piercing Baton of Force Bolt", Caster, value: 300, burden: 50, Appraised(2, 1.03));
        var plus2 = Item(rules, 3, "Plain Wand", Caster, value: 300, burden: 50, Appraised(3, 1.02));

        var plan = rules.Plan("Corpse of Tusker Guard", "Amy", [plus3, plus2], burden: 0, capacity: 5000, freeSlots: 20);

        Assert.Equal("War Magic bonus +3%", plus3.KeepAside);
        Assert.Null(plus2.KeepAside); // +2% is under the profile's 3
        Assert.Equal([2u], plan.Kept); // Value 300, v/b 6 and a payout of 270: far under 60 and 4,000
        Assert.Equal(MageLootVerdict.KeepAside, plan.VerdictOf(2));
        Assert.Equal(MageLootVerdict.ValuePerBurden, plan.VerdictOf(3));
        Assert.Equal(1, plan.WorthALookCount);
        Assert.Contains("worth_a_look=1", plan.Summary(), StringComparison.Ordinal);
        Assert.Contains(plan.DecisionLines(), l => l.StartsWith("loot: kept Piercing Baton of Force Bolt", StringComparison.Ordinal) && l.Contains("worth a look", StringComparison.Ordinal));
        Assert.Equal(["loot: worth a look: 'Piercing Baton of Force Bolt' 0x00000002 (War Magic bonus +3%)"], plan.WorthALookLines());
    }

    [Fact]
    public void A_ring_with_a_major_Focus_cantrip_is_kept_whatever_it_is_worth_and_a_minor_cantrip_is_not()
    {
        var rules = DenRules();
        var major = Item(rules, 2, "Ring of Focus", Jewelry, value: 50, burden: 15, Appraised(2, null, MajorFocus));
        var minor = Item(rules, 3, "Minor Ring", Jewelry, value: 50, burden: 15, Appraised(3, null, MinorFocus));
        var wrongFamily = Item(rules, 4, "Heavy Ring", Jewelry, value: 50, burden: 15, Appraised(4, null, MajorHeavyAptitude, MajorArmor));
        var epic = Item(rules, 5, "Epic Ring", Jewelry, value: 50, burden: 15, Appraised(5, null, EpicWillpower));
        var noAppraisal = Item(rules, 6, "Unread Ring", Jewelry, value: 50, burden: 15, null);

        var plan = rules.Plan("Corpse of Tusker Guard", "Amy", [major, minor, wrongFamily, epic, noAppraisal], burden: 0, capacity: 5000, freeSlots: 20);

        Assert.Equal("Major Focus", major.KeepAside);
        Assert.Null(minor.KeepAside); // only the profile's tiers count
        Assert.Null(wrongFamily.KeepAside); // a major cantrip the mage has no use for
        Assert.Equal("Epic Willpower", epic.KeepAside); // the den's block names Epic and Legendary too
        Assert.Null(noAppraisal.KeepAside); // the spells come from an appraisal only
        Assert.Equal([2u, 5u], plan.Kept);
        Assert.Equal(2, plan.WorthALookCount);
        Assert.Equal([2u, 5u], plan.WorthALookGuids);
    }

    [Fact]
    public void A_bonus_and_a_cantrip_on_one_item_are_both_named_and_a_melee_weapon_with_a_bonus_is_not_a_caster()
    {
        var rules = DenRules();

        var both = Item(rules, 2, "Sceptre", Caster, 100, 50, Appraised(2, 1.05, MajorFocus));
        var melee = Item(rules, 3, "Sword", Melee, 100, 50, Appraised(3, 1.10));

        Assert.Equal("War Magic bonus +5%; Major Focus", both.KeepAside);
        Assert.Null(melee.KeepAside);
        Assert.False(KeepAsideRule.NeedsAppraisal(Melee)); // not asked about either
        Assert.True(KeepAsideRule.NeedsAppraisal(Caster) && KeepAsideRule.NeedsAppraisal(Jewelry) && KeepAsideRule.NeedsAppraisal(0x2) && KeepAsideRule.NeedsAppraisal(0x4));
    }

    [Fact]
    public void A_full_pack_or_the_load_line_still_stops_a_keep_aside_item_and_the_line_says_so()
    {
        var rules = DenRules();
        var wand = Item(rules, 2, "Sceptre", Caster, 100, 50, Appraised(2, 1.03));

        var full = rules.Plan("Corpse of Tusker Guard", "Amy", [wand], burden: 0, capacity: 5000, freeSlots: 0);
        var heavy = rules.Plan("Corpse of Tusker Guard", "Amy", [wand], burden: 4990, capacity: 5000, freeSlots: 20);

        Assert.Empty(full.Kept);
        Assert.Equal(MageLootVerdict.Slots, full.VerdictOf(2));
        Assert.Equal(["loot: worth a look: 'Sceptre' 0x00000002 (War Magic bonus +3%); left: no free slot"], full.WorthALookLines());
        Assert.Equal(MageLootVerdict.Load, heavy.VerdictOf(2));
        Assert.Equal(0, full.WorthALookCount); // counted when kept
    }

    [Fact]
    public void The_keep_aside_items_are_taken_before_the_ranked_ones()
    {
        var rules = DenRules();
        var rich = new MageLootItem(2, "Rich Armour", 0x2, 50_000, 100);
        var wand = Item(rules, 3, "Sceptre", Caster, 100, 50, Appraised(3, 1.03));

        var plan = rules.Plan("Corpse of Tusker Guard", "Amy", [rich, wand], burden: 0, capacity: 5000, freeSlots: 1);

        Assert.Equal([3u], plan.Kept); // the one free slot goes to the wand
        Assert.Equal(MageLootVerdict.Slots, plan.VerdictOf(2));
    }

    [Fact]
    public void The_tally_counts_for_the_soak_line()
    {
        var before = KeepAsideTally.WorthALook;

        KeepAsideTally.Add();

        Assert.True(KeepAsideTally.WorthALook >= before + 1);
    }

    [Fact]
    public void A_spell_name_the_table_lacks_is_named_so_a_typo_in_the_profile_is_not_silent()
    {
        var rule = new KeepAsideRule(new GroundKeepAside(3, ["Major"], ["Focus", "Wilpower"]));

        Assert.Equal(["Major Wilpower"], rule.NamesMissingFrom(Spells));
    }

    // ------------------------------------------------------------------------------------------------ D149: a heavy find only as a wearable upgrade

    /// <summary>The level-80 mage's numbers for the self-equip: War Magic 300, Arcane Lore 250.</summary>
    private sealed class Mage80 : IGearSkills
    {
        public int Level => 80;

        public int AdvancementClass(uint skill) => 3;

        public int? Current(uint skill) => skill switch { MageTemplate.WarMagic => 300, MageTemplate.ArcaneLore => 250, _ => null };

        public int? Base(uint skill) => Current(skill) - 20;
    }

    /// <summary>The worn set of the long run of 2026-10-30: the Faran Robe with Hood (0x7F01, ClothingPriority 0x17F00), the Leather Gauntlets casting Major Focus, the plain Wand in the hand.</summary>
    private static List<WornPiece> Worn() =>
    [
        new(0x80000001, "Faran Robe with Hood", 0x4, 0x7F01, 0x17F00, null),
        new(0x80000002, "Leather Gauntlets", 0x2, 0x20, 0x8000, SelfEquipTests.Appraised(0x80000002, null, null, MajorFocus)),
        new(0x80000005, "Wand", Caster, SelfEquipJudge.Held, 0, SelfEquipTests.Appraised(0x80000005)),
    ];

    /// <summary>A corpse's item with its locations and coverage, as the loot step reads it.</summary>
    private static WorldObject Find(uint guid, string name, uint type, ushort burden, uint locations, uint coverage = 0)
    {
        var basis = LootRulesTests.Item(type, 500, burden, guid, name);
        return new WorldObject(basis.Created with { Weenie = basis.Weenie with { ValidLocations = locations, Priority = coverage } }, isSelf: false, createdAt: 0);
    }

    /// <summary>The loot step's wiring: the keep-aside match and, for a heavy find, the self-equip judge against <see cref="Worn"/>; <paramref name="asked"/> collects the judge's calls.</summary>
    private static MageLootItem Judged(MageLootRules rules, WorldObject item, Appraisal appraisal, List<uint>? asked = null)
    {
        var judge = new SelfEquipJudge(rules.Thresholds.KeepAside!.SpellNames, SelfEquipJudge.ElementOf("Flame Bolt"), rules.SpellName);
        var worn = Worn();
        return rules.ItemFor(item, appraisal, held: false, c =>
        {
            asked?.Add(c.Guid);
            return judge.Judge(c, worn, new Mage80());
        });
    }

    private static Appraisal FireSceptre(uint guid, double bonus, int warReq) =>
        SelfEquipTests.Appraised(guid, new() { [45] = 0x10, [158] = 1, [159] = 34, [160] = warReq, [109] = 200 }, new() { [AppraisalIds.ElementalDamageMod] = bonus });

    [Fact]
    public void A_heavy_find_worth_a_look_that_is_no_wearable_upgrade_is_left_on_the_corpse_with_its_line_M5_7_D149()
    {
        var rules = DenRules();
        var shield = Judged(rules, Find(2, "Covenant Shield", 0x2, 600, 0x00200000), SelfEquipTests.Appraised(2, null, null, MajorWillpower)); // a shield: no slot a mage uses
        var bracelet = Judged(rules, Find(3, "Heavy Bracelet", Jewelry, 160, 0x00030000), SelfEquipTests.Appraised(3, null, null, MajorFocus)); // its cantrip is worn already
        var sceptre = Judged(rules, Find(4, "Fire Sceptre", Caster, 200, SelfEquipJudge.Held), FireSceptre(4, 1.05, warReq: 400)); // better, but the wield requirement is unmet
        var unjudged = rules.ItemFor(Find(5, "Covenant Greaves", 0x2, 425, 0x6000, 0x300), SelfEquipTests.Appraised(5, null, null, MajorWillpower), held: false); // no judge given: left, named

        var plan = rules.Plan("Corpse of Tusker Guard", "Amy", [shield, bracelet, sceptre, unjudged], burden: 0, capacity: 5000, freeSlots: 20);

        Assert.Equal(150, rules.Thresholds.MaxItemBurden); // the den profile's max_item_burden
        Assert.Empty(plan.Kept);
        Assert.All([2u, 3u, 4u, 5u], g => Assert.Equal(MageLootVerdict.HeavyNotUpgrade, plan.VerdictOf(g)));
        var lines = plan.WorthALookLines().ToArray();
        Assert.Equal(4, lines.Length);
        Assert.Equal("loot: worth a look but heavy and not an upgrade: left 'Covenant Shield' 0x00000002 burden 600 (Major Willpower; review: no free slot for Major Willpower: its locations 0x00200000 are no jewellery slot and it has no coverage)", lines[0]);
        Assert.Equal("loot: worth a look but heavy and not an upgrade: left 'Heavy Bracelet' 0x00000003 burden 160 (Major Focus; review: its cantrips (Major Focus) are cast by a worn piece already)", lines[1]);
        Assert.StartsWith("loot: worth a look but heavy and not an upgrade: left 'Fire Sceptre' 0x00000004 burden 200 (War Magic bonus +5%; review: better but wield: skill 34 current 300 < 400", lines[2], StringComparison.Ordinal);
        Assert.Equal("loot: worth a look but heavy and not an upgrade: left 'Covenant Greaves' 0x00000005 burden 425 (Major Willpower; not judged: the loot step gave no self-equip judge)", lines[3]);
        Assert.Equal(0, plan.WorthALookCount); // none goes to the review pack, so the sale's keep-aside list gains nothing
        Assert.Contains("heavy_not_upgrade=4", plan.Summary(), StringComparison.Ordinal);
        Assert.Contains(plan.DecisionLines(), l => l.StartsWith("loot: left Covenant Shield", StringComparison.Ordinal) && l.EndsWith(", worth a look but heavy and not an upgrade)", StringComparison.Ordinal));
        Assert.Equal(LootDecision.Burden, plan.Decide(Find(2, "Covenant Shield", 0x2, 600, 0x00200000), new LootContext(Burden: 0, Capacity: 5000, Inventory: null)));
    }

    [Fact]
    public void A_heavy_find_the_self_equip_rates_an_upgrade_is_kept_aside_and_its_line_says_why_M5_7_D149()
    {
        var rules = DenRules();
        var sceptre = Judged(rules, Find(2, "Fire Sceptre", Caster, 200, SelfEquipJudge.Held), FireSceptre(2, 1.05, warReq: 250)); // swapped in for the plain Wand
        var compass = Judged(rules, Find(3, "Compass", Jewelry, 180, 0x04000000), SelfEquipTests.Appraised(3, null, null, MajorWillpower)); // the trinket slot is free and no worn piece casts it

        var plan = rules.Plan("Corpse of Tusker Guard", "Amy", [sceptre, compass], burden: 0, capacity: 5000, freeSlots: 20);

        Assert.Equal(SelfEquipKind.Swap, sceptre.Gear!.Kind);
        Assert.Equal(SelfEquipKind.Wear, compass.Gear!.Kind);
        Assert.Equal([2u, 3u], plan.WorthALookGuids.Order());
        Assert.Equal(MageLootVerdict.KeepAside, plan.VerdictOf(2));
        Assert.Equal(MageLootVerdict.KeepAside, plan.VerdictOf(3));
        var lines = plan.WorthALookLines().ToArray();
        Assert.StartsWith("loot: worth a look: 'Fire Sceptre' 0x00000002 (War Magic bonus +5%); heavy (burden 200 over 150) but an upgrade: swap in: better: war damage x1.05 against x1", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("loot: worth a look: 'Compass' 0x00000003 (Major Willpower); heavy (burden 180 over 150) but an upgrade: wear: casts Major Willpower that no worn piece casts, at the free location 0x04000000", lines[1], StringComparison.Ordinal);
        Assert.DoesNotContain("heavy_not_upgrade", plan.Summary(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_light_find_worth_a_look_is_kept_aside_without_asking_the_self_equip_even_one_it_could_not_wear_M5_7_D149()
    {
        var rules = DenRules();
        var asked = new List<uint>();
        var sleeves = Judged(rules, Find(2, "Studded Leather Sleeves", 0x2, 150, 0x1800, 0x1000), SelfEquipTests.Appraised(2, null, null, MajorWillpower), asked); // exactly the cap, not over it; the robe covers the arms
        var ring = Judged(rules, Find(3, "Ring", Jewelry, 10, 0x000C0000), SelfEquipTests.Appraised(3, null, null, MajorFocus), asked); // its cantrip is worn already
        var wand = Judged(rules, Find(4, "Plain Sceptre", Caster, 50, SelfEquipJudge.Held), FireSceptre(4, 1.03, warReq: 400), asked); // a +3% caster the mage cannot wield yet

        var plan = rules.Plan("Corpse of Tusker Guard", "Amy", [sleeves, ring, wand], burden: 0, capacity: 5000, freeSlots: 20);

        Assert.Empty(asked); // F30 unchanged for the light finds
        Assert.All([sleeves, ring, wand], i => Assert.Null(i.Gear));
        Assert.Equal([2u, 3u, 4u], plan.WorthALookGuids.Order());
        Assert.Contains("loot: worth a look: 'Studded Leather Sleeves' 0x00000002 (Major Willpower)", plan.WorthALookLines());
    }

    [Fact]
    public void A_heavy_piece_the_robes_coverage_forbids_is_no_upgrade_and_is_left_M5_7_D149_D129()
    {
        var rules = DenRules();
        var breastplate = Judged(rules, Find(2, "Celdon Breastplate", 0x2, 900, 0x00000200, 0x400), SelfEquipTests.Appraised(2, null, null, MajorWillpower)); // the long run's: chest, under the robe
        var boots = Judged(rules, Find(3, "Heavy Boots", 0x2, 300, SelfEquipJudge.FootWear, SelfEquipJudge.FeetCoverage), SelfEquipTests.Appraised(3, null, null, MajorWillpower)); // no shoes under the robe

        var plan = rules.Plan("Corpse of Tusker Guard", "Amy", [breastplate, boots], burden: 0, capacity: 5000, freeSlots: 20);

        Assert.Empty(plan.Kept);
        var lines = plan.WorthALookLines().ToArray();
        Assert.Equal("loot: worth a look but heavy and not an upgrade: left 'Celdon Breastplate' 0x00000002 burden 900 (Major Willpower; review: no free slot for Major Willpower: the worn 'Faran Robe with Hood' 0x80000001 covers 0x400 of its coverage 0x400 (ACE: \"You must remove your Faran Robe with Hood to wield Celdon Breastplate\"))", lines[0]);
        Assert.Equal("loot: worth a look but heavy and not an upgrade: left 'Heavy Boots' 0x00000003 burden 300 (Major Willpower; review: no free slot for Major Willpower: the worn 'Faran Robe with Hood' 0x80000001 covers 0x10000 of its coverage 0x10000 (ACE: \"You must remove your Faran Robe with Hood to wield Heavy Boots\"))", lines[1]);
    }
}
