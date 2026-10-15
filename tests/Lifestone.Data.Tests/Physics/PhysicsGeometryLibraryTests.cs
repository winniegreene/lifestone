using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.Bsp;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.World.Cells;

namespace Lifestone.Data.Tests.Physics;

/// <summary>
/// <see cref="PhysicsGeometryLibrary"/> on the real dats: what has a collision shape, caching, and the Academy spawn
/// cell's room shape (the start of the DoD 8 Academy walk).
/// </summary>
public sealed class PhysicsGeometryLibraryTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    /// <summary>The Training Academy cell new characters start in (CharGen StarterAreas[0].Locations[0]).</summary>
    private const uint AcademySpawnCell = 0x860201AD;

    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void A_GfxObj_has_a_shape_only_with_the_physics_flag_and_shapes_are_shared()
    {
        var library = new PhysicsGeometryLibrary(Dats);
        var ids = Dats.Read(d => d.Portal.GetAllIdsOfType<GfxObj>().ToArray());
        var withPhysics = ids.First(id => Dats.TryGetPortal<GfxObj>(id, out var g) && g.Flags.HasFlag(DatReaderWriter.Enums.GfxObjFlags.HasPhysics));
        var drawOnly = ids.First(id => Dats.TryGetPortal<GfxObj>(id, out var g) && !g.Flags.HasFlag(DatReaderWriter.Enums.GfxObjFlags.HasPhysics));

        var shape = library.GetGfxObj(withPhysics);
        Assert.NotNull(shape);
        Assert.Same(shape, library.GetGfxObj(withPhysics));
        Assert.NotEmpty(shape.Polygons);
        Assert.False(shape.Bsp.Root.IsLeaf);

        Assert.Null(library.GetGfxObj(drawOnly));
        Assert.Null(library.GetGfxObj(0x0100FFFF));   // not in the dat
    }

    [RequiresDatsFact]
    public void The_academy_spawn_cell_has_a_walkable_floor_in_its_physics_BSP()
    {
        var cell = EnvCellData.Read(Dats, AcademySpawnCell);
        Assert.NotNull(cell);
        var library = new PhysicsGeometryLibrary(Dats);
        var shape = library.GetCellStruct(cell.EnvironmentId, cell.CellStructureIndex);
        Assert.Same(shape, library.GetEnvironment(cell.EnvironmentId)![cell.CellStructureIndex]);

        // The drawing side of the cells track found the same struct's floors; physics has a floor polygon too.
        var structure = new CellStructureLibrary(Dats).Get(cell);
        Assert.Equal(structure.PhysicsPolygonCount, shape.Polygons.Count);
        Assert.Contains(shape.Polygons, p => p.Plane.Normal.Z >= 0.66417414f);

        // Every leaf polygon of the tree is one of the struct's polygons.
        var leaves = new List<PhysicsBspLeaf>();
        Collect(shape.Bsp.Root, leaves);
        Assert.All(leaves.SelectMany(l => l.Polygons), p => Assert.Same(shape.Polygons[p.PolyId], p));

        Assert.Throws<InvalidDataException>(() => library.GetCellStruct(cell.EnvironmentId, 0xFFFF));
        Assert.Throws<InvalidDataException>(() => library.GetCellStruct(0x0D00FFFF, 0));
    }

    [Fact]
    public void A_physics_node_with_a_missing_child_is_refused_loudly()
    {
        var leaf = new DatReaderWriter.Types.PhysicsBSPNode { Type = BSPNodeType.Leaf };
        var single = new DatReaderWriter.Types.PhysicsBSPNode { Type = BSPNodeType.BPnn, PosNode = leaf };
        var tree = new DatReaderWriter.Types.PhysicsBSPTree { Root = single };
        var gfxObj = new GfxObj { Flags = GfxObjFlags.HasPhysics, PhysicsBSP = tree };
        var e = Assert.Throws<InvalidDataException>(() => GfxObjPhysics.FromDat(0x01000001, gfxObj));
        Assert.StartsWith("TODO(physics): GfxObj 0x01000001: physics BSP node BPnn", e.Message, StringComparison.Ordinal);

        single.Type = BSPNodeType.BPnN;
        Assert.Throws<InvalidDataException>(() => GfxObjPhysics.FromDat(0x01000001, gfxObj));

        single.NegNode = new DatReaderWriter.Types.PhysicsBSPNode { Type = BSPNodeType.Leaf };
        var shape = GfxObjPhysics.FromDat(0x01000001, gfxObj);
        Assert.NotNull(shape);
        Assert.NotNull(shape.Bsp.Root.PosNode);
        Assert.NotNull(shape.Bsp.Root.NegNode);
    }

    private static void Collect(PhysicsBspNode node, List<PhysicsBspLeaf> leaves)
    {
        if (node is PhysicsBspLeaf leaf)
        {
            leaves.Add(leaf);
            return;
        }

        Collect(node.PosNode!, leaves);
        Collect(node.NegNode!, leaves);
    }
}
