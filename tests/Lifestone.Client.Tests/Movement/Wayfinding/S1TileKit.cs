using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Bsp;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// Synthetic landblocks for the tile builder's tests (lane S1, no dats): terrain from a height or water function, a box,
/// a cylinder-sphere and a sphere static, a building with an EnvCell and an outside portal, and a whole landblock assembled
/// from parts. Built on <see cref="MovementTestKit"/>'s terrain and height table.
/// </summary>
internal static class S1TileKit
{
    /// <summary>The terrain type the kit gives water vertices (the first of the five water types).</summary>
    public const int WaterTerrainType = 16;

    /// <summary>The synthetic landblock the tests build on.</summary>
    public static LandblockCoord Block => MovementTestKit.Block;

    /// <summary>Terrain whose vertex (x, y) is <paramref name="height"/> metres high and water where <paramref name="water"/> says so.</summary>
    public static LandblockTerrain Terrain(LandblockCoord coord, Func<int, int, int> height, Func<int, int, bool>? water = null)
    {
        var words = new ushort[TerrainConstants.VertexCount];
        var heights = new byte[TerrainConstants.VertexCount];
        for (var x = 0; x < TerrainConstants.VerticesPerSide; x++)
        {
            for (var y = 0; y < TerrainConstants.VerticesPerSide; y++)
            {
                var index = TerrainConstants.VertexIndex(x, y);
                heights[index] = (byte)Math.Clamp(height(x, y), 0, 255);
                words[index] = (ushort)((water?.Invoke(x, y) == true ? WaterTerrainType : 1) << 2);
            }
        }

        return new LandblockTerrain(coord, words, heights);
    }

    /// <summary>A flat landblock at height 0.</summary>
    public static LandblockCollision Flat() => MovementTestKit.Flat(Block);

    /// <summary>A ramp rising <paramref name="metresPerVertex"/> metres per vertex along X (a slope of metresPerVertex / 24).</summary>
    public static LandblockCollision Ramp(int metresPerVertex) =>
        LandblockCollisionBuilder.FromTerrain(Terrain(Block, (x, _) => x * metresPerVertex), MovementTestKit.Heights);

    /// <summary>A flat landblock with water at the vertices <paramref name="water"/> names.</summary>
    public static LandblockCollision Water(Func<int, int, bool> water) =>
        LandblockCollisionBuilder.FromTerrain(Terrain(Block, (_, _) => 0, water), MovementTestKit.Heights);

    /// <summary>A landblock of <paramref name="coord"/>, flat, with <paramref name="statics"/> registered in the cells they reach.</summary>
    public static LandblockCollision FlatWith(LandblockCoord coord, params PhysStaticObject[] statics) =>
        LandblockCollisionBuilder.FromParts(Terrain(coord, (_, _) => 0), MovementTestKit.Heights, [], statics);

