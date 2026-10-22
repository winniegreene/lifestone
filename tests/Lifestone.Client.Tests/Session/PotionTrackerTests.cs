using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 3.1 s1: <c>Drink</c> over the order ACE really sends (cache/m4/3-1/capture.md C3, live): the Drink F74C at +10 ms, at +0.74 s the 02E9, the line "The Potion of Healing restores N points of your Health.", the sound and the 0197 (or 0024 for the
/// last potion), the Ready F74C at +0.76 s, the UseDone 0 at +1.48 s. Confirmed on the UseDone 0 with the stack fallen or the item removed; a potion drunk at a full vital is spent and Wasted.
/// </summary>
public sealed class PotionTrackerTests
{
    private const uint Potion = HealRig.Potion;

    /// <summary>The send, the Drink F74C at +10 ms and the clock at +0.74 s.</summary>
    private static (HealRig Rig, HealHandle Handle) Sent(uint potions = 5)
    {
        var r = new HealRig(potions: potions);
        var handle = r.DrinkPotion();
        r.Tick(0.01);
        r.ActionMotion(HealRig.Drink);
        r.Tick(0.73);
        return (r, handle);
    }

    [Fact]
    public void Drink_sends_0x0036_Use_for_the_potion()
    {
        var r = new HealRig();
        var handle = r.DrinkPotion();

        Assert.Equal(Potion, Assert.Single(r.R.H.Net.Of(FakeSendKind.Use)).Guid);
        Assert.Empty(r.R.H.Net.Of(FakeSendKind.UseWithTarget));
        Assert.Equal(ActionKind.Drink, handle.Kind);
    }

    [Fact]
    public void A_potion_restores_25_and_is_Confirmed_at_the_UseDone_not_at_the_effect()
    {
        var (r, handle) = Sent();

        // C3 (55721): 02E9 Health, F7E0 "restores 25 points", F750, 0197 stack 5 -> 4, the Ready at +0.76 s; the UseDone only at +1.48 s.
        r.Vital(HealRig.Health, 72);
        r.Line("The Potion of Healing restores 25 points of your Health.");
        r.Stack(Potion, 4);
        r.Tick(0.02);
        r.Ready();
        r.Tick();
        Assert.False(handle.IsCompleted);

        r.Tick(0.72);
        r.UseDone();
        r.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.Confirmed, outcome.Kind);
        Assert.Contains("restored 25 Health", outcome.Detail, StringComparison.Ordinal);
        var result = Assert.IsType<HealResult>(handle.Result);
        Assert.Equal(HealResultKind.Drank, result.Kind);
        Assert.Equal(25, result.Amount);
        Assert.Equal("Health", result.Vital);
        Assert.Equal(4, result.Left);
        Assert.True(result.Restored);
    }

    [Fact]
    public void A_potion_at_a_full_vital_is_spent_and_Confirmed_as_Wasted()
    {
        var (r, handle) = Sent();

        // C3: 188/188 health, the Drink F74C, 0197 stack 4 -> 3, F7E0 "restores 0 points of your Health.", no 02E9, UseDone 0 at +1.48 s.
        r.Line("The Potion of Healing restores 0 points of your Health.");
        r.Stack(Potion, 4);
        r.Tick(0.74);
        r.UseDone();
        r.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.Confirmed, outcome.Kind);
        Assert.True(outcome.Succeeded);
        Assert.Contains("wasted", outcome.Detail, StringComparison.Ordinal);
        var result = Assert.IsType<HealResult>(handle.Result);
        Assert.Equal(HealResultKind.Wasted, result.Kind);
        Assert.Equal(0, result.Amount);
        Assert.False(result.Restored);
    }

    [Fact]
    public void The_last_potion_ends_in_0024_and_is_Confirmed_with_no_stack_left()
    {
        var (r, handle) = Sent(potions: 1);
        r.Vital(HealRig.Health, 72);
        r.Line("The Potion of Healing restores 25 points of your Health.");
        r.Removed(Potion);
        r.Tick(0.74);
        r.UseDone();
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, handle.Outcome!.Value.Kind);
        var result = Assert.IsType<HealResult>(handle.Result);
        Assert.Equal(HealResultKind.Drank, result.Kind);
        Assert.Equal(0, result.Left);
        Assert.False(r.R.H.State.Self.Inventory.Contains(Potion));
    }

    [Fact]
    public void A_stamina_potion_reads_its_own_vital()
    {
        var (r, handle) = Sent();
        r.Line("The Potion of Stamina restores 25 points of your Stamina.");
        r.Stack(Potion, 4);
        r.UseDone();
        r.Tick();

        var result = Assert.IsType<HealResult>(handle.Result);
        Assert.Equal("Stamina", result.Vital);
        Assert.Equal(25, result.Amount);
    }

    [Fact]
    public void A_UseDone_with_a_code_is_a_ServerError_with_the_code_named()
    {
        var r = new HealRig();
        var handle = r.DrinkPotion();
        r.UseDone(0x001D);
        r.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.ServerError, outcome.Kind);
        Assert.Equal(0x001Du, outcome.Code);
        Assert.Contains("0x001D", outcome.Detail, StringComparison.Ordinal);
        Assert.Contains("YoureTooBusy", outcome.Detail, StringComparison.Ordinal);
        Assert.Null(handle.Result);
    }

    [Theory]
    [InlineData(0x001Du, "too busy")]
    [InlineData(0x04EBu, "in the air")]
    public void A_028A_refusal_then_UseDone_0_is_a_ServerError_with_the_refusal_s_code_and_text(uint code, string text)
    {
        var r = new HealRig();
        var handle = r.DrinkPotion();

        // The spec (W/Food.cs): a refusal is the 028A and then UseDone 0, never UseDone with a code; the stack did not change.
        r.Error(code);
        r.UseDone();
        r.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.ServerError, outcome.Kind);
        Assert.Equal(code, outcome.Code);
        Assert.Contains(text, outcome.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Null(handle.Result);
    }

    [Fact]
    public void A_silent_UseDone_0_with_no_stack_change_is_a_ServerError_that_nothing_happened()
    {
        var (r, handle) = Sent();
        r.UseDone();
        r.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.ServerError, outcome.Kind);
        Assert.Contains("nothing happened", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stack_that_did_not_fall_does_not_confirm_a_drink()
    {
        var (r, handle) = Sent();
        r.Line("The Potion of Healing restores 25 points of your Health.");
        r.Stack(Potion, 5); // the same size as before the send: a pick-up or a merge, not this drink
        r.UseDone();
        r.Tick();

        Assert.Equal(ActionOutcomeKind.ServerError, handle.Outcome!.Value.Kind);
        Assert.Contains("nothing happened", handle.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_drink_that_never_ends_times_out_after_20_seconds()
    {
        var r = new HealRig();
        var handle = r.DrinkPotion();
        r.Tick(19.9);
        Assert.False(handle.IsCompleted);
        r.Tick(0.2);

        Assert.Equal(ActionOutcomeKind.TimedOut, handle.Outcome!.Value.Kind);
    }
}
