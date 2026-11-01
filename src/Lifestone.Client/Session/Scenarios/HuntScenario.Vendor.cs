using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The hunt's shops (M4 3.2 s4a; DoD 10 and 12, D14): after the hunt is back in Holtburg (<c>Home</c>, after <c>wind-restore</c> and <c>wind-home</c>, so the Mace is wielded again and the bow is in the pack) and before the log off, the hunter
/// sells its loot to Shopkeeper Renald the Elder (<c>--sell</c>, <see cref="SellRules"/>) and buys the Handy Healing Kits it is short of at Thelnoth Cort the Healer (<c>--buy-kits</c>), then goes back to Holtburg by <c>place poi</c>.
/// Stages <c>vendor-arrive vendor-open vendor-sell vendor-sell-settle vendor-buy vendor-buy-settle</c>. The window <see cref="VendorWindow"/> is what <see cref="VendorFrameSampler"/> measures.
/// </summary>
/// <remarks>
/// <para><b>Getting there.</b> Both shops are indoors, so the hunter is put into the shop by <c>place loc</c> at the vendor's own position (cell, x, y and z of the lead's read-only SELECT, <c>cache/m4/3-2/vendors.tsv</c>, with the vendor's
/// heading): that is within Renald's 5 m and Thelnoth's 3 m use radius at distance 0, and it is a point the server's own data says is walkable inside the cell. <c>TODO(m4-vendor-approach)</c> names the walk through the shop's door that is not
/// built. The vendor is then looked up in the read model by weenie class id (718 Renald, 715 Thelnoth: the guids are the world's) and opened with <c>OpenVendor</c>.</para>
/// <para><b>The sale is batched</b>, 20 items to a <c>Sell</c>: capture C2 shows one sale is one UseDone whatever its length, so a batch costs one round trip, one coin check and one stretch of the vendor window instead of one per item, and the
/// payout is compared per batch; 20 keeps one game action well inside one packet (8 bytes an item). ACE drops an item it will not take with a 02EB line and goes on, so a batch with one unsellable item still sells the rest.</para>
/// <para><b>A failed visit never fails the hunt.</b> Every failure (the teleport, the vendor not in view, the open refused, the sale or the purchase refused) is a warning, <c>sell=failed:&lt;reason&gt;</c> or <c>buy=failed:&lt;reason&gt;</c> on the
/// detail line, and the loop goes on to the way home and the log off. The vendor has no close message in ACE: the visit ends by leaving the shop (the model closes the vendor by distance), which also ends <see cref="VendorWindow"/>.</para>
/// <para><b>The purchase</b> (D14) refills the loop's kits only; the fighter build and <see cref="FighterKit"/> are unchanged. It buys the missing count of the 0062's row for weenie class 628 (cut to what the pyreals cover), at the price
/// <see cref="VendorStock.PriceToBuy(VendorStockEntry, uint)"/> predicts; the trade tracker compares the pyreals paid with it.</para>
/// <para><b>Appraisal.</b> With <c>--appraise-loot</c> on, each item the corpse loop picks up is identified once (<see cref="IActionApi.Identify"/>, Query lane, answered in 1 to 10 ms by ACE, capture C3) when it appears in the pack; the
/// answers are counted by <see cref="AppraisalTally"/> (<c>appraised=</c>). The corpse loop is not edited: the scenario diffs the held guids against the snapshot it took when the loop began.</para>
/// </remarks>
public sealed partial class HuntScenario
{
    /// <summary>The weenie class id of Shopkeeper Renald the Elder.</summary>
    public const uint RenaldWcid = 718;

    /// <summary>The weenie class id of Thelnoth Cort the Healer.</summary>
    public const uint ThelnothWcid = 715;

    /// <summary>The items one <c>Sell</c> carries at most (the remarks).</summary>
    public const int SellBatch = 20;

    /// <summary>The longest, in seconds, an <c>OpenVendor</c> is waited for (the tracker's own limit is 20 s from the send).</summary>
    public const double VendorOpenSeconds = 30;

    /// <summary>The longest, in seconds, a <c>Sell</c> or a <c>Buy</c> is waited for (the tracker's own limit is 10 s from the send, and 5 s more for the new objects).</summary>
    public const double VendorTradeSeconds = 30;

    /// <summary>The wait, in seconds, after a trade for the read model to show it (the payout stack and the sold items' moves follow the UseDone by a few milliseconds).</summary>
    public const double VendorSettleSeconds = 1;

    /// <summary>The most sale batches one visit sends (Holtburg Hunter's ~80 items in <see cref="SellBatch"/> items each).</summary>
    public const int SellBatchesMax = 4;

    /// <summary>
    /// The most one shop visit takes, in seconds, before it gives up: a place (<see cref="PlaceSeconds"/>, the <see cref="SettleSeconds"/> settle, <see cref="WaitSeconds"/> for the vendor to appear) twice (the -X side is tried when the +X spot is out of reach), the open
    /// (<see cref="VendorOpenSeconds"/>), then the trade batches (<see cref="VendorTradeSeconds"/> and the <see cref="VendorSettleSeconds"/> settle each).
    /// </summary>
    public const double VendorVisitBaseSeconds = 2 * (PlaceSeconds + SettleSeconds + WaitSeconds) + VendorOpenSeconds;

