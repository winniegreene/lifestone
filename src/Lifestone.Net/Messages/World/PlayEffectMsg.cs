namespace Lifestone.Net.Messages;

/// <summary>F755 PlayEffect (ACE <c>GameMessageScript</c>): play a physics script (PlayScript) on an object.</summary>
/// <param name="Guid">The object.</param>
/// <param name="ScriptId">The PlayScript value.</param>
/// <param name="Speed">The speed (intensity) multiplier.</param>
public readonly record struct PlayEffectMsg(uint Guid, uint ScriptId, float Speed);
