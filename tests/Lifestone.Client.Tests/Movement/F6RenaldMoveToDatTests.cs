using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Physics;
using Lifestone.Client.Tests.Movement.Wayfinding;
using Lifestone.Client.Tests.Physics;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Net.Diagnostics;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement;

/// <summary>
/// M5 item 5 fix F6 (the live gate's round 2: the Brawler's vendor-facts and chests stations). ACE's walk-to-use toward Renald the Elder timed out (its chain's 15 s,
/// <c>Player_Move.cs:16, 119-124</c> @95ef426d: the OpenVendor's UseDone 0 came 15.0 s after the Use, 18.07 s with the vendor wait's 3.0 s) and sent nothing more, while the
/// client's MoveTo stayed under way: retail's <c>MoveToManager</c> counts no-progress checks (<c>fail_progress_count</c>) and never ends on them, so the next station's runner
/// yielded to it for 570 s. On the dats the MoveTo from the journey's arrivals (the floor point, the doorway) arrives within Renald's use radius, and one toward an NPC touches his
/// cylinder and arrives; a MoveTo an NPC holds in the shop's doorway is ended by the controller's stuck rule (ACE's timeout, <c>TODO(m5-moveto-stuck)</c>), named, and the runner that
/// yielded to it runs on.
/// </summary>
/// <remarks>
/// Test data, read-only from the pinned world database (ACE-World-Database-v0.9.296), as <see cref="ServerObjectRouteTests"/>: Renald the Elder 0x7A9B4021 at (139.23, 18.33, 94.01)
/// in cell 0xA9B4013F with UseRadius 5 (docs/milestones/m4-fights-and-loot.md section 5.9), the human Setup 0x02000001, the shop's front door 0x7A9B4015 (Setup 0x020019FF at
/// (132.56, 17.115, 94), turned 180 degrees). The NPC in the doorway is the test's choice.
/// </remarks>
[Trait("Tier", "Exhaustive")]
public sealed class F6RenaldMoveToDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const double Fps = 60.0;
    private const uint RenaldFrontRoomCell = 0xA9B4013F;
    private const uint Renald = 0x7A9B4021;
    private const uint Blocker = 0x7A9B4073;
    private const uint FrontDoor = 0x7A9B4015;
    private const uint HumanSetupId = 0x02000001;
    private const float RenaldUseRadius = 5f;
    private static readonly Vector3 RenaldAt = new(139.23f, 18.33f, 94.01f);

    private DatSet Dats => fixture.Dats;

    [RequiresDatsTheory]
    [InlineData(133.35f, 15.65f, 90f)] // the floor arrival (F1IndoorStartDatTests)
    [InlineData(133.5f, 15.5f, 90f)] // the den profile's stop point for Renald from D120 (m5-renald-shop-floor) to M5 7 P7p, 5.04 m out (VendorStopConfirmDatTests)
    [InlineData(132.84f, 17.17f, 80f)] // 6.5 m from Renald on the line from the door: where a leg from the street stops first
    [InlineData(132.84f, 17.17f, 0f)] // the same, facing along the front wall (round 2's frame)
    public void From_the_journeys_arrivals_ACEs_MoveTo_to_Renald_arrives_within_his_use_radius(float x, float y, float heading)
    {
        var scene = Scene(new Vector3(x, y, 94.01f), heading, renald: true, door: true);
        var start = scene.CylinderTo(Renald);
        Assert.True(start > RenaldUseRadius, $"ACE sends the MoveTo: {start:F3} m cylinder to cylinder, over the 5 m use radius");

        scene.MoveToObject(Renald, RenaldUseRadius);
        var seconds = scene.RunWhileMovingTo(16.0);

        output.WriteLine($"from ({x}, {y}) heading {heading}: {start:F3} m, {seconds:F2} s, {scene.Walker.Position}, cylinder {scene.CylinderTo(Renald):F3} m; {scene.Walker.Controller.LastServerMoveToEnd}");
        Assert.Equal("arrived", scene.Walker.Controller.LastServerMoveToEnd);
        Assert.True(scene.CylinderTo(Renald) <= RenaldUseRadius, "within ACE's IsWithinUseRadiusOf (cylinder distance, WorldObject_Use.cs:47-61)");
        Assert.True(seconds < 3.0, $"{seconds:F2} s");
        Assert.Equal(0, scene.Walker.Controller.ServerMoveToStuckEnds);
    }

    [RequiresDatsFact]
    public void A_MoveTo_to_an_NPC_at_the_default_use_radius_touches_his_cylinder_and_arrives()
    {
        var scene = Scene(new Vector3(139.23f, 21.0f, 94.01f), 0f, renald: true, door: false); // behind Renald's counter, his side
        scene.MoveToObject(Renald, 0.6f); // ACE's default UseRadius (WorldObject_Use.cs:50)
        var seconds = scene.RunWhileMovingTo(16.0);

        var cylinder = scene.CylinderTo(Renald);
        output.WriteLine($"{seconds:F2} s, {scene.Walker.Position}, cylinder {cylinder:F3} m; {scene.Walker.Controller.LastServerMoveToEnd}");
        Assert.Equal("arrived", scene.Walker.Controller.LastServerMoveToEnd);
        Assert.InRange(cylinder, -0.01f, 0.6f);
    }

    [RequiresDatsFact]
    public void A_MoveTo_to_Renald_across_his_counter_at_the_default_use_radius_ends_named_by_the_no_progress_rule()
    {
        // M6 sprint (m6-moveto-no-progress): the counter holds the body from the first step, so the MoveTo closes nothing and is ended after ServerMoveToNoProgressSeconds, not ACE's 15 s
        var log = new RecordingLogSink();
        var scene = Scene(new Vector3(136.0f, 18.33f, 94.01f), 90f, renald: true, door: false, log: log);
        scene.MoveToObject(Renald, 0.6f);
        var seconds = scene.RunWhileMovingTo(30.0);

        var line = log.Lines.FirstOrDefault(l => l.Contains("(m6-moveto-no-progress)", StringComparison.Ordinal));
        output.WriteLine($"{seconds:F2} s, {scene.Walker.Position}, cylinder {scene.CylinderTo(Renald):F3} m; {line}");
        Assert.Equal("stuck", scene.Walker.Controller.LastServerMoveToEnd);
        Assert.InRange(seconds, PlayerMovementController.ServerMoveToNoProgressSeconds - 0.1, PlayerMovementController.ServerMoveToNoProgressSeconds + 0.5);
        Assert.Equal(0, log.Lines.Count(l => l.Contains("TODO(m5-moveto-stuck)", StringComparison.Ordinal))); // ACE's timeout never came
        Assert.True(scene.CylinderTo(Renald) > 1.5f, "the counter holds the body on the room's side");
        Assert.NotNull(line);
        Assert.Contains("type=MoveToObject", line, StringComparison.Ordinal);
        Assert.Contains("target=0x7A9B4021", line, StringComparison.Ordinal);
    }

    [RequiresDatsFact]
    public void A_MoveTo_two_NPCs_hold_in_the_doorway_ends_named_by_the_stuck_rule_at_ACEs_timeout()
    {
        var log = new RecordingLogSink();
        var scene = Blocked(log);
        var seconds = scene.RunWhileMovingTo(30.0);

        var line = log.Lines.FirstOrDefault(l => l.Contains("TODO(m5-moveto-stuck)", StringComparison.Ordinal));
        output.WriteLine($"{seconds:F2} s, {scene.Walker.Position}; {scene.Walker.Controller.LastServerMoveToEnd}; {line}");
        Assert.Equal("stuck", scene.Walker.Controller.LastServerMoveToEnd);
        Assert.Equal(1, scene.Walker.Controller.ServerMoveToStuckEnds);
        Assert.InRange(seconds, PlayerMovementController.ServerMoveToLimitSeconds - 0.1, PlayerMovementController.ServerMoveToLimitSeconds + 0.2);
        Assert.False(scene.Walker.Physics.IsMovingTo);
        Assert.Equal(WeenieError.ActionCancelled, scene.Walker.Physics.Movement!.MoveTo!.LastCancelReason);
        Assert.NotNull(line);
        Assert.Contains("held_s=15.0", line, StringComparison.Ordinal);
        Assert.True(scene.Walker.Position.Frame.Origin.X > 133.0f, "the NPC held the body inside the shop");
    }

    [RequiresDatsFact]
    public void A_runner_that_yields_to_a_stuck_MoveTo_hears_it_end_and_runs_on()
    {
        var log = new RecordingLogSink();
        var scene = Blocked(log);
        scene.Run(0.5);

        var map = new NavMap();
        map.Attach(NavTileBuilder.Build(new LandblockCollisionBuilder(Dats).Build(LandblockCoord.Holtburg)));
        var cells = LandblockCellSet.Load(new CellStructureLibrary(Dats), LandblockCoord.Holtburg);
        var h = new ActionHarness(offline: true);
        var now = scene.Now;
        var began = now;
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, () => now, "f6-yield");
        h.Api.Move.Bind(scene.Walker.Controller, scene.Walker.Controller.Options.RunByDefault);
        using var confirmer = new RouteConfirmer(scene.World, scene.Walker.Physics.Setup!);
        using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(scene.Walker.Controller), new IndoorOutdoorPathSource(new PathPlanner(map), cells), confirmer, audit, new NavRunnerOptions { Name = "f6-yield" }, log);
        runner.Start(NavGoal.ToPoint(new NavPoint(0xA9B40000, new Vector3(135.0f, 15.2f, 94.01f)), RenaldFrontRoomCell), now);
        double? resumed = null;
        var yielded = false;
        while (!runner.IsDone && now < began + 20)
        {
            now += 1.0 / Fps;
            scene.Step(now);
            var state = runner.Step(now);
            h.Tick(1.0 / Fps);
            yielded |= state == NavRunState.Yielding;
            if (yielded && resumed is null && state != NavRunState.Yielding)
            {
                resumed = now;
            }
        }

        var ended = log.Lines.FirstOrDefault(l => l.Contains("the server's MoveTo was ended as stuck", StringComparison.Ordinal));
        output.WriteLine($"{runner.State} at {now:F2} s (resumed {resumed:F2}); {runner.VerdictFields()}; {scene.Walker.Position}; {runner.FailureReason}; {ended}");
        Assert.True(yielded, "the runner yielded to the server's MoveTo");
        Assert.NotNull(ended);
        Assert.Contains("end=stuck", ended, StringComparison.Ordinal);
        Assert.Equal(1, runner.YieldsStuck);
        Assert.NotNull(resumed);
        Assert.InRange(resumed!.Value, PlayerMovementController.ServerMoveToLimitSeconds - 0.1, PlayerMovementController.ServerMoveToLimitSeconds + 0.3); // the MoveTo began at 0
        Assert.NotEqual(NavRunState.Yielding, runner.State); // running on to its own goal, the keys its own again
        Assert.DoesNotContain("m5-moveto-stuck", runner.FailureReason ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>The body inside the shop, two NPCs side by side in the open doorway, and ACE's MoveToPosition to the street beyond him.</summary>
    private Scene Blocked(ILogSink log)
    {
        var scene = Scene(new Vector3(134.3f, 17.12f, 94.01f), 270f, renald: false, door: true, blockersAt: [new Vector3(132.9f, 16.45f, 94.01f), new Vector3(132.9f, 17.8f, 94.01f)], log: log);
        scene.MoveToPosition(new CellFrame(0xA9B40029, new AcFrame(new Vector3(128.0f, 17.12f, 94.01f), Quaternion.Identity)));
        return scene;
    }

    private Scene Scene(Vector3 at, float heading, bool renald, bool door, Vector3[]? blockersAt = null, ILogSink? log = null)
    {
        var world = ServerObjectPhysicsTests.HoltburgWorld(Dats);
        var host = new ServerObjectPhysicsHost(world, Dats, log: NullLogSink.Instance);
        if (door)
        {
            // Open, as ACE's Door.Open leaves it (Ethereal, Door.cs:111-130 @95ef426d).
            host.Create(new ServerObjectDescription(FrontDoor, 0x020019FF, 1f, (uint)(PhysicsState.ReportCollisions | PhysicsState.Ethereal), IsCreature: false, IsSelf: false, DescriptionFlags: 0x1000), new CellFrame(0xA9B40029, new AcFrame(new Vector3(132.56f, 17.115f, 94f), new Quaternion(0f, 0f, -1f, 0f))));
            Assert.True(host.Hosts(FrontDoor));
        }

        if (renald)
        {
            host.Create(Npc(Renald), new CellFrame(RenaldFrontRoomCell, new AcFrame(RenaldAt, Quaternion.Identity)));
            Assert.True(host.Hosts(Renald));
        }

        for (var i = 0; i < (blockersAt?.Length ?? 0); i++)
        {
            host.Create(Npc(Blocker + (uint)i), new CellFrame(RenaldFrontRoomCell, new AcFrame(blockersAt![i], Quaternion.Identity)));
            Assert.True(host.Hosts(Blocker + (uint)i), "the NPC stands in the doorway");
        }

        var walker = OfflineWalker.Create(Dats, world, log ?? NullLogSink.Instance, new OfflinePlayerWeenie(230));
        world.UseTime(0.0);
        Assert.True(walker.Place(new CellFrame(RenaldFrontRoomCell, RetailFrames.SetHeading(new AcFrame(at, Quaternion.Identity), heading))), $"placed at {at}");
        var targets = new Targets { [walker.Physics.Id] = walker.Physics };
        foreach (var guid in new[] { Renald, Blocker, Blocker + 1 })
        {
            if (host.TryGetObject(guid, out var obj))
            {
                targets[guid] = obj;
                obj.TargetWorld = targets;
            }
        }

        walker.Physics.TargetWorld = targets; // the avatar set's part: a target id names the object that reports
        return new Scene(world, host, walker);
    }

    private static ServerObjectDescription Npc(uint guid) =>
        new(guid, HumanSetupId, 1f, (uint)PhysicsState.ReportCollisions, IsCreature: true, IsSelf: false, DescriptionFlags: 0);

    private sealed class Targets : Dictionary<uint, ITargetable>, ITargetWorld
    {
        public ITargetable? FindObject(uint objectId) => TryGetValue(objectId, out var t) ? t : null;
    }
}

