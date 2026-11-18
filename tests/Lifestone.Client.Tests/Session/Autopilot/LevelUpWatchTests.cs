using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6's hero video (the owner, 2026-11-18): when her next level is near, the attributes page is up so the level-up shows with the clock (<see cref="LevelUpWatch"/>). Near is read live: the experience left is at most
/// two of her largest gains this run; the page stays up 8 s after the level changes.
/// </summary>
public sealed class LevelUpWatchTests
{
    private static readonly ulong[] LevelXp = [0, 0, 1_000, 3_000, 6_000]; // cumulative, by level (a test table: level 2 at 1,000, level 3 at 3,000)

    private static PlayerQualities Her(int level, long total)
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Ints[PlayerQualities.LevelProperty] = level;
        q.Int64s[PlayerQualities.TotalExperienceProperty] = total;
        return q;
    }

    [Fact]
    public void The_page_is_wanted_within_two_of_her_largest_gains_and_held_eight_seconds_past_the_level()
    {
        var lines = new List<string>();
        var watch = new LevelUpWatch(lines.Add);

        watch.Tick(true, Her(2, 1_000), LevelXp, 0);
        watch.Tick(true, Her(2, 1_300), LevelXp, 1); // a gain of 300; 1,700 left > 600
        Assert.False(watch.WantsPage);
        Assert.Equal(300, watch.LargestGain);

        watch.Tick(true, Her(2, 2_400), LevelXp, 2); // a gain of 1,100; 600 left <= 2,200
        Assert.True(watch.WantsPage);

        watch.Tick(true, Her(3, 3_200), LevelXp, 3); // the level: held from here
        Assert.True(watch.WantsPage);
        watch.Tick(true, Her(3, 3_200), LevelXp, 10.9); // 2,800 left to level 4, more than 2 x 1,100
        Assert.True(watch.WantsPage);
        watch.Tick(true, Her(3, 3_200), LevelXp, 11.0);
        Assert.False(watch.WantsPage);
        Assert.Equal(3, lines.Count); // opens, the level, given back
    }

    [Fact]
    public void Nothing_is_wanted_before_a_gain_without_a_table_or_while_the_autopilot_is_off()
    {
        var watch = new LevelUpWatch(_ => { });
        watch.Tick(true, Her(2, 2_990), LevelXp, 0); // 10 left, but no gain seen yet
        Assert.False(watch.WantsPage);
        watch.Tick(true, Her(2, 2_995), null, 1);
        Assert.False(watch.WantsPage);
        watch.Tick(false, Her(2, 2_999), LevelXp, 2);
        Assert.False(watch.WantsPage);
        Assert.Equal(0, watch.LargestGain);
    }
}
