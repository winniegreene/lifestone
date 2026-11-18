using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// The predictive steering outdoors (<see cref="NavRunnerOptions.PredictiveOutdoors"/>; docs/wayfinding.md "Predictive steering"): the 345 m run from the Facility Hub's Barracks drop 0xF07E0040
/// (171.613, 188.834, 10.9546) to the front of Tusker Armory's portal in 0xEF7F000C (34.2122, 94.0914, 12.01), walked through the headless streamer by the explore-mode walker at 60 frames a second with the physics
/// confirming, once with each steering. Both arrive with no rung; the predictive steering presses fewer keys and reverses fewer turns, and takes no longer. Measured on 2026-11-16: the ordinary steering 42 key
/// changes, 10 reversals, 46.7 s; the predictive 28, 2, 46.5 s. Exhaustive tier: it reads the dats and skips without them.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class OutdoorPredictiveDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const double Fps = 60.0;

    [RequiresDatsFact]
    public void The_barracks_drop_to_the_armory_runs_with_fewer_keys_and_no_rung()
    {
        var ordinary = Walk(predictive: false);
        var predictive = Walk(predictive: true);
        Assert.Equal(NavRunState.Arrived, ordinary.State);
        Assert.Equal(NavRunState.Arrived, predictive.State);
        Assert.Equal(0, ordinary.Rungs);
        Assert.Equal(0, predictive.Rungs);
        Assert.True(predictive.Replans <= ordinary.Replans, $"replans {predictive.Replans} against {ordinary.Replans}");
        Assert.True(predictive.KeyChanges < ordinary.KeyChanges, $"keys {predictive.KeyChanges} against {ordinary.KeyChanges}");
        Assert.True(predictive.TurnReversals < ordinary.TurnReversals, $"reversals {predictive.TurnReversals} against {ordinary.TurnReversals}");
        Assert.True(predictive.RunSeconds <= ordinary.RunSeconds + 0.1, $"run {predictive.RunSeconds:F1} s against {ordinary.RunSeconds:F1} s");
    }

    private (NavRunState State, int Rungs, int Replans, int KeyChanges, int TurnReversals, double RunSeconds) Walk(bool predictive)
    {
        var world = new PhysicsWorld();
        var map = new NavMap();
        using var streamer = new HeadlessStreamer(fixture.Dats, null, world, map, NullLogSink.Instance);
        const uint StartCell = 0xF07E0040;
        streamer.EnsureNow(StartCell);
        var h = new ActionHarness(offline: true);
        var walker = OfflineWalker.Create(fixture.Dats, world, NullLogSink.Instance);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        Assert.True(walker.Place(new CellFrame(StartCell, new AcFrame(new Vector3(171.613f, 188.834f, 10.9546f), Quaternion.Identity))));
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), new OutdoorPathSource(new PathPlanner(map)), confirmer, null, new NavRunnerOptions { Name = "armory", ConfirmBudgetMilliseconds = 2, PredictiveOutdoors = predictive }, NullLogSink.Instance);
        var goal = NavGoal.ToPoint(new NavPoint(0xEF7F0000, new Vector3(34.2122f, 94.0914f, 12.01f)), 0xEF7F000C);
        var now = 0.0;
        var nextFollow = 0.0;
        runner.Start(goal, now);
        while (!runner.IsDone && now < 400.0)
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
            while (streamer.Busy)
            {
                Thread.Sleep(1);
                streamer.Pump();
            }
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{(predictive ? "predictive" : "ordinary")}: {runner.State} rungs={runner.Rungs} replans={runner.Replans} keys={runner.KeyChanges} {runner.SteeringFields()} run_s={runner.RunSeconds:F1} {runner.FailureReason}"));
        return (runner.State, runner.Rungs, runner.Replans, runner.KeyChanges, runner.TurnReversals, runner.RunSeconds);
    }
}
