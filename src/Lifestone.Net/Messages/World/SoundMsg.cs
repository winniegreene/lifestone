namespace Lifestone.Net.Messages;

/// <summary>F750 Sound (ACE <c>GameMessageSound</c>): play a sound on an object.</summary>
/// <param name="Guid">The object.</param>
/// <param name="SoundId">The sound (a Sound enum value of the object's sound table).</param>
/// <param name="Volume">The volume, 0 to 1.</param>
public readonly record struct SoundMsg(uint Guid, uint SoundId, float Volume);
