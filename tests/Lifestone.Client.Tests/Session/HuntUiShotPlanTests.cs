using System.Collections.Immutable;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session;

/// <summary>The melee hunt leg's retail-UI frames (M4 1.3 s2): <c>c1-combat</c>, <c>c1-powerbar</c> and <c>meter</c>, their triggers, moments, drops and the camera and meter rules they are judged by.</summary>
public sealed class HuntUiShotPlanTests
{
    private const int Combat = 0;
    private const int PowerBar = 1;
    private const int Meter = 2;

    [Fact]
    public void The_frames_are_named_for_the_files_the_brief_names()
    {
        Assert.Equal(["c1-combat", "c1-powerbar", "meter", "corpse", "heal", "potion", "vendor"], HuntUiShotPlan.Names); // 3.1 p adds heal and potion, 3.2 s4a the vendor frame
        Assert.Equal("cache/m4/3-2/hunt-vendor.png", HuntUiShotPlan.ShotPath("cache/m4/3-2/hunt.png", HuntUiShotPlan.Vendor).Replace('\\', '/'));
        Assert.Equal(1.0, HuntUiShotPlan.DelaysSeconds[HuntUiShotPlan.Vendor]);
        Assert.Equal("cache/m4/3-1/hunt-heal.png", HuntUiShotPlan.ShotPath("cache/m4/3-1/hunt.png", HuntUiShotPlan.Heal).Replace('\\', '/'));
        Assert.Equal("cache/m4/3-1/hunt-potion.png", HuntUiShotPlan.ShotPath("cache/m4/3-1/hunt.png", HuntUiShotPlan.Potion).Replace('\\', '/'));
        Assert.Equal(HuntUiShotPlan.Names.Length, HuntUiShotPlan.DelaysSeconds.Length);
        Assert.Equal(HuntUiShotPlan.Names.Length, HuntUiShotPlan.Triggers.Length);
        Assert.Equal("cache/m4/2-1/hunt-corpse.png", HuntUiShotPlan.ShotPath("cache/m4/2-1/hunt.png", HuntUiShotPlan.Corpse).Replace('\\', '/')); // 2.1 F1a
        Assert.Equal("cache/m4/1-3/hunt-c1-combat.png", HuntUiShotPlan.ShotPath("cache/m4/1-3/hunt.png", Combat).Replace('\\', '/'));
        Assert.Equal("cache/m4/1-3/hunt-c1-powerbar.png", HuntUiShotPlan.ShotPath("cache/m4/1-3/hunt.png", PowerBar).Replace('\\', '/'));
        Assert.Equal("cache/m4/1-3/hunt-meter.png", HuntUiShotPlan.ShotPath("cache/m4/1-3/hunt.png", Meter).Replace('\\', '/'));
    }

    [Fact]
    public void Each_slot_is_due_after_its_own_delay_and_not_before_the_hud_is_hidden()
    {
        var plan = new HuntUiShotPlan();
        plan.Trigger(Combat, 10.0);
        Assert.True(plan.Owed);
        Assert.Equal(-1, plan.Due(10.2, hiddenFrames: 9)); // 0.3 s after the trigger
        Assert.Equal(-1, plan.Due(10.3, hiddenFrames: 1));
        Assert.Equal(Combat, plan.Due(10.3, hiddenFrames: 2));

        plan.Trigger(PowerBar, 20.0); // no delay: due at once
        plan.MarkTaken(Combat);
        Assert.Equal(PowerBar, plan.Due(20.0, 2));
        plan.MarkTaken(PowerBar);

        plan.Trigger(Meter, 30.0);
        Assert.Equal(-1, plan.Due(30.05, 5));
        Assert.Equal(Meter, plan.Due(30.1, 5));
        plan.MarkTaken(Meter);

        Assert.False(plan.Owed);
        Assert.Equal(3, plan.Taken);
        plan.Trigger(Combat, 40.0); // a taken frame is not taken twice
        Assert.False(plan.Owed);
    }

