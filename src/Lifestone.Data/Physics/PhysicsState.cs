namespace Lifestone.Data.Physics;

/// <summary>
/// A physics object's state bits (retail <c>CPhysicsObj::state</c>, the u32 the server sends as PhysicsState). The
/// values are the ones the 11.4186 decompile tests: <c>CPhysicsObj::FindObjCollisions</c> (11.4186 0x0050F050, 11.6096
/// 0x0050FB20) reads Static 1, Ethereal 4, IgnoreCollisions 0x10, Missile 0x40 and HasPhysicsBsp 0x10000;
/// <c>CTransition::validate_transition</c> (0x0050AA70, 0x0050B540) reads Gravity 0x400; <c>CPhysicsObj::set_initial_frame</c>
/// (0x00514120, 0x00514C20) reads ParticleEmitter 0x1000; <c>CPhysicsObj::CacheHasPhysicsBSP</c> (0x0050F570, 0x00510040)
/// writes HasPhysicsBsp; the <c>CPhysicsObj</c> constructor sets 0x400C08
/// (<see cref="DefaultForNewObject"/>). The names are ACE's (<c>ACE.Entity.Enum.PhysicsState</c>).
/// </summary>
[Flags]
public enum PhysicsState : uint
{
    /// <summary>No bit.</summary>
    None = 0,

    /// <summary>A static object (<c>CPhysicsObj::InitObjectBegin</c> sets it for objects that are not dynamic).</summary>
    Static = 0x1,

    /// <summary>Unused.</summary>
    Unused1 = 0x2,

    /// <summary>Other objects pass through it.</summary>
    Ethereal = 0x4,

    /// <summary>Collisions are reported to the weenie.</summary>
    ReportCollisions = 0x8,

    /// <summary>Collisions are ignored (with Ethereal: no collision test at all).</summary>
    IgnoreCollisions = 0x10,

    /// <summary>Not drawn.</summary>
    NoDraw = 0x20,

    /// <summary>A missile: never steps down, and is treated as a creature by other objects.</summary>
    Missile = 0x40,

    /// <summary>Can be pushed.</summary>
    Pushable = 0x80,

    /// <summary>Turns to its path.</summary>
    AlignPath = 0x100,

    /// <summary>Collisions clip its path.</summary>
    PathClipped = 0x200,

    /// <summary>Falls under gravity.</summary>
    Gravity = 0x400,

    /// <summary>Lit.</summary>
    LightingOn = 0x800,

    /// <summary>A particle emitter (no part array frame).</summary>
    ParticleEmitter = 0x1000,

    /// <summary>Unused.</summary>
    Unused2 = 0x2000,

    /// <summary>Hidden.</summary>
    Hidden = 0x4000,

    /// <summary>Scripted collision.</summary>
    ScriptedCollision = 0x8000,

    /// <summary>Some part's level-0 GfxObj has a physics BSP (<c>CPhysicsObj::CacheHasPhysicsBSP</c>).</summary>
    HasPhysicsBsp = 0x10000,

    /// <summary>Does not bounce.</summary>
    Inelastic = 0x20000,

    /// <summary>A static with a default animation.</summary>
    HasDefaultAnim = 0x40000,

    /// <summary>A static with a default script.</summary>
    HasDefaultScript = 0x80000,

    /// <summary>Cloaked.</summary>
    Cloaked = 0x100000,

    /// <summary>Collisions with it are reported as environment collisions.</summary>
    ReportCollisionsAsEnvironment = 0x200000,

    /// <summary>Slides along ledges instead of walking off them.</summary>
    EdgeSlide = 0x400000,

    /// <summary>Sledding.</summary>
    Sledding = 0x800000,

    /// <summary>Frozen.</summary>
    Frozen = 0x1000000,

    /// <summary>
    /// The state the <c>CPhysicsObj</c> constructor (11.4186 0x005123F0, 11.6096 0x00512EF0) gives every new object,
    /// the constant 0x400C08: EdgeSlide, LightingOn, Gravity and ReportCollisions.
    /// </summary>
    DefaultForNewObject = EdgeSlide | LightingOn | Gravity | ReportCollisions,
}
