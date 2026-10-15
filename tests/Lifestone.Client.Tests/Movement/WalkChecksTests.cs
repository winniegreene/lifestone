using System.Numerics;
using Lifestone.Client.Camera;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Checks;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Data.World.Data;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement;

/// <summary>
/// The walk checks of <c>--offline-player-checks</c> (<see cref="WalkCheckSuite"/>) run headless on the real dats, with the chase camera's model and sweep
/// standing in for the Godot camera, and the cadence audit's rules on synthetic message streams.
/// </summary>
public sealed class WalkChecksTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const double Step = 1.0 / 60.0;

    [RequiresDatsFact]
    public void The_walk_suite_holds_on_the_real_collision()
    {
        var host = new HeadlessWalkHost(fixture.Dats);
        var suite = WalkCheckSuite.Plan(host);
        var now = 0.0;
        while (!suite.Done && now < 900.0)
        {
            now += Step;
            host.Frame(now);
            suite.Tick(now);
        }

        foreach (var verdict in suite.Verdicts)
        {
            output.WriteLine($"{(verdict.Passed ? "PASS" : "FAIL")} {verdict.Name}: {verdict.Detail}");
        }

        Assert.True(suite.Done, $"the suite did not finish in {now:F0} s of simulated time (running {suite.Current?.Name})");
        Assert.Equal(suite.Scenarios.Count, suite.Verdicts.Count);
        Assert.All(suite.Verdicts, v => Assert.True(v.Passed, $"{v.Name}: {v.Detail}"));
    }

    [RequiresDatsFact]
    public void A_camera_behind_a_wall_fails_the_walk()
    {
        // Control for the camera audit: the same wall walk with the camera moved 4 m east, through the shop's west wall, is caught.
        var host = new HeadlessWalkHost(fixture.Dats)
        {
            CameraTamper = pose => pose with { Viewer = pose.Viewer with { Frame = pose.Viewer.Frame with { Origin = pose.Viewer.Origin + new Vector3(4f, 0f, 0f) } } },
        };
        var route = Lifestone.Client.Movement.Routes.BuildingRoutes.WallBump("wall", host.Cells, host.World, WalkCheckSuite.WallDoorCell);
        var scenario = new RouteWalk("wall-with-a-broken-camera", route, Lifestone.Client.Movement.Routes.RouteStatus.Stuck, 40.0);
        var now = 0.0;
        while (!scenario.Finished && now < 120.0)
        {
            now += Step;
            host.Frame(now);
            scenario.Tick(host, now);
        }

        Assert.True(scenario.Finished);
        output.WriteLine(scenario.Verdict.Detail);
        Assert.False(scenario.Verdict.Passed);
        Assert.Contains("camera", scenario.Verdict.Detail, StringComparison.Ordinal);
    }

    [RequiresDatsFact]
    public void A_teleport_without_a_server_leaves_the_player_constrained_unless_the_host_drops_it()
    {
        // Retail's server echoes every accepted position and the constraint follows the player. Offline nothing echoes, so the explore-mode host drops the
        // constraint at each teleport (PlayerController.Teleport); without that, a lap that goes 16 m from the spot is slowed (docs/physics.md section 12, D23).
        foreach (var keep in new[] { true, false })
        {
            var host = new HeadlessWalkHost(fixture.Dats) { KeepConstraint = keep };
            var suite = WalkCheckSuite.Plan(host);
            var lap = suite.Scenarios[0];
            var now = 0.0;
            while (!lap.Finished && now < 120.0)
            {
                now += Step;
                host.Frame(now);
                lap.Tick(host, now);
            }

            var factor = host.Controller.Stats.MinConstraintFactor;
            output.WriteLine($"constraint kept: {keep}; the slowest the constraint made the lap: x{factor:F3}");
            Assert.Equal(keep, factor < 0.999f);
        }
    }

    [Fact]
    public void A_clean_stream_of_messages_passes_the_cadence_audit()
    {
        var cell = 0xA9B40019u;
        var messages = new List<SentMessage>
        {
            Message(SentKind.AutonomousPosition, 0.05, cell), // before the window: it only sets the state
            Message(SentKind.MoveToState, 0.10, cell),
            Message(SentKind.AutonomousPosition, 0.10 + (61 * Step), cell), // the first frame more than a second after the MoveToState
            Message(SentKind.AutonomousPosition, 0.10 + (122 * Step), cell),
        };
        var samples = Walk(0.10, 2.30, cell);
        Assert.Empty(CadenceAudit.Verify(messages, 1, samples));
    }

    [Fact]
    public void An_AutonomousPosition_in_the_air_is_a_violation()
    {
        var cell = 0xA9B40019u;
        var messages = new List<SentMessage> { Message(SentKind.AutonomousPosition, 0.5, cell, onGround: false) };
        var problems = CadenceAudit.Verify(messages, 0, []);
        Assert.Contains(problems, p => p.Contains("not on walkable ground", StringComparison.Ordinal));
    }

    [Fact]
    public void An_early_AutonomousPosition_without_a_cell_or_plane_change_is_a_violation()
    {
        var cell = 0xA9B40019u;
        var messages = new List<SentMessage>
        {
            Message(SentKind.AutonomousPosition, 0.1, cell),
            Message(SentKind.AutonomousPosition, 0.6, cell),
        };
        Assert.Single(CadenceAudit.Verify(messages, 0, []));

        // The same message a half second later is fine when the cell changed, or the contact plane did.
        messages[1] = Message(SentKind.AutonomousPosition, 0.6, 0xA9B40020u);
        Assert.Empty(CadenceAudit.Verify(messages, 0, []));
        messages[1] = Message(SentKind.AutonomousPosition, 0.6, cell, plane: Slope(0.3f));
        Assert.Empty(CadenceAudit.Verify(messages, 0, []));
    }

    [Fact]
    public void Moving_on_the_ground_for_more_than_the_interval_without_a_message_is_a_violation()
    {
        var cell = 0xA9B40019u;
        var messages = new List<SentMessage> { Message(SentKind.AutonomousPosition, 0.1, cell) };
        var problems = CadenceAudit.Verify(messages, 0, Walk(0.1, 1.6, cell));
        Assert.Contains(problems, p => p.Contains("after the last message", StringComparison.Ordinal));
    }

    [Fact]
    public void A_change_of_cell_on_the_ground_needs_an_AutonomousPosition_in_the_same_frame()
    {
        var messages = new List<SentMessage> { Message(SentKind.AutonomousPosition, 0.1, 0xA9B40019u) };
        var samples = new List<WalkSample>
        {
            new(0.2, At(0xA9B4001Au, 0f), true, Step),
        };
        Assert.Contains(CadenceAudit.Verify(messages, 0, samples), p => p.Contains("last AutonomousPosition was for cell", StringComparison.Ordinal));
        messages.Add(Message(SentKind.AutonomousPosition, 0.2, 0xA9B4001Au));
        Assert.Empty(CadenceAudit.Verify(messages, 0, samples));
    }

    private static SentMessage Message(SentKind kind, double time, uint cell, bool onGround = true, Plane? plane = null) =>
        new(kind, time, At(cell, 0f), onGround, plane ?? Slope(0.1f));

    private static Plane Slope(float x) => new(Vector3.Normalize(new Vector3(x, 0f, 1f)), 0f);

    private static CellFrame At(uint cell, float x) => new(cell, new AcFrame(new Vector3(x, 0f, 94f), Quaternion.Identity));

    /// <summary>A player walking along x on the ground, one sample per step, whose position changes every frame.</summary>
    private static List<WalkSample> Walk(double from, double to, uint cell)
    {
        var samples = new List<WalkSample>();
        for (var t = from + Step; t <= to; t += Step)
        {
            samples.Add(new WalkSample(t, At(cell, (float)(t * 5.0)), true, Step));
        }

        return samples;
    }

    /// <summary>The walk checks' host without Godot: the real controller on the real collision, the chase camera's model swept through it.</summary>
    private sealed class HeadlessWalkHost : IWalkHost
    {
        private readonly ChaseCameraModel _camera = new();
        private readonly OfflineWalker _walker;
        private double _lastFrame;
        private CameraPose? _pose;

        public HeadlessWalkHost(DatSet dats)
        {
            var builder = new LandblockCollisionBuilder(dats);
            World = new PhysicsWorld();
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    World.Attach(builder.Build(LandblockCoord.Holtburg.Offset(dx, dy)!.Value));
                }
            }

            Trace = new MovementTrace();
            _walker = OfflineWalker.Create(dats, World, NullLogSink.Instance, sink: Trace, options: new MovementOptions { RunByDefault = true });
            Trace.Attach(_walker.Controller);
            Cells = LandblockCellSet.Load(new CellStructureLibrary(dats), LandblockCoord.Holtburg);
            var poi = AcePointsOfInterest.All.Single(p => p.DisplayName == "Holtburg");
            World.SetTime(0.0);
            World.UseTime(0.0);
            Assert.True(_walker.Place(new CellFrame(poi.CellId, new AcFrame(poi.Origin, Quaternion.Identity))));
        }

        public PlayerMovementController Controller => _walker.Controller;

        public PhysicsWorld World { get; }

        public MovementTrace Trace { get; }

        public LandblockCellSet Cells { get; }

        public bool IsReady => !_walker.Physics.PlacementPending;

        public double FrameSeconds { get; private set; } = Step;

        public CameraPose? Camera => _pose;

        /// <summary>True to leave the constraint a teleport arrival sets (what the explore-mode host must not do offline).</summary>
        public bool KeepConstraint { get; init; }

        /// <summary>Changes the camera's pose before the checks see it (a control for the camera audit).</summary>
        public Func<CameraPose, CameraPose>? CameraTamper { get; init; }

        public float ViewerRadius => 0.3f;

        public bool FirstPerson
        {
            get => _camera.InHead;
            set
            {
                if (value && !_camera.InHead)
                {
                    _camera.SetInHead();
                }
                else if (!value && _camera.InHead)
                {
                    _camera.SetDefaultOffsets();
                }
            }
        }

        public void Teleport(in CellFrame frame)
        {
            _walker.Controller.Apply(new ObjectMoved(0x50000001u, frame, PositionVerdict.SelfTeleport, true, true, null, null), _lastFrame);
            if (!KeepConstraint)
            {
                _walker.Physics.UnConstrain(); // offline there is no server to echo the position, so nothing keeps the player near the spot
            }

            _camera.Snap();
        }

        /// <summary>One frame: the controller's step, then the camera.</summary>
        public void Frame(double now)
        {
            FrameSeconds = _lastFrame == 0 ? Step : now - _lastFrame;
            _lastFrame = now;
            _walker.Controller.Tick(now);
            var pose = _camera.Update(now, _walker.Controller.Position, (in CellFrame from, in CellFrame to) => World.SweepViewer(from, to, ViewerRadius));
            _pose = CameraTamper?.Invoke(pose) ?? pose;
        }
    }
}
