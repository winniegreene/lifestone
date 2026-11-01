using System.Globalization;
using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.World;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// What the shop stations share (M4 P5b J3a; ports the walk and the reads of <c>HunterShopLeg</c> in <c>VendorLiveTests.cs</c>): the walk to an NPC through <see cref="JourneyContext.Travel"/> as a <see cref="TravelPurpose.Vendor"/> request with
/// the vendor as an object goal when the object table holds him, the check that he is in reach (the cell and the use radius), and the pack reads. The station never places: the old second try, on the other side of the NPC, is a second travel request.
/// </summary>
public abstract class ShopLeg : StationLeg
{
    /// <summary>Thelnoth Cort the Healer's cellar.</summary>
    public const uint ThelnothCell = 0xA9B40111;

    /// <summary>Renald the Elder's back room, where he stands behind the counter: not walkable on foot (<c>TODO(room-steps)</c>), the journey never asks for it as a cell to stand in.</summary>
    public const uint RenaldCell = 0xA9B40141;

    /// <summary>Renald's front room, the cell the journey walks to (the shop goal; his back room is behind the counter).</summary>
    public const uint RenaldFrontRoomCell = 0xA9B4013F;

    /// <summary>
    /// What the NPC's name must contain to be Thelnoth ("Thelnoth Cort the Healer"). Never a first name alone: Renald's shop holds two vendors, "Shopkeeper Renald the Elder" (wcid 718, mask 0x480467A7) and "Scribe Renald the Younger" (wcid 717), and the
    /// nearer of two names containing "Renald" was the Younger (gate round 2: his 0062, 16 rows, took neither Clothing nor Armor).
    /// </summary>
    public const string ThelnothName = "Thelnoth Cort";

    /// <summary>What the NPC's name must contain to be Renald the Elder, the shopkeeper (not "Scribe Renald the Younger", who stands in the same shop): see <see cref="ThelnothName"/>.</summary>
    public const string RenaldName = "Renald the Elder";

    /// <summary>The metres beside the NPC a place stands (VendorStandOffMetres in the hunt, the loopback facts' 1.5 m).</summary>
    public const float StandOff = 1.5f;

    /// <summary>Thelnoth's spot (cache/m4/3-2/vendors.tsv).</summary>
    public static readonly Vector3 ThelnothAt = new(30.02f, 159.68f, 66.01f);

    /// <summary>Renald's spot in his back room, behind the counter (the walks end at <see cref="RenaldStand"/> in his front room, never here).</summary>
    public static readonly Vector3 RenaldAt = new(139.23f, 18.33f, 94.01f);

    /// <summary>
    /// M5 7 P7p: the one stand point every walk to Renald the Elder ends at, as a point goal (the hunt's vendor stage, the vendor-facts station and the resupply station alike): the stop cell and the <c>stop_point</c>
    /// the den profile names for him (<see cref="GroundVendor.StopPoint"/>), read from the profile, never copied. It lies inside ACE's use radius of him (5 m, cylinder to cylinder: <c>IsWithinUseRadiusOf</c>,
    /// WorldObject_Use.cs:47-61 @95ef426d), so the open needs no MoveTo of ACE's: the item-end gate's walks that ended at 6.5 m from him, centre to centre, stood about 5.05 m out, and ACE's straight MoveTo from there ran into
    /// the barrels by his counter and timed out (<c>VendorStopConfirmDatTests</c> proves both on the dats).
    /// </summary>
    public static (uint Cell, Vector3 Point) RenaldStand => RenaldStandOf(GroundCatalog.Get(FightOptions.DefaultGround));

    /// <summary>The stop cell and stop point <paramref name="ground"/>'s profile names for Renald the Elder (weenie class <see cref="HuntScenario.RenaldWcid"/>); a profile without them throws, named (a loud stub, never a guess).</summary>
    /// <param name="ground">The hunting ground whose profile lists the Holtburg vendors.</param>
    public static (uint Cell, Vector3 Point) RenaldStandOf(HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(ground);
        foreach (var vendor in ground.Vendors)
        {
            if (vendor.Wcid == HuntScenario.RenaldWcid)
            {
                return (vendor.StopCell, vendor.StopPoint ?? throw new InvalidOperationException($"TODO(renald-stand): the profile '{ground.Key}' names Renald the Elder with no stop_point; every walk to him ends at it"));
            }
        }

        throw new InvalidOperationException($"TODO(renald-stand): the profile '{ground.Key}' does not name Renald the Elder (weenie class {HuntScenario.RenaldWcid}); every walk to him ends at its stop_point");
    }

    /// <summary>
    /// M5 7 P7p (D141, check, act, check): true when a vendor open ended other than Confirmed and the server MoveTos the movement controller ended as stuck rose while it was under way (<paramref name="stuckEndsBefore"/>, read
    /// before the open was sent, to <paramref name="stuckEndsAfter"/>, read when it settled): ACE's walk-to-use ran into something and its chain timed out, the item-end gate's "UseDone 0 but no 0062". The named recovery is a walk to
    /// <see cref="RenaldStand"/> and one more open.
    /// </summary>
    /// <param name="outcome">The open's outcome, or null while unsettled.</param>
    /// <param name="stuckEndsBefore">The stuck ends before the open.</param>
    /// <param name="stuckEndsAfter">The stuck ends after it.</param>
    public static bool RefusedAfterStuckMoveTo(ActionOutcome? outcome, int stuckEndsBefore, int stuckEndsAfter) =>
        outcome is { Kind: not ActionOutcomeKind.Confirmed } && stuckEndsAfter > stuckEndsBefore;

    /// <summary>Creates the leg.</summary>
    /// <param name="name">The leg's name.</param>
    /// <param name="context">The journey's context.</param>
    protected ShopLeg(string name, JourneyContext context)
        : base(name, context)
    {
    }

