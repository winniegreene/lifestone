using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 6 slice B2, the fight's users of the walk seam (<see cref="IFightWalk.BeginLegTo"/>, <see cref="IFightWalk.CellsOnLine"/>, <see cref="IFightWalk.FloorSpots"/>). <c>m5-defend-walk-to</c>: an attacker out of range or line
/// is walked toward, to the spot on the line to it with range and a line, at most <see cref="FightScenario.MaxDefenceWalksTo"/> legs; a refusal, no spot or the bound spent fall back to the tour's next leg, named.
/// <c>m5-attacker-reach</c>: the reach stands in as the cast range, the TODO named once. <c>m5-defend-loot-first</c>: a hit while a corpse is sought or looted is answered (the pick-ups kept, the open view closed and its close read
/// back). <c>m5-loot-walk-for-los</c>: a corpse with no line is walked to a floor spot with one, once, before it is left. Over the same scripted ACE and walk as the other fight tests; nothing here needs the dats or a server.
/// </summary>
public sealed partial class FightScenarioTests
{
    private static CellFrame SpotAt(uint cell, float y) => new(cell, new AcFrame(new Vector3(PlayerX, y, 30), Quaternion.Identity));

    [Fact]
    public void An_attacker_with_no_line_is_walked_toward_to_the_spot_on_the_line_with_range_and_a_line_and_then_cast_at()
    {
        var r = new Rig();
        r.Add(TuskerGuid, TuskerSlave, OtherFloorCell, 45);
        var virindi = r.Add(VirindiGuid, Virindi, FloorCell, 30, "Virindi"); // 10 m off, behind a wall from where the mage stands
        var spot = SpotAt(OtherFloorCell, 26); // on the line, 4 m from it, round the wall
        var walk = new FakeWalk { BlockedCell = FloorCell, TicksPerLeg = 3, TraceCells = [Den, OtherFloorCell, FloorCell] };
        walk.Floor.Add(SpotAt(Den, 22)); // the mage's own room: in range, but no line from there
        walk.Floor.Add(spot);
        walk.ClearFromCells.Add(OtherFloorCell);
        var scenario = ToTheFight(r, r.Scenario(walk));

        MeleeHit(r);
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal([spot], walk.Spots);
        Assert.Equal(1, LinesWith(r, "self-defence: a walk toward Virindi 0x80002001 at 0x01050303, 10 m off with no shot from here: to 0x010502E1 [100 26 30], 6 m off on the line to it, 4 m from it with a clear line (the cast range 30 m; within its reach, the mage's cast range 30 m stands in, TODO(m5-attacker-reach); leg 1 of 2)"));
        Assert.Equal(1, LinesWith(r, "walking: purpose=self-defence (a walk toward Virindi 0x80002001"));
        Assert.Equal(1, LinesWith(r, "self-defence: TODO(m5-attacker-reach): the reach of Virindi 0x80002001 (heard by damage notice) is not read"));
        Assert.Equal(0, LinesWith(r, "self-defence: no attacker is in range with a clear line")); // not the tour's leg
        Assert.True(virindi.Dead);
        Assert.Equal(VirindiGuid, r.CastTargets[0]);
        Assert.Equal(1, scenario.Defended);
    }

    [Fact]
    public void The_walks_toward_one_attacker_are_bounded_then_the_tours_leg_stands_named_once()
    {
        var r = new Rig(fight: new FightOptions { DefendForgetSeconds = 30 });
        r.Add(TuskerGuid, TuskerSlave, OtherFloorCell, 90, hitsToKill: 100);
        r.Add(VirindiGuid, Virindi, FloorCell, 60, "Virindi", hitsToKill: 100); // 40 m off: out of the 30 m cast range, the line clear
        var near = SpotAt(OtherFloorCell, 35); // 25 m from it: in range
        var walk = new FakeWalk { TicksPerLeg = 2, TraceCells = [Den, OtherFloorCell, FloorCell] };
        walk.Floor.Add(SpotAt(OtherFloorCell, 25)); // 35 m from it: out of range, passed over
        walk.Floor.Add(near);
        var scenario = ToTheFight(r, r.Scenario(walk));

        MeleeHit(r);
        for (var i = 0; i < 40; i++)
        {
            if (i % 8 == 0)
            {
                MeleeHit(r); // it keeps shooting; the fake walk never brings the mage nearer
            }

            r.Frame(scenario);
        }

        Assert.Equal([near, near], walk.Spots);
        Assert.Equal(1, LinesWith(r, "self-defence: a walk toward Virindi 0x80002001 at 0x01050303, 40 m off with no shot from here: to 0x010502E1 [100 35 30], 15 m off on the line to it, 25 m from it with a clear line (the cast range 30 m; within its reach, the mage's cast range 30 m stands in, TODO(m5-attacker-reach); leg 1 of 2)"));
        Assert.Equal(2, LinesWith(r, "leg 2 of 2)")); // the decision and its walking line
        Assert.Equal(1, LinesWith(r, "self-defence: the walk toward Virindi 0x80002001 falls back to the tour's next leg (as before m5-defend-walk-to): 2 leg(s) toward it brought no shot (the bound 2, FightScenario.MaxDefenceWalksTo)"));
        Assert.True(walk.Legs > walk.Spots.Count); // the tour's legs after the bound
        Assert.DoesNotContain(r.CastTargets, t => t == VirindiGuid);
    }