    [Fact]
    public void The_corpse_frame_is_due_0_3_s_after_the_open_and_given_up_after_2_s_so_the_close_waits_at_most_that_long_2_1_F1()
    {
        var plan = new HuntUiShotPlan();
        plan.Trigger(HuntUiShotPlan.Corpse, 50.0);
        Assert.True(plan.Owed);
        Assert.Equal(-1, plan.Due(50.2, 5));
        Assert.Equal(HuntUiShotPlan.Corpse, plan.Due(50.31, 5));
        plan.Expire(51.9);
        Assert.True(plan.Owed);
        plan.Expire(52.1); // the other slots hold 3 s; this one 2 s
        Assert.False(plan.Owed);
        Assert.Contains("corpse: given up (held longer than 2 s)", plan.Note(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_dropped_trigger_waits_for_the_next_one_and_a_frame_owed_too_long_is_given_up()
    {
        var plan = new HuntUiShotPlan();
        plan.Trigger(PowerBar, 1.0);
        plan.Drop(PowerBar); // the meter had passed the run's power at the frame's moment
        Assert.True(plan.Waiting(PowerBar));
        Assert.False(plan.Owed);

        plan.Trigger(PowerBar, 4.0);
        plan.Expire(6.9);
        Assert.True(plan.Owed);
        plan.Expire(7.1);
        Assert.False(plan.Owed);
        Assert.False(plan.Waiting(PowerBar));
        var note = plan.Note();
        Assert.Contains("c1-powerbar: given up (held longer than 3 s), 1 trigger(s) dropped", note, StringComparison.Ordinal);
        Assert.Contains("c1-combat: not taken (trigger '", note, StringComparison.Ordinal);
        Assert.Contains("meter: not taken (trigger '", note, StringComparison.Ordinal);
    }

    [Fact]
    public void A_frame_that_could_not_be_saved_is_given_up_by_name()
    {
        var plan = new HuntUiShotPlan();
        plan.Trigger(Meter, 1.0);
        plan.MarkFailed(Meter, "the frame could not be saved");
        Assert.False(plan.Owed);
        Assert.Contains("meter: given up (the frame could not be saved)", plan.Note(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0.0f, 0.5f, false)] // not begun
    [InlineData(0.04f, 0.5f, false)] // too near 0
    [InlineData(0.07f, 0.5f, false)] // 1.3-R2: the first frames of the charge read as an empty bar
    [InlineData(0.3f, 0.5f, true)]
    [InlineData(0.06f, 0.5f, false)]
    [InlineData(0.25f, 0.5f, true)] // half the power: the window opens
    [InlineData(0.44f, 0.5f, true)]
    [InlineData(0.46f, 0.5f, false)] // too near the run's power
    [InlineData(0.5f, 0.5f, false)] // at the power: the bar stops charging
    [InlineData(0.3f, 0.0f, false)] // a run with no power has no mid-charge
    public void Mid_charge_is_from_half_the_run_s_power_to_a_margin_short_of_it(float level, float power, bool expected) =>
        Assert.Equal(expected, HuntUiShotPlan.MidCharge(level, power));

    [Theory]
    [InlineData(1.0f, 0.9f, true)]
    [InlineData(1.0f, 0.998f, false)] // within a hundredth of a bar: a rounding, not a hit
    [InlineData(1.0f, 1.0f, false)]
    [InlineData(float.NaN, 0.5f, false)] // the meter had not shown at selection
    [InlineData(1.0f, float.NaN, false)]
    public void The_meter_is_lowered_when_it_is_below_its_value_at_selection(float atSelection, float now, bool expected) =>
        Assert.Equal(expected, HuntUiShotPlan.MeterLowered(atSelection, now));

    [Fact]
    public void The_camera_is_framed_when_settled_on_the_framed_distance_and_not_blocked()
    {
        Assert.InRange(HuntUiShotPlan.FramedMetres, 4.6f, 4.8f); // |(0, -4.5, 1.35)|
        Assert.True(HuntUiShotPlan.CameraFramed(HuntUiShotPlan.FramedMetres, blocked: false));
        Assert.True(HuntUiShotPlan.CameraFramed(HuntUiShotPlan.FramedMetres - 0.29f, blocked: false));
        Assert.False(HuntUiShotPlan.CameraFramed(2.6f, blocked: false)); // retail's own distance: the framing has not been applied or has not settled
        Assert.False(HuntUiShotPlan.CameraFramed(0.5f, blocked: true)); // a tree pulled the viewer in
        Assert.False(HuntUiShotPlan.CameraFramed(HuntUiShotPlan.FramedMetres, blocked: true));
    }

    [Fact]
    public void The_target_must_be_within_six_metres_for_the_frames()
    {
        Assert.Equal(6.0, HuntUiShotPlan.TargetRangeMetres);
        Assert.Equal(3.0, HuntUiShotPlan.HoldSeconds);
    }

    [Theory]
    [InlineData(100f, 100f, 50f, 50f, 0f)] // apart
    [InlineData(0f, 0f, 100f, 100f, 1f)] // the player's box covers the target's
    [InlineData(80f, 0f, 100f, 100f, 0.2f)] // a fifth
    public void The_player_box_overlap_is_the_share_of_the_target_box_it_covers_1_3_L1(float px, float py, float pw, float ph, float expected) =>
        Assert.Equal(expected, HuntUiShotPlan.Overlap(new Lifestone.Client.Play.ScreenRect(0f, 0f, 100f, 100f), new Lifestone.Client.Play.ScreenRect(px, py, pw, ph)), 3);

    [Fact]
    public void A_target_behind_the_player_is_not_fit_for_a_frame_and_a_separated_one_is_1_3_L1()
    {
        var target = new Lifestone.Client.Play.ScreenRect(350f, 200f, 60f, 80f);
        var behind = new Lifestone.Client.Play.ScreenRect(330f, 190f, 100f, 200f);
        var apart = new Lifestone.Client.Play.ScreenRect(500f, 190f, 100f, 200f);
        Assert.False(HuntUiShotPlan.TargetVisible(in target, true, in behind, true, 450f, out var covered));
        Assert.True(covered > HuntUiShotPlan.MaxOverlap);
        Assert.True(HuntUiShotPlan.TargetVisible(in target, true, in apart, true, 450f, out var none));
        Assert.Equal(0f, none);
        Assert.False(HuntUiShotPlan.TargetVisible(in target, false, in apart, true, 450f, out _)); // off screen
        Assert.False(HuntUiShotPlan.TargetVisible(in target, true, in apart, true, 220f, out _)); // under the combat window's top edge
        Assert.True(HuntUiShotPlan.TargetVisible(in target, true, in behind, false, 450f, out _)); // no player box: not judged
    }

    [Fact]
    public void The_side_offset_goes_to_one_side_then_the_other_then_back_to_the_start_1_3_L1()
    {
        var first = HuntUiShotPlan.NextSideOffset(0f);
        var second = HuntUiShotPlan.NextSideOffset(first);
        Assert.Equal(HuntUiShotPlan.SideOffsetDegrees, first);
        Assert.Equal(-HuntUiShotPlan.SideOffsetDegrees, second);
        Assert.Equal(0f, HuntUiShotPlan.NextSideOffset(second));
    }

    [Fact]
    public void The_corpse_frame_is_given_up_after_its_2_second_hold_and_the_slot_says_so()
    {
        // M4 2.2 s4: the frame waits for the container window inside the scenario's hold; a window that never draws gives the slot up by name (the runner logs why).
        var plan = new HuntUiShotPlan();
        plan.Trigger(HuntUiShotPlan.Corpse, 50.0);
        Assert.True(plan.Owed);
        Assert.False(plan.IsGivenUp(HuntUiShotPlan.Corpse));
        plan.Expire(51.9);
        Assert.True(plan.Owed);
        plan.Expire(52.1);
        Assert.False(plan.Owed);
        Assert.True(plan.IsGivenUp(HuntUiShotPlan.Corpse));
        Assert.Contains("held longer than 2 s", plan.Note(), StringComparison.Ordinal);
        Assert.False(plan.IsTaken(HuntUiShotPlan.Corpse));
        Assert.False(plan.IsGivenUp(Combat));
    }

    [Fact]
    public void A_dropped_corpse_trigger_stays_owed_so_the_loot_has_not_started_and_the_2_second_hold_runs_from_the_first_trigger()
    {
        // M4 2.2 s4: the container window is not drawn (or the corpse is hidden) at the frame's moment: the other slots go back to waiting and let the scenario step; this one stays owed, checked again after 0.3 s.
        var plan = new HuntUiShotPlan();
        plan.Trigger(HuntUiShotPlan.Corpse, 100.0);
        plan.Expire(100.0);
        Assert.Equal(HuntUiShotPlan.Corpse, plan.Due(100.31, 5));
        plan.Drop(HuntUiShotPlan.Corpse);
        Assert.True(plan.Owed); // the scenario stays held in the frame of the drop too
        Assert.False(plan.Waiting(HuntUiShotPlan.Corpse));
        Assert.Equal(-1, plan.Due(100.5, 5)); // armed again at the drop's clock: due 0.3 s after it
        Assert.Equal(HuntUiShotPlan.Corpse, plan.Due(100.62, 5));

        plan.Trigger(HuntUiShotPlan.Corpse, 100.7); // a repeated trigger changes nothing (the slot is not waiting)
        plan.Drop(HuntUiShotPlan.Corpse);
        plan.Expire(101.9);
        Assert.True(plan.Owed);
        plan.Expire(102.1); // 2 s from the FIRST trigger, though the last drop was at 100.7
        Assert.False(plan.Owed);
        Assert.True(plan.IsGivenUp(HuntUiShotPlan.Corpse));
        Assert.Contains("corpse: given up (held longer than 2 s), 2 trigger(s) dropped", plan.Note(), StringComparison.Ordinal);

        // The frame is taken from a dropped-and-armed slot all the same.
        var taken = new HuntUiShotPlan();
        taken.Trigger(HuntUiShotPlan.Corpse, 10.0);
        taken.Drop(HuntUiShotPlan.Corpse);
        Assert.Equal(HuntUiShotPlan.Corpse, taken.Due(10.4, 5));
        taken.MarkTaken(HuntUiShotPlan.Corpse);
        Assert.False(taken.Owed);
        Assert.True(taken.IsTaken(HuntUiShotPlan.Corpse));
    }

    [Fact]
    public void M4_2_2_fix2_R15_ReleaseCorpse_gives_up_an_owed_corpse_slot_by_name_and_ends_the_hold_and_touches_nothing_else()
    {
        // The hunt's retreat rule or its seconds abandoned the corpse sequence while the frame was owed (HuntScenario.GuardWhileHeld): the window is closing, so the hold ends at once, not after the 2 s.
        var plan = new HuntUiShotPlan();
        plan.Trigger(HuntUiShotPlan.Corpse, 50.0);
        plan.Drop(HuntUiShotPlan.Corpse); // a dropped trigger stays owed: the hold the guard must be able to end
        Assert.True(plan.Owed);

        plan.ReleaseCorpse("the retreat rule or the hunt's time came while the frame was owed (the corpse window is closing)");
        Assert.False(plan.Owed);
        Assert.True(plan.IsGivenUp(HuntUiShotPlan.Corpse));
        Assert.False(plan.IsTaken(HuntUiShotPlan.Corpse));
        Assert.Contains("corpse: given up (the retreat rule or the hunt's time came", plan.Note(), StringComparison.Ordinal);

        // A slot that is not owed is left as it is: a taken frame stays taken, a waiting one stays waiting, and another slot's hold is not released.
        var taken = new HuntUiShotPlan();
        taken.Trigger(HuntUiShotPlan.Corpse, 10.0);
        taken.MarkTaken(HuntUiShotPlan.Corpse);
        taken.ReleaseCorpse("late");
        Assert.True(taken.IsTaken(HuntUiShotPlan.Corpse));
        var waiting = new HuntUiShotPlan();
        waiting.Trigger(Combat, 20.0);
        waiting.ReleaseCorpse("no corpse owed");
        Assert.True(waiting.Owed);
        Assert.True(waiting.Waiting(HuntUiShotPlan.Corpse));
        Assert.False(waiting.IsGivenUp(HuntUiShotPlan.Corpse));
    }

    [Fact]
    public void The_corpse_objdesc_facts_count_the_createobjects_parts_palettes_and_textures_and_say_when_an_update_changed_the_model()
    {
        // M4 2.2 s4, q6-corpse-objdesc-live: the counts the lead sets beside the picture.
        Assert.Equal("objdesc_parts=0 objdesc_palettes=0 objdesc_textures=0 objdesc_base_palette=none objdesc_updated=no", HuntUiShotPlan.ObjDescFacts(ModelData.Empty, false));
        var model = new ModelData(
            0x04000123,
            [new SubPaletteSwap(0x04000456, 0, 8)],
            [new TextureChange(3, 0x05000001, 0x05000002), new TextureChange(4, 0x05000003, 0x05000004)],
            [new AnimPartChange(0, 0x01000010), new AnimPartChange(1, 0x01000011), new AnimPartChange(2, 0x01000012)]);
        Assert.Equal("objdesc_parts=3 objdesc_palettes=1 objdesc_textures=2 objdesc_base_palette=0x04000123 objdesc_updated=yes", HuntUiShotPlan.ObjDescFacts(model, true));
        Assert.Throws<ArgumentNullException>(() => HuntUiShotPlan.ObjDescFacts(null!, false));
    }

    // ------------------------------------------------------------------ M4 3.1 p: the heal and potion frames

    private static HealSnapshot Counters(int heals = 0, int potions = 0, int wasted = 0, int points = 0) => new(heals, potions, wasted, points);

    [Fact]
    public void The_heal_and_potion_frames_are_taken_one_second_after_their_trigger_and_framed_like_the_others_with_no_target_asked_for()
    {
        Assert.Equal(4, HuntUiShotPlan.Heal);
        Assert.Equal(5, HuntUiShotPlan.Potion);
        Assert.Equal(1.0, HuntUiShotPlan.HealDelaySeconds);
        Assert.Equal(1.0, HuntUiShotPlan.DelaysSeconds[HuntUiShotPlan.Heal]);
        Assert.Equal(1.0, HuntUiShotPlan.DelaysSeconds[HuntUiShotPlan.Potion]);
        Assert.True(HuntUiShotPlan.IsHealSlot(HuntUiShotPlan.Heal) && HuntUiShotPlan.IsHealSlot(HuntUiShotPlan.Potion));
        Assert.DoesNotContain(Enumerable.Range(0, 4), HuntUiShotPlan.IsHealSlot); // the target frames keep the target rule
        Assert.Equal(3.0, HuntUiShotPlan.HoldSeconds); // each is held at most 3 s, as the others are
    }

    [Fact]
    public void A_kit_whose_roll_won_triggers_the_heal_frame_once_and_a_lost_roll_or_a_drink_does_not()
    {
        var plan = new HuntUiShotPlan();
        plan.ObserveHeals(Counters(), 10.0);
        Assert.False(plan.Owed);

        // A lost roll counts a kit use but not a heal: nothing is owed.
        plan.ObserveHeals(Counters(heals: 0), 11.0);
        Assert.False(plan.Owed);

        // A confirmed kit use whose roll won: the heal frame is owed, due 1 s later and not before the HUD has been hidden.
        plan.ObserveHeals(Counters(heals: 1, points: 40), 12.0);
        Assert.True(plan.Owed);
        Assert.Equal(-1, plan.Due(12.9, 5));
        Assert.Equal(-1, plan.Due(13.0, 1));
        Assert.Equal(HuntUiShotPlan.Heal, plan.Due(13.0, 2));
        plan.MarkTaken(HuntUiShotPlan.Heal);
        Assert.False(plan.Owed);
        Assert.True(plan.IsTaken(HuntUiShotPlan.Heal));
        Assert.False(plan.IsTaken(HuntUiShotPlan.Potion)); // a kit does not trigger the potion frame

        // Each is triggered once: a later heal asks for nothing.
        plan.ObserveHeals(Counters(heals: 2, points: 90), 30.0);
        Assert.False(plan.Owed);
        Assert.Equal(1, plan.Taken);
    }

    [Fact]
    public void A_potion_that_restored_points_triggers_the_potion_frame_but_a_wasted_drink_or_an_unread_line_does_not()
    {
        var plan = new HuntUiShotPlan();

        // A drink spent at a full vital restored nothing (Wasted): no frame.
        plan.ObserveHeals(Counters(potions: 1, wasted: 1, points: 0), 5.0);
        Assert.False(plan.Owed);

        // A confirmed drink whose line was not read counts the potion and no points: no Drank result is known, so no frame.
        plan.ObserveHeals(Counters(potions: 2, wasted: 1, points: 0), 6.0);
        Assert.False(plan.Owed);

        // A Drank result: the potion counted, nothing wasted, the points rose.
        plan.ObserveHeals(Counters(potions: 3, wasted: 1, points: 25), 7.0);
        Assert.True(plan.Owed);
        Assert.Equal(-1, plan.Due(7.99, 5));
        Assert.Equal(HuntUiShotPlan.Potion, plan.Due(8.0, 5));
        plan.MarkTaken(HuntUiShotPlan.Potion);
        plan.ObserveHeals(Counters(potions: 4, wasted: 1, points: 50), 20.0);
        Assert.False(plan.Owed); // once
    }

    [Fact]
    public void A_heal_and_a_potion_seen_together_owe_both_frames_and_a_frame_owed_for_more_than_three_seconds_is_given_up_by_name()
    {
        var plan = new HuntUiShotPlan();
        plan.ObserveHeals(Counters(heals: 1, potions: 1, points: 65), 100.0);
        Assert.Equal(HuntUiShotPlan.Heal, plan.Due(101.0, 5));
        plan.MarkTaken(HuntUiShotPlan.Heal);
        Assert.Equal(HuntUiShotPlan.Potion, plan.Due(101.0, 5));
        plan.Expire(102.9);
        Assert.True(plan.Owed);
        plan.Expire(103.1); // held at most 3 s, as the others are
        Assert.False(plan.Owed);
        Assert.True(plan.IsGivenUp(HuntUiShotPlan.Potion));
        Assert.Contains("potion: given up (held longer than 3 s)", plan.Note(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_dropped_heal_trigger_waits_for_the_next_heal_and_the_end_note_names_a_frame_whose_trigger_never_came()
    {
        var plan = new HuntUiShotPlan();
        plan.ObserveHeals(Counters(heals: 1, points: 30), 50.0);
        Assert.Equal(HuntUiShotPlan.Heal, plan.Due(51.0, 5));
        plan.Drop(HuntUiShotPlan.Heal); // the camera was not framed at its moment
        Assert.False(plan.Owed);
        plan.ObserveHeals(Counters(heals: 2, points: 70), 60.0); // the next heal re-arms it
        Assert.True(plan.Owed);
        plan.MarkTaken(HuntUiShotPlan.Heal);

        var note = plan.Note();
        Assert.Contains("heal: taken, 1 trigger(s) dropped", note, StringComparison.Ordinal);
        Assert.Contains("potion: not taken (trigger '" + HuntUiShotPlan.Triggers[HuntUiShotPlan.Potion] + "' never held)", note, StringComparison.Ordinal); // no potion came: the note says so
        Assert.Contains("c1-combat: not taken", note, StringComparison.Ordinal);
    }

    [Fact]
    public void The_runner_reads_the_heals_from_the_hunt_drivers_tally_and_frames_the_heal_slots_without_a_target_M4_3_1_p()
    {
        var runner = Repository.CheckoutFiles.ReadCSharpCode("src/Lifestone.Game/scripts/Session/HuntLegRunner.cs");
        Assert.Contains("var healTally = scenario.HealTally;", runner, StringComparison.Ordinal);
        Assert.Contains("_uiPlan.ObserveHeals(new HealSnapshot(healTally.Heals, healTally.Potions, healTally.Wasted, healTally.Points), _runSeconds);", runner, StringComparison.Ordinal);
        Assert.Contains("var healSlot = HuntUiShotPlan.IsHealSlot(slot);", runner, StringComparison.Ordinal);
        Assert.Contains("if (holds && combat is not null && !healSlot)", runner, StringComparison.Ordinal); // no target projection for the heal frames
        Assert.DoesNotContain("scenario._heal", runner, StringComparison.Ordinal); // never the scenario's private state
    }
}
