using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.World;
using Lifestone.Data.Terrain;

namespace Lifestone.Client.Tests;

public sealed class WorldLocationTests
{
    [Theory]
    [InlineData("holtburg", 0xA9B40019u, 84f, 7.1f, 94f, true)]
    [InlineData("Arwic", 0xC6A90009u, 46.805f, 4.219f, 42.005f, true)]
    [InlineData("A9B4", 0xA9B4FFFFu, 96f, 96f, 0f, false)]
    [InlineData("0xC6A9", 0xC6A9FFFFu, 96f, 96f, 0f, false)]
    [InlineData("A9B40019", 0xA9B40019u, 84f, 12f, 0f, false)] // centre of cell (3, 0)
    [InlineData("a9b40019@84,7.1,94", 0xA9B40019u, 84f, 7.1f, 94f, true)]
    [InlineData("01D90100@3,-49.9,0", 0x01D90100u, 3f, -49.9f, 0f, true)] // an EnvCell needs an explicit origin
    [InlineData("holtburg@1,2,3", 0xA9B40019u, 1f, 2f, 3f, true)]
    public void Parses_names_landblocks_cells_and_exact_origins(string text, uint cellId, float x, float y, float z, bool hasHeight)
    {
        Assert.True(WorldLocations.TryParse(text, out var location, out var error), error);
        Assert.Equal(cellId, location.Position.CellId);
        Assert.Equal(new Vector3(x, y, z), location.Position.Local);
        Assert.Equal(hasHeight, location.HasHeight);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nowhere")]
    [InlineData("A9B")]
    [InlineData("A9B4001")]
    [InlineData("FFB4")] // landblock 0xFF is off the map
    [InlineData("A9B40100")] // EnvCell without an origin
    [InlineData("A9B4@1,2")]
    public void Rejects_bad_locations_with_a_reason(string text)
    {
        Assert.False(WorldLocations.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Holtburg_faces_nine_degrees_east_of_north()
    {
        // Quaternion (w 0.996917, z -0.078459): a 9 degree clockwise turn from north.
        Assert.Equal(9.0, WorldLocations.Holtburg.HeadingDegrees, 2);
        Assert.Equal(0.0, WorldLocations.Arwic.HeadingDegrees, 9);
        Assert.Equal(LandblockCoord.Holtburg, WorldLocations.Holtburg.Position.Landblock);
    }

    [Fact]
    public void Holtburg_to_Arwic_is_about_six_kilometres_east_south_east()
    {
        Assert.True(FlyPathSpec.TryParse("holtburg:arwic:150", out var spec, out var error), error);
        Assert.NotNull(spec);
        Assert.Equal(new LandblockCoord(0xC6, 0xA9), spec.To.Position.Landblock);
        Assert.Equal(5921.4, spec.HorizontalLength, 0);
        Assert.Equal(spec.HorizontalLength / 150.0, spec.DurationSeconds, 9);
        Assert.InRange(spec.HeadingDegrees, 110.0, 112.0);

        Assert.False(FlyPathSpec.TryParse("holtburg:arwic", out _, out _));
        Assert.False(FlyPathSpec.TryParse("holtburg:arwic:-3", out _, out _));
        Assert.False(FlyPathSpec.TryParse("holtburg:mars:10", out _, out _));
    }
}

public sealed class FrameTimeStatsTests
{
    [Fact]
    public void Window_keeps_the_last_n_frames()
    {
        var window = new FrameTimeWindow(3);
        window.Add(10);
        window.Add(40);
        window.Add(20);
        Assert.Equal(40, window.Max);
        window.Add(5);
        window.Add(6);
        Assert.Equal(20, window.Max);
        Assert.Equal((20 + 5 + 6) / 3.0, window.Average, 9);
        Assert.Equal(3, window.Count);
    }

    [Fact]
    public void Recorder_reports_nearest_rank_percentiles_and_hitches()
    {
        var recorder = new FrameTimeRecorder();
        for (var i = 1; i <= 100; i++)
        {
            recorder.Add(i);
        }

        Assert.Equal(99, recorder.Percentile(99));
        Assert.Equal(50, recorder.Percentile(50));
        Assert.Equal(100, recorder.Percentile(100));
        Assert.Equal(1, recorder.Percentile(0.5));
        Assert.Equal(100, recorder.Max);
        Assert.Equal(50.5, recorder.Average, 9);
        Assert.Equal(50, recorder.CountAbove(50));
        Assert.Equal(0, new FrameTimeRecorder().Percentile(99));
    }
}
