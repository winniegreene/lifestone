using Lifestone.Client.Cinematic;

namespace Lifestone.Client.Tests.Cinematic;

public sealed class CameraPathTests
{
    private static readonly Vec3d Origin = new(32532, 34567, 94);

    [Fact]
    public void Arc_timing_moves_at_constant_speed_along_a_curved_path()
    {
        var keys = new[]
        {
            new PathKey(Origin, Yaw: 0),
            new PathKey(Origin + new Vec3d(100, 0, 0), Yaw: 0),
            new PathKey(Origin + new Vec3d(110, 80, 20), Yaw: 0),
            new PathKey(Origin + new Vec3d(300, 90, 0), Yaw: 0),
        };
        var path = new CameraPath(keys, 10.0, Easing.Linear, PathTiming.Auto, 60);
        Assert.Equal(PathTiming.Arc, path.Timing);
        var expected = path.Length / 600.0;
        for (var i = 0; i < 600; i++)
        {
            var step = Vec3d.Distance(path.Sample(i / 60.0).Position, path.Sample((i + 1) / 60.0).Position);
            Assert.InRange(step, expected * 0.97, expected * 1.03);
        }

        Assert.True(Vec3d.Distance(keys[^1].Position, path.Sample(10).Position) < 1e-9);
        Assert.True(Vec3d.Distance(keys[0].Position, path.Sample(0).Position) < 1e-9);
    }

    [Fact]
    public void An_eased_shot_starts_and_ends_at_rest()
    {
        var path = new CameraPath([new PathKey(Origin, Yaw: 90), new PathKey(Origin + new Vec3d(200, 0, 0), Yaw: 90)], 8.0, new Easing(EaseKind.InOut), PathTiming.Arc, 60);
        var first = Vec3d.Distance(path.Sample(0).Position, path.Sample(1 / 60.0).Position);
        var middle = Vec3d.Distance(path.Sample(4).Position, path.Sample(4 + (1 / 60.0)).Position);
        var last = Vec3d.Distance(path.Sample(8 - (1 / 60.0)).Position, path.Sample(8).Position);
        Assert.True(first < middle / 50, $"first {first} middle {middle}");
        Assert.True(last < middle / 50, $"last {last} middle {middle}");
        Assert.Equal(100.0, path.Sample(4).Position.X - Origin.X, 6);
    }

    [Fact]
    public void Key_timing_reaches_each_key_at_its_time()
    {
        var keys = new[]
        {
            new PathKey(Origin, Yaw: 10, Time: 0),
            new PathKey(Origin + new Vec3d(10, 0, 0), Yaw: 20, Time: 1),
            new PathKey(Origin + new Vec3d(200, 0, 0), Yaw: 30, Time: 5),
            new PathKey(Origin + new Vec3d(220, 0, 0), Yaw: 40, Time: 6),
        };
        var path = new CameraPath(keys, 7.0, Easing.Linear, PathTiming.Auto, 60);
        Assert.Equal(PathTiming.Keys, path.Timing);
        foreach (var key in keys)
        {
            var sample = path.Sample(key.Time!.Value);
            Assert.True(Vec3d.Distance(key.Position, sample.Position) < 1e-6, $"t = {key.Time}");
            Assert.Equal(key.Yaw!.Value, sample.Yaw, 6);
        }

        Assert.True(Vec3d.Distance(keys[^1].Position, path.Sample(7).Position) < 1e-6); // holds after the last key
    }

    [Fact]
    public void Headings_turn_the_short_way_round()
    {
        var path = new CameraPath([new PathKey(Origin, Yaw: 350), new PathKey(Origin, Yaw: 10)], 2.0, Easing.Linear, PathTiming.Auto, 60);
        Assert.Equal(PathTiming.Uniform, path.Timing); // a fixed camera pans in uniform time
        for (var t = 0.0; t <= 2.0; t += 0.05)
        {
            var yaw = path.Sample(t).Yaw;
            Assert.True(yaw >= 349.999 || yaw <= 10.001, $"t = {t}: yaw {yaw} went the long way");
        }

        Assert.Equal(0.0, path.Sample(1.0).Yaw, 6);
    }

    [Fact]
    public void A_look_at_path_keeps_its_target_in_the_middle_of_the_frame()
    {
        var target = Origin + new Vec3d(0, 0, 10);
        var keys = new[]
        {
            new PathKey(Origin + new Vec3d(-200, -100, 80), Look: target),
            new PathKey(Origin + new Vec3d(0, -220, 60), Look: target),
            new PathKey(Origin + new Vec3d(180, -90, 40), Look: target),
        };
        var path = new CameraPath(keys, 9.0, Easing.Linear, PathTiming.Auto, 55);
        for (var t = 0.0; t <= 9.0; t += 0.25)
        {
            var sample = path.Sample(t);
            var (yaw, pitch) = CameraPath.Facing(sample.Position, target);
            Assert.Equal(0.0, CameraPath.Unwrap(yaw, sample.Yaw) - yaw, 6);
            Assert.Equal(pitch, sample.Pitch, 6);
            Assert.Equal(55.0, sample.Fov, 9);
        }
    }

