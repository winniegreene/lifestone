using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Bsp;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.Transitions;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Data.Tests.Physics;

/// <summary>
/// Synthetic physics worlds without the dats: terrain from a height function, boxes and walls as static objects with
/// physics BSPs shaped like retail's (a chain of face planes ending in one solid leaf).
/// </summary>
internal static class PhysicsTestKit
{
    /// <summary>A landblock far from anything in the tests that use the dats.</summary>
    public static readonly LandblockCoord Block = new(0x40, 0x40);

    /// <summary>A height table of 256 entries from <paramref name="height"/>.</summary>
    public static float[] Heights(Func<int, float> height)
    {
        var table = new float[256];
        for (var i = 0; i < table.Length; i++)
        {
            table[i] = height(i);
        }

        return table;
    }

    /// <summary>Terrain whose vertex (x, y) uses height index <paramref name="heightIndex"/>(x, y) and terrain type 1 (dry land).</summary>
    public static LandblockTerrain TerrainOf(LandblockCoord coord, Func<int, int, byte> heightIndex, Func<int, int, int>? terrainType = null)
    {
        var words = new ushort[TerrainConstants.VertexCount];
        var heights = new byte[TerrainConstants.VertexCount];
        for (var x = 0; x < TerrainConstants.VerticesPerSide; x++)
        {
            for (var y = 0; y < TerrainConstants.VerticesPerSide; y++)
            {
                var index = TerrainConstants.VertexIndex(x, y);
                heights[index] = heightIndex(x, y);
                words[index] = (ushort)((terrainType?.Invoke(x, y) ?? 1) << 2);
            }
        }

        return new LandblockTerrain(coord, words, heights);
    }

    /// <summary>A flat landblock at height 0 with <paramref name="statics"/> on it.</summary>
    public static LandblockCollision Flat(LandblockCoord coord, params PhysStaticObject[] statics) =>
        LandblockCollisionBuilder.FromParts(TerrainOf(coord, (_, _) => 0), Heights(_ => 0f), [], statics);

    /// <summary>A world with <paramref name="blocks"/> attached.</summary>
    public static PhysicsWorld WorldOf(params LandblockCollision[] blocks) => WorldOf(null, blocks);

    /// <summary>A world with <paramref name="blocks"/> attached, running with <paramref name="compat"/>.</summary>
    public static PhysicsWorld WorldOf(PhysicsCompat? compat, params LandblockCollision[] blocks)
    {
        var world = new PhysicsWorld(compat);
        foreach (var block in blocks)
        {
            world.Attach(block);
        }

        return world;
    }

