using System.Numerics;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 7 AM: a bolt that stops short outdoors keeps the fight on its target (the stay rule), the pick's line test over the terrain, and the dungeon's old rule untouched. The real case is gate A's stretch 3 (13:37:49 UTC of
/// the climb log): the bolt left landblock 0xAAB3 at (3.8, 171.4) and flew 15 m/s along (9.58, -11.29, 2.39) at a Mite Snippet 68.8 m off; it stopped 2.95 s later, 44 m out.
/// </summary>
public sealed partial class FightScenarioTests
{
    private const uint LiveBlock = 0xAAB30000;

    private static CellFrame LiveFrame(uint cell, float x, float y, float z) => new(cell, new AcFrame(new Vector3(x, y, z), Quaternion.Identity));

    [Fact]
    public void The_live_wall_hit_at_68_m_steps_half_the_way_toward_the_target_and_the_second_step_goes_to_the_side_too()
    {
        var here = LiveFrame(LiveBlock | 8, 3.8f, 171.4f, 100.8f); // the bolt's origin, cell (0, 7)
        var target = LiveFrame(LiveBlock | 11, 47.7f, 119.6f, 112.0f); // origin plus 68.8 m along the bolt's velocity, cell (1, 4)
        var flat = Math.Sqrt(((47.7 - 3.8) * (47.7 - 3.8)) + ((119.6 - 171.4) * (119.6 - 171.4)));

        var first = FightScenario.WallStaySpot(in here, in target, 1)!.Value;
        var second = FightScenario.WallStaySpot(in here, in target, 2)!.Value;

        var toFirst = Math.Sqrt(Math.Pow(first.Origin.X - 3.8, 2) + Math.Pow(first.Origin.Y - 171.4, 2));
        Assert.Equal(flat / 2, toFirst, 0.5); // half of 68 m
        Assert.Equal(0xAAB3u, first.CellId >> 16);
        Assert.True(first.IsOutdoor);
        var offLine = Math.Abs(((second.Origin.X - first.Origin.X) * (119.6 - 171.4)) - ((second.Origin.Y - first.Origin.Y) * (47.7 - 3.8))) / flat; // second's distance from the first's spot across the line
        Assert.Equal(FightScenario.WallStaySideMetres, offLine, 0.5);
    }

    [Fact]
    public void The_stay_step_never_goes_inside_the_stand_off_and_a_target_inside_it_has_no_straight_step()
    {
        var here = LiveFrame(LiveBlock | 8, 10f, 100f, 100f);
        var near = LiveFrame(LiveBlock | 8, 10f, 112f, 100f); // 12 m: room 2 m
        var onTop = LiveFrame(LiveBlock | 8, 10f, 105f, 100f); // 5 m: inside the stand-off

        var step = FightScenario.WallStaySpot(in here, in near, 1)!.Value;

        Assert.Equal(2.0, step.Origin.Y - 100f, 0.1);
        Assert.Null(FightScenario.WallStaySpot(in here, in onTop, 1));
        Assert.NotNull(FightScenario.WallStaySpot(in here, in onTop, 2)); // the side step is still a move
        Assert.Null(FightScenario.WallStaySpot(LiveFrame(0x01050303, 0, 0, 0), in near, 1)); // indoors: no outdoor spot
    }

    [Fact]
    public void Outdoors_a_wall_hit_walks_toward_the_same_target_and_shoots_again_and_the_tour_is_not_taken()
    {
        var r = BeltRig(level: 5);
        var scout = r.Add(ScoutGuid, BanderlingScout, BeltCell, 45, "Banderling Scout"); // 25 m, inside the cast range
        r.Script(Shot.Wall);
        var walk = new FakeWalk();
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(scout.Dead);
        Assert.Equal((1, 1), (scenario.Tally.WallHits, walk.Spots.Count)); // one step to a spot, no tour leg
        Assert.Equal(1, walk.Legs);
        Assert.Equal(1, LinesWith(r, "wall stay: wall hit 1 of 3 on 0x80003001 (Banderling Scout)"));
        Assert.Equal(0, LinesWith(r, "walking: a bolt hit a wall"));
        Assert.All(r.CastTargets, t => Assert.Equal(ScoutGuid, t));
        Assert.Empty(scenario.WallBlocked);
    }

    private sealed class BlockedShots(uint blocked) : Lifestone.Client.Session.Casting.IShotCheck
    {
        public Lifestone.Client.Session.Casting.ShotVerdict Check(Lifestone.Client.Session.Contracts.IGameSessionView session, uint spellId, uint target) =>
            new(target == blocked ? Lifestone.Client.Session.Casting.ShotAnswer.Blocked : Lifestone.Client.Session.Casting.ShotAnswer.Clear, target == blocked ? "a ledge" : "open air");
    }

