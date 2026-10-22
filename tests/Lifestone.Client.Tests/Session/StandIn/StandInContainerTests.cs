using System.Numerics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 2.1 s3: the stand-in's corpses and containers in the order ACE really sends (cache/m4/2-1/capture.md C2 and C3): the corpse a kill leaves, the open (the turn, 0196, UseDone, then the contents' F745s), the three ways to close,
/// the re-open, the chest (its open and close animations) and the second container that closes the first. Nothing here is ACE: the offline stand-in is proved against the capture, the live loops prove ACE.
/// </summary>
public sealed class StandInContainerTests
{
    private const double Tolerance = 0.015;
    private const uint Chest = 0x80000E01;

    private static StandInAttackRig Killed(out uint corpse)
    {
        var rig = new StandInAttackRig();
        rig.MeleeKit(2.0, health: 1);
        rig.Net.SendMeleeAttack(StandInAttackRig.Target, 2, 0.5f);
        rig.Run(3.0);
        corpse = rig.Cast.Session.State.Objects.All.Single(o => o.Name.StartsWith("Corpse of", StringComparison.Ordinal)).Guid;
        rig.Cast.Begin();
        return rig;
    }

    private static void Use(StandInAttackRig rig, uint guid, double seconds = 1.0)
    {
        rig.Cast.Begin();
        rig.Net.SendUse(guid);
        rig.Run(seconds);
    }

    private static double At(StandInAttackRig rig, string tag, int occurrence = 0) => rig.Cast.Timeline.Where(e => e.Tag == tag).ElementAt(occurrence).At;

    [Fact]
    public void A_kill_leaves_a_corpse_named_for_the_creature_in_the_tick_the_creature_goes_0_54_seconds_after_the_01AD()
    {
        using var rig = new StandInAttackRig();
        rig.MeleeKit(2.0, health: 1);
        rig.Net.SendMeleeAttack(StandInAttackRig.Target, 2, 0.5f);
        rig.Run(3.0);

        var corpse = Assert.Single(rig.Cast.Session.State.Objects.All, o => o.Name.StartsWith("Corpse of", StringComparison.Ordinal));
        Assert.Equal("Corpse of Tusker Slave", corpse.Name);
        Assert.Equal(21u, corpse.Weenie.WeenieClassId);
        Assert.NotEqual(0u, corpse.Weenie.ObjectDescriptionFlags & WorldObjectTable.CorpseFlag);
        Assert.Null(corpse.ContainerId);
        Assert.False(rig.Cast.Session.State.Objects.Contains(StandInAttackRig.Target));
        Assert.Equal(StandInWorld.CorpseSeconds, At(rig, "F745") - At(rig, "01AD"), Tolerance * 4);
        Assert.Equal(At(rig, "F745"), At(rig, "F747"), 1e-9); // the corpse and the creature's removal in one tick
        Assert.Equal(["F745", "F747"], rig.Tags.Where(t => t is "F745" or "F747"));
        Assert.Equal(2, rig.World.ContentsOf(corpse.Guid).Count);
    }

    [Fact]
    public void Use_on_the_corpse_is_the_turn_then_0196_UseDone_and_the_contents_16_ms_later()
    {
        using var rig = Killed(out var corpse);

        Use(rig, corpse);

        Assert.Equal(["F74C", "F748", "0196", "01C7", "F745", "F745"], rig.Tags);
        Assert.Equal(At(rig, "0196"), At(rig, "01C7"), 1e-9);
        Assert.Equal(StandInWorld.ContentsLagSeconds, At(rig, "F745") - At(rig, "01C7"), Tolerance);
        Assert.Equal(corpse, rig.World.OpenContainerGuid);
        var inside = rig.Cast.Session.State.Objects.All.Where(o => o.ContainerId == corpse).ToList();
        Assert.Equal(["Bowl", "Smock"], inside.Select(o => o.Name).Order());
        Assert.Equal(rig.World.ContentsOf(corpse).Order(), inside.Select(o => o.Guid).Order());
    }

    [Fact]
    public void NoLongerViewingContents_closes_the_corpse_with_0052_and_no_UseDone()
    {
        using var rig = Killed(out var corpse);
        Use(rig, corpse);

        rig.Cast.Begin();
        rig.Net.SendNoLongerViewingContents(corpse);
        rig.Run(1.0);

        Assert.Equal(["0052"], rig.Tags);
        Assert.Equal(0u, rig.World.OpenContainerGuid);
    }

    [Fact]
    public void Use_on_the_open_corpse_closes_it_with_0052_and_UseDone_in_one_tick()
    {
        using var rig = Killed(out var corpse);
        Use(rig, corpse);

        Use(rig, corpse);

        Assert.Equal(["0052", "01C7"], rig.Tags);
        Assert.Equal(At(rig, "0052"), At(rig, "01C7"), 1e-9);
        Assert.Equal(0u, rig.World.OpenContainerGuid);
    }

    [Fact]
    public void A_reopen_resends_0196_and_the_contents()
    {
        using var rig = Killed(out var corpse);
        Use(rig, corpse);
        rig.Net.SendNoLongerViewingContents(corpse);
        rig.Run(1.0);

        Use(rig, corpse);

        Assert.Equal(["F74C", "F748", "0196", "01C7", "F745", "F745"], rig.Tags);
        Assert.Equal(corpse, rig.World.OpenContainerGuid);
    }

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

    [Fact]
    public void Opening_a_second_container_closes_the_first_with_its_0052_first()
    {
        using var rig = Killed(out var corpse);
        Assert.NotNull(rig.Server.PlaceChest(Chest, new Vector3(0f, 3f, 0f)));
        rig.Run(0.5);
        Use(rig, corpse);

        Use(rig, Chest, 2.5);

        Assert.Equal(["0052", "F74C", "0196", "01C7", "F74C"], rig.Tags);
        Assert.Equal(Chest, rig.World.OpenContainerGuid);
    }

    [Fact]
    public void A_NoLongerViewingContents_for_a_container_that_is_not_open_is_ignored()
    {
        using var rig = Killed(out var corpse);

        rig.Net.SendNoLongerViewingContents(corpse);
        rig.Run(1.0);

        Assert.Empty(rig.Tags);
    }
}