    /// <summary>The guid of the NPC the last <see cref="GoNear"/> found in reach, or 0.</summary>
    protected uint NpcGuid { get; private set; }

    /// <summary>
    /// True once a door that a walk of this station itself Used turned Ethereal (M5 item 5, d1 and F2: the runner stops at a closed door on its route, <c>purpose=door</c>, and Uses it through <see cref="SessionDoorFeed"/>;
    /// <see cref="DoorWatch"/> reads that Use, by guid, and the door's Ethereal state; the vendor's own Use, another door and a door found open never count). Report-only: the station's <c>door_opened=</c>.
    /// </summary>
    protected bool DoorOpened { get; private set; }

    /// <summary><c>yes</c> or <c>no</c>: <see cref="DoorOpened"/>, for the verdict line.</summary>
    protected string DoorOpenedField => DoorOpened ? "yes" : "no";

    /// <summary>The NPC's use radius: the UseRadius its weenie description sent, else the model's default (<c>W/WorldObject_Use.cs</c> 47-61, as the vendor model's close rule reads it).</summary>
    /// <param name="npc">The NPC's object.</param>
    public static double UseRadiusOf(WorldObject npc)
    {
        ArgumentNullException.ThrowIfNull(npc);
        return npc.Weenie.UseRadius ?? State.GameStateReducer.VendorDefaultUseRadius;
    }

    /// <summary>
    /// The centre-to-centre distance at which the shop entry counts the NPC in reach: the use radius plus the margin the vendor model's close rule stands for the two cylinder radii (ACE measures the use edge to edge;
    /// <c>GameStateReducer.VendorCloseMarginMetres</c>), so an open the station sends is one ACE does not refuse for range.
    /// </summary>
    /// <param name="npc">The NPC's object.</param>
    public static double ReachOf(WorldObject npc) => UseRadiusOf(npc) + State.GameStateReducer.VendorCloseMarginMetres;

    /// <summary>
    /// How far from an unseen vendor's own point the walk to it stops, centre to centre, in metres: <see cref="ReachOf"/> for a vendor whose use radius is not yet known, the model's default (0.6 m) plus the
    /// close rule's margin (1.0 m), 1.6 m, inside every vendor's reach (the Holtburg vendors' UseRadius is 3 m, Renald's 5, in the pinned world database). Since M5 item 5 (D70) the vendor's own body stands in the
    /// player's physics world, so a point goal at his own point with the 0.5 m stop tolerance is never reached: the body jams against him about 1 m out and the run ends stuck (M5 item 7's soak, round 10, Thelnoth's
    /// cellar 0xA9B40111; <c>m5-renald-shop-floor</c>, D120). Not retail: Lifestone's own stand-off.
    /// </summary>
    public const double UnseenVendorReachMetres = State.GameStateReducer.VendorDefaultUseRadius + State.GameStateReducer.VendorCloseMarginMetres;

