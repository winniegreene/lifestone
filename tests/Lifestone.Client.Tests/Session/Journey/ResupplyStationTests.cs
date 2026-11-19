using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.World;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;
using Lifestone.Data.Magic;
using MageKeepAside = Lifestone.Client.Tests.Session.Mage.KeepAsideRuleTests;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M5 item 3 m3: the resupply station over both dungeon profiles (the den's Holtburg, the Tusker Temple's Fort Tethana) and the scripted vendors (<see cref="ScriptedShop"/>): the buy list from the stock and the held counts, the vendor order,
/// the walks' purposes, a note changed when the coin is short, a purse too thin, a vendor not found, ACE's refusals, the sale only with looting on, the carried lines never bought, a price that differs from the profile, and the vendor's
/// live place beside the profile's. Every number is read from the profile; nothing here is a vendor constant of the station.
/// </summary>
public sealed partial class ResupplyStationTests
{
    private const string Den = "black-spawn-den-aluvian";
    private const string Temple = "tusker-temple-fort-tethana";
    private const uint MiscType = 0x80;
    private const uint ClothingType = 0x4;

    /// <summary>A travel seam that records each request and puts the player at its destination at once (a run that arrived).</summary>
    private sealed class WalkingTravel(VendorRig rig) : IJourneyTravel
    {
        public List<TravelRequest> Requests { get; } = [];

        /// <summary>M5 7: the reason a walk to the named place fails (the leg ends Failed with it and the player stays where he is), or null when it arrives; the place is the destination's name (a vendor's).</summary>
        public Func<string, string?>? FailFor { get; set; }

        public bool Busy => false;

        public ITravelLeg GoTo(IGameSessionView session, TravelRequest request, double now)
        {
            Requests.Add(request);
            if (FailFor?.Invoke(request.Destination.Name) is { } why)
            {
                return new Failing(why);
            }

            var at = request.Destination.Location.Position;
            rig.StandAt(at.CellId, at.Local);
            return new Arrived();
        }

        private sealed class Failing(string why) : ITravelLeg
        {
            public TravelMeans Means => TravelMeans.Run;

            public string? Failure => why;

            public string Fields => string.Empty;

            public TravelStatus Step(IGameSessionView session, double now) => TravelStatus.Failed;

            public void Pause()
            {
            }

            public void Resume(double now)
            {
            }

            public void Dispose()
            {
            }
        }

        private sealed class Arrived : ITravelLeg
        {
            public TravelMeans Means => TravelMeans.Run;

            public string? Failure => null;

            public string Fields => string.Empty;

            public TravelStatus Step(IGameSessionView session, double now) => TravelStatus.Arrived;

            public void Pause()
            {
            }

            public void Resume(double now)
            {
            }

            public void Dispose()
            {
            }
        }
    }

    /// <summary>The world of one ground's home: the player at the lifestone, each vendor at his profile place with his shop scripted from his <c>sells</c>, the stock held as told.</summary>
    private sealed class Home
    {
        private uint _row = 0x80006000;
        private uint _item = 0x80007000;

        /// <param name="sidePacks">M6 8 D: the side packs the player holds (the layout wants three: <see cref="PackLayout.PacksWanted"/>); fewer, and the trip buys the rest at the vendor whose <c>sells</c> lists the Pack.</param>
        /// <param name="ground">M6 item 14 (m6-goldscarab): the ground when it is not the catalog's of <paramref name="key"/> (a profile a test changed by <c>with</c>: a town stock line it has not).</param>
        public Home(string key, int coins, Func<StockLine, int>? held = null, bool notes = true, Func<GroundVendor, bool>? absent = null, Lifestone.Net.Messages.PlayerDescriptionMsg? description = null, int sidePacks = 3, HuntingGround? ground = null)
        {
            Ground = ground ?? GroundCatalog.Get(key);
            Vendor = new VendorRig(coins, description);
            Shops = new ScriptedShop(Vendor);
            Travel = new WalkingTravel(Vendor);
            Stations = new J3aStationRig(Vendor.R);
            Vendor.StandAt(Ground.Home.LifestoneCell, Ground.Home.LifestonePoint);
            foreach (var v in Ground.Vendors)
            {
                if (absent?.Invoke(v) != true)
                {
                    Vendor.PlaceAt(v.Guid, v.Name, v.Cell, v.Point, 3f, v.Wcid);
                }

                var shop = Shops.Add(v.Guid, v.BuysItemTypes);
                foreach (var ware in v.Sells)
                {
                    shop.Rows.Add((-1, ++_row, ware.Name, ware.Wcid, MiscType, ware.Price));
                }

                shop.AddTradeNotes(() => ++_row);
            }

            for (var i = 0; i < sidePacks; i++)
            {
                var pack = Vendor.NextGuid();
                Vendor.Arrive(pack, "Pack", 136, 0x200, 65);
                Vendor.R.Raw(AceM3Writers.PutObjInContainer(VendorRig.Player, 8_000 + (uint)i, pack, VendorRig.Player, 0, 1)); // a side pack (ContainerType 1)
            }

            foreach (var line in Ground.Supplies.Stock)
            {
                var count = held?.Invoke(line) ?? line.Keep;
                if (count > 0)
                {
                    Own(line.Name, line.Wcid, MiscType, 1, (ushort)count);
                }
            }

            if (notes)
            {
                foreach (var note in Ground.Supplies.Purse.Notes)
                {
                    for (var i = 0; i < note.Count; i++)
                    {
                        Own(note.Name, note.Wcid, VendorStock.PromissoryNoteType, (uint)note.Value);
                    }
                }
            }
        }

        public HuntingGround Ground { get; }

        /// <summary>M5 7 P7j: the trips' memory, shared by the runs of one home (a refused learn is remembered across trips).</summary>
        public SaleMemory Memory { get; } = new();

        public VendorRig Vendor { get; }

        public ScriptedShop Shops { get; }

        public WalkingTravel Travel { get; }

        public J3aStationRig Stations { get; }

        public uint Own(string name, uint wcid, uint itemType, uint value, ushort? stack = null, ushort? maxStack = null)
        {
            var guid = ++_item;
            Vendor.Arrive(guid, name, wcid, itemType, value, stack, maxStack);
            Vendor.Put(guid);
            return guid;
        }

        /// <summary>M6 2 f: a clothing piece in the main pack with the wear locations and coverage the judge reads (the weenie header's ValidLocations and Priority).</summary>
        public uint OwnPiece(string name, uint wcid, uint validLocations, uint coverage, uint value = 100)
        {
            var guid = ++_item;
            var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name, itemType: ClothingType);
            var header = WeenieHeaderFlags.Container | WeenieHeaderFlags.Value | WeenieHeaderFlags.ValidLocations | WeenieHeaderFlags.Priority;
            Vendor.H.R.Reducer.OnCreateObject(
                Vendor.H.R.Context,
                basis with { Weenie = basis.Weenie with { Header = header, WeenieClassId = wcid, ContainerId = VendorRig.Player, Value = value, ValidLocations = validLocations, Priority = coverage } },
                isUpdate: false);
            Vendor.Put(guid);
            return guid;
        }

        /// <summary>M6 2 f: an NPC standing <paramref name="metres"/> east of the lifestone point (his weenie, so the return pass finds him).</summary>
        public uint PlaceNpc(uint guid, string name, uint wcid, float metres)
        {
            Vendor.PlaceAt(guid, name, Ground.Home.LifestoneCell, Ground.Home.LifestonePoint + new Vector3(metres, 0, 0), 3f, wcid);
            return guid;
        }

        public ScriptedShop.Shop ShopOf(uint wcid) => Shops.Get(Ground.VendorSelling(wcid)!.Guid);

        /// <param name="facts">M6 item 14: sets the journey's facts before the trip starts (an order's: <see cref="JourneyFacts.CoinToNotes"/>).</param>
        /// <param name="character">M6 14 (m6-lore-cantrips): the journey's character (the climber's keep-aside has a default).</param>
        public ResupplyRun Start(ResupplyOptions? options = null, IReadOnlyDictionary<uint, SpellInfo>? spells = null, Action<JourneyFacts>? facts = null, string character = "BSD Mage")
        {
            var context = new JourneyContext
            {
                Character = character,
                Account = "test",
                Log = Vendor.H.Log,
                Travel = Travel,
                Spells = spells ?? new Dictionary<uint, SpellInfo>(),
                Dungeon = Ground,
            };
            facts?.Invoke(context.Facts);
            return (ResupplyRun)new ResupplyStation(options).Start(context);
        }

        public ScenarioStatus Run(ResupplyRun run, int frames = 400) => Stations.Run(run, frames, pump: Shops.Pump);

        public IEnumerable<string> Lines => Vendor.H.Log.Entries.Select(e => e.Message);

