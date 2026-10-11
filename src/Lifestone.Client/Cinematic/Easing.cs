using System.Globalization;

namespace Lifestone.Client.Cinematic;

/// <summary>The shape of an easing curve (<see cref="Easing"/>).</summary>
public enum EaseKind
{
    /// <summary>Constant speed.</summary>
    Linear,

    /// <summary>Quadratic ease-in: starts at rest.</summary>
    In,

    /// <summary>Quadratic ease-out: ends at rest.</summary>
    Out,

    /// <summary>Smoothstep (3u² − 2u³): starts and ends at rest.</summary>
    InOut,

    /// <summary>Half a cosine: starts and ends at rest, gentler than smoothstep in the middle.</summary>
    Sine,

    /// <summary>Smootherstep (6u⁵ − 15u⁴ + 10u³): rest at both ends with zero acceleration there too.</summary>
    Smoother,

    /// <summary>
    /// Trapezoidal speed: accelerates linearly over the first <see cref="Easing.RampIn"/> of the shot, holds a constant
    /// speed, decelerates over the last <see cref="Easing.RampOut"/>. The camera-operator move: a dolly that eases
    /// into and out of a steady glide (either ramp may be 0, so a shot can leave moving into a crossfade).
    /// </summary>
    Ramp,
}

/// <summary>
/// An easing curve on normalised shot time: maps u in [0, 1] to progress in [0, 1], monotonically, with e(0) = 0 and
/// e(1) = 1. Written in shot lists as <c>linear</c>, <c>in</c>, <c>out</c>, <c>inOut</c>, <c>sine</c>,
/// <c>smoother</c>, <c>ramp:A</c> (both ramps A) or <c>ramp:A,B</c> (ramp in A, ramp out B; fractions of the shot,
/// A + B ≤ 1).
/// </summary>
public readonly record struct Easing(EaseKind Kind, double RampIn = 0.0, double RampOut = 0.0)
{
    public static Easing Linear => new(EaseKind.Linear);

    /// <summary>Evaluates the curve; <paramref name="u"/> is clamped to [0, 1].</summary>
    public double Evaluate(double u)
    {
        u = Math.Clamp(u, 0.0, 1.0);
        switch (Kind)
        {
            case EaseKind.In:
                return u * u;
            case EaseKind.Out:
                return 1.0 - ((1.0 - u) * (1.0 - u));
            case EaseKind.InOut:
                return u * u * (3.0 - (2.0 * u));
            case EaseKind.Sine:
                return 0.5 - (0.5 * Math.Cos(Math.PI * u));
            case EaseKind.Smoother:
                return u * u * u * ((u * ((u * 6.0) - 15.0)) + 10.0);
            case EaseKind.Ramp:
                return EvaluateRamp(u, RampIn, RampOut);
            default:
                return u;
        }
    }

    /// <summary>
    /// Trapezoidal speed profile with ramp fractions <paramref name="a"/> (in) and <paramref name="b"/> (out): the speed
    /// rises linearly to v over [0, a], stays v, falls to 0 over [1 − b, 1]; v = 1 / (1 − a/2 − b/2) so the distance is 1.
    /// </summary>
    private static double EvaluateRamp(double u, double a, double b)
    {
        var v = 1.0 / (1.0 - (a / 2.0) - (b / 2.0));
        if (a > 0 && u < a)
        {
            return v * u * u / (2.0 * a);
        }

        if (b > 0 && u > 1.0 - b)
        {
            var w = 1.0 - u;
            return 1.0 - (v * w * w / (2.0 * b));
        }

        return v * ((a / 2.0) + (u - a));
    }

    /// <summary>Parses a shot list's easing name (see the type remarks). Case-insensitive.</summary>
    public static bool TryParse(string? text, out Easing easing, out string? error)
    {
        easing = Linear;
        error = null;
        var spec = (text ?? string.Empty).Trim();
        if (spec.Length == 0)
        {
            return true;
        }

        var colon = spec.IndexOf(':', StringComparison.Ordinal);
        var name = (colon < 0 ? spec : spec[..colon]).ToLowerInvariant();
        if (name != "ramp" && colon >= 0)
        {
            error = $"easing '{spec}': only 'ramp' takes parameters";
            return false;
        }

        switch (name)
        {
            case "linear":
                easing = Linear;
                return true;
            case "in":
                easing = new Easing(EaseKind.In);
                return true;
            case "out":
                easing = new Easing(EaseKind.Out);
                return true;
            case "inout" or "smooth" or "smoothstep":
                easing = new Easing(EaseKind.InOut);
                return true;
            case "sine":
                easing = new Easing(EaseKind.Sine);
                return true;
            case "smoother":
                easing = new Easing(EaseKind.Smoother);
                return true;
            case "ramp":
                return TryParseRamp(colon < 0 ? "0.2" : spec[(colon + 1)..], out easing, out error);
            default:
                error = $"easing '{spec}' is not one of linear, in, out, inOut, sine, smoother, ramp:A[,B]";
                return false;
        }
    }

    private static bool TryParseRamp(string args, out Easing easing, out string? error)
    {
        easing = Linear;
        error = null;
        var parts = args.Split(',', StringSplitOptions.TrimEntries);
        var values = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || !double.IsFinite(values[i]) || values[i] < 0 || values[i] > 1)
            {
                error = $"easing 'ramp:{args}': ramp fractions must be numbers in [0, 1]";
                return false;
            }
        }

        if (values.Length is not (1 or 2))
        {
            error = $"easing 'ramp:{args}': expected ramp:A or ramp:A,B";
            return false;
        }

        var a = values[0];
        var b = values.Length == 2 ? values[1] : values[0];
        if (a + b > 1.0 + 1e-12)
        {
            error = $"easing 'ramp:{args}': the two ramps together exceed the shot (A + B > 1)";
            return false;
        }

        easing = new Easing(EaseKind.Ramp, a, b);
        return true;
    }

    public override string ToString() => Kind == EaseKind.Ramp
        ? string.Create(CultureInfo.InvariantCulture, $"ramp:{RampIn:0.###},{RampOut:0.###}")
        : Kind.ToString();
}
