using Lifestone.Data.Effects;

namespace Lifestone.Client.PortalSpace;

/// <summary>
/// The time slicing of retail <c>CPhysicsObj::update_position</c> for one creature-mode object (the portal tunnel):
/// it decides, from the caller's clock, whether the object takes a step this frame and how long that step is.
/// </summary>
/// <remarks>
/// <para>Re-expresses, by function name, <c>CPhysicsObj::update_position</c> (11.4186 0x00514520, 11.6096 0x00515020),
/// which <c>CreatureMode::Render</c> (11.4186 0x004529D0, 11.6096 0x00452A10) calls for each creature-mode object while
/// it draws them. With e = now − <see cref="UpdateTime"/>:</para>
/// <list type="bullet">
/// <item>e ≤ ε (<see cref="ObjectClock.Epsilon"/>): no step, and the clock moves to <c>now</c>;</item>
/// <item>ε &lt; e &lt; <see cref="ObjectClock.MinQuantum"/> (1/30 s): no step, and the clock stays where it is, so the
/// time builds up for a later frame;</item>
/// <item><see cref="ObjectClock.MinQuantum"/> ≤ e ≤ <see cref="ObjectClock.MaxElapsed"/> (2 s): one step of the whole
/// e, handed to <c>CPhysicsObj::UpdatePositionInternal</c> (11.4186 0x00512C30, 11.6096 0x00513730) as a float, and
/// the clock moves to <c>now</c>;</item>
/// <item>e &gt; 2 s: no step, and the clock moves to <c>now</c> (the gap is skipped).</item>
/// </list>
/// <para>So, unlike <c>CPhysicsObj::update_object</c> (<see cref="ObjectClock"/>), a long gap is one step, never split
/// into 0.2 s quanta, and an object drawn at more than 30 frames per second steps only every second or third frame: at
/// 60 fps the tunnel's spin moves 20 to 30 times a second, as in retail.</para>
/// <para>The clock is the object's <c>update_time</c>. <c>CPhysicsObj::InitObjectBegin</c> (11.4186 0x0050FF80, 11.6096
/// 0x00510A50) sets it when the object is made, and for a creature-mode object nothing else moves it:
/// <c>CPhysicsObj::set_sequence_animation</c> leaves it alone. So the clock outlives a restart of the spin, and a tunnel
/// shown again within 2 s of its last step catches up on the time it was hidden in its first step.</para>
/// <para>Allocation-free. Frame thread only.</para>
/// </remarks>
public sealed class CreatureModeClock
{
    /// <summary>Starts the clock at <paramref name="createdAt"/>, the object's creation (retail <c>InitObjectBegin</c>).</summary>
    public CreatureModeClock(double createdAt)
    {
        UpdateTime = createdAt;
    }

    /// <summary>The clock value of the last step or skip (retail <c>update_time</c>).</summary>
    public double UpdateTime { get; private set; }

    /// <summary>
    /// Decides the step at <paramref name="now"/>: true with the step's length in <paramref name="quantum"/> (the elapsed
    /// time rounded to a float, as retail passes it), false when the object does not step this frame.
    /// </summary>
    public bool TryStep(double now, out double quantum)
    {
        quantum = 0.0;
        var elapsed = now - UpdateTime;
        if (elapsed <= ObjectClock.Epsilon || elapsed > ObjectClock.MaxElapsed)
        {
            UpdateTime = now;
            return false;
        }

        if (elapsed < ObjectClock.MinQuantum)
        {
            return false; // carried: the next frame measures from the same update_time
        }

        quantum = (float)elapsed;
        UpdateTime = now;
        return true;
    }
}
