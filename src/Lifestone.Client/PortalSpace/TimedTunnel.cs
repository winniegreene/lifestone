using System.Buffers.Binary;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;

namespace Lifestone.Client.PortalSpace;

/// <summary>
/// The tunnel's frame counter without the tunnel: what the state machine reads of the spin (its current frame), for the
/// headless host and the machine tests, which have no tunnel to draw and need no dats.
/// </summary>
/// <remarks>
/// <para>It steps the same <see cref="TunnelSpin"/> with the same clock as <see cref="TunnelObject"/> (a
/// <see cref="CreatureModeClock"/>, retail <c>CPhysicsObj::update_position</c>, 11.4186 0x00514520, 11.6096 0x00515020,
/// as <c>CreatureMode::Render</c> calls it), over a stand-in animation of 120 empty frames (no parts, no hooks), so its
/// frame numbers are the real tunnel's bit for bit. The spin moves only once at least 1/30 s has built up since its last
/// step, by the whole time: after a step at time t the frame is 1 + ⌊40 · (t − t₀)⌋ mod 119, t₀ being the clock's value
/// when the spin restarted (to the float rounding of the steps), and between steps it holds. Like the real tunnel, a gap of more than 2 s between two
/// <see cref="AdvanceTo"/> calls is skipped, not stepped, and the clock carries over a <see cref="Restart"/>.</para>
/// <para>It fires no hooks, so <see cref="DrainEvents"/> always returns 0: the headless host is silent. Allocation-free per
/// <see cref="AdvanceTo"/>; <see cref="Restart"/> allocates the sequence node.</para>
/// </remarks>
public sealed class TimedTunnel : IPortalTunnel
{
    /// <summary>The stand-in animation's id. It names no dat record.</summary>
    public const uint StandInAnimationId = 0x03000000;

    private readonly TunnelSpin _spin;
    private readonly CreatureModeClock _clock;

    /// <summary>Creates a stopped counter over an animation of <paramref name="frameCount"/> frames.</summary>
    /// <param name="frameCount">The stand-in animation's frame count, at least 2 (the loop starts at frame 1); the default
    /// is the retail loop's 120 frames (looping 1 to 119).</param>
    /// <param name="createdAt">The host's frame time when the counter is made (the object's clock starts then).</param>
    public TimedTunnel(int frameCount = TunnelSpin.RetailLastFrame + 1, double createdAt = 0.0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frameCount, 2);
        _spin = new TunnelSpin(new StandInSource(StandIn(frameCount)), StandInAnimationId);
        _clock = new CreatureModeClock(createdAt);
    }

    /// <inheritdoc/>
    public int CurrentFrame => _spin.CurrentFrame;

    /// <summary>The spin's frame number (a double, as retail keeps it).</summary>
    public double FrameNumber => _spin.FrameNumber;

    /// <summary>The counter is running.</summary>
    public bool IsSpinning => _spin.IsSpinning;

    /// <summary>The clock value of the last step or skip (retail <c>update_time</c>).</summary>
    public double UpdateTime => _clock.UpdateTime;

    /// <inheritdoc/>
    public void Restart() => _spin.Restart();

    /// <inheritdoc/>
    public void Stop() => _spin.Stop();

    /// <inheritdoc/>
    public void AdvanceTo(double now)
    {
        if (_clock.TryStep(now, out var quantum))
        {
            _spin.Update(quantum);
        }
    }

    /// <inheritdoc/>
    public int DrainEvents(List<EffectEvent> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        return 0;
    }

    /// <summary>
    /// The stand-in: a raw Animation record of <paramref name="frameCount"/> frames with no parts, no root motion and no
    /// hooks, decoded by the same decoder as the dats' records.
    /// </summary>
    private static AnimationData StandIn(int frameCount)
    {
        // u32 id, u32 flags, u32 parts, u32 frames, then per frame (no part frames) a u32 hook count of 0.
        var raw = new byte[16 + (4 * frameCount)];
        BinaryPrimitives.WriteUInt32LittleEndian(raw, StandInAnimationId);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(12), (uint)frameCount);
        return AnimationData.Decode(StandInAnimationId, raw);
    }

    /// <summary>Serves the stand-in for its id.</summary>
    private sealed class StandInSource(AnimationData animation) : IAnimationSource
    {
        public AnimationData? GetAnimation(uint animId) => animId == animation.Id ? animation : null;
    }
}
