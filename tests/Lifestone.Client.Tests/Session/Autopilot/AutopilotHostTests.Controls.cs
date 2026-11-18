using Lifestone.Client.Play;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Play;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M5 7 D145: the bot is controlled from the chat window. The movement keys are locked while it runs; <c>/stop</c> (<see cref="AutopilotHostLogic.StopHere"/>) stops him where he stands: the run dropped, the queued automation cancelled, every
/// held key released, the keys unlocked; any stop it makes on its own (a stop rule, a failure, the kill switch, its disposal) unlocks them the same way, with a chat line saying why.
/// </summary>
public sealed partial class AutopilotHostTests
{
    private static readonly uint Forward = MovementBindings.CommandOf(MovementBindings.MoveForward);

    [Fact]
    public void M5_7_D145_the_keys_lock_while_it_runs_and_stop_cancels_the_queue_releases_every_held_key_and_unlocks()
    {
        var rig = new Rig(output, Den, new AutopilotOptions(), chatty: true);
        var controller = new MovementKeyLockTests.RecordingIntents();
        rig.Moves.Bind(controller, false);
        var chat = new List<string>();
        rig.Host.ChatNotice += chat.Add;
        Assert.False(rig.Moves.KeyLock.Locked);

        rig.Host.Toggle(rig.View, reason: AutopilotHostLogic.ResumeReason);
        Assert.True(rig.Host.KeysLocked); // the autopilot has the controls
        Assert.True(rig.RunUntil(() => rig.Fighting()));
        Assert.True(rig.Moves.KeyLock.Locked);

        // Automation holds a key (applied by the session's tick) and has work queued that it has not sent (the chat lane's burst is spent).
        var walk = rig.Moves.Command(Forward, true, ActionSource.Automation);
        Assert.True(rig.RunUntil(() => walk.IsCompleted, 50));
        Assert.Equal(1, rig.Moves.AutomationHeld);
        var queued = Enumerable.Range(0, 40).Select(i => rig.View.Actions.Talk("queued " + i, ActionSource.Automation)).ToList();
        Assert.Contains(queued, static h => !h.IsCompleted);
        var person = rig.View.Actions.Talk("the person's line", ActionSource.Ui);
        var sent = rig.Sent.Count;

        Assert.Equal(AutopilotHostLogic.StoppedHereLine, rig.Host.StopHere(rig.View)); // /stop
        Assert.Equal(AutopilotMode.Interrupted, rig.Host.Mode);
        Assert.All(queued, static h => Assert.True(h.IsCompleted, "every queued automation action is cancelled"));
        Assert.Contains(queued, static h => h.Outcome!.Value.Kind == ActionOutcomeKind.Cancelled);
        Assert.False(person.IsCompleted && person.Outcome!.Value.Kind == ActionOutcomeKind.Cancelled); // the person's own line is not the autopilot's to cancel
        Assert.Equal((0, 0), (rig.Moves.AutomationHeld, rig.Moves.QueuedAutomation)); // nothing stays held
        Assert.Contains($"cmd {Forward:X8} False", controller.Calls); // the walk was let go on the controller
        Assert.False(rig.Moves.KeyLock.Locked); // the keys are the person's: after /stop, W moves him
        Assert.False(rig.View.Actions.KillSwitch); // a stop, not the switch: the person's actions go on being accepted
        Assert.DoesNotContain(chat, static l => l.StartsWith("Lifestone:", StringComparison.Ordinal)); // /stop's own answer says it; no second line
        Assert.Contains(rig.World.Lines, static l => l.Contains("stopped by /stop where it stands (no recall, no log off)", StringComparison.Ordinal));
        Assert.DoesNotContain("recall-home", AutopilotRig.Kinds(rig.World));

        rig.Frame(); // the check one frame after: nothing held, nothing queued, the keys unlocked
        Assert.Contains(rig.World.Lines, static l => l.Contains(AutopilotHostLogic.StandDownCheckLine + ": ok", StringComparison.Ordinal));
        for (var i = 0; i < 40; i++)
        {
            rig.Frame();
        }

        Assert.Equal(sent, rig.Sent.Count); // the loop is not stepped
        Assert.Equal(AutopilotMode.Interrupted, rig.Host.Mode);

        // /auto hands the controls back: the keys lock again.
        rig.Host.Toggle(rig.View, reason: AutopilotHostLogic.ResumeReason);
        Assert.Equal((AutopilotMode.Running, true), (rig.Host.Mode, rig.Moves.KeyLock.Locked));
        rig.Host.Dispose();
        Assert.False(rig.Moves.KeyLock.Locked);
    }

