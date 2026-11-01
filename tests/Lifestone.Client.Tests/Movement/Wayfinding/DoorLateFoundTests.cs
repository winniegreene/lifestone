using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// M5 item 7, F2 ("open the door before meeting it"): a door the plan did not know (not yet in the object table, or its doorway not yet placeable) is found on the rest of the path as soon as the
/// door feed's doors or the path source's tiles change, and at the first stuck rung, and becomes a door step the runner stands off and Uses before contact. On the real dats (Exhaustive tier),
/// Holtburg's lifestone to Renald's shop through the front door 0x7A9B4015 (as <see cref="DoorLegTests"/>).
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class DoorLateFoundTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const double Fps = 60.0;
    private const uint HoltburgSetup = 0x020019FF;
    private const uint RenaldFrontRoomCell = 0xA9B4013F;
    private const uint FrontDoor = 0x7A9B4015;
    private static readonly Vector3 RenaldAt = new(139.23f, 18.33f, 94.01f);
    private static readonly Vector3 FrontDoorAt = new(132.56f, 17.115f, 94f);

    private DatSet Dats => fixture.Dats;

    private static CellFrame LifestoneStart() => new(0xA9B40019, new AcFrame(new Vector3(81.33f, 11.80f, 94.01f), Quaternion.Identity));

    private static NavGoal Renald() => NavGoal.ToObject(0x7A9B4021, new NavPoint(0xA9B40000, RenaldAt), 6.5f, needSight: false, RenaldFrontRoomCell);

    private NavDoor Door() =>
        new(FrontDoor, new CellFrame(0xA9B40000, new AcFrame(FrontDoorAt, Quaternion.Identity)), DoorCrossings.SetupExtents(Dats)(HoltburgSetup)!.Value, false, null);

    [RequiresDatsFact]
    public void A_door_that_enters_the_object_table_mid_run_is_found_ahead_and_used_before_contact_with_no_stuck_rung()
    {
        var doors = new ScriptedDoors { OpenDelaySeconds = 0.5 };

        var leg = Run("late-feed", doors, (now, _, _, d) =>
        {
            if (now >= 1.0 && d.Version == 0)
            {
                d.With(Door()); // the server's door arrives as the body nears
                d.Version++;
            }
        });

        Assert.True(leg.Runner.State == NavRunState.Arrived, leg.Runner.FailureReason);
        Assert.Equal(0, leg.Runner.Rungs);
        Assert.Equal(1, leg.Runner.DoorUses);
        Assert.Equal(1, leg.Runner.DoorsOpened);
        Assert.Equal([FrontDoor], doors.Used);
        var used = Assert.Single(doors.UsedAt);
        Assert.True(LandDefs.IsOutdoorCell(used.CellId), $"the Use was sent outdoors (0x{used.CellId:X8}), before the doorway");
        Assert.InRange(Vector2.Distance(new Vector2(used.Origin.X, used.Origin.Y), new Vector2(132.55f, 17.1f)), 0.9f, 1.8f);
        var found = Assert.Single(leg.Log.Lines, l => l.Contains("the route crosses a door (found after the plan)", StringComparison.Ordinal));
        Assert.Contains("door=0x7A9B4015", found, StringComparison.Ordinal);
        Assert.DoesNotContain(leg.Log.Lines, l => l.Contains("stuck: a rung", StringComparison.Ordinal));
        Assert.DoesNotContain(leg.Log.Lines, l => l.Contains("replanned", StringComparison.Ordinal));
        Assert.Equal(RenaldFrontRoomCell, leg.At.CellId);
    }

    [RequiresDatsFact]
    public void A_doorway_that_becomes_placeable_mid_run_when_a_tile_attaches_is_found_the_same_way()
    {
        var doors = new ScriptedDoors { OpenDelaySeconds = 0.5 }.With(Door());
        var placeable = false;

        var leg = Run("late-doorway", doors, (now, map, cells, d) =>
        {
            d.FixedCrossings ??= list => placeable ? DoorCrossings.Map(list, new RoomGraph(cells), map) : [];
            if (now >= 1.0 && !placeable)
            {
                placeable = true; // the doorway's landblock arrives: the map's version moves
                var builder = new LandblockCollisionBuilder(Dats);
                map.Attach(NavTileBuilder.Build(builder.Build(LandblockCoord.Holtburg.Offset(1, 0)!.Value)));
            }
        });

        Assert.True(leg.Runner.State == NavRunState.Arrived, leg.Runner.FailureReason);
        Assert.Equal(0, leg.Runner.Rungs);
        Assert.Equal([FrontDoor], doors.Used);
        Assert.True(LandDefs.IsOutdoorCell(Assert.Single(doors.UsedAt).CellId));
        Assert.Single(leg.Log.Lines, l => l.Contains("(found after the plan)", StringComparison.Ordinal));
    }

    [RequiresDatsFact]
    public void A_door_nothing_announced_is_found_at_the_first_stuck_rung_before_any_recovery_move()
    {
        var doors = new ScriptedDoors { OpenDelaySeconds = 0.5 };
        var added = false;

        var leg = Run("late-rung", doors, (now, _, _, d) =>
        {
            // The body is held still from 0.5 s to 3.0 s (so the stuck ladder fires); the door joins the table then, but no version moves.
            if (now >= 0.5 && !added)
            {
                added = true;
                d.With(Door());
            }
        }, frozen: (0.5, 3.0));

        Assert.True(leg.Runner.State == NavRunState.Arrived, leg.Runner.FailureReason);
        Assert.Equal(0, leg.Runner.Rungs); // found at the first rung's check, which then made no move
        var found = Assert.Single(leg.Log.Lines, l => l.Contains("(found after the plan)", StringComparison.Ordinal));
        output.WriteLine(found);
        Assert.Equal([FrontDoor], doors.Used);
        Assert.True(LandDefs.IsOutdoorCell(Assert.Single(doors.UsedAt).CellId));
    }

    [RequiresDatsFact]
    public void The_same_held_body_with_no_door_anywhere_climbs_its_rungs_as_before()
    {
        var leg = Run("no-door-held", new ScriptedDoors(), null, frozen: (0.5, 3.0));

        Assert.True(leg.Runner.Rungs >= 1, "the held body reads as stuck");
        Assert.DoesNotContain(leg.Log.Lines, l => l.Contains("(found after the plan)", StringComparison.Ordinal));
        Assert.Empty(leg.Runner.DoorSteps);
    }

    [RequiresDatsFact]
    public void A_run_with_no_doors_is_unchanged()
    {
        var doors = new ScriptedDoors();

        var leg = Run("no-doors", doors, null);

        Assert.True(leg.Runner.State == NavRunState.Arrived, leg.Runner.FailureReason);
        Assert.Empty(leg.Runner.DoorSteps);
        Assert.Equal(0, leg.Runner.DoorUses);
        Assert.Empty(doors.Used);
        Assert.DoesNotContain(leg.Log.Lines, l => l.Contains("the route crosses a door", StringComparison.Ordinal));
    }

    private sealed record Leg(NavRunner Runner, CellFrame At, RecordingLogSink Log);

    private Leg Run(string name, ScriptedDoors doors, Action<double, NavMap, LandblockCellSet, ScriptedDoors>? frame, (double From, double To)? frozen = null)
    {
        var builder = new LandblockCollisionBuilder(Dats);
        var world = new PhysicsWorld();
        var map = new NavMap();
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                var block = builder.Build(LandblockCoord.Holtburg.Offset(dx, dy)!.Value);
                world.Attach(block);
                if (dx == 0 && dy == 0)
                {
                    map.Attach(NavTileBuilder.Build(block));
                }
            }
        }

        var cells = LandblockCellSet.Load(new CellStructureLibrary(Dats), LandblockCoord.Holtburg);
        var h = new ActionHarness(offline: true);
        var now = 0.0;
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, () => now, name);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(230), sink: audit);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        Assert.True(walker.Place(LifestoneStart()), "placed at the lifestone");
        doors.Where = () => walker.Position;
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var log = new RecordingLogSink();
        var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), new IndoorOutdoorPathSource(new PathPlanner(map), cells), confirmer, audit, new NavRunnerOptions { Name = name }, log) { Doors = doors };
        runner.Start(Renald(), now);
        while (!runner.IsDone && now < 120)
        {
            now += 1.0 / Fps;
            doors.Now = now;
            frame?.Invoke(now, map, cells, doors);
            if (frozen is not { } hold || now < hold.From || now >= hold.To)
            {
                walker.Controller.Tick(now);
            }

            runner.Step(now);
            h.Tick(1.0 / Fps);
        }

        output.WriteLine($"{name}: {runner.State} {runner.VerdictFields()} at 0x{walker.Position.CellId:X8}; {runner.FailureReason}");
        runner.Dispose();
        return new Leg(runner, walker.Position, log);
    }
}
