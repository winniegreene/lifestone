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
    public double Now => _controller.Now;
}
