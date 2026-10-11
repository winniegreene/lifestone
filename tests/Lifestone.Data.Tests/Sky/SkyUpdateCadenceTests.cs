using Lifestone.Data.World.Sky;

namespace Lifestone.Data.Tests.Sky;

/// <summary>LScape::UseTime fog/light cadence (acclient.c:307221).</summary>
public sealed class SkyUpdateCadenceTests
{
    private static SkyDescription Sky(double tick, double lightTick) =>
        new(tick, lightTick, [new SkyDayGroup(0, 5f, "Test", [], [])]);

    [Fact]
    public void Fog_every_tick_and_light_every_light_tick()
    {
        var cadence = new SkyUpdateCadence(Sky(0.8, 15.0));
        Assert.Equal(SkyUpdates.Fog | SkyUpdates.Lighting, cadence.Tick(1000.0));
        Assert.Equal(SkyUpdates.None, cadence.Tick(1000.5));
        Assert.Equal(SkyUpdates.Fog, cadence.Tick(1000.9));
        Assert.Equal(1001.7, cadence.NextTick, 9);
        Assert.Equal(SkyUpdates.Fog, cadence.Tick(1014.9));
        Assert.Equal(SkyUpdates.None, cadence.Tick(1015.1)); // light is only checked inside a fog tick
        Assert.Equal(SkyUpdates.Fog | SkyUpdates.Lighting, cadence.Tick(1015.75));
        Assert.Equal(1030.75, cadence.NextLightTick, 9);

        cadence.Reset();
        Assert.Equal(SkyUpdates.Fog | SkyUpdates.Lighting, cadence.Tick(1016.0));
    }
}
