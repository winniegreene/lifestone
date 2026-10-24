using System.Numerics;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using Lifestone.Data.Geometry;
using Lifestone.Data.Models;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests.Cells;
using Lifestone.Data.World.Cells;
using DatEnvironment = DatReaderWriter.DBObjs.Environment;

namespace Lifestone.Data.Tests.Models;

/// <summary>
/// The one ConstructMesh implementation (<see cref="RetailConstructMesh"/>) behind both adapters: GfxObjs
/// (<see cref="GfxObjMeshBuilder"/>) and EnvCell CellStructs (<see cref="CellMeshBuilder"/>). Every distinct cell mesh of
/// the dats is compared through both in <c>CellExhaustiveTests.Every_distinct_cell_mesh_is_identical_through_the_cell_and_gfxobj_paths</c>.
/// </summary>
public sealed class RetailConstructMeshTests(CellsFixture fixture) : IClassFixture<CellsFixture>
{
    private const uint Textured0 = 0x08000010;
    private const uint Textured1 = 0x08000011;
    private const uint Untextured = 0x08000012;

    [Fact]
    public void Shared_synthetic_cases_build_the_same_mesh_through_the_cell_and_gfxobj_adapters()
    {
        // Surface list with a duplicate (index 3 names index 0's Surface) and an untextured Surface (index 2).
        uint[] surfaces = [Textured0, Textured1, Untextured, Textured0];
        Vector3[] vertices = [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0), new(2, 0.5f, 0)];
        (ushort[] Vertices, byte[] PosUv, byte[] NegUv, short Pos, short Neg, byte Stippling, int Sides)[] polygons =
        [
            ([0, 1, 2, 3], [0, 0, 0, 1], [], 0, 0, 0, 0),             // single-sided, UV index 1 does not exist (0, 0)
            ([1, 4, 2], [0, 0, 0], [], 1, 1, 1, 1),                    // double-sided, stippled (WRAP for index 1)
            ([0, 1, 2], [], [], 2, 0, 4, 0),                           // NoPos on the untextured Surface: filtered
            ([0, 2, 3], [0, 0, 0], [0, 0, 0], 3, 1, 1, 2),             // separate back; index 3 = Textured0 but WRAP
            ([2, 3, 0], [0, 0, 0], [], 0, 0, 0, 0),                    // more index-0 triangles (one CLAMP block)
            ([0, 1], [0, 0], [], 0, 0, 0, 0),                          // degenerate
        ];

        var cellStructure = CellStructureTestFactory.Create(
            vertices,
            [.. polygons.Select((p, i) => new CellPolygon((ushort)i, p.Vertices, p.PosUv, p.NegUv, p.Pos, p.Neg, p.Stippling, p.Sides))]);
        var cell = CellMeshBuilder.Build(cellStructure, surfaces, id => id == Untextured ? 0x1u : 0x2u);

        // The same polygons as DatReaderWriter records (the factory's vertices: +Z normals, one UV = (x, y)).
        var swVertices = vertices
            .Select((v, i) => (Key: (ushort)i, Vertex: new SWVertex { Origin = v, Normal = Vector3.UnitZ, UVs = [new Vec2Duv { U = v.X, V = v.Y }] }))
            .ToDictionary(x => x.Key, x => x.Vertex);
        var datPolygons = polygons.Select((p, i) => new KeyValuePair<ushort, Polygon>((ushort)i, new Polygon
        {
            VertexIds = [.. p.Vertices.Select(v => (short)v)],
            PosUVIndices = [.. p.PosUv],
            NegUVIndices = [.. p.NegUv],
            PosSurface = p.Pos,
            NegSurface = p.Neg,
            Stippling = (StipplingType)p.Stippling,
            SidesType = (CullMode)p.Sides,
        })).ToList();
        var gfx = GfxObjMeshBuilder.BuildPolygons(0x0D00FFFF, surfaces, swVertices, datPolygons, id => id != Untextured);

        MeshAssert.Identical(cell.Mesh, gfx.Mesh);
        Assert.Equal(cell.SubMeshInfo.Select(i => (int)i.SurfaceIndex), gfx.SubMeshInfo.Select(i => i.FirstSurfaceIndex));
        Assert.Equal((4, 1, 1), (cell.DrawnPolygonCount, cell.UntexturedPolygonCount, cell.DegeneratePolygonCount));

        // Retail subset order (first surface index, positive side first): index 0 (CLAMP); index 1 (WRAP) and its
        // reversed copy, which polygon 3's negative side (neg_surface 1) joins; index 3 (Textured0 again, but WRAP).
        Assert.Equal(
            new[]
            {
                (Textured0, MeshSide.Positive, TextureAddressMode.Clamp),
                (Textured1, MeshSide.Positive, TextureAddressMode.Wrap),
                (Textured1, MeshSide.Negative, TextureAddressMode.Wrap),
                (Textured0, MeshSide.Positive, TextureAddressMode.Wrap),
            },
            cell.Mesh.SubMeshes.Select(s => (s.SurfaceId, s.Side, s.Addressing)));
        Assert.Equal(2, gfx.SubMeshInfo[0].PolygonCount);

