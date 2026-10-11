using System.Numerics;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using Lifestone.Data.Dat;
using Lifestone.Data.Geometry;
using Lifestone.Data.Models;
using Xunit.Abstractions;

namespace Lifestone.Data.Tests.Models;

/// <summary>
/// Whole-dat sweeps: every GfxObj, Setup and GfxObjDegradeInfo of the user's end-of-retail portal.dat is decoded by
/// DatReaderWriter, re-read by the independent <see cref="RawModelReader"/> (byte-exact), and turned into
/// Lifestone model data whose invariants are checked. Expected totals were measured independently with the python
/// research readers (session scratch <c>models/survey.py</c>, <c>survey2.py</c>) and agree with
/// <c>docs/research/dat-format.md</c> §4 where it reports them.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class ModelsExhaustiveTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    // Totals measured on the end-of-retail portal.dat; the sweeps below say how each is counted.
    private const int MergedSubMeshCount = 84;
    private const int MergedGfxObjCount = 70;
    private const int SplitByAddressingPairCount = 24;
    private const int SplitByAddressingClampPolygonCount = 137;
    private const int UpperLevelCount = 6_082;

    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void Every_GfxObj_builds_a_well_formed_mesh_that_matches_an_independent_decode()
    {
        var ids = Dats.Read(d => d.Portal.GetAllIdsOfType<GfxObj>().ToArray());
        Assert.Equal(15_318, ids.Length);

        var surfaceIds = Dats.Read(d => d.Portal.GetAllIdsOfType<Surface>().ToHashSet());
        long triangles = 0, vertices = 0, subMeshes = 0;
        int sides0 = 0, sides1 = 0, sides2 = 0, noPos = 0, stippled = 0, zeroNormalGfxObjs = 0, outOfRangeUv = 0;
        int rawZeroNormalVertices = 0, rawZeroNormalGfxObjs = 0, rawDrawnZeroNormalGfxObjs = 0;
        int withDegrade = 0, wrapSubMeshes = 0, clampSubMeshes = 0, leafRootDrawing = 0, leafRootPhysics = 0;
        int frontAgree = 0, frontTotal = 0, backAgree = 0, backTotal = 0;
        int mergedSubMeshes = 0, mergedGfxObjs = 0, splitPairs = 0, splitClampPolygons = 0, clampUvsOutside = 0;
        long comparedTriangles = 0;

        foreach (var id in ids)
        {
            Assert.True(Dats.TryGetPortal<GfxObj>(id, out var gfxObj), $"0x{id:X8}");
            var bytes = Dats.Read(d => d.Portal.TryGetFileBytes(id, out var b) ? b : null);
            Assert.NotNull(bytes);
            var raw = RawModelReader.ReadGfxObj(bytes);
            Assert.True(raw.Consumed == bytes.Length, $"0x{id:X8}: raw reader consumed {raw.Consumed} of {bytes.Length} bytes");
            AssertSameDecode(id, raw, gfxObj);

            var mesh = GfxObjMeshBuilder.Build(id, gfxObj);
            AssertWellFormed(mesh, surfaceIds);
            var independent = AssertMatchesIndependentConstructMesh(mesh, raw);
            comparedTriangles += independent.Triangles;
            splitClampPolygons += independent.SplitClampPolygons;
            clampUvsOutside += independent.ClampUvsOutsideUnitSquare;
            mergedSubMeshes += mesh.SubMeshInfo.Count(i => i.Ranges.Count > 1);
            mergedGfxObjs += mesh.SubMeshInfo.Any(i => i.Ranges.Count > 1) ? 1 : 0;
            splitPairs += mesh.Mesh.SubMeshes.GroupBy(s => (s.SurfaceId, s.Side)).Count(g => g.Count() > 1);

            var zeroNormalKeys = raw.Vertices.Where(v => v.Normal == Vector3.Zero).Select(v => (short)v.Key).ToHashSet();
            rawZeroNormalVertices += zeroNormalKeys.Count;
            rawZeroNormalGfxObjs += zeroNormalKeys.Count > 0 ? 1 : 0;
            rawDrawnZeroNormalGfxObjs += raw.Polygons.Any(p => p.Polygon.VertexIds.Length >= 3 && p.Polygon.VertexIds.Any(zeroNormalKeys.Contains)) ? 1 : 0;

            foreach (var (_, polygon) in raw.Polygons)
            {
                switch (polygon.SidesType)
                {
                    case 0: sides0++; break;
                    case 1: sides1++; break;
                    case 2: sides2++; break;
                }

                if ((polygon.Stippling & 4) != 0)
                {
                    noPos++;
                }

                if (polygon.Stippling == 1)
                {
                    stippled++;
                }
            }

            CountWindingAgreement(mesh, ref frontAgree, ref frontTotal, ref backAgree, ref backTotal);
            triangles += mesh.Mesh.TriangleCount;
            vertices += mesh.Mesh.SubMeshes.Sum(s => s.Positions.Length);
            subMeshes += mesh.Mesh.SubMeshes.Count;
            zeroNormalGfxObjs += mesh.Stats.ZeroNormalVertexCount > 0 ? 1 : 0;
            outOfRangeUv += mesh.Stats.OutOfRangeUvCount;
            withDegrade += mesh.DegradeId != 0 ? 1 : 0;
            wrapSubMeshes += mesh.Mesh.SubMeshes.Count(s => s.Addressing == TextureAddressMode.Wrap);
            clampSubMeshes += mesh.Mesh.SubMeshes.Count(s => s.Addressing == TextureAddressMode.Clamp);
            leafRootDrawing += mesh.DrawingSphere is null ? 1 : 0;
            leafRootPhysics += mesh.Flags.HasFlag(GfxObjMeshFlags.HasPhysics) && mesh.PhysicsSphere is null ? 1 : 0;
        }

        output.WriteLine(
            $"{ids.Length} GfxObjs: {subMeshes} sub-meshes, {vertices} vertices, {triangles} triangles; " +
            $"wrap/clamp sub-meshes {wrapSubMeshes}/{clampSubMeshes}; {withDegrade} with DIDDegrade; " +
            $"{leafRootDrawing} without a drawing sphere, {leafRootPhysics} physics GfxObjs without a physics sphere; " +
            $"front/back winding agreement {frontAgree}/{frontTotal}, {backAgree}/{backTotal}; {comparedTriangles} " +
            $"triangles compared corner by corner; {mergedSubMeshes} sub-meshes in {mergedGfxObjs} GfxObjs merge surface " +
            $"indices; {splitPairs} (surface, side) pairs split by WRAP/CLAMP ({splitClampPolygons} CLAMP polygons); " +
            $"{clampUvsOutside} CLAMP corners with a UV outside [0, 1]");

        // Independent python survey of the same dats (dat-format.md §4 reports the sides histogram).
        Assert.Equal(714_822, sides0);
        Assert.Equal(154_479, sides1);
        Assert.Equal(11, sides2);
        Assert.Equal(4_657, noPos);
        Assert.Equal(254_841, stippled);
        // 16,043 stored normals are exactly zero (1,000 GfxObjs); drawing polygons use them in 99 GfxObjs.
        Assert.Equal(16_043, rawZeroNormalVertices);
        Assert.Equal(1_000, rawZeroNormalGfxObjs);
        Assert.Equal(rawDrawnZeroNormalGfxObjs, zeroNormalGfxObjs);
        Assert.Equal(99, zeroNormalGfxObjs);
        Assert.Equal(2, outOfRangeUv);
        Assert.Equal(4_131, withDegrade);
        Assert.Equal(0, leafRootDrawing);
        Assert.Equal(0, leafRootPhysics);

        // One Surface id at several used surface indices: indices with the same retail addressing share a sub-mesh but
        // keep their own ranges; indices whose WRAP/CLAMP differs get one sub-mesh each, so 24 (surface, side) pairs are
        // split in two (the GfxObjMeshBuilder remarks quote these totals).
        Assert.Equal(MergedSubMeshCount, mergedSubMeshes);
        Assert.Equal(MergedGfxObjCount, mergedGfxObjs);
        Assert.Equal(SplitByAddressingPairCount, splitPairs);
        Assert.Equal(SplitByAddressingClampPolygonCount, splitClampPolygons);

        // Every CLAMP corner has its UV inside [0, 1], so a consumer that sampled a CLAMP sub-mesh with WRAP would only
        // change bilinear filtering at texture edges.
        Assert.Equal(0, clampUvsOutside);

        // AC front faces are counter-clockwise under the right-hand rule: the fan triangles agree with the stored
        // vertex normals (rendering.md measured 99.3% on a sample); the negative side flips both, so it agrees too.
        Assert.True(frontAgree > 0.98 * frontTotal, $"front {frontAgree}/{frontTotal}");
        Assert.True(backAgree > 0.98 * backTotal, $"back {backAgree}/{backTotal}");
    }

    [RequiresDatsFact]
    public void Every_Setup_resolves_and_matches_an_independent_decode_including_lights()
    {
        var ids = Dats.Read(d => d.Portal.GetAllIdsOfType<Setup>().ToArray());
        Assert.Equal(5_935, ids.Length);

        int withLights = 0, withResting = 0, nonUnitScale = 0, withDefaultAnimation = 0, hooks = 0;
        var partIds = new HashSet<uint>();
        var assemblies = new List<ModelAssembly>(ids.Length);
        foreach (var id in ids)
        {
            Assert.True(Dats.TryGetPortal<Setup>(id, out var setup), $"0x{id:X8}");
            var bytes = Dats.Read(d => d.Portal.TryGetFileBytes(id, out var b) ? b : null);
            Assert.NotNull(bytes);
            var raw = RawModelReader.ReadSetup(bytes);
            Assert.True(raw.Consumed == bytes.Length, $"0x{id:X8}: raw reader consumed {raw.Consumed} of {bytes.Length} bytes");

            var assembly = ModelAssembly.FromSetup(id, setup);
            assemblies.Add(assembly);
            AssertSameSetup(id, raw, assembly);

            var pose = assembly.ComputeRestPose();
            Assert.Equal(assembly.Parts.Count, pose.Length);
            foreach (var p in pose)
            {
                Assert.True(IsFinite(p.Origin) && IsFinite(p.Scale) && IsFinite(p.Orientation), $"0x{id:X8} pose");
                Assert.True(MathF.Abs(p.Orientation.Length() - 1f) < 1e-3f, $"0x{id:X8} non-unit quaternion {p.Orientation}");
            }

            withLights += assembly.Lights.Count > 0 ? 1 : 0;
            Assert.True(assembly.Lights.Count <= 1, $"0x{id:X8}: {assembly.Lights.Count} lights"); // DatReaderWriter keys lights by type
            withResting += assembly.PlacementFrames.ContainsKey(PlacementIds.Resting) ? 1 : 0;
            nonUnitScale += assembly.Parts.Any(p => p.DefaultScale != Vector3.One) ? 1 : 0;
            withDefaultAnimation += assembly.DefaultAnimationId != 0 ? 1 : 0;
            hooks += assembly.PlacementFrames.Values.Sum(f => f.HookCount);
            foreach (var part in assembly.Parts)
            {
                partIds.Add(part.GfxObjId);
            }
        }

        // Retail CPartArray::InitParts refuses an object when any part fails CPhysicsPart::makePhysicsPart.
        var unloadable = partIds.Where(p => !(p >> 24 == 0x01 && LodChain.Load(Dats, p).IsLoadable)).ToHashSet();
        var failingSetups = assemblies.Where(a => a.Parts.Any(p => unloadable.Contains(p.GfxObjId))).Select(a => a.Id).ToArray();

        output.WriteLine(
            $"{ids.Length} Setups: {withLights} with lights, {withResting} with Resting, {nonUnitScale} with non-unit " +
            $"DefaultScale, {withDefaultAnimation} with DefaultAnimation, {partIds.Count} distinct part ids, " +
            $"unloadable parts [{string.Join(", ", unloadable.Order().Select(p => $"0x{p:X8}"))}]");

        Assert.Equal(608, withLights);
        Assert.Equal(2_629, withResting);
        Assert.Equal(308, nonUnitScale);
        Assert.Equal(134, withDefaultAnimation);
        Assert.Equal(0, hooks);
        Assert.Equal(new uint[] { 0x02001C4F, 0x02001C50 }, failingSetups.Order().ToArray());
        Assert.Equal(new uint[] { 0x00000000, 0x01004E29 }, unloadable.Order().ToArray());
        Assert.DoesNotContain(partIds, p => p >> 24 == 0x02); // no nested Setups
    }

    [RequiresDatsFact]
    public void Every_degrade_table_matches_an_independent_decode_and_selects_monotonically()
    {
        var ids = Dats.Read(d => d.Portal.GetAllIdsOfType<GfxObjDegradeInfo>().ToArray());
        Assert.Equal(4_131, ids.Length);

        var gfxObjIds = Dats.Read(d => d.Portal.GetAllIdsOfType<GfxObj>().ToHashSet());
        var modes = new Dictionary<DegradeMode, int>();
        int firstLevelZeroDistance = 0, upperLevels = 0, upperLevelsWithOwnGfxObj = 0;
        foreach (var id in ids)
        {
            var table = DegradeTable.Load(Dats, id);
            Assert.NotNull(table);
            var bytes = Dats.Read(d => d.Portal.TryGetFileBytes(id, out var b) ? b : null);
            Assert.NotNull(bytes);
            var raw = RawModelReader.ReadDegrade(bytes);
            Assert.Equal(bytes.Length, raw.Consumed);
            Assert.Equal(raw.Levels.Length, table.Levels.Count);
            for (var i = 0; i < raw.Levels.Length; i++)
            {
                var (gfx, mode, min, ideal, max) = raw.Levels[i];
                var level = table.Levels[i];
                Assert.Equal(gfx, level.GfxObjId);
                Assert.Equal((int)mode, (int)level.Mode);
                Assert.Equal(min, level.MinDistance);
                Assert.Equal(ideal, level.IdealDistance);
                Assert.Equal(max, level.MaxDistance);
                modes[level.Mode] = modes.GetValueOrDefault(level.Mode) + 1;

                // Exactly the last level draws nothing; every other level's GfxObj exists.
                Assert.Equal(i == raw.Levels.Length - 1, level.DrawsNothing);
                Assert.True(level.DrawsNothing || gfxObjIds.Contains(level.GfxObjId), $"0x{id:X8} level {i}");
            }

            firstLevelZeroDistance += table.Levels[0].MaxDistance == 0f ? 1 : 0;

            // Retail draws every level with the level-0 surface array (CPhysicsPart::SetGfxObjArray, LodChain
            // remarks). Lifestone resolves each level's sub-meshes through the level's own list, which is only right
            // while the lists agree: fail loudly on a dat where they differ.
            var level0 = RawSurfaces(table.Levels[0].GfxObjId);
            for (var i = 1; i < table.Levels.Count; i++)
            {
                var levelId = table.Levels[i].GfxObjId;
                if (levelId == 0)
                {
                    continue;
                }

                upperLevels++;
                upperLevelsWithOwnGfxObj += levelId != table.Levels[0].GfxObjId ? 1 : 0;
                Assert.True(
                    level0.SequenceEqual(RawSurfaces(levelId)),
                    $"0x{id:X8} level {i} (0x{levelId:X8}) lists other surfaces than level 0 (0x{table.Levels[0].GfxObjId:X8})");
            }

            // Retail get_degrade never goes back to a nearer level as the distance grows.
            foreach (var bias in new[] { -1f, 0f, 0.5f, 1f })
            {
                var previous = 0;
                for (var distance = 0f; distance < 3000f; distance += 2.5f)
                {
                    var level = DegradeSelector.SelectLevel(table, distance, bias);
                    Assert.InRange(level, previous, table.Levels.Count - 1);
                    previous = level;
                }
            }
        }

        output.WriteLine(
            $"{ids.Length} degrade tables; modes {string.Join(", ", modes.Select(kv => $"{kv.Key}={kv.Value}"))}; " +
            $"{upperLevels} non-empty levels above 0 ({upperLevelsWithOwnGfxObj} with a GfxObj other than level 0's) " +
            "list the level-0 surfaces");
        Assert.Equal(13_501, modes[DegradeMode.Normal]);
        Assert.Equal(259, modes[DegradeMode.Billboard]);
        Assert.Equal(11, modes[DegradeMode.AxisAlignedY]);
        Assert.Equal(573, modes[DegradeMode.AxisAlignedZ]);
        Assert.False(modes.ContainsKey(DegradeMode.AxisAlignedX));
        Assert.Equal(11, firstLevelZeroDistance);
        Assert.Equal(UpperLevelCount, upperLevels);
    }

    private static void AssertSameDecode(uint id, RawModelReader.RawGfxObj raw, GfxObj gfxObj)
    {
        Assert.Equal(id, raw.Id);
        Assert.Equal(raw.Flags, (uint)gfxObj.Flags);
        Assert.Equal(raw.Surfaces, gfxObj.Surfaces.Select(s => s.DataId).ToArray());
        Assert.Equal(raw.Vertices.Length, gfxObj.VertexArray.Vertices.Count);

        // Retail CPolygon::UnPack indexes the vertex array by position; the stored keys are exactly 0..n-1.
        Assert.Equal(Enumerable.Range(0, raw.Vertices.Length).Select(i => (ushort)i), raw.Vertices.Select(v => v.Key));
        foreach (var v in raw.Vertices)
        {
            var drw = gfxObj.VertexArray.Vertices[v.Key];
            Assert.Equal(v.Origin, drw.Origin);
            Assert.Equal(v.Normal, drw.Normal);
            Assert.Equal(v.Uvs, drw.UVs.Select(uv => new Vector2(uv.U, uv.V)).ToArray());
        }

        Assert.Equal(raw.PhysicsPolygonCount, gfxObj.PhysicsPolygons.Count);
        Assert.Equal(raw.SortCenter, gfxObj.SortCenter);
        Assert.Equal(raw.Polygons.Select(p => p.Key), gfxObj.Polygons.Keys);
        foreach (var (key, p) in raw.Polygons)
        {
            var drw = gfxObj.Polygons[key];
            Assert.Equal(p.Stippling, (byte)drw.Stippling);
            Assert.Equal(p.SidesType, (int)drw.SidesType);
            Assert.Equal(p.PosSurface, drw.PosSurface);
            Assert.Equal(p.NegSurface, drw.NegSurface);
            Assert.Equal(p.VertexIds, drw.VertexIds);
            Assert.Equal(p.PosUv, drw.PosUVIndices);
            Assert.Equal(p.NegUv, drw.NegUVIndices);
        }

        Assert.Equal(raw.DegradeId, (raw.Flags & 8) != 0 ? gfxObj.DIDDegrade : 0u);
    }

    private static void AssertSameSetup(uint id, RawModelReader.RawSetup raw, ModelAssembly assembly)
    {
        Assert.Equal(id, raw.Id);
        Assert.Equal(raw.Flags, (uint)assembly.Flags);
        Assert.Equal(raw.Parts, assembly.Parts.Select(p => p.GfxObjId).ToArray());
        Assert.Equal(
            raw.Parents?.Select(p => p == uint.MaxValue ? -1 : (int)p).ToArray() ?? Enumerable.Repeat(-1, raw.Parts.Length).ToArray(),
            assembly.Parts.Select(p => p.ParentIndex).ToArray());
        Assert.Equal(raw.Scales ?? Enumerable.Repeat(Vector3.One, raw.Parts.Length).ToArray(), assembly.Parts.Select(p => p.DefaultScale).ToArray());

        Assert.Equal(raw.Holding.Length, assembly.HoldingLocations.Count);
        foreach (var (key, partId, frame) in raw.Holding)
        {
            Assert.Equal(new LocationFrame(partId, new PartFrame(frame.Origin, frame.Orientation)), assembly.HoldingLocations[key]);
        }

        Assert.Equal(raw.Connection.Length, assembly.ConnectionPoints.Count);
        foreach (var (key, partId, frame) in raw.Connection)
        {
            Assert.Equal(new LocationFrame(partId, new PartFrame(frame.Origin, frame.Orientation)), assembly.ConnectionPoints[key]);
        }

        Assert.Equal(raw.Placements.Select(p => (uint)p.Key).Order(), assembly.PlacementFrames.Keys.Order());
        foreach (var (key, frames, hooks) in raw.Placements)
        {
            var placement = assembly.PlacementFrames[(uint)key];
            Assert.Equal(hooks, placement.HookCount);
            Assert.Equal(frames.Select(f => new PartFrame(f.Origin, f.Orientation)), placement.Frames);
        }

        Assert.Equal(raw.CylSpheres.Select(c => new ModelCylSphere(c.LowPoint, c.Radius, c.Height)), assembly.CylSpheres);
        Assert.Equal(raw.Spheres.Select(ToSphere), assembly.Spheres);
        Assert.Equal(raw.Height, assembly.Height);
        Assert.Equal(raw.Radius, assembly.Radius);
        Assert.Equal(raw.StepUp, assembly.StepUpHeight);
        Assert.Equal(raw.StepDown, assembly.StepDownHeight);
        Assert.Equal(ToSphere(raw.Sorting), assembly.SortingSphere);
        Assert.Equal(ToSphere(raw.Selection), assembly.SelectionSphere);

        // The earlier prototype skipped 44 bytes per light and misread the trailing ids of all 608 lit Setups; the raw reader
        // uses the validated 48-byte LIGHTINFO, so agreement here proves DatReaderWriter reads it the same way.
        Assert.Equal(raw.Lights.Length, assembly.Lights.Count);
        for (var i = 0; i < raw.Lights.Length; i++)
        {
            var r = raw.Lights[i];
            var l = assembly.Lights[i];
            Assert.Equal(r.Type, l.Type);
            Assert.Equal(new PartFrame(r.Frame.Origin, r.Frame.Orientation), l.Offset);
            Assert.Equal(r.Color, l.ColorArgb);
            Assert.Equal(r.Intensity, l.Intensity);
            Assert.Equal(r.Falloff, l.Falloff);
            Assert.Equal(r.ConeAngleBits, BitConverter.SingleToUInt32Bits(l.ConeAngle));
            Assert.Equal(0, l.Type);
            Assert.Equal(0xCDCDCDCDu, r.ConeAngleBits); // MSVC debug fill: never set by the tools
        }

        Assert.Equal(
            raw.Defaults,
            new[]
            {
                assembly.DefaultAnimationId, assembly.DefaultScriptId, assembly.DefaultMotionTableId,
                assembly.DefaultSoundTableId, assembly.DefaultScriptTableId,
            });
    }

    internal static void AssertWellFormed(GfxObjMesh mesh, IReadOnlySet<uint>? surfaceIds)
    {
        var id = mesh.Id;
        Assert.Equal(mesh.Mesh.SubMeshes.Count, mesh.SubMeshInfo.Count);
        Assert.Equal(
            mesh.Mesh.SubMeshes.Count,
            mesh.Mesh.SubMeshes.Select(s => (s.SurfaceId, s.Side, s.Addressing)).Distinct().Count());
        var previousOrder = (-1, MeshSide.Positive);
        for (var s = 0; s < mesh.Mesh.SubMeshes.Count; s++)
        {
            var sub = mesh.Mesh.SubMeshes[s];
            var info = mesh.SubMeshInfo[s];
            Assert.True(surfaceIds is null || surfaceIds.Contains(sub.SurfaceId), $"0x{id:X8}: surface 0x{sub.SurfaceId:X8} missing");
            Assert.True(sub.SurfaceId >> 24 == 0x08, $"0x{id:X8}: 0x{sub.SurfaceId:X8} is not a Surface id");
            Assert.Equal(sub.Positions.Length, sub.Normals.Length);
            Assert.Equal(sub.Positions.Length, sub.Uvs.Length);
            Assert.True(sub.Indices.Length > 0 && sub.Indices.Length % 3 == 0, $"0x{id:X8}: {sub.Indices.Length} indices");
            Assert.True(info.PolygonCount > 0);

            // Ranges: ascending surface indices whose contiguous blocks cover every index.
            Assert.NotEmpty(info.Ranges);
            Assert.Equal(info.FirstSurfaceIndex, info.Ranges[0].SurfaceIndex);
            var next = 0;
            for (var r = 0; r < info.Ranges.Count; r++)
            {
                var range = info.Ranges[r];
                Assert.True(r == 0 || range.SurfaceIndex > info.Ranges[r - 1].SurfaceIndex, $"0x{id:X8}: ranges out of order");
                Assert.True(range.FirstIndex == next && range.IndexCount > 0 && range.IndexCount % 3 == 0, $"0x{id:X8}: range {range}");
                next += range.IndexCount;
            }

            Assert.Equal(sub.Indices.Length, next);
            Assert.True(
                (info.FirstSurfaceIndex, sub.Side).CompareTo(previousOrder) > 0,
                $"0x{id:X8}: sub-meshes not ordered by (surface index, side)");
            previousOrder = (info.FirstSurfaceIndex, sub.Side);

            var used = new bool[sub.Positions.Length];
            foreach (var index in sub.Indices)
            {
                Assert.True(index >= 0 && index < sub.Positions.Length, $"0x{id:X8}: index {index} out of range");
                used[index] = true;
            }

            Assert.True(used.All(u => u), $"0x{id:X8}: unreferenced vertex");
            foreach (var p in sub.Positions)
            {
                Assert.True(IsFinite(p), $"0x{id:X8}: position {p}");
            }

            foreach (var uv in sub.Uvs)
            {
                Assert.True(float.IsFinite(uv.X) && float.IsFinite(uv.Y), $"0x{id:X8}: uv {uv}");
            }

            foreach (var n in sub.Normals)
            {
                // Stored normals are unit length or exactly zero (16,043 vertices in 1,000 GfxObjs); retail copies
                // them unnormalised, so zero normals stay zero.
                var length = n.Length();
                Assert.True(IsFinite(n) && (length == 0f || MathF.Abs(length - 1f) < 1e-3f), $"0x{id:X8}: normal {n}");
            }
        }

        Assert.True(IsFinite(mesh.SortCenter));
        Assert.True(IsFinite(mesh.Mesh.Bounds.Min) && IsFinite(mesh.Mesh.Bounds.Max));
        Assert.True(IsFinite(mesh.VertexBounds.Min) && IsFinite(mesh.VertexBounds.Max));
        if (mesh.Mesh.SubMeshes.Count > 0)
        {
            Assert.True(Vector3.Min(mesh.Mesh.Bounds.Min, mesh.VertexBounds.Min) == mesh.VertexBounds.Min);
            Assert.True(Vector3.Max(mesh.Mesh.Bounds.Max, mesh.VertexBounds.Max) == mesh.VertexBounds.Max);
        }
    }

    /// <summary>A raw <c>CSphere</c> (centre x, y, z, radius) as a <see cref="ModelSphere"/>.</summary>
    internal static ModelSphere ToSphere(Vector4 sphere) => new(new Vector3(sphere.X, sphere.Y, sphere.Z), sphere.W);

    private uint[] RawSurfaces(uint gfxObjId) => RawModelReader.ReadGfxObj(RawModelReader.PortalBytes(Dats, gfxObjId)).Surfaces;

    /// <summary>
    /// Restates retail <c>D3DPolyRender::ConstructMesh</c> and <c>copyVert</c> over the raw decode (no DatReaderWriter,
    /// no builder code) and compares every emitted triangle corner: position, normal (negated on the negative side),
    /// UV (index 0 without a UV table; (0, 0) when the index is negative as a signed byte or past the vertex's UV list),
    /// fan order (v0, v[t+1], v[t+2]) and its reversal on the negative side, the per-(vertex id, UV index) vertex split,
    /// the grouping of each sub-mesh's triangles by surface index (file order inside a group) and each group's
    /// WRAP/CLAMP flag (<c>isStippledOrAlphaedMask[pos_surface] |= (char)stippling &gt; 0</c>).
    /// </summary>
    /// <remarks>
    /// The one intended difference from retail is restated as such: retail reverses only the second winding of a
    /// <c>sides_type 1</c> polygon (<c>w ? 2 - c : c</c>) and writes a <c>sides_type 2</c> negative side unreversed;
    /// Lifestone reverses every negative-side triangle (GfxObjMeshBuilder remarks, 14 triangles on end-of-retail data).
    /// </remarks>
    private static (long Triangles, int SplitClampPolygons, int ClampUvsOutsideUnitSquare) AssertMatchesIndependentConstructMesh(GfxObjMesh mesh, RawModelReader.RawGfxObj raw)
    {
        var wrap = new bool[raw.Surfaces.Length];
        foreach (var (_, p) in raw.Polygons)
        {
            if (unchecked((sbyte)p.Stippling) > 0)
            {
                wrap[(ushort)p.PosSurface] = true;
            }
        }

        var groups = new Dictionary<(uint SurfaceId, MeshSide Side, bool Wrap), SortedDictionary<int, ExpectedGroup>>();
        var vertexKeys = new Dictionary<(uint SurfaceId, MeshSide Side, bool Wrap), HashSet<(int VertexId, int Uv)>>();
        foreach (var (polygonKey, p) in raw.Polygons)
        {
            var n = p.VertexIds.Length;
            if (n < 3)
            {
                continue;
            }

            var sides = p.SidesType == 2 ? 2 : 1;
            var windings = p.SidesType == 1 ? 2 : 1;
            for (var s = 0; s < sides; s++)
            {
                var surfaceIndex = (ushort)(s == 0 ? p.PosSurface : p.NegSurface);
                var uvs = s == 0 ? p.PosUv : p.NegUv;
                for (var w = 0; w < windings; w++)
                {
                    var negative = s == 1 || w == 1;
                    var key = (raw.Surfaces[surfaceIndex], negative ? MeshSide.Negative : MeshSide.Positive, wrap[surfaceIndex]);
                    if (!groups.TryGetValue(key, out var byIndex))
                    {
                        groups[key] = byIndex = [];
                        vertexKeys[key] = [];
                    }

                    if (!byIndex.TryGetValue(surfaceIndex, out var group))
                    {
                        byIndex[surfaceIndex] = group = new ExpectedGroup();
                    }

                    group.Polygons.Add(polygonKey);
                    for (var t = 0; t < n - 2; t++)
                    {
                        var triangle = new ExpectedCorner[3];
                        for (var c = 0; c < 3; c++)
                        {
                            var k = c == 0 ? 0 : c + t;
                            int uv = uvs.Length == n ? uvs[k] : 0;
                            var vertex = raw.Vertices[(ushort)p.VertexIds[k]];
                            var uvValue = unchecked((sbyte)uv) >= 0 && uv < vertex.Uvs.Length ? vertex.Uvs[uv] : Vector2.Zero;
                            triangle[negative ? 2 - c : c] = new ExpectedCorner(vertex.Origin, negative ? -vertex.Normal : vertex.Normal, uvValue);
                            vertexKeys[key].Add(((ushort)p.VertexIds[k], uv));
                        }

                        group.Corners.AddRange(triangle);
                    }
                }
            }
        }

        var id = raw.Id;
        Assert.Equal(groups.Count, mesh.Mesh.SubMeshes.Count);
        long triangles = 0;
        var clampUvsOutside = 0;
        var splitClampPolygons = new HashSet<ushort>();
        var splitPairs = groups.Keys.GroupBy(k => (k.SurfaceId, k.Side)).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        for (var s = 0; s < mesh.Mesh.SubMeshes.Count; s++)
        {
            var sub = mesh.Mesh.SubMeshes[s];
            var info = mesh.SubMeshInfo[s];
            var subWrap = sub.Addressing == TextureAddressMode.Wrap;
            if (!groups.TryGetValue((sub.SurfaceId, sub.Side, subWrap), out var byIndex))
            {
                Assert.Fail($"0x{id:X8}: unexpected sub-mesh 0x{sub.SurfaceId:X8} {sub.Side} {sub.Addressing}");
                return default;
            }

            var label = $"0x{id:X8} 0x{sub.SurfaceId:X8} {sub.Side} {sub.Addressing}";
            Assert.True(vertexKeys[(sub.SurfaceId, sub.Side, subWrap)].Count == sub.Positions.Length, $"{label}: vertex count");
            Assert.True(byIndex.Keys.SequenceEqual(info.Ranges.Select(r => r.SurfaceIndex)), $"{label}: surface indices");
            Assert.True(byIndex.Values.Sum(g => g.Polygons.Count) == info.PolygonCount, $"{label}: polygon count");

            var corner = 0;
            var r = 0;
            foreach (var (surfaceIndex, group) in byIndex)
            {
                var range = info.Ranges[r++];
                if (range.FirstIndex != corner || range.IndexCount != group.Corners.Count || subWrap != wrap[surfaceIndex])
                {
                    Assert.Fail($"{label}: range {range} differs from surface index {surfaceIndex} at {corner}, {group.Corners.Count} corners, wrap {wrap[surfaceIndex]}");
                }

                if (!subWrap && splitPairs.Contains((sub.SurfaceId, sub.Side)))
                {
                    splitClampPolygons.UnionWith(group.Polygons);
                }

                foreach (var expected in group.Corners)
                {
                    var index = sub.Indices[corner];
                    var actual = new ExpectedCorner(sub.Positions[index], sub.Normals[index], sub.Uvs[index]);
                    if (actual != expected)
                    {
                        Assert.Fail($"{label}: corner {corner} is {actual}, independent restatement gives {expected}");
                    }

                    if (!subWrap && (expected.Uv.X is < 0f or > 1f || expected.Uv.Y is < 0f or > 1f))
                    {
                        clampUvsOutside++;
                    }

                    corner++;
                }
            }

            Assert.True(corner == sub.Indices.Length, $"{label}: {sub.Indices.Length} indices, {corner} expected");
            triangles += corner / 3;
        }

        return (triangles, splitClampPolygons.Count, clampUvsOutside);
    }

    private readonly record struct ExpectedCorner(Vector3 Position, Vector3 Normal, Vector2 Uv);

    private sealed class ExpectedGroup
    {
        public List<ExpectedCorner> Corners { get; } = [];

        public HashSet<ushort> Polygons { get; } = [];
    }

    private static void CountWindingAgreement(GfxObjMesh mesh, ref int frontAgree, ref int frontTotal, ref int backAgree, ref int backTotal)
    {
        foreach (var sub in mesh.Mesh.SubMeshes)
        {
            for (var t = 0; t < sub.Indices.Length; t += 3)
            {
                int a = sub.Indices[t], b = sub.Indices[t + 1], c = sub.Indices[t + 2];
                var face = Vector3.Cross(sub.Positions[b] - sub.Positions[a], sub.Positions[c] - sub.Positions[a]);
                var stored = sub.Normals[a] + sub.Normals[b] + sub.Normals[c];
                if (face.LengthSquared() < 1e-12f || stored.LengthSquared() < 1e-6f)
                {
                    continue;
                }

                var agree = Vector3.Dot(face, stored) > 0f;
                if (sub.Side == MeshSide.Positive)
                {
                    frontTotal++;
                    frontAgree += agree ? 1 : 0;
                }
                else
                {
                    backTotal++;
                    backAgree += agree ? 1 : 0;
                }
            }
        }
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static bool IsFinite(Quaternion q) =>
        float.IsFinite(q.X) && float.IsFinite(q.Y) && float.IsFinite(q.Z) && float.IsFinite(q.W);
}
