using System.Numerics;
using Lifestone.Data.World.Cells;

namespace Lifestone.Data.Tests.Cells;

/// <summary>Synthetic CellStructures for unit tests of the builder (conformance tests use the real dats).</summary>
internal static class CellStructureTestFactory
{
    /// <summary>Vertices with +Z normals and one UV each (u = x, v = y); no portals; an always-inside BSP.</summary>
    public static CellStructure Create(Vector3[] vertices, params CellPolygon[] polygons) =>
        CellStructure.FromParts(
            0x0D00FFFF,
            0,
            vertices,
            vertices.Select(_ => Vector3.UnitZ).ToArray(),
            vertices.Select(v => new[] { new Vector2(v.X, v.Y) }).ToArray(),
            polygons,
            [],
            CellBsp.FromPlanes([]),
            hasDrawingBsp: true);
}
