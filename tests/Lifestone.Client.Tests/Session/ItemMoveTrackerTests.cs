using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 2.1 s2: <c>PickUp</c>, <c>Drop</c>, the three splits and <c>Give</c> over the orders ACE really sends (cache/m4/2-1/capture.md C2 and C4), the refusals, the timeouts, and the busy rule: the Inventory lane holds the
/// next move until the previous move's chain ends (the own return-stance F74C and one more motion length: 0.74 s from the ground, 1.35 s from a corpse) and until 3 s after an own teleport.
/// </summary>
public sealed class ItemMoveTrackerTests
{
    private const uint Player = WorldActionRig.Player;
    private const uint Staff = WorldActionRig.Staff;
    private const uint Sword = WorldActionRig.Sword;
    private const uint Ground = 0x80000501;
    private const uint Corpse = 0x80000201;
    private const uint CorpseItem = 0x80000211;
    private const uint Arrows = 0x80000601;
    private const uint NewArrows = 0x80000602;
    private const uint Alfrin = 0x80000301;
    private const uint Ammo = 0x00800000;

    private sealed class Rig
    {
        private uint _event = 100;
        private byte _seq;

        public Rig()
        {
            R = new WorldActionRig();
            R.Describe([new ContentDesc(Staff, 0), new ContentDesc(Sword, 0), new ContentDesc(Arrows, 0)], []);
            R.Item(Staff, Player);
            R.Item(Sword, Player);
            R.Item(Arrows, Player);
            Stack(Arrows, 50);
        }

        public WorldActionRig R { get; }

        public ActionApi Api => R.Api;

        public int Sent(FakeSendKind kind) => R.H.Net.Of(kind).Count;

        public void Stack(uint guid, uint size) => R.Raw(AceM3Writers.SetStackSize(++_seq, guid, size, size));

