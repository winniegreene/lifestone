using Lifestone.Client.Audio;
using Lifestone.Client.PortalSpace;
using Lifestone.Data.Effects;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.PortalSpace;

/// <summary>
/// Portal space allocates nothing per frame: the easing table, the zoom, the lens and the roll's random source (PS3,
/// PS4), the state machine with the teleport flags and the destination readiness (PS6 to PS8), and the tunnel's frame
/// counter (PS5). Measured on this thread in the non-parallel <see cref="AllocationMeasurementCollection"/>; a window
/// with a GC inside is not judged.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class PortalSpaceAllocationTests
{
    [Fact]
    public void A_frame_of_zoom_maths_allocates_nothing()
    {
        var random = new MsvcCrtRandom(3);
        var gameFocal = PortalZoom.FocalFromVerticalFov(Math.PI / 3.0);
        var checksum = Frame(random, gameFocal, 0.5); // warm up (static tables, JIT)

        var allocated = 0L;
        var gcFrames = 0;
        for (var frame = 0; frame < 2000; frame++)
        {
            var window = AllocationWindow.Open();
            checksum += Frame(random, gameFocal, frame / 2000.0);
            var (bytes, gcInside) = window.Close();
            if (gcInside)
            {
                gcFrames++;
                continue;
            }

            allocated += bytes;
        }

        Assert.True(double.IsFinite(checksum));
        Assert.True(gcFrames <= 20, $"{gcFrames} of 2000 frames had a GC inside (not judged); too many to trust the measurement");
        Assert.True(allocated == 0, $"the zoom maths allocated {allocated} B over 2000 frames");
    }

    [Fact]
    public void Teleport_after_teleport_the_machine_flags_and_readiness_allocate_nothing()
    {
        var tunnel = new FakeTunnel { Frame = 70 };
        var progress = new TeleportProgress();
        var machine = new PortalSpaceMachine(tunnel, new MsvcCrtRandom(5), () => 1.7320508f);
        var world = new FakeWorld().SetVisible(0x01D90105, 0x01D90106, 0x01D90107);
        var policy = ReadinessPolicy.Default;
        var checksum = 0.0;
        var cues = 0;
        ushort stamp = 0;

        // One cycle of 600 frames at 1/60 s: an F751, its position 2 s later, then the whole sequence back to state 0.
        void Frame(int index)
        {
            var now = index / 60.0;
            switch (index % 600)
            {
                case 0:
                    progress.OnPlayerTeleport(++stamp, (ushort)(stamp - 1));
                    break;
                case 120:
                    progress.OnPlayerPositionTeleported();
                    break;
            }

            var frame = machine.Update(now, progress);
            var cell = (index / 600) % 2 == 0 ? 0xA9B40001u : 0x01D90105u;
            var readiness = DestinationReadiness.Evaluate(cell, world, policy);
            progress.Tick(readiness.IsReady);
            if (frame.TunnelVisible)
            {
                tunnel.AdvanceTo(now);
            }

            checksum += frame.Lens.VerticalFovRadians + frame.Lens.Near + frame.RollDegrees + frame.StateElapsed + readiness.Checked;
            cues |= (int)frame.Cues;
        }

        progress.OnPlayerCreated();
        for (var index = 1; index < 1200; index++)
        {
            Frame(index); // warm up: the login, one teleport, the JIT
        }

        var (allocated, gcFrames) = Measure(1200, 12000, Frame);

        Assert.True(double.IsFinite(checksum));
        Assert.True((cues & (int)PortalCues.SendLoginComplete) != 0, "the cycle never reached the end of state 6");
        Assert.True(gcFrames <= 100, $"{gcFrames} frames had a GC inside (not judged); too many to trust the measurement");
        Assert.True(allocated == 0, $"20 teleports allocated {allocated} B");
    }

    [Fact]
    public void The_timed_tunnel_s_steady_steps_allocate_nothing()
    {
        var tunnel = new TimedTunnel();
        tunnel.Restart();
        for (var index = 1; index <= 3; index++)
        {
            tunnel.AdvanceTo(index / 60.0); // at least one step before the measurement (the JIT)
        }

        var checksum = 0;

        var (allocated, gcFrames) = Measure(4, 3 * 179, index =>
        {
            tunnel.AdvanceTo(index / 60.0);
            checksum += tunnel.CurrentFrame;
        });

        Assert.True(checksum > 0);
        Assert.True(gcFrames <= 20, $"{gcFrames} frames had a GC inside (not judged)");
        Assert.True(allocated == 0, $"three loops allocated {allocated} B");
    }

    /// <summary>Runs <paramref name="frame"/> for indices [<paramref name="from"/>, <paramref name="to"/>), each in its own window.</summary>
    internal static (long Allocated, int GcFrames) Measure(int from, int to, Action<int> frame)
    {
        var allocated = 0L;
        var gcFrames = 0;
        for (var index = from; index < to; index++)
        {
            var window = AllocationWindow.Open();
            frame(index);
            var (bytes, gcInside) = window.Close();
            if (gcInside)
            {
                gcFrames++;
                continue;
            }

            allocated += bytes;
        }

        return (allocated, gcFrames);
    }

    /// <summary>What one frame of states 1, 2, 5 and 6 computes, summed so nothing is optimised away.</summary>
    private static double Frame(MsvcCrtRandom random, float gameFocal, double t)
    {
        var collapse = new PortalLens(PortalZoom.Collapse(gameFocal, t), Overridden: true);
        var expand = new PortalLens(PortalZoom.Expand(gameFocal, t), Overridden: true);
        return collapse.VerticalFovRadians + collapse.Near + collapse.FrustumHeightAtNear
            + expand.VerticalFovDegrees + expand.Near + expand.FrustumHeightAtNear
            + AnimLevel.Fraction((float)t) + random.RandDouble(0.6, 1.8) + random.RandDouble(0.0, 360.0);
    }
}

/// <summary>
/// Step PS5 on the real dats: the tunnel object's steady steps allocate nothing, the hum included, once its event lists
/// have grown.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
[Trait("Tier", "Exhaustive")]
public sealed class TunnelObjectAllocationTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [RequiresDatsFact]
    public void Three_loops_of_the_spin_and_the_hum_allocate_nothing()
    {
        var tunnel = TunnelObject.Create(fixture.Dats, PortalSpaceAssets.Resolve(fixture.Dats.EnumMaps));
        var events = new List<EffectEvent>(4);
        tunnel.Restart();
        for (var index = 1; index <= 2 * 179; index++)
        {
            // Two loops: the JIT runs, and both lists of the effect object's hook swap grow on their first hum.
            tunnel.AdvanceTo(index / 60.0);
            tunnel.DrainEvents(events);
            events.Clear();
        }

        var hums = 0;
        var (allocated, gcFrames) = PortalSpaceAllocationTests.Measure((2 * 179) + 1, (5 * 179) + 1, index =>
        {
            tunnel.AdvanceTo(index / 60.0);
            hums += tunnel.DrainEvents(events);
            events.Clear();
        });

        Assert.Equal(3, hums);
        Assert.True(gcFrames <= 20, $"{gcFrames} frames had a GC inside (not judged)");
        Assert.True(allocated == 0, $"three loops allocated {allocated} B");
    }
}
