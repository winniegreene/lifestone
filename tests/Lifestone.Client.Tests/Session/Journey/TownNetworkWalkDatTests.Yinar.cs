using System.Globalization;
using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Animation;
using Lifestone.Data.Tests;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 14 (m6-14-yinar; live ladder61, 2026-11-17 14:13:44 to 14:14:16 UTC): the Atlan wand's step 22 hops Samsur to the Yinar Portal. The walk to the portal's front (the approach side, 3 m south of the portal, chosen at the Samsur drop because the
/// facing side lies past the portal) planned a horizon, ran 250 m north and replanned from 0x977C000E (27.44, 124.05, 6.34) for a creature on the route; that plan, with the goal now on the attached tiles, failed <c>goal-scarred</c>, and the plan without
/// the list met no portal: <c>no path: portal-keep-off:none</c>.
/// </summary>
public sealed partial class TownNetworkWalkDatTests
{
    /// <summary>
    /// The walk to the Yinar Portal's front, from the live replan's point, with the list the runner keeps (every other portal, and the own one by its touch and her stop), plans whole and is confirmed by the physics; and its last point is the front,
    /// outside the own portal's reach.
    /// </summary>
    [RequiresDatsFact]
    public void The_walk_to_the_yinar_portals_front_plans_from_the_live_replan_with_its_own_portal_kept_off()
    {
        var portal = RungPortal.Of(Routes.Edge("samsur-to-yinar"), Routes);
        var portalCell = PortalCell(portal.Cell, portal.Origin);
        var into = Routes.Edge("network-to-samsur");
        var drop = new CellFrame(into.DestCell, new AcFrame(into.DestOrigin, Quaternion.Identity));
        var (frontCell, front, basis) = TravelLeg.FirstFront(portal, portalCell, drop);
        if (basis == TravelLeg.FrontBasisFacing && TravelLeg.FacingPastPortal(portal, portalCell, drop))
        {
            (frontCell, front, basis) = TravelLeg.FrontOf(portal with { Facing = null }, portalCell, drop); // the hop's own choice at the drop (live: front=approach why=facing-past-portal)
        }

        Assert.Equal(TravelLeg.FrontBasisApproach, basis);
        Assert.Equal(0x977E0003u, frontCell);
        Assert.InRange(Vector2.Distance(new Vector2(front.X, front.Y), new Vector2(20.64f, 50.13f)), 0f, 0.05f); // the live goal

        var from = new CellFrame(0x977C000Eu, new AcFrame(new Vector3(27.44f, 124.05f, 6.34f), Quaternion.Identity));
        using var rig = Rig(from);
        var goal = rig.PointGoal(frontCell, front);
        var keep = portal.KeepOffForFront().Near(NavMap.FromCellFrame(from).Global, goal.Point.Global, 192f).WithOwnClearance(PortalKeepOff.OwnReach(NavRunner.StopMetres(goal.StopTolerance)));
        Assert.Equal(portal.Name, keep.Own!.Value.Name);
        var own = Vector2.Distance(goal.Point.Global, keep.Own!.Value.Global);
        Assert.True(own > keep.OwnClearance, string.Create(CultureInfo.InvariantCulture, $"the front stands {own:0.00} m from the portal, the reach is {keep.OwnClearance:0.00} m"));

        var walk = Walk(rig, from, goal, keep);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"walk to the yinar front from the live replan: metres={walk.Metres:0.0} pass={(walk.Ok ? "yes" : "no")} {walk.Why}"));
        Assert.True(walk.Ok, walk.Why);
        Assert.Equal(portalCell >> 16, walk.End.CellId >> 16);
        Assert.InRange(Vector2.Distance(new Vector2(walk.End.Origin.X, walk.End.Origin.Y), new Vector2(front.X, front.Y)), 0f, goal.StopTolerance + 0.01f);
    }

    /// <summary>
    /// The rest of the step after the Town Network, with the lists the live legs keep: from the Samsur drop to the Yinar Portal's front (the own portal and every other kept off), the seek to the portal (the others kept off), and from the Yinar
    /// drop the go to the Minor Smoldering Stone as the quest station asks it (the stone by its guid within the use range; the go keeps no list), each planned whole and confirmed by the physics.
    /// </summary>
    [RequiresDatsFact]
    public void The_atlan_wands_go_to_the_minor_smoldering_stone_plans_from_samsur_with_the_live_lists()
    {
        var portal = RungPortal.Of(Routes.Edge("samsur-to-yinar"), Routes);
        var portalCell = PortalCell(portal.Cell, portal.Origin);
        var into = Routes.Edge("network-to-samsur");
        var drop = new CellFrame(into.DestCell, new AcFrame(into.DestOrigin, Quaternion.Identity));
        var (frontCell, front, basis) = TravelLeg.FirstFront(portal, portalCell, drop);
        if (basis == TravelLeg.FrontBasisFacing && TravelLeg.FacingPastPortal(portal, portalCell, drop))
        {
            (frontCell, front, _) = TravelLeg.FrontOf(portal with { Facing = null }, portalCell, drop);
        }

        using (var rig = Rig(drop))
        {
            var goal = rig.PointGoal(frontCell, front);
            var keep = portal.KeepOffForFront().Near(NavMap.FromCellFrame(drop).Global, goal.Point.Global, 192f).WithOwnClearance(PortalKeepOff.OwnReach(NavRunner.StopMetres(goal.StopTolerance)));
            var toFront = Walk(rig, drop, goal, keep);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"quest-walk name=samsur-to-yinar-front metres={toFront.Metres:0.0} pass={(toFront.Ok ? "yes" : "no")} {toFront.Why}"));
            Assert.True(toFront.Ok, toFront.Why);

            var seek = Walk(rig, toFront.End, rig.ObjectGoal(portal.Guid, portalCell, portal.Origin, DenTravelStation.PortalRangeMetres), portal.KeepOffTheOthers());
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"quest-walk name=yinar-seek metres={seek.Metres:0.0} pass={(seek.Ok ? "yes" : "no")} {seek.Why}"));
            Assert.True(seek.Ok, seek.Why);
        }

        var go = QuestCatalog.Get("atlan-wand").Steps.Single(s => s.Kind == QuestStepKind.Go && s.Target?.Wcid == 6320);
        var stone = go.Place!;
        var edge = Routes.Edge("samsur-to-yinar");
        var yinar = new CellFrame(edge.DestCell, new AcFrame(edge.DestOrigin, Quaternion.Identity));
        using (var rig = Rig(yinar))
        {
            var walk = Walk(rig, yinar, rig.ObjectGoal(0x7A66C000u, stone.Cell, stone.Point!.Value, QuestStepLeg.UseRange)); // the stone's guid: the pinned world SQL's landblock_instance row (the quest file's source)
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"quest-walk name=yinar-drop-to-stone metres={walk.Metres:0.0} pass={(walk.Ok ? "yes" : "no")} {walk.Why}"));
            Assert.True(walk.Ok, walk.Why);
            Assert.Equal(stone.Cell, walk.End.CellId);
            Assert.True(Vector3.Distance(walk.End.Origin, stone.Point!.Value) <= QuestStepLeg.UseRange + 0.5, $"the walk ended at {walk.End}");
        }
    }
}
