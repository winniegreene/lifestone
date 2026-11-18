using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Client.Tests.Session;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.RetailUi.Dialogs;

/// <summary>
/// M6 item 5 Q5 (closes <c>m5-6-headless-confirm-reply</c>, the UI half): a question the automation answers (<see cref="IActionApi.RespondToConfirmation"/> with <see cref="ActionSource.Automation"/>: the autopilot's tick, the quest station's
/// confirm step) closes locally as answered, and <see cref="ServerConfirmation"/> closes its dialog with no second answer; the player's own answer still closes it once.
/// </summary>
public sealed class ServerConfirmationAutomationTests
{
    private static void Ask(WorldActionRig r, uint type, uint context, string text) =>
        r.H.R.Reducer.OnConfirmationRequest(r.H.R.Context, new GameEventHeader(ReducerHarness.PlayerGuid, 9, GameEventTypes.CharacterConfirmationRequest), new ConfirmationRequestEvent(type, context, text));

    [Fact]
    public void An_answer_from_the_automation_closes_the_dialog_without_a_second_answer()
    {
        var r = new WorldActionRig();
        var scene = DialogFactoryTests.Scene();
        using var confirmation = new ServerConfirmation(scene.Manager, new View(r.H));
        Ask(r, 7, 0x21, "Do you want the tusk back?");
        Assert.Single(scene.Manager.Dialogs.OpenDialogs);

        r.H.Api.RespondToConfirmation(7, 0x21, false, ActionSource.Automation);
        r.Tick();
        r.Tick();

        Assert.Empty(scene.Manager.Dialogs.OpenDialogs);
        Assert.Equal(0, confirmation.Open);
        Assert.Equal(0, confirmation.Answered); // the dialog sent nothing
        var sent = Assert.Single(r.H.Net.Of(FakeSendKind.ConfirmationResponse)); // the automation's No, once
        Assert.Equal((7u, 0x21u, 0), (sent.Amount, sent.Guid, sent.Index));
        Assert.Contains(scene.Log.Lines, static l => l.Contains("was answered by the automation (No); its dialog closes", StringComparison.Ordinal));
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
