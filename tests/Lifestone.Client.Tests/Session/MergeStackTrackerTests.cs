using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// <c>MergeStack</c> (GameAction 0x0054; M3 1.4 fix A, finding 1.4-R2): the codec's fields in the send, confirmed on the target's 0x0197 to the summed size with the source gone (a whole merge: ACE's 0x0024 first,
/// <c>Player_Inventory.cs</c> 3096, then 0x0197 3121-3123) or shrunk (a partial merge: the source's 0x0197 first, 3151-3153, then the target's 3159-3161), a ServerError on ACE's 0x00A0 (the source for most refusals,
/// the target for a stuck or traded one), refused at once for what the model says cannot merge, a 5 s timeout, and one in flight at a time (the Inventory lane).
/// </summary>
public sealed class MergeStackTrackerTests
{
    private const uint Player = WorldActionRig.Player;
    private const uint Hyssop = 774;
    private const uint Scarab = 691;
    private const uint SmallStack = 0x80000201;
    private const uint BigStack = 0x80000202;
    private const uint Lead = 0x80000203;
    private const uint Staff = WorldActionRig.Staff;

    private static byte _seq;

    /// <summary>A pack of two stacks of Hyssop (10 and 15, at most 100), a stack of lead, and a staff that does not stack.</summary>
    private static WorldActionRig Rig()
    {
        var r = new WorldActionRig();
        r.Describe([new ContentDesc(SmallStack, 0), new ContentDesc(BigStack, 0), new ContentDesc(Lead, 0), new ContentDesc(Staff, 0)]);
        Stack(r, SmallStack, Hyssop, 10);
        Stack(r, BigStack, Hyssop, 15);
        Stack(r, Lead, Scarab, 7);
        r.Item(Staff, Player);
        return r;
    }

