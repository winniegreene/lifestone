using Lifestone.Client.RetailUi.Core;

namespace Lifestone.Client.Tests.RetailUi.Core;

/// <summary>The UI sound counter (M3 1.3 s2a): every request is played or dropped; silenced and muted are subsets of played.</summary>
public sealed class RetailUiSoundStatsTests
{
    [Fact]
    public void A_new_counter_reads_zero()
    {
        var stats = new RetailUiSoundStats();

        Assert.Equal((0, 0, 0, 0, 0), (stats.Requested, stats.Played, stats.Dropped, stats.Silenced, stats.Muted));
    }

    [Fact]
    public void A_sound_handed_to_the_player_is_played_whatever_the_volume()
    {
        var stats = new RetailUiSoundStats();

        stats.RecordPlayed(silenced: false, muted: true); // an automated run: Master is silent (D8) and the sound still counts
        stats.RecordPlayed(silenced: false, muted: false);

        Assert.Equal(2, stats.Played);
        Assert.Equal(2, stats.Requested);
        Assert.Equal(1, stats.Muted);
        Assert.Equal(0, stats.Dropped);
    }

    [Fact]
    public void A_sound_with_no_player_is_dropped_not_played()
    {
        var stats = new RetailUiSoundStats();

        stats.RecordDropped();

        Assert.Equal((1, 0, 1), (stats.Requested, stats.Played, stats.Dropped));
    }

    [Fact]
    public void A_sound_the_player_started_no_voice_for_is_played_and_silenced()
    {
        var stats = new RetailUiSoundStats();

        stats.RecordPlayed(silenced: true, muted: false);
        stats.RecordDropped();

        Assert.Equal(stats.Requested, stats.Played + stats.Dropped);
        Assert.Equal(1, stats.Silenced);
        Assert.Equal("sounds_ui=1 requested=2 dropped=1 silenced=1 muted=0", stats.ToString());
    }
}
