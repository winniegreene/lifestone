using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>M4 3.1 s1: the hunt's heal counters. One <c>Record</c> a finished heal handle; only a Confirmed handle counts, and the line's kind picks the counter.</summary>
public sealed class HealTallyTests
{
    private static long _id;

    private static HealHandle Done(ActionKind kind, ActionOutcomeKind outcome, HealResult? result = null)
    {
        var handle = new HealHandle(++_id, kind, ActionSource.Host, "test", 0);
        handle.TryComplete(result, new ActionOutcome(outcome), 1, static (_, e) => throw e);
        return handle;
    }

    private static HealHandle Kit(HealResult? result, ActionOutcomeKind outcome = ActionOutcomeKind.Confirmed) => Done(ActionKind.UseWithTarget, outcome, result);

    private static HealHandle Potion(HealResult? result, ActionOutcomeKind outcome = ActionOutcomeKind.Confirmed) => Done(ActionKind.Drink, outcome, result);

    [Fact]
    public void A_new_tally_is_all_zero()
    {
        var t = new HealTally();
        Assert.Equal((0, 0, 0, 0, 0, 0), (t.KitUses, t.Heals, t.FailedRolls, t.Potions, t.Wasted, t.Points));
    }

    [Fact]
    public void A_won_kit_roll_counts_a_use_a_heal_and_its_points()
    {
        var t = new HealTally();
        t.Record(Kit(new HealResult(HealResultKind.Healed, 73, "Health", 14)));

        Assert.Equal((1, 1, 0, 0, 0, 73), (t.KitUses, t.Heals, t.FailedRolls, t.Potions, t.Wasted, t.Points));
    }

    [Fact]
    public void A_lost_kit_roll_counts_a_use_and_a_failed_roll_but_no_points()
    {
        var t = new HealTally();
        t.Record(Kit(new HealResult(HealResultKind.FailedRoll, 0, string.Empty, 19)));

        Assert.Equal((1, 0, 1, 0, 0, 0), (t.KitUses, t.Heals, t.FailedRolls, t.Potions, t.Wasted, t.Points));
    }

    [Fact]
    public void A_potion_counts_a_drink_and_its_points_and_a_wasted_one_counts_no_points()
    {
        var t = new HealTally();
        t.Record(Potion(new HealResult(HealResultKind.Drank, 25, "Health", 4)));
        t.Record(Potion(new HealResult(HealResultKind.Wasted, 0, "Health", 3)));

        Assert.Equal((0, 0, 0, 2, 1, 25), (t.KitUses, t.Heals, t.FailedRolls, t.Potions, t.Wasted, t.Points));
    }

    [Fact]
    public void The_captured_run_five_failed_rolls_in_six_at_low_health_is_six_kit_uses()
    {
        // capture.md C1: at 43/188 health 5 of 6 uses failed; the sixth healed 73.
        var t = new HealTally();
        for (var i = 0; i < 5; i++)
        {
            t.Record(Kit(new HealResult(HealResultKind.FailedRoll, 0, string.Empty, 19 - i)));
        }

        t.Record(Kit(new HealResult(HealResultKind.Healed, 73, "Health", 14)));

        Assert.Equal((6, 1, 5, 0, 0, 73), (t.KitUses, t.Heals, t.FailedRolls, t.Potions, t.Wasted, t.Points));
    }

    [Theory]
    [InlineData(ActionOutcomeKind.ServerError)]
    [InlineData(ActionOutcomeKind.Refused)]
    [InlineData(ActionOutcomeKind.TimedOut)]
    [InlineData(ActionOutcomeKind.Cancelled)]
    [InlineData(ActionOutcomeKind.DryRun)]
    public void A_handle_that_did_not_end_Confirmed_counts_nothing(ActionOutcomeKind outcome)
    {
        var t = new HealTally();
        t.Record(Kit(new HealResult(HealResultKind.Healed, 73, "Health", 14), outcome));
        t.Record(Potion(new HealResult(HealResultKind.Drank, 25, "Health", 4), outcome));

        Assert.Equal((0, 0, 0, 0, 0, 0), (t.KitUses, t.Heals, t.FailedRolls, t.Potions, t.Wasted, t.Points));
    }

    [Fact]
    public void A_handle_still_in_flight_counts_nothing()
    {
        var t = new HealTally();
        t.Record(new HealHandle(++_id, ActionKind.UseWithTarget, ActionSource.Host, "test", 0));

        Assert.Equal(0, t.KitUses);
    }

    [Fact]
    public void A_confirmed_use_whose_line_was_not_read_counts_the_use_only()
    {
        var t = new HealTally();
        t.Record(Kit(null));
        t.Record(Potion(null));

        Assert.Equal((1, 0, 0, 1, 0, 0), (t.KitUses, t.Heals, t.FailedRolls, t.Potions, t.Wasted, t.Points));
    }

    [Fact]
    public void A_handle_of_another_kind_is_ignored_and_null_is_refused()
    {
        var t = new HealTally();
        t.Record(Done(ActionKind.Use, ActionOutcomeKind.Confirmed, new HealResult(HealResultKind.Healed, 73, "Health", 14)));

        Assert.Equal(0, t.Points);
        Assert.Throws<ArgumentNullException>(() => t.Record(null!));
    }
}
