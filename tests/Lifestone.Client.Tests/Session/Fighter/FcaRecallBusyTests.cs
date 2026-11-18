using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// M4 P5b fix FC-a1 (gate round 1, the Godot mage's death station): after the corpse trip's last pickup the way home sent Lifestone Recall 27 ms after the pickup's confirmation, into ACE's busy chain (@95ef426d
/// <c>Player_Inventory.cs</c> 759-782: the pickup is busy until its return stance and one more motion length), and ACE answered 0x001D. <see cref="RecallHome"/> now waits, bounded and named (<c>recall:busy-wait</c>), until
/// <see cref="IActionApi.BusyReason"/> (the Action API's own lane rule) clears, then casts once. M6 14 recall unblock (live 2026-11-14 17:27:27 to 17:27:42 UTC: the urgent trip's recall waited 15 s on a corpse open's held Use lane and
/// failed <c>recall:busy-wait</c>, and she died five minutes later): an open ground container is closed first and the close checked (<see cref="OpenContainerPreempt"/>), so its hold is never waited out; past the bound the cast goes anyway
/// and ACE judges it; a busy refusal is retried once, after the busy clears, then named. Over <see cref="C1cRecallRig"/> (the mage, the real Action API, a scripted ACE).
/// </summary>
public sealed class FcaRecallBusyTests
{
    private const uint Player = WorldActionRig.Player;
    private const uint Corpse = 0x80000F73;
    private const uint CorpseItem = 0x80000F74;
    private const uint Slave = 0x8000AD05;
    private const uint LostSpell = 1176; // Harm Other VI: ACE never answers it here (the test's hook takes it)
    private static uint _event = 2000;

    /// <summary>ACE's 0052 for every NoLongerViewingContents the client sent since the last look (ACE closes a corpse at once, capture C2).</summary>
    private static void AnswerCloses(C1cRecallRig t, ref int seen)
    {
        var closes = t.R.H.Net.Of(FakeSendKind.NoLongerViewingContents);
        for (; seen < closes.Count; seen++)
        {
            t.R.H.R.Reducer.OnCloseGroundContainer(t.R.H.R.Context, new GameEventHeader(Player, ++_event, GameEventTypes.CloseGroundContainer), new CloseGroundContainerEvent(closes[seen].Guid));
        }
    }

    private static ActionHandle PickUpFromCorpse(C1cRecallRig t)
    {
        var basis = ReducerHarness.Create(Corpse, ReducerHarness.Stamps(), null, name: "Corpse of BSD Mage");
        t.R.H.R.Reducer.OnCreateObject(t.R.H.R.Context, basis, isUpdate: false);
        t.R.H.R.Reducer.OnViewContents(t.R.H.R.Context, new GameEventHeader(Player, 900, GameEventTypes.ViewContents), new ViewContentsEvent(Corpse, [new ViewContentsEntry(CorpseItem, 0)]));
        t.R.Item(CorpseItem, Corpse);
        t.R.Tick();
        var pick = t.R.Api.PickUp(CorpseItem, 0, 0, ActionSource.Host);
        t.R.Tick();
        return pick;
    }

