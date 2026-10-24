using System.Numerics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 2.1 s2: the Action API's container and item-move actions over s3's stand-in, which answers in the live order of cache/m4/2-1/capture.md: open a stand-in chest, pick an item up, drop it, close the chest; the three
/// splits; a give accepted and a give refused. Moves are queued back to back: the Inventory lane's hold keeps each one clear of the stand-in's busy window (028A 0x001D), so <see cref="StandInWorld.BusyRefusals"/> stays 0.
/// </summary>
public sealed class StandInContainerActionTests
{
    private const uint Npc = 0x80000E10;

    private static IActionApi Actions(StandInAttackRig rig) => rig.Cast.Session.Actions;

    private const uint Chest = 0x80000E01;

    private static StandInAttackRig StockedChest(out uint chest)
    {
        var rig = new StandInAttackRig();
        Assert.NotNull(rig.Server.PlaceChest(Chest, new Vector3(0f, 2f, 0f), ("Bowl", StandInWorld.BowlWcid, 1), ("Smock", StandInWorld.SmockWcid, 1)));
        rig.Run(0.5);
        chest = Chest;
        return rig;
    }

    [Fact]
    public void Open_a_chest_pick_an_item_up_drop_it_and_close_the_chest()
    {
        using var rig = StockedChest(out var corpse);

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

}
