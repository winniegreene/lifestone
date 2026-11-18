using Lifestone.Client.Audio;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Audio;

/// <summary>A terrain source whose one landblock arrives late, as <see cref="BackgroundAmbientTerrain"/>'s reads do: null until <see cref="Arrive"/>, which bumps the revision.</summary>
internal sealed class LateAmbientTerrain(FakeAmbientTerrain inner) : IAmbientTerrain
{
    private bool _arrived;
    private int _revision;

    public int Revision => _revision;

    public void Arrive()
    {
        _arrived = true;
        _revision++;
    }

    public LandblockTerrain? Get(LandblockCoord coord) => _arrived ? inner.Get(coord) : null;
}

/// <summary>
/// M3 1.3 fix A (1.3-R3): the ambient scan runs when the listener's cell changes, as retail's <c>CellManager::ChangePosition</c> (11.6096 0x00455AD0) does in its
/// new-cell branch, while <c>Ambient::UseTime</c> (0x005524A0) runs every frame; and (1.3-R1 (d)) the terrain is read off the frame thread.
/// </summary>
public sealed class AmbientScanCadenceTests
{
    private static CellFrame InCell(ushort cell, float x, float y) =>
        new(((uint)AmbientSoundPlayerTests.Home.X << 24) | ((uint)AmbientSoundPlayerTests.Home.Y << 16) | cell, new AcFrame(new System.Numerics.Vector3(x, y, 0f), System.Numerics.Quaternion.Identity));

    [Fact]
    public void The_scan_runs_when_the_cell_changes_and_not_while_the_listener_walks_inside_one_but_the_queue_runs_every_frame()
    {
        var sink = new RecordingAmbientSink();
        var player = AmbientSoundPlayerTests.Player(AmbientSoundPlayerTests.RegionOf(AmbientSoundPlayerTests.Sound(0x46, 0.6f, 1f, 1f, 1f)), new FakeAmbientTerrain(AmbientSoundPlayerTests.Home, (3, 2)), sink);

        // 600 frames inside one cell, walking 20 m: one scan, 600 frames, and the sound still comes due every second.
        for (var frame = 0; frame < 600; frame++)
        {
            player.Update(InCell(0x0021, 72f + (frame * 0.03f), 88f), outdoors: true, frame / 60.0);
        }

        Assert.Equal(600, player.Frames);
        Assert.Equal(1, player.Scans);
        Assert.InRange(player.Played, 9, 11);

        // A new cell: one more scan, however many frames follow in it.
        for (var frame = 0; frame < 120; frame++)
        {
            player.Update(InCell(0x0022, 80f, 88f), outdoors: true, 10.0 + (frame / 60.0));
        }

        Assert.Equal(2, player.Scans);

        // The outdoors flag changing is a scan too (a doorway: retail's seen-outside test), and so is coming back.
        player.Update(InCell(0x0022, 80f, 88f), outdoors: false, 12.0);
        player.Update(InCell(0x0022, 80f, 88f), outdoors: true, 12.1);
        Assert.Equal(4, player.Scans);
    }

    [Fact]
    public void A_landblock_that_arrives_late_is_scanned_when_it_arrives_without_the_listener_changing_cell()
    {
        var sink = new RecordingAmbientSink();
        var late = new LateAmbientTerrain(new FakeAmbientTerrain(AmbientSoundPlayerTests.Home, (3, 2)));
        var player = AmbientSoundPlayerTests.Player(AmbientSoundPlayerTests.RegionOf(AmbientSoundPlayerTests.Sound(0x46, 0.6f, 1f, 1f, 1f)), late, sink);

        for (var frame = 0; frame < 120; frame++)
        {
            player.Update(InCell(0x0021, 72f, 88f), outdoors: true, frame / 60.0);
        }

        Assert.Equal(0, player.Draws); // nothing to hear yet
        Assert.Equal(1, player.Scans);

        late.Arrive();
        for (var frame = 0; frame < 120; frame++)
        {
            player.Update(InCell(0x0021, 72f, 88f), outdoors: true, 2.0 + (frame / 60.0));
        }

        Assert.Equal(2, player.Scans);
        Assert.True(player.Draws >= 1, "the late landblock's sound was never drawn");
    }

