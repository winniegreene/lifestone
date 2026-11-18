using Lifestone.Client.Session.Autopilot;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 item 5 Q5 (closes <c>m5-6-headless-confirm-reply</c>): the headless host answered no server question; the autopilot's tick now decides every open question by Q1's <c>QuestConfirmRule</c> with no step, over the scripted ACE: an
/// unasked question of a type ACE keeps (7, a Yes/No) is answered No once, logged, and checked closed after; a swear allegiance (1) is left open, logged once, never answered.
/// </summary>
public sealed class AutopilotConfirmTests(ITestOutputHelper output)
{
    [Fact]
    public void The_tick_answers_an_unasked_yes_no_with_No_once_and_leaves_an_allegiance_open()
    {
        var w = new ApWorld(Den, output) { Prime = c => { c.Facts.LifestoneTied = true; c.Facts.SetPortalTie(Den, Lifestone.Client.Session.Scenarios.Journey.PortalTie.Tied); } };
        w.OwnKit();
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.State == AutopilotState.Fight);
        var h = w.Trip.Vendor.H;
        void Ask(uint type, uint context, string text) =>
            h.R.Reducer.OnConfirmationRequest(h.R.Context, new GameEventHeader(ReducerHarness.PlayerGuid, 9, GameEventTypes.CharacterConfirmationRequest), new ConfirmationRequestEvent(type, context, text));

        Ask(7, 0x51, "Will you help the tuskers?");
        Ask(1, 0x52, "Bob wants to swear allegiance to you.");
        var at = w.Now;
        w.Run(autopilot, _ => w.Now - at > 8);

        var sent = Assert.Single(h.Net.Of(FakeSendKind.ConfirmationResponse));
        Assert.Equal((7u, 0x51u, 0), (sent.Amount, sent.Guid, sent.Index));
        Assert.Single(w.Lines, l => l.StartsWith("autopilot confirm: type=7 text=\"Will you help the tuskers?\" decision=no why=unasked: no confirm step runs", StringComparison.Ordinal));
        Assert.Single(w.Lines, l => l.StartsWith("autopilot confirm: answered No (type 7, context 81, action ", StringComparison.Ordinal));
        Assert.Single(w.Lines, l => l.Equals("autopilot confirm: the question (type 7, context 81) closed after the answer", StringComparison.Ordinal));
        Assert.Single(w.Lines, l => l.StartsWith("autopilot confirm: type=1 text=\"Bob wants to swear allegiance to you.\" decision=leave", StringComparison.Ordinal)); // logged once, never answered
        Assert.Contains(h.State.PendingConfirmations, q => q.Type == 1);
    }
}
