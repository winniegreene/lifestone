using System.Numerics;
using Lifestone.Client.World;
using Lifestone.Data.Terrain;

namespace Lifestone.Client.Tests;

public sealed class FloatingOriginTests
{
    [Fact]
    public void Render_local_positions_are_relative_to_the_origin_landblock_corner()
    {
        var origin = new FloatingOrigin(LandblockCoord.Holtburg);
        var holtburg = WorldLocations.Holtburg.Global;
        Assert.Equal(new Vector3(84f, 7.1f, 94f), origin.ToRenderLocal(holtburg));
        Assert.Equal(new Vector3(192f, -384f, 0f), origin.LandblockOffset(new LandblockCoord(0xAA, 0xB2)));
        var back = origin.ToGlobal(new Vector3(84f, 7.1f, 94f));
        Assert.True(back.DistanceTo(holtburg) < 1e-5);
    }

    [Fact]
    public void Rebases_only_past_the_threshold_and_onto_the_focus_landblock()
    {
        var origin = new FloatingOrigin(new LandblockCoord(100, 100), rebaseDistance: 2);
        var inside = AcCoordinates.LandblockOrigin(new LandblockCoord(102, 98)) + new Vector3(191f, 1f, 0f);
        Assert.False(origin.Update(inside, out _));
        Assert.Equal(new LandblockCoord(100, 100), origin.Origin);

        var outside = AcCoordinates.LandblockOrigin(new LandblockCoord(103, 99)) + new Vector3(0.5f, 10f, 0f);
        Assert.True(origin.Update(outside, out var shift));
        Assert.Equal(new LandblockCoord(103, 99), origin.Origin);
        Assert.Equal(new OriginShift(new LandblockCoord(100, 100), new LandblockCoord(103, 99), new Vector3(-576f, 192f, 0f)), shift);
        Assert.Equal(1, origin.RebaseCount);
    }

    [Fact]
    public void A_shift_maps_old_render_positions_onto_new_ones()
    {
        var origin = new FloatingOrigin(new LandblockCoord(10, 20), rebaseDistance: 0);
        var global = AcCoordinates.LandblockOrigin(new LandblockCoord(12, 19)) + new Vector3(37.25f, 150.5f, 12f);
        var before = origin.ToRenderLocal(global);
        var shift = origin.Rebase(new LandblockCoord(12, 19));
        var after = origin.ToRenderLocal(global);
        Assert.Equal(after, before + shift.Delta);
        Assert.Equal(new Vector3(37.25f, 150.5f, 12f), after);
    }

    [Fact]
    public void Render_positions_stay_small_and_exact_however_far_and_often_the_focus_moves()
    {
        // Fly a path that crosses the whole map back and forth with thousands of rebases: render-local positions must
        // stay within (N + 1) landblocks of the origin and match the double-precision truth to float rounding.
        var origin = new FloatingOrigin(new LandblockCoord(0, 0), rebaseDistance: 2);
        var random = new Random(42);
        var focus = new GlobalPosition(100.0, 100.0, 50.0);
        var maxError = 0.0;
        for (var step = 0; step < 200_000; step++)
        {
            var heading = random.NextDouble() * 2 * Math.PI;
            focus = new GlobalPosition(
                Math.Clamp(focus.X + (Math.Sin(heading) * 40.0), 0, 48_959.0),
                Math.Clamp(focus.Y + (Math.Cos(heading) * 40.0) + 0.5, 0, 48_959.0),
                focus.Z);
            origin.Update(focus, out _);
            var local = origin.ToRenderLocal(focus);
            Assert.True(Math.Abs(local.X) <= 3 * 192 && Math.Abs(local.Y) <= 3 * 192, $"step {step}: {local} at origin {origin.Origin}");
            var back = origin.ToGlobal(local);
            maxError = Math.Max(maxError, Math.Max(Math.Abs(back.X - focus.X), Math.Abs(back.Y - focus.Y)));
        }

        Assert.True(origin.RebaseCount > 1_000, $"{origin.RebaseCount} rebases");

        // Float spacing at 576 m is 2^-14 m (61 µm); the round trip loses at most half of it.
        Assert.True(maxError <= Math.Pow(2, -15), $"max error {maxError} m");

        // The same point far from the map origin in plain float would be off by millimetres.
        var far = new GlobalPosition(48_000.123456, 47_000.654321, 0);
        Assert.True(Math.Abs((float)far.X - far.X) > 1e-4);
    }

    [Fact]
    public void Landblock_offsets_are_exact_multiples_of_192()
    {
        var origin = new FloatingOrigin(new LandblockCoord(0xFE, 0x00));
        var offset = origin.LandblockOffset(new LandblockCoord(0x00, 0xFE));
        Assert.Equal(-254f * 192f, offset.X);
        Assert.Equal(254f * 192f, offset.Y);
        Assert.Equal(-48_768f, offset.X);
    }

    [Fact]
    public void Rebase_threshold_must_not_be_negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FloatingOrigin(LandblockCoord.Holtburg, -1));
    }
}
