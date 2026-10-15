using Lifestone.Data.Animation;
using Lifestone.Data.Effects;

namespace Lifestone.Client.Audio;

/// <summary>
/// Turns the sound hooks an object's animation fires into sounds: the hook's <c>Execute</c> in retail, which calls the
/// sound manager with the object's position and sound table (<c>CAnimHook</c> overrides, 11.4186
/// PORTAL/canim/CAnimHook.c). Every other hook is ignored.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><see cref="SoundTweakedHook"/> → <see cref="ISoundSink.PlayWave"/> with the hook's priority, probability and
/// volume (<c>SoundTweakedHook::Execute</c>, 11.4186 0x00526A80, 11.6096 0x00527680).</item>
/// <item><see cref="SoundHook"/> → <see cref="ISoundSink.PlaySound"/> (<c>SoundHook::Execute</c>, 11.4186 0x00526A20,
/// 11.6096 0x00527620).</item>
/// <item><see cref="SoundTableHook"/> → <see cref="ISoundSink.PlayFromTable(uint, uint, SoundOrigin)"/> on the object's
/// sound table, or nothing when the object has none (<c>SoundTableHook::Execute</c>, 11.4186 0x00526AB0, 11.6096
/// 0x005276B0).</item>
/// </list>
/// <para>The hook's direction was already applied by the animation that fired it. The origin is where the object was
/// relative to the listener when the hook fired; retail reads the object's position once, so a sound does not follow
/// its object.</para>
/// <para>One router serves one object (<see cref="SoundTableId"/>); a host with many objects calls the static
/// <see cref="Route"/> with each object's table instead. Frame thread only. Allocation-free.</para>
/// </remarks>
/// <param name="sink">Where the sounds go (normally a <see cref="RetailSoundPlayer"/>).</param>
public sealed class SoundHookRouter(ISoundSink sink) : IEffectEventSink
{
    /// <summary>Where the sounds go.</summary>
    public ISoundSink Sink { get; } = sink ?? throw new ArgumentNullException(nameof(sink));

    /// <summary>The object's sound table (0x20xxxxxx), or 0 when it has none (its SoundTable hooks are silent).</summary>
    public uint SoundTableId { get; set; }

    /// <summary>Sound hooks routed.</summary>
    public int Routed { get; private set; }

    /// <summary>Events that were not sound hooks.</summary>
    public int Ignored { get; private set; }

    /// <inheritdoc/>
    public void OnEffectEvent(in EffectEvent effectEvent, in SoundOrigin origin)
    {
        if (Route(effectEvent, origin, Sink, SoundTableId))
        {
            Routed++;
        }
        else
        {
            Ignored++;
        }
    }

    /// <summary>
    /// Routes one event from an object with sound table <paramref name="soundTableId"/> (0 for none) at
    /// <paramref name="origin"/> to <paramref name="sink"/>.
    /// </summary>
    /// <returns>True when the event was a sound hook (even a SoundTable hook on an object without a table).</returns>
    public static bool Route(in EffectEvent effectEvent, in SoundOrigin origin, ISoundSink sink, uint soundTableId)
    {
        ArgumentNullException.ThrowIfNull(sink);
        switch (effectEvent.Hook)
        {
            case SoundTweakedHook tweaked:
                sink.PlayWave(tweaked.SoundId, tweaked.Priority, tweaked.Probability, tweaked.Volume, origin);
                return true;
            case SoundHook sound:
                sink.PlaySound(sound.SoundId, origin);
                return true;
            case SoundTableHook table:
                if (soundTableId != 0)
                {
                    sink.PlayFromTable(soundTableId, table.SoundType, origin);
                }

                return true;
            default:
                return false;
        }
    }
}
