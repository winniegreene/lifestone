using System.Globalization;
using System.Numerics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// M6 14 pit-hold (live 2026-11-13, the Tusker Abode, <c>cache/m6/14/live/abode5</c>): the profile pits' lips, where the edge slide holds a body in the pit's floorless cell (the climber stood at 0x5C4502A9 [125.1 -60.2 0.0], 0.1 to
/// 0.2 m past the west brink, from 14:53:51 to 15:04:39 UTC); the running jump's flight under load (retail's load modifier past 100%), and the loot's load line held to 100% on a ground with jumps. No dats.
/// </summary>
public sealed class PitHoldTests(ITestOutputHelper output)
{
    /// <summary>Qwinnie at level 47 with the buff bot's level VII set (PitJumpDatTests: Run 103, Jump 178, 11.54 m).</summary>
    private const int BuffedRun = 103;

    private const int BuffedJump = 178;

    [Theory]
    [InlineData("tusker-abode", 0x5C450000u)]
    [InlineData("tusker-holding", 0x5B480000u)]
    public void The_live_spot_stands_on_the_west_lip_and_a_fall_or_the_middle_on_no_lip(string key, uint landblock)
    {
        var pit = Assert.Single(GroundCatalog.Get(key).Jumps);
        Assert.Equal(landblock | 0x2A9, pit.PitCell);
        Assert.Equal(landblock | 0x2A0, pit.LipSideOf(new Vector3(125.15f, -60.2f, 0.0f))!.Cell); // the live spot: 0.15 m past the west brink at x 125, at the floor's height
        Assert.Equal(landblock | 0x2A0, pit.LipSideOf(new Vector3(125.9f, -57f, 0.3f))!.Cell); // the lip's whole width, within the step-up
        Assert.Equal(landblock | 0x2B9, pit.LipSideOf(new Vector3(134.8f, -60f, 0.005f))!.Cell); // the east lip
        Assert.Null(pit.LipSideOf(new Vector3(125.2f, -60f, -3f))); // falling: under the floor by more than the step-up
        Assert.Null(pit.LipSideOf(new Vector3(130f, -60f, 0f))); // over the middle (a body in flight)
        Assert.Null(pit.LipSideOf(new Vector3(128f, -60f, -18f))); // on the pit's floor
    }

    [Fact]
    public void The_room_planner_finds_the_lip_only_in_a_profile_pit_s_cell()
    {
        var abode = GroundCatalog.Get("tusker-abode");
        var pit = Assert.Single(abode.Jumps);
        var lip = PlannerLip(abode.Jumps, pit.PitCell, new Vector3(125.15f, -60.2f, 0f));
        Assert.Equal((pit, 0x5C4502A0u), (lip!.Value.Pit, lip.Value.Side.Cell));
        Assert.Null(PlannerLip(abode.Jumps, 0x5C4502A0u, new Vector3(125.15f, -60.2f, 0f))); // not the pit's cell
        Assert.Null(PlannerLip([], pit.PitCell, new Vector3(125.15f, -60.2f, 0f))); // a graph with no jumps knows no lip
    }

    /// <summary>
    /// The flight under load: retail's load modifier is 1 up to a load of 1.0 and falls as 2 - load past it, on the run rate and the jump's height both; her 11.54 m holds to 100% and falls under the Abode's need of 10.75 m at about
    /// 107% (125%, the outskirts' loot line, flies 8.79 m; 150%, the Abode's burden trip line, 6.19 m: both fall into the pit).
    /// </summary>
    [Fact]
    public void The_reach_holds_to_a_load_of_100_percent_and_falls_under_the_need_by_107()
    {
        var need = Assert.Single(GroundCatalog.Get("tusker-abode").Jumps).NeedMetres;
        var bare = JumpReach.FlightMetres(new OfflinePlayerWeenie(BuffedRun, BuffedJump))!.Value;
        foreach (var load in new[] { 0f, 0.5f, 0.92f, 0.9947f, 1.0f })
        {
            Assert.Equal(bare, JumpReach.FlightMetres(BuffedRun, BuffedJump, load), 4); // 0.92: her load after the 14:00 trip; 0.9947: 17,158 of 17,250 at 14:57
        }

        Assert.Equal(11.54f, bare, 2);
        var crossing = 1.0f;
        while (JumpReach.FlightMetres(BuffedRun, BuffedJump, crossing + 0.001f) >= need)
        {
            crossing += 0.001f;
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Run {BuffedRun} Jump {BuffedJump}: {bare:0.00} m to a load of 1.0; under the need {need:0.00} m past {crossing:0.000}; 1.25 flies {JumpReach.FlightMetres(BuffedRun, BuffedJump, 1.25f):0.00} m, 1.5 {JumpReach.FlightMetres(BuffedRun, BuffedJump, 1.5f):0.00} m"));
        Assert.InRange(crossing, 1.065f, 1.075f);
        Assert.True(JumpReach.FlightMetres(BuffedRun, BuffedJump, 1.25f) < need);
        Assert.True(JumpReach.FlightMetres(BuffedRun, BuffedJump, 1.5f) < need);
    }

    [Theory]
    [InlineData(0, true, 100)] // no limit: the jump line
    [InlineData(150, true, 100)]
    [InlineData(125, true, 100)]
    [InlineData(100, true, 100)]
    [InlineData(85, true, 85)] // the Holding's own, under it
    [InlineData(0, false, 0)] // no jump: the line as it is
    [InlineData(125, false, 125)]
    public void The_loot_line_is_held_to_100_percent_on_a_ground_with_jumps(int line, bool jumps, int held)
    {
        Assert.Equal(held, MageLootThresholds.JumpCapped(line, jumps));
    }

    [Theory]
    [InlineData("tusker-abode", 100)] // no loot section: the default line, the jump line
    [InlineData("tusker-holding", 85)] // its own loot.load_line_percent, under the jump line
    [InlineData("holtburg-outskirts", 125)] // no jump: its own line
    public void The_ground_s_loot_line_is_the_fight_s(string key, int line)
    {
        var ground = GroundCatalog.Get(key);
        Assert.Equal(line, MageLootThresholds.LoadLineOf(ground));
        Assert.Equal(line, new FightOptions { GroundForTest = ground }.EffectiveLootThresholds.LoadLinePercent);
    }

    [Fact]
    public void A_run_that_asks_for_a_higher_loot_line_is_still_held_to_the_jump_line_on_the_abode()
    {
        var options = new FightOptions { GroundForTest = GroundCatalog.Get("tusker-abode"), LootThresholds = new MageLootThresholds { LoadLinePercent = 150 } };
        Assert.Equal(MageLootThresholds.JumpLoadLinePercent, options.EffectiveLootThresholds.LoadLinePercent);
    }

    private static (PitJump Pit, PitJumpSide Side)? PlannerLip(IReadOnlyList<PitJump> jumps, uint cell, Vector3 at)
    {
        var none = Lifestone.Data.World.Cells.LandblockCellSet.FromCells(Lifestone.Data.World.Cells.CellStructureLibrary.FromStructures([]), new Lifestone.Data.Terrain.LandblockCoord(0x5C, 0x45), []);
        var planner = new CellRoutePlanner(none) { Jumps = jumps };
        return planner.LipOf(cell, at);
    }
}