    /// <summary>M6 8 L: the case that escaped the wall rule: a target under a ledge ends each cast with no bolt the tracker takes (the owner's 6 or 7 wasted casts), and the climb's fight drops it at the same two as the quest station's.</summary>
    [Fact]
    public void Two_casts_with_no_bolt_drop_the_target_by_name_and_the_pick_takes_the_next()
    {
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40, "Drudge Skulker");
        r.Add(0x80001002, TuskerSlave, 0x01050303, 44);
        r.Script(Shot.NoProjectile, Shot.NoProjectile);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(2, r.CastTargets.Count(t => t == 0x80001001));
        Assert.Equal(1, LinesWith(r, "shot drop: 0x80001001 (Drudge Skulker) is left alone for 120 s: 2 casts in a row wasted"));
        Assert.Equal(2, scenario.ShotTally.WastedCasts);
        Assert.Contains(r.Log.Lines, l => l.Contains("wasted_casts=2 shots_blocked=0 shot_disagreements=0", StringComparison.Ordinal));
    }

    /// <summary>M6 8 L: a target that takes damage is kept: a landed bolt between two with no bolt starts the count again.</summary>
    [Fact]
    public void A_target_hit_between_two_casts_with_no_bolt_is_not_dropped()
    {
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        r.Script(Shot.NoProjectile, Shot.Normal, Shot.NoProjectile);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(1, scenario.Tally.Kills);
        Assert.Equal(0, LinesWith(r, "shot drop:"));
    }

    /// <summary>M6 8 L: the shot is checked before it is paid for: in a dungeon a blocked target gets no cast at all and the pick takes the next.</summary>
    [Fact]
    public void A_blocked_shot_in_a_dungeon_is_not_cast_and_the_target_is_dropped()
    {
        var r = new Rig();
        r.R.Api.UseShots(new BlockedShots(0x80001001));
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        r.Add(0x80001002, TuskerSlave, 0x01050303, 44);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.DoesNotContain(0x80001001u, r.CastTargets);
        Assert.Equal(1, scenario.ShotTally.ShotsBlocked);
        Assert.Equal(1, LinesWith(r, "the shot is blocked: a ledge"));
    }

    /// <summary>M6 8 L: outdoors a blocked shot takes the stay rule's step toward a better line before it costs anything.</summary>
    [Fact]
    public void Outdoors_a_blocked_shot_steps_toward_a_better_line_and_casts_nothing_from_here()
    {
        var r = BeltRig(level: 5);
        r.R.Api.UseShots(new BlockedShots(ScoutGuid));
        r.Add(ScoutGuid, BanderlingScout, BeltCell, 45, "Banderling Scout");
        var walk = new FakeWalk();
        var scenario = r.Scenario(walk);

        r.Run(scenario);

        Assert.DoesNotContain(ScoutGuid, r.CastTargets);
        Assert.True(walk.Spots.Count >= 1, string.Join(" | ", r.Log.Lines.TakeLast(8)));
        Assert.True(scenario.ShotTally.ShotsBlocked >= 1);
    }

    [Fact]
    public void Three_wall_hits_on_one_target_block_it_for_120_s_then_the_pick_takes_it_again()
    {
        var r = BeltRig(level: 5);
        var scout = r.Add(ScoutGuid, BanderlingScout, BeltCell, 45, "Banderling Scout");
        r.Script(Shot.Wall, Shot.Wall, Shot.Wall);
        var walk = new FakeWalk();
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(scout.Dead);
        Assert.Equal(3, scenario.Tally.WallHits);
        Assert.Equal(2, walk.Spots.Count); // hits 1 and 2 stepped; the third blocked
        Assert.True(walk.Legs >= 3, "the blocked target left the pick no one: the tour walked while it waited");
        Assert.Equal(1, LinesWith(r, "wall hit 3 of 3 on 0x80003001 (Banderling Scout): it is blocked for 120 s"));
        Assert.Equal(1, LinesWith(r, "wall stay: 0x80003001 is back in the pick"));
        Assert.True(r.Now >= 120, "it was shot at only after its block");
    }

    [Fact]
    public void A_dungeons_wall_hit_still_walks_the_tour_and_never_says_wall_stay()
    {
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        r.Script(Shot.Wall);
        var walk = new FakeWalk();
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal((1, 0), (walk.Legs, walk.Spots.Count));
        Assert.Equal(0, LinesWith(r, "wall stay:"));
    }

    [Fact]
    public void The_outdoor_line_test_sees_a_hill_between_the_two_and_a_flat_field_is_clear()
    {
        var from = LiveFrame(LiveBlock | 8, 3.8f, 171.4f, 100.8f);
        var to = LiveFrame(LiveBlock | 11, 47.7f, 119.6f, 112.0f);
        float? Flat(uint cell, float x, float y) => 100.8f;
        float? Hill(uint cell, float x, float y) => Math.Abs(x - 26f) < 4f ? 112.5f : 100.8f; // a rise across the middle, over both ends' bolt height
        float? Unread(uint cell, float x, float y) => null;

        Assert.True(OutdoorFightWalk.ClearOverTerrain(Flat, in from, in to));
        Assert.False(OutdoorFightWalk.ClearOverTerrain(Hill, in from, in to));
        Assert.True(OutdoorFightWalk.ClearOverTerrain(Unread, in from, in to)); // no terrain read is no evidence of a wall
        Assert.True(OutdoorFightWalk.ClearOverTerrain(Hill, in from, LiveFrame(LiveBlock | 8, 5f, 172f, 100.8f))); // 3 m apart: both ends' own ground
        Assert.True(OutdoorFightWalk.ClearOverTerrain(Hill, LiveFrame(0x01050303, 0, 0, 0), in to)); // an indoor end
    }
}
