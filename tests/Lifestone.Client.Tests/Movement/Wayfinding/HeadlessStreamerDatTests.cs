using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Data;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// P5a S6 on the real dats (Exhaustive tier, skips without them): the headless streamer builds the collision on its two workers and each landblock's tile after the attach,
/// the tiles equal what a build with every attached neighbour gives (a landblock that attaches later and casts shadows into a tile rebuilds it), the focus moving releases both,
/// and the explore-mode walker runs from Holtburg's lifestone to the belt cell 0xA5B00032 through the streamer instead of S5's hand-attached corridor, holding about a dozen
/// landblocks and never the whole straight line's.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class HeadlessStreamerDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint BeltCell = 0xA5B00032;

    /// <summary>The cell of the Archer's forest ground, about 2 km from the lifestone.</summary>
    private const uint ForestCell = 0x9FB50039;
    private const double Fps = 60.0;

    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void The_ring_is_attached_with_its_tiles_the_tiles_equal_a_build_with_every_neighbour_and_the_focus_moving_releases_both()
    {
        var world = new PhysicsWorld();
        var map = new NavMap();
        using var streamer = new HeadlessStreamer(Dats, null, world, map, NullLogSink.Instance);
        var lifestone = Lifestone();
        streamer.EnsureNow(lifestone.CellId);

        Assert.Equal(9, world.AttachedCount);
        Assert.Equal(9, map.AttachedLandblocks.Count);
        Assert.True(streamer.IsSettled(lifestone.CellId));
        Assert.False(streamer.Busy);
        output.WriteLine(streamer.StatsLine());

        // Every tile equals the build with the neighbours attached now (the rebuild when a later landblock casts shadows into an earlier tile).
        foreach (var id in map.AttachedLandblocks.ToArray())
        {
            var coord = LandblockCoord.FromId(id);
            var neighbours = new List<LandblockCollision>();
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if ((dx != 0 || dy != 0) && coord.Offset(dx, dy) is { } near && world.GetLandblock(near) is { } other)
                    {
                        neighbours.Add(other);
                    }
                }
            }

            var expected = NavTileBuilder.Build(world.GetLandblock(coord)!, NavBuildOptions.Default with { Neighbours = neighbours });
            Assert.True(map.TryGetTile(id, out var tile));
            Assert.True(expected.Flags.AsSpan().SequenceEqual(tile.Flags), $"tile {coord} differs from a build with every attached neighbour");
        }

        // The hysteresis: the first land cell of the east landblock keeps the focus where it is, the second takes it; a jump of two landblocks takes it at once.
        var holtburg = LandblockCoord.Holtburg;
        uint InEast(int cellX) => ((uint)(holtburg.X + 1) << 24) | ((uint)holtburg.Y << 16) | (uint)((cellX * 8) + 1);
        streamer.SetFocus(InEast(0));
        Assert.Equal(lifestone.CellId, streamer.Collision.FocusCellId);
        streamer.SetFocus(InEast(1));
        Assert.Equal(InEast(1), streamer.Collision.FocusCellId);
        streamer.SetFocus(lifestone.CellId); // the first cell of the west side of the border: held by the same rule from the other side
        Assert.Equal(lifestone.CellId, streamer.Collision.FocusCellId);
        streamer.EnsureNow(lifestone.CellId);
        Assert.Equal(9, world.AttachedCount);

        // The focus moves two landblocks east: the west column goes with its tiles, the east column comes.
        var east = lifestone with { CellId = ((uint)(LandblockCoord.Holtburg.X + 2) << 24) | ((uint)LandblockCoord.Holtburg.Y << 16) | 0x20 };
        streamer.EnsureNow(east.CellId);
        Assert.Equal(9, world.AttachedCount);
        Assert.Equal(9, map.AttachedLandblocks.Count);
        Assert.False(map.TryGetTile(((uint)LandblockCoord.Holtburg.X << 24) | ((uint)(LandblockCoord.Holtburg.Y - 1) << 16), out _));
        Assert.True(streamer.Releases >= 6);
        Assert.Equal(0, streamer.TileFailures);
    }

    [RequiresDatsTheory]
    [InlineData(BeltCell)]
    [InlineData(ForestCell)]
    public void The_walker_runs_from_the_lifestone_to_the_ground_through_the_streamer_holding_about_a_dozen_landblocks(uint groundCell)
    {
        var start = Lifestone();
        var world = new PhysicsWorld();
        var map = new NavMap();
        using var streamer = new HeadlessStreamer(Dats, null, world, map, NullLogSink.Instance);
        var heapBefore = GC.GetTotalMemory(true);
        streamer.EnsureNow(start.CellId);
        var heapRing = GC.GetTotalMemory(true) - heapBefore;

        var h = new ActionHarness(offline: true);
        var now = 0.0;
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, () => now, "ground-streamer-" + groundCell.ToString("X8"));
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, sink: audit);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        Assert.True(walker.Place(start));
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var low = (int)(groundCell & 0xFFFF) - 1;
        var beltLocal = new Vector3(((low / 8) * 24f) + 12f, ((low % 8) * 24f) + 12f, 0f);
        var goal = NavGoal.ToPoint(new NavPoint(groundCell & 0xFFFF0000u, beltLocal));
        var from = NavMap.FromCellFrame(start).Global;
        var line = Vector2.Distance(from, goal.Point.Global);
        using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), new OutdoorPathSource(new PathPlanner(map)), confirmer, audit, new NavRunnerOptions { Name = "ground-streamer", ConfirmBudgetMilliseconds = 2 });
        runner.Start(goal, now);

        var nextFollow = 0.0;
        var visited = new List<uint> { walker.Position.CellId };
        var wall = System.Diagnostics.Stopwatch.StartNew();
        while (!runner.IsDone && now < 900)
        {
            now += 1.0 / Fps;
            if (now >= nextFollow)
            {
                nextFollow = now + 0.5; // the scenario's corridor interval
                streamer.FollowRoute(runner, walker.Position);
            }

            streamer.Pump();
            walker.Controller.Tick(now);
            runner.Step(now);
            h.Tick(1.0 / Fps);
            if (walker.Position.CellId != visited[^1])
            {
                visited.Add(walker.Position.CellId);
            }

            if (streamer.Busy)
            {
                Thread.Sleep(1); // the workers run in real time, the walk in simulated time
            }
        }

        var peak = streamer.PeakAttached;
        output.WriteLine($"{runner.State} {runner.VerdictFields()} stuck={runner.Stuck}; straight line {line:F0} m; wall {wall.Elapsed.TotalSeconds:F1} s");
        output.WriteLine(streamer.StatsLine());
        var heapEnd = GC.GetTotalMemory(true) - heapBefore;
        output.WriteLine($"managed heap above the start: {heapRing / 1048576.0:F1} MB for the nine of the first ring (collision, tiles and the dat caches they filled), {heapEnd / 1048576.0:F1} MB at the end with {world.AttachedCount} landblocks and {map.AttachedLandblocks.Count} tiles attached");
        Assert.True(runner.State == NavRunState.Arrived, runner.FailureReason);
        Assert.Equal(0, runner.Rungs);
        Assert.Equal(groundCell, visited[^1]);
        Assert.True(runner.RouteMetres > line * 0.95f);
        Assert.InRange(peak, 9, 12); // the focus ring, the two ahead and the one behind: never the straight line's whole corridor
        Assert.InRange(streamer.PeakTiles, 9, 12);
        Assert.Equal(0, streamer.TileFailures);
        Assert.True(streamer.Attaches > peak, $"{streamer.Attaches} attaches over the run: the corridor moved on and released what it left");
        Assert.True(streamer.Releases > 0);

        // nav-natural-steering, the same arrival with fewer turns: P5a's pursuit pressed a turn key 468 times to the belt (1,029.5 m, 415 key changes a minute)
        // and 806 to the forest (1,864.0 m, 394 a minute); the straight run, which tracks the route near statics (NavRunner.TightClearanceNodes), 45 to 53 and
        // 59 to 76 (33 to 52 a minute) over ten runs each.
        Assert.InRange(runner.Turns, 1, groundCell == BeltCell ? 80 : 120);
        Assert.InRange(runner.KeyChangesPerMinute, 0, 70);
    }

    private static CellFrame Lifestone()
    {
        var poi = AcePointsOfInterest.All.Single(p => p.DisplayName == "Holtburg"); // beside the lifestone
        return new CellFrame(poi.CellId, new AcFrame(poi.Origin, Quaternion.Identity));
    }
}
