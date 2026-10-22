using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// <c>Wield</c> (0x001A), <c>Unwield</c> and <c>MoveToPack</c> (both 0x0019; M3 1.2 s4b): each confirmed on the inventory message ACE sends (0x0023 for a wield, not the 0x02DA and 0x02CE zeros
/// before it; 0x0022 for a move), a ServerError on 0x00A0 (error 0 included), refused at once for an item the model does not have, 5 s timeout, one in flight at a time in call order.
/// </summary>
public sealed class InventoryTrackerTests
{
    private const uint Player = WorldActionRig.Player;
    private const uint Pack1 = WorldActionRig.Pack1;
    private const uint Sword = WorldActionRig.Sword;
    private const uint Staff = WorldActionRig.Staff;
    private const uint Gem = WorldActionRig.Gem;
    private const uint Robe = WorldActionRig.Robe;
    private const uint MainHand = WorldActionRig.MainHand;

    private static byte _seq;

    /// <summary>A pack with a staff, a sword and a side pack, a robe worn, and the objects the model keeps for them.</summary>
    private static WorldActionRig Rig()
    {
        var r = new WorldActionRig();
        r.Describe(
            [new ContentDesc(Staff, 0), new ContentDesc(Sword, 0), new ContentDesc(Pack1, 1)],
            [new EquippedDesc(Robe, 0x200, 7)]);
        r.Item(Staff, Player);
        r.Item(Sword, Player);
        r.Item(Robe, Player);
        return r;
    }

    // ACE's wield (Player_Inventory.cs 297-306): 02DA Wielder 0, 02CE CurrentWieldedLocation 0, then 0023.
    private static void AceWield(WorldActionRig r, uint item, uint location)
    {
        r.Raw(AceM3Writers.PublicUpdateInstanceId(++_seq, item, 3, 0));
        r.Raw(AceM3Writers.PublicUpdatePropertyInt(_seq, item, 10, 0));
        r.Server((p, s) => AceM3Writers.WieldObject(p, s, item, (int)location));
    }