    [Fact]
    public void M5_7_D145_the_locks_notice_goes_to_the_chat_rate_limited_and_a_key_never_takes_the_controls()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        var chat = new List<string>();
        rig.Host.ChatNotice += chat.Add;
        rig.Host.Toggle(rig.View);
        Assert.True(rig.RunUntil(() => rig.Fighting()));
        chat.Clear(); // the start's audit summary (P7n); this test reads the lock's notice
        var keys = rig.Moves.KeyLock;

        // The person's input path asks the lock (MovementBindings.Lock): swallowed, logged, the notice once in a few seconds.
        Assert.True(keys.Swallow(MovementBindings.MoveForward, "MoveForward", true, 10));
        Assert.True(keys.Swallow(MovementBindings.MoveForward, "MoveForward", false, 10.5));
        Assert.True(keys.Swallow(MovementBindings.TurnLeft, "TurnLeft", true, 11));
        Assert.Equal([HumanKeyLock.NoticeLine], chat);
        rig.Moves.ReportHumanKey("MoveForward"); // even a key reported past the lock does nothing to the dial now (D98's hand-off is retired)
        Assert.Equal((AutopilotMode.Running, 0), (rig.Host.Mode, rig.Host.Interrupted));

        rig.Host.StopHere(rig.View);
        Assert.False(keys.Swallow(MovementBindings.MoveForward, "MoveForward", true, 20)); // after /stop the key goes through
        Assert.Single(chat);
        rig.Host.Dispose();
    }

    [Fact]
    public void M5_7_D145_a_stop_it_makes_on_its_own_unlocks_the_keys_and_says_why_in_the_chat()
    {
        var rig = new Rig(output, Den, new AutopilotOptions { Minutes = 0.5 }); // the minutes rule in a run a person started (not the soak)
        var chat = new List<string>();
        rig.Host.ChatNotice += chat.Add;
        rig.Host.Toggle(rig.View, reason: AutopilotHostLogic.ResumeReason);
        Assert.True(rig.Moves.KeyLock.Locked);
        Assert.True(rig.RunUntil(() => rig.Host.Mode == AutopilotMode.Done));
        Assert.False(rig.Moves.KeyLock.Locked);
        var line = Assert.Single(chat.FindAll(static l => l.StartsWith("Lifestone:", StringComparison.Ordinal)));
        Assert.Contains("the autopilot stopped (finished: stop=minutes)", line, StringComparison.Ordinal);
        Assert.Contains("the controls are yours", line, StringComparison.Ordinal);
        Assert.Contains(rig.World.Lines, static l => l.Contains("the run ended by itself (finished: stop=minutes)", StringComparison.Ordinal));
        rig.Frame();
        Assert.Contains(rig.World.Lines, static l => l.Contains(AutopilotHostLogic.StandDownCheckLine + ": ok", StringComparison.Ordinal));
        rig.Host.Dispose();

        // A failure (stop=failed) the same way.
        var failing = new Rig(output, Den, new AutopilotOptions());
        var said = new List<string>();
        failing.Host.ChatNotice += said.Add;
        failing.World.Factory.FailWith = spec => spec.Kind == StationKinds.Buff ? "scripted: no" : null;
        failing.Host.Toggle(failing.View);
        Assert.True(failing.RunUntil(() => failing.Host.Mode == AutopilotMode.Done));
        Assert.False(failing.Moves.KeyLock.Locked);
        Assert.Contains("the autopilot stopped (failed:", Assert.Single(said.FindAll(static l => l.StartsWith("Lifestone:", StringComparison.Ordinal))), StringComparison.Ordinal);
        failing.Host.Dispose();
    }

    [Fact]
    public void M5_7_D145_the_kill_switch_and_a_disposed_dial_never_leave_the_keys_locked()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        var chat = new List<string>();
        rig.Host.ChatNotice += chat.Add;
        rig.Host.Toggle(rig.View);
        Assert.True(rig.RunUntil(() => rig.Fighting()));
        rig.View.Actions.KillSwitch = true;
        rig.Host.Tick(rig.View, rig.World.Now);
        Assert.Equal(AutopilotMode.Halted, rig.Host.Mode);
        Assert.False(rig.Moves.KeyLock.Locked);
        Assert.Contains("the kill switch halted the autopilot", Assert.Single(chat.FindAll(static l => l.StartsWith("Lifestone:", StringComparison.Ordinal))), StringComparison.Ordinal);
        rig.View.Actions.KillSwitch = false;
        rig.Host.Dispose();

        // A host fault disposes the dial while it runs: the keys are unlocked and the automation let go.
        var faulted = new Rig(output, Den, new AutopilotOptions());
        faulted.Host.Toggle(faulted.View);
        Assert.True(faulted.RunUntil(() => faulted.Fighting()));
        Assert.True(faulted.Moves.KeyLock.Locked);
        faulted.Host.Dispose();
        Assert.False(faulted.Moves.KeyLock.Locked);
        Assert.Contains(faulted.World.Lines, static l => l.Contains("autopilot keys: unlocked (the autopilot was disposed (running))", StringComparison.Ordinal));
    }

    // M6 6 MD1: m5-autopilot-test-chat, the scripted hand-off's bounded and logged wait for a main chat (the host's AutopilotTestHandOff asks it before each typing; it never calls into the logic).
    [Fact]
    public void M6_6_MD1_test_chat_the_wait_names_itself_once_ends_when_a_main_chat_is_up_and_says_how_long()
    {
        var wait = new ChatWait(10);
        Assert.Equal(ChatWaitResult.Waiting, wait.Poll(1, false, out var first));
        Assert.Contains("waiting up to 10 s before typing", first, StringComparison.Ordinal);
        Assert.Equal(ChatWaitResult.Waiting, wait.Poll(1, false, out var second));
        Assert.Null(second); // one line a wait, not one a frame
        Assert.Equal(ChatWaitResult.Ready, wait.Poll(1, true, out var ready));
        Assert.Contains("is up after 2 s; typing", ready, StringComparison.Ordinal);
        Assert.Equal(ChatWaitResult.Ready, wait.Poll(1, true, out var quiet));
        Assert.Null(quiet); // a chat that was up all along says nothing
    }

    [Fact]
    public void M6_6_MD1_test_chat_the_wait_is_bounded_and_gives_up_by_its_named_todo()
    {
        var wait = new ChatWait(3);
        Assert.Equal(ChatWaitResult.Waiting, wait.Poll(1, false, out _));
        Assert.Equal(ChatWaitResult.Waiting, wait.Poll(1, false, out _));
        Assert.Equal(ChatWaitResult.GaveUp, wait.Poll(1, false, out var line));
        Assert.Contains("TODO(m5-autopilot-test-chat)", line, StringComparison.Ordinal);
        Assert.Contains("within 3 s", line, StringComparison.Ordinal);
        Assert.Equal(0, wait.Waited); // the next wait (the hand-back's) starts from nothing
        Assert.Equal(ChatWaitResult.Waiting, wait.Poll(1, false, out var again));
        Assert.NotNull(again);
    }

    [Fact]
    public void M6_6_MD1_test_chat_a_screen_that_comes_back_in_time_restarts_the_count_for_the_next_wait()
    {
        var wait = new ChatWait(5);
        Assert.Equal(ChatWaitResult.Waiting, wait.Poll(4, false, out _));
        Assert.Equal(ChatWaitResult.Ready, wait.Poll(1, true, out _));
        Assert.Equal(ChatWaitResult.Waiting, wait.Poll(4, false, out _)); // 4 s of a new wait, not 8 s of one
    }
}
