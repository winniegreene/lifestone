using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Movement;
using Lifestone.Client.Tests.Movement.Wayfinding;
using Lifestone.Client.Tests.Session;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// <c>m5-fight-walk-recover</c> and the walks on the runner (M5 item 2 g5): a failed leg (the runner blocked, a stall, over its time) is abandoned and the walk goes on to the next waypoint of its <see cref="IGroundTour"/>,
/// three failed legs in a row end it named, an arrival resets the count; the headless journey host's den walk stops at a no-go cell, named; and a den leg over the dats arrives on the <see cref="NavRunner"/> with its turns
/// a metre logged. The scripted walk is shared with the death's composed fight (<c>DeathScenarioTests.WalkRecover.cs</c>).
/// </summary>
public sealed class GroundWalkRecoverTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const int FighterRunSkill = 230;
    private const double Fps = 60.0;

    private static readonly Vector2 RingStart = new(93f, 99f);

    [Fact]
    public void Three_failed_legs_in_a_row_end_the_walk_named_and_an_arrival_resets_the_count()
    {
        var recovery = new LegRecovery(NullLogSink.Instance);

        Assert.Null(recovery.Failed(1, 0x01050001, "a stall"));
        Assert.Null(recovery.Failed(2, 0x01050002, "a stall"));
        recovery.Arrived();
        Assert.Equal(0, recovery.FailedInARow);
        Assert.Null(recovery.Failed(4, 0x01050004, "a stall"));
        Assert.Null(recovery.Failed(5, 0x01050005, "the runner is blocked"));
        var ended = recovery.Failed(6, 0x01050006, "over its time");

        Assert.Equal(LegRecovery.MaxFailedLegsInARow, recovery.FailedInARow);
        Assert.Equal(4, recovery.Recovered);
        Assert.NotNull(ended);
        Assert.StartsWith("3 legs failed in a row (leg 4 to 0x01050004: a stall; leg 5 to 0x01050005: the runner is blocked; leg 6 to 0x01050006: over its time)", ended, StringComparison.Ordinal);
    }

    [Fact]
    public void The_profile_tour_goes_round_in_the_profiles_order_and_never_offers_the_players_cell()
    {
        var ground = DenGround.Profile;
        var tour = new ProfileTour(ground);

        Assert.Equal(ground.Tour.Count, tour.Waypoints);
        var first = tour.Next(0, 0.0);
        Assert.Equal(ground.Tour[0], first);
        Assert.Equal(ground.Tour[1], tour.Next(ground.Tour[0], 1.0)); // the cell the player stands in is never offered back
        Assert.Equal(ground.Tour[2], tour.Next(0, 2.0));
        ((IGroundTour)tour).LegFailed(ground.Tour[2], 3.0); // the default does nothing
        for (var i = 0; i < ground.Tour.Count; i++)
        {
            Assert.Equal(ground.Tour[(3 + i) % ground.Tour.Count], tour.Next(0, 4.0 + i)); // and round again
        }
    }

    [Fact]
    public void A_scripted_walk_whose_leg_fails_goes_on_to_the_next_waypoint()
    {
        var walk = new ScriptedRecoveringWalk(leg => leg == 1);
        Assert.True(walk.BeginLeg(null!, 0.0, out var first));

        var statuses = Enumerable.Range(0, 12).Select(_ => walk.Tick(null!, 0.0)).ToList();

        Assert.Equal("scripted leg 1", first);
        Assert.Equal(2, walk.Legs); // the failed leg, then the recovery leg
        Assert.Equal(1, walk.Recovery.Recovered);
        Assert.DoesNotContain(FightWalkStatus.Failed, statuses);
        Assert.Contains(FightWalkStatus.Arrived, statuses);
        Assert.Null(walk.Failure);
        Assert.Equal(0, walk.Recovery.FailedInARow); // the arrival reset the count
    }

    [Fact]
    public void A_scripted_walk_whose_legs_all_fail_stops_at_the_third_naming_each()
    {
        var walk = new ScriptedRecoveringWalk(_ => true);
        Assert.True(walk.BeginLeg(null!, 0.0, out _));

        var last = FightWalkStatus.Walking;
        for (var i = 0; i < 20 && last == FightWalkStatus.Walking; i++)
        {
            last = walk.Tick(null!, 0.0);
        }

        Assert.Equal(FightWalkStatus.Failed, last);
        Assert.Equal(3, walk.Legs);
        Assert.StartsWith("3 legs failed in a row (leg 1 to 0x0105", walk.Failure, StringComparison.Ordinal);
        Assert.Contains("leg 3 to 0x0105", walk.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stalled_runner_is_blocked_and_the_walk_goes_on_to_the_next_waypoint_and_arrives()
    {
        var rig = new IndoorRig(IndoorRoomKit.Ring(), 0f, IndoorRoomKit.RingSouthWest, RingStart, heading: 0f);
        var tour = new ListTour(IndoorRoomKit.RingNorthEast, IndoorRoomKit.RingSouthEast);
        var legs = new RunnerLegs(RingGround(legSeconds: 600), tour, rig.Graph, NullLogSink.Instance, RingCentre);
        rig.Body.Frozen = true; // the body does not move: the runner's ladder runs out and the runner is Blocked
        Assert.True(legs.Begin(rig.Runner, rig.Indoor.Position, rig.Body.Now, rig.Body.Now, out var first), legs.Failure);
        output.WriteLine(first);

        var status = FightWalkStatus.Walking;
        for (var i = 0; i < 6000 && legs.Recovered == 0 && status == FightWalkStatus.Walking; i++)
        {
            status = Frame(rig, legs);
        }

        Assert.Equal(1, legs.Recovered);
        Assert.Equal(FightWalkStatus.Walking, status);
        Assert.Equal([IndoorRoomKit.RingNorthEast], tour.Failed); // the tour was told which waypoint's leg failed
        Assert.Equal(2, legs.Legs);
        Assert.Equal(IndoorRoomKit.RingSouthEast, tour.Offered[^1]); // and the next waypoint came from it

        rig.Body.Frozen = false;
        for (var i = 0; i < 6000 && status == FightWalkStatus.Walking; i++)
        {
            status = Frame(rig, legs);
        }

        output.WriteLine($"{status} legs={legs.Legs} recovered={legs.Recovered} {rig.Runner.VerdictFields()}");
        Assert.Equal(FightWalkStatus.Arrived, status);
        Assert.Equal(IndoorRoomKit.RingSouthEast, rig.Indoor.CellId);
        Assert.Equal(0, legs.FailedInARow);
        Assert.Equal(1, legs.Recovered);
    }

    [Fact]
    public void Legs_over_their_time_fail_three_in_a_row_and_the_walk_names_each()
    {
        var rig = new IndoorRig(IndoorRoomKit.Ring(), 0f, IndoorRoomKit.RingSouthWest, RingStart, heading: 0f);
        var legs = new RunnerLegs(RingGround(legSeconds: 0.2), new ListTour(IndoorRoomKit.RingNorthEast, IndoorRoomKit.RingSouthEast, IndoorRoomKit.RingNorthWest), rig.Graph, NullLogSink.Instance, RingCentre);
        Assert.True(legs.Begin(rig.Runner, rig.Indoor.Position, rig.Body.Now, rig.Body.Now, out _), legs.Failure);

        var status = FightWalkStatus.Walking;
        for (var i = 0; i < 600 && status == FightWalkStatus.Walking; i++)
        {
            status = Frame(rig, legs);
        }

        Assert.Equal(FightWalkStatus.Failed, status);
        Assert.Equal(3, legs.Legs);
        Assert.Equal(2, legs.Recovered);
        Assert.StartsWith("3 legs failed in a row (", legs.Failure, StringComparison.Ordinal);
        Assert.Equal(3, legs.Failure!.Split("the leg did not finish within 0.2 s").Length - 1);
        output.WriteLine(legs.Failure);
    }

    [Fact]
    public void A_walk_with_no_waypoint_a_walk_joins_fails_naming_the_cell()
    {
        var rig = new IndoorRig(IndoorRoomKit.Ring(), 0f, IndoorRoomKit.RingSouthWest, RingStart, heading: 0f);
        var legs = new RunnerLegs(RingGround(legSeconds: 60), new ListTour(0x40409999u), rig.Graph, NullLogSink.Instance, RingCentre);

        Assert.False(legs.Begin(rig.Runner, rig.Indoor.Position, rig.Body.Now, rig.Body.Now, out _));
        Assert.Contains($"no walk joins the player's cell 0x{IndoorRoomKit.RingSouthWest:X8}", legs.Failure, StringComparison.Ordinal);
    }

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void A_den_leg_of_the_walk_arrives_on_the_runner_with_its_turns_a_metre_logged()
    {
        var dats = fixture.Dats;
        var den = DenGround.Profile;
        var coord = GroundRooms.CoordOf(den);
        var geometry = new PhysicsGeometryLibrary(dats);
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(dats, geometry).Build(coord));
        var graph = GroundRooms.GraphOf(LandblockCellSet.Load(new CellStructureLibrary(dats), coord), den);
        var h = new ActionHarness(offline: true);
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, null, "den-walk");
        var walker = OfflineWalker.Create(dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(FighterRunSkill), sink: audit);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        Assert.Equal(Data.Physics.Objects.SetPositionError.OK, walker.Physics.SetPositionSimple(den.Arrival.Frame, sliding: true));
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), new RoomPathSource(graph), confirmer, audit, new NavRunnerOptions { Name = "ground-walk" });
        var log = new RecordingLogSink();
        var legs = new RunnerLegs(den, new ProfileTour(den), graph, log);
        var now = 0.0;
        var (turns, metres) = (0, 0f);

        for (var leg = 1; leg <= 6; leg++)
        {
            var from = walker.Position;
            Assert.True(legs.Begin(runner, in from, now, now, out var description), legs.Failure);
            var status = FightWalkStatus.Walking;
            var end = now + den.Walk.LegSeconds;
            while (status == FightWalkStatus.Walking && now < end)
            {
                now += 1.0 / Fps;
                walker.Controller.Tick(now);
                status = legs.Step(walker.Position, now, now);
                h.Tick(1.0 / Fps);
            }

            var rate = runner.RouteMetres > 0f ? runner.Turns / runner.RouteMetres : 0f;
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{description}: {status} turns_per_m={rate:0.000} {runner.VerdictFields()}"));
            Assert.Equal(FightWalkStatus.Arrived, status);
            Assert.Equal(0, runner.Rungs);
            Assert.True(runner.Turns <= runner.RouteMetres * IndoorSteeringTests.MaxTurnsPerMetre, $"{description}: {runner.Turns} turns over {runner.RouteMetres:0.0} m");
            turns += runner.Turns;
            metres += runner.RouteMetres;
        }

        Assert.Equal(0, legs.Recovered);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{legs.Legs} den legs on the runner: {turns} turns over {metres:0.0} m, {turns / metres:0.000} a metre"));
    }

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void The_headless_journey_hosts_den_walk_stops_at_a_no_go_cell_naming_it()
    {
        var log = new RecordingLogSink();
        using var host = new HeadlessJourneyRunnerHost(fixture.Dats, log);
        var probe = new NoGoProbe(host, fixture.Dats);
        using var run = new ProbeRun(probe);
        run.Finish(() => host.Body.Streamer is { Busy: true }, maxSeconds: 400);
        output.WriteLine($"{probe.Stage}: {probe.Failure}; leg {probe.Leg}; the walk said: {probe.WalkFailure}");

        Assert.Null(probe.Failure);
        Assert.Equal("done", probe.Stage);
        Assert.Equal(GroundWalk.NoGoReason(DenGround.Profile, NoGoCell), probe.WalkFailure);
    }

    /// <summary>The exit's room of the den (no-go).</summary>
    private const uint NoGoCell = 0x01050341;

    private static HuntingGround RingGround(double legSeconds) => DenGround.Profile with { Walk = new WalkModel(0f, 1f, 0.0, legSeconds) };

    /// <summary>The middle of a ring room (the kit's rooms have no floor polygons, so the room graph has no frame for them).</summary>
    private static CellFrame? RingCentre(uint cell)
    {
        var at = cell switch
        {
            IndoorRoomKit.RingSouthWest => new Vector3(96f, 96f, 0f),
            IndoorRoomKit.RingSouthEast => new Vector3(106f, 96f, 0f),
            IndoorRoomKit.RingNorthWest => new Vector3(96f, 106f, 0f),
            IndoorRoomKit.RingNorthEast => new Vector3(106f, 106f, 0f),
            _ => (Vector3?)null,
        };
        return at is { } o ? new CellFrame(cell, new AcFrame(o, Quaternion.Identity)) : null;
    }

    private static FightWalkStatus Frame(IndoorRig rig, RunnerLegs legs)
    {
        rig.Body.Advance(S5Rig.Dt);
        rig.Indoor.Sync();
        var status = legs.Step(rig.Indoor.Position, rig.Body.Now, rig.Body.Now);
        rig.H.Tick(S5Rig.Dt);
        return status;
    }

    /// <summary>A tour over a list of cells, recording what it offered and which legs failed.</summary>
    private sealed class ListTour(params uint[] cells) : IGroundTour
    {
        private int _next;

        public List<uint> Offered { get; } = [];

        public List<uint> Failed { get; } = [];

        public int Waypoints => cells.Length;

        public uint? Next(uint from, double now)
        {
            for (var i = 0; i < cells.Length; i++)
            {
                var cell = cells[_next++ % cells.Length];
                if (cell != from)
                {
                    Offered.Add(cell);
                    return cell;
                }
            }

            return null;
        }

        public void LegFailed(uint cell, double now) => Failed.Add(cell);
    }

    /// <summary>Logs in, has the server place the player at Holtburg and then in the den, begins a den leg on the host's walk, puts the body in the exit's no-go room and ticks the walk.</summary>
    private sealed class NoGoProbe(HeadlessJourneyRunnerHost host, DatSet dats) : IScenario
    {
        private readonly LoginScenario _login = new("Amy") { StayInWorld = true };
        private IFightWalk? _walk;
        private double _since;

        public string Name => "no-go-probe";

        public string Stage { get; private set; } = "login";

        public string? Failure { get; private set; }

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public string Leg { get; private set; } = string.Empty;

        public string? WalkFailure { get; private set; }

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            host.Tick(session, now);
            switch (Stage)
            {
                case "login":
                    var login = _login.Step(session, now);
                    if (login == ScenarioStatus.Failed)
                    {
                        return Fail("login: " + _login.Failure);
                    }

                    if (login == ScenarioStatus.Succeeded)
                    {
                        session.Tools.Place(Destination("lifestone", 0xA9B40019u, new Vector3(84f, 7.7f, 94f)), ActionSource.Host);
                        return Go("holtburg", session);
                    }

                    return ScenarioStatus.Running;
                case "holtburg":
                    if (host.Body.Placed && host.Body.Walker!.Position.CellId >> 16 == 0xA9B4)
                    {
                        session.Tools.Place(Destination("den", DenGround.Arrival.CellId, DenGround.Arrival.Origin), ActionSource.Host);
                        return Go("recalled", session);
                    }

                    return session.Clock.Now - _since > 120 ? Fail("holtburg: the body was not placed: " + host.PlaceBlocker) : ScenarioStatus.Running;
                case "recalled":
                    if (host.Body.Placed && host.Body.Walker!.Position.CellId >> 16 == 0x0105)
                    {
                        _walk = host.FightWalk(null!, DenGround.Profile);
                        _walk.Prepare(session, now);
                        if (!_walk.BeginLeg(session, now, out var description))
                        {
                            return Fail("den: the walk began no leg: " + _walk.Failure);
                        }

                        Leg = description;
                        return Go("leg", session);
                    }

                    return session.Clock.Now - _since > 120 ? Fail("den: the body did not follow the player: " + host.PlaceBlocker) : ScenarioStatus.Running;
                case "leg":
                    if (_walk!.Tick(session, now) == FightWalkStatus.Failed)
                    {
                        return Fail("the leg failed before the no-go cell: " + _walk.Failure);
                    }

                    if (session.Clock.Now - _since > 1.0)
                    {
                        var room = LandblockCellSet.Load(new CellStructureLibrary(dats), GroundRooms.CoordOf(DenGround.Profile)).GetCell(NoGoCell)!;
                        host.Body.World!.SetTime(session.Clock.Now);
                        var put = host.Body.Walker!.Physics.SetPositionSimple(new CellFrame(room.Id, new AcFrame(room.Frame.Origin, Quaternion.Identity)), sliding: true);
                        if (put != Data.Physics.Objects.SetPositionError.OK)
                        {
                            return Fail("the body could not be put in the no-go room: " + put);
                        }

                        return Go("nogo", session);
                    }

                    return ScenarioStatus.Running;
                case "nogo":
                    if (_walk!.Tick(session, now) == FightWalkStatus.Failed)
                    {
                        WalkFailure = _walk.Failure;
                        _walk.Dispose();
                        return Go("done", session);
                    }

                    return session.Clock.Now - _since > 5 ? Fail("the walk did not stop in the no-go cell") : ScenarioStatus.Running;
                default:
                    return Failure is null ? ScenarioStatus.Succeeded : ScenarioStatus.Failed;
            }
        }

        private static TeleportDestination Destination(string name, uint cell, Vector3 at) =>
            new(name, [], new WorldLocation(name, new CellPosition(cell, at), HasHeight: true, HeadingDegrees: 0), "no-go test");

        private ScenarioStatus Go(string stage, IGameSessionView session)
        {
            Stage = stage;
            _since = session.Clock.Now;
            return stage == "done" ? ScenarioStatus.Succeeded : ScenarioStatus.Running;
        }

        private ScenarioStatus Fail(string why)
        {
            Failure = why;
            Stage = "failed";
            return ScenarioStatus.Failed;
        }
    }

    /// <summary>The headless runner over the scripted ACE (the journey tests' run, copied: its class is private to theirs).</summary>
    private sealed class ProbeRun : IDisposable
    {
        public ProbeRun(IScenario scenario)
        {
            Time = new ManualTimeProvider();
            Runner = HeadlessRunner.StartWith(
                new HeadlessRunOptions { Session = new SessionOptions { Account = "test" }, QuitAfterSeconds = 3000 },
                scenario,
                SessionTestData.SecretsV2(),
                SessionTestData.PidsReady(),
                () => SessionTestData.EndOfRetail,
                () => HeadlessTestWorld.Assets,
                _ => new HeadlessTestWorld(),
                Time,
                new RecordingLogSink(),
                new MemoryStream(),
                new SocketFactorySpy().Open,
                (_, sink, tap) => new ScriptedAceSession(sink, tap, Time) { AcceptsMovement = true });
        }

        public ManualTimeProvider Time { get; }

        public HeadlessRunner Runner { get; }

        public void Finish(Func<bool> busy, double maxSeconds)
        {
            for (var t = 0.0; t < maxSeconds && !Runner.IsDone; t += 1.0 / 60.0)
            {
                Time.Advance(TimeSpan.FromSeconds(1.0 / 60.0));
                Runner.Step(Runner.Session.Clock.Now);
                if (busy())
                {
                    Thread.Sleep(1); // the workers run in real time, the clock in simulated time
                }
            }
        }

        public void Dispose() => Runner.Dispose();
    }
}