    // ACE's unwield (400-405 and 1359): the same two zeros, 02DA Container, then 0022 into the pack.
    private static void AceUnwield(WorldActionRig r, uint item, uint container, uint placement)
    {
        r.Raw(AceM3Writers.PublicUpdateInstanceId(++_seq, item, 3, 0));
        r.Raw(AceM3Writers.PublicUpdatePropertyInt(_seq, item, 10, 0));
        r.Raw(AceM3Writers.PublicUpdateInstanceId(_seq, item, 2, container));
        r.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, item, container, placement, 0));
    }

    [Fact]
    public void A_wield_round_trip_sends_0x001A_and_only_ACE_s_0023_confirms_not_the_zeros_before_it()
    {
        var r = Rig();
        var wield = r.Api.Wield(Staff, EquipMask.MeleeWeapon, ActionSource.Ui);
        r.Tick();

        var sent = Assert.Single(r.H.Net.Of(FakeSendKind.GetAndWieldItem));
        Assert.Equal((Staff, (uint)EquipMask.MeleeWeapon), (sent.Guid, sent.Amount));

        r.Raw(AceM3Writers.PublicUpdateInstanceId(++_seq, Staff, 3, 0));
        r.Raw(AceM3Writers.PublicUpdatePropertyInt(_seq, Staff, 10, 0));
        r.Tick();
        Assert.False(wield.IsCompleted);

        r.Server((p, s) => AceM3Writers.WieldObject(p, s, Staff, (int)MainHand));
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, wield.Outcome!.Value.Kind);
        Assert.Equal(Staff, r.H.State.Self.Inventory.EquippedAt(MainHand));
    }

    [Fact]
    public void A_wield_of_an_item_in_a_side_pack_is_allowed_and_an_unheld_or_already_wielded_one_is_refused()
    {
        var r = Rig();
        r.Item(Gem, Pack1);
        r.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, Gem, Pack1, 0, 0)); // the gem enters the side pack
        r.Tick();

        Assert.Equal(InventoryPlaceKind.InSidePack, r.H.State.Self.Inventory.Find(Gem).Kind);
        var side = r.Api.Wield(Gem, EquipMask.Held, ActionSource.Ui);
        r.Tick();
        Assert.Equal(Gem, Assert.Single(r.H.Net.Of(FakeSendKind.GetAndWieldItem)).Guid);
        AceWield(r, Gem, (uint)EquipMask.Held);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, side.Outcome!.Value.Kind);

        var stranger = r.Api.Wield(0x80000999, EquipMask.Held, ActionSource.Ui);
        var worn = r.Api.Wield(Robe, EquipMask.ChestArmor, ActionSource.Ui);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Refused, stranger.Outcome!.Value.Kind);
        Assert.Contains("not in the pack or a side pack", stranger.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(ActionOutcomeKind.Refused, worn.Outcome!.Value.Kind);
        Assert.Contains("already wielded", worn.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Single(r.H.Net.Of(FakeSendKind.GetAndWieldItem));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0x0036u)]
    public void ACE_s_refusal_names_the_item_and_even_error_0_is_a_ServerError(uint error)
    {
        var r = Rig();
        var wield = r.Api.Wield(Sword, EquipMask.MeleeWeapon, ActionSource.Ui);
        r.Tick();

        r.Server((p, s) => AceM3Writers.InventoryServerSaysFailed(p, s, Gem, 7)); // another item's refusal is not this one's
        r.Tick();
        Assert.False(wield.IsCompleted);

        r.Server((p, s) => AceM3Writers.InventoryServerSaysFailed(p, s, Sword, error));
        r.Tick();
        Assert.Equal((ActionOutcomeKind.ServerError, error), (wield.Outcome!.Value.Kind, wield.Outcome.Value.Code));
        Assert.Contains("0x80000101", wield.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_wield_times_out_at_5_seconds_and_the_next_waits_its_turn()
    {
        var r = Rig();
        var first = r.Api.Wield(Staff, EquipMask.MeleeWeapon, ActionSource.Ui);
        var second = r.Api.Wield(Sword, EquipMask.MeleeWeapon, ActionSource.Ui);
        r.Tick();
        Assert.Equal([Staff], r.H.Net.Of(FakeSendKind.GetAndWieldItem).Select(s => s.Guid));

        r.Tick(4.9);
        Assert.False(first.IsCompleted);
        r.Tick(0.2);
        Assert.Equal(ActionOutcomeKind.TimedOut, first.Outcome!.Value.Kind);
        Assert.Equal([Staff, Sword], r.H.Net.Of(FakeSendKind.GetAndWieldItem).Select(s => s.Guid));
        Assert.False(second.IsCompleted);
        Assert.Equal(5.0, ActionPolicy.InventorySeconds);
    }

    [Fact]
    public void Wield_unwield_and_move_share_one_lane_and_leave_in_call_order()
    {
        var r = Rig();
        var wield = r.Api.Wield(Staff, EquipMask.MeleeWeapon, ActionSource.Ui);
        var unwield = r.Api.Unwield(Robe, ActionSource.Ui);
        var move = r.Api.MoveToPack(Sword, Pack1, 0, ActionSource.Ui);
        r.Tick();
        Assert.Equal(1, r.H.Net.Sends.Count(s => s.Kind is FakeSendKind.GetAndWieldItem or FakeSendKind.PutItemInContainer));

        r.Server((p, s) => AceM3Writers.WieldObject(p, s, Staff, (int)MainHand));
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, wield.Outcome!.Value.Kind);
        Assert.Equal(Robe, Assert.Single(r.H.Net.Of(FakeSendKind.PutItemInContainer)).Guid);
        Assert.False(unwield.IsCompleted);
        Assert.False(move.IsCompleted);

        AceUnwield(r, Robe, Player, 0);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, unwield.Outcome!.Value.Kind);
        Assert.Equal([Robe, Sword], r.H.Net.Of(FakeSendKind.PutItemInContainer).Select(s => s.Guid));
    }

    [Fact]
    public void An_unwield_puts_the_item_into_the_players_own_pack_and_ACE_s_0022_confirms_it_after_the_zeros()
    {
        var r = Rig();
        var unwield = r.Api.Unwield(Robe, ActionSource.Ui);
        r.Tick();

        var sent = Assert.Single(r.H.Net.Of(FakeSendKind.PutItemInContainer));
        Assert.Equal((Robe, 0, Player.ToString("X8")), (sent.Guid, sent.Index, sent.Target));

        r.Raw(AceM3Writers.PublicUpdateInstanceId(++_seq, Robe, 3, 0));
        r.Raw(AceM3Writers.PublicUpdatePropertyInt(_seq, Robe, 10, 0));
        r.Tick();
        Assert.False(unwield.IsCompleted);

        r.Raw(AceM3Writers.PublicUpdateInstanceId(_seq, Robe, 2, Player));
        r.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, Robe, Player, 0, 0));
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, unwield.Outcome!.Value.Kind);
        Assert.Equal(InventoryPlaceKind.MainPack, r.H.State.Self.Inventory.Find(Robe).Kind);
    }

    [Fact]
    public void An_unwield_of_an_item_that_is_not_worn_is_refused_at_once()
    {
        var r = Rig();
        var unwield = r.Api.Unwield(Sword, ActionSource.Ui);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Refused, unwield.Outcome!.Value.Kind);
        Assert.Contains("not wielded or worn", unwield.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Empty(r.H.Net.Of(FakeSendKind.PutItemInContainer));
    }

    [Fact]
    public void A_move_to_a_side_pack_sends_the_container_and_slot_and_is_confirmed_by_the_0022_into_that_pack_only()
    {
        var r = Rig();
        var move = r.Api.MoveToPack(Sword, Pack1, 2, ActionSource.Ui);
        r.Tick();

        var sent = Assert.Single(r.H.Net.Of(FakeSendKind.PutItemInContainer));
        Assert.Equal((Sword, 2, Pack1.ToString("X8")), (sent.Guid, sent.Index, sent.Target));

        r.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, Staff, Pack1, 0, 0)); // another item moves: not the answer
        r.Tick();
        Assert.False(move.IsCompleted);

        r.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, Sword, Pack1, 2, 0));
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, move.Outcome!.Value.Kind);
        Assert.Equal(InventoryPlaceKind.InSidePack, r.H.State.Self.Inventory.Find(Sword).Kind);
    }

    [Fact]
    public void A_move_with_container_0_means_the_main_pack_and_a_worn_item_moved_out_confirms_as_unwielded()
    {
        var r = Rig();
        var move = r.Api.MoveToPack(Robe, 0, 0, ActionSource.Ui);
        r.Tick();
        Assert.Equal(Player.ToString("X8"), Assert.Single(r.H.Net.Of(FakeSendKind.PutItemInContainer)).Target);

        AceUnwield(r, Robe, Player, 0);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, move.Outcome!.Value.Kind);
    }

    [Fact]
    public void A_move_of_an_item_not_held_is_a_pick_up_and_a_move_ACE_refuses_is_a_ServerError()
    {
        var r = Rig();
        var stranger = r.Api.MoveToPack(0x80000999, Pack1, 0, ActionSource.Ui);
        var refused = r.Api.MoveToPack(Staff, Pack1, 0, ActionSource.Ui);
        r.Tick();
        Assert.Null(stranger.Outcome); // M4 2.1 s2: an item the player does not hold is a pick-up (TODO(m3-pickup) closed): sent first, the lane in call order
        Assert.Equal(0x80000999u, Assert.Single(r.H.Net.Of(FakeSendKind.PutItemInContainer)).Guid);

        r.Server((p, s) => AceM3Writers.InventoryServerSaysFailed(p, s, 0x80000999, 0)); // the pick-up is refused; the lane then sends the next move
        r.Tick();
        Assert.Equal(ActionOutcomeKind.ServerError, stranger.Outcome!.Value.Kind);
        r.Server((p, s) => AceM3Writers.InventoryServerSaysFailed(p, s, Staff, 0));
        r.Tick();
        Assert.Equal((ActionOutcomeKind.ServerError, 0u), (refused.Outcome!.Value.Kind, refused.Outcome.Value.Code));
    }

    [Fact]
    public void Nothing_is_sent_before_the_world_and_the_argument_checks_still_apply()
    {
        var early = new WorldActionRig(present: false);
        var handles = new[]
        {
            early.Api.Wield(Staff, EquipMask.Held, ActionSource.Ui),
            early.Api.Unwield(Staff, ActionSource.Ui),
            early.Api.MoveToPack(Staff, 0, 0, ActionSource.Ui),
        };
        early.Tick();
        Assert.All(handles, h => Assert.Equal(ActionOutcomeKind.Refused, h.Outcome!.Value.Kind));
        Assert.Empty(early.H.Net.Sends);
    }
}