/// <summary>
/// The default tier of M5 item 5 F6 (no dats): the controller names how a server MoveTo ended (an arrival, the unknown target's walk to the sent origin), and the runner's backstop fails a yield a body
/// never ends at ACE's MoveTo timeout, named (<c>TODO(m5-moveto-stuck)</c>).
/// </summary>
public sealed class F6ServerMoveToWatchTests
{
    private static readonly ushort NonCombat = MotionCommandTable.ToWire(MotionCommands.NonCombat);

    [Fact]
    public void A_server_MoveTo_that_arrives_is_named_arrived_and_the_stuck_rule_stays_quiet()
    {
        var (controller, _, _) = MovementTestKit.FlatController();
        var body = new PhysicsPlayerBody(controller.Player) { ServerControl = controller };
        var target = MovementTestKit.At(MovementTestKit.Block, 96f, 101f, 0f);
        var parameters = new MovementParameters { Flags = MovementParameterFlags.CanWalk | MovementParameterFlags.MoveTowards, DistanceToObject = 0.6f };
        Assert.Equal(MovementUnpackResult.Applied, body.ApplyServerMovement(new WireMovement(WireMovementType.MoveToPosition, NonCombat, false, false, default, MoveTo: new WireMoveTo(0, target, parameters, 1f)), [], autonomous: false, out _));
        Assert.Null(controller.LastServerMoveToEnd);

        MovementTestKit.Run(controller, 12.0);

        Assert.False(controller.IsMovingTo);
        Assert.Equal("arrived", controller.LastServerMoveToEnd);
        Assert.Equal(0, controller.ServerMoveToStuckEnds);
        Assert.Equal("arrived", new ControllerNavBody(controller).ServerMoveToEnd);
    }

