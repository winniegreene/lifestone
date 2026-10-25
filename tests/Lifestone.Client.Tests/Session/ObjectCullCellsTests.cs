using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.State;
using Lifestone.Client.Streaming;
using Lifestone.Client.Tests.Interiors;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using static Lifestone.Client.Tests.Session.ReducerHarness;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M5 0 fix R1: inside a dungeon cell (an EnvCell not seen from outside) the cull's visible set is the player's cell and its
/// visible-cell list (ACE's <c>envCell.VisibleCells</c>, <c>ObjectMaint.cs:345-348, 365-373</c> @95ef426d), read through
/// <see cref="ObjectCull.Cells"/>; a building cell and a host without lists keep the landblock rule.
/// </summary>
public sealed class ObjectCullCellsTests
{
    private const uint RoomA = 0x01050100;
    private const uint RoomB = 0x01050101;
    private const uint Hall = 0x01050102;
    private const uint Tusker = 0x80000500;

    /// <summary>Three dungeon cells: A and B each see the hall, the hall sees both; A and B do not see each other.</summary>
    private sealed class FakeCells(bool seenOutside = false) : IWorldAttachments
    {
        public Dictionary<uint, uint[]> Lists { get; } = new()
        {
            [RoomA] = [Hall],
            [RoomB] = [Hall],
            [Hall] = [RoomA, RoomB],
        };

        public AttachState Terrain(LandblockCoord landblock) => AttachState.Attached;

        public AttachState Objects(LandblockCoord landblock) => AttachState.Disabled;

        public AttachState Collision(LandblockCoord landblock) => AttachState.Disabled;

        public AttachState Cell(uint envCellId) => AttachState.Attached;

        public IReadOnlyList<uint>? VisibleCells(uint envCellId) => Lists.TryGetValue(envCellId, out var list) ? list : null;

        public bool SeenOutside(uint envCellId) => seenOutside;
    }

    internal static void Tick(ReducerHarness h, double seconds)
    {
        h.Time.Advance(TimeSpan.FromSeconds(seconds));
        h.Reducer.CullObjects();
    }

    internal static void Spawn(ReducerHarness h, uint guid, uint cell) =>
        h.Reducer.OnCreateObject(h.Context, Create(guid, Stamps(), Position(cell), name: "Tusker"), isUpdate: false);

    private static ReducerHarness Den(IWorldAttachments? cells)
    {
        var h = new ReducerHarness();
        h.EnterWorld(RoomA);
        h.Reducer.Cull.Cells = cells;
        return h;
    }

    [Fact]
    public void In_a_dungeon_cell_an_object_in_a_cell_it_does_not_see_is_culled_and_one_it_sees_stays()
    {
        var h = Den(new FakeCells());
        Spawn(h, Tusker, RoomB);
        Spawn(h, Tusker + 1, Hall);

        Tick(h, 0.5);
        Assert.True(h.Reducer.Cull.DungeonView);
        Assert.True(h.Reducer.Cull.IsPending(Tusker));
        Assert.False(h.Reducer.Cull.IsPending(Tusker + 1));
        Tick(h, ObjectCull.CullSeconds);

        Assert.False(h.State.Objects.Contains(Tusker));
        Assert.True(h.State.Objects.Contains(Tusker + 1));
        Assert.DoesNotContain(h.Log.Entries, e => e.Message.Contains("TODO(m5-object-cull-cells)", StringComparison.Ordinal));
    }

    [Fact]
    public void The_player_stepping_into_a_cell_that_sees_the_object_cancels_its_cull()
    {
        var h = Den(new FakeCells());
        Spawn(h, Tusker, RoomB);
        Tick(h, 0.5);
        Assert.True(h.Reducer.Cull.IsPending(Tusker));

        Tick(h, 10.0);
        h.Reducer.OnUpdatePosition(h.Context, Move(PlayerGuid, 1, 2, 1, 1, Hall));
        Tick(h, 1.0);
        Assert.False(h.Reducer.Cull.IsPending(Tusker));
        Tick(h, ObjectCull.CullSeconds + 5);

        Assert.True(h.State.Objects.Contains(Tusker));
    }

    [Fact]
    public void A_building_cell_keeps_the_landblock_rule()
    {
        var h = Den(new FakeCells(seenOutside: true));
        Spawn(h, Tusker, RoomB);
        Tick(h, 0.5);

        Assert.False(h.Reducer.Cull.DungeonView);
        Assert.False(h.Reducer.Cull.IsPending(Tusker));
    }

