using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Scheduling;
using Lifestone.Client.Streaming;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using static Lifestone.Client.Tests.Movement.MovementTestKit;

namespace Lifestone.Client.Tests.Movement;

/// <summary>
/// <see cref="CollisionStreamer.SetCorridor"/> (P5a S6): the route's landblocks are requested and kept beside the focus's 3x3, the ones the route leaves stay for
/// <c>keepBehind</c> more and are then released as the route moves on, the same list changes nothing, and with no corridor the streamer is the one the other tests prove.
/// </summary>
public sealed class CollisionStreamerCorridorTests
{
    private static readonly LandblockCoord Centre = new(0x50, 0x50);

    private static uint Block(int dx, int dy = 0) => ((uint)(Centre.X + dx) << 24) | ((uint)(Centre.Y + dy) << 16);

    private static uint Cell(LandblockCoord coord) => ((uint)coord.X << 24) | ((uint)coord.Y << 16) | 0x01;

    private static CollisionStreamer Make(PhysicsWorld world, WorkerPool workers, List<LandblockCoord>? builds = null) =>
        new(world, (c, _) => { builds?.Add(c); return Flat(c); }, workers, NullLogSink.Instance);

    [Fact]
    public void A_corridor_is_attached_beside_the_focus_3x3()
    {
        var world = new PhysicsWorld();
        using var workers = new WorkerPool(2);
        using var streamer = Make(world, workers);
        streamer.SetFocus(Cell(Centre));
        streamer.SetCorridor([Block(3), Block(4)], 1);
        streamer.PumpSynchronously();

        Assert.Equal(11, world.AttachedCount); // nine round the focus and the two ahead
        Assert.Equal(11, streamer.RequestedCount);
        Assert.True(world.IsAttached(new LandblockCoord((byte)(Centre.X + 3), Centre.Y)));
        Assert.True(world.IsAttached(new LandblockCoord((byte)(Centre.X + 4), Centre.Y)));
        Assert.False(world.IsAttached(new LandblockCoord((byte)(Centre.X + 2), Centre.Y))); // not named, not in the ring
        Assert.Equal(2, streamer.Corridor.Count);
    }

    [Fact]
    public void The_landblocks_a_route_leaves_stay_for_keepBehind_and_are_then_released()
    {
        var world = new PhysicsWorld();
        using var workers = new WorkerPool(2);
        using var streamer = Make(world, workers);
        streamer.SetFocus(Cell(new LandblockCoord(0x20, 0x20))); // a focus far from the corridor, so only the corridor is under test
        LandblockCoord At(int x) => new((byte)(0x60 + x), 0x60);
        uint Id(int x) => ((uint)(0x60 + x) << 24) | (0x60u << 16);

        streamer.SetCorridor([Id(0), Id(1), Id(2)], 1);
        streamer.PumpSynchronously();
        Assert.True(world.IsAttached(At(0)) && world.IsAttached(At(1)) && world.IsAttached(At(2)));

        streamer.SetCorridor([Id(1), Id(2), Id(3)], 1); // block 0 dropped: kept as the one behind
        streamer.PumpSynchronously();
        Assert.True(world.IsAttached(At(0)) && world.IsAttached(At(3)));

        streamer.SetCorridor([Id(2), Id(3), Id(4)], 1); // block 1 dropped: block 0 is now two behind and goes
        streamer.PumpSynchronously();
        Assert.False(world.IsAttached(At(0)));
        Assert.True(world.IsAttached(At(1)) && world.IsAttached(At(2)) && world.IsAttached(At(3)) && world.IsAttached(At(4)));

        streamer.SetCorridor([Id(3), Id(4), Id(5)], 0); // nothing is kept behind: block 1 and 2 go at once
        streamer.PumpSynchronously();
        Assert.False(world.IsAttached(At(1)) || world.IsAttached(At(2)));
        Assert.True(world.IsAttached(At(3)) && world.IsAttached(At(5)));

        streamer.SetCorridor([], 0);
        streamer.PumpSynchronously();
        Assert.False(world.IsAttached(At(3)) || world.IsAttached(At(5)));
        Assert.Equal(9, world.AttachedCount); // only the focus's 3x3 is left
    }

    [Fact]
    public void The_same_corridor_again_changes_nothing_and_a_corridor_may_be_set_before_the_focus()
    {
        var world = new PhysicsWorld();
        using var workers = new WorkerPool(2);
        var builds = new List<LandblockCoord>();
        using var streamer = Make(world, workers, builds);
        streamer.SetCorridor([Block(5)], 0); // no focus yet: nothing requested
        Assert.Equal(0, streamer.RequestedCount);
        streamer.SetFocus(Cell(Centre));
        Assert.Equal(10, streamer.RequestedCount);
        streamer.PumpSynchronously();
        var count = builds.Count;
        streamer.SetCorridor([Block(5)], 0);
        streamer.PumpSynchronously();
        Assert.Equal(count, builds.Count);
        Assert.Equal(10, world.AttachedCount);
    }

    [Fact]
    public void Ids_off_the_map_and_repeats_are_left_out_and_a_negative_keep_is_refused()
    {
        var world = new PhysicsWorld();
        using var workers = new WorkerPool(1);
        using var streamer = Make(world, workers);
        streamer.SetCorridor([Block(3), Block(3), 0xFFFF0000u, Block(3) | 0x32], 2);
        Assert.Equal([new LandblockCoord((byte)(Centre.X + 3), Centre.Y)], streamer.Corridor);
        Assert.Throws<ArgumentOutOfRangeException>(() => streamer.SetCorridor([], -1));
        Assert.Throws<ArgumentNullException>(() => streamer.SetCorridor(null!, 0));
    }

    [Fact]
    public void Without_a_corridor_the_streamer_holds_the_focus_3x3_only_and_reports_its_attaches_and_releases()
    {
        var world = new PhysicsWorld();
        using var workers = new WorkerPool(2);
        using var streamer = Make(world, workers);
        var attached = new List<LandblockCoord>();
        var released = new List<LandblockCoord>();
        streamer.BlockAttached += (c, _) => attached.Add(c);
        streamer.BlockReleased += released.Add;

        streamer.SetFocus(Cell(Centre));
        streamer.PumpSynchronously();
        Assert.Empty(streamer.Corridor);
        Assert.Equal(9, world.AttachedCount);
        Assert.Equal(9, attached.Count);

        streamer.SetFocus(Cell(new LandblockCoord((byte)(Centre.X + 1), Centre.Y)));
        streamer.PumpSynchronously();
        Assert.Equal(12, attached.Count); // the east column came
        Assert.Equal(3, released.Count); // the west column went
        Assert.All(released, c => Assert.Equal(Centre.X - 1, c.X));
    }

    [Fact]
    public void The_frame_budget_path_attaches_a_corridor_too()
    {
        var world = new PhysicsWorld();
        using var workers = new WorkerPool(2);
        using var streamer = Make(world, workers);
        var budget = new FrameBudgetScheduler(TimeSpan.FromMilliseconds(100));
        streamer.SetFocus(Cell(Centre));
        streamer.SetCorridor([Block(3)], 1);
        var deadline = DateTime.UtcNow.AddSeconds(20);
        var target = new LandblockCoord((byte)(Centre.X + 3), Centre.Y);
        while (world.AttachedCount < 10 && DateTime.UtcNow < deadline)
        {
            streamer.Pump(budget);
            budget.Drain();
            Thread.Sleep(1);
        }

        Assert.Equal(AttachState.Attached, streamer.StateOf(target));
        Assert.Equal(10, world.AttachedCount);
    }
}
