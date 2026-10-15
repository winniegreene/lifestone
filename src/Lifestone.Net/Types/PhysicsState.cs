// Ported from ACEmulator/ACE@95ef426d Source/ACE.Entity/Enum/PhysicsState.cs (AGPL-3.0)
// with changes: the names and values only, as a uint flags enum with a None member; HasPhysicsBSP is spelled
// HasPhysicsBsp; XML docs added.
namespace Lifestone.Net.Types;

/// <summary>
/// Retail <c>PhysicsState</c> bits, the u32 in a CreateObject PhysicsDesc and in SetState (F74B). A newly logged-in
/// player is <c>IgnoreCollisions | Gravity | Hidden | EdgeSlide</c> until LoginComplete (docs/protocol.md §5.3).
/// </summary>
[Flags]
public enum PhysicsState : uint
{
    /// <summary>No bits set.</summary>
    None = 0,

    /// <summary>Never moves.</summary>
    Static = 0x00000001,

    /// <summary>Unused.</summary>
    Unused1 = 0x00000002,

    /// <summary>Other objects pass through it.</summary>
    Ethereal = 0x00000004,

    /// <summary>Reports collisions to its owner.</summary>
    ReportCollisions = 0x00000008,

    /// <summary>Does not collide.</summary>
    IgnoreCollisions = 0x00000010,

    /// <summary>Not drawn.</summary>
    NoDraw = 0x00000020,

    /// <summary>A missile.</summary>
    Missile = 0x00000040,

    /// <summary>Can be pushed.</summary>
    Pushable = 0x00000080,

    /// <summary>Aligns to its path.</summary>
    AlignPath = 0x00000100,

    /// <summary>Its path is clipped.</summary>
    PathClipped = 0x00000200,

    /// <summary>Falls under gravity.</summary>
    Gravity = 0x00000400,

    /// <summary>Lighting is on.</summary>
    LightingOn = 0x00000800,

    /// <summary>A particle emitter.</summary>
    ParticleEmitter = 0x00001000,

    /// <summary>Unused.</summary>
    Unused2 = 0x00002000,

    /// <summary>Hidden: not drawn, animation and physics frozen (m2-plan §1.6 item 14).</summary>
    Hidden = 0x00004000,

    /// <summary>Uses scripted collision.</summary>
    ScriptedCollision = 0x00008000,

    /// <summary>Has a physics BSP.</summary>
    HasPhysicsBsp = 0x00010000,

    /// <summary>Inelastic.</summary>
    Inelastic = 0x00020000,

    /// <summary>Has a default animation.</summary>
    HasDefaultAnim = 0x00040000,

    /// <summary>Has a default script.</summary>
    HasDefaultScript = 0x00080000,

    /// <summary>Cloaked.</summary>
    Cloaked = 0x00100000,

    /// <summary>Reports collisions as the environment.</summary>
    ReportCollisionsAsEnvironment = 0x00200000,

    /// <summary>Slides along edges instead of falling off them.</summary>
    EdgeSlide = 0x00400000,

    /// <summary>Sledding.</summary>
    Sledding = 0x00800000,

    /// <summary>Frozen.</summary>
    Frozen = 0x01000000,
}
