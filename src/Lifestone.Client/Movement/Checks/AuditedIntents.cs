using System.Globalization;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Checks;

/// <summary>
/// The player's intents (<see cref="IPlayerMovementIntents"/>) as a walk check drives them, passed on to the real controller unchanged, with every call
/// audited against the controller's messages: each press or release of a movement command is exactly one MoveToState
/// (<c>CommandInterpreter::SendMovementEvent</c>), the run key sends none (<c>ACCmdInterp::OnAction</c> for input action 0x32 calls
/// <c>SetHoldRun</c> directly), starting a jump charge sends a MoveToState, and a jump that is not refused sends one Jump.
/// </summary>
public sealed class AuditedIntents : IPlayerMovementIntents, IDisposable
{
    private const int MaxProblems = 20;

    private readonly PlayerMovementController _controller;
    private readonly MovementTrace _trace;

    /// <summary>Audits calls to <paramref name="controller"/>, whose messages <paramref name="trace"/> records.</summary>
    public AuditedIntents(PlayerMovementController controller, MovementTrace trace)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(trace);
        _controller = controller;
        _trace = trace;
        controller.Notice += OnNotice;
    }

    /// <summary>The calls that broke a rule, one line each (at most 20).</summary>
    public List<string> Problems { get; } = [];

    /// <summary>Presses and releases of movement commands passed on.</summary>
    public int Commands { get; private set; }

    /// <summary>Jumps released (DoJump calls with a charge pending).</summary>
    public int Jumps { get; private set; }

    /// <summary>Jump charges or jumps the controller refused (it reports them as notices).</summary>
    public int Refusals { get; private set; }

    /// <inheritdoc/>
    public CellFrame Position => _controller.Position;

    /// <inheritdoc/>
    public bool OnGround => _controller.OnGround;

    /// <inheritdoc/>
    public int ServerMoveToStuckEnds => _controller.ServerMoveToStuckEnds;

    /// <inheritdoc/>
    public string? LastServerMoveToEnd => _controller.LastServerMoveToEnd;

    /// <inheritdoc/>
    public bool? IsHeld(uint command) => _controller.IsHeld(command);

    /// <inheritdoc/>
    public void HandleCommand(uint command, bool start, float speed = 1f)
    {
        var before = _trace.Messages.Count;
        var expected = !(_controller.ControlledByServer && !start); // a release while the server controls the player is dropped
        _controller.HandleCommand(command, start, speed);
        Commands++;
        Expect(before, expected ? 1 : 0, SentKind.MoveToState, $"HandleCommand(0x{command:X8}, {(start ? "press" : "release")})");
    }

    /// <inheritdoc/>
    public void SetHoldRun(bool on)
    {
        var before = _trace.Messages.Count;
        _controller.SetHoldRun(on);
        Expect(before, 0, SentKind.MoveToState, $"SetHoldRun({on})");
    }

    /// <inheritdoc/>
    public void SetHoldSidestep(bool on) => _controller.SetHoldSidestep(on);

    /// <inheritdoc/>
    public void ToggleAutoRun() => _controller.ToggleAutoRun();

    /// <inheritdoc/>
    public void CommenceJump(double now)
    {
        var before = _trace.Messages.Count;
        var refusals = Refusals;
        _controller.CommenceJump(now);
        Expect(before, Refusals == refusals ? 1 : 0, SentKind.MoveToState, "CommenceJump");
    }

    /// <inheritdoc/>
    public void DoJump(double now)
    {
        var before = _trace.Messages.Count;
        var refusals = Refusals;
        var pending = _controller.Jump.Pending;
        _controller.DoJump(now);
        if (pending)
        {
            Jumps++;
            Expect(before, Refusals == refusals ? 1 : 0, SentKind.Jump, "DoJump");
        }
    }

    /// <summary>Stops listening to the controller's notices.</summary>
    public void Dispose() => _controller.Notice -= OnNotice;

    private void OnNotice(MovementNotice notice)
    {
        if (notice.Kind == MovementNoticeKind.JumpRefused)
        {
            Refusals++;
        }
    }

    private void Expect(int before, int count, SentKind kind, string what)
    {
        var sent = _trace.Messages.Count - before;
        var wrongKind = false;
        for (var i = before; i < _trace.Messages.Count; i++)
        {
            wrongKind |= _trace.Messages[i].Kind != kind;
        }

        if ((sent != count || wrongKind) && Problems.Count < MaxProblems)
        {
            Problems.Add(string.Create(CultureInfo.InvariantCulture, $"{what} sent {sent} message(s) (the {kind} count should be {count}) at {_controller.Now:F3} s"));
        }
    }
}