    /// <summary>
    /// What the two shop visits and the way home add to the hunt leg's whole-check limit (3.2-R15), in seconds: the sale of up to <see cref="SellBatchesMax"/> batches, the purchase of one batch, the place back to Holtburg after them. The wind-down's own
    /// allowance holds the restore and the first way home; this is on top when <c>--sell</c> or <c>--buy-kits</c> is on.
    /// </summary>
    public const double VendorTripsSeconds = VendorVisitBaseSeconds + SellBatchesMax * (VendorTradeSeconds + VendorSettleSeconds) + VendorVisitBaseSeconds + (VendorTradeSeconds + VendorSettleSeconds) + PlaceSeconds + SettleSeconds;

    /// <summary>How far from the player the vendor must be looked for, in metres (the player is placed at the vendor's position).</summary>
    public const float VendorFindMetres = 40f;

    /// <summary>
    /// How far from the vendor the hunter is placed, in metres, along the cell's X axis (3.2-R14): the capture and <c>VendorLiveTests</c> stood 1.5 m beside the NPC and it worked; the vendor's own spot would put the player's body inside the NPC's.
    /// The first try is on the +X side; when it lands outside the shop's cell or the vendor's use radius the -X side is tried once.
    /// </summary>
    public const float VendorStandOffMetres = 1.5f;

    /// <summary>The seconds the hunt gives an identified item's answer before it stops counting it (the tracker's own limit is 5 s).</summary>
    public const double AppraiseScanSeconds = 0.25;

    /// <summary>The seconds after a corpse loop's end its pick-ups are still looked for (the model shows a pick-up a tick or two after the confirmation).</summary>
    public const double AppraiseTailSeconds = 2;

    /// <summary>Renald the Elder's shop: cell 0xA9B40141 at (139.23, 18.33, 94.01), the heading of his quaternion (w 0.9871, z -0.1603): 18.4 degrees (cache/m4/3-2/vendors.tsv).</summary>
    public static TeleportDestination RenaldShop { get; } = Shop("Shopkeeper Renald the Elder (shop)", 0xA9B40141, new Vector3(139.23f, 18.33f, 94.01f), 0.9871, -0.1603);

    /// <summary>Thelnoth Cort the Healer's shop: cell 0xA9B40111 at (30.02, 159.68, 66.01), the heading of his quaternion (w 0.9975, z 0.0706): -8.1 degrees (cache/m4/3-2/vendors.tsv).</summary>
    public static TeleportDestination ThelnothShop { get; } = Shop("Thelnoth Cort the Healer (shop)", 0xA9B40111, new Vector3(30.02f, 159.68f, 66.01f), 0.9975, 0.0706);

    private enum VendorVisit
    {
        None,
        Renald,
        Thelnoth,
    }

    private readonly List<uint> _sellList = [];
    private readonly List<uint> _sellBatch = [];
    private readonly int[] _sellSkipped = new int[Enum.GetValues<SellDecision>().Length];
    private readonly AppraisalTally _appraisals = new();
    private readonly List<IdentifyHandle> _identifies = [];
    private readonly HashSet<uint> _heldKnown = [];
    private readonly List<uint> _heldScratch = [];
    private bool _vendorTripBegun;
    private int _stuckEndsAtOpen;
    private bool _renaldOpenRetried;
    private DoorWatch? _doorWatch;
    private bool _vendorPlace;
    private int _vendorSide;
    private bool _vendorWindow;
    private bool _wantSell;
    private bool _wantBuy;
    private bool _sellMaskLogged;
    private VendorVisit _visit;
    private uint _vendorGuid;
    private uint _saleVendor;
    private VendorTradeHandle? _trade;
    private int _sellSent;
    private int _sellErrors;
    private string? _sellFirstError;
    private int _coinsBefore;
    private int _kitsBefore;
    private int _buyAsked;
    private int _buyCost;
    private string _sellOutcome = "not-run";
    private string _buyOutcome = "not-run";
    private CorpseLooter? _appraiseLooter;
    private double _appraiseNextScan;
    private double _appraiseTailUntil = double.NegativeInfinity;

    /// <summary>True from a vendor's open being sent to the visit's end (the hunt leaves the shop): the window <see cref="VendorFrameSampler"/> measures (<c>vendor_frame_max_ms</c>, M4 3.2 s4a).</summary>
    public bool VendorWindow => _vendorWindow;

    /// <summary>Renald's object id from his visit's look-up until it ends, else 0: the runner takes the <c>vendor</c> review frame 1 s after the 0062 for this vendor.</summary>
    public uint SaleVendor => _saleVendor;

    /// <summary>The items the loop's end sold (<c>sold</c>).</summary>
    public int Sold => _tally.Sold;

    /// <summary>The Handy Healing Kits the loop's end bought (<c>bought</c>).</summary>
    public int Bought => _tally.Bought;

    /// <summary>The pyreals the sale paid (<c>pyreals</c>, the coin delta of the sale).</summary>
    public int Pyreals => _tally.Pyreals;

    /// <summary>The looted items identified with the tables (<c>appraised</c>).</summary>
    public int Appraised => _appraisals.Appraised;

    /// <summary>How the sale ended: <c>off</c>, <c>not-run</c>, <c>none</c> (nothing the shop takes was held), <c>ok</c>, <c>partial</c>, <c>skipped:&lt;why&gt;</c> or <c>failed:&lt;reason&gt;</c>.</summary>
    public string SellOutcome => !Has(HuntStages.Vendor) ? "n/a" : _options.Sell ? _sellOutcome : "off";