    [Fact]
    public void A_server_MoveToObject_to_an_object_unknown_here_walks_to_the_sent_origin_and_is_named_arrived()
    {
        var (controller, _, _) = MovementTestKit.FlatController();
        var body = new PhysicsPlayerBody(controller.Player) { ServerControl = controller };
        var nowhere = MovementTestKit.At(MovementTestKit.Block, 96f, 106f, 0f);
        var parameters = new MovementParameters { Flags = MovementParameterFlags.CanWalk | MovementParameterFlags.MoveTowards | MovementParameterFlags.UseSpheres, DistanceToObject = 0.6f };
        Assert.Equal(MovementUnpackResult.Applied, body.ApplyServerMovement(new WireMovement(WireMovementType.MoveToObject, NonCombat, false, false, default, MoveTo: new WireMoveTo(0x70000099, nowhere, parameters, 1f)), [], autonomous: false, out _));

        MovementTestKit.Run(controller, PlayerMovementController.ServerMoveToLimitSeconds + 1.0);

        Assert.False(controller.IsMovingTo);
        Assert.Equal("arrived", controller.LastServerMoveToEnd); // retail's unpack_movement moves to the sent origin when the object is unknown (MovementManager)
        Assert.True(PositionFrames.Distance(nowhere, controller.Position) < 1.5f, controller.Position.ToString());
        Assert.Equal(0, controller.ServerMoveToStuckEnds);
    }

