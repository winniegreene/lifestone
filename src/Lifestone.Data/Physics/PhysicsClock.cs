namespace Lifestone.Data.Physics;

/// <summary>
/// The 30 Hz gate of retail <c>CPhysics::UseTime</c> (11.4186 0x00509950, 11.6096 0x0050A420; re-expressed, nothing
/// copied): physics runs on a frame only when at least <see cref="PhysicsGlobals.MinQuantum"/> (1/30 s) has passed since
/// it last ran, so at 60 fps every other frame steps the world. Each object then slices the elapsed time into steps of
/// at most <see cref="PhysicsGlobals.MaxQuantum"/> on its own clock (<c>CPhysicsObj::update_object</c>, the same rule as
/// <see cref="Effects.ObjectClock"/>).
/// </summary>
/// <remarks>
/// <para>Retail keeps <c>last_update</c> in a static; a clock that goes backwards (a negative gap) only resets it. The
/// host passes its own monotonic seconds; nothing here reads a system clock, so runs are repeatable.</para>
/// <para>Not thread-safe: one clock per world, used by the main thread (or the headless tick).</para>
/// </remarks>
public sealed class PhysicsClock
{
    /// <summary>Starts the clock at <paramref name="now"/> (seconds).</summary>
    public PhysicsClock(double now)
    {
        LastUpdate = now;
    }

    /// <summary>When physics last ran (or when the clock was last reset), in the host's seconds.</summary>
    public double LastUpdate { get; private set; }

    /// <summary>
    /// The frame's gate: true when physics runs now, which moves <see cref="LastUpdate"/> to <paramref name="now"/>.
    /// A gap below 1/30 s returns false and keeps accumulating; a negative gap resets the clock and returns false.
    /// </summary>
    /// <param name="now">The host's monotonic clock in seconds.</param>
    /// <param name="elapsed">The gap since <see cref="LastUpdate"/> before the call (retail's <c>quantum</c>, which
    /// <c>CPhysics::UseTime</c> also hands to the texture-velocity update).</param>
    public bool UseTime(double now, out double elapsed)
    {
        elapsed = now - LastUpdate;
        if (elapsed < 0.0)
        {
            LastUpdate = now;
            return false;
        }

        if (elapsed < PhysicsGlobals.MinQuantum)
        {
            return false;
        }

        LastUpdate = now;
        return true;
    }
}
