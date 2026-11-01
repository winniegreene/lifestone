using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Client.Tests.Session;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.RetailUi.Dialogs;

/// <summary>
/// M5 6 u1, the server's questions as dialogs (<see cref="ServerConfirmation"/>): shown with the server's text as a type 1 confirmation, Yes and No send the answer once, a question the server
/// closes closes its dialog without an answer, and two questions of different types queue (retail refuses the second: the single-slot flag restores that). The dialog layout is the synthetic one of
/// <see cref="DialogFactoryTests"/> (Yes at 330,310; No at 430,310); the session is ACE's reducer and the real action API over a fake network.
/// </summary>
public sealed class ServerConfirmationTests
{
    private const uint Player = ReducerHarness.PlayerGuid;

    /// <summary>How a question ended without the player's answer.</summary>
    public enum EndedBy
    {
        /// <summary>GameEvent 0x0276.</summary>
        ServerDone,

        /// <summary>A newer question of the same type.</summary>
        Replaced,

        /// <summary>The character left the world.</summary>
        WorldExit,
    }

    private static void Ask(WorldActionRig r, uint type, uint context, string text = "Really?") =>
        r.H.R.Reducer.OnConfirmationRequest(r.H.R.Context, new GameEventHeader(Player, 9, GameEventTypes.CharacterConfirmationRequest), new ConfirmationRequestEvent(type, context, text));

    private static void Done(WorldActionRig r, uint type, uint context) =>
        r.H.R.Reducer.OnConfirmationDone(r.H.R.Context, new GameEventHeader(Player, 10, GameEventTypes.CharacterConfirmationDone), new ConfirmationDoneEvent(type, context));

    private static string TextOf(UiScene scene)
    {
        var dialog = scene.Manager.Dialogs.OpenDialogs.First();
        return Assert.IsType<UiTextElement>(dialog.GetChildRecursive(UiDialog.TextElement)).Text;
    }

    [Fact]
    public void A_question_shows_as_a_yes_no_confirmation_with_the_servers_text_and_yes_answers_once()
    {
        var r = new WorldActionRig();
        var scene = DialogFactoryTests.Scene();
        using var confirmation = new ServerConfirmation(scene.Manager, new View(r.H));

        Ask(r, 7, 0x21, "Do you really want to?");
        Assert.IsType<UiConfirmationDialog>(Assert.Single(scene.Manager.Dialogs.OpenDialogs));
        Assert.Equal("Do you really want to?", TextOf(scene)); // type 7 shows the text as it is
        Assert.Equal([(7u, 0x21u)], confirmation.Questions);

        scene.Click(330, 310); // Yes
        r.Tick();

        var sent = Assert.Single(r.H.Net.Of(FakeSendKind.ConfirmationResponse));
        Assert.Equal((7u, 0x21u, 1), (sent.Amount, sent.Guid, sent.Index));
        Assert.Equal(1, confirmation.Answered);
        Assert.Empty(scene.Manager.Dialogs.OpenDialogs);
        Assert.Equal(0, confirmation.Open);
        Assert.Null(r.H.State.PendingConfirmation);
        Assert.Contains(scene.Log.Lines, static l => l.Contains("answered Yes", StringComparison.Ordinal));
    }

    [Fact]
    public void No_sends_a_refusal_and_the_craft_style_types_add_Continue()
    {
        var r = new WorldActionRig();
        var scene = DialogFactoryTests.Scene();
        using var confirmation = new ServerConfirmation(scene.Manager, new View(r.H));

        Ask(r, 5, 0x30, "This will consume the gem.");
        Assert.Equal("This will consume the gem. Continue?", TextOf(scene)); // CraftInteraction, AlterSkill, AlterAttribute and Augmentation: retail appends " Continue?"
        scene.Click(430, 310); // No
        r.Tick();

        var sent = Assert.Single(r.H.Net.Of(FakeSendKind.ConfirmationResponse));
        Assert.Equal((5u, 0x30u, 0), (sent.Amount, sent.Guid, sent.Index));
        Assert.Equal(1, confirmation.Answered);
    }

