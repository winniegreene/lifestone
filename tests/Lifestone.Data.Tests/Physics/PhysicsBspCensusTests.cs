using System.Numerics;
using DatReaderWriter.DBObjs;
using Lifestone.Data.Dat;
using Lifestone.Data.Models;
using Lifestone.Data.Physics.Bsp;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.Primitives;
using Xunit.Abstractions;

namespace Lifestone.Data.Tests.Physics;

/// <summary>
/// Whole-dat sweeps over every collision shape of the end-of-retail portal.dat: every GfxObj with physics and every
/// Environment cell struct decodes through <see cref="PhysicsGeometryLibrary"/>, matches the independent
/// <see cref="RawPhysicsBspReader"/> node for node, and feeds the census that decides how null children are handled
/// (docs/physics.md §5).
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class PhysicsBspCensusTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void Every_GfxObj_physics_BSP_decodes_and_matches_the_independent_reader()
    {
        var ids = Dats.Read(d => d.Portal.GetAllIdsOfType<GfxObj>().ToArray());
        var census = new Census("GfxObj");
        foreach (var id in ids)
        {
            var bytes = Dats.Read(d => d.Portal.TryGetFileBytes(id, out var b) ? b : null)!;
            var raw = RawPhysicsBspReader.ReadGfxObj(bytes);
            var decoded = PhysicsGeometryLibrary.DecodeGfxObj(id, bytes);
            Assert.True((raw is null) == (decoded is null), $"0x{id:X8}: physics flag disagrees");
            if (raw is null || decoded is null)
            {
                continue;
            }

            AssertSameShape($"GfxObj 0x{id:X8}", raw, decoded.Polygons, decoded.Bsp);
            census.Add(decoded.Polygons, decoded.Bsp);
        }

        census.Write(output);
        Assert.Equal(15_318, ids.Length);
        Assert.Equal(1_258, census.Trees);
        Assert.Equal(115_159, census.Polygons);
        Assert.Equal("BPnN 124690, LEAF 125948", census.TypesLine);
        Assert.Equal("solid 0 empty 65568, solid 1 with polygons 60380", census.LeavesLine);
        Assert.Equal("3: 83807, 4: 30225, 5: 931, 6: 164, 7: 15, 8: 15, 10: 1, 12: 1", census.CornersLine);
        Assert.Equal(193, census.MaxDepth);
        AssertClosed(census);
    }

    [RequiresDatsFact]
    public void Every_Environment_cell_struct_physics_BSP_decodes_and_matches_the_independent_reader()
    {
        var ids = Dats.Read(d => d.Portal.GetAllIdsOfType<DatReaderWriter.DBObjs.Environment>().ToArray());
        var census = new Census("CellStruct");
        foreach (var id in ids)
        {
            var bytes = Dats.Read(d => d.Portal.TryGetFileBytes(id, out var b) ? b : null)!;
            var raw = RawPhysicsBspReader.ReadEnvironment(bytes);
            var decoded = PhysicsGeometryLibrary.DecodeEnvironment(id, bytes);
            Assert.Equal(raw.Length, decoded.Length);
            for (var i = 0; i < raw.Length; i++)
            {
                Assert.Equal((uint)i, raw[i].Key);
                AssertSameShape($"CellStruct 0x{id:X8}[{i}]", raw[i].Shape, decoded[i].Polygons, decoded[i].Bsp);
                census.Add(decoded[i].Polygons, decoded[i].Bsp);
            }
        }

        census.Write(output);
        Assert.Equal(772, ids.Length);
        Assert.Equal(3_168, census.Trees);
        Assert.Equal(30_726, census.Polygons);
        Assert.Equal("BPnN 22363, LEAF 25531", census.TypesLine);
        Assert.Equal("solid 0 empty 9358, solid 0 with polygons 16173", census.LeavesLine);
        Assert.Equal("3: 10751, 4: 19660, 5: 211, 6: 61, 7: 2, 8: 36, 10: 2, 12: 3", census.CornersLine);
        Assert.Equal(62, census.MaxDepth);
        AssertClosed(census);
    }

    [RequiresDatsFact]
    public void Setups_that_collide_by_BSP_use_their_parts_level_zero_GfxObjs()
    {
        // CPhysicsPart::find_obj_collisions (11.4186 0x0050D8D0, 11.6096 0x0050E3A0) collides with gfxobj[0], the
        // degrade table's level 0 (LodChain.NearestGfxObjId), and only when that GfxObj has a physics BSP.
        var library = new PhysicsGeometryLibrary(Dats);
        var setupIds = Dats.Read(d => d.Portal.GetAllIdsOfType<Setup>().ToArray());
        int bspSetups = 0, parts = 0, partsWithBsp = 0, rootOnlyBsp = 0;
        foreach (var id in setupIds)
        {
            var assembly = ModelAssembly.Load(Dats, id);
            if (assembly is null || (assembly.Flags & ModelAssemblyFlags.HasPhysicsBsp) == 0)
            {
                continue;
            }

            bspSetups++;
            foreach (var part in assembly.Parts.Where(p => p.IsGfxObj))
            {
                parts++;
                var chain = LodChain.Load(Dats, part.GfxObjId);
                var levelZero = chain.IsLoadable ? library.GetGfxObj(chain.NearestGfxObjId) : null;
                partsWithBsp += levelZero is null ? 0 : 1;
                rootOnlyBsp += levelZero is null && library.GetGfxObj(part.GfxObjId) is not null ? 1 : 0;
            }
        }

        output.WriteLine($"{bspSetups} Setups with HasPhysicsBsp, {parts} parts, {partsWithBsp} with a level-0 physics BSP, {rootOnlyBsp} whose BSP is only on the root GfxObj");
        Assert.Equal(5_935, setupIds.Length);
        Assert.Equal((530, 2_522, 973, 0), (bspSetups, parts, partsWithBsp, rootOnlyBsp));
    }

    /// <summary>The census facts that decide the rules of docs/physics.md §5.</summary>
    private static void AssertClosed(Census census)
    {
        Assert.Equal(0, census.RootLeaves);
        Assert.Equal(0, census.MissingChildren);       // no null child anywhere: every internal node is BPnN
        Assert.Equal(0, census.NonFinitePlanes);       // every physics polygon has three or more corners and an area
        Assert.Equal(0, census.SphereViolations);      // every node sphere holds its subtree's polygons (1e-3)
    }

    private static void AssertSameShape(string owner, RawPhysicsBspReader.RawShape raw, IReadOnlyList<PhysPolygon> polygons, PhysicsBspTree tree)
    {
        Assert.True(raw.VertexKeys.Select((k, i) => k == i).All(b => b), $"{owner}: vertex keys are not positions");
        Assert.Equal(raw.Polygons.Length, polygons.Count);
        for (var i = 0; i < polygons.Count; i++)
        {
            var rawPolygon = raw.Polygons[i];
            var polygon = polygons[i];
            Assert.True(rawPolygon.Id == i && polygon.PolyId == i, $"{owner}: polygon {i} stored as {rawPolygon.Id}");
            Assert.Equal(rawPolygon.VertexIds.Length, polygon.NumPoints);
            for (var v = 0; v < polygon.NumPoints; v++)
            {
                Assert.Equal(raw.Vertices[rawPolygon.VertexIds[v]], polygon.Vertices[v]);
            }

            Assert.Equal(rawPolygon.SidesType, polygon.SidesType);
        }

        AssertSameNode(owner, raw.Root, tree.Root, polygons);
    }

    private static void AssertSameNode(string owner, RawPhysicsBspReader.RawNode raw, PhysicsBspNode node, IReadOnlyList<PhysPolygon> polygons)
    {
        Assert.Equal(raw.Tag, (uint)node.Type);
        Assert.Equal(new Vector3(raw.Sphere.X, raw.Sphere.Y, raw.Sphere.Z), node.BoundingSphere.Center);
        Assert.Equal(raw.Sphere.W, node.BoundingSphere.Radius);
        if (node is PhysicsBspLeaf leaf)
        {
            Assert.Equal(raw.LeafIndex, leaf.LeafIndex);
            Assert.Equal(raw.Solid, leaf.Solid);
            Assert.Equal(raw.Polygons.Length, leaf.Polygons.Count);
            for (var i = 0; i < raw.Polygons.Length; i++)
            {
                Assert.Same(polygons[raw.Polygons[i]], leaf.Polygons[i]);
            }

            return;
        }

        Assert.Equal(new Plane(raw.Plane.X, raw.Plane.Y, raw.Plane.Z, raw.Plane.W), node.SplittingPlane);
        Assert.True((raw.Pos is null) == (node.PosNode is null), $"{owner}: positive child presence differs");
        Assert.True((raw.Neg is null) == (node.NegNode is null), $"{owner}: negative child presence differs");
        if (raw.Pos is not null)
        {
            AssertSameNode(owner, raw.Pos, node.PosNode!, polygons);
        }

        if (raw.Neg is not null)
        {
            AssertSameNode(owner, raw.Neg, node.NegNode!, polygons);
        }
    }

    /// <summary>Counts over a family of trees, written to the test log.</summary>
    private sealed class Census(string kind)
    {
        private readonly SortedDictionary<string, int> _types = new(StringComparer.Ordinal);
        private readonly SortedDictionary<string, int> _leaves = new(StringComparer.Ordinal);
        private readonly SortedDictionary<int, int> _corners = [];
        private int _missingChildClear;

        public int Trees { get; private set; }

        public int Polygons { get; private set; }

        public int RootLeaves { get; private set; }

        public int NonFinitePlanes { get; private set; }

        public int MissingChildren { get; private set; }

        public int SphereViolations { get; private set; }

        public int MaxDepth { get; private set; }

        public string TypesLine => string.Join(", ", _types.Select(t => $"{t.Key} {t.Value}"));

        public string LeavesLine => string.Join(", ", _leaves.Select(t => $"{t.Key} {t.Value}"));

        public string CornersLine => string.Join(", ", _corners.Select(t => $"{t.Key}: {t.Value}"));

        public void Add(IReadOnlyList<PhysPolygon> polygons, PhysicsBspTree tree)
        {
            Trees++;
            Polygons += polygons.Count;
            RootLeaves += tree.Root.IsLeaf ? 1 : 0;
            foreach (var polygon in polygons)
            {
                _corners[polygon.NumPoints] = _corners.GetValueOrDefault(polygon.NumPoints) + 1;
                var n = polygon.Plane.Normal;
                if (!float.IsFinite(n.X) || !float.IsFinite(n.Y) || !float.IsFinite(n.Z) || !float.IsFinite(polygon.Plane.D))
                {
                    NonFinitePlanes++;
                }
            }

            Visit(tree.Root, 0);
        }

        public void Write(ITestOutputHelper output)
        {
            output.WriteLine($"{kind}: {Trees} trees, {Polygons} physics polygons, max depth {MaxDepth}, {RootLeaves} root leaves");
            output.WriteLine($"{kind} node types: {TypesLine}");
            output.WriteLine($"{kind} leaves: {LeavesLine}");
            output.WriteLine($"{kind} polygon corners: {CornersLine}; non-finite planes {NonFinitePlanes}");
            output.WriteLine($"{kind} internal nodes with a missing child: {MissingChildren}, of which the node sphere lies wholly on the present side: {_missingChildClear}");
            output.WriteLine($"{kind} node spheres not containing their polygons (1e-3): {SphereViolations}");
        }

        private void Visit(PhysicsBspNode node, int depth)
        {
            MaxDepth = Math.Max(MaxDepth, depth);
            var name = PhysicsBspNode.TypeName(node.Type);
            _types[name] = _types.GetValueOrDefault(name) + 1;
            if (node is PhysicsBspLeaf leaf)
            {
                var key = $"solid {leaf.Solid} {(leaf.Polygons.Count == 0 ? "empty" : "with polygons")}";
                _leaves[key] = _leaves.GetValueOrDefault(key) + 1;
            }
            else if (node.PosNode is null || node.NegNode is null)
            {
                MissingChildren++;
                var distance = Vector3.Dot(node.SplittingPlane.Normal, node.BoundingSphere.Center) + node.SplittingPlane.D;
                var clearance = node.PosNode is null ? -distance - node.BoundingSphere.Radius : distance - node.BoundingSphere.Radius;
                _missingChildClear += clearance >= 0 ? 1 : 0;
            }

            foreach (var vertex in SubtreeVertices(node))
            {
                if (Vector3.Distance(vertex, node.BoundingSphere.Center) > node.BoundingSphere.Radius + 1e-3f)
                {
                    SphereViolations++;
                    break;
                }
            }

            if (node.PosNode is not null)
            {
                Visit(node.PosNode, depth + 1);
            }

            if (node.NegNode is not null)
            {
                Visit(node.NegNode, depth + 1);
            }
        }

        private static IEnumerable<Vector3> SubtreeVertices(PhysicsBspNode node)
        {
            if (node is PhysicsBspLeaf leaf)
            {
                foreach (var polygon in leaf.Polygons)
                {
                    foreach (var vertex in polygon.Vertices.ToArray())
                    {
                        yield return vertex;
                    }
                }

                yield break;
            }

            foreach (var child in new[] { node.PosNode, node.NegNode })
            {
                if (child is null)
                {
                    continue;
                }

                foreach (var vertex in SubtreeVertices(child))
                {
                    yield return vertex;
                }
            }
        }
    }
}
