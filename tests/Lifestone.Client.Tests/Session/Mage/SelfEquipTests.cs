using System.Collections.Immutable;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 7 D122, D125: what the mage puts on by itself (<see cref="SelfEquipJudge"/>), the footwear rule (<see cref="SelfEquipJudge.PickFootwear"/>) and the worn items' mana kept with stones (<see cref="ItemManaKeeper"/>). The finds are round 12's
/// "worth a look" (a +3% Fire Sceptre, an Acid Sceptre, a Compass with Major War Magic Aptitude, Scalemail Greaves with Major Item Enchantment Aptitude) and a ring with Major Arcane Prowess; the worn set is the database's (the Faran Robe
/// with Hood at 0x7F01, ClothingPriority 0x17F00; the Leather Gauntlets; the Shirt; the Wide Breeches; the plain Wand in the hand).
/// </summary>
public sealed class SelfEquipTests
{
    private const uint MajorWar = 4001, MajorArcane = 4002, MajorItem = 4003, MajorFocus = 4004;
    private const uint Fire = 0x10, Acid = 0x20;
    private const uint Me = 0x50000001;

    private static readonly Dictionary<uint, string> Names = new()
    {
        [MajorWar] = "Major War Magic Aptitude",
        [MajorArcane] = "Major Arcane Prowess",
        [MajorItem] = "Major Item Enchantment Aptitude",
        [MajorFocus] = "Major Focus",
    };

    private static readonly SelfEquipJudge Judge = new(
        ["Major War Magic Aptitude", "Major Arcane Prowess", "Major Item Enchantment Aptitude", "Major Focus"],
        SelfEquipJudge.ElementOf("Flame Bolt"),
        id => Names.GetValueOrDefault(id));

    private sealed class Mage(int war = 300, int lore = 250, int level = 80) : IGearSkills
    {
        public int Level => level;

        public int AdvancementClass(uint skill) => 3;

        public int? Current(uint skill) => skill switch { MageTemplate.WarMagic => war, MageTemplate.ArcaneLore => lore, _ => null };

        public int? Base(uint skill) => Current(skill) - 20;
    }

    internal static Appraisal Appraised(uint guid, Dictionary<uint, int>? ints = null, Dictionary<uint, double>? floats = null, params uint[] spells) => new(
        new IdentifyResponseMsg(
            guid,
            IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.FloatStatsTable | IdentifyResponseFlags.SpellBook,
            true,
            (ints ?? []).ToImmutableDictionary(),
            ImmutableDictionary<uint, long>.Empty,
            ImmutableDictionary<uint, bool>.Empty,
            (floats ?? []).ToImmutableDictionary(),
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
            null),
        1,
        0);

    private static List<WornPiece> Database(Appraisal? wand = null) =>
    [
        new(0x80000001, "Faran Robe with Hood", 0x4, 0x7F01, 0x17F00, null),
        new(0x80000002, "Leather Gauntlets", 0x2, 0x20, 0x8000, null),
        new(0x80000003, "Shirt", 0x4, 0x2 | 0x8 | 0x10, 0x68, null),
        new(0x80000004, "Wide Breeches", 0x4, 0x40 | 0x80, 0x13, null),
        new(0x80000005, "Wand", 0x8000, SelfEquipJudge.Held, 0, wand ?? Appraised(0x80000005)),
    ];

    private static GearCandidate Sceptre(uint guid, string name, uint element, double bonus, int warReq = 250, int lore = 200) =>
        new(guid, name, 0x8000, SelfEquipJudge.Held, 0, 50, Appraised(guid, new() { [45] = (int)element, [158] = 1, [159] = 34, [160] = warReq, [109] = lore }, new() { [AppraisalIds.ElementalDamageMod] = bonus }));