    [Theory]
    [InlineData("untraced", "this walk cannot trace the line to it (CellsOnLine: not traced)")]
    [InlineData("no-spots", "this walk offers no floor spots (FloorSpots: none; TODO(m5-defend-step-spots))")]
    [InlineData("no-line", "no floor spot in the 3 cell(s) on the line to it is within the cast range 30 m with a clear line (1 line(s) swept)")]
    public void A_walk_toward_an_attacker_that_finds_no_spot_falls_back_to_the_tours_leg_named(string walkCase, string why)
    {
        var r = new Rig();
        r.Add(TuskerGuid, TuskerSlave, OtherFloorCell, 45);
        var virindi = r.Add(VirindiGuid, Virindi, FloorCell, 30, "Virindi");
        var walk = new FakeWalk { BlockedCell = FloorCell, TicksPerLeg = 4 };
        if (walkCase != "untraced")
        {
            walk.TraceCells = [Den, OtherFloorCell, FloorCell];
        }

        if (walkCase == "no-line")
        {
            walk.Floor.Add(SpotAt(OtherFloorCell, 26)); // on the line and in range, but the wall hides it from there too
        }

        var scenario = ToTheFight(r, r.Scenario(walk));

        MeleeHit(r);
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Empty(walk.Spots);
        Assert.Equal(1, LinesWith(r, "self-defence: the walk toward Virindi 0x80002001 falls back to the tour's next leg (as before m5-defend-walk-to): " + why));
        Assert.Equal(1, LinesWith(r, "self-defence: no attacker is in range with a clear line")); // today's walk, the tour's next leg
        Assert.True(virindi.Dead); // the tour's leg arrived and brought the line, as before
        Assert.Equal(1, scenario.Defended);
    }

    [Fact]
    public void An_attack_while_a_corpse_is_looted_puts_it_off_keeps_the_pick_ups_closes_it_read_back_and_the_fight_answers()
    {
        var r = new Rig(fight: LootRetryFight(kills: 3, defendForget: 5)) { Corpses = true };
        StockCorpse(r);
        var a = r.Add(SlaveA, TuskerSlave, Den, 30);
        r.Add(VirindiGuid, Virindi, Den, 40, "Virindi", hitsToKill: 100_000); // 20 m off, 10 m from the corpse: not beside it, in range with a line
        var scenario = r.Scenario(new FakeWalk());
        var hit = false;

        for (var i = 0; i < 400 && !hit; i++)
        {
            FrameWith(r, scenario, () =>
            {
                if (a.Dead && !hit && r.Picked.Count == 1)
                {
                    hit = true;
                    MeleeHit(r); // the Virindi hits the mage while the first pick-up is confirmed
                }
            });
        }

        for (var i = 0; i < 8; i++)
        {
            r.Frame(scenario);
        }

        Assert.True(hit);
        Assert.Equal(1, LinesWith(r, "loot: the corpse 0x80001101 was being looted: kept 1 of "));
        Assert.Equal(1, LinesWith(r, "is put off: Virindi 0x80002001 attacked the mage 0 s ago while the corpse was being looted"));
        Assert.Equal(1, LinesWith(r, "loot: the open corpse 0x80001101 is closed first (its view was open; the close is sent, the view is read again)"));
        Assert.Equal(1, LinesWith(r, "loot: the corpse 0x80001101 is closed (the view confirms it)"));
        Assert.Equal([CorpseA], r.Closed);
        Assert.Single(r.Picked); // nothing more is picked up while the fight answers
        Assert.Contains(VirindiGuid, r.CastTargets);
        Assert.Contains(" loot_left_aggro=1 ", scenario.LootFields(), StringComparison.Ordinal);
        Assert.Matches(@"^loot=on looted=0 kept=1 ", scenario.LootFields());
    }

