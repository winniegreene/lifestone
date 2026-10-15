using Lifestone.Client.Session.Contracts;
using Lifestone.Data.World;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.State;

/// <summary>
/// What the per-object gate decides for a server message about one object. The values are retail's own results of the
/// <c>SmartBox::Handle*</c> functions (1 applied, 2 dropped, 3 ignored, 4 queued), so the reducer's log lines and the
/// retail code read alike.
/// </summary>
internal enum ObjectGateVerdict : byte
{
    /// <summary>The object exists with the message's instance: apply it (each field still checks its own stamp).</summary>
    Apply = 1,

    /// <summary>The message belongs to an older instance of the object: drop it (<c>MessageDropped(OlderInstance)</c>).</summary>
    Drop = 2,

    /// <summary>A delete of the player's own object: ignored (<c>MessageDropped(SelfDelete)</c>).</summary>
    IgnoredSelf = 3,

    /// <summary>
    /// The object is unknown, or the message belongs to a newer instance than the one held: park it until that
    /// instance's CreateObject, then replay it (<c>SmartBox::ProcessObjectNetBlobs</c>).
    /// </summary>
    Park = 4,
}

/// <summary>
/// One of an object's nine sequences, in <see cref="PhysicsTimestamps"/> (wire) order, which is also the index of retail's
/// <c>CPhysicsObj::update_times</c> array: position 0, state 2, vector 3, teleport 4, visual description 7, instance 8.
/// </summary>
internal enum ObjectStamp : byte
{
    /// <summary>Position (UpdatePosition, PickupEvent, ParentEvent's child).</summary>
    Position,

    /// <summary>Movement (UpdateMotion).</summary>
    Movement,

    /// <summary>Physics state (SetState).</summary>
    State,

    /// <summary>Velocity and omega (VectorUpdate).</summary>
    Vector,

    /// <summary>Teleport (PlayerTeleport, UpdatePosition).</summary>
    Teleport,

    /// <summary>Server control (UpdateMotion).</summary>
    ServerControl,

    /// <summary>Force position (UpdatePosition).</summary>
    ForcePosition,

    /// <summary>Visual description (ObjDescEvent).</summary>
    ObjDesc,

    /// <summary>Instance: a change means the object was re-created.</summary>
    Instance,
}

/// <summary>
/// Retail's per-object message gate, which the reducer (ST8) puts in front of every message about an existing object,
/// and the per-field stamp rule behind it. Pure functions over <see cref="Sequence16"/>; allocation-free.
/// </summary>
/// <remarks>
/// <para><b>The instance gate</b> is the head every one of these SmartBox handlers shares (re-expressed from the 11.4186
/// decompile, nothing copied; addresses 11.4186 / 11.6096): <c>HandleSetState</c> (0x004533E0 / 0x00453420),
/// <c>HandleObjDescEvent</c> (0x00453340 / 0x00453380), <c>HandleVectorUpdate</c> (0x00453480 / 0x004534C0),
/// <c>HandlePickupEvent</c> (0x00453530 / 0x00453570), <c>HandleParentEvent</c> (0x004535D0 / 0x00453610),
/// <c>UnpackPositionEvent</c> (0x004542C0 / 0x00454360) and <c>HandleDeleteObject</c> (0x00451EA0 / 0x00451EE0). The
/// object is looked up; when it is missing, or the message's instance stamp is newer than the object's
/// (<c>CPhysicsObj::is_newer</c>, 0x00451AD0 / 0x00451B10, wrap included), the message is queued for the object; when the
/// stamps are equal it is applied; otherwise (an older instance) it is dropped. <c>HandleDeleteObject</c> first ignores
/// a delete of the player. <c>HandleParentEvent</c> gates the parent (the message's instance stamp is the parent's, as
/// ACE's <c>GameMessageParentEvent</c> writes it), then queues the message under the child when the child is unknown.
/// Queued messages are replayed in arrival order right after the object's CreateObject
/// (<c>SmartBox::ProcessObjectNetBlobs</c>, 0x00454B20 / 0x00454BC0); the parking store and its bounds belong to the
/// reducer (ST8).</para>
/// <para><b>The field stamps.</b> An applied message changes a field only when its own stamp is newer than the stored one
/// (<c>CPhysicsObj::newer_event</c>, 0x00451B10 / 0x00451B50, through <see cref="TryAdvance"/>), as retail's
/// <c>SmartBox::DoSetState</c> (0x004520D0 / 0x00452110: the state stamp), <c>DoVectorUpdate</c> (0x004521C0 /
/// 0x00452200: the vector stamp), <c>DoPickupEvent</c> (0x00452240 / 0x00452280: the position stamp),
/// <c>DoParentEvent</c> (0x00452290 / 0x004522D0: the child's position stamp) and <c>UpdateVisualDesc</c> (0x00451F40 /
/// 0x00451F80: the visual-description stamp) do. <c>SmartBox::HandlePlayerTeleport</c> (0x00452150 / 0x00452190) is
/// the exception: it accepts a teleport stamp that is not older (<see cref="Sequence16.NotOlder"/>) and does not store
/// it.</para>
/// </remarks>
internal static class ObjectMessageGate
{
    /// <summary>The instance gate (see the remarks).</summary>
    /// <param name="known">True when the object exists.</param>
    /// <param name="storedInstance">The existing object's instance stamp (ignored when unknown).</param>
    /// <param name="messageInstance">The message's instance stamp.</param>
    internal static ObjectGateVerdict Check(bool known, ushort storedInstance, ushort messageInstance)
    {
        if (!known || Sequence16.IsNewer(storedInstance, messageInstance))
        {
            return ObjectGateVerdict.Park;
        }

        return storedInstance == messageInstance ? ObjectGateVerdict.Apply : ObjectGateVerdict.Drop;
    }

