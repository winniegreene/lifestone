using System.Numerics;
using Lifestone.Data.Geometry;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;

namespace Lifestone.Data.Tests.Cells;

public sealed class CellGeometryTests(CellsFixture fixture) : IClassFixture<CellsFixture>
{
    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Holtburg_inn_cells_build_well_formed_meshes_facing_into_the_rooms()
    {
        var set = LandblockCellSet.Load(fixture.Structures, LandblockCoord.Holtburg);
        var cache = new CellMeshCache(fixture.Structures);
        var probesInside = 0;
        var probesOutside = 0;
        foreach (var id in Enumerable.Range(0x100, 17).Select(i => 0xA9B40000u | (uint)i))
        {
            var cell = set.GetCell(id)!;
            var mesh = cache.Get(cell);
            var structure = set.GetStructure(cell);
            CellMeshAssert.WellFormed(mesh, cell.SurfaceIds);
            Assert.True(mesh.HasDrawingBsp);
            Assert.Equal(mesh.PolygonCount, mesh.DrawnPolygonCount + mesh.UntexturedPolygonCount + mesh.DegeneratePolygonCount);

            // The six-plus portal faces of the inn are invisible (NoPos, untextured): none reaches the mesh.
            Assert.True(mesh.UntexturedPolygonCount >= structure.Portals.Count(p => structure.Polygons[p.PolygonId].NoPos));

            foreach (var sub in mesh.Mesh.SubMeshes.Where(s => s.Side == MeshSide.Positive))
            {
                for (var t = 0; t < sub.Indices.Length; t += 3)
                {
                    var a = sub.Positions[sub.Indices[t]];
                    var b = sub.Positions[sub.Indices[t + 1]];
                    var c = sub.Positions[sub.Indices[t + 2]];
                    var n = Vector3.Cross(b - a, c - a);
                    if (n.Length() < 1e-6f)
                    {
                        continue;
                    }

                    var probe = ((a + b + c) / 3f) + (Vector3.Normalize(n) * 0.05f);
                    if (structure.Bsp.Contains(probe))
                    {
                        probesInside++;
                    }
                    else
                    {
                        probesOutside++;
                    }
                }
            }
        }

        // Front faces (CCW, right-handed) point into the room: a point just in front of a drawn triangle is inside the
        // cell for the overwhelming majority (the rest are e.g. pillars and trims whose fronts face into the room
        // from inside the room's boundary planes).
        Assert.True(probesInside > 9 * probesOutside, $"inside {probesInside}, outside {probesOutside}");
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Mesh_cache_shares_builds_and_is_bounded()
    {
        var set = LandblockCellSet.Load(fixture.Structures, LandblockCoord.Holtburg);
        var cache = new CellMeshCache(fixture.Structures, capacity: 8);
        var cell = set.GetCell(0xA9B40100u)!;

        var results = new CellMesh[32];
        Parallel.For(0, results.Length, i => results[i] = cache.Get(cell.EnvironmentId, cell.CellStructureIndex, cell.SurfaceIds.ToList()));
        Assert.All(results, r => Assert.Same(results[0], r));
        Assert.Equal(1, cache.Misses);
        Assert.Equal(31, cache.Hits);

        // Same struct, different surface list: a different mesh.
        var other = cache.Get(cell.EnvironmentId, cell.CellStructureIndex, cell.SurfaceIds.Reverse().ToList());
        Assert.NotSame(results[0], other);

        foreach (var c in set.Cells)
        {
            cache.Get(c);
        }

        Assert.Equal(8, cache.Count);
    }

    [Fact]
    public void Mesh_key_uses_the_whole_surface_list()
    {
        var a = new CellMeshKey(0x0D000001, 2, [0x08000001, 0x08000002]);
        var b = new CellMeshKey(0x0D000001, 2, [0x08000001, 0x08000002]);
        var c = new CellMeshKey(0x0D000001, 2, [0x08000002, 0x08000001]);
        var d = new CellMeshKey(0x0D000001, 3, [0x08000001, 0x08000002]);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
        Assert.NotEqual(a, d);
    }

    [Fact]
    public void Builder_follows_retail_sides_uv_and_untextured_rules()
    {
        // A unit square (vertices 0-3, CCW seen from +Z) used by three polygons.
        var structure = SyntheticStructure(
            [new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0)],
            new CellPolygon(0, [0, 1, 2, 3], [0, 0, 0, 1], [], 0, 0, 0, 0), // single-sided, UV index 1 on corner 3 (out of range there -> 0,0)
            new CellPolygon(1, [0, 1, 2, 3], [0, 0, 0, 0], [], 1, 0, 1, 1), // double-sided, stippled (wrap)
            new CellPolygon(2, [0, 1, 2], [], [], 2, 0, 4, 0)); // NoPos on an untextured surface: dropped
        var surfaces = new uint[] { 0x08000010, 0x08000011, 0x08000012 };
        var mesh = CellMeshBuilder.Build(structure, surfaces, id => id == 0x08000012 ? 0x1u /* Base1Solid */ : 0x2u /* Base1Image */);

        Assert.Equal(3, mesh.PolygonCount);
        Assert.Equal(2, mesh.DrawnPolygonCount);
        Assert.Equal(1, mesh.UntexturedPolygonCount);
        Assert.Equal(3, mesh.Mesh.SubMeshes.Count);

        var single = mesh.Mesh.SubMeshes[0];
        Assert.Equal((0x08000010u, MeshSide.Positive), (single.SurfaceId, single.Side));
        Assert.Equal(new[] { 0, 1, 2, 0, 2, 3 }, single.Indices);
        Assert.All(single.Normals, n => Assert.Equal(Vector3.UnitZ, n));
        Assert.Equal(Vector2.Zero, single.Uvs[3]); // UV index 1 does not exist on that vertex
        Assert.Equal(TextureAddressMode.Clamp, single.Addressing);

        var back = mesh.Mesh.SubMeshes.Single(s => s.Side == MeshSide.Negative);
        Assert.Equal(0x08000011u, back.SurfaceId);
        Assert.Equal(new[] { 2, 1, 0, 3, 2, 0 }, back.Indices);
        Assert.All(back.Normals, n => Assert.Equal(-Vector3.UnitZ, n));
        var a = back.Positions[back.Indices[0]];
        var b = back.Positions[back.Indices[1]];
        var c = back.Positions[back.Indices[2]];
        Assert.True(Vector3.Cross(b - a, c - a).Z < 0, "the negative side winds the other way");
        Assert.Equal(TextureAddressMode.Wrap, back.Addressing);
    }