/// <summary>
/// A scripted <see cref="IFightWalk"/> that recovers failed legs as the real walks do, with their <see cref="LegRecovery"/>: leg <c>n</c> (counting the recovery legs) fails on its second tick when <c>fails(n)</c> is true, else
/// it arrives on its fourth. Used by this file's tests and by the death's composed fight (<c>DeathScenarioTests.WalkRecover.cs</c>); <see cref="Recover"/> false is the walk before g5 (a failed leg ends the walk).
/// </summary>
internal sealed class ScriptedRecoveringWalk(Func<int, bool> fails, bool recover = true, int ticksPerLeg = 4) : IFightWalk
{
    private int _ticks;
    private bool _walking;

    public LegRecovery Recovery { get; } = new(NullLogSink.Instance);

    public bool Recover { get; } = recover;

    public int Legs { get; private set; }

    public string? Failure { get; private set; }

    public void Prepare(IGameSessionView session, double now)
    {
    }

    public bool BeginLeg(IGameSessionView session, double now, out string description)
    {
        Start();
        description = $"scripted leg {Legs}";
        return true;
    }

    public FightWalkStatus Tick(IGameSessionView session, double now)
    {
        if (!_walking)
        {
            return FightWalkStatus.Idle;
        }

        _ticks++;
        if (fails(Legs) && _ticks >= 2)
        {
            _walking = false;
            if (!Recover)
            {
                Failure = $"leg {Legs}: scripted stall";
                return FightWalkStatus.Failed;
            }

            if (Recovery.Failed(Legs, 0x01050000u + (uint)Legs, "scripted stall") is { } ended)
            {
                Failure = ended;
                return FightWalkStatus.Failed;
            }

            Start();
            return FightWalkStatus.Walking;
        }

        if (_ticks >= ticksPerLeg)
        {
            _walking = false;
            Recovery.Arrived();
            return FightWalkStatus.Arrived;
        }

        return FightWalkStatus.Walking;
    }

    public void Abandon() => _walking = false;

    public void Dispose()
    {
    }

    private void Start()
    {
        Legs++;
        _ticks = 0;
        _walking = true;
    }
}
