using Lifestone.Client.Diagnostics;
using Lifestone.Client.Avatars;
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
/// <item><see cref="SoundTableHook"/> → <see cref="ISoundSink.PlayFromTable(uint, uint, SoundOrigin)"/> on the sound table
/// of the object that fired it, or nothing when the object has none (<c>SoundTableHook::Execute</c>, 11.4186 0x00526AB0,
/// 11.6096 0x005276B0).</item>
/// <item><see cref="ServerSoundTableHook"/> → <see cref="ISoundSink.PlayFromTable(uint, uint, SoundOrigin, float)"/> on its
/// own table at the server's volume.</item>
/// </list>
/// <para>The hook's direction was already applied by the animation that fired it. The origin is where the object was
/// relative to the listener when the hook fired; retail reads the object's position once, so a sound does not follow
/// its object.</para>
/// <para>The object's sound table travels with each event (<see cref="IEffectEventSink.OnEffectEvent(in EffectEvent, in SoundOrigin, uint)"/>): one router serves
/// every object of a host (the avatars, the living statics), and the old per-router table of the portal tunnel's single
/// object is folded into that parameter (the tunnel calls the static <see cref="Route"/> with table 0). The sound the
/// server asks an object to play (<see cref="ServerSoundTableHook"/>, F750) carries its own table and the caller's volume:
/// <c>SmartBox::HandleSoundEvent</c> (11.6096 0x00452000), then <c>CPhysicsObj::play_sound</c>, which ends in
/// <c>SoundManager::PlaySoundA(SoundType, obj, float)</c> (11.6096 0x00551700).</para>
/// <para>Two kinds of counter, apart (M3 1.3 fix B, review 1.3-R2). <b>Handed</b> counters (<see cref="HooksPlayed"/>, <see cref="Footsteps"/>,
/// <see cref="PlayerFootsteps"/>, <see cref="ServerSounds"/>) count what was handed to the sound player, whether or not it chose a wave: the human sound
/// table 0x20000001 has no variant for footstep types 55 and 56 and retail uses the object's own table only (no fallback), so the player's footsteps
/// are handed over and never sound. <b>Sounded</b> counters (<see cref="HooksSounded"/>, <see cref="ServerSounded"/>, <see cref="PlayerFootstepsSounded"/>)
/// count the voices the hand-off started, read as the delta of <see cref="RetailSoundPlayer.Played"/> around each call; they exist only when the sink is a
/// <see cref="RetailSoundPlayer"/> (<see cref="CountsSounded"/>). Both are independent of the volume (an automated run is silent, M2 D8).
/// Frame thread only. Allocation-free.</para>
/// </remarks>
/// <param name="sink">Where the sounds go (normally a <see cref="RetailSoundPlayer"/>).</param>
public sealed class SoundHookRouter(ISoundSink sink) : IEffectEventSink, ISoundVoiceCounter
{
    private readonly RetailSoundPlayer? _player = sink as RetailSoundPlayer;

    private bool _footstepTodoLogged;

    /// <summary>DatReaderWriter's <c>Sound.Footstep1</c>.</summary>
    public const uint Footstep1 = 55;

    /// <summary>DatReaderWriter's <c>Sound.Footstep2</c>.</summary>
    public const uint Footstep2 = 56;

    /// <summary>Where the sounds go.</summary>
    public ISoundSink Sink { get; } = sink ?? throw new ArgumentNullException(nameof(sink));

    /// <summary>Sound hooks (Sound, SoundTweaked, SoundTable) and server sounds routed, including those of an object without a sound table.</summary>
    public int Routed { get; private set; }

    /// <summary>Events that were not sound hooks.</summary>
    public int Ignored { get; private set; }

    /// <summary>Animation and script sound hooks handed to the sound player (footsteps included; the server's sounds are counted apart).</summary>
    public int HooksPlayed { get; private set; }

    /// <summary>The SoundTable hooks of a footstep type (<see cref="Footstep1"/>, <see cref="Footstep2"/>) handed to the sound player: a subset of <see cref="HooksPlayed"/>.</summary>
    public int Footsteps { get; private set; }

    /// <summary>The Footstep hooks fired by the player's own body, handed to the sound player: a subset of <see cref="Footsteps"/> (the avatars say which object fired the event).</summary>
    public int PlayerFootsteps { get; private set; }

    /// <summary>The sounds the server asked for (F750) handed to the sound player.</summary>
    public int ServerSounds { get; private set; }

    /// <summary>True when the sink is a <see cref="RetailSoundPlayer"/>, so the <c>*Sounded</c> counters are meaningful; otherwise they stay 0.</summary>
    public bool CountsSounded => _player is not null;

    /// <summary>Voices the animation and script hooks started (footsteps included): a hook that chose a wave, passed its probability roll and the attenuation, and got a voice.</summary>
    public int HooksSounded { get; private set; }

    /// <inheritdoc/>
    public int VoicesStarted => HooksSounded + ServerSounded;

    /// <summary>Voices the player's own Footstep hooks started: 0 with the human table, which has no variant for footstep types 55 and 56.</summary>
    public int PlayerFootstepsSounded { get; private set; }

    /// <summary>Voices the server's sounds (F750) started.</summary>
    public int ServerSounded { get; private set; }