    /// <summary>How the purchase ended: <c>off</c>, <c>not-run</c>, <c>enough</c> (the kits were held), <c>ok</c>, <c>partial</c>, <c>skipped:&lt;why&gt;</c> or <c>failed:&lt;reason&gt;</c>.</summary>
    public string BuyOutcome => !Has(HuntStages.Vendor) ? "n/a" : _options.BuyKits > 0 ? _buyOutcome : "off";

    /// <summary>The vendor fields (<c>sold= bought= pyreals= appraised= sell= buy= buy_pyreals=</c>): the detail line's, and the vendor station's beside the verdict.</summary>
    public string VendorFields() =>
        string.Create(CultureInfo.InvariantCulture, $"sold={_tally.Sold} bought={_tally.Bought} pyreals={_tally.Pyreals} appraised={_appraisals.Appraised} sell={Token(SellOutcome)} buy={Token(BuyOutcome)} buy_pyreals={_buyCost}");

    /// <summary>
    /// <c>yes</c> only when a door the vendor trip's walk itself Used (the runner's Use through <see cref="SessionDoorFeed"/>, by guid) turned Ethereal during the trip (M5 item 5, d2 and F2's 5-R2: the vendor station's
    /// <c>door_opened=</c>, <see cref="DoorWatch"/>, as the vendor-facts station's), else <c>no</c>: another lane's door, ACE opening a door, a door found open and the vendor's own Use never count
    /// (also <c>no</c> for a trip by place loc, which walks through no door). Report-only.
    /// </summary>
    public string DoorOpenedField => _doorWatch?.Field ?? "no";

    /// <summary>
    /// Starts the trip's <see cref="DoorWatch"/>: the trip's walk goes through the journey seam's runner, which opens a closed shop door (purpose=door); the watch reads whether it did. The previous trip's watch is
    /// disposed first, so no subscription piles up (M5 5 F2, 5-R8).
    /// </summary>
    /// <param name="session">The session.</param>
    internal void BeginDoorWatch(IGameSessionView session)
    {
        if (_doorWatch is { } previous)
        {
            previous.Dispose();
            _subscriptions.Remove(previous);
        }

        _doorWatch = new DoorWatch(session);
        _subscriptions.Add(_doorWatch);
    }

    /// <summary>A value on the detail line has no spaces.</summary>
    private static string Token(string text)
    {
        var line = text.Replace(' ', '_').Replace('=', ':');
        return line.Length > 100 ? line[..100] : line;
    }

    private static TeleportDestination Shop(string name, uint cell, Vector3 at, double w, double z)
    {
        // retail's heading is -2 atan2(z, w) (PlaceRequests.LocationOf is its inverse), in degrees
        var heading = -2.0 * Math.Atan2(z, w) * (180.0 / Math.PI);
        return new TeleportDestination(name, [], new WorldLocation(name, new CellPosition(cell, at), HasHeight: true, HeadingDegrees: heading), "Holtburg vendor (cache/m4/3-2/vendors.tsv)");
    }

    // ------------------------------------------------------------------------------------------------ the trip

