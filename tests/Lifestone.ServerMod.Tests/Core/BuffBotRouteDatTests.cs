using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.ServerTools;
using Xunit.Abstractions;

namespace Lifestone.ServerMod.Tests.Core;

/// <summary>
/// D138: the bot's walk to Archmage Cindrue (<see cref="BuffBotRules.ShopRoute"/>) is the journeys' own leg planner's route on the retail dats (Exhaustive tier, as the journeys' other dat tests): from the bot's post
/// to her entry hall the planner finds a whole path, and the pinned points and the planned path lie on each other (each within <see cref="Tolerance"/> of the other's line), so no leg of the bot's walk cuts a corner
/// the planner goes round; the way back plans too. What this does not show: what ACE's creature move-to does on those legs (the lead's live trip, the <c>restock: at the shop</c> and <c>back (ok</c> lines).
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class BuffBotRouteDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>The metres a pinned point and the planned path may stand apart.</summary>
    private const float Tolerance = 0.3f;

    [RequiresDatsFact]
    public void The_bots_walk_to_Cindrue_is_the_planners_route_from_its_post_to_her_entry_hall_and_back()
    {
        var route = BuffBotRules.ShopRoute;
        var planner = new JourneyLegPlanner(fixture.Dats);
        var builder = new LandblockCollisionBuilder(fixture.Dats);
        var map = new NavMap();
        for (var x = 0xA8; x <= 0xAA; x++)
        {
            for (var y = 0xB3; y <= 0xB5; y++)
            {
                map.Attach(NavTileBuilder.Build(builder.Build(new LandblockCoord((byte)x, (byte)y))));
            }
        }

        var post = Frame(route[0]);
        var stop = route[^1];
        var there = planner.SourceFor(post.CellId, stop.Cell, map).Plan(post, NavGoal.ToPoint(new NavPoint(stop.Cell & 0xFFFF0000u, new Vector3(stop.X, stop.Y, stop.Z)), stop.Cell), new NavScars());
        output.WriteLine($"post -> hall: {there.Status} {there.Reason}, {there.Waypoints.Count} waypoints: {string.Join(" ", there.Waypoints.Select(w => $"{w.At.CellId:X8}({w.At.Origin.X:F2},{w.At.Origin.Y:F2})"))}");
        Assert.Equal(NavPathStatus.Found, there.Status);
        Assert.Equal(stop.Cell, there.Waypoints[^1].At.CellId);

        var planned = there.Waypoints.Select(w => new Vector2(w.At.Origin.X, w.At.Origin.Y)).ToList();
        var pinned = route.Select(p => new Vector2(p.X, p.Y)).ToList();
        foreach (var p in route)
        {
            Assert.True(Off(new Vector2(p.X, p.Y), planned) <= Tolerance, $"the pinned point 0x{p.Cell:X8} ({p.X}, {p.Y}) is {Off(new Vector2(p.X, p.Y), planned):F2} m off the planned path");
            Assert.Contains(there.Waypoints, w => w.At.CellId == p.Cell); // each pinned cell is one the path passes
        }

        foreach (var w in planned)
        {
            Assert.True(Off(w, pinned) <= Tolerance, $"the planned point ({w.X:F2}, {w.Y:F2}) is {Off(w, pinned):F2} m off the pinned walk");
        }

        var home = planner.SourceFor(stop.Cell, route[0].Cell, map).Plan(Frame(stop), NavGoal.ToPoint(new NavPoint(BuffBotWire.Cell & 0xFFFF0000u, new Vector3(BuffBotWire.X, BuffBotWire.Y, 94f)), BuffBotWire.Cell & 0xFFFF0000u), new NavScars());
        output.WriteLine($"hall -> post: {home.Status} {home.Reason}, {home.Waypoints.Count} waypoints");
        Assert.Equal(NavPathStatus.Found, home.Status);
    }

    private static CellFrame Frame(BuffBotWaypoint p) => new(p.Cell, new AcFrame(new Vector3(p.X, p.Y, p.Z), Quaternion.Identity));

    /// <summary>The metres from <paramref name="point"/> to the nearest point of the line through <paramref name="line"/>.</summary>
    private static float Off(Vector2 point, IReadOnlyList<Vector2> line)
    {
        var best = float.MaxValue;
        for (var i = 1; i < line.Count; i++)
        {
            var (a, b) = (line[i - 1], line[i]);
            var ab = b - a;
            var t = ab.LengthSquared() < 1e-9f ? 0f : Math.Clamp(Vector2.Dot(point - a, ab) / ab.LengthSquared(), 0f, 1f);
            best = Math.Min(best, Vector2.Distance(point, a + (t * ab)));
        }

        return best;
    }
}
