using System.Numerics;
using Lifestone.Data.World;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// An object entered the session's object table (<see cref="GameState.Objects"/>): F745 CreateObject for an unknown
/// object or a newer instance, or F7DB UpdateObject (a forced re-create). Its data is in
/// <see cref="WorldObjectTable.TryGet"/>. Re-expresses <c>SmartBox::HandleCreateObject</c> (11.4186 0x00454C80,
/// 11.6096 0x00454D20) and <c>SmartBox::HandleUpdateObject</c> (11.4186 0x00455580, 11.6096 0x00455620).
/// </summary>
/// <remarks>
/// Messages that arrived for the object before it existed were parked and are applied right after this event, in
/// arrival order, each with its own event (<c>SmartBox::ProcessObjectNetBlobs</c>, 11.4186 0x00454B20, 11.6096
/// 0x00454BC0).
/// </remarks>
/// <param name="Guid">The object.</param>
/// <param name="IsSelf">True for the player's own object.</param>
/// <param name="InWorld">True when it has a position or a parent; false for an object held nowhere visible (inventory).</param>
/// <param name="Recreated">
/// True when the object already existed and was replaced (F7DB, or a CreateObject with a newer instance): drop
/// everything held for the guid and build it again. No <see cref="ObjectDeleted"/> precedes it.
/// </param>
public readonly record struct ObjectCreated(uint Guid, bool IsSelf, bool InWorld, bool Recreated) : IGameEvent;

/// <summary>
/// A CreateObject for an object already known with the same instance refreshed its descriptions
/// (<see cref="WorldObject.Created"/>). Changes of position, motion, state, vector and parent that the same message
/// carried follow as their own events.
/// </summary>
/// <param name="Guid">The object.</param>
/// <param name="Changes">Which descriptions changed.</param>
public readonly record struct ObjectUpdated(uint Guid, ObjectChanges Changes) : IGameEvent;

/// <summary>What an <see cref="ObjectUpdated"/> changed.</summary>
[Flags]
public enum ObjectChanges
{
    /// <summary>Nothing (the message carried the same descriptions).</summary>
    None = 0,

    /// <summary>The visual description (<see cref="WorldObject.Model"/>).</summary>
    Model = 1,

    /// <summary>The physics description (setup, tables, scale, translucency and the rest).</summary>
    Physics = 2,

    /// <summary>The weenie description (name, type, icon, flags).</summary>
    Weenie = 4,
}

/// <summary>
/// F747 DeleteObject removed an object (<c>SmartBox::HandleDeleteObject</c>, 11.4186 0x00451EA0, 11.6096 0x00451EE0).
/// A delete for the player's own object is ignored, as retail does, and reported as <see cref="MessageDropped"/>.
/// </summary>
/// <param name="Guid">The object.</param>
public readonly record struct ObjectDeleted(uint Guid) : IGameEvent;

/// <summary>
/// F748 UpdatePosition was accepted, classified as retail's <c>SmartBox::HandleReceivedPosition</c> does (11.4186
/// 0x00453FD0, 11.6096 0x00454070). The reducer only classifies; physics (the player) and the avatars (everything else)
/// act on the verdict. Stale updates are not published as moves; they are <see cref="MessageDropped"/>.
/// </summary>
/// <param name="Guid">The object.</param>
/// <param name="Position">The position exactly as the server sent it (the rotation not normalised).</param>
/// <param name="Verdict">How the update applies.</param>
/// <param name="TeleportAdvanced">True when the update's teleport sequence was newer than the object's.</param>
/// <param name="HasContact">The update's Grounded flag (the object is on the ground).</param>
/// <param name="Velocity">The velocity, when the update carried one.</param>
/// <param name="Placement">The placement id, when the update carried one.</param>
public readonly record struct ObjectMoved(
    uint Guid,
    CellFrame Position,
    PositionVerdict Verdict,
    bool TeleportAdvanced,
    bool HasContact,
    Vector3? Velocity,
    uint? Placement) : IGameEvent
{
    /// <summary>True for the player's own object (every verdict but <see cref="PositionVerdict.Remote"/>).</summary>
    public bool IsSelf => Verdict != PositionVerdict.Remote;
}