    [Fact]
    public void A_reset_drops_the_weights_so_a_sound_due_is_not_played_and_the_next_update_scans_again()
    {
        var sink = new RecordingAmbientSink();
        var player = AmbientSoundPlayerTests.Player(AmbientSoundPlayerTests.RegionOf(AmbientSoundPlayerTests.Sound(0x46, 0.6f, 1f, 1f, 1f)), new FakeAmbientTerrain(AmbientSoundPlayerTests.Home, (3, 2)), sink);

        player.Update(InCell(0x0021, 72f, 88f), outdoors: true, 0.0);
        Assert.Equal(1, player.Draws);
        Assert.True(player.TotalWeight > 0f);

        player.Reset();
        Assert.Equal(0f, player.TotalWeight);
        player.Reset(); // twice is nothing

        player.Update(InCell(0x0021, 72f, 88f), outdoors: true, 0.5); // the same cell, but the scan was forgotten
        Assert.Equal(2, player.Scans);
        Assert.True(player.TotalWeight > 0f);
    }

    [Fact]
    [Trait("Tier", "Timing")] // m5-ambientscan-cadence-flake: the one test of the class that waits on the thread pool (the cadence tests count frames, not time)
    public void The_background_terrain_reads_on_a_pool_thread_returns_null_until_it_is_read_and_bumps_the_revision()
    {
        var home = AmbientSoundPlayerTests.Home;
        var terrain = new FakeAmbientTerrain(home, (3, 2)).Get(home)!;
        using var release = new ManualResetEventSlim();
        var readThreads = new List<int>();
        var cache = new BackgroundAmbientTerrain(coord =>
        {
            lock (readThreads)
            {
                readThreads.Add(Environment.CurrentManagedThreadId);
            }

            release.Wait(TimeSpan.FromSeconds(10));
            return coord == home ? terrain : null;
        });

        var before = cache.Revision;
        Assert.Null(cache.Get(home)); // queued, not read here
        Assert.Null(cache.Get(home)); // still pending: no second read
        Assert.Equal(0, cache.ReadsOnCallerThread);

        release.Set();
        Assert.True(SpinWait.SpinUntil(() => cache.ReadsFinished == 1, TimeSpan.FromSeconds(10)), "the pool read did not finish in 10 s");
        Assert.True(SpinWait.SpinUntil(() => cache.Revision != before, TimeSpan.FromSeconds(10)), "the revision did not move in 10 s"); // the revision is published before the count, so it is already there

        Assert.NotEqual(before, cache.Revision);
        Assert.Same(terrain, cache.Get(home));
        lock (readThreads)
        {
            Assert.Single(readThreads);
            Assert.NotEqual(Environment.CurrentManagedThreadId, readThreads[0]);
        }

        Assert.Equal(1, cache.ReadsStarted);
        Assert.Null(cache.Get(home.Offset(1, 0)!.Value)); // another block starts its own read
        Assert.Equal(2, cache.ReadsStarted);
    }

    [Fact]
    public void The_synchronous_mode_reads_inside_get_and_a_read_that_throws_is_counted_and_not_retried()
    {
        var home = AmbientSoundPlayerTests.Home;
        var reads = 0;
        var cache = new BackgroundAmbientTerrain(
            _ =>
            {
                reads++;
                throw new InvalidDataException("unreadable");
            },
            background: false);

        Assert.Null(cache.Get(home));
        Assert.Null(cache.Get(home));
        Assert.Equal(1, reads);
        Assert.Equal(1, cache.ReadsOnCallerThread);
        Assert.Equal(1, cache.Failures);
    }
}
