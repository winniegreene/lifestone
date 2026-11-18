using System.Numerics;
using Lifestone.Client.Session.Vitals;
using Xunit;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// M6 14 run past (D137, the owner's amendment of M5 D88 on travel legs): the rule for one attacker against her run of 7.7 m/s (her buffed Run on 2026-11-14), and the seconds she stays in a thrower's range.
/// </summary>
public sealed class RunPastTests
{
    private static RunPastHer Her(bool blocked = false, bool atGoal = false, double above = 150) => new(7.7, "her Run 102", blocked, atGoal, above, "35% (the attackers' line)");

    private static RunPastAttacker Melee(double? speed, int hits, double metres = 3) =>
        new(0x80000001, "Tusker", metres, speed is { } s ? new Pace(s, "its facts") : null, "no motion table is known for it", false, 0, hits, 0, 0);

    private static RunPastAttacker Thrower(double speed, double perSecond, double seconds) =>
        new(0x80000002, "Chittick", 25, new Pace(speed, "its facts"), string.Empty, true, 40.8, 1, perSecond, seconds);

    private static (RunPastCall, string) Call(in RunPastHer her, in RunPastAttacker a) => (RunPast.Judge(her, a).Call, RunPast.Judge(her, a).Reason);

    /// <summary>
    /// A melee attacker under her speed less a tenth (6.93 m/s) is left behind and named with both speeds; one at or over it beyond melee reach, or whose pace is not known, is run on from until it hits again (<c>measuring</c>), then
    /// fought (<c>keeps-pace</c>); one under it that still hits a running body keeps pace too. Blocked in melee, it is fought; at the door, any attacker still on her is.
    /// </summary>
    [Fact]
    public void She_runs_on_from_what_she_outpaces_and_fights_what_keeps_pace_blocks_her_or_waits_at_the_door()
    {
        var left = RunPast.Judge(Her(), Melee(5.5, 0));
        Assert.Equal((RunPastCall.RunOn, "outrun"), (left.Call, left.Reason));
        Assert.Equal("'Tusker' 0x80000001 at 3.0 m is left behind: its 5.5 m/s (its facts) against her 7.7 m/s (her Run 102)", left.Why);
        Assert.Equal((RunPastCall.RunOn, "measuring"), Call(Her(), Melee(7.0, 0, metres: 8))); // within a tenth of her, beyond melee: it keeps pace and is measured
        Assert.Equal((RunPastCall.Fight, "keeps-pace"), Call(Her(), Melee(17, 1, metres: 8)));
        Assert.Equal((RunPastCall.RunOn, "measuring"), Call(Her(), Melee(null, 0)));
        Assert.Equal((RunPastCall.Fight, "keeps-pace"), Call(Her(), Melee(null, 1)));
        var still = RunPast.Judge(Her(), Melee(5.5, 2));
        Assert.Equal((RunPastCall.Fight, "keeps-pace"), (still.Call, still.Reason));
        Assert.Contains("keeps hitting a running body though its pace is under hers", still.Why, StringComparison.Ordinal);

        Assert.Equal((RunPastCall.Fight, "blocked"), Call(Her(blocked: true), Melee(5.5, 0, metres: 2)));
        Assert.Equal((RunPastCall.RunOn, "outrun"), Call(Her(blocked: true), Melee(5.5, 0, metres: 12))); // not in her way
        Assert.Equal((RunPastCall.Fight, "at-goal"), Call(Her(atGoal: true), Melee(5.5, 0)));
        Assert.True(RunPast.KeepsPace(6.94, 7.7) && !RunPast.KeepsPace(6.92, 7.7)); // her 7.7 less a tenth is 6.93
    }

    /// <summary>
    /// M6 14 caught on the run (the owner's ruling, 2026-11-15: asked whether she was fighting or running; live ladder25 04:08:00 UTC, her run 7.9 m/s): a melee attacker whose known pace keeps pace with hers and that stands within melee reach has caught her,
    /// so she fights it at once (<c>caught</c>, both speeds in the words), before any second hit: the Tusker Guard of 13.8 m/s at 2.2 m, the Armored Tusker of 16.0 at 2.9 m, the Silver Tusker of 17.0 at 2.6 m. Beyond melee reach the same
    /// pace is measured; a pace not known in reach is measured until it hits again; a slower one in reach is left behind.
    /// </summary>
    [Fact]
    public void A_melee_attacker_that_keeps_pace_within_melee_reach_has_caught_her_and_is_fought_at_once()
    {
        var her = new RunPastHer(7.9, "her Run 108: rate 1.964 x 4 m/s", false, false, 150, "15% (the attackers' line)");
        RunPastAttacker Tusker(string name, double speed, double metres) => new(0x80005928, name, metres, new Pace(speed, "its facts"), string.Empty, false, 0, 0, 0, 0);
        var guard = RunPast.Judge(her, Tusker("Tusker Guard", 13.8, 2.2));
        Assert.Equal((RunPastCall.Fight, "caught"), (guard.Call, guard.Reason));
        Assert.Equal("'Tusker Guard' 0x80005928 at 2.2 m keeps pace with her and stands within the 5 m of melee: it has caught her and she cannot outrun it, so she fights it at once (its 13.8 m/s (its facts) against her 7.9 m/s (her Run 108: rate 1.964 x 4 m/s))", guard.Why);
        Assert.Equal((RunPastCall.Fight, "caught"), Call(her, Tusker("Armored Tusker", 16.0, 2.9)));
        Assert.Equal((RunPastCall.Fight, "caught"), Call(her, Tusker("Silver Tusker", 17.0, 2.6)));
        Assert.Equal((RunPastCall.Fight, "caught"), Call(her, Tusker("Silver Tusker", 17.0, RunPast.MeleeReachMetres))); // at the edge of melee
        var closing = RunPast.Judge(her, Tusker("Silver Tusker", 17.0, 9));
        Assert.Equal((RunPastCall.RunOn, "measuring"), (closing.Call, closing.Reason));
        Assert.Contains("is as fast as her but not yet within the 5 m of melee: she runs on and fights it when it closes or hits again", closing.Why, StringComparison.Ordinal);
        Assert.Equal((RunPastCall.RunOn, "measuring"), Call(her, Melee(null, 0, metres: 2.2))); // a pace not known: measured until it hits again
        Assert.Equal((RunPastCall.RunOn, "outrun"), Call(her, Tusker("Reedshark Ravager", 5.0, 2.2))); // slower: left behind
        Assert.Equal((RunPastCall.Fight, "at-goal"), Call(her with { AtGoal = true }, Tusker("Tusker Guard", 13.8, 2.2))); // the door's rule stands first
    }

