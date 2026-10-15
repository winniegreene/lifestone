using System.Diagnostics;
using System.Globalization;
using Godot;
using Lifestone.Client.Camera;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Checks;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.World;

namespace Lifestone.Game.Player;

/// <summary>
/// <c>--offline-player-checks</c>: the walk checks (<see cref="WalkCheckSuite"/>) run in the real explore scene with the real offline player. Once the world around
/// the player is complete, a route driver (<see cref="Lifestone.Client.Movement.Routes.RouteDriver"/>) walks the player through the controller's intents
/// exactly as the keys would, one scenario after the other: a lap of Holtburg's ring, a standing and a running jump, a walk into a wall, the stairs up into a
/// building and back out, the cellar's stairs down and back up (the last two also in first person). It runs at process priority -4, after the player (-20) and
/// the camera (-5) have done their frame, and prints one <c>[walkcheck] PASS|FAIL &lt;name&gt;</c> line per check and a final line, then quits (0 = all hold).
/// </summary>
/// <remarks>
/// <para><b>What it asserts</b> (docs/physics.md §12): the player is blocked by the wall and slides along it; the stairs and the ramps are climbed and descended
/// with the player's cell following through the doorways; the standing and the running jump leave the ground, rise, land and never sink into it; each key
/// change is one MoveToState, and the dry-run sink sees every message the controller sends; the AutonomousPosition follows the retail cadence (only while
/// Contact and OnWalkable, within 1.0 s of the last message while the player moves, at once on a cell change); the chase camera and the first-person
/// camera are never inside geometry; no frame is over 50 ms.</para>
/// <para><b>Frame times.</b> A frame is Godot's delta (the time between two frames, the draw included). The routes are planned once when the world is
/// complete (the ring is validated against the collision, <c>plan_ms</c> in the log) and the frame that plans is not counted; every other frame from the
/// first scenario's first frame to the last is, the teleports between scenarios included.</para>
/// </remarks>
public sealed partial class PlayerWalkChecks : Node, IWalkHost
{
    /// <summary>Process priority: after the player (-20) and its camera (-5).</summary>
    public const int Priority = -4;

    private const int SettleFrames = 30;
    private const double SettleTimeoutSeconds = 90.0;
    private const double RunTimeoutSeconds = 420.0;

    private readonly PlayerController _player;
    private readonly CaptureReadiness _readiness;
    private readonly Task<LandblockCellSet> _cells;
    private readonly DryRunMovementSink? _dryRun;
    private readonly long _dryMoveToState;
    private readonly long _dryAutonomous;
    private readonly long _dryJump;
    private readonly List<(string Name, bool Passed, string Detail)> _results = [];
    private WalkCheckSuite? _suite;
    private Phase _phase = Phase.Settle;
    private int _settled;
    private int _reported;
    private double _phaseSeconds;
    private long _runFrames;
    private double _maxFrameMs;
    private int _slowFrames;
    private bool _skipNextFrame;
    private bool _done;

    private enum Phase
    {
        Settle,
        Run,
    }

    private PlayerWalkChecks(GameServices services, WorldRoot world, PlayerController player)
    {
        _player = player;
        _readiness = new CaptureReadiness(services, world);
        Trace = new MovementTrace();
        Trace.Attach(player.Controller);
        if (player.Walker.Sink is DryRunMovementSink dry)
        {
            _dryRun = dry;
            _dryMoveToState = dry.MoveToStateCount;
            _dryAutonomous = dry.AutonomousPositionCount;
            _dryJump = dry.JumpCount;
            dry.Tap = Trace;
        }

        var structures = services.CellStructures;
        _cells = Task.Run(() => LandblockCellSet.Load(structures, LandblockCoord.Holtburg));
        Name = "PlayerWalkChecks";
        ProcessPriority = Priority;
    }

    /// <summary>Installs the checks over the offline player.</summary>
    public static PlayerWalkChecks Install(Node main, GameServices services, WorldRoot world, PlayerController player)
    {
        ArgumentNullException.ThrowIfNull(main);
        var checks = new PlayerWalkChecks(services, world, player);
        main.AddChild(checks);
        Log.Info("walkcheck", "offline-player checks: the real controller driven through its intents, in the explore scene; they start when the world around the player is complete");
        return checks;
    }

    /// <inheritdoc/>
    public PlayerMovementController Controller => _player.Controller;

    /// <inheritdoc/>
    public PhysicsWorld World => _player.Walker.World;

    /// <inheritdoc/>
    public MovementTrace Trace { get; }

    /// <inheritdoc/>
    public LandblockCellSet Cells => _cells.Result;

    /// <inheritdoc/>
    public bool IsReady => !_player.Placing && _cells.IsCompletedSuccessfully;

    /// <inheritdoc/>
    public double FrameSeconds { get; private set; }

    /// <inheritdoc/>
    public CameraPose? Camera => _player.Camera is { } camera && camera.Pose.Viewer.CellId != 0 ? camera.Pose : null;

    /// <inheritdoc/>
    public float ViewerRadius => ChaseCamera.ViewerRadius;

    /// <inheritdoc/>
    public bool FirstPerson
    {
        get => _player.CameraModel.InHead;
        set
        {
            var model = _player.CameraModel;
            if (value && !model.InHead)
            {
                model.SetInHead();
            }
            else if (!value && model.InHead)
            {
                model.SetDefaultOffsets();
            }
        }
    }