    /// <summary>ACE's 0022 into the player and the own return-stance F74C (capture C4's order).</summary>
    private static void ConfirmPickUp(C1cRecallRig t)
    {
        t.R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, CorpseItem, Player, 0, 0));
        t.R.H.R.Bus.Publish(new ObjectMotionChanged(Player, true, false, new ServerMotionState { MovementType = MovementType.Interpreted }));
    }

    private static void FramesFor(C1cRecallRig t, double seconds, ref int closes)
    {
        var end = t.Now + seconds;
        while (t.Now < end - 1e-9)
        {
            t.Frame(0.05);
            AnswerCloses(t, ref closes);
        }
    }

    [Fact]
    public void The_recall_waits_while_a_corpse_pickups_chain_holds_then_casts_once_and_arrives()
    {
        var t = new C1cRecallRig(null);
        var closes = 0;
        var pick = PickUpFromCorpse(t);
        Assert.True(t.Recall.Begin(t.Now));
        Assert.NotNull(t.R.Api.BusyReason);

        // The pickup is in flight for 2 s: no cast leaves. M6 14: the corpse's open view is closed first (the way out goes before the loot), then the wait.
        FramesFor(t, 2.0, ref closes);
        Assert.Equal(0, t.Casts);
        Assert.Equal(RecallStatus.Running, t.Recall.Status);
        Assert.Equal(1, t.Recall.PreemptCloses);
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("recall:busy-wait: the player is busy (an item move is in flight)", StringComparison.Ordinal));

        // Confirmed with its return stance: the Inventory lane holds the chain (1.35 s from a corpse, plus the 0.1 s margin); still no cast.
        ConfirmPickUp(t);
        t.Frame(0.05);
        Assert.Equal(ActionOutcomeKind.Confirmed, pick.Outcome!.Value.Kind);
        var confirmedAt = t.Now;
        FramesFor(t, 1.2, ref closes);
        Assert.Equal(0, t.Casts);
        Assert.Contains("Inventory lane holds a move's chain", t.R.Api.BusyReason, StringComparison.Ordinal);

        // The chain ends: one cast, after it, and the recall arrives.
        var end = t.Now + 60;
        while (t.Recall.Status == RecallStatus.Running && t.Now < end)
        {
            t.Frame(0.05);
            AnswerCloses(t, ref closes);
        }

        Assert.Equal(RecallStatus.Done, t.Recall.Status);
        Assert.Equal(1, t.Casts);
        Assert.Equal(["cast:1635", "teleport", "present"], t.Events);
        Assert.Contains("LoginComplete", t.R.Api.BusyReason, StringComparison.Ordinal); // after the recall's own teleport, until the client sends it (the rig does not)
        var freed = Assert.Single(t.R.H.Log.Lines, l => l.Contains("recall:busy-wait: the player is free after", StringComparison.Ordinal));
        var waited = double.Parse(freed[(freed.LastIndexOf("after ", StringComparison.Ordinal) + 6)..].Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(waited >= 3.0, freed); // the 2 s pickup and its 1.35 s chain, less the frames the close took
        Assert.True(t.Now - confirmedAt > 1.35);
    }

    [Fact]
    public void A_player_still_busy_after_the_bound_sends_the_cast_anyway_and_ACEs_answer_judges_it_M6_14()
    {
        // Before M6 14 the recall failed recall:busy-wait here and sent nothing; live, that failure left her fighting under the urgent trip until her buffs ran out.
        var t = new C1cRecallRig(null);
        var closes = 0;
        PickUpFromCorpse(t); // ACE never answers it: in flight until its 25 s timeout, past the 15 s bound
        Assert.True(t.Recall.Begin(t.Now));
        var begun = t.Now;
        var castAt = double.NaN;
        var end = t.Now + 60;
        while (t.Recall.Status == RecallStatus.Running && t.Now < end)
        {
            t.Frame(0.25);
            AnswerCloses(t, ref closes);
            if (t.Casts > 0 && double.IsNaN(castAt))
            {
                castAt = t.Now;
            }
        }

        Assert.Equal(RecallStatus.Done, t.Recall.Status);
        Assert.Null(t.Recall.Failure);
        Assert.Equal(1, t.Casts);
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains($"recall:busy-wait: the player is still busy after {RecallHome.BusyWaitSeconds:0} s (an item move is in flight): the cast is sent anyway", StringComparison.Ordinal));
        Assert.True(castAt - begun >= RecallHome.BusyWaitSeconds, $"the cast left {castAt - begun:0.00} s after the recall began");
    }

    [Fact]
    public void A_busy_answer_to_the_cast_is_retried_once_after_the_busy_clears_then_the_next_is_named_recall_cast_M6_14()
    {
        // Before M5 1 fix A the first 0x001D ended the recall (recall:cast, one cast); M5's fix resent it three times a second apart. M6 14 (the owner's brief): once, after the busy clears and at least a second on, then named.
        var t = new C1cRecallRig(null);
        var answered = false;
        t.Caster.CastHook = _ =>
        {
            answered = true;
            return true;
        };
        Assert.Null(t.R.Api.BusyReason);
        Assert.True(t.Recall.Begin(t.Now));
        var end = t.Now + 20;
        while (t.Recall.Status == RecallStatus.Running && t.Now < end)
        {
            t.Frame(0.25);
            if (answered)
            {
                answered = false;
                t.R.UseDone(0x001D); // ACE: You're too busy
                t.R.Tick();
                t.Recall.Step(t.Now);
            }
        }

        Assert.Equal(RecallStatus.Failed, t.Recall.Status);
        Assert.Equal("recall:cast", t.Recall.Failure);
        Assert.Contains("You're too busy (0x001D) again after the busy cleared", t.Recall.Detail, StringComparison.Ordinal);
        Assert.Contains("0x1D", t.Recall.Detail, StringComparison.Ordinal);
        Assert.Equal(1 + RecallHome.BusyRetries, t.R.H.Net.Of(FakeSendKind.CastUntargetedSpell).Count); // the cast and its one retry
        Assert.Single(t.R.H.Log.Lines, l => l.Contains("recall: ACE answered You're too busy (0x001D; VerifyBusy, Player_Magic.cs 375-383 @95ef426d): Lifestone Recall is retried once", StringComparison.Ordinal));
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("recall:busy-wait: the retry after ACE's busy refusal waits", StringComparison.Ordinal));
    }

    [Fact]
    public void An_urgent_recall_closes_the_open_corpse_whose_UseDone_an_older_debt_took_and_casts_at_once_M6_14_live_shape()
    {
        // The live shape (17:27:09 to 17:27:42): an attack spell ACE never answered timed out and owed its UseDone; the corpse open's UseDone came first and paid that debt; its 0196 and contents confirmed it in the same tick,
        // so the open held the Use lane for a UseDone that had come. The urgent trip's recall then waited 15 s on it and failed. Now it closes the corpse, checks the close, and casts at once.
        var t = new C1cRecallRig(null);
        var closes = 0;
        t.Caster.TargetedCastHook = (spell, _) => spell == LostSpell; // ACE never answers this one
        t.R.Server((p, seq) => AceM3Writers.MagicUpdateSpell(p, seq, (ushort)LostSpell)); // M6 15: she knows it, so the player rules put it on a spell bar before its cast, as live
        var basis = ReducerHarness.Create(Corpse, ReducerHarness.Stamps(), null, name: "Corpse of Tusker Slave");
        t.R.H.R.Reducer.OnCreateObject(t.R.H.R.Context, basis, isUpdate: false);

        t.R.Server((p, seq) => AceM3Writers.MagicUpdateSpell(p, seq, (ushort)LostSpell)); // M6 15 (the player's rules, merged with the villa): the spell is in her book, so the rule puts it on a bar before its cast
        t.R.Tick();
        var lost = t.R.Api.Cast(LostSpell, Slave, ActionSource.Automation);
        t.Frame(0.05);
        FramesFor(t, ActionPolicy.CastSeconds + 0.1, ref closes);
        Assert.Equal(ActionOutcomeKind.TimedOut, lost.Outcome!.Value.Kind);

        var open = t.R.Api.OpenContainer(Corpse, ActionSource.Automation);
        t.R.Tick();
        Assert.Equal(Corpse, Assert.Single(t.R.H.Net.Of(FakeSendKind.Use)).Guid);
        t.R.UseDone(); // logged first, in the same tick as the confirmation: taken for the older debt
        t.R.H.R.Reducer.OnViewContents(t.R.H.R.Context, new GameEventHeader(Player, ++_event, GameEventTypes.ViewContents), new ViewContentsEvent(Corpse, [new ViewContentsEntry(CorpseItem, 0)]));
        t.R.Item(CorpseItem, Corpse);
        t.R.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, open.Outcome!.Value.Kind);
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("a late UseDone 0x01C7 (code 0) belongs to an action that already ended; dropped", StringComparison.Ordinal));
        Assert.Contains("the Use lane holds until ACE's UseDone", t.R.Api.BusyReason, StringComparison.Ordinal);

        // The urgent trip: the recall closes the corpse first, checks the server's answer, and casts at once.
        Assert.True(t.Recall.Begin(t.Now));
        var begun = t.Now;
        while (t.Casts == 0 && t.Now < begun + 5)
        {
            t.Frame(0.05);
            AnswerCloses(t, ref closes);
        }

        Assert.Equal(1, t.Casts);
        Assert.True(t.Now - begun < 0.5, $"the cast left {t.Now - begun:0.00} s after the recall began"); // the close, not the hold's 2 s bound, freed the lane
        Assert.Equal(1, t.Recall.PreemptCloses);
        Assert.Equal(Corpse, Assert.Single(t.R.H.Net.Of(FakeSendKind.NoLongerViewingContents)).Guid);
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("recall: preempt: check before: the ground container 0x80000F73 is open (busy: the Use lane holds until ACE's UseDone (a container or a vendor))", StringComparison.Ordinal));
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("recall: preempt: check after: the close", StringComparison.Ordinal) && l.Contains("the view is closed, the Use lane its open held is free: Lifestone Recall goes now", StringComparison.Ordinal));
        Assert.DoesNotContain(t.R.H.Log.Lines, l => l.Contains("recall:busy-wait: the player is busy", StringComparison.Ordinal));

        var stop = t.Now + 60;
        while (t.Recall.Status == RecallStatus.Running && t.Now < stop)
        {
            t.Frame(0.05);
            AnswerCloses(t, ref closes);
        }

        Assert.Equal(RecallStatus.Done, t.Recall.Status);
        Assert.Equal(1, t.Casts);
    }
}
