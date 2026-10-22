using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 3.1 s1: <c>UseWithTarget</c> with a healing kit over the order ACE really sends (cache/m4/3-1/capture.md C1, C2 and C4, live, ACE 95ef426d): the heal motion's F74C at +10 ms, at +1.48 s the line and the kit's 02CE Structure, the
/// UseDone 0 and the Ready F74C with it. A use is Confirmed on the UseDone 0 with the Structure fallen (a won roll and a lost roll alike), a UseDone with a code is a ServerError, and a silent UseDone 0 is "nothing happened".
/// </summary>
public sealed class HealTrackerTests
{
    private const uint Player = HealRig.Player;
    private const uint Kit = HealRig.Kit;

    /// <summary>A kit use up to the moment before the UseDone: the send, the heal motion at +10 ms, then 1.48 s.</summary>
    private static (HealRig Rig, HealHandle Handle) Sent()
    {
        var r = new HealRig();
        var handle = r.HealSelf();
        r.Tick(0.01);
        r.ActionMotion(HealRig.SkillHealSelf);
        r.Tick(1.47);
        Assert.False(handle.IsCompleted); // the effect and the UseDone come together at +1.48 s, not before
        return (r, handle);
    }

    [Fact]
    public void The_kit_use_sends_0x0035_with_the_kit_first_and_the_player_as_the_target()
    {
        var r = new HealRig();
        var handle = r.HealSelf();

        var sent = Assert.Single(r.R.H.Net.Of(FakeSendKind.UseWithTarget));
        Assert.Equal(Kit, sent.Guid);
        Assert.Equal(Player.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), sent.Target);
        Assert.Equal(ActionKind.UseWithTarget, handle.Kind);
        Assert.Equal(ActionStatus.Sent, handle.Status);
        Assert.Empty(r.R.H.Net.Of(FakeSendKind.Use));
    }

    [Fact]
    public void A_failed_roll_is_Confirmed_as_a_spent_use_with_nothing_healed()
    {
        var (r, handle) = Sent();

        // C1: F7E0 "You fail to heal yourself...", 02CE Structure 19, UseDone 0 (no 02E9, no 02CF); the Ready 10 ms later.
        r.Line("You fail to heal yourself. Your Handy Healing Kit has 19 uses left.");
        r.Structure(Kit, 19);
        r.UseDone();
        r.Tick(0.01);
        r.Ready();
        r.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.Confirmed, outcome.Kind);
        Assert.True(outcome.Succeeded);
        Assert.Contains("failed roll", outcome.Detail, StringComparison.Ordinal);
        Assert.Contains("19", outcome.Detail, StringComparison.Ordinal);
        var result = Assert.IsType<HealResult>(handle.Result);
        Assert.Equal(HealResultKind.FailedRoll, result.Kind);
        Assert.Equal(0, result.Amount);
        Assert.Equal(19, result.Left);
        Assert.False(result.Restored);
    }

    [Fact]
    public void A_successful_heal_is_Confirmed_with_the_amount_the_vital_and_the_uses_left()
    {
        var (r, handle) = Sent();

        // C1 (74584): 02E9 Stamina 103 -> 88, 02E9 Health 43 -> 116, F7E0 the line, 02CE Structure 14, UseDone 0, the Ready 30 ms later.
        r.Vital(HealRig.Stamina, 88);
        r.Vital(HealRig.Health, 116);
        r.Line("You heal yourself for 73 Health points. Your Handy Healing Kit has 14 uses left.");
        r.Structure(Kit, 14);
        Assert.False(handle.IsCompleted);
        r.UseDone();
        r.Tick(0.03);
        r.Ready();
        r.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.Confirmed, outcome.Kind);
        Assert.Contains("healed 73 Health", outcome.Detail, StringComparison.Ordinal);
        var result = Assert.IsType<HealResult>(handle.Result);
        Assert.Equal(HealResultKind.Healed, result.Kind);
        Assert.Equal(73, result.Amount);
        Assert.Equal("Health", result.Vital);
        Assert.Equal(14, result.Left);
        Assert.False(result.Critical);
        Assert.True(result.Restored);
    }

    [Fact]
    public void An_expertly_heal_is_a_critical_result()
    {
        var (r, handle) = Sent();
        r.Line("You expertly heal yourself for 110 Health points. Your Handy Healing Kit has 13 uses left.");
        r.Structure(Kit, 13);
        r.UseDone();
        r.Tick();

        var result = Assert.IsType<HealResult>(handle.Result);
        Assert.Equal(ActionOutcomeKind.Confirmed, handle.Outcome!.Value.Kind);
        Assert.True(result.Critical);
        Assert.Equal(110, result.Amount);
        Assert.Contains("critical", handle.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void The_uses_left_come_from_the_Structure_when_the_line_has_none()
    {
        var (r, handle) = Sent();
        r.Line("You heal yourself for 12 Health points.");
        r.Structure(Kit, 18);
        r.UseDone();
        r.Tick();

        Assert.Equal(18, Assert.IsType<HealResult>(handle.Result).Left);
    }

    [Fact]
    public void A_busy_refusal_UseDone_0x001D_is_a_ServerError_and_names_the_code()
    {
        var r = new HealRig();
        var handle = r.HealSelf();

        // C2: a repeat 0.2 s after the first use got UseDone 0x001D at +2 ms, with no 028A and no 028B.
        r.Tick(0.002);
        r.UseDone(0x001D);
        r.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.ServerError, outcome.Kind);
        Assert.Equal(0x001Du, outcome.Code);
        Assert.Contains("0x001D", outcome.Detail, StringComparison.Ordinal);
        Assert.Contains("YoureTooBusy", outcome.Detail, StringComparison.Ordinal);
        Assert.Null(handle.Result);
    }

    [Fact]
    public void An_untrained_healer_gets_UseDone_0x04FC_and_the_kit_keeps_its_uses()
    {
        var r = new HealRig();
        var handle = r.HealSelf();

        // C4 (BSD Mage): UseDone 0x04FC at +2 ms; no 028A, no 02EB, no motion, no 02CE Structure.
        r.Tick(0.002);
        r.UseDone(0x04FC);
        r.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.ServerError, outcome.Kind);
        Assert.Equal(0x04FCu, outcome.Code);
        Assert.Contains("0x04FC", outcome.Detail, StringComparison.Ordinal);
        Assert.Contains("YouArentTrainedInHealing", outcome.Detail, StringComparison.Ordinal);
        Assert.Contains("trained in healing", outcome.Detail, StringComparison.Ordinal);
        Assert.True(r.R.H.State.Self.Inventory.TryGetInt(Kit, 92, out var uses));
        Assert.Equal(20, uses);
    }

    [Theory]
    [InlineData(0x04FEu, "YouCantHealThat")]
    [InlineData(0x04EBu, "YouCantDoThatWhileInTheAir")]
    public void The_other_refusal_UseDones_are_ServerErrors_with_their_code_named(uint code, string name)
    {
        var r = new HealRig();
        var handle = r.HealSelf();
        r.UseDone(code);
        r.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.ServerError, outcome.Kind);
        Assert.Equal(code, outcome.Code);
        Assert.Contains("0x" + code.ToString("X4", System.Globalization.CultureInfo.InvariantCulture), outcome.Detail, StringComparison.Ordinal);
        Assert.Contains(name, outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void The_last_use_ends_in_0024_and_is_Confirmed_with_no_uses_left()
    {
        var (r, handle) = Sent();

        // ACE: the line "... is used up.", 02CE Structure 0, then the consumption (0024 and 02CD), then UseDone 0.
        r.Vital(HealRig.Stamina, 60);
        r.Vital(HealRig.Health, 150);
        r.Line("You heal yourself for 40 Health points. Your Handy Healing Kit is used up.");
        r.Structure(Kit, 0);
        r.Removed(Kit);
        r.UseDone();
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, handle.Outcome!.Value.Kind);
        var result = Assert.IsType<HealResult>(handle.Result);
        Assert.Equal(HealResultKind.Healed, result.Kind);
        Assert.Equal(40, result.Amount);
        Assert.Equal(0, result.Left);
        Assert.False(r.R.H.State.Self.Inventory.Contains(Kit));
    }

    [Fact]
    public void A_consumed_kit_confirms_even_when_only_the_0024_was_seen()
    {
        var (r, handle) = Sent();
        r.Line("You fail to heal yourself. Your Handy Healing Kit is used up.");
        r.Removed(Kit);
        r.UseDone();
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, handle.Outcome!.Value.Kind);
        var result = Assert.IsType<HealResult>(handle.Result);
        Assert.Equal(HealResultKind.FailedRoll, result.Kind);
        Assert.Equal(0, result.Left);
    }

    [Fact]
    public void A_silent_UseDone_0_is_a_ServerError_that_nothing_happened()
    {
        var r = new HealRig();
        var handle = r.HealSelf();

        // The spec: a kit not held, or no target, is a UseDone 0 with no other message.
        r.Tick(0.002);
        r.UseDone();
        r.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.ServerError, outcome.Kind);
        Assert.Equal(0u, outcome.Code);
        Assert.Contains("nothing happened", outcome.Detail, StringComparison.Ordinal);
        Assert.Null(handle.Result);
    }

    [Fact]
    public void A_Structure_that_did_not_fall_does_not_confirm_a_use()
    {
        var (r, handle) = Sent();
        r.Line("You heal yourself for 12 Health points. Your Handy Healing Kit has 20 uses left.");
        r.Structure(Kit, 20); // the same value as before the send
        r.UseDone();
        r.Tick();

        Assert.Equal(ActionOutcomeKind.ServerError, handle.Outcome!.Value.Kind);
        Assert.Contains("nothing happened", handle.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Null(handle.Result);
    }

    [Fact]
    public void A_Structure_that_fell_before_the_send_is_not_this_uses_proof()
    {
        var r = new HealRig();
        r.Structure(Kit, 19); // an earlier use's update, before this send
        var handle = r.HealSelf();
        r.Tick(1.48);
        r.UseDone();
        r.Tick();

        Assert.Equal(ActionOutcomeKind.ServerError, handle.Outcome!.Value.Kind);
        Assert.Contains("nothing happened", handle.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_full_health_refusal_0x028B_0x04FF_ends_the_use_as_a_refusal_with_its_text()
    {
        var r = new HealRig();
        var handle = r.HealSelf();

        // ACE sends the 028B and then UseDone 0 (W/Healer.cs:94); not captured live: TODO(m4-heal-full-vital-live).
        r.ErrorText(0x04FF, "Holtburg Hunter");
        r.UseDone();
        r.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.ServerError, outcome.Kind);
        Assert.Equal(0x04FFu, outcome.Code);
        Assert.Contains("Holtburg Hunter is already at full health!", outcome.Detail, StringComparison.Ordinal);
        Assert.Contains("TODO(m4-heal-full-vital-live)", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_full_stamina_02EB_text_ends_the_use_as_a_refusal_with_its_text()
    {
        var r = new HealRig();
        var handle = r.HealSelf();
        r.Transient("Holtburg Hunter is already at full stamina!");
        r.UseDone();
        r.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.ServerError, outcome.Kind);
        Assert.Equal(0u, outcome.Code);
        Assert.Contains("already at full stamina", outcome.Detail, StringComparison.Ordinal);
        Assert.Contains("TODO(m4-heal-full-vital-live)", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_use_that_never_ends_times_out_after_20_seconds_and_a_late_UseDone_is_dropped()
    {
        var r = new HealRig();
        var handle = r.HealSelf();
        r.Tick(19.9);
        Assert.False(handle.IsCompleted);
        r.Tick(0.2);

        Assert.Equal(ActionOutcomeKind.TimedOut, handle.Outcome!.Value.Kind);
        Assert.Equal(20.0, ActionPolicy.HealSeconds);

        // ACE's UseDone for it may still come: it belongs to the ended use, not to the next one.
        var next = r.HealSelf(HealRig.Kit2);
        r.UseDone();
        r.Tick();
        Assert.False(next.IsCompleted);
        r.Tick(1.5);
        r.Structure(HealRig.Kit2, 19);
        r.UseDone();
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, next.Outcome!.Value.Kind);
    }

    [Fact]
    public void The_heal_motion_and_another_players_Ready_are_not_the_Ready_that_frees_the_lane()
    {
        var (r, handle) = Sent();
        r.Line("You fail to heal yourself. Your Handy Healing Kit has 19 uses left.");
        r.Structure(Kit, 19);
        r.UseDone();
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, handle.Outcome!.Value.Kind);

        var queued = r.R.Api.UseWithTarget(HealRig.Kit2, Player, ActionSource.Ui);
        r.R.H.R.Bus.Publish(new ObjectMotionChanged(0x80000999, false, false, new ServerMotionState { MovementType = MovementType.Interpreted, Forward = 0x0003 }));
        r.Tick(0.12);
        Assert.Equal(ActionStatus.Queued, queued.Status); // no own Ready yet: the lane waits the 0.25 s fallback and the margin
        r.Tick(0.25);
        Assert.Equal(ActionStatus.Sent, queued.Status);
    }

    [Fact]
    public void The_heal_is_logged_with_its_kind_and_outcome()
    {
        var (r, handle) = Sent();
        r.Line("You fail to heal yourself. Your Handy Healing Kit has 19 uses left.");
        r.Structure(Kit, 19);
        r.UseDone();
        r.Tick();

        Assert.True(handle.IsCompleted);
        Assert.Contains(r.R.H.ActionLog.All(), e => e.Kind == ActionKind.UseWithTarget && e.Outcome.Kind == ActionOutcomeKind.Confirmed);
    }
}