    /// <inheritdoc/>
    public void Teleport(in CellFrame frame) => _player.Teleport(frame);

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_done)
        {
            return;
        }

        FrameSeconds = delta;
        _phaseSeconds += delta;
        try
        {
            switch (_phase)
            {
                case Phase.Settle:
                    Settle();
                    break;
                case Phase.Run:
                    Run(delta);
                    break;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or AggregateException)
        {
            Log.Exception("walkcheck", "offline-player checks crashed", ex);
            Finish(crashed: true);
        }
    }

    private void Settle()
    {
        var ready = IsReady && _readiness.IsComplete;
        _settled = ready ? _settled + 1 : 0;
        if (_settled < SettleFrames && _phaseSeconds < SettleTimeoutSeconds)
        {
            return;
        }

        if (!ready)
        {
            Record("world-ready", passed: false, $"the world around the player was not complete after {_phaseSeconds:F0} s ({_readiness.Describe()}; placing={_player.Placing}; cells={_cells.Status})");
            Finish(crashed: false);
            return;
        }

        var timer = Stopwatch.StartNew();
        _suite = WalkCheckSuite.Plan(this);
        Log.Info("walkcheck", "routes planned", ("plan_ms", timer.Elapsed.TotalMilliseconds), ("checks", _suite.Scenarios.Count), ("names", string.Join(", ", _suite.Scenarios.Select(s => s.Name))));
        _phase = Phase.Run;
        _phaseSeconds = 0;
        _skipNextFrame = true; // the frame after the planning carries its cost in its delta
    }

    private void Run(double delta)
    {
        var suite = _suite!;
        if (_skipNextFrame)
        {
            _skipNextFrame = false;
        }
        else
        {
            _runFrames++;
            _maxFrameMs = Math.Max(_maxFrameMs, delta * 1000.0);
            if (delta * 1000.0 > WalkScenario.SlowFrameSeconds * 1000.0)
            {
                _slowFrames++;
                Log.Warn("walkcheck", "slow frame", ("ms", delta * 1000.0), ("running", suite.Current?.Name ?? "-"), ("frame", _runFrames));
            }
        }

        suite.Tick(_player.Controller.Now);
        for (; _reported < suite.Verdicts.Count; _reported++)
        {
            var verdict = suite.Verdicts[_reported];
            Record(verdict.Name, verdict.Passed, verdict.Detail);
        }

        if (suite.Done)
        {
            Finish(crashed: false);
        }
        else if (_phaseSeconds > RunTimeoutSeconds)
        {
            Record("run-time", passed: false, $"the checks did not finish in {RunTimeoutSeconds:F0} s (running {suite.Current?.Name})");
            Finish(crashed: false);
        }
    }

    private void Finish(bool crashed)
    {
        _done = true;
        if (!crashed)
        {
            var stats = _player.Controller.Stats;
            Record(
                "frame-times",
                _slowFrames == 0 && _runFrames > 0,
                string.Create(CultureInfo.InvariantCulture, $"{_runFrames} frames from the first check to the last (the planning frame not counted), max {_maxFrameMs:F1} ms, {_slowFrames} over {WalkScenario.SlowFrameSeconds * 1000.0:F0} ms; the player's own work at most {_player.MaxOwnMilliseconds:F2} ms in a frame"));
            if (_dryRun is { } dry)
            {
                var moveToState = dry.MoveToStateCount - _dryMoveToState;
                var autonomous = dry.AutonomousPositionCount - _dryAutonomous;
                var jump = dry.JumpCount - _dryJump;
                var traced = (Trace.Count(SentKind.MoveToState), Trace.Count(SentKind.AutonomousPosition), Trace.Count(SentKind.Jump));
                var holds = (moveToState, autonomous, jump) == traced && moveToState > 0 && autonomous > 0 && jump == 2;
                Record(
                    "dry-run-sink",
                    holds,
                    $"the sink saw {moveToState} MoveToState, {autonomous} AutonomousPosition and {jump} Jump while the checks ran (the trace saw {traced.Item1}, {traced.Item2}, {traced.Item3}; two jumps were made); it sent none of them anywhere; the controller's own totals are {stats.MoveToStateSent}, {stats.AutonomousPositionSent}, {stats.JumpSent}");
            }
            else
            {
                Record("dry-run-sink", passed: false, "the player's movement sink is not the dry-run sink");
            }
        }

        var failed = crashed || _results.Any(r => !r.Passed);
        Log.Info(
            "walkcheck",
            failed ? "OFFLINE-PLAYER CHECKS FAILED" : "all offline-player checks hold",
            ("pass", _results.Count(r => r.Passed)),
            ("fail", _results.Count(r => !r.Passed)),
            ("max_frame_ms", _maxFrameMs));
        Log.FlushToGodot();
        GetTree().Quit(failed ? 1 : 0);
    }

    private void Record(string name, bool passed, string detail)
    {
        _results.Add((name, passed, detail));
        if (passed)
        {
            Log.Info("walkcheck", $"PASS {name}", ("detail", detail));
        }
        else
        {
            Log.Error("walkcheck", $"FAIL {name}", ("detail", detail));
        }
    }
}
