using Lifestone.Client.RetailUi.Options;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The stand-in's answers to the retail tranche (M5 6 k1; <c>StandInWorld.Retail6.cs</c>): each is ACE's answer (cited in that file), read back through the real Action API and the read model.
/// </summary>
public sealed class StandInRetail6Tests
{
    private sealed class Rig : IDisposable
    {
        private readonly ManualTimeProvider _time = new();

        public Rig(StandInVisit? visit = null)
        {
            Log = new RecordingLogSink();
            var v = visit ?? new StandInVisit
            {
                EnterAfter = 0.2,
                UnhideAfter = 1.0,
                Stats = new StandInPlayerStats { Level = 40 },
                Items = StandInServer.WithKey(),
            };
            Session = GameSession.Offline(
                h =>
                {
                    StandInSessions.CharacterSelect(h, new StandInCharacter("Stand-in Alpha"));
                    StandInSessions.PlayerVisits(h, v);
                    StandInSessions.AnswerWorld(h, v);
                },
                _time,
                Log);
            Server = StandInSessions.Server(Session.Reducer);
            Run(3.0);
        }

        public RecordingLogSink Log { get; }

        public GameSession Session { get; }

        public StandInServer Server { get; }

        public StandInWorld World => Session.Reducer.LocalResponder!.World!;

        public void Run(double seconds)
        {
            for (var t = 0.0; t < seconds - 1e-9; t += 0.1)
            {
                _time.Advance(TimeSpan.FromSeconds(0.1));
                Session.Tick(Session.Clock.Now);
            }
        }

        public IEnumerable<string> ChatText() => Enumerable.Range(0, Session.State.Chat.Count).Select(i => Session.State.Chat[i].Text);

        public void Dispose() => Session.Dispose();
    }

    [Fact]
    public void A_question_is_asked_and_No_is_recorded_with_no_0276_because_ACE_sends_none_for_an_answer()
    {
        using var rig = new Rig();
        Assert.Equal(1u, rig.Server.AskQuestion());
        rig.Run(0.5);
        var open = Assert.Single(rig.Session.State.PendingConfirmations);
        Assert.Equal((StandInWorld.YesNoType, 1u, StandInWorld.StandInQuestionText), (open.Type, open.Context, open.Text));
        Assert.Equal(0u, rig.Server.AskQuestion()); // ACE keeps one question of a type

        var answer = rig.Session.Actions.RespondToConfirmation(open.Type, open.Context, false, ActionSource.Ui);
        rig.Run(0.5);
        Assert.True(answer.IsCompleted);
        Assert.Equal(ActionOutcomeKind.Sent, answer.Outcome!.Value.Kind);
        Assert.Equal([(StandInWorld.YesNoType, 1u, false)], rig.Server.QuestionAnswers);
        Assert.Empty(rig.Session.State.PendingConfirmations);
        rig.Run(35); // past ACE's 30 s: an answered question does not time out
        Assert.Equal(0, rig.Server.QuestionsClosed);
    }

    [Fact]
    public void An_unanswered_question_is_closed_by_a_0276_after_thirty_seconds_with_ACEs_line()
    {
        using var rig = new Rig();
        rig.Server.AskQuestion();
        rig.Run(5);
        Assert.Single(rig.Session.State.PendingConfirmations);
        Assert.Equal(0, rig.Server.QuestionsClosed);
        rig.Run(27);
        Assert.Equal(1, rig.Server.QuestionsClosed);
        Assert.Empty(rig.Session.State.PendingConfirmations);
        Assert.Contains(rig.ChatText(), static t => t == StandInWorld.QuestionTimedOutLine);
        // 6-R1: ACE keeps the slot of a Yes/No question after its timeout (ConfirmationManager.cs:57-84) until the client answers, so a second question is refused...
        Assert.Equal(0u, rig.Server.AskQuestion());

        // ...and the answer (No, as the dialog sends for a done) frees it: the second question is asked and accepted.
        var answer = rig.Session.Actions.RespondToConfirmation(StandInWorld.YesNoType, 1, false, ActionSource.Ui);
        rig.Run(0.5);
        Assert.Equal(ActionOutcomeKind.Sent, answer.Outcome!.Value.Kind); // accepted although the question is closed: ACE still holds it
        Assert.Equal([(StandInWorld.YesNoType, 1u, false)], rig.Server.QuestionAnswers);
        Assert.Equal(2u, rig.Server.AskQuestion());
        rig.Run(0.5);
        Assert.Equal(2u, Assert.Single(rig.Session.State.PendingConfirmations).Context);
    }

