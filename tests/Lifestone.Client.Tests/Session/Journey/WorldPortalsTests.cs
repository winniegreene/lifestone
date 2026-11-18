using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Travel;
using Lifestone.Client.World;
using Lifestone.Data.World;
using Lifestone.Client.Navigation;
using Lifestone.Net.Diagnostics;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 14 (m6-14-plaza; live ladder62, 2026-11-17 16:29:41 UTC: Sand's Edge Portal took her to 0xA586003D on the walk to the Yinar Portal's front, and the hop named the Yinar Portal), no dats: the world's placed portals
/// (<see cref="WorldPortals"/>, the pinned world SQL), every one on the travel data's known list, and the portal a teleport came from named by where it sent the body.
/// </summary>
public sealed class WorldPortalsTests
{
    private static RungPortal Yinar => RungPortal.Of(TravelRoutes.Default.Edge("samsur-to-yinar"), TravelRoutes.Default);

    /// <summary>The file holds the world's 4,530 placed portals; Sand's Edge stands in Samsur's plaza and sends a body to 0xA586003D; every placed portal is on the known list a walk keeps off.</summary>
    [Fact]
    public void The_world_file_holds_every_placed_portal_and_the_known_list_carries_them()
    {
        Assert.Equal(4530, WorldPortals.All.Count);
        var sands = WorldPortals.All.Single(p => p.Guid == 0x7977E00Au);
        Assert.Equal(("Sand's Edge Portal", 13124u, 0x977E0000u, 0xA586003Du), (sands.Name, sands.Wcid, sands.Cell, sands.DestCell));
        Assert.Equal(new Vector3(28.88f, 29.92f, 29.19f), sands.Origin, new Vector3Comparer(0.01f));
        Assert.True(sands.Outdoor);

        var known = RungPortal.AllKnown;
        Assert.All(WorldPortals.All.Where(p => p.Cell >> 16 == 0x977E), p => Assert.Contains(known, k => Vector2.Distance(k.Global, p.Global) <= 0.5f));
        Assert.Contains(Yinar.KeepOffForFront().Portals, k => k.Name == "Sand's Edge Portal" && !k.Own);
        Assert.Contains(Yinar.KeepOffTheOthers().Portals, k => k.Name == "Sand's Edge Portal");
        Assert.DoesNotContain(Yinar.KeepOffTheOthers().Portals, k => Vector2.Distance(k.Global, Yinar.Global()) <= 0.5f); // its own is still left out of the seek's list
    }

    /// <summary>The hop's taker: the arrival in its own destination's landblock is its own (null); Sand's Edge's destination names Sand's Edge, the placed portal near the walk's line sending a body there; a landblock no placed portal near the line sends a body to names none.</summary>
    [Fact]
    public void A_teleport_during_the_walk_names_the_portal_whose_destination_it_was()
    {
        var into = TravelRoutes.Default.Edge("network-to-samsur");
        var drop = new CellFrame(into.DestCell, new Lifestone.Data.Animation.AcFrame(into.DestOrigin, Quaternion.Identity)); // the live walk's start
        Assert.Null(TravelLeg.TakerOf(Yinar, 0xA9730005u, drop));
        Assert.Equal(0x7977E00Au, TravelLeg.TakerOf(Yinar, 0xA586003Du, drop)?.Guid);
        Assert.Equal(0x7977E00Au, TravelLeg.TakerOf(Yinar, 0xA586003Du)?.Guid);
        Assert.Null(TravelLeg.TakerOf(Yinar, 0x12340001u, drop));

        // a portal that sends a body there but stands far off the walk's line could not have taken her: none is named
        var hub = RungPortal.Of(TravelRoutes.Default.Edge("hub-barracks"), TravelRoutes.Default);
        Assert.Null(TravelLeg.TakerOf(hub, 0xA9B40019u));
    }

    /// <summary>The leg's list with no request list (a quest go): the known and placed portals near the walk's line, none within the clearance of its end, none twice; with the hop's list its own portal stays its own.</summary>
    [Fact]
    public void Every_walk_keeps_the_portals_near_its_line_and_none_at_its_end()
    {
        var yinar = Yinar.Global();
        var south = yinar + new Vector2(0f, -150f);
        var front = yinar + new Vector2(0f, -3f);
        var view = new EmptyView();
        var bare = JourneyRunLeg.LegKeepOff(view, Request(null), south, front)!;
        Assert.Contains(bare.Portals, p => p.Name == "Sand's Edge Portal");
        Assert.DoesNotContain(bare.Portals, p => p.Name == "Yinar Portal"); // within the clearance of the end: an end beside a portal is never refused
        Assert.Null(bare.Own);
        Assert.Equal(bare.Portals.Count, bare.Portals.Select(p => (p.Global.X, p.Global.Y)).Distinct().Count());

        var hop = JourneyRunLeg.LegKeepOff(view, Request(Yinar.KeepOffForFront()), south, front)!;
        Assert.Equal("Yinar Portal", hop.Own!.Value.Name);
        Assert.Single(hop.Portals, p => Vector2.Distance(p.Global, yinar) <= 0.5f);
        Assert.Contains(hop.Portals, p => p.Name == "Sand's Edge Portal");
        Assert.All(hop.Portals, p => Assert.True(Vector2.Distance(p.Global, south) <= 400f)); // a landblock off the line at most
    }

