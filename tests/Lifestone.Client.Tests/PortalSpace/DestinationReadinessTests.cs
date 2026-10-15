using Lifestone.Client.PortalSpace;
using Lifestone.Client.Streaming;
using Lifestone.Data.Terrain;

namespace Lifestone.Client.Tests.PortalSpace;

/// <summary>
/// A world whose attach states the test sets: every landblock layer and cell answers <see cref="Default"/> unless set.
/// Lookups allocate nothing.
/// </summary>
internal sealed class FakeWorld : IWorldAttachments
{
    private readonly Dictionary<(LandblockCoord, ReadinessItem), AttachState> _layers = [];
    private readonly Dictionary<uint, AttachState> _cells = [];
    private readonly Dictionary<uint, IReadOnlyList<uint>> _visible = [];
    private readonly HashSet<uint> _seenOutside = [];

    public AttachState Default { get; set; } = AttachState.Attached;

    public AttachState CollisionDefault { get; set; } = AttachState.Attached;

    public FakeWorld Set(LandblockCoord landblock, ReadinessItem layer, AttachState state)
    {
        _layers[(landblock, layer)] = state;
        return this;
    }

    public FakeWorld SetCell(uint cell, AttachState state)
    {
        _cells[cell] = state;
        return this;
    }

    public FakeWorld SetVisible(uint cell, params uint[] visible)
    {
        _visible[cell] = visible;
        return this;
    }

    public FakeWorld SetSeenOutside(uint cell)
    {
        _seenOutside.Add(cell);
        return this;
    }

    public AttachState Terrain(LandblockCoord landblock) => Layer(landblock, ReadinessItem.Terrain, Default);

    public AttachState Objects(LandblockCoord landblock) => Layer(landblock, ReadinessItem.Objects, Default);

    public AttachState Collision(LandblockCoord landblock) => Layer(landblock, ReadinessItem.Collision, CollisionDefault);

    public AttachState Cell(uint envCellId) => _cells.TryGetValue(envCellId, out var state) ? state : Default;

    public IReadOnlyList<uint>? VisibleCells(uint envCellId) => _visible.TryGetValue(envCellId, out var list) ? list : null;

    public bool SeenOutside(uint envCellId) => _seenOutside.Contains(envCellId);

    private AttachState Layer(LandblockCoord landblock, ReadinessItem layer, AttachState fallback) =>
        _layers.TryGetValue((landblock, layer), out var state) ? state : fallback;
}

/// <summary>Step PS8: Lifestone's "not blocking for cells" (m2-log-in.md §4.7, D29).</summary>
public sealed class DestinationReadinessTests
{
    private const uint HoltburgCell = 0xA9B40001;
    private const uint DungeonCell = 0x01D90105;
    private const uint InteriorCell = 0xA9B40150;
    private static readonly LandblockCoord Holtburg = LandblockCoord.Holtburg;

    [Fact]
    public void Open_land_needs_the_full_3_by_3_of_terrain_objects_and_collision()
    {
        var report = DestinationReadiness.Evaluate(HoltburgCell, new FakeWorld(), ReadinessPolicy.Default);

        Assert.True(report.IsReady);
        Assert.Equal(27, report.Checked);
        Assert.Equal(27, report.Attached);
        Assert.Equal((0, 0, 0), (report.Waiting, report.Failed, report.Disabled));
        Assert.Equal(ReadinessItem.None, report.FirstWaitingKind);
    }