    /// <summary>The land cell of <paramref name="coord"/> under landblock-local (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static uint LandCellAt(LandblockCoord coord, float x, float y) =>
        CellIds.LandCellAt(coord, x, y) ?? throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}) is outside {coord}.");

    /// <summary>A position in the land cell under (x, y) of <paramref name="coord"/>, facing north.</summary>
    public static CellFrame At(LandblockCoord coord, float x, float y, float z) =>
        new(LandCellAt(coord, x, y), new AcFrame(new Vector3(x, y, z), Quaternion.Identity));

    /// <summary>
    /// A static box standing on its base at landblock-local (<paramref name="x"/>, <paramref name="y"/>,
    /// <paramref name="z"/>), <paramref name="size"/> across, as a GfxObj with a retail-shaped physics BSP.
    /// </summary>
    public static PhysStaticObject Box(LandblockCoord coord, float x, float y, float z, Vector3 size, uint id = 0x0100F000)
    {
        var position = At(coord, x, y, z);
        var shape = BoxShape(id, size);
        var part = new PhysPart(position, shape, Vector3.One, Always2D: false);
        return new PhysStaticObject(
            id,
            position,
            PhysicsState.DefaultForNewObject | PhysicsState.Static | PhysicsState.HasPhysicsBsp,
            1.0f,
            [part],
            [],
            []);
    }

    /// <summary>
    /// A box GfxObj <paramref name="size"/> across with its base centred on the origin: six faces wound counter-clockwise
    /// from outside, in a BSP of face planes (positive side empty) ending in a solid leaf that holds the faces.
    /// </summary>
    public static GfxObjShape BoxShape(uint id, Vector3 size)
    {
        var hx = size.X * 0.5f;
        var hy = size.Y * 0.5f;
        var h = size.Z;
        PhysPolygon[] faces =
        [
            Polygon(0, new(-hx, -hy, h), new(hx, -hy, h), new(hx, hy, h), new(-hx, hy, h)),
            Polygon(1, new(-hx, hy, 0), new(hx, hy, 0), new(hx, -hy, 0), new(-hx, -hy, 0)),
            Polygon(2, new(hx, -hy, 0), new(hx, hy, 0), new(hx, hy, h), new(hx, -hy, h)),
            Polygon(3, new(-hx, hy, 0), new(-hx, -hy, 0), new(-hx, -hy, h), new(-hx, hy, h)),
            Polygon(4, new(hx, hy, 0), new(-hx, hy, 0), new(-hx, hy, h), new(hx, hy, h)),
            Polygon(5, new(-hx, -hy, 0), new(hx, -hy, 0), new(hx, -hy, h), new(-hx, -hy, h)),
        ];

        var sphere = new PhysSphere(new Vector3(0, 0, h * 0.5f), new Vector3(hx, hy, h * 0.5f).Length() + 0.01f);
        PhysicsBspNode node = PhysicsBspLeaf.Create(0, 1, sphere, faces);
        for (var i = faces.Length - 1; i >= 0; i--)
        {
            var empty = PhysicsBspLeaf.Create(i + 1, 0, sphere, []);
            node = PhysicsBspNode.Create(faces[i].Plane, sphere, empty, node);
        }

        var physics = GfxObjPhysics.FromParts(id, faces, PhysicsBspTree.FromRoot(node));
        return GfxObjShape.FromParts(id, new BBox(new Vector3(-hx, -hy, 0), new Vector3(hx, hy, h)), physics);
    }

    /// <summary>
    /// A body shaped like the human Setup 0x02000001: spheres at heights 0.475 and 1.35 of radius 0.48, step up 0.6,
    /// step down 1.5 (<see cref="LandscapeCollisionTests"/> checks these against the dats).
    /// </summary>
    public static PhysicsTestBody Human(PhysicsWorld world) =>
        new(world, stepUpHeight: 0.6f, stepDownHeight: 1.5f, spheres: [new PhysSphere(new Vector3(0, 0, 0.475f), 0.48f), new PhysSphere(new Vector3(0, 0, 1.35f), 0.48f)]);

    /// <summary>A polygon with the given corners (counter-clockwise seen from its front).</summary>
    public static PhysPolygon Polygon(int id, params Vector3[] corners) => new(id, corners);
}

/// <summary>
/// A moving object made of spheres, driven through a <see cref="PhysicsWorld"/> as retail's <c>CPhysicsObj::transition</c>
/// drives one (<c>get_object_info</c>'s flags, the contact plane and sliding normal carried from move to move,
/// <c>SetPositionInternal</c>'s bookkeeping), moved by explicit offsets: the transition machine's tests (PH3, PH4), which
/// predate <see cref="Lifestone.Data.Physics.Objects.PhysicsObj"/> (PH5) and need no clock, velocity or motion.
/// </summary>
internal sealed class PhysicsTestBody
{
    private readonly PhysicsWorld _world;
    private readonly PhysSphere[] _spheres;
    private uint _contactPlaneCellId;
    private bool _contactIsWater;
    private Vector3 _slidingNormal;
    private bool _sliding;

    public PhysicsTestBody(PhysicsWorld world, float radius = 0.5f, float stepUpHeight = 0.4f, float stepDownHeight = 0.4f, PhysicsState state = PhysicsState.DefaultForNewObject, PhysSphere[]? spheres = null)
    {
        _world = world;
        _spheres = spheres ?? [new PhysSphere(new Vector3(0, 0, radius), radius)];
        StepUpHeight = stepUpHeight;
        StepDownHeight = stepDownHeight;
        State = state;
    }

    public float StepUpHeight { get; }

