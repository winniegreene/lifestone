using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Travel;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// M6 7 Z on the real dats (Exhaustive tier, skips without them): the island run against the danger map. Below level 15 the climber's only way to the Burrow and the Lodge is the run from the Refuge's arrival, and a night's crossings at level 10 went 7 through, 6 dead, all to tuskers
/// near the straight line. Each case walks the body through the headless streamer as <see cref="IslandRunDatTests"/> does, the straight way (the plain run's goal) and by the travel file's <c>via</c> points in turn, out and back, and reads the body's track against every
/// cell of <see cref="TravelRoutes.DangerCells"/>: the cells whose disc it enters (<see cref="TravelDangerTable.ClearMetres"/>: awareness, scatter, bodies) and the cells it comes within the awareness alone of (the awareness plus the bodies, a creature standing on its generator).
/// </summary>
/// <remarks>
/// The numbers go to the test log (metres, seconds, cells before and after, the cells by name). The world rows are the travel file's own, checked by <c>TravelRoutesSqlTests.Every_danger_table_is_what_the_pinned_world_SQL_holds</c>.
/// The fronts are the entry fronts of <see cref="IslandRunDatTests"/>. Water stays what it was: these runs cross none. The back run ends at the Refuge's arrival, 13.5 m from the Refuge's portal 42852, so it reads the way home's side of the island as well.
/// </remarks>
[Trait("Tier", "Exhaustive")]
public sealed class IslandDangerRunDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const double Fps = 60.0;

    private const uint RefugeCell = 0xF6820033;

    /// <summary>The seconds the via legs' stops may add over the extra metres' own time (a leg's end and the next leg's plan).</summary>
    private const double MostExtraSeconds = 30.0;

    /// <summary>A slow body's metres a second, for the extra metres' own time (a level-10 climber crosses at about 4.7).</summary>
    private const double SlowMetresASecond = 4.0;

    private static readonly Vector3 RefugeArrival = new(145.7f, 49.9f, 58.0f);

    private sealed record Case(string Name, string Ground, uint FrontBlock, Vector3 Front, int MostNoticed, int MostInDisc);

    private static readonly Case[] Cases =
    [
        // The Burrow's front stands 22.6 m from the generator 0xFA81:0,7: no walk to it keeps out of that one.
        new("burrow", "tusker-burrow", 0xF9810000, new(179.24f, 186.73f, 12.5f), 1, 1),
        new("lodge", "tusker-lodge", 0xFA7F0000, new(10.54f, 120.14f, 45.2f), 0, 1), // out 0 and 0; back, one disc's edge, 33 m from 0xF881:0,1
    ];

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var c in Cases)
        {
            data.Add(c.Name);
        }

        return data;
    }

    private DatSet Dats => fixture.Dats;

    [RequiresDatsTheory]
    [MemberData(nameof(Names))]
    public void The_via_run_arrives_out_and_back_and_keeps_off_the_encounter_cells(string name)
    {
        var c = Cases.Single(x => x.Name == name);
        var run = TravelRoutes.Default.RoutesFor(c.Ground).Single(r => r.Key == "by-refuge").Run!;
        var via = run.Via!.Points;
        var refuge = new CellFrame(RefugeCell, new AcFrame(RefugeArrival, Quaternion.Identity));
        var front = new NavPoint(c.FrontBlock, c.Front);
        var refugePoint = new NavPoint(RefugeCell & 0xFFFF0000u, RefugeArrival);
        var points = via.Select(v => new NavPoint(v.Cell & 0xFFFF0000u, v.Origin)).ToArray();

        var before = Walk(name + " straight", refuge, [front]);
        var after = Walk(name + " via", refuge, [.. points, front]);
        var backBefore = Walk(name + " straight back", FrameOf(front), [refugePoint]);
        var backAfter = Walk(name + " via back", FrameOf(front), [.. points.Reverse(), refugePoint]);

        foreach (var w in new[] { before, after, backBefore, backAfter })
        {
            output.WriteLine(w.Line());
        }

        foreach (var v in via)
        {
            Assert.Equal(CellIds.LandCellAt(LandblockCoord.FromId(v.Cell), v.Origin.X, v.Origin.Y)!.Value, v.Cell);
        }

        foreach (var w in new[] { after, backAfter })
        {
            Assert.Equal(0, w.Rungs);
            Assert.Equal(0, w.WallFrames);
            Assert.True(w.Arrived, w.Failure);
            Assert.InRange(w.Noticed.Count, 0, c.MostNoticed);
            Assert.InRange(w.InDisc.Count, 0, c.MostInDisc);
        }

        Assert.True(after.Noticed.Count < before.Noticed.Count || after.InDisc.Count < before.InDisc.Count, "the via run keeps off more cells than the straight run");
        Assert.InRange(after.Seconds, 0, before.Seconds + MostExtraSeconds + (Math.Max(0, after.Metres - before.Metres) / SlowMetresASecond));
    }

    private static CellFrame FrameOf(NavPoint p) =>
        new(CellIds.LandCellAt(LandblockCoord.FromId(p.LandblockId), p.Local.X, p.Local.Y)!.Value, new AcFrame(p.Local, Quaternion.Identity));

    /// <summary>A walk in legs from <paramref name="start"/>: each goal a point goal, as a <c>TravelPurpose.Cell</c> run's, the next leg planned where the last one ended, on one streamer and one body.</summary>
    private Track Walk(string name, CellFrame start, IReadOnlyList<NavPoint> goals)
    {
        var world = new PhysicsWorld();
        var map = new NavMap();
        using var streamer = new HeadlessStreamer(Dats, null, world, map, NullLogSink.Instance);
        streamer.EnsureNow(start.CellId);
        var h = new ActionHarness(offline: true);
        var now = 0.0;
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, () => now, "danger-" + name);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, sink: audit);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        Assert.True(walker.Place(start), $"placed at 0x{start.CellId:X8}");
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var track = new Track(name);
        var nextFollow = 0.0;
        for (var g = 0; g < goals.Count; g++)
        {
            var goal = NavGoal.ToPoint(goals[g], null);
            using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), new OutdoorPathSource(new PathPlanner(map)), confirmer, audit, new NavRunnerOptions { Name = $"danger-{name}-{g}", ConfirmBudgetMilliseconds = 2 });
            streamer.FollowRoute(runner, walker.Position);
            runner.Start(goal, now);
            var limit = now + 120.0 + (Vector2.Distance(NavMap.FromCellFrame(walker.Position).Global, goal.Point.Global) / 2.5);
            while (!runner.IsDone && now < limit)
            {
                now += 1.0 / Fps;
                if (now >= nextFollow)
                {
                    nextFollow = now + 0.5;
                    streamer.FollowRoute(runner, walker.Position);
                }

                streamer.Pump();
                walker.Controller.Tick(now);
                runner.Step(now);
                h.Tick(1.0 / Fps);
                track.Sample(NavMap.FromCellFrame(walker.Position).Global, walker.Physics.CollidingWithEnvironment && runner.RouteMetres > 1f);
                if (streamer.Busy)
                {
                    Thread.Sleep(1);
                }
            }

            track.Rungs += runner.Rungs;
            track.Arrived = runner.State == NavRunState.Arrived;
            track.Failure = $"leg {g}: {runner.State}: {runner.FailureReason}";
            if (!track.Arrived)
            {
                break;
            }
        }

        track.Seconds = now;
        return track;
    }

    /// <summary>A body's track against the danger map.</summary>
    private sealed class Track(string name)
    {
        private static readonly (string Name, Vector2 At, float Disc, float Noticed)[] Cells =
            [.. TravelRoutes.Default.DangerCells.SelectMany(t => t.Cells.Select(c => (string.Create(CultureInfo.InvariantCulture, $"0x{c.Landblock:X4}:{c.CellX},{c.CellY}"), c.Global, t.ClearMetres, NavCreatures.AggroRadius((float)t.AwarenessMetres))))];

        private const int ReadEveryFrames = 6;

        private Vector2? _last;
        private int _frame;

        public double Metres { get; private set; }

        public double Seconds { get; set; }

        public int Rungs { get; set; }

        public int WallFrames { get; private set; }

        public bool Arrived { get; set; }

        public string? Failure { get; set; }

        /// <summary>The cells whose disc the track entered, each with the nearest metres.</summary>
        public Dictionary<string, float> InDisc { get; } = [];

        /// <summary>The cells the track came within the awareness of.</summary>
        public Dictionary<string, float> Noticed { get; } = [];

        public void Sample(Vector2 at, bool wall)
        {
            WallFrames += wall ? 1 : 0;
            if (_last is { } last)
            {
                var step = Vector2.Distance(last, at);
                Metres += step < 20f ? step : 0f;
            }

            _last = at;
            if (_frame++ % ReadEveryFrames != 0)
            {
                return; // about 1 m a read at a run's speed
            }

            foreach (var c in Cells)
            {
                var d = Vector2.Distance(at, c.At);
                if (d <= c.Disc)
                {
                    InDisc[c.Name] = MathF.Min(InDisc.GetValueOrDefault(c.Name, float.MaxValue), d);
                }

                if (d <= c.Noticed)
                {
                    Noticed[c.Name] = MathF.Min(Noticed.GetValueOrDefault(c.Name, float.MaxValue), d);
                }
            }
        }

        public string Line() => string.Create(
            CultureInfo.InvariantCulture,
            $"{name}: arrived={Arrived} walked={Metres:F0} m sim_s={Seconds:F0} rungs={Rungs} wall_frames={WallFrames} noticed={Noticed.Count} [{string.Join(' ', Noticed.Select(k => $"{k.Key}@{k.Value:F0}"))}] in_disc={InDisc.Count} [{string.Join(' ', InDisc.Select(k => $"{k.Key}@{k.Value:F0}"))}] {(Arrived ? string.Empty : Failure)}");
    }
}
