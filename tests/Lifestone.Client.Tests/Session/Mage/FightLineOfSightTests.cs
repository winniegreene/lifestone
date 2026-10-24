using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// The clear-line rule of the fight loop (M3 4.2 P4) on the Black Spawn Den's own collision from the dats: the sweep <c>GameFightWalk.HasClearLine</c> runs
/// (<c>PhysicsWorld.SweepViewer</c> at a bolt's height, 0.3 m) is blocked from the arrival plaza to the tuskers' plaza, which is where three bolts a run hit the walls, and clear within a plaza.
/// </summary>
public sealed class FightLineOfSightTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const float Radius = 0.3f;

    // ACE's ProjHeight of a 1.8 m body (the lift GameFightWalk applies to both ends).
    private const float Lift = 1.8f * (2f / 3f);

    private PhysicsWorld Den()
    {
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(fixture.Dats).Build(new LandblockCoord(1, 5)));
        return world;
    }

    private static CellFrame At(uint cell, float x, float y, float z) => new(cell, new AcFrame(new Vector3(x, y, z), Quaternion.Identity));

    private static bool Blocked(PhysicsWorld world, CellFrame from, CellFrame to)
    {
        var a = new CellFrame(from.CellId, from.Frame with { Origin = from.Frame.Origin + new Vector3(0, 0, Lift) });
        var b = new CellFrame(to.CellId, to.Frame with { Origin = to.Frame.Origin + new Vector3(0, 0, Lift) });
        return world.SweepViewer(in a, in b, Radius).Blocked;
    }

    [RequiresDatsFact]
    public void The_arrival_plaza_has_no_line_to_the_tuskers_plaza_and_a_plaza_sees_its_own_floor()
    {
        var world = Den();
        // The live legs' own numbers (cache/m3/4-1 legbsd log): the bolt left the arrival at (100.43, -208.92) toward the tuskers 44.2 m away; after the walk the player stood in 0x0383 with a tusker 2.9 m off.
        var arrival = At(0x01050375, 100.43f, -208.92f, 0.03f);
        var tuskers = At(0x01050383, 116.7f, -167.9f, 0.06f);
        Assert.True(Blocked(world, arrival, tuskers), "the straight line through the rock must be blocked");
        Assert.False(Blocked(world, At(0x01050383, 116.58f, -170.50f, 0.06f), At(0x01050383, 115.8f, -173.3f, 0.06f)), "within the plaza the line is clear");
    }

    [RequiresDatsFact]
    public void The_tour_cells_see_each_other_as_the_table_says()
    {
        var world = Den();
        var floor = DenRooms.FloorOf(LandblockCellSet.Load(new CellStructureLibrary(fixture.Dats), new LandblockCoord(1, 5)));
        var cells = new List<uint> { 0x01050375 };
        foreach (var low in DenWalk.Tour)
        {
            cells.Add(0x01050000u | low);
        }

        var planner = new RoomGraph(floor, DenRooms.MaxLean);
        var lines = new List<string>();
        foreach (var a in cells)
        {
            var seen = new List<string>();
            foreach (var b in cells)
            {
                if (a != b && planner.RoomFrame(a) is { } f && planner.RoomFrame(b) is { } t && !Blocked(world, f, t))
                {
                    seen.Add($"{b & 0xFFFF:X4}");
                }
            }

            lines.Add($"{a & 0xFFFF:X4}: {string.Join(' ', seen)}");
        }

        foreach (var l in lines)
        {
            output.WriteLine(l);
        }

        // Written once from the run (centre of each plaza's floor to the next's, a bolt's height, 0.3 m sphere): the arrival sees nothing, the tour cells see only their pair. A physics change that moves this is noticed.
        Assert.Equal(
            "0375: | 0383: | 0377: 036F | 036F: 0377 | 033F: 0338 | 0338: 033F | 0320: | 0303: | 0310: 030D | 030D: 0310 | 0325: | 02E2: 02E1 | 02E1: 02E2",
            string.Join(" | ", lines.Select(l => l.TrimEnd())));
    }

    /// <summary>The tour's room frames, longest pairs first (the picks the loop sweeps are creature-to-player lines of this length).</summary>
    private List<(uint A, uint B, CellFrame From, CellFrame To, float Length)> TourPairs()
    {
        var floor = DenRooms.FloorOf(LandblockCellSet.Load(new CellStructureLibrary(fixture.Dats), new LandblockCoord(1, 5)));
        var planner = new RoomGraph(floor, DenRooms.MaxLean);
        var cells = new List<uint> { 0x01050375 };
        foreach (var low in DenWalk.Tour)
        {
            cells.Add(0x01050000u | low);
        }

        var pairs = new List<(uint, uint, CellFrame, CellFrame, float)>();
        foreach (var a in cells)
        {
            foreach (var b in cells)
            {
                if (a < b && planner.RoomFrame(a) is { } f && planner.RoomFrame(b) is { } t)
                {
                    pairs.Add((a, b, f, t, (float)Lifestone.Client.Session.Tools.PositionAgreement.DistanceMetres(in f, in t)));
                }
            }
        }

        pairs.Sort((x, y) => y.Item5.CompareTo(x.Item5));
        return pairs;
    }

    /// <summary>One sweep's cost in milliseconds: the fastest of a few runs after a warm-up sweep (the first call pays the JIT and the cell cache, which a leg pays once, not per pick; the fastest run is the cost without the parallel test classes' CPU contention).</summary>
    private static double SweepMilliseconds(PhysicsWorld world, CellFrame from, CellFrame to)
    {
        const int Runs = 7;
        Blocked(world, from, to);
        var best = double.MaxValue;
        for (var i = 0; i < Runs; i++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Blocked(world, from, to);
            best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
        }

        return best;
    }

    [RequiresDatsFact]
    public void The_dens_six_longest_pick_sweeps_each_cost_under_2_ms_and_a_long_clear_line_reads_clear()
    {
        // 4.2 R5: a Choose runs up to MaxLinePicks sweeps in one tick (the fight window's frame budget is 75 ms). The tour's longest lines are all blocked by rock (the sweep ends at the first contact);
        // the dearest case is a clear line, which the sweep walks end to end in 0.3 m steps (Transition.CalcNumSteps, the viewer branch).
        var world = Den();
        var pairs = TourPairs();
        var worst = 0.0;
        foreach (var (a, b, from, to, length) in pairs.Take(FightScenario.MaxLinePicks))
        {
            var ms = SweepMilliseconds(world, from, to);
            output.WriteLine($"{a & 0xFFFF:X4}-{b & 0xFFFF:X4} {length:0.0} m blocked={Blocked(world, from, to)} {ms:0.000} ms");
            Assert.True(ms < 2.0, $"the pick sweep {a & 0xFFFF:X4}-{b & 0xFFFF:X4} ({length:0} m) cost {ms:0.000} ms (at most 2)");
            worst = Math.Max(worst, ms);
        }

        // The longest line of the tour that is clear: its length and cost are the per-metre figure a 44-72 m pick would multiply.
        var clear = pairs.Where(p => !Blocked(world, p.From, p.To)).OrderByDescending(p => p.Length).First();
        var clearMs = SweepMilliseconds(world, clear.From, clear.To);
        output.WriteLine($"clear {clear.A & 0xFFFF:X4}-{clear.B & 0xFFFF:X4} {clear.Length:0.0} m {clearMs:0.000} ms; worst blocked pick {worst:0.000} ms");
        Assert.True(clear.Length >= 20f, $"the longest clear line of the tour is {clear.Length:0.0} m (a line of at least 20 m reads clear)");
        Assert.True(clearMs < 2.0, $"the {clear.Length:0} m clear sweep cost {clearMs:0.000} ms (at most 2)");
    }
}
