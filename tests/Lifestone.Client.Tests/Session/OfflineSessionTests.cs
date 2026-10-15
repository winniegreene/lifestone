using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.World;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// Offline sessions (ST7b, checkpoint C-state-offline): <see cref="GameSession.Offline"/> fed by
/// <see cref="StandInSessions"/> through the same reducer as a live session, every server action refused, selection
/// local, the timed data check run by <see cref="GameSession.Tick"/>, the disconnected screen's failure, and reconnect.
/// </summary>
public sealed class OfflineSessionTests
{
    [Fact]
    public void An_offline_session_shows_the_stand_in_list_at_character_select()
    {
        var log = new RecordingLogSink();
        using var session = GameSession.Offline(h => StandInSessions.CharacterSelect(h), new ManualTimeProvider(), log);

        var state = session.State;
        Assert.True(state.IsOffline);
        Assert.Equal(SessionPhase.CharacterSelect, state.Phase);
        Assert.Equal(DataCheckState.Complete, state.DataCheck);
        Assert.Equal(StandInSessions.WorldName, state.WorldName);
        Assert.Equal(["Stand-in Alpha", "Stand-in Beta", "Stand-in Gamma"], state.Characters.Entries.Select(e => e.Name));
        Assert.Equal([false, false, true], state.Characters.Entries.Select(e => e.PendingDeletion));
        Assert.Equal(StandInSessions.FirstCharacterGuid + 2, state.Characters.Entries[2].Guid);
        Assert.Equal(StandInSessions.SlotCount, state.Characters.SlotCount);
        Assert.Equal(default, session.Link);
        Assert.True(log.AnyLineContains("offline session"));
    }

    [Fact]
    public void Server_actions_are_refused_and_selection_is_local()
    {
        var log = new RecordingLogSink();
        using var session = GameSession.Offline(h => StandInSessions.CharacterSelect(h, new StandInCharacter("Rook"), new StandInCharacter("Knight", PendingDeletion: true)), new ManualTimeProvider(), log);
        var selected = new List<uint>();
        session.Events.Subscribe((in CharacterSelected e) => selected.Add(e.Guid));
        var actions = session.Actions;
        var guid = session.State.Characters.Entries[0].Guid;

        ActionHandle[] refused =
        [
            actions.Login(),
            actions.EnterWorld(guid, ActionSource.Ui),
            actions.DeleteCharacter(guid, ActionSource.Ui),
            actions.RestoreCharacter(guid, ActionSource.Ui),
            actions.LoginComplete(),
            actions.Use(guid, ActionSource.Ui),
            actions.QueryHealth(guid, ActionSource.Ui),
            actions.Talk("hello", ActionSource.Ui),
            actions.Tell("Someone", "hello", ActionSource.Ui),
            actions.LogOff(ActionSource.Ui),
            actions.Quit(ActionSource.Ui),
        ];
        foreach (var handle in refused)
        {
            Assert.True(handle.IsCompleted);
            Assert.Equal(ActionOutcomeKind.Refused, handle.Outcome!.Value.Kind);
            Assert.Contains("offline", handle.Outcome.Value.Detail, StringComparison.Ordinal);
            Assert.True(handle.Completion.IsCompletedSuccessfully);
        }

        var select = actions.SelectCharacter(guid, ActionSource.Ui);
        Assert.Equal(ActionOutcomeKind.Local, select.Outcome!.Value.Kind);
        Assert.Equal(guid, session.State.Characters.SelectedGuid);
        Assert.Equal([guid], selected);
        Assert.Equal(ActionOutcomeKind.Refused, actions.SelectCharacter(0x5000FFFF, ActionSource.Ui).Outcome!.Value.Kind);

        Assert.Equal(refused.Length + 2, session.ActionLog.Count);
        Assert.Equal("Talk chars=5", session.ActionLog[7].Summary);
        Assert.Equal(Enumerable.Range(1, refused.Length + 2).Select(i => (long)i), Enumerable.Range(0, session.ActionLog.Count).Select(i => session.ActionLog[i].Id));
        Assert.DoesNotContain(log.Lines, l => l.Contains("hello", StringComparison.Ordinal));
    }

