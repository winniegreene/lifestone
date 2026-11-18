using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>The dial's side of the steer desk: the person hold, a stop by an order, leaving, the claim on a run's end and the re-seat.</summary>
public sealed partial class AutopilotHostTests
{
    private static AutopilotSeatCarry SeatCarryOf(bool ladderChecked) => new(ladderChecked, 7, true, 12, true, 3, "den", 1, 2, (1, 40, 1), new Dictionary<string, string>(StringComparer.Ordinal) { ["q"] = "failed:x" }, true);

    [Fact]
    public void A_persons_stop_holds_in_any_mode_until_their_own_press_and_an_orders_press_does_not_clear_it()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        Assert.False(rig.Host.PersonHold);
        rig.Host.StopHere(rig.View); // typed while off: nothing to stop, and still a hold
        Assert.True(rig.Host.PersonHold);

        rig.Host.Toggle(rig.View, "steer"); // the dial's own press clears nothing: the desk is what refuses an order under a hold
        Assert.True(rig.Host.PersonHold);
        rig.Host.Toggle(rig.View, null, AutopilotHostLogic.ResumeReason); // nor does the move-on's press, which looks like /auto to the dial
        Assert.True(rig.Host.PersonHold);

        rig.Host.PersonPressed(); // only the host's /auto says a person pressed
        Assert.False(rig.Host.PersonHold);
        rig.Host.Dispose();
    }

    [Fact]
    public void A_stop_by_an_order_records_no_hold_and_a_persons_stop_is_never_the_orders_to_resume()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        rig.Host.Toggle(rig.View, "steer");
        Assert.True(rig.RunUntil(() => rig.Fighting()));

        Assert.True(rig.Host.StopHereByOrder(rig.View, "steer-x"));
        Assert.Equal((AutopilotMode.Interrupted, false, true), (rig.Host.Mode, rig.Host.PersonHold, rig.Host.InterruptedByOrder));
        Assert.True(rig.Host.HandBack(rig.View));
        Assert.False(rig.Host.InterruptedByOrder);

        Assert.True(rig.RunUntil(() => rig.Fighting()));
        rig.Host.StopHere(rig.View);
        Assert.Equal((AutopilotMode.Interrupted, true, false), (rig.Host.Mode, rig.Host.PersonHold, rig.Host.InterruptedByOrder));
        Assert.False(rig.Host.StopHereByOrder(rig.View, "steer-y")); // already interrupted: nothing to stop
        rig.Host.Dispose();
    }

    [Fact]
    public void Leaving_while_off_logs_off_where_it_stands_with_exit_code_0_and_a_repeat_changes_nothing()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        rig.Host.Tick(rig.View, rig.World.Now);
        Assert.Null(rig.Host.Leave("steer-x"));
        var end = Assert.IsType<AutopilotUnattendedEnd>(rig.Host.UnattendedEnd);
        Assert.Equal(0, end.ExitCode);
        Assert.Contains("asked to leave (steer-x)", end.Decision, StringComparison.Ordinal);
        Assert.Null(rig.Host.Leave("steer-y"));
        Assert.Same(end, rig.Host.UnattendedEnd);
        rig.Host.Dispose();
    }

    [Fact]
    public void Leaving_a_run_a_person_started_still_recalls_and_quits_and_cannot_be_asked_while_stopping()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        var asked = 0;
        rig.Host.AfterRun = _ =>
        {
            asked++;
            return true;
        };
        rig.Host.Toggle(rig.View); // a person's run: not unattended
        Assert.True(rig.RunUntil(() => rig.Fighting()));

        Assert.Null(rig.Host.Leave("steer-x"));
        Assert.Equal(AutopilotMode.Stopping, rig.Host.Mode);
        Assert.Equal("stopping", rig.Host.Leave("steer-y"));
        Assert.True(rig.RunUntil(() => rig.Host.Mode == AutopilotMode.Done));

        var end = Assert.IsType<AutopilotUnattendedEnd>(rig.Host.UnattendedEnd);
        Assert.Equal(0, end.ExitCode);
        Assert.Contains("stop=request:" + AutopilotHostLogic.StopFileReason, end.Decision, StringComparison.Ordinal);
        Assert.Equal(0, asked); // leaving is never the desk's to claim
        rig.Host.Dispose();
    }

    [Fact]
    public void A_claimed_end_does_not_quit_or_raise_stopped_and_an_unclaimed_end_is_as_before()
    {
        var claimed = new Rig(output, Den, new AutopilotOptions { Minutes = 0.5 });
        var stops = 0;
        claimed.Host.Stopped += _ => stops++;
        claimed.Host.AfterRun = _ => true;
        claimed.Host.Toggle(claimed.View, Option);
        Assert.True(claimed.RunUntil(() => claimed.Host.Mode == AutopilotMode.Done));
        Assert.Null(claimed.Host.UnattendedEnd);
        Assert.Equal(0, stops);
        claimed.Host.Dispose();

        var passed = new Rig(output, Den, new AutopilotOptions { Minutes = 0.5 });
        passed.Host.AfterRun = _ => false;
        passed.Host.Toggle(passed.View, Option);
        Assert.True(passed.RunUntil(() => passed.Host.Mode == AutopilotMode.Done));
        Assert.Equal(0, Assert.IsType<AutopilotUnattendedEnd>(passed.Host.UnattendedEnd).ExitCode);
        passed.Host.Dispose();
    }

    [Fact]
    public void A_seat_in_a_lull_replaces_the_scenario_keeps_the_controls_and_the_counts_and_never_counts_as_a_person()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        rig.Host.Toggle(rig.View);
        Assert.True(rig.RunUntil(() => rig.Fighting(2)));
        var old = rig.Host.Scenario!;
        var (cycle, locked) = (old.Cycle, rig.Host.KeysLocked);

        Assert.Null(rig.Host.AskSeat(rig.Host.Task, "the rules changed"));
        Assert.True(rig.Host.SeatAsked);
        Assert.True(rig.RunUntil(() => rig.Host.Seats == 1), "the seat was never made");

        var fresh = rig.Host.Scenario!;
        Assert.NotSame(old, fresh);
        Assert.Equal((AutopilotMode.Running, 0, 0, cycle, locked), (rig.Host.Mode, rig.Host.Interrupted, rig.Host.Resumed, fresh.Cycle, rig.Host.KeysLocked));
        Assert.True(fresh.CycleTally.Kills >= 2, $"kills carried: {fresh.CycleTally.Kills}");
        var carried = fresh.SeatCarry;
        Assert.Equal(old.SeatCarry with { QuestFailed = carried.QuestFailed }, carried); // the real seat, not only the constructor
        Assert.False(rig.Host.SeatAsked);
        Assert.False(rig.Host.PersonHold);
        Assert.Equal("seat-rate", rig.Host.AskSeat(rig.Host.Task, "again"));
        rig.Host.Dispose();
    }

    [Fact]
    public void A_seat_cannot_be_asked_of_a_dial_that_is_not_running_and_a_person_stop_forgets_it()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        Assert.Equal("not-running", rig.Host.AskSeat(rig.Host.Task, "x"));
        rig.Host.Toggle(rig.View);
        Assert.True(rig.RunUntil(() => rig.Fighting()));
        Assert.Null(rig.Host.AskSeat(rig.Host.Task, "x"));
        rig.Host.StopHere(rig.View);
        rig.Host.Toggle(rig.View);
        Assert.False(rig.Host.SeatAsked);
        rig.Host.Dispose();
    }

    [Fact]
    public void A_stop_asked_after_a_seat_wins_and_no_seat_is_made()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        rig.Host.Toggle(rig.View);
        Assert.True(rig.RunUntil(() => rig.Fighting()));
        Assert.Null(rig.Host.AskSeat(rig.Host.Task, "x"));
        rig.Host.Stop("key");
        Assert.True(rig.RunUntil(() => rig.Host.Mode == AutopilotMode.Done));
        Assert.Equal(0, rig.Host.Seats);
        Assert.Equal("request:key", rig.Host.Scenario!.StopName);
        rig.Host.Dispose();
    }

    [Fact]
    public void A_stop_asked_while_a_seat_waits_at_the_known_state_ends_the_run_and_does_not_stall_it()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        rig.Host.Toggle(rig.View);
        Assert.Null(rig.Host.AskSeat(rig.Host.Task, "x")); // from the first frames: the audit, then the known state
        rig.Host.Stop("key");
        Assert.True(rig.RunUntil(() => rig.Host.Mode == AutopilotMode.Done, 6000), "the run never ended");
        Assert.Equal((0, "request:key"), (rig.Host.Seats, rig.Host.Scenario!.StopName));
        rig.Host.Dispose();
    }

    [Fact]
    public void A_persons_stop_ends_a_way_out_in_progress_and_their_own_press_does_not_quit_the_game()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        rig.Host.Toggle(rig.View);
        Assert.True(rig.RunUntil(() => rig.Fighting()));
        Assert.Null(rig.Host.Leave("steer-x"));
        rig.Host.StopHere(rig.View); // a person takes over mid-leave
        rig.Host.PersonPressed();
        Assert.True(rig.Host.HandBack(rig.View));
        Assert.Equal(AutopilotMode.Running, rig.Host.Mode); // no stop was re-asked
        Assert.True(rig.RunUntil(() => rig.Fighting()));
        Assert.Null(rig.Host.UnattendedEnd);
        rig.Host.Dispose();
    }

    [Fact]
    public void A_seed_with_no_seat_carries_no_guards_and_a_seat_carry_round_trips_through_a_seed()
    {
        var world = new ApWorld(Den, output);
        Assert.Null(world.Autopilot(new AutopilotOptions()).Seed.Seat);

        var carry = SeatCarryOf(ladderChecked: true);
        var back = world.Autopilot(new AutopilotOptions(), seed: new AutopilotSeed(1, 0) { Seat = carry }).SeatCarry;
        Assert.Equal(carry with { QuestFailed = back.QuestFailed }, back);
        Assert.Equal(carry.QuestFailed, back.QuestFailed);
    }

    [Fact]
    public void A_stop_asked_during_a_trip_with_a_move_on_due_ends_the_run_on_the_old_rung()
    {
        // Level 5 on a run built on the den: the next rung check is a move-on. The audit sends her on a trip first (no spare caster), and the stop is asked in it.
        var w = new ApWorld(Den, output, atHome: false);
        w.Level(5);
        var autopilot = w.Autopilot(new AutopilotOptions { Job = AutopilotJob.LevelUp }, seed: new AutopilotSeed(1, 0) { Seat = SeatCarryOf(ladderChecked: true) });
        w.Run(autopilot, _ => autopilot.State == AutopilotState.Trip, 40000);
        Assert.Equal(AutopilotState.Trip, autopilot.State);

        autopilot.RequestStop("key");
        w.Run(autopilot);

        Assert.Equal("request:key", autopilot.StopName);
    }

    [Fact]
    public void A_seat_with_a_move_on_due_takes_the_move_on_where_a_fresh_run_is_a_failure()
    {
        ApWorld World()
        {
            var w = new ApWorld(Den, output, atHome: false);
            w.Level(5); // the outskirts' band, on a run built on the den
            var wielded = w.Trip.Own("Wand", 31336, 0x8000, 100);
            w.Trip.Vendor.R.Server((p, s) => Lifestone.Tests.Shared.Net.AceM3Writers.WieldObject(p, s, wielded, 0x01000000));
            w.Trip.Vendor.R.Tick();
            w.Trip.Own("Spare Wand", 31336, 0x8000, 100);
            return w;
        }

        var options = new AutopilotOptions { Job = AutopilotJob.LevelUp };
        var fresh = World();
        var failed = fresh.Autopilot(options);
        fresh.Run(failed);
        Assert.Equal("failed:rung-ground-mismatch:outskirts", failed.StopName);

        var seated = World();
        var moved = seated.Autopilot(options, seed: new AutopilotSeed(1, 0) { Seat = SeatCarryOf(ladderChecked: true) });
        seated.Run(moved);
        Assert.Equal("rung-moved:outskirts", moved.StopName);
    }
}
