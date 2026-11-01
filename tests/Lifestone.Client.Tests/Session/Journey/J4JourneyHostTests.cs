using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Tests.Session.Grounds;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.World;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// The station catalog and the headless journey host (M4 P5b J4). The catalog builds every kind the three plans name (no dats: construction only); with the dats
/// (Exhaustive tier) each station's run starts and takes a first step over the J1 rig, the runner host walks the client's body to a point goal and an object goal
/// over <see cref="ScriptedAceSession"/> (placing the body again after the server's place), and the mage's journey over the stand-in session reaches its first
/// station without a place beyond the budget. What this cannot show (the lead's live runs prove it): ACE's answers to every station, the long routes.
/// </summary>
public sealed class J4JourneyHostTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private DatSet Dats => fixture.Dats;

    [Fact]
    public void The_catalog_builds_a_station_for_every_kind_of_the_three_plans()
    {
        foreach (var plan in JourneyPlans.All)
        {
            var factory = JourneyStations.Create(plan, [], null, "lifestone-test");
            foreach (var spec in plan.Stations)
            {
                var station = factory.Create(spec);
                Assert.Equal(spec.Name, station.Name);
                Assert.NotNull(station.Needs);
            }
        }

        // The 3-argument signature (H1) is the 4-argument one with no login name; a kind no plan names is built too, an unknown kind is a named refusal.
        var mage = JourneyStations.Create(JourneyPlans.Mage, [], null);
        Assert.Equal("vendor-facts", mage.Create(new StationSpec("vendor-facts", StationKinds.VendorFacts, StationNeeds.None, 10)).Name);
        var unknown = Assert.Throws<NotSupportedException>(() => mage.Create(new StationSpec("x", "teleport-home", StationNeeds.None, 10)));
        Assert.Contains("TODO(j4-station-kind)", unknown.Message, StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() => mage.Create(JourneyPlans.Melee.Stations.Single(s => s.Kind == StationKinds.Hunt))); // the mage has no fighter template
        Assert.Throws<ArgumentException>(() => JourneyStations.Create(JourneyPlans.Melee, ["--hunt-kills=many"], null));
    }

    [Fact]
    public void The_vendor_and_buff_judges_leave_out_only_what_this_host_cannot_meet()
    {
        var log = new RecordingLogSink();
        Assert.True(JourneyStations.OkWithout(["kills=0 (>= 1 expected)", "swings=0 (>= 1 expected for the melee hunt)"], JourneyStations.VendorUnjudged, "vendor", log));
        Assert.False(JourneyStations.OkWithout(["the hunt failed: travel:run:blocked", "kills=0 (>= 1 expected)"], JourneyStations.VendorUnjudged, "vendor", log));
        Assert.True(JourneyStations.OkWithout(["sounds_server=n/a: the run has no server-sound counter (D27 judges >= 1)"], JourneyStations.HostOnlyProblems, "buff", log));
        Assert.False(JourneyStations.OkWithout(["buffs=20/24 (every wanted line active expected)"], JourneyStations.HostOnlyProblems, "buff", log));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Every_station_of_the_three_plans_starts_and_steps_over_a_scripted_context()
    {
        var spells = SpellTableData.Load(Dats).Spells;
        foreach (var plan in JourneyPlans.All)
        {
            var factory = JourneyStations.Create(plan, [], Dats, "lifestone-test");
            foreach (var spec in plan.Stations)
            {
                var rig = new J1JourneyRig();
                var context = J4Context(rig, plan, spells, Dats);
                var run = factory.Create(spec).Start(context);
                var status = run.Step(rig.View, rig.Now);
                output.WriteLine($"{plan.Name}/{spec.Name}: {status} stage={run.Stage} {run.Failure}");
                (run as IDisposable)?.Dispose();
            }
        }
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void The_runner_host_walks_the_body_to_a_point_and_to_an_object_after_the_server_placed_the_player()
    {
        using var host = new HeadlessJourneyRunnerHost(Dats, new RecordingLogSink());
        ScriptedAceSession? server = null;
        var probe = new J4HostProbe(host, () => server!);
        using var run = new J4Run(probe, s =>
        {
            server = s;
            s.AcceptsMovement = true;
        });
        run.Finish(() => host.Body.Streamer is { Busy: true }, maxSeconds: 400);
        output.WriteLine($"{probe.Stage}: {probe.Failure}; point {probe.PointFields}; object {probe.ObjectFields}; placements {host.Placements}");

        Assert.Null(probe.Failure);
        Assert.Equal("done", probe.Stage);
        Assert.True(host.Placements >= 1, $"{host.Placements} placements: the body was placed where the server put the player after the place");
        Assert.True(probe.PointMetres > 5, probe.PointFields);
        Assert.True(probe.ObjectMetres > 5, probe.ObjectFields);
        Assert.Contains("rungs=0", probe.PointFields, StringComparison.Ordinal);
        Assert.Equal(1, server!.Talked.Count(t => t.StartsWith("@lifestone place", StringComparison.Ordinal)));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void The_runner_hosts_den_fight_walk_reports_its_recovered_legs_instead_of_n_a()
    {
        // F3: the host's GroundFightWalk keeps its own LegRecovery; the journey's den-fight line read legs_recovered=n/a until it reported the count.
        using var host = new HeadlessJourneyRunnerHost(Dats, new RecordingLogSink());
        var walk = host.FightWalk(null!, DenGround.Profile); // the context is not read
        Assert.Equal(0, walk.LegsRecovered); // none yet: a number, not n/a
        (walk as IDisposable)?.Dispose();
    }

    /// <summary>
    /// M4 P5b F1-1: the live mage's den fight failed <c>travel:run:not-placed</c> 120 s after the recall into the den, as the body waited for the eight
    /// landblocks round a dungeon's, which the streamer never requests for a closed EnvCell. Here the server places the player at Holtburg, then in the
    /// den's arrival cell 0x01050375: the body follows (logged once with the cell), and the den fight's walk runs a leg over the room graph to a spot.
    /// </summary>
    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void The_body_follows_the_player_into_the_den_after_a_teleport_and_the_den_walk_runs_a_leg_over_the_room_graph()
    {
        var log = new RecordingLogSink();
        using var host = new HeadlessJourneyRunnerHost(Dats, log);
        var probe = new F1DenProbe(host);
        using var run = new J4Run(probe, s => s.AcceptsMovement = true);
        run.Finish(() => host.Body.Streamer is { Busy: true }, maxSeconds: 400);
        output.WriteLine($"{probe.Stage}: {probe.Failure}; walk {probe.Walk}; placements {host.Placements}; blocker {host.PlaceBlocker}");
        foreach (var line in log.Lines.Where(l => l.Contains("journey-runner", StringComparison.Ordinal)))
        {
            output.WriteLine(line);
        }

        Assert.Null(probe.Failure);
        Assert.Equal("done", probe.Stage);
        Assert.InRange(host.Placements, 2, 3); // Holtburg and the den, after the login cell's own placement when its collision settles before the first place (load-dependent)
        Assert.Equal(1, log.Lines.Count(l => l.Contains("[journey-runner] the body follows the player to 0x01050375", StringComparison.Ordinal)));
        Assert.Equal(0x0105u, host.Body.Walker!.Position.CellId >> 16);
        Assert.NotEqual(DenGround.Arrival.CellId, host.Body.Walker.Position.CellId); // the leg left the arrival cell for a spot
        Assert.Contains("leg to 0x0105", probe.Walk, StringComparison.Ordinal);
    }

    /// <summary>
    /// F6 (the owner's D47 rule that he always moves with purpose): the headless host's den fight honours the tour's hold as <see cref="RunnerLegs"/> does (F5): a tour that names no purpose to walk makes no leg (the host runs
    /// none, the body stays in the arrival cell), logs the hold, and the walk arrives after <see cref="RunnerLegs.HoldRecheckSeconds"/> for the fight to ask again.
    /// </summary>
    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void The_hosts_den_fight_walk_makes_no_leg_while_the_tour_holds_and_arrives_after_the_recheck()
    {
        var log = new RecordingLogSink();
        using var host = new HeadlessJourneyRunnerHost(Dats, log);
        var tour = new FixedTour(0x01050383u, hold: true);
        var probe = new F1DenProbe(host, tour);
        using var run = new J4Run(probe, s => s.AcceptsMovement = true);
        run.Finish(() => host.Body.Streamer is { Busy: true }, maxSeconds: 400);
        output.WriteLine($"{probe.Stage}: {probe.Failure}; walk {probe.Walk}; legs {host.Legs}");

        Assert.Null(probe.Failure);
        Assert.Equal("done", probe.Stage);
        Assert.StartsWith("hold in 0x01050375 (no leg; test tour)", probe.Walk, StringComparison.Ordinal);
        Assert.Equal(0, host.Legs); // no leg was run
        Assert.Equal(DenGround.Arrival.CellId, host.Body.Walker!.Position.CellId); // and the player stood
        Assert.DoesNotContain(log.Lines, l => l.Contains("a leg on foot begins", StringComparison.Ordinal));
        Assert.Equal(0, tour.Arrivals);
    }

    /// <summary>F6: a leg the tour does walk tells the tour the runner's walking seconds (<see cref="IGroundTour.LegWalked"/>), as <see cref="RunnerLegs"/> does, not the time from the offer to the arrival.</summary>
    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void The_hosts_den_fight_walk_tells_the_tour_the_runners_walking_seconds()
    {
        using var host = new HeadlessJourneyRunnerHost(Dats, new RecordingLogSink());
        var tour = new FixedTour(0x01050383u, hold: false);
        var probe = new F1DenProbe(host, tour);
        using var run = new J4Run(probe, s => s.AcceptsMovement = true);
        run.Finish(() => host.Body.Streamer is { Busy: true }, maxSeconds: 400);
        output.WriteLine($"{probe.Stage}: {probe.Failure}; walk {probe.Walk}; legs {host.Legs}; walked {tour.WalkedSeconds}");

        Assert.Null(probe.Failure);
        Assert.Equal("done", probe.Stage);
        Assert.Equal(1, host.Legs);
        Assert.Equal(1, tour.Arrivals);
        Assert.True(tour.WalkedSeconds is > 1.0 and < 60.0, $"walking seconds {tour.WalkedSeconds}");
    }

    /// <summary>A tour of one cell, holding or walking, that records the arrivals and the walking seconds it is told.</summary>
    private sealed class FixedTour(uint cell, bool hold) : IGroundTour
    {
        public int Waypoints => 1;

        public TourPurpose Purpose => hold ? TourPurpose.Hold : TourPurpose.Seen;

        public string PurposeNote => "test tour";

        public int Arrivals { get; private set; }

        public double? WalkedSeconds { get; private set; }

        public uint? Next(uint from, double now) => cell;

        public void LegArrived(uint cell, double now) => Arrivals++;

        public void LegWalked(uint cell, double now, double walkSeconds)
        {
            WalkedSeconds = walkSeconds;
            Arrivals++;
        }
    }

    /// <summary>
    /// M4 P5b F1-2 and F1-4 through the host: the Brawler started the bind in Renald's back room 0xA9B40141 and failed a generic
    /// <c>travel:run:blocked: ... start-blocked</c>. Placed there by the server, a leg to the lifestone now fails by the name
    /// <c>travel:run:indoor-start-unreachable:0xA9B40141</c> (TODO(room-steps)), before a metre is run.
    /// </summary>
    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void A_leg_from_Renalds_back_room_fails_by_the_name_indoor_start_unreachable()
    {
        var log = new RecordingLogSink();
        using var host = new HeadlessJourneyRunnerHost(Dats, log);
        var probe = new F1LegProbe(host, 0xA9B40141, new System.Numerics.Vector3(140.85f, 18.11f, 94.01f), 0xA9B40019, new System.Numerics.Vector3(81.33f, 11.80f, 94f));
        using var run = new J4Run(probe, s => s.AcceptsMovement = true);
        run.Finish(() => host.Body.Streamer is { Busy: true }, maxSeconds: 300);
        output.WriteLine($"{probe.Stage}: {probe.Failure}; leg {probe.LegFailure}");

        Assert.Null(probe.Failure);
        Assert.Equal("done", probe.Stage);
        Assert.StartsWith("travel:run:indoor-start-unreachable:0xA9B40141: ", probe.LegFailure, StringComparison.Ordinal);
        Assert.Contains(log.Lines, l => l.Contains("[journey-runner] a leg on foot begins leg=cell-1", StringComparison.Ordinal) && l.Contains("cell=outdoor from=0xA9B40141", StringComparison.Ordinal));
    }

    /// <summary>
    /// Live round 2 (after F1): the Archer's walk from the lifestone to Thelnoth (object 0x7A9B402C within 3 m) was planned into his cellar 0xA9B40111, the
    /// room the station's request names, though he stands outdoors in 0xA9B4000F (round 1 arrived by the outdoor planner); the route's last leg through
    /// the cellar's wall was refused three times. An object outdoors is an outdoor goal: from the lifestone and from beside him in 0xA9B4000F, the leg
    /// arrives within 3 m.
    /// </summary>
    [Trait("Tier", "Exhaustive")]
    [RequiresDatsTheory]
    [InlineData(0xA9B40019u, 84f, 7.7f, 94f)]
    [InlineData(0xA9B4000Fu, 40f, 150f, -1f)]
    public void The_walk_to_Thelnoth_outdoors_arrives_within_3_m_though_the_request_names_his_cellar(uint startCell, float x, float y, float z)
    {
        var log = new RecordingLogSink();
        using var host = new HeadlessJourneyRunnerHost(Dats, log);
        ScriptedAceSession? server = null;
        var thelnoth = new System.Numerics.Vector3(36.60f, 157.82f, 66.12f); // the live object table's position
        var start = new System.Numerics.Vector3(x, y, z < 0 ? HeadlessBody.TerrainZ(Dats, startCell, x, y) : z);
        var probe = new F1LegProbe(host, startCell, start, 0xA9B40111, thelnoth, new F1Object(() => server!, 0x7A9B402C, 0xA9B4000F, thelnoth, 3.0));
        using var run = new J4Run(probe, s =>
        {
            server = s;
            s.AcceptsMovement = true;
        });
        run.Finish(() => host.Body.Streamer is { Busy: true }, maxSeconds: 300);
        var end = host.Body.Walker!.Position;
        var metres = System.Numerics.Vector2.Distance(new System.Numerics.Vector2(end.Origin.X, end.Origin.Y), new System.Numerics.Vector2(thelnoth.X, thelnoth.Y));
        output.WriteLine($"{probe.Stage}: {probe.Failure}; leg {probe.LegFailure ?? "arrived"} {probe.Fields}; at 0x{end.CellId:X8} {end.Origin}, {metres:F2} m from Thelnoth");

        Assert.Null(probe.Failure);
        Assert.Null(probe.LegFailure);
        Assert.True(metres <= 3.0f + 0.3f, $"{metres:F2} m");
        Assert.Contains(log.Lines, l => l.Contains("[journey-runner] a leg on foot begins leg=vendor-1", StringComparison.Ordinal) && l.Contains("cell=outdoor", StringComparison.Ordinal));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void The_mages_journey_over_the_stand_in_session_reaches_its_first_station_without_a_place_beyond_the_budget()
    {
        var log = new RecordingLogSink();
        var host = new HeadlessJourneyRunnerHost(Dats, log);
        using var journey = new HeadlessJourneyScenario(JourneyPlans.Mage, [], Dats, SpellTableData.Load(Dats).Spells, log, "lifestone-test", host);
        using var run = new J4Run(journey, s =>
        {
            s.AcceptsMovement = true;
            s.Characters.Add(new AceCharacter(0x50000010, JourneyPlans.Mage.Character));
        });
        var moved = false;
        run.Finish(
            () => host.Body.Streamer is { Busy: true },
            until: () => journey.Stage.StartsWith("station:", StringComparison.Ordinal) || journey.Lines.Count > 0,
            maxSeconds: 600,
            perFrame: () =>
            {
                // The stand-in plays no named place: once the login is over (its own where check passed) and before the known state is read, the server moves the
                // character to Holtburg's lifestone in peace (a teleport the server sends, as a login there would).
                var state = run.Runner.Session.State;
                if (!moved && journey.Stage == "known-state" && run.Server.PlayerGuid is { } guid && state.Self.Guid == guid)
                {
                    moved = true;
                    var stamps = state.Self.Stamps;
                    var teleport = (ushort)(stamps.Teleport + 1);
                    run.Server.Inject(AceWireWriters.PlayerTeleport(teleport));
                    run.Server.Inject(AceWireWriters.UpdatePosition(guid, new AcePosition(0xA9B40019, 84f, 7.7f, 94f), true, new AceUpdatePositionSequences(1, (ushort)(stamps.Position + 1), teleport, 1)));
                    run.Server.Inject(AceWireWriters.PrivateUpdatePropertyInt(9, CombatModes.Property, (int)CombatModes.NonCombat)); // in peace, as a login is
                    run.Server.Inject(AceWireWriters.SetState(guid, (uint)(Lifestone.Net.Types.PhysicsState.Gravity | Lifestone.Net.Types.PhysicsState.EdgeSlide), 1, (ushort)(40 + teleport)));
                }
            });
        output.WriteLine($"stage {journey.Stage}; lines {journey.Lines.Count}; {journey.Failure}");
        foreach (var line in journey.Lines)
        {
            output.WriteLine(line);
        }

        Assert.True(journey.Stage.StartsWith("station:", StringComparison.Ordinal) || journey.Lines.Any(l => l.StartsWith("journey station=", StringComparison.Ordinal)), $"the journey did not reach a station (stage {journey.Stage}, failure {journey.Failure})");
        Assert.NotNull(journey.Journey!.Context.Keeper);
        Assert.Same(host, journey.Journey.Context.Runners);
        var places = run.Server.Talked.Count(t => t.StartsWith("@lifestone place", StringComparison.Ordinal));
        Assert.InRange(places, 0, 1 + JourneyPlans.Mage.TeleportBudget); // the pre-journey recovery place, then the budget
        Assert.True(journey.Journey.Context.Facts.Teleports <= JourneyPlans.Mage.TeleportBudget);
    }

    private static JourneyContext J4Context(J1JourneyRig rig, JourneyPlan plan, IReadOnlyDictionary<uint, SpellInfo> spells, DatSet dats)
    {
        var travel = rig.Context(plan.Character).Travel;
        return new JourneyContext { Character = plan.Character, Account = plan.Account, Log = rig.Log, Travel = travel, Spells = spells, Dats = dats, Facts = rig.Facts };
    }

    /// <summary>Logs in, places the player at Holtburg's lifestone (one server place), runs to a point 9 m east, then to an object 10 m north of the start.</summary>
    private sealed class J4HostProbe(HeadlessJourneyRunnerHost host, Func<ScriptedAceSession> server) : IScenario
    {
        private const uint Marker = 0x7A9B4999;
        private readonly LoginScenario _login = new("Amy") { StayInWorld = true };
        private ITravelLeg? _leg;
        private double _since;

        public string Name => "j4-host-probe";

        public string Stage { get; private set; } = "login";

        public string? Failure { get; private set; }

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public string PointFields { get; private set; } = string.Empty;

        public string ObjectFields { get; private set; } = string.Empty;

        public double PointMetres { get; private set; }

        public double ObjectMetres { get; private set; }

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
                        session.Tools.Place(Destination("lifestone", 84f, 7.7f, 94f, height: true), ActionSource.Host);
                        return Go("placing", session);
                    }

                    return ScenarioStatus.Running;
                case "placing":
                    if (host.Placements >= 1 && host.Body.Placed && session.State.Self.Position is { } at && at.CellId >> 16 == 0xA9B4)
                    {
                        _leg = host.Run(session, new TravelRequest(Destination("east", 93f, 7.7f, 0f, height: false), TravelPurpose.Cell), now);
                        return Go("point", session);
                    }

                    return session.Clock.Now - _since > 120 ? Fail("placing: the body was not placed at Holtburg") : ScenarioStatus.Running;
                case "point":
                    return Leg(session, now, "object");
                case "object":
                    return Leg(session, now, "done");
                default:
                    return Failure is null ? ScenarioStatus.Succeeded : ScenarioStatus.Failed;
            }
        }

        private ScenarioStatus Leg(IGameSessionView session, double now, string next)
        {
            var status = _leg!.Step(session, now);
            if (status == TravelStatus.Running)
            {
                return session.Clock.Now - _since > 120 ? Fail(Stage + ": the leg did not end") : ScenarioStatus.Running;
            }

            if (status == TravelStatus.Failed)
            {
                return Fail(Stage + ": " + _leg.Failure);
            }

            var metres = double.Parse(System.Text.RegularExpressions.Regex.Match(_leg.Fields, @"route_m=([0-9.]+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            _leg.Dispose();
            if (Stage == "point")
            {
                (PointFields, PointMetres) = (_leg.Fields, metres);
                server().Inject(AceWireWriters.ObjectCreate(new AceCreateObject { Guid = Marker, Name = "J4 marker", WeenieClassId = 1, Position = new AcePosition(0xA9B40019, 84f, 17.7f, 94f) }));
                _leg = host.Run(session, new TravelRequest(Destination("marker", 84f, 17.7f, 94f, height: true), TravelPurpose.Seek, Marker, 2.0), now);
            }
            else
            {
                (ObjectFields, ObjectMetres) = (_leg.Fields, metres);
            }

            return Go(next, session);
        }

        private static TeleportDestination Destination(string name, float x, float y, float z, bool height) =>
            new(name, [], new WorldLocation(name, new CellPosition(0xA9B40019, new System.Numerics.Vector3(x, y, z)), HasHeight: height, HeadingDegrees: 0), "j4 test");

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

    /// <summary>F1-1: logs in, places the player at Holtburg's lifestone, then in the den's arrival cell (two server places), then walks the den fight's first leg.</summary>
    private sealed class F1DenProbe(HeadlessJourneyRunnerHost host, IGroundTour? tour = null) : IScenario
    {
        private readonly LoginScenario _login = new("Amy") { StayInWorld = true };
        private IFightWalk? _walk;
        private double _since;

        public string Name => "f1-den-probe";

        public string Stage { get; private set; } = "login";

        public string? Failure { get; private set; }

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public string Walk { get; private set; } = string.Empty;

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
                        session.Tools.Place(Destination("lifestone", 0xA9B40019, new System.Numerics.Vector3(84f, 7.7f, 94f)), ActionSource.Host);
                        return Go("holtburg", session);
                    }

                    return ScenarioStatus.Running;
                case "holtburg":
                    if (host.Placements >= 1 && host.Body.Placed && host.Body.Walker!.Position.CellId >> 16 == 0xA9B4)
                    {
                        session.Tools.Place(Destination("den", DenGround.Arrival.CellId, DenGround.Arrival.Origin), ActionSource.Host);
                        return Go("den", session);
                    }

                    return session.Clock.Now - _since > 120 ? Fail("holtburg: the body was not placed: " + host.PlaceBlocker) : ScenarioStatus.Running;
                case "den":
                    if (host.Body.Placed && host.Body.Walker!.Position.CellId >> 16 == 0x0105)
                    {
                        _walk = tour is null ? host.FightWalk(null!, DenGround.Profile) : host.FightWalk(null!, DenGround.Profile, tour);
                        _walk.Prepare(session, now);
                        if (!_walk.BeginLeg(session, now, out var description))
                        {
                            return Fail("den: the walk began no leg: " + _walk.Failure);
                        }

                        Walk = description;
                        return Go("walk", session);
                    }

                    return session.Clock.Now - _since > 120 ? Fail("den: the body did not follow the player: " + host.PlaceBlocker) : ScenarioStatus.Running;
                case "walk":
                    switch (_walk!.Tick(session, now))
                    {
                        case FightWalkStatus.Arrived:
                            _walk.Dispose();
                            return Go("done", session);
                        case FightWalkStatus.Failed:
                            return Fail("walk: " + _walk.Failure);
                        default:
                            return session.Clock.Now - _since > 120 ? Fail("walk: the leg did not end") : ScenarioStatus.Running;
                    }

                default:
                    return Failure is null ? ScenarioStatus.Succeeded : ScenarioStatus.Failed;
            }
        }

        private static TeleportDestination Destination(string name, uint cell, System.Numerics.Vector3 at) =>
            new(name, [], new WorldLocation(name, new CellPosition(cell, at), HasHeight: true, HeadingDegrees: 0), "f1 test");

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

    /// <summary>An object the server creates for <see cref="F1LegProbe"/> before its leg: the leg is an object goal to it.</summary>
    private sealed record F1Object(Func<ScriptedAceSession> Server, uint Guid, uint Cell, System.Numerics.Vector3 At, double Range);

    /// <summary>F1-2: logs in, has the server place the player at a start, then runs one leg on foot to an outdoor point and keeps how it ended.</summary>
    private sealed class F1LegProbe(HeadlessJourneyRunnerHost host, uint startCell, System.Numerics.Vector3 start, uint toCell, System.Numerics.Vector3 to, F1Object? target = null) : IScenario
    {
        private readonly LoginScenario _login = new("Amy") { StayInWorld = true };
        private ITravelLeg? _leg;
        private double _since;
        private bool _injected;

        public string Name => "f1-leg-probe";

        public string Fields { get; private set; } = string.Empty;

        public string Stage { get; private set; } = "login";

        public string? Failure { get; private set; }

        public string? LegFailure { get; private set; }

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

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
                        session.Tools.Place(Destination("start", startCell, start, height: true), ActionSource.Host);
                        _since = session.Clock.Now;
                        Stage = "placing";
                    }

                    return ScenarioStatus.Running;
                case "placing":
                    if (host.Body.Placed && host.Body.Walker!.Position.CellId == startCell)
                    {
                        if (target is { } o && !session.State.Objects.TryGet(o.Guid, out _))
                        {
                            if (!_injected)
                            {
                                _injected = true;
                                o.Server().Inject(AceWireWriters.ObjectCreate(new AceCreateObject { Guid = o.Guid, Name = "F1 object", WeenieClassId = 1, Position = new AcePosition(o.Cell, o.At.X, o.At.Y, o.At.Z) }));
                            }

                            return ScenarioStatus.Running;
                        }

                        _leg = host.Run(session, target is { } t
                            ? new TravelRequest(Destination("object", toCell, to, height: true), TravelPurpose.Vendor, t.Guid, t.Range)
                            : new TravelRequest(Destination("lifestone", toCell, to, height: true), TravelPurpose.Cell), now);
                        _since = session.Clock.Now;
                        Stage = "leg";
                        return ScenarioStatus.Running;
                    }

                    return session.Clock.Now - _since > 120 ? Fail("placing: the body was not placed: " + host.PlaceBlocker) : ScenarioStatus.Running;
                case "leg":
                    var status = _leg!.Step(session, now);
                    if (status == TravelStatus.Running)
                    {
                        return session.Clock.Now - _since > 120 ? Fail("leg: it did not end") : ScenarioStatus.Running;
                    }

                    LegFailure = _leg.Failure;
                    Fields = _leg.Fields;
                    _leg.Dispose();
                    Stage = "done";
                    return ScenarioStatus.Succeeded;
                default:
                    return Failure is null ? ScenarioStatus.Succeeded : ScenarioStatus.Failed;
            }
        }

        private static TeleportDestination Destination(string name, uint cell, System.Numerics.Vector3 at, bool height) =>
            new(name, [], new WorldLocation(name, new CellPosition(cell, at), HasHeight: height, HeadingDegrees: 0), "f1 test");

        private ScenarioStatus Fail(string why)
        {
            Failure = why;
            Stage = "failed";
            return ScenarioStatus.Failed;
        }
    }

    /// <summary>A headless run over the scripted server at 60 Hz on a manual clock (the WayfindScenarioTests harness); real time passes while the streamer's workers build.</summary>
    private sealed class J4Run : IDisposable
    {
        public J4Run(IScenario scenario, Action<ScriptedAceSession> configure)
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
                Log,
                new MemoryStream(),
                new SocketFactorySpy().Open,
                (_, sink, tap) =>
                {
                    Server = new ScriptedAceSession(sink, tap, Time);
                    configure(Server);
                    return Server;
                });
        }

        public ManualTimeProvider Time { get; }

        public RecordingLogSink Log { get; } = new();

        public HeadlessRunner Runner { get; }

        public ScriptedAceSession Server { get; private set; } = null!;

        public void Finish(Func<bool> busy, Func<bool>? until = null, double maxSeconds = 600, Action? perFrame = null)
        {
            for (var t = 0.0; t < maxSeconds && !Runner.IsDone && until?.Invoke() != true; t += 1.0 / 60.0)
            {
                Time.Advance(TimeSpan.FromSeconds(1.0 / 60.0));
                Runner.Step(Runner.Session.Clock.Now);
                perFrame?.Invoke();
                if (busy())
                {
                    Thread.Sleep(1); // the workers run in real time, the clock in simulated time
                }
            }
        }

        public void Dispose() => Runner.Dispose();
    }
}