        // The unfiltered GfxObj build keeps the untextured sub-mesh; dropping it at draw time gives the same mesh.
        var unfiltered = GfxObjMeshBuilder.BuildPolygons(0x0D00FFFF, surfaces, swVertices, datPolygons);
        Assert.Contains(unfiltered.Mesh.SubMeshes, s => s.SurfaceId == Untextured);
        MeshAssert.Identical(cell.Mesh, new MeshData([.. unfiltered.Mesh.SubMeshes.Where(s => s.SurfaceId != Untextured)]));
    }

    [Fact]
    public void Unknown_sides_type_fails_loudly_in_both_adapters()
    {
        var vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) };
        var structure = CellStructureTestFactory.Create(vertices, new CellPolygon(0, [0, 1, 2], [0, 0, 0], [], 0, 0, 0, 3));
        Assert.Throws<NotSupportedException>(() => CellMeshBuilder.Build(structure, [Textured0], _ => 2u));

        // GfxObjs used to draw such a polygon single-sided without a word; no GfxObj in the dats has one (ModelsExhaustiveTests).
        var gfxObj = GfxObjMeshBuilderTests.Synthetic(
            [Textured0],
            GfxObjMeshBuilderTests.Pentagon(),
            new Polygon { SidesType = (CullMode)3, PosSurface = 0, VertexIds = [0, 1, 2], PosUVIndices = [0, 0, 0] });
        Assert.Throws<NotSupportedException>(() => GfxObjMeshBuilder.Build(0x01000001, gfxObj));
    }

    [Fact]
    public void Uv_table_of_the_wrong_length_fails_loudly()
    {
        // CPolygon::UnPack stores no table or one index per corner; anything else is malformed.
        var gfxObj = GfxObjMeshBuilderTests.Synthetic(
            [Textured0],
            GfxObjMeshBuilderTests.Pentagon(),
            new Polygon { SidesType = CullMode.Landblock, PosSurface = 0, VertexIds = [0, 1, 2, 3], PosUVIndices = [0, 0, 0] });
        Assert.Throws<InvalidDataException>(() => GfxObjMeshBuilder.Build(0x01000001, gfxObj));

        var vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) };
        var structure = CellStructureTestFactory.Create(vertices, new CellPolygon(0, [0, 1, 2], [0, 0], [], 0, 0, 0, 0));
        Assert.Throws<InvalidDataException>(() => CellMeshBuilder.Build(structure, [Textured0], _ => 2u));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Holtburg_cells_build_the_same_mesh_through_both_adapters()
    {
        var set = LandblockCellSet.Load(fixture.Structures, LandblockCoord.Holtburg);
        var cache = new CellMeshCache(fixture.Structures, capacity: 256);
        var compared = 0;
        foreach (var cell in set.Cells)
        {
            var mesh = cache.Get(cell);
            Assert.True(fixture.Dats.TryGetPortal<DatEnvironment>(cell.EnvironmentId, out var environment));
            var cellStruct = environment.Cells[cell.CellStructureIndex];
            var surfaceTypes = cell.SurfaceIds.Distinct().ToDictionary(id => id, id => fixture.Dats.TryGetPortal<Surface>(id, out var s) ? s.Type : 0);
            var gfx = GfxObjMeshBuilder.BuildPolygons(
                cell.EnvironmentId,
                cell.SurfaceIds,
                cellStruct.VertexArray.Vertices,
                cellStruct.Polygons,
                id => SurfaceDrawRules.IsDrawn(surfaceTypes[id], MeshDrawContext.EnvCell));
            MeshAssert.Identical(mesh.Mesh, gfx.Mesh);
            compared++;
        }

        Assert.Equal(set.Count, compared);
        Assert.True(compared > 100, $"{compared} Holtburg cells");
    }
}

/// <summary>Exact equality of two meshes (same sub-meshes in the same order, same vertices and indices).</summary>
internal static class MeshAssert
{
    public static void Identical(MeshData expected, MeshData actual)
    {
        Assert.Equal(expected.SubMeshes.Count, actual.SubMeshes.Count);
        for (var i = 0; i < expected.SubMeshes.Count; i++)
        {
            var e = expected.SubMeshes[i];
            var a = actual.SubMeshes[i];
            Assert.Equal((e.SurfaceId, e.Side, e.Addressing), (a.SurfaceId, a.Side, a.Addressing));
            Assert.Equal(e.Positions, a.Positions);
            Assert.Equal(e.Normals, a.Normals);
            Assert.Equal(e.Uvs, a.Uvs);
            Assert.Equal(e.Indices, a.Indices);
        }
    }
}