    [Theory]
    [InlineData(EndedBy.ServerDone)]
    [InlineData(EndedBy.Replaced)]
    [InlineData(EndedBy.WorldExit)]
    public void A_question_the_server_closes_closes_its_dialog_and_only_a_done_is_answered_with_No(EndedBy how)
    {
        var r = new WorldActionRig();
        var scene = DialogFactoryTests.Scene();
        using var confirmation = new ServerConfirmation(scene.Manager, new View(r.H));
        Ask(r, 7, 0x21);
        Assert.Single(scene.Manager.Dialogs.OpenDialogs);

        switch (how)
        {
            case EndedBy.ServerDone:
                Done(r, 7, 0x21);
                break;
            case EndedBy.Replaced:
                Ask(r, 7, 0x22, "A newer one"); // ACE keeps one question per type: the new one replaces the old
                break;
            default:
                r.H.R.Reducer.OnCharacterLogOff(r.H.R.Context);
                break;
        }

        r.Tick();
        if (how == EndedBy.Replaced)
        {
            Assert.Equal([(7u, 0x22u)], confirmation.Questions);
            Assert.Equal("A newer one", TextOf(scene)); // the old dialog closed, the new one shows
        }
        else
        {
            Assert.Empty(scene.Manager.Dialogs.OpenDialogs);
            Assert.Equal(0, confirmation.Open);
        }

        Assert.Equal(0, confirmation.Answered); // the player gave none
        if (how == EndedBy.ServerDone)
        {
            // 6-R1: the done of a type ACE keeps (7 here) is answered with No, so ACE's slot for that type is freed.
            var sent = Assert.Single(r.H.Net.Of(FakeSendKind.ConfirmationResponse));
            Assert.Equal((7u, 0x21u, 0), (sent.Amount, sent.Guid, sent.Index));
            Assert.Equal(1, confirmation.ServerClosedReplies);
        }
        else
        {
            Assert.Empty(r.H.Net.Of(FakeSendKind.ConfirmationResponse));
        }
    }

    [Theory]
    [InlineData(2u, true)]
    [InlineData(3u, true)]
    [InlineData(5u, true)]
    [InlineData(6u, true)]
    [InlineData(7u, true)]
    [InlineData(1u, false)]
    [InlineData(4u, false)]
    public void The_done_of_a_type_ACE_keeps_is_answered_No_once_and_the_rest_are_not(uint type, bool answered)
    {
        var r = new WorldActionRig();
        var scene = DialogFactoryTests.Scene();
        using var confirmation = new ServerConfirmation(scene.Manager, new View(r.H));
        Ask(r, type, 0x40);

        Done(r, type, 0x40);
        r.Tick();
        r.Tick();

        Assert.Empty(scene.Manager.Dialogs.OpenDialogs);
        var sends = r.H.Net.Of(FakeSendKind.ConfirmationResponse);
        if (answered)
        {
            var sent = Assert.Single(sends); // ACE's ConfirmationManager.cs:57-84 keeps the slot of types 2, 3, 5, 6, 7 until this 0x0275
            Assert.Equal((type, 0x40u, 0), (sent.Amount, sent.Guid, sent.Index));
            Assert.Empty(r.H.State.ClosedAwaitingReply); // the reply was sent: nothing is owed
        }
        else
        {
            Assert.Empty(sends); // ACE removes types 1 and 4 itself
        }
    }

    [Fact]
    public void A_done_with_no_dialog_shown_is_still_answered_so_the_servers_slot_is_freed()
    {
        var r = new WorldActionRig();
        var scene = DialogFactoryTests.Scene();
        using var confirmation = new ServerConfirmation(scene.Manager, new View(r.H), retailSingleSlot: true);
        Ask(r, 7, 0x21, "First?");
        Ask(r, 5, 0x22, "Dropped by the single-slot flag?"); // never shown
        Assert.Single(scene.Manager.Dialogs.OpenDialogs);

        Done(r, 5, 0x22); // ACE times the dropped one out
        r.Tick();

        var sent = Assert.Single(r.H.Net.Of(FakeSendKind.ConfirmationResponse));
        Assert.Equal((5u, 0x22u, 0), (sent.Amount, sent.Guid, sent.Index));
        Assert.Single(scene.Manager.Dialogs.OpenDialogs); // the first is still open
    }

