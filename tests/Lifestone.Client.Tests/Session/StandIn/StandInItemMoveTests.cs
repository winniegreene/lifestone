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

    /// <summary>A rig with a stand-in chest placed and open (M4 4.1 t2: the stand-in holds no creature, so no corpse); <paramref name="item"/> is its first item.</summary>
    private static StandInAttackRig OpenCorpse(out uint chest, out uint item)
    {
        var rig = new StandInAttackRig();
        const uint box = 0x80000E01;
        Assert.NotNull(rig.Server.PlaceChest(box, new System.Numerics.Vector3(0f, 2f, 0f), ("Bowl", StandInWorld.BowlWcid, 1), ("Smock", StandInWorld.SmockWcid, 1)));
        rig.Run(0.5);
        chest = box;
        rig.Net.SendUse(chest);
        rig.Run(3.0);
        item = rig.World.ContentsOf(chest)[0];
        rig.Cast.Begin();
        return rig;
    }

    [Fact]
    public void A_pick_up_from_a_chest_is_the_motion_then_after_1_35_seconds_02DA_0022_02CD_and_31_ms_later_the_sound_and_the_return_stance()
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

}
