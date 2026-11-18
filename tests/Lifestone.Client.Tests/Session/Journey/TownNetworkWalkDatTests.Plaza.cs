using System.Globalization;
using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Net.Diagnostics;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 14 (m6-14-plaza; live ladder62, 2026-11-17 16:28:29 to 16:29:41 UTC): the walk to the Yinar Portal's front kept off its own portal alone (<c>portals=1 of=85</c>): the other eighteen portals of Samsur's plaza (landblock 0x977E) are in no
/// travel record, and Sand's Edge Portal, 23.8 m south of the Yinar Portal on her way up, took her to 0xA586003D.
/// </summary>
public sealed partial class TownNetworkWalkDatTests
{
    /// <summary>
    /// The walk from the Samsur drop to the Yinar Portal's front, with the list the leg keeps (<see cref="JourneyRunLeg.LegKeepOff"/> over the hop's own list): every portal of the plaza is on it (the own one by its reach, the others by the
    /// clearance), the plan reaches the front and is confirmed by the physics, and no plan of the walk comes within the clearance of Sand's Edge or any other portal of the plaza.
    /// </summary>
    [RequiresDatsFact]
    public void The_walk_to_the_yinar_front_from_the_samsur_drop_keeps_clear_of_sands_edge_and_the_plazas_portals() => PlazaWalk(null);

    /// <summary>
    /// M6 14 (m6-14-plaza2; live ladder63, 2026-11-17 18:06:38 UTC): the same walk replanned for a creature from 0x977D0006 (3.34, 132.72, 31.22), with the list made at the drop: every plan the planner makes passes the runner's exact check of
    /// the whole line (live: the fourth plan's leg 4 passed within the Maythen Geroyu Villas Portal's clearance and the run ended <c>portal-keep-off</c>).
    /// </summary>
    [RequiresDatsFact]
    public void The_walk_to_the_yinar_front_from_the_live_replan_north_of_the_plaza_keeps_every_clearance() =>
        PlazaWalk(new CellFrame(0x977D0006u, new AcFrame(new Vector3(3.34f, 132.72f, 31.22f), Quaternion.Identity)));

    private void PlazaWalk(CellFrame? replan)
    {
        var portal = RungPortal.Of(Routes.Edge("samsur-to-yinar"), Routes);
        var portalCell = PortalCell(portal.Cell, portal.Origin);
        var into = Routes.Edge("network-to-samsur");
        var drop = new CellFrame(into.DestCell, new AcFrame(into.DestOrigin, Quaternion.Identity));
        var (frontCell, front, _, _) = TravelLeg.HopFront(portal, portalCell, drop, portal.KeepOffForFront()); // the hop's own choice

        using var rig = Rig(drop);
        var goal = rig.PointGoal(frontCell, front);
        var request = new TravelRequest(
            new TeleportDestination("samsur-to-yinar-front", [], new WorldLocation("samsur-to-yinar-front", new CellPosition(frontCell, front), HasHeight: true, HeadingDegrees: 0), "plaza test"),
            TravelPurpose.Cell,
            KeepOff: portal.KeepOffForFront());
        var dropG = NavMap.FromCellFrame(drop).Global;
        var keep = JourneyRunLeg.LegKeepOff(new EmptyView(), request, dropG, goal.Point.Global)!.Near(dropG, goal.Point.Global, 192f).WithOwnClearance(PortalKeepOff.OwnReach(NavRunner.StopMetres(goal.StopTolerance)));

        var plaza = WorldPortals.All.Where(p => p.Cell >> 16 == 0x977E).ToList();
        Assert.Equal(19, plaza.Count); // the pinned world SQL's placed portals in 0x977E
        Assert.All(plaza, p => Assert.Contains(keep.Portals, k => Vector2.Distance(k.Global, p.Global) <= 0.5f));
        Assert.Equal("Yinar Portal", keep.Own!.Value.Name);
        var sands = keep.Portals.Single(k => k.Name == "Sand's Edge Portal");
        Assert.Equal(PortalKeepOff.ClearanceMetres, keep.ClearanceOf(sands), 4);

        var start = replan ?? drop;
        rig.Streamer.EnsureNow(start.CellId);
        var walk = Walk(rig, start, goal, keep); // each plan's line is checked against the whole list (FirstMeeting): a plan that came within a portal's clearance fails the walk by name
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"walk to the yinar front from 0x{start.CellId:X8} ({start.Origin.X:0.00}, {start.Origin.Y:0.00}), plaza kept off ({keep.Portals.Count} portals): metres={walk.Metres:0.0} pass={(walk.Ok ? "yes" : "no")} {walk.Why}"));
        Assert.True(walk.Ok, walk.Why);
        Assert.Equal(portalCell >> 16, walk.End.CellId >> 16);
        Assert.InRange(Vector2.Distance(new Vector2(walk.End.Origin.X, walk.End.Origin.Y), new Vector2(front.X, front.Y)), 0f, goal.StopTolerance + 0.01f);
        var end = NavMap.FromCellFrame(walk.End).Global;
        Assert.All(plaza.Where(p => p.Guid != 0x7977E006u), p => Assert.True(Vector2.Distance(end, p.Global) > PortalKeepOff.ClearanceMetres, $"the walk ended within the clearance of {p.Name}"));
    }

    /// <summary>A session view that holds no objects (the leg's live portals: none).</summary>
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
