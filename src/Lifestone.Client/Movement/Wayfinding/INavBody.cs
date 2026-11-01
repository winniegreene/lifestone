using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>
/// What the runner (<c>NavRunner</c>, P5a design 3.2) reads of the player's body: a small read-only view over
/// <c>PlayerMovementController</c> that both hosts share (the Godot play host and the headless <c>OfflineWalker</c>), so the
/// runner has no engine and no controller type in its signature. Writes never go through it: the runner moves the
/// player only through <c>IActionApi.Move</c> (the human's keys, R2). Game thread only; every member is a plain read.
/// </summary>
public interface INavBody
{
    /// <summary>The player's cell and frame after the last physics step.</summary>
    CellFrame Position { get; }

    /// <summary>The player's cell id (<see cref="Position"/>'s <c>CellId</c>); 0 before the body is placed.</summary>
    uint CellId { get; }

    /// <summary>True while the player stands on walkable ground (retail Contact and OnWalkable): a jump or a fall is false.</summary>
    bool OnGround { get; }

    /// <summary>
    /// True while the server is driving the player: retail's <c>controlled_by_server</c> with a MoveTo or TurnTo under way
    /// (<c>CPhysicsObj::IsMovingTo</c>, 11.6096 0x0050F5E0), e.g. ACE's own melee or Use approach. While it is true a movement
    /// key would take control from the server (<c>TakeControlFromServer</c>), so the runner presses nothing (state Yielding).
    /// </summary>
    bool ServerDriving { get; }

    /// <summary>
    /// How the last server MoveTo or TurnTo ended (<c>PlayerMovementController.LastServerMoveToEnd</c>: <c>arrived</c>, <c>cancelled (0xNNNN Name)</c> or <c>stuck</c>, the
    /// controller's <c>TODO(m5-moveto-stuck)</c> rule); null while one is under way, before the first, or for a body that does not say (M5 item 5 F6).
    /// </summary>
    string? ServerMoveToEnd => null;

    /// <summary>
    /// The controller's clock in seconds (the latest time it was stepped to): the time base of the player's physics, the one
    /// the runner's stuck timers use so a headless run at any tick rate and a 30 Hz play run agree.
    /// </summary>
    double Now { get; }
}