/// <summary>
/// How an accepted position update applies (<c>SmartBox::HandleReceivedPosition</c>, 11.4186 0x00453FD0, 11.6096
/// 0x00454070, with the sequence rules of <c>CPhysicsObj::is_newer</c> and <c>newer_event</c>).
/// </summary>
public enum PositionVerdict
{
    /// <summary>
    /// The player, with a newer force-position sequence and a teleport sequence that is not older: the server corrects
    /// the player's position (a blip; retail keeps the current heading). Every one counts as a correction (DoD 8).
    /// </summary>
    ForceBlip,

    /// <summary>The player, with a newer position sequence and a newer teleport sequence: the player arrived from a teleport.</summary>
    SelfTeleport,

    /// <summary>The player, with a newer position sequence and the same teleport sequence: the server echoing an accepted move.</summary>
    SelfEcho,

    /// <summary>Any other object, with a newer position sequence.</summary>
    Remote,
}

/// <summary>
/// F74C UpdateMotion was accepted (or a CreateObject carried movement). The motion is stored on
/// <see cref="WorldObject.Motion"/> for every object, the player's own included. Whether to act on the player's own
/// motion is the motion owners' rule (avatars and physics, following retail): an autonomous echo of the player's input
/// is not replayed, while a server-controlled MoveTo or TurnTo for the player (types 6-9, as ACE sends for a Use out of
/// range) is acted on.
/// </summary>
/// <param name="Guid">The object.</param>
/// <param name="IsSelf">True for the player's own object.</param>
/// <param name="Autonomous">True when the motion is the client's own (an echo of the player's input).</param>
/// <param name="Motion">The motion, as the wire carried it (commands are raw u16 low words; map them with
/// <c>Lifestone.Data.Animation.MotionCommandTable</c>).</param>
public readonly record struct ObjectMotionChanged(uint Guid, bool IsSelf, bool Autonomous, ServerMotionState Motion) : IGameEvent;

/// <summary>F74E VectorUpdate was accepted (<c>SmartBox::HandleVectorUpdate</c>, 11.4186 0x00453480, 11.6096 0x004534C0).</summary>
/// <param name="Guid">The object.</param>
/// <param name="Velocity">The new velocity, metres per second in AC axes.</param>
/// <param name="Omega">The new angular velocity, radians per second.</param>
public readonly record struct ObjectVectorChanged(uint Guid, Vector3 Velocity, Vector3 Omega) : IGameEvent;

/// <summary>
/// F74B SetState was accepted and changed the object's physics state (<c>SmartBox::HandleSetState</c>, 11.4186
/// 0x004533E0, 11.6096 0x00453420; <c>SmartBox::DoSetState</c>, 11.4186 0x004520D0, 11.6096 0x00452110). The hidden
/// transition (portal's <c>HiddenTransition</c>) reacts only to a change of <see cref="PhysicsState.Hidden"/>.
/// </summary>
/// <param name="Guid">The object.</param>
/// <param name="IsSelf">True for the player's own object.</param>
/// <param name="Previous">The state before.</param>
/// <param name="State">The state now.</param>
public readonly record struct ObjectStateChanged(uint Guid, bool IsSelf, PhysicsState Previous, PhysicsState State) : IGameEvent;

/// <summary>
/// F749 ParentEvent was accepted: <paramref name="Child"/> is now held by <paramref name="Parent"/> (a wielded item, a
/// carried torch; <c>SmartBox::HandleParentEvent</c>, 11.4186 0x004535D0, 11.6096 0x00453610). The child has no
/// position of its own while parented.
/// </summary>
/// <param name="Child">The held object.</param>
/// <param name="Parent">The holder.</param>
/// <param name="Location">The holding location (retail <c>ParentLocation</c>).</param>
/// <param name="Placement">The child's placement.</param>
public readonly record struct ObjectParented(uint Child, uint Parent, int Location, int Placement) : IGameEvent;

