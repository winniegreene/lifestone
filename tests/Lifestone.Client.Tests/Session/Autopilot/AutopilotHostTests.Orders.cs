using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Autopilot.Commands;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>The command registry over the dial (2026-11-17): orders taken at a safe point and never mid-cast, the queue, the pause and the refusals by name.</summary>
public sealed partial class AutopilotHostTests
{
    private static CommandRunner Runner(Rig rig, List<string>? lines = null)
    {
        var dial = new DialCommandTarget(
            rig.Host,
            () => rig.View,
            person =>
            {
                rig.Host.Toggle(rig.View, person ? null : "steer", AutopilotHostLogic.ResumeReason);
                return "pressed";
            },
            via => rig.Host.StopHere(rig.View, via));
        var runner = new CommandRunner(CommandRegistry.Default, dial, rig.World.Trip.Context().Log);
        if (lines is not null)
        {
            runner.Line += lines.Add;
        }

        rig.Host.AfterRun = runner.ClaimEnd;
        return runner;
    }

    /// <summary>Frames of the dial, then the runner, as the play host runs them.</summary>
    private static bool RunOrders(Rig rig, CommandRunner runner, Func<bool> reached, int frames = 40000, Action? perFrame = null)
    {
        for (var i = 0; i < frames; i++)
        {
            if (reached())
            {
                return true;
            }

            rig.Frame();
            runner.Tick();
            perFrame?.Invoke();
        }

        return reached();
    }

    [Fact]
    public void A_trip_ordered_in_a_fight_is_taken_in_its_lull_the_fight_asked_to_stop_cleanly_and_the_fight_resumes_after_the_trip()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        var lines = new List<string>();
        var runner = Runner(rig, lines);
        rig.Host.Toggle(rig.View);
        Assert.True(rig.RunUntil(() => rig.Fighting(2)));
        var before = Kinds(rig.World).Length;

        var entry = runner.Submit("/trip", "chat", person: true);
        Assert.Equal((CommandEntryState.Waiting, "waiting"), (entry.State, entry.Name));
        Assert.Equal(ScenarioOrderState.Waiting, entry.Order!.State);

        var stopped = rig.Host.Scenario!.CurrentRun as IStoppableFight; // the fight in hand as the order is asked
        Assert.True(RunOrders(rig, runner, () => entry.Final, perFrame: () =>
        {
            if (entry.Order is { State: ScenarioOrderState.Waiting } && rig.Host.Scenario!.CurrentRun is IStoppableFight fight)
            {
                stopped = fight; // the fight in hand while the order waited: the one its lull stops
            }
        }));

        Assert.Equal((CommandEntryState.Done, "trip-done"), (entry.State, entry.Name));
        var order = entry.Order!;
        Assert.Equal("lull", order.TakenAt);
        Assert.Equal(AutopilotState.Fight, order.TakenInState); // taken in the fight's own lull arm, never inside a station's step
        Assert.Equal(AutopilotScenario.OrderStop, stopped?.StopReason); // the fight was asked to stop by name and ended cleanly: nothing was cut mid-cast
        Assert.Contains("logistics", Kinds(rig.World).Skip(before));
        Assert.Contains(lines, l => l.Contains("taken: taken at the lull", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("done: trip-done: check after: the trip's station ended well", StringComparison.Ordinal));

        Assert.True(RunOrders(rig, runner, () => rig.Fighting()), "the fight never resumed after the trip"); // the loop goes on: back to the ground
        Assert.Equal(AutopilotMode.Running, rig.Host.Mode);
        rig.Host.Dispose();
    }

    [Fact]
    public void An_order_is_refused_by_name_when_its_precondition_fails_and_a_stub_names_its_todo()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        var runner = Runner(rig);

        var trip = runner.Submit("trip", "chat", person: true); // the dial is off
        Assert.Equal((CommandEntryState.Refused, "not-running"), (trip.State, trip.Name));

        var quest = runner.Submit("/quest no-such-quest", "desk");
        Assert.Equal((CommandEntryState.Refused, "unknown-quest"), (quest.State, quest.Name));

        var stub = runner.Submit("/buy scarab 3", "chat", person: true);
        Assert.Equal((CommandEntryState.Refused, "not-built"), (stub.State, stub.Name));
        Assert.StartsWith("TODO(m6-cmd-buy)", stub.Detail, StringComparison.Ordinal);

        var spell = runner.Submit("/learn Focus", "chat", person: true);
        Assert.Equal((CommandEntryState.Refused, "not-built"), (spell.State, spell.Name));
        Assert.StartsWith("TODO(m6-cmd-learn-spell)", spell.Detail, StringComparison.Ordinal);

