using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement;

/// <summary>
/// The movement sink of the explore-mode player (M2 G5): the messages the retail command interpreter would send to the server
/// (MoveToState, AutonomousPosition, Jump) are counted and written to the log as they would go out, and nothing is sent anywhere.
/// Online, <see cref="PlayerMovementController"/> is given the session's sink instead and the controller does not change.
/// </summary>
/// <remarks>
/// Each message is one <c>movement</c> info line starting <c>dry run</c>, so a log shows the cadence (<c>docs/physics.md</c> §12): a
/// MoveToState after every key event, an AutonomousPosition about once a second while the player moves. The last message of each kind is
/// kept for the physics overlay. Main thread only.
/// </remarks>
public sealed class DryRunMovementSink : IMovementSink
{
    private const string Category = "movement";
    private readonly ILogSink _log;

    /// <param name="log">Where the messages are written.</param>
    public DryRunMovementSink(ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <summary>
    /// Where each message is also passed on, after it is counted and logged (the walk checks' <c>MovementTrace</c>); null for none. Nothing is sent
    /// to a server by either.
    /// </summary>
    public IMovementSink? Tap { get; set; }

    /// <summary>MoveToState messages that would have been sent.</summary>
    public long MoveToStateCount { get; private set; }

    /// <summary>AutonomousPosition messages that would have been sent.</summary>
    public long AutonomousPositionCount { get; private set; }

    /// <summary>Jump messages that would have been sent.</summary>
    public long JumpCount { get; private set; }

    /// <summary>The last message, as one line for the overlay (empty before the first).</summary>
    public string Last { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public void MoveToState(RawMotionState raw, in CellFrame position, bool contact, bool standingLongJump)
    {
        ArgumentNullException.ThrowIfNull(raw);
        MoveToStateCount++;
        Last = string.Create(
            CultureInfo.InvariantCulture,
            $"MoveToState forward 0x{raw.ForwardCommand:X8} x{raw.ForwardSpeed:0.##} side 0x{raw.SideStepCommand:X8} turn 0x{raw.TurnCommand:X8} hold {raw.CurrentHoldKey} contact {contact} longjump {standingLongJump} {Where(position)}");
        _log.Info(Category, "dry run: MoveToState (not sent)", new LogField("message", Last), new LogField("count", MoveToStateCount));
        Tap?.MoveToState(raw, position, contact, standingLongJump);
    }

    /// <inheritdoc/>
    public void AutonomousPosition(in CellFrame position, bool contact)
    {
        AutonomousPositionCount++;
        Last = string.Create(CultureInfo.InvariantCulture, $"AutonomousPosition contact {contact} {Where(position)}");
        _log.Info(Category, "dry run: AutonomousPosition (not sent)", new LogField("message", Last), new LogField("count", AutonomousPositionCount));
        Tap?.AutonomousPosition(position, contact);
    }

    /// <inheritdoc/>
    public void Jump(float extent, Vector3 velocity, in CellFrame position)
    {
        JumpCount++;
        Last = string.Create(CultureInfo.InvariantCulture, $"Jump extent {extent:0.###} velocity ({velocity.X:0.0}, {velocity.Y:0.0}, {velocity.Z:0.0}) {Where(position)}");
        _log.Info(Category, "dry run: Jump (not sent)", new LogField("message", Last), new LogField("count", JumpCount));
        Tap?.Jump(extent, velocity, position);
    }

    private static string Where(in CellFrame position) =>
        string.Create(CultureInfo.InvariantCulture, $"at 0x{position.CellId:X8} ({position.Origin.X:0.00}, {position.Origin.Y:0.00}, {position.Origin.Z:0.00}) heading {position.HeadingDegrees:0}");
}
