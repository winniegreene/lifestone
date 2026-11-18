using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 7 AX: the pull of a wanted trophy's holder, on the real Burrow's keep-out and holder cell, over the scripted ACE and walk. Proved: she walks to the stand by a bounded leg count and fails by name when it cannot be reached; from the stand the shot goes at the named holder though its cell is kept out, the try is counted and the keep-out is lifted for the fight;
/// an absent holder is waited for by name up to the limit, never shot at, and the failure is remembered; a held tusk ends the pull. A character who may not try does not begin. A tusker in reach of her is fought first by the ordinary pick.
/// </summary>
public sealed partial class FightScenarioTests
{
    private const uint HolderGuid = 0x80002101;
    private const uint HolderWcid = 22589;
    private const uint HolderCell = 0x57480106;
    private const uint StandCell = 0x5748013D;
    private const uint Tusk = 22423;

    /// <summary>The rig on the Burrow's data: the stand moved to where she stands (or <paramref name="offStand"/> metres off), the fullness bar off, the pull's wait limit as given.</summary>
    private static Rig PullRig(float offStand = 0, double wait = 420, int kills = 1)
    {
        var burrow = GroundCatalog.Get("tusker-burrow");
        var pull = burrow.Hunt!.Pulls[0];
        var at = AtStand(new Rig()).Self.Position!.Value.Origin; // where the rig stands her, so a stand can be put there
        var stand = pull.Stand! with { Point = at with { X = at.X + offStand }, WaitSeconds = wait };
        var r = new Rig(fight: new FightOptions
        {
            Ground = "tusker-burrow",
            GroundForTest = burrow with { PullRest = null, Hunt = burrow.Hunt with { Pulls = [pull with { Stand = stand }] } },
            Want = [Tusk],
            Kills = kills,
        });
        AtStand(r);
        return r;
    }

    private static GameState AtStand(Rig r)
    {
        r.R.H.R.Reducer.OnUpdatePosition(r.R.H.R.Context, ReducerHarness.Move(ActionHarness.Amy, 1, 3, 2, 1, cell: StandCell, x: PlayerX));
        r.R.H.Unhide(stateStamp: 5);
        return r.R.H.State;
    }

    private static int Pulled(Rig r, string text) => r.R.H.Log.Lines.Count(l => l.Contains(text, StringComparison.Ordinal));

    [Fact]
    public void From_the_stand_the_shot_goes_at_the_named_holder_in_the_kept_out_cell_and_the_try_is_counted()
    {
        var r = PullRig();
        r.Add(HolderGuid, HolderWcid, HolderCell, 40, "Female Tusker");
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(HolderGuid, r.CastTargets[0]);
        Assert.Equal(1, Pulled(r, "pull: check before the shot: holder 0x80002101"));
        Assert.Equal(1, Pulled(r, "pull: check after the walk: at the stand"));
        Assert.Equal(1, TrophyPullMemory.For(r.R.H.State).TriesOf("tusk-female"));
    }

    [Fact]
    public void An_unwanted_hall_tusker_is_never_the_shot_only_the_named_holder()
    {
        var r = PullRig();
        r.Add(TuskerGuid, HolderWcid - 22353, 0x57480105, 30, "Female Tusker"); // slot 5's weenie 236 in the keep-out, nearer than the holder
        r.Add(HolderGuid, HolderWcid, HolderCell, 40, "Female Tusker");
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(HolderGuid, r.CastTargets[0]);
    }

    [Fact]
    public void She_walks_to_the_stand_and_a_stand_she_cannot_reach_fails_by_name_with_the_keep_out_back()
    {
        var r = PullRig(offStand: 5, kills: 1);
        var walk = new FakeWalk { TicksPerLeg = 1 }; // the rig's walk moves nobody
        var scenario = ToTheFight(r, r.Scenario(walk));

        r.Run(scenario, 120);

        Assert.Equal(3, walk.Spots.Count);
        Assert.All(walk.Spots, s => Assert.Equal(StandCell, s.CellId));
        Assert.Equal(1, Pulled(r, "pull: failed:stand-unreachable"));
        Assert.Equal("stand-unreachable", TrophyPullMemory.For(r.R.H.State).FailureOf("tusk-female"));
        Assert.Empty(r.CastTargets);
    }

    [Fact]
    public void An_absent_holder_is_waited_for_by_name_up_to_the_limit_and_then_fails_by_name()
    {
        var r = PullRig(wait: 20);
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));

        r.Run(scenario, 60);

        Assert.Equal(1, Pulled(r, "pull: the holder 22589 is not in 0x57480106: waiting for its respawn at the stand, at most 20 s"));
        Assert.Equal(1, Pulled(r, "pull: failed:holder-absent"));
        Assert.Empty(r.CastTargets);
        Assert.Equal(0, TrophyPullMemory.For(r.R.H.State).TriesOf("tusk-female")); // no shot, no try: but the failure is named
        Assert.False(TrophyPullMemory.For(r.R.H.State).MayTry("tusk-female", 2));
    }

    [Fact]
    public void A_holder_that_respawns_inside_the_limit_is_shot()
    {
        var r = PullRig(wait: 400);
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));
        r.Run(scenario, 10);
        Assert.Equal(1, Pulled(r, "waiting for its respawn at the stand"));
        Assert.Empty(r.CastTargets);

        r.Add(HolderGuid, HolderWcid, HolderCell, 40, "Female Tusker");

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));
        Assert.Equal(HolderGuid, r.CastTargets[0]);
    }

    [Fact]
    public void A_held_tusk_ends_the_pull_with_no_shot()
    {
        var r = PullRig();
        var item = 0x80003001u;
        var basis = ReducerHarness.Create(item, ReducerHarness.Stamps(), null, name: "Female Tusker Tusk");
        r.R.H.R.Reducer.OnCreateObject(r.R.H.R.Context, basis with { Weenie = basis.Weenie with { Header = WeenieHeaderFlags.Container, ContainerId = ActionHarness.Amy, WeenieClassId = Tusk } }, isUpdate: false);
        r.R.H.R.Reducer.OnInventoryPutObjInContainer(r.R.H.R.Context, new GameEventHeader(ActionHarness.Amy, 0, 0x0022), new InventoryPutObjInContainerEvent(item, ActionHarness.Amy, 0, 0));
        r.Add(HolderGuid, HolderWcid, HolderCell, 40, "Female Tusker");
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));

        r.Run(scenario, 20);

        Assert.Equal(1, Pulled(r, "pull: done: Female Tusker Tusk: the tusk is held; the keep-out is back"));
        Assert.Empty(r.CastTargets);
    }

    [Fact]
    public void A_character_whose_tries_are_spent_does_not_begin()
    {
        var r = PullRig();
        var memory = TrophyPullMemory.For(r.R.H.State);
        memory.NoteTry("tusk-female");
        memory.NoteTry("tusk-female");
        r.Add(HolderGuid, HolderWcid, HolderCell, 40, "Female Tusker");
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));

        r.Run(scenario, 20);

        Assert.Equal(1, Pulled(r, "pull: Female Tusker Tusk: not tried: tries spent (2 of 2)"));
        Assert.Equal(0, Pulled(r, "pull: check before the shot"));
    }
}