    [Fact]
    public void A_cell_whose_list_is_not_known_yet_keeps_the_landblock_rule_until_it_is()
    {
        var cells = new FakeCells();
        cells.Lists.Remove(RoomA);
        var h = Den(cells);
        Spawn(h, Tusker, RoomB);
        Tick(h, 0.5);
        Assert.False(h.Reducer.Cull.IsPending(Tusker));

        cells.Lists[RoomA] = [Hall];
        Tick(h, 1.0);
        Assert.True(h.Reducer.Cull.IsPending(Tusker));
    }

    [Fact]
    public void Without_lists_a_dungeon_keeps_the_whole_landblock_and_the_TODO_is_logged_once()
    {
        var h = Den(cells: null);
        Spawn(h, Tusker, RoomB);
        for (var i = 0; i < 40; i++)
        {
            Tick(h, 1.0);
        }

        Assert.True(h.State.Objects.Contains(Tusker));
        Assert.Single(h.Log.Entries, e => e.Message.Contains("TODO(m5-object-cull-cells)", StringComparison.Ordinal));
    }
}

/// <summary>
/// M5 0 fix R1 on the Black Spawn Den's real cells (landblock 0x0105), with the headless host's provider
/// (<see cref="DataOnlyAttachments"/>, focused on the player's cell as the headless runner does each tick). Skips without the dats.
/// </summary>
public sealed class ObjectCullDenTests(InteriorsFixture fixture) : IClassFixture<InteriorsFixture>
{
    private const uint Arrival = 0x01050375;
    private const uint Tusker = 0x80000600;

    [RequiresDatsFact]
    public void A_tusker_outside_the_arrival_cells_list_is_culled_one_inside_stays_and_a_step_into_a_cell_that_sees_it_cancels()
    {
        var world = new DataOnlyAttachments(fixture.Dats, NullLogSink.Instance);
        world.Focus(Arrival);
        Assert.False(world.SeenOutside(Arrival));
        var arrivalList = world.VisibleCells(Arrival);
        Assert.NotNull(arrivalList);
        Assert.NotEmpty(arrivalList);

        // A cell in the arrival cell's list (the step) that sees a cell the arrival cell does not see (the far cell).
        uint step = 0;
        uint far = 0;
        foreach (var c in arrivalList)
        {
            foreach (var x in world.VisibleCells(c) ?? [])
            {
                if (x != Arrival && !arrivalList.Contains(x))
                {
                    (step, far) = (c, x);
                    break;
                }
            }

            if (far != 0)
            {
                break;
            }
        }

        Assert.NotEqual(0u, far);
        var inside = arrivalList[0];

        var h = new ReducerHarness();
        h.EnterWorld(Arrival);
        h.Reducer.Cull.Cells = world;
        ObjectCullCellsTests.Spawn(h, Tusker, far);
        ObjectCullCellsTests.Spawn(h, Tusker + 1, inside);
        ObjectCullCellsTests.Tick(h, 0.5);
        Assert.True(h.Reducer.Cull.DungeonView);
        Assert.True(h.Reducer.Cull.IsPending(Tusker));
        Assert.False(h.Reducer.Cull.IsPending(Tusker + 1));
        ObjectCullCellsTests.Tick(h, ObjectCull.CullSeconds);
        Assert.False(h.State.Objects.Contains(Tusker));
        Assert.True(h.State.Objects.Contains(Tusker + 1));

        // A second tusker in the far cell: the player crosses into the cell that sees it, and its pending cull is cancelled.
        ObjectCullCellsTests.Spawn(h, Tusker + 2, far);
        ObjectCullCellsTests.Tick(h, 1.0);
        Assert.True(h.Reducer.Cull.IsPending(Tusker + 2));
        h.Reducer.OnUpdatePosition(h.Context, Move(PlayerGuid, 1, 2, 1, 1, step));
        world.Focus(step);
        ObjectCullCellsTests.Tick(h, 1.0);
        Assert.False(h.Reducer.Cull.IsPending(Tusker + 2));
        ObjectCullCellsTests.Tick(h, ObjectCull.CullSeconds + 5);
        Assert.True(h.State.Objects.Contains(Tusker + 2));
    }
}