    [Fact]
    public void Builder_fails_loudly_on_unknown_sides_type_and_bad_surface_index()
    {
        var verts = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) };
        var unknownSides = SyntheticStructure(verts, new CellPolygon(0, [0, 1, 2], [0, 0, 0], [], 0, 0, 0, 3));
        Assert.Throws<NotSupportedException>(() => CellMeshBuilder.Build(unknownSides, [0x08000001u], _ => 2u));
        var badSurface = SyntheticStructure(verts, new CellPolygon(0, [0, 1, 2], [0, 0, 0], [], 5, 0, 0, 0));
        Assert.Throws<InvalidDataException>(() => CellMeshBuilder.Build(badSurface, [0x08000001u], _ => 2u));
    }

    private static CellStructure SyntheticStructure(Vector3[] vertices, params CellPolygon[] polygons) =>
        CellStructureTestFactory.Create(vertices, polygons);
}

internal static class CellMeshAssert
{
    public static void WellFormed(CellMesh mesh, IReadOnlyList<uint> surfaceIds)
    {
        Assert.Equal(mesh.Mesh.SubMeshes.Count, mesh.SubMeshInfo.Count);
        var seen = new HashSet<(uint, MeshSide, TextureAddressMode)>();
        for (var s = 0; s < mesh.Mesh.SubMeshes.Count; s++)
        {
            var sub = mesh.Mesh.SubMeshes[s];
            Assert.True(seen.Add((sub.SurfaceId, sub.Side, sub.Addressing)), "one sub-mesh per (surface, side, addressing)");
            Assert.Equal(sub.SurfaceId, surfaceIds[mesh.SubMeshInfo[s].SurfaceIndex]);
            Assert.Contains(sub.SurfaceId, surfaceIds);
            Assert.Equal(sub.Positions.Length, sub.Normals.Length);
            Assert.Equal(sub.Positions.Length, sub.Uvs.Length);
            Assert.True(sub.Indices.Length > 0 && sub.Indices.Length % 3 == 0);
            foreach (var i in sub.Indices)
            {
                Assert.InRange(i, 0, sub.Positions.Length - 1);
            }

            foreach (var p in sub.Positions)
            {
                Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z));
            }

            foreach (var uv in sub.Uvs)
            {
                Assert.True(float.IsFinite(uv.X) && float.IsFinite(uv.Y));
            }
        }
    }
}
