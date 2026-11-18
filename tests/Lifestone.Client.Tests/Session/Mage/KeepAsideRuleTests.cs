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
using Lifestone.Data.Tests;
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
    private const uint MajorFocus = 2574; // M6 14 useful cantrips: the real spell ids (ACE's spell table), so the judge reads what each raises
    private const uint MinorFocus = 2581;
    private const uint MajorHeavyAptitude = 2531;
    private const uint MajorArmor = 2571;
    private const uint EpicWillpower = 4227;
    private const uint MajorWillpower = 2577;

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
        rules.Thresholds.KeepAside!.Match(type, appraisal, rules.SpellName, UsefulCantripTests.Qwinnie) is { } why ? new MageLootItem(guid, name, type, value, burden) with { KeepAside = why, Look = true } : new MageLootItem(guid, name, type, value, burden); // the rule's match (M6 14: Look, the soak line's worth a look)

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
        Assert.Null(wrongFamily.KeepAside); // M6 14 useful cantrips: Heavy Weapons she neither holds nor plans, and Major Armor raises body armour: not useful
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
    public void The_loot_plan_takes_a_keep_aside_piece_before_heavier_lower_value_loot_at_the_load_line_M6_14()
    {
        // The climber on the Barracks (no keep_aside block: her default runs) at 60 burden under the load line (100% of 17,850): a Major Focus ring (burden 15, Value 500) beside a mana stone the kit wants (burden 50, Value 40) and a
        // silver ewer (burden 60, Value 400). Before M6 14 the kit's stones went first and took the room; the ring was left over the line.
        var rules = new MageLootRules(new FightOptions { Loot = true, Ground = "tusker-barracks", KeepAsideDefault = KeepAsideRule.DefaultFor(MageTemplate.ClimberName) }.EffectiveLootThresholds, Spells);
        var stone = new MageLootItem(3, "Mana Stone", PackRules.ManaStoneType, 40, 50);
        var ewer = new MageLootItem(4, "Silver Ewer", 0x80, 400, 60);
        var ring = rules.ItemFor(Find(2, "Ring", Jewelry, 15, 0x000C0000), Appraised(2, null, MajorFocus), held: false);
        const int Capacity = 17_850, Burden = Capacity - 60;

        var plan = rules.Plan("Corpse of Tusker Guard", "Winnie", [stone, ewer, ring], Burden, Capacity, freeSlots: 20, stonesWanted: 1);

        Assert.Equal(("Major Focus", true), (ring.KeepAside, ring.Look));
        Assert.Equal([2u], plan.Kept); // the ring first; the stone and the ewer would carry her over the line after it
        Assert.Equal(MageLootVerdict.KeepAside, plan.VerdictOf(2));
        Assert.Equal(MageLootVerdict.Load, plan.VerdictOf(3));
        Assert.Equal(MageLootVerdict.Load, plan.VerdictOf(4));
        Assert.Equal(1, plan.WorthALookCount);

        // Any other character on the Barracks keeps nothing aside: the ring is plain loot, the stone the kit wants goes first, and the ring is left over the line.
        var plain = new MageLootRules(new FightOptions { Loot = true, Ground = "tusker-barracks", KeepAsideDefault = KeepAsideRule.DefaultFor("BSD Mage") }.EffectiveLootThresholds, Spells);
        var plainRing = plain.ItemFor(Find(2, "Ring", Jewelry, 15, 0x000C0000), Appraised(2, null, MajorFocus), held: false);
        var before = plain.Plan("Corpse of Tusker Guard", "BSD Mage", [stone, ewer, plainRing], Burden, Capacity, freeSlots: 20, stonesWanted: 1);
        Assert.Null(plainRing.KeepAside);
        Assert.Equal([3u], before.Kept);
        Assert.Equal(MageLootVerdict.Load, before.VerdictOf(2));
    }

    [Fact]
    public void The_tally_counts_for_the_soak_line()
    {
        var before = KeepAsideTally.WorthALook;
        var guid = 0x8F000000u + (uint)Environment.TickCount % 0x10000u;

        Assert.True(KeepAsideTally.Add(guid));
        Assert.False(KeepAsideTally.Add(guid)); // M6 14 (m6-lore-cantrips): a piece counts once over the process

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

    [Fact]
    public void A_scroll_the_keep_scroll_hook_names_is_kept_aside_and_never_sold_and_without_the_hook_it_is_plain_loot_M6_item_1()
    {
        const uint Writable = 0x00002000; // ItemType.Writable: a scroll
        var scroll = Find(7, "Scroll of Flame Bolt II", Writable, 5, 0);
        var hooked = new MageLootRules(Den() with { KeepScroll = o => o.Name.StartsWith("Scroll of Flame Bolt", StringComparison.Ordinal) ? "the plan's next level: read it" : null }, Spells);
        var plain = DenRules();

        var kept = hooked.ItemFor(scroll, appraisal: null, held: false);
        var other = hooked.ItemFor(Find(8, "Scroll of Frost Bolt II", Writable, 5, 0), appraisal: null, held: false);

        Assert.Equal("the plan's next level: read it", kept.KeepAside);
        Assert.Equal(MageLootVerdict.KeepAside, hooked.Plan("Corpse of Drudge Skulker", "Winnie", [kept], burden: 0, capacity: 1500, freeSlots: 20).VerdictOf(7));
        Assert.Null(other.KeepAside);
        Assert.Null(plain.ItemFor(scroll, appraisal: null, held: false).KeepAside);
    }

    private static Appraisal FireSceptre(uint guid, double bonus, int warReq) =>
        SelfEquipTests.Appraised(guid, new() { [45] = 0x10, [158] = 1, [159] = 34, [160] = warReq, [109] = 200 }, new() { [AppraisalIds.ElementalDamageMod] = bonus });

    [Fact]
    public void A_heavy_find_worth_a_look_that_is_no_wearable_upgrade_is_left_on_the_corpse_with_its_line_but_a_great_cantrip_piece_is_kept_M5_7_D149_M6_14()
    {
        var rules = DenRules();
        var shield = Judged(rules, Find(2, "Covenant Shield", 0x2, 600, 0x00200000), SelfEquipTests.Appraised(2, null, null, MajorWillpower)); // a shield: no slot a mage uses
        var bracelet = Judged(rules, Find(3, "Heavy Bracelet", Jewelry, 160, 0x00030000), SelfEquipTests.Appraised(3, null, null, MajorFocus)); // its cantrip is worn already
        var sceptre = Judged(rules, Find(4, "Fire Sceptre", Caster, 200, SelfEquipJudge.Held), FireSceptre(4, 1.05, warReq: 400)); // better, but the wield requirement is unmet
        var unjudged = rules.ItemFor(Find(5, "Covenant Greaves", 0x2, 425, 0x6000, 0x300), SelfEquipTests.Appraised(5, null, null, MajorWillpower), held: false); // no judge given: named

        var plan = rules.Plan("Corpse of Tusker Guard", "Amy", [shield, bracelet, sceptre, unjudged], burden: 0, capacity: 5000, freeSlots: 20);

        Assert.Equal(150, rules.Thresholds.MaxItemBurden); // the den profile's max_item_burden
        // M6 14 keep every great cantrip (the owner, 2026-11-15, amends D149): a heavy piece with a Major cantrip of the rule is kept whatever the self-equip says; the heavy rule still leaves the wand kept for its War Magic bonus
        Assert.Equal([3u, 5u, 2u], plan.Kept); // the great pieces, by value per burden
        Assert.All([2u, 3u, 5u], g => Assert.Equal(MageLootVerdict.KeepAside, plan.VerdictOf(g)));
        Assert.Equal(MageLootVerdict.HeavyNotUpgrade, plan.VerdictOf(4));
        var lines = plan.WorthALookLines().ToArray();
        Assert.Equal(4, lines.Length);
        Assert.Equal("loot: worth a look: 'Covenant Shield' 0x00000002 (Major Willpower); heavy (burden 600 over 150) and not an upgrade, but kept for its great cantrip (the owner, 2026-11-15): review: no free slot for Major Willpower: its locations 0x00200000 are no jewellery slot and it has no coverage", lines[0]);
        Assert.Equal("loot: worth a look: 'Heavy Bracelet' 0x00000003 (Major Focus); heavy (burden 160 over 150) and not an upgrade, but kept for its great cantrip (the owner, 2026-11-15): review: its cantrips (Major Focus) are cast by a worn piece already", lines[1]);
        Assert.StartsWith("loot: worth a look but heavy and not an upgrade: left 'Fire Sceptre' 0x00000004 burden 200 (War Magic bonus +5%; review: better but wield: skill 34 current 300 < 400", lines[2], StringComparison.Ordinal);
        Assert.Equal("loot: worth a look: 'Covenant Greaves' 0x00000005 (Major Willpower); heavy (burden 425 over 150) and not an upgrade, but kept for its great cantrip (the owner, 2026-11-15): not judged: the loot step gave no self-equip judge", lines[3]);
        Assert.Equal(3, plan.WorthALookCount);
        Assert.Contains("heavy_not_upgrade=1", plan.Summary(), StringComparison.Ordinal);
        Assert.Contains(plan.DecisionLines(), l => l.StartsWith("loot: left Fire Sceptre", StringComparison.Ordinal) && l.EndsWith(", worth a look but heavy and not an upgrade)", StringComparison.Ordinal));
        Assert.Contains(plan.DecisionLines(), l => l.StartsWith("loot: kept Covenant Shield", StringComparison.Ordinal) && l.EndsWith(", worth a look, a great cantrip)", StringComparison.Ordinal));
        Assert.Equal(LootDecision.Burden, plan.Decide(Find(4, "Fire Sceptre", Caster, 200, SelfEquipJudge.Held), new LootContext(Burden: 0, Capacity: 5000, Inventory: null)));
        Assert.Equal(LootDecision.Take, plan.Decide(Find(2, "Covenant Shield", 0x2, 600, 0x00200000), new LootContext(Burden: 0, Capacity: 5000, Inventory: null)));
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
        Assert.StartsWith("loot: worth a look: 'Fire Sceptre' 0x00000002 (War Magic bonus +5%); heavy (burden 200 over 150) but an upgrade: swap in: better: 'Fire Sceptre' 0x00000002 score 2.5 (war +5%", lines[0], StringComparison.Ordinal); // M6 14 (m6-caster): the caster score
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
    public void A_heavy_piece_the_robes_coverage_forbids_is_no_upgrade_but_its_great_cantrip_keeps_it_M5_7_D149_D129_M6_14()
    {
        var rules = DenRules();
        var breastplate = Judged(rules, Find(2, "Celdon Breastplate", 0x2, 900, 0x00000200, 0x400), SelfEquipTests.Appraised(2, null, null, MajorWillpower)); // the long run's: chest, under the robe
        var boots = Judged(rules, Find(3, "Heavy Boots", 0x2, 300, SelfEquipJudge.FootWear, SelfEquipJudge.FeetCoverage), SelfEquipTests.Appraised(3, null, null, MajorWillpower)); // no shoes under the robe

        var plan = rules.Plan("Corpse of Tusker Guard", "Amy", [breastplate, boots], burden: 0, capacity: 5000, freeSlots: 20);

        Assert.Equal([3u, 2u], plan.Kept); // M6 14 keep every great cantrip (the owner, 2026-11-15): kept for the review pack though the robe forbids them now
        var lines = plan.WorthALookLines().ToArray();
        Assert.Equal("loot: worth a look: 'Celdon Breastplate' 0x00000002 (Major Willpower); heavy (burden 900 over 150) and not an upgrade, but kept for its great cantrip (the owner, 2026-11-15): review: no free slot for Major Willpower: the worn 'Faran Robe with Hood' 0x80000001 covers 0x400 of its coverage 0x400 (ACE: \"You must remove your Faran Robe with Hood to wield Celdon Breastplate\")", lines[0]);
        Assert.Equal("loot: worth a look: 'Heavy Boots' 0x00000003 (Major Willpower); heavy (burden 300 over 150) and not an upgrade, but kept for its great cantrip (the owner, 2026-11-15): review: no free slot for Major Willpower: the worn 'Faran Robe with Hood' 0x80000001 covers 0x10000 of its coverage 0x10000 (ACE: \"You must remove your Faran Robe with Hood to wield Heavy Boots\")", lines[1]);
    }
}

/// <summary>
/// M6 2 a (<c>m5-keep-aside-spell-name</c>): every profile's keep-aside cantrip names against the SpellTable, so a typo fails here and not silently in a run (the fight's runtime guard stays, FightScenario.cs). The walk over the
/// profiles is dat-free; the table's check needs the dats and skips without them, as the other SpellTable tests.
/// </summary>
public sealed class KeepAsideProfileNamesTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [Fact]
    public void The_walk_reads_every_profile_with_a_block_and_names_each_missing_name_with_its_ground()
    {
        var grounds = new List<string>();

        var missing = KeepAsideRule.ProfileNamesMissing(GroundCatalog.All, new Dictionary<uint, SpellInfo>(), grounds);

        Assert.Contains("black-spawn-den-aluvian", grounds);
        Assert.Equal(GroundCatalog.All.Count(g => g.Loot?.KeepAside is not null), grounds.Count);
        Assert.Contains("black-spawn-den-aluvian: Major Focus", missing); // an empty table lacks every name: each is named with its ground
        Assert.All(missing, m => Assert.Contains(": ", m, StringComparison.Ordinal));
    }

    [RequiresDatsFact]
    public void Every_profiles_keep_aside_name_is_a_spell_of_the_SpellTable()
    {
        var table = SpellTableData.Load(fixture.Dats);
        var grounds = new List<string>();

        var missing = KeepAsideRule.ProfileNamesMissing(GroundCatalog.All, table.Spells, grounds);

        Assert.NotEmpty(grounds);
        Assert.Empty(missing);
    }
}
