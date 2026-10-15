using System.Numerics;
using DatReaderWriter.Enums;
using Lifestone.Data.Appearance;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Client.Avatars;

/// <summary>
/// Everything an avatar is built from: one CreateObject's physics and visual descriptions (retail <c>PhysicsDesc</c> and
/// <c>VisualDesc</c>) in Lifestone.Data's terms, with the object's current position, parent and motion as the session
/// knows them. <see cref="AvatarWire.FromWorldObject"/> builds it from the session's <c>WorldObject</c>; the stress
/// source and tests build it directly.
/// </summary>
public sealed record AvatarCreate
{
    /// <summary>The object id.</summary>
    public required uint Guid { get; init; }

    /// <summary>The player's own object.</summary>
    public bool IsSelf { get; init; }

    /// <summary>The weenie is a creature (item type 0x10): its motions are adjusted and need contact.</summary>
    public bool IsCreature { get; init; }

    /// <summary>The Setup (PhysicsDesc CSetup, flag 0x1).</summary>
    public required uint SetupId { get; init; }

    /// <summary>
    /// The motion table (PhysicsDesc MTable, flag 0x2), or 0 for none. Retail's <c>set_description</c> replaces the
    /// Setup's default table with this one, so 0 means the object has none (it plays its Setup's default animation).
    /// </summary>
    public uint MotionTableId { get; init; }

    /// <summary>The sound table (STable), or 0.</summary>
    public uint SoundTableId { get; init; }

    /// <summary>The PhysicsScriptTable (PeTable), or 0.</summary>
    public uint PhysicsScriptTableId { get; init; }

    /// <summary>The visual description.</summary>
    public ObjDesc ObjDesc { get; init; } = ObjDesc.Empty;

    /// <summary>The physics state bits (retail <c>PhysicsState</c>).</summary>
    public uint PhysicsState { get; init; }

    /// <summary>The position, or null for an object with none (held, or in a container).</summary>
    public CellFrame? Position { get; init; }

    /// <summary>Whether the position was grounded (an UpdatePosition's flag; a create's position counts as grounded).</summary>
    public bool HasContact { get; init; } = true;

    /// <summary>The parent, or 0.</summary>
    public uint ParentGuid { get; init; }

    /// <summary>The holding location on the parent.</summary>
    public int ParentLocation { get; init; }

    /// <summary>The placement (PhysicsDesc AnimationFrame, flag 0x20000), used when there is no movement.</summary>
    public uint Placement { get; init; }

    /// <summary>The children the description lists (retail <c>CObjectMaint::SetChildren</c>): their ids and locations.</summary>
    public IReadOnlyList<(uint Guid, int Location)> Children { get; init; } = [];

    /// <summary>The movement (PhysicsDesc Movement, flag 0x10000), or null.</summary>
    public WireMovement? Movement { get; init; }

    /// <summary>The queued actions of a type-0 <see cref="Movement"/>.</summary>
    public IReadOnlyList<WireMotionItem> MovementActions { get; init; } = [];

    /// <summary>The movement is the client's own (the u32 after it).</summary>
    public bool MovementAutonomous { get; init; }

    /// <summary>The scale (ObjScale), 1 when absent.</summary>
    public float Scale { get; init; } = 1f;

    /// <summary>The translucency, 0 when absent.</summary>
    public float Translucency { get; init; }

    /// <summary>The velocity.</summary>
    public Vector3 Velocity { get; init; }

    /// <summary>The acceleration.</summary>
    public Vector3 Acceleration { get; init; }

    /// <summary>The angular velocity.</summary>
    public Vector3 Omega { get; init; }

    /// <summary>The default script type, played by DefaultScript hooks.</summary>
    public PlayScript DefaultScript { get; init; }

    /// <summary>The default script's intensity.</summary>
    public float DefaultScriptIntensity { get; init; }
}
