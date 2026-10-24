using Lifestone.Client.Cinematic;

namespace Lifestone.Client.Tests.Cinematic;

public sealed class EasingAndSplineTests
{
    public static TheoryData<string> AllEasings => new() { "linear", "in", "out", "inOut", "sine", "smoother", "ramp:0.2", "ramp:0.3,0", "ramp:0,0.25", "ramp:0.5,0.5" };

    [Theory]
    [MemberData(nameof(AllEasings))]
    public void Every_easing_runs_from_0_to_1_monotonically(string name)
    {
        Assert.True(Easing.TryParse(name, out var ease, out var error), error);
        Assert.Equal(0.0, ease.Evaluate(0.0), 12);
        Assert.Equal(1.0, ease.Evaluate(1.0), 12);
        var previous = 0.0;
        for (var i = 1; i <= 1000; i++)
        {
            var v = ease.Evaluate(i / 1000.0);
            Assert.True(v >= previous - 1e-12, $"{name} decreases at {i / 1000.0}");
            Assert.InRange(v, 0.0, 1.0 + 1e-12);
            previous = v;
        }

        Assert.Equal(0.0, ease.Evaluate(-3), 12);
        Assert.Equal(1.0, ease.Evaluate(7), 12);
    }

    [Fact]
    public void A_ramp_glides_at_constant_speed_between_its_ramps_and_its_speed_is_continuous()
    {
        Assert.True(Easing.TryParse("ramp:0.2,0.3", out var ease, out _));
        const double h = 1e-6;
        double Speed(double u) => (ease.Evaluate(u + h) - ease.Evaluate(u - h)) / (2 * h);
        var glide = 1.0 / (1.0 - 0.1 - 0.15);
        foreach (var u in new[] { 0.25, 0.4, 0.55, 0.69 })
        {
            Assert.Equal(glide, Speed(u), 6);
        }

        Assert.Equal(0.0, Speed(1e-5), 3); // starts at rest
        Assert.Equal(0.0, Speed(1 - 1e-5), 3); // ends at rest
        Assert.Equal(Speed(0.2 - 1e-4), Speed(0.2 + 1e-4), 2); // no speed jump at the ramp's end
        Assert.Equal(Speed(0.7 - 1e-4), Speed(0.7 + 1e-4), 2);
    }

    [Fact]
    public void A_ramp_with_no_out_ramp_leaves_moving()
    {
        Assert.True(Easing.TryParse("ramp:0.3,0", out var ease, out _));
        var speedAtEnd = (ease.Evaluate(1.0) - ease.Evaluate(1.0 - 1e-4)) / 1e-4;
        Assert.True(speedAtEnd > 1.1, $"speed at the end {speedAtEnd}");
    }

