using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// Lane S3's synthetic rooms for the room graph's tests (P5a, no dats): a row of 10 m square rooms along +Y whose doorways are 2 m wide and
/// 2.2 m tall, upright or leaning, with an outside portal in the first room's south wall (the door of
/// <see cref="S1TileKit.HouseWithSouthDoor"/>: the same cell id 0x40400100 at (96, 96)). Built through the Data library's internal seams
/// (<c>CellStructure.FromParts</c>, <c>EnvCellData.FromParts</c>, <c>CellStructureLibrary.FromStructures</c>).
/// </summary>
internal static class S3RoomKit
{
    /// <summary>The first room (the entry cell, with the outside portal); the next rooms are +1 and +2.</summary>
    public const uint A = 0x40400100u;

    /// <summary>The second room.</summary>
    public const uint B = 0x40400101u;

    /// <summary>The third room.</summary>
    public const uint C = 0x40400102u;

    /// <summary>The vertical part of a leaning doorway's normal (the den's plazas lean 0.196).</summary>
    public const float LeanNormalZ = 0.196f;

    private const uint UprightEnvironment = 0x0D00F300u;
    private const uint LeaningEnvironment = 0x0D00F301u;

    /// <summary>The synthetic landblock the rooms are in.</summary>
    public static LandblockCoord Block => S1TileKit.Block;

    /// <summary>
    /// The three rooms in a row, A at (96, 96), B at (96, 106), C at (96, 116): A's north door to B, B's to C, A's south door outside.
    /// <paramref name="leaning"/> tilts every doorway's top by 0.44 m (a normal with a vertical part of <see cref="LeanNormalZ"/>).
    /// </summary>
    public static LandblockCellSet Rooms(bool leaning = false, bool withOutside = true)
    {
        var library = CellStructureLibrary.FromStructures([Structure(UprightEnvironment, 0f), Structure(LeaningEnvironment, 0.44f)]);
        var environment = leaning ? LeaningEnvironment : UprightEnvironment;
        return LandblockCellSet.FromCells(library, Block,
        [
            Cell(A, environment, 96f, [Inside(0, B), withOutside ? Outside(1) : Inside(1, 0x40400199u)]),
            Cell(B, environment, 106f, [Inside(0, C), Inside(1, A)]),
            Cell(C, environment, 116f, [Inside(1, B)]),
        ]);
    }

    private static EnvCellData Cell(uint id, uint environment, float y, CellPortalData[] portals) =>
        EnvCellData.FromParts(id, environment, 0, new Placement(new Vector3(96f, y, 0f), Quaternion.Identity), portals);

    /// <summary>A portal to <paramref name="other"/> (both sides see the cell on the negative side of the plane, so PortalSide).</summary>
    private static CellPortalData Inside(ushort polygon, uint other) => new(1, true, true, polygon, other, 0);

    private static CellPortalData Outside(ushort polygon) => new(4, false, true, polygon, CellIds.Outside, -1);

    /// <summary>A 10 x 10 x 3 m room (cell-local x and y from -5 to 5): polygon 0 the north doorway, polygon 1 the south one, each leaning by <paramref name="lean"/> m of height.</summary>
    private static CellStructure Structure(uint environment, float lean)
    {
        Vector3[] positions =
        [
            new(1f, 5f, 0f), new(-1f, 5f, 0f), new(-1f, 5f + lean, 2.2f), new(1f, 5f + lean, 2.2f), // north: counter-clockwise seen from +Y, normal +Y
            new(-1f, -5f, 0f), new(1f, -5f, 0f), new(1f, -5f - lean, 2.2f), new(-1f, -5f - lean, 2.2f), // south: normal -Y
        ];
        CellPolygon[] polygons = [Polygon(0, 0, 1, 2, 3), Polygon(1, 4, 5, 6, 7)];
        var inward = new[]
        {
            new CellPlane(new Vector3(1, 0, 0), 5f), new CellPlane(new Vector3(-1, 0, 0), 5f),
            new CellPlane(new Vector3(0, 1, 0), 5f), new CellPlane(new Vector3(0, -1, 0), 5f),
            new CellPlane(new Vector3(0, 0, 1), 0f), new CellPlane(new Vector3(0, 0, -1), 3f),
        };
        return CellStructure.FromParts(
            environment,
            0,
            positions,
            positions.Select(_ => Vector3.UnitY).ToArray(),
            positions.Select(v => new[] { new Vector2(v.X, v.Y) }).ToArray(),
            polygons,
            [0, 1],
            CellBsp.FromPlanes(inward),
            hasDrawingBsp: true);
    }

    private static CellPolygon Polygon(ushort id, params ushort[] vertices) =>
        new(id, vertices, [], [], 0, 0, 0, 0);
}
