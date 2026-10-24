using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Tests.Session.Contracts;

/// <summary>
/// M4 3.1 s1: <c>HealResult.TryParse</c> over the three lines ACE sends for a heal (<c>Healer.DoHealing</c> and <c>Food.ApplyBoost</c>, ACE 95ef426d; the first two confirmed live in cache/m4/3-1/capture.md C1 and C3): a won kit roll, a lost
/// kit roll and a potion, with the "expertly" critical, the singular and used-up tails, and the text that is not a heal. A parse allocates nothing but the result.
/// </summary>
public sealed class HealResultTests
{
    private static HealResult? Sink { get; set; }

    [Fact]
    public void A_won_kit_roll_reads_the_amount_the_vital_and_the_uses_left()
    {
        Assert.True(HealResult.TryParse("You heal yourself for 73 Health points. Your Handy Healing Kit has 14 uses left.", out var r));
        Assert.Equal(new HealResult(HealResultKind.Healed, 73, "Health", 14), r);
        Assert.False(r.Critical);
        Assert.True(r.Restored);
    }

    [Fact]
    public void The_expertly_prefix_is_a_critical()
    {
        Assert.True(HealResult.TryParse("You expertly heal yourself for 110 Health points. Your Handy Healing Kit has 13 uses left.", out var r));
        Assert.Equal(new HealResult(HealResultKind.Healed, 110, "Health", 13, true), r);
    }

    [Theory]
    [InlineData("You heal yourself for 5 Health points. Your Handy Healing Kit has 1 use left.", 1)]
    [InlineData("You heal yourself for 5 Health points. Your Handy Healing Kit is used up.", 0)]
    [InlineData("You heal yourself for 5 Health points.", HealResult.Unknown)]
    [InlineData("You heal yourself for 5 Health points. Something else.", HealResult.Unknown)]
    public void The_tail_gives_the_uses_left_singular_used_up_or_nothing(string line, int left)
    {
        Assert.True(HealResult.TryParse(line, out var r));
        Assert.Equal(HealResultKind.Healed, r.Kind);
        Assert.Equal(5, r.Amount);
        Assert.Equal(left, r.Left);
    }

    [Theory]
    [InlineData("You heal yourself for 30 Stamina points. Your Stamina Kit has 9 uses left.", "Stamina")]
    [InlineData("You heal yourself for 30 Spirit points. Your Kit has 9 uses left.", "Spirit")]
    public void The_vital_is_the_kit_s_own(string line, string vital)
    {
        Assert.True(HealResult.TryParse(line, out var r));
        Assert.Equal(vital, r.Vital);
        Assert.Equal(30, r.Amount);
    }

    [Fact]
    public void A_lost_kit_roll_is_a_FailedRoll_with_the_uses_left_and_no_vital()
    {
        Assert.True(HealResult.TryParse("You fail to heal yourself. Your Handy Healing Kit has 19 uses left.", out var r));
        Assert.Equal(new HealResult(HealResultKind.FailedRoll, 0, string.Empty, 19), r);
        Assert.False(r.Restored);
    }

    [Theory]
    [InlineData("You fail to heal yourself. Your Handy Healing Kit is used up.", 0)]
    [InlineData("You fail to heal yourself.", HealResult.Unknown)]
    public void A_lost_roll_s_tail_reads_the_same_way(string line, int left)
    {
        Assert.True(HealResult.TryParse(line, out var r));
        Assert.Equal(HealResultKind.FailedRoll, r.Kind);
        Assert.Equal(left, r.Left);
    }

    [Fact]
    public void A_potion_line_reads_the_restore_and_the_vital()
    {
        Assert.True(HealResult.TryParse("The Potion of Healing restores 25 points of your Health.", out var r));
        Assert.Equal(new HealResult(HealResultKind.Drank, 25, "Health"), r);
        Assert.Equal(HealResult.Unknown, r.Left); // the stack left is not in the line: the tracker fills it from the 0197
        Assert.True(r.Restored);
    }

    [Theory]
    [InlineData("The Potion of Stamina restores 25 points of your Stamina.", "Stamina", 25)]
    [InlineData("The Handy Healing Kit restorer restores 7 points of your Health.", "Health", 7)]
    public void A_potion_of_another_vital_reads_its_own(string line, string vital, int amount)
    {
        Assert.True(HealResult.TryParse(line, out var r));
        Assert.Equal(HealResultKind.Drank, r.Kind);
        Assert.Equal(vital, r.Vital);
        Assert.Equal(amount, r.Amount);
    }

    [Fact]
    public void A_potion_that_restores_0_points_is_Wasted()
    {
        Assert.True(HealResult.TryParse("The Potion of Healing restores 0 points of your Health.", out var r));
        Assert.Equal(HealResultKind.Wasted, r.Kind);
        Assert.Equal(0, r.Amount);
        Assert.Equal("Health", r.Vital);
        Assert.False(r.Restored);
    }

    [Theory]
    [InlineData("")]
    [InlineData("You heal Bob for 12 Health points. Your Handy Healing Kit has 14 uses left.")]
    [InlineData("You fail to heal Bob. Your Handy Healing Kit has 14 uses left.")]
    [InlineData("You hit the Tusker Crimsonback for 12 points of damage.")]
    [InlineData("The Potion of Healing takes 5 points of your Health.")]
    [InlineData("The Tusker Crimsonback restores nothing.")]
    [InlineData("You heal yourself for Health points.")]
    [InlineData("You heal yourself for 73 Health points")]
    [InlineData("You heal yourself for 99999999999 Health points.")]
    [InlineData("You heal yourself for 73 points.")]
    [InlineData("The Potion of Healing restores 25 points of your Health")]
    [InlineData("The Potion of Healing restores points of your Health.")]
    [InlineData("The Potion of Healing restores 25 points of your .")]
    [InlineData("Your Handy Healing Kit has 14 uses left.")]
    [InlineData("You heal yourself")]
    [InlineData("The")]
    public void Text_that_is_not_a_heal_is_not_read(string line)
    {
        Assert.False(HealResult.TryParse(line, out var r));
        Assert.Null(r);
    }

    [Fact]
    public void Parsing_a_line_that_is_not_a_heal_allocates_nothing()
    {
        string[] lines =
        [
            "You hit the Tusker Crimsonback for 12 points of damage.",
            "You heal Bob for 12 Health points. Your Handy Healing Kit has 14 uses left.",
            "The Tusker Crimsonback restores nothing.",
            "Welcome to Asheron's Call!",
        ];
        AllocationAssert.Nothing(() =>
        {
            foreach (var line in lines)
            {
                HealResult.TryParse(line, out _);
            }
        });
    }

    [Theory]
    [InlineData("You heal yourself for 73 Health points. Your Handy Healing Kit has 14 uses left.")]
    [InlineData("You fail to heal yourself. Your Handy Healing Kit has 19 uses left.")]
    [InlineData("The Potion of Healing restores 25 points of your Health.")]
    public void Parsing_a_heal_line_allocates_exactly_the_result(string line)
    {
        // One HealResult is the whole allocation: the vitals' names are literals and the tail is read in place. The sink keeps the JIT from stack-allocating the comparison object.
        HealResult.TryParse(line, out var warm);
        Sink = warm;
        var before = GC.GetAllocatedBytesForCurrentThread();
        HealResult.TryParse(line, out var read);
        Sink = read;
        var parsed = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        Sink = new HealResult(HealResultKind.Healed, 1, "Health", 1, true);
        var one = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(one > 0);
        Assert.Equal(one, parsed);
    }
}
