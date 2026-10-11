using System.Numerics;
using Lifestone.Client.Interiors;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Tests.Interiors;

/// <summary>The per-cell draw state, its slots and the doorway rows (docs/interiors.md §3.4).</summary>
public sealed class CellDrawStateTests
{
    [Fact]
    public void A_valid_row_is_always_first_and_invalid_rows_are_none()
    {
        Assert.Equal((0, -1), Rows(CellDrawState.Create(false, -1, 0)));
        Assert.Equal((3, 7), Rows(CellDrawState.Create(false, 3, 7)));
        Assert.Equal((3, -1), Rows(CellDrawState.Create(false, 3, 3)));
        Assert.Equal((-1, -1), Rows(CellDrawState.Create(false, CellDrawState.MaxRow + 1, -5)));
        Assert.False(CellDrawState.Create(true, -1, -1).Clipped);
        Assert.True(CellDrawState.Create(false, -1, 2).Clipped);
        Assert.Equal(CellDrawState.None, CellDrawState.Create(false, -1, -1));
    }

    [Theory]
    [InlineData(false, -1, -1)]
    [InlineData(true, -1, -1)]
    [InlineData(false, 0, -1)]
    [InlineData(false, 12, 127)]
    [InlineData(false, CellDrawState.MaxRow, 0)]
    public void The_texel_round_trips(bool front, int a, int b)
    {
        var state = CellDrawState.Create(front, a, b);
        var (r, g, bb, alpha) = state.ToBytes();
        Assert.Equal(state, CellDrawState.FromBytes(r, g, bb));
        Assert.Equal(255, alpha);
        Assert.Equal(front ? 255 : 0, r);
    }

    [Fact]
    public void None_is_the_all_zero_texel_the_table_starts_with()
    {
        Assert.Equal(CellDrawState.None, CellDrawState.FromBytes(0, 0, 0));
    }

    [Fact]
    public void Slots_start_at_one_are_reused_and_run_out_to_zero()
    {
        var slots = new CellSlotAllocator(4);
        var a = slots.Allocate();
        var b = slots.Allocate();
        var c = slots.Allocate();
        Assert.Equal(new[] { 1, 2, 3 }, new[] { a, b, c });
        Assert.Equal(0, slots.Allocate());
        Assert.Equal(1, slots.Exhausted);
        slots.Release(b);
        Assert.Equal(b, slots.Allocate());
        Assert.Equal(3, slots.InUse);
        Assert.Equal(3, slots.Peak);
        slots.Release(0); // ignored
        Assert.Throws<ArgumentOutOfRangeException>(() => slots.Release(9));
    }

    [Fact]
    public void Doorway_rows_go_to_the_nearest_doorways_when_there_are_too_many()
    {
        var plane = new CellPlane(Vector3.UnitZ, 0f);
        var doorways = new[] { 50f, 5f, 30f, 1f }
            .Select(d => new DoorwayView(new LandblockCoord(0xA9, 0xB4), plane, [], d * d))
            .ToArray();
        Span<int> rowOf = stackalloc int[4];
        var order = new List<int>();

        Assert.Equal(4, DoorwayRows.Assign(doorways, 8, rowOf, order));
        Assert.Equal(new[] { 0, 1, 2, 3 }, rowOf.ToArray());

        Assert.Equal(2, DoorwayRows.Assign(doorways, 2, rowOf, order));
        Assert.Equal(new[] { -1, 1, -1, 0 }, rowOf.ToArray());
        Assert.Equal(new[] { 3, 1 }, order);
    }

    private static (int A, int B) Rows(CellDrawState state) => (state.DoorwayRowA, state.DoorwayRowB);
}