    [Fact]
    public void An_attack_before_the_corpse_appears_is_answered_at_once_and_the_corpse_is_put_off_when_it_does()
    {
        var r = new Rig(fight: LootRetryFight(kills: 3, defendForget: 5)) { Corpses = true, DeathDelay = 2, CorpseAtDeletion = true, HealthZeroAtKill = true }; // the kill is settled before the body is gone, so its corpse is sought before it is made
        var a = r.Add(SlaveA, TuskerSlave, Den, 30);
        r.Add(VirindiGuid, Virindi, Den, 40, "Virindi", hitsToKill: 100_000);
        var scenario = r.Scenario(new FakeWalk());
        var hit = false;
        var attackAt = double.NaN;
        var castsBefore = 0;

        for (var i = 0; i < 400 && LinesWith(r, "is put off:") == 0; i++)
        {
            FrameWith(r, scenario, () =>
            {
                if (a.Dead && !hit)
                {
                    hit = true;
                    attackAt = r.Now;
                    castsBefore = r.CastTargets.Count;
                    MeleeHit(r); // the Virindi hits the mage in the tick the tusker dies, before its corpse is made
                }
            });
        }

        Assert.True(hit);
        Assert.Equal(1, LinesWith(r, "loot: the corpse of 'Tusker Slave' has not appeared yet and Virindi 0x80002001 attacked the mage"));
        Assert.Contains(VirindiGuid, r.CastTargets.Skip(castsBefore)); // answered while the corpse was waited for
        Assert.Equal(1, LinesWith(r, "loot: the corpse 0x80001101 of 'Tusker Slave' is put off: Virindi 0x80002001 attacked the mage"));
        Assert.Equal(1, LinesWith(r, "while the corpse was being sought"));
        Assert.Empty(r.Opened);
        Assert.Equal(0, scenario.LootCorpsesLeft); // put off, still queued
    }

    private static FightOptions LosLootFight() => new() { Kills = 1, Seconds = 1000, Loot = true, LootThresholds = RigLoot, LootAggroMetres = 0 };

    [Fact]
    public void A_corpse_with_no_line_is_walked_to_a_floor_spot_with_one_once_and_then_opened()
    {
        var r = new Rig(fight: LosLootFight()) { Corpses = true };
        StockCorpse(r);
        var first = r.Add(SlaveA, TuskerSlave, Den, 30);
        var spot = SpotAt(OtherFloorCell, 27); // 7 m off, 3 m from the corpse, round the wall
        var walk = new FakeWalk { TicksPerLeg = 2 };
        walk.Floor.Add(SpotAt(FloorCell, 50)); // 20 m from the corpse: beyond the loot radius, never swept
        walk.Floor.Add(spot);
        walk.ClearFromCells.Add(OtherFloorCell);
        var scenario = r.Scenario(walk);
        for (var i = 0; i < 4000 && !first.Dead; i++)
        {
            Assert.Equal(ScenarioStatus.Running, r.Frame(scenario));
        }

        walk.BlockedCell = Den; // the corpse lies behind a wall from where the mage stands
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal([spot], walk.Spots);
        Assert.Equal(1, LinesWith(r, "loot: the corpse 0x80001101 of 'Tusker Slave' is 10 m away with no clear line of sight to it (los=no): the fight's own walk goes first (once) to 0x010502E1 [100 27 30], 7 m off, 3 m from the corpse with a clear line to it (1 spot(s) swept of 1 within the loot radius 15 m)"));
        Assert.Equal(1, LinesWith(r, "walking: purpose=loot (the corpse 0x80001101 of 'Tusker Slave': 0x010502E1 "));
        Assert.Equal(1, LinesWith(r, "the corpse has no clear line of sight from where the mage stood, so the fight's own walk goes to a spot with one (m5-loot-walk-for-los)"));
        Assert.Equal(1, LinesWith(r, "loot: the walk for a line to the corpse 0x80001101 of 'Tusker Slave' ended; it is looked at again"));
        Assert.Equal([CorpseA], r.Opened);
        Assert.Equal(0, scenario.LootCorpsesLeft);
    }

    [Fact]
    public void A_corpse_still_out_of_sight_after_the_walk_for_a_line_is_left_by_name()
    {
        var r = new Rig(fight: LosLootFight()) { Corpses = true };
        StockCorpse(r);
        var first = r.Add(SlaveA, TuskerSlave, Den, 30);
        var walk = new FakeWalk { TicksPerLeg = 2, KeepBlockedCell = true }; // the spot's line is clear by the sweep, but the walk there never brings one
        walk.Floor.Add(SpotAt(OtherFloorCell, 27));
        walk.ClearFromCells.Add(OtherFloorCell);
        var scenario = r.Scenario(walk);
        for (var i = 0; i < 4000 && !first.Dead; i++)
        {
            Assert.Equal(ScenarioStatus.Running, r.Frame(scenario));
        }

        walk.BlockedCell = Den;
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Single(walk.Spots); // once
        Assert.Empty(r.Opened);
        Assert.Equal(1, LinesWith(r, "with no clear line of sight to it (los=no); the walk for a line went, and it still has none; left, not walked to by ACE"));
        Assert.Matches(@"loot_skipped_unreached=1 loot_skipped_hostile=0", scenario.LootFields());
        Assert.Equal(1, scenario.LootCorpsesLeft);
    }
}
