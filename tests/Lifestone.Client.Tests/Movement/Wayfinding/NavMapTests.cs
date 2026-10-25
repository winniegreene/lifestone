using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using static Lifestone.Client.Tests.Movement.Wayfinding.S2TileKit;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// <see cref="NavMap"/> (P5a lane S2) on hand-built tiles: attaching and detaching by landblock, the global node lattice
/// across landblock edges, the conversions, the step rules, the lattice line of sight and the coarse regions.
/// </summary>
public sealed class NavMapTests
{
    [Fact]
    public void Tiles_attach_and_detach_by_landblock()
    {
        var map = new NavMap();
        Assert.Throws<ArgumentNullException>(() => map.Attach(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.Attach(new NavTile(0xFF100000u)));
        var a = Flat(Block(0x10, 0x20)).Clear();
        var b = Flat(Block(0x11, 0x20)).Clear();
        map.Attach(a);
        map.Attach(b);
        Assert.Equal(2, map.Version);
        Assert.Equal([Block(0x10, 0x20), Block(0x11, 0x20)], map.AttachedLandblocks.Order());
        Assert.True(map.TryGetTile(0x1020FFFFu, out var got));
        Assert.Same(a, got);
        Assert.Equal(128, map.RegionCount);

        var replacement = Flat(Block(0x10, 0x20)).Clear();
        map.Attach(replacement);
        Assert.True(map.TryGetTile(Block(0x10, 0x20), out got));
        Assert.Same(replacement, got);
        Assert.Equal(2, map.AttachedLandblocks.Count);

        Assert.True(map.Detach(Block(0x11, 0x20)));
        Assert.False(map.Detach(Block(0x11, 0x20)));
        Assert.False(map.TryGetTile(Block(0x11, 0x20), out _));
        Assert.Equal(64, map.RegionCount);
        Assert.False(map.TryGetNode(P(10, 10, Block(0x11, 0x20)), out _));
    }

    [Fact]
    public void Points_are_global_by_the_block_offset_and_normalise_across_edges()
    {
        var p = NavPoint.At(Block(0x7F, 0x7F), 193.5f, -0.5f, 4f);
        Assert.Equal(new Vector2((0x7F * 192) + 193.5f, (0x7F * 192) - 0.5f), p.Global);
        var n = p.Normalized();
        Assert.Equal(Block(0x80, 0x7E), n.LandblockId);
        Assert.Equal(new Vector3(1.5f, 191.5f, 4f), n.Local);
        Assert.Equal(p.Global, n.Global);
        Assert.Equal(((0x80 * 192) + 1, (0x7E * 192) + 191), NavMap.GlobalNode(p));
        Assert.Equal(1f, NavPoint.At(Block(0x7F, 0x10), 191.5f, 5f).HorizontalDistance(NavPoint.At(Block(0x80, 0x10), 0.5f, 5f)), 4);
    }

    [Fact]
    public void Cell_frames_convert_both_ways()
    {
        var frame = new CellFrame(0xA9B40031u, new AcFrame(new Vector3(150f, 12f, 42f), Quaternion.Identity));
        var p = NavMap.FromCellFrame(frame);
        Assert.Equal(new NavPoint(0xA9B40000u, new Vector3(150f, 12f, 42f)), p);
        var back = NavMap.ToCellFrame(p);
        Assert.Equal(0xA9B40031u, back.CellId);
        Assert.Equal(frame.Origin, back.Origin);

        // An origin past the landblock's edge lands in the next landblock's cell.
        var over = NavMap.FromCellFrame(new CellFrame(0xA9B40031u, new AcFrame(new Vector3(200f, 12f, 0f), Quaternion.Identity)));
        Assert.Equal(0xAAB40000u, over.LandblockId);
        Assert.Equal(LandDefs.LandCoordToCellId((0xAA * 8) + 0, (0xB4 * 8) + 0), NavMap.ToCellFrame(over).CellId);
    }

    /// <summary>
    /// M4 4.2 F1 (DoD 8): a dungeon room's origin lies outside 0-192 m of its landblock (the Black Spawn Den's 0x01050377, where the mage's corpse lay at y -139),
    /// and the room graph and the sight probe read the goal's local origin in the goal cell's frame. Normalised like ground, the corpse's goal wrapped to
    /// 0x01040000 [110.02 52.93] and the den plan ran 201 m into the rock in all three journey gates. An indoor point keeps its own landblock and origin; the
    /// global position is the same either way; an outdoor point still wraps (above).
    /// </summary>
    [Fact]
    public void An_indoor_cell_frame_keeps_its_own_landblock_and_origin_however_far_outside_0_192_it_lies()
    {
        var corpse = new CellFrame(0x01050377u, new AcFrame(new Vector3(110.02f, -139.07f, 0.01f), Quaternion.Identity));
        var p = NavMap.FromCellFrame(corpse);
        Assert.Equal(0x01050000u, p.LandblockId);
        Assert.Equal(corpse.Origin, p.Local);
        Assert.Equal(new NavPoint(0x01050000u, corpse.Origin).Normalized().Global, p.Global); // the same place in global metres
        Assert.Equal(0x01040000u, new NavPoint(0x01050000u, corpse.Origin).Normalized().LandblockId); // what the old rule made of it

        // Past the east edge too, and a building's room inside 0-192 is unchanged as before.
        var east = NavMap.FromCellFrame(new CellFrame(0x01050390u, new AcFrame(new Vector3(230f, 40f, -6f), Quaternion.Identity)));
        Assert.Equal(new NavPoint(0x01050000u, new Vector3(230f, 40f, -6f)), east);
        Assert.Equal(new NavPoint(0xA9B40000u, new Vector3(140f, 20f, 94f)), NavMap.FromCellFrame(new CellFrame(0xA9B4013Fu, new AcFrame(new Vector3(140f, 20f, 94f), Quaternion.Identity))));
    }

    [Fact]
    public void Neighbours_cross_tile_edges_and_the_byte_carry()
    {
        var map = Map(Flat(Block(0x7F, 0x7F)), Flat(Block(0x80, 0x7F)), Flat(Block(0x7F, 0x80)), Flat(Block(0x80, 0x80)));
        Assert.True(map.TryNode(P(191.5f, 191.5f, Block(0x7F, 0x7F)).Global, out int corner));
        int east = map.Neighbour(corner, 0);
        int north = map.Neighbour(corner, 1);
        int ne = map.Neighbour(corner, 4);
        Assert.Equal(((0x80 * 192) + 0, (0x7F * 192) + 191), (map.NodeGx(east), map.NodeGy(east)));
        Assert.Equal(((0x7F * 192) + 191, (0x80 * 192) + 0), (map.NodeGx(north), map.NodeGy(north)));
        Assert.Equal(((0x80 * 192) + 0, (0x80 * 192) + 0), (map.NodeGx(ne), map.NodeGy(ne)));
        Assert.True(map.CanStep(corner, ne, 4));

        // The west and south of the four are not attached.
        Assert.True(map.TryNode(P(0.5f, 0.5f, Block(0x7F, 0x7F)).Global, out int sw));
        Assert.Equal(-1, map.Neighbour(sw, 2));
        Assert.Equal(-1, map.Neighbour(sw, 3));
        Assert.Equal(-1, map.Neighbour(sw, 6));
    }

    [Fact]
    public void A_diagonal_never_cuts_a_blocked_corner_and_steps_keep_the_height_rules()
    {
        var map = Map(Flat().Box(11, 10, 11, 10).Height(20, 20, 20, 20, 0.7f).Height(22, 20, 22, 20, 0.5f).Height(24, 20, 24, 20, -2.5f));
        Assert.True(map.TryNode(P(10.5f, 10.5f).Global, out int a));
        int ne = map.Neighbour(a, 4);
        int nw = map.Neighbour(a, 5);
        Assert.False(map.CanStep(a, ne, 4)); // (11, 10) is blocked: no cut past its corner.
        Assert.True(map.CanStep(a, nw, 5));

        Assert.True(map.TryNode(P(19.5f, 20.5f).Global, out int low));
        Assert.False(map.CanStep(low, map.Neighbour(low, 0), 0)); // a 0.7 m rise
        int top = map.Neighbour(low, 0);
        Assert.True(map.CanStep(top, low, 2)); // a 0.7 m drop
        Assert.True(map.TryNode(P(21.5f, 20.5f).Global, out int b));
        Assert.True(map.CanStep(b, map.Neighbour(b, 0), 0)); // a 0.5 m rise
        Assert.True(map.TryNode(P(23.5f, 20.5f).Global, out int c));
        Assert.False(map.CanStep(c, map.Neighbour(c, 0), 0)); // a 2.5 m drop
    }

    [Fact]
    public void The_lattice_line_of_sight_samples_clearance_and_steps()
    {
        var map = Map(Flat().Box(50, 40, 60, 60).Height(100, 0, 191, 191, 1f), Flat(Block(0xAA, 0xB4)));
        Assert.True(map.LineOfSight(P(10, 50), P(45, 50)));
        Assert.False(map.LineOfSight(P(10, 50), P(70, 50))); // through the box
        Assert.True(map.LineOfSight(P(10, 30), P(70, 30))); // beside it, clearance 10 > 9 from the box
        Assert.False(map.LineOfSight(P(10, 37), P(70, 37), minClearance: 4)); // three nodes off it
        Assert.True(map.LineOfSight(P(10, 37), P(70, 37), minClearance: 3));
        Assert.False(map.LineOfSight(P(90, 10), P(110, 10))); // up the 1 m ledge at x 100
        Assert.True(map.LineOfSight(P(110, 10), P(90, 10))); // dropping off it is a step
        Assert.True(map.LineOfSight(P(150, 10), NavPoint.At(0xAAB40000u, 30f, 20f))); // across the landblock edge
        Assert.False(map.LineOfSight(P(150, 10), NavPoint.At(0xA9B50000u, 30f, 20f))); // into a tile not attached
    }

    [Fact]
    public void Land_cells_split_into_regions_by_a_flood_fill_rebuilt_per_tile()
    {
        // A wall down the middle of land cell (2, 2) (nodes 48-71) splits it in two; a 1 m ledge splits cell (5, 5).
        var tile = Flat().Box(59, 48, 60, 71).Height(120, 120, 143, 131, 1f);
        var map = Map(tile);
        Assert.Equal(2, map.RegionsInCell(Lb, 2, 2));
        Assert.Equal(2, map.RegionsInCell(Lb, 5, 5));
        Assert.Equal(1, map.RegionsInCell(Lb, 0, 0));
        Assert.Equal(66, map.RegionCount);

        // The ledge's regions link one way: down off the ledge, not up it.
        Assert.True(map.TryNode(P(130.5f, 125.5f).Global, out int high));
        Assert.True(map.TryNode(P(130.5f, 140.5f).Global, out int low));
        int rHigh = map.RegionOfNode(high);
        int rLow = map.RegionOfNode(low);
        Assert.Contains(map.RegionEdges(rHigh), e => map.EdgeTarget(e) == rLow);
        Assert.DoesNotContain(map.RegionEdges(rLow), e => map.EdgeTarget(e) == rHigh);

        // Re-attaching the landblock with the wall gone rebuilds its graph.
        map.Attach(Flat().Clear());
        Assert.Equal(1, map.RegionsInCell(Lb, 2, 2));
        Assert.Equal(64, map.RegionCount);
    }

    [Fact]
    public void Regions_link_across_tile_edges_and_unlink_on_detach()
    {
        var map = Map(Flat(Block(0x10, 0x10)), Flat(Block(0x11, 0x10)));
        Assert.True(map.TryNode(P(180.5f, 100.5f, Block(0x10, 0x10)).Global, out int west));
        Assert.True(map.TryNode(P(10.5f, 100.5f, Block(0x11, 0x10)).Global, out int east));
        int rw = map.RegionOfNode(west);
        int re = map.RegionOfNode(east);
        Assert.Contains(map.RegionEdges(rw), e => map.EdgeTarget(e) == re);
        Assert.Contains(map.RegionEdges(re), e => map.EdgeTarget(e) == rw);
        Assert.Equal(8, map.RegionEdges(rw).Count); // W, N, S, NW, SW inside the tile; E, NE, SE across the edge

        map.Detach(Block(0x11, 0x10));
        Assert.True(map.TryNode(P(180.5f, 100.5f, Block(0x10, 0x10)).Global, out west));
        Assert.Equal(5, map.RegionEdges(map.RegionOfNode(west)).Count);
    }
}
