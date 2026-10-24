using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The Action API's core (step ST9; docs/session.md §7): handles complete on the server's confirmation inside the update,
/// never on the send; timeouts read the session clock; session-phase actions go one at a time; rate limits queue rather
/// than drop; the kill switch and dry run touch automation only; continuations run inside the update on the game thread;
/// a loud stub fails visibly; every completion reaches the action log and one log line.
/// </summary>
public sealed class ActionApiCoreTests
{
    [Fact]
    public void Handle_completes_on_confirmation_not_on_send()
    {
        var h = new ActionHarness();
        h.Present();
        var use = h.Api.Use(0x80000100, ActionSource.Ui);
        Assert.Equal(ActionStatus.Queued, use.Status);
        Assert.Empty(h.Net.Of(FakeSendKind.Use));

        h.Tick();
        Assert.Equal(ActionStatus.Sent, use.Status);
        Assert.Equal(0x80000100u, Assert.Single(h.Net.Of(FakeSendKind.Use)).Guid);
        Assert.NotNull(use.SentAt);
        Assert.False(use.IsCompleted);

        // The confirmation arrives during the pump; the handle completes in the next update, not before.
        h.R.Reducer.OnUseDone(h.R.Context, h.Header, new UseDoneEvent(0));
        Assert.False(use.IsCompleted);
        h.Tick(0.1);
        Assert.Equal(ActionOutcomeKind.Confirmed, use.Outcome!.Value.Kind);
        Assert.True(use.Completion.IsCompletedSuccessfully);
        Assert.Equal(h.R.Clock.Now, use.CompletedAt);
    }