    [Fact]
    public void A_question_timed_out_is_answered_No_by_the_dialog_and_the_next_one_of_the_type_is_asked()
    {
        using var rig = new Rig();
        var scene = Lifestone.Client.Tests.RetailUi.Dialogs.DialogFactoryTests.Scene();
        using var confirmation = new Lifestone.Client.RetailUi.Dialogs.ServerConfirmation(scene.Manager, rig.Session);
        rig.Server.AskQuestion();
        rig.Run(1);
        Assert.Single(scene.Manager.Dialogs.OpenDialogs);
        rig.Run(31); // the 30 s timeout: the 0x0276 closes the dialog, which answers No
        Assert.Equal(1, rig.Server.QuestionsClosed);
        Assert.Empty(scene.Manager.Dialogs.OpenDialogs);
        Assert.Equal(1, confirmation.ServerClosedReplies);
        Assert.Equal([(StandInWorld.YesNoType, 1u, false)], rig.Server.QuestionAnswers);
        Assert.Equal(2u, rig.Server.AskQuestion()); // the stand-in's slot was freed by that reply
        rig.Run(0.5);
        Assert.Single(scene.Manager.Dialogs.OpenDialogs); // the second question shows
    }

    [Fact]
    public void A_visit_scripts_a_question_after_the_unhide()
    {
        var visit = new StandInVisit
        {
            EnterAfter = 0.2,
            UnhideAfter = 1.0,
            Stats = new StandInPlayerStats { Level = 40 },
            Confirmations = [new StandInConfirmation(0.5, StandInWorld.YesNoType, "Scripted question?")],
        };
        using var rig = new Rig(visit);
        var open = Assert.Single(rig.Session.State.PendingConfirmations);
        Assert.Equal("Scripted question?", open.Text);
    }

    [Fact]
    public void The_options_words_are_stored_and_the_next_description_returns_them()
    {
        using var rig = new Rig();
        var q = rig.Session.State.Self.Qualities;
        var before = q.CharacterOptions1;
        var send = rig.Session.Actions.SetCharacterOptions(before | PlayerQualities.SideBySideVitalsOption, q.CharacterOptions2, ActionSource.Ui);
        rig.Run(0.5);
        Assert.True(send.IsCompleted);
        Assert.Equal(ActionOutcomeKind.Sent, send.Outcome!.Value.Kind); // no more "offline session" refusal
        Assert.Equal((before | PlayerQualities.SideBySideVitalsOption, q.CharacterOptions2), rig.Server.StoredOptions);
        Assert.Equal(1, rig.World.OptionSends);

        // The next description: a second entry of the run (the responder keeps the words as the database does across logins).
        rig.Session.Reconnect();
        rig.Run(4.0);
        Assert.Equal(before | PlayerQualities.SideBySideVitalsOption, rig.Session.State.Self.Qualities.CharacterOptions1);
    }

    [Fact]
    public void An_applied_option_tells_its_readers_again_when_the_model_takes_the_words_so_the_vitals_window_can_follow()
    {
        using var rig = new Rig();
        using var options = new GameOptions(rig.Session, rig.Log);
        var seen = new List<string>();
        options.Changed += o => seen.Add(o.Name);
        Assert.True(options.TrySet(PlayerOptionTable.SideBySideVitals, true));
        Assert.Contains(PlayerOptionTable.SideBySideVitals.Name, seen); // at the Apply, the words still pending
        Assert.False(rig.Session.State.Self.Qualities.SideBySideVitals);
        seen.Clear();
        rig.Run(0.5); // the tick sends the words; the stand-in takes them and the model holds them
        Assert.True(rig.Session.State.Self.Qualities.SideBySideVitals);
        Assert.Contains(PlayerOptionTable.SideBySideVitals.Name, seen); // told again, now that the model agrees: the shell's vitals window reads the model
    }

