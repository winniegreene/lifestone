using System.Collections.Concurrent;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Scheduling;
using Lifestone.Client.Streaming;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Movement.MovementTestKit;

namespace Lifestone.Client.Tests.Movement;

/// <summary>
/// <see cref="CollisionStreamer"/> (m2-plan PH7): the 3x3 around an outdoor player, an EnvCell's landblock (and its 3x3
/// when it sees outside), attaching within the frame budget or synchronously, releasing what the focus leaves, and a
/// failed build settling as <see cref="AttachState.Failed"/> with a loud log.
/// </summary>
public sealed class CollisionStreamerTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private static readonly LandblockCoord Centre = new(0x50, 0x50);

    [Fact]
    public void The_streamer_checks_its_arguments()
    {
        var world = new PhysicsWorld();
        using var workers = new WorkerPool(1);
        Func<LandblockCoord, CancellationToken, LandblockCollision> build = (c, _) => Flat(c);
        Assert.Throws<ArgumentNullException>(() => new CollisionStreamer(null!, build, workers, NullLogSink.Instance));
        Assert.Throws<ArgumentNullException>(() => new CollisionStreamer(world, null!, workers, NullLogSink.Instance));
        Assert.Throws<ArgumentNullException>(() => new CollisionStreamer(world, build, null!, NullLogSink.Instance));
        Assert.Throws<ArgumentNullException>(() => new CollisionStreamer(world, build, workers, null!));
        using var streamer = new CollisionStreamer(world, build, workers, NullLogSink.Instance);
        Assert.Same(world, streamer.World);
        Assert.Equal(AttachState.Pending, streamer.StateOf(Centre));
    }

    [Fact]
    public void An_outdoor_focus_builds_its_3x3_on_the_workers_and_attaches_it_through_the_frame_budget()
    {
        var world = new PhysicsWorld();
        using var workers = new WorkerPool(2);
        var builds = new ConcurrentBag<LandblockCoord>();
        using var streamer = new CollisionStreamer(world, (c, _) => { builds.Add(c); return Flat(c); }, workers, NullLogSink.Instance);
        var budget = new FrameBudgetScheduler(TimeSpan.FromMilliseconds(100));

        streamer.SetFocus(Cell(Centre, 0x12));
        Assert.Equal(9, streamer.RequestedCount);
        Assert.Equal(AttachState.Pending, streamer.StateOf(Centre));
        PumpUntilSettled(streamer, budget, Ring(Centre));

        Assert.Equal(9, builds.Count);
        foreach (var coord in Ring(Centre))
        {
            Assert.Equal(AttachState.Attached, streamer.StateOf(coord));
            Assert.True(world.IsAttached(coord));
        }

        Assert.Equal(AttachState.Pending, streamer.StateOf(Centre.Offset(2, 0)!.Value));
        Assert.Equal(9, world.AttachedCount);

        // The same cell again changes nothing.
        streamer.SetFocus(Cell(Centre, 0x12));
        Assert.Equal(9, streamer.RequestedCount);
    }

    [Fact]
    public void Moving_east_releases_the_west_column_and_builds_the_east_one()
    {
        var world = new PhysicsWorld();
        using var workers = new WorkerPool(2);
        using var streamer = new CollisionStreamer(world, (c, _) => Flat(c), workers, NullLogSink.Instance);
        streamer.SetFocus(Cell(Centre, 0x01));
        streamer.PumpSynchronously();
        Assert.Equal(9, world.AttachedCount);

        var east = Centre.Offset(1, 0)!.Value;
        streamer.SetFocus(Cell(east, 0x01));
        streamer.PumpSynchronously();

        Assert.Equal(9, world.AttachedCount);
        foreach (var coord in Ring(east))
        {
            Assert.Equal(AttachState.Attached, streamer.StateOf(coord));
        }

        for (var dy = -1; dy <= 1; dy++)
        {
            var west = Centre.Offset(-1, dy)!.Value;
            Assert.False(world.IsAttached(west));
            Assert.Equal(AttachState.Pending, streamer.StateOf(west));
        }
    }

    [Fact]
    public void A_failed_build_settles_as_failed_and_is_logged_once()
    {
        var world = new PhysicsWorld();
        using var workers = new WorkerPool(2);
        var log = new RecordingLogSink();
        var broken = Centre.Offset(1, 1)!.Value;
        using var streamer = new CollisionStreamer(
            world,
            (c, _) => c == broken ? throw new InvalidDataException("bad landblock record") : Flat(c),
            workers,
            log);
        var budget = new FrameBudgetScheduler(TimeSpan.FromMilliseconds(100));
        streamer.SetFocus(Cell(Centre, 0x01));
        PumpUntilSettled(streamer, budget, Ring(Centre));
        streamer.Pump(budget);

        Assert.Equal(AttachState.Failed, streamer.StateOf(broken));
        Assert.True(streamer.StateOf(broken).IsSettled());
        Assert.Equal(8, world.AttachedCount);
        Assert.Equal(1, streamer.FailureCount);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogSeverity.Error, entry.Severity);
        Assert.Equal("physics", entry.Category);
        output.WriteLine(entry.Line);
        Assert.Contains("0x5151", entry.Line, StringComparison.Ordinal);
        Assert.Contains("bad landblock record", entry.Line, StringComparison.Ordinal);

        // Synchronously, too.
        var syncWorld = new PhysicsWorld();
        using var sync = new CollisionStreamer(syncWorld, (c, _) => c == broken ? throw new InvalidDataException("bad") : Flat(c), workers, log);
        sync.SetFocus(Cell(Centre, 0x01));
        sync.PumpSynchronously();
        Assert.Equal(AttachState.Failed, sync.StateOf(broken));
        Assert.Equal(8, syncWorld.AttachedCount);
    }

    [Fact]
    [Trait("Tier", "Timing")] // collision-streamer-settle-flake: waits for the streamer's workers to settle within 10 s; the workers starve under load
    public void Collision_the_host_attached_is_neither_rebuilt_nor_replaced_nor_detached()
    {
        foreach (var synchronous in new[] { true, false })
        {
            var world = new PhysicsWorld();
            var hosts = Flat(Centre);
            world.Attach(hosts);
            using var workers = new WorkerPool(2);
            var builds = new ConcurrentBag<LandblockCoord>();
            using var streamer = new CollisionStreamer(world, (c, _) => { builds.Add(c); return Flat(c); }, workers, NullLogSink.Instance);
            var budget = new FrameBudgetScheduler(TimeSpan.FromMilliseconds(100));

            streamer.SetFocus(Cell(Centre, 0x01));
            Settle(streamer, budget, synchronous, Ring(Centre));

            Assert.Equal(8, builds.Count);
            Assert.DoesNotContain(Centre, builds);
            Assert.Same(hosts, world.GetLandblock(Centre));
            Assert.Equal(AttachState.Attached, streamer.StateOf(Centre));
            Assert.Equal(9, world.AttachedCount);

            // Leaving: the streamer's eight go, the host's stays.
            var far = Centre.Offset(5, 0)!.Value;
            streamer.SetFocus(Cell(far, 0x01));
            Settle(streamer, budget, synchronous, Ring(far));
            Assert.Same(hosts, world.GetLandblock(Centre));
            Assert.Equal(10, world.AttachedCount);
            Assert.False(world.IsAttached(Centre.Offset(1, 1)!.Value));
        }
    }

    [Fact]
    public void Collision_the_host_attaches_over_the_streamers_is_theirs_and_never_detached()
    {
        foreach (var synchronous in new[] { true, false })
        {
            var world = new PhysicsWorld();
            using var workers = new WorkerPool(2);
            var builds = new ConcurrentBag<LandblockCoord>();
            using var streamer = new CollisionStreamer(world, (c, _) => { builds.Add(c); return Flat(c); }, workers, NullLogSink.Instance);
            var budget = new FrameBudgetScheduler(TimeSpan.FromMilliseconds(100));
            streamer.SetFocus(Cell(Centre, 0x01));
            Settle(streamer, budget, synchronous, Ring(Centre));
            Assert.Equal(9, builds.Count);
            var streamers = world.GetLandblock(Centre);
            Assert.NotNull(streamers);

            // The host replaces the streamer's collision with its own: the streamer neither rebuilds nor replaces it.
            var hosts = Flat(Centre);
            world.Attach(hosts);
            Settle(streamer, budget, synchronous, Ring(Centre));
            Assert.Equal(9, builds.Count);
            Assert.Same(hosts, world.GetLandblock(Centre));
            Assert.Equal(AttachState.Attached, streamer.StateOf(Centre));

            // Leaving: the streamer's eight go, the host's stays.
            var far = Centre.Offset(5, 0)!.Value;
            streamer.SetFocus(Cell(far, 0x01));
            Settle(streamer, budget, synchronous, Ring(far));
            Assert.Same(hosts, world.GetLandblock(Centre));
            Assert.Equal(10, world.AttachedCount);
            Assert.False(world.IsAttached(Centre.Offset(-1, 0)!.Value));

            // Coming back: the host's collision is still the one attached, and the streamer builds only the other eight.
            streamer.SetFocus(Cell(Centre, 0x01));
            Settle(streamer, budget, synchronous, Ring(Centre));
            Assert.Same(hosts, world.GetLandblock(Centre));
            Assert.Equal(9 + 9 + 8, builds.Count);
        }
    }

    [Fact]
    public void A_build_that_finishes_after_the_host_attached_the_landblock_is_dropped()
    {
        var world = new PhysicsWorld();
        using var workers = new WorkerPool(1);
        using var release = new ManualResetEventSlim();
        using var streamer = new CollisionStreamer(world, (c, _) => { release.Wait(); return Flat(c); }, workers, NullLogSink.Instance);
        var budget = new FrameBudgetScheduler(TimeSpan.FromMilliseconds(100));
        streamer.SetFocus(Cell(Centre, 0x01));
        var hosts = Flat(Centre);
        world.Attach(hosts);
        release.Set();
        PumpUntilSettled(streamer, budget, Ring(Centre));

        Assert.Same(hosts, world.GetLandblock(Centre));
        Assert.Equal(9, world.AttachedCount);

        // The host detaches it again: the streamer builds its own on the next pumps.
        world.Detach(Centre);
        Assert.Equal(AttachState.Pending, streamer.StateOf(Centre));
        PumpUntilSettled(streamer, budget, Ring(Centre));
        Assert.Equal(AttachState.Attached, streamer.StateOf(Centre));
        Assert.NotSame(hosts, world.GetLandblock(Centre));
    }

    [Fact]
    public void A_focus_off_the_map_requests_only_what_is_on_it()
    {
        var world = new PhysicsWorld();
        using var workers = new WorkerPool(1);
        using var streamer = new CollisionStreamer(world, (c, _) => Flat(c), workers, NullLogSink.Instance);
        streamer.SetFocus(Cell(new LandblockCoord(0, 0), 0x01));
        Assert.Equal(4, streamer.RequestedCount);
        streamer.PumpSynchronously();
        Assert.Equal(4, world.AttachedCount);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void An_EnvCell_that_sees_outside_gets_the_3x3_and_a_dungeon_cell_only_its_landblock()
    {
        var builder = new LandblockCollisionBuilder(fixture.Dats);
        using var workers = new WorkerPool(2);

        // A Holtburg building room sees the landscape (owner decision D29).
        var holtburg = new PhysicsWorld();
        using var streamer = new CollisionStreamer(holtburg, (c, ct) => builder.Build(c, ct), workers, NullLogSink.Instance);
        var room = builder.Build(LandblockCoord.Holtburg).EnvCells.Values.First(c => c.SeenOutside).Id;
        streamer.SetFocus(room);
        Assert.Equal(1, streamer.RequestedCount);
        streamer.PumpSynchronously();
        Assert.Equal(9, holtburg.AttachedCount);
        Assert.Equal(AttachState.Attached, streamer.StateOf(LandblockCoord.Holtburg.Offset(-1, 1)!.Value));

        // The Training Academy's spawn room is a dungeon: only its own landblock.
        var academy = new PhysicsWorld();
        using var dungeon = new CollisionStreamer(academy, (c, ct) => builder.Build(c, ct), workers, NullLogSink.Instance);
        dungeon.SetFocus(0x860201AD);
        dungeon.PumpSynchronously();
        Assert.Equal(1, academy.AttachedCount);
        Assert.Equal(AttachState.Attached, dungeon.StateOf(new LandblockCoord(0x86, 0x02)));
        Assert.Equal(AttachState.Pending, dungeon.StateOf(new LandblockCoord(0x87, 0x02)));
    }

    /// <summary>Pumps and drains until every landblock in <paramref name="coords"/> has settled (at most 30 s: the helper's own bound, raised from 10 s after it timed out once under the full suite's load).</summary>
    private static void PumpUntilSettled(CollisionStreamer streamer, FrameBudgetScheduler budget, IEnumerable<LandblockCoord> coords)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            streamer.Pump(budget);
            budget.Drain();
            if (coords.All(c => streamer.StateOf(c).IsSettled()))
            {
                return;
            }

            Thread.Sleep(5);
        }

        Assert.Fail("the collision did not settle within 30 s");
    }

    /// <summary>Settles the focus with <see cref="CollisionStreamer.PumpSynchronously"/> or through the frame budget.</summary>
    private static void Settle(CollisionStreamer streamer, FrameBudgetScheduler budget, bool synchronous, IEnumerable<LandblockCoord> coords)
    {
        if (synchronous)
        {
            streamer.PumpSynchronously();
            return;
        }

        PumpUntilSettled(streamer, budget, coords);

        // One more pump detaches what the focus left.
        streamer.Pump(budget);
        budget.Drain();
    }

    private static IEnumerable<LandblockCoord> Ring(LandblockCoord centre)
    {
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                yield return centre.Offset(dx, dy)!.Value;
            }
        }
    }

    private static uint Cell(LandblockCoord coord, uint cell) => ((uint)coord.X << 24) | ((uint)coord.Y << 16) | cell;
}