    /// <summary>
    /// The price line of a ware whose wire price (<see cref="VendorStock.PriceToBuy(VendorStockEntry, uint)"/> of one, over the vendor's 0062) differs from the profile's (<c>HuntingGround.PriceOf</c>), or null when they agree: the wire wins
    /// (M5 item 3 m3; the profile's price is the planner's estimate, the 0062 the vendor's own).
    /// </summary>
    /// <param name="ware">The ware's name.</param>
    /// <param name="vendor">The vendor's name.</param>
    /// <param name="wire">The price of one from the 0062.</param>
    /// <param name="profile">The profile's price, or null when the profile lists none.</param>
    public static string? PriceNote(string ware, string vendor, uint wire, int? profile) =>
        profile is { } p && p == wire
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"price: {ware} at {vendor}: the wire's {wire} pyreals, the profile's {(profile is { } q ? q.ToString(CultureInfo.InvariantCulture) : "none")}: the wire's used");

    /// <summary>
    /// Walks to <paramref name="who"/> and sets <see cref="NpcGuid"/> to him when he is in the object table, the player stands in one of <paramref name="cells"/> and he is within <paramref name="radius"/>. The first request stands
    /// <paramref name="offset"/> m on one side of him, the second (after a miss, <c>TODO(m4-vendor-approach)</c>) on the other. He is the nearest object whose name contains <paramref name="nameContains"/>
    /// (<see cref="ThelnothName"/>, <see cref="RenaldName"/>: the full distinguishing name, never a first name two vendors share).
    /// </summary>
    protected IEnumerable<ActionHandle?> GoNear(string who, string nameContains, uint[] cells, Vector3 npc, float offset, double radius)
    {
        NpcGuid = 0;
        using var doors = new DoorWatch(S);
        foreach (var side in new[] { offset, -offset })
        {
            var known = NearestContaining(nameContains, 400);
            var at = new Vector3(npc.X + side, npc.Y, npc.Z);
            var label = $"{who} {side:+0.0;-0.0} m";
            foreach (var h in TravelTo(new TravelRequest(Destination(label, cells[0], at), TravelPurpose.Vendor, known, radius), label)) { yield return h; }

            DoorOpened |= doors.DoorOpened;
            foreach (var h in Until(() => NearestContaining(nameContains, 20) != 0, NpcSeconds)) { yield return h; }

            NpcGuid = NearestContaining(nameContains, 20);
            var here = S.State.Self.Position;
            var inCell = here is { } p && Array.IndexOf(cells, p.CellId) >= 0;
            var distance = double.NaN;
            if (NpcGuid != 0 && here is { } h0 && S.State.Objects.TryGet(NpcGuid, out var o) && o.Position is { } q)
            {
                distance = Vector3.Distance(q.Origin, h0.Origin);
            }

            Log($"{who}: travelled {side:+0.0;-0.0} m; in a wanted cell {inCell}; the NPC {(NpcGuid == 0 ? "unknown" : "0x" + NpcGuid.ToString("X8", CultureInfo.InvariantCulture))} at {distance:0.0} m");
            if (NpcGuid != 0 && inCell && distance <= radius)
            {
                yield break;
            }
        }
    }

    /// <summary>
    /// M5 7 P7p: walks to <see cref="RenaldStand"/> as a point goal (no object, no range: the stop tolerance) and sets <see cref="NpcGuid"/> to Renald the Elder (the nearest object whose name contains
    /// <see cref="RenaldName"/>) when the object table holds him. Logs where the walk ended, the cell and his distance, centre to centre, and whether that is within <see cref="HuntScenario.RenaldUseMetres"/>.
    /// </summary>
    /// <param name="why">Why the walk is made, for the log (the first walk, or the recovery after a refused open).</param>
    protected IEnumerable<ActionHandle?> GoToRenaldStand(string why)
    {
        NpcGuid = 0;
        using var doors = new DoorWatch(S);
        var (cell, point) = RenaldStand;
        var label = "Renald's stand";
        Log(string.Create(CultureInfo.InvariantCulture, $"Renald: {why}: a point goal to his stand 0x{cell:X8} ({point.X:0.##}, {point.Y:0.##}, {point.Z:0.##}), the den profile's stop_point, inside his 5 m use radius (no MoveTo of ACE's is needed)"));
        foreach (var h in TravelTo(new TravelRequest(Destination(label, cell, point), TravelPurpose.Vendor), label)) { yield return h; }

        DoorOpened |= doors.DoorOpened;
        foreach (var h in Until(() => NearestContaining(RenaldName, 20) != 0, NpcSeconds)) { yield return h; }

        NpcGuid = NearestContaining(RenaldName, 20);
        var here = S.State.Self.Position;
        var inCell = here is { } p && p.CellId == cell;
        var distance = double.NaN;
        if (NpcGuid != 0 && here is { } h0 && S.State.Objects.TryGet(NpcGuid, out var o) && o.Position is { } q)
        {
            distance = q.ToGlobal().DistanceTo(h0.ToGlobal());
        }

        var at = here is { } w ? string.Create(CultureInfo.InvariantCulture, $"0x{w.CellId:X8} ({w.Origin.X:0.00}, {w.Origin.Y:0.00}, {w.Origin.Z:0.00})") : "unknown";
        Log(string.Create(CultureInfo.InvariantCulture, $"Renald: at his stand: the walk ended at {at}; in his front room {inCell}; the NPC {(NpcGuid == 0 ? "unknown" : "0x" + NpcGuid.ToString("X8", CultureInfo.InvariantCulture))} at {distance:0.00} m centre to centre (within {HuntScenario.RenaldUseMetres:0.0} m: {distance <= HuntScenario.RenaldUseMetres})"));
    }

    /// <summary>The last open <see cref="OpenRenald"/> sent (the recovery's when it ran), or null before the first.</summary>
    protected ActionHandle? RenaldOpen { get; private set; }

    /// <summary>The guid <see cref="RenaldOpen"/> was sent to (the recovery's walk may find him again under the same guid).</summary>
    protected uint RenaldOpenGuid { get; private set; }

    /// <summary>
    /// M5 7 P7p (D141, check, act, check): opens Renald the Elder (<paramref name="renald"/>) and reads the movement controller's stuck ends before the send and after the settle. When the open ended other than Confirmed and ACE's
    /// walk-to-use ended stuck in between (<see cref="RefusedAfterStuckMoveTo"/>), the named recovery runs once: the walk to <see cref="RenaldStand"/> (<see cref="GoToRenaldStand"/>), then one more open. Every step is logged by
    /// name; the caller judges <see cref="RenaldOpen"/>, the last open sent.
    /// </summary>
    /// <param name="renald">His guid.</param>
    /// <param name="what">The check's name for the log (<c>(b) OpenVendor Renald</c>).</param>
    protected IEnumerable<ActionHandle?> OpenRenald(uint renald, string what)
    {
        var stuckBefore = S.Actions.Move.ServerMoveToStuckEnds;
        RenaldOpenGuid = renald;
        RenaldOpen = S.Actions.OpenVendor(renald, ActionSource.Host);
        yield return RenaldOpen;
        var stuckAfter = S.Actions.Move.ServerMoveToStuckEnds;
        if (!RefusedAfterStuckMoveTo(RenaldOpen.Outcome, stuckBefore, stuckAfter))
        {
            Log($"{what}: {Describe(RenaldOpen)} (server MoveTos ended stuck: {stuckBefore} -> {stuckAfter}; no recovery needed)");
            yield break;
        }

        Log($"{what}: {Describe(RenaldOpen)} after ACE's MoveTo ended {S.Actions.Move.LastServerMoveToEnd ?? "unknown"} (stuck ends {stuckBefore} -> {stuckAfter}): recovery: walking to his stand and opening once more");
        foreach (var h in GoToRenaldStand("the recovery after a refused open")) { yield return h; }

        RenaldOpenGuid = NpcGuid != 0 ? NpcGuid : renald;
        var again = S.Actions.Move.ServerMoveToStuckEnds;
        RenaldOpen = S.Actions.OpenVendor(RenaldOpenGuid, ActionSource.Host);
        yield return RenaldOpen;
        Log($"{what}, the recovery's open: {Describe(RenaldOpen)} (server MoveTos ended stuck: {again} -> {S.Actions.Move.ServerMoveToStuckEnds})");
    }

    /// <summary>The main pack's objects (ContainerType 0: no side pack, no focus).</summary>
    protected IEnumerable<(uint Guid, WorldObject Object)> PackItems()
    {
        foreach (var entry in S.State.Self.Inventory.Pack)
        {
            if (entry.ContainerType == 0 && S.State.Objects.TryGet(entry.Guid, out var o))
            {
                yield return (entry.Guid, o);
            }
        }
    }

    /// <summary>
    /// The cheapest main-pack item <paramref name="accept"/> takes that is not worn, not a kit, a potion, a draught, a tincture, the fighter's weapons, arrows or pyreals, has a Value of at least 1 and is no container (a stack of one is
    /// preferred to a stack of many when its Value is within 100). Ported from <c>HunterShopLeg.Cheapest</c>.
    /// </summary>
    protected (uint Guid, WorldObject Object)? Cheapest(Func<WorldObject, bool> accept)
    {
        string[] keepNames = ["Kit", "Potion", "Draught", "Tincture", "Mace", "Bow", "Arrow", "Pyreal"];
        uint[] keepWcids = [628, 629, 377, 378, 379, 2457, 2460, 27326, 331, 307, 300, 273];
        (uint Guid, WorldObject Object)? best = null;
        foreach (var (guid, o) in PackItems())
        {
            var w = o.Weenie;
            if (Array.IndexOf(keepWcids, w.WeenieClassId) >= 0 || w.WielderId is { } wielder && wielder != 0 || keepNames.Any(n => w.Name.Contains(n, StringComparison.OrdinalIgnoreCase))
                || S.State.Self.Inventory.Find(guid).Kind != InventoryPlaceKind.MainPack || (w.Value ?? 0) < 1 || (w.ItemType & 0x200) != 0 || !accept(o))
            {
                continue;
            }

            var loneBetter = best is { } b && (b.Object.Weenie.StackSize ?? 1) > 1 && (w.StackSize ?? 1) <= 1 && w.Value <= b.Object.Weenie.Value + 100;
            if (best is null || w.Value < best.Value.Object.Weenie.Value || loneBetter)
            {
                best = (guid, o);
            }
        }

        return best;
    }
}

