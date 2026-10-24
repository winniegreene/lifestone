using System.Collections.Immutable;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The offline stand-in's opt-in responder (<see cref="StandInSessions.AnswerLocally"/>; m2-plan G2 U3): delete, restore and create are
/// answered locally through the reducer (a synthetic F655 acknowledgement and F658, F643) and every other server action stays
/// refused; without the opt-in nothing changes, and a live session can neither have one nor reach one.
/// </summary>
public sealed class StandInResponderTests
{
    private const double Frame = 1.0 / 60.0;

    private sealed class Rig : IDisposable
    {
        public Rig(bool answer = true, TimeSpan? delay = null, params StandInCharacter[] characters)
        {
            Session = GameSession.Offline(
                h =>
                {
                    StandInSessions.CharacterSelect(h, characters);
                    if (answer)
                    {
                        StandInSessions.AnswerLocally(h, delay);
                    }
                },
                Time,
                NullLogSink.Instance);
            Session.Events.Subscribe((in CharacterListChanged e) => Reasons.Add(e.Reason));
            Session.Events.Subscribe((in CharacterCreateResult e) => Results.Add(e));
        }

        public ManualTimeProvider Time { get; } = new();

        public GameSession Session { get; }

        public List<CharacterListReason> Reasons { get; } = [];

        public List<CharacterCreateResult> Results { get; } = [];

        public IActionApi Actions => Session.Actions;

        public CharacterListState List => Session.State.Characters;

        /// <summary>Advances the clock and ticks, <paramref name="seconds"/> worth of frames.</summary>
        public void Run(double seconds)
        {
            var frames = Math.Max(1, (int)Math.Ceiling(seconds / Frame));
            for (var i = 0; i < frames; i++)
            {
                Time.Advance(TimeSpan.FromSeconds(Frame));
                Session.Tick(Session.Clock.Now);
            }
        }

        public void Dispose() => Session.Dispose();
    }

    private static CharacterCreateIntent Intent(string name) =>
        new(6, 1, default, 0, 10, 10, 10, 10, 10, 10, 1, [.. Enumerable.Repeat(1u, CharacterCreateRequest.RequiredSkillCount)], name, 0);

