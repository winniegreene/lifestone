using System.Numerics;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using Lifestone.Data.Geometry;
using Lifestone.Data.Models;

namespace Lifestone.Data.Tests.Models;

public sealed class GfxObjMeshBuilderTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    /// <summary>
    /// (Surface id, side, vertices, triangles) per sub-mesh, in builder order, computed independently by the python
    /// research reader (session scratch <c>models/survey2.py</c> over <c>acparse.py</c>, which applies the retail
    /// D3DPolyRender::ConstructMesh rules with plain dictionaries and sets).
    /// </summary>
    public static TheoryData<uint, string> IndependentSubMeshCounts => new()
    {
        // Holtburg building (LandBlockInfo 0xA9B4FFFE BuildInfo[0].ModelId).
        { 0x01000C1E, "080007E6+40/24 080006E8+24/15 08000660+34/22 08000660-12/6 08000370+24/14 080007E3+21/7 080006E6+16/12 080007E4+8/4 08000371+66/50 0800074E+6/4 080007E2+24/18" },

        // Another Holtburg building and a static used 19 times in Holtburg.
        { 0x01000BC3, "080008CA+12/6 08000371+44/34 080007E3+6/2 08000742+20/16 08000742-8/4 080001A5+28/18" },
        { 0x010007C4, "08000203+10/6 08000203-10/6" },

        // Tree canopy of scenery Setup 0x02000258 (23 m tree): fully double-sided.
        { 0x010037A1, "080011CE+195/274 080011CE-195/274" },

        // Separate-back polygon (sides_type 2) with a different negative surface, merged with sides_type 1 backs.
        { 0x010008AA, "08000102+20/10 08000CEE+4/2 08000102-20/10" },
        { 0x0100326E, "080003ED+24/16 080003EE+42/26 080003EF-4/2 080003EF+104/144" },

        // NoPos polygons (surface 0x080000DF) are still drawn on their positive side, with UV index 0.
        { 0x0100007B, "08000398+4/2 08000399+10/9 08000397+27/25 080000DF+10/8" },

        // Human head part of Setup 0x02000001.
        { 0x0100005A, "08000B01+10/6 08000B02+30/44 08000B03+11/11 08000B04+10/9 08000B05+14/12" },
    };

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsTheory]
    [MemberData(nameof(IndependentSubMeshCounts))]
    public void Sub_mesh_counts_match_the_independent_python_reader(uint gfxObjId, string expected)
    {
        var mesh = GfxObjMeshBuilder.Load(fixture.Dats, gfxObjId);
        Assert.NotNull(mesh);
        var actual = string.Join(' ', mesh.Mesh.SubMeshes.Select(s =>
            $"{s.SurfaceId:X8}{(s.Side == MeshSide.Positive ? '+' : '-')}{s.Positions.Length}/{s.Indices.Length / 3}"));

        // The python reader lists sub-meshes in first-use order; compare as sets, then check our order separately.
        Assert.Equal(expected.Split(' ').Order(), actual.Split(' ').Order());
        ModelsExhaustiveTests.AssertWellFormed(mesh, surfaceIds: null);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Holtburg_building_carries_sort_center_bounds_and_degrade_link()
    {
        var mesh = GfxObjMeshBuilder.Load(fixture.Dats, 0x01000C1E)!;
        Assert.Equal(GfxObjMeshFlags.HasPhysics | GfxObjMeshFlags.HasDrawing | GfxObjMeshFlags.HasDegrade, mesh.Flags);
        Assert.Equal(0x110004EEu, mesh.DegradeId);
        Assert.Equal(103, mesh.Stats.SourceVertexCount);
        Assert.Equal(102, mesh.Stats.DrawingPolygonCount);
        Assert.Equal(11, mesh.Mesh.SubMeshes.Count);
        Assert.Equal(275, mesh.Mesh.SubMeshes.Sum(s => s.Positions.Length));
        Assert.Equal(176, mesh.Mesh.TriangleCount);
        Assert.NotNull(mesh.DrawingSphere);
        Assert.NotNull(mesh.PhysicsSphere);

        // Every emitted vertex lies inside the drawing sphere and the vertex-array bounds.
        var sphere = mesh.DrawingSphere.Value;
        foreach (var p in mesh.Mesh.SubMeshes.SelectMany(s => s.Positions))
        {
            Assert.True(Vector3.Distance(p, sphere.Center) <= sphere.Radius + 1e-3f);
        }

        Assert.True(mesh.VertexBounds.Size.Z > 1f);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void NoPos_polygons_use_uv_index_zero()
    {
        var gfxObj = fixture.Dats.TryGetPortal<GfxObj>(0x0100007B, out var g) ? g : null;
        Assert.NotNull(gfxObj);
        var mesh = GfxObjMeshBuilder.Build(0x0100007B, gfxObj);
        Assert.Equal(8, mesh.Stats.NoPosPolygonCount);

        var noPos = gfxObj.Polygons.Values.Where(p => p.Stippling.HasFlag(StipplingType.NoPos)).ToArray();
        Assert.All(noPos, p => Assert.Empty(p.PosUVIndices));
        var surfaceId = gfxObj.Surfaces[noPos[0].PosSurface].DataId;
        var sub = mesh.Mesh.SubMeshes.Single(s => s.SurfaceId == surfaceId && s.Side == MeshSide.Positive);
        foreach (var vertexId in noPos.SelectMany(p => p.VertexIds).Distinct())
        {
            var vertex = gfxObj.VertexArray.Vertices[(ushort)vertexId];
            var expectedUv = vertex.UVs.Count > 0 ? new Vector2(vertex.UVs[0].U, vertex.UVs[0].V) : Vector2.Zero;
            Assert.Contains(Enumerable.Range(0, sub.Positions.Length), i => sub.Positions[i] == vertex.Origin && sub.Uvs[i] == expectedUv);
        }
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Separate_back_polygon_uses_negative_surface_and_uvs()
    {
        var gfxObj = fixture.Dats.TryGetPortal<GfxObj>(0x010008AA, out var g) ? g : null;
        Assert.NotNull(gfxObj);
        var polygon = gfxObj.Polygons[2];
        Assert.Equal(CullMode.Clockwise, polygon.SidesType);
        Assert.Equal(0x08000CEEu, gfxObj.Surfaces[polygon.PosSurface].DataId);
        Assert.Equal(0x08000102u, gfxObj.Surfaces[polygon.NegSurface].DataId);

        var mesh = GfxObjMeshBuilder.Build(0x010008AA, gfxObj);
        Assert.Equal(1, mesh.Stats.SeparateBackPolygonCount);
        var back = mesh.Mesh.SubMeshes.Single(s => s.SurfaceId == 0x08000102 && s.Side == MeshSide.Negative);
        for (var i = 0; i < polygon.VertexIds.Count; i++)
        {
            var vertex = gfxObj.VertexArray.Vertices[(ushort)polygon.VertexIds[i]];
            var uv = vertex.UVs[polygon.NegUVIndices[i]];
            Assert.Contains(
                Enumerable.Range(0, back.Positions.Length),
                j => back.Positions[j] == vertex.Origin && back.Normals[j] == -vertex.Normal && back.Uvs[j] == new Vector2(uv.U, uv.V));
        }
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Anchor_gfxobj_is_a_single_transparent_triangle()
    {
        var mesh = GfxObjMeshBuilder.Load(fixture.Dats, 0x010001EC)!;
        Assert.Equal(0x1100002Eu, mesh.DegradeId);
        var sub = Assert.Single(mesh.Mesh.SubMeshes);
        Assert.Equal(0x08000015u, sub.SurfaceId);
        Assert.Equal(1, mesh.Mesh.TriangleCount);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Polygon_core_also_builds_an_EnvCell_cellstruct()
    {
        // Holtburg interior cell 0xA9B40100: Environment 0x0D00034F, CellStruct 0, 11 surfaces, 31 polygons of which
        // the 6 portal polygons are NoPos on fully translucent surfaces (python reader, session scratch).
        Assert.True(fixture.Dats.TryGetCell<EnvCell>(0xA9B40100, out var cell));
        Assert.True(fixture.Dats.TryGetPortal<DatReaderWriter.DBObjs.Environment>(0x0D000000u | cell.EnvironmentId, out var environment));
        Assert.Equal(0x0D00034Fu, 0x0D000000u | cell.EnvironmentId);
        var cellStruct = environment.Cells[cell.CellStructure];
        var surfaces = cell.Surfaces.Select(s => 0x08000000u | s).ToArray();
        Assert.Equal(11, surfaces.Length);

        var built = GfxObjMeshBuilder.BuildPolygons(0xA9B40100, surfaces, cellStruct.VertexArray.Vertices, cellStruct.Polygons);
        Assert.Equal(31, built.Stats.DrawingPolygonCount);
        Assert.Equal(6, built.Stats.NoPosPolygonCount);
        Assert.Equal(cellStruct.Polygons.Values.Sum(p => p.VertexIds.Count - 2), built.Mesh.TriangleCount);
        Assert.All(built.Mesh.SubMeshes, s => Assert.Contains(s.SurfaceId, surfaces));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Surface_indices_with_different_retail_addressing_get_one_sub_mesh_each()
    {
        // 0x010000D1 lists Surface 0x080001CF at indices 0, 1 and 2; retail draws them as three subsets whose
        // WRAP/CLAMP flags disagree. The expected flags are recomputed from the raw record.
        var raw = RawGfxObj(0x010000D1);
        var wrap = new bool[raw.Surfaces.Length];
        foreach (var (_, p) in raw.Polygons)
        {
            wrap[(ushort)p.PosSurface] |= unchecked((sbyte)p.Stippling) > 0;
        }

        var mesh = GfxObjMeshBuilder.Load(fixture.Dats, 0x010000D1)!;
        var subs = mesh.Mesh.SubMeshes
            .Select((m, i) => (Mesh: m, Info: mesh.SubMeshInfo[i]))
            .Where(x => x.Mesh.SurfaceId == 0x080001CF && x.Mesh.Side == MeshSide.Positive)
            .ToList();
        Assert.Equal(
            new[] { TextureAddressMode.Wrap, TextureAddressMode.Clamp }.ToHashSet(),
            subs.Select(x => x.Mesh.Addressing).ToHashSet());
        foreach (var (sub, info) in subs)
        {
            Assert.All(info.Ranges, r => Assert.Equal(wrap[r.SurfaceIndex], sub.Addressing == TextureAddressMode.Wrap));
        }

        Assert.Equal(new[] { 0, 1, 2 }, subs.SelectMany(x => x.Info.Ranges.Select(r => r.SurfaceIndex)).Order());
        Assert.Equal(
            raw.Polygons.Where(p => raw.Surfaces[(ushort)p.Polygon.PosSurface] == 0x080001CF).Sum(p => 3 * (p.Polygon.VertexIds.Length - 2)),
            subs.Sum(x => x.Info.Ranges.Sum(r => r.IndexCount)));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void EnvCell_portal_polygons_are_on_untextured_surfaces_that_retail_skips()
    {
        Assert.True(fixture.Dats.TryGetCell<EnvCell>(0xA9B40100, out var cell));
        Assert.True(fixture.Dats.TryGetPortal<DatReaderWriter.DBObjs.Environment>(0x0D000000u | cell.EnvironmentId, out var environment));
        var cellStruct = environment.Cells[cell.CellStructure];
        var surfaces = cell.Surfaces.Select(s => 0x08000000u | s).ToArray();
        var built = GfxObjMeshBuilder.BuildPolygons(0xA9B40100, surfaces, cellStruct.VertexArray.Vertices, cellStruct.Polygons);

        var portalSurfaces = cellStruct.Polygons.Values
            .Where(p => p.Stippling.HasFlag(StipplingType.NoPos))
            .Select(p => surfaces[(ushort)p.PosSurface])
            .ToHashSet();
        Assert.Contains(0x080007E6u, portalSurfaces);

        // Independent check of the Surface types: the first u32 of each raw Surface record.
        var drawn = SurfaceDrawRules.DrawnSubMeshes(fixture.Dats, built.Mesh, MeshDrawContext.EnvCell);
        for (var i = 0; i < built.Mesh.SubMeshes.Count; i++)
        {
            var surfaceId = built.Mesh.SubMeshes[i].SurfaceId;
            var textured = (RawSurfaceType(surfaceId) & 6) != 0;
            Assert.Equal(textured, drawn[i]);
            Assert.True(textured != portalSurfaces.Contains(surfaceId), $"0x{surfaceId:X8}: textured {textured}");
        }

        Assert.All(SurfaceDrawRules.DrawnSubMeshes(fixture.Dats, built.Mesh, MeshDrawContext.Object), Assert.True);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Building_skips_its_untextured_portal_sub_mesh_only_when_drawn_as_a_building()
    {
        var mesh = GfxObjMeshBuilder.Load(fixture.Dats, 0x01000C1E)!;
        var asBuilding = SurfaceDrawRules.DrawnSubMeshes(fixture.Dats, mesh.Mesh, MeshDrawContext.Building);
        var asObject = SurfaceDrawRules.DrawnSubMeshes(fixture.Dats, mesh.Mesh, MeshDrawContext.Object);
        Assert.All(asObject, Assert.True);

        var skipped = mesh.Mesh.SubMeshes.Where((_, i) => !asBuilding[i]).ToArray();
        Assert.Equal(
            mesh.Mesh.SubMeshes.Where(s => (RawSurfaceType(s.SurfaceId) & 6) == 0).Select(s => (s.SurfaceId, s.Side)),
            skipped.Select(s => (s.SurfaceId, s.Side)));

        // The door and window portals: Surface 0x080007E6, 40 vertices and 24 triangles (python reader), all of them
        // NoPos polygons.
        var portal = Assert.Single(skipped);
        Assert.Equal((0x080007E6u, MeshSide.Positive), (portal.SurfaceId, portal.Side));
        Assert.Equal((40, 24), (portal.Positions.Length, portal.Indices.Length / 3));
        var raw = RawGfxObj(0x01000C1E);
        var portalPolygons = raw.Polygons.Where(p => raw.Surfaces[(ushort)p.Polygon.PosSurface] == 0x080007E6u).ToArray();
        Assert.NotEmpty(portalPolygons);
        Assert.All(portalPolygons, p => Assert.True((p.Polygon.Stippling & 4) != 0));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Missing_gfxobj_loads_as_null()
    {
        Assert.Null(GfxObjMeshBuilder.Load(fixture.Dats, 0x01004E29));
    }

    // ---------------------------------------------------------------- synthetic GfxObjs (no dats needed)

    [Fact]
    public void Single_sided_pentagon_is_a_ccw_fan_with_stored_normals()
    {
        var gfxObj = Synthetic(
            [0x08000001],
            Pentagon(),
            new Polygon { SidesType = CullMode.Landblock, PosSurface = 0, NegSurface = -1, VertexIds = [0, 1, 2, 3, 4], PosUVIndices = [0, 0, 0, 0, 0] });

        var mesh = GfxObjMeshBuilder.Build(0x01000001, gfxObj);
        var sub = Assert.Single(mesh.Mesh.SubMeshes);
        Assert.Equal(MeshSide.Positive, sub.Side);
        Assert.Equal(0x08000001u, sub.SurfaceId);
        Assert.Equal(new[] { 0, 1, 2, 0, 2, 3, 0, 3, 4 }, sub.Indices);
        Assert.All(sub.Normals, n => Assert.Equal(Vector3.UnitZ, n));
        for (var t = 0; t < sub.Indices.Length; t += 3)
        {
            var face = Vector3.Cross(
                sub.Positions[sub.Indices[t + 1]] - sub.Positions[sub.Indices[t]],
                sub.Positions[sub.Indices[t + 2]] - sub.Positions[sub.Indices[t]]);
            Assert.True(face.Z > 0f);
        }

        var info = Assert.Single(mesh.SubMeshInfo);
        Assert.Equal((0, 1), (info.FirstSurfaceIndex, info.PolygonCount));
        Assert.Equal(TextureAddressMode.Clamp, sub.Addressing); // stippling 0: retail CLAMP
        Assert.Equal(new SurfaceIndexRange(0, 0, 9), Assert.Single(info.Ranges));
    }

    [Fact]
    public void Double_sided_polygon_gets_a_reversed_copy_with_negated_normals_and_the_same_uvs()
    {
        var gfxObj = Synthetic(
            [0x08000001],
            Pentagon(),
            new Polygon { SidesType = CullMode.None, PosSurface = 0, NegSurface = 0, VertexIds = [0, 1, 2, 3], PosUVIndices = [1, 1, 1, 1] });

        var mesh = GfxObjMeshBuilder.Build(0x01000001, gfxObj);
        Assert.Equal(2, mesh.Mesh.SubMeshes.Count);
        var front = mesh.Mesh.SubMeshes[0];
        var back = mesh.Mesh.SubMeshes[1];
        Assert.Equal((0x08000001u, MeshSide.Positive), (front.SurfaceId, front.Side));
        Assert.Equal((0x08000001u, MeshSide.Negative), (back.SurfaceId, back.Side));
        Assert.Equal(new[] { 0, 1, 2, 0, 2, 3 }, front.Indices);
        Assert.Equal(new[] { 2, 1, 0, 3, 2, 0 }, back.Indices);
        Assert.Equal(front.Positions, back.Positions);
        Assert.Equal(front.Uvs, back.Uvs);
        Assert.All(back.Normals, n => Assert.Equal(-Vector3.UnitZ, n));
        Assert.All(front.Uvs, uv => Assert.Equal(new Vector2(0.5f, 0.25f), uv));
        Assert.Equal(1, mesh.Stats.DoubleSidedPolygonCount);
    }

    [Fact]
    public void Separate_back_side_uses_neg_surface_and_neg_uvs_or_index_zero_when_absent()
    {
        var gfxObj = Synthetic(
            [0x08000001, 0x08000002],
            Pentagon(),
            new Polygon { SidesType = CullMode.Clockwise, PosSurface = 0, NegSurface = 1, VertexIds = [0, 1, 2], PosUVIndices = [0, 0, 0], NegUVIndices = [1, 1, 1] },
            new Polygon { Stippling = StipplingType.NoNeg, SidesType = CullMode.Clockwise, PosSurface = 0, NegSurface = 1, VertexIds = [2, 3, 4], PosUVIndices = [1, 1, 1] });

        var mesh = GfxObjMeshBuilder.Build(0x01000001, gfxObj);
        Assert.Equal(2, mesh.Stats.SeparateBackPolygonCount);
        var back = mesh.Mesh.SubMeshes.Single(s => s.Side == MeshSide.Negative);
        Assert.Equal(0x08000002u, back.SurfaceId);
        Assert.Equal(new[] { 2, 1, 0, 5, 4, 3 }, back.Indices);

        // First polygon: explicit neg UV index 1; second: NoNeg, so UV index 0 for every corner.
        Assert.Equal(new Vector2(0.5f, 0.25f), back.Uvs[0]);
        Assert.Equal(new Vector2(0.2f, 0.4f), back.Uvs[3]); // vertex 2's UV 0
        Assert.Equal(new[] { 2, 2 }, mesh.SubMeshInfo.Select(i => i.PolygonCount));
    }

    [Fact]
    public void NoPos_uses_uv_zero_and_out_of_range_uv_is_zero()
    {
        var vertices = Pentagon();
        vertices[4].UVs.Clear();
        var gfxObj = Synthetic(
            [0x08000001],
            vertices,
            new Polygon { Stippling = StipplingType.NoPos, SidesType = CullMode.Landblock, PosSurface = 0, VertexIds = [0, 1, 2] },
            new Polygon { SidesType = CullMode.Landblock, PosSurface = 0, VertexIds = [2, 3, 4], PosUVIndices = [1, 7, 1] });

        var mesh = GfxObjMeshBuilder.Build(0x01000001, gfxObj);
        var sub = Assert.Single(mesh.Mesh.SubMeshes);
        Assert.Equal(1, mesh.Stats.NoPosPolygonCount);
        Assert.Equal(2, mesh.Stats.OutOfRangeUvCount);

        // Vertex 2 appears with uv index 0 (NoPos) and uv index 1: split into two vertices.
        Assert.Equal(6, sub.Positions.Length);
        Assert.Equal(Vector2.Zero, sub.Uvs[0]);
        Assert.Equal(new Vector2(0.5f, 0.25f), sub.Uvs[3]);
        Assert.Equal(Vector2.Zero, sub.Uvs[4]); // index 7 is out of range
        Assert.Equal(Vector2.Zero, sub.Uvs[5]); // vertex 4 has no UVs
        Assert.Equal(TextureAddressMode.Wrap, sub.Addressing); // stippling 4 > 0 marks the surface index "stippled"
    }

    [Fact]
    public void Same_surface_id_at_two_indices_with_the_same_addressing_shares_a_sub_mesh_with_per_index_ranges()
    {
        var gfxObj = Synthetic(
            [0x08000005, 0x08000009, 0x08000005],
            Pentagon(),
            new Polygon { SidesType = CullMode.Landblock, PosSurface = 1, VertexIds = [0, 1, 2], PosUVIndices = [0, 0, 0] },
            new Polygon { Stippling = StipplingType.Positive, SidesType = CullMode.Landblock, PosSurface = 2, VertexIds = [0, 2, 3], PosUVIndices = [0, 0, 0] },
            new Polygon { Stippling = StipplingType.Positive, SidesType = CullMode.Landblock, PosSurface = 0, VertexIds = [0, 3, 4], PosUVIndices = [0, 0, 0] });

        var mesh = GfxObjMeshBuilder.Build(0x01000001, gfxObj);
        Assert.Equal(new[] { 0x08000005u, 0x08000009u }, mesh.Mesh.SubMeshes.Select(s => s.SurfaceId));
        Assert.Equal(new[] { TextureAddressMode.Wrap, TextureAddressMode.Clamp }, mesh.Mesh.SubMeshes.Select(s => s.Addressing));

        // Retail keeps one subset per surface index; indices 0 and 2 are both WRAP, so they share a sub-mesh. The merged
        // sub-mesh holds index 0's triangle first although its polygon comes last in the file.
        var merged = mesh.SubMeshInfo[0];
        Assert.Equal((0, 2), (merged.FirstSurfaceIndex, merged.PolygonCount));
        Assert.Equal(new[] { new SurfaceIndexRange(0, 0, 3), new SurfaceIndexRange(2, 3, 3) }, merged.Ranges);
        Assert.Equal(new[] { 0, 2, 3, 0, 1, 2 }, mesh.Mesh.SubMeshes[0].Indices);

        var single = mesh.SubMeshInfo[1];
        Assert.Equal(new SurfaceIndexRange(1, 0, 3), Assert.Single(single.Ranges));

        // Shared corner vertex 0 (same uv index) is emitted once in the merged sub-mesh.
        Assert.Equal(4, mesh.Mesh.SubMeshes[0].Positions.Length);
    }

    [Fact]
    public void Same_surface_id_at_two_indices_with_different_addressing_gets_a_sub_mesh_per_addressing()
    {
        // Index 0 is WRAP (its polygon is stippled), index 2 is CLAMP: retail samples the two subsets differently, so
        // MeshData keeps them apart (one SubMesh per surface, side and addressing). Index 0's mask bit comes from the
        // last polygon in the file, so the builder must know every polygon before it assigns the first triangle.
        var gfxObj = Synthetic(
            [0x08000005, 0x08000009, 0x08000005],
            Pentagon(),
            new Polygon { SidesType = CullMode.Landblock, PosSurface = 1, VertexIds = [0, 1, 2], PosUVIndices = [0, 0, 0] },
            new Polygon { SidesType = CullMode.Landblock, PosSurface = 2, VertexIds = [0, 2, 3], PosUVIndices = [0, 0, 0] },
            new Polygon { SidesType = CullMode.Landblock, PosSurface = 0, VertexIds = [0, 1, 4], PosUVIndices = [0, 0, 0] },
            new Polygon { Stippling = StipplingType.Positive, SidesType = CullMode.Landblock, PosSurface = 0, VertexIds = [0, 3, 4], PosUVIndices = [0, 0, 0] });

        var mesh = GfxObjMeshBuilder.Build(0x01000001, gfxObj);
        Assert.Equal(
            new[] { (0x08000005u, TextureAddressMode.Wrap), (0x08000009u, TextureAddressMode.Clamp), (0x08000005u, TextureAddressMode.Clamp) },
            mesh.Mesh.SubMeshes.Select(s => (s.SurfaceId, s.Addressing)));
        Assert.Equal(new[] { 0, 1, 2 }, mesh.SubMeshInfo.Select(i => i.FirstSurfaceIndex));
        Assert.Equal(new[] { 2, 1, 1 }, mesh.SubMeshInfo.Select(i => i.PolygonCount));
        Assert.Equal(new[] { 0, 1, 2, 0, 3, 2 }, mesh.Mesh.SubMeshes[0].Indices);
        Assert.Equal(new[] { 0, 1, 2 }, mesh.Mesh.SubMeshes[2].Indices);
    }

    [Fact]
    public void Uv_index_is_read_as_a_signed_byte_like_copyVert()
    {
        var vertices = Pentagon();
        for (var i = 2; i < 130; i++)
        {
            vertices[0].UVs.Add(new Vec2Duv { U = i, V = i });
        }

        var gfxObj = Synthetic(
            [0x08000001],
            vertices,
            new Polygon { SidesType = CullMode.Landblock, PosSurface = 0, VertexIds = [0, 1, 2], PosUVIndices = [129, 1, 1] },
            new Polygon { SidesType = CullMode.Landblock, PosSurface = 0, VertexIds = [0, 2, 3], PosUVIndices = [127, 1, 1] });

        var mesh = GfxObjMeshBuilder.Build(0x01000001, gfxObj);
        var sub = Assert.Single(mesh.Mesh.SubMeshes);

        // 129 exists in vertex 0's 130-entry UV list but is -127 as a char, so retail writes (0, 0); 127 is used.
        Assert.Equal(Vector2.Zero, sub.Uvs[0]);
        Assert.Equal(new Vector2(127f, 127f), sub.Uvs[3]);
        Assert.Equal(1, mesh.Stats.OutOfRangeUvCount);
    }

    [Fact]
    public void Root_spheres_come_from_the_bsp_roots_and_a_physics_leaf_root_keeps_its_sphere()
    {
        var gfxObj = Synthetic([0x08000001], Pentagon(), new Polygon { PosSurface = 0, VertexIds = [0, 1, 2], PosUVIndices = [0, 0, 0] });
        gfxObj.Flags = GfxObjFlags.HasPhysics | GfxObjFlags.HasDrawing;
        gfxObj.PhysicsBSP = new PhysicsBSPTree
        {
            Root = new PhysicsBSPNode { Type = BSPNodeType.Leaf, BoundingSphere = new Sphere { Origin = new Vector3(1f, 2f, 3f), Radius = 4f } },
        };
        gfxObj.DrawingBSP = new DrawingBSPTree { Root = new DrawingBSPNode { Type = BSPNodeType.Leaf } };

        // BSPLEAF::UnPackLeaf stores a sphere only in physics trees; BSPTREE::GetSphere returns the root's.
        var mesh = GfxObjMeshBuilder.Build(0x01000001, gfxObj);
        Assert.Equal(new ModelSphere(new Vector3(1f, 2f, 3f), 4f), mesh.PhysicsSphere);
        Assert.Null(mesh.DrawingSphere);
        Assert.Equal(mesh.PhysicsSphere, ModelAssembly.FromGfxObj(0x01000001, mesh).SortingSphere);

        gfxObj.DrawingBSP.Root = new DrawingBSPNode
        {
            Type = BSPNodeType.BPIN,
            BoundingSphere = new Sphere { Origin = new Vector3(5f, 6f, 7f), Radius = 8f },
        };
        Assert.Equal(new ModelSphere(new Vector3(5f, 6f, 7f), 8f), GfxObjMeshBuilder.Build(0x01000001, gfxObj).DrawingSphere);

        gfxObj.Flags = GfxObjFlags.HasDrawing;
        Assert.Null(GfxObjMeshBuilder.Build(0x01000001, gfxObj).PhysicsSphere);
    }

    [Fact]
    public void Untextured_surfaces_are_skipped_only_for_buildings_and_envcells()
    {
        const SurfaceType portal = SurfaceType.Base1Solid | SurfaceType.Translucent;
        Assert.False(SurfaceDrawRules.IsTextured(portal));
        Assert.False(SurfaceDrawRules.IsTextured(SurfaceType.Base1Solid));
        Assert.True(SurfaceDrawRules.IsTextured(SurfaceType.Base1Image));
        Assert.True(SurfaceDrawRules.IsTextured(SurfaceType.Base1ClipMap | SurfaceType.Translucent));

        Assert.True(SurfaceDrawRules.IsDrawn(portal, MeshDrawContext.Object));
        Assert.False(SurfaceDrawRules.IsDrawn(portal, MeshDrawContext.Building));
        Assert.False(SurfaceDrawRules.IsDrawn(portal, MeshDrawContext.EnvCell));
        Assert.True(SurfaceDrawRules.IsDrawn(SurfaceType.Base1Image, MeshDrawContext.Building));
        Assert.True(SurfaceDrawRules.IsDrawn(SurfaceType.Base1ClipMap, MeshDrawContext.EnvCell));
    }

    [Fact]
    public void Bad_indices_fail_loudly()
    {
        var missingVertex = Synthetic([0x08000001], Pentagon(), new Polygon { PosSurface = 0, VertexIds = [0, 1, 9], PosUVIndices = [0, 0, 0] });
        Assert.Throws<InvalidDataException>(() => GfxObjMeshBuilder.Build(0x01000001, missingVertex));

        var missingSurface = Synthetic([0x08000001], Pentagon(), new Polygon { PosSurface = 3, VertexIds = [0, 1, 2], PosUVIndices = [0, 0, 0] });
        Assert.Throws<InvalidDataException>(() => GfxObjMeshBuilder.Build(0x01000001, missingSurface));
    }

    [Fact]
    public void Degenerate_polygons_emit_nothing_and_physics_only_gfxobjs_are_empty()
    {
        var degenerate = Synthetic([0x08000001], Pentagon(), new Polygon { PosSurface = 0, VertexIds = [0, 1], PosUVIndices = [0, 0] });
        var mesh = GfxObjMeshBuilder.Build(0x01000001, degenerate);
        Assert.Empty(mesh.Mesh.SubMeshes);
        Assert.Equal(1, mesh.Stats.DegeneratePolygonCount);

        var physicsOnly = Synthetic([0x08000001], Pentagon(), new Polygon { PosSurface = 0, VertexIds = [0, 1, 2], PosUVIndices = [0, 0, 0] });
        physicsOnly.Flags = GfxObjFlags.HasPhysics;
        Assert.Empty(GfxObjMeshBuilder.Build(0x01000001, physicsOnly).Mesh.SubMeshes);
    }

    private RawModelReader.RawGfxObj RawGfxObj(uint id) => RawModelReader.ReadGfxObj(RawModelReader.PortalBytes(fixture.Dats, id));

    private uint RawSurfaceType(uint surfaceId) => RawModelReader.ReadSurfaceType(RawModelReader.PortalBytes(fixture.Dats, surfaceId));

    internal static GfxObj Synthetic(uint[] surfaces, List<SWVertex> vertices, params Polygon[] polygons)
    {
        var gfxObj = new GfxObj
        {
            Flags = GfxObjFlags.HasDrawing,
            VertexArray = new VertexArray { VertexType = VertexType.CSWVertexType },
            SortCenter = new Vector3(0.5f, 0.5f, 0f),
        };
        gfxObj.Surfaces.AddRange(surfaces.Select(s => (QualifiedDataId<Surface>)s));
        for (var i = 0; i < vertices.Count; i++)
        {
            gfxObj.VertexArray.Vertices.Add((ushort)i, vertices[i]);
        }

        for (var i = 0; i < polygons.Length; i++)
        {
            gfxObj.Polygons.Add((ushort)i, polygons[i]);
        }

        return gfxObj;
    }

    /// <summary>A unit pentagon in the XY plane, counter-clockwise seen from +Z, normals +Z, two UVs per vertex.</summary>
    internal static List<SWVertex> Pentagon()
    {
        Vector3[] corners = [new(0, 0, 0), new(1, 0, 0), new(1.3f, 0.8f, 0), new(0.5f, 1.4f, 0), new(-0.3f, 0.8f, 0)];
        return corners.Select((c, i) => new SWVertex
        {
            Origin = c,
            Normal = Vector3.UnitZ,
            UVs = [new Vec2Duv { U = i * 0.1f, V = i * 0.2f }, new Vec2Duv { U = 0.5f, V = 0.25f }],
        }).ToList();
    }
}