/// <summary>
/// The appraise-items station (M4 P5b J3a; the items half of <c>AppraiseLiveTests</c>, :34): <see cref="IActionApi.Identify"/> on a Handy Healing Kit of the pack and on the character's weapon, each Confirmed and read back from the model
/// (<see cref="IGameSessionView.TryGetAppraisal"/>), never from the handle's own claim. Nothing is created, moved or sold. Needs a kit; the weapon is the one-hand launcher or Mace the character holds (<paramref name="weaponWcid"/> names it, 0 finds a launcher
/// or the Mace in the pack or the hand).
/// </summary>
/// <param name="weaponWcid">The weenie class of the weapon to identify, or 0 for a launcher or the Mace the character holds.</param>
public sealed class AppraiseItemsStation(uint weaponWcid = 0) : IStation
{
    /// <inheritdoc />
    public string Name => "appraise-items";

    /// <inheritdoc />
    public StationNeeds Needs { get; } = new(Kits: 1);

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context) => new AppraiseItemsRun(context, weaponWcid);
}

/// <summary>What the two appraise stations share: the tally and the per-target lines.</summary>
public abstract class AppraiseRun : ShopLeg
{
    private readonly Dictionary<string, string> _lines = [];

    /// <summary>Creates the run.</summary>
    /// <param name="name">The leg's name.</param>
    /// <param name="context">The journey's context.</param>
    protected AppraiseRun(string name, JourneyContext context)
        : base(name, context)
    {
    }

    /// <summary>The counter fed with each Identify (<c>appraised=</c>, <c>failed=</c>, <c>cooldowns=</c>, <c>not_found=</c>).</summary>
    public AppraisalTally Tally { get; } = new();

    /// <summary>False when a failed creature roll carried a profile (ACE withholds it).</summary>
    public bool FailedRollHadNoProfile { get; private set; } = true;

    /// <summary>The line of one target (<c>flags=0x... success= kind= profile= confirmed=</c>), or <c>n/a</c>.</summary>
    protected string LineOf(string what) => _lines.GetValueOrDefault(what, "n/a");

    /// <summary>The tally's fields.</summary>
    protected string TallyFields() =>
        string.Create(CultureInfo.InvariantCulture, $"appraised={Tally.Appraised} failed={Tally.Failed} cooldowns={Tally.Cooldowns} not_found={Tally.NotFound}");

    /// <summary>Identifies <paramref name="guid"/> (skipped when 0), records it in the tally and writes the target's line.</summary>
    protected IEnumerable<ActionHandle?> IdentifyOne(string what, uint guid)
    {
        if (guid == 0)
        {
            yield break;
        }

        var identify = S.Actions.Identify(guid, ActionSource.Host);
        yield return identify;
        Tally.Record(identify);
        var confirmed = Confirmed(identify, $"Identify {what}");
        var known = S.TryGetAppraisal(guid, out var appraisal);
        Check(known && appraisal is not null, $"{what}: the model holds no appraisal after the Confirmed Identify");
        if (appraisal is null)
        {
            yield break;
        }

        var profile = appraisal.Weapon is not null ? "weapon" : appraisal.Creature is not null ? "creature" : appraisal.Armor is not null ? "armor" : "none";
        _lines[what] = $"flags=0x{(uint)appraisal.Flags:X}/success={(appraisal.Success ? 1 : 0)}/kind={identify.Result?.Kind}/profile={profile}/confirmed={Yes(confirmed)}";
        if (what == "thelnoth" && !appraisal.Success)
        {
            FailedRollHadNoProfile = appraisal.Creature is null;
            Log($"a failed creature roll was seen: flags=0x{(uint)appraisal.Flags:X}, ints {appraisal.Ints.Count}, creature profile {(appraisal.Creature is null ? "withheld" : "PRESENT")} (TODO(m4-appraise-fail-live) can close)");
        }

        foreach (var h in Pause(0.5)) { yield return h; }
    }