    [Fact]
    public void Without_the_opt_in_delete_restore_and_create_are_refused_as_before()
    {
        using var rig = new Rig(answer: false);
        var guid = rig.List.Entries[0].Guid;

        var delete = rig.Actions.DeleteCharacter(guid, ActionSource.Ui);
        var restore = rig.Actions.RestoreCharacter(rig.List.Entries[2].Guid, ActionSource.Ui);
        var create = rig.Actions.CreateCharacter(Intent("Nobody"), ActionSource.Ui);

        foreach (var handle in new[] { delete, restore, create })
        {
            Assert.True(handle.IsCompleted);
            Assert.Equal(ActionOutcomeKind.Refused, handle.Outcome!.Value.Kind);
            Assert.Contains("offline session", handle.Outcome.Value.Detail, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_delete_is_answered_with_an_acknowledgement_and_the_list_that_follows_it()
    {
        using var rig = new Rig(delay: TimeSpan.FromSeconds(0.5));
        var guid = rig.List.Entries[0].Guid;
        var versionBefore = rig.List.Version;

        var handle = rig.Actions.DeleteCharacter(guid, ActionSource.Ui);
        rig.Run(0.4);
        Assert.False(handle.IsCompleted);
        Assert.False(rig.List.Entries[0].PendingDeletion);

        rig.Run(0.2);

        Assert.True(handle.IsCompleted);
        Assert.Equal(ActionOutcomeKind.Confirmed, handle.Outcome!.Value.Kind);
        Assert.Equal([CharacterListReason.DeleteAcknowledged, CharacterListReason.Received], rig.Reasons);
        Assert.True(rig.List.Entries[0].PendingDeletion);
        Assert.Equal(1u, rig.List.Entries[0].DeleteTimeout); // ACE reports at least 1 (D52)
        Assert.Equal(["Stand-in Alpha", "Stand-in Beta", "Stand-in Gamma"], rig.List.Entries.Select(e => e.Name));
        Assert.Equal(StandInSessions.SlotCount, rig.List.SlotCount);
        Assert.True(rig.List.Version > versionBefore);
    }

    [Fact]
    public void A_restore_clears_the_pending_deletion_through_an_ok_answer()
    {
        using var rig = new Rig(delay: TimeSpan.Zero);
        var guid = rig.List.Entries[2].Guid;

        var handle = rig.Actions.RestoreCharacter(guid, ActionSource.Ui);
        rig.Run(0.1);

        Assert.Equal(ActionOutcomeKind.Confirmed, handle.Outcome!.Value.Kind);
        Assert.False(rig.List.Entries[2].PendingDeletion);
        Assert.Equal([CharacterListReason.Restored], rig.Reasons);
        var result = Assert.Single(rig.Results);
        Assert.True(result.Restore);
        Assert.True(result.Succeeded);
        Assert.Equal(1u, result.ResultCode);
        Assert.Equal(guid, result.Guid);
    }

    [Fact]
    public void A_create_appends_the_character_and_a_name_in_use_is_answered_as_the_server_does()
    {
        using var rig = new Rig(delay: TimeSpan.Zero);

        var created = rig.Actions.CreateCharacter(Intent("Wanderer"), ActionSource.Ui);
        rig.Run(0.1);

        Assert.Equal(ActionOutcomeKind.Confirmed, created.Outcome!.Value.Kind);
        Assert.Equal(4, rig.List.Entries.Count);
        Assert.Equal("Wanderer", rig.List.Entries[3].Name);
        Assert.False(rig.List.Entries[3].PendingDeletion);
        Assert.Equal(StandInSessions.FirstCharacterGuid + 3, rig.List.Entries[3].Guid);
        Assert.Equal([CharacterListReason.Created], rig.Reasons);

        var again = rig.Actions.CreateCharacter(Intent("WANDERER"), ActionSource.Ui);
        rig.Run(0.1);

        Assert.Equal(ActionOutcomeKind.ServerError, again.Outcome!.Value.Kind);
        Assert.Equal((uint)CharGenResult.NameInUse, again.Outcome.Value.Code);
        Assert.Equal(4, rig.List.Entries.Count);
        Assert.Contains(rig.Results, r => !r.Succeeded && r.ResultCode == (uint)CharGenResult.NameInUse && !r.Restore);
    }

    [Fact]
    public void Every_other_server_action_stays_refused_with_the_responder_on()
    {
        using var rig = new Rig();
        var guid = rig.List.Entries[0].Guid;

        ActionHandle[] refused =
        [
            rig.Actions.Login(),
            rig.Actions.EnterWorld(guid, ActionSource.Ui),
            rig.Actions.Talk("hello", ActionSource.Ui),
            rig.Actions.LogOff(ActionSource.Ui),
            rig.Actions.Quit(ActionSource.Ui),
        ];

        foreach (var handle in refused)
        {
            Assert.Equal(ActionOutcomeKind.Refused, handle.Outcome!.Value.Kind);
            Assert.Contains("offline session", handle.Outcome.Value.Detail, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_reconnect_runs_the_stand_in_again_and_keeps_answering()
    {
        using var rig = new Rig(delay: TimeSpan.Zero);
        rig.Session.Reconnect();
        Assert.Equal(3, rig.List.Entries.Count);

        var handle = rig.Actions.DeleteCharacter(rig.List.Entries[0].Guid, ActionSource.Ui);
        rig.Run(0.1);

        Assert.Equal(ActionOutcomeKind.Confirmed, handle.Outcome!.Value.Kind);
        Assert.True(rig.List.Entries[0].PendingDeletion);
    }

    [Fact]
    public void The_responder_attaches_to_an_offline_reducer_only()
    {
        // A handler that is not an offline session's reducer (here a live session's) cannot take the responder.
        using var live = new LiveHarness();
        Assert.Throws<ArgumentException>(() => StandInSessions.AnswerLocally(live.Session.Reducer));
        Assert.Throws<ArgumentException>(() => StandInSessions.AnswerLocally(new NullHandler()));
        Assert.Null(live.Session.Reducer.LocalResponder);
        Assert.Throws<ArgumentOutOfRangeException>(() => GameSession.Offline(h => StandInSessions.AnswerLocally(h, TimeSpan.FromSeconds(-1)), new ManualTimeProvider(), NullLogSink.Instance));
    }

    [Fact]
    public void A_live_session_still_sends_delete_to_its_network_session_and_does_not_answer_itself()
    {
        using var live = new LiveHarness();
        Assert.True(live.RunUntil(() => live.State.Phase == SessionPhase.Idle));
        Assert.Null(live.Session.Reducer.LocalResponder);

        // Not logged in: the precondition fails against the live reducer's state, not the offline refusal.
        var handle = live.Actions.DeleteCharacter(0x50000001, ActionSource.Ui);
        live.Run(0.2);

        Assert.True(handle.IsCompleted);
        Assert.Equal(ActionOutcomeKind.Refused, handle.Outcome!.Value.Kind);
        Assert.DoesNotContain("offline session", handle.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Contains("not at the character list", handle.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    private sealed class NullHandler : IInboundMessageHandler
    {
    }
}
