using Lifestone.Client.Diagnostics;
using Lifestone.Client.Options;
using Lifestone.Client.Play;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// The Godot journey host's testable parts (M4 P5b J5): the journey's option parsing (the kind, the plan, the account rule) and the frame observer's names over a scripted
/// factory and a scripted session (no Godot): <c>station-&lt;name&gt;</c> at each station's end, J7's event frames only when asked, the in-station names and the per-station frame times.
/// </summary>
public sealed class J5JourneyFramesTests
{
    private static JourneyPlan J5Plan(params StationSpec[] stations) => new("j5", "BSD Mage", "test", StationWhere.Belt, 1, stations);

    private static StationSpec J5Spec(string name, StationNeeds? needs = null, string kind = StationKinds.HarmHeal) => new(name, kind, needs ?? StationNeeds.None, 30);

    // ------------------------------------------------------------------------------------------------ the options

    [Theory]
    [InlineData("mage", "test")]
    [InlineData("melee", "test-melee")]
    [InlineData("archer", "test-archer")]
    public void The_journey_check_parses_the_kind_and_the_plan_on_its_own_account(string journey, string account)
    {
        var options = PlayOptions.Parse(["--login-check=journey", $"--journey={journey}", $"--account={account}"]);

        Assert.Empty(options.Errors);
        Assert.Equal(LoginCheckKind.Journey, options.LoginCheck);
        Assert.Equal(journey, options.Journey);
    }

    [Fact]
    public void The_mage_journey_runs_on_the_test_account_when_none_is_given_and_the_others_need_theirs()
    {
        Assert.Empty(PlayOptions.Parse(["--login-check=journey", "--journey=mage"]).Errors);

        var melee = PlayOptions.Parse(["--login-check=journey", "--journey=melee"]);
        Assert.Contains(melee.Errors, e => e.Contains("--account=test-melee", StringComparison.Ordinal));
        Assert.Null(melee.Journey);
    }

    [Theory]
    [InlineData("mage", "test-melee")]
    [InlineData("melee", "test")]
    [InlineData("archer", "test-melee")]
    [InlineData("mage", "play")]
    public void A_journey_on_the_wrong_account_is_refused(string journey, string account)
    {
        var options = PlayOptions.Parse(["--login-check=journey", $"--journey={journey}", $"--account={account}"]);

        Assert.Single(options.Errors);
        Assert.Contains("refused", options.Errors[0], StringComparison.Ordinal);
        Assert.Null(options.Journey);
    }

