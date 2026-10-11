using Lifestone.Client.World;

namespace Lifestone.Client.Cinematic;

/// <summary>How shot time maps onto a camera path (<see cref="CameraPath"/>).</summary>
public enum PathTiming
{
    /// <summary><see cref="Keys"/> when every key has a time, else <see cref="Arc"/> for a moving camera, else <see cref="Uniform"/>.</summary>
    Auto,

    /// <summary>Constant speed along the path (arc length), shaped by the shot's easing. The default for moving shots.</summary>
    Arc,

    /// <summary>Equal time per key segment, shaped by the easing (pans and tilts from a fixed point).</summary>
    Uniform,

    /// <summary>Each key is reached at its own time (<see cref="PathKey.Time"/>); a monotone cubic smooths between them.</summary>
    Keys,
}

/// <summary>One camera key of a path: a global position (AC axes, metres) and either a heading and pitch or a look-at point.</summary>
/// <param name="Position">Eye position (global metres: +X east, +Y north, +Z up).</param>
/// <param name="Yaw">Compass heading in degrees (0 north, 90 east), or null when <paramref name="Look"/> gives the facing.</param>
/// <param name="Pitch">Pitch in degrees (positive looks up); null = 0 with a yaw, or from <paramref name="Look"/>.</param>
/// <param name="Look">A point to look at (global metres), or null.</param>
/// <param name="Fov">Vertical field of view in degrees, or null for the shot's.</param>
/// <param name="Time">Seconds from the shot start at which the key is reached (<see cref="PathTiming.Keys"/>), or null.</param>
public readonly record struct PathKey(Vec3d Position, double? Yaw = null, double? Pitch = null, Vec3d? Look = null, double? Fov = null, double? Time = null);

/// <summary>A camera at one moment: global position, compass heading, pitch and vertical field of view (degrees).</summary>
public readonly record struct CameraSample(Vec3d Position, double Yaw, double Pitch, double Fov);

/// <summary>
/// A deterministic camera move: a centripetal Catmull-Rom path through the keys' positions, the facing interpolated along
/// it (a look-at spline when every key looks at a point, else monotone heading and pitch curves), the field of view, and
/// a mapping from shot time to the path that follows the shot's <see cref="Easing"/> and <see cref="PathTiming"/>.
/// </summary>
/// <remarks>
/// <para>Engine-agnostic and pure: <see cref="Sample"/> depends only on the time, so a frame-stepped recording
/// (Godot's Movie Maker at a fixed frame rate) is exactly reproducible.</para>
/// <para>Headings are unwrapped key to key (each within 180° of the previous one), so a pan from 350° to 10° turns
/// 20° east rather than 340° west. A look-at path keeps the target framed even where the eye curves; a heading path
/// is the classic keyed pan.</para>
/// </remarks>
public sealed class CameraPath
{
    /// <summary>Arc-length samples per path segment.</summary>
    public const int ArcSamplesPerSegment = 64;

    /// <summary>Paths shorter than this (metres) count as a fixed camera (<see cref="PathTiming.Uniform"/> under <see cref="PathTiming.Auto"/>).</summary>
    public const double FixedCameraLength = 0.5;

    private readonly CatmullRomSpline _positions;
    private readonly CatmullRomSpline? _looks;
    private readonly MonotoneCubic? _yaw;
    private readonly MonotoneCubic? _pitch;
    private readonly MonotoneCubic _fov;
    private readonly MonotoneCubic? _keyTimes;
    private readonly double[] _arcS;
    private readonly double[] _arcLength;
    private readonly Easing _ease;