    /// <summary>The instance gate for <paramref name="target"/> (null when the object is unknown).</summary>
    internal static ObjectGateVerdict Check(WorldObject? target, ushort messageInstance) =>
        Check(target is not null, target?.Stamps.Instance ?? 0, messageInstance);

    /// <summary>
    /// <c>SmartBox::HandleDeleteObject</c>: a delete of the player is ignored; any other object goes through the instance
    /// gate.
    /// </summary>
    /// <param name="guid">The object the delete names.</param>
    /// <param name="playerGuid">The player's object id (0 outside the world).</param>
    /// <param name="target">The object, or null when unknown.</param>
    /// <param name="messageInstance">The delete's instance stamp.</param>
    internal static ObjectGateVerdict CheckDelete(uint guid, uint playerGuid, WorldObject? target, ushort messageInstance) =>
        guid != 0 && guid == playerGuid ? ObjectGateVerdict.IgnoredSelf : Check(target, messageInstance);

    /// <summary>
    /// <c>SmartBox::HandleParentEvent</c>, in retail's order: an unknown parent or a newer parent instance parks the message
    /// under the parent; otherwise an unknown child parks it under the child (even when the parent's instance is older);
    /// otherwise the parent's instance decides between apply (equal) and drop (older).
    /// </summary>
    /// <param name="parentGuid">The parent the message names.</param>
    /// <param name="parent">The parent, or null when unknown.</param>
    /// <param name="childGuid">The child the message names.</param>
    /// <param name="child">The child, or null when unknown.</param>
    /// <param name="parentInstance">The message's instance stamp (the parent's).</param>
    /// <param name="parkUnder">For <see cref="ObjectGateVerdict.Park"/>, the object whose CreateObject replays the message.</param>
    internal static ObjectGateVerdict CheckParent(
        uint parentGuid,
        WorldObject? parent,
        uint childGuid,
        WorldObject? child,
        ushort parentInstance,
        out uint parkUnder)
    {
        var verdict = Check(parent, parentInstance);
        if (verdict == ObjectGateVerdict.Park)
        {
            parkUnder = parentGuid;
            return verdict;
        }

        if (child is null)
        {
            parkUnder = childGuid;
            return ObjectGateVerdict.Park;
        }

        parkUnder = 0;
        return verdict;
    }

    /// <summary>One of the nine stamps.</summary>
    internal static ushort Get(in PhysicsTimestamps stamps, ObjectStamp which) => which switch
    {
        ObjectStamp.Position => stamps.Position,
        ObjectStamp.Movement => stamps.Movement,
        ObjectStamp.State => stamps.State,
        ObjectStamp.Vector => stamps.Vector,
        ObjectStamp.Teleport => stamps.Teleport,
        ObjectStamp.ServerControl => stamps.ServerControl,
        ObjectStamp.ForcePosition => stamps.ForcePosition,
        ObjectStamp.ObjDesc => stamps.ObjDesc,
        ObjectStamp.Instance => stamps.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(which), which, "not an object stamp"),
    };

    /// <summary>
    /// Retail <c>CPhysicsObj::newer_event</c> on one stamp: when <paramref name="incoming"/> is newer than the stored
    /// stamp, stores it in <paramref name="stamps"/> and returns true (apply the change); otherwise leaves the stamps alone
    /// and returns false (ignore the change).
    /// </summary>
    internal static bool TryAdvance(ref PhysicsTimestamps stamps, ObjectStamp which, ushort incoming)
    {
        var stored = Get(stamps, which);
        if (!Sequence16.NewerEvent(ref stored, incoming))
        {
            return false;
        }

        stamps = which switch
        {
            ObjectStamp.Position => stamps with { Position = stored },
            ObjectStamp.Movement => stamps with { Movement = stored },
            ObjectStamp.State => stamps with { State = stored },
            ObjectStamp.Vector => stamps with { Vector = stored },
            ObjectStamp.Teleport => stamps with { Teleport = stored },
            ObjectStamp.ServerControl => stamps with { ServerControl = stored },
            ObjectStamp.ForcePosition => stamps with { ForcePosition = stored },
            ObjectStamp.ObjDesc => stamps with { ObjDesc = stored },
            _ => stamps with { Instance = stored },
        };
        return true;
    }
}
