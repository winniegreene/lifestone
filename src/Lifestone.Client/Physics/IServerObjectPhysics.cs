using Lifestone.Data.World;

namespace Lifestone.Client.Physics;

/// <summary>
/// What a server object's physics host needs from one CreateObject (M5 D70): the object, its Setup and scale, its
/// PhysicsState bits and what its weenie description says about it.
/// </summary>
/// <param name="Guid">The object id.</param>
/// <param name="SetupId">The Setup (PhysicsDesc CSetup).</param>
/// <param name="Scale">The scale (ObjScale), 1 when absent.</param>
/// <param name="PhysicsState">The PhysicsState bits (retail <c>PhysicsState</c>: Ethereal 0x4, ReportCollisions 0x8,
/// IgnoreCollisions 0x10, Hidden 0x4000, ...).</param>
/// <param name="IsCreature">The weenie is a creature (ItemType 0x10): never stepped onto (docs/physics.md §10).</param>
/// <param name="IsSelf">The client's own player, whose physics object is the player rig's: the host makes none.</param>
/// <param name="DescriptionFlags">The weenie description's <c>ObjectDescriptionFlags</c> (BF_PLAYER 0x8, ...), or null
/// when the caller does not have them (the avatar set passes its create's flags; only a caller without a weenie description, a test or the stress source, gives null).</param>
public readonly record struct ServerObjectDescription(
    uint Guid,
    uint SetupId,
    float Scale,
    uint PhysicsState,
    bool IsCreature,
    bool IsSelf,
    uint? DescriptionFlags);

/// <summary>
/// The feed of a server-object physics host (M5 D70): the avatar set calls it from its create, move, state, parent,
/// pickup and delete paths in the game; the headless body drives it from the session's object table (slice h2). Every
/// call for an object the host keeps nothing for (no collision shape, never created, the player's own) does nothing.
/// </summary>
/// <remarks>Calls are made on the thread that owns the player's <c>PhysicsWorld</c>.</remarks>
public interface IServerObjectPhysics
{
    /// <summary>
    /// A CreateObject (or a recreate, which replaces the earlier object): the host makes a physics object when the
    /// Setup has collision and places it at <paramref name="position"/>, or keeps it out of the world (null: held or
    /// contained).
    /// </summary>
    void Create(in ServerObjectDescription description, CellFrame? position);

    /// <summary>The object is at <paramref name="position"/> now (a server move, or where its avatar stepped to).</summary>
    void Move(uint guid, in CellFrame position);

    /// <summary>The object's PhysicsState bits changed (SetState: a door opening turns Ethereal).</summary>
    void SetState(uint guid, uint physicsState);

    /// <summary>The object left the world but is still known: picked up into a container, parented (wielded), or
    /// out of its cell. A later <see cref="Move"/> puts it back.</summary>
    void LeaveWorld(uint guid);

    /// <summary>The object is gone (DeleteObject): its physics object is destroyed.</summary>
    void Remove(uint guid);

    /// <summary>Once per frame: what retail's update of the object would do that the host must do itself (a door
    /// turning solid again while something stands in it is retried, CheckEthereal).</summary>
    void Tick();
}
