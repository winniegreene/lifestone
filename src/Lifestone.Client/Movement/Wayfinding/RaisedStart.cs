using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>Where a <see cref="RaisedStart"/> is.</summary>
public enum RaisedStartState
{
    /// <summary>Trying the headings: step again.</summary>
    Running,

    /// <summary>The body stands on the room's floor: plan the leg from where it is now.</summary>
    Down,

    /// <summary>No heading took the body down within the tries (<see cref="RaisedStart.Failure"/> names it).</summary>
    Failed,
}

/// <summary>
/// Steps a body off a raised start before a leg plans (M4 P5b fix FC-a2). Live (gate round 1, the headless Brawler): ACE's walk-to-use MoveToObject toward Renald the Elder (the vendor station) carried the
/// journey's body, which follows the server's MoveTo (P5b-R1), up onto the end of his counter (cell 0xA9B4013F, z 94.94 over the room's floor at 94.00, static 0x02000121 0.5 m away); the next legs planned
/// from there straight west to the front room's door, a way off the counter's top the physics refuses (the body moved 0.01 m), and every rung of the runner's ladder failed: "stuck after 3 plans and 12 rungs"
/// for the walk to Thelnoth and for the chests. Both are retail's physics (docs/physics.md §8, "props are stairs"; <c>RenaldCounterStepDatTests</c>): the 0.5 m box at the counter's end
/// (static 0x02000121) takes the body up in two step-ups under 0.6 m, and the step down off the counter's edge needs ground under the sphere's half-radius footprint. Here, when the plan's start stands more than <see cref="RaisedMetres"/> above the plan's next point in the same room (<see cref="FloorBelow"/>), the body is
/// turned to each heading in turn (the bearing to that point first, then 45 degrees either side of it, wider each time, the way back last) and walked forward through the player's keys
/// (<see cref="IActionApi.Move"/>, as the runner presses them) for at most <see cref="WalkSeconds"/>, until it stands within <see cref="FloorToleranceMetres"/> of the floor; then the leg plans from there.
/// No heading down within the tries ends it <see cref="RaisedStartState.Failed"/> with the name <c>raised-start-unreachable:&lt;cell&gt;</c> (the journey's leg logs it and plans from where the body stands). Lifestone's own rule;
/// retail's player steps off by hand.
/// </summary>
public sealed class RaisedStart
{
    /// <summary>A start this far above the plan's next point in the same room is raised (on a counter or a table, not on the floor), in metres. Not retail: Lifestone's own.</summary>
    public const float RaisedMetres = 0.5f;

    /// <summary>The body is down once within this of the floor's height, in metres. Not retail: Lifestone's own.</summary>
    public const float FloorToleranceMetres = 0.3f;

    /// <summary>The longest a heading is walked once faced, in seconds. Not retail: Lifestone's own.</summary>
    public const double WalkSeconds = 1.0;

    /// <summary>The longest the turn to a heading takes before that heading is walked anyway, in seconds. Not retail: Lifestone's own.</summary>
    public const double TurnSeconds = 2.0;

    /// <summary>Facing within this of the heading ends the turn, in degrees. Not retail: Lifestone's own.</summary>
    public const float FacedDegrees = 8f;

    private static readonly float[] Offsets = [0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f];

    private readonly MoveIntents _move;
    private readonly Func<CellFrame> _position;
    private readonly ILogSink _log;
    private readonly string _category;
    private readonly uint _cell;
    private readonly float _floorZ;
    private readonly float _bearing;
    private int _try = -1;
    private double _phaseStart;
    private bool _walking;
    private int _turn;
    private bool _forward;

    /// <summary>A step-off for a body raised over <paramref name="floorZ"/>, first toward <paramref name="toward"/> (the plan's next point).</summary>
    /// <param name="move">The player's keys.</param>
    /// <param name="position">The body's position.</param>
    /// <param name="floorZ">The room floor's height (the plan's next point's).</param>
    /// <param name="toward">The plan's next point, whose bearing is tried first.</param>
    /// <param name="log">The log.</param>
    /// <param name="category">The log category.</param>
    public RaisedStart(MoveIntents move, Func<CellFrame> position, float floorZ, Vector3 toward, ILogSink log, string category)
    {
        _move = move ?? throw new ArgumentNullException(nameof(move));
        _position = position ?? throw new ArgumentNullException(nameof(position));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _category = category;
        var at = position();
        _cell = at.CellId;
        _floorZ = floorZ;
        _bearing = HeadingOf(toward - at.Origin);
    }

    /// <summary>The named failure (<c>raised-start-unreachable:&lt;cell&gt;</c>), or null.</summary>
    public string? Failure { get; private set; }

    /// <summary>The headings tried so far.</summary>
    public int Tries => _try + 1;