    public static TheoryData<int, int, ReadinessItem> Neighbours()
    {
        var data = new TheoryData<int, int, ReadinessItem>();
        foreach (var layer in new[] { ReadinessItem.Terrain, ReadinessItem.Objects, ReadinessItem.Collision })
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    data.Add(dx, dy, layer);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Neighbours))]
    public void One_pending_piece_anywhere_in_the_ring_blocks_and_is_named(int dx, int dy, ReadinessItem layer)
    {
        var landblock = Holtburg.Offset(dx, dy)!.Value;
        var world = new FakeWorld().Set(landblock, layer, AttachState.Pending);

        var report = DestinationReadiness.Evaluate(HoltburgCell, world, ReadinessPolicy.Default);

        Assert.False(report.IsReady);
        Assert.Equal(1, report.Waiting);
        Assert.Equal(layer, report.FirstWaitingKind);
        Assert.Equal(landblock.LandBlockId, report.FirstWaitingId);
    }

    [Fact]
    public void The_centre_is_named_first_then_the_neighbours_from_the_south_west()
    {
        var world = new FakeWorld()
            .Set(Holtburg.Offset(1, 1)!.Value, ReadinessItem.Terrain, AttachState.Pending)
            .Set(Holtburg.Offset(-1, -1)!.Value, ReadinessItem.Collision, AttachState.Pending)
            .Set(Holtburg, ReadinessItem.Objects, AttachState.Pending);

        var report = DestinationReadiness.Evaluate(HoltburgCell, world, ReadinessPolicy.Default);
        Assert.Equal(3, report.Waiting);
        Assert.Equal((ReadinessItem.Objects, Holtburg.LandBlockId), (report.FirstWaitingKind, report.FirstWaitingId));

        world.Set(Holtburg, ReadinessItem.Objects, AttachState.Attached);
        report = DestinationReadiness.Evaluate(HoltburgCell, world, ReadinessPolicy.Default);
        Assert.Equal((ReadinessItem.Collision, 0xA8B3FFFFu), (report.FirstWaitingKind, report.FirstWaitingId));
    }

    [Theory]
    [InlineData(0x00000001u, 4)] // the south-west corner of the map
    [InlineData(0xFEFE0001u, 4)] // landblock 0xFF is off the map (LandDefs::in_bounds)
    [InlineData(0x00FE0001u, 4)]
    [InlineData(0xFE000001u, 4)]
    [InlineData(0x00800001u, 6)] // the west edge
    [InlineData(0x80FE0001u, 6)] // the north edge
    [InlineData(0x80800001u, 9)]
    public void The_ring_is_clipped_at_the_map_s_edge(uint cell, int landblocks)
    {
        var report = DestinationReadiness.Evaluate(cell, new FakeWorld(), ReadinessPolicy.Default);

        Assert.True(report.IsReady);
        Assert.Equal(3 * landblocks, report.Checked);
    }

    [Fact]
    public void Failed_and_disabled_pieces_settle_and_are_counted()
    {
        var failed = Holtburg.Offset(0, 1)!.Value;
        var world = new FakeWorld { CollisionDefault = AttachState.Disabled }
            .Set(failed, ReadinessItem.Objects, AttachState.Failed)
            .Set(Holtburg.Offset(1, 0)!.Value, ReadinessItem.Terrain, AttachState.Failed);

        var report = DestinationReadiness.Evaluate(HoltburgCell, world, ReadinessPolicy.Default);

        Assert.True(report.IsReady);
        Assert.Equal(0, report.Waiting);
        Assert.Equal(2, report.Failed);
        Assert.Equal(9, report.Disabled);
        Assert.Equal(16, report.Attached);
        Assert.Equal((ReadinessItem.Terrain, Holtburg.Offset(1, 0)!.Value.LandBlockId), (report.FirstFailedKind, report.FirstFailedId));
    }

    [Fact]
    public void A_landblock_the_world_does_not_know_yet_is_pending()
    {
        var world = new FakeWorld { Default = AttachState.Pending, CollisionDefault = AttachState.Pending }
            .Set(Holtburg, ReadinessItem.Terrain, AttachState.Attached)
            .Set(Holtburg, ReadinessItem.Objects, AttachState.Attached)
            .Set(Holtburg, ReadinessItem.Collision, AttachState.Disabled);

        var report = DestinationReadiness.Evaluate(HoltburgCell, world, ReadinessPolicy.Default);

        Assert.False(report.IsReady);
        Assert.Equal(24, report.Waiting);
        Assert.Equal((ReadinessItem.Terrain, Holtburg.Offset(-1, -1)!.Value.LandBlockId), (report.FirstWaitingKind, report.FirstWaitingId));
    }

    [Fact]
    public void A_dungeon_cell_needs_itself_and_its_visible_cells_and_no_landscape()
    {
        var world = new FakeWorld { Default = AttachState.Attached }
            .SetVisible(DungeonCell, 0x01D90106, 0x01D90107)
            .Set(LandblockCoord.FromId(DungeonCell), ReadinessItem.Terrain, AttachState.Pending);

        var report = DestinationReadiness.Evaluate(DungeonCell, world, ReadinessPolicy.Default);
        Assert.True(report.IsReady);
        Assert.Equal(3, report.Checked);

        world.SetCell(0x01D90107, AttachState.Pending);
        report = DestinationReadiness.Evaluate(DungeonCell, world, ReadinessPolicy.Default);
        Assert.False(report.IsReady);
        Assert.Equal((ReadinessItem.Cell, 0x01D90107u), (report.FirstWaitingKind, report.FirstWaitingId));

        world.SetCell(DungeonCell, AttachState.Failed);
        report = DestinationReadiness.Evaluate(DungeonCell, world, ReadinessPolicy.Default);
        Assert.Equal((1, 1), (report.Waiting, report.Failed));
        Assert.Equal((ReadinessItem.Cell, DungeonCell), (report.FirstFailedKind, report.FirstFailedId));
    }

    [Fact]
    public void An_unknown_visible_cell_list_is_pending()
    {
        var report = DestinationReadiness.Evaluate(DungeonCell, new FakeWorld(), ReadinessPolicy.Default);

        Assert.False(report.IsReady);
        Assert.Equal(2, report.Checked);
        Assert.Equal((ReadinessItem.CellList, DungeonCell), (report.FirstWaitingKind, report.FirstWaitingId));
    }

    [Fact]
    public void A_seen_outside_interior_waits_for_the_outdoor_ring_when_the_policy_says_so()
    {
        var world = new FakeWorld()
            .SetVisible(InteriorCell, 0xA9B40151)
            .SetSeenOutside(InteriorCell)
            .Set(Holtburg.Offset(0, -1)!.Value, ReadinessItem.Terrain, AttachState.Pending);

        var on = DestinationReadiness.Evaluate(InteriorCell, world, new ReadinessPolicy(OutdoorRingForSeenOutsideCells: true));
        var off = DestinationReadiness.Evaluate(InteriorCell, world, new ReadinessPolicy(OutdoorRingForSeenOutsideCells: false));

        Assert.False(on.IsReady);
        Assert.Equal(2 + 27, on.Checked);
        Assert.Equal(ReadinessItem.Terrain, on.FirstWaitingKind);
        Assert.True(off.IsReady);
        Assert.Equal(2, off.Checked);
        Assert.True(ReadinessPolicy.Default.OutdoorRingForSeenOutsideCells); // D29
    }

    [Fact]
    public void An_interior_that_does_not_see_outside_never_waits_for_the_ring()
    {
        var world = new FakeWorld()
            .SetVisible(InteriorCell)
            .Set(Holtburg, ReadinessItem.Terrain, AttachState.Pending);

        var report = DestinationReadiness.Evaluate(InteriorCell, world, ReadinessPolicy.Default);

        Assert.True(report.IsReady);
        Assert.Equal(1, report.Checked);
    }

    [Fact]
    public void A_dungeon_in_landblock_0xFF_has_no_ring()
    {
        const uint cell = 0xFF010100;
        var world = new FakeWorld().SetVisible(cell).SetSeenOutside(cell);

        var report = DestinationReadiness.Evaluate(cell, world, ReadinessPolicy.Default);

        Assert.True(report.IsReady);
        Assert.Equal(1, report.Checked);
    }

    [Theory]
    [InlineData(0x00000000u)] // no position yet
    [InlineData(0xA9B40000u)]
    [InlineData(0xA9B40041u)] // between the land cells and the EnvCells
    [InlineData(0xA9B400FFu)]
    [InlineData(0xA9B4FFFEu)] // LandBlockInfo
    [InlineData(0xA9B4FFFFu)] // LandBlock
    [InlineData(0xFF000001u)] // an outdoor cell off the map
    [InlineData(0x00FF0001u)]
    public void A_cell_id_that_is_not_a_player_cell_is_never_ready(uint cell)
    {
        var report = DestinationReadiness.Evaluate(cell, new FakeWorld(), ReadinessPolicy.Default);

        Assert.False(report.IsReady);
        Assert.Equal(1, report.Waiting);
        Assert.Equal((ReadinessItem.InvalidCell, cell), (report.FirstWaitingKind, report.FirstWaitingId));
        Assert.Contains($"0x{cell:X8} is not a valid player cell", report.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_names_the_counts_and_the_first_pieces()
    {
        var world = new FakeWorld { CollisionDefault = AttachState.Disabled }
            .Set(Holtburg.Offset(1, 1)!.Value, ReadinessItem.Terrain, AttachState.Pending)
            .Set(Holtburg, ReadinessItem.Objects, AttachState.Failed);

        var waiting = DestinationReadiness.Evaluate(HoltburgCell, world, ReadinessPolicy.Default).Describe();
        world.Set(Holtburg.Offset(1, 1)!.Value, ReadinessItem.Terrain, AttachState.Attached);
        var ready = DestinationReadiness.Evaluate(HoltburgCell, world, ReadinessPolicy.Default).Describe();

        Assert.Equal("waiting for 1 of 27 pieces, first Terrain 0xAAB5FFFF; attached 16, failed 1, disabled 9, first failed Objects 0xA9B4FFFF", waiting);
        Assert.Equal("ready: 27 pieces settled; attached 17, failed 1, disabled 9, first failed Objects 0xA9B4FFFF", ready);
        Assert.Equal("ready: 27 pieces settled; attached 27, failed 0, disabled 0", DestinationReadiness.Evaluate(HoltburgCell, new FakeWorld(), ReadinessPolicy.Default).Describe());
    }
}
