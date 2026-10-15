using System.Collections.Immutable;
using System.Numerics;

namespace Lifestone.Net.Types;

/// <summary>
/// The PhysicsDesc block of CreateObject (F745/F7DB), field for field (docs/protocol.md §5.3): flags, the physics state,
/// the flagged fields in ACE's write order, and the nine sequences. A field is null (or empty) when its flag is absent;
/// <see cref="Flags"/> keeps the raw flags, so a flag whose payload is empty (Movement with length 0) stays visible.
/// </summary>
public sealed record PhysicsDesc
{
    /// <summary>The raw PhysicsDesc flags.</summary>
    public required PhysicsDescFlags Flags { get; init; }

    /// <summary>The object's physics state.</summary>
    public required PhysicsState State { get; init; }

    /// <summary>
    /// The header-less MovementData (flag 0x10000 with a non-zero byte length); null when the flag is absent or the
    /// length is 0.
    /// </summary>
    public ServerMotionState? Movement { get; init; }

    /// <summary>The u32 is-autonomous that follows a non-empty <see cref="Movement"/>.</summary>
    public bool MovementIsAutonomous { get; init; }

    /// <summary>The placement (AnimationFrame, flag 0x20000), sent only when Movement is absent.</summary>
    public uint? AnimationFrame { get; init; }

    /// <summary>The position (flag 0x8000); the spawn position for the player's own object.</summary>
    public AcPosition? Position { get; init; }

    /// <summary>The motion table (MTable, flag 0x2).</summary>
    public uint? MotionTableId { get; init; }

    /// <summary>The sound table (STable, flag 0x800).</summary>
    public uint? SoundTableId { get; init; }

    /// <summary>The physics-effect table (PeTable, flag 0x1000).</summary>
    public uint? PhysicsEffectTableId { get; init; }

    /// <summary>The setup (CSetup, flag 0x1).</summary>
    public uint? SetupId { get; init; }

    /// <summary>The parent and the location on it (flag 0x20).</summary>
    public ObjectLink? Parent { get; init; }

    /// <summary>The children and their locations (flag 0x40), in wire order.</summary>
    public ImmutableArray<ObjectLink> Children { get; init; } = [];

    /// <summary>The scale (ObjScale, flag 0x80).</summary>
    public float? Scale { get; init; }

    /// <summary>The friction (flag 0x100).</summary>
    public float? Friction { get; init; }

    /// <summary>The elasticity (flag 0x200).</summary>
    public float? Elasticity { get; init; }

    /// <summary>The translucency (flag 0x40000).</summary>
    public float? Translucency { get; init; }

    /// <summary>The velocity (flag 0x4).</summary>
    public Vector3? Velocity { get; init; }

    /// <summary>The acceleration (flag 0x8).</summary>
    public Vector3? Acceleration { get; init; }

    /// <summary>The angular velocity (Omega, flag 0x10).</summary>
    public Vector3? Omega { get; init; }

    /// <summary>The default script (flag 0x2000).</summary>
    public uint? DefaultScript { get; init; }

    /// <summary>The default script's intensity (flag 0x4000).</summary>
    public float? DefaultScriptIntensity { get; init; }

    /// <summary>The nine sequences that close the block.</summary>
    public required PhysicsTimestamps Timestamps { get; init; }
}

/// <summary>A parent or child link of a <see cref="PhysicsDesc"/>: the other object's GUID and the location (holding
/// slot) it is attached at.</summary>
/// <param name="Guid">The linked object.</param>
/// <param name="Location">The parent location (holding slot).</param>
public readonly record struct ObjectLink(uint Guid, uint Location);

/// <summary>The PhysicsDesc flags (docs/protocol.md §5.3). The payloads are written in ACE's order, not bit order.</summary>
[Flags]
public enum PhysicsDescFlags : uint
{
    /// <summary>No optional fields.</summary>
    None = 0,

    /// <summary>u32 setup id.</summary>
    CSetup = 0x1,

    /// <summary>u32 motion table id.</summary>
    MTable = 0x2,

    /// <summary>3 × f32 velocity.</summary>
    Velocity = 0x4,

    /// <summary>3 × f32 acceleration.</summary>
    Acceleration = 0x8,

    /// <summary>3 × f32 angular velocity.</summary>
    Omega = 0x10,

    /// <summary>u32 parent GUID, u32 parent location.</summary>
    Parent = 0x20,

    /// <summary>u32 count, then the children.</summary>
    Children = 0x40,

    /// <summary>f32 scale.</summary>
    ObjScale = 0x80,

    /// <summary>f32 friction.</summary>
    Friction = 0x100,

    /// <summary>f32 elasticity.</summary>
    Elasticity = 0x200,

    /// <summary>Timestamps; never written by ACE (the nine sequences are always present).</summary>
    Timestamps = 0x400,

    /// <summary>u32 sound table id.</summary>
    STable = 0x800,

    /// <summary>u32 physics-effect table id.</summary>
    PeTable = 0x1000,

    /// <summary>u32 default script.</summary>
    DefaultScript = 0x2000,

    /// <summary>f32 default script intensity.</summary>
    DefaultScriptIntensity = 0x4000,

    /// <summary>The 32-byte position.</summary>
    Position = 0x8000,

    /// <summary>u32 length, then the header-less MovementData and u32 is-autonomous when the length is not 0.</summary>
    Movement = 0x10000,

    /// <summary>u32 placement (only without Movement).</summary>
    AnimationFrame = 0x20000,

    /// <summary>f32 translucency.</summary>
    Translucency = 0x40000,
}
