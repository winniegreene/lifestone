using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Housing;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Journey;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Tests.Session.Housing;

/// <summary>
/// M6 item 16, slice E5b: the Crude Lockpick buy at the vendor counter (<c>VendorCounter.Buy</c>), over the scripted vendor (<see cref="VendorRig"/>, <see cref="ScriptedShop"/>): the Merchant at his catalog place
/// (<see cref="LockpickFacts.Merchant"/>) with the lockpick at the catalog's price, the body beside him. What these prove: a buy the coin covers runs as before, with its line and lines; a buy the coin does not cover is paid from a
/// trade note of the main pack changed at his counter (the slice's one behaviour change: it was the stop <c>lockpick:purse-short</c> before the walk); at a counter whose mask takes no note the stop <c>lockpick:purse-short</c> stands.
/// The Merchant's own mask is not in the corpus notes (<c>TODO(m6-lockpick-note-mask)</c>): the masks here are the tests'.
/// </summary>
public sealed class LockpickBuyStationTests
{
    private const uint MiscType = VendorRig.MiscType;
    private const uint Note10k = 2625;
    private const uint LockpickRow = 0x80006001;

    private sealed class Cottage
    {
        public Cottage(int coins, uint mask)
        {
            var merchant = LockpickFacts.Merchant;
            Rig = new VendorRig(coins);
            Shops = new ScriptedShop(Rig);
            Travel = new LogisticsStationTests.TripTravel(Rig);
            Rig.PlaceAt(merchant.Guid, merchant.Name, merchant.Cell, merchant.Point, 3f, merchant.Wcid);
            Shops.Add(merchant.Guid, mask).Rows.Add((-1, LockpickRow, "Crude Lockpick", VillaFacts.LockpickWcid, MiscType, LockpickFacts.CatalogPrice)); // the scripted shop sells at 1.0, so the row's Value is its price
            Rig.StandAt(merchant.Cell, merchant.Point + new Vector3(2, 0, 0)); // in the Merchant's landblock: no recall home first
            Rig.Tick();
        }

        public VendorRig Rig { get; }

        public ScriptedShop Shops { get; }

        public LogisticsStationTests.TripTravel Travel { get; }

        public long Coin => Rig.State.Self.Qualities.CoinValue;

        public uint Note()
        {
            var guid = Rig.NextGuid();
            Rig.Arrive(guid, "Trade Note (10,000)", Note10k, VendorStock.PromissoryNoteType, 10_000);
            Rig.Put(guid);
            Rig.Tick();
            return guid;
        }

        public (IStationRun Run, ScenarioStatus Status) Buy()
        {
            var context = new JourneyContext { Character = "BSD Mage", Account = "test", Log = Rig.H.Log, Travel = Travel, Spells = new Dictionary<uint, SpellInfo>() };
            var run = new LockpickBuyStation(back: false).Start(context);
            return (run, new J3aStationRig(Rig.R).Run(run, 400, pump: Shops.Pump));
        }

        public IEnumerable<string> Lines => Rig.H.Log.Entries.Select(e => e.Message);

        public bool Logged(string part) => Lines.Any(l => l.Contains(part, StringComparison.Ordinal));

