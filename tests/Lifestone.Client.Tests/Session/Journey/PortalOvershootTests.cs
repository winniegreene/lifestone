using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 14 (portal overshoot; the live log of 2026-11-13, ladder1 step3): the hop's check after its walks, over the rung rig with a body of the host's own (<see cref="RungRig.ContextWithBody"/>), scripted as the live walks were.
/// <list type="bullet">
/// <item>19:20:58 UTC, <c>hub-barracks</c>: the walk to the approach-side front (78.8, -42.8) ended Arrived with her body at the point; the server's last report of her was (79.5, -37.2), a second of her run behind and on the far side of the portal, and
/// 14 ms after the walk ended the server's teleport came (its sweep of her reports crossed the portal). The hop failed <c>arrival-off-point</c> on the stale report and the fallback cast a recall into the portal space.</item>
/// <item>19:33:35 UTC, <c>network-to-refuge</c>: the same stale report, (80, -167.6) for a point at (79.4, -173.3), and no teleport: the body stood at the point (the next walk's runner read it at (78.9, -173.7)).</item>
/// </list>
/// </summary>
public sealed class PortalOvershootTests
{
    private static readonly TravelRoutes Routes = TravelRoutes.Default;

    private static readonly (uint Cell, Vector3 At) HubDrop = (0x8A020212, new Vector3(58.6391f, -89.9231f, 6.005f));

    private static readonly (uint Cell, Vector3 At) NetworkDrop = (0x00070133, new Vector3(60f, -70f, 0.005f));

    private static HuntingGround Barracks => GroundCatalog.Get("tusker-barracks");

    private static string Hex(uint guid) => guid.ToString("X8", CultureInfo.InvariantCulture);

    private static (RungRig Rig, RungPortal Portal, PortalHopLeg Leg) Hop(string key, (uint Cell, Vector3 At) start, int level = 51)
    {
        var t = new RungRig(level, at: start);
        t.Portals(Routes, key);
        var portal = RungPortal.Of(Routes.Edge(key), Routes);
        return (t, portal, new PortalHopLeg(t.ContextWithBody(Barracks), portal, "the test's rung change"));
    }

