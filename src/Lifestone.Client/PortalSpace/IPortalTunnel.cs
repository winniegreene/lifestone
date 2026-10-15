using Lifestone.Data.Effects;

namespace Lifestone.Client.PortalSpace;

/// <summary>
/// The portal-space tunnel object as the state machine and the director drive it: retail's <c>teleportObj</c>, the
/// UIASSET tunnel Setup spinning its UIASSET animation. Implementations: <see cref="TunnelObject"/> (the real object over
/// the dats) and <see cref="TimedTunnel"/> (the frame counter alone, for the headless host and tests); both step the same
/// <see cref="TunnelSpin"/>, so their frames agree.
/// </summary>
/// <remarks>
/// The machine calls <see cref="Restart"/> and <see cref="Stop"/> and reads <see cref="CurrentFrame"/>; the director
/// calls <see cref="AdvanceTo"/> and <see cref="DrainEvents"/>. Called on the frame thread only. A steady frame allocates
/// nothing.
/// </remarks>
public interface IPortalTunnel
{
    /// <summary>
    /// The current animation frame (retail <c>CPhysicsObj::get_curr_frame_number</c>, 11.4186 0x0050F780, 11.6096
    /// 0x00510250): 1 to 119 while spinning, 0 when stopped. The exit window of state 4 reads it.
    /// </summary>
    int CurrentFrame { get; }

    /// <summary>
    /// Starts the spin from frame 1 at 40 fps, interrupting anything else (retail
    /// <c>CPhysicsObj::set_sequence_animation(anim, 1, 1, 40)</c>, 11.4186 0x0050F6F0, 11.6096 0x005101C0). It touches
    /// neither the object's clock nor its parts' pose, as retail's does not: the next <see cref="AdvanceTo"/> measures from
    /// the tunnel's last step (or skip), and only a step poses the parts.
    /// </summary>
    void Restart();

    /// <summary>Stops the spin (retail <c>CPhysicsObj::clear_sequence_anims</c>, 11.4186 0x0050F760, 11.6096 0x00510230).</summary>
    void Stop();

    /// <summary>
    /// Advances the spin to <paramref name="now"/> as retail <c>CPhysicsObj::update_position</c> (11.4186 0x00514520,
    /// 11.6096 0x00515020) does (<see cref="CreatureModeClock"/>): no step until at least 1/30 s has built up, then one
    /// step of the whole time; a gap over 2 s is skipped. Called only while the tunnel is shown: retail updates
    /// creature-mode objects inside <c>CreatureMode::Render</c> (11.4186 0x004529D0, 11.6096 0x00452A10).
    /// </summary>
    void AdvanceTo(double now);

    /// <summary>
    /// Moves the hook events the spin fired since the last call (the tunnel hum: a SoundTweaked hook on frame 2) into
    /// <paramref name="into"/>, oldest first, and returns how many. A tunnel without hooks returns 0.
    /// </summary>
    int DrainEvents(List<EffectEvent> into);
}
