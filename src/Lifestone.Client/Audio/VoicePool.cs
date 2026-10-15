namespace Lifestone.Client.Audio;

/// <summary>
/// Retail's 16 voices and the rule that picks one for a new sound: <c>SoundManager::PlaySoundInternal(SoundBufRef*, int,
/// int)</c> (11.4186 0x0054FEC0, 11.6096 0x00550AD0) over <c>SoundManager::playing_sounds_</c>.
/// </summary>
/// <remarks>
/// <para><b>The rule.</b> Starting at a cursor, the first voice that is empty, holds priority 0 (the float's bits all
/// zero), or has stopped playing (<c>SoundBuf::GetStatus</c>, 11.4186 0x00552850, 11.6096 0x00553470) is taken. When all
/// 16 are busy, a second pass from the cursor takes the first voice whose stored priority is lower than the new sound's,
/// stopping it; when there is none, the new sound is dropped. The cursor then moves to the voice after the one taken.
/// Retail also stores each voice's start time, which nothing reads; it is not kept.</para>
/// <para><b>In practice</b> the priority a voice stores is the cached sound buffer's (<c>SoundBufRef::data_</c>), which
/// keeps <c>SoundData</c>'s default 0 (<see cref="RetailSoundPlayer.StoredPriority"/>): every voice holds priority 0, so
/// the first pass always takes the voice at the cursor, and the 17th sound replaces the oldest whether or not it is still
/// playing. The general rule is kept here so the finding stays visible and testable.</para>
/// <para>Frame thread only. Allocation-free.</para>
/// </remarks>
public sealed class VoicePool
{
    private readonly bool[] _occupied = new bool[RetailSoundRules.VoiceCount];
    private readonly float[] _priority = new float[RetailSoundRules.VoiceCount];

    /// <summary>The voice the next search starts at (retail <c>curr_playing_buffer_</c>).</summary>
    public int Cursor { get; private set; }

    /// <summary>Whether <paramref name="voice"/> has been given a sound since the last <see cref="Reset"/>.</summary>
    public bool IsOccupied(int voice) => _occupied[voice];

    /// <summary>The priority <paramref name="voice"/> stored when it was taken.</summary>
    public float PriorityOf(int voice) => _priority[voice];

    /// <summary>
    /// Picks the voice for a new sound of <paramref name="priority"/>, asking <paramref name="output"/> which voices still
    /// play and stopping one that loses on priority; returns the voice, now holding <paramref name="priority"/>, or −1
    /// when the sound is dropped.
    /// </summary>
    public int Acquire(float priority, IAudioOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var count = RetailSoundRules.VoiceCount;
        var chosen = -1;
        for (var i = 0; i < count; i++)
        {
            var voice = (Cursor + i) % count;
            if (!_occupied[voice] || BitConverter.SingleToInt32Bits(_priority[voice]) == 0 || !output.IsPlaying(voice))
            {
                chosen = voice;
                break;
            }
        }

        if (chosen < 0)
        {
            for (var i = 0; i < count; i++)
            {
                var voice = (Cursor + i) % count;
                if (_priority[voice] < priority)
                {
                    output.Stop(voice);
                    chosen = voice;
                    break;
                }
            }

            if (chosen < 0)
            {
                return -1;
            }
        }

        _occupied[chosen] = true;
        _priority[chosen] = priority;
        Cursor = (chosen + 1) % count;
        return chosen;
    }

    /// <summary>Forgets every voice and puts the cursor back to 0 (the output's voices are the caller's to stop).</summary>
    public void Reset()
    {
        Array.Clear(_occupied);
        Array.Clear(_priority);
        Cursor = 0;
    }
}