        public string Dump => string.Join(" | ", Lines.Where(l => l.Contains("lockpick", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_lockpick_the_coin_covers_is_bought_as_before_with_its_line()
    {
        var price = LockpickFacts.CatalogPrice;
        var cottage = new Cottage(1_000, MiscType | VendorStock.PromissoryNoteType);

        var (run, status) = cottage.Buy();

        Assert.True(status == ScenarioStatus.Succeeded, cottage.Dump);
        Assert.Equal(233, price); // ceil(1.55 x 150 - 0.1), the catalog's (LockpickFacts)
        Assert.True(cottage.Logged($"lockpick: check before the buy: Crude Lockpick listed at {price}, 1 cost {price} within the coin 1000; "), cottage.Dump);
        Assert.True(cottage.Logged($"lockpick: check after the buy: the lockpicks 0 -> 1, the coin 1000 -> {1_000 - price}"), cottage.Dump);
        Assert.True(cottage.Logged($"lockpick_buy: held_before=0 held_after=1 price={price} purse_before=1000 purse_after={1_000 - price} ok=yes reason=bought"), cottage.Dump);
        Assert.Equal((1, 0), (cottage.Shops.Buys, cottage.Shops.Sells));
        Assert.False(cottage.Logged("purse:")); // no note is touched while the coin pays
        Assert.True(run.Ok);
    }

    [Fact]
    public void A_lockpick_the_coin_does_not_cover_is_paid_from_a_trade_note_changed_at_the_Merchants_counter()
    {
        var price = LockpickFacts.CatalogPrice;
        var cottage = new Cottage(100, MiscType | VendorStock.PromissoryNoteType);
        var note = cottage.Note();

        var (run, status) = cottage.Buy();

        Assert.True(status == ScenarioStatus.Succeeded, cottage.Dump); // before E5b: the stop lockpick:purse-short before the walk ("the coin 100 does not cover one Crude Lockpick at 233")
        Assert.True(cottage.Logged("lockpick: check before: purse-short: the coin 100 does not cover one Crude Lockpick at 233"), cottage.Dump); // the rule's own line stays
        Assert.True(cottage.Logged("lockpick: the coin 100 is short of 233; the trade notes held (10000) cover the rest: a note is changed at the Merchant's counter before the buy (TODO(m6-lockpick-note-mask)"), cottage.Dump);
        Assert.True(cottage.Logged("lockpick: purse: the coin was short: changed Trade Note (10,000) to coin at the Merchant: paid 10000 pyreals (the coin 100 -> 10100)"), cottage.Dump);
        Assert.True(cottage.Logged("lockpick: paid from the purse: 1 trade note(s) changed to coin at the Merchant before the buy"), cottage.Dump);
        Assert.True(cottage.Logged($"lockpick: check after the buy: the lockpicks 0 -> 1, the coin 10100 -> {10_100 - price}"), cottage.Dump);
        Assert.True(cottage.Logged($"lockpick_buy: held_before=0 held_after=1 price={price} purse_before=100 purse_after={10_100 - price} ok=yes reason=bought"), cottage.Dump);
        Assert.Equal((1, 1, 10_100L - price), (cottage.Shops.Buys, cottage.Shops.Sells, cottage.Coin));
        Assert.False(cottage.Rig.State.Self.Inventory.Contains(note));
        Assert.True(run.Ok);
    }

    [Fact]
    public void M6_16_E5a2_a_lockpick_the_coin_does_not_cover_is_paid_from_a_trade_note_inside_a_side_pack()
    {
        // 100 coin and the 10,000 note inside a side pack. Before the slice the step's check before counted the main pack's notes alone: the stop lockpick:purse-short before the walk.
        var price = LockpickFacts.CatalogPrice;
        var cottage = new Cottage(100, MiscType | VendorStock.PromissoryNoteType);
        var pack = cottage.Rig.NextGuid();
        cottage.Rig.Arrive(pack, "Pack", 136, 0x200, 65);
        cottage.Rig.Put(pack, containerType: 1);
        var note = cottage.Rig.NextGuid();
        cottage.Rig.Arrive(note, "Trade Note (10,000)", Note10k, VendorStock.PromissoryNoteType, 10_000);
        cottage.Rig.Put(note, pack);
        cottage.Rig.Tick();
        Assert.Equal(InventoryPlaceKind.InSidePack, cottage.Rig.State.Self.Inventory.Find(note).Kind);

        var (run, status) = cottage.Buy();

        Assert.True(status == ScenarioStatus.Succeeded, cottage.Dump);
        Assert.True(cottage.Logged("lockpick: the coin 100 is short of 233; the trade notes held (10000) cover the rest: a note is changed at the Merchant's counter before the buy (TODO(m6-lockpick-note-mask)"), cottage.Dump);
        Assert.True(cottage.Logged($"lockpick: purse: Trade Note (10,000) 0x{note:X8} (x1) lies in the side pack 0x{pack:X8}: a single note, sold where it lies"), cottage.Dump);
        Assert.True(cottage.Logged("lockpick: purse: the coin was short: changed Trade Note (10,000) to coin at the Merchant: paid 10000 pyreals (the coin 100 -> 10100)"), cottage.Dump);
        Assert.True(cottage.Logged($"lockpick_buy: held_before=0 held_after=1 price={price} purse_before=100 purse_after={10_100 - price} ok=yes reason=bought"), cottage.Dump);
        Assert.Equal((1, 1, 10_100L - price), (cottage.Shops.Buys, cottage.Shops.Sells, cottage.Coin));
        Assert.False(cottage.Rig.State.Self.Inventory.Contains(note));
        Assert.True(run.Ok);
    }

    [Fact]
    public void A_short_coin_with_no_note_stops_before_the_walk_and_a_counter_that_takes_no_note_stops_there_by_name()
    {
        var bare = new Cottage(100, MiscType | VendorStock.PromissoryNoteType);
        var (first, status) = bare.Buy();
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal("lockpick:purse-short: the coin 100 does not cover one Crude Lockpick at 233", first.Failure); // as before: no note to change, nothing walked
        Assert.Empty(bare.Travel.Requests);
        Assert.Equal(0, bare.Shops.Uses);

        var masked = new Cottage(100, MiscType);
        var note = masked.Note();
        var (second, ended) = masked.Buy();
        Assert.Equal(ScenarioStatus.Failed, ended);
        Assert.StartsWith("lockpick:purse-short: the coin 100 does not cover 1 Crude Lockpick at 233 each (233); the shop's mask takes no trade note (the 10000 held in notes are not changed here)", second.Failure, StringComparison.Ordinal);
        Assert.Equal((0, 0), (masked.Shops.Buys, masked.Shops.Sells));
        Assert.True(masked.Rig.State.Self.Inventory.Contains(note));
        Assert.True(masked.Logged("lockpick_buy: held_before=0 held_after=0 price=233 purse_before=100 purse_after=100 ok=no reason=purse-short"), masked.Dump);
    }
}
