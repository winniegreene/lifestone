using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Data.Animation;
using Lifestone.Data.Tests;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 item 14 (m6-goldscarab) on the retail dats: the walks of a town stock stop (<c>RungChangeStation.TownStockStops</c>), for every vendor a profile's <c>town_stock</c> names (Ludora the Healer of Dryreach; Archmage Sammam al-Ashiz of Uziz,
/// who sells the Gold and the Pyreal Scarab). From the drop of the Town Network's portal into the town, the shop step's walk to the vendor (<c>ResupplyRun.RoomRequest</c>: his own cell and point, the unseen vendor's reach as the stop);
/// from where that walk ends, the hop back: the walk to the front of the town's own Portal to Town Network and the seek to it, as the main theory walks a hop. Every plan is the journeys' own and is confirmed by the walker's physics.
/// The Network's own legs of a stop (from a Network drop to the town's portal, and from the stop's drop back in the Network on to the route's next portal) are the main theory's cases of the two edges.
/// </summary>
public sealed partial class TownNetworkWalkDatTests
{
    /// <summary>Every town stock vendor of every profile, once: the town and the vendor's guid.</summary>
    public static IEnumerable<object[]> TownStockVendors() =>
        GroundCatalog.All.SelectMany(g => g.TownStock).GroupBy(l => l.Vendor.Guid).Select(g => new object[] { g.First().Town, g.Key });

    /// <summary>The profiles name two town stock vendors today; a third is a new row of this theory by itself.</summary>
    [Fact]
    public void The_town_stock_vendors_are_Ludora_of_Dryreach_and_Sammam_of_Uziz()
    {
        Assert.Equal([("Dryreach", 0x7DA75026u), ("Uziz", 0x7A260006u)], TownStockVendors().Select(v => ((string)v[0], (uint)v[1])).OrderBy(v => v.Item1, StringComparer.Ordinal));
    }

    /// <summary>
    /// The stop's two walks in the town, from the Network drop to the vendor and from his counter to the portal back, planned whole and confirmed. One line a vendor
    /// (<c>town-stock-walk town= vendor= drop_to_vendor_m= vendor_to_portal_m= front= pass=</c>) is the profiles' source sentence.
    /// </summary>
    /// <param name="town">The vendor's town.</param>
    /// <param name="vendorGuid">The vendor's guid.</param>
    [RequiresDatsTheory]
    [MemberData(nameof(TownStockVendors))]
    public void A_town_stock_stop_walks_from_the_Network_drop_to_the_vendor_and_from_his_counter_to_the_portal_back(string town, uint vendorGuid)
    {
        var line = GroundCatalog.All.SelectMany(g => g.TownStock).First(l => l.Vendor.Guid == vendorGuid);
        var edges = TownStock.EdgesOf(line, "town-network", Routes);
        Assert.True(edges is not null, $"{town}: the travel file holds no portal from the Town Network into the landblock 0x{line.Vendor.Cell >> 16:X4} and back");
        var (into, back) = edges!.Value;
        var drop = new CellFrame(into.DestCell, new AcFrame(into.DestOrigin, Quaternion.Identity));
        using var rig = Rig(drop);
        var clock = Stopwatch.StartNew();

        var vendorCell = PortalCell(line.Vendor.Cell, line.Vendor.Point);
        var toVendor = Walk(rig, drop, rig.VendorGoal(vendorCell, line.Vendor.Point, ShopLeg.UnseenVendorReachMetres));
        Assert.True(toVendor.Ok, $"{town}: the walk from the drop 0x{drop.CellId:X8} to {line.Vendor.Name} 0x{vendorCell:X8} failed: {toVendor.Why}");
        Assert.Equal(vendorCell >> 16, toVendor.End.CellId >> 16);
        var short3 = Vector3.Distance(toVendor.End.Origin, line.Vendor.Point);
        Assert.True(short3 <= ShopLeg.UnseenVendorReachMetres + 0.5, $"{town}: the walk ended {short3:0.0} m from {line.Vendor.Name}, past the reach {ShopLeg.UnseenVendorReachMetres:0.0} m");

        var home = HopWalk(rig, toVendor.End, back);
        var text = string.Create(CultureInfo.InvariantCulture, $"town-stock-walk town={town} vendor=0x{vendorGuid:X8} drop=0x{drop.CellId:X8} drop_to_vendor_m={toVendor.Metres:0.0} vendor_to_portal_m={home.Metres:0.0} front={home.Basis} plan_ms={clock.Elapsed.TotalMilliseconds:0} pass={(home.Ok ? "yes" : "no")}");
        output.WriteLine(text);
        Assert.True(home.Ok, text + " the hop back failed: " + home.Why);
    }

