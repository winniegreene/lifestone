using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>M6 7 Y: <see cref="RecentHits"/>, replayed on r2b's three blows (7 at 0.49 s, 18 at 3.80 s, 16 at 5.27 s, the last blow at 8.39 s).</summary>
public sealed class RecentHitsTests
{
    private static RecentHits R2b(int blows)
    {
        var hits = new RecentHits();
        (double At, double Damage)[] all = [(0.49, 7), (3.80, 18), (5.27, 16)];
        foreach (var (at, damage) in all.Take(blows))
        {
            hits.Note(at, damage);
        }

        return hits;
    }

    [Fact]
    public void No_hits_read_nothing_and_nothing_kills()
    {
        var read = new RecentHits().Read(100);
        Assert.Equal((0, 0, 0, false), (read.PerSecond, read.BiggestHit, read.Hits, read.Live));
        Assert.False(read.NextHitKills(1));
        Assert.True(read.Survives(1, 30));
    }

    [Fact]
    public void One_hit_reads_over_the_seed_interval_not_over_a_long_window()
    {
        var read = R2b(1).Read(0.5);
        Assert.Equal(7 / RecentHits.SeedAttackSeconds, read.PerSecond, 6);
        Assert.Equal(RecentHits.SeedAttackSeconds, read.IntervalSeconds, 6);
        Assert.True(read.Live);
    }

    [Fact]
    public void Two_blows_read_their_own_interval_about_eleven_a_second_where_the_fights_mean_read_one()
    {
        var read = R2b(3).Read(5.28);
        Assert.Equal(2, read.Hits); // the 7 is out of the recent seconds
        Assert.Equal(1.47, read.IntervalSeconds, 2);
        Assert.Equal(17 / 1.47, read.PerSecond, 2);
        Assert.Equal(18, read.BiggestHit);
    }

    [Fact]
    public void The_next_hit_kills_when_the_biggest_blow_takes_the_health_left()
    {
        var read = R2b(3).Read(5.28);
        Assert.True(read.NextHitKills(8));
        Assert.True(read.NextHitKills(18));
        Assert.False(read.NextHitKills(19));
    }

    [Fact]
    public void Survives_is_the_incoming_over_the_seconds_and_no_killing_blow_due_inside_them()
    {
        var read = R2b(2).Read(3.81); // 12.5 a hit every 3.31 s
        Assert.True(read.Survives(24, 2.85)); // r2b's first decision: the bolt was worth it at 24 of 49
        Assert.False(read.Survives(8, 3.4));  // the next blow is due at 3.3 s and it takes the 8 left
        Assert.True(read.Survives(8, 1.0));   // a one-second cast lands before it
        Assert.False(R2b(3).Read(5.28).Survives(8, 2.85));
    }

    [Fact]
    public void A_silence_of_two_intervals_ends_the_read_back_to_the_plain_average_a_dead_attacker_hits_no_more()
    {
        var read = R2b(3).Read(5.27 + (2.1 * 1.47));
        Assert.False(read.Live);
        Assert.Equal(41 / RecentHits.AverageSeconds, read.PerSecond, 6);
        Assert.False(read.NextHitKills(1));
    }

    [Fact]
    public void Two_hits_in_one_tick_are_two_attackers_not_a_twenty_millisecond_swing()
    {
        var hits = new RecentHits();
        hits.Note(10.00, 10);
        hits.Note(10.02, 10);
        Assert.Equal(10 / RecentHits.MinAttackSeconds, hits.Read(10.02).PerSecond, 6);
    }

    [Fact]
    public void The_read_never_falls_under_the_plain_average()
    {
        var hits = new RecentHits();
        for (var i = 0; i < 10; i++)
        {
            hits.Note(i * 0.3, 50);
        }

        hits.Note(9.0, 1);
        hits.Note(9.5, 1); // the last two are small: the ten seconds behind them still count
        Assert.Equal(502 / RecentHits.AverageSeconds, hits.Read(9.6).PerSecond, 6);
    }
}
