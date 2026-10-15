using Lifestone.Data.Effects;

namespace Lifestone.Client.Audio;

/// <summary>
/// Receives the hook events an object's animation or physics scripts fire that the effect layer hands on
/// (<see cref="EffectObject.DrainEvents"/>), with where the object is relative to the listener. The sound router
/// (<see cref="SoundHookRouter"/>) implements it: Sound, SoundTable and SoundTweaked hooks become sounds through an
/// <see cref="ISoundSink"/>; every other hook is ignored.
/// </summary>
/// <remarks>Called on the frame thread by whoever updates objects (the wave-2 world objects; the portal tunnel).</remarks>
public interface IEffectEventSink
{
    /// <summary>One event from an object at <paramref name="origin"/>.</summary>
    void OnEffectEvent(in EffectEvent effectEvent, in SoundOrigin origin);
}
