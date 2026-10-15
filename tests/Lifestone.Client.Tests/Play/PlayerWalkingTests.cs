using System.Numerics;
using Lifestone.Client.Camera;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Data.World.Scenery;

namespace Lifestone.Client.Tests.Play;

/// <summary>
/// The walking player of M2 G5 (<c>--offline-player</c>) without the engine, on the real dats: the keys of the game key map through the router and
/// <see cref="MovementBindings"/> to the human walker on Holtburg's collision, the dry-run sink's messages, and the chase camera swept through the
/// same collision.
/// </summary>
public sealed class PlayerWalkingTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private DatSet Dats => fixture.Dats;

    [RequiresLocalDatsFact]
    public void The_run_key_walks_a_player_who_runs_by_default_and_w_runs()
    {
        var run = Pace(shiftHeld: false, out var runSink);
        var walk = Pace(shiftHeld: true, out _);

        Assert.InRange(run, 6.0, 8.6); // the run cycle at the placeholder run rate
        Assert.InRange(walk, 2.0, 3.2); // the walk cycle
        Assert.True(run > 2.5 * walk);
        Assert.Equal(2, runSink.MoveToStateCount); // a MoveToState for the key down and one for the key up
        Assert.InRange(runSink.AutonomousPositionCount, 3, 7); // about one a second while it runs
    }

    [RequiresLocalDatsFact]
    public void The_turn_keys_turn_the_character_and_a_strafe_key_moves_it_sideways_keeping_its_heading()
    {
        var rig = Rig();
        rig.Settle(At(96f, 20f, 95f));
        var heading = rig.Walker.Position.HeadingDegrees;

        rig.Scene.Input.KeyDown(DikScanCodes.D); // turn right
        rig.Run(0.5);
        rig.Scene.Input.KeyUp(DikScanCodes.D);
        rig.Run(0.2);
        var turned = Angle(rig.Walker.Position.HeadingDegrees - heading);

        var before = rig.Walker.Position;
        rig.Scene.Input.KeyDown(DikScanCodes.C); // strafe right
        rig.Run(1.0);
        rig.Scene.Input.KeyUp(DikScanCodes.C);
        var moved = before.GetOffset(rig.Walker.Position);

        Assert.InRange(turned, 20.0, 140.0); // to the right, clockwise
        Assert.True(rig.Walker.OnGround);
        Assert.InRange(new Vector2(moved.X, moved.Y).Length(), 1.0f, 6.0f);
        Assert.Equal(before.HeadingDegrees, rig.Walker.Position.HeadingDegrees, 0); // a strafe does not turn
    }

    [RequiresLocalDatsFact]
    public void Holding_the_jump_key_charges_the_bar_and_releasing_it_sends_the_jump_with_that_extent()
    {
        var rig = Rig();
        rig.Settle(At(96f, 20f, 95f));
        var groundZ = rig.Walker.Position.Origin.Z;

        rig.Scene.Input.KeyDown(DikScanCodes.Space);
        rig.Run(0.5);
        Assert.True(rig.Walker.Controller.Jump.Building);
        rig.Scene.Input.KeyUp(DikScanCodes.Space);
        var peak = groundZ;
        var airborne = false;
        for (var i = 0; i < 90; i++)
        {
            rig.Run(1.0 / 60.0);
            peak = MathF.Max(peak, rig.Walker.Position.Origin.Z);
            airborne |= !rig.Walker.OnGround;
        }

        Assert.True(airborne);
        Assert.InRange(peak - groundZ, 0.3f, 4f);
        Assert.Equal(1, rig.Sink.JumpCount);
        Assert.True(rig.Walker.OnGround); // it landed
    }

    [RequiresLocalDatsFact]
    public void The_chase_camera_follows_the_player_and_the_sweep_keeps_it_out_of_the_ground()
    {
        var rig = Rig();
        rig.Settle(At(96f, 20f, 95f));
        var sweep = new ViewerSweep((in CellFrame from, in CellFrame to) => rig.World.SweepViewer(from, to, 0.3f));
        var model = new ChaseCameraModel();
        var now = rig.Now;
        var player = rig.Walker.Position;

        var pose = model.Update(now, player, sweep);

        // 2.5 m behind and 2.25 m above the feet, looking where the player faces, not blocked, in the player's cell.
        var terrain = new TerrainSampler(Dats.GetLandblockTerrain(LandblockCoord.Holtburg)!, Dats.TerrainRegion.LandHeightTable);
        Assert.False(pose.Blocked);
        Assert.Equal(player.CellId, pose.Viewer.CellId);
        Assert.InRange(Vector3.Distance(pose.Viewer.Origin, pose.Pivot.Origin), 2.5f, 2.7f);
        Assert.InRange(pose.Viewer.Origin.Z, player.Origin.Z + 2.2f, player.Origin.Z + 2.3f);

        // Lowered a long way, the camera would be under the ground: the sweep stops it above the terrain, and says so.
        for (var i = 0; i < 100; i++)
        {
            model.Lower(now + (i * 0.02), keepGoing: false, modOverride: 1f);
        }

        model.Snap();
        var low = model.Update(now + 3.0, player, sweep);
        var ground = terrain.GetHeight(low.Viewer.Origin.X, low.Viewer.Origin.Y);
        Assert.True(model.ViewerOffset.Z < 0f, $"offset {model.ViewerOffset}");
        Assert.True(low.Desired.Origin.Z < ground, "the lowered camera wanted to be under the ground");
        Assert.True(low.Blocked);
        Assert.True(low.Viewer.Origin.Z >= ground - 0.01f, $"the camera is {low.Viewer.Origin.Z - ground:F3} m above the terrain");
    }

    [RequiresLocalDatsFact]
    public void An_interpolated_draw_position_stays_between_the_steps_while_the_player_runs()
    {
        var rig = Rig();
        rig.Settle(At(96f, 20f, 95f));
        rig.Scene.Input.KeyDown(DikScanCodes.W);
        rig.Run(0.5);
        var controller = rig.Walker.Controller;
        var seen = 0;
        for (var i = 0; i < 120; i++)
        {
            rig.Run(1.0 / 60.0);
            var drawn = DrawInterpolation.Interpolate(controller.PreviousPosition, controller.Position, controller.PreviousStepTime, controller.LastStepTime, rig.Now, stepping: false);
            var from = DrawInterpolation.InSpaceOf(controller.PreviousPosition, controller.Position.CellId).Origin;
            var to = controller.Position.Origin;
            var low = Vector3.Min(from, to) - new Vector3(1e-3f);
            var high = Vector3.Max(from, to) + new Vector3(1e-3f);
            Assert.True(drawn.Origin.X >= low.X && drawn.Origin.X <= high.X && drawn.Origin.Y >= low.Y && drawn.Origin.Y <= high.Y, $"{drawn.Origin} outside {from} .. {to}");
            seen += drawn.Origin != to ? 1 : 0;
        }

        Assert.InRange(seen, 30, 120); // most frames between steps are drawn between the positions
    }

    private double Pace(bool shiftHeld, out DryRunMovementSink sink)
    {
        var rig = Rig();
        rig.Settle(At(96f, 20f, 95f));
        rig.Walker.Physics.SetHeading(90f); // east, along the flat road
        var start = rig.Walker.Position;
        if (shiftHeld)
        {
            rig.Scene.Input.KeyDown(DikScanCodes.LShift);
        }

        rig.Scene.Input.KeyDown(DikScanCodes.W);
        rig.Run(4.0);
        rig.Scene.Input.KeyUp(DikScanCodes.W);
        var walked = start.GetOffset(rig.Walker.Position);
        sink = rig.Sink;
        return new Vector2(walked.X, walked.Y).Length() / 4.0;
    }

    private static double Angle(double degrees)
    {
        var d = degrees % 360.0;
        return d < 0 ? d + 360.0 : d;
    }

    private Rigging Rig()
    {
        var builder = new LandblockCollisionBuilder(Dats);
        var world = new PhysicsWorld();
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                world.Attach(builder.Build(LandblockCoord.Holtburg.Offset(dx, dy)!.Value));
            }
        }

        var sink = new DryRunMovementSink(NullLogSink.Instance);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, sink: sink, options: new MovementOptions { RunByDefault = true });
        var scene = new UiScene([]);
        scene.Input.Bindings = RetailKeyBindings.Load(Dats);
        var rig = new Rigging(walker, world, sink, scene);
        var movement = new MovementBindings(walker.Controller, () => rig.Now, () => walker.Controller.AutoRun);
        scene.Input.RegisterInputMap(UiInputRouter.MovementMap, movement, 1000);
        world.UseTime(0.0);
        return rig;
    }

    private static CellFrame At(float x, float y, float z) =>
        new(CellIds.LandCellAt(LandblockCoord.Holtburg, x, y)!.Value, new AcFrame(new Vector3(x, y, z), Quaternion.Identity));

    private sealed class Rigging(OfflineWalker walker, PhysicsWorld world, DryRunMovementSink sink, UiScene scene)
    {
        public OfflineWalker Walker => walker;

        public PhysicsWorld World => world;

        public DryRunMovementSink Sink => sink;

        public UiScene Scene => scene;

        public double Now { get; private set; }

        /// <summary>Places the walker and lets it settle for a second.</summary>
        public void Settle(in CellFrame at)
        {
            Assert.True(walker.Place(at));
            Run(1.0);
            Assert.True(walker.OnGround);
        }

        /// <summary>Runs frames at 60 Hz for <paramref name="seconds"/>: the router's clock, then the controller's frame.</summary>
        public void Run(double seconds)
        {
            var frames = Math.Max(1, (int)Math.Round(seconds * 60.0));
            for (var i = 0; i < frames; i++)
            {
                Now += 1.0 / 60.0;
                scene.Frame(1.0 / 60.0);
                walker.Controller.Tick(Now);
            }
        }
    }
}