        public void Put(uint item, uint container, uint placement = 0) => R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, item, container, placement, 0));

        public void Failed(uint item, uint error = 0) => R.Server((p, s) => AceM3Writers.InventoryServerSaysFailed(p, s, item, error));

        public void Error(uint code) => R.Raw(AceWireWriters.WeenieError(Player, ++_event, code));

        public void ErrorText(uint code, string text) => R.Raw(AceWireWriters.WeenieErrorWithString(Player, ++_event, code, text));

        public void Transient(string text) => R.Raw(AceWireWriters.TransientString(Player, ++_event, text));

        public void Dropped(uint item) => R.Server((p, s) => AceM3Writers.PutObjectIn3D(p, s, item));

        public void Wield(uint item, uint mask) => R.Server((p, s) => AceM3Writers.WieldObject(p, s, item, (int)mask));

        public void Removed(uint item) => R.Raw(AceM3Writers.InventoryRemoveObject(item));

        public void ReturnStance() => R.H.R.Bus.Publish(new ObjectMotionChanged(Player, true, false, new ServerMotionState { MovementType = MovementType.Interpreted }));

        public void Teleported() => R.H.R.Bus.Publish(new SelfTravelChanged(SelfTravel.TeleportAnnounced, SelfTravel.TeleportArrived));

        public void GroundItem(uint guid) => R.Create(guid, 1f, missile: false);

        public void OpenCorpse()
        {
            var basis = ReducerHarness.Create(Corpse, ReducerHarness.Stamps(), null, name: "Corpse of Tusker");
            R.H.R.Reducer.OnCreateObject(R.H.R.Context, basis, isUpdate: false);
            R.H.R.Reducer.OnViewContents(
                R.H.R.Context,
                new GameEventHeader(Player, ++_event, GameEventTypes.ViewContents),
                new ViewContentsEvent(Corpse, [new ViewContentsEntry(CorpseItem, 0)]));
            R.Item(CorpseItem, Corpse);
            R.Tick();
        }
    }

    // ---- pick up -------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_pick_up_from_the_ground_sends_0x0019_and_is_Confirmed_on_the_0022_naming_the_player()
    {
        var r = new Rig();
        r.GroundItem(Ground);
        var pick = r.Api.PickUp(Ground, 0, 0, ActionSource.Ui);
        r.R.Tick();

        var sent = Assert.Single(r.R.H.Net.Of(FakeSendKind.PutItemInContainer));
        Assert.Equal(Ground, sent.Guid);
        Assert.Equal(Player.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), sent.Target);

        r.R.Tick(0.74);
        Assert.False(pick.IsCompleted);
        r.Put(Ground, Player);
        r.R.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, pick.Outcome!.Value.Kind);
        Assert.True(r.R.H.State.Self.Inventory.Find(Ground).Held);
        Assert.Contains(r.R.H.ActionLog.All(), e => e.Kind == ActionKind.PickUp && e.Outcome.Kind == ActionOutcomeKind.Confirmed);
    }

    [Fact]
    public void A_0022_into_another_container_does_not_confirm_a_pick_up_into_the_player()
    {
        var r = new Rig();
        r.GroundItem(Ground);
        var pick = r.Api.PickUp(Ground, 0, 0, ActionSource.Ui);
        r.R.Tick();
        r.Put(Ground, 0x80000999); // a container the model does not know: the item leaves the player's holdings, it did not arrive
        r.R.Tick();
        Assert.False(pick.IsCompleted);
    }

    [Theory]
    [InlineData(0x001Du, "too busy")]
    [InlineData(0x0029u, null)]
    public void A_refused_pick_up_is_a_ServerError_with_the_028A_code_and_its_text(uint code, string? text)
    {
        var r = new Rig();
        r.GroundItem(Ground);
        var pick = r.Api.PickUp(Ground, 0, 0, ActionSource.Ui);
        r.R.Tick();
        r.Error(code);
        r.Failed(Ground);
        r.R.Tick();

        var outcome = pick.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.ServerError, outcome.Kind);
        Assert.Equal(code, outcome.Code);
        Assert.Contains(ActionText.Hex(code), outcome.Detail, StringComparison.Ordinal);
        if (text is not null)
        {
            Assert.Contains(text, outcome.Detail, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("Source item not found!")]
    public void A_refused_pick_up_carries_the_02EB_text_and_code_0(string text)
    {
        var r = new Rig();
        r.GroundItem(Ground);
        var pick = r.Api.PickUp(Ground, 0, 0, ActionSource.Ui);
        r.R.Tick();
        r.Transient(text);
        r.Failed(Ground);
        r.R.Tick();

        Assert.Equal(ActionOutcomeKind.ServerError, pick.Outcome!.Value.Kind);
        Assert.Equal(0u, pick.Outcome.Value.Code);
        Assert.Contains(text, pick.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_busy_refusal_is_a_ServerError_and_is_never_retried_silently()
    {
        var r = new Rig();
        r.GroundItem(Ground);
        var pick = r.Api.PickUp(Ground, 0, 0, ActionSource.Ui);
        r.R.Tick();
        r.Error(0x001D);
        r.Failed(Ground);
        r.R.Tick(5);

        Assert.Equal(0x001Du, pick.Outcome!.Value.Code);
        Assert.Equal(1, r.Sent(FakeSendKind.PutItemInContainer));
    }

    [Fact]
    public void A_pick_up_that_hears_nothing_times_out_after_25_seconds_and_bad_arguments_are_refused_at_once()
    {
        var r = new Rig();
        r.GroundItem(Ground);
        var pick = r.Api.PickUp(Ground, 0, 0, ActionSource.Ui);
        r.R.Tick();
        r.R.Tick(24.9);
        Assert.False(pick.IsCompleted);
        r.R.Tick(0.2);
        Assert.Equal(ActionOutcomeKind.TimedOut, pick.Outcome!.Value.Kind);

        Assert.Equal(ActionOutcomeKind.Refused, r.Api.PickUp(0, 0, 0, ActionSource.Ui).Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, r.Api.PickUp(Ground, 0, -1, ActionSource.Ui).Outcome!.Value.Kind);
    }

    [Fact]
    public void MoveToPack_of_an_item_the_player_does_not_hold_is_a_pick_up_under_its_own_kind()
    {
        var r = new Rig();
        r.GroundItem(Ground);
        var move = r.Api.MoveToPack(Ground, 0, 0, ActionSource.Ui);
        r.R.Tick();

        Assert.Equal(Ground, Assert.Single(r.R.H.Net.Of(FakeSendKind.PutItemInContainer)).Guid);
        r.Put(Ground, Player);
        r.R.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, move.Outcome!.Value.Kind);
        Assert.Equal(ActionKind.MoveToPack, move.Kind);
        Assert.Contains(r.R.H.ActionLog.All(), e => e.Kind == ActionKind.MoveToPack && e.Outcome.Kind == ActionOutcomeKind.Confirmed);
    }

    // ---- the busy rule -------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void After_a_ground_pick_up_the_next_move_waits_for_the_return_stance_and_0_74_seconds()
    {
        var r = new Rig();
        r.GroundItem(Ground);
        var pick = r.Api.PickUp(Ground, 0, 0, ActionSource.Ui);
        var drop = r.Api.Drop(Staff, ActionSource.Ui);
        r.R.Tick();
        r.R.Tick(0.74);
        r.Put(Ground, Player);
        r.ReturnStance(); // capture C4: the return-stance F74C right after the 0022
        r.R.Tick();
        Assert.True(pick.IsCompleted);
        Assert.Equal(0, r.Sent(FakeSendKind.DropItem));

        r.R.Tick(0.6);
        Assert.Equal(0, r.Sent(FakeSendKind.DropItem));
        Assert.False(drop.IsCompleted);

        r.R.Tick(0.2); // 0.8 s after the return stance: the measured 0.74 s has passed, the 0.1 s margin (2.1-R8) has not
        Assert.Equal(0, r.Sent(FakeSendKind.DropItem));
        r.R.Tick(0.1);
        Assert.Equal(Staff, Assert.Single(r.R.H.Net.Of(FakeSendKind.DropItem)).Guid);
    }

    [Fact]
    public void After_a_pick_up_from_a_corpse_the_wait_is_1_35_seconds()
    {
        var r = new Rig();
        r.OpenCorpse();
        var pick = r.Api.PickUp(CorpseItem, 0, 0, ActionSource.Ui);
        var drop = r.Api.Drop(Staff, ActionSource.Ui);
        r.R.Tick();
        r.R.Tick(1.35);
        r.Put(CorpseItem, Player);
        r.ReturnStance();
        r.R.Tick();
        Assert.True(pick.IsCompleted);

        r.R.Tick(1.2);
        Assert.Equal(0, r.Sent(FakeSendKind.DropItem));
        r.R.Tick(0.2); // 1.4 s: past 1.35 s, inside the 0.1 s margin (2.1-R8)
        Assert.Equal(0, r.Sent(FakeSendKind.DropItem));
        r.R.Tick(0.1);
        Assert.Equal(1, r.Sent(FakeSendKind.DropItem));
        Assert.False(drop.IsCompleted); // sent, waiting for its 019A
    }

    [Fact]
    public void Without_a_return_stance_the_lane_is_freed_a_quarter_second_plus_the_motion_after_the_confirmation()
    {
        var r = new Rig();
        r.GroundItem(Ground);
        r.Api.PickUp(Ground, 0, 0, ActionSource.Ui);
        r.Api.Drop(Staff, ActionSource.Ui);
        r.R.Tick();
        r.Put(Ground, Player);
        r.R.Tick();
        r.R.Tick(0.9);
        Assert.Equal(0, r.Sent(FakeSendKind.DropItem));
        r.R.Tick(0.2);
        Assert.Equal(1, r.Sent(FakeSendKind.DropItem));
    }

    [Fact]
    public void Loot_moves_go_one_at_a_time_in_call_order()
    {
        var r = new Rig();
        r.GroundItem(Ground);
        r.GroundItem(0x80000502);
        r.Api.PickUp(Ground, 0, 0, ActionSource.Ui);
        var second = r.Api.PickUp(0x80000502, 0, 0, ActionSource.Ui);
        r.R.Tick();
        Assert.Equal(1, r.Sent(FakeSendKind.PutItemInContainer));

        r.R.Tick(0.74);
        r.Put(Ground, Player);
        r.ReturnStance();
        r.R.Tick(0.3);
        Assert.Equal(1, r.Sent(FakeSendKind.PutItemInContainer));
        r.R.Tick(0.6);
        Assert.Equal(2, r.Sent(FakeSendKind.PutItemInContainer));
        Assert.False(second.IsCompleted);
    }

    [Fact]
    public void A_move_after_an_own_teleport_waits_for_the_LoginComplete_and_then_a_margin_2_1_R2()
    {
        var r = new Rig();
        r.Teleported();
        var drop = r.Api.Drop(Staff, ActionSource.Ui);
        r.R.Tick(6); // the tunnel's 5.6 s in the live chest recording: ACE is still teleporting and the client has not sent the LoginComplete
        Assert.Equal(0, r.Sent(FakeSendKind.DropItem));
        r.Api.MarkLoginCompleteSent();
        r.R.Tick(0.4);
        Assert.Equal(0, r.Sent(FakeSendKind.DropItem));
        r.R.Tick(0.2); // the 0.5 s margin after the LoginComplete's send has passed
        Assert.Equal(1, r.Sent(FakeSendKind.DropItem));
        Assert.False(drop.IsCompleted);
    }

    [Fact]
    public void A_session_that_never_sends_the_LoginComplete_waits_the_cap_then_goes_with_a_warning_2_1_R2()
    {
        var r = new Rig();
        r.Teleported();
        r.Api.Drop(Staff, ActionSource.Ui);
        r.R.Tick(9.8);
        Assert.Equal(0, r.Sent(FakeSendKind.DropItem));
        r.R.Tick(0.4);
        Assert.Equal(1, r.Sent(FakeSendKind.DropItem));
        Assert.Contains(r.R.H.Log.Entries, e => e.Message.Contains("no LoginComplete was sent", StringComparison.Ordinal));
    }

    [Fact]
    public void A_pick_up_that_times_out_is_a_plain_timeout_with_no_merge_log_2_2_cap()
    {
        // M4 2.2 cap (live capture of 2026-10-21): ACE never merges at pick-up, so the tracker has no merge case; a pick-up without its 0022 only times out.
        var r = new Rig();
        r.GroundItem(Ground);
        var pick = r.Api.PickUp(Ground, 0, 0, ActionSource.Ui);
        r.R.Tick();
        r.R.Tick(26);
        Assert.Equal(ActionOutcomeKind.TimedOut, pick.Outcome!.Value.Kind);
        Assert.DoesNotContain(r.R.H.Log.Entries, e => e.Message.Contains("m4-pickup-merge", StringComparison.Ordinal));
    }

    [Fact]
    public void A_stack_picked_up_beside_a_held_stack_of_its_class_is_confirmed_by_its_own_0022_2_2_cap()
    {
        // The capture's arrows and coins: a stack arrives as its own stack with a normal 0022 (the 0197 of the held stack, if any, does not matter).
        var r = new Rig();
        r.GroundItem(Ground);
        var pick = r.Api.PickUp(Ground, 0, 0, ActionSource.Ui);
        r.R.Tick();
        r.Put(Ground, Player);
        r.R.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, pick.Outcome!.Value.Kind);
        Assert.DoesNotContain(r.R.H.Log.Entries, e => e.Message.Contains("m4-pickup-merge", StringComparison.Ordinal));
    }

    [Fact]
    public void A_pick_up_confirmed_just_before_its_timeout_owes_no_UseDone_so_the_next_Use_takes_its_own_2_1_R9()
    {
        var r = new Rig();
        r.GroundItem(Ground);
        var pick = r.Api.PickUp(Ground, 0, 0, ActionSource.Ui);
        r.R.Tick();
        r.R.Tick(24.5);
        r.Put(Ground, Player);
        r.ReturnStance();
        r.R.Tick();
        Assert.True(pick.IsCompleted);
        for (var i = 0; i < 24; i++)
        {
            r.R.Tick(0.05); // through the 25 s timeout while the chain still holds the lane: ExpireTimeouts lets the hold go (it must not book an owed UseDone)
        }

        var use = r.Api.Use(Staff, ActionSource.Ui);
        r.R.Tick();
        r.R.UseDone();
        r.R.Tick();
        Assert.True(use.IsCompleted, "the UseDone belongs to the Use, not to a dropped 'owed' one");
    }

    // ---- drop and the splits -------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_drop_is_Confirmed_on_the_019A_and_refused_at_once_for_an_item_the_player_does_not_hold()
    {
        var r = new Rig();
        var drop = r.Api.Drop(Staff, ActionSource.Ui);
        r.R.Tick();
        Assert.Equal(Staff, Assert.Single(r.R.H.Net.Of(FakeSendKind.DropItem)).Guid);
        r.R.Tick(0.73);
        Assert.False(drop.IsCompleted);
        r.Dropped(Staff);
        r.R.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, drop.Outcome!.Value.Kind);

        var stranger = r.Api.Drop(0x80000999, ActionSource.Ui);
        r.R.Tick(2);
        Assert.Equal(ActionOutcomeKind.Refused, stranger.Outcome!.Value.Kind);
        Assert.Equal(1, r.Sent(FakeSendKind.DropItem));
    }

    [Fact]
    public void A_split_to_the_pack_is_Confirmed_on_the_0022_of_a_new_guid_before_its_F745_and_the_old_stacks_0197()
    {
        var r = new Rig();
        var split = r.Api.SplitToContainer(Arrows, 0, 0, 10, ActionSource.Ui);
        r.R.Tick();

        var sent = Assert.Single(r.R.H.Net.Of(FakeSendKind.SplitToContainer));
        Assert.Equal((Arrows, 10u), (sent.Guid, sent.Amount));
        r.Put(NewArrows, Player); // capture C4: the new guid's 0022 first
        r.R.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, split.Outcome!.Value.Kind);
        Assert.Contains(ActionText.Hex(NewArrows), split.Outcome.Value.Detail, StringComparison.Ordinal);
        r.Stack(Arrows, 40);
        r.R.Item(NewArrows, Player);
    }

    [Fact]
    public void A_split_to_the_ground_is_Confirmed_on_the_old_stacks_0197_once_the_new_stacks_F745_has_followed()
    {
        var r = new Rig();
        var split = r.Api.SplitTo3D(Arrows, 5, ActionSource.Ui);
        r.R.Tick();
        Assert.Equal((Arrows, 5u), (r.R.H.Net.Of(FakeSendKind.SplitTo3D)[0].Guid, r.R.H.Net.Of(FakeSendKind.SplitTo3D)[0].Amount));

        r.R.Tick(0.74);
        r.Stack(Arrows, 45);
        r.R.Tick();
        Assert.False(split.IsCompleted);
        r.GroundItem(NewArrows);
        r.R.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, split.Outcome!.Value.Kind);
    }

    [Fact]
    public void A_split_to_the_ground_whose_F745_never_comes_is_Confirmed_after_3_seconds_with_a_warning()
    {
        var r = new Rig();
        var split = r.Api.SplitTo3D(Arrows, 5, ActionSource.Ui);
        r.R.Tick();
        r.Stack(Arrows, 45);
        r.R.Tick(2.9);
        Assert.False(split.IsCompleted);
        r.R.Tick(0.2);
        Assert.Equal(ActionOutcomeKind.Confirmed, split.Outcome!.Value.Kind);
        Assert.Contains(r.R.H.Log.Entries, e => e.Message.Contains("F745 did not arrive", StringComparison.Ordinal));
    }

    [Fact]
    public void A_split_to_the_wield_slot_is_Confirmed_on_the_0023_of_a_new_guid_and_refused_on_the_02EB_and_00A0_of_the_new_guid()
    {
        var r = new Rig();
        var split = r.Api.SplitToWield(Arrows, EquipMask.MissileAmmo, 5, ActionSource.Ui);
        r.R.Tick();
        var sent = Assert.Single(r.R.H.Net.Of(FakeSendKind.SplitToWield));
        Assert.Equal((Arrows, Ammo, 5u), (sent.Guid, (uint)sent.Index, sent.Amount));

        r.Stack(Arrows, 45);
        r.Wield(NewArrows, Ammo);
        r.R.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, split.Outcome!.Value.Kind);

        // Run 2: the slot already holds ammunition.
        r.R.Tick(2);
        var refused = r.Api.SplitToWield(Arrows, EquipMask.MissileAmmo, 5, ActionSource.Ui);
        r.R.Tick();
        r.Stack(Arrows, 40);
        r.Transient("You must remove your Arrow to wield Arrow");
        r.Failed(0x80000603);
        r.Stack(Arrows, 45);
        r.R.Tick();

        var outcome = refused.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.ServerError, outcome.Kind);
        Assert.Contains("You must remove your Arrow", outcome.Detail, StringComparison.Ordinal);
        Assert.True(r.R.H.State.Self.Inventory.TryGetStack(Arrows, out var size, out _));
        Assert.Equal(45u, size);
    }

    [Fact]
    public void The_splits_refuse_bad_arguments_at_once()
    {
        var r = new Rig();
        Assert.Equal(ActionOutcomeKind.Refused, r.Api.SplitToContainer(Arrows, 0, 0, 0, ActionSource.Ui).Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, r.Api.SplitTo3D(0, 5, ActionSource.Ui).Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, r.Api.SplitToWield(Arrows, EquipMask.None, 5, ActionSource.Ui).Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, r.Api.Give(0, Staff, 1, ActionSource.Ui).Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, r.Api.Give(Alfrin, Staff, 0, ActionSource.Ui).Outcome!.Value.Kind);
    }

    // ---- give ----------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void An_accepted_give_is_Confirmed_on_the_0024_and_the_foreign_0022_after_it_changes_nothing()
    {
        var r = new Rig();
        var give = r.Api.Give(Alfrin, Staff, 1, ActionSource.Ui);
        r.R.Tick();
        var sent = Assert.Single(r.R.H.Net.Of(FakeSendKind.GiveObject));
        Assert.Equal((Staff, 1u), (sent.Guid, sent.Amount));
        Assert.Equal(Alfrin.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), sent.Target);

        r.R.Tick(0.7);
        r.Removed(Staff);
        r.Put(Staff, Alfrin);
        r.R.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, give.Outcome!.Value.Kind);
        Assert.False(r.R.H.State.Self.Inventory.Find(Staff).Held);
    }

    [Fact]
    public void After_an_accepted_give_the_next_move_waits_0_95_seconds_and_after_a_refused_give_it_does_not_wait()
    {
        var r = new Rig();
        r.Api.Give(Alfrin, Staff, 1, ActionSource.Ui);
        r.Api.Drop(Sword, ActionSource.Ui);
        r.R.Tick(0.7);
        r.Removed(Staff);
        r.R.Tick();
        r.R.Tick(0.9);
        Assert.Equal(0, r.Sent(FakeSendKind.DropItem));
        r.R.Tick(0.2);
        Assert.Equal(1, r.Sent(FakeSendKind.DropItem));
        Assert.Single(r.R.H.Log.Entries, e => e.Message.Contains("TODO(m4-give-chain)", StringComparison.Ordinal)); // 2.1-R7: logged when the give first holds the lane

        var r2 = new Rig();
        r2.Api.Give(Alfrin, Staff, 1, ActionSource.Ui);
        r2.Api.Drop(Sword, ActionSource.Ui);
        r2.R.Tick();
        r2.Failed(Staff, 0x04CD);
        r2.R.Tick();
        Assert.Equal(1, r2.Sent(FakeSendKind.DropItem));
    }

    [Fact]
    public void A_give_of_part_of_a_stack_is_Confirmed_on_the_0197()
    {
        var r = new Rig();
        var give = r.Api.Give(Alfrin, Arrows, 5, ActionSource.Ui);
        r.R.Tick(0.7);
        r.Stack(Arrows, 45);
        r.R.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, give.Outcome!.Value.Kind);
        Assert.Contains("part of the stack", give.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void An_NPC_with_no_emote_for_the_item_refuses_with_028B_0x046A_and_00A0_and_the_text_with_the_NPCs_name()
    {
        var r = new Rig();
        var give = r.Api.Give(Alfrin, Arrows, 1, ActionSource.Ui);
        r.R.Tick();
        r.ErrorText(0x046A, "Alfrin");
        r.Failed(Arrows);
        r.R.Tick();

        var outcome = give.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.ServerError, outcome.Kind);
        Assert.Equal(0x046Au, outcome.Code);
        Assert.Contains("Alfrin", outcome.Detail, StringComparison.Ordinal);
        Assert.True(r.R.H.State.Self.Inventory.Find(Arrows).Held);
    }

    [Fact]
    public void A_028B_refusal_with_no_00A0_ends_the_give_by_itself_after_half_a_second()
    {
        var r = new Rig();
        var give = r.Api.Give(Alfrin, Staff, 1, ActionSource.Ui);
        r.R.Tick();
        r.ErrorText(0x03EF, "Alfrin");
        r.R.Tick(0.4);
        Assert.False(give.IsCompleted);
        r.R.Tick(0.2);
        Assert.Equal(ActionOutcomeKind.ServerError, give.Outcome!.Value.Kind);
        Assert.Equal(0x03EFu, give.Outcome.Value.Code);
    }

    [Fact]
    public void A_give_refused_with_a_00A0_code_0x04CD_and_a_give_sent_busy_each_carry_their_codes()
    {
        var r = new Rig();
        var refuse = r.Api.Give(Alfrin, Staff, 1, ActionSource.Ui);
        r.R.Tick();
        r.Failed(Staff, 0x04CD);
        r.R.Tick();
        Assert.Equal(0x04CDu, refuse.Outcome!.Value.Code);

        r.R.Tick(2);
        var busy = r.Api.Give(Alfrin, Sword, 1, ActionSource.Ui);
        r.R.Tick();
        r.Error(0x001D);
        r.Failed(Sword);
        r.R.Tick();
        Assert.Equal(0x001Du, busy.Outcome!.Value.Code);
        Assert.Equal(ActionOutcomeKind.ServerError, busy.Outcome.Value.Kind);
    }

    [Fact]
    public void A_give_that_hears_nothing_times_out_after_20_seconds()
    {
        var r = new Rig();
        var give = r.Api.Give(Alfrin, Staff, 1, ActionSource.Ui);
        r.R.Tick();
        r.R.Tick(19.9);
        Assert.False(give.IsCompleted);
        r.R.Tick(0.2);
        Assert.Equal(ActionOutcomeKind.TimedOut, give.Outcome!.Value.Kind);
    }

    // ---- M5 7 D141: the pick-up's typed before-check -----------------------------------------------------------------------------------------------------

    [Fact]
    public void M5_7_D141_a_pick_up_of_an_item_seen_when_asked_and_gone_at_its_turn_is_refused_TargetGone_and_nothing_is_sent()
    {
        var r = new Rig();
        r.GroundItem(Ground);
        var pick = r.Api.PickUp(Ground, 0, 0, ActionSource.Automation);
        r.R.H.R.Reducer.OnDeleteObject(r.R.H.R.Context, new DeleteObjectMsg(Ground, 1)); // another took it, or it rotted, before the lane's turn
        r.R.Tick();

        Assert.Equal((ActionOutcomeKind.Refused, ActionReason.TargetGone), (pick.Outcome!.Value.Kind, pick.Outcome.Value.Reason));
        Assert.Contains("is gone", pick.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(0, r.Sent(FakeSendKind.PutItemInContainer));

        // An item in the viewed container only (its CreateObject not here) is seen too; one never seen is left to the server.
        r.OpenCorpse();
        var fromCorpse = r.Api.PickUp(CorpseItem, 0, 0, ActionSource.Automation);
        r.R.Tick();
        Assert.Equal(1, r.Sent(FakeSendKind.PutItemInContainer));
        Assert.False(fromCorpse.IsCompleted);
    }

    [Fact]
    public void M5_7_D141_a_pick_up_with_every_pack_slot_taken_is_refused_NoFreeSlot_and_nothing_is_sent()
    {
        var r = new WorldActionRig();
        var full = Enumerable.Range(0, 102).Select(i => new ContentDesc(0x80001000u + (uint)i, 0)).ToArray(); // the main pack's 102 slots (ACE's player capacity), no side pack
        r.Describe(full, []);
        r.Create(Ground, 1f, missile: false);
        var pick = r.Api.PickUp(Ground, 0, 0, ActionSource.Automation);
        r.Tick();

        Assert.Equal((ActionOutcomeKind.Refused, ActionReason.NoFreeSlot), (pick.Outcome!.Value.Kind, pick.Outcome.Value.Reason));
        Assert.Contains("no free pack slot", pick.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Empty(r.H.Net.Of(FakeSendKind.PutItemInContainer));

        var room = new WorldActionRig();
        room.Describe(full[..101], []);
        room.Create(Ground, 1f, missile: false);
        var fits = room.Api.PickUp(Ground, 0, 0, ActionSource.Automation);
        room.Tick();
        Assert.Single(room.H.Net.Of(FakeSendKind.PutItemInContainer)); // one slot free: sent
        Assert.False(fits.IsCompleted);
    }
}