    [Theory]
    [InlineData("bounce")]
    [InlineData("ramp:0.7")]
    [InlineData("ramp:0.6,0.6")]
    [InlineData("ramp:x")]
    [InlineData("linear:2")]
    [InlineData("ramp:0.1,0.1,0.1")]
    public void Bad_easings_are_refused_with_a_message(string name)
    {
        Assert.False(Easing.TryParse(name, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void An_empty_easing_is_linear_and_names_are_case_insensitive()
    {
        Assert.True(Easing.TryParse(null, out var none, out _));
        Assert.Equal(EaseKind.Linear, none.Kind);
        Assert.True(Easing.TryParse("InOut", out var inOut, out _));
        Assert.Equal(EaseKind.InOut, inOut.Kind);
        Assert.True(Easing.TryParse("ramp", out var ramp, out _));
        Assert.Equal(new Easing(EaseKind.Ramp, 0.2, 0.2), ramp);
    }

    [Fact]
    public void Catmull_rom_passes_through_every_point_and_two_points_make_a_straight_line()
    {
        var points = new[] { new Vec3d(0, 0, 0), new Vec3d(10, 5, 2), new Vec3d(30, -4, 8), new Vec3d(31, 20, 0) };
        var spline = new CatmullRomSpline(points);
        for (var i = 0; i < points.Length; i++)
        {
            Assert.True(Vec3d.Distance(points[i], spline.Evaluate(i)) < 1e-9, $"point {i}");
        }

        var line = new CatmullRomSpline([new Vec3d(0, 0, 0), new Vec3d(100, 50, 10)]);
        for (var s = 0.0; s <= 1.0; s += 0.125)
        {
            var p = line.Evaluate(s);
            Assert.True(Vec3d.Distance(p, new Vec3d(100 * s, 50 * s, 10 * s)) < 1e-9, $"s = {s}: {p}");
        }
    }

    [Fact]
    public void Catmull_rom_survives_coincident_points_and_clamps_its_parameter()
    {
        var spline = new CatmullRomSpline([new Vec3d(1, 1, 1), new Vec3d(1, 1, 1), new Vec3d(5, 1, 1), new Vec3d(5, 1, 1)]);
        for (var s = -1.0; s <= 4.0; s += 0.05)
        {
            var p = spline.Evaluate(s);
            Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z), $"s = {s}");
            Assert.InRange(p.X, 1 - 1e-6, 5 + 1e-6);
        }

        Assert.Equal(new Vec3d(1, 1, 1), spline.Evaluate(-5));
        Assert.Equal(new Vec3d(5, 1, 1), spline.Evaluate(99));
        Assert.Equal(new Vec3d(7, 8, 9), new CatmullRomSpline([new Vec3d(7, 8, 9)]).Evaluate(0.5));
    }

    [Fact]
    public void Centripetal_catmull_rom_does_not_loop_on_unevenly_spaced_points()
    {
        // A short segment between two long ones: the uniform variant overshoots into a loop here; the centripetal one
        // stays between its ends along x.
        var spline = new CatmullRomSpline([new Vec3d(0, 0, 0), new Vec3d(100, 0, 0), new Vec3d(101, 1, 0), new Vec3d(101, 100, 0)]);
        for (var s = 1.0; s <= 2.0; s += 0.01)
        {
            var p = spline.Evaluate(s);
            Assert.InRange(p.X, 99.5, 101.6);
            Assert.InRange(p.Y, -0.6, 1.6);
        }
    }

    [Fact]
    public void Monotone_cubic_hits_its_knots_reproduces_lines_and_never_overshoots()
    {
        var line = new MonotoneCubic([0, 1, 3, 4], [2, 4, 8, 10]);
        for (var x = 0.0; x <= 4.0; x += 0.1)
        {
            Assert.Equal(2 + (2 * x), line.Evaluate(x), 9);
        }

        // A step: values must stay within each interval's ends.
        double[] xs = [0, 1, 2, 3, 4, 5];
        double[] ys = [0, 0, 0, 10, 10, 10];
        var step = new MonotoneCubic(xs, ys);
        for (var i = 0; i < xs.Length; i++)
        {
            Assert.Equal(ys[i], step.Evaluate(xs[i]), 12);
        }

        for (var x = 0.0; x <= 5.0; x += 0.01)
        {
            Assert.InRange(step.Evaluate(x), -1e-12, 10 + 1e-12);
        }

        // Flat at a local maximum (the camera stops pitching there instead of swinging past the mark).
        var peak = new MonotoneCubic([0, 1, 2], [0, 30, 0]);
        Assert.Equal(30, peak.Evaluate(1), 12);
        Assert.True(peak.Evaluate(0.98) <= 30 && peak.Evaluate(1.02) <= 30);
        Assert.InRange((peak.Evaluate(1.001) - peak.Evaluate(0.999)) / 0.002, -0.1, 0.1);
    }

    [Fact]
    public void Monotone_cubic_refuses_knots_that_do_not_increase()
    {
        Assert.Throws<ArgumentException>(() => new MonotoneCubic([0, 1, 1], [0, 1, 2]));
        Assert.Throws<ArgumentException>(() => new MonotoneCubic([0, 1], [0]));
        Assert.Equal(5, new MonotoneCubic([2], [5]).Evaluate(-10));
    }

    [Theory]
    [InlineData(0.8, 0.1, 0.3)]
    [InlineData(0.2, 0.7, 0.5)]
    [InlineData(0.5, 0.5, 1.0)]
    public void Time_lapse_spans_run_forwards_across_midnight(double start, double end, double span)
    {
        Assert.Equal(span, TimeLapse.Span(start, end), 12);
        Assert.Equal(span * 7620.0 / 8.0, TimeLapse.Speed(start, end, 8.0, 7620.0), 9);
    }
}
