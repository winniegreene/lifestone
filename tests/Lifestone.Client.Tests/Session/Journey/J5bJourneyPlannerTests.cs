using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M4 P5b J5b: the Godot journey host's parity with the headless host. One leg planner (<see cref="JourneyLegPlanner"/>) and one leg (<see cref="JourneyRunLeg"/>)
/// for both hosts: a request's goal and path source for the headless host's cases and the Godot host's (a den graph built elsewhere, an indoor start that the old
/// Godot rule gave to the outdoor planner); the settle gate the Godot player waits on before it plans (live: <c>no-tile</c> and <c>start-not-attached</c> as the
/// first walks planned before any tile, and right after the teleport to the forest), with its named timeout; and the den walk over the player's own body, begun
/// after a recall into the den that happened before the station (live: the den fight's walker was never placed). What this cannot show: the Godot player's
/// streamer and controller (the lead's live journeys prove them).
/// </summary>
public sealed class J5bJourneyPlannerTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint Lifestone = 0xA9B40019;

    private DatSet Dats => fixture.Dats;

    [Fact]
    public void A_finished_run_arrives_or_fails_by_the_same_names_in_both_hosts()
    {
        var sight = Request(Lifestone, new Vector3(84, 7.7f, 94)) with { ObjectGuid = 0x7A9B402C, NeedSight = true };
        var noSight = sight with { NeedSight = false };
        Assert.True(JourneyLegPlanner.Arrived(NavRunState.Arrived, sight));
        Assert.False(JourneyLegPlanner.Arrived(NavRunState.ArrivedWithoutSight, sight));
        Assert.True(JourneyLegPlanner.Arrived(NavRunState.ArrivedWithoutSight, noSight)); // Godot counted only Arrived before J5b
        Assert.Equal("travel:run:interrupted: a key", JourneyLegPlanner.FailureOf(NavRunState.Interrupted, "a key", Lifestone));
        Assert.Equal("travel:run:blocked: no reason given", JourneyLegPlanner.FailureOf(NavRunState.Blocked, null, Lifestone));
        var room = JourneyLegPlanner.FailureOf(NavRunState.Blocked, $"{IndoorOutdoorPathSource.IndoorStartUnreachable}: no door", 0xA9B40141);
        Assert.StartsWith($"travel:run:{IndoorOutdoorPathSource.IndoorStartUnreachable}:0xA9B40141: ", room, StringComparison.Ordinal);
        Assert.StartsWith("travel:run:blocked: ", JourneyLegPlanner.FailureOf(NavRunState.Blocked, IndoorOutdoorPathSource.IndoorStartUnreachable, Lifestone), StringComparison.Ordinal); // an outdoor start keeps the generic name
    }

    [RequiresDatsFact]
    public void A_request_gives_the_same_goal_and_source_in_both_hosts()
    {
        var planner = new JourneyLegPlanner(Dats);
        var map = new NavMap();
        var rig = new J1JourneyRig();

        // A point outdoors with no height: the terrain's height and an outdoor goal (Godot used the request's z of 0 before J5b). An outdoor goal is planned to its
        // landblock's id, which the source rule does not take for an outdoor cell: the indoor-outdoor source over that landblock, which hands a goal with no room to
        // the outdoor planner (the headless host's rule since J4, kept; Godot made an outdoor source before J5b, the same plan without the landblock's rooms).
        var point = planner.GoalOf(rig.View, Request(Lifestone, new Vector3(84, 7.7f, 0), height: false));
        Assert.Null(point.CellId);
        Assert.Equal(HeadlessBody.TerrainZ(Dats, Lifestone, 84, 7.7f), point.Point.Local.Z, 3);
        Assert.Equal(0xA9B40000u, JourneyLegPlanner.GoalCellOf(point));
        Assert.IsType<IndoorOutdoorPathSource>(planner.SourceFor(Lifestone, JourneyLegPlanner.GoalCellOf(point), map));
        Assert.IsType<OutdoorPathSource>(planner.SourceFor(Lifestone, 0xA9B4000F, map)); // two outdoor cells

        // Thelnoth outdoors though the request names his cellar: an outdoor object goal at his own position (live round 2).
        rig.CreateAt(0x7A9B402C, 0xA9B4000F, 36.6f);
        var thelnoth = planner.GoalOf(rig.View, Request(0xA9B40111, new Vector3(36.6f, 157.8f, 66f)) with { ObjectGuid = 0x7A9B402C, Range = 3 });
        Assert.True(thelnoth.IsObject);
        Assert.Null(thelnoth.CellId);
        Assert.Equal(3f, thelnoth.Range);
        Assert.Equal(36.6f, thelnoth.Point.Local.X, 3);
        Assert.Equal(0xA9B40000u, JourneyLegPlanner.GoalCellOf(thelnoth)); // not his cellar 0xA9B40111

        // Renald indoors behind his counter: the goal is the front room the request names (F1-3), the indoor-outdoor source (the old Godot rule took his own room).
        rig.CreateAt(0x7A9B4011, 0xA9B40140, 140f);
        var renald = planner.GoalOf(rig.View, Request(HeadlessJourneyRunnerHost.RenaldFrontRoomCell, new Vector3(140, 20, 94)) with { ObjectGuid = 0x7A9B4011, Range = 6.5 });
        Assert.Equal(HeadlessJourneyRunnerHost.RenaldFrontRoomCell, renald.CellId);
        Assert.IsType<IndoorOutdoorPathSource>(planner.SourceFor(Lifestone, JourneyLegPlanner.GoalCellOf(renald), map));

        // From inside the chest room to the lifestone: the indoor-outdoor source and its exit route (F1-2; Godot gave it to the outdoor planner: start-blocked).
        Assert.IsType<IndoorOutdoorPathSource>(planner.SourceFor(0xA9B4016A, Lifestone & 0xFFFF0000u, map));

        // The headless host's source is the planner's (the den needs no nav map, so no body is built here).
        using var host = new HeadlessJourneyRunnerHost(Dats, new RecordingLogSink());
        Assert.IsType<RoomPathSource>(host.SourceFor(Lifestone, J1JourneyRig.DenCell));
        Assert.Same(host.DenGraph(), host.Planner.DenGraph());
    }

    [RequiresDatsFact]
    public void The_Godot_hosts_den_graph_comes_from_its_worker_once()
    {
        var built = 0;
        var graph = DenRooms.GraphOf(new JourneyLegPlanner(Dats).CellsOf(J1JourneyRig.DenCell));
        var godot = new JourneyLegPlanner(Dats, den: () =>
        {
            built++;
            return graph;
        });
        Assert.IsType<RoomPathSource>(godot.SourceFor(J1JourneyRig.DenCell, 0x01050383, null));
        Assert.IsType<RoomPathSource>(godot.SourceFor(J1JourneyRig.DenCell, 0x01050390, null));
        Assert.Equal(1, built); // once, from the host's worker; never rebuilt here
    }

    [Fact]
    public void A_den_leg_with_no_den_graph_fails_travel_run_no_path_source()
    {
        var rig = new J1JourneyRig(J1JourneyRig.DenCell);
        var log = new RecordingLogSink();
        var planner = new JourneyLegPlanner(null, den: () => throw new InvalidOperationException("the journey built no den room graph (TODO(m4-journey-den-graph))"));
        var body = new J5bScriptedBody(rig) { Settled = true };
        using var leg = new JourneyRunLeg(planner, body, Request(0x01050383, new Vector3(10, 10, 0)), "spot-1", rig.View.Clock.Now, log);
        Assert.Equal(TravelStatus.Failed, leg.Step(rig.View, rig.Now));
        Assert.StartsWith("travel:run:no-path-source: the journey built no den room graph", leg.Failure, StringComparison.Ordinal);
        Assert.Equal(0, body.Runners);
    }

    /// <summary>
    /// M4 4.2 F1 (DoD 8): the death station's corpse trip, as it ran in the fresh-r2 gate. The den fight ended with the mage in 0x0105036F at (100.08, -140.87); its
    /// corpse 0x800012C1 lay in 0x01050377 at (110.02, -139.07), a negative local y as every room of the den's south half has. The object goal took the corpse's
    /// position through <see cref="NavMap.FromCellFrame"/>, which normalised it like ground into 0x01040000 [110.02 52.93]; the room graph read that local origin in
    /// the corpse room's frame, 192 m north of the corpse, and planned 201 m over 8 waypoints, which the physics refused at segment 6 three times (travel-failed in
    /// p5b-r3, fu1-r1 and fresh-r2). Now the goal is the corpse's own room and origin, the plan is the few metres between the rooms and ends on the corpse. What
    /// this cannot show: the live server's corpse and the den's live walk (the lead's death station).
    /// </summary>
    [RequiresDatsFact]
    public void The_mages_corpse_in_the_den_is_an_object_goal_in_its_own_room_and_a_short_plan_from_the_den_fights_end()
    {
        const uint FightEndCell = 0x0105036F;
        const uint CorpseCell = 0x01050377;
        const uint Corpse = 0x800012C1;
        var fightEnd = new Vector3(100.08f, -140.87f, 0f);
        var corpseAt = new Vector3(110.02f, -139.07f, 0.01f);
        var rig = new J1JourneyRig(FightEndCell);
        rig.TeleportNow(FightEndCell, fightEnd);
        rig.CreateAt(Corpse, CorpseCell, corpseAt.X, corpseAt.Y, corpseAt.Z);
        const string Name = "the mage's corpse";
        var request = new TravelRequest(
            new TeleportDestination(Name, [], new WorldLocation(Name, new CellPosition(CorpseCell, corpseAt), true, 0), "the death loop's remembered corpse position (M4 2.2 s1)"),
            TravelPurpose.Corpse,
            Corpse,
            DeathScenario.CorpseTravelRangeMetres);
        var planner = new JourneyLegPlanner(Dats);

        var goal = planner.GoalOf(rig.View, request);
        Assert.True(goal.IsObject);
        Assert.Equal(new NavPoint(0x01050000u, corpseAt), goal.Point); // not 0x01040000 [110.02 52.93]
        Assert.Equal(CorpseCell, goal.CellId);
        Assert.Equal(2f, goal.Range);

        var log = new RecordingLogSink();
        var body = new J5bScriptedBody(rig) { Settled = true };
        using var leg = new JourneyRunLeg(planner, body, request, "corpse-4", rig.View.Clock.Now, log);
        leg.Step(rig.View, rig.Now); // planned in this step; the scripted body has no movement controller, so the run's first key ends it (the walk is NavRunnerDatTests')
        Assert.IsType<RoomPathSource>(body.Source);
        Assert.DoesNotContain("physics refused", leg.Failure ?? string.Empty, StringComparison.Ordinal);
        var begin = Assert.Single(log.Lines, l => l.Contains("a leg on foot begins leg=corpse-4", StringComparison.Ordinal));
        output.WriteLine(begin);
        Assert.Contains("goal=\"0x01050000 [110.02 -139.07 0.01]\" cell=0x01050377 from=0x0105036F object=0x800012C1", begin, StringComparison.Ordinal);

        var path = leg.Runner!.Path;
        Assert.NotNull(path);
        Assert.Equal(NavPathStatus.Found, path.Status);
        var metres = 0f;
        for (var i = 1; i < path.Waypoints.Count; i++)
        {
            metres += Vector2.Distance(path.Waypoints[i - 1].Point.Global, path.Waypoints[i].Point.Global);
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{metres:0.0} m over {path.Waypoints.Count} waypoints: {string.Join(" -> ", path.Waypoints.Select(w => w.At.ToString()))}"));
        Assert.InRange(metres, 9.9f, 30f); // the rooms are 10 m apart in a straight line; the bogus plan was 201 m
        Assert.Equal(CorpseCell, path.Waypoints[^1].At.CellId);
        Assert.Equal(corpseAt, path.Waypoints[^1].At.Origin);
        Assert.All(path.Waypoints, w => Assert.Equal(0x0105u, w.At.CellId >> 16));
    }

    /// <summary>
    /// Live (Godot): the first walks (bind, seek) planned before any nav tile was built and failed <c>no-tile</c>. Now a leg asked before the focus round the
    /// player settled waits (named <c>focus-not-settled</c>, no runner, no key), and plans in the frame the focus settles.
    /// </summary>
    [RequiresDatsFact]
    public void A_leg_asked_before_the_tiles_waits_then_plans_when_the_focus_settles()
    {
        var rig = new J1JourneyRig();
        var log = new RecordingLogSink();
        var body = new J5bScriptedBody(rig);
        using var leg = new JourneyRunLeg(new JourneyLegPlanner(Dats), body, Request(Lifestone, new Vector3(100, 40, 94)), "bind-1", rig.View.Clock.Now, log);
        for (var i = 0; i < 40; i++)
        {
            Assert.Equal(TravelStatus.Running, leg.Step(rig.View, rig.Now));
            rig.Frame();
        }

        Assert.Equal(0, body.Runners);
        Assert.Null(leg.Runner);
        Assert.StartsWith($"focus-not-settled:0x{Lifestone:X8}: ", leg.Waited, StringComparison.Ordinal);
        Assert.DoesNotContain(log.Lines, l => l.Contains("a leg on foot begins", StringComparison.Ordinal));

        body.Settled = true;
        leg.Step(rig.View, rig.Now);
        Assert.Equal(1, body.Runners);
        Assert.NotNull(leg.Runner);
        Assert.IsType<IndoorOutdoorPathSource>(body.Source);
        var begin = Assert.Single(log.Lines, l => l.Contains("[journey-runner] a leg on foot begins leg=bind-1", StringComparison.Ordinal));
        output.WriteLine(begin);
        Assert.Contains($"from=0x{Lifestone:X8}", begin, StringComparison.Ordinal);
    }

    /// <summary>
    /// Live (Godot): right after the teleport to the forest 0x9FB50039 a leg planned from where the body stood before it and failed <c>start-not-attached</c>.
    /// Now a leg asked while the body is still where the teleport took the player from waits <c>not-placed:&lt;the server's cell&gt;</c>; one whose wait does
    /// not end fails by the name <c>travel:run:not-settled:&lt;cell&gt;</c> after <see cref="JourneySettleGate.Seconds"/>, never <c>no-tile</c> or <c>start-not-attached</c>.
    /// </summary>
    [Fact]
    public void A_leg_after_a_teleport_waits_for_the_body_and_a_wait_that_never_ends_fails_not_settled_by_name()
    {
        var rig = new J1JourneyRig();
        var log = new RecordingLogSink();
        var body = new J5bScriptedBody(rig) { Settled = true, At = new CellFrame(Lifestone, new AcFrame(new Vector3(84, 20, 30), Quaternion.Identity)) };
        rig.TeleportNow(J1JourneyRig.BeltCell);
        rig.Frame();
        using var leg = new JourneyRunLeg(new JourneyLegPlanner(null), body, Request(J1JourneyRig.BeltCell, new Vector3(100, 40, 94)), "seek-1", rig.View.Clock.Now, log);
        Assert.Equal(TravelStatus.Running, leg.Step(rig.View, rig.Now));
        Assert.StartsWith($"not-placed:0x{J1JourneyRig.BeltCell:X8}: ", leg.Waited, StringComparison.Ordinal);

        var status = TravelStatus.Running;
        var start = rig.Now;
        while (status == TravelStatus.Running && rig.Now - start < JourneySettleGate.Seconds + 5)
        {
            rig.Frame();
            status = leg.Step(rig.View, rig.Now);
        }

        output.WriteLine(leg.Failure);
        Assert.Equal(TravelStatus.Failed, status);
        Assert.InRange(rig.Now - start, JourneySettleGate.Seconds, JourneySettleGate.Seconds + 1);
        Assert.StartsWith($"{JourneySettleGate.NotSettled}:0x{J1JourneyRig.BeltCell:X8}: not ready within 120 s (not-placed:0x{J1JourneyRig.BeltCell:X8}", leg.Failure, StringComparison.Ordinal);
        Assert.Equal(0, body.Runners);

        // The gate itself: a placed body waits for its focus, then may plan.
        Assert.StartsWith("focus-not-settled:", JourneySettleGate.Reason(rig.View, new CellFrame(J1JourneyRig.BeltCell, new AcFrame(new Vector3(84, 20, 30), Quaternion.Identity)), _ => false), StringComparison.Ordinal);
        Assert.Null(JourneySettleGate.Reason(rig.View, new CellFrame(J1JourneyRig.BeltCell, new AcFrame(new Vector3(84, 20, 30), Quaternion.Identity)), _ => true));
    }

    /// <summary>
    /// Live (Godot): the mage was recalled into the den before the den fight started, and the fight's offline walker (<see cref="DenWalk"/>) waits for a teleport
    /// it never saw: <c>fight: no walk could be planned: the den walker is not placed: no teleport of the player reached it</c>, and the death after it timed out.
    /// The Godot host now gives the den fight and the death the M3 fight leg's walk over the player's own body (<see cref="GameFightWalk"/>), which the play places
    /// wherever the server has the player. Here the server places the player in the den first; a body that follows the server (the headless host's, standing in
    /// for the play's player) is placed there; only then is the walk made: it begins a leg on the room graph and arrives. The offline walker made at the same
    /// moment still fails by its old name (the live failure).
    /// </summary>
    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void The_den_walk_over_the_players_body_walks_after_a_recall_that_happened_before_the_station()
    {
        var log = new RecordingLogSink();
        using var host = new HeadlessJourneyRunnerHost(Dats, log);
        var probe = new J5bDenProbe(host, Dats, log);
        using var run = new J5bRun(probe);
        run.Finish(() => host.Body.Streamer is { Busy: true }, maxSeconds: 400);
        output.WriteLine($"{probe.Stage}: {probe.Failure}; walk {probe.Walk}; offline walker: {probe.OfflineFailure}");

        Assert.Null(probe.Failure);
        Assert.Equal("done", probe.Stage);
        Assert.Contains("from 0x01050375 to 0x0105", probe.Walk, StringComparison.Ordinal);
        Assert.Equal("the den walker is not placed: no teleport of the player reached it", probe.OfflineFailure);
        Assert.NotEqual(BlackSpawnDen.Arrival.CellId, host.Body.Walker!.Position.CellId);
    }

    private static TravelRequest Request(uint cell, Vector3 at, bool height = true) =>
        new(new TeleportDestination("j5b", [], new WorldLocation("j5b", new CellPosition(cell, at), HasHeight: height, HeadingDegrees: 0), "j5b test"), TravelPurpose.Spot);

    /// <summary>A body for the shared leg whose focus the test settles (the Godot player's streamer, scripted): the gate is the Godot host's own <see cref="JourneySettleGate"/>.</summary>
    private sealed class J5bScriptedBody(J1JourneyRig rig) : IJourneyLegBody, INavBody
    {
        public bool Settled { get; set; }

        /// <summary>Where the body stands; null: where the server has the player.</summary>
        public CellFrame? At { get; set; }

        public int Runners { get; private set; }

        public INavPathSource? Source { get; private set; }

        public string LogCategory => HeadlessJourneyRunnerHost.LogCategory;

        public double WaitSeconds => JourneySettleGate.Seconds;

        public double CorridorSeconds => 0.5;

        public NavMap? Map { get; } = new();

        public CellFrame Position => At ?? rig.View.State.Self.Position!.Value;

        public bool Placed => true;

        public int Moves => 0;

        public uint CellId => Position.CellId;

        public bool OnGround => true;

        public bool ServerDriving => false;

        public double Now => rig.Now;

        public void Frame(IGameSessionView session, double now)
        {
        }

        public string? WaitReason(IGameSessionView session) => JourneySettleGate.Reason(session, Position, _ => Settled);

        public string WaitFailure(IGameSessionView session, string reason) => JourneySettleGate.Failure(session.State.Self.Position?.CellId ?? 0, reason);

        public NavRunner NewRunner(IGameSessionView session, INavPathSource source, string name)
        {
            Runners++;
            Source = source;
            return new NavRunner(session.Actions.Move, this, source, null, null, new NavRunnerOptions { Name = name });
        }

        public void FollowRoute(NavRunner runner)
        {
        }
    }

    /// <summary>Logs in, has the server place the player at Holtburg and then in the den (the recall before the station), waits for the body there, then makes the walks.</summary>
    private sealed class J5bDenProbe(HeadlessJourneyRunnerHost host, DatSet dats, ILogSink log) : IScenario
    {
        private readonly LoginScenario _login = new("Amy") { StayInWorld = true };
        private IFightWalk? _walk;
        private double _since;

        public string Name => "j5b-den-probe";

        public string Stage { get; private set; } = "login";

        public string? Failure { get; private set; }

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public string Walk { get; private set; } = string.Empty;

        public string? OfflineFailure { get; private set; }

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
                        session.Tools.Place(Destination("lifestone", Lifestone, new Vector3(84f, 7.7f, 94f)), ActionSource.Host);
                        return Go("holtburg", session);
                    }

                    return ScenarioStatus.Running;
                case "holtburg":
                    if (host.Body.Placed && host.Body.Walker!.Position.CellId >> 16 == 0xA9B4)
                    {
                        session.Tools.Place(Destination("den", BlackSpawnDen.Arrival.CellId, BlackSpawnDen.Arrival.Origin), ActionSource.Host); // the recall, before the station
                        return Go("recalled", session);
                    }

                    return session.Clock.Now - _since > 120 ? Fail("holtburg: the body was not placed: " + host.PlaceBlocker) : ScenarioStatus.Running;
                case "recalled":
                    if (host.Body.Placed && host.Body.Walker!.Position.CellId >> 16 == 0x0105)
                    {
                        // The station starts now, after the recall: an offline walker never sees a teleport; the walk over the player's body needs none.
                        using (var offline = DenWalk.Create(dats, log))
                        {
                            offline.Prepare(session, now);
                            OfflineFailure = offline.BeginLeg(session, now, out _) ? "began a leg" : offline.Failure;
                        }

                        _walk = new GameFightWalk(new RoutePlayHost(() => host.Body.Placed ? host.Body.Walker : null, () => 0), host.DenGraph(), log);
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

        private static TeleportDestination Destination(string name, uint cell, Vector3 at) =>
            new(name, [], new WorldLocation(name, new CellPosition(cell, at), HasHeight: true, HeadingDegrees: 0), "j5b test");

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

    /// <summary>The headless runner over the scripted ACE (J4's run, copied: its class is private to J4's tests).</summary>
    private sealed class J5bRun : IDisposable
    {
        public J5bRun(IScenario scenario)
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