    private static void Stack(WorldActionRig r, uint guid, uint wcid, ushort size)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: "Hyssop");
        r.H.R.Reducer.OnCreateObject(
            r.H.R.Context,
            basis with
            {
                Weenie = basis.Weenie with
                {
                    Header = WeenieHeaderFlags.Container | WeenieHeaderFlags.StackSize | WeenieHeaderFlags.MaxStackSize,
                    ContainerId = Player,
                    WeenieClassId = wcid,
                    StackSize = size,
                    MaxStackSize = 100,
                },
            },
            isUpdate: false);
    }

    // ACE's whole merge inside the pack: the source's removal, then the target's new size.
    private static void AceWholeMerge(WorldActionRig r, uint source, uint target, uint targetSize)
    {
        r.Raw(AceM3Writers.InventoryRemoveObject(source));
        r.Raw(AceM3Writers.SetStackSize(++_seq, target, targetSize, 0));
    }

    [Fact]
    public void A_whole_merge_sends_0x0054_and_is_confirmed_by_the_targets_new_size_after_the_sources_removal()
    {
        var r = Rig();
        var merge = r.Api.MergeStack(SmallStack, BigStack, 10, ActionSource.Ui);
        r.Tick();

        var sent = Assert.Single(r.H.Net.Of(FakeSendKind.StackableMerge));
        Assert.Equal((SmallStack, 10, BigStack.ToString("X8")), (sent.Guid, sent.Index, sent.Target));

        r.Raw(AceM3Writers.InventoryRemoveObject(SmallStack)); // ACE's first answer: not enough
        r.Tick();
        Assert.False(merge.IsCompleted);

        r.Raw(AceM3Writers.SetStackSize(++_seq, BigStack, 25, 0));
        r.Tick();
        var outcome = merge.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.Confirmed, outcome.Kind);
        Assert.Contains("now 25", outcome.Detail, StringComparison.Ordinal);
        Assert.False(r.H.State.Self.Inventory.Contains(SmallStack));
        Assert.True(r.H.State.Self.Inventory.TryGetStack(BigStack, out var size, out _));
        Assert.Equal(25u, size);
    }

    [Fact]
    public void A_partial_merge_waits_for_both_the_sources_shrunk_size_and_the_targets_summed_size()
    {
        var r = Rig();
        var merge = r.Api.MergeStack(SmallStack, BigStack, 4, ActionSource.Ui);
        r.Tick();

        r.Raw(AceM3Writers.SetStackSize(++_seq, SmallStack, 6, 0)); // ACE's order: the source first
        r.Tick();
        Assert.False(merge.IsCompleted);

        r.Raw(AceM3Writers.SetStackSize(++_seq, BigStack, 19, 0));
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, merge.Outcome!.Value.Kind);
        Assert.True(r.H.State.Self.Inventory.Contains(SmallStack));
    }

    [Fact]
    public void A_target_size_that_is_not_the_sum_does_not_confirm()
    {
        var r = Rig();
        var merge = r.Api.MergeStack(SmallStack, BigStack, 10, ActionSource.Ui);
        r.Tick();

        AceWholeMerge(r, SmallStack, BigStack, 24); // someone else's change, not 15 + 10
        r.Tick();
        Assert.False(merge.IsCompleted);
    }

    [Fact]
    public void ACE_s_failure_for_the_source_or_the_target_is_a_ServerError_with_its_code()
    {
        var r = Rig();
        var different = r.Api.MergeStack(SmallStack, BigStack, 3, ActionSource.Ui);
        var stuck = r.Api.MergeStack(BigStack, SmallStack, 3, ActionSource.Ui);
        r.Tick();

        r.Server((p, s) => AceM3Writers.InventoryServerSaysFailed(p, s, SmallStack, 0x0427)); // YouCannotMergeDifferentStacks
        r.Tick();
        Assert.Equal((ActionOutcomeKind.ServerError, 0x0427u), (different.Outcome!.Value.Kind, different.Outcome.Value.Code));
        Assert.Equal(2, r.H.Net.Of(FakeSendKind.StackableMerge).Count()); // the next one went

        r.Server((p, s) => AceM3Writers.InventoryServerSaysFailed(p, s, SmallStack, 0)); // the target of the second one (a stuck or traded target)
        r.Tick();
        Assert.Equal((ActionOutcomeKind.ServerError, 0u), (stuck.Outcome!.Value.Kind, stuck.Outcome.Value.Code));
        Assert.Contains("ACE gives none", stuck.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_merge_nobody_answers_times_out_after_five_seconds_and_the_next_waits_its_turn()
    {
        var r = Rig();
        var first = r.Api.MergeStack(SmallStack, BigStack, 10, ActionSource.Ui);
        var second = r.Api.MergeStack(BigStack, SmallStack, 1, ActionSource.Ui);
        r.Tick();
        Assert.Single(r.H.Net.Of(FakeSendKind.StackableMerge));

        r.Tick(4.9);
        Assert.False(first.IsCompleted);
        r.Tick(0.2);
        Assert.Equal(ActionOutcomeKind.TimedOut, first.Outcome!.Value.Kind);
        r.Tick();
        Assert.Equal(2, r.H.Net.Of(FakeSendKind.StackableMerge).Count());
        Assert.False(second.IsCompleted);
    }

    [Theory]
    [InlineData(0, "must be at least 1")]
    [InlineData(-3, "must be at least 1")]
    [InlineData(11, "holds 10, not 11")]
    public void An_amount_ACE_refuses_is_refused_at_once(int amount, string why)
    {
        var r = Rig();
        var merge = r.Api.MergeStack(SmallStack, BigStack, amount, ActionSource.Ui);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Refused, merge.Outcome!.Value.Kind);
        Assert.Contains(why, merge.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Empty(r.H.Net.Of(FakeSendKind.StackableMerge));
    }

    [Fact]
    public void Stacks_the_model_says_cannot_merge_are_refused_at_once_and_nothing_is_sent()
    {
        var r = Rig();
        var handles = new (ActionHandle Handle, string Why)[]
        {
            (r.Api.MergeStack(0, BigStack, 1, ActionSource.Ui), "no stack"),
            (r.Api.MergeStack(SmallStack, SmallStack, 1, ActionSource.Ui), "into itself"),
            (r.Api.MergeStack(0x80000999, BigStack, 1, ActionSource.Ui), "not held"),
            (r.Api.MergeStack(SmallStack, 0x80000999, 1, ActionSource.Ui), "target stack"),
            (r.Api.MergeStack(SmallStack, Lead, 1, ActionSource.Ui), "different items"),
            (r.Api.MergeStack(Staff, BigStack, 1, ActionSource.Ui), "different items"),
        };
        r.Tick();

        Assert.All(handles, h =>
        {
            Assert.Equal(ActionOutcomeKind.Refused, h.Handle.Outcome!.Value.Kind);
            Assert.Contains(h.Why, h.Handle.Outcome.Value.Detail, StringComparison.Ordinal);
        });
        Assert.Empty(r.H.Net.Of(FakeSendKind.StackableMerge));
    }

    [Fact]
    public void A_merge_past_the_targets_maximum_stack_is_refused_at_once()
    {
        var r = Rig();
        Stack(r, BigStack, Hyssop, 95);
        var merge = r.Api.MergeStack(SmallStack, BigStack, 10, ActionSource.Ui);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Refused, merge.Outcome!.Value.Kind);
        Assert.Contains("does not fit", merge.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_is_sent_before_the_world()
    {
        var early = new WorldActionRig(present: false);
        var merge = early.Api.MergeStack(SmallStack, BigStack, 1, ActionSource.Ui);
        early.Tick();

        Assert.Equal(ActionOutcomeKind.Refused, merge.Outcome!.Value.Kind);
        Assert.Empty(early.H.Net.Sends);
    }
}
