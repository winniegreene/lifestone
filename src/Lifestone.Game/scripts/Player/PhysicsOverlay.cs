using Godot;

namespace Lifestone.Game.Player;

/// <summary>
/// <c>--physics-overlay</c>: the player's physics as text in the corner of the window (M2 G5). It shows what the physics and the command
/// interpreter hold now: the cell, position, speed, contact plane and transient state, the keys held (run, autorun, the jump bar), the movement
/// messages the dry-run sink would have sent (and the last one), the input events, and the camera sweep.
/// </summary>
public sealed partial class PhysicsOverlay : Label
{
    private readonly PlayerController _player;
    private readonly ChaseCamera _camera;
    private double _since;

    public PhysicsOverlay(PlayerController player, ChaseCamera camera)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(camera);
        _player = player;
        _camera = camera;
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        _since += delta;
        if (_since < 0.1)
        {
            return;
        }

        _since = 0;
        Text = _player.DescribePhysics() + "\n  " + _camera.Describe();
    }
}
