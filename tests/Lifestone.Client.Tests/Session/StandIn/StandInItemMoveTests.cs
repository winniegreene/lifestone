using System.Numerics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 2.1 s3: the stand-in's item moves in the order and spacing of cache/m4/2-1/capture.md C2 and C4: the pick-up from a corpse (1.35 s) and from the ground (0.74 s, then the F74A), the drop, the three splits, the give accepted and
/// refused, the busy refusal (028A 0x001D and 00A0 at once) and the split onto a wielded ammunition slot. Nothing here is ACE: the offline stand-in is proved against the capture, the live loops prove ACE.
/// </summary>
public sealed class StandInItemMoveTests
{
    private const double Tolerance = 0.015;
    private const uint Npc = 0x80000E10;

    private static uint SelfOf(StandInAttackRig rig) => rig.Cast.Session.State.Self.Guid;

    private static double At(StandInAttackRig rig, string tag, int occurrence = 0) => rig.Cast.Timeline.Where(e => e.Tag == tag).ElementAt(occurrence).At;

    /// <summary>A rig with the tusker killed and its corpse open; <paramref name="item"/> is its first item.</summary>
    private static StandInAttackRig OpenCorpse(out uint corpse, out uint item)
    {
        var rig = new StandInAttackRig();
        rig.MeleeKit(2.0, health: 1);
        rig.Net.SendMeleeAttack(StandInAttackRig.Target, 2, 0.5f);
        rig.Run(3.0);
        corpse = rig.Cast.Session.State.Objects.All.Single(o => o.Name.StartsWith("Corpse of", StringComparison.Ordinal)).Guid;
        rig.Net.SendUse(corpse);
        rig.Run(1.0);
        item = rig.World.ContentsOf(corpse)[0];
        rig.Cast.Begin();
        return rig;
    }

    [Fact]
    public void A_pick_up_from_the_corpse_is_the_motion_then_after_1_35_seconds_02DA_0022_02CD_and_31_ms_later_the_sound_and_the_return_stance()
    {
        using var rig = OpenCorpse(out _, out var item);

        rig.Net.SendPutItemInContainer(item, SelfOf(rig), 0);
        rig.Run(3.0);

        Assert.Equal(["F748", "F74C", "02DA", "0022", "02CD", "F750", "F74C"], rig.Tags);
        Assert.Equal(StandInWorld.PickupContainerSeconds, At(rig, "02DA") - At(rig, "F74C"), Tolerance);
        Assert.Equal(StandInWorld.ReturnStanceLagSeconds, At(rig, "F750") - At(rig, "0022"), Tolerance);
        Assert.True(rig.Cast.Session.State.Self.Inventory.Find(item).Held);
        Assert.Contains(rig.World.Pack, e => e.Guid == item);
    }

    [Fact]
    public void A_pick_up_from_the_ground_takes_0_74_seconds_and_ends_with_the_items_F74A()
    {
        using var rig = new StandInAttackRig();
        var here = rig.Cast.Session.State.Self.Position!.Value;
        const uint ground = 0x80060100;
        rig.World.DropOnGround(new StandInPackEntry(ground, StandInWorld.BowlWcid, "Bowl", 1), new AcPosition(here.CellId, here.Frame.Origin, here.Frame.Orientation));
        rig.Run(0.5);
        rig.Cast.Begin();

        rig.Net.SendPutItemInContainer(ground, SelfOf(rig), 0);
        rig.Run(3.0);

        Assert.Equal(["F748", "F74C", "02DA", "0022", "02CD", "F750", "F74C", "F74A"], rig.Tags);
        Assert.Equal(StandInWorld.PickupGroundSeconds, At(rig, "02DA") - At(rig, "F74C"), Tolerance);
        Assert.Equal(StandInWorld.PickupEventLagSeconds, At(rig, "F74A") - At(rig, "F74C", 1), Tolerance);
        Assert.Empty(rig.World.GroundItems);
        Assert.True(rig.Cast.Session.State.Self.Inventory.Find(ground).Held);
    }