    [Fact]
    public void A_runner_whose_body_never_ends_the_server_MoveTo_fails_named_at_ACEs_timeout()
    {
        var rig = new S5Rig(50f, 50f);
        rig.Runner.Start(S5Rig.To(50f, 90f), rig.Body.Now);
        rig.RunFor(1);
        rig.Body.ServerDriving = true;
        var from = rig.Body.Now;
        while (!rig.Runner.IsDone && rig.Body.Now - from < 30)
        {
            rig.Frame();
        }

        Assert.Equal(NavRunState.Failed, rig.Runner.State);
        Assert.Contains("TODO(m5-moveto-stuck)", rig.Runner.FailureReason, StringComparison.Ordinal);
        Assert.InRange(rig.Body.Now - from, NavRunner.YieldLimitSeconds - 0.1, NavRunner.YieldLimitSeconds + 0.2);
        Assert.Equal(1, rig.Runner.YieldsStuck);
    }

}

/// <summary>The walker in Holtburg with the server-object host; the server's movements applied as the session applies them (<see cref="IPlayerMovementBody.ApplyServerMovement"/>).</summary>
internal sealed class Scene(PhysicsWorld world, ServerObjectPhysicsHost host, OfflineWalker walker)
{
    private static readonly ushort NonCombat = MotionCommandTable.ToWire(MotionCommands.NonCombat);