        public bool Logged(string part) => Lines.Any(l => l.Contains(part, StringComparison.Ordinal));
    }

    private static StockLine Line(HuntingGround ground, string name) => ground.Supplies.Stock.Single(s => s.Name == name);

    [Theory]
    [InlineData(Den, "Archmage Cindrue", "Thelnoth Cort the Healer")]
    [InlineData(Temple, "Shopkeeper Yoshi Deshi", "Zisaha bint Jaidka the Mage")]
    public void Every_line_under_its_keep_is_bought_back_to_it_in_the_shortest_walk_and_the_carried_lines_never(string key, string first, string second)
    {
        var home = new Home(key, 2_000_000, held: l => l.TripUnder - 1);
        var run = home.Start();

        var status = home.Run(run);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        var g = home.Ground;
        var expected = g.Supplies.Stock.Where(l => !g.IsCarried(l.Wcid)).ToList();
        Assert.Equal(expected.Select(l => l.Wcid), run.BuyList.Select(b => b.Line.Wcid));
        Assert.All(run.BuyList, b => Assert.Equal(b.Line.Keep - (b.Line.TripUnder - 1), b.Count));
        Assert.All(run.BuyList, b => Assert.Same(g.VendorSelling(b.Line.Wcid), b.Vendor));
        Assert.Equal([first, second], run.Visits.Select(v => v.Vendor.Name));
        Assert.All(home.Travel.Requests, r => Assert.Equal(TravelPurpose.Vendor, r.Purpose));
        Assert.Equal(0, home.Stations.Places);
        foreach (var visit in run.Visits)
        {
            Assert.True(home.Logged($"walking: purpose=vendor:{visit.Vendor.Name} ({visit.Buys.Count} lines to buy)"), visit.Vendor.Name);
        }

        Assert.Equal(run.BuyList.Sum(b => (long)b.Count), run.Bought);
        Assert.Equal(run.BuyList.Sum(b => (long)b.Count * g.PriceOf(b.Line.Wcid)!.Value), run.Spent);
        foreach (var carried in g.Supplies.Stock.Where(l => g.IsCarried(l.Wcid)))
        {
            Assert.DoesNotContain(run.BuyList, b => b.Line.Wcid == carried.Wcid);
            Assert.True(home.Logged($"resupply: {carried.Name} is carried"), carried.Name);
        }

        Assert.Equal(key == Den ? 4 : 0, g.Supplies.Stock.Count(l => g.IsCarried(l.Wcid))); // M5 7 D148: Cindrue sells the Silver Scarab, so it left the carried lines; the den's Platinum and Pyreal Scarabs (D8), (M5 7 F10) the ring's Diamond Scarab and (M5 7 F25) the buff's Silver and Gold Scarabs; Fort Tethana's mage sells both
        Assert.StartsWith($"bought={run.Bought} spent={run.Spent} notes_sold=0 sold=0 earned=0 vendors=2 refused=0 refusals=none short=0 not_found=0 loot=off problems=0 granted=0", run.VerdictFields(), StringComparison.Ordinal);
    }

    /// <summary>M5 4 fix F2 (4-R3): the visit's vendor open, sale and buys are Automation sends, so the kill switch withdraws the one queued and refuses the rest, and nothing more goes to the vendor.</summary>
    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void M5_4_f2_the_kill_switch_withdraws_a_queued_vendor_buy_and_the_rest_of_the_visit_sends_nothing(string key)
    {
        var home = new Home(key, 2_000_000, held: l => l.TripUnder - 1);
        var run = home.Start();
        var api = home.Vendor.Api;
        var status = ScenarioStatus.Running;
        for (var i = 0; i < 400 && status == ScenarioStatus.Running && !(home.Shops.Uses > 0 && api.QueuedCount > 0); i++)
        {
            status = home.Run(run, 1);
        }

        Assert.Equal(ScenarioStatus.Running, status);
        Assert.Equal(0, home.Shops.Buys); // the first buy is queued, not yet sent
        Assert.Equal(1, api.QueuedCount);
        var log = home.Stations.View.ActionLog;
        Assert.Empty(EntriesOf(log, ActionKind.Buy));

        api.KillSwitch = true; // the person throws it
        Assert.Equal(0, api.QueuedCount);
        var withdrawn = Assert.Single(EntriesOf(log, ActionKind.Buy));
        Assert.Equal((ActionSource.Automation, ActionOutcomeKind.Cancelled), (withdrawn.Source, withdrawn.Outcome.Kind));

        status = home.Run(run, 400);

        Assert.NotEqual(ScenarioStatus.Running, status);
        Assert.Equal(0, home.Shops.Buys); // not one buy of the visit went out after the switch
        Assert.Equal(0, home.Shops.Sells);
        Assert.Equal(0, run.Bought);
        Assert.All(EntriesOf(log, ActionKind.Buy).Concat(EntriesOf(log, ActionKind.OpenVendor)), e => Assert.Equal(ActionSource.Automation, e.Source));
        Assert.True(home.Logged("the kill switch is on"));
    }

    private static List<ActionLogEntry> EntriesOf(ActionLog log, ActionKind kind)
    {
        var found = new List<ActionLogEntry>();
        for (var i = 0; i < log.Count; i++)
        {
            if (log[i].Kind == kind)
            {
                found.Add(log[i]);
            }
        }

        return found;
    }

    private static void PlaceLifestone(Home home)
    {
        var at = new AcPosition(home.Ground.Home.LifestoneCell, home.Ground.Home.LifestonePoint, Quaternion.Identity);
        var basis = ReducerHarness.Create(0x7A9B4999, ReducerHarness.Stamps(), at, name: "Lifestone");
        home.Vendor.H.R.Reducer.OnCreateObject(home.Vendor.H.R.Context, basis with { Weenie = basis.Weenie with { ObjectDescriptionFlags = WorldObjectTable.LifestoneFlag } }, isUpdate: false);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void M5_4_a3_a_mage_standing_in_the_lifestone_steps_off_it_to_the_lifestone_point_before_the_first_vendor_walk(string key)
    {
        var home = new Home(key, 2_000_000, held: l => l.TripUnder - 1);
        PlaceLifestone(home); // after Lifestone Recall the mage stands inside the lifestone's cylinder
        var run = home.Start();

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

        Assert.True(run.SteppedOff);
        var first = home.Travel.Requests[0];
        Assert.Equal(TravelPurpose.Bind, first.Purpose); // the recall-home station's walk (the bind's), not a vendor walk
        Assert.Equal("home-lifestone-point", first.Destination.Name);
        Assert.Equal((home.Ground.Home.LifestoneCell, home.Ground.Home.LifestonePoint), (first.Destination.Location.Position.CellId, first.Destination.Location.Position.Local));
        Assert.All(home.Travel.Requests.Skip(1), r => Assert.Equal(TravelPurpose.Vendor, r.Purpose)); // one step-off, then the vendors
        Assert.True(home.Logged("stands in lifestone"));
        Assert.True(home.Logged("m5-logistics-step-off"));
        Assert.True(run.Bought > 0);
        Assert.True(home.Lines.ToList().FindIndex(l => l.Contains("stepping off it", StringComparison.Ordinal)) < home.Lines.ToList().FindIndex(l => l.Contains("walking: purpose=vendor:", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void M5_4_a3_a_mage_not_in_a_lifestone_makes_no_step_off(string key)
    {
        var home = new Home(key, 2_000_000, held: l => l.TripUnder - 1);
        var run = home.Start();

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

        Assert.False(run.SteppedOff);
        Assert.DoesNotContain(home.Travel.Requests, r => r.Purpose == TravelPurpose.Bind);
    }

    [Theory]
    [InlineData(Den, 2625u)]
    [InlineData(Temple, 2626u)]
    public void A_trade_note_is_changed_to_coin_when_the_coin_is_short_the_smallest_that_covers_the_buy(string key, uint noteWcid)
    {
        var home = new Home(key, 100, held: l => l.Name == "Prismatic Taper" ? l.Keep - 10 : l.Keep);
        var run = home.Start();

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

        var taper = Line(home.Ground, "Prismatic Taper");
        var price = home.Ground.PriceOf(taper.Wcid)!.Value;
        Assert.Equal(1, run.NotesSold);
        Assert.Equal(10, run.Bought);
        Assert.Equal(10L * price, run.Spent);
        var note = home.Ground.Supplies.Purse.Notes.Single(n => n.Wcid == noteWcid);
        Assert.True(home.Logged($"purse: changed {note.Name} to coin"));
        Assert.Equal(100 + note.Value - (10 * price), home.Vendor.State.Self.Qualities.CoinValue); // paid at 1.0 (D49)
        Assert.Equal(0, run.Short);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_purse_too_thin_even_after_the_notes_buys_what_it_can_and_names_the_rest(string key)
    {
        var home = new Home(key, 100, held: l => l.Name == "Prismatic Taper" ? l.Keep - 10 : l.Keep, notes: false);
        var run = home.Start();

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

        var price = home.Ground.PriceOf(Line(home.Ground, "Prismatic Taper").Wcid)!.Value;
        Assert.Equal(100 / price, run.Bought);
        Assert.Equal(1, run.Short);
        Assert.Equal(0, run.NotesSold);
        Assert.True(home.Logged($"purse: short by {(10 * price) - 100} pyreals for Prismatic Taper"));
        Assert.Equal(0, home.Stations.ToolRequests("item")); // never a grant
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_vendor_not_found_is_named_and_the_station_goes_on_to_the_next(string key)
    {
        var g = GroundCatalog.Get(key);
        var taperSeller = g.VendorSelling(Line(g, "Prismatic Taper").Wcid)!;
        var home = new Home(key, 2_000_000, held: l => l.Name is "Prismatic Taper" or "Mana Potion" ? l.Keep - 5 : l.Keep, absent: v => v.Guid == taperSeller.Guid);
        var run = home.Start();

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run, 800));

        Assert.Equal(1, run.NotFound);
        Assert.Equal(1, run.Vendors);
        Assert.Equal(5, run.Bought); // the potions only
        Assert.True(home.Logged($"vendor: {taperSeller.Name} 0x{taperSeller.Guid:X8} not found in reach"));
        Assert.Contains("not_found=1", run.VerdictFields(), StringComparison.Ordinal);
    }

    /// <summary>M5 7 (TODO(m5-vendor-unreachable)): a walk the physics refused skips that vendor and the trip goes on to the next, instead of failing the whole trip.</summary>
    [Fact]
    public void A_sell_only_vendor_that_cannot_be_reached_is_skipped_and_the_trip_goes_on_to_the_buy_vendor()
    {
        var g = GroundCatalog.Get(Den);
        var taperSeller = g.VendorSelling(Line(g, "Prismatic Taper").Wcid)!;
        var buyer = g.Vendors.Single(v => v.Name == "Shopkeeper Renald the Elder");
        Assert.NotEqual(taperSeller.Guid, buyer.Guid);
        var home = new Home(Den, 2_000_000, held: l => l.Name == "Prismatic Taper" ? l.Keep - 5 : l.Keep);
        home.Travel.FailFor = place => place == buyer.Name ? "the physics refused the path 3 times in a row" : null;
        var shirt = home.Own("Shirt", 130, ClothingType, 100);
        var run = home.Start(new ResupplyOptions(true, () => [shirt]));

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run, 800));

        Assert.Equal(1, run.VendorsSkipped);
        Assert.Equal(5, run.Bought);
        Assert.Equal(0, run.Sold);
        Assert.Empty(run.Problems);
        Assert.True(home.Vendor.State.Self.Inventory.Contains(shirt));
        Assert.True(home.Logged($"vendor: {buyer.Name} could not be reached (the physics refused the path 3 times in a row); skipped: 1 items wait to be sold, 0 lines are not bought here this trip (TODO(m5-vendor-unreachable))"));
        Assert.Contains("vendors_skipped=1", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains("not_found=0", run.VerdictFields(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_buy_vendor_that_cannot_be_reached_is_skipped_its_lines_are_not_bought_and_the_trip_ends_well(string key)
    {
        var g = GroundCatalog.Get(key);
        var taperSeller = g.VendorSelling(Line(g, "Prismatic Taper").Wcid)!;
        var home = new Home(key, g.Supplies.Purse.Coin, held: l => l.Name is "Prismatic Taper" or "Mana Potion" ? l.Keep - 5 : l.Keep); // the coin at the purse's: no coin-to-notes walk to the same vendor
        home.Travel.FailFor = place => place == taperSeller.Name ? "no walkable ground" : null;
        var run = home.Start();

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run, 800));

        Assert.Equal(1, run.VendorsSkipped);
        Assert.Equal(1, run.Vendors);
        Assert.Equal(5, run.Bought); // the potions only
        Assert.Empty(run.Problems);
        Assert.True(home.Logged($"vendor: {taperSeller.Name} could not be reached (no walkable ground); skipped: 0 items wait to be sold, "));
        Assert.True(home.Logged("TODO(m5-vendor-unreachable)"));
        Assert.Contains("vendors_skipped=1", run.VerdictFields(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void ACE_refusals_are_named_a_line_and_a_field_each_out_of_stock_and_a_refused_buy(string key)
    {
        var home = new Home(key, 2_000_000, held: l => l.Name is "Prismatic Taper" or "Mana Potion" ? l.Keep - 5 : l.Keep);
        var tapers = home.ShopOf(Line(home.Ground, "Prismatic Taper").Wcid);
        var row = tapers.Rows.Single(r => r.Name == "Prismatic Taper");
        tapers.Rows[tapers.Rows.IndexOf(row)] = row with { Stack = 0 };
        home.ShopOf(Line(home.Ground, "Mana Potion").Wcid).BuyRefusal = "Your pack is full."; // a test text: ACE's own line for it is unread (TODO(m5-resupply-refusal-text))
        var run = home.Start();

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

        Assert.Equal(0, run.Bought);
        Assert.Equal(2, run.Refusals.Count);
        Assert.Contains("stock", run.Refusals);
        Assert.Contains("pack", run.Refusals);
        Assert.True(home.Logged("refused: stock: Prismatic Taper"));
        Assert.True(home.Logged("refused: pack: Mana Potion x5"));
        Assert.Contains("refused=2", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Empty(run.Problems);
    }

    [Fact]
    public void A_buy_refused_for_coin_is_kind_coin()
    {
        Assert.Equal("coin", ResupplyRun.Kind("the vendor refused the buy (GameEvent 0x00A0): no reason given (too few pyreals: the price is 300 and the player holds 100; ACE sends no text for it)", "other"));
        Assert.Equal("unsellable", ResupplyRun.Kind("The Bread is unsellable.", "other"));
        Assert.Equal("other", ResupplyRun.Kind(null, "other"));
    }

    [Theory]
    [InlineData(Den, "Shopkeeper Renald the Elder")]
    [InlineData(Temple, "Shopkeeper Yoshi Deshi")]
    public void The_loot_is_sold_at_the_loot_buyer_only_with_looting_on_and_the_unsellable_is_left_and_named(string key, string buyer)
    {
        foreach (var loot in new[] { true, false })
        {
            var home = new Home(key, 1000);
            var shirt = home.Own("Shirt", 130, ClothingType, 100);
            var bread = home.Own("Bread", VendorStock.BreadWcid, 0x20, 5);
            var cap = home.Own("Odd Cap", 9_999, ClothingType, 50);
            home.Shops.Get(home.Ground.Vendors.Single(v => v.Name == buyer).Guid).Unsellable.Add(9_999);
            var run = home.Start(new ResupplyOptions(loot, () => [shirt, bread, cap]));

            Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

            if (loot)
            {
                var visit = Assert.Single(run.Visits);
                Assert.Equal(buyer, visit.Vendor.Name);
                Assert.True(visit.Sell);
                Assert.True(home.Logged($"walking: purpose=vendor:{buyer} (0 lines to buy, 3 items to sell)"));
                Assert.Equal(1, run.Sold);
                Assert.Equal(90, run.Earned); // floor(0.9 x 100 + 0.1)
                Assert.Equal(["unsellable", "unsellable"], run.Refusals); // the Bread by ACE's class list before the send, the cap by ACE's 02EB
                Assert.False(home.Vendor.State.Self.Inventory.Contains(shirt));
                Assert.True(home.Vendor.State.Self.Inventory.Contains(bread));
                Assert.True(home.Vendor.State.Self.Inventory.Contains(cap));
                Assert.True(home.Logged("TODO(m4-vendor-issellable-wire)"));
            }
            else
            {
                Assert.Empty(run.Visits);
                Assert.Empty(home.Travel.Requests);
                Assert.Equal(0, home.Shops.Sells);
                Assert.Contains("sold=0", run.VerdictFields(), StringComparison.Ordinal);
                Assert.Contains("loot=off", run.VerdictFields(), StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_price_that_differs_from_the_profile_is_logged_and_the_wire_is_paid(string key)
    {
        var home = new Home(key, 2_000_000, held: l => l.Name == "Prismatic Taper" ? l.Keep - 10 : l.Keep);
        var taper = Line(home.Ground, "Prismatic Taper");
        var profile = home.Ground.PriceOf(taper.Wcid)!.Value;
        var shop = home.ShopOf(taper.Wcid);
        var row = shop.Rows.Single(r => r.Wcid == taper.Wcid);
        shop.Rows[shop.Rows.IndexOf(row)] = row with { Value = profile + 5 };
        var run = home.Start();

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

        Assert.True(home.Logged($"price: Prismatic Taper at {home.Ground.VendorSelling(taper.Wcid)!.Name}: the wire's {profile + 5} pyreals, the profile's {profile}: the wire's used"));
        Assert.Equal(10L * (profile + 5), run.Spent);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void The_vendor_is_met_by_guid_where_he_stands_and_his_live_place_is_logged_beside_the_profiles(string key)
    {
        var g = GroundCatalog.Get(key);
        var seller = g.VendorSelling(Line(g, "Mana Potion").Wcid)!;
        var home = new Home(key, g.Supplies.Purse.Coin + g.PriceOf(Line(g, "Mana Potion").Wcid)!.Value + 50, held: l => l.Name == "Mana Potion" ? l.Keep - 1 : l.Keep); // the coin back at the floor after the buy: no notes walk
        var outdoors = (seller.Cell & 0xFFFF0000) | 0x000F;
        var at = seller.Point + new Vector3(4, 3, 0);
        home.Vendor.PlaceAt(seller.Guid, seller.Name, outdoors, at, 3f, seller.Wcid);
        var run = home.Start();

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

        var request = Assert.Single(home.Travel.Requests);
        Assert.Equal(seller.Guid, request.ObjectGuid);
        Assert.Equal(3.0, request.Range, 3);
        Assert.Equal(outdoors, request.Destination.Location.Position.CellId); // where he stands, not the profile's stop
        Assert.True(home.Logged($"vendor: {seller.Name} live 0x{outdoors:X8} ({at.X:0.0}, {at.Y:0.0}, {at.Z:0.0}), profile 0x{seller.Cell:X8}"));
        Assert.True(home.Logged("off by 5.0 m"));
        Assert.Equal(1, run.Bought);
    }

    /// <summary>
    /// M5 item 3 fix F4. Only the den has a vendor whose stop cell is a room in front of him (Archmage Cindrue's entry hall, Renald's front room): every Fort Tethana vendor stops in his own cell, so the temple has no such case and
    /// Thelnoth is the same-cell vendor. Cindrue is placed with the default use radius (0.6 m, reach 1.6 m) so the player at her stop point is out of reach, as the dats put it (3.0 m under her floor).
    /// </summary>
    [Fact]
    public void A_vendor_in_a_room_in_front_of_him_is_walked_to_as_a_point_and_opened_beyond_his_reach_with_no_second_walk_while_a_same_cell_vendor_keeps_his_object_goal()
    {
        var g = GroundCatalog.Get(Den);
        var cindrue = g.VendorSelling(Line(g, "Prismatic Taper").Wcid)!;
        var thelnoth = g.VendorSelling(Line(g, "Mana Potion").Wcid)!;
        Assert.NotEqual(cindrue.Cell, cindrue.StopCell);
        Assert.Equal(thelnoth.Cell, thelnoth.StopCell);
        var home = new Home(Den, g.Supplies.Purse.Coin + (5 * (g.PriceOf(Line(g, "Prismatic Taper").Wcid)!.Value + g.PriceOf(Line(g, "Mana Potion").Wcid)!.Value)) + 50, held: l => l.Name is "Prismatic Taper" or "Mana Potion" ? l.Keep - 5 : l.Keep); // no notes walk
        home.Vendor.PlaceAt(cindrue.Guid, cindrue.Name, cindrue.Cell, cindrue.Point, 0.6f, cindrue.Wcid);
        var run = home.Start();

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

        // Cindrue: one walk, a point request to her stop cell and stop point; no object goal, no range, no second walk.
        var hers = Assert.Single(home.Travel.Requests, r => r.Destination.Name == cindrue.Name);
        Assert.Equal(0u, hers.ObjectGuid);
        Assert.Equal(0.0, hers.Range);
        Assert.Equal(new CellPosition(cindrue.StopCell, cindrue.StopPoint!.Value), hers.Destination.Location.Position);
        Assert.DoesNotContain(home.Travel.Requests, r => r.ObjectGuid == cindrue.Guid);
        var metres = Vector3.Distance(cindrue.StopPoint!.Value, cindrue.Point);
        Assert.True(metres > 1.6f, "the stop point is beyond the 1.6 m reach");
        Assert.True(home.Logged($"vendor: {cindrue.Name} is {metres:0.0} m from his front room 0x{cindrue.StopCell:X8}, beyond his reach 1.6 m: the room graph stops here (TODO(room-steps)); the open asks ACE's move-to for the rest (Player_Use.cs:205 CreateMoveToChain, Player_Move.cs:92 MoveToObject @95ef426d)"));
        Assert.False(home.Logged($"vendor: {cindrue.Name} is {metres:0.0} m away after the walk"), "no second walk is announced");

        // Thelnoth: stands in his own stop cell, so his walk is the object goal as before, and no front-room line.
        var his = Assert.Single(home.Travel.Requests, r => r.Destination.Name == thelnoth.Name);
        Assert.Equal(thelnoth.Guid, his.ObjectGuid);
        Assert.Equal(3.0, his.Range, 3);
        Assert.Equal(thelnoth.StopCell, his.Destination.Location.Position.CellId);
        Assert.False(home.Logged($"vendor: {thelnoth.Name} is"));

        // The use opened both: the open is not gated on the distance, and both stocks were bought back to the keep.
        Assert.Equal(2, run.Vendors);
        Assert.Equal(10, run.Bought);
        Assert.Equal(0, run.NotFound);
        Assert.Empty(run.Refusals);
    }

    /// <summary>
    /// M5 item 7 (D120, the soak's round 10): a same-cell vendor not yet in the object table is walked to as a point request to his own cell and point, stopping <see cref="ShopLeg.UnseenVendorReachMetres"/> (1.6 m) short,
    /// because since item 5 his own body stands on that point in the player's physics world and a 0.5 m stop there jams against him. A front-room vendor's point request keeps no range: the body stands on the stop point.
    /// The temple's taper seller is the unseen vendor here (the vendor rig always creates Thelnoth, the den's same-cell vendor, so his walk is the object goal).
    /// </summary>
    [Fact]
    public void An_unseen_same_cell_vendor_is_walked_to_as_a_point_request_that_stops_short_of_his_body()
    {
        var g = GroundCatalog.Get(Temple);
        var seller = g.VendorSelling(Line(g, "Prismatic Taper").Wcid)!;
        Assert.Equal(seller.Cell, seller.StopCell);
        Assert.Null(seller.StopPoint);
        var home = new Home(Temple, 2_000_000, held: l => l.Name is "Prismatic Taper" or "Mana Potion" ? l.Keep - 5 : l.Keep, absent: v => v.Guid == seller.Guid);
        var run = home.Start();

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run, 800));

        var his = home.Travel.Requests.First(r => r.Destination.Name == seller.Name); // M6 8 I: the first walk is the shopping's; Zisaha sells the purse notes now, a later walk converts the coin
        Assert.Equal(0u, his.ObjectGuid);
        Assert.Equal(ShopLeg.UnseenVendorReachMetres, his.Range);
        Assert.Equal(1.6, his.Range, 3);
        Assert.Equal(new CellPosition(seller.Cell, seller.Point), his.Destination.Location.Position);
        Assert.Equal(1, run.NotFound); // he never appeared in this rig; the station goes on

        var den = GroundCatalog.Get(Den);
        var cindrue = den.VendorSelling(Line(den, "Prismatic Taper").Wcid)!;
        var front = new Home(Den, 2_000_000, held: l => l.Name == "Prismatic Taper" ? l.Keep - 5 : l.Keep, absent: v => v.Guid == cindrue.Guid);
        Assert.Equal(ScenarioStatus.Succeeded, front.Run(front.Start(), 800));
        var hers = front.Travel.Requests.Where(r => r.Destination.Name == cindrue.Name).ToList();
        Assert.NotEmpty(hers); // M5 7: her lines and her notes may each walk to her while she is unseen
        Assert.All(hers, r =>
        {
            Assert.Equal(0u, r.ObjectGuid);
            Assert.Equal(0.0, r.Range); // a front-room stop point: the body stands on it
        });
    }

    [Fact]
    public void Away_from_the_home_the_station_fails_named_and_buys_nothing()
    {
        var home = new Home(Den, 2_000_000, held: l => l.TripUnder - 1);
        home.Vendor.StandAt(0x01050100, new Vector3(10, 10, 0));
        var run = home.Start();

        Assert.Equal(ScenarioStatus.Failed, home.Run(run));

        Assert.Empty(home.Travel.Requests);
        Assert.Contains("why=not_at_the_home", run.VerdictFields(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void M5_3_F5_a_kit_trip_buys_the_ground_s_wielded_kit_line_at_the_vendor_that_sells_it_and_counts_and_sells_nothing_else(string key)
    {
        var home = new Home(key, 2_000_000, held: l => l.TripUnder - 1); // every stock line is short: a stock trip would buy them all
        var wand = home.Ground.Supplies.Kit.Single(k => k.Slot == KitSlot.Wielded);
        var vendor = home.Ground.VendorSelling(wand.Wcid)!;
        var run = home.Start(new ResupplyOptions(Loot: true, Kit: [wand]));

        var status = home.Run(run);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        var buy = Assert.Single(run.BuyList);
        Assert.Equal((wand.Wcid, vendor, 1), (buy.Line.Wcid, buy.Vendor, buy.Count));
        Assert.Equal(vendor.Name, Assert.Single(run.Visits).Vendor.Name);
        Assert.Equal(TravelPurpose.Vendor, Assert.Single(home.Travel.Requests).Purpose);
        Assert.True(home.Logged($"walking: purpose=vendor:{vendor.Name} (1 lines to buy)"));
        Assert.True(home.Logged("resupply: a kit trip (D53)"));
        Assert.False(home.Logged("sale: kit"), "no loot is offered on a kit trip");
        Assert.Equal((1L, (long)home.Ground.PriceOf(wand.Wcid)!.Value, 0), (run.Bought, run.Spent, run.Sold)); // one wand at the profile's price, nothing else
        Assert.Equal(1, home.Shops.Buys);
        Assert.Equal(0, home.Stations.Places);
        Assert.StartsWith($"bought=1 spent={run.Spent} notes_sold=0 sold=0 earned=0 vendors=1 refused=0 refusals=none short=0 not_found=0 loot=on problems=0 granted=0", run.VerdictFields(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void M5_3_F5_a_kit_line_no_vendor_sells_is_named_and_never_granted(string key)
    {
        var home = new Home(key, 2_000_000);
        var nothing = new KitLine(999_999, "Wand Nobody Sells", KitSlot.Wielded);
        var unsold = new List<KitLine>();
        var planned = ResupplyPlanner.KitBuyList(home.Ground, [home.Ground.Supplies.Kit.Single(k => k.Slot == KitSlot.Wielded), nothing], unsold);
        Assert.Single(planned);
        Assert.Equal([nothing], unsold);

        var run = home.Start(new ResupplyOptions(Kit: [nothing]));
        Assert.Equal(ScenarioStatus.Failed, home.Run(run));

        Assert.Contains($"no vendor of {home.Ground.Home.Town} sells the kit line Wand Nobody Sells (weenie 999999): the profile lists none, and nothing is granted (D51)", run.Failure, StringComparison.Ordinal);
        Assert.Empty(home.Travel.Requests);
        Assert.Equal(0L, run.Bought);
        Assert.Equal(0, home.Shops.Buys);
        Assert.Contains("why=", run.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_kind_is_registered_and_the_catalog_builds_it_with_the_fights_loot_switch()
    {
        Assert.Contains(StationKinds.Resupply, StationKinds.All);
        var spec = new StationSpec("resupply", StationKinds.Resupply, null, 120);
        var off = Assert.IsType<ResupplyStation>(JourneyStations.Create(JourneyPlans.Mage, [], null).Create(spec));
        var on = Assert.IsType<ResupplyStation>(JourneyStations.Create(JourneyPlans.Mage, ["--fight-loot=on"], null).Create(spec));
        Assert.False(off.Options.Loot);
        Assert.True(on.Options.Loot);
        Assert.True(off.Needs.Peace);
        Assert.DoesNotContain(JourneyPlans.All.SelectMany(p => p.Stations), s => s.Kind == StationKinds.Resupply); // slice c2's logistics station calls it
    }

    [Fact]
    public void The_kept_loot_is_sold_to_every_vendor_that_buys_its_type_the_Jeweler_for_jewellery_and_gems_M5_7_F28()
    {
        var ground = GroundCatalog.Get(Den);
        const uint Armour = 0x2, Jewelry = 0x8, Gem = 0x800, Caster = 0x8000;
        var types = new uint[] { Armour, Jewelry, Gem, Armour, Caster, Jewelry, 0x4000000 }; // the last: a type no vendor buys

        var buyers = ResupplyPlanner.LootBuyers(ground, types);

        Assert.Equal([("Monyra the Jeweler", 3), ("Shopkeeper Renald the Elder", 2), ("Archmage Cindrue", 1)], buyers.Select(b => (b.Vendor.Name, b.Count))); // most items first; the unbuyable one is in no count
        Assert.Empty(ResupplyPlanner.LootBuyers(ground, [0x4000000u]));
        Assert.Equal("Monyra the Jeweler", ResupplyPlanner.LootBuyer(ground, types)!.Name); // the one-buyer reading is unchanged: the vendor taking the most

        var visits = ResupplyPlanner.Order([], buyers, Vector2.Zero);

        Assert.Equal(3, visits.Count);
        Assert.All(visits, v => Assert.True(v.Sell));
        Assert.Equal(6, visits.Sum(v => v.SellCount));
        Assert.Contains(visits, v => v.Vendor.Wcid == 716 && v.SellCount == 3 && v.WalkLine.Contains("3 items to sell", StringComparison.Ordinal));
    }

    /// <summary>Fills the pack with worthless items (Value 0: never offered) until <paramref name="free"/> slots are left.</summary>
    private static void FillPackTo(Home home, int free)
    {
        var i = 0;
        while (Lifestone.Client.Session.Quartermaster.PackView.MainPackFree(home.Vendor.State) > free)
        {
            home.Own("Filler " + ++i, 9_000 + (uint)i, MiscType, 0);
        }
    }

    [Fact]
    public void M5_7_G1_the_coin_above_the_purse_s_coin_is_turned_into_the_largest_trade_note_a_vendor_sells()
    {
        var keep = (int)CoinFloat.Of(0); // M6 14 pack rules: the float, the trip's own spend (nothing: every line at its keep) rounded up to one whole coin stack
        var home = new Home(Den, keep + 860_000);
        var cindrue = home.Ground.VendorSelling(20630);
        Assert.Equal("Archmage Cindrue", cindrue!.Name);
        var price = home.Ground.PriceOf(20630)!.Value;
        Assert.Equal(287_500, price); // 250,000 x 1.15 (Vendor.cs 577-585 @95ef426d)
        var run = home.Start(new ResupplyOptions(true));

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

        // M5 7 D136 follow-up: greedy, largest first, notes of min_note (25,000) and up only: 860,000 above the floor = 2 x 250,000 (575,000), 1 x 200,000 (230,000); 55,000 stays coin (under the 50,000's 57,500)
        Assert.Equal(3, run.NotesBought);
        Assert.Equal(keep + 55_000, home.Vendor.State.Self.Qualities.CoinValue);
        Assert.True(home.Logged("purse: bought 2 x Trade Note (250,000) at Archmage Cindrue"));
        Assert.True(home.Logged("purse: bought 1 x Trade Note (200,000) at Archmage Cindrue"));
        Assert.False(home.Logged("x Trade Note (10,000) at Archmage Cindrue")); // under one coin stack: no slot saved
        Assert.Contains("notes_bought=3", run.VerdictFields(), StringComparison.Ordinal);

        // the coin under the floor plus the smallest such note's price (the 50,000's 57,500) buys nothing, walks nowhere and says why
        var thin = new Home(Den, keep + 57_499);
        var thinRun = thin.Start(new ResupplyOptions(true));
        Assert.Equal(ScenarioStatus.Succeeded, thin.Run(thinRun));
        Assert.Equal(0, thinRun.NotesBought);
        Assert.Empty(thin.Travel.Requests);
        Assert.True(thin.Logged($"purse: coin {keep + 57_499}, floor {keep} (float {keep}: the trip's own spend 0 "));
        Assert.True(thin.Logged("), above 57499, notes of 25000 and up (a smaller note saves no pack slot and costs 15%): no notes: the coin above the floor is under the smallest such note's price (Trade Note (50,000) at 57500); it stays coin"));
    }

    [Fact]
    public void M5_7_G1_a_sale_the_free_slots_cannot_hold_goes_in_batches_and_is_never_refused_whole_12b()
    {
        var home = new Home(Den, 1_000);
        var pieces = Enumerable.Range(0, 8).Select(i => home.Own("Rich Armour " + i, 5_000 + (uint)i, 0x2, 50_000)).ToList();
        FillPackTo(home, 3);
        Assert.Equal(3, Lifestone.Client.Session.Quartermaster.PackView.MainPackFree(home.Vendor.State));
        var run = home.Start(new ResupplyOptions(true));

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run, 4000));

        Assert.Equal(0, home.Shops.SlotRefusals); // the shop models ACE's whole-sale refusal (Player_Commerce.cs 176-191): it never happened
        Assert.DoesNotContain("pack", run.Refusals);
        Assert.True(run.Sold >= 1, "the first batch sold");
        Assert.True(home.Logged("items wait: their payout needs more coin stacks than the 3 free slots hold"));
        Assert.Equal(8 - run.Sold, pieces.Count(g => home.Vendor.State.Self.Inventory.Contains(g))); // what waited is still in the pack, for the next round or trip
    }

    [Fact]
    public void M5_7_D99_a_full_pack_drops_the_offers_cheapest_item_to_open_a_slot_and_then_sells()
    {
        // Soak round 11: no free slot sold nothing (ACE's coin needs a slot), and one free slot was held back for a note, so a full pack never sold.
        var home = new Home(Den, 1_000);
        var pieces = Enumerable.Range(0, 6).Select(i => home.Own("Armour " + i, 5_000 + (uint)i, 0x2, 4_000u + ((uint)i * 1_000u))).ToList();
        FillPackTo(home, 0);
        Assert.Equal(0, Lifestone.Client.Session.Quartermaster.PackView.MainPackFree(home.Vendor.State));
        var run = home.Start(new ResupplyOptions(true));

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run, 4000));

        Assert.Equal(0, home.Shops.SlotRefusals);
        Assert.Equal(1, run.DroppedItems);
        Assert.Equal(1 + run.JunkDropped, home.Shops.Drops); // M6 14 pack rules: after the sale the junk pass drops the value-0 fillers (value-zero-drop), each its own drop
        Assert.Equal(80, run.JunkDropped);
        Assert.False(home.Vendor.State.Self.Inventory.Contains(pieces[0])); // the cheapest went to the ground
        Assert.True(home.Logged("the offer's cheapest, was dropped at the home to open one"));
        Assert.True(run.Sold >= 1, "a sale followed the drop");
    }

    [Fact]
    public void M5_7_G1_the_sale_offers_every_pack_item_that_is_not_kit_so_loot_kept_before_a_death_or_a_restart_sells_with_no_kept_list()
    {
        var home = new Home(Den, 1_000);
        var shirt = home.Own("Shirt", 130, ClothingType, 100);
        var run = home.Start(new ResupplyOptions(true, null)); // no kept list at all

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

        Assert.Equal(1, run.Sold);
        Assert.False(home.Vendor.State.Self.Inventory.Contains(shirt));
        Assert.False(home.Logged("TODO(m5-resupply-kept-list)"));
    }

    [Fact]
    public void M5_7_F30_a_kept_aside_item_is_never_offered_by_guid_and_by_its_appraisal_so_a_restart_still_protects_it()
    {
        var home = new Home(Den, 1_000);
        var noted = home.Own("Noted Necklace", 9_301, 0x8, 1_000); // M6 14 (the owner, 2026-11-16: drop the loot casters too): the memory's note is shown on a non-caster
        var wand = home.Own("Slashing Sceptre of Blades", 9_302, 0x8000, 1_000); // nothing noted it: the appraisal the session holds says +4%
        var plain = home.Own("Plain Wand", 9_303, 0x8000, 1_000); // asked about before the sale; the answer carries no bonus
        var shirt = home.Own("Shirt", 130, ClothingType, 100); // the positive control
        var memory = new SaleMemory();
        memory.KeptAside.Add(noted);
        home.Vendor.State.Appraisals.Add(MageKeepAside.Answer(wand, 1.04), 0);
        var run = home.Start(new ResupplyOptions(true, null, null, memory));

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

        var sent = home.Vendor.H.Net.Of(FakeSendKind.Sell).SelectMany(s => s.Items ?? []).Select(i => i.item).ToList();
        Assert.Equal([shirt], sent.Order()); // the noted necklace is not offered; the two wands are the caster rule's: with no caster in hand, the floor's two (M6 14)
        Assert.True(home.Vendor.State.Self.Inventory.Contains(noted) && home.Vendor.State.Self.Inventory.Contains(wand) && home.Vendor.State.Self.Inventory.Contains(plain));
        Assert.False(home.Logged("is kept aside (War Magic bonus +4%)")); // the war bonus alone keeps no caster now
        Assert.True(home.Logged("is kept aside (her gear rules need it: the gear floor's spare"));
        Assert.True(home.Logged("is kept aside (noted by the loot step), worth a look: not offered"));
        Assert.True(home.Logged("asked about 2 caster/wearable pack item(s) for the keep-aside rule"));
    }

    [Fact]
    public void M6_14_a_Major_cantrip_piece_on_tusker_barracks_is_kept_aside_for_the_climber_and_never_sold()
    {
        // The Barracks' profile has no keep_aside block (the grounds hold facts only); the climber's default is the den's block, and a profile's own block still wins.
        var barracks = GroundCatalog.Get("tusker-barracks");
        Assert.Null(barracks.Loot?.KeepAside);
        Assert.Equal(GroundCatalog.Get(Den).Loot!.KeepAside, KeepAsideRule.ClimberDefault);
        Assert.Equal(KeepAsideRule.ClimberDefault, KeepAsideRule.SettingFor(barracks, MageTemplate.ClimberName));
        Assert.Null(KeepAsideRule.SettingFor(barracks, "BSD Mage")); // no default for any other character
        var holding = GroundCatalog.Get("tusker-holding");
        Assert.Same(holding.Loot!.KeepAside, KeepAsideRule.SettingFor(holding, MageTemplate.ClimberName));

        // The fight's loot runs it on the Barracks (the journey sets the default from the plan's character): a Major Focus ring is kept aside whatever it is worth, and counted worth a look.
        var fight = new FightOptions { Loot = true, Ground = "tusker-barracks", KeepAsideDefault = KeepAsideRule.DefaultFor(MageTemplate.ClimberName) };
        Assert.Null((fight with { KeepAsideDefault = null }).EffectiveLootThresholds.KeepAside);
        var spells = new Dictionary<uint, SpellInfo> { [2574] = WorldActionRig.Spell(2574, DatReaderWriter.Enums.SpellType.Enchantment) with { Name = "Major Focus" } };
        var rules = new MageLootRules(fight.EffectiveLootThresholds, spells);
        var corpseRing = Lifestone.Client.Tests.Session.Fighter.LootRulesTests.Item(0x8, 1, 15, 2, "Ring");
        var found = rules.ItemFor(corpseRing, MageKeepAside.Appraised(2, null, 2574), held: false);
        var plan = rules.Plan("Corpse of Tusker Guard", "Winnie", [found], burden: 0, capacity: 17_850, freeSlots: 20);
        Assert.Equal(("Major Focus", MageLootVerdict.KeepAside, 1), (found.KeepAside, plan.VerdictOf(2), plan.WorthALookCount)); // Value 1: under every floor

        // The sale at home: the ring is never offered (by its appraisal, with no loot step's note), the shirt beside it is.
        var home = new Home("tusker-barracks", 1_000);
        var ring = home.Own("Ring", 297, 0x8, 2_000);
        var shirt = home.Own("Shirt", 130, ClothingType, 100);
        home.Vendor.State.Appraisals.Add(MageKeepAside.Answer(ring, null, 2574), 0);
        var run = home.Start(new ResupplyOptions(true, null), spells, character: MageTemplate.ClimberName);

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

        var sent = home.Vendor.H.Net.Of(FakeSendKind.Sell).SelectMany(s => s.Items ?? []).Select(i => i.item).ToList();
        Assert.Contains(shirt, sent);
        Assert.DoesNotContain(ring, sent);
        Assert.True(home.Vendor.State.Self.Inventory.Contains(ring));
        Assert.True(home.Logged("is kept aside (Major Focus), worth a look: not offered"));
    }

    [Fact]
    public void M5_7_G1_the_whole_kit_is_offered_to_the_sell_step_and_none_of_it_is_sold()
    {
        var home = new Home(Den, 1_000);
        var ground = home.Ground;
        var protectedWcids = new HashSet<uint>();
        foreach (var kit in ground.Supplies.Kit)
        {
            home.Own(kit.Name, kit.Wcid, kit.Slot == KitSlot.Wielded ? 0x8000u : 0x2u, 1_000); // the wand a caster, the robe and gauntlets armour: types the vendors buy
            protectedWcids.Add(kit.Wcid);
        }

        foreach (var focus in Lifestone.Client.Session.Mage.GearKit.MageFociWcids)
        {
            home.Own("Focus " + focus, focus, MiscType, 1_000); // Renald's mask takes the foci's type
            protectedWcids.Add(focus);
        }

        foreach (var line in ground.Supplies.Stock)
        {
            protectedWcids.Add(line.Wcid); // Home owns each at its keep, Value 1
        }

        foreach (var note in ground.Supplies.Purse.Notes)
        {
            protectedWcids.Add(note.Wcid); // Home owns the purse's notes
        }

        home.Own("Pyreal", 273, 0x40, 5_000);
        protectedWcids.Add(273);
        var shirt = home.Own("Shirt", 130, ClothingType, 100); // the positive control: loot is sold
        var run = home.Start(new ResupplyOptions(true, null));

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

        Assert.Equal(1, run.Sold);
        Assert.False(home.Vendor.State.Self.Inventory.Contains(shirt));
        var sent = home.Vendor.H.Net.Of(FakeSendKind.Sell).SelectMany(s => s.Items ?? []).Select(i => i.item).ToList();
        Assert.Equal([shirt], sent);
        foreach (var wcid in protectedWcids)
        {
            Assert.Contains(home.Vendor.State.Self.Inventory.Pack, e => home.Vendor.State.Objects.TryGet(e.Guid, out var o) && o.Weenie.WeenieClassId == wcid);
        }
    }

    [Fact]
    public void M6_sprint_an_item_ace_marks_unsellable_is_appraised_before_the_sale_and_never_offered()
    {
        // Runs 1-5 (2026-11-19): "The Health Draughts is unsellable." and the like, 19 times in run 5: weenies 2457, 377, 379 and 2460 carry IsSellable 0 (world weenie_properties_bool type 69) and ACE refuses them
        // (Player_Commerce.cs 268-272 @95ef426d); they reached the sale as the pack budget's surplus drinks. The rule is the class's, whatever offers it: the appraisal carries the bool, so the sale looks first, the item stays
        // in the pack and the vendor never answers "is unsellable". A class with no use (9_998) stands for them here: a drink she uses is kept by its own rule before the sale.
        var home = new Home(Den, 1_000);
        var shirt = home.Own("Shirt", 130, ClothingType, 100);
        var charm = home.Own("Odd Charm", 9_998, MiscType, 85);
        home.Shops.AnswersIdentify = true;
        home.Shops.NotSellable.Add(9_998);
        home.Shops.Get(home.Ground.Vendors.Single(v => v.Name == "Shopkeeper Renald the Elder").Guid).Unsellable.Add(9_998);
        var memory = new SaleMemory();
        var run = home.Start(new ResupplyOptions(true, () => [shirt, charm], null, memory));

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));
        Assert.Contains(charm, home.Shops.Identified);
        Assert.True(home.Logged("marked unsellable on the wire"));
        Assert.True(home.Logged("m6-sale-appraise"));
        Assert.Equal(1, run.Sold);
        Assert.Equal(["unsellable"], run.Refusals); // the client's own check before the send; no 02EB from the vendor
        Assert.True(home.Vendor.State.Self.Inventory.Contains(charm));
        Assert.False(home.Vendor.State.Self.Inventory.Contains(shirt));
        Assert.False(memory.SellableClass[9_998]);
        Assert.True(memory.SellableClass[130]);
    }

    [Theory]
    [InlineData(2457u, "Health Draught")]
    [InlineData(2460u, "Mana Draught")]
    public void M6_7_AU_an_unsellable_potion_she_drinks_is_kept_never_dropped_after_two_refusals(uint wcid, string name)
    {
        var home = new Home(Den, 1_000);
        home.Own(name, wcid, MiscType, 85);
        home.Shops.Get(home.Ground.Vendors.Single(v => v.Name == "Shopkeeper Renald the Elder").Guid).Unsellable.Add(wcid);
        var memory = new SaleMemory();

        for (var trip = 0; trip < 3; trip++)
        {
            var run = home.Start(new ResupplyOptions(true, null, null, memory));
            Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));
            Assert.Equal(0, run.DroppedItems);
        }

        Assert.Empty(home.Vendor.H.Net.Of(FakeSendKind.DropItem));
        Assert.True(ResupplyRun.IsUsableConsumable(wcid));
        Assert.False(ResupplyRun.IsUsableConsumable(9_999)); // an item with no use is still dropped (the test above)
    }

    [Fact]
    public void M5_7_G1_an_item_a_vendor_refuses_on_two_trips_is_dropped_once_and_never_offered_again()
    {
        var home = new Home(Den, 1_000);
        home.Own("Odd Cap", 9_999, ClothingType, 50);
        home.Shops.Get(home.Ground.Vendors.Single(v => v.Name == "Shopkeeper Renald the Elder").Guid).Unsellable.Add(9_999);
        var memory = new SaleMemory();

        var first = home.Start(new ResupplyOptions(true, null, null, memory));
        Assert.Equal(ScenarioStatus.Succeeded, home.Run(first));
        Assert.Equal(0, first.DroppedItems);
        Assert.Empty(home.Vendor.H.Net.Of(FakeSendKind.DropItem));

        var second = home.Start(new ResupplyOptions(true, null, null, memory));
        Assert.Equal(ScenarioStatus.Succeeded, home.Run(second));
        Assert.Equal(1, second.DroppedItems);
        Assert.Single(home.Vendor.H.Net.Of(FakeSendKind.DropItem));
        Assert.True(home.Logged("was refused by a vendor on 2 trips: dropped at the home"));

        var sellsBefore = home.Shops.Sells;
        var third = home.Start(new ResupplyOptions(true, null, null, memory));
        Assert.Equal(ScenarioStatus.Succeeded, home.Run(third));
        Assert.Equal(sellsBefore, home.Shops.Sells); // not offered again
        Assert.Empty(third.Refusals);
        Assert.Equal(0, third.DroppedItems);
    }

    /// <summary>
    /// M6 2 b: the outskirts home of a climber at <paramref name="level"/>: the stock full (no stock line to buy), the Pathwarden Robe worn (the chest covered), the Training Wand wielded or not, Archmage Cindrue's Wand row a caster
    /// (ItemType 0x8000, as ACE creates it), and ACE's side of the gear phase and the equip pass: each Identify answered plain, each wield and move as ACE answers it, or every wield refused.
    /// </summary>
    private sealed class FloorHome
    {
        private int _seen;
        private uint _seq = 3000;
        private byte _wield = 90;

        /// <param name="coins">The coin she holds (M6 item 14: a purse that pays for the leather set makes it due on the trip; <c>ResupplyStationTests.Leather.cs</c>).</param>
        /// <param name="notes">True: the purse's trade note is held too.</param>
        /// <param name="gauntlets">True (the default): the chest's Pathwarden Gauntlets are worn; false: the hands are left to the test.</param>
        /// <param name="description">M6 16 E5e5: her description when a test reads her skills (an untrained defence against a piece's limit); null: the rig's plain one.</param>
        public FloorHome(int level = 10, bool training = true, bool refuseWield = false, int coins = 10_000, bool notes = true, bool gauntlets = true, PlayerDescriptionMsg? description = null)
        {
            Home = new Home("holtburg-outskirts", coins, notes: notes, description: description);
            RefuseWield = refuseWield;
            var r = Home.Vendor.R;
            r.PrivateInt(25, level);
            var rows = Home.ShopOf(2472).Rows;
            var i = rows.FindIndex(x => x.Wcid == 2472);
            rows[i] = rows[i] with { ItemType = 0x8000 };
            if (training)
            {
                Training = Home.Own("Training Wand", 12748, 0x8000, 25);
                r.Server((p, s) => AceM3Writers.WieldObject(p, s, Training, (int)HeldSlot));
            }

            Robe = Home.Own("Pathwarden Robe", 40439, ClothingType, 0);
            r.Server((p, s) => AceM3Writers.WieldObject(p, s, Robe, 0x7F00));
            if (gauntlets)
            {
                var pair = Home.Own("Pathwarden Gauntlets", 33606, ClothingType, 0); // M6 7 AH: the outskirts sell hand armour now, so the climber of these floor tests wears the chest's pair (the floor asks nothing of the hands)
                r.Server((p, s) => AceM3Writers.WieldObject(p, s, pair, 0x20));
            }
        }

        public Home Home { get; }

        public bool RefuseWield { get; }

        /// <summary>M6 2 f: the guids whose wield ACE refuses (the others are answered).</summary>
        public HashSet<uint> RefuseOnly { get; } = [];

        public uint Training { get; }

        public uint Robe { get; }

        public PlayerInventory Inventory => Home.Vendor.State.Self.Inventory;

        public int WieldsRefused { get; private set; }

        /// <summary>The int properties an Identify of an item answers with (ItemCurMana 107, ItemMaxMana 108), by guid; plain when absent.</summary>
        public Dictionary<uint, Dictionary<uint, int>> Ints { get; } = [];

        /// <summary>M6 16 E5e5: the spells an Identify of an item answers with, by guid (a piece's cantrips, so an appraisal asked again mid-trip still carries them); none when absent.</summary>
        public Dictionary<uint, uint[]> Spells { get; } = [];

        public uint OwnSpareWand() => Home.Own("Wand", 2472, 0x8000, 200);

        public void Pump()
        {
            Home.Shops.Pump();
            var r = Home.Vendor.R;
            var sends = r.H.Net.Sends;
            for (; _seen < sends.Count; _seen++)
            {
                var s = sends[_seen];
                switch (s.Kind)
                {
                    case FakeSendKind.Identify:
                        r.H.R.Reducer.OnIdentifyResponse(r.H.R.Context, new GameEventHeader(VendorRig.Player, ++_seq, 0x00C9), new IdentifyResponseMsg(
                            s.Guid, IdentifyResponseFlags.IntStatsTable | (Spells.ContainsKey(s.Guid) ? IdentifyResponseFlags.SpellBook : 0), true, Ints.TryGetValue(s.Guid, out var ints) ? ints.ToImmutableDictionary() : ImmutableDictionary<uint, int>.Empty, ImmutableDictionary<uint, long>.Empty, ImmutableDictionary<uint, bool>.Empty, ImmutableDictionary<uint, double>.Empty,
                            ImmutableDictionary<uint, string>.Empty, ImmutableDictionary<uint, uint>.Empty, Spells.TryGetValue(s.Guid, out var cast) ? [.. cast] : [], null, null, null, null, null, null, null, null));
                        break;
                    case FakeSendKind.GetAndWieldItem:
                        if (RefuseWield || RefuseOnly.Contains(s.Guid))
                        {
                            WieldsRefused++;
                            Home.Vendor.ItemRefusal(s.Guid);
                            break;
                        }

                        r.Raw(AceM3Writers.PublicUpdateInstanceId(++_wield, s.Guid, 3, 0));
                        r.Raw(AceM3Writers.PublicUpdatePropertyInt(_wield, s.Guid, 10, 0));
                        r.Server((p, q) => AceM3Writers.WieldObject(p, q, s.Guid, (int)s.Amount));
                        break;
                    case FakeSendKind.PutItemInContainer:
                        var target = uint.Parse(s.Target!, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        if (Inventory.Find(s.Guid).Kind == InventoryPlaceKind.Equipped)
                        {
                            r.Raw(AceM3Writers.PublicUpdateInstanceId(++_wield, s.Guid, 3, 0));
                            r.Raw(AceM3Writers.PublicUpdatePropertyInt(_wield, s.Guid, 10, 0));
                        }

                        Home.Vendor.Put(s.Guid, target);
                        break;
                }
            }
        }

        public ScenarioStatus Run(ResupplyRun run) => Home.Stations.Run(run, 600, pump: Pump);
    }

    [Fact]
    public void M6_2_b_a_one_caster_climber_buys_the_floor_Wand_at_Cindrue_beside_the_kit_and_the_equip_pass_reads_casters_2()
    {
        var f = new FloorHome();
        var run = f.Home.Start(new ResupplyOptions(true, () => [], Gear: true));

        Assert.Equal(ScenarioStatus.Succeeded, f.Run(run));

        var line = Assert.Single(run.FloorPlan!.Lines);
        Assert.Equal((GearFloorKind.Caster, 2472u, 270, "Archmage Cindrue"), (line.Kind, line.Wcid, line.Price, line.Vendor.Name));
        Assert.Equal(2472u, run.BuyList[0].Line.Wcid); // the floor's line first, beside the stock
        Assert.Empty(f.Home.Ground.Supplies.Kit); // never in supplies.kit: the kit-less death's rearm stays corpse or none
        Assert.True(GearKit.HasNoKit(f.Home.Ground));
        Assert.Equal((1, 270L), (run.FloorBought, run.Spent));
        Assert.Empty(run.FloorRefused);
        Assert.True(f.Home.Logged("gear floor: buy 1 x Wand at Archmage Cindrue: check before: open 0x7A9B4033 yes, the profile's price 270, coin 10000, held 0"));
        Assert.True(f.Home.Logged("gear floor: bought 1 x Wand at Archmage Cindrue: check after: held 0 -> 1, paid 270"));
        Assert.Equal(f.Training, f.Inventory.EquippedAt(HeldSlot)); // the plain Wand is no better: the Training Wand stays in the hands, the Wand is the spare
        Assert.Equal(f.Robe, f.Inventory.EquippedAt(0x7F00));
        Assert.Equal("equip pass: worn=0 replaced=0 casters=2 refused=0 (none)", run.EquipLine);
        Assert.True(f.Home.Logged("equip pass: worn=0 replaced=0 casters=2 refused=0 (none)"));
        var order = f.Home.Lines.ToList();
        Assert.True(order.FindIndex(l => l.Contains("gear floor: bought", StringComparison.Ordinal)) < order.FindIndex(l => l.Contains("equip pass: worn=", StringComparison.Ordinal))); // after the buys
        Assert.Null(run.Failure);
    }

    [Fact]
    public void M6_2_b_with_no_caster_held_the_floor_buys_two_and_a_refused_wield_is_named_and_ends_in_the_no_caster_stop()
    {
        var f = new FloorHome(training: false, refuseWield: true);
        var run = f.Home.Start(new ResupplyOptions(true, () => [], Gear: true));

        Assert.NotEqual(ScenarioStatus.Running, f.Run(run));

        Assert.Equal(2, run.FloorPlan!.Lines.Count);
        Assert.Equal(2, run.FloorBought);
        Assert.Null(f.Inventory.EquippedAt(HeldSlot));
        Assert.True(f.WieldsRefused >= 1);
        Assert.True(f.Home.Logged("gear: caster pick: keep no caster")); // two plain Wands beat no held caster by nothing: the rearm wields one
        Assert.StartsWith("equip pass: worn=0 replaced=0 casters=2 refused=1 (rearm 'Wand' (ServerError: the server refused 0x", run.EquipLine, StringComparison.Ordinal); // the refused wield, named
        Assert.StartsWith("no-caster: at the end of the trip: the hands hold no caster after the caster pick: the wield of 'Wand' 0x", run.Failure, StringComparison.Ordinal);
        Assert.True(f.Home.Logged("gear: STOP no-caster: the hands hold no caster after the caster pick and none can be wielded: the packs hold no caster")); // before the buys: the floor may still buy one
    }

    [Fact]
    public void M6_2_b_below_the_floor_level_the_trip_buys_no_caster_and_the_element_is_the_rungs()
    {
        var f = new FloorHome(level: 4);
        var run = f.Home.Start(new ResupplyOptions(true, () => [], Gear: true));

        Assert.Equal(ScenarioStatus.Succeeded, f.Run(run));

        Assert.Empty(run.FloorPlan!.Lines);
        Assert.DoesNotContain(run.BuyList, b => b.Line.Wcid == 2472);
        Assert.Equal(0, run.FloorBought);
        Assert.True(f.Home.Logged("gear floor: level 4 is under the floor level 5 (the ladder's start rung's move_on.level): nothing; casters=1"));
        Assert.True(f.Home.Logged("gear: element 0x10: the spell plan's family Flame Bolt 0x10 (no ladder rung of level 4 names holtburg-outskirts)"));

        var seven = new FloorHome(level: 7);
        var onRung = seven.Home.Start(new ResupplyOptions(true, () => [], Gear: true));
        Assert.Equal(ScenarioStatus.Succeeded, seven.Run(onRung));
        Assert.True(seven.Home.Logged("gear: element 0x10: the rung's element fire 0x10 (rung outskirts)")); // m5-self-equip-element: the rung first
    }

    [Fact]
    public void M6_2_b_a_caster_whose_spells_dropped_is_wielded_again_with_check_act_check_and_the_flag_clears()
    {
        var f = new FloorHome();
        f.OwnSpareWand(); // two casters: the floor buys nothing
        f.Ints[f.Training] = new() { [107] = 100, [108] = 500 }; // mana back after a refill, its spells still off
        CasterSpellsDropped.Shared.Mark("Training Wand", [f.Training]);
        try
        {
            var run = f.Home.Start(new ResupplyOptions(true, () => [], Gear: true));

            Assert.Equal(ScenarioStatus.Succeeded, f.Run(run));

            Assert.True(f.Home.Logged("-> Rewield (mana is back but its spells dropped"));
            Assert.True(f.Home.Logged("gear: rewield 'Training Wand' 0x" + f.Training.ToString("X8", CultureInfo.InvariantCulture) + ": off to "));
            Assert.True(f.Home.Logged("check after: held, its spells back with its mana"));
            Assert.Equal(f.Training, f.Inventory.EquippedAt(HeldSlot));
            Assert.False(CasterSpellsDropped.Shared.IsDropped(f.Training, "Training Wand"));
            Assert.Empty(run.FloorPlan!.Lines);
        }
        finally
        {
            CasterSpellsDropped.Shared.Reset();
        }
    }

    [Fact]
    public void M6_2_b_the_casters_mana_want_joins_the_floor_and_the_High_Mana_Charge_is_bought_at_Cindrue()
    {
        var f = new FloorHome();
        f.OwnSpareWand();
        f.Ints[f.Training] = new() { [107] = 0, [108] = 500 }; // out of mana, no charged spare, no stone (CasterManaRule: WantStone)
        var run = f.Home.Start(new ResupplyOptions(true, () => [], Gear: true));

        Assert.Equal(ScenarioStatus.Succeeded, f.Run(run));

        var line = Assert.Single(run.FloorPlan!.Lines);
        Assert.Equal((GearFloorKind.ManaCharge, 4615u, 1, 3375, "Archmage Cindrue"), (line.Kind, line.Wcid, line.Count, line.Price, line.Vendor.Name)); // the smallest charge that covers the 500 (row 21054)
        Assert.True(f.Home.Logged("gear mana: the caster's want joins the floor: out of mana, no charged spare and no charged stone is held; want 1 x wcid 4615"));
        Assert.Equal(1, run.FloorBought);
        Assert.Equal(3375, run.Spent);
    }

    [Fact]
    public void M6_2_b_a_kit_trip_plans_no_floor_and_runs_no_equip_pass()
    {
        var f = new FloorHome(training: false);
        var run = f.Home.Start(new ResupplyOptions(Kit: [new KitLine(2472, "Wand", KitSlot.Wielded)]));

        f.Run(run);

        Assert.Null(run.FloorPlan);
        Assert.Null(run.EquipLine);
    }

    private static uint _appraisalSeq = 7000;

    /// <summary>The session holds an appraisal of <paramref name="guid"/> (what an Identify answers): its ints (114 Attuned, 45 DamageType), floats and bools (91 Retained).</summary>
    private static void Appraise(Home home, uint guid, Dictionary<uint, int>? ints = null, Dictionary<uint, double>? floats = null, Dictionary<uint, bool>? bools = null)
    {
        var r = home.Vendor.R;
        r.H.R.Reducer.OnIdentifyResponse(r.H.R.Context, new GameEventHeader(VendorRig.Player, ++_appraisalSeq, 0x00C9), new IdentifyResponseMsg(
            guid,
            IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.BoolStatsTable | IdentifyResponseFlags.FloatStatsTable,
            true,
            (ints ?? []).ToImmutableDictionary(),
            ImmutableDictionary<uint, long>.Empty,
            (bools ?? []).ToImmutableDictionary(),
            (floats ?? []).ToImmutableDictionary(),
            ImmutableDictionary<uint, string>.Empty,
            ImmutableDictionary<uint, uint>.Empty,
            [],
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null));
    }

    [Fact]
    public void M6_2_d_the_sale_never_offers_an_attuned_a_value_0_or_a_retained_piece_and_names_them_once_a_trip()
    {
        var f = new FloorHome();
        var shirt = f.Home.Own("Shirt", 130, ClothingType, 100); // the positive control: plain loot is sold
        var attuned = f.Home.Own("Attuned Cap", 9_001, ClothingType, 50);
        var valueless = f.Home.Own("Pathwarden Sash", 9_002, ClothingType, 0);
        var retained = f.Home.Own("Retained Glove", 9_003, ClothingType, 40);
        Appraise(f.Home, attuned, ints: new() { [114] = 1 }); // PropertyInt 114 Attuned (ACE refuses a drop; the sale pays nothing the quest pieces are worth)
        Appraise(f.Home, retained, bools: new() { [91] = true }); // PropertyBool 91 Retained (Player_Commerce.cs:268)
        var run = f.Home.Start(new ResupplyOptions(true, () => []));

        Assert.Equal(ScenarioStatus.Succeeded, f.Run(run));

        var sent = f.Home.Vendor.H.Net.Of(FakeSendKind.Sell).SelectMany(s => s.Items ?? []).Select(i => i.item).ToList();
        Assert.Equal([shirt], sent);
        Assert.Equal(1, run.Sold);
        Assert.All(new[] { attuned, valueless, retained }, g => Assert.True(f.Inventory.Contains(g)));
        Assert.Empty(run.Refusals); // nothing was offered to be refused
        Assert.Equal(1, f.Home.Lines.Count(l => l.Contains("sale: kept 3 (1 attuned, 1 retained, 1 value 0)", StringComparison.Ordinal)));
    }

    [Fact]
    public void M6_2_d_the_spare_caster_the_floor_keeps_is_never_sold_and_the_second_round_does_not_name_it_again()
    {
        var f = new FloorHome();
        var spare = f.OwnSpareWand(); // the Training Wand in hand and a Wand (Value 200, a caster Archmage Cindrue buys) in the pack: the floor wants two
        var run = f.Home.Start(new ResupplyOptions(true, () => []));

        Assert.Equal(ScenarioStatus.Succeeded, f.Run(run));

        Assert.Equal(0, run.Sold);
        Assert.True(f.Inventory.Contains(spare));
        Assert.Empty(f.Home.Vendor.H.Net.Of(FakeSendKind.Sell));
        Assert.Equal(1, f.Home.Lines.Count(l => l.Contains("is kept aside (her gear rules need it: not appraised: never sold blind), worth a look: not offered", StringComparison.Ordinal))); // M6 14: the caster rule (CasterKeep) names the keep
    }

    [Fact]
    public void M6_2_d_with_more_casters_than_the_floor_wants_the_best_by_score_is_kept_and_the_worse_one_is_loot()
    {
        var f = new FloorHome();
        var plain = f.OwnSpareWand();
        var fire = f.OwnSpareWand();
        Appraise(f.Home, plain, ints: new() { [45] = 0x10 }, floats: new() { [AppraisalIds.ElementalDamageMod] = 1.0 });
        Appraise(f.Home, fire, ints: new() { [45] = 0x10 }, floats: new() { [AppraisalIds.ElementalDamageMod] = 1.05 }); // fire is the rung's element: the better caster
        var run = f.Home.Start(new ResupplyOptions(true, () => []));

        Assert.Equal(ScenarioStatus.Succeeded, f.Run(run));

        Assert.Equal(1, run.Sold); // three casters held (the Training Wand wielded), two wanted: one spare is kept, the other sold
        Assert.True(f.Inventory.Contains(fire));
        Assert.False(f.Inventory.Contains(plain));
        Assert.True(f.Home.Logged("'Wand' 0x" + fire.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) + " is kept aside (her gear rules need it: ")); // M6 14: the caster rule (CasterKeep) names the keep
        Assert.True(f.Home.Logged("gear: caster keep: 'Wand' 0x" + plain.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) + ": sell loot"));
    }

    [Fact]
    public void M6_2_d_an_unappraised_extra_caster_is_never_sold_on_a_guess()
    {
        var f = new FloorHome();
        var a = f.OwnSpareWand();
        var b = f.OwnSpareWand(); // three held, two wanted, and no appraisal of either spare to rank them by
        var run = f.Home.Start(new ResupplyOptions(true, () => []));

        Assert.Equal(ScenarioStatus.Succeeded, f.Run(run));

        Assert.Equal(0, run.Sold);
        Assert.True(f.Inventory.Contains(a) && f.Inventory.Contains(b));
        Assert.Equal(2, f.Home.Lines.Count(l => l.Contains("is kept aside (her gear rules need it: not appraised: never sold blind)", StringComparison.Ordinal))); // M6 14: the caster rule (CasterKeep)
    }
}