    [Fact]
    public void A_missing_or_unknown_plan_and_a_plan_without_the_check_are_errors()
    {
        Assert.Contains("needs --journey", PlayOptions.Parse(["--login-check=journey"]).Errors.Single(), StringComparison.Ordinal);
        Assert.Contains("is not a plan", PlayOptions.Parse(["--login-check=journey", "--journey=healer"]).Errors.Single(), StringComparison.Ordinal);
        Assert.Contains("only --login-check=journey", PlayOptions.Parse(["--login-check=hunt", "--journey=mage"]).Errors.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_journey_kind_is_not_part_of_all_and_moves_the_screenshot_to_its_own_stem()
    {
        Assert.NotEqual(LoginCheckKind.All, PlayOptions.Parse(["--login-check=journey", "--journey=mage"]).LoginCheck);
        Assert.Equal(LoginCheckKind.All, PlayOptions.Parse(["--login-check"]).LoginCheck);
        Assert.Equal(LoginCheckKind.All, PlayOptions.Parse(["--login-check=all"]).LoginCheck);

        var shot = PlayOptions.Parse(["--login-check=journey", "--journey=mage", "--play-screenshot=cache/x/shot.png"]);
        Assert.Equal("cache/x/shot.png", shot.BuffShotPath);
        Assert.Null(shot.ScreenshotPath);
        Assert.Equal(JourneyShots.Off, shot.JourneyShots);
    }

    [Fact]
    public void The_journey_option_is_registered_for_the_main_scene()
    {
        Assert.True(OptionCatalog.Registry.IsKnown("--journey"));
        Assert.True(OptionCatalog.Registry.AppliesTo("--journey", OptionScenes.Main));
    }

    // ------------------------------------------------------------------------------------------------ the frame names

    [Fact]
    public void The_frame_names_follow_the_station_and_the_legs_slot_names()
    {
        Assert.Equal("station-harm-heal", JourneyFrameNames.StationEnd("harm-heal"));
        Assert.Equal("hunt-c1-combat", JourneyFrameNames.InStation("hunt", "c1-combat"));
        Assert.Equal("cache/x/shot-station-hunt.png", JourneyFrameNames.PathFor("cache/x/shot.png", "station-hunt").Replace('\\', '/'));
        Assert.Equal(["c1-combat", "c1-powerbar", "meter", "corpse", "heal", "potion", "vendor"], JourneyFrameNames.SlotsOf(StationKinds.Hunt));
        Assert.Equal(["vendor"], JourneyFrameNames.SlotsOf(StationKinds.Vendor));
        Assert.Equal(["bolt", "hit", "kill"], JourneyFrameNames.SlotsOf(StationKinds.DenFight));
        Assert.Equal(DeathShotPlan.Names, JourneyFrameNames.SlotsOf(StationKinds.Death));
        Assert.Equal([JourneyFrameNames.BuffMidCast], JourneyFrameNames.SlotsOf(StationKinds.Buff));
        Assert.Empty(JourneyFrameNames.SlotsOf(StationKinds.Bind));
    }

    [Fact]
    public void A_journey_over_a_scripted_factory_asks_for_one_frame_at_each_stations_end_in_order()
    {
        var t = new J1JourneyRig();
        var observer = new JourneyFrameObserver("cache/x/shot.png", null, NullLogSink.Instance);
        var plan = J5Plan(
            J5Spec("build-verify", new(StationWhere.Holtburg), StationKinds.BuildVerify),
            J5Spec("hunt", new(StationWhere.Belt), StationKinds.Hunt),
            J5Spec("harm-heal"),
            J5Spec("vendor", new(StationWhere.Holtburg, Peace: true), StationKinds.Vendor));
        var journey = t.Journey(plan, new J1Factory(), t.Context(observer: observer));

        Assert.Equal(ScenarioStatus.Succeeded, t.Run(journey));

        Assert.Equal(["station-build-verify", "station-hunt", "station-harm-heal", "station-vendor"], observer.Requested);
        var paths = new List<string>();
        while (observer.TryDequeue(out var request))
        {
            Assert.Equal(JourneyFrameKind.StationEnd, request.Kind);
            paths.Add(request.Path.Replace('\\', '/'));
        }

        Assert.Equal(["cache/x/shot-station-build-verify.png", "cache/x/shot-station-hunt.png", "cache/x/shot-station-harm-heal.png", "cache/x/shot-station-vendor.png"], paths);
        Assert.Null(observer.Current);
    }

    [Fact]
    public void A_failed_station_still_gets_its_end_frame_and_no_frames_are_asked_for_without_a_path()
    {
        var t = new J1JourneyRig();
        var asked = new JourneyFrameObserver("cache/x/shot.png", null, NullLogSink.Instance);
        var quiet = new JourneyFrameObserver(null, null, NullLogSink.Instance);
        var failing = new J1Factory(new() { ["s2"] = new(Succeed: false, Failure: "boom", Fields: "kills=0") });
        var journey = t.Journey(J5Plan(J5Spec("s1"), J5Spec("s2")), failing, t.Context(observer: asked));
        Assert.Equal(ScenarioStatus.Failed, t.Run(journey));
        Assert.Equal(["station-s1", "station-s2"], asked.Requested);

        var t2 = new J1JourneyRig();
        var journey2 = t2.Journey(J5Plan(J5Spec("s1")), new J1Factory(), t2.Context(observer: quiet));
        Assert.Equal(ScenarioStatus.Succeeded, t2.Run(journey2));
        Assert.Empty(quiet.Requested);
        Assert.Equal(0, quiet.Pending);
        Assert.False(quiet.FramesAsked);
    }

    [Fact]
    public void Event_frames_are_asked_for_only_when_the_shots_seam_is_on_and_are_capped_per_station()
    {
        var off = new JourneyFrameObserver("cache/x/shot.png", JourneyShots.Off, NullLogSink.Instance);
        off.StationStarting("hunt", 0);
        off.StationEvent("hunt", "frost-arrow", 1);
        Assert.Empty(off.Requested);

        var on = new JourneyFrameObserver("cache/x/shot.png", new JourneyShots(Enabled: true, MaxPerStation: 2, EventGapSeconds: 0), NullLogSink.Instance);
        on.StationStarting("hunt", 0);
        on.StationEvent("hunt", "frost-arrow", 1);
        on.StationEvent("hunt", "frost-arrow", 2);
        on.StationEvent("hunt", "frost-arrow", 3); // over the cap
        on.StationEnded("hunt", true, 4);
        on.StationStarting("den-fight", 5);
        on.StationEvent("den-fight", "ring-of-fire", 6);

        Assert.Equal(["hunt-frost-arrow", "hunt-frost-arrow", "station-hunt", "den-fight-ring-of-fire"], on.Requested);
        Assert.True(on.TryDequeue(out var first));
        Assert.Equal(JourneyFrameKind.Event, first.Kind);
        Assert.Equal("hunt", first.Station);
    }

    [Fact]
    public void The_frame_times_are_kept_per_station_and_a_frame_after_a_saved_shot_is_left_out()
    {
        var observer = new JourneyFrameObserver("cache/x/shot.png", null, NullLogSink.Instance);
        observer.Frame(0.5, skip: false); // between stations: counted nowhere
        observer.StationStarting("hunt", 0);
        observer.Frame(0.016, skip: false);
        observer.Frame(0.080, skip: true);
        observer.Frame(0.030, skip: false);
        observer.StationEnded("hunt", true, 1);
        observer.StationStarting("vendor", 2);

        Assert.Equal("frame_max_ms=30.0 frames=2", observer.FrameFields("hunt"));
        Assert.Equal("frame_max_ms=n/a frames=0", observer.FrameFields("vendor"));
        Assert.Equal("frame_max_ms=n/a frames=0", observer.FrameFields("never-ran"));
    }

    [Fact]
    public void The_journey_shows_its_running_station_to_the_host_while_the_observer_is_called()
    {
        var t = new J1JourneyRig();
        var observer = new SeeingObserver();
        var journey = t.Journey(J5Plan(J5Spec("s1")), new J1Factory(), t.Context(observer: observer));
        observer.Journey = journey;

        Assert.Equal(ScenarioStatus.Succeeded, t.Run(journey));

        Assert.Equal(["s1"], observer.RunSeenAtStart);
        Assert.Null(journey.CurrentRun);
    }

    private sealed class SeeingObserver : IJourneyObserver
    {
        public JourneyScenario? Journey { get; set; }

        public List<string> RunSeenAtStart { get; } = [];

        public void StationStarting(string station, double now)
        {
            if (Journey?.CurrentRun is { } run)
            {
                RunSeenAtStart.Add(run.Name);
            }
        }

        public void StationEnded(string station, bool ok, double now)
        {
        }

        public void StationEvent(string station, string name, double now)
        {
        }
    }

    [Fact]
    public void A_tile_feeder_over_the_players_own_streamer_pumps_tiles_only_and_leaves_the_streamer_and_the_pool_alive()
    {
        var world = new Lifestone.Data.Physics.World.PhysicsWorld();
        using var workers = new Lifestone.Client.Scheduling.WorkerPool(1);
        using var collision = new Lifestone.Client.Movement.CollisionStreamer(world, (_, _) => throw new InvalidOperationException("J5: nothing is built in this test"), workers, NullLogSink.Instance);
        var map = new Lifestone.Client.Movement.Wayfinding.NavMap();
        var feeder = new Lifestone.Client.Movement.Wayfinding.HeadlessStreamer(world, collision, workers, map, NullLogSink.Instance);

        feeder.AdoptAttached(Lifestone.Data.Terrain.LandblockCoord.FromId(0xA9B40019), 2); // nothing is attached: no job
        feeder.PumpTiles();
        Assert.Equal(0, feeder.TilesPending);
        Assert.Empty(map.AttachedLandblocks);

        feeder.Dispose();
        feeder.Dispose();
        collision.SetFocus(0xA9B40019); // the streamer is the player's: still usable after the feeder is gone
        Assert.Throws<ObjectDisposedException>(feeder.PumpTiles);
    }
}
