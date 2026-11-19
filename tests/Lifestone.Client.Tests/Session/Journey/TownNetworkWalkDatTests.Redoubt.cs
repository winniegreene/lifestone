using System.Globalization;
using System.Numerics;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Data.Animation;
using Lifestone.Data.Tests;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 redoubt (the owner's decision of 2026-11-18: the rung <c>outskirts</c> hunts the Holtburg Redoubt), on the retail dats: the route <c>on-foot-to-the-entry</c> of <c>grounds.holtburg-redoubt</c> walked from Holtburg's lifestone point (the
/// profile's home) to the entry's front point (the rung change's run leg), then the seek to the portal within its use range: each planned and confirmed by the physics, as the island runs and the Cave of Alabree's are. The fetch quest
/// <c>worcer-heirlooms</c> walks the same ground from its own file (<see cref="A_fetch_quest_walks_end_to_end_from_its_own_file"/>).
/// </summary>
public sealed partial class TownNetworkWalkDatTests
{
    [RequiresDatsFact]
    public void The_run_from_holtburgs_lifestone_to_the_redoubts_entry_front_plans_and_the_seek_reaches_the_portal()
    {
        var ground = GroundCatalog.Get("holtburg-redoubt");
        var entry = ground.Entry!;
        var route = Assert.Single(TravelRoutes.Default.RoutesFor(ground.Key));
        Assert.Equal(("on-foot-to-the-entry", "holtburg", 0), (route.Key, route.Start, route.Steps.Count));
        Assert.Equal((entry.Wcid, entry.Guid, entry.Origin), (route.Run!.EntryWcid, route.Run.EntryGuid, route.Run.EntryOrigin));

        // the route's metres are the straight line on global x/y from the start lifestone's row to the entry's row
        var start = TravelRoutes.Default.StartLifestone;
        var line = Vector2.Distance(
            new Vector2(((start.Cell >> 24) * 192f) + start.Origin.X, (((start.Cell >> 16) & 0xFF) * 192f) + start.Origin.Y),
            new Vector2(((entry.Cell >> 24) * 192f) + entry.Origin.X, (((entry.Cell >> 16) & 0xFF) * 192f) + entry.Origin.Y));
        Assert.Equal(route.Run.Metres, line, 0.1);

        var home = new CellFrame(ground.Home.LifestoneCell, new AcFrame(ground.Home.LifestonePoint, Quaternion.Identity));
        using var rig = Rig(home);
        var at = rig.RealRoom(home);
        var run = Walk(rig, at, rig.PointGoal(entry.FrontCell, entry.Front));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"redoubt-entry run metres={run.Metres:0.0} (straight line {line:0.0}) pass={(run.Ok ? "yes" : "no")} {run.Why}"));
        Assert.True(run.Ok, run.Why);
        Assert.InRange(run.Metres, line - 10, line * 1.5); // a walk on the land: about the line, the front point 3 m short of the portal

        var seek = Walk(rig, rig.RealRoom(run.End), rig.ObjectGoal(entry.Guid, PortalCell(entry.Cell, entry.Origin), entry.Origin, DenTravelStation.PortalRangeMetres));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"redoubt-entry seek metres={seek.Metres:0.0} pass={(seek.Ok ? "yes" : "no")} {seek.Why}"));
        Assert.True(seek.Ok, seek.Why);
    }
}
