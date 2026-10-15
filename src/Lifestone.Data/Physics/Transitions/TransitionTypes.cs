// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Transition.cs (TransitionState),
// Source/ACE.Server/Physics/ObjectInfo.cs (ObjectInfoState) and Source/ACE.Server/Physics/SpherePath.cs
// (InsertType) (AGPL-3.0) with changes: Lifestone names; values checked against the retail decompile (below).

namespace Lifestone.Data.Physics.Transitions;

/// <summary>
/// What a collision test did to the moving sphere (retail <c>TransitionState</c>, the int every <c>CSphere</c>,
/// <c>CCylSphere</c> and <c>CTransition</c> step returns: 1 OK, 2 collided, 3 adjusted, 4 slid).
/// </summary>
public enum TransitionState
{
    /// <summary>Not set.</summary>
    Invalid = 0,

    /// <summary>Nothing in the way.</summary>
    OK = 1,

    /// <summary>Blocked: the step is refused.</summary>
    Collided = 2,

    /// <summary>The check position was moved (stepped, landed, pushed out) and must be tested again.</summary>
    Adjusted = 3,

    /// <summary>The check position slid along the obstacle and must be tested again.</summary>
    Slid = 4,
}

/// <summary>
/// The moving object's flags for one transition (retail <c>OBJECTINFO::state</c>; the bits the decompile tests are
/// Contact 1 and OnWalkable 2, e.g. <c>CSphere::intersects_sphere</c>'s <c>state &amp; 3</c>, PathClipped 8 and
/// PerfectClip 0x40).
/// </summary>
[Flags]
public enum ObjectInfoState
{
    /// <summary>No flag.</summary>
    None = 0x0,

    /// <summary>The object touches a contact plane.</summary>
    Contact = 0x1,

    /// <summary>The contact plane is walkable.</summary>
    OnWalkable = 0x2,

    /// <summary>The camera's viewer sphere.</summary>
    IsViewer = 0x4,

    /// <summary>Collisions clip the path at the point of contact.</summary>
    PathClipped = 0x8,

    /// <summary>The orientation is not interpolated along the path.</summary>
    FreeRotate = 0x10,

    /// <summary>Exact time-of-impact clipping against spheres and cylinders.</summary>
    PerfectClip = 0x40,

    /// <summary>Nothing passes through the object.</summary>
    IsImpenetrable = 0x80,

    /// <summary>The object is a player.</summary>
    IsPlayer = 0x100,

    /// <summary>The object slides along ledges instead of walking off them.</summary>
    EdgeSlide = 0x200,

    /// <summary>Creatures do not block the object.</summary>
    IgnoreCreatures = 0x400,

    /// <summary>The object is a player killer.</summary>
    IsPK = 0x800,

    /// <summary>The object is a lite player killer.</summary>
    IsPKLite = 0x1000,
}

/// <summary>
/// How a path is inserted (retail <c>SPHEREPATH::InsertType</c>: <c>init_path</c> sets 0 with a begin position and 1
/// without one).
/// </summary>
public enum InsertType
{
    /// <summary>Moving from a begin position to an end position.</summary>
    Transition = 0,

    /// <summary>Placing at the end position (no begin position).</summary>
    Placement = 1,

    /// <summary>The first placement of an object.</summary>
    InitialPlacement = 2,
}
