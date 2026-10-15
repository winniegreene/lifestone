using Lifestone.Data.World;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The player's own state (<see cref="GameState.Self"/>): who the player is, where the server last put it, the
/// sequences the movement messages echo, and the qualities. The player is also in <see cref="GameState.Objects"/> with
/// everything an object has.
/// </summary>
public sealed class SelfState
{
    /// <summary>A state with no player. Only the session creates one.</summary>
    internal SelfState()
    {
    }

    /// <summary>The player's object id from F746 PlayerCreate, or 0 outside the world.</summary>
    public uint Guid { get; internal set; }

    /// <summary>The character's name, or null outside the world.</summary>
    public string? Name { get; internal set; }

    /// <summary>Where the player is between entering the world, teleports and materialising.</summary>
    public SelfTravel Travel { get; internal set; }

    /// <summary>The player's position as the server last set it (its CreateObject, a teleport, an echo or a blip), or null.</summary>
    public CellFrame? Position { get; internal set; }

    /// <summary>The player object's nine sequences as last accepted.</summary>
    public PhysicsTimestamps Stamps { get; internal set; }

    /// <summary>The player object's physics state as last accepted.</summary>
    public PhysicsState PhysicsState { get; internal set; }

    /// <summary>True while the player object is hidden (entering the world or teleporting, until the server unhides it).</summary>
    public bool Hidden => (PhysicsState & PhysicsState.Hidden) != 0;

    /// <summary>
    /// The four sequences every movement message echoes (MoveToState, AutonomousPosition and Jump; net's
    /// <see cref="ObjSeqs"/>): instance, server control, teleport and force position.
    /// </summary>
    public ObjSeqs Sequences => new(Stamps.Instance, Stamps.ServerControl, Stamps.Teleport, Stamps.ForcePosition);

    /// <summary>The player's qualities from PlayerDescription and the private updates (empty until the description arrives).</summary>
    public PlayerQualities Qualities { get; } = new();
}