    /// <summary>
    /// M6 14 caught on the run (live ladder27 06:10:19.6-20.7 UTC, her run 7.9 m/s): a fight-back's set was spent while three Stinging Chitticks of 5.6 m/s bit her at 2.4 to 4.2 m; judged outrun, she sheathed, the walk began and the
    /// run past stopped it again within the second. While she stands to fight, a melee attacker within reach is on her and is fought before she runs on, whatever its pace (<c>on-her</c>); one beyond reach is still left to the run, a
    /// ranged one keeps its own rule, and on the run the slower one is left behind as before.
    /// </summary>
    [Fact]
    public void While_she_stands_to_fight_a_slower_melee_attacker_on_her_is_fought_before_she_runs_on()
    {
        var her = new RunPastHer(7.9, "her Run 108: rate 1.964 x 4 m/s", false, false, 150, "15% (the attackers' line)");
        var standing = her with { Standing = true };
        RunPastAttacker Chittick(double metres) => new(0x8000B21E, "Stinging Chittick", metres, new Pace(5.6, "its facts"), string.Empty, false, 0, 0, 0, 0);
        var onHer = RunPast.Judge(standing, Chittick(2.4));
        Assert.Equal((RunPastCall.Fight, "on-her"), (onHer.Call, onHer.Reason));
        Assert.Contains("stands in melee and attacks her while she stands to fight: it is on her, so she fights it before she runs on, whatever its pace (its 5.6 m/s (its facts) against her 7.9 m/s", onHer.Why, StringComparison.Ordinal);
        Assert.Equal((RunPastCall.Fight, "on-her"), Call(standing, Chittick(RunPast.MeleeReachMetres))); // at the edge of melee
        Assert.Equal((RunPastCall.RunOn, "outrun"), Call(standing, Chittick(8))); // beyond reach: still left to the run
        Assert.Equal((RunPastCall.RunOn, "outrun"), Call(her, Chittick(2.4))); // on the run: the slower one is left behind
        Assert.Equal((RunPastCall.RunOn, "passing"), Call(standing, Thrower(5.6, 2, 5))); // a ranged one keeps its own rule
    }

    /// <summary>
    /// A ranged attacker is paid in health: its measured damage a second over the seconds left in its range on her way; she fights it only when that would bring her to the retreat line before she is out of range, or when it keeps pace
    /// (it never leaves her). The seconds in range are the line's chord to the far edge of its circle; a line that never enters it is none.
    /// </summary>
    [Fact]
    public void A_ranged_attacker_is_passed_unless_its_hits_would_reach_the_retreat_line_before_she_is_out_of_range()
    {
        var passing = RunPast.Judge(Her(), Thrower(5.6, 2, 5));
        Assert.Equal((RunPastCall.RunOn, "passing"), (passing.Call, passing.Reason));
        Assert.Contains("2 damage a second (measured) over 5.0 s left in its 41 m range: about 10, against 150 over the retreat line", passing.Why, StringComparison.Ordinal);
        var line = RunPast.Judge(Her(above: 60), Thrower(5.6, 8, 10));
        Assert.Equal((RunPastCall.Fight, "ranged-line"), (line.Call, line.Reason));
        Assert.Contains("would bring her to the retreat line before she is out of its range", line.Why, StringComparison.Ordinal);
        Assert.Equal((RunPastCall.Fight, "ranged-line"), Call(Her(), Thrower(9, 1, 5))); // as fast as her: it keeps her in range

        Assert.Equal(10 + Math.Sqrt(375), RunPast.SecondsInRange(Vector2.Zero, new Vector2(1, 0), 1, new Vector2(10, 5), 20), 3);
        Assert.Equal((10 + Math.Sqrt(375)) / 5, RunPast.SecondsInRange(Vector2.Zero, new Vector2(3, 0), 5, new Vector2(10, 5), 20), 3);
        Assert.Equal(0, RunPast.SecondsInRange(Vector2.Zero, new Vector2(1, 0), 5, new Vector2(0, 30), 20)); // her line never enters it
        Assert.Equal(double.PositiveInfinity, RunPast.SecondsInRange(Vector2.Zero, Vector2.Zero, 5, new Vector2(3, 0), 20)); // standing in it

        var wide = RunPast.MeanSecondsInRange(40.8, 22, 7.7);
        var narrow = RunPast.MeanSecondsInRange(10, 22, 7.7);
        Assert.InRange(wide, 22.0 / 7.7, (22.0 + 40.8) / 7.7); // noticed at 22 m at most, then out of 40.8 m
        Assert.True(narrow < wide && narrow > 0);
    }
}
