using System.Collections.Immutable;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 7 S2b (D125, the owner's addition): a worn item's mana tracked from what ACE sends (no push as it burns: reads and the low and out lines), the drain weighed against the sale, the kit's mana stones kept under the floors, and the
/// activation requirements with retail's lines. The case is round 12's Compass (Major War Magic Aptitude, base mana 10: a point every 120 s, <c>ItemManaDatTests</c>).
/// </summary>
public sealed class ItemManaTests
{
    private const uint Me = 0x50000001;
    private const uint Compass = 0x80003A64;

    private static Appraisal Appraised(uint guid, Dictionary<uint, int>? ints = null, Dictionary<uint, double>? floats = null, Dictionary<uint, uint>? dids = null) => new(
        new IdentifyResponseMsg(
            guid,
            IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.FloatStatsTable,
            true,
            (ints ?? []).ToImmutableDictionary(),
            ImmutableDictionary<uint, long>.Empty,
            ImmutableDictionary<uint, bool>.Empty,
            (floats ?? []).ToImmutableDictionary(),
            ImmutableDictionary<uint, string>.Empty,
            (dids ?? []).ToImmutableDictionary(),
            [],
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

    private static Appraisal CompassAt(int mana) => Appraised(Compass, new() { [107] = mana, [108] = 160 }, new() { [5] = -1.0 / WornManaWatch.CompassSecondsPerPoint });

    [Fact]
    public void The_watch_predicts_the_Compass_burning_a_point_every_120_s_between_reads_and_logs_each_read_beside_its_prediction()
    {
        var log = new RecordingLogSink();
        var watch = new WornManaWatch(log, "autopilot");

        watch.Read(Compass, "Compass", CompassAt(160), now: 0);
        Assert.Equal(160, watch.Predicted(Compass, 0));
        Assert.Equal(155, watch.Predicted(Compass, 600)); // ACE burns -ManaRate x 5 s a heartbeat (Player_Tick.cs:624-660) and sends nothing as it falls
        watch.Read(Compass, "Compass", CompassAt(155), now: 600);

        var lines = log.Entries.Select(e => e.Message).ToList();
        Assert.Equal("gear mana: read 'Compass' 0x80003A64 160/160 (100%), burns 0.0083/s while its spells are on: 5.3 h left; the first read", lines[0]);
        Assert.Equal("gear mana: read 'Compass' 0x80003A64 155/160 (96.9%), burns 0.0083/s while its spells are on: 5.2 h left; predicted 155 from the read 600 s ago", lines[1]);
        Assert.Equal(2, watch.ReadCount);
    }

    [Fact]
    public void ACEs_low_and_out_lines_are_alarms_and_out_sets_the_prediction_to_zero()
    {
        var log = new RecordingLogSink();
        var watch = new WornManaWatch(log, "autopilot");
        watch.Read(Compass, "Compass", CompassAt(5), now: 0);

        Assert.True(watch.OnChat("Your Compass is low on Mana.", 10));
        Assert.Equal(4, watch.Predicted(Compass, 120));
        Assert.True(watch.OnChat("Your Compass is out of Mana.", 600));
        Assert.Equal(0, watch.Predicted(Compass, 600));
        Assert.False(watch.OnChat("You cast Flame Bolt VI on the Tusker Slave.", 601));

        Assert.Equal(2, watch.Alarms);
        Assert.Contains(log.Entries, e => e.Severity == LogSeverity.Warning && e.Message.StartsWith("gear mana: ALARM: the server says \"Your Compass is out of Mana.\"", StringComparison.Ordinal));
    }

    [Fact]
    public void A_drain_is_chosen_only_when_the_captured_mana_is_worth_more_than_the_payout_and_every_item_weighed_is_logged()
    {
        var moderate = new HeldStone(10, "Moderate Mana Stone", null, ItemManaKeeper.EfficiencyByWcid[27330]); // 1.2
        DrainTarget[] junk =
        [
            new(20, "Bracelet", 1000, 1200, 2000, Payout: 1800), // 1,200 mana: 2,160 pyreals against 1,800: drain
            new(21, "Pantaloons", 2000, 2400, 6000, Payout: 5400), // 2,400 mana: 4,320 against 5,400: sell
            new(22, "Necklace", 1500, 1500, 1000, Payout: 900), // 1,800 mana: 3,240 against 900: the best margin
        ];
        var weighed = new List<string>();

        var step = ItemManaKeeper.Next([new WornMana(1, "Compass", 160, 160, -1.0 / 120)], [moderate], junk, Me, weighed);

        Assert.Equal(ItemManaAct.Capture, step.Act);
        Assert.Equal(22u, step.Target);
        Assert.Equal(
            [
                "gear mana: weigh 'Bracelet' 0x00000014: 1000 mana -> 1200 in the stone, worth 2160 pyreals at 1.8 a point, against a payout of 1800: drain",
                "gear mana: weigh 'Pantaloons' 0x00000015: 2000 mana -> 2400 in the stone, worth 4320 pyreals at 1.8 a point, against a payout of 5400: sell",
                "gear mana: weigh 'Necklace' 0x00000016: 1500 mana -> 1800 in the stone, worth 3240 pyreals at 1.8 a point, against a payout of 900: drain",
            ],
            weighed);
        Assert.Contains("the stone 'Moderate Mana Stone' (efficiency 1.2)", step.Why, StringComparison.Ordinal);

        var sellOnly = ItemManaKeeper.Next([new WornMana(1, "Compass", 160, 160, -1.0 / 120)], [moderate], [junk[1]], Me);
        Assert.Equal(ItemManaAct.None, sellOnly.Act); // nothing worth draining: the loot is sold
    }

    [Fact]
    public void The_kit_keeps_mana_stones_under_the_floors_while_short_of_its_target_and_the_excess_goes_by_value()
    {
        var rules = new MageLootRules(new MageLootThresholds { MinValuePerBurden = 60, MinPayout = 4000 });
        MageLootItem[] items =
        [
            new(1, "Moderate Mana Stone", PackRules.ManaStoneType, 2500, 50, Wcid: 27330), // round 12 left 22 of these: v/b 50, payout 2,250
            new(2, "Mana Stone", PackRules.ManaStoneType, 1000, 50, Wcid: 2435),
            new(3, "Lesser Mana Stone", PackRules.ManaStoneType, 500, 50, Wcid: 2434),
        ];

        var plan = rules.Plan("Corpse of Tusker Slave", "BSD Mage", items, burden: 0, capacity: 20_000, freeSlots: 20, stonesWanted: 2);

        Assert.Equal([1u, 2u], plan.Kept);
        Assert.Equal(MageLootVerdict.KitStone, plan.VerdictOf(1));
        Assert.Equal(MageLootVerdict.ValuePerBurden, plan.VerdictOf(3)); // the kit's target is met: the excess goes by the value rules
        Assert.Contains("kit_stones=2", plan.Summary(), StringComparison.Ordinal);
        Assert.Contains(plan.DecisionLines(), l => l.StartsWith("loot: kept Moderate Mana Stone", StringComparison.Ordinal) && l.EndsWith(", for the kit's mana stones)", StringComparison.Ordinal));
        Assert.Equal(PackJob.Kit, new PackRules(null).JobOf(new PackItem(1, "Moderate Mana Stone", 27330, PackRules.ManaStoneType, 0), false, false));
    }

    private sealed class Mage : IGearSkills
    {
        public int Level => 80;

        public int Heritage => 1;

        public int AdvancementClass(uint skill) => skill == MageTemplate.MeleeDefense ? 1 : 3;

        public int? Current(uint skill) => skill switch { MageTemplate.ArcaneLore => 250, MageTemplate.MeleeDefense => 140, _ => 300 };

        public int? Base(uint skill) => Current(skill);
    }

    [Theory]
    [InlineData(115, 200, 37u, 6u, "activation: Melee Defense 140 < 200 (its spells would stay off; retail: \"Your Melee Defense is too low to use that item's magic.\")")] // the armour loot's limit (LootGenerationFactory_Magic.cs:177-203)
    [InlineData(369, 100, 0u, 0u, "activation: level 80 < 100 (its spells would stay off; retail: \"You are not high enough level to use that!\")")]
    [InlineData(188, 2, 0u, 0u, "activation: heritage 1 is not the item's 2 (its spells would stay off; retail: \"You must be of heritage 2 to use that item's magic.\")")]
    [InlineData(366, 6, 0u, 0u, "activation: Melee Defense untrained (its spells would stay off; retail: \"You must have Melee Defense trained to use that item's magic\")")]
    public void Each_activation_requirement_is_read_in_ACEs_order_with_retails_line(uint property, int value, uint did, uint didValue, string why)
    {
        var dids = did == 0 ? null : new Dictionary<uint, uint> { [did] = didValue };
        var appraisal = Appraised(1, new() { [property] = value }, null, dids);

        Assert.Equal(why, SelfEquipJudge.Unmet(appraisal, new Mage()));
    }

    [Fact]
    public void Every_wield_and_activation_refusal_code_shows_retails_line_or_none_as_retail()
    {
        foreach (var (code, arg, text) in new[]
        {
            (SelfEquipJudge.TooLowToUseItemMagic, "Arcane Lore", "Your Arcane Lore is too low to use that item's magic."),
            (SelfEquipJudge.MustSpecializeToUseItemMagic, "War Magic", "You must have War Magic specialized to use that item's magic."),
            (SelfEquipJudge.MustBeToUseItemMagic, "Aluvian", "You must be Aluvian to use that item's magic."),
            (0x04BEu, string.Empty, "You do not own that item!"), // the AllowedWielder refusal (Player_Inventory.cs:2103-2105)
            (0x0585u, string.Empty, "You are restricted to clothes and armor created for your race."),
            (0x0586u, string.Empty, "That item was specifically created for another race."),
        })
        {
            Assert.True(WeenieErrorText.RetailShows(code), $"0x{code:X4}");
            Assert.True(WeenieErrorText.TryFormat(code, arg, out _, out var shown));
            Assert.Equal(text, shown);
        }

        Assert.False(WeenieErrorText.RetailShows(SelfEquipJudge.SkillTooLow)); // ACE's 0x00A0 wield refusals (Player_Inventory.cs:1655-1661): the 11.6096 client prints no line for them
        Assert.False(WeenieErrorText.RetailShows(SelfEquipJudge.LevelTooLow));
        Assert.EndsWith("which retail prints no line for", SelfEquipJudge.WieldRefusal(SelfEquipJudge.SkillTooLow), StringComparison.Ordinal);
    }

    [Fact]
    public void The_low_and_out_lines_and_an_activation_refusal_reach_the_chat_log_as_retails_lines()
    {
        var rig = new WorldActionRig();
        using var tap = new ChatLogTap(rig.H.R.Bus, rig.H.Log);

        rig.Raw(AceWireWriters.ServerMessage("Your Compass is low on Mana.", 0x07)); // CheckLowMana, Player_Tick.cs:675: ChatMessageType.Magic
        rig.Raw(AceWireWriters.ServerMessage("Your Compass is out of Mana.", 0x07)); // HandleManaDepleted, :683
        rig.Server((p, s) => AceWireWriters.WeenieErrorWithString(p, s, SelfEquipJudge.TooLowToUseItemMagic, "Arcane Lore")); // TryActivateSpells' refusal, WorldObject_Use.cs:204-210

        var chat = rig.H.Log.Entries.Where(e => e.Category == ChatLogTap.LogCategory).Select(e => e.Line).ToList();
        Assert.Contains(chat, l => l.Contains("kind=System type=7 text=\"Your Compass is low on Mana.\"", StringComparison.Ordinal));
        Assert.Contains(chat, l => l.Contains("kind=System type=7 text=\"Your Compass is out of Mana.\"", StringComparison.Ordinal));
        Assert.Contains(chat, l => l.Contains("text=\"Your Arcane Lore is too low to use that item's magic.\"", StringComparison.Ordinal) && !l.Contains("text=\"\"", StringComparison.Ordinal));
    }

    [Fact]
    public void The_keeper_asks_the_caster_rule_first_a_swap_or_a_rewield_is_the_callers_and_a_due_caster_outside_the_worn_list_is_refilled()
    {
        var atlan = new WornMana(0xA0000001, "Atlan Wand", 181, 500, -500.0 / (5.5 * 3600));
        HeldStone[] charged = [new(30, "High Mana Charge", 500, 1)];
        ManaOffer[] cindrue = [new(2304, "Archmage Cindrue", 4615, 3375)];

        var refill = ItemManaKeeper.Next([], charged, [], Me, null, new CasterManaInput(atlan, false, [], cindrue));
        Assert.Equal(ItemManaAct.Refill, refill.Act);
        Assert.Equal(30u, refill.Stone);
        Assert.Equal(CasterManaAct.RefillDue, refill.Caster!.Value.Act);

        var out0 = atlan with { Mana = 0 };
        var spare = new WornMana(0xA0000003, "Spare Wand", 200, 200, -0.01);
        var swap = ItemManaKeeper.Next([out0], charged, [], Me, null, new CasterManaInput(out0, true, [spare], cindrue));
        Assert.Equal(ItemManaAct.None, swap.Act);
        Assert.Equal(CasterManaAct.SwapToSpare, swap.Caster!.Value.Act);
        Assert.StartsWith("caster mana: Atlan Wand 0/500 0h -> SwapToSpare (", swap.ToString(), StringComparison.Ordinal);

        var back = atlan with { Mana = 500 };
        var rewield = ItemManaKeeper.Next([back], [], [], Me, null, new CasterManaInput(back, true, [], cindrue));
        Assert.Equal(ItemManaAct.None, rewield.Act);
        Assert.Equal(CasterManaAct.Rewield, rewield.Caster!.Value.Act);
    }

    [Fact]
    public void A_low_caster_with_no_charged_stone_closes_m5_stone_short_with_the_want_and_without_a_caster_the_old_TODO_stays()
    {
        var atlan = new WornMana(0xA0000001, "Atlan Wand", 100, 500, -500.0 / (5.5 * 3600));
        HeldStone[] empty = [new(32, "Lesser Mana Stone", null, 0.25)];
        ManaOffer[] cindrue = [new(2304, "Archmage Cindrue", 4614, 2025), new(2304, "Archmage Cindrue", 4615, 3375)];

        var step = ItemManaKeeper.Next([atlan], empty, [], Me, null, new CasterManaInput(atlan, false, [], cindrue));
        Assert.Equal(ItemManaAct.None, step.Act);
        Assert.Equal(CasterManaAct.WantStone, step.Caster!.Value.Act);
        Assert.Equal(4615u, step.Caster.Value.WantWcid);
        Assert.Contains("want 1 x wcid 4615", step.Why, StringComparison.Ordinal);
        Assert.DoesNotContain("TODO(m5-stone-short)", step.Why, StringComparison.Ordinal);

        Assert.Contains("TODO(m5-stone-short)", ItemManaKeeper.Next([atlan], empty, [], Me).Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_caster_input_with_a_full_caster_changes_nothing_for_the_worn_items()
    {
        var atlan = new WornMana(0xA0000001, "Atlan Wand", 500, 500, -500.0 / (5.5 * 3600));
        var low = new WornMana(1, "Compass", 400, 1000, -0.02);
        HeldStone[] stones = [new(11, "Greater Mana Stone", 700)];

        var plain = ItemManaKeeper.Next([low, atlan], stones, [], Me);
        var asked = ItemManaKeeper.Next([low, atlan], stones, [], Me, null, new CasterManaInput(atlan, false, [], []));

        Assert.Equal(plain.Act, asked.Act);
        Assert.Equal(plain.Stone, asked.Stone);
        Assert.Equal(plain.Why, asked.Why);
        Assert.Equal(CasterManaAct.None, asked.Caster!.Value.Act);
    }
}