    public float StepDownHeight { get; }

    /// <summary>The object's physics state (EdgeSlide and Gravity from the constructor default).</summary>
    public PhysicsState State { get; set; }

    public CellFrame Position { get; private set; }

    public PhysCell? Cell { get; private set; }

    public bool HasContact { get; private set; }

    public bool OnWalkable { get; private set; }

    public Plane ContactPlane { get; private set; }

    /// <summary>The last move's collision normal, when it had one.</summary>
    public Vector3? CollisionNormal { get; private set; }

    /// <summary>True when the last move ran into the environment.</summary>
    public bool CollidedWithEnvironment { get; private set; }

    /// <summary>The cells the last transition touched.</summary>
    public List<uint> Cells { get; } = [];

    /// <summary>Places the object at <paramref name="at"/> (a placement with a landing step).</summary>
    public bool Place(in CellFrame at)
    {
        var transition = _world.Transitions.Rent();
        try
        {
            Init(transition, admin: true);
            transition.InitPath(_world.GetCell(at.CellId), null, at);
            return Finish(transition, transition.FindValidPosition());
        }
        finally
        {
            _world.Transitions.Return(transition);
        }
    }

    /// <summary>Moves the object from where it is towards <paramref name="to"/> in one transition.</summary>
    public bool MoveTo(in CellFrame to)
    {
        var transition = _world.Transitions.Rent();
        try
        {
            Init(transition, admin: false);
            transition.InitPath(Cell, Position, to);
            return Finish(transition, transition.FindValidPosition());
        }
        finally
        {
            _world.Transitions.Return(transition);
        }
    }

    /// <summary>Moves the object by <paramref name="offset"/> (landblock space of its cell).</summary>
    public bool MoveBy(Vector3 offset) => MoveTo(Position.AddOffset(offset));

    /// <summary>Retail <c>CPhysicsObj::get_object_info</c>'s flags with the carried contact state.</summary>
    private void Init(Transition transition, bool admin)
    {
        var flags = ObjectInfoState.None;
        if ((State & PhysicsState.EdgeSlide) != 0)
        {
            flags |= ObjectInfoState.EdgeSlide;
        }

        if (!admin)
        {
            if (HasContact)
            {
                transition.InitContactPlane(_contactPlaneCellId, ContactPlane, _contactIsWater);
                flags |= ObjectInfoState.Contact;
                if (OnWalkable)
                {
                    flags |= ObjectInfoState.OnWalkable;
                }
            }

            if (_sliding)
            {
                transition.InitSlidingNormal(_slidingNormal);
            }
        }

        if ((State & PhysicsState.Missile) != 0)
        {
            flags |= ObjectInfoState.PathClipped;
        }

        transition.InitObject(this, flags, State, 1.0f, StepUpHeight, StepDownHeight);
        transition.InitSphere(_spheres, 1.0f);
    }

    /// <summary>Retail <c>CPhysicsObj::SetPositionInternal(transition)</c>'s bookkeeping.</summary>
    private bool Finish(Transition transition, bool found)
    {
        var collisions = transition.CollisionInfo;
        CollisionNormal = collisions.CollisionNormalValid ? collisions.CollisionNormal : null;
        CollidedWithEnvironment = collisions.CollidedWithEnvironment;
        if (!found || transition.SpherePath.CurCell is null)
        {
            return false;
        }

        Cells.Clear();
        for (var i = 0; i < transition.CellArray.Count; i++)
        {
            Cells.Add(transition.CellArray.IdAt(i));
        }

        Position = transition.SpherePath.CurPos;
        Cell = transition.SpherePath.CurCell;
        HasContact = collisions.ContactPlaneValid;
        ContactPlane = collisions.ContactPlane;
        _contactPlaneCellId = collisions.ContactPlaneCellId;
        _contactIsWater = collisions.ContactPlaneIsWater;
        OnWalkable = HasContact && ContactPlane.Normal.Z >= PhysicsGlobals.FloorZ;
        _sliding = collisions.SlidingNormalValid;
        _slidingNormal = collisions.SlidingNormal;
        return true;
    }
}