    [Fact]
    public void A_drop_is_the_motion_then_02DA_02CD_02DA_019A_the_items_F745_and_its_settling_messages_the_sound_and_the_return_stance()
    {
        using var rig = new StandInAttackRig();
        var arrows = rig.Spawn(StandInAttackRig.Arrows, 50);
        rig.Cast.Begin();

        rig.Net.SendDropItem(arrows);
        rig.Run(3.0);

        Assert.Equal(["F748", "F74C", "02DA", "02CD", "02DA", "019A", "F745", "F748", "F74B", "F748", "F750", "F74C"], rig.Tags);
        Assert.Equal(StandInWorld.DropSeconds, At(rig, "02DA") - At(rig, "F74C"), Tolerance);
        Assert.Contains(arrows, rig.World.GroundItems);
        Assert.False(rig.Cast.Session.State.Self.Inventory.Find(arrows).Held);
        Assert.True(rig.Cast.Session.State.Objects.TryGet(arrows, out var dropped) && dropped.ContainerId is null or 0);
    }

    [Fact]
    public void A_split_to_a_container_is_the_new_guids_0022_then_0197_then_the_new_stacks_F745_and_the_burden_0_56_seconds_later()
    {
        using var rig = new StandInAttackRig();
        var arrows = rig.Spawn(StandInAttackRig.Arrows, 50);
        rig.Cast.Begin();

        rig.Net.SendSplitToContainer(arrows, SelfOf(rig), 0, 10);
        rig.Run(2.0);

        Assert.Equal(["0022", "0197", "F745", "02CD"], rig.Tags);
        Assert.Equal(StandInWorld.BurdenLagSeconds, At(rig, "02CD") - At(rig, "0022"), Tolerance);
        Assert.Equal([40, 10], rig.World.Pack.Where(e => e.Wcid == StandInAttackRig.Arrows).Select(e => e.Stack));
        var stack = rig.World.Pack.Single(e => e.Stack == 10);
        Assert.NotEqual(arrows, stack.Guid);
        Assert.True(rig.Cast.Session.State.Objects.Contains(stack.Guid));
    }

    [Fact]
    public void A_split_to_3D_is_the_motion_then_0197_02CD_the_new_stacks_F745_with_no_container_F748_F74B_the_sound_and_the_return_stance()
    {
        using var rig = new StandInAttackRig();
        var arrows = rig.Spawn(StandInAttackRig.Arrows, 50);
        rig.Cast.Begin();

        rig.Net.SendSplitTo3D(arrows, 5);
        rig.Run(3.0);

        Assert.Equal(["F748", "F74C", "0197", "02CD", "F745", "F748", "F74B", "F750", "F74C"], rig.Tags);
        Assert.Equal(StandInWorld.PickupGroundSeconds, At(rig, "0197") - At(rig, "F74C"), Tolerance);
        var created = Assert.Single(rig.World.GroundItems);
        Assert.True(rig.Cast.Session.State.Objects.TryGet(created, out var lying) && lying.ContainerId is null);
        Assert.Equal(45, rig.World.Pack.Single(e => e.Guid == arrows).Stack);
    }

    [Fact]
    public void A_split_to_wield_is_0197_02DA_02CE_0023_F625_the_sound_and_the_new_stacks_F745_with_no_animation()
    {
        using var rig = new StandInAttackRig();
        var arrows = rig.Spawn(StandInAttackRig.Arrows, 50);
        rig.Cast.Begin();

        rig.Net.SendSplitToWield(arrows, (uint)EquipMask.MissileAmmo, 5);
        rig.Run(1.5);

        Assert.Equal(["0197", "02DA", "02CE", "0023", "F625", "F750", "F745"], rig.Tags);
        var wielded = Assert.Single(rig.World.Wielded);
        Assert.Equal(5, wielded.Value.Stack);
        Assert.True(rig.Cast.Session.State.Self.Inventory.Find(wielded.Key).Kind == InventoryPlaceKind.Equipped);
        Assert.Equal(45, rig.World.Pack.Single(e => e.Guid == arrows).Stack);
    }

    [Fact]
    public void A_split_to_wield_onto_a_slot_holding_ammunition_is_refused_with_the_02EB_line_00A0_and_the_old_stack_restored()
    {
        using var rig = new StandInAttackRig();
        rig.Wield(rig.Spawn(StandInAttackRig.Arrows, 5), EquipMask.MissileAmmo);
        var arrows = rig.Spawn(StandInAttackRig.Arrows, 50);
        rig.Cast.Begin();

        rig.Net.SendSplitToWield(arrows, (uint)EquipMask.MissileAmmo, 5);
        rig.Run(1.5);

        Assert.Equal(["0197", "02EB", "00A0", "02CD", "0197"], rig.Tags);
        Assert.Equal(50, rig.World.Pack.Single(e => e.Guid == arrows).Stack);
        Assert.Single(rig.World.Wielded);
    }

