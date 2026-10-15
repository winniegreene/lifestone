// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Collision/CollisionInfo.cs (AGPL-3.0)
// with changes: fields only for what the primitives, the transition and PhysicsObj read; the collided-object list is a
// reused array (retail's DArray) instead of a List; set_collision_normal and set_sliding_normal store the normalised
// vector, as retail does (ACE normalises a copy and stores the input).

using System.Numerics;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.Primitives;

namespace Lifestone.Data.Physics.Transitions;

/// <summary>
/// What a transition learned about contact and collision (retail <c>COLLISIONINFO</c>): the contact plane under the
/// object, the last known one, the sliding and collision normals, the moving objects it ran into and the stationary-fall
/// count.
/// </summary>
/// <remarks>One per <see cref="Transition"/>; reset by <see cref="Init"/>. Not thread-safe; nothing allocates once the
/// object array has grown.</remarks>
internal sealed class CollisionInfo
{
    private PhysicsObj?[] _collideObjects = new PhysicsObj?[4];

    /// <summary>True while <see cref="LastKnownContactPlane"/> holds a plane.</summary>
    public bool LastKnownContactPlaneValid;

    /// <summary>The contact plane the object stood on before this transition.</summary>
    public Plane LastKnownContactPlane;

    /// <summary>True when the last known contact plane is water.</summary>
    public bool LastKnownContactPlaneIsWater;

    /// <summary>The cell the last known contact plane belongs to.</summary>
    public uint LastKnownContactPlaneCellId;

    /// <summary>True while <see cref="ContactPlane"/> holds a plane.</summary>
    public bool ContactPlaneValid;

    /// <summary>The plane the object rests on, in its cell's landblock space.</summary>
    public Plane ContactPlane;

    /// <summary>True when the contact plane is water (retail sets it for every sphere and cylinder top too).</summary>
    public bool ContactPlaneIsWater;

    /// <summary>The cell the contact plane belongs to (0 when not known).</summary>
    public uint ContactPlaneCellId;

    /// <summary>True while <see cref="SlidingNormal"/> holds a normal.</summary>
    public bool SlidingNormalValid;

    /// <summary>The horizontal normal the object is sliding along.</summary>
    public Vector3 SlidingNormal;

    /// <summary>True while <see cref="CollisionNormal"/> holds a normal.</summary>
    public bool CollisionNormalValid;

    /// <summary>The unit normal of the last collision (zero when it was too short to normalise).</summary>
    public Vector3 CollisionNormal;

    /// <summary>The offset the transition adjusted the end position by.</summary>
    public Vector3 AdjustOffset;

    /// <summary>True when the transition collided with the environment (cells, terrain, buildings).</summary>
    public bool CollidedWithEnvironment;

    /// <summary>Frames the object has stood still while falling (the stationary-fall failsafe counts to 3).</summary>
    public int FramesStationaryFall;

    /// <summary>How many moving objects the transition ran into (retail <c>num_collide_object</c>).</summary>
    public int CollideObjectCount { get; private set; }

    /// <summary>The last object that stopped the transition (retail <c>last_collided_object</c>), or null.</summary>
    public PhysicsObj? LastCollidedObject { get; private set; }

    /// <summary>The <paramref name="index"/>th object the transition ran into, in the order they were met.</summary>
    public PhysicsObj CollideObjectAt(int index) => _collideObjects[index]!;

    /// <summary>Retail <c>COLLISIONINFO::init</c> (11.4186 0x00509D60, 11.6096 0x0050A830): clears the flags and counts.</summary>
    public void Init()
    {
        LastKnownContactPlaneValid = false;
        ContactPlaneValid = false;
        SlidingNormalValid = false;
        CollisionNormalValid = false;
        Array.Clear(_collideObjects, 0, CollideObjectCount);
        CollideObjectCount = 0;
        LastCollidedObject = null;
        CollidedWithEnvironment = false;
        ContactPlaneCellId = 0;
        FramesStationaryFall = 0;
    }

    /// <summary>
    /// Retail <c>COLLISIONINFO::add_object</c> (11.4186 0x006B4E20, 11.6096 0x006B5D60): records <paramref name="obj"/>
    /// once, and as the last collided object when it stopped the transition (<paramref name="state"/> other than OK).
    /// </summary>
    public void AddObject(PhysicsObj obj, TransitionState state)
    {
        for (var i = 0; i < CollideObjectCount; i++)
        {
            if (ReferenceEquals(_collideObjects[i], obj))
            {
                return;
            }
        }

        if (CollideObjectCount == _collideObjects.Length)
        {
            Array.Resize(ref _collideObjects, CollideObjectCount + 10);
        }

        _collideObjects[CollideObjectCount++] = obj;
        if (state != TransitionState.OK)
        {
            LastCollidedObject = obj;
        }
    }

    /// <summary>Retail <c>COLLISIONINFO::set_contact_plane</c> (11.4186 0x00509D80, 11.6096 0x0050A850).</summary>
    public void SetContactPlane(in Plane plane, bool isWater)
    {
        ContactPlaneValid = true;
        ContactPlane = plane;
        ContactPlaneIsWater = isWater;
    }

    /// <summary>
    /// Retail <c>COLLISIONINFO::set_collision_normal</c> (11.4186 0x0050A000, 11.6096 0x0050AAD0): stores the normal
    /// normalised, or zero when it is shorter than <see cref="PhysicsGlobals.Epsilon"/>.
    /// </summary>
    public void SetCollisionNormal(Vector3 normal)
    {
        CollisionNormalValid = true;
        CollisionNormal = normal;
        if (VectorMath.NormalizeCheckSmall(ref CollisionNormal))
        {
            CollisionNormal = Vector3.Zero;
        }
    }

    /// <summary>
    /// Retail <c>COLLISIONINFO::set_sliding_normal</c> (11.4186 0x0050A060, 11.6096 0x0050AB30): stores the normal's
    /// horizontal part normalised, or zero when it is too short.
    /// </summary>
    public void SetSlidingNormal(Vector3 normal)
    {
        SlidingNormalValid = true;
        SlidingNormal = new Vector3(normal.X, normal.Y, 0f);
        if (VectorMath.NormalizeCheckSmall(ref SlidingNormal))
        {
            SlidingNormal = Vector3.Zero;
        }
    }
}
