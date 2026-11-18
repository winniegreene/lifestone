using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Grounds;
using Lifestone.Data.Animation;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Tests.Shared;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 6 B1 (<c>m5-loot-walk-back-walk</c>, <c>m5-loot-approach-trace</c>, <c>m5-defend-step-spots</c>): the seam table of <see cref="IFightWalk"/>'s <c>BeginLegTo</c>, <c>CellsOnLine</c> and <c>FloorSpots</c>: every live
/// implementation declares all three (or is on the named refusal list), a walk without a planner answers <c>no-planner:&lt;walk type&gt;</c> logged once, and the two walks B1 gave the calls (the headless host's here) behave.
/// </summary>
public sealed class FightWalkSeamTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    /// <summary>Walks that declare none of the three on purpose, each with why (the lead's contract with the slice that owns it).</summary>
    private static readonly Dictionary<string, string> NamedRefusals = new()
    {
    };

    private static readonly string[] Calls = [nameof(IFightWalk.BeginLegTo), nameof(IFightWalk.CellsOnLine), nameof(IFightWalk.FloorSpots)];

    [Fact]
    public void Every_live_fight_walk_declares_the_three_calls_or_is_on_the_named_refusal_list()
    {
        var walks = typeof(IFightWalk).Assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IFightWalk).IsAssignableFrom(t)).ToArray();
        Assert.Contains(walks, t => t.Name == "GroundFightWalk");
        Assert.Contains(walks, t => t.Name == "OutdoorFightWalk");
        Assert.Contains(walks, t => t.Name == "GroundWalk");
        Assert.Contains(walks, t => t.Name == "GameFightWalk");

        var missing = new List<string>();
        foreach (var walk in walks)
        {
            var map = walk.GetInterfaceMap(typeof(IFightWalk));
            foreach (var call in Calls)
            {
                var declared = Enumerable.Range(0, map.InterfaceMethods.Length)
                    .Where(i => map.InterfaceMethods[i].Name == call || map.InterfaceMethods[i].Name == "get_" + call)
                    .All(i => map.TargetMethods[i].DeclaringType == walk);
                if (!declared && !NamedRefusals.ContainsKey(walk.Name))
                {
                    missing.Add($"{walk.Name}.{call}");
                }
            }
        }

        Assert.Empty(missing);
        Assert.All(NamedRefusals.Keys, name => Assert.Contains(walks, t => t.Name == name)); // a stale entry fails
    }

    [Fact]
    public void A_walk_without_a_planner_answers_a_named_refusal_logged_once_per_instance()
    {
        var sink = new RecordingLogSink();
        FightWalkRefusals.Sink = sink;
        try
        {
            IFightWalk walk = new PlannerlessWalk();
            var cells = new List<uint> { 1 };

            Assert.False(walk.BeginLegTo(null!, 0, default, out var why));
            Assert.Equal("no-planner:PlannerlessWalk", why);
            Assert.False(walk.BeginLegTo(null!, 1, default, out _));
            Assert.Equal(LineTrace.NotTraced, walk.CellsOnLine(default, default, cells));
            Assert.Empty(cells);
            Assert.Empty(walk.FloorSpots);
            Assert.Empty(walk.FloorSpots);
            Assert.False(walk.AvoidSpot(default, 0, out why));
            Assert.Equal("no-planner:PlannerlessWalk", why);

            Assert.Equal(4, sink.Lines.Count(l => l.Contains("fight walk refusal:", StringComparison.Ordinal) && l.Contains("no-planner:PlannerlessWalk", StringComparison.Ordinal))); // one line a call, not a line a ask
            Assert.Equal(1, sink.Lines.Count(l => l.Contains("FloorSpots -> no-planner:PlannerlessWalk", StringComparison.Ordinal)));
            Assert.Contains("no-planner:PlannerlessWalk/BeginLegTo", FightWalkRefusals.Refused);

            IFightWalk other = new PlannerlessWalk(); // another instance logs again
            other.BeginLegTo(null!, 0, default, out _);
            Assert.Equal(2, sink.Lines.Count(l => l.Contains("BeginLegTo -> no-planner:PlannerlessWalk", StringComparison.Ordinal))); // only this walk type: the sink is process-wide, other classes' walks may add lines
        }
        finally
        {
            FightWalkRefusals.Sink = null;
        }
    }

    [Fact]
    public void The_kept_refusals_are_bounded_and_the_overflow_is_counted()
    {
        for (var n = 0; n < FightWalkRefusals.MaxKept + 5; n++)
        {
            IFightWalk walk = new PlannerlessWalk();
            walk.BeginLegTo(null!, 0, default, out _);
        }

        Assert.True(FightWalkRefusals.Refused.Count <= FightWalkRefusals.MaxKept);
        Assert.True(FightWalkRefusals.Overflowed >= 5);
    }

    [RequiresDatsFact]
    public void The_headless_hosts_ground_walk_answers_the_three_calls_over_the_room_graph()
    {
        using var host = new HeadlessJourneyRunnerHost(fixture.Dats, new RecordingLogSink());
        var walk = host.FightWalk(null!, DenGround.Profile);

        var spots = walk.FloorSpots;
        Assert.NotEmpty(spots);
        Assert.Same(spots, walk.FloorSpots); // built once
        Assert.All(spots, s => Assert.NotNull(host.GroundGraph(DenGround.Profile).RoomFrame(s.CellId)));

        var cells = new List<uint> { 7 };
        Assert.Equal(LineTrace.NotTraced, walk.CellsOnLine(spots[0], spots[^1], cells)); // no body yet: no physics to trace with
        Assert.Empty(cells);

        // BeginLegTo needs a session and a placed body (the host ticks the session first): the room-graph leg is the lead's loopback (J4 den walk), not a unit case.
        walk.Dispose();
    }

    private sealed class PlannerlessWalk : IFightWalk
    {
        public string? Failure => null;

        public void Prepare(IGameSessionView session, double now)
        {
        }

        public bool BeginLeg(IGameSessionView session, double now, out string description)
        {
            description = string.Empty;
            return false;
        }

        public FightWalkStatus Tick(IGameSessionView session, double now) => FightWalkStatus.Idle;

        public void Abandon()
        {
        }

        public void Dispose()
        {
        }
    }
}