    [Fact]
    public void Timeout_comes_from_the_injected_clock()
    {
        var h = new ActionHarness();
        h.Present();
        var query = h.Api.QueryHealth(0x80000100, ActionSource.Ui);
        h.Tick();
        h.Tick(ActionPolicy.QueryHealthSeconds - 0.1);
        Assert.False(query.IsCompleted);

        h.Tick(0.1);
        Assert.Equal(ActionOutcomeKind.TimedOut, query.Outcome!.Value.Kind);
        Assert.Contains("within 5 s", query.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Contains(
            h.Log.Entries,
            e => e.Severity == LogSeverity.Warning && e.Message.Contains("QueryHealth", StringComparison.Ordinal) && e.Message.Contains("TimedOut", StringComparison.Ordinal));
    }

    [Fact]
    public void Session_phase_actions_are_serialised()
    {
        var h = new ActionHarness();
        h.AtCharacterSelect();
        var delete = h.Api.DeleteCharacter(ActionHarness.Cy, ActionSource.Ui);
        var restore = h.Api.RestoreCharacter(ActionHarness.Bo, ActionSource.Ui);
        var enter = h.Api.EnterWorld(ActionHarness.Amy, ActionSource.Ui);

        h.Tick();
        Assert.Equal([FakeSendKind.DeleteCharacter], h.Net.Kinds());
        Assert.Equal(ActionStatus.Queued, restore.Status);
        h.Tick(1);
        Assert.Equal([FakeSendKind.DeleteCharacter], h.Net.Kinds());

        // The delete completes, and the restore goes out in the same update; the enter waits for the restore.
        h.R.Reducer.OnCharacterDeleteAck(h.R.Context);
        h.CharacterList(("Amy", 0u, ActionHarness.Amy), ("Bo", 1u, ActionHarness.Bo), ("Cy", 1u, ActionHarness.Cy));
        h.Tick(0.1);
        Assert.Equal(ActionOutcomeKind.Confirmed, delete.Outcome!.Value.Kind);
        Assert.Equal([FakeSendKind.DeleteCharacter, FakeSendKind.RestoreCharacter], h.Net.Kinds());
        Assert.Equal(ActionStatus.Queued, enter.Status);
    }

    [Fact]
    public void Rate_limit_queues_then_sends()
    {
        var h = new ActionHarness();
        h.Present();
        var talks = Enumerable.Range(0, 11).Select(i => h.Api.Talk("line " + i, ActionSource.Ui)).ToArray();

        h.Tick();
        Assert.Equal(ActionPolicy.ChatBurst, h.Net.Said().Count);
        h.Tick(0.25);
        Assert.Equal(ActionPolicy.ChatBurst + 1, h.Net.Said().Count);
        h.Tick(0.5);
        Assert.Equal(ActionPolicy.ChatBurst + 3, h.Net.Said().Count);

        // Nothing was dropped, and the lines left in call order.
        Assert.Equal(talks.Select((_, i) => "line " + i), h.Net.Said());
        Assert.All(talks, t => Assert.False(t.IsCompleted));
    }

    [Fact]
    public void KillSwitch_refuses_and_cancels_only_automation()
    {
        var h = new ActionHarness();
        h.Present();
        var bot = h.Api.Talk("bot", ActionSource.Automation);
        var person = h.Api.Talk("person", ActionSource.Ui);

        h.Api.KillSwitch = true;
        Assert.Equal(ActionOutcomeKind.Cancelled, bot.Outcome!.Value.Kind);
        Assert.Equal(ActionApi.KillSwitchRefusal, bot.Outcome.Value.Detail);
        Assert.False(person.IsCompleted);
        var refused = h.Api.Use(0x80000100, ActionSource.Automation);
        Assert.Equal(ActionOutcomeKind.Refused, refused.Outcome!.Value.Kind);
        Assert.Contains(h.Log.Entries, e => e.Severity == LogSeverity.Warning && e.Message.Contains("kill switch on", StringComparison.Ordinal));

        h.Tick();
        Assert.Equal(["person"], h.Net.Said());

        h.Api.KillSwitch = false;
        Assert.False(h.Api.Talk("bot again", ActionSource.Automation).IsCompleted);
    }

    [Fact]
    public void DryRun_never_calls_the_session()
    {
        var h = new ActionHarness();
        h.Present();
        h.Tick();
        var before = h.Net.Sends.Count;
        h.Api.DryRun = true;
        var bot = h.Api.Use(0x80000100, ActionSource.Automation);
        var talk = h.Api.Talk("would say", ActionSource.Automation);
        h.Tick();

        Assert.Equal(ActionOutcomeKind.DryRun, bot.Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.DryRun, talk.Outcome!.Value.Kind);
        Assert.Contains("GameAction 0x0036 Use", bot.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(before, h.Net.Sends.Count);
        Assert.Contains(h.Log.Lines, l => l.Contains("dry run: Use guid=0x80000100", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Log.Lines, l => l.Contains("would say", StringComparison.Ordinal));

        // A person's action still goes.
        h.Api.Use(0x80000100, ActionSource.Ui);
        h.Tick();
        Assert.Single(h.Net.Of(FakeSendKind.Use));
    }

    [Fact]
    public void Continuations_run_inside_Tick_on_the_game_thread()
    {
        var h = new ActionHarness();
        h.Present();
        var context = new GameThreadSynchronizationContext();
        using var installed = context.Install();
        var gameThread = Environment.CurrentManagedThreadId;
        var continuedOn = 0;
        var updating = false;
        ActionHandle? next = null;

        async Task UseThenTalk()
        {
            await h.Api.Use(0x80000100, ActionSource.Automation).Completion;
            continuedOn = Environment.CurrentManagedThreadId;
            Assert.True(updating, "the continuation must run inside the update");
            next = h.Api.Talk("after the use", ActionSource.Automation);
        }

        var flow = UseThenTalk();
        h.Tick();
        h.R.Reducer.OnUseDone(h.R.Context, h.Header, new UseDoneEvent(0));
        updating = true;
        h.Tick(0.1);
        updating = false;

        Assert.True(flow.IsCompletedSuccessfully);
        Assert.Equal(0, context.PendingCount);
        Assert.Equal(gameThread, continuedOn);
        Assert.NotNull(next);
        Assert.Equal(ActionStatus.Queued, next.Status);
        h.Tick();
        Assert.Equal(["after the use"], h.Net.Said());
    }

    [Fact]
    public void Unimplemented_kind_fails_loudly()
    {
        var h = new ActionHarness();
        h.Present();
        var intent = h.Api.Move.Command(0x45000005, true, ActionSource.Automation);
        Assert.Equal(ActionOutcomeKind.NotImplemented, intent.Outcome!.Value.Kind);
        Assert.Contains("TODO(state)", intent.Outcome.Value.Detail, StringComparison.Ordinal);
        var error = Assert.Single(h.Log.Entries, e => e.Severity == LogSeverity.Error);
        Assert.Contains("MoveIntent Command 0x45000005 start", error.Message, StringComparison.Ordinal);

        // A net stub (NotSupportedException) is a loud stub too.
        h.Net.Throw = new NotSupportedException("TODO(net): N9 QueryHealth is not built yet");
        var query = h.Api.QueryHealth(0x80000100, ActionSource.Ui);
        h.Tick();
        Assert.Equal(ActionOutcomeKind.NotImplemented, query.Outcome!.Value.Kind);
        Assert.Contains("TODO(net)", query.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_send_the_network_session_refuses_is_refused_and_a_closed_one_cancels()
    {
        var h = new ActionHarness();
        h.Present();
        h.Tick();
        h.Net.Throw = new InvalidOperationException("Use is not allowed in phase Closing");
        var use = h.Api.Use(0x80000100, ActionSource.Ui);
        h.Tick();
        Assert.Equal(ActionOutcomeKind.Refused, use.Outcome!.Value.Kind);
        Assert.Contains("not allowed in phase Closing", use.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Null(use.SentAt);

        h.Net.Throw = new ObjectDisposedException("session");
        var talk = h.Api.Talk("hello", ActionSource.Ui);
        h.Tick();
        Assert.Equal(ActionOutcomeKind.Cancelled, talk.Outcome!.Value.Kind);
    }

    [Fact]
    public void Preconditions_are_checked_again_when_the_turn_comes()
    {
        var h = new ActionHarness();
        h.AtCharacterSelect();
        var enter = h.Api.EnterWorld(ActionHarness.Amy, ActionSource.Ui);
        var delete = h.Api.DeleteCharacter(ActionHarness.Amy, ActionSource.Ui);

        // While the enter is in flight the list changes: the delete's character is gone when its turn comes.
        h.Tick();
        h.CharacterList(("Cy", 0u, ActionHarness.Cy));
        h.R.Reducer.OnCharacterError(h.R.Context, new CharacterErrorMsg(0x0B));
        h.R.Reducer.OnSessionEvent(SessionEvent.CharacterErrorReceived(h.R.Time.GetTimestamp(), SessionPhase.CharacterSelect, 0x0B));
        h.Tick();
        Assert.Equal(ActionOutcomeKind.ServerError, enter.Outcome!.Value.Kind);
        Assert.Equal(0x0Bu, enter.Outcome.Value.Code);
        Assert.Equal(ActionOutcomeKind.Refused, delete.Outcome!.Value.Kind);
        Assert.Equal("the character is not in the list", delete.Outcome.Value.Detail);
    }

    [Fact]
    public void Every_completion_reaches_the_action_log_and_one_log_line()
    {
        var h = new ActionHarness();
        h.AtCharacterSelect();
        var select = h.Api.SelectCharacter(ActionHarness.Cy, ActionSource.Ui);
        var talk = h.Api.Talk("secret words", ActionSource.Ui);
        h.Tick();

        Assert.Equal(ActionOutcomeKind.Local, select.Outcome!.Value.Kind);
        Assert.Equal(ActionHarness.Cy, h.State.Characters.SelectedGuid);
        Assert.Equal(ActionOutcomeKind.Refused, talk.Outcome!.Value.Kind);
        Assert.Equal(2, h.ActionLog.Count);
        var entry = h.ActionLog[1];
        Assert.Equal((talk.Id, ActionKind.Talk, ActionSource.Ui, "Talk chars=12"), (entry.Id, entry.Kind, entry.Source, entry.Summary));
        Assert.Equal(talk.Outcome, entry.Outcome);
        var line = Assert.Single(h.Log.Entries, e => e.Message.StartsWith("action #" + talk.Id, StringComparison.Ordinal));
        Assert.Equal(LogSeverity.Info, line.Severity);
        Assert.Contains(line.Fields, f => f.Key == "ms");
        Assert.Contains(line.Fields, f => f.Key == "source" && Equals(f.Value, ActionSource.Ui));
        Assert.DoesNotContain(h.Log.Lines, l => l.Contains("secret words", StringComparison.Ordinal) || l.Contains(ActionHarness.Password, StringComparison.Ordinal));
    }

    [Fact]
    public void The_action_log_keeps_the_last_1024()
    {
        var h = new ActionHarness();
        h.AtCharacterSelect();
        for (var i = 0; i < ActionLog.Capacity + 6; i++)
        {
            h.Api.SelectCharacter(ActionHarness.Amy, ActionSource.Test);
        }

        Assert.Equal(ActionLog.Capacity, h.ActionLog.Count);
        Assert.Equal(ActionLog.Capacity + 6, h.ActionLog.Total);
        Assert.Equal(7, h.ActionLog[0].Id);
    }

    [Fact]
    public void A_quit_goes_first_and_cancels_everything_else()
    {
        var h = new ActionHarness();
        h.Present();
        var use = h.Api.Use(0x80000100, ActionSource.Ui);
        h.Tick();
        var talk = h.Api.Talk("bye", ActionSource.Ui);
        var logoff = h.Api.LogOff(ActionSource.Ui);
        var quit = h.Api.Quit(ActionSource.Host);
        h.Tick();

        Assert.Equal(ActionOutcomeKind.Sent, quit.Outcome!.Value.Kind);
        Assert.Equal("F653 then Disconnect", quit.Outcome.Value.Detail);
        Assert.All([use, talk, logoff], a => Assert.Equal(ActionOutcomeKind.Cancelled, a.Outcome!.Value.Kind));
        Assert.Equal(FakeSendKind.Quit, h.Net.Kinds()[^1]);
        Assert.Empty(h.Net.Of(FakeSendKind.LogOff));
        Assert.DoesNotContain(h.Net.Of(FakeSendKind.Talk), s => s.Text == "bye");

        var again = h.Api.Quit(ActionSource.Host);
        h.Tick();
        Assert.Equal(ActionOutcomeKind.Refused, again.Outcome!.Value.Kind);
    }

    [Fact]
    public void A_session_failure_ends_what_is_in_flight()
    {
        var h = new ActionHarness();
        h.Present();
        var use = h.Api.Use(0x80000100, ActionSource.Ui);
        var logoff = h.Api.LogOff(ActionSource.Ui);
        h.Tick();
        h.Phase(SessionPhase.InWorld, SessionPhase.Failed);
        var silence = new SessionFailure(SessionFailureKind.Silence, SessionPhase.LoggingOff, "nothing arrived for 20 s");
        h.R.Reducer.OnSessionEvent(SessionEvent.Failed(h.R.Time.GetTimestamp(), silence));
        h.Tick();

        Assert.Equal(ActionOutcomeKind.Cancelled, use.Outcome!.Value.Kind);
        Assert.Contains("the session failed", use.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(ActionOutcomeKind.TimedOut, logoff.Outcome!.Value.Kind);
        Assert.Contains("Silence", logoff.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Reset_and_dispose_cancel_what_is_pending()
    {
        var h = new ActionHarness();
        h.Present();
        var talk = h.Api.Talk("queued", ActionSource.Ui);
        h.Api.Reset();
        Assert.Equal(ActionOutcomeKind.Cancelled, talk.Outcome!.Value.Kind);
        Assert.Contains("reconnect", talk.Outcome.Value.Detail, StringComparison.Ordinal);

        var use = h.Api.Use(0x80000100, ActionSource.Ui);
        h.Api.Dispose();
        Assert.Equal(ActionOutcomeKind.Cancelled, use.Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Cancelled, h.Api.Talk("late", ActionSource.Ui).Outcome!.Value.Kind);
        Assert.Null(h.R.Reducer.ToolReplyRouted);
    }

    [Fact]
    public void Lanes_and_limits_are_the_documented_defaults()
    {
        Assert.Equal((4.0, 8), (ActionPolicy.ChatPerSecond, ActionPolicy.ChatBurst));
        Assert.Equal(4.0, ActionPolicy.UsePerSecond);
        Assert.Equal(2.0, ActionPolicy.ToolsPerSecond);
        Assert.Equal(
            [30.0, 15, 10, 10, 110, 15, 20, 5, 5, 5, 30, 5],
            [ActionPolicy.LoginSeconds, ActionPolicy.CreateSeconds, ActionPolicy.DeleteSeconds, ActionPolicy.RestoreSeconds,
             ActionPolicy.EnterWorldSeconds, ActionPolicy.LoginCompleteSeconds, ActionPolicy.UseSeconds, ActionPolicy.QueryHealthSeconds,
             ActionPolicy.TalkSeconds, ActionPolicy.TellSeconds, ActionPolicy.LogOffSeconds, ActionPolicy.ToolSeconds]);
        var limiter = new RateLimiter(2, 2);
        Assert.True(limiter.TryTake(0));
        Assert.True(limiter.TryTake(0));
        Assert.False(limiter.TryTake(0.4));
        Assert.True(limiter.TryTake(0.5));
        Assert.False(limiter.TryTake(0.5));
    }
}
