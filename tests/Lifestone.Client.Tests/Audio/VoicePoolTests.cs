using Lifestone.Client.Audio;

namespace Lifestone.Client.Tests.Audio;

/// <summary>Step PS10: retail's 16 voices (<c>SoundManager::PlaySoundInternal</c>).</summary>
public sealed class VoicePoolTests
{
    [Fact]
    public void With_retails_stored_priority_the_17th_sound_replaces_the_oldest_even_while_it_plays()
    {
        var pool = new VoicePool();
        var output = new RecordingAudioOutput();
        var voices = new List<int>();
        for (var i = 0; i < 17; i++)
        {
            var voice = pool.Acquire(RetailSoundPlayer.StoredPriority, output);
            voices.Add(voice);
            output.Play(new VoicePlay(voice, 0x0A000001, 0, 0, AudioBus.Effects));
        }

        Assert.Equal([.. Enumerable.Range(0, 16), 0], voices);
        Assert.True(output.IsPlaying(0));
        Assert.Empty(output.Stops); // the first pass takes a priority-0 voice without asking or stopping
        Assert.Equal(1, pool.Cursor);
    }

    [Fact]
    public void A_voice_that_has_stopped_playing_is_taken_first()
    {
        var pool = new VoicePool();
        var output = new RecordingAudioOutput();
        for (var i = 0; i < 16; i++)
        {
            output.Play(new VoicePlay(pool.Acquire(0.5f, output), 0x0A000001, 0, 0, AudioBus.Effects));
        }

        output.Finish(7);

        Assert.Equal(7, pool.Acquire(0.5f, output));
        Assert.Equal(8, pool.Cursor);
        Assert.Empty(output.Stops);
    }

    [Fact]
    public void When_all_16_play_a_lower_priority_voice_is_stopped_or_the_sound_is_dropped()
    {
        var pool = new VoicePool();
        var output = new RecordingAudioOutput();
        for (var i = 0; i < 16; i++)
        {
            output.Play(new VoicePlay(pool.Acquire(i == 5 ? 0.25f : 0.5f, output), 0x0A000001, 0, 0, AudioBus.Effects));
        }

        // Equal priorities do not lose: only voice 5 is lower.
        Assert.Equal(5, pool.Acquire(0.4f, output));
        Assert.Equal([5], output.Stops);
        Assert.Equal(0.4f, pool.PriorityOf(5));
        output.Play(new VoicePlay(5, 0x0A000001, 0, 0, AudioBus.Effects));

        Assert.Equal(-1, pool.Acquire(0.4f, output));
        Assert.Equal(6, pool.Cursor);

        // A higher priority takes the first lower voice from the cursor.
        Assert.Equal(6, pool.Acquire(0.9f, output));
    }

    [Fact]
    public void Only_a_priority_whose_bits_are_all_zero_counts_as_none()
    {
        var pool = new VoicePool();
        var output = new RecordingAudioOutput();
        for (var i = 0; i < 16; i++)
        {
            output.Play(new VoicePlay(pool.Acquire(-0f, output), 0x0A000001, 0, 0, AudioBus.Effects));
        }

        // −0.0 is not retail's "priority 0" (its bits are not zero), and 0 is not lower than it.
        Assert.Equal(-1, pool.Acquire(0f, output));
        Assert.Equal(0, pool.Acquire(0.1f, output));
    }

    [Fact]
    public void Reset_forgets_every_voice()
    {
        var pool = new VoicePool();
        var output = new RecordingAudioOutput();
        pool.Acquire(0.5f, output);
        pool.Acquire(0.5f, output);

        pool.Reset();

        Assert.Equal(0, pool.Cursor);
        Assert.False(pool.IsOccupied(0));
        Assert.Equal(0, pool.Acquire(0.5f, output));
    }
}