    public PhysicsWorld World => world;

    public OfflineWalker Walker => walker;

    public double Now { get; private set; }

    /// <summary>ACE's walk-to-use MoveToObject (<c>Creature.MoveToObject</c>, <c>Creature.cs:276-290</c> @95ef426d): retail's default flags (UseSpheres among them), the use radius as the distance to the object.</summary>
    public void MoveToObject(uint guid, float useRadius)
    {
        Assert.True(host.TryGetObject(guid, out var target));
        Assert.True(walker.Physics.Weenie!.TryGetRunRate(out var runRate));
        var parameters = new MovementParameters() with { Flags = MovementParameters.DefaultFlags | MovementParameterFlags.CanCharge, WalkRunThreshold = 1f, DistanceToObject = useRadius };
        Apply(new WireMovement(WireMovementType.MoveToObject, NonCombat, false, false, default, MoveTo: new WireMoveTo(guid, target.Position, parameters, runRate)));
    }

    /// <summary>ACE's MoveToPosition (<c>Creature.MoveToPosition</c>, <c>Creature.cs:295-304</c>): to the position itself.</summary>
    public void MoveToPosition(in CellFrame position)
    {
        Assert.True(walker.Physics.Weenie!.TryGetRunRate(out var runRate));
        var parameters = new MovementParameters() with { Flags = MovementParameters.DefaultFlags | MovementParameterFlags.CanCharge, WalkRunThreshold = 1f, DistanceToObject = 0f };
        Apply(new WireMovement(WireMovementType.MoveToPosition, NonCombat, false, false, default, MoveTo: new WireMoveTo(0, position, parameters, runRate)));
    }

    /// <summary>Cylinder to cylinder, as ACE's <c>IsWithinUseRadiusOf</c> measures (<c>WorldObject_Use.cs:47-61</c>).</summary>
    public float CylinderTo(uint guid)
    {
        Assert.True(host.TryGetObject(guid, out var target));
        return PositionFrames.CylinderDistance(walker.Physics.Radius, walker.Physics.Height, walker.Position, target.Radius, target.Height, target.Position);
    }

    public void Step(double now)
    {
        Now = now;
        walker.Controller.Tick(now);
        host.Tick();
    }

    public void Run(double seconds)
    {
        var end = Now + seconds;
        while (Now < end)
        {
            Step(Now + (1.0 / 60.0));
        }
    }

    /// <summary>Steps while the MoveTo is under way (up to <paramref name="limit"/> s); answers the seconds it took.</summary>
    public double RunWhileMovingTo(double limit)
    {
        var start = Now;
        while (walker.Physics.IsMovingTo && Now - start < limit)
        {
            Step(Now + (1.0 / 60.0));
        }

        Step(Now + (1.0 / 60.0)); // the controller names the end on the step after it
        return Now - start;
    }

    private void Apply(in WireMovement movement) =>
        Assert.Equal(MovementUnpackResult.Applied, ((IPlayerMovementBody)walker.Body).ApplyServerMovement(movement, [], autonomous: false, out _));
}