    /// <summary>The fields common to both stations' verdicts, with the problems and the failure.</summary>
    protected string Tail() => $" problems={Problems.Count} granted={Granted}" + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));
}

/// <summary>The appraise-items station's run (see <see cref="AppraiseItemsStation"/>).</summary>
public sealed class AppraiseItemsRun : AppraiseRun
{
    private readonly uint _weaponWcid;

    internal AppraiseItemsRun(JourneyContext context, uint weaponWcid)
        : base("appraise-items", context)
    {
        _weaponWcid = weaponWcid;
    }

    /// <inheritdoc />
    public override string VerdictFields() => $"kit={LineOf("kit")} bow={LineOf("bow")} " + TallyFields() + Tail();

    /// <inheritdoc />
    protected override IEnumerable<ActionHandle?> Script()
    {
        var kit = PickKit();
        var weapon = FindWeapon();
        Check(kit != 0, "no Handy Healing Kit in the pack to identify");
        Check(weapon != 0, "the character's weapon (a launcher or the Mace) is not held: nothing to identify");
        foreach (var h in IdentifyOne("kit", kit)) { yield return h; }

        foreach (var h in IdentifyOne("bow", weapon)) { yield return h; }
    }

    private uint FindWeapon()
    {
        if (_weaponWcid != 0)
        {
            return HeldOf(_weaponWcid).FirstOrDefault();
        }

        foreach (var wcid in FighterItems.LauncherWcids)
        {
            if (HeldOf(wcid).FirstOrDefault() is var guid and not 0)
            {
                return guid;
            }
        }

        return HeldOf(FighterKit.MaceWcid).FirstOrDefault();
    }
}

/// <summary>
/// The appraise-vendor station (M4 P5b J3a; the vendor half of <c>AppraiseLiveTests</c>): on foot to Thelnoth Cort the Healer's cellar (cell 0xA9B40111, within 3 m) as a <see cref="TravelPurpose.Vendor"/> request with Thelnoth as the object
/// goal, then <see cref="IActionApi.Identify"/> on him (an NPC is a Creature in ACE: the creature profile and the armour levels, or a failed roll with the tables and no profile). D23: nobody is spawned.
/// </summary>
public sealed class AppraiseVendorStation : IStation
{
    /// <inheritdoc />
    public string Name => "appraise-vendor";

    /// <inheritdoc />
    public StationNeeds Needs { get; } = new(Where: StationWhere.Holtburg, Peace: true);

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context) => new AppraiseVendorRun(context);
}

/// <summary>The appraise-vendor station's run (see <see cref="AppraiseVendorStation"/>).</summary>
public sealed class AppraiseVendorRun : AppraiseRun
{
    internal AppraiseVendorRun(JourneyContext context)
        : base("appraise-vendor", context)
    {
    }

    /// <inheritdoc />
    public override string VerdictFields() => $"thelnoth={LineOf("thelnoth")} " + TallyFields() + Tail();

    /// <inheritdoc />
    protected override IEnumerable<ActionHandle?> Script()
    {
        Stage = "walk";
        foreach (var h in GoNear("Thelnoth", ThelnothName, [ThelnothCell], ThelnothAt, StandOff, HuntScenario.ThelnothUseMetres)) { yield return h; }

        Check(NpcGuid != 0, "Thelnoth never came into the object table in reach");
        Stage = "identify";
        foreach (var h in IdentifyOne("thelnoth", NpcGuid)) { yield return h; }
    }
}

/// <summary>
/// The vendor-facts station (M4 P5b J3a; ports <c>VendorLiveTests</c> :44): on foot to Thelnoth's cellar, one Handy Healing Kit bought at the price the 0062 predicts (<see cref="VendorStock.PriceToBuy(VendorStockEntry, uint)"/>) and sold back at
/// <see cref="VendorStock.PayoutFor(WorldObject)"/>, an armour item outside his mask refused with the unsellable text, then on foot to Renald the Elder's FRONT room (cell 0xA9B4013F, never the back room 0xA9B40141 behind the counter:
/// <c>TODO(room-steps)</c>) where one item his mask takes is sold. The sale items are what the pack already holds (loot the hunt kept); only when it holds none does the station grant one with the item verb (counted, <see cref="ScriptLeg.Granted"/>),
/// and pyreals when the pack holds fewer than 50 (the buy needs 14). A granted item is sold back so nothing is left over.
/// </summary>
public sealed class VendorFactsStation : IStation
{
    /// <inheritdoc />
    public string Name => "vendor-facts";

    /// <inheritdoc />
    public StationNeeds Needs { get; } = new(Where: StationWhere.Holtburg, Peace: true);

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context) => new VendorFactsRun(context);
}

/// <summary>The vendor-facts station's run (see <see cref="VendorFactsStation"/>).</summary>
public sealed class VendorFactsRun : ShopLeg
{
    private const uint PyrealWcid = 273;
    private const int PyrealGrant = 100;
    private const long PyrealFloor = 50;
    private const uint ShirtWcid = 130;
    private const uint BracersWcid = 25637;
    private readonly List<string> _verdicts = [];
    private readonly Dictionary<string, string> _fields = [];
    private uint _shirt;
    private uint _bracers;
    private bool _shirtGranted;
    private bool _bracersGranted;
    private bool _vendorWindow;

    internal VendorFactsRun(JourneyContext context)
        : base("vendor-facts", context)
    {
    }

    /// <summary>The four verdict lines of the ported fact, in order: <c>vendor buy=</c>, <c>vendor sellback=</c>, <c>vendor refused=</c>, <c>vendor sold=</c>.</summary>
    public IReadOnlyList<string> Verdicts => _verdicts;