    /// <param name="keys">At least one key.</param>
    /// <param name="duration">Shot length in seconds (positive).</param>
    /// <param name="ease">Easing of the shot's progress.</param>
    /// <param name="timing">How time maps onto the path.</param>
    /// <param name="defaultFov">Field of view for keys without one.</param>
    /// <exception cref="ArgumentException">Inconsistent keys (see the messages).</exception>
    public CameraPath(IReadOnlyList<PathKey> keys, double duration, Easing ease, PathTiming timing, double defaultFov)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            throw new ArgumentException("A camera path needs at least one key.", nameof(keys));
        }

        if (!(duration > 0) || !double.IsFinite(duration))
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "The duration must be a positive number of seconds.");
        }

        Keys = keys.ToArray();
        Duration = duration;
        _ease = ease;
        _positions = new CatmullRomSpline(Keys.Select(k => k.Position).ToArray());

        // Facing: a look-at spline when every key looks at a point and none sets angles; else angles per key.
        var allLook = Keys.All(k => k.Look is not null && k.Yaw is null && k.Pitch is null);
        if (allLook)
        {
            _looks = new CatmullRomSpline(Keys.Select(k => k.Look!.Value).ToArray());
        }
        else
        {
            var yaws = new double[Keys.Count];
            var pitches = new double[Keys.Count];
            for (var i = 0; i < Keys.Count; i++)
            {
                var k = Keys[i];
                if (k.Yaw is { } yaw)
                {
                    yaws[i] = yaw;
                    pitches[i] = k.Pitch ?? 0.0;
                }
                else if (k.Look is { } look)
                {
                    (yaws[i], pitches[i]) = Facing(k.Position, look);
                    pitches[i] = k.Pitch ?? pitches[i];
                }
                else
                {
                    throw new ArgumentException($"Key {i} has neither a yaw nor a look-at point.", nameof(keys));
                }

                if (i > 0)
                {
                    yaws[i] = Unwrap(yaws[i - 1], yaws[i]);
                }
            }

            _yaw = MonotoneCubic.Uniform(yaws);
            _pitch = MonotoneCubic.Uniform(pitches);
        }

        _fov = MonotoneCubic.Uniform(Keys.Select(k => k.Fov ?? defaultFov).ToArray());

        // Arc-length table over the spline parameter.
        var segments = Math.Max(1, Keys.Count - 1);
        var samples = Keys.Count == 1 ? 1 : segments * ArcSamplesPerSegment;
        _arcS = new double[samples + 1];
        _arcLength = new double[samples + 1];
        var previous = _positions.Evaluate(0);
        for (var i = 1; i <= samples; i++)
        {
            var s = Keys.Count == 1 ? 0.0 : (double)i / ArcSamplesPerSegment;
            var p = _positions.Evaluate(s);
            _arcS[i] = s;
            _arcLength[i] = _arcLength[i - 1] + Vec3d.Distance(previous, p);
            previous = p;
        }

        Length = _arcLength[^1];
        Timing = timing switch
        {
            PathTiming.Auto when Keys.Count > 1 && Keys.All(k => k.Time is not null) => PathTiming.Keys,
            PathTiming.Auto => Length > FixedCameraLength ? PathTiming.Arc : PathTiming.Uniform,
            PathTiming.Arc when Length <= FixedCameraLength => PathTiming.Uniform,
            _ => timing,
        };

        if (Timing == PathTiming.Keys && Keys.Count > 1)
        {
            var times = new double[Keys.Count];
            for (var i = 0; i < Keys.Count; i++)
            {
                times[i] = Keys[i].Time ?? throw new ArgumentException($"Key {i} has no time, but the path is timed by its keys.", nameof(keys));
            }

            if (Math.Abs(times[0]) > 1e-9)
            {
                throw new ArgumentException($"The first key must be at t = 0 (it is at {times[0]}).", nameof(keys));
            }

            if (times[^1] > duration + 1e-9)
            {
                throw new ArgumentException($"The last key (t = {times[^1]}) is after the end of the shot ({duration} s).", nameof(keys));
            }

            _keyTimes = new MonotoneCubic(times, Enumerable.Range(0, Keys.Count).Select(i => (double)i).ToArray());
        }
    }

    /// <summary>The keys as given.</summary>
    public IReadOnlyList<PathKey> Keys { get; }

    /// <summary>Shot length in seconds.</summary>
    public double Duration { get; }

    /// <summary>Length of the eye's path in metres.</summary>
    public double Length { get; }

    /// <summary>The timing in force (<see cref="PathTiming.Auto"/> resolved).</summary>
    public PathTiming Timing { get; }

    /// <summary>The path parameter (key index plus fraction) at shot time <paramref name="t"/> (clamped to the shot).</summary>
    public double ParameterAt(double t)
    {
        var n = Keys.Count;
        if (n == 1)
        {
            return 0.0;
        }

        var u = _ease.Evaluate(t / Duration);
        return Timing switch
        {
            PathTiming.Keys => _keyTimes!.Evaluate(u * Duration),
            PathTiming.Arc => ParameterAtLength(u * Length),
            _ => u * (n - 1),
        };
    }

    /// <summary>The camera at shot time <paramref name="t"/> (seconds from the shot start; clamped to the shot).</summary>
    public CameraSample Sample(double t)
    {
        var s = ParameterAt(t);
        var position = _positions.Evaluate(s);
        double yaw, pitch;
        if (_looks is not null)
        {
            (yaw, pitch) = Facing(position, _looks.Evaluate(s));
        }
        else
        {
            yaw = _yaw!.Evaluate(s);
            pitch = _pitch!.Evaluate(s);
        }

        return new CameraSample(position, Wrap360(yaw), Math.Clamp(pitch, -89.0, 89.0), _fov.Evaluate(s));
    }

    /// <summary>Compass heading and pitch (degrees) from <paramref name="eye"/> towards <paramref name="target"/>.</summary>
    public static (double Yaw, double Pitch) Facing(Vec3d eye, Vec3d target)
    {
        var d = target - eye;
        var horizontal = d.HorizontalLength;
        if (horizontal < 1e-9 && Math.Abs(d.Z) < 1e-9)
        {
            return (0.0, 0.0);
        }

        var yaw = horizontal < 1e-9 ? 0.0 : AcCoordinates.HeadingOf(d.X, d.Y);
        return (yaw, Math.Atan2(d.Z, horizontal) * 180.0 / Math.PI);
    }

    /// <summary><paramref name="angle"/> moved by whole turns to within 180° of <paramref name="previous"/>.</summary>
    public static double Unwrap(double previous, double angle)
    {
        var d = (angle - previous) % 360.0;
        if (d > 180.0)
        {
            d -= 360.0;
        }
        else if (d < -180.0)
        {
            d += 360.0;
        }

        return previous + d;
    }

    /// <summary>An angle in [0, 360).</summary>
    public static double Wrap360(double degrees)
    {
        degrees %= 360.0;
        return degrees < 0 ? degrees + 360.0 : degrees + 0.0;
    }

    private double ParameterAtLength(double length)
    {
        if (length <= 0)
        {
            return 0.0;
        }

        if (length >= Length)
        {
            return Keys.Count - 1;
        }

        var i = Array.BinarySearch(_arcLength, length);
        if (i >= 0)
        {
            return _arcS[i];
        }

        i = ~i;
        var span = _arcLength[i] - _arcLength[i - 1];
        var f = span > 0 ? (length - _arcLength[i - 1]) / span : 0.0;
        return _arcS[i - 1] + (f * (_arcS[i] - _arcS[i - 1]));
    }
}