    /// <summary>
    /// M6 item 14 (m6-vendor-portal-keep-off; live 2026-11-13: the first walk from Uziz's drop to Archmage Sammam al-Ashiz ran into the Haven Residential Halls Portal and the run ended): the same walk with the portals of Uziz's landblock
    /// kept off, as the leg keeps off the portals the client holds (<c>JourneyRunLeg.LivePortals</c>), is planned whole, confirmed, and no leg of it passes within the clearance of a portal. The portals are the world copy's rows of
    /// landblock 0xA260 (SELECT weenie_Class_Id, obj_Cell_Id, origin_X, origin_Y, origin_Z FROM landblock_instance JOIN weenie ON class_Id = weenie_Class_Id WHERE type = 7 AND obj_Cell_Id >> 16 = 0xA260, read 2026-11-13):
    /// Haven Residential Halls Portal 21464 outdoors at (164.4, 53.9, 20.0), Matron Hive West 24434 outdoors at (132.9, 47.5, 20.0), Portal to Town Network 43066 in 0xA2600029 at (129.7, 1.4, 20.2) and the Effigy Foundry Portal 19718
    /// in the room 0xA2600121 at (162.4, 22.0, 14.8). The plan without the list is logged beside it (76.8 m against 77.3 m with the list, 2026-11-13); live, the body met the portal on a second plan made from where a stall left it, which this test does not replay.
    /// </summary>
    [RequiresDatsFact]
    public void The_walk_from_Uzizs_drop_to_its_archmage_keeps_clear_of_the_Haven_Residential_Halls_Portal()
    {
        const uint uziz = 0xA2600000;
        KeptPortal Kept(string name, float x, float y, float z, bool outdoor) => new(name, new NavPoint(uziz, new Vector3(x, y, z)).Global, z, outdoor);
        var keep = new PortalKeepOff([
            Kept("Haven Residential Halls Portal", 164.4f, 53.9f, 20.0f, true),
            Kept("Matron Hive West", 132.9f, 47.5f, 20.0f, true),
            Kept("Portal to Town Network", 129.7f, 1.4f, 20.2f, true),
            Kept("Effigy Foundry Portal", 162.4f, 22.0f, 14.8f, false),
        ]);
        var line = GroundCatalog.Get("tusker-cave").TownStock.First(l => l.Wcid == 687);
        var (into, _) = TownStock.EdgesOf(line, "town-network", Routes)!.Value;
        var drop = new CellFrame(into.DestCell, new AcFrame(into.DestOrigin, Quaternion.Identity));
        var vendorCell = PortalCell(line.Vendor.Cell, line.Vendor.Point);

        using (var bare = Rig(drop))
        {
            var walk = Walk(bare, drop, bare.VendorGoal(vendorCell, line.Vendor.Point, ShopLeg.UnseenVendorReachMetres));
            Assert.True(walk.Ok, walk.Why);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"uziz-walk list=none metres={walk.Metres:0.0}"));
        }

        using var rig = Rig(drop);
        var clear = Walk(rig, drop, rig.VendorGoal(vendorCell, line.Vendor.Point, ShopLeg.UnseenVendorReachMetres), keep);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"uziz-walk list=4 metres={clear.Metres:0.0} ok={clear.Ok} why={clear.Why ?? "none"}"));
        Assert.True(clear.Ok, $"the walk from the drop 0x{drop.CellId:X8} to {line.Vendor.Name} with the portals kept off failed: {clear.Why}");
        Assert.True(Vector3.Distance(clear.End.Origin, line.Vendor.Point) <= ShopLeg.UnseenVendorReachMetres + 0.5);
    }

    /// <summary>
    /// The whole stop at Uziz as the Cave's way out makes it, in metres: from Holtburg's drop in the Network to the Uziz portal, from Uziz's drop to Archmage Sammam al-Ashiz, from his counter to Uziz's Portal to Town Network, and from
    /// its drop in the Network on to the Facility Hub portal; beside it the way with no stop (Holtburg's drop straight to the Facility Hub portal). Every leg is whole and confirmed, and the stop adds under 250 m. The town not chosen is
    /// logged, not judged: Fiun Outpost (the pinned rows of 2026-11-12: its Network portal 0x7000702F in 0x0007012C, the drop 0x38F7001B, the Fiun Archmage 0x738F7002 in 0x38F70100, its Portal to Town Network 0x738F7012), the only other
    /// seller of the Gold Scarab whose walks plan whole from a Network drop; its numbers are the tusker-cave.json town stock line's source sentence.
    /// </summary>
    [RequiresDatsFact]
    public void The_whole_stop_at_Uziz_is_walked_from_Holtburgs_drop_in_the_Network_and_on_to_the_Facility_Hub_portal()
    {
        var holtburgDrop = Drop("holtburg-to-network");
        var into = Routes.Edge("network-to-uziz");
        var back = Routes.Edge("uziz-to-network");
        var next = Routes.Edge("network-to-facility-hub");
        var sammam = GroundCatalog.Get("tusker-cave").TownStock.First(l => l.Wcid == 687).Vendor;

        double hubIn, town, townBack, hubOn, direct;
        using (var rig = Rig(holtburgDrop))
        {
            var leg = HopWalk(rig, rig.RealRoom(holtburgDrop), into);
            Assert.True(leg.Ok, "Holtburg's drop to the Uziz portal: " + leg.Why);
            hubIn = leg.Metres;
            var straight = HopWalk(rig, rig.RealRoom(holtburgDrop), next);
            Assert.True(straight.Ok, "Holtburg's drop to the Facility Hub portal: " + straight.Why);
            direct = straight.Metres;
            var backDrop = Drop("uziz-to-network");
            var on = HopWalk(rig, rig.RealRoom(backDrop), next);
            Assert.True(on.Ok, "Uziz's drop in the Network to the Facility Hub portal: " + on.Why);
            hubOn = on.Metres;
        }

        var drop = Drop("network-to-uziz");
        using (var rig = Rig(drop))
        {
            var toVendor = Walk(rig, drop, rig.VendorGoal(sammam.Cell, sammam.Point, ShopLeg.UnseenVendorReachMetres));
            Assert.True(toVendor.Ok, "Uziz's drop to Sammam: " + toVendor.Why);
            town = toVendor.Metres;
            Assert.Contains(0xA2600100u, toVendor.Cells); // through the room behind his street door (the door 0x7A260000 stands in it)
            Assert.Equal(sammam.Cell, toVendor.End.CellId);
            var home = HopWalk(rig, toVendor.End, back);
            Assert.True(home.Ok, "Sammam's counter to Uziz's portal back: " + home.Why);
            townBack = home.Metres;
        }

        var whole = hubIn + town + townBack + hubOn;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"town-stock-stop town=Uziz network_in_m={hubIn:0.0} drop_to_vendor_m={town:0.0} vendor_to_portal_m={townBack:0.0} network_on_m={hubOn:0.0} whole_m={whole:0.0} no_stop_m={direct:0.0} added_m={whole - direct:0.0}"));
        Assert.InRange(whole - direct, 0, 250);

        // The town not chosen, logged for the source sentence (never judged: a change of its walk changes no behaviour).
        var fiunPortal = new RungPortal("network-to-fiun", "Fiun Outpost", 42999, 0x7000702F, 0x0007012C, new Vector3(53.8f, -170.018f, -0.063f), 0x38F7, null, null, [0x0007], new PortalFacing(-0.707107, 0, 0, -0.707107).Forward);
        var fiunBack = new RungPortal("fiun-to-network", "Portal to Town Network", 42852, 0x738F7012, 0x38F70019, new Vector3(82.7407f, 15.5784f, 2.198f), 0x0007, null, null, [0x38F7], new PortalFacing(0.999636, 0, 0, 0.026977).Forward);
        var fiunDrop = new CellFrame(0x38F7001B, new AcFrame(new Vector3(91f, 54f, -0.095f), Quaternion.Identity));
        var fiunBackDrop = new CellFrame(0x00070145, new AcFrame(new Vector3(70f, -80f, 0.005f), Quaternion.Identity));
        using var hub = Rig(holtburgDrop);
        var a = HopWalk(hub, hub.RealRoom(holtburgDrop), fiunPortal);
        var d = HopWalk(hub, hub.RealRoom(fiunBackDrop), RungPortal.Of(next, Routes));
        using var outpost = Rig(fiunDrop);
        var b = Walk(outpost, fiunDrop, outpost.VendorGoal(0x38F70100, new Vector3(103.523f, 9.38596f, 2.21048f), ShopLeg.UnseenVendorReachMetres));
        var c = b.Ok ? HopWalk(outpost, b.End, fiunBack) : new HopWalkResult(false, 0, "none", "the walk to the vendor failed");
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"town-stock-stop town=Fiun (not chosen) network_in_m={a.Metres:0.0} ({a.Ok}) drop_to_vendor_m={b.Metres:0.0} ({b.Ok}) vendor_to_portal_m={c.Metres:0.0} ({c.Ok}) network_on_m={d.Metres:0.0} ({d.Ok}) whole_m={a.Metres + b.Metres + c.Metres + d.Metres:0.0}"));
    }

    private sealed record HopWalkResult(bool Ok, double Metres, string Basis, string? Why);

    private HopWalkResult HopWalk(StreamRig rig, CellFrame from, TravelEdge edge) => HopWalk(rig, from, RungPortal.Of(edge, Routes));

    /// <summary>A hop's walk from <paramref name="from"/> as the main theory walks it: to the front point (the facing side; when that walk fails, the approach side, the hop's one recovery), then the seek to within the portal's range.</summary>
    private static HopWalkResult HopWalk(StreamRig rig, CellFrame from, RungPortal portal)
    {
        var portalCell = PortalCell(portal.Cell, portal.Origin);
        var (frontCell, front, basis) = TravelLeg.FrontOf(portal, portalCell, from);
        (frontCell, front) = rig.FrontRoom(frontCell, front, out var inNoRoom);
        var walk = inNoRoom && basis == TravelLeg.FrontBasisFacing ? new WalkResult(false, 0, from, "front-in-no-room") : Walk(rig, from, rig.PointGoal(frontCell, front));
        if (!walk.Ok && basis == TravelLeg.FrontBasisFacing)
        {
            var facingWhy = walk.Why;
            var (approachCell, approach, _) = TravelLeg.FrontOf(portal with { Facing = null }, portalCell, from);
            (approachCell, approach) = rig.FrontRoom(approachCell, approach, out _);
            basis = TravelLeg.FrontBasisApproach + "(front-wall)";
            walk = Walk(rig, from, rig.PointGoal(approachCell, approach));
            if (!walk.Ok)
            {
                return new HopWalkResult(false, walk.Metres, basis, $"facing: {facingWhy}; approach: {walk.Why}");
            }
        }
        else if (!walk.Ok)
        {
            return new HopWalkResult(false, walk.Metres, basis, walk.Why);
        }

        var seek = Walk(rig, walk.End, rig.ObjectGoal(portal.Guid == 0 ? 1u : portal.Guid, portalCell, portal.Origin, DenTravelStation.PortalRangeMetres));
        if (!seek.Ok)
        {
            return new HopWalkResult(false, walk.Metres + seek.Metres, basis, "the seek: " + seek.Why);
        }

        var end = seek.End;
        var off = Vector2.Distance(new Vector2(end.Origin.X, end.Origin.Y), new Vector2(portal.Origin.X, portal.Origin.Y));
        return end.CellId >> 16 == portalCell >> 16 && off <= DenTravelStation.PortalRangeMetres + 0.5
            ? new HopWalkResult(true, walk.Metres + seek.Metres, basis, null)
            : new HopWalkResult(false, walk.Metres + seek.Metres, basis, string.Create(CultureInfo.InvariantCulture, $"the seek ended at {end}, {off:0.0} m from the portal"));
    }

    private static CellFrame Drop(string edge)
    {
        var e = Routes.Edge(edge);
        return new CellFrame(e.DestCell, new AcFrame(e.DestOrigin, Quaternion.Identity));
    }
}
