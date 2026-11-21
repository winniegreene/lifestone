using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 14 pack room (<c>m6-pack-room</c>; live 2026-11-14, <c>cache/m6/14/live/ladder18</c>): the climber's visit of 22:25 UTC replayed through the home walk. Level 66, Strength 119 with the bot's line (a capacity of 17,850), her burden
/// 17,776; 333 Prismatic Tapers left of the 850 she went out with 57 minutes before, the drinks untouched (100 Mana Potions, 33 Potions of Healing, 29 Mana Tinctures, a stack of 1 and one of 20 Health Tinctures, 9 Health Elixirs from the
/// loot), and the loot kept 3,735 burden and left 338 items over its load line meanwhile. The visit then bought 517 tapers back and sold none of the drinks; with the budget it buys to her need and sells the drinks she did not drink.
/// </summary>
public sealed partial class ResupplyStationTests
{
    private const string Barracks = "tusker-barracks";
    private const uint HealthElixirWcid = 2458;

    [Fact]
    public void M6_14_pack_room_her_live_shape_buys_under_the_budget_and_sells_the_drinks_she_did_not_drink()
    {
        var description = new PlayerDescriptionMsg
        {
            IntProperties = ImmutableDictionary<uint, int>.Empty.Add(PlayerQualities.LevelProperty, 66),
            Attributes = [new AttributeDesc(1, 0, 119, 0)], // Strength 79 and the bot's 40: 17,850
        };
        var home = new Home(Barracks, 2_000_000, held: l => l.Wcid switch { GearKit.PrismaticTaperWcid => 333, FighterKit.PotionWcid or FighterKit.ManaPotionWcid => 0, _ => l.Keep }, description: description);
        home.Own("Potion of Healing", FighterKit.PotionWcid, MiscType, 33 * 170, stack: 33, maxStack: 100); // the weenie's Value 170 a potion (GearKit.SupplyWeenies)
        home.Own("Mana Potion", FighterKit.ManaPotionWcid, MiscType, 100 * 170, stack: 100, maxStack: 100);
        var oneTincture = home.Own("Health Tincture", HealthDrinks.TinctureWcid, MiscType, 500, stack: 1, maxStack: 100);
        var tinctures = home.Own("Health Tincture", HealthDrinks.TinctureWcid, MiscType, 20 * 500, stack: 20, maxStack: 100);
        home.Own("Mana Tincture", ManaDrinks.TinctureWcid, MiscType, 29 * 500, stack: 29, maxStack: 100);
        var elixirs = home.Own("Health Elixir", HealthElixirWcid, MiscType, 9 * 300, stack: 9, maxStack: 100);
        home.Own("Gold Scarab", 687, MiscType, 100, stack: 100, maxStack: 100);
        home.Own("Pyreal Scarab", 690, MiscType, 95, stack: 95, maxStack: 100);
        var loot = home.Own("Chainmail Sleeves", 99_002, MiscType, 2_000);
        home.Vendor.R.PrivateInt(PlayerInventory.EncumbranceProperty, 17_776);
        var state = home.Vendor.State;
        var room = PackRoomMemory.For(state);
        var departed = home.Ground.Supplies.Stock.ToDictionary(l => l.Wcid, l => l.Keep);
        departed[GearKit.PrismaticTaperWcid] = 850;
        departed[FighterKit.PotionWcid] = 33;
        departed[HealthDrinks.TinctureWcid] = 21;
        departed[ManaDrinks.TinctureWcid] = 29;
        departed[687] = 100;
        departed[690] = 95;
        room.Visited(-57 * 60, departed, new Dictionary<uint, int>()); // the 21:28 visit
        room.NoteLoot(leftItems: 338, leftValue: 2_112_938, leftBurden: 132_036, keptBurden: 3_735);
        var run = home.Start(new ResupplyOptions(true, () => []));

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run, 1_200));

        var budget = run.PackBudget!;
        Assert.Equal(PackBudgetVerdict.KitFirst, budget.Verdict); // M6 audit (the owner's decision of 2026-11-19): 75 minutes of her use come before the loot's room
        Assert.Equal(17_850, budget.Capacity);
        Assert.Single(home.Lines, l => l.Contains("pack budget (m6-pack-room): capacity 17850 at the loot's load line 100% = 17850", StringComparison.Ordinal));
        Assert.True(home.Logged("pack budget: measured: 57."), string.Join(" | ", home.Lines.Where(l => l.Contains("pack budget", StringComparison.Ordinal))));

        var tapers = Assert.Single(run.BuyList, b => b.Line.Wcid == GearKit.PrismaticTaperWcid);
        Assert.Equal((850, 517), (tapers.Line.Keep, tapers.Count)); // her line 300 and 75 minutes of the 517 she burned in 57 (981) pass the profile's 850: the keep stands (it was 817, one interval's use)
        Assert.False(home.Logged("pack budget: Prismatic Taper: this visit buys to"));
        Assert.DoesNotContain(run.BuyList, b => b.Line.Wcid is FighterKit.ManaPotionWcid or FighterKit.PotionWcid);

        int Held(uint wcid) => GearInventory.CountOf(state, [], wcid);
        Assert.False(state.Self.Inventory.Contains(loot)); // the loot sold, as before
        Assert.False(state.Self.Inventory.Contains(elixirs)); // no line names it and she drank none: the whole stack sold
        Assert.False(state.Self.Inventory.Contains(oneTincture));
        Assert.True(state.Self.Inventory.Contains(tinctures));
        Assert.Equal((11, 7, 7, 11), (Held(FighterKit.ManaPotionWcid), Held(FighterKit.PotionWcid), Held(HealthDrinks.TinctureWcid), Held(ManaDrinks.TinctureWcid))); // each drink one over its trip line
        Assert.Equal((100, 95), (Held(687), Held(690))); // a scarab is never sold
        Assert.True(home.Shops.Splits >= 4, $"{home.Shops.Splits} splits");
        Assert.Equal(89 + 26 + 14 + 18 + 9, run.DrinksOverBudget);
        Assert.True(home.Logged("pack budget: Health Tincture: 21 held, the cut keep 7: offered for sale"));

        Assert.Equal((2, 0), (room.Visits, room.LoadLeftItems)); // the visit ended: the measure starts again, the full-pack line waits for the next item the loot leaves
        Assert.Equal(7, room.KeepOf(HealthDrinks.TinctureWcid)); // the town stock's stop on the way out buys to it
        Assert.Equal(95, room.KeepOf(690));
        Assert.Null(run.Failure);
    }

    /// <summary>
    /// M6 sprint (the owner, 2026-11-20: drop anything unsellable once it has no value; run 6, 2026-11-20 03:37 UTC: "Potion of Healing is marked unsellable on the wire", the surplus left in the pack): the Potions of Healing over
    /// the cut keep go on the sale, ACE marks the class unsellable (IsSellable 0, weenie 377, world weenie_properties_bool type 69), so the surplus is dropped at the home on this trip; the seven of the cut keep stay.
    /// </summary>
    [Fact]
    public void M6_sprint_the_drinks_over_the_cut_that_ace_will_not_buy_are_dropped_and_the_cut_keep_stays()
    {
        var description = new PlayerDescriptionMsg
        {
            IntProperties = ImmutableDictionary<uint, int>.Empty.Add(PlayerQualities.LevelProperty, 66),
            Attributes = [new AttributeDesc(1, 0, 119, 0)],
        };
        var home = new Home(Barracks, 2_000_000, held: l => l.Wcid switch { GearKit.PrismaticTaperWcid => 333, FighterKit.PotionWcid or FighterKit.ManaPotionWcid => 0, _ => l.Keep }, description: description);
        var potions = home.Own("Potion of Healing", FighterKit.PotionWcid, MiscType, 33 * 170, stack: 33, maxStack: 100);
        home.Own("Mana Potion", FighterKit.ManaPotionWcid, MiscType, 100 * 170, stack: 100, maxStack: 100);
        home.Own("Health Tincture", HealthDrinks.TinctureWcid, MiscType, 21 * 500, stack: 21, maxStack: 100);
        home.Own("Mana Tincture", ManaDrinks.TinctureWcid, MiscType, 29 * 500, stack: 29, maxStack: 100);
        home.Vendor.R.PrivateInt(PlayerInventory.EncumbranceProperty, 17_776);
        home.Shops.AnswersIdentify = true;
        home.Shops.NotSellable.Add(FighterKit.PotionWcid);
        var state = home.Vendor.State;
        var room = PackRoomMemory.For(state);
        var departed = home.Ground.Supplies.Stock.ToDictionary(l => l.Wcid, l => l.Keep);
        departed[GearKit.PrismaticTaperWcid] = 850;
        departed[FighterKit.PotionWcid] = 33;
        departed[HealthDrinks.TinctureWcid] = 21;
        departed[ManaDrinks.TinctureWcid] = 29;
        room.Visited(-57 * 60, departed, new Dictionary<uint, int>());
        room.NoteLoot(leftItems: 338, leftValue: 2_112_938, leftBurden: 132_036, keptBurden: 3_735);
        var run = home.Start(new ResupplyOptions(true, () => []));

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run, 1_200));

        int Held(uint wcid) => GearInventory.CountOf(state, [], wcid);
        Assert.Equal(7, Held(FighterKit.PotionWcid)); // the cut keep stays
        Assert.True(state.Self.Inventory.Contains(potions));
        Assert.True(run.DroppedItems >= 1, $"dropped {run.DroppedItems}");
        Assert.NotEmpty(home.Vendor.H.Net.Of(FakeSendKind.DropItem));
        Assert.True(home.Logged("Potion of Healing is marked unsellable on the wire"));
        Assert.True(home.Logged("is marked unsellable (IsSellable=0) and no rule keeps it, so it has no value to her: dropped at the home"));
        Assert.False(home.Logged("is a potion she drinks (the emergency rule or the mana rule): kept, never dropped"));
        Assert.Null(run.Failure);
    }

    [Fact]
    public void M6_14_pack_room_the_first_visit_of_a_session_cuts_nothing_and_starts_the_measure()
    {
        var description = new PlayerDescriptionMsg { Attributes = [new AttributeDesc(1, 0, 119, 0)] };
        var home = new Home(Barracks, 2_000_000, held: l => l.Wcid == GearKit.PrismaticTaperWcid ? 333 : l.Keep, description: description);
        home.Vendor.R.PrivateInt(PlayerInventory.EncumbranceProperty, 12_000);
        var run = home.Start(new ResupplyOptions(true, () => []));

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run, 1_200));

        Assert.Equal(PackBudgetVerdict.Unmeasured, run.PackBudget!.Verdict);
        Assert.Equal((850, 517), Assert.Single(run.BuyList, b => b.Line.Wcid == GearKit.PrismaticTaperWcid) is var b ? (b.Line.Keep, b.Count) : default);
        Assert.True(home.Logged("m6-pack-budget-unmeasured"));
        Assert.Equal(1, PackRoomMemory.For(home.Vendor.State).Visits);
    }
}