    /// <summary>
    /// True from a vendor's open being sent to the visit's end (M4 4.2 J1, the window <see cref="VendorFrameSampler"/> measures, as <see cref="HuntScenario.VendorWindow"/> is the vendor station's): Thelnoth's visit ends when the
    /// station walks away to Renald (leaving the shop closes the vendor: ACE has no close message), Renald's with the station.
    /// </summary>
    public bool VendorWindow => _vendorWindow;

    /// <inheritdoc />
    public override string VerdictFields()
    {
        var parts = new List<string>();
        foreach (var key in new[] { "buy", "sellback", "refused", "sold" })
        {
            parts.Add(_fields.GetValueOrDefault(key, key + "=no"));
        }

        return string.Join(' ', parts) + $" problems={Problems.Count} granted={Granted} vendor_frame_max_ms={HuntLegVerdict.FrameMs(Context.WindowFrames?.VendorFrameMaxMs)} door_opened={DoorOpenedField}" + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));
    }

    /// <inheritdoc />
    protected override IEnumerable<ActionHandle?> Script()
    {
        Stage = "coins";
        foreach (var h in GrantIfMissing(() => Coins() >= PyrealFloor ? 1u : 0u, PyrealWcid, $"Pyreals (the pack holds fewer than {PyrealFloor}; the stack is {PyrealGrant})", _ => { }, PyrealGrant)) { yield return h; }

        Check(Coins() >= PyrealFloor, $"the pack holds {Coins()} pyreals, below {PyrealFloor}: the buy cannot be paid");

        Stage = "thelnoth";
        foreach (var h in GoNear("Thelnoth", ThelnothName, [ThelnothCell], ThelnothAt, StandOff, HuntScenario.ThelnothUseMetres)) { yield return h; }

        var thelnoth = NpcGuid;
        if (thelnoth == 0)
        {
            Check(false, "(a): Thelnoth Cort the Healer never came into the object table in reach; nothing was done");
            yield break;
        }

        _vendorWindow = true; // the window opens with the send (VendorFrameSampler)
        var open = S.Actions.OpenVendor(thelnoth, ActionSource.Host);
        yield return open;
        var opened = Confirmed(open, "(a) OpenVendor Thelnoth") && S.State.Vendor is { } && S.State.Vendor.Vendor == thelnoth;
        Check(opened, "(a): the vendor model holds Thelnoth's 0062 after the open");
        var stock = S.State.Vendor;
        var row = stock?.Entries.FirstOrDefault(e => e.WeenieClassId == StationItems.KitWcid);
        if (stock is null || row is null)
        {
            Check(false, "(a): Thelnoth's 0062 holds no Handy Healing Kit 628; no buy");
        }
        else
        {
            foreach (var h in BuyAndSellBack(thelnoth, stock, row)) { yield return h; }

            foreach (var h in Refusal(thelnoth, stock)) { yield return h; }
        }

        Stage = "renald";
        _vendorWindow = false; // walking away from Thelnoth ends his visit
        foreach (var h in GoToRenaldStand("the first walk")) { yield return h; }

        var renald = NpcGuid;
        if (renald == 0)
        {
            Check(false, "(b): Renald the Elder never came into the object table in reach; no sale");
        }
        else
        {
            foreach (var h in SellOne(renald)) { yield return h; }

            foreach (var h in SellBracers(renald)) { yield return h; }
        }
    }

    private IEnumerable<ActionHandle?> BuyAndSellBack(uint thelnoth, VendorStock stock, VendorStockEntry row)
    {
        var kitsBefore = InPack(StationItems.KitWcid);
        var predicted = stock.PriceToBuy(row, 1);
        var coinsBefore = Coins();
        var buy = S.Actions.Buy(thelnoth, [(row.Guid, 1u)], ActionSource.Host);
        yield return buy;
        var bought = Confirmed(buy, "(a) Buy 1 kit") && buy.Result is { } r && r.Matched;
        foreach (var h in Until(() => InPack(StationItems.KitWcid).Count > kitsBefore.Count, 5)) { yield return h; }

        var arrived = InPack(StationItems.KitWcid).Count == kitsBefore.Count + 1;
        var delta = Coins() - coinsBefore;
        var ok = bought && arrived && delta == -(long)predicted && buy.Result!.Actual == predicted && predicted == 14;
        Check(ok, $"(a): the buy was Confirmed ({bought}), the kit arrived ({arrived}), the CoinValue delta {delta} is -{predicted} and the price is 14");
        Add("buy", $"vendor buy={Yes(ok)} price={buy.Result?.Actual.ToString(CultureInfo.InvariantCulture) ?? "n/a"} predicted={predicted} coins_delta={delta} kit_arrived={Yes(arrived)}");
        foreach (var h in Pause(1)) { yield return h; }

        var fresh = InPack(StationItems.KitWcid).Except(kitsBefore).ToList();
        if (fresh.Count != 1 || !S.State.Objects.TryGet(fresh[0], out var newKit))
        {
            Check(false, "(a): no single new kit to sell back");
            Add("sellback", "vendor sellback=no payout=n/a predicted=n/a coins_delta=n/a kits=n/a");
            yield break;
        }

        var payoutPredicted = S.State.Vendor?.PayoutFor(newKit) ?? 0;
        var coinsMid = Coins();
        var sell = S.Actions.Sell(thelnoth, [fresh[0]], ActionSource.Host);
        yield return sell;
        var sold = Confirmed(sell, "(a) Sell the new kit back") && sell.Result is { } sr && sr.Matched;
        foreach (var h in Pause(1)) { yield return h; }

        var payoutDelta = Coins() - coinsMid;
        var kitsAfter = InPack(StationItems.KitWcid).Count;
        var okSell = sold && payoutDelta == payoutPredicted && payoutPredicted == 9 && kitsAfter == kitsBefore.Count;
        Check(okSell, $"(a): the sale back was Confirmed ({sold}), the payout {payoutDelta} is the predicted {payoutPredicted} (9) and the character holds {kitsAfter} kits (was {kitsBefore.Count})");
        Add("sellback", $"vendor sellback={Yes(okSell)} payout={sell.Result?.Actual.ToString(CultureInfo.InvariantCulture) ?? "n/a"} predicted={payoutPredicted} coins_delta={payoutDelta} kits={kitsAfter}/{kitsBefore.Count}");
    }

    private IEnumerable<ActionHandle?> Refusal(uint thelnoth, VendorStock stock)
    {
        // An armour item (ItemType 0x2) outside Thelnoth's mask: one the pack holds (looted), else a Leather Bracers from the item verb (counted, sold to Renald afterwards).
        bool Outside(WorldObject o) => (o.Weenie.ItemType & 0x2) != 0 && (o.Weenie.ItemType & stock.Mask) == 0;
        var grantedBefore = Granted;
        foreach (var h in GrantIfMissing(() => Cheapest(Outside)?.Guid ?? 0, BracersWcid, "Leather Bracers 25637 (armour outside Thelnoth's mask)", g => _bracers = g)) { yield return h; }

        _bracersGranted = Granted > grantedBefore;
        if (_bracers == 0 || !S.State.Self.Inventory.Contains(_bracers) || !S.State.Objects.TryGet(_bracers, out var bracersObject) || !Outside(bracersObject))
        {
            Check(false, "(c): no armour item outside Thelnoth's mask is in the pack (and the grant failed); no refusal case");
            Add("refused", "vendor refused=no kind=n/a detail='no item' item_kept=n/a");
            yield break;
        }

        var coinsBefore = Coins();
        var sell = S.Actions.Sell(thelnoth, [_bracers], ActionSource.Host);
        yield return sell;
        foreach (var h in Pause(1)) { yield return h; }

        var detail = sell.Outcome?.Detail ?? string.Empty;
        var kept = S.State.Self.Inventory.Contains(_bracers);
        var ok = sell.Outcome is { Succeeded: false } && detail.Contains("unsellable", StringComparison.Ordinal) && kept && Coins() == coinsBefore;
        Check(ok, $"(c): offering '{bracersObject.Weenie.Name}' (ItemType 0x{bracersObject.Weenie.ItemType:X}) was refused with the unsellable text (kind {sell.Outcome?.Kind}, '{detail}'), the item is kept ({kept}) and the coins are as they were");
        Add("refused", $"vendor refused={Yes(ok)} kind={sell.Outcome?.Kind} detail='{detail}' item_kept={Yes(kept)}");
    }

    /// <summary>
    /// True when <paramref name="item"/> is a sale item for Renald: the vendor's test takes it (<paramref name="buys"/>, <c>VendorStock.Buys</c>) and ACE's own sale refusals do not apply
    /// (<see cref="VendorStock.Unsellable"/>: Player_Commerce.cs:268 @95ef426d; closes <c>TODO(m4-vendor-food-unsellable)</c>: the refusal is the Retained / IsSellable clause, not the Food type, so other food sells).
    /// </summary>
    /// <param name="item">The pack item.</param>
    /// <param name="buys">The vendor's own test of the item.</param>
    public static bool SaleCandidate(WorldObject item, Func<WorldObject, bool> buys)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(buys);
        return buys(item) && !VendorStock.Unsellable(item);
    }

    /// <summary>
    /// Why the item cannot be sold to the vendor now, naming the clause, or null when it can: no item (the grant gave no guid), not in the pack, not in the object table, the vendor's stock not open (none, or another vendor's), or the sale test's own clause
    /// (<see cref="VendorStock.DescribeRefusal"/>: mask, value, retained, unsellable-list).
    /// </summary>
    /// <param name="guid">The item's guid (0 when there is none).</param>
    /// <param name="inPack">Whether the model's inventory holds it.</param>
    /// <param name="item">The object table's entry, or null.</param>
    /// <param name="stock">The vendor the model holds open, or null.</param>
    /// <param name="vendor">The vendor the item is for.</param>
    internal static string? SaleBlock(uint guid, bool inPack, WorldObject? item, VendorStock? stock, uint vendor)
    {
        if (guid == 0)
        {
            return "no item: the grant gave no guid";
        }

        if (!inPack)
        {
            return $"the item 0x{guid:X8} is not in the pack";
        }

        if (item is null)
        {
            return $"the item 0x{guid:X8} is not in the object table";
        }

        if (stock is null)
        {
            return "his stock is not open (the vendor model holds none)";
        }

        if (stock.Vendor != vendor)
        {
            return $"the open stock is 0x{stock.Vendor:X8}'s, not 0x{vendor:X8}'s";
        }

        return stock.Refusal(item) == SaleRefusal.None ? null : stock.DescribeRefusal(item);
    }

    /// <summary>The vendor as a failure line names him: his name and guid from the object table, and his open stock's mask and row count (or that none is open).</summary>
    private string VendorLabel(uint vendor, VendorStock? stock)
    {
        var name = S.State.Objects.TryGet(vendor, out var o) ? o.Name : "unknown";
        var open = stock is null ? "no stock open" : $"the open stock is 0x{stock.Vendor:X8}'s: mask 0x{stock.Mask:X8}, {stock.Entries.Length} rows";
        return $"'{name}' 0x{vendor:X8} ({open})";
    }

    private IEnumerable<ActionHandle?> SellOne(uint renald)
    {
        _vendorWindow = true;
        foreach (var h in OpenRenald(renald, "(b) OpenVendor Renald")) { yield return h; }

        var open = RenaldOpen!;
        renald = RenaldOpenGuid;
        var stock = S.State.Vendor;
        if (!Confirmed(open, "(b) OpenVendor Renald") || stock is null || stock.Vendor != renald)
        {
            Check(false, "(b): Renald's 0062 did not arrive; no sale");
            Add("sold", "vendor sold=no item='n/a' payout=n/a predicted=n/a coins_delta=n/a gone=n/a");
            yield break;
        }

        Log($"Renald's stock: {VendorLabel(renald, stock)}");

        // One item his mask takes (food is refused whatever the mask says: SaleCandidate): loot the pack holds, else a Shirt from the item verb (counted).
        var grantedBefore = Granted;
        foreach (var h in GrantIfMissing(() => Cheapest(o => SaleCandidate(o, stock.Buys))?.Guid ?? 0, ShirtWcid, "Shirt 130 (inside Renald's mask)", g => _shirt = g)) { yield return h; }

        _shirtGranted = Granted > grantedBefore;
        Log(_shirtGranted ? "the sale item was granted with the item verb" : "the sale item is loot the pack already held");
        var block = SaleBlock(_shirt, S.State.Self.Inventory.Contains(_shirt), S.State.Objects.TryGet(_shirt, out var shirt) ? shirt : null, S.State.Vendor, renald);
        if (block is not null || shirt is null)
        {
            Check(false, $"(b): no sale item for {VendorLabel(renald, S.State.Vendor)}: {block}; no sale");
            Add("sold", "vendor sold=no item='n/a' payout=n/a predicted=n/a coins_delta=n/a gone=n/a");
            yield break;
        }

        var predicted = stock.PayoutFor(shirt);
        var coinsBefore = Coins();
        var sell = S.Actions.Sell(renald, [_shirt], ActionSource.Host);
        yield return sell;
        var confirmed = Confirmed(sell, "(b) Sell the item") && sell.Result is { } r && r.Matched;
        foreach (var h in Pause(1)) { yield return h; }

        var delta = Coins() - coinsBefore;
        var gone = !S.State.Self.Inventory.Contains(_shirt);
        var ok = confirmed && gone && delta == predicted;
        Log($"left the pack: '{shirt.Weenie.Name}' {_shirt:X8} to Renald (payout {delta})");
        Check(ok, $"(b): selling '{shirt.Weenie.Name}' (Value {shirt.Weenie.Value}) was Confirmed ({confirmed}), it left the pack ({gone}) and the CoinValue delta {delta} is the predicted {predicted}");
        Add("sold", $"vendor sold={Yes(ok)} item='{shirt.Weenie.Name}' payout={sell.Result?.Actual.ToString(CultureInfo.InvariantCulture) ?? "n/a"} predicted={predicted} coins_delta={delta} gone={Yes(gone)}");
    }

    /// <summary>Sells the Bracers the station granted itself to Renald (armour is in his mask), so nothing is left over; a looted armour item the pack already held is kept. Checked like the sale, without a verdict line.</summary>
    private IEnumerable<ActionHandle?> SellBracers(uint renald)
    {
        var stock = S.State.Vendor;
        if (!_bracersGranted)
        {
            Log("the refusal item was loot the pack already held: kept, not sold");
            yield break;
        }

        if (_shirt == _bracers)
        {
            // The cheapest sale candidate of (b) was the granted Bracers itself (armour is in Renald's mask): (b)'s sale already
            // sold it, so nothing is left over (P5b live gate round 3: the cleanup then found it gone and failed the station).
            Log("the granted Bracers were (b)'s sale item: already sold, nothing left over");
            yield break;
        }

        var block = SaleBlock(_bracers, S.State.Self.Inventory.Contains(_bracers), S.State.Objects.TryGet(_bracers, out var bracers) ? bracers : null, stock, renald);
        if (block is not null || bracers is null || stock is null)
        {
            Check(false, $"(b): the granted Bracers could not be sold back to {VendorLabel(renald, stock)}: {block}");
            yield break;
        }

        var predicted = stock.PayoutFor(bracers);
        var coinsBefore = Coins();
        var sell = S.Actions.Sell(renald, [_bracers], ActionSource.Host);
        yield return sell;
        var confirmed = Confirmed(sell, "(b) Sell the granted Bracers") && sell.Result is { Matched: true };
        foreach (var h in Pause(1)) { yield return h; }

        var delta = Coins() - coinsBefore;
        var gone = !S.State.Self.Inventory.Contains(_bracers);
        Log($"left the pack: the granted '{bracers.Weenie.Name}' {_bracers:X8} to Renald (payout {delta})");
        Check(confirmed && gone && delta == predicted, $"(b): selling the granted '{bracers.Weenie.Name}' was Confirmed ({confirmed}), it left the pack ({gone}) and the CoinValue delta {delta} is the predicted {predicted}");
    }

    /// <summary>Records one verdict line and its fields (the group's own names; the shared ones get the group as a prefix).</summary>
    private void Add(string group, string line)
    {
        _verdicts.Add(line);
        var body = line["vendor ".Length..];
        var fields = new List<string>();
        foreach (var token in SplitFields(body))
        {
            var key = token[..token.IndexOf('=', StringComparison.Ordinal)];
            fields.Add(key == group || key is "kit_arrived" or "kits" or "item_kept" or "item" or "gone" ? token : group + "_" + token);
        }

        _fields[group] = string.Join(' ', fields);
    }

    /// <summary>The <c>key=value</c> tokens of a verdict line, a quoted value ('...') kept whole with its spaces turned into underscores.</summary>
    private static IEnumerable<string> SplitFields(string body)
    {
        var token = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var c in body)
        {
            if (c == '\'')
            {
                quoted = !quoted;
                token.Append(c);
            }
            else if (c == ' ' && !quoted)
            {
                if (token.Length > 0)
                {
                    yield return token.ToString();
                    token.Clear();
                }
            }
            else
            {
                token.Append(quoted && c == ' ' ? '_' : c);
            }
        }

        if (token.Length > 0)
        {
            yield return token.ToString();
        }
    }
}
