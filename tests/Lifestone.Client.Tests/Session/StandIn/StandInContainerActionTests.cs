using System.Numerics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 2.1 s2: the Action API's container and item-move actions over s3's stand-in, which answers in the live order of cache/m4/2-1/capture.md: open the tusker's corpse, pick an item up, drop it, close the corpse; the three
/// splits; a give accepted and a give refused. Moves are queued back to back: the Inventory lane's hold keeps each one clear of the stand-in's busy window (028A 0x001D), so <see cref="StandInWorld.BusyRefusals"/> stays 0.
/// </summary>
public sealed class StandInContainerActionTests
{
    private const uint Npc = 0x80000E10;

    private static IActionApi Actions(StandInAttackRig rig) => rig.Cast.Session.Actions;

    private static StandInAttackRig KilledTusker(out uint corpse)
    {
        var rig = new StandInAttackRig();
        rig.MeleeKit(2.0, health: 1);
        rig.Net.SendMeleeAttack(StandInAttackRig.Target, 2, 0.5f);
        rig.Run(3.0);
        corpse = rig.Cast.Session.State.Objects.All.Single(o => o.Name.StartsWith("Corpse of", StringComparison.Ordinal)).Guid;
        return rig;
    }

    [Fact]
    public void Open_the_tuskers_corpse_pick_an_item_up_drop_it_and_close_the_corpse()
    {
        using var rig = KilledTusker(out var corpse);

        var open = Actions(rig).OpenContainer(corpse, ActionSource.Host);
        rig.Run(2.0);
        Assert.Equal(ActionOutcomeKind.Confirmed, open.Outcome!.Value.Kind);
        Assert.Equal(corpse, rig.Cast.Session.State.Containers.ViewedGuid);
        var item = rig.World.ContentsOf(corpse)[0];

        var pick = Actions(rig).PickUp(item, 0, 0, ActionSource.Host);
        var drop = Actions(rig).Drop(item, ActionSource.Host); // queued behind the pick-up's chain
        rig.Run(8.0);

        Assert.Equal(ActionOutcomeKind.Confirmed, pick.Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Confirmed, drop.Outcome!.Value.Kind);
        Assert.Contains(item, rig.World.GroundItems);
        Assert.Equal(0, rig.World.BusyRefusals);

        var close = Actions(rig).CloseContainer(corpse, ActionSource.Host);
        rig.Run(2.0);
        Assert.Equal(ActionOutcomeKind.Confirmed, close.Outcome!.Value.Kind);
        Assert.Equal(0u, rig.Cast.Session.State.Containers.ViewedGuid);

        var again = Actions(rig).CloseContainer(corpse, ActionSource.Host);
        rig.Run(0.2);
        Assert.Equal("not open", again.Outcome!.Value.Detail);
    }

    [Fact]
    public void A_ground_item_is_picked_up_with_PickUp_and_with_MoveToPack_and_the_second_waits_for_the_first_chain()
    {
        using var rig = new StandInAttackRig();
        var here = rig.Cast.Session.State.Self.Position!.Value;
        const uint first = 0x80060100;
        const uint second = 0x80060101;
        rig.World.DropOnGround(new StandInPackEntry(first, StandInWorld.BowlWcid, "Bowl", 1), new AcPosition(here.CellId, here.Frame.Origin, here.Frame.Orientation));
        rig.World.DropOnGround(new StandInPackEntry(second, StandInWorld.BowlWcid, "Bowl", 1), new AcPosition(here.CellId, here.Frame.Origin, here.Frame.Orientation));
        rig.Run(0.5);

        var pick = Actions(rig).PickUp(first, 0, 0, ActionSource.Host);
        var move = Actions(rig).MoveToPack(second, 0, 0, ActionSource.Host);
        rig.Run(6.0);

        Assert.Equal(ActionOutcomeKind.Confirmed, pick.Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Confirmed, move.Outcome!.Value.Kind);
        Assert.True(rig.Cast.Session.State.Self.Inventory.Find(second).Held);
        Assert.Equal(0, rig.World.BusyRefusals);
    }

    [Fact]
    public void The_three_splits_each_Confirm_in_the_order_the_stand_in_answers()
    {
        using var rig = new StandInAttackRig();
        var arrows = rig.Spawn(StandInAttackRig.Arrows, 50);

        var toPack = Actions(rig).SplitToContainer(arrows, 0, 0, 10, ActionSource.Host);
        var to3D = Actions(rig).SplitTo3D(arrows, 5, ActionSource.Host);
        var toWield = Actions(rig).SplitToWield(arrows, EquipMask.MissileAmmo, 5, ActionSource.Host);
        rig.Run(8.0);

        Assert.Equal(ActionOutcomeKind.Confirmed, toPack.Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Confirmed, to3D.Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Confirmed, toWield.Outcome!.Value.Kind);
        Assert.Equal(0, rig.World.BusyRefusals);
        Assert.Single(rig.World.GroundItems);
    }

    [Fact]
    public void A_wield_split_onto_a_wielded_ammunition_slot_is_a_ServerError_with_the_02EB_text()
    {
        using var rig = new StandInAttackRig();
        var arrows = rig.Spawn(StandInAttackRig.Arrows, 50);
        var first = Actions(rig).SplitToWield(arrows, EquipMask.MissileAmmo, 5, ActionSource.Host);
        rig.Run(2.0);
        Assert.Equal(ActionOutcomeKind.Confirmed, first.Outcome!.Value.Kind);

        var second = Actions(rig).SplitToWield(arrows, EquipMask.MissileAmmo, 5, ActionSource.Host);
        rig.Run(2.0);
        Assert.Equal(ActionOutcomeKind.ServerError, second.Outcome!.Value.Kind);
        Assert.Contains("You must remove your", second.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_give_the_NPC_accepts_is_Confirmed_and_a_give_it_has_no_emote_for_is_a_ServerError_0x046A()
    {
        using var rig = new StandInAttackRig();
        Assert.NotNull(rig.Server.PlaceNear(new StandInObject(Npc, "Alfrin", default), new Vector3(0f, 2f, 0f)));
        rig.Run(0.5);
        var mace = rig.Spawn(StandInAttackRig.Mace);
        var bow = rig.Spawn(StandInAttackRig.Bow);
        rig.World.AcceptGive(Npc, StandInAttackRig.Mace);

        var accepted = Actions(rig).Give(Npc, mace, 1, ActionSource.Host);
        var refused = Actions(rig).Give(Npc, bow, 1, ActionSource.Host);
        rig.Run(8.0);

        Assert.Equal(ActionOutcomeKind.Confirmed, accepted.Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.ServerError, refused.Outcome!.Value.Kind);
        Assert.Equal(0x046Au, refused.Outcome.Value.Code);
        Assert.Contains("Alfrin", refused.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(0, rig.World.BusyRefusals);
    }
}