public sealed partial class FightScenarioTests
{
    [Fact]
    public void The_outdoor_walk_runs_a_leg_to_a_spot_through_the_travel_seam_and_a_failed_one_ends_arrived_without_telling_the_tour()
    {
        var r = BeltRig();
        var cells = BeltGround.Outdoor!.Cells;
        var travel = new ScriptedTravel();
        using var walk = new OutdoorFightWalk(BeltGround, travel, (_, _, _) => 10f, r.Log);
        var goal = new CellFrame(cells[2].Cell, new AcFrame(new Vector3(30f, 40f, 11f), Quaternion.Identity));

        Assert.True(walk.BeginLegTo(r.View, 0, in goal, out var description));
        Assert.Equal(TravelPurpose.Cell, travel.Requests[0].Purpose);
        Assert.Equal(cells[2].Cell, travel.Requests[0].Destination.Location.Position.CellId);
        Assert.Equal(new Vector3(30f, 40f, 11f), travel.Requests[0].Destination.Location.Position.Local);
        Assert.Contains("to the spot in", description, StringComparison.Ordinal);
        Assert.Equal(cells[2].Cell, walk.LegGoalCell);
        Assert.Equal(FightWalkStatus.Arrived, walk.Tick(r.View, 1));
        Assert.Equal(-1, ((OutdoorTour)walk.Tour).Offered); // the tour was not asked: no offer made

        travel.Status = TravelStatus.Failed;
        Assert.True(walk.BeginLegTo(r.View, 2, in goal, out _));
        Assert.Equal(FightWalkStatus.Arrived, walk.Tick(r.View, 3)); // no recovery onto the tour
        Assert.Equal(0, walk.LegsRecovered);
        Assert.Equal(1, LinesWith(r, "failed (travel:run:scripted); ended Arrived without recovery onto the tour"));

        var indoor = new CellFrame(0x01050375, goal.Frame);
        Assert.False(walk.BeginLegTo(r.View, 4, in indoor, out var why));
        Assert.Contains("not an outdoor cell", why, StringComparison.Ordinal);
    }

    [Fact]
    public void The_outdoor_walk_lists_the_outdoor_cells_a_line_crosses_and_the_profiles_cells_as_floor_spots()
    {
        var r = BeltRig();
        var cells = BeltGround.Outdoor!.Cells;
        using var walk = new OutdoorFightWalk(BeltGround, new ScriptedTravel(), (cell, _, _) => cell == cells[0].Cell ? null : 10f, r.Log);
        const uint Block = 0xA5B00000;
        var from = new CellFrame(Block | 1, new AcFrame(new Vector3(12f, 12f, 10f), Quaternion.Identity)); // the cell at (0, 0)
        var to = new CellFrame(Block | 17, new AcFrame(new Vector3(60f, 12f, 10f), Quaternion.Identity)); // two cells further: low word 1 + 2 * 8

        var line = new List<uint>();
        Assert.Equal(LineTrace.Traced, walk.CellsOnLine(in from, in to, line));
        Assert.Equal([Block | 1, Block | 9, Block | 17], line);

        var otherBlock = new CellFrame(0xA4B00001, to.Frame);
        Assert.Equal(LineTrace.Stopped, walk.CellsOnLine(in from, in otherBlock, line)); // not known past the start
        Assert.Equal([Block | 1], line);
        Assert.Equal(LineTrace.Stopped, walk.CellsOnLine(new CellFrame(0x01050375, from.Frame), in to, line));

        var spots = walk.FloorSpots;
        Assert.Equal(cells.Count - 1, spots.Count); // the cell with no terrain height is left out
        Assert.DoesNotContain(spots, s => s.CellId == cells[0].Cell);
        Assert.All(spots, s => Assert.Equal(10f + OutdoorFightWalk.StandOffMetres, s.Origin.Z));
        Assert.Same(spots, walk.FloorSpots);
    }
}
