namespace Lifestone.Client.Cinematic;

/// <summary>How close a camera path comes to the ground (<see cref="ShotPaths.CheckClearance"/>).</summary>
/// <param name="MinClearance">The smallest eye height above the ground found (metres; +∞ when no ground was known).</param>
/// <param name="AtTime">Shot time of that smallest clearance.</param>
/// <param name="SamplesBelow">Samples whose clearance was under the required one.</param>
/// <param name="Samples">Samples checked.</param>
public readonly record struct ClearanceReport(double MinClearance, double AtTime, int SamplesBelow, int Samples)
{
    public bool Clear => SamplesBelow == 0;
}

/// <summary>Turns a shot's keys into its camera motion: world paths in global metres, spell orbits.</summary>
public static class ShotPaths
{
    /// <summary>
    /// The camera path of a world shot. Key positions and look-at points are [east, north, up] metres from
    /// <paramref name="anchor"/> (global metres; the start location's spot); a key marked <c>ground</c> (or
    /// <c>lookGround</c>) takes its up from the ground under it (<paramref name="groundAt"/>, by global x and y; where the
    /// ground is unknown, from the anchor).
    /// </summary>
    /// <exception cref="ArgumentException">The shot is not a world shot, or its keys are inconsistent.</exception>
    public static CameraPath Build(Shot shot, Vec3d anchor, Func<double, double, double?> groundAt)
    {
        ArgumentNullException.ThrowIfNull(shot);
        ArgumentNullException.ThrowIfNull(groundAt);
        if (shot.Scene != ShotScene.World)
        {
            throw new ArgumentException($"Shot '{shot.Id}' is not a world shot.", nameof(shot));
        }

        var keys = shot.Keys.Select(k => new PathKey(
            Resolve(k.Position ?? Vec3d.Zero, k.PositionAboveGround, anchor, groundAt),
            k.Yaw,
            k.Pitch,
            k.Look is { } look ? Resolve(look, k.LookAboveGround, anchor, groundAt) : null,
            k.Fov,
            k.Time)).ToArray();
        return new CameraPath(keys, shot.Duration, shot.Ease, shot.Timing, shot.Fov);
    }

    /// <summary>The orbit camera of a spell shot.</summary>
    /// <exception cref="ArgumentException">The shot is not a spell shot.</exception>
    public static OrbitTrack BuildOrbit(Shot shot)
    {
        ArgumentNullException.ThrowIfNull(shot);
        if (shot.Scene != ShotScene.Spell)
        {
            throw new ArgumentException($"Shot '{shot.Id}' is not a spell shot.", nameof(shot));
        }

        var keys = shot.Keys.Select(k => new OrbitKey(k.Time, k.Yaw ?? 0, k.Pitch ?? 0, k.Distance ?? 6, k.Focus ?? "mid", k.Offset ?? Vec3d.Zero, k.Fov)).ToArray();
        return new OrbitTrack(keys, shot.Duration, shot.Ease, shot.Fov, shot.Smooth);
    }

    /// <summary>
    /// Samples <paramref name="path"/> every <paramref name="step"/> seconds and reports how close the eye comes to the
    /// ground (<paramref name="groundAt"/> by global x and y; unknown ground is skipped).
    /// </summary>
    public static ClearanceReport CheckClearance(CameraPath path, Func<double, double, double?> groundAt, double required, double step = 1.0 / 30.0)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(groundAt);
        var min = double.PositiveInfinity;
        var at = 0.0;
        var below = 0;
        var samples = 0;
        var count = Math.Max(1, (int)Math.Ceiling(path.Duration / step));
        for (var i = 0; i <= count; i++)
        {
            var t = Math.Min(path.Duration, i * step);
            var eye = path.Sample(t).Position;
            samples++;
            if (groundAt(eye.X, eye.Y) is not { } ground)
            {
                continue;
            }

            var clearance = eye.Z - ground;
            if (clearance < min)
            {
                min = clearance;
                at = t;
            }

            if (clearance < required)
            {
                below++;
            }
        }

        return new ClearanceReport(min, at, below, samples);
    }

    private static Vec3d Resolve(Vec3d offset, bool aboveGround, Vec3d anchor, Func<double, double, double?> groundAt)
    {
        var x = anchor.X + offset.X;
        var y = anchor.Y + offset.Y;
        var baseZ = aboveGround && groundAt(x, y) is { } ground ? ground : anchor.Z;
        return new Vec3d(x, y, baseZ + offset.Z);
    }
}