    /// <summary>
    /// Called by <c>Home</c> the first time the hunt is back in Holtburg: decides the visits (the sale when <c>--sell</c> is on and the pack holds something the shop's mask takes, the purchase when <c>--buy-kits</c> is above the kits held)
    /// and starts the first. Null when there is nothing to do (the caller goes on to the log off).
    /// </summary>
    private ScenarioStatus? VendorTripBegin(IGameSessionView session)
    {
        if (_vendorTripBegun)
        {
            return null;
        }

        _vendorTripBegun = true;
        if (!Has(HuntStages.Vendor))
        {
            return null; // M4 P5b: the shops are their own station in a journey (the stage is logged once as skipped; sell=n/a buy=n/a)
        }

        BeginDoorWatch(session);

        _wantSell = _options.Sell;
        _wantBuy = _options.BuyKits > 0;
        if (!_wantSell && !_wantBuy)
        {
            return null;
        }

        if (Failure is not null || !_returned)
        {
            _sellOutcome = _wantSell ? "skipped:" + (Failure is not null ? "the hunt failed" : "not back in Holtburg") : _sellOutcome;
            _buyOutcome = _wantBuy ? "skipped:" + (Failure is not null ? "the hunt failed" : "not back in Holtburg") : _buyOutcome;
            _log.Info(LogCategory, "vendors: skipped, " + (Failure is not null ? "the hunt failed (nothing is sold or bought after a failure)" : "the hunter is not back in Holtburg"));
            return null;
        }

        if (_wantSell && SellRules.Default.Select(session.State, null, _sellList, _scratch) == 0)
        {
            _sellOutcome = "none";
            _wantSell = false;
            _log.Info(LogCategory, "sell: nothing held that the shop's mask takes (" + SellRules.Default.Describe(null) + "); no trip to Renald");
        }

        if (_wantBuy)
        {
            var kits = KitsHeld(session);
            if (kits >= _options.BuyKits)
            {
                _buyOutcome = "enough";
                _wantBuy = false;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"buy: {kits} Handy Healing Kit(s) held, --buy-kits {_options.BuyKits}; no trip to Thelnoth"));
            }
        }

        if (!_wantSell && !_wantBuy)
        {
            return null;
        }

        _visit = _wantSell ? VendorVisit.Renald : VendorVisit.Thelnoth;
        return VendorGo(session);
    }

    private ScenarioStatus VendorGo(IGameSessionView session, int side = 1)
    {
        if (OnFoot)
        {
            return VendorGoOnFoot(session);
        }

        _vendorSide = side;
        var shop = Beside(_visit == VendorVisit.Renald ? RenaldShop : ThelnothShop, side * VendorStandOffMetres);
        var at = shop.Location.Position.Local;
        _vendorPlace = true;
        _vendorGuid = 0;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vendors: going to {shop.Name}: place loc cell 0x{shop.Location.Position.CellId:X8} at ({at.X:0.##}, {at.Y:0.##}, {at.Z:0.##}), {Math.Abs(VendorStandOffMetres):0.0} m {(side > 0 ? "+X" : "-X")} of the vendor (TODO(m4-vendor-approach): the walk through the door is not built)"));
        BeginPlace(session, shop, "vendor-arrive", home: false, TravelPurpose.Vendor);
        return ScenarioStatus.Running;
    }

    /// <summary>Renald the Elder's front room (inside the shop's door, in front of his counter): the shop goal on foot, never his room 0xA9B40141 behind the counter (TODO(room-steps): its floor is a metre above).</summary>
    public const uint RenaldFrontRoomCell = 0xA9B4013F;

    /// <summary>
    /// The radius the walk ends in: Renald 6.5 m (from his front room: no standable front-room floor lies within 5 m of him,
    /// 5.72 m measured by P5b F1, and the counter blocks every sight line, so the goal asks for no sight), Thelnoth 3 m (his
    /// cellar 0xA9B40111 is his shop's own cell). Not retail: Lifestone's own; ACE's approach walks the rest of the use range.
    /// </summary>
    private double VendorUseMetres => _visit == VendorVisit.Renald ? RenaldUseMetres : ThelnothUseMetres;

    /// <summary>
    /// The centre-to-centre radius Renald the Elder counts as in reach after a walk (the hunt's arrival check and the vendor-facts station's, M4 P5b fix FC-b2): 6.5 m from his front room. Since M5 7 P7p no walk ends
    /// at this radius any more: every walk to him is a point goal to <see cref="ShopLeg.RenaldStand"/> (an object goal within 6.5 m stood about 5.05 m out, cylinder to cylinder, outside ACE's 5 m use radius).
    /// </summary>
    public const double RenaldUseMetres = 6.5;

    /// <summary>The radius every walk to Thelnoth Cort ends in: 3 m (his cellar is his shop's own cell).</summary>
    public const double ThelnothUseMetres = 3.0;

    /// <summary>
    /// The shop door on foot (M4 P5b, lane J2a; closes the live walk of <c>m4-vendor-approach</c>). Thelnoth: the travel seam's object goal is the vendor within its use radius, the point his own x and y in his cellar; his
    /// guid when he is in view, else a point goal. Renald (M5 7 P7p): a point goal to <see cref="ShopLeg.RenaldStand"/> in his front room <see cref="RenaldFrontRoomCell"/>, the den profile's stop point, inside his use
    /// radius: the old object goal within <see cref="RenaldUseMetres"/> ended about 5.05 m out, cylinder to cylinder, and ACE's MoveTo from there ran into the barrels by his counter (the item-end gate).
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="why">Why the walk is made, for the log (the first walk, or the recovery after a refused open).</param>
    private ScenarioStatus VendorGoOnFoot(IGameSessionView session, string why = "the first walk")
    {
        _vendorSide = 0;
        _vendorPlace = true;
        _vendorGuid = 0;
        var renald = _visit == VendorVisit.Renald;
        if (renald)
        {
            var (standCell, standPoint) = ShopLeg.RenaldStand;
            var stand = RenaldShop with { Name = "Renald the Elder's stand (on foot)", Location = RenaldShop.Location with { Position = new CellPosition(standCell, standPoint) } };
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vendors: on foot to Renald the Elder ({why}): a point goal to his stand 0x{standCell:X8} ({standPoint.X:0.##}, {standPoint.Y:0.##}, {standPoint.Z:0.##}), the den profile's stop_point, inside his 5 m use radius (no MoveTo of ACE's is needed)"));
            BeginPlace(session, stand, "vendor-arrive", home: false, TravelPurpose.Vendor);
            return ScenarioStatus.Running;
        }

        var shop = ThelnothShop;
        var cell = shop.Location.Position.CellId;
        var goal = shop with { Name = shop.Name + " (on foot)", Location = shop.Location with { Position = shop.Location.Position with { CellId = cell } } };
        var guid = FindVendor(session, ThelnothWcid);
        var at = goal.Location.Position.Local;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vendors: on foot to {VisitName}: cell 0x{cell:X8}, the vendor's point ({at.X:0.##}, {at.Y:0.##}, {at.Z:0.##}), {(guid == 0 ? "a point goal (the vendor is not in view yet)" : $"the object goal 0x{guid:X8} within {VendorUseMetres:0} m")}"));
        BeginPlace(session, goal, "vendor-arrive", home: false, TravelPurpose.Vendor, guid, VendorUseMetres);
        return ScenarioStatus.Running;
    }

    /// <summary>The shop's destination moved <paramref name="dx"/> metres along the cell's X axis (the vendor's position is the shop's).</summary>
    private static TeleportDestination Beside(TeleportDestination shop, float dx)
    {
        var position = shop.Location.Position;
        return shop with { Name = shop.Name + string.Create(CultureInfo.InvariantCulture, $" {dx:+0.0;-0.0} m"), Location = shop.Location with { Position = position with { Local = position.Local + new Vector3(dx, 0f, 0f) } } };
    }

    private ScenarioStatus VendorStep(IGameSessionView session)
    {
        switch (Stage)
        {
            case "vendor-arrive":
                return VendorArrive(session);
            case "vendor-open":
                return VendorOpened(session);
            case "vendor-sell":
                return VendorSold(session);
            case "vendor-sell-settle":
                return _clock >= _settleUntil ? VendorSellSettled(session) : ScenarioStatus.Running;
            case "vendor-buy":
                return VendorBought(session);
            case "vendor-buy-settle":
                return _clock >= _settleUntil ? VendorBuySettled(session) : ScenarioStatus.Running;
            default:
                return VendorAbort(session, $"TODO(state): unknown vendor stage '{Stage}'");
        }
    }

    private bool _lootMaskLogged;

    /// <summary>Says once which type mask the loot rule works with and where it came from (3.2-R16): Renald's 0062 read this session, or the shard's value.</summary>
    private void LogLootMaskSource(IGameSessionView session)
    {
        if (_lootMaskLogged || _lootRules.MaskNote.Length == 0)
        {
            return;
        }

        _lootMaskLogged = true;
        var context = LootContext.Of(session.State);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: type mask 0x{_lootRules.EffectiveMask(in context):X8} from {(context.VendorMask is null ? "the shard's value (no 0062 of Renald the Elder read this session)" : "Renald the Elder's 0062 read this session")}"));
    }

    private string VisitName => _visit == VendorVisit.Renald ? "Renald the Elder" : "Thelnoth Cort";

    private ScenarioStatus VendorArrive(IGameSessionView session)
    {
        if (_clock < _settleUntil)
        {
            return ScenarioStatus.Running;
        }

        var wcid = _visit == VendorVisit.Renald ? RenaldWcid : ThelnothWcid;
        var guid = FindVendor(session, wcid);
        if (guid == 0)
        {
            return _clock - _stageStart > WaitSeconds
                ? VendorAbort(session, string.Create(CultureInfo.InvariantCulture, $"{VisitName} (weenie class {wcid}) is not in view within {WaitSeconds:0} s of the arrival"))
                : ScenarioStatus.Running;
        }

        if (_lastMeans != TravelMeans.Place && !WithinUse(session, guid))
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vendors: the walk ended outside {VisitName}'s use radius ({VendorUseMetres:0} m); the open is sent and ACE's own approach walks the rest (TODO(m4-vendor-approach))"));
        }
        else if (_vendorSide > 0 && !WithinReach(session, guid))
        {
            _log.Info(LogCategory, $"vendors: the +X spot is outside {VisitName}'s cell or use radius; trying the -X side once");
            return VendorGo(session, -1);
        }

        _vendorGuid = guid;
        _saleVendor = _visit == VendorVisit.Renald ? guid : 0;
        _vendorWindow = true;
        _stuckEndsAtOpen = session.Actions.Move.ServerMoveToStuckEnds;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vendors: opening {VisitName} 0x{guid:X8} (server MoveTos ended stuck so far: {_stuckEndsAtOpen})"));
        _pending = session.Actions.OpenVendor(guid, ActionSource.Host);
        return Enter("vendor-open");
    }

    /// <summary>True when the player is in the shop's cell and within the vendor's use radius (Renald 5 m, Thelnoth 3 m; the retail rule is the server's, <c>TODO(m4-vendor-approach)</c>).</summary>
    private bool WithinReach(IGameSessionView session, uint vendor)
    {
        var shop = _visit == VendorVisit.Renald ? RenaldShop : ThelnothShop;
        if (session.State.Self.Position is not { } here || here.CellId != shop.Location.Position.CellId || !session.State.Objects.TryGet(vendor, out var npc) || npc.Position is not { } there)
        {
            return false;
        }

        return there.ToGlobal().DistanceTo(here.ToGlobal()) <= VendorUseMetres;
    }

    /// <summary>True when the player is within the vendor's use radius, whatever the cell (the walk's arrival: the front room is not the vendor's cell).</summary>
    private bool WithinUse(IGameSessionView session, uint vendor) =>
        session.State.Self.Position is { } here && session.State.Objects.TryGet(vendor, out var npc) && npc.Position is { } there && there.ToGlobal().DistanceTo(here.ToGlobal()) <= VendorUseMetres + 0.5;

    private uint FindVendor(IGameSessionView session, uint wcid)
    {
        uint found = 0;
        var best = (double)VendorFindMetres;
        var here = session.State.Self.Position;
        foreach (var o in session.State.Objects.All)
        {
            if (o.IsSelf || o.Weenie.WeenieClassId != wcid)
            {
                continue;
            }

            var d = here is { } h && o.Position is { } p ? p.ToGlobal().DistanceTo(h.ToGlobal()) : 0.0;
            if (found == 0 || d < best)
            {
                found = o.Guid;
                best = d;
            }
        }

        return found;
    }

    private ScenarioStatus VendorOpened(IGameSessionView session)
    {
        if (!Settled(out var opened))
        {
            if (_clock - _stageStart > VendorOpenSeconds)
            {
                _pending = null;
                return VendorAbort(session, string.Create(CultureInfo.InvariantCulture, $"the open of {VisitName} got no answer within {VendorOpenSeconds:0} s"));
            }

            return ScenarioStatus.Running;
        }

        if (opened.Kind != ActionOutcomeKind.Confirmed)
        {
            var stuckNow = session.Actions.Move.ServerMoveToStuckEnds;
            if (_visit == VendorVisit.Renald && OnFoot && !_renaldOpenRetried && ShopLeg.RefusedAfterStuckMoveTo(opened, _stuckEndsAtOpen, stuckNow))
            {
                // M5 7 P7p (D141, check, act, check): ACE's walk-to-use ended stuck (TODO(m5-moveto-stuck)) and the open was refused; the named recovery, once: his stand, then one more open.
                _renaldOpenRetried = true;
                _vendorWindow = false;
                _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vendors: opening {VisitName}: {Describe(opened)} after ACE's MoveTo ended {session.Actions.Move.LastServerMoveToEnd ?? "unknown"} (stuck ends {_stuckEndsAtOpen} -> {stuckNow}): recovery: walking to his stand and opening once more"));
                return VendorGoOnFoot(session, "the recovery after a refused open");
            }

            return VendorAbort(session, $"opening {VisitName}: " + Describe(opened));
        }

        if (session.Vendor is not { } stock || stock.Vendor != _vendorGuid)
        {
            return VendorAbort(session, $"{VisitName} opened but the read model holds no stock for 0x{_vendorGuid:X8}");
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vendors: {VisitName} is open: {stock.Entries.Length} row(s), mask 0x{stock.Mask:X8}, he pays {stock.BuyPrice:0.##} and charges {stock.SellPrice:0.##} of the Value"));
        return _visit == VendorVisit.Renald ? BeginSale(session, stock) : BeginBuy(session, stock);
    }

    // ------------------------------------------------------------------------------------------------ the sale

    private ScenarioStatus BeginSale(IGameSessionView session, VendorStock stock)
    {
        var rules = SellRules.Default;
        if (!_sellMaskLogged)
        {
            _sellMaskLogged = true;
            _log.Info(LogCategory, "sell: rule " + rules.Describe(stock) + (SellRules.FromWire(stock) ? string.Empty : "; the shard's mask is used because no 0062 was read"));
        }

        Array.Clear(_sellSkipped);
        var count = rules.Select(session.State, stock, _sellList, _scratch, _sellSkipped);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"sell: {count} item(s) to sell; left: {SkipSummary()}"));
        if (count == 0)
        {
            _sellOutcome = "none";
            return VendorEnd(session);
        }

        _coinsBefore = session.State.Self.Qualities.CoinValue;
        _sellSent = 0;
        _sellErrors = 0;
        _sellFirstError = null;
        var expected = 0L;
        foreach (var guid in _sellList)
        {
            if (session.State.Objects.TryGet(guid, out var item))
            {
                expected += stock.PayoutFor(item);
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"sell: '{item.Name}' 0x{guid:X8} (wcid {item.Weenie.WeenieClassId}, type 0x{item.ItemType:X}, value {item.Weenie.Value ?? 0}, pays {stock.PayoutFor(item)})"));
            }
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"sell: pyreals before {_coinsBefore}, the vendor should pay {expected} for {count} item(s) in {(count + SellBatch - 1) / SellBatch} sale(s)"));
        return SendSellBatch(session);
    }

    private string SkipSummary()
    {
        var parts = new List<string>();
        for (var i = 0; i < _sellSkipped.Length; i++)
        {
            if (_sellSkipped[i] > 0 && (SellDecision)i != SellDecision.Sell)
            {
                parts.Add(SellRules.ReasonName((SellDecision)i) + "=" + _sellSkipped[i].ToString(CultureInfo.InvariantCulture));
            }
        }

        return parts.Count == 0 ? "nothing" : string.Join(' ', parts);
    }

    private ScenarioStatus SendSellBatch(IGameSessionView session)
    {
        _sellBatch.Clear();
        for (var i = _sellSent; i < _sellList.Count && _sellBatch.Count < SellBatch; i++)
        {
            _sellBatch.Add(_sellList[i]);
        }

        _sellSent += _sellBatch.Count;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"sell: sending {_sellBatch.Count} item(s) to {VisitName} ({_sellSent} of {_sellList.Count})"));
        _trade = session.Actions.Sell(_vendorGuid, [.. _sellBatch], ActionSource.Host);
        _pending = _trade;
        return Enter("vendor-sell");
    }

    private ScenarioStatus VendorSold(IGameSessionView session)
    {
        if (!Settled(out var trade))
        {
            if (_clock - _stageStart > VendorTradeSeconds)
            {
                _pending = null;
                return VendorAbort(session, string.Create(CultureInfo.InvariantCulture, $"the sale got no answer within {VendorTradeSeconds:0} s"));
            }

            return ScenarioStatus.Running;
        }

        var result = _trade?.Result;
        _log.Info(LogCategory, $"sell: the sale ended {Describe(trade)}" + (result is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (predicted {result.Predicted}, paid {result.Actual}{(result.Warn is null ? string.Empty : ", WARN " + result.Warn)})")));
        if (trade.Kind != ActionOutcomeKind.Confirmed)
        {
            _sellErrors++;
            _sellFirstError ??= Describe(trade);
        }

        if (_sellSent < _sellList.Count)
        {
            return SendSellBatch(session);
        }

        _settleUntil = _clock + VendorSettleSeconds;
        return Enter("vendor-sell-settle");
    }

    private ScenarioStatus VendorSellSettled(IGameSessionView session)
    {
        var inventory = session.State.Self.Inventory;
        var sold = 0;
        foreach (var guid in _sellList)
        {
            if (!inventory.Find(guid).Held)
            {
                sold++;
            }
        }

        var pyreals = session.State.Self.Qualities.CoinValue - _coinsBefore;
        _tally.AddSale(sold, pyreals);
        if (sold == 0)
        {
            _sellOutcome = "failed:" + (_sellFirstError ?? "no item left the pack");
            if (!Has(HuntStages.Hunt))
            {
                Failure ??= $"vendors: {VisitName}: nothing was sold ({_sellOutcome})";
            }

            _log.Warn(LogCategory, $"sell: nothing was sold ({_sellOutcome}); the hunt goes on");
        }
        else
        {
            _sellOutcome = sold == _sellList.Count && _sellErrors == 0 ? "ok" : "partial";
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"sell: done sold={sold} of {_sellList.Count} pyreals={pyreals} ({_sellOutcome}{(_sellErrors > 0 ? ", " + _sellErrors + " sale(s) refused: " + _sellFirstError : string.Empty)})"));
        }

        return VendorEnd(session);
    }

    // ------------------------------------------------------------------------------------------------ the buy

    private int KitsHeld(IGameSessionView session)
    {
        var state = session.State;
        GearInventory.Collect(state, _scratch);
        var kits = 0;
        for (var i = 0; i < _scratch.Count; i++)
        {
            if (state.Objects.TryGet(_scratch[i], out var o) && o.Weenie.WeenieClassId == FighterKit.HealingKitWcid && (o.Weenie.Structure ?? 1) > 0)
            {
                kits++;
            }
        }

        return kits;
    }

    private ScenarioStatus BeginBuy(IGameSessionView session, VendorStock stock)
    {
        _kitsBefore = KitsHeld(session);
        var missing = _options.BuyKits - _kitsBefore;
        if (missing <= 0)
        {
            _buyOutcome = "enough";
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"buy: {_kitsBefore} Handy Healing Kit(s) held, --buy-kits {_options.BuyKits}; nothing to buy"));
            return VendorEnd(session);
        }

        VendorStockEntry? row = null;
        foreach (var entry in stock.Entries)
        {
            if (entry.WeenieClassId == FighterKit.HealingKitWcid)
            {
                row = entry;
                break;
            }
        }

        if (row is null)
        {
            _buyOutcome = "failed:the stock has no Handy Healing Kit row";
            _log.Warn(LogCategory, $"buy: {VisitName}'s stock has no row for weenie class {FighterKit.HealingKitWcid}; the hunt goes on");
            return VendorEnd(session);
        }

        _coinsBefore = session.State.Self.Qualities.CoinValue;
        var amount = (uint)missing;
        while (amount > 0 && (!row.IsBuyable(amount) || stock.PriceToBuy(row, amount) > _coinsBefore))
        {
            amount--;
        }

        if (amount == 0)
        {
            _buyOutcome = "failed:" + (row.IsBuyable(1) ? "the pyreals do not cover one kit" : "the row cannot be bought");
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"buy: {_buyOutcome} ({_coinsBefore} pyreals, one kit costs {stock.PriceToBuy(row, 1)}); the hunt goes on"));
            return VendorEnd(session);
        }

        _buyAsked = (int)amount;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"buy: {_kitsBefore} kit(s) held, --buy-kits {_options.BuyKits}: buying {amount} '{row.Name}' (row 0x{row.Guid:X8}, Value {row.Value}) at the predicted {stock.PriceToBuy(row, amount)} pyreals of {_coinsBefore}"));
        _trade = session.Actions.Buy(_vendorGuid, [(row.Guid, amount)], ActionSource.Host);
        _pending = _trade;
        return Enter("vendor-buy");
    }

    private ScenarioStatus VendorBought(IGameSessionView session)
    {
        if (!Settled(out var trade))
        {
            if (_clock - _stageStart > VendorTradeSeconds)
            {
                _pending = null;
                return VendorAbort(session, string.Create(CultureInfo.InvariantCulture, $"the purchase got no answer within {VendorTradeSeconds:0} s"));
            }

            return ScenarioStatus.Running;
        }

        var result = _trade?.Result;
        _log.Info(LogCategory, $"buy: the purchase ended {Describe(trade)}" + (result is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (predicted {result.Predicted}, paid {result.Actual}, objects {result.ObjectsArrived} of {result.ObjectsExpected}{(result.Warn is null ? string.Empty : ", WARN " + result.Warn)})")));
        if (trade.Kind != ActionOutcomeKind.Confirmed)
        {
            _buyOutcome = "failed:" + Describe(trade);
            _log.Warn(LogCategory, $"buy: {_buyOutcome}; the hunt goes on");
            return VendorEnd(session);
        }

        _settleUntil = _clock + VendorSettleSeconds;
        return Enter("vendor-buy-settle");
    }

    private ScenarioStatus VendorBuySettled(IGameSessionView session)
    {
        var bought = Math.Max(0, KitsHeld(session) - _kitsBefore);
        _buyCost = Math.Max(0, _coinsBefore - session.State.Self.Qualities.CoinValue);
        _tally.AddBought(bought);
        _buyOutcome = bought == 0 ? "failed:no kit arrived" : bought == _buyAsked ? "ok" : "partial";
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"buy: done bought={bought} of {_buyAsked} cost={_buyCost} pyreals ({_buyOutcome}); {_kitsBefore + bought} kit(s) held"));
        return VendorEnd(session);
    }

    // ------------------------------------------------------------------------------------------------ the end of a visit

    /// <summary>A visit failed: the reason is the visit's outcome and the hunt goes on (a failed sale never fails the hunt).</summary>
    private ScenarioStatus VendorAbort(IGameSessionView session, string why)
    {
        _pending = null;
        _trade = null;
        if (!Has(HuntStages.Hunt))
        {
            // The vendor station (M4 P5b F2): the visit is the whole run, so a visit that failed (the walk to the shop included) fails it, with the visit's reason.
            Failure ??= $"vendors: {VisitName}: {why}";
            _log.Warn(LogCategory, $"vendors: {VisitName}: {why}; the vendor-only run fails with it");
        }
        else
        {
            _log.Warn(LogCategory, $"vendors: {VisitName}: {why}; the hunt goes on (a failed shop visit never fails the hunt)");
        }

        if (_visit == VendorVisit.Renald)
        {
            _sellOutcome = "failed:" + why;
        }
        else
        {
            _buyOutcome = "failed:" + why;
        }

        return VendorEnd(session);
    }

    /// <summary>The visit is over (leaving the shop closes the vendor and ends the window): on to the purchase when it is wanted, else back to Holtburg by <c>place poi</c>.</summary>
    private ScenarioStatus VendorEnd(IGameSessionView session)
    {
        _vendorWindow = false;
        _saleVendor = 0;
        _vendorGuid = 0;
        if (_visit == VendorVisit.Renald && _wantBuy)
        {
            _visit = VendorVisit.Thelnoth;
            return VendorGo(session);
        }

        _visit = VendorVisit.None;
        _vendorPlace = false;
        if (!Has(HuntStages.Home))
        {
            _log.Info(LogCategory, "vendors: done; the home stage is skipped (the journey's next station travels)");
            return Home(session);
        }

        _log.Info(LogCategory, "vendors: done; back to Holtburg");
        BeginPlace(session, _holtburg, "wind-home-done", home: true, TravelPurpose.Home);
        return ScenarioStatus.Running;
    }

    // ------------------------------------------------------------------------------------------------ appraising the loot

    /// <summary>
    /// <c>--appraise-loot</c>: counts the answers that came in and, while the corpse loop runs (and a moment after), identifies each item that appeared in the pack since the loop began, once. Cheap while idle: a scan every
    /// <see cref="AppraiseScanSeconds"/> only while a loop is running or just ended.
    /// </summary>
    private void StepAppraise(IGameSessionView session)
    {
        for (var i = _identifies.Count - 1; i >= 0; i--)
        {
            if (_identifies[i].Outcome is not null)
            {
                RecordAppraisal(session, _identifies[i]);
                _identifies.RemoveAt(i);
            }
        }

        if (!_options.AppraiseLoot)
        {
            return;
        }

        var looter = _looter ?? _drainLooter;
        if (looter is null)
        {
            if (_clock > _appraiseTailUntil)
            {
                return;
            }
        }
        else
        {
            _appraiseTailUntil = _clock + AppraiseTailSeconds;
            if (!ReferenceEquals(looter, _appraiseLooter))
            {
                // A new loop: what is held now is not loot (the first step after the loop was made comes before its first pick-up).
                _appraiseLooter = looter;
                GearInventory.Collect(session.State, _heldScratch);
                _heldKnown.Clear();
                foreach (var held in _heldScratch)
                {
                    _heldKnown.Add(held);
                }

                _appraiseNextScan = _clock + AppraiseScanSeconds;
                return;
            }
        }

        if (_clock < _appraiseNextScan)
        {
            return;
        }

        _appraiseNextScan = _clock + AppraiseScanSeconds;
        GearInventory.Collect(session.State, _heldScratch);
        foreach (var guid in _heldScratch)
        {
            if (!_heldKnown.Add(guid))
            {
                continue;
            }

            var name = session.State.Objects.TryGet(guid, out var item) ? item.Name : "?";
            _log.Info(LogCategory, $"appraise: identifying the looted '{name}' 0x{guid:X8}");
            _identifies.Add(session.Actions.Identify(guid, ActionSource.Host));
        }
    }

    private void RecordAppraisal(IGameSessionView session, IdentifyHandle handle)
    {
        _appraisals.Record(handle);
        _tally.SetAppraised(_appraisals.Appraised);
        var guid = handle.Summary;
        if (handle.Outcome is { Kind: ActionOutcomeKind.Confirmed } && handle.Result is { } result)
        {
            _log.Info(LogCategory, $"appraise: {handle.Summary} answered {result.Kind}{(result.Cooldown ? " (the 5 s cooldown)" : string.Empty)}; appraised={_appraisals.Appraised}");
        }
        else
        {
            _log.Warn(LogCategory, $"appraise: {guid} ended {(handle.Outcome is { } o ? Describe(o) : "unfinished")}; not counted");
        }
    }

    /// <summary>Counts the answers that are in when the scenario ends (an unanswered Identify is not counted).</summary>
    private void FinishAppraise()
    {
        for (var i = _identifies.Count - 1; i >= 0; i--)
        {
            if (_identifies[i].Outcome is not null)
            {
                _appraisals.Record(_identifies[i]);
            }
        }

        _identifies.Clear();
        _tally.SetAppraised(_appraisals.Appraised);
    }
}
