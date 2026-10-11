namespace Lifestone.Client.Cinematic;

/// <summary>
/// One key of an orbit camera (the spell viewer's): heading looked along, pitch, distance from the focus, and which
/// point it orbits — a named point of the stage (resolved every frame, so a key can follow a flying projectile) plus an
/// offset in metres (AC axes).
/// </summary>
/// <param name="Time">Seconds from the shot start, or null to spread the keys evenly over the shot.</param>
/// <param name="Yaw">Compass heading the camera looks along (degrees).</param>
/// <param name="Pitch">Pitch in degrees (negative looks down).</param>
/// <param name="Distance">Metres from the focus point.</param>
/// <param name="Focus">A stage point name (the spell viewer knows <c>caster</c>, <c>target</c>, <c>mid</c>, <c>hands</c>, <c>projectile</c>).</param>
/// <param name="Offset">Added to the focus point (metres).</param>
/// <param name="Fov">Vertical field of view, or null for the shot's.</param>
public readonly record struct OrbitKey(double? Time, double Yaw, double Pitch, double Distance, string Focus, Vec3d Offset, double? Fov = null);

/// <summary>An orbit camera at one moment.</summary>
public readonly record struct OrbitSample(double Yaw, double Pitch, double Distance, Vec3d Focus, double Fov);

/// <summary>
/// A keyed orbit camera: heading, pitch, distance and field of view follow monotone cubics through the keys (no
/// overshoot), and the focus point blends from one key's point to the next with a smoothstep, each key's point resolved
/// at the current moment. With <see cref="SmoothSeconds"/> the result is also low-passed (an exponential follow with a
/// fixed step, as the showcase director does), so a camera that tracks a projectile does not jitter.
/// </summary>
public sealed class OrbitTrack
{
    private readonly OrbitKey[] _keys;
    private readonly double[] _times;
    private readonly MonotoneCubic _yaw;
    private readonly MonotoneCubic _pitch;
    private readonly MonotoneCubic _distance;
    private readonly MonotoneCubic _fov;
    private readonly Easing _ease;
    private OrbitSample? _smoothed;

    public OrbitTrack(IReadOnlyList<OrbitKey> keys, double duration, Easing ease, double defaultFov, double smoothSeconds = 0.0)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            throw new ArgumentException("An orbit track needs at least one key.", nameof(keys));
        }

        if (!(duration > 0) || !double.IsFinite(duration))
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "The duration must be a positive number of seconds.");
        }

        _keys = keys.ToArray();
        Duration = duration;
        _ease = ease;
        SmoothSeconds = Math.Max(0.0, smoothSeconds);
        var timed = _keys.All(k => k.Time is not null);
        if (!timed && _keys.Any(k => k.Time is not null))
        {
            throw new ArgumentException("Give every orbit key a time, or none (spread evenly).", nameof(keys));
        }

        _times = timed
            ? _keys.Select(k => k.Time!.Value).ToArray()
            : _keys.Select((_, i) => _keys.Length == 1 ? 0.0 : duration * i / (_keys.Length - 1)).ToArray();
        if (_keys.Length == 1)
        {
            _times = [0.0];
        }

        var yaws = new double[_keys.Length];
        for (var i = 0; i < _keys.Length; i++)
        {
            yaws[i] = i == 0 ? _keys[0].Yaw : CameraPath.Unwrap(yaws[i - 1], _keys[i].Yaw);
        }

        _yaw = new MonotoneCubic(_times, yaws);
        _pitch = new MonotoneCubic(_times, _keys.Select(k => k.Pitch).ToArray());
        _distance = new MonotoneCubic(_times, _keys.Select(k => k.Distance).ToArray());
        _fov = new MonotoneCubic(_times, _keys.Select(k => k.Fov ?? defaultFov).ToArray());
    }

    public double Duration { get; }

    /// <summary>Time constant of the follow filter (0 = none).</summary>
    public double SmoothSeconds { get; }

    /// <summary>The keys as given.</summary>
    public IReadOnlyList<OrbitKey> Keys => _keys;

    /// <summary>The unsmoothed camera at shot time <paramref name="t"/>.</summary>
    /// <param name="t">Seconds from the shot start.</param>
    /// <param name="focusPoint">Resolves a key's focus name to a point now.</param>
    public OrbitSample Sample(double t, Func<string, Vec3d> focusPoint)
    {
        ArgumentNullException.ThrowIfNull(focusPoint);
        var time = _ease.Evaluate(t / Duration) * Duration;
        var focus = FocusAt(time, focusPoint);
        return new OrbitSample(CameraPath.Wrap360(_yaw.Evaluate(time)), _pitch.Evaluate(time), Math.Max(0.05, _distance.Evaluate(time)), focus, _fov.Evaluate(time));
    }

    /// <summary>
    /// The camera for the next fixed step: <see cref="Sample"/>, low-passed with <see cref="SmoothSeconds"/> over
    /// <paramref name="step"/> seconds (the first call, and <see cref="Reset"/>, cut straight to the sample).
    /// </summary>
    public OrbitSample Step(double t, double step, Func<string, Vec3d> focusPoint)
    {
        var want = Sample(t, focusPoint);
        if (SmoothSeconds <= 0 || _smoothed is not { } current)
        {
            _smoothed = want;
            return want;
        }

        var k = 1.0 - Math.Exp(-Math.Max(0.0, step) / SmoothSeconds);
        var yaw = current.Yaw + ((CameraPath.Unwrap(current.Yaw, want.Yaw) - current.Yaw) * k);
        var next = new OrbitSample(
            CameraPath.Wrap360(yaw),
            current.Pitch + ((want.Pitch - current.Pitch) * k),
            current.Distance + ((want.Distance - current.Distance) * k),
            Vec3d.Lerp(current.Focus, want.Focus, k),
            current.Fov + ((want.Fov - current.Fov) * k));
        _smoothed = next;
        return next;
    }

    /// <summary>Forgets the follow filter's state (the next <see cref="Step"/> cuts).</summary>
    public void Reset() => _smoothed = null;

    /// <summary>The camera position for an orbit sample: <c>focus − forward × distance</c> (AC axes).</summary>
    public static Vec3d EyeOf(OrbitSample sample)
    {
        var yaw = sample.Yaw * Math.PI / 180.0;
        var pitch = sample.Pitch * Math.PI / 180.0;
        var forward = new Vec3d(Math.Sin(yaw) * Math.Cos(pitch), Math.Cos(yaw) * Math.Cos(pitch), Math.Sin(pitch));
        return sample.Focus - (forward * sample.Distance);
    }

    private Vec3d FocusAt(double time, Func<string, Vec3d> focusPoint)
    {
        var n = _keys.Length;
        if (n == 1 || time <= _times[0])
        {
            return focusPoint(_keys[0].Focus) + _keys[0].Offset;
        }

        if (time >= _times[n - 1])
        {
            return focusPoint(_keys[n - 1].Focus) + _keys[n - 1].Offset;
        }

        var i = Array.BinarySearch(_times, time);
        if (i >= 0)
        {
            return focusPoint(_keys[i].Focus) + _keys[i].Offset;
        }

        i = ~i - 1;
        var a = focusPoint(_keys[i].Focus) + _keys[i].Offset;
        var b = focusPoint(_keys[i + 1].Focus) + _keys[i + 1].Offset;
        var w = (time - _times[i]) / (_times[i + 1] - _times[i]);
        return Vec3d.Lerp(a, b, w * w * (3.0 - (2.0 * w)));
    }
}