    [Fact]
    public void Look_keys_mixed_with_angle_keys_become_angles()
    {
        var keys = new[]
        {
            new PathKey(Origin, Look: Origin + new Vec3d(0, 100, 0)), // north, level
            new PathKey(Origin + new Vec3d(0, 50, 0), Yaw: 90, Pitch: -10),
        };
        var path = new CameraPath(keys, 4.0, Easing.Linear, PathTiming.Auto, 60);
        Assert.Equal(0.0, path.Sample(0).Yaw, 6);
        Assert.Equal(0.0, path.Sample(0).Pitch, 6);
        Assert.Equal(90.0, path.Sample(4).Yaw, 6);
        Assert.Equal(-10.0, path.Sample(4).Pitch, 6);
    }

    [Fact]
    public void Field_of_view_follows_the_keys_and_defaults_to_the_shot()
    {
        var keys = new[] { new PathKey(Origin, Yaw: 0, Fov: 70), new PathKey(Origin + new Vec3d(0, 10, 0), Yaw: 0, Fov: 40), new PathKey(Origin + new Vec3d(0, 20, 0), Yaw: 0) };
        var path = new CameraPath(keys, 2.0, Easing.Linear, PathTiming.Uniform, 50);
        Assert.Equal(70.0, path.Sample(0).Fov, 9);
        Assert.Equal(40.0, path.Sample(1).Fov, 9);
        Assert.Equal(50.0, path.Sample(2).Fov, 9);
        Assert.InRange(path.Sample(0.5).Fov, 40, 70);
    }

    [Fact]
    public void Sampling_is_deterministic_and_clamped_to_the_shot()
    {
        var keys = new[] { new PathKey(Origin, Yaw: 5, Pitch: -3), new PathKey(Origin + new Vec3d(40, 40, 5), Yaw: 50, Pitch: 4), new PathKey(Origin + new Vec3d(90, 10, 8), Yaw: 120, Pitch: 0) };
        var a = new CameraPath(keys, 6.0, new Easing(EaseKind.Ramp, 0.2, 0.2), PathTiming.Auto, 60);
        var b = new CameraPath(keys, 6.0, new Easing(EaseKind.Ramp, 0.2, 0.2), PathTiming.Auto, 60);
        for (var i = 0; i <= 360; i++)
        {
            Assert.Equal(a.Sample(i / 60.0), b.Sample(i / 60.0));
        }

        Assert.Equal(a.Sample(0), a.Sample(-1));
        Assert.Equal(a.Sample(6), a.Sample(60));
    }

    [Fact]
    public void Inconsistent_keys_are_refused()
    {
        Assert.Throws<ArgumentException>(() => new CameraPath([], 1, Easing.Linear, PathTiming.Auto, 60));
        Assert.Throws<ArgumentException>(() => new CameraPath([new PathKey(Origin), new PathKey(Origin + new Vec3d(1, 0, 0), Yaw: 0)], 1, Easing.Linear, PathTiming.Auto, 60));
        Assert.Throws<ArgumentException>(() => new CameraPath([new PathKey(Origin, Yaw: 0, Time: 0.5), new PathKey(Origin + new Vec3d(10, 0, 0), Yaw: 0, Time: 1)], 2, Easing.Linear, PathTiming.Keys, 60));
        Assert.Throws<ArgumentException>(() => new CameraPath([new PathKey(Origin, Yaw: 0, Time: 0), new PathKey(Origin + new Vec3d(10, 0, 0), Yaw: 0, Time: 3)], 2, Easing.Linear, PathTiming.Keys, 60));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraPath([new PathKey(Origin, Yaw: 0)], 0, Easing.Linear, PathTiming.Auto, 60));
    }

    [Fact]
    public void Ground_relative_keys_take_their_height_from_the_ground_and_the_clearance_check_finds_dips()
    {
        // Ground rises 1 m per 10 m east of the anchor.
        double? Ground(double x, double y) => Origin.Z + ((x - Origin.X) / 10.0);
        var shot = new Shot
        {
            Id = "s",
            Start = "holtburg",
            Duration = 4,
            Keys =
            [
                new ShotKey { Position = new Vec3d(0, 0, 5), PositionAboveGround = true, Yaw = 90 },
                new ShotKey { Position = new Vec3d(100, 0, 5), PositionAboveGround = true, Yaw = 90 },
                new ShotKey { Position = new Vec3d(200, 0, 2), Yaw = 90 }, // 2 m above the anchor, 18 m under the ground there
            ],
        };
        var path = ShotPaths.Build(shot, Origin, Ground);
        Assert.Equal(Origin.Z + 5, path.Keys[0].Position.Z, 9);
        Assert.Equal(Origin.Z + 15, path.Keys[1].Position.Z, 9);
        Assert.Equal(Origin.Z + 2, path.Keys[2].Position.Z, 9);
        var report = ShotPaths.CheckClearance(path, Ground, 3.0);
        Assert.False(report.Clear);
        Assert.True(report.MinClearance < -15, $"min clearance {report.MinClearance}");
        Assert.Equal(4.0, report.AtTime, 6);

        var safe = ShotPaths.CheckClearance(ShotPaths.Build(shot with { Keys = shot.Keys.Take(2).ToArray() }, Origin, Ground), Ground, 3.0);
        Assert.True(safe.Clear, $"min clearance {safe.MinClearance}");
        Assert.Equal(5.0, safe.MinClearance, 6);
    }
}