    [Fact]
    public void The_kill_switch_and_dry_run_apply_to_automation_only()
    {
        using var session = GameSession.Offline(h => StandInSessions.CharacterSelect(h), new ManualTimeProvider(), NullLogSink.Instance);
        var guid = session.State.Characters.Entries[0].Guid;
        session.Actions.KillSwitch = true;
        Assert.Contains("kill switch", session.Actions.SelectCharacter(guid, ActionSource.Automation).Outcome!.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(ActionOutcomeKind.Local, session.Actions.SelectCharacter(guid, ActionSource.Ui).Outcome!.Value.Kind);
        session.Actions.KillSwitch = false;
        session.Actions.DryRun = true;
        Assert.Equal(ActionOutcomeKind.DryRun, session.Actions.Talk("hi", ActionSource.Automation).Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, session.Actions.Talk("hi", ActionSource.Host).Outcome!.Value.Kind);
    }

    [Fact]
    public void The_timed_data_check_completes_in_tick_on_the_session_clock()
    {
        var time = new ManualTimeProvider();
        using var session = GameSession.Offline(
            h => StandInSessions.DataCheckThenCharacterSelect(h, StandInSessions.DefaultDataCheckDuration),
            time,
            NullLogSink.Instance);
        var phases = new List<SessionPhase>();
        session.Events.Subscribe((in SessionPhaseChanged e) => phases.Add(e.Phase));
        Assert.Equal(SessionPhase.DataCheck, session.State.Phase);
        Assert.Equal(DataCheckState.Checking, session.State.DataCheck);

        time.Advance(TimeSpan.FromSeconds(1.9));
        session.Tick(1.9);
        Assert.Equal(SessionPhase.DataCheck, session.State.Phase);
        Assert.Empty(session.State.Characters.Entries);

        time.Advance(TimeSpan.FromSeconds(0.1));
        session.Tick(2.0);
        Assert.Equal(SessionPhase.CharacterSelect, session.State.Phase);
        Assert.Equal(DataCheckState.Complete, session.State.DataCheck);
        Assert.Equal(3, session.State.Characters.Entries.Count);
        Assert.Equal([SessionPhase.CharacterSelect], phases);
        Assert.Equal(2.0, session.FrameTime);
        Assert.True(session.LastTickMs >= 0);
    }

    [Fact]
    public void A_data_check_in_progress_stays_in_the_data_check()
    {
        using var session = GameSession.Offline(StandInSessions.DataCheckInProgress, new ManualTimeProvider(), NullLogSink.Instance);
        Assert.Equal(SessionPhase.DataCheck, session.State.Phase);
        session.Tick(100);
        Assert.Equal(DataCheckState.Checking, session.State.DataCheck);
    }

    [Fact]
    public void Disconnected_fails_with_the_given_failure_and_reconnect_runs_the_stand_in_again()
    {
        var runs = 0;
        using var session = GameSession.Offline(
            h =>
            {
                if (runs++ == 0)
                {
                    StandInSessions.Disconnected(h, StandInSessions.ServerStopped);
                }
                else
                {
                    StandInSessions.CharacterSelect(h);
                }
            },
            new ManualTimeProvider(),
            NullLogSink.Instance);
        Assert.Equal(SessionPhase.Failed, session.State.Phase);
        Assert.Same(StandInSessions.ServerStopped, session.State.Failure);
        var changes = new List<SessionPhaseChanged>();
        session.Events.Subscribe((in SessionPhaseChanged e) => changes.Add(e));

        session.Reconnect();
        Assert.Equal(SessionPhase.CharacterSelect, session.State.Phase);
        Assert.Null(session.State.Failure);
        Assert.Equal(SessionPhase.Idle, changes[0].Phase);
        Assert.Equal(SessionPhase.CharacterSelect, changes[^1].Phase);
        Assert.True(session.State.IsOffline);
        Assert.Equal(2, runs);
    }

    [Fact]
    public void A_failure_before_the_data_check_stops_at_its_own_phase()
    {
        using var session = GameSession.Offline(h => StandInSessions.Disconnected(h, StandInSessions.NoServer), new ManualTimeProvider(), NullLogSink.Instance);
        Assert.Equal(SessionPhase.Failed, session.State.Phase);
        Assert.Equal(DataCheckState.NotStarted, session.State.DataCheck);
        Assert.Equal(SessionFailureKind.ConnectTimeout, session.State.Failure!.Kind);
    }

    [Fact]
    public void Stand_in_data_checks_its_names_and_its_handler()
    {
        Assert.Throws<ArgumentException>(() => GameSession.Offline(h => StandInSessions.CharacterSelect(h, new StandInCharacter("")), new ManualTimeProvider(), NullLogSink.Instance));
        Assert.Throws<ArgumentException>(() => GameSession.Offline(h => StandInSessions.CharacterSelect(h, new StandInCharacter("Ünïcode")), new ManualTimeProvider(), NullLogSink.Instance));
        Assert.Throws<ArgumentException>(() => GameSession.Offline(h => StandInSessions.CharacterSelect(h, new StandInCharacter(new string('a', 33))), new ManualTimeProvider(), NullLogSink.Instance));
        Assert.Throws<ArgumentException>(() => StandInSessions.CharacterSelect(new PlainHandler()));
        var sink = new CountingSink();
        Assert.Throws<ArgumentException>(() => StandInSessions.DataCheckThenCharacterSelect(sink, TimeSpan.Zero));
        StandInSessions.CharacterSelect(sink);
        Assert.Equal(1, sink.Lists);
    }

    [Fact]
    public void Stand_in_names_are_Lifestone_authored()
    {
        Assert.All(StandInSessions.DefaultCharacters, c => Assert.StartsWith("Stand-in ", c.Name, StringComparison.Ordinal));
        Assert.StartsWith("Lifestone", StandInSessions.WorldName, StringComparison.Ordinal);
    }

    [Fact]
    public void Offline_movement_is_counted_and_logged_once()
    {
        var log = new RecordingLogSink();
        using var session = GameSession.Offline(h => StandInSessions.CharacterSelect(h), new ManualTimeProvider(), log);
        var position = new CellFrame(0xA9B40019, new Lifestone.Data.Animation.AcFrame(Vector3.Zero, Quaternion.Identity));
        session.Actions.Movement.AutonomousPosition(position, true);
        session.Actions.Movement.Jump(0.5f, Vector3.UnitZ, position);
        Assert.Single(log.Entries, e => e.Severity == LogSeverity.Warning && e.Message.Contains("movement messages are not sent", StringComparison.Ordinal));
    }

    [Fact]
    public void The_session_binds_to_the_thread_that_ticks_it()
    {
        // Created here, ticked first on another thread: from then on that thread owns it.
        using var session = GameSession.Offline(h => StandInSessions.CharacterSelect(h), new ManualTimeProvider(), NullLogSink.Instance);
        var other = new Thread(() => session.Tick(0));
        other.Start();
        other.Join();
        Assert.IsType<InvalidOperationException>(Record.Exception(() => session.Tick(1)));
        Assert.IsType<InvalidOperationException>(Record.Exception(() => session.Events.Subscribe((in ChatReceived _) => { })));
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => session.Tick(2));
    }

    private sealed class PlainHandler : IInboundMessageHandler
    {
    }

    private sealed class CountingSink : IAcSessionSink
    {
        public int Lists { get; private set; }

        public void OnSessionEvent(in SessionEvent sessionEvent)
        {
        }

        public void OnCharacterList(in InboundContext context, Lifestone.Net.Messages.CharacterListMsg message) => Lists++;
    }
}