    /// <summary>
    /// The Facility Hub's Eastham Portal stands between the Banderling Conquest Dungeon and Mosswart Maze portals (the world file's placements, 5.58 m apart): the two share the lane between them (each keeps half the gap less a body's radius,
    /// at least its touch and a body's width), a lone portal keeps the whole clearance, and the hop's front, whose approach point from the drop lies 0.7 m from the Banderling portal, is turned round the Eastham Portal into that lane, clear of both.
    /// </summary>
    [Fact]
    public void Close_portals_share_their_lane_and_a_front_in_a_neighbours_clearance_is_turned_round_its_portal()
    {
        var eastham = RungPortal.Of(TravelRoutes.Default.Edge("facility-hub-to-eastham"), TravelRoutes.Default);
        var keep = eastham.KeepOffForFront();
        var banderling = keep.Portals.Single(p => p.Name == "Banderling Conquest Dungeon" && Vector2.Distance(p.Global, eastham.Global()) < 10f);
        var mosswart = keep.Portals.Single(p => p.Name == "Mosswart Maze" && Vector2.Distance(p.Global, eastham.Global()) < 10f);
        var gap = Vector2.Distance(banderling.Global, mosswart.Global);
        Assert.InRange(gap, 5.5f, 5.7f);
        Assert.Equal(MathF.Max(PortalKeepOff.LaneFloorMetres, (gap / 2f) - PortalKeepOff.BodyRadiusMetres), keep.ClearanceOf(banderling), 3);
        Assert.True(keep.ClearanceOf(banderling) > PortalKeepOff.TouchMetres);
        Assert.Equal(PortalKeepOff.ClearanceMetres, keep.ClearanceOf(keep.Portals.First(p => p.Name == "Sand's Edge Portal")), 4);

        var drop = new CellFrame(0x8A020212u, new Lifestone.Data.Animation.AcFrame(TravelRoutes.Default.Edge("network-to-facility-hub").DestOrigin, Quaternion.Identity));
        var (_, approach, _) = TravelLeg.FrontOf(eastham with { Facing = null }, eastham.Cell, drop);
        Assert.True(Vector2.Distance(new Vector2(approach.X, approach.Y), new Vector2(107.261f, -39.874f)) < keep.ClearanceOf(banderling)); // the approach point stands in the Banderling portal's clearance

        var (cell, front, basis, why) = TravelLeg.HopFront(eastham, eastham.Cell, drop, keep);
        Assert.Equal(TravelLeg.FrontBasisApproach, basis);
        Assert.Contains("front-turned=", why, StringComparison.Ordinal);
        Assert.Equal(3f, Vector2.Distance(new Vector2(front.X, front.Y), new Vector2(eastham.Origin.X, eastham.Origin.Y)), 3);
        var g = new NavPoint(cell & 0xFFFF0000u, front).Global;
        Assert.All(keep.Portals.Where(p => !p.Own), p => Assert.True(Vector2.Distance(p.Global, g) >= keep.ClearanceOf(p), $"the front stands within the clearance of {p.Name}"));
    }

    private static TravelRequest Request(PortalKeepOff? keep) =>
        new(new TeleportDestination("t", [], new WorldLocation("t", new CellPosition(0x977E0003u, Vector3.Zero), HasHeight: true, HeadingDegrees: 0), "test"), TravelPurpose.Cell, KeepOff: keep);

    private sealed class Vector3Comparer(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 x, Vector3 y) => Vector3.Distance(x, y) <= tolerance;

        public int GetHashCode(Vector3 obj) => 0;
    }

    private sealed class EmptyView : IGameSessionView
    {
        private readonly ActionHarness _h = new(offline: true);

        public GameState State => _h.State;

        public IEventBus Events => _h.R.Bus;

        public IActionApi Actions => _h.Api;

        public PlayerTools Tools => null!;

        public SessionClock Clock => _h.R.Clock;

        public ActionLog ActionLog => _h.ActionLog;

        public LinkStats Link => default;
    }
}
