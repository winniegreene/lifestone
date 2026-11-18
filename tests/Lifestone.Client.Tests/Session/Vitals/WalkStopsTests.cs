using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// M6 14 the crowded passage, from live ladder44 (the Crater Pathway 0x01A7, 2026-11-16 22:26:23-22:30:07 UTC): the walk to the Surface portal's front stood 457, 285, 268, 266, 255, 182, 165 m from it at its first seven stops and 95 m at
/// the eighth; the eight fight-backs made 4, 8, 3, 2, 1, 2, 3, 1 casts for as many kills. The old cap of 8 stops recalled her there; the rule fights on while she gains and kills, and retreats when she stalls or kills nothing.
/// </summary>
public sealed class WalkStopsTests
{
    private static readonly WalkStop[] Ladder44 =
    [
        new(457, 4, 4), new(285, 8, 8), new(268, 3, 3), new(266, 2, 2), new(255, 1, 1), new(182, 2, 2), new(165, 3, 3),
    ];

    [Fact]
    public void The_live_walk_that_gained_and_killed_at_every_stop_fights_on_at_its_eighth()
    {
        var v = WalkStops.Judge(Ladder44, 95);
        Assert.False(v.Retreat, v.Why);
        Assert.StartsWith("gaining: 160 m on the goal over the last 3 stops (255 m then, 95 m now), their fight-backs 6 kill(s) in 6 cast(s)", v.Why, StringComparison.Ordinal);
        Assert.False(WalkStops.Judge(Ladder44.Take(2).ToList(), 268).Retreat); // fewer than three stops: nothing to judge
    }

    [Fact]
    public void A_walk_that_stalls_retreats()
    {
        WalkStop[] stuck = [new(120, 2, 2), new(120.4, 1, 1), new(119.8, 2, 2)];
        var v = WalkStops.Judge(stuck, 119.5);
        Assert.True(v.Retreat);
        Assert.StartsWith("stalled: 0.5 m gained on the goal over the last 3 stops", v.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_walk_whose_fight_backs_kill_nothing_retreats_while_it_still_gains()
    {
        WalkStop[] futile = [new(300, 5, 0), new(250, 4, 0), new(200, 6, 0)];
        var v = WalkStops.Judge(futile, 150);
        Assert.True(v.Retreat);
        Assert.Equal("not winning: the last 3 fight-backs made 15 cast(s) and no kill", v.Why);
    }
}
