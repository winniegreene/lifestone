using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// A dungeon is run through, not walked (M5 item 2 fix F6, the owner's D47 rule that he always moves with purpose; live round 2 watched the BSD Mage walk 52 m of the den's first leg with no creature near).
/// The cause, reproduced here: <see cref="NavRunner"/> walked within <c>WalkNearDoorwayMetres</c> of every route vertex marked Doorway, and on a room graph every cell-to-cell portal is a Doorway vertex, so a
/// leg across a few rooms was a walk. The walk exists for the thin doorway cell outdoors (a running step of 0.26 m or more can cross it between two physics steps: docs/physics.md section 15); indoors the body
/// runs through a doorway it can run straight through and walks near one only where the route turns sharply there or the physics' sweep stops short of it. Per ground over every dungeon profile (D46): the
/// walked share of each leg's metres, and the leg's speed, on the ground's own legs from its arrival (Exhaustive tier: the dats; skipped without them).
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class IndoorRunThroughDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>The Run skill whose rate is the fighters' buffed one, 2.47 (as <c>IndoorSteeringDatTests</c>).</summary>
    private const int FighterRunSkill = 230;

    private const double Fps = 60.0;

    /// <summary>At most this share of any leg's metres is walked (a corner turned sharply at a doorway, the start, the stop within <see cref="NavRunner.WalkNearStopMetres"/> of the goal).</summary>
    public const float MaxWalkedShare = 0.3f;

    /// <summary>At most this share of all the ground's legs' metres is walked.</summary>
    public const float MaxWalkedShareOfAllLegs = 0.15f;

    /// <summary>The most legs a ground's walk is measured over (its first tour cells, from the arrival).</summary>
    private const int MaxLegs = 12;

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var len2 = ab.LengthSquared();
        var t = len2 <= 0f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        return Vector2.Distance(p, a + (ab * t));
    }

    public static TheoryData<string> Dungeons() => new(GroundCatalog.All.Where(g => g.Kind == GroundKind.Dungeon).Select(g => g.Key));

    [RequiresDatsTheory]
    [MemberData(nameof(Dungeons))]
    public void A_dungeons_legs_are_run_not_walked_through_their_doorways(string key)
    {
        var ground = GroundCatalog.Get(key);
        var dats = fixture.Dats;
        var landblock = Assert.Single(ground.Landblocks);
        var coord = new LandblockCoord((byte)(landblock >> 8), (byte)(landblock & 0xFF));
        var geometry = new PhysicsGeometryLibrary(dats);
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(dats, geometry).Build(coord));
        var graph = GroundRooms.GraphOf(LandblockCellSet.Load(new CellStructureLibrary(dats), coord), ground);
        var h = new ActionHarness(offline: true);
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, null, "run-through");
        var walker = OfflineWalker.Create(dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(FighterRunSkill), sink: audit);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        Assert.Equal(Data.Physics.Objects.SetPositionError.OK, walker.Physics.SetPositionSimple(ground.Arrival.Frame, sliding: true));
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var source = new RoomPathSource(graph);
        var now = 0.0;
        var (metres, walked, seconds) = (0f, 0f, 0.0);
        var failures = new List<string>();
        var legs = 0;
        var worstMiss = 0f;
        var totalWall = 0;
        foreach (var cellId in ground.Tour.Where(c => graph.RoomFrame(c) is not null).Distinct().Take(MaxLegs))
        {
            var goal = NavGoal.ToPoint(new NavPoint(cellId & 0xFFFF0000u, graph.RoomFrame(cellId)!.Value.Origin), cellId);
            using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), source, confirmer, audit, new NavRunnerOptions { Name = "leg-" + (cellId & 0xFFFF).ToString("X4", CultureInfo.InvariantCulture) });
            runner.Start(goal, now);
            var end = now + 120;
            var wallFrames = 0;
            var track = new List<Vector2> { NavMap.FromCellFrame(walker.Position).Global };
            while (!runner.IsDone && now < end)
            {
                now += 1.0 / Fps;
                walker.Controller.Tick(now);
                runner.Step(now);
                h.Tick(1.0 / Fps);
                track.Add(NavMap.FromCellFrame(walker.Position).Global);
                wallFrames += walker.Physics.CollidingWithEnvironment && runner.RouteMetres > 1f ? 1 : 0; // the placement's latch (31 frames at the first leg's start, before and after F6) is not a wall hit
            }

            // How near the body's track came to each doorway vertex of the leg: running through a doorway must still go through it (the doorway is 2 m wide, the body 0.96 m).
            var miss = runner.Path!.Waypoints.Where(w => w.Doorway).Select(w => w.Point.Global).Select(d => Enumerable.Range(1, track.Count - 1).Min(i => SegmentDistance(d, track[i - 1], track[i]))).DefaultIfEmpty(0f).Max();
            totalWall += wallFrames;
            worstMiss = Math.Max(worstMiss, miss);

            var share = runner.RouteMetres > 0f ? runner.WalkedMetres / runner.RouteMetres : 0f;
            var speed = runner.RunSeconds > 0 ? runner.RouteMetres / runner.RunSeconds : 0.0;
            var line = string.Create(CultureInfo.InvariantCulture, $"0x{cellId & 0xFFFF:X4} {runner.State} walked_m={runner.WalkedMetres:0.0} of {runner.RouteMetres:0.0} m (share {share:0.00}) speed={speed:0.00} m/s turns={runner.Turns} doorway_miss_m={miss:0.00} wall_frames={wallFrames} {runner.VerdictFields()}");
            output.WriteLine(line + (runner.FailureReason is { } why ? " -- " + why : string.Empty));
            legs++;
            metres += runner.RouteMetres;
            walked += runner.WalkedMetres;
            seconds += runner.RunSeconds;
            if (runner.State != NavRunState.Arrived || runner.Rungs != 0 || walker.Position.CellId != cellId || (runner.RouteMetres > 8f && share > MaxWalkedShare))
            {
                failures.Add(line);
            }
        }

        var all = metres > 0f ? walked / metres : 0f;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{key}: {legs} legs, {walked:0.0} of {metres:0.0} m walked (share {all:0.000}) in {seconds:0.0} s, {(seconds > 0 ? metres / seconds : 0):0.00} m/s, the track passed every doorway vertex within {worstMiss:0.00} m, {totalWall} frames of wall contact once under way"));
        Assert.True(legs > 0, key + ": no leg measured");
        Assert.Empty(failures);
        Assert.True(totalWall == 0, key + ": the body was in contact with the environment for " + totalWall + " frames of its legs (running through a doorway must not clip its jambs)");
        Assert.True(all <= MaxWalkedShareOfAllLegs, string.Create(CultureInfo.InvariantCulture, $"{key}: {all:0.000} of the legs' metres were walked"));
    }
}