/// <summary>
/// F74A PickupEvent was accepted: the object left the world (picked up into a container; <c>SmartBox::HandlePickupEvent</c>,
/// 11.4186 0x00453530, 11.6096 0x00453570). It stays in the table with <see cref="WorldObject.InWorld"/> false.
/// </summary>
/// <param name="Guid">The object.</param>
public readonly record struct ObjectPickedUp(uint Guid) : IGameEvent;

/// <summary>
/// F625 ObjDescEvent was accepted: the object's visual description changed (<c>SmartBox::HandleObjDescEvent</c>,
/// 11.4186 0x00453340, 11.6096 0x00453380).
/// </summary>
/// <param name="Guid">The object.</param>
/// <param name="Model">The new visual description (also <see cref="WorldObject.Model"/>).</param>
public readonly record struct ObjectDescChanged(uint Guid, ModelData Model) : IGameEvent;

/// <summary>
/// F750 Sound: the server asked an object to play a sound from its sound table (<c>SmartBox::HandleSoundEvent</c>, 11.4186
/// 0x00451FC0, 11.6096 0x00452000, which calls <c>CPhysicsObj::play_sound</c>). Like retail, a sound for an object that
/// does not exist yet is parked and played when the object is created. Nothing in the read model changes.
/// </summary>
/// <param name="Guid">The object.</param>
/// <param name="SoundId">The sound type, a value of the object's sound table (retail <c>SoundType</c>).</param>
/// <param name="Volume">The volume, 0 to 1.</param>
public readonly record struct ObjectSoundPlayed(uint Guid, uint SoundId, float Volume) : IGameEvent;

/// <summary>
/// F755 PlayEffect: the server asked an object to play a physics script by type (<c>SmartBox::HandlePlayScriptType</c>,
/// 11.4186 0x00452070, 11.6096 0x004520B0, which calls <c>CPhysicsObj::play_script</c>). Like retail, a script for an
/// object that does not exist yet is parked and played when the object is created. Nothing in the read model changes.
/// </summary>
/// <param name="Guid">The object.</param>
/// <param name="ScriptType">The script type (retail <c>PScriptType</c>), resolved through the object's physics-effect table.</param>
/// <param name="Speed">The intensity (retail's <c>mod</c>).</param>
public readonly record struct ObjectScriptPlayed(uint Guid, uint ScriptType, float Speed) : IGameEvent;

/// <summary>
/// F751 PlayerTeleport was accepted (its sequence is not older than the player's: <c>SmartBox::HandlePlayerTeleport</c>,
/// 11.4186 0x00452150, 11.6096 0x00452190): the player is about to be moved, and portal space starts.
/// </summary>
/// <param name="TeleportSequence">The message's teleport sequence.</param>
public readonly record struct PlayerTeleportAnnounced(ushort TeleportSequence) : IGameEvent;

/// <summary>The player's travel state changed (<see cref="SelfState.Travel"/>).</summary>
/// <param name="Previous">The state before.</param>
/// <param name="Travel">The state now.</param>
public readonly record struct SelfTravelChanged(SelfTravel Previous, SelfTravel Travel) : IGameEvent;

/// <summary>
/// Where the player is between entering the world, teleports and materialising (A0's view of retail's SmartBox flags;
/// the portal track's <c>TeleportProgress</c> keeps retail's own).
/// </summary>
public enum SelfTravel
{
    /// <summary>No player object: before the player's CreateObject, or after logoff.</summary>
    NotInWorld,

    /// <summary>The player's CreateObject arrived with a position (entering the world); it is hidden until LoginComplete.</summary>
    Arriving,

    /// <summary>An F751 PlayerTeleport was accepted; the destination position has not arrived yet.</summary>
    TeleportAnnounced,

    /// <summary>A <see cref="PositionVerdict.SelfTeleport"/> position arrived: the player is at the destination, still hidden.</summary>
    TeleportArrived,

    /// <summary>LoginComplete was sent (once per arrival); the server will clear the Hidden state.</summary>
    Materialising,

    /// <summary>A SetState cleared Hidden on the player: the player is in the world and may move.</summary>
    Present,
}
