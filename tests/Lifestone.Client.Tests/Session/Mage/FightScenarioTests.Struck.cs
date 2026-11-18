using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 14, the struck proof (<c>m6-struck-proof</c>): a creature whose melee blow just came is cast at where the mage stands, whatever the line test says of its last known position (ACE sends no position for a monster in
/// melee that follows her, so the client knows it where it stood before). A blow from farther than <see cref="FightScenario.StruckProofMetres"/> proves nothing and the walk toward it stands as before. Over the same scripted
/// ACE and walk as the other fight tests; nothing here needs the dats or a server.
/// </summary>
public sealed partial class FightScenarioTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_attacker_whose_blow_just_came_from_close_is_cast_at_with_no_walk_though_the_line_test_says_blocked(bool evaded)
    {
        var r = new Rig();
        r.Add(TuskerGuid, TuskerSlave, OtherFloorCell, 45);
        var virindi = r.Add(VirindiGuid, Virindi, FloorCell, 25, "Virindi"); // 5 m off by its last known position, behind a wall by the line test
        var walk = new FakeWalk { BlockedCell = FloorCell, KeepBlockedCell = true, TicksPerLeg = 3, TraceCells = [Den, OtherFloorCell, FloorCell] };
        var scenario = ToTheFight(r, r.Scenario(walk));

        if (evaded)
        {
            Evade(r);
        }
        else
        {
            MeleeHit(r);
        }

        for (var i = 0; i < 400 && !virindi.Dead; i++)
        {
            r.Frame(scenario);
        }

        Assert.True(virindi.Dead);
        Assert.Equal(VirindiGuid, r.CastTargets[0]);
        Assert.Equal(0, walk.Legs); // no leg while it was on her
        Assert.Equal(1, scenario.StruckProofs);
        Assert.Equal(1, LinesWith(r, "self-defence: Virindi 0x80002001 struck the mage "));
        Assert.Equal(1, LinesWith(r, $" s ago ({(evaded ? "evasion notice" : "damage notice")}): it stands at her, whatever the line test says of its last known position (0x01050303 [100 25 30], 5 m from her at 0x01050375 [100 20 30]; a monster in melee follows her with no position sent"));
        Assert.Equal(1, LinesWith(r, "it is cast at (m6-struck-proof, TODO(m6-sticky-position))"));
        Assert.Equal(0, LinesWith(r, "self-defence: no attacker is in range with a clear line"));
        Assert.Equal(1, scenario.Defended);
    }

    [Fact]
    public void A_proven_attacker_the_shot_rule_drops_loses_its_proof_and_she_does_not_cast_at_it_again_while_the_drop_stands()
    {
        // Live 2026-11-12 13:00 to 13:07 UTC: the proof and the close-attacker rule kept her casting at a tusker no bolt reached.
        var r = new Rig(fight: new FightOptions { DefendForgetSeconds = 30 });
        r.Add(TuskerGuid, TuskerSlave, OtherFloorCell, 90, hitsToKill: 100); // out of range: nothing else to cast at
        r.Add(VirindiGuid, Virindi, FloorCell, 22, "Virindi", hitsToKill: 100); // 2 m off by its record, behind a ledge
        r.Script(Shot.NoProjectile, Shot.NoProjectile, Shot.NoProjectile, Shot.NoProjectile, Shot.NoProjectile, Shot.NoProjectile);
        var walk = new FakeWalk { BlockedCell = FloorCell, KeepBlockedCell = true, TicksPerLeg = 3 };
        var scenario = ToTheFight(r, r.Scenario(walk));

        for (var i = 0; i < 200; i++)
        {
            if (i % 8 == 0)
            {
                MeleeHit(r); // it keeps striking
            }

            r.Frame(scenario);
        }

        Assert.Equal(2, r.CastTargets.Count(t => t == VirindiGuid)); // the two wasted casts, and no third while the drop stands
        Assert.Equal(1, LinesWith(r, "shot drop: 0x80002001 (Virindi) is left alone for 120 s: 2 casts in a row wasted"));
        Assert.Equal(1, LinesWith(r, "self-defence: the struck proof of Virindi 0x80002001 stands down: the shot rule dropped it"));
        Assert.Equal(1, scenario.StruckProofsVoid);
        Assert.Equal(0, LinesWith(r, "is back: it stands"));
    }

    [Fact]
    public void M6_14_an_attacker_at_her_with_something_between_them_is_answered_by_a_sidestep_to_a_point_with_a_line_not_the_tours_leg()
    {
        // Live 2026-11-12 13:00 UTC, the Cave: a tusker on the other side of a torch. A spell line names the attacker here, so no struck proof stands and the line test decides, as it does once the shot rule has dropped it.
        var r = new Rig();
        r.Add(TuskerGuid, TuskerSlave, OtherFloorCell, 45);
        var virindi = r.Add(VirindiGuid, Virindi, FloorCell, 23, "Virindi"); // 3 m off, something between them from where she stands
        var walk = new FakeWalk { BlockedCell = FloorCell, TicksPerLeg = 3, TraceCells = [Den, OtherFloorCell] }; // no room middle is offered: the walk toward it finds no spot
        walk.ClearFromCells.Add(OtherFloorCell); // from the cell a step to the side ends in, the line is clear
        var scenario = ToTheFight(r, r.Scenario(walk));

        r.R.Raw(AceWireWriters.ServerMessage("Virindi scorches you for 12 points with Flame Bolt.", 7));
        for (var i = 0; i < 400 && !virindi.Dead; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal(1, scenario.Sidesteps);
        Assert.Equal(0, scenario.StruckProofs);
        var spot = Assert.Single(walk.Spots);
        Assert.Equal(OtherFloorCell, spot.CellId);
        Assert.Equal(2f, System.Numerics.Vector3.Distance(spot.Origin, new System.Numerics.Vector3(PlayerX, 20, 30)), 3);
        Assert.Equal(1, LinesWith(r, "self-defence: a sidestep for a line to Virindi 0x80002001, 3 m off with something between them: to 0x010502E1 [102 20 30], 2 m to the side"));
        Assert.Equal(1, LinesWith(r, "walking: purpose=self-defence (a sidestep for a line to Virindi 0x80002001"));
        Assert.Equal(0, LinesWith(r, "self-defence: no attacker is in range with a clear line")); // not the tour's leg
        Assert.True(virindi.Dead);
    }

    [Fact]
    public void M6_14_an_attacker_past_the_sidesteps_reach_falls_back_to_the_tours_leg_and_says_why()
    {
        var r = new Rig();
        r.Add(TuskerGuid, TuskerSlave, OtherFloorCell, 45);
        r.Add(VirindiGuid, Virindi, FloorCell, 30, "Virindi"); // 10 m off: past the sidestep's reach
        var walk = new FakeWalk { BlockedCell = FloorCell, TicksPerLeg = 4, TraceCells = [Den, OtherFloorCell, FloorCell] };
        var scenario = ToTheFight(r, r.Scenario(walk));

        MeleeHit(r);
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(0, scenario.Sidesteps);
        Assert.Equal(1, LinesWith(r, "; no sidestep: it stands 10 m off, past the sidestep's reach 6 m"));
        Assert.Equal(1, LinesWith(r, "self-defence: no attacker is in range with a clear line"));
    }

    [Fact]
    public void A_blow_from_farther_than_the_proofs_reach_proves_nothing_and_the_walk_toward_the_attacker_stands()
    {
        var r = new Rig();
        r.Add(TuskerGuid, TuskerSlave, OtherFloorCell, 45);
        var virindi = r.Add(VirindiGuid, Virindi, FloorCell, 20 + (float)FightScenario.StruckProofMetres + 1, "Virindi"); // an archer's distance: its arrow says nothing of where it stands
        var walk = new FakeWalk { BlockedCell = FloorCell, TicksPerLeg = 3 };
        var scenario = ToTheFight(r, r.Scenario(walk));

        MeleeHit(r);
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(0, scenario.StruckProofs);
        Assert.Equal(0, LinesWith(r, "m6-struck-proof"));
        Assert.Equal(1, LinesWith(r, "self-defence: no attacker is in range with a clear line")); // the walk, as before
        Assert.True(walk.Legs >= 1);
        Assert.True(virindi.Dead); // the leg brought the line
    }

    [Fact]
    public void A_spell_line_proves_nothing_about_where_its_caster_stands()
    {
        var r = new Rig();
        r.Add(TuskerGuid, TuskerSlave, OtherFloorCell, 45);
        r.Add(VirindiGuid, Virindi, FloorCell, 25, "Virindi");
        var walk = new FakeWalk { BlockedCell = FloorCell, TicksPerLeg = 3 };
        var scenario = ToTheFight(r, r.Scenario(walk));

        r.R.Raw(AceWireWriters.ServerMessage("Virindi scorches you for 12 points with Flame Bolt.", 7));
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(0, scenario.StruckProofs);
        Assert.True(walk.Legs >= 1); // no line by the test, no proof: the walk
    }
}
