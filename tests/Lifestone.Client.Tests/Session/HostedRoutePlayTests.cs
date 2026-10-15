using Lifestone.Client.Movement;
using Lifestone.Client.Play;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Dat;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// A <see cref="RoutePlay"/> that drives the game's player through <see cref="RoutePlayHost"/> (G6 W12) against <see cref="ScriptedAceSession"/> playing ACE for a walk.
/// The test is the game's side of the contract: it builds the player's walker once the character's data is there (with <see cref="RoutePlayHost.Sink"/> as its
/// sink and the presence gate as <see cref="RoutePlayHost.Inner"/>), applies the player's own positions to it (placing it by the teleports, as play mode does), and
/// ticks its controller. The scenario must wait for that player and for its placement, put its audit in <see cref="RoutePlayHost.Current"/> for the walk, hold
/// the movement back through <see cref="RoutePlayHost.Hold"/> while the character is sent home, count the gate's held messages (<c>HeldNow</c>, in
/// <see cref="RoutePlay.Unmatched"/>) and release everything however the play ends.
/// </summary>
public sealed class HostedRoutePlayTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void Hosted_laps_wait_for_the_players_placement_walk_through_the_audit_and_hold_the_movement_while_going_home()
    {
        using var run = new HostedRun(Dats, placementDelaySeconds: 2.0, configure: s => s.AcceptsMovement = true);
        var result = run.Finish();
        var play = run.Scenario.Play;
        output.WriteLine($"{result.SummaryLine("memory")}: {result.Failure}; {play.SummaryLine}; held {run.Gate!.Held}; where asks at the teleport {run.WhereAsksAtTeleport} and at the placement {run.WhereAsksAtPlacement}");

        Assert.True(result.Ok, result.Failure);
        Assert.Equal("done", result.Stage);
        Assert.Equal(0, result.Corrections);
        Assert.InRange(result.WhereMaxMetres!.Value, 0.0, 0.5);
        Assert.Equal(1, play.Audit!.Laps);
        Assert.True(play.Audit.MoveToStateSent >= 3, $"{play.Audit.MoveToStateSent} MoveToState: the game's walker drove the walk");
        Assert.Same(run.Walker, play.Walker); // the scenario walked the game's own walker, not one of its own

        // It waited for the placement: the body was placed 2 s after the player was present at the ring start, and no where-am-I was asked of the server until it stood there.
        Assert.True(run.WhereAsksAtTeleport >= 0);
        Assert.Equal(run.WhereAsksAtTeleport, run.WhereAsksAtPlacement);
        Assert.True(run.PlacedAt - run.PresentAt >= 2.0, $"placed {run.PlacedAt - run.PresentAt:F2} s after the player was present again");
        Assert.Equal(0, run.Server.IgnoredWhileTeleporting);

        // The audit stood in front of the gate for the walk, and the hold was on while the character went home.
        Assert.True(run.SawAuditCurrent, "the audit was never the host's current sink");
        Assert.True(run.SawHolding, "the movement was never held back while the character was sent home");
        Assert.True(run.Injected > 0, "the game's controller sent nothing while the movement was held");
        Assert.Equal(run.Injected, run.Gate.Held); // the gate held back what the game sent while the character went home
        Assert.Equal(0, play.Unmatched); // every message the gate let through was echoed; the held ones are not counted as lost

        play.Dispose();
        Assert.Null(run.Host.Current);
        Assert.Null(run.Host.Hold);
    }

    [RequiresDatsFact]
    public void A_host_that_never_has_a_player_fails_the_play_naming_it_and_holds_no_sink()
    {
        using var run = new HostedRun(Dats, placementDelaySeconds: 0, configure: s => s.AcceptsMovement = true, buildPlayer: false);
        var result = run.Finish();
        output.WriteLine($"{result.SummaryLine("memory")}: {result.Failure}");

        Assert.False(result.Ok);
        Assert.Contains("the game has no player body to drive", result.Failure, StringComparison.Ordinal);
        Assert.Null(run.Host.Current);
        Assert.Null(run.Scenario.Play.Walker);
    }

    [RequiresDatsFact]
    public void A_hosted_play_that_ends_mid_walk_releases_the_walkers_keys_for_the_games_controller()
    {
        // The walker is the game's own player controller: whatever ends the play, a key the route driver held must not be left held (Dispose and Fail both abandon the driver).
        using var run = new HostedRun(Dats, placementDelaySeconds: 0, configure: s => s.AcceptsMovement = true);
        var controller = () => run.Walker!.Controller;
        run.RunUntil(() => run.Walker is not null && controller().Motion.Raw.ForwardCommand == MotionCommands.WalkForward && controller().HoldRun);
        output.WriteLine($"walking: forward held, run held {controller().HoldRun}, at {run.Walker!.Position.CellId:X8}");
        Assert.True(controller().HoldRun);

        run.Scenario.Play.Dispose();

        Assert.False(controller().HoldRun, "the walk's run modifier is still held after the play ended");
        Assert.NotEqual(MotionCommands.WalkForward, controller().Motion.Raw.ForwardCommand);
        Assert.Null(run.Host.Current);
    }

    /// <summary>A headless run of the Holtburg laps with the game's side played by the test: the player's walker, its placement, its ticks.</summary>
    private sealed class HostedRun : IDisposable
    {
        private readonly DatSet _dats;
        private readonly PhysicsGeometryLibrary _geometry;
        private readonly PhysicsWorld _world;
        private readonly double _placementDelay;
        private readonly bool _buildPlayer;
        private readonly List<(ObjectMoved Move, double At)> _moves = [];
        private OfflineWalker? _walker;
        private bool _placed;
        private double _lastCadence = double.NegativeInfinity;

        public HostedRun(DatSet dats, double placementDelaySeconds, Action<ScriptedAceSession> configure, bool buildPlayer = true)
        {
            (_dats, _placementDelay, _buildPlayer) = (dats, placementDelaySeconds, buildPlayer);
            _geometry = new PhysicsGeometryLibrary(dats);
            _world = HoltburgLapsScenario.AttachAround(dats, _geometry, LandblockCoord.Holtburg);
            Host = new RoutePlayHost(() => _walker, () => Gate?.Held ?? 0);
            Scenario = new HoltburgLapsScenario(dats, Log, character: "Amy", laps: 1, host: Host);
            Runner = HeadlessRunner.StartWith(
                new HeadlessRunOptions { Session = new SessionOptions { Account = "test" } },
                Scenario,
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
            Subscription = Runner.Session.Events.Subscribe<ObjectMoved>((in ObjectMoved e) =>
            {
                if (e.IsSelf)
                {
                    _moves.Add((e, Runner.Session.Clock.Now));
                    if (e.Verdict == PositionVerdict.SelfTeleport && LandblockCoord.FromId(e.Position.CellId) == LandblockCoord.Holtburg)
                    {
                        WhereAsksAtTeleport = WhereAsks();
                    }
                }
            });
        }

        public ManualTimeProvider Time { get; } = new();

        public RecordingLogSink Log { get; } = new();

        public HeadlessRunner Runner { get; }

        public HoltburgLapsScenario Scenario { get; }

        public RoutePlayHost Host { get; }

        public ScriptedAceSession Server { get; private set; } = null!;

        public PresenceGatedMovementSink? Gate { get; private set; }

        public OfflineWalker? Walker => _walker;

        public IDisposable Subscription { get; }

        public double TeleportedAt { get; private set; } = double.NaN;

        public double PresentAt { get; private set; } = double.NaN;

        public double PlacedAt { get; private set; } = double.NaN;

        public int WhereAsksAtTeleport { get; private set; } = -1;

        public int WhereAsksAtPlacement { get; private set; } = -1;

        public int Injected { get; private set; }

        public bool SawAuditCurrent { get; private set; }

        public bool SawHolding { get; private set; }

        private int WhereAsks() => Server.Talked.Count(t => t.StartsWith("@lifestone where", StringComparison.Ordinal));

        public HeadlessRunResult Finish(double maxSeconds = 240)
        {
            RunUntil(() => false, maxSeconds);
            Assert.True(Runner.IsDone, $"the run did not end within {maxSeconds} s (stage {Scenario.Stage})");
            return Runner.Result;
        }

        /// <summary>Steps frames until the run is done or <paramref name="until"/> holds (checked after each frame).</summary>
        public void RunUntil(Func<bool> until, double maxSeconds = 240)
        {
            for (var t = 0.0; t < maxSeconds && !Runner.IsDone && !until(); t += 1.0 / 60.0)
            {
                Time.Advance(TimeSpan.FromSeconds(1.0 / 60.0));
                var now = Runner.Session.Clock.Now;
                Game(now);
                Runner.Step(now);
                SawAuditCurrent |= Host.Current is not null;
                SawHolding |= Host.Holding;
            }

            Assert.True(Runner.IsDone || until(), $"the condition never held within {maxSeconds} s (stage {Scenario.Stage})");
        }

        /// <summary>What play mode does each frame: build the player when the character is known, place it where the server puts it, tick its controller.</summary>
        private void Game(double now)
        {
            var session = Runner.Session;
            if (_walker is null && _buildPlayer && session.State.Self.Qualities.IsKnown)
            {
                Gate = new PresenceGatedMovementSink(session.Actions.Movement, () => !Host.Holding && session.State.Self.Travel == SelfTravel.Present);
                Host.Inner = Gate;
                var weenie = QualitiesWeenie.FromSkillTable(session.State.Self.Qualities, SkillTableData.Load(_dats), Log);
                _walker = OfflineWalker.Create(_dats, _world, Log, weenie, _geometry, id: session.State.Self.Guid, sink: Host.Sink);
            }

            if (_walker is null)
            {
                _moves.Clear();
                return;
            }

            var ready = 0;
            foreach (var (move, at) in _moves)
            {
                if (!_world.IsAttached(LandblockCoord.FromId(move.Position.CellId)))
                {
                    ready++; // the login's own arrival at the Academy: the test world holds Holtburg only
                    continue;
                }

                if (move.Verdict == PositionVerdict.SelfTeleport && double.IsNaN(TeleportedAt))
                {
                    TeleportedAt = at;
                }

                if (!_placed)
                {
                    if (move.Verdict != PositionVerdict.SelfTeleport)
                    {
                        ready++;
                        continue;
                    }

                    // The game places the body once it has streamed the landblock the player was sent to: after the portal tunnel (the player is present again), plus the delay.
                    if (session.State.Self.Travel != SelfTravel.Present)
                    {
                        break;
                    }

                    PresentAt = double.IsNaN(PresentAt) ? now : PresentAt;
                    if (now - PresentAt < _placementDelay)
                    {
                        break;
                    }

                    Assert.True(_walker.Place(move.Position), "no valid position for the walker where the server put the player");
                    _placed = true;
                    PlacedAt = now;
                    WhereAsksAtPlacement = WhereAsks();
                }

                _walker.Controller.Apply(move, now);
                ready++;
            }

            _moves.RemoveRange(0, ready);
            if (_placed)
            {
                _walker.Controller.Tick(now);
                if (Host.Holding && now - _lastCadence >= 0.5)
                {
                    // The player's controller keeps its own position cadence while the scenario sends the character home: the gate must hold these back, and the scenario
                    // must not count them as messages the server lost.
                    _lastCadence = now;
                    Host.Sink.AutonomousPosition(_walker.Position, true);
                    Injected++;
                }
            }
        }

        public void Dispose()
        {
            Subscription.Dispose();
            Runner.Dispose();
            Scenario.Play.Dispose();
        }
    }
}