    [Fact]
    public void The_reply_to_a_closed_question_is_accepted_once_and_a_second_one_is_refused()
    {
        var r = new WorldActionRig();
        Ask(r, 5, 0x31);
        Done(r, 5, 0x31);
        Assert.Equal([(5u, 0x31u)], r.H.State.ClosedAwaitingReply);

        var first = r.Api.RespondToConfirmation(5, 0x31, false, ActionSource.Ui);
        r.Tick();
        var second = r.Api.RespondToConfirmation(5, 0x31, false, ActionSource.Ui);
        r.Tick();

        Assert.Single(r.H.Net.Of(FakeSendKind.ConfirmationResponse));
        Assert.Equal(ActionOutcomeKind.Sent, first.Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, second.Outcome!.Value.Kind);
    }

    [Fact]
    public void Two_questions_of_different_types_queue_and_each_answer_goes_to_its_own_question()
    {
        var r = new WorldActionRig();
        var scene = DialogFactoryTests.Scene();
        using var confirmation = new ServerConfirmation(scene.Manager, new View(r.H));
        Ask(r, 7, 0x21, "First?");
        Ask(r, 6, 0x22, "Second?");

        Assert.Equal("First?", TextOf(scene));
        Assert.Single(scene.Manager.Dialogs.OpenDialogs); // one at a time
        Assert.Equal(2, confirmation.Open);

        scene.Click(330, 310); // Yes to the first
        r.Tick();
        Assert.Equal("Second? Continue?", TextOf(scene)); // the next opens when it closes
        scene.Click(430, 310); // No to the second
        r.Tick();

        var sent = r.H.Net.Of(FakeSendKind.ConfirmationResponse);
        Assert.Equal([(7u, 0x21u, 1), (6u, 0x22u, 0)], sent.Select(static s => (s.Amount, s.Guid, s.Index)).ToArray());
    }

    [Fact]
    public void Retails_single_slot_drops_the_second_question_when_the_flag_restores_it()
    {
        var r = new WorldActionRig();
        var scene = DialogFactoryTests.Scene();
        using var confirmation = new ServerConfirmation(scene.Manager, new View(r.H), retailSingleSlot: true);
        Ask(r, 7, 0x21, "First?");
        Ask(r, 6, 0x22, "Second?");

        Assert.Equal([(7u, 0x21u)], confirmation.Questions);
        Assert.Contains(scene.Log.Lines, static l => l.Contains("refuses while a confirmation is open", StringComparison.Ordinal));
    }

    [Fact]
    public void A_question_already_open_when_the_shell_is_made_is_shown_and_the_social_types_say_so_once()
    {
        var r = new WorldActionRig();
        Ask(r, 1, 0x40, "Swear allegiance?");
        Ask(r, 4, 0x41, "Join the fellowship?");
        var scene = DialogFactoryTests.Scene();
        using var confirmation = new ServerConfirmation(scene.Manager, new View(r.H));

        Assert.Equal([(1u, 0x40u), (4u, 0x41u)], confirmation.Questions);
        Assert.Single(scene.Log.Lines, static l => l.Contains("TODO(g6-confirm-social)", StringComparison.Ordinal));
    }

    private sealed class View(ActionHarness h) : IGameSessionView
    {
        public GameState State => h.State;

        public IEventBus Events => h.R.Bus;

        public IActionApi Actions => h.Api;

        public PlayerTools Tools => null!;

        public SessionClock Clock => h.R.Clock;

        public ActionLog ActionLog => h.ActionLog;

        public LinkStats Link => default;
    }
}
