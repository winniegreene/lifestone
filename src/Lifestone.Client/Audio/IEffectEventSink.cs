using Lifestone.Data.Effects;

namespace Lifestone.Client.Audio;

/// <summary>
/// Receives the hook events an object's animation or physics scripts fire that the effect layer hands on
/// (<see cref="EffectObject.DrainEvents"/>), with where the object is relative to the listener. The sound router
/// (<see cref="SoundHookRouter"/>) implements it: Sound, SoundTable and SoundTweaked hooks, and the sound the server asks an
/// object to play, become sounds through an <see cref="ISoundSink"/>; every other hook is ignored.
/// </summary>
/// <remarks>Called on the frame thread by whoever updates objects (the wave-2 world objects; the portal tunnel).</remarks>
public interface IEffectEventSink
{
    /// <summary>
    /// One event from an object at <paramref name="origin"/>. <paramref name="soundTableId"/> is the emitting object's
    /// sound table (0x20xxxxxx, or 0 when it has none): a SoundTable hook plays from the table of the object that fired
    /// it, as retail's <c>SoundTableHook::Execute</c> (11.6096 0x005276B0) hands the object to the sound manager.
    /// </summary>
    void OnEffectEvent(in EffectEvent effectEvent, in SoundOrigin origin, uint soundTableId);

    /// <summary>
    /// The same event, told whether the firing object is the player's own body (M3 1.3 fix B): lets a sink count the player's footsteps apart from
    /// every other object's. A sink that does not care keeps this default, which drops the flag.
    /// </summary>
    void OnEffectEvent(in EffectEvent effectEvent, in SoundOrigin origin, uint soundTableId, bool fromPlayer) =>
        OnEffectEvent(effectEvent, origin, soundTableId);
}
