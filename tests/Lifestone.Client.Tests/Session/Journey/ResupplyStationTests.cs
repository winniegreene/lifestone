using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.World;
using Lifestone.Net.Types;
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

        public Home(string key, int coins, Func<StockLine, int>? held = null, bool notes = true, Func<GroundVendor, bool>? absent = null, Lifestone.Net.Messages.PlayerDescriptionMsg? description = null)
        {
            Ground = GroundCatalog.Get(key);
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

        public ScriptedShop.Shop ShopOf(uint wcid) => Shops.Get(Ground.VendorSelling(wcid)!.Guid);

        public ResupplyRun Start(ResupplyOptions? options = null) => (ResupplyRun)new ResupplyStation(options).Start(new JourneyContext
        {
            Character = "BSD Mage",
            Account = "test",
            Log = Vendor.H.Log,
            Travel = Travel,
            Spells = new Dictionary<uint, SpellInfo>(),
            Dungeon = Ground,
        });

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

        var his = Assert.Single(home.Travel.Requests, r => r.Destination.Name == seller.Name);
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
        while (Lifestone.Client.Session.Mage.MageLootRules.FreeSlots(home.Vendor.State) > free)
        {
            home.Own("Filler " + ++i, 9_000 + (uint)i, MiscType, 0);
        }
    }

    [Fact]
    public void M5_7_G1_the_coin_above_the_purse_s_coin_is_turned_into_the_largest_trade_note_a_vendor_sells()
    {
        var home = new Home(Den, 900_000);
        var cindrue = home.Ground.VendorSelling(20630);
        Assert.Equal("Archmage Cindrue", cindrue!.Name);
        var price = home.Ground.PriceOf(20630)!.Value;
        Assert.Equal(287_500, price); // 250,000 x 1.15 (Vendor.cs 577-585 @95ef426d)
        var keep = home.Ground.Supplies.Purse.Coin;
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
        Assert.True(thin.Logged($"purse: coin {keep + 57_499}, floor {keep}, above 57499, notes of 25000 and up (a smaller note saves no pack slot and costs 15%): no notes: the coin above the floor is under the smallest such note's price (Trade Note (50,000) at 57500); it stays coin"));
    }

    [Fact]
    public void M5_7_G1_a_sale_the_free_slots_cannot_hold_goes_in_batches_and_is_never_refused_whole_12b()
    {
        var home = new Home(Den, 1_000);
        var pieces = Enumerable.Range(0, 8).Select(i => home.Own("Rich Armour " + i, 5_000 + (uint)i, 0x2, 50_000)).ToList();
        FillPackTo(home, 3);
        Assert.Equal(3, Lifestone.Client.Session.Mage.MageLootRules.FreeSlots(home.Vendor.State));
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
        Assert.Equal(0, Lifestone.Client.Session.Mage.MageLootRules.FreeSlots(home.Vendor.State));
        var run = home.Start(new ResupplyOptions(true));

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run, 4000));

        Assert.Equal(0, home.Shops.SlotRefusals);
        Assert.Equal(1, run.DroppedItems);
        Assert.Equal(1, home.Shops.Drops);
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
        var noted = home.Own("Noted Sceptre", 9_301, 0x8000, 1_000);
        var wand = home.Own("Slashing Sceptre of Blades", 9_302, 0x8000, 1_000); // nothing noted it: the appraisal the session holds says +4%
        var plain = home.Own("Plain Wand", 9_303, 0x8000, 1_000); // asked about before the sale; the answer carries no bonus
        var shirt = home.Own("Shirt", 130, ClothingType, 100); // the positive control
        var memory = new SaleMemory();
        memory.KeptAside.Add(noted);
        home.Vendor.State.Appraisals.Add(MageKeepAside.Answer(wand, 1.04), 0);
        var run = home.Start(new ResupplyOptions(true, null, null, memory));

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

        var sent = home.Vendor.H.Net.Of(FakeSendKind.Sell).SelectMany(s => s.Items ?? []).Select(i => i.item).ToList();
        Assert.Equal([plain, shirt], sent.Order()); // the noted sceptre and the +4% one are not offered
        Assert.True(home.Vendor.State.Self.Inventory.Contains(noted) && home.Vendor.State.Self.Inventory.Contains(wand));
        Assert.True(home.Logged("is kept aside (War Magic bonus +4%), worth a look: not offered"));
        Assert.True(home.Logged("is kept aside (noted by the loot step), worth a look: not offered"));
        Assert.True(home.Logged("asked about 2 caster/wearable pack item(s) for the keep-aside rule"));
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
}
