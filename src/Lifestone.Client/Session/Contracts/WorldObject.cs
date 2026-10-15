using System.Numerics;
using Lifestone.Data.World;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// One object the server created (<see cref="GameState.Objects"/>): its descriptions as the last CreateObject carried
/// them, and its current position, parent, motion, vectors, state and sequences as later messages changed them. The
/// reducer owns it; consumers read it, typically when an <c>Object*</c> event names it.
/// </summary>
public sealed class WorldObject
{
    /// <summary>A new object from its CreateObject. Only the session creates one.</summary>
    internal WorldObject(CreateObjectMsg created, bool isSelf, double createdAt)
    {
        ArgumentNullException.ThrowIfNull(created);
        Created = created;
        IsSelf = isSelf;
        CreatedAt = createdAt;
        Model = created.Model;
    }

    /// <summary>The object id.</summary>
    public uint Guid => Created.Guid;

    /// <summary>True for the player's own object.</summary>
    public bool IsSelf { get; }

    /// <summary>The last CreateObject (or UpdateObject) for this instance: its visual, physics and weenie descriptions as sent.</summary>
    public CreateObjectMsg Created { get; internal set; }

    /// <summary>The weenie description (name, class, type, icon and the optional fields).</summary>
    public WeenieDesc Weenie => Created.Weenie;

    /// <summary>The object description bits of the weenie description (<c>ObjectDescriptionFlags</c>: player, attackable, vendor, portal and so on).</summary>
    public uint DescriptionFlags => Created.Weenie.ObjectDescriptionFlags;

    /// <summary>The weenie description's ItemType.</summary>
    public uint ItemType => Created.Weenie.ItemType;

    /// <summary>The container that holds the object (the weenie description's ContainerId), or null.</summary>
    public uint? ContainerId => Created.Weenie.ContainerId;

    /// <summary>The creature that wields the object (the weenie description's WielderId), or null.</summary>
    public uint? WielderId => Created.Weenie.WielderId;

    /// <summary>The server's radar blip colour (1 to 10), or null when the description sent none.</summary>
    public byte? RadarBlipColor => Created.Weenie.RadarBlipColor;

    /// <summary>The server's radar behaviour, or null when the description sent none.</summary>
    public byte? RadarBehavior => Created.Weenie.RadarBehavior;

    /// <summary>The physics description as created (setup, tables, scale, translucency, default script).</summary>
    public PhysicsDesc Physics => Created.Physics;

    /// <summary>The object's name.</summary>
    public string Name => Created.Weenie.Name;

    /// <summary>The current visual description (the create's, or the last accepted ObjDescEvent's).</summary>
    public ModelData Model { get; internal set; }

    /// <summary>The current physics state (the create's, or the last accepted SetState's).</summary>
    public PhysicsState State { get; internal set; }

    /// <summary>The current position, or null while the object has none (parented, picked up, or created without one).</summary>
    public CellFrame? Position { get; internal set; }

    /// <summary>What holds the object, or null.</summary>
    public ObjectAttachment? Parent { get; internal set; }

    /// <summary>True while the object has a position or a parent; false for an object held nowhere visible (inventory).</summary>
    public bool InWorld => Position is not null || Parent is not null;

    /// <summary>The current velocity (metres per second, AC axes).</summary>
    public Vector3 Velocity { get; internal set; }

    /// <summary>The current angular velocity (radians per second).</summary>
    public Vector3 Omega { get; internal set; }

    /// <summary>The current motion, or null when the server sent none (the motion table's default state).</summary>
    public ServerMotionState? Motion { get; internal set; }

    /// <summary>True when <see cref="Motion"/> is the client's own (autonomous) motion.</summary>
    public bool MotionAutonomous { get; internal set; }

    /// <summary>The nine sequences as last accepted.</summary>
    public PhysicsTimestamps Stamps { get; internal set; }

    /// <summary>The object's health fraction from the last UpdateHealth for it, or null.</summary>
    public float? Health { get; internal set; }

    /// <summary>When this instance was created, in session-clock seconds.</summary>
    public double CreatedAt { get; internal set; }
}

/// <summary>What holds an object (F749 ParentEvent, or a CreateObject's parent).</summary>
/// <param name="ParentGuid">The holder.</param>
/// <param name="Location">The holding location (retail <c>ParentLocation</c>).</param>
/// <param name="Placement">The held object's placement.</param>
public readonly record struct ObjectAttachment(uint ParentGuid, int Location, int Placement);