    /// <summary>
    /// The floor's height under a raised start: the height of <paramref name="plan"/>'s first later point in <paramref name="start"/>'s room when the start stands more than <see cref="RaisedMetres"/> above it, else null
    /// (an outdoor start, a failed plan, a start on the floor).
    /// </summary>
    /// <param name="plan">The plan from <paramref name="start"/>.</param>
    /// <param name="start">Where the body stands.</param>
    /// <param name="next">The plan's point whose height it is.</param>
    public static float? FloorBelow(NavPath plan, in CellFrame start, out Vector3 next)
    {
        ArgumentNullException.ThrowIfNull(plan);
        next = default;
        if (plan.Status != NavPathStatus.Found || LandDefs.IsOutdoorCell(start.CellId))
        {
            return null;
        }

        for (var i = 1; i < plan.Waypoints.Count; i++)
        {
            var w = plan.Waypoints[i].At;
            if (w.CellId != start.CellId)
            {
                return null;
            }

            if (Vector2.Distance(new Vector2(w.Origin.X, w.Origin.Y), new Vector2(start.Origin.X, start.Origin.Y)) < 0.25f)
            {
                continue; // the start's own point again
            }

            next = w.Origin;
            return start.Origin.Z - w.Origin.Z > RaisedMetres ? w.Origin.Z : null;
        }

        return null;
    }

    /// <summary>Advances the step-off: call every frame while <see cref="RaisedStartState.Running"/>; no key stays held once it ends.</summary>
    /// <param name="now">The frame time.</param>
    public RaisedStartState Step(double now)
    {
        if (Failure is not null)
        {
            return RaisedStartState.Failed;
        }

        var at = _position();
        if (MathF.Abs(at.Origin.Z - _floorZ) <= FloorToleranceMetres)
        {
            Release();
            _log.Info(_category, string.Create(CultureInfo.InvariantCulture, $"raised start: the body stepped down to the floor at 0x{at.CellId:X8} [{at.Origin.X:0.00} {at.Origin.Y:0.00} {at.Origin.Z:0.00}] after {Tries} heading(s); the leg plans from here"));
            return RaisedStartState.Down;
        }

        if (_try < 0 || (_walking && now - _phaseStart >= WalkSeconds))
        {
            if (++_try >= Offsets.Length)
            {
                Release();
                Failure = string.Create(CultureInfo.InvariantCulture, $"raised-start-unreachable:0x{_cell:X8}: no heading took the body down to the floor at z {_floorZ:0.00} (it stands at z {at.Origin.Z:0.00}) in {Offsets.Length} tries");
                _log.Warn(_category, "raised start: " + Failure);
                return RaisedStartState.Failed;
            }

            if (_try == 0)
            {
                _log.Info(_category, string.Create(CultureInfo.InvariantCulture, $"raised start: the body stands {at.Origin.Z - _floorZ:0.00} m above the room's floor in 0x{_cell:X8} (on a counter or a table); it steps off before the leg plans"));
            }

            SetForward(false);
            _walking = false;
            _phaseStart = now;
        }

        var want = Normalize(_bearing + Offsets[_try]);
        if (!_walking)
        {
            var delta = Signed(want - RetailFrames.GetHeading(at.Frame));
            if (MathF.Abs(delta) <= FacedDegrees || now - _phaseStart >= TurnSeconds)
            {
                SetTurn(0);
                SetForward(true);
                _walking = true;
                _phaseStart = now;
            }
            else
            {
                SetTurn(delta > 0 ? 1 : -1); // headings grow clockwise: a positive difference is a right turn
            }
        }

        return RaisedStartState.Running;
    }

    /// <summary>Releases every key the step-off holds.</summary>
    public void Release()
    {
        SetTurn(0);
        SetForward(false);
    }

    private static float HeadingOf(Vector3 d) => Normalize(MathF.Atan2(d.X, d.Y) * 180f / MathF.PI);

    private static float Normalize(float degrees) => ((degrees % 360f) + 360f) % 360f;

    private static float Signed(float degrees)
    {
        var d = Normalize(degrees);
        return d > 180f ? d - 360f : d;
    }

    private void SetForward(bool on)
    {
        if (on != _forward)
        {
            _forward = on;
            _move.Command(MotionCommands.WalkForward, on, ActionSource.Automation);
        }
    }

    private void SetTurn(int turn)
    {
        if (turn == _turn)
        {
            return;
        }

        if (_turn != 0)
        {
            _move.Command(_turn > 0 ? MotionCommands.TurnRight : MotionCommands.TurnLeft, false, ActionSource.Automation);
        }

        _turn = turn;
        if (turn != 0)
        {
            _move.Command(turn > 0 ? MotionCommands.TurnRight : MotionCommands.TurnLeft, true, ActionSource.Automation);
        }
    }
}
