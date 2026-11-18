using System.Collections.Immutable;
using System.Globalization;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Magic;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M5 item 3 c2a: the logistics station over both dungeon profiles (the den's Holtburg, the Tusker Temple's Fort Tethana) on the scripted vendors (<see cref="ScriptedShop"/>) and a scripted experience server: the forced trip end to end (Lifestone Recall
/// home, the vendors, the buys, the experience, the recall back, <c>granted=0 places=0</c>), the counters' own trip without the force, a skip when none is due, a stop that fails by name even when forced, a resupply that fails named with the recall
/// back still made, a failed recall home, the verb counter's delta read, the kept loot reaching the sale, and the fields' names and order. Every number is the profile's; no vendor, price or threshold is a constant here.
/// </summary>
public sealed class LogisticsStationTests(ITestOutputHelper output)
{
    private const string Den = "black-spawn-den-aluvian";
    private const string Temple = "tusker-temple-fort-tethana";
    private const uint MiscType = 0x80;
    private const uint ClothingType = 0x4;

    private static readonly XpTableData Table = ScriptedMage.SyntheticTable();

    private static readonly string[] FieldNames = ["trigger", "granted", "places", "loot", "sold", "bought", "spent", "notes_sold", "spent_xp", "raises", "learned", "scrolls_bought"];

    /// <summary>A travel seam that records each request and puts the player at its destination at once (a recall for Home and Ground, a run for the rest), or fails or stays put for a named purpose.</summary>
    internal sealed class TripTravel(VendorRig rig) : IJourneyTravel
    {
        public List<TravelRequest> Requests { get; } = [];

        /// <summary>The purpose whose travel fails (<c>travel:recall:refused</c>).</summary>
        public TravelPurpose? FailOn { get; set; }

        /// <summary>The purpose whose travel arrives without moving the player (a recall that did not take the mage home).</summary>
        public TravelPurpose? StayPutOn { get; set; }

        /// <summary>Called as each travel starts (a test that makes the station send a mod verb).</summary>
        public Action<TravelRequest>? OnGoTo { get; set; }

        public bool Busy => false;

        /// <summary>M6 8 K: the survival layer's holds of the walk.</summary>
        public int WalkHolds { get; private set; }

        /// <summary>M6 8 K: the survival layer's releases of the walk.</summary>
        public int WalkReleases { get; private set; }

        public void HoldWalk() => WalkHolds++;

        public void ReleaseWalk(double now) => WalkReleases++;

        public ITravelLeg GoTo(IGameSessionView session, TravelRequest request, double now)
        {
            Requests.Add(request);
            OnGoTo?.Invoke(request);
            var recall = request.Purpose is TravelPurpose.Home or TravelPurpose.Ground;
            if (FailOn == request.Purpose)
            {
                return new Leg(recall ? TravelMeans.Recall : TravelMeans.Run, "travel:recall:refused (a scripted refusal)");
            }

            if (StayPutOn != request.Purpose)
            {
                var at = request.Destination.Location.Position;
                rig.StandAt(at.CellId, at.Local);
            }

            return new Leg(recall ? TravelMeans.Recall : TravelMeans.Run, null);
        }

        private sealed class Leg(TravelMeans means, string? failure) : ITravelLeg
        {
            public TravelMeans Means => means;

            public string? Failure => failure;

            public string Fields => string.Empty;

            public TravelStatus Step(IGameSessionView session, double now) => failure is null ? TravelStatus.Arrived : TravelStatus.Failed;

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

    /// <summary>The session view of a station: <see cref="J3aView"/>'s members and the XpTable the host gives the session.</summary>
    internal sealed class TripView(WorldActionRig rig) : IGameSessionView
    {
        public GameState State => rig.H.State;

        public IEventBus Events => rig.H.R.Bus;

        public IActionApi Actions => rig.Api;

        public PlayerTools Tools => rig.Api.Tools;

        public SessionClock Clock => rig.H.R.Clock;

        public ActionLog ActionLog => rig.H.ActionLog;

        public LinkStats Link => default;

        public XpTableData? XpTable => Table;

        /// <summary>M6 16 E2b: the live positions the view answers with: the server's reports, until a test draws bodies.</summary>
        public LivePositions Live { get; set; } = LivePositions.ReportsOnly;

        /// <summary>The window's close, as the game session's (M5 7 P7n: the autopilot's audit closes a vendor left open).</summary>
        public void CloseVendorWindow() => rig.H.R.Reducer.CloseOpenVendor("the player closed the vendor window");
    }

    /// <summary>The scripted ACE for the raises (as XpSpendRuleTests': the available experience first, then the stat's private update); the character starts with every planned stat at rank 0 and a Strength so its burden capacity is known.</summary>
    internal sealed class XpServer
    {
        private readonly ActionHarness _h;
        private readonly Dictionary<(MageStatKind Kind, uint Id), (uint Ranks, uint Spent, uint Class)> _stats = [];
        private int _seen;
        private long _total;
        private byte _sequence = 80; // M5 7 P7n: this world's own: a counter shared by the tests in parallel jumped a quality's 8-bit stamp past 127 and the reducer dropped the raise as stale (the "trip 5 s" flake)

        public XpServer(ActionHarness h, ulong total, long available)
        {
            _h = h;
            _total = (long)total;
            Available = available;
            Plan = MageSpendingPlanner.Compute(Table, total);
            var attributes = new List<AttributeDesc> { new(1, 0, 100, 0) }; // Strength, so the capacity is known
            var vitals = new List<VitalDesc>();
            var skills = new List<SkillDesc>();
            foreach (var t in Plan.Targets)
            {
                var cls = t.Kind != MageStatKind.Skill ? 0u : t.Specialized ? 3u : 2u;
                _stats[(t.Kind, t.Id)] = (0, 0, cls);
                switch (t.Kind)
                {
                    case MageStatKind.Attribute:
                        attributes.RemoveAll(a => a.Id == t.Id);
                        attributes.Add(new AttributeDesc(t.Id, 0, 100, 0));
                        break;
                    case MageStatKind.Vital:
                        vitals.Add(new VitalDesc(t.Id, 0, 0, 0, 100));
                        break;
                    default:
                        skills.Add(new SkillDesc(t.Id, 0, 1, cls, 0, 0, 0, 0.0));
                        break;
                }
            }

            h.R.Reducer.OnPlayerDescription(
                h.R.Context,
                new GameEventHeader(ActionHarness.Amy, 1, GameEventTypes.PlayerDescription),
                new PlayerDescriptionMsg
                {
                    Int64Properties = ImmutableDictionary<uint, long>.Empty
                        .Add(PlayerQualities.TotalExperienceProperty, (long)total)
                        .Add(PlayerQualities.AvailableExperienceProperty, available),
                    Attributes = [.. attributes],
                    Vitals = [.. vitals],
                    Skills = [.. skills],
                });
            h.Tick();
        }

        public SpendingPlan Plan { get; }

        public long Available { get; private set; }

        /// <summary>Every raise ACE took.</summary>
        public int Raised { get; private set; }

        /// <summary>M6 7 R: while true ACE drops every raise without a word (as it does a raise over the pool): the action times out.</summary>
        public bool Deaf { get; set; }

        /// <summary>M6 7 R: the kills' experience: the total and the pool grow by <paramref name="xp"/>, as ACE's two updates do.</summary>
        public void Earn(long xp)
        {
            _total += xp;
            Available += xp;
            Update(PrivateUpdateKind.PropertyInt64, PlayerQualities.TotalExperienceProperty, _total);
            Update(PrivateUpdateKind.PropertyInt64, PlayerQualities.AvailableExperienceProperty, Available);
        }

        public void Pump()
        {
            var sends = _h.Net.Sends;
            while (_seen < sends.Count)
            {
                var send = sends[_seen++];
                var kind = send.Kind switch
                {
                    FakeSendKind.RaiseAttribute => MageStatKind.Attribute,
                    FakeSendKind.RaiseVital => MageStatKind.Vital,
                    FakeSendKind.RaiseSkill => MageStatKind.Skill,
                    _ => (MageStatKind?)null,
                };
                if (kind is null || Deaf)
                {
                    continue;
                }

                var id = (uint)send.Guid;
                var xp = (uint)send.Amount;
                Raised++;
                var (ranks, spent, cls) = _stats[(kind.Value, id)];
                spent += xp;
                var list = kind.Value switch
                {
                    MageStatKind.Attribute => Table.AttributeXpList,
                    MageStatKind.Vital => Table.VitalXpList,
                    _ => cls >= 3 ? Table.SpecializedSkillXpList : Table.TrainedSkillXpList,
                };
                ranks = 0;
                while (ranks + 1 < list.Count && list[(int)ranks + 1] <= spent)
                {
                    ranks++;
                }

                _stats[(kind.Value, id)] = (ranks, spent, cls);
                Available -= xp;
                Update(PrivateUpdateKind.PropertyInt64, PlayerQualities.AvailableExperienceProperty, Available);
                switch (kind)
                {
                    case MageStatKind.Attribute:
                        Update(PrivateUpdateKind.Attribute, id, attribute: new AttributeDesc(id, ranks, 100, spent));
                        break;
                    case MageStatKind.Vital:
                        Update(PrivateUpdateKind.Vital, id, vital: new VitalDesc(id, ranks, 0, spent, 100));
                        break;
                    default:
                        Update(PrivateUpdateKind.Skill, id, skill: new SkillDesc(id, (ushort)ranks, 1, cls, spent, 0, 0, 0.0));
                        break;
                }
            }
        }

        private void Update(PrivateUpdateKind kind, uint key, long value = 0, AttributeDesc attribute = default, VitalDesc vital = default, SkillDesc skill = default) =>
            _h.R.Reducer.OnPrivateUpdate(_h.R.Context, new PrivateUpdateMsg(kind, ++_sequence, key, value, attribute, vital, skill));
    }

    /// <summary>The world of one ground's trip: the mage in the ground (or at the home), each home vendor at his profile place with his shop scripted from his <c>sells</c>, the stock held as told, the purse in coin and notes.</summary>
    internal sealed class Trip
    {
        private uint _row = 0x80006000;
        private uint _item = 0x80007000;

        public Trip(string key, int coins = 300_000, Func<StockLine, int>? held = null, bool inGround = true, long xp = 6000, int sidePacks = 0)
        {
            Ground = GroundCatalog.Get(key);
            Vendor = new VendorRig(coins);
            Xp = new XpServer(Vendor.H, (ulong)xp, xp);
            Vendor.Coins(coins);
            Shops = new ScriptedShop(Vendor);
            Travel = new TripTravel(Vendor);
            View = new TripView(Vendor.R);
            var arrival = Ground.ArrivalDestination().Location.Position;
            if (inGround)
            {
                Vendor.StandAt(arrival.CellId, arrival.Local);
            }
            else
            {
                Vendor.StandAt(Ground.Home.LifestoneCell, Ground.Home.LifestonePoint);
            }

            foreach (var v in Ground.Vendors)
            {
                Vendor.PlaceAt(v.Guid, v.Name, v.Cell, v.Point, 3f, v.Wcid);
                var shop = Shops.Add(v.Guid, v.BuysItemTypes);
                foreach (var ware in v.Sells)
                {
                    shop.Rows.Add((-1, ++_row, ware.Name, ware.Wcid, MiscType, ware.Price));
                }

                shop.AddTradeNotes(() => ++_row);
            }

            for (var i = 0; i < sidePacks; i++)
            {
                // M6 8 D: the side packs the player holds (the layout wants three; a short layout buys the rest on a trip, ResupplyPlanner.PackBuy)
                var pack = Vendor.NextGuid();
                Vendor.Arrive(pack, "Pack", 136, 0x200, 65);
                Vendor.R.Raw(AceM3Writers.PutObjInContainer(VendorRig.Player, 8_000 + (uint)i, pack, VendorRig.Player, 0, 1));
            }

            foreach (var line in Ground.Supplies.Stock)
            {
                var count = held?.Invoke(line) ?? line.Keep;
                if (count > 0)
                {
                    Own(line.Name, line.Wcid, MiscType, 1, (ushort)count);
                }
            }

            foreach (var note in Ground.Supplies.Purse.Notes)
            {
                for (var i = 0; i < note.Count; i++)
                {
                    Own(note.Name, note.Wcid, VendorStock.PromissoryNoteType, (uint)note.Value);
                }
            }
        }

        public HuntingGround Ground { get; }

        public VendorRig Vendor { get; }

        public XpServer Xp { get; }

        public ScriptedShop Shops { get; }

        public TripTravel Travel { get; }

        public TripView View { get; }

        public uint Own(string name, uint wcid, uint itemType, uint value, ushort? stack = null)
        {
            var guid = ++_item;
            Vendor.Arrive(guid, name, wcid, itemType, value, stack);
            Vendor.Put(guid);
            return guid;
        }

        public JourneyContext Context() => new()
        {
            Character = "BSD Mage",
            Account = "test",
            Log = Vendor.H.Log,
            Travel = Travel,
            Spells = new Dictionary<uint, SpellInfo>(),
            Dungeon = Ground,
        };

        public IStationRun Start(bool forced, ResupplyOptions? options = null) => new LogisticsStation(forced, options).Start(Context());

        public ScenarioStatus Run(IStationRun run, int frames = 4000)
        {
            var status = ScenarioStatus.Running;
            for (var i = 0; i < frames && status == ScenarioStatus.Running; i++)
            {
                Vendor.R.Tick(0.25);
                Shops.Pump();
                Xp.Pump();
                Vendor.R.Tick();
                status = run.Step(View, Vendor.H.R.Clock.Now);
            }

            output.WriteLine(string.Join(Environment.NewLine, Lines.Where(l => l.Contains("logistics", StringComparison.Ordinal) || l.Contains("walking:", StringComparison.Ordinal))));
            return status;
        }

        private ITestOutputHelper output = null!;

        public Trip WithOutput(ITestOutputHelper o)
        {
            output = o;
            return this;
        }

        public IEnumerable<string> Lines => Vendor.H.Log.Entries.Select(e => e.Message);

        public bool Logged(string part) => Lines.Any(l => l.Contains(part, StringComparison.Ordinal));

        /// <summary>The mod verbs the scripted server was sent (the rig's own <c>status</c> request is not one).</summary>
        public int ModVerbsSent => Vendor.H.Net.ToolRequests().Count(t => t is not null && new[] { "item ", "xp ", "place ", "corpses ", "spell " }.Any(v => t.StartsWith("@lifestone " + v, StringComparison.Ordinal)));
    }

    private Trip Make(string key, int coins = 300_000, Func<StockLine, int>? held = null, bool inGround = true, long xp = 6000, int sidePacks = 0) => new Trip(key, coins, held, inGround, xp, sidePacks).WithOutput(output);

    private static string Field(string fields, string name) =>
        fields.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(p => p.StartsWith(name + "=", StringComparison.Ordinal))?[(name.Length + 1)..] ?? "?";

    private static int Int(string fields, string name) => int.Parse(Field(fields, name), CultureInfo.InvariantCulture);

    /// <summary>Every restockable line one under its trip line (above its stop line); the carried lines at their keep.</summary>
    private static Func<StockLine, int> AllLow(HuntingGround g) => l => g.IsCarried(l.Wcid) ? l.Keep : Math.Max(l.StopUnder, l.TripUnder - 1);

    [Theory]
    [InlineData(Den, "Archmage Cindrue", "Thelnoth Cort the Healer", "Archmage Cindrue")]
    [InlineData(Temple, "Shopkeeper Yoshi Deshi", "Zisaha bint Jaidka the Mage", "Zisaha bint Jaidka the Mage")] // M6 8 I: Zisaha sells the purse notes, so the fort converts its coin too
    public void The_forced_trip_recalls_home_walks_to_the_vendors_buys_spends_the_experience_and_recalls_back_with_no_grant_and_no_place(string key, string first, string second, string? notes)
    {
        // M5 7 D136 follow-up: the den's coin above the floor after the buys goes into notes at Cindrue, a walk of its own after the others (M6 8 I: the fort's Zisaha sells the purse notes too)
        List<string> vendors = notes is null ? [first, second] : [first, second, notes];
        var trip = Make(key, held: AllLow(GroundCatalog.Get(key)), sidePacks: 3); // M6 8 D: the layout is whole, so no pack is bought and the walk is the stock's
        var run = trip.Start(forced: true);

        var status = trip.Run(run);

        var fields = run.VerdictFields();
        output.WriteLine(fields);
        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.True(run.Ok, run.Failure);
        var purposes = trip.Travel.Requests.Select(r => r.Purpose).ToList();
        Assert.Equal([TravelPurpose.Home, .. vendors.Select(_ => TravelPurpose.Vendor), TravelPurpose.Ground], purposes); // the recall home, the vendors, the recall back
        Assert.Equal(trip.Ground.Home.LifestoneCell >> 16, trip.Travel.Requests[0].Destination.Location.Position.CellId >> 16);
        Assert.Equal(trip.Ground.ArrivalDestination().Location.Position.CellId, trip.Travel.Requests[^1].Destination.Location.Position.CellId);
        Assert.Equal(vendors, trip.Travel.Requests.Where(r => r.Purpose == TravelPurpose.Vendor).Select(r => r.Destination.Name));
        Assert.Equal(notes is not null, trip.Logged("purse: bought "));
        Assert.True(trip.Logged("walking: purpose=home"));
        Assert.True(trip.Logged($"walking: purpose=vendor:{first}"));
        Assert.True(trip.Logged($"walking: purpose=vendor:{second}"));
        Assert.True(trip.Logged("walking: purpose=ground"));
        Assert.Equal(FieldNames, fields.Split(' ').Take(FieldNames.Length).Select(p => p[..p.IndexOf('=', StringComparison.Ordinal)]));
        Assert.StartsWith("trigger=forced granted=0 places=0 loot=off sold=0 ", fields, StringComparison.Ordinal);
        Assert.True(Int(fields, "bought") > 0);
        Assert.True(Int(fields, "spent") > 0);
        Assert.True(Int(fields, "spent_xp") > 0);
        Assert.True(Int(fields, "raises") > 0);
        Assert.Equal(Int(fields, "raises"), trip.Xp.Raised);
        Assert.Equal(6000 - Int(fields, "spent_xp"), trip.Vendor.State.Self.Qualities.AvailableExperience);
        Assert.Equal(0, trip.ModVerbsSent); // no item, xp, place, corpses or spell verb of the mod
        Assert.DoesNotContain(" skipped=", fields, StringComparison.Ordinal);
        Assert.DoesNotContain(" why=", fields, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void Without_the_force_the_counters_start_the_trip_and_name_the_line(string key)
    {
        var ground = GroundCatalog.Get(key);
        var low = ground.Supplies.Stock.First(l => !ground.IsCarried(l.Wcid));
        var trip = Make(key, held: l => l.Wcid == low.Wcid ? Math.Max(l.StopUnder, l.TripUnder - 1) : l.Keep);
        var run = trip.Start(forced: false);

        Assert.Equal(ScenarioStatus.Succeeded, trip.Run(run));

        var fields = run.VerdictFields();
        Assert.Equal("stock:" + low.Name.Replace(' ', '_'), Field(fields, "trigger"));
        Assert.Equal([TravelPurpose.Home, TravelPurpose.Vendor, TravelPurpose.Ground], trip.Travel.Requests.Select(r => r.Purpose).Distinct().ToList());
        Assert.Equal(low.Keep, trip.Vendor.State.Self.Inventory.Pack.Where(e => trip.Vendor.State.Objects.TryGet(e.Guid, out var o) && o.Weenie.WeenieClassId == low.Wcid).Sum(e => trip.Vendor.State.Objects.TryGet(e.Guid, out var o) ? (o.Weenie.StackSize ?? 1) : 0)); // bought back to the keep
        Assert.True(Int(fields, "bought") > 0);
        Assert.Equal("0", Field(fields, "granted"));
        Assert.Equal("0", Field(fields, "places"));
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void With_nothing_due_and_no_force_the_trip_is_skipped_by_name_and_the_mage_stays_in_the_ground(string key)
    {
        var trip = Make(key);
        var run = trip.Start(forced: false);

        var status = trip.Run(run);

        var fields = run.VerdictFields();
        output.WriteLine(fields);
        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal("none", Field(fields, "trigger"));
        Assert.Equal("no-trip-due", Field(fields, "skipped"));
        Assert.Empty(trip.Travel.Requests);
        Assert.Equal(0, Int(fields, "bought"));
        Assert.Equal(0, Int(fields, "spent_xp"));
        Assert.Equal(0, trip.Xp.Raised);
        Assert.True(trip.Logged("logistics: no trip: no-trip-due"));
    }

    [Theory]
    [InlineData(Den, true)]
    [InlineData(Den, false)]
    [InlineData(Temple, true)]
    public void A_stop_fails_the_station_by_name_even_when_the_trip_is_forced_and_the_mage_goes_nowhere(string key, bool forced)
    {
        var ground = GroundCatalog.Get(key);
        var stop = ground.Supplies.Stock.FirstOrDefault(l => l.StopUnder > 0 && ground.IsCarried(l.Wcid)); // M5 7 audit: a carried line (no vendor restocks it); a restockable one is the trip's to restock (below)
        if (stop is null)
        {
            Assert.All(ground.Supplies.Stock.Where(l => l.StopUnder > 0), l => Assert.False(ground.IsCarried(l.Wcid))); // the ground's home sells every line with a stop line: no stop fails its trip
            return;
        }

        var trip = Make(key, held: l => l.Wcid == stop.Wcid ? l.StopUnder - 1 : l.Keep);
        var run = trip.Start(forced);

        var status = trip.Run(run);

        var fields = run.VerdictFields();
        output.WriteLine(fields);
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.False(run.Ok);
        Assert.Contains($"stop: {stop.Name}: {stop.StopUnder - 1} held, under its stop line {stop.StopUnder}", run.Failure, StringComparison.Ordinal);
        Assert.Equal("stop:" + stop.Name.Replace(' ', '_'), Field(fields, "trigger"));
        Assert.Contains(" why=logistics:_stop:", fields, StringComparison.Ordinal);
        Assert.Empty(trip.Travel.Requests);
        if (ground.IsCarried(stop.Wcid))
        {
            Assert.DoesNotContain("m5-platinum-stock", run.Failure, StringComparison.Ordinal); // M6 1 d (D12): the carried stop names the buy-trip
            Assert.Contains("; buy-trip ", run.Failure, StringComparison.Ordinal);
            Assert.True(trip.Logged("logistics: the carried line is the buy-trip's: "));
        }
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_restockable_line_under_its_stop_line_is_restocked_by_the_trip_M5_7_audit(string key)
    {
        var ground = GroundCatalog.Get(key);
        var stop = ground.Supplies.Stock.First(l => l.StopUnder > 0 && !ground.IsCarried(l.Wcid));
        var trip = Make(key, held: l => l.Wcid == stop.Wcid ? l.StopUnder - 1 : l.Keep);
        var run = trip.Start(forced: false);

        Assert.Equal(ScenarioStatus.Succeeded, trip.Run(run));

        var fields = run.VerdictFields();
        output.WriteLine(fields);
        Assert.Equal("stop:" + stop.Name.Replace(' ', '_'), Field(fields, "trigger"));
        Assert.Contains(TravelPurpose.Vendor, trip.Travel.Requests.Select(r => r.Purpose));
        Assert.True(Int(fields, "bought") > 0);
        Assert.True(trip.Logged("logistics: the line under its stop line is sold at home: the trip restocks it (M5 7 audit)"));
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_resupply_that_fails_is_named_the_experience_is_still_spent_and_the_recall_back_is_still_made(string key)
    {
        var trip = Make(key);
        trip.Travel.StayPutOn = TravelPurpose.Home; // the recall arrived but did not take the mage home: the resupply finds him in the ground
        var run = trip.Start(forced: true);

        var status = trip.Run(run);

        var fields = run.VerdictFields();
        output.WriteLine(fields);
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.StartsWith("resupply: not at the home", run.Failure, StringComparison.Ordinal);
        Assert.Contains(" why=resupply:", fields, StringComparison.Ordinal);
        Assert.Contains("not at the home", string.Join('|', trip.Lines), StringComparison.Ordinal);
        Assert.Equal([TravelPurpose.Home, TravelPurpose.Ground], trip.Travel.Requests.Select(r => r.Purpose));
        Assert.True(Int(fields, "spent_xp") > 0);
        Assert.Equal(trip.Ground.ArrivalDestination().Location.Position.CellId, trip.Vendor.State.Self.Position!.Value.CellId); // back in the ground
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_failed_recall_home_ends_the_station_there_named_and_nothing_is_bought(string key)
    {
        var trip = Make(key);
        trip.Travel.FailOn = TravelPurpose.Home;
        var run = trip.Start(forced: true);

        var status = trip.Run(run);

        var fields = run.VerdictFields();
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("travel:recall:refused", run.Failure, StringComparison.Ordinal);
        Assert.Contains(" why=", fields, StringComparison.Ordinal);
        Assert.Equal([TravelPurpose.Home], trip.Travel.Requests.Select(r => r.Purpose));
        Assert.Equal("0", Field(fields, "bought"));
        Assert.Equal(0, trip.Xp.Raised);
    }

    [Fact]
    public void A_mage_that_starts_at_home_goes_to_the_vendors_and_stays_there()
    {
        var trip = Make(Den, inGround: false, held: AllLow(GroundCatalog.Get(Den)));
        var run = trip.Start(forced: true);

        Assert.Equal(ScenarioStatus.Succeeded, trip.Run(run));

        Assert.DoesNotContain(trip.Travel.Requests, r => r.Purpose == TravelPurpose.Ground);
        Assert.True(trip.Logged("the trip did not start in the ground, so there is no recall back"));
    }

    [Fact]
    public void The_fields_count_the_verb_counters_delta_over_the_station_not_a_constant()
    {
        var trip = Make(Den);
        trip.Travel.OnGoTo = r =>
        {
            if (r.Purpose == TravelPurpose.Home)
            {
                trip.View.Tools.Place(r.Destination, ActionSource.Host); // a mod teleport sent inside the station: the counter sees it
            }
        };
        var run = trip.Start(forced: true);

        trip.Run(run);

        var fields = run.VerdictFields();
        output.WriteLine(fields);
        Assert.Equal("1", Field(fields, "places"));
        Assert.Equal("0", Field(fields, "granted"));
    }

    [Theory]
    [InlineData(Den, "Shopkeeper Renald the Elder")]
    [InlineData(Temple, "Shopkeeper Yoshi Deshi")]
    public void With_looting_on_the_fights_kept_list_is_sold_at_the_loot_buyer(string key, string buyer)
    {
        var trip = Make(key);
        var shirt = trip.Own("Shirt", 130, ClothingType, 100);
        var run = trip.Start(forced: true, new ResupplyOptions(Loot: true, Kept: () => [shirt]));

        Assert.Equal(ScenarioStatus.Succeeded, trip.Run(run));

        var fields = run.VerdictFields();
        output.WriteLine(fields);
        Assert.Equal("on", Field(fields, "loot"));
        Assert.Equal("1", Field(fields, "sold"));
        Assert.Contains(trip.Travel.Requests, r => r.Purpose == TravelPurpose.Vendor && r.Destination.Name == buyer);
        Assert.False(trip.Vendor.State.Self.Inventory.Contains(shirt));
    }

    [Fact]
    public void The_catalog_builds_the_station_from_the_plans_kind_the_trip_forced_by_the_argument_and_the_kept_list_from_the_fight()
    {
        var plan = JourneyPlans.Mage;
        var spec = plan.Stations.Single(s => s.Name == "logistics");
        output.WriteLine(string.Join(" -> ", plan.Stations.Select(s => $"{s.Name}[{s.Kind}]={s.ExpectedSeconds:0}s")));

        var forced = (LogisticsStation)JourneyStations.Create(plan, [], null, "acct").Create(spec);
        var unforced = (LogisticsStation)JourneyStations.Create(plan, [], null, "acct").Create(spec with { Args = null });
        var looting = (LogisticsStation)JourneyStations.Create(plan, ["--fight-loot=on"], null, "acct").Create(spec);

        Assert.True(forced.Forced);
        Assert.False(unforced.Forced);
        Assert.False(forced.Resupply.Loot);
        Assert.True(looting.Resupply.Loot);
        Assert.Empty(looting.Resupply.Kept!()); // the list is the den fight's (none ran here), read when the resupply starts
        Assert.Equal("logistics", forced.Name);
        Assert.True(forced.Needs.Peace);
        Assert.Equal(["build-verify", "gear", "buff", "cast-facts", "kit-refused", "harm-heal", "bind", "den-travel", "den-fight", "logistics", "death", "recall-home"], plan.Stations.Select(s => s.Name));
    }

    [Theory]
    [InlineData(false, "at home")]
    [InlineData(true, "in the ground")]
    public void M6_14_T_every_trip_spends_the_experience_before_the_buys_so_the_plan_reads_what_the_spend_leaves(bool inGround, string where)
    {
        // The sprint of 2026-11-12: the run's first trip bought, then spent 22,710 experience; the raised skills made seven more scrolls due with 2,231 coin left. Live at 05:23 UTC, a trip from the ground: the plan read before the spend
        // of 69,280 named one level of the dance line, the counters after it the next, and the visit had served neither.
        const string First = "logistics: the experience is spent before the buys";
        const string Spend = "logistics: experience call 1: ended";
        const string Buys = "logistics: the resupply ended";
        const string After = "logistics: check after the visit: the counters say";

        var trip = Make(Den, inGround: inGround, held: AllLow(GroundCatalog.Get(Den)));
        var run = trip.Start(forced: true);
        Assert.Equal(ScenarioStatus.Succeeded, trip.Run(run));
        var lines = trip.Lines.ToList();
        int At(string part) => lines.FindIndex(l => l.Contains(part, StringComparison.Ordinal));
        Assert.True(At(First) >= 0 && At(First) < At(Spend) && At(Spend) < At(Buys), string.Join(Environment.NewLine, lines.Where(l => l.Contains("logistics", StringComparison.Ordinal))));
        Assert.Contains($"(the trip started {where}; M6 14 T)", lines[At(First)], StringComparison.Ordinal);
        Assert.True(At(Buys) < lines.FindLastIndex(l => l.Contains(Spend, StringComparison.Ordinal))); // the second look after the buys
        Assert.True(At(Buys) < At(After)); // the check after, with the buys and the spend made
        Assert.True(Int(run.VerdictFields(), "spent_xp") > 0);
        Assert.True(Int(run.VerdictFields(), "raises") > 0);
        Assert.Equal(Int(run.VerdictFields(), "raises"), trip.Xp.Raised); // the second look finds the pool spent and raises nothing twice
    }
}
