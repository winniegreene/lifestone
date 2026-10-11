using System.Numerics;
using Lifestone.Client.Streaming;
using Lifestone.Client.World;
using Lifestone.Data.Terrain;

namespace Lifestone.Client.Tests;

public sealed class StreamingPlannerTests
{
    private static StreamingFocus At(LandblockCoord landblock, float x = 96f, float y = 96f, StreamingFocusKind kind = StreamingFocusKind.Outdoor) =>
        new(AcCoordinates.LandblockOrigin(landblock) + new Vector3(x, y, 50f), kind);

    [Fact]
    public void First_update_loads_the_square_ring_nearest_first()
    {
        var planner = new StreamingPlanner(new StreamingSettings { LoadRadius = 2, MaxInFlight = 100 });
        var plan = planner.Update(At(LandblockCoord.Holtburg, 150f, 40f));

        Assert.Equal(25, plan.ToLoad.Count);
        Assert.Equal(25, plan.ToLoad.Distinct().Count());
        Assert.Equal(LandblockCoord.Holtburg, plan.ToLoad[0]);
        Assert.All(plan.ToLoad, c => Assert.InRange(StreamingPlanner.Ring(c, LandblockCoord.Holtburg), 0, 2));

        // Focus near the east/south edges of 0xA9B4: the east and south neighbours (touching, distance 42 and 40 m)
        // come before the west and north ones; the list never gets farther then nearer.
        var focus = planner.Focus!.Value.Position;
        var distances = plan.ToLoad.Select(c => StreamingPlanner.EdgeDistanceSquared(focus, c)).ToList();
        Assert.Equal(distances.Order(), distances);
        Assert.Equal(new LandblockCoord(0xA9, 0xB3), plan.ToLoad[1]); // 40 m south
        Assert.Equal(new LandblockCoord(0xAA, 0xB4), plan.ToLoad[2]); // 42 m east
        Assert.Equal(25, planner.InFlightCount);
        Assert.Equal(0, planner.PendingCount);
        Assert.Empty(plan.ToCancel);
        Assert.Empty(plan.ToUnload);
    }

    [Fact]
    public void The_in_flight_limit_holds_back_far_landblocks_until_slots_free()
    {
        var planner = new StreamingPlanner(new StreamingSettings { LoadRadius = 2, MaxInFlight = 4 });
        var focus = At(LandblockCoord.Holtburg);
        var first = planner.Update(focus);
        Assert.Equal(4, first.ToLoad.Count);
        Assert.Equal(21, planner.PendingCount);
        Assert.False(planner.IsIdle);

        // Nothing new while every slot is busy.
        Assert.True(planner.Update(focus).IsEmpty);

        Assert.True(planner.MarkLoaded(first.ToLoad[0]));
        Assert.True(planner.MarkFailed(first.ToLoad[1]));
        var second = planner.Update(focus);
        Assert.Equal(2, second.ToLoad.Count);
        Assert.Empty(second.ToLoad.Intersect(first.ToLoad));

        var loaded = new HashSet<LandblockCoord> { first.ToLoad[0] };
        var guard = 0;
        while (!planner.IsIdle && guard++ < 100)
        {
            foreach (var coord in planner.Snapshot().Where(p => p.Value == LandblockLoadState.Loading).Select(p => p.Key))
            {
                Assert.True(planner.MarkLoaded(coord));
                loaded.Add(coord);
            }

            planner.Update(focus);
        }

        Assert.True(planner.IsIdle);
        Assert.Equal(24, planner.LoadedCount); // 25 minus the failed one, which is not retried while in range
        Assert.Equal(LandblockLoadState.Failed, planner.StateOf(first.ToLoad[1]));
        Assert.Equal(24, loaded.Count);
    }

    [Fact]
    public void Hysteresis_keeps_landblocks_one_ring_past_the_load_radius()
    {
        var planner = new StreamingPlanner(new StreamingSettings { LoadRadius = 1, UnloadHysteresis = 1, MaxInFlight = 100 });
        var start = new LandblockCoord(50, 50);
        LoadAll(planner, At(start));
        Assert.Equal(9, planner.LoadedCount);

        // One landblock east: the west column is at ring 2 = load radius + hysteresis, so it stays; the new east
        // column loads.
        var plan = planner.Update(At(new LandblockCoord(51, 50)));
        Assert.Empty(plan.ToUnload);
        Assert.Equal(3, plan.ToLoad.Count);
        Assert.All(plan.ToLoad, c => Assert.Equal(52, c.X));
        MarkAllLoaded(planner, plan);

        // Back again: nothing to do at all (no thrashing).
        Assert.True(planner.Update(At(start)).IsEmpty);

        // Two landblocks east of the start: the start's west column is now at ring 3 and unloads, nearest first.
        plan = planner.Update(At(new LandblockCoord(52, 50)));
        Assert.Equal(new[] { new LandblockCoord(49, 50), new LandblockCoord(49, 49), new LandblockCoord(49, 51) }, plan.ToUnload);
        Assert.Equal(3, plan.ToLoad.Count);
        Assert.All(plan.ToLoad, c => Assert.Equal(53, c.X));
        Assert.Null(planner.StateOf(new LandblockCoord(49, 50)));
    }

