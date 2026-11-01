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
/// <see cref="IActionApi.BusyReason"/> (the Action API's own lane rule) clears, then casts once; a 0x001D after that is still <c>recall:cast</c>. Over <see cref="C1cRecallRig"/> (the mage, the real Action API, a scripted ACE).
/// </summary>
public sealed class FcaRecallBusyTests
{
    private const uint Player = WorldActionRig.Player;
    private const uint Corpse = 0x80000F73;
    private const uint CorpseItem = 0x80000F74;

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

    private static void FramesFor(C1cRecallRig t, double seconds)
    {
        var end = t.Now + seconds;
        while (t.Now < end - 1e-9)
        {
            t.Frame(0.05);
        }
    }

    [Fact]
    public void The_recall_waits_while_a_corpse_pickups_chain_holds_then_casts_once_and_arrives()
    {
        var t = new C1cRecallRig(null);
        var pick = PickUpFromCorpse(t);
        Assert.True(t.Recall.Begin(t.Now));
        Assert.NotNull(t.R.Api.BusyReason);

        // The pickup is in flight for 2 s: no cast leaves.
        FramesFor(t, 2.0);
        Assert.Equal(0, t.Casts);
        Assert.Equal(RecallStatus.Running, t.Recall.Status);
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("recall:busy-wait: the player is busy (an item move is in flight)", StringComparison.Ordinal));

        // Confirmed with its return stance: the Inventory lane holds the chain (1.35 s from a corpse, plus the 0.1 s margin); still no cast.
        ConfirmPickUp(t);
        t.Frame(0.05);
        Assert.Equal(ActionOutcomeKind.Confirmed, pick.Outcome!.Value.Kind);
        var confirmedAt = t.Now;
        FramesFor(t, 1.2);
        Assert.Equal(0, t.Casts);
        Assert.Contains("Inventory lane holds a move's chain", t.R.Api.BusyReason, StringComparison.Ordinal);

        // The chain ends: one cast, after it, and the recall arrives.
        var end = t.Now + 60;
        while (t.Recall.Status == RecallStatus.Running && t.Now < end)
        {
            t.Frame(0.05);
        }

        Assert.Equal(RecallStatus.Done, t.Recall.Status);
        Assert.Equal(1, t.Casts);
        Assert.Equal(["cast:1635", "teleport", "present"], t.Events);
        Assert.Contains("LoginComplete", t.R.Api.BusyReason, StringComparison.Ordinal); // after the recall's own teleport, until the client sends it (the rig does not)
        var freed = Assert.Single(t.R.H.Log.Lines, l => l.Contains("recall:busy-wait: the player is free after", StringComparison.Ordinal));
        var waited = double.Parse(freed[(freed.LastIndexOf("after ", StringComparison.Ordinal) + 6)..].Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(waited >= 2.0 + 1.35, freed);
        Assert.True(t.Now - confirmedAt > 1.35);
    }

    [Fact]
    public void A_player_still_busy_after_the_bound_fails_recall_busy_wait_and_no_cast_is_sent()
    {
        var t = new C1cRecallRig(null);
        PickUpFromCorpse(t); // ACE never answers it: in flight until its 25 s timeout, past the 15 s bound
        Assert.True(t.Recall.Begin(t.Now));
        var end = t.Now + 30;
        while (t.Recall.Status == RecallStatus.Running && t.Now < end)
        {
            t.Frame(0.25);
        }

        Assert.Equal(RecallStatus.Failed, t.Recall.Status);
        Assert.Equal("recall:busy-wait", t.Recall.Failure);
        Assert.Contains($"still busy after {RecallHome.BusyWaitSeconds:0} s (an item move is in flight)", t.Recall.Detail, StringComparison.Ordinal);
        Assert.Equal(0, t.Casts);
        Assert.Empty(t.R.H.Net.Of(FakeSendKind.CastUntargetedSpell));
        Assert.EndsWith("recall_why=recall:busy-wait", t.Recall.Fields, StringComparison.Ordinal);
    }

    [Fact]
    public void A_busy_answer_to_the_cast_after_the_wait_is_waited_out_and_resent_three_times_then_is_recall_cast_M5_1_fix_A_1_R6()
    {
        // Before M5 1 fix A the first 0x001D ended the recall (recall:cast, one cast). The cast policy's busy rule (one second, three times) now resends it; the fourth busy answer is the end.
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
        Assert.Contains("0x1D", t.Recall.Detail, StringComparison.Ordinal);
        Assert.Equal(CastRules.DefaultBusyRetries + 1, t.R.H.Net.Of(FakeSendKind.CastUntargetedSpell).Count); // the cast and its three resends
        Assert.DoesNotContain(t.R.H.Log.Lines, l => l.Contains("recall:busy-wait", StringComparison.Ordinal));
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("busy (0x001D) 4 times", StringComparison.Ordinal));
    }
}