    /// <summary>
    /// The live Barracks case: the front point lies 3 m short of the portal on the far side from the rooms' way in, she arrives at it at her run's speed, the server's report of her is 5.6 m behind, and 14 ms after the walk the server's teleport
    /// takes her into the hop's own destination. The check after reads her body (at the point), waits for the server's verdict, sees the teleport, and the hop is taken (<c>walked-in</c>): no Use, no seek, no failure, no fallback.
    /// </summary>
    [Fact]
    public void The_live_Barracks_walk_that_ends_in_its_portal_is_the_hop_taken()
    {
        var (t, portal, leg) = Hop("hub-barracks", HubDrop);
        var edge = Routes.Edge("hub-barracks");
        t.Travel.EchoAt = r => r.Destination.Name == "hub-barracks-front" ? (r.Destination.Location.Position.CellId, new Vector3(79.5f, -37.2f, -12.05f)) : null;
        t.Travel.TeleportAfterArrival = r => r.Destination.Name == "hub-barracks-front" ? (edge.DestCell, edge.DestOrigin, 0.014) : null;

        var (_, status) = t.RunHop(leg);

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(12)));
        Assert.Null(leg.FailureName);
        Assert.True(leg.Outcome.Taken);
        var front = Assert.Single(t.Travel.Requests, r => r.Destination.Name == "hub-barracks-front");
        Assert.Equal(78.8f, front.Destination.Location.Position.Local.X, 1); // the approach side, 3 m short of the portal, as live
        Assert.Equal(-42.8f, front.Destination.Location.Position.Local.Y, 1);
        Assert.Equal(portal.Name, front.KeepOff!.Own!.Value.Name); // the front walk keeps off its own portal (the prevention's list)
        Assert.Contains(t.Lines("walk check after: at the point"), l => l.Contains("her body; the server's last report of her is cell 0x8A02011E (79.5, -37.2), 5.6 m from her body", StringComparison.Ordinal));
        Assert.Single(t.Lines("hop hub-barracks: check after the walk to the front: the server's last position is 5.6 m from her body; waiting up to 2.4 s"));
        Assert.Single(t.Lines("hop hub-barracks: walked-in: a teleport began"));
        Assert.Single(t.Lines("hop hub-barracks: walked-in: Facility Hub: Tusker Barracks took the player as the walk ended (taken=touch)"));
        Assert.Single(t.Lines("hop:hub-barracks: check after: present in cell 0xF07E0040"));
        Assert.DoesNotContain(t.Events, e => e.StartsWith("use:", StringComparison.Ordinal));
        Assert.DoesNotContain(t.Travel.Requests, r => r.Purpose == TravelPurpose.Seek);
        Assert.DoesNotContain(t.R.H.Log.Lines, l => l.Contains("arrival-off-point", StringComparison.Ordinal));
        Assert.Equal(0xF07Eu, t.Landblock);
    }

    /// <summary>
    /// The live Refuge case: the walk ends at its point with the server's report of her 5.7 m behind and no teleport. The check after reads her body and passes (both positions named), the wait for the server's verdict ends at its bound, and the
    /// hop goes on: the seek (which keeps only the other portals: it goes inside the portal's reach on purpose) and the Use, which takes her.
    /// </summary>
    [Fact]
    public void A_walk_the_server_reports_a_second_behind_passes_its_check_after_and_the_hop_goes_on()
    {
        var (t, portal, leg) = Hop("network-to-refuge", NetworkDrop);
        t.Travel.EchoAt = r => r.Destination.Name == "network-to-refuge-front" ? (r.Destination.Location.Position.CellId, new Vector3(80f, -167.6f, -0.05f)) : null;

        var (_, status) = t.RunHop(leg);

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(12)));
        Assert.False(leg.Outcome.Taken);
        Assert.True(t.Lines("walk check after: at the point").Any(l => l.Contains("(80, -167.6), 5.8 m from her body", StringComparison.Ordinal)), string.Join(" || ", t.R.H.Log.Lines));
        Assert.Single(t.Lines("hop network-to-refuge: check after the walk to the front: no teleport within 2.4 s"));
        Assert.Single(t.Lines("hop network-to-refuge: check after the seek: the server's last position agrees with her body"));
        var seek = Assert.Single(t.Travel.Requests, r => r.Purpose == TravelPurpose.Seek);
        Assert.Null(seek.KeepOff!.Own);
        Assert.Contains("use:" + Hex(Routes.Edge("network-to-refuge").Guid), t.Events);
        Assert.Equal(0xF682u, t.Landblock);
    }

    /// <summary>A teleport that follows the walk into any landblock but the hop's destination is no hop: the hop fails by name, <c>hop:&lt;key&gt;:moved-elsewhere:&lt;landblock&gt;</c>, and sends no Use.</summary>
    [Fact]
    public void A_teleport_after_the_walk_into_another_landblock_fails_the_hop_by_name()
    {
        var (t, _, leg) = Hop("hub-barracks", HubDrop);
        t.Travel.EchoAt = r => r.Destination.Name == "hub-barracks-front" ? (r.Destination.Location.Position.CellId, new Vector3(79.5f, -37.2f, -12.05f)) : null;
        t.Travel.TeleportAfterArrival = r => r.Destination.Name == "hub-barracks-front" ? (0xA9B40019u, new Vector3(84f, 7.7f, 94f), 0.014) : null;

        var (_, status) = t.RunHop(leg);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.StartsWith("hop:hub-barracks:moved-elsewhere:A9B4", leg.FailureName, StringComparison.Ordinal);
        Assert.Single(t.Lines("hop hub-barracks: walked-in: a teleport began"));
        Assert.DoesNotContain(t.Events, e => e.StartsWith("use:", StringComparison.Ordinal));
    }

    /// <summary>
    /// A rung change's recall (live 19:20:59 UTC: the route fallback's Lifestone Recall was cast 1 s into a portal space and refused four times, 0x001D): from the hub at level 20 the plan is home first, and a teleport begins as she goes to the
    /// magic mode for the recall. The check before the cast waits for the arrival (one line each side); the recall is cast only once she is present. The fallback's recall is the same leg.
    /// </summary>
    [Fact]
    public void A_rung_change_s_recall_waits_for_the_arrival_before_it_casts()
    {
        var burrow = GroundCatalog.Get("tusker-burrow");
        var t = new RungRig(20, at: HubDrop);
        var book = t.R.H.State.Self.Spellbook;
        book.IsKnown = true;
        book.Learn(RecallHome.LifestoneRecallSpell);
        t.R.CombatModeIs(Lifestone.Client.Session.Contracts.CombatModes.NonCombat); // she goes to the magic mode for the recall
        t.Portals(Routes, "holtburg-to-network");
        t.RecallTo[RecallHome.LifestoneRecallSpell] = (0xA9B40019, new Vector3(84f, 7.7f, 94f));
        var teleported = false;
        t.OnModeChange = _ =>
        {
            if (!teleported)
            {
                teleported = true;
                t.Teleport(0xF9810038, new Vector3(155.6f, 188.2f, 13.04f), unhideAfter: 4.0); // a portal space under the recall
            }
        };

        t.RunStation(new RungChangeStation(), t.Context(burrow), maxSeconds: 120);

        Assert.True(teleported, string.Join(" || ", t.R.H.Log.Lines.Where(l => l.Contains("journey", StringComparison.Ordinal))));
        Assert.Contains(t.Lines("rung_change plan: "), l => l.Contains("legs=[lifestone-recall:lifestone-recall,", StringComparison.Ordinal));
        var lines = t.R.H.Log.Lines.ToList();
        var wait = lines.FindIndex(l => l.Contains("recall:lifestone: check before: the character is in ", StringComparison.Ordinal));
        var present = lines.FindIndex(l => l.Contains("recall:lifestone: check after the wait: travel Present", StringComparison.Ordinal));
        var cast = lines.FindIndex(l => l.Contains("(1635)", StringComparison.Ordinal) && l.Contains("try 1", StringComparison.Ordinal));
        Assert.True(wait >= 0 && present > wait && cast > present, string.Join(" | ", lines.TakeLast(20)));
        Assert.True(t.Events.IndexOf("teleport:F9810038") < t.Events.IndexOf("cast:" + RecallHome.LifestoneRecallSpell));
    }
}