    [Fact]
    public void Stale_in_flight_loads_are_cancelled_and_their_late_results_rejected()
    {
        var planner = new StreamingPlanner(new StreamingSettings { LoadRadius = 1, UnloadHysteresis = 0, MaxInFlight = 100 });
        var first = planner.Update(At(new LandblockCoord(20, 20)));
        Assert.Equal(9, first.ToLoad.Count);

        // Teleport far away before anything finished: every in-flight load is cancelled, the new ring starts.
        var second = planner.Update(At(new LandblockCoord(200, 200)));
        Assert.Equal(first.ToLoad.ToHashSet(), second.ToCancel.ToHashSet());
        Assert.Equal(9, second.ToLoad.Count);
        Assert.Equal(9, planner.InFlightCount);

        // A cancelled load that completes anyway must be discarded.
        Assert.False(planner.MarkLoaded(first.ToLoad[0]));
        Assert.False(planner.MarkFailed(first.ToLoad[1]));
        Assert.Equal(9, planner.InFlightCount);
    }

    [Fact]
    public void A_failed_landblock_is_retried_after_it_leaves_and_re_enters_the_range()
    {
        var planner = new StreamingPlanner(new StreamingSettings { LoadRadius = 0, UnloadHysteresis = 0, MaxInFlight = 4 });
        var home = new LandblockCoord(30, 30);
        var plan = planner.Update(At(home));
        Assert.True(planner.MarkFailed(Assert.Single(plan.ToLoad)));
        Assert.True(planner.Update(At(home)).IsEmpty);

        planner.Update(At(new LandblockCoord(40, 30)));
        Assert.Null(planner.StateOf(home));
        plan = planner.Update(At(home));
        Assert.Equal(home, Assert.Single(plan.ToLoad));
    }

    [Fact]
    public void Indoor_focus_keeps_only_the_indoor_radius()
    {
        var planner = new StreamingPlanner(new StreamingSettings { LoadRadius = 2, UnloadHysteresis = 0, IndoorLoadRadius = 0, MaxInFlight = 100 });
        var dungeon = new LandblockCoord(0x05, 0xD9);
        LoadAll(planner, At(dungeon));
        Assert.Equal(25, planner.LoadedCount);

        var plan = planner.Update(At(dungeon, kind: StreamingFocusKind.Indoor));
        Assert.Equal(24, plan.ToUnload.Count);
        Assert.DoesNotContain(dungeon, plan.ToUnload);
        Assert.Equal(1, planner.DesiredCount);
        Assert.Equal(0, planner.LoadRadiusFor(StreamingFocusKind.Indoor));
    }

    [Fact]
    public void Rings_are_clipped_at_the_map_edges()
    {
        var planner = new StreamingPlanner(new StreamingSettings { LoadRadius = 2, MaxInFlight = 100 });
        var plan = planner.Update(At(new LandblockCoord(0, 0), 5f, 5f));
        Assert.Equal(9, plan.ToLoad.Count);
        Assert.All(plan.ToLoad, c => Assert.True(c.X <= 2 && c.Y <= 2));

        planner = new StreamingPlanner(new StreamingSettings { LoadRadius = 2, MaxInFlight = 100 });
        plan = planner.Update(new StreamingFocus(new GlobalPosition(48_900, 48_900, 0)));
        Assert.Equal(9, plan.ToLoad.Count);
        Assert.All(plan.ToLoad, c => Assert.True(c.X is >= 0xFC and <= 0xFE && c.Y is >= 0xFC and <= 0xFE));
    }

    [Fact]
    public void Plans_are_deterministic()
    {
        StreamingPlan Run() => new StreamingPlanner(new StreamingSettings { LoadRadius = 3, MaxInFlight = 10 }).Update(At(LandblockCoord.Holtburg));
        Assert.Equal(Run().ToLoad, Run().ToLoad);
    }

    [Fact]
    public void Reset_returns_everything_to_tear_down()
    {
        var planner = new StreamingPlanner(new StreamingSettings { LoadRadius = 1, MaxInFlight = 100 });
        var plan = planner.Update(At(LandblockCoord.Holtburg));
        planner.MarkLoaded(plan.ToLoad[0]);
        var (loaded, inFlight) = planner.Reset();
        Assert.Single(loaded);
        Assert.Equal(8, inFlight.Count);
        Assert.Equal(0, planner.InFlightCount);
        Assert.Equal(0, planner.LoadedCount);
    }

    [Fact]
    public void Settings_default_to_the_retail_landscape_radius()
    {
        var settings = new StreamingSettings();
        Assert.Equal(8, settings.LoadRadius);
        Assert.Throws<ArgumentOutOfRangeException>(() => new StreamingPlanner(new StreamingSettings { MaxInFlight = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StreamingPlanner(new StreamingSettings { LoadRadius = -1 }));
    }

    private static void LoadAll(StreamingPlanner planner, StreamingFocus focus)
    {
        for (var i = 0; i < 1000 && (i == 0 || !planner.IsIdle); i++)
        {
            MarkAllLoaded(planner, planner.Update(focus));
        }
    }

    private static void MarkAllLoaded(StreamingPlanner planner, StreamingPlan plan)
    {
        foreach (var coord in plan.ToLoad)
        {
            Assert.True(planner.MarkLoaded(coord));
        }
    }
}
