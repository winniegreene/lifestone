using Lifestone.Data.Physics.Objects;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>
/// <see cref="INavBody"/> over a <see cref="PlayerMovementController"/>: the same view in the Godot play host
/// (<c>PlayerRig.Controller</c>) and the headless scenarios (<c>OfflineWalker.Controller</c>). Allocates nothing per read.
/// </summary>
public sealed class ControllerNavBody : INavBody
{
    private readonly PlayerMovementController _controller;

    /// <param name="controller">The player's movement controller.</param>
    public ControllerNavBody(PlayerMovementController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _controller = controller;
    }

    /// <summary>The controller read.</summary>
    public PlayerMovementController Controller => _controller;

    /// <inheritdoc/>
    public CellFrame Position => _controller.Position;

    /// <inheritdoc/>
    public uint CellId => _controller.Position.CellId;

    /// <inheritdoc/>
    public bool OnGround => _controller.OnGround;

    /// <inheritdoc/>
    public bool ServerDriving => _controller.ControlledByServer && _controller.IsMovingTo;

    /// <inheritdoc/>
    public string? ServerMoveToEnd => _controller.LastServerMoveToEnd;

    /// <inheritdoc/>
    public double Now => _controller.Now;

    /// <inheritdoc/>
    public float? JumpReachMetres => Routes.JumpReach.FlightMetres(_controller.Player.Weenie);

    /// <inheritdoc/>
    public double? JumpBarLevel => _controller.Jump.Pending ? _controller.Jump.GetPowerBarLevel(_controller.Now, _controller.Motion.Interpreted.CurrentStyle) : 0.0;

    /// <inheritdoc/>
    public float? RunSpeedMetresPerSecond => _controller.Player.Weenie is { } weenie && weenie.TryGetRunRate(out var rate) ? Data.Motion.MotionInterpreter.RunAnimSpeed * rate : null;

    /// <inheritdoc/>
    public bool? StandingLongJump => _controller.Motion.StandingLongJump;

    /// <inheritdoc/>
    public float RunSpeed => _controller.Motion.GetMaxSpeed();

    /// <inheritdoc/>
    public NavBodyContact Contact => _controller.Player.LastTransitionObstacle.Kind switch
    {
        TransitionObstacleKind.Environment => NavBodyContact.Wall,
        TransitionObstacleKind.Object => NavBodyContact.Object,
        TransitionObstacleKind.Step => NavBodyContact.Step,
        _ => NavBodyContact.None,
    };

    /// <inheritdoc/>
    public uint ContactObject => _controller.Player.LastTransitionObstacle.Object?.Id ?? 0;

    /// <inheritdoc/>
    public System.Numerics.Vector3 ContactNormal => _controller.Player.LastTransitionObstacle.Normal;
}
