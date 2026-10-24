using System.Numerics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 2.1 s3: the stand-in's containers in the order ACE really sends (cache/m4/2-1/capture.md C2 and C3): the chest (its open and close animations) and the second container that closes the first. M4 4.1 t2 (D23, D26): the stand-in holds no creature, so it leaves no corpse; the tests of a corpse's open, close and re-open
/// were deleted with it (a creature's corpse is proved live by the hunt leg and <c>ContainerLiveTests</c>). Nothing here is ACE: the offline stand-in is proved against the capture, the live loops prove ACE.
/// </summary>
public sealed class StandInContainerTests
{
    private const double Tolerance = 0.015;
    private const uint Chest = 0x80000E01;

    private static void Use(StandInAttackRig rig, uint guid, double seconds = 1.0)
    {
        rig.Cast.Begin();
        rig.Net.SendUse(guid);
        rig.Run(seconds);
    }

    private static double At(StandInAttackRig rig, string tag, int occurrence = 0) => rig.Cast.Timeline.Where(e => e.Tag == tag).ElementAt(occurrence).At;

    [Fact]
    public void A_chest_opens_1_24_seconds_after_the_turn_then_its_On_motion_follows_the_UseDone()
    {
        using var rig = new StandInAttackRig();
        Assert.NotNull(rig.Server.PlaceChest(Chest, new Vector3(0f, 3f, 0f), ("Bowl", StandInWorld.BowlWcid, 1)));
        rig.Run(0.5);

        Use(rig, Chest, 2.5);

        Assert.Equal(["F74C", "0196", "01C7", "F74C", "F745"], rig.Tags);
        Assert.Equal(StandInWorld.ChestOpenSeconds, At(rig, "0196") - At(rig, "F74C"), Tolerance);
        Assert.Equal(Chest, rig.World.OpenContainerGuid);
        Assert.Contains(rig.Cast.Session.State.Objects.All, o => o.ContainerId == Chest && o.Name == "Bowl");
        Assert.Contains("TODO(m4-standin-chest)", string.Join('\n', rig.Cast.Log.Entries.Select(e => e.Line)), StringComparison.Ordinal);
    }

    [Fact]
    public void Use_on_the_open_chest_is_its_Off_motion_UseDone_then_0052_0_42_seconds_later_and_0195_is_Off_then_0052_with_no_UseDone()
    {
        using var rig = new StandInAttackRig();
        Assert.NotNull(rig.Server.PlaceChest(Chest, new Vector3(0f, 3f, 0f)));
        rig.Run(0.5);
        Use(rig, Chest, 2.5);

        Use(rig, Chest, 1.5);

        Assert.Equal(["F74C", "01C7", "0052"], rig.Tags);
        Assert.Equal(StandInWorld.ChestCloseSeconds, At(rig, "0052") - At(rig, "01C7"), Tolerance);

        Use(rig, Chest, 2.5);
        rig.Cast.Begin();
        rig.Net.SendNoLongerViewingContents(Chest);
        rig.Run(1.5);

        Assert.Equal(["F74C", "0052"], rig.Tags);
        Assert.Equal(StandInWorld.ChestCloseSeconds, At(rig, "0052") - At(rig, "F74C"), Tolerance);
        Assert.Equal(0u, rig.World.OpenContainerGuid);
    }

}
