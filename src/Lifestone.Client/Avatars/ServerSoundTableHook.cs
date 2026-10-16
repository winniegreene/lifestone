using Lifestone.Data.Animation;

namespace Lifestone.Client.Avatars;

/// <summary>
/// A sound the server asked an object to play (F750 Sound: <c>SmartBox::HandleSoundEvent</c>, 11.6096 0x00452000, then <c>CPhysicsObj::play_sound</c>), as an effect event: the sound
/// type to look up in the object's sound table and the volume the server gave. It travels the same path as the hook events an animation or a script fires
/// (<c>AvatarSet.EffectEvents</c>, <see cref="Lifestone.Client.Audio.IEffectEventSink"/>), so the sink that hears the hooks hears it.
/// </summary>
/// <remarks>
/// Unlike a <see cref="SoundTableHook"/> (the entry's own volume), this one carries the caller's volume: retail's <c>SoundManager::PlaySoundA(SoundType, obj, volume)</c>, which the sound
/// sink's <c>PlayFromTable(table, type, origin, volume)</c> overload re-expresses. The router that ships today ignores it (it routes only SoundTweaked, Sound and SoundTable hooks): M3 1.3 s1
/// teaches the sink to play it.
/// </remarks>
/// <param name="SoundTableId">The object's sound table (0x20xxxxxx), or 0 when it has none (retail then plays nothing).</param>
/// <param name="SoundType">The sound type (retail <c>SoundType</c>; DatReaderWriter's <c>Sound</c> enum).</param>
/// <param name="Volume">The volume, 0 to 1 (the message's).</param>
public sealed record ServerSoundTableHook(uint SoundTableId, uint SoundType, float Volume) : AnimHook(AnimHookDirection.Both)
{
    /// <inheritdoc/>
    public override AnimHookType Type => AnimHookType.SoundTable;
}