    /// <summary>Why the hooks handed over did or did not sound (report-only; M3 1.3 fix B): by hook kind, reason and distance.</summary>
    public HookSoundDiagnostics Diagnostics { get; } = new();

    /// <summary>SoundTable hooks and server sounds of an object without a sound table: silent, as retail.</summary>
    public int NoTable { get; private set; }

    /// <inheritdoc/>
    public void OnEffectEvent(in EffectEvent effectEvent, in SoundOrigin origin, uint soundTableId) =>
        OnEffectEvent(effectEvent, origin, soundTableId, fromPlayer: false);

    /// <inheritdoc/>
    public void OnEffectEvent(in EffectEvent effectEvent, in SoundOrigin origin, uint soundTableId, bool fromPlayer)
    {
        var before = _player?.Played ?? 0;
        var rollsBefore = _player?.RollFailed ?? 0;
        var attenuationBefore = _player?.AttenuationFailed ?? 0;
        var outcome = Dispatch(effectEvent, origin, Sink, soundTableId);
        var sounded = (_player?.Played ?? 0) - before;
        if (_player is not null && outcome is Outcome.Hook or Outcome.Footstep)
        {
            Diagnostics.Note(effectEvent.Hook, origin.Distance, sounded, _player.RollFailed - rollsBefore, _player.AttenuationFailed - attenuationBefore, _player);
        }

        switch (outcome)
        {
            case Outcome.Ignored:
                Ignored++;
                return;
            case Outcome.NoTable:
                NoTable++;
                break;
            case Outcome.Hook:
                HooksPlayed++;
                HooksSounded += sounded;
                break;
            case Outcome.Footstep:
                HooksPlayed++;
                Footsteps++;
                HooksSounded += sounded;
                if (fromPlayer)
                {
                    PlayerFootsteps++;
                    PlayerFootstepsSounded += sounded;
                    if (sounded == 0 && !_footstepTodoLogged && _player is { } player && player.Log.IsEnabled(LogSeverity.Warning))
                    {
                        _footstepTodoLogged = true;
                        player.Log.Warn(
                            "audio",
                            $"TODO(footstep-sound-table): the player's Footstep hook (sound type {effectEvent.Hook switch { SoundTableHook table => table.SoundType, _ => 0u }}) found no wave in its sound table 0x{soundTableId:X8}; the human table has no variant for types 55 and 56 and retail reads the object's own table only (SoundTableHook::Execute 0x005276B0 -> CSoundTable::Lookup 0x00552D20), so the player's footsteps are silent; whether ACE gives the player another table is to be confirmed live by a read-only SELECT. Logged once");
                    }
                }

                break;
            case Outcome.Server:
                ServerSounds++;
                ServerSounded += sounded;
                break;
        }

        Routed++;
    }

    /// <summary>
    /// Routes one event from an object with sound table <paramref name="soundTableId"/> (0 for none) at
    /// <paramref name="origin"/> to <paramref name="sink"/>.
    /// </summary>
    /// <returns>True when the event was a sound hook (even a SoundTable hook on an object without a table).</returns>
    public static bool Route(in EffectEvent effectEvent, in SoundOrigin origin, ISoundSink sink, uint soundTableId) =>
        Dispatch(effectEvent, origin, sink, soundTableId) != Outcome.Ignored;

    private static Outcome Dispatch(in EffectEvent effectEvent, in SoundOrigin origin, ISoundSink sink, uint soundTableId)
    {
        ArgumentNullException.ThrowIfNull(sink);
        switch (effectEvent.Hook)
        {
            case SoundTweakedHook tweaked:
                sink.PlayWave(tweaked.SoundId, tweaked.Priority, tweaked.Probability, tweaked.Volume, origin);
                return Outcome.Hook;
            case SoundHook sound:
                sink.PlaySound(sound.SoundId, origin);
                return Outcome.Hook;
            case SoundTableHook table:
                if (soundTableId == 0)
                {
                    return Outcome.NoTable;
                }

                sink.PlayFromTable(soundTableId, table.SoundType, origin);
                return table.SoundType is Footstep1 or Footstep2 ? Outcome.Footstep : Outcome.Hook;
            case ServerSoundTableHook server:
                if (server.SoundTableId == 0)
                {
                    return Outcome.NoTable;
                }

                sink.PlayFromTable(server.SoundTableId, server.SoundType, origin, server.Volume);
                return Outcome.Server;
            default:
                return Outcome.Ignored;
        }
    }

    private enum Outcome
    {
        Ignored,
        NoTable,
        Hook,
        Footstep,
        Server,
    }
}

/// <summary>
/// An effect sink that knows how many voices its hand-offs have started ("sounded", M3 1.3 fix B), so a caller can read the delta around a delivery without a second sink (the avatar set's cast window,
/// M3 2.1 s2b). <see cref="SoundHookRouter"/> implements it over a <see cref="RetailSoundPlayer"/>; with any other sink the count stays 0.
/// </summary>
public interface ISoundVoiceCounter
{
    /// <summary>Voices started so far by the animation and script hooks and the server's sounds (<see cref="SoundHookRouter.HooksSounded"/> plus <see cref="SoundHookRouter.ServerSounded"/>).</summary>
    int VoicesStarted { get; }
}
