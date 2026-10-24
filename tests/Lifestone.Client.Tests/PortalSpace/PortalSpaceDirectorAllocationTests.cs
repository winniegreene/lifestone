using Lifestone.Client.Audio;
using Lifestone.Client.PortalSpace;
using Lifestone.Client.Tests.Audio;
using Lifestone.Data.Effects;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.PortalSpace;

/// <summary>
/// Step PS11: the director allocates nothing per frame, through whole teleports (the machine, the readiness, the flags,
/// the cues, the retail sound player and the sinks) and while it holds the real tunnel with its hum. Measured in the
/// non-parallel <see cref="AllocationMeasurementCollection"/>; a window with a GC inside is not judged.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class PortalSpaceDirectorAllocationTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [Fact]
    public void Ten_thousand_steps_of_teleport_after_teleport_allocate_nothing()
    {
        var tunnel = new FakeTunnel { Frame = 70 };
        var machine = new PortalSpaceMachine(tunnel, new MsvcCrtRandom(5), () => PortalRig.Sixty);
        var progress = new TeleportProgress();
        var output = new CountingAudioOutput();
        var sound = new RetailSoundPlayer(output, RetailSoundPlayerTests.Source(), new RetailRandom(5), new MsvcCrtRandom(5));
        var sinks = new CountingPortalSinks();
        var director = new PortalSpaceDirector(machine, progress, DirectorRig.Assets, ReadinessPolicy.Default, sound, sinks)
        {
            LoginComplete = sinks,
        };
        var world = new FakeWorld().SetVisible(0x01D90105, 0x01D90106, 0x01D90107);
        ushort stamp = 0;

        // One cycle of 600 frames at 1/60 s: an F751, its position 2 s later, then the whole sequence back to state 0.
        void Frame(int index)
        {
            switch (index % 600)
            {
                case 0:
                    progress.OnPlayerTeleport(++stamp, (ushort)(stamp - 1));
                    break;
                case 120:
                    progress.OnPlayerPositionTeleported();
                    break;
            }

            var cell = (index / 600) % 2 == 0 ? 0xA9B40001u : 0x01D90105u;
            director.Step(index / 60.0, cell, world);
        }

        progress.OnPlayerCreated();
        for (var index = 1; index < 1200; index++)
        {
            Frame(index); // warm up: the login, one teleport, the JIT
        }

        var (allocated, gcFrames) = PortalSpaceAllocationTests.Measure(1200, 11200, Frame);

        Assert.True(sinks.LoginCompletes >= 17, $"only {sinks.LoginCompletes} LoginCompletes in 18 teleports");
        Assert.True(output.Plays >= 2 * 17, "the whooshes did not play");
        Assert.True(sinks.Notices > 0);
        Assert.True(gcFrames <= 100, $"{gcFrames} frames had a GC inside (not judged); too many to trust the measurement");
        Assert.True(allocated == 0, $"10,000 director steps allocated {allocated} B");
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Holding_the_real_tunnel_with_its_hum_allocates_nothing()
    {
        var dats = fixture.Dats;
        var assets = PortalSpaceAssets.Resolve(dats.EnumMaps);
        var tunnel = TunnelObject.Create(dats, assets);
        var machine = new PortalSpaceMachine(tunnel, new MsvcCrtRandom(5), () => PortalRig.Sixty);
        var progress = new TeleportProgress();
        var output = new CountingAudioOutput();
        var sound = new RetailSoundPlayer(output, new Lifestone.Data.Audio.DatAudioSource(dats), new RetailRandom(5), new MsvcCrtRandom(5));
        var director = new PortalSpaceDirector(machine, progress, assets, ReadinessPolicy.Default, sound, new CountingPortalSinks());
        var world = new FakeWorld();
        progress.BeginLocal();
        progress.Hold(true);

        void Frame(int index) => director.Step(index / 60.0, 0xA9B40001, world);

        for (var index = 1; index < 600; index++)
        {
            Frame(index); // warm up: the Begin, the first hums, the JIT
        }

        var (allocated, gcFrames) = PortalSpaceAllocationTests.Measure(600, 10600, Frame);

        Assert.Equal(TeleportAnimState.InTunnel, machine.State);
        Assert.True(director.TunnelEvents >= 55, $"only {director.TunnelEvents} hums in 176 s");
        Assert.True(output.Plays >= 55);
        Assert.True(gcFrames <= 100, $"{gcFrames} frames had a GC inside (not judged); too many to trust the measurement");
        Assert.True(allocated == 0, $"10,000 frames in the real tunnel allocated {allocated} B");
    }

    /// <summary>Notice and LoginComplete sinks that only count.</summary>
    private sealed class CountingPortalSinks : IPortalNoticeSink, ILoginCompleteSink
    {
        public int Notices { get; private set; }

        public int LoginCompletes { get; private set; }

        public void PostNotice(string literal, uint textType) => Notices++;

        public void SendLoginComplete() => LoginCompletes++;
    }
}
