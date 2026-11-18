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

    /// <summary>The weenie description's CombatUse (ACE: 1 Melee, 2 Missile, 3 Ammo, 4 Shield, 5 TwoHanded), or null when the description sent none (M4 1.3 s1: what <c>GetDefaultCombatMode</c> reads).</summary>
    public sbyte? CombatUse => Created.Weenie.CombatUse;

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

    /// <summary>
    /// When the server last gave <see cref="Position"/> (the CreateObject's position, then each accepted UpdatePosition), in session-clock seconds; NaN while it never has. M6 item 14 (<c>m6-live-position</c>): the age
    /// of the server's report, which the fight's measure prints beside the body's position (ACE sends no position for a monster that sticks to the player, so the report of one in melee grows old).
    /// </summary>
    public double PositionAt { get; internal set; } = double.NaN;

    /// <summary>
    /// M6 item 14 (the owner, 2026-11-12: use the information the server gives): where the server last named this object as the TARGET of a movement order, or null while it never has. A MoveToObject order
    /// (F74C, movement type 6) carries "the location of the target" beside its guid (ACE <c>Network/Motion/MoveToObject.cs</c>:10 and 16: <c>Origin = new Origin(motion.Position)</c>), so each time the server
    /// walks the player to a thing she used or attacked, and each time it sends a creature after something, it says where that thing stands. The client kept the order on the mover and dropped this.
    /// </summary>
    public CellFrame? OrderPosition { get; internal set; }

    /// <summary>When the server last gave <see cref="OrderPosition"/>, in session-clock seconds; NaN while it never has.</summary>
    public double OrderPositionAt { get; internal set; } = double.NaN;

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

    /// <summary>
    /// Whether the server says the object is locked (a door's or a chest's ACE <c>PropertyBool.Locked</c>, key 3; M5 6 w2), from the last GameMessage 0x02D2 for this
    /// instance; null until the server says. The CreateObject does not carry it: its weenie description has no lock field, and the Openable description bit
    /// is not the lock (ACE sets Openable to "not locked" for a container, corpse, chest, hook or storage, but forces it on for a locked chest under its
    /// <c>fix_chest_missing_inventory_window</c> property, and never sets it for a door: @95ef426d <c>WorldObject_Networking.cs:845-856</c>), so nothing is read from it.
    /// </summary>
    public bool? Locked { get; internal set; }

    /// <summary>Where <see cref="Locked"/> was read from (M6 8 C, D117): <c>appraised</c> (the 0x00C9 answer, ACE's <c>AppraiseInfo.cs:135-165</c>), <c>property-update</c> (0x02D2) or <c>refused-use</c> (the Use's sound 0x94); null while unknown.</summary>
    public string? LockedFrom { get; internal set; }

    /// <summary>Every public property the server changed for this instance since its CreateObject (GameMessages 0x02D0, 0x02D2, 0x02D4, 0x02D6, 0x02D8), by kind and key.</summary>
    public ObjectProperties Properties { get; } = new();
}

/// <summary>
/// The public properties the server updated for one object (<see cref="WorldObject.Properties"/>; M5 6 w2), by kind and ACE's key; the description's own
/// values are not in it (the CreateObject carries a fixed set of fields, not a property table). Game thread only.
/// </summary>
public sealed class ObjectProperties
{
    internal ObjectProperties()
    {
    }

    /// <summary>How many values are kept, over every kind.</summary>
    public int Count => Bools.Count + Int64s.Count + Floats.Count + Strings.Count + DataIds.Count;

    internal Dictionary<uint, bool> Bools { get; } = [];

    internal Dictionary<uint, long> Int64s { get; } = [];

    internal Dictionary<uint, double> Floats { get; } = [];

    internal Dictionary<uint, string> Strings { get; } = [];

    internal Dictionary<uint, uint> DataIds { get; } = [];

    /// <summary>The last update sequence accepted per kind and key (the reducer's <c>WTimeStamper</c> rule).</summary>
    internal Dictionary<(ObjectPropertyKind Kind, uint Key), byte> Stamps { get; } = [];

    /// <summary>The last 0x02D2 value of <paramref name="key"/>.</summary>
    public bool TryGetBool(uint key, out bool value) => Bools.TryGetValue(key, out value);

    /// <summary>The last 0x02D0 value of <paramref name="key"/>.</summary>
    public bool TryGetInt64(uint key, out long value) => Int64s.TryGetValue(key, out value);

    /// <summary>The last 0x02D4 value of <paramref name="key"/>.</summary>
    public bool TryGetFloat(uint key, out double value) => Floats.TryGetValue(key, out value);

    /// <summary>The last 0x02D6 value of <paramref name="key"/>.</summary>
    public bool TryGetString(uint key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value) => Strings.TryGetValue(key, out value);

    /// <summary>The last 0x02D8 value of <paramref name="key"/>.</summary>
    public bool TryGetDataId(uint key, out uint value) => DataIds.TryGetValue(key, out value);
}

/// <summary>What holds an object (F749 ParentEvent, or a CreateObject's parent).</summary>
/// <param name="ParentGuid">The holder.</param>
/// <param name="Location">The holding location (retail <c>ParentLocation</c>).</param>
/// <param name="Placement">The held object's placement.</param>
public readonly record struct ObjectAttachment(uint ParentGuid, int Location, int Placement);
