namespace Lifestone.Client.Cinematic;

/// <summary>
/// A centripetal Catmull-Rom spline through points (alpha 0.5, Barry–Goldman evaluation): it passes through every
/// point, has no cusps or self-intersections inside a segment, and overshoots far less than the uniform variant when the
/// spacing of the points varies — the usual choice for camera paths.
/// </summary>
/// <remarks>
/// The parameter is the segment index plus the fraction along it: s in [0, n − 1] for n points. The end segments use
/// phantom points mirrored through the end points (P₋₁ = 2P₀ − P₁, Pₙ = 2Pₙ₋₁ − Pₙ₋₂), so two points give a straight
/// line at the ends' own spacing. Coincident points are allowed (a zero-length segment evaluates to its point).
/// </remarks>
public sealed class CatmullRomSpline
{
    /// <summary>The knot exponent: 0 uniform, 0.5 centripetal, 1 chordal.</summary>
    public const double Alpha = 0.5;

    private const double MinKnotStep = 1e-4;
    private readonly Vec3d[] _points;

    public CatmullRomSpline(IReadOnlyList<Vec3d> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
        {
            throw new ArgumentException("A spline needs at least one point.", nameof(points));
        }

        _points = points.ToArray();
    }

    /// <summary>The points the spline passes through.</summary>
    public IReadOnlyList<Vec3d> Points => _points;

    /// <summary>The largest parameter value (n − 1).</summary>
    public double MaxParameter => _points.Length - 1;

    /// <summary>The point at parameter <paramref name="s"/> (clamped to [0, n − 1]).</summary>
    public Vec3d Evaluate(double s)
    {
        var n = _points.Length;
        if (n == 1)
        {
            return _points[0];
        }

        s = Math.Clamp(s, 0.0, n - 1);
        var i = Math.Min((int)Math.Floor(s), n - 2);
        var f = s - i;
        var p1 = _points[i];
        var p2 = _points[i + 1];
        if (f <= 0.0)
        {
            return p1;
        }

        if (f >= 1.0)
        {
            return p2;
        }

        var p0 = i > 0 ? _points[i - 1] : (2.0 * p1) - p2;
        var p3 = i + 2 < n ? _points[i + 2] : (2.0 * p2) - p1;
        if (Vec3d.Distance(p1, p2) < 1e-12)
        {
            return p1;
        }

        var t0 = 0.0;
        var t1 = t0 + KnotStep(p0, p1);
        var t2 = t1 + KnotStep(p1, p2);
        var t3 = t2 + KnotStep(p2, p3);
        var t = t1 + (f * (t2 - t1));

        var a1 = Mix(p0, p1, t0, t1, t);
        var a2 = Mix(p1, p2, t1, t2, t);
        var a3 = Mix(p2, p3, t2, t3, t);
        var b1 = Mix(a1, a2, t0, t2, t);
        var b2 = Mix(a2, a3, t1, t3, t);
        return Mix(b1, b2, t1, t2, t);
    }

    private static double KnotStep(Vec3d a, Vec3d b) => Math.Max(Math.Pow(Vec3d.Distance(a, b), Alpha), MinKnotStep);

    private static Vec3d Mix(Vec3d a, Vec3d b, double ta, double tb, double t) =>
        (a * ((tb - t) / (tb - ta))) + (b * ((t - ta) / (tb - ta)));
}

/// <summary>
/// A monotone piecewise-cubic Hermite interpolant (PCHIP: Fritsch–Carlson shape preservation with the Fritsch–Butland
/// interior slopes): it passes through every knot, is C¹, and never overshoots — between two knots it stays between
/// their values, and it is flat at a local extremum. Used for camera angles, field of view and time-to-parameter maps,
/// where an overshoot would show as the camera swinging past its mark and back.
/// </summary>
public sealed class MonotoneCubic
{
    private readonly double[] _x;
    private readonly double[] _y;
    private readonly double[] _m;

    /// <param name="x">Knot positions, strictly increasing.</param>
    /// <param name="y">Values at the knots.</param>
    public MonotoneCubic(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        if (x.Count == 0 || x.Count != y.Count)
        {
            throw new ArgumentException("Knots and values must be non-empty and of equal length.");
        }

        for (var i = 1; i < x.Count; i++)
        {
            if (!(x[i] > x[i - 1]))
            {
                throw new ArgumentException($"Knot positions must be strictly increasing (x[{i}] = {x[i]} after {x[i - 1]}).", nameof(x));
            }
        }

        _x = x.ToArray();
        _y = y.ToArray();
        _m = Slopes(_x, _y);
    }

    /// <summary>Knots at 0, 1, …, n − 1 (a key-index parameter).</summary>
    public static MonotoneCubic Uniform(IReadOnlyList<double> y)
    {
        ArgumentNullException.ThrowIfNull(y);
        return new MonotoneCubic(Enumerable.Range(0, y.Count).Select(i => (double)i).ToArray(), y);
    }

    /// <summary>The value at <paramref name="x"/> (clamped to the knot range).</summary>
    public double Evaluate(double x)
    {
        var n = _x.Length;
        if (n == 1 || x <= _x[0])
        {
            return _y[0];
        }

        if (x >= _x[n - 1])
        {
            return _y[n - 1];
        }

        var k = Array.BinarySearch(_x, x);
        if (k >= 0)
        {
            return _y[k];
        }

        k = ~k - 1;
        var h = _x[k + 1] - _x[k];
        var t = (x - _x[k]) / h;
        var t2 = t * t;
        var t3 = t2 * t;
        var h00 = (2 * t3) - (3 * t2) + 1;
        var h10 = t3 - (2 * t2) + t;
        var h01 = (-2 * t3) + (3 * t2);
        var h11 = t3 - t2;
        return (h00 * _y[k]) + (h10 * h * _m[k]) + (h01 * _y[k + 1]) + (h11 * h * _m[k + 1]);
    }

    private static double[] Slopes(double[] x, double[] y)
    {
        var n = x.Length;
        var m = new double[n];
        if (n < 2)
        {
            return m;
        }

        var d = new double[n - 1];
        var h = new double[n - 1];
        for (var k = 0; k < n - 1; k++)
        {
            h[k] = x[k + 1] - x[k];
            d[k] = (y[k + 1] - y[k]) / h[k];
        }

        m[0] = d[0];
        m[n - 1] = d[n - 2];
        for (var k = 1; k < n - 1; k++)
        {
            if (d[k - 1] * d[k] <= 0)
            {
                m[k] = 0; // a local extremum (or a flat stretch): flat, so no overshoot
                continue;
            }

            var w1 = (2 * h[k]) + h[k - 1];
            var w2 = h[k] + (2 * h[k - 1]);
            m[k] = (w1 + w2) / ((w1 / d[k - 1]) + (w2 / d[k]));
        }

        // Fritsch–Carlson: limit the end slopes so the end intervals stay monotone too.
        for (var k = 0; k < n - 1; k++)
        {
            if (d[k] == 0)
            {
                m[k] = 0;
                m[k + 1] = 0;
                continue;
            }

            var a = m[k] / d[k];
            var b = m[k + 1] / d[k];
            if (a < 0)
            {
                m[k] = 0;
                a = 0;
            }

            if (b < 0)
            {
                m[k + 1] = 0;
                b = 0;
            }

            var s = (a * a) + (b * b);
            if (s > 9)
            {
                var tau = 3 / Math.Sqrt(s);
                m[k] = tau * a * d[k];
                m[k + 1] = tau * b * d[k];
            }
        }

        return m;
    }
}