    /// <summary>
    /// A static box standing on its base at landblock-local (x, y, z), <paramref name="size"/> across, turned
    /// <paramref name="yaw"/> radians about the vertical, a GfxObj with a (trivial) physics BSP so it counts as a physics part.
    /// </summary>
    public static PhysStaticObject Box(LandblockCoord coord, float x, float y, float z, Vector3 size, float yaw = 0f, uint id = 0x0100F001)
    {
        var position = new CellFrame(CellIds.LandCellAt(coord, x, y)!.Value, new AcFrame(new Vector3(x, y, z), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw)));
        var half = new Vector3(size.X * 0.5f, size.Y * 0.5f, size.Z);
        var sphere = new PhysSphere(new Vector3(0, 0, size.Z * 0.5f), new Vector3(half.X, half.Y, size.Z * 0.5f).Length() + 0.01f);
        var physics = GfxObjPhysics.FromParts(id, [], PhysicsBspTree.FromRoot(PhysicsBspLeaf.Create(0, 1, sphere, [])));
        var shape = GfxObjShape.FromParts(id, new BBox(new Vector3(-half.X, -half.Y, 0), half), physics);
        return new PhysStaticObject(
            id,
            position,
            PhysicsState.DefaultForNewObject | PhysicsState.Static | PhysicsState.HasPhysicsBsp,
            1f,
            [new PhysPart(position, shape, Vector3.One, Always2D: false)],
            [],
            []);
    }

    /// <summary>A static with one cylinder-sphere (no physics BSP): a tree trunk, a lamp post.</summary>
    public static PhysStaticObject Cylinder(LandblockCoord coord, float x, float y, float z, float radius, float height, uint id = 0x0100F002) =>
        new(id, At(coord, x, y, z), PhysicsState.DefaultForNewObject | PhysicsState.Static, 1f, [], [new PhysCylSphere(Vector3.Zero, height, radius)], []);

    /// <summary>A static with one sphere only (the last resort of retail's shape order).</summary>
    public static PhysStaticObject Ball(LandblockCoord coord, float x, float y, float z, float radius, uint id = 0x0100F003) =>
        new(id, At(coord, x, y, z), PhysicsState.DefaultForNewObject | PhysicsState.Static, 1f, [], [], [new PhysSphere(new Vector3(0, 0, radius), radius)]);

    /// <summary>A box static filed under an EnvCell id (the furniture of a room), which the tile leaves to the room graph.</summary>
    public static PhysStaticObject IndoorBox(float x, float y, float z, Vector3 size)
    {
        var box = Box(Block, x, y, z, size);
        return new PhysStaticObject(box.ModelId, new CellFrame(0x40400100u, box.Position.Frame), box.State, 1f, [.. box.Parts], [], []);
    }

    /// <summary>
    /// A whole flat landblock assembled from parts (the collision set's internal constructor): <paramref name="statics"/>,
    /// <paramref name="buildings"/> and <paramref name="envCells"/> as given, with no cell shadows.
    /// </summary>
    public static LandblockCollision Assemble(IEnumerable<PhysStaticObject> statics, IEnumerable<PhysBuilding>? buildings = null, IEnumerable<PhysEnvCell>? envCells = null)
    {
        var flat = Flat();
        return new LandblockCollision(
            Block,
            [.. flat.LandCells],
            (envCells ?? []).ToDictionary(c => c.Id),
            [.. buildings ?? []],
            [.. statics],
            [],
            new ushort[TerrainConstants.VertexCount],
            WaterType.NotWater);
    }

    /// <summary>
    /// A building 10 m square at (96, 96) with a doorway in its south wall (y = 91) into one EnvCell. The portal polygon is
    /// <paramref name="doorWidth"/> wide and 2.2 m tall, the cell on its negative side, so the outdoors lies to the south (-Y).
    /// </summary>
    public static (PhysBuilding Building, PhysEnvCell Cell) HouseWithSouthDoor(float doorWidth)
    {
        var landCell = CellIds.LandCellAt(Block, 96f, 96f)!.Value;
        var position = new CellFrame(landCell, new AcFrame(new Vector3(96f, 96f, 0f), Quaternion.Identity));
        var shape = GfxObjShape.FromParts(0x01001234u, new BBox(new Vector3(-5, -5, 0), new Vector3(5, 5, 6)), null);
        var cellId = 0x40400100u;
        var portal = new BuildingPortalData(1, true, true, cellId, 0, []);
        var building = new PhysBuilding(0x01001234u, position, new PhysPart(position, shape, Vector3.One, Always2D: false), [portal]);

        var w = doorWidth * 0.5f;
        Vector3[] corners = [new(-w, -5f, 0f), new(w, -5f, 0f), new(w, -5f, 2.2f), new(-w, -5f, 2.2f)];
        var doorway = new PhysCellPortal(CellIds.Outside, 0, PortalSide: true, new Plane(new Vector3(0, -1, 0), -5f), corners);
        var cell = new PhysEnvCell(cellId, new CellFrame(cellId, new AcFrame(new Vector3(96f, 96f, 0f), Quaternion.Identity)), CellBsp.FromPlanes([]), null, [doorway], [], seenOutside: true);
        return (building, cell);
    }

    /// <summary>
    /// A building at (96, 96) whose bounding box is 10 m square but whose shell is an L: two roof polygons at z 5 covering the
    /// south half (y 91 to 96) and the west half of the north (x 91 to 96, y 96 to 101), the north-east quadrant open.
    /// </summary>
    public static PhysBuilding LShapedHouse()
    {
        var landCell = CellIds.LandCellAt(Block, 96f, 96f)!.Value;
        var position = new CellFrame(landCell, new AcFrame(new Vector3(96f, 96f, 0f), Quaternion.Identity));
        PhysPolygon[] roofs =
        [
            new(0, new Vector3[] { new(-5, -5, 5), new(5, -5, 5), new(5, 0, 5), new(-5, 0, 5) }),
            new(1, new Vector3[] { new(-5, 0, 5), new(0, 0, 5), new(0, 5, 5), new(-5, 5, 5) }),
        ];
        var sphere = new PhysSphere(new Vector3(0, 0, 3), 8f);
        var physics = GfxObjPhysics.FromParts(0x01005678u, roofs, PhysicsBspTree.FromRoot(PhysicsBspLeaf.Create(0, 1, sphere, [])));
        var shape = GfxObjShape.FromParts(0x01005678u, new BBox(new Vector3(-5, -5, 0), new Vector3(5, 5, 6)), physics);
        return new PhysBuilding(0x01005678u, position, new PhysPart(position, shape, Vector3.One, Always2D: false), []);
    }

    private static CellFrame At(LandblockCoord coord, float x, float y, float z) =>
        new(CellIds.LandCellAt(coord, x, y)!.Value, new AcFrame(new Vector3(x, y, z), Quaternion.Identity));
}