    [Fact]
    public void A_plus_3_percent_Fire_Sceptre_is_swapped_in_for_the_plain_wand_with_the_numbers_logged()
    {
        var choice = Judge.Judge(Sceptre(0x90000001, "Fire Sceptre", Fire, 1.03), Database(), new Mage());

        Assert.Equal(SelfEquipKind.Swap, choice.Kind);
        Assert.Equal(SelfEquipJudge.Held, choice.Location);
        Assert.Equal(0x80000005u, choice.Replaces);
        Assert.Contains("'Fire Sceptre' 0x90000001 score 1.5 (war +3%", choice.Why, StringComparison.Ordinal); // M6 14 (m6-caster): the caster score, 0.5 a percent of damage on the plan's element
        Assert.Contains("against 'Wand' 0x80000005 score 0 (war +0%", choice.Why, StringComparison.Ordinal);
        Assert.Contains("on the plan's element 0x10", choice.Why, StringComparison.Ordinal);
        Assert.Contains("wield: skill 34 current 300 >= 250, activation: Arcane Lore 250 >= 200", choice.Why, StringComparison.Ordinal);
        Assert.StartsWith("gear: swap in 'Fire Sceptre' 0x90000001: better:", choice.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_bonus_of_another_element_does_not_count_for_Flame_Bolt_so_the_sceptre_is_no_upgrade()
    {
        var choice = Judge.Judge(Sceptre(0x90000002, "Acid Sceptre", Acid, 1.03), Database(), new Mage());

        Assert.Equal(SelfEquipKind.None, choice.Kind); // ACE applies ElementalDamageMod only when the caster's DamageType is the spell's (WorldObject_Weapon.cs:382-401); the keep-aside rule keeps it in the review pack for its +3%
        Assert.Contains("not better: 'Acid Sceptre' 0x90000002 score 0 (war +0%", choice.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_better_caster_short_of_a_wield_requirement_is_kept_for_review()
    {
        var choice = Judge.Judge(Sceptre(0x90000003, "Fire Sceptre", Fire, 1.05, 400, 200), Database(), new Mage());

        Assert.Equal(SelfEquipKind.Review, choice.Kind);
        Assert.StartsWith("better but wield: skill 34 current 300 < 400", choice.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void M6_14_a_better_caster_short_of_an_activation_requirement_goes_on_with_its_spells_off()
    {
        // M6 14 (m6-caster): ACE wields it and refuses only its spells (TryActivateSpells, Player_Inventory.cs:313-335 @95ef426d), so its own bonuses count: a swap, named.
        var choice = Judge.Judge(Sceptre(0x90000003, "Fire Sceptre", Fire, 1.05, 250, 300), Database(), new Mage());

        Assert.Equal(SelfEquipKind.Swap, choice.Kind);
        Assert.EndsWith("its spells stay off (activation: Arcane Lore 250 < 300 (its spells would stay off; retail: \"Your Arcane Lore is too low to use that item's magic.\")), its own bonuses count", choice.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_compass_with_a_useful_major_cantrip_is_worn_in_the_free_trinket_slot()
    {
        var compass = new GearCandidate(0x90000004, "Compass", 0x8, 0x04000000, 0, 100, Appraised(0x90000004, new() { [109] = 150 }, null, MajorWar));

        var choice = Judge.Judge(compass, Database(), new Mage());

        Assert.Equal(SelfEquipKind.Wear, choice.Kind);
        Assert.Equal(0x04000000u, choice.Location);
        Assert.Contains("casts Major War Magic Aptitude that no worn piece casts", choice.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ring_takes_the_free_finger_and_a_cantrip_already_worn_is_not_worn_twice()
    {
        var ring = new GearCandidate(0x90000005, "Ring", 0x8, 0x00040000 | 0x00080000, 0, 10, Appraised(0x90000005, null, null, MajorArcane));
        var worn = Database();
        worn.Add(new WornPiece(0x80000006, "Gold Ring", 0x8, 0x00080000, 0, Appraised(0x80000006, null, null, MajorFocus)));

        var choice = Judge.Judge(ring, worn, new Mage());
        Assert.Equal(SelfEquipKind.Wear, choice.Kind);
        Assert.Equal(0x00040000u, choice.Location); // the right finger is taken: the left

        var same = new GearCandidate(0x90000006, "Other Ring", 0x8, 0x00040000 | 0x00080000, 0, 10, Appraised(0x90000006, null, null, MajorFocus));
        Assert.Equal(SelfEquipKind.Review, Judge.Judge(same, worn, new Mage()).Kind);
    }

    [Fact]
    public void Greaves_whose_coverage_the_robe_holds_have_no_free_slot_and_go_to_review()
    {
        var greaves = new GearCandidate(0x90000007, "Scalemail Greaves", 0x2, 0x6000, 0x300, 425, Appraised(0x90000007, null, null, MajorItem));

        var choice = Judge.Judge(greaves, Database(), new Mage());

        Assert.Equal(SelfEquipKind.Review, choice.Kind);
        Assert.Contains("the worn 'Faran Robe with Hood' 0x80000001 covers 0x300", choice.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void The_robe_covers_the_feet_so_no_shoe_is_worn_and_the_reason_is_logged()
    {
        var shoes = new GearCandidate(0x90000008, "Shoes", 0x4, SelfEquipJudge.FootWear, SelfEquipJudge.FeetCoverage, 90, Appraised(0x90000008, new() { [28] = 20 }));

        var pick = SelfEquipJudge.PickFootwear([shoes], Database(), new Mage(), out var why);

        Assert.Null(pick);
        Assert.StartsWith("feet covered by Faran Robe with Hood (0x80000001, location 0x00007F01, ClothingPriority 0x17F00 against Feet 0x10000)", why, StringComparison.Ordinal);
        Assert.Contains("(D129: the mage keeps the robe, as retail)", why, StringComparison.Ordinal);
    }

    [Fact]
    public void With_the_feet_free_the_best_armoured_shoe_whose_requirements_are_met_is_picked()
    {
        var worn = Database().Where(w => w.Name != "Faran Robe with Hood").ToList();
        var sandals = new GearCandidate(0x90000009, "Sandals", 0x4, SelfEquipJudge.FootWear, SelfEquipJudge.FeetCoverage, 90, Appraised(0x90000009, new() { [28] = 20 }));
        var loafers = new GearCandidate(0x9000000A, "Loafers", 0x4, SelfEquipJudge.FootWear, SelfEquipJudge.FeetCoverage, 120, Appraised(0x9000000A, new() { [28] = 40 }));
        var boots = new GearCandidate(0x9000000B, "Heavy Boots", 0x2, SelfEquipJudge.FootWear, SelfEquipJudge.FeetCoverage, 300, Appraised(0x9000000B, new() { [28] = 120, [158] = 7, [160] = 120 }));

        var pick = SelfEquipJudge.PickFootwear([sandals, loafers, boots], worn, new Mage(), out var why);

        Assert.Same(loafers, pick); // the boots need level 120
        Assert.StartsWith("'Loafers' 0x9000000A: armour level 40, burden 120, the feet are free; refused: 'Heavy Boots' 0x9000000B: wield: level 80 < 120", why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_worn_item_under_half_or_under_two_hours_is_refilled_with_the_least_stone_that_covers_the_need()
    {
        var low = new WornMana(1, "Compass", 400, 1000, -0.02);
        HeldStone[] stones = [new(10, "Greater Mana Stone", 5000), new(11, "Greater Mana Stone", 700), new(12, "Greater Mana Stone", null)];

        var step = ItemManaKeeper.Next([low, new WornMana(2, "Ring", 900, 1000, -0.01)], stones, [], Me);

        Assert.Equal(ItemManaAct.Refill, step.Act);
        Assert.Equal(11u, step.Stone); // the need is 700: the stone of 700 covers it
        Assert.Equal(Me, step.Target);
        Assert.StartsWith("gear mana: refill: the stone 0x0000000B on the player: 'Compass' 400/1000 (333 min left", step.ToString(), StringComparison.Ordinal);

        var draining = ItemManaKeeper.Next([new WornMana(3, "Amulet", 900, 1000, -0.2)], stones, [], Me); // 4,500 s left: under two hours
        Assert.Equal(ItemManaAct.Refill, draining.Act);
    }

    [Fact]
    public void An_empty_stone_drains_the_main_packs_junk_item_whose_mana_is_worth_most_over_its_sale_while_charged_stones_are_short()
    {
        var full = new WornMana(1, "Compass", 1000, 1000, -0.001);
        HeldStone[] stones = [new(10, "Greater Mana Stone", null, 2.0)];
        DrainTarget[] junk = [new(20, "Bracelet", 300, 600, 900), new(21, "Pantaloons", 1200, 1500, 5000), new(22, "Gem", 1200, 1200, 300)];

        var step = ItemManaKeeper.Next([full], stones, junk, Me);

        Assert.Equal(ItemManaAct.Capture, step.Act);
        Assert.Equal(10u, step.Stone);
        Assert.Equal(22u, step.Target); // the Gem's 2,400 captured points are worth 4,320 against its 300: the best margin (S2b)
        Assert.Contains("0 charged of 2 wanted", step.Why, StringComparison.Ordinal);
        Assert.Equal(ItemManaAct.None, ItemManaKeeper.Next([], stones, junk, Me).Act); // nothing worn needs mana: no loot is destroyed

        var none = ItemManaKeeper.Next([new WornMana(1, "Compass", 100, 1000, -0.01)], [new HeldStone(10, "Greater Mana Stone", null)], [], Me);
        Assert.Equal(ItemManaAct.None, none.Act);
        Assert.Contains("TODO(m5-stone-short)", none.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void The_cast_on_you_line_of_a_worn_item_reaches_the_sessions_chat_log()
    {
        var rig = new WorldActionRig();
        using var tap = new ChatLogTap(rig.H.R.Bus, rig.H.Log);

        rig.Raw(AceWireWriters.ServerMessage("Compass cast Major War Magic Aptitude on you, surpassing Minor War Magic Aptitude", 0x07)); // ACE's CreateEnchantment line for an item caster (WorldObject_Magic.cs:429-444), ChatMessageType.Magic
        rig.Raw(AceWireWriters.ServerMessage("Your Compass is low on Mana.", 0x07)); // CheckLowMana (Player_Tick.cs:662-679)

        var chat = rig.H.Log.Entries.Where(e => e.Category == ChatLogTap.LogCategory).Select(e => e.Line).ToList();
        Assert.Contains(chat, l => l.Contains("text=\"Compass cast Major War Magic Aptitude on you, surpassing Minor War Magic Aptitude\"", StringComparison.Ordinal));
        Assert.Contains(chat, l => l.Contains("text=\"Your Compass is low on Mana.\"", StringComparison.Ordinal));
    }
}