    private static StandInAttackRig WithNpc(out uint mace, out uint bow)
    {
        var rig = new StandInAttackRig();
        Assert.NotNull(rig.Server.PlaceNear(new StandInObject(Npc, "Alfrin", default), new Vector3(0f, 2f, 0f)));
        rig.Run(0.5);
        mace = rig.Spawn(StandInAttackRig.Mace);
        bow = rig.Spawn(StandInAttackRig.Bow);
        rig.World.AcceptGive(Npc, StandInAttackRig.Mace);
        rig.Cast.Begin();
        return rig;
    }

    [Fact]
    public void A_give_an_NPC_accepts_is_the_turn_then_after_0_70_seconds_0024_02CD_0022_naming_the_NPC_the_line_and_the_sound()
    {
        using var rig = WithNpc(out var mace, out _);

        rig.Net.SendGiveObject(Npc, mace, 1);
        rig.Run(3.0);

        Assert.Equal(["F74C", "0024", "02CD", "0022", "F7E0", "F750"], rig.Tags);
        Assert.Equal(StandInWorld.GiveSeconds, At(rig, "0024") - At(rig, "F74C"), Tolerance);
        Assert.DoesNotContain(rig.World.Pack, e => e.Guid == mace);
        Assert.Contains(rig.Cast.Chat, l => l.Text.Contains("You give Alfrin", StringComparison.Ordinal));
    }

    [Fact]
    public void A_give_of_any_other_item_or_to_any_other_NPC_is_the_turn_then_028B_and_00A0()
    {
        using var rig = WithNpc(out var mace, out var bow);

        rig.Net.SendGiveObject(Npc, bow, 1);
        rig.Run(2.0);

        Assert.Equal(["F74C", "028B", "00A0"], rig.Tags);
        Assert.Contains(rig.World.Pack, e => e.Guid == bow);

        rig.Run(3.0);
        rig.Cast.Begin();
        rig.Net.SendGiveObject(0x80000E99, mace, 1);
        rig.Run(2.0);

        Assert.Equal(["F74C", "028B", "00A0"], rig.Tags);
        Assert.Contains(rig.World.Pack, e => e.Guid == mace);
    }

    [Fact]
    public void A_move_sent_while_the_previous_chain_runs_is_refused_at_once_with_028A_and_00A0_and_works_after_the_chain()
    {
        using var rig = OpenCorpse(out _, out var item);
        rig.Net.SendPutItemInContainer(item, SelfOf(rig), 0);
        rig.Run(1.9); // the 0022 has come; the return stance and one more animation length are still to run
        rig.Cast.Begin();

        rig.Net.SendDropItem(item);
        rig.Run(0.8);

        Assert.Equal(["028A", "00A0"], rig.Tags);
        Assert.Equal(1, rig.World.BusyRefusals);
        Assert.Empty(rig.World.GroundItems);

        rig.Run(3.0);
        rig.Cast.Begin();
        rig.Net.SendDropItem(item);
        rig.Run(2.5);

        Assert.Contains("019A", rig.Tags);
        Assert.Contains(item, rig.World.GroundItems);
    }

    [Fact]
    public void A_give_sent_while_the_previous_move_runs_is_refused_busy_too()
    {
        using var rig = WithNpc(out var mace, out var bow);
        rig.Net.SendDropItem(bow);
        rig.Run(0.5); // the drop's motion has started; its 02DA is still to come
        rig.Cast.Begin();

        rig.Net.SendGiveObject(Npc, mace, 1);
        rig.Run(0.4);

        Assert.Equal(["028A", "00A0"], rig.Tags);
        Assert.Contains(rig.World.Pack, e => e.Guid == mace);
    }

    [Fact]
    public void A_move_of_an_item_the_player_neither_holds_nor_can_see_is_00A0()
    {
        using var rig = new StandInAttackRig();
        rig.Cast.Begin();

        rig.Net.SendDropItem(0x80099999);
        rig.Net.SendSplitTo3D(0x80099998, 3);
        rig.Run(1.0);

        Assert.Equal(["00A0", "00A0"], rig.Tags);
    }
}