    [Fact]
    public void The_lifestone_recall_plays_the_recall_and_teleports_to_the_sanctuary()
    {
        using var rig = new Rig();
        var teleports = rig.World.Teleports;
        var recall = rig.Session.Actions.RecallLifestone(ActionSource.Ui);
        rig.Run(6);
        Assert.True(recall.IsCompleted);
        Assert.Equal(ActionOutcomeKind.Confirmed, recall.Outcome!.Value.Kind);
        Assert.Equal(teleports + 1, rig.World.Teleports);
        Assert.Contains(rig.ChatText(), static t => t.EndsWith(" is recalling to the lifestone.", StringComparison.Ordinal));
        Assert.Equal(1, rig.Server.ChatAnswers.LifestoneRecalls);
    }

    [Fact]
    public void The_marketplace_recall_waits_ACEs_fourteen_seconds_and_goes_to_the_stand_in_point()
    {
        using var rig = new Rig();
        var recall = rig.Session.Actions.RecallMarketplace(ActionSource.Ui);
        rig.Run(10);
        Assert.False(recall.IsCompleted);
        rig.Run(6);
        Assert.True(recall.IsCompleted);
        Assert.Equal(ActionOutcomeKind.Confirmed, recall.Outcome!.Value.Kind);
        Assert.Contains(rig.ChatText(), static t => t.EndsWith(" is recalling to the marketplace.", StringComparison.Ordinal));
        Assert.Equal(1, rig.Server.ChatAnswers.MarketplaceRecalls);
    }

    [Fact]
    public void An_emote_is_echoed_to_the_emoter_a_tell_is_refused_as_not_online_and_afk_answers_0x02D1()
    {
        using var rig = new Rig();
        var emote = rig.Session.Actions.Emote("waves", ActionSource.Ui);
        var tell = rig.Session.Actions.Tell("Nobody", "hello", ActionSource.Ui);
        var afk = rig.Session.Actions.SetAfk(true, "gone", ActionSource.Ui);
        rig.Run(1.5);
        Assert.Equal(ActionOutcomeKind.Confirmed, emote.Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.ServerError, tell.Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Confirmed, afk.Outcome!.Value.Kind);
        Assert.True(rig.Session.State.Self.Qualities.Afk);
        var answers = rig.Server.ChatAnswers;
        Assert.Equal(["waves"], answers.Emotes);
        Assert.Equal([("Nobody", "hello")], answers.Tells);
        Assert.True(answers.Afk);
        Assert.Equal("gone", answers.AfkMessage);

        var back = rig.Session.Actions.SetAfk(false, null, ActionSource.Ui);
        rig.Run(1.0);
        Assert.Equal(ActionOutcomeKind.Confirmed, back.Outcome!.Value.Kind);
        Assert.False(rig.Session.State.Self.Qualities.Afk);
    }

    [Fact]
    public void A_stand_in_key_unlocks_a_stand_in_chest_with_0x02D2_and_ACEs_line_and_the_chest_reads_locked_until_then()
    {
        using var rig = new Rig();
        const uint chest = 0x80000E01;
        Assert.NotNull(rig.Server.PlaceChest(chest, new System.Numerics.Vector3(0f, 2f, 0f)));
        rig.Run(1.0);
        rig.Server.LockChest(chest);
        rig.Run(1.0);
        Assert.True(rig.Session.State.Objects.TryGet(chest, out var placed));
        Assert.True(placed.Locked);

        var spawn = rig.Session.Tools.SpawnItem(StandInServer.KeyWcid, 1, ActionSource.Host);
        rig.Run(2.0);
        Assert.True(spawn.IsCompleted, spawn.Summary);
        var key = spawn.Result!.Guid;

        var use = rig.Session.Actions.Use(chest, ActionSource.Ui);
        rig.Run(1.0);
        Assert.True(use.IsCompleted);
        Assert.True(rig.Session.State.Objects.TryGet(chest, out var stillLocked) && stillLocked.Locked == true);

        var unlock = rig.Session.Actions.UseWithTarget(key, chest, ActionSource.Ui);
        rig.Run(1.5);
        Assert.True(unlock.IsCompleted);
        Assert.True(rig.Session.State.Objects.TryGet(chest, out var opened) && opened.Locked == false);
        Assert.Contains(rig.ChatText(), static t => t.EndsWith(" has been unlocked.\n", StringComparison.Ordinal));
        Assert.False(rig.World.IsLockedChest(chest));
    }
}