        Assert.Equal((CommandEntryState.Refused, "unknown-command"), (runner.Submit("/fly", "chat").State, runner.Submit("/fly", "chat").Name));
        Assert.Equal("bad-args", runner.Submit("/quest", "chat").Name);
        Assert.Equal("bad-args", runner.Submit("/rebuff everyone", "chat").Name);
        Assert.Equal(AutopilotMode.Off, rig.Host.Mode);
        rig.Host.Dispose();
    }

    [Fact]
    public void Orders_queue_one_behind_another_and_status_queue_cancel_and_clear_answer_at_once()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        var lines = new List<string>();
        var runner = Runner(rig, lines);
        rig.Host.Toggle(rig.View);
        Assert.True(rig.RunUntil(() => rig.Fighting()));

        var trip = runner.Submit("/trip", "chat", person: true);
        var rebuff = runner.Submit("/rebuff", "chat", person: true);
        var home = runner.Submit("/home", "chat", person: true);
        Assert.Equal((CommandEntryState.Waiting, CommandEntryState.Queued, CommandEntryState.Queued), (trip.State, rebuff.State, home.State));
        Assert.Same(trip, runner.Active);
        Assert.Equal([rebuff, home], runner.Queued);

        var status = runner.Submit("/status", "chat", person: true);
        Assert.Equal(CommandEntryState.Answered, status.State);
        Assert.Contains("autopilot running", status.Detail, StringComparison.Ordinal);
        Assert.Contains("queued 2", status.Detail, StringComparison.Ordinal);
        Assert.Contains(trip.Label, runner.Submit("/queue", "chat").Detail, StringComparison.Ordinal);

        Assert.Contains("is cancelled", runner.Submit("/cancel", "chat").Detail, StringComparison.Ordinal); // the last queued: /home
        Assert.Equal(CommandEntryState.Cancelled, home.State);
        Assert.Contains("1 queued order(s) taken off", runner.Submit("/clear", "chat").Detail, StringComparison.Ordinal);
        Assert.Equal(CommandEntryState.Cancelled, rebuff.State);
        Assert.Empty(runner.Queued);

        var again = runner.Submit("/rebuff", "chat", person: true);
        Assert.Equal(CommandEntryState.Queued, again.State);
        Assert.True(RunOrders(rig, runner, () => again.Final), "the queued rebuff never ran");
        Assert.Equal(CommandEntryState.Done, trip.State); // the trip first, then the rebuff at its own safe point
        Assert.Equal((CommandEntryState.Done, "rebuff-done"), (again.State, again.Name));
        Assert.Contains(again.Order!.TakenAt, new[] { "lull", "known-state" });
        Assert.All(lines, l => Assert.StartsWith(CommandRunner.Prefix, l, StringComparison.Ordinal));
        rig.Host.Dispose();
    }

    [Fact]
    public void A_pause_waits_for_a_safe_point_an_orders_resume_gives_it_back_and_a_persons_stop_empties_the_queue()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        var runner = Runner(rig);
        rig.Host.Toggle(rig.View);
        Assert.True(rig.RunUntil(() => rig.Fighting()));

        var pause = runner.Submit("/pause", "desk");
        Assert.Equal(CommandEntryState.Waiting, pause.State);
        Assert.True(RunOrders(rig, runner, () => pause.Final));
        Assert.Equal((CommandEntryState.Done, "paused"), (pause.State, pause.Name));
        Assert.Equal((AutopilotMode.Interrupted, true, false), (rig.Host.Mode, rig.Host.InterruptedByOrder, rig.Host.PersonHold));

        var resume = runner.Submit("/resume", "desk");
        Assert.Equal((CommandEntryState.Done, AutopilotMode.Running), (resume.State, rig.Host.Mode));

        Assert.True(rig.RunUntil(() => rig.Fighting()));
        var trip = runner.Submit("/trip", "chat", person: true);
        var queued = runner.Submit("/rebuff", "chat", person: true);
        runner.StopByPerson("/stop");
        Assert.Equal((CommandEntryState.Cancelled, CommandEntryState.Cancelled), (trip.State, queued.State));
        Assert.Null(rig.Host.Scenario!.Order);
        Assert.Equal((AutopilotMode.Interrupted, true), (rig.Host.Mode, rig.Host.PersonHold));
        Assert.Equal("person-stopped", runner.Submit("/resume", "desk").Name); // a person's stop is given back only by a person
        Assert.Equal("not-running", runner.Submit("/trip", "desk").Name);
        rig.Host.Dispose();
    }

    [Fact]
    public void Ladder_from_another_task_is_a_reseat_with_the_counts_carried_and_then_idle_recalls_home_after_the_order()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        var runner = Runner(rig);
        Assert.Null(rig.Host.UseTask(AutopilotTask.Errand));
        rig.Host.Toggle(rig.View);
        Assert.True(RunOrders(rig, runner, () => rig.Host.Scenario?.State is AutopilotState.KnownState or AutopilotState.Trip or AutopilotState.Fight));

        var ladder = runner.Submit("/ladder", "chat", person: true);
        Assert.True(RunOrders(rig, runner, () => ladder.Final));
        Assert.True(ladder.State == CommandEntryState.Done || ladder.Name == "seat-rate", $"{ladder.State} {ladder.Name}: {ladder.Detail}");

        var fresh = new Rig(output, Den, new AutopilotOptions());
        var orders = Runner(fresh);
        fresh.Host.Toggle(fresh.View);
        Assert.True(fresh.RunUntil(() => fresh.Fighting()));
        var idle = orders.Submit("/rebuff then idle", "chat", person: true);
        Assert.True(RunOrders(fresh, orders, () => idle.Final));
        Assert.Equal(CommandEntryState.Done, idle.State);
        Assert.Contains("then idle: she recalls home and holds", idle.Detail, StringComparison.Ordinal);
        Assert.True(RunOrders(fresh, orders, () => fresh.Host.Mode == AutopilotMode.Done), "the run never ended at home");
        rig.Host.Dispose();
        fresh.Host.Dispose();
    }
}
