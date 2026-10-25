using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement;

/// <summary>
/// Why the headless body that followed ACE's walk-to-use MoveToObject toward Renald the Elder (cell 0xA9B4013F, M4 P5b gate round 1) ended on his counter at z 94.94 over the
/// front room's floor at 94.00 (docs/physics.md §8, "props as stairs"). It is not one climb of 0.94 m: the counter (static 0x02000272, top 94.938) has a box at its west end
/// (static 0x02000121, top 94.53), and the walk steps up onto the box (0.53 m) and from the box onto the counter (0.41 m), each under the human's 0.6 m step-up, as retail's
/// <c>CTransition::step_up</c> does. Straight at the counter's face away from the box the 0.94 m step is refused and the body stops on the floor. Up there, the body hangs
/// on the counter's west edge, its centre over the floor, and a step down needs the walkable it lands on under the sphere's half-radius footprint
/// (<c>BSPLEAF::hits_walkable</c>), which a step up never asks: west toward the door it does not move, south-west it steps down onto the box and then the floor.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class RenaldCounterStepDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const double Fps = 60.0;
    private const uint RenaldFrontRoomCell = 0xA9B4013F;
    private const float FloorZ = 94.00f;
    private const float BoxTopZ = 94.53f; // static 0x02000121's physics BSP top
    private const float CounterTopZ = 94.938f; // static 0x02000272's physics BSP top
    private static readonly Vector3 FloorArrival = new(133.35f, 15.65f, 94.01f); // where the walk to Renald at 6.5 m arrives (F1IndoorStartDatTests)
    private static readonly Vector3 RenaldPoint = new(139.23f, 18.33f, 94.01f); // the vendor's point (the journey's facts)
    private static readonly Vector3 LiveStuckAt = new(134.76f, 15.40f, 94.94f); // the live end of the MoveTo (FcaRenaldExitDatTests)
    private static readonly ushort NonCombat = MotionCommandTable.ToWire(MotionCommands.NonCombat);

    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void A_server_MoveTo_toward_Renald_climbs_his_counter_by_the_box_at_its_end_in_two_step_ups()
    {
        var walker = Walker(Holtburg(), FloorArrival, Bearing(FloorArrival, RenaldPoint));
        var rises = MoveTo(walker, RenaldPoint, 3.0);

        var stepUp = walker.Physics.GetStepUpHeight();
        Assert.Equal(0.6f, stepUp);
        Assert.Equal(2, rises.Count(r => r.Rise > 0.1f));
        var (box, counter) = (rises.First(r => r.Rise > 0.1f), rises.Last(r => r.Rise > 0.1f));
        Assert.InRange(box.PlaneZ, BoxTopZ - 0.01f, BoxTopZ + 0.01f);
        Assert.InRange(counter.PlaneZ, CounterTopZ - 0.01f, CounterTopZ + 0.01f);
        Assert.All(rises, r => Assert.True(r.Rise <= stepUp, $"a rise of {r.Rise:F3} m over the step-up {stepUp} m at {r.At}"));
        Assert.True(walker.Position.Frame.Origin.Z > CounterTopZ - 0.01f, $"the body ends on the counter as live: {walker.Position}");
    }

    [RequiresDatsFact]
    public void Straight_at_the_counters_face_away_from_the_box_the_step_up_is_refused_and_the_body_stops_on_the_floor()
    {
        var start = new Vector3(133.30f, 13.95f, 94.01f);
        var behind = new Vector3(136.00f, 13.95f, 94.01f); // inside the counter, east of its west face at x 135.14
        var walker = Walker(Holtburg(), start, Bearing(start, behind));
        var rises = MoveTo(walker, behind, 3.0);

        Assert.Empty(rises);
        var at = walker.Position.Frame.Origin;
        Assert.InRange(at.Z, FloorZ, FloorZ + 0.01f);
        Assert.InRange(at.X, 135.14f - 0.48f - 0.1f, 135.14f - 0.48f + 0.01f); // the lower sphere against the counter's west face
        Assert.True(walker.OnGround, walker.Position.ToString());
    }

    [RequiresDatsFact]
    public void On_the_counters_edge_west_does_not_move_and_south_west_steps_down_by_the_box()
    {
        var west = Walker(Holtburg(), LiveStuckAt, 270f);
        Walk(west, 2.0);
        Assert.True(Vector2.Distance(new Vector2(west.Position.Frame.Origin.X, west.Position.Frame.Origin.Y), new Vector2(LiveStuckAt.X, LiveStuckAt.Y)) < 0.05f, west.Position.ToString());
        Assert.InRange(west.Position.Frame.Origin.Z, CounterTopZ, CounterTopZ + 0.01f);

        var southWest = Walker(Holtburg(), LiveStuckAt, 225f);
        var path = Walk(southWest, 2.0);
        Assert.Contains(path, z => MathF.Abs(z - (BoxTopZ + 0.005f)) < 0.01f);
        Assert.InRange(southWest.Position.Frame.Origin.Z, FloorZ, FloorZ + 0.01f);
    }

    private static float Bearing(Vector3 from, Vector3 to) => (450f - (MathF.Atan2(to.Y - from.Y, to.X - from.X) * 180f / MathF.PI)) % 360f;

    private OfflineWalker Walker(PhysicsWorld world, Vector3 at, float heading)
    {
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(230));
        world.UseTime(0.0);
        Assert.True(walker.Place(new CellFrame(RenaldFrontRoomCell, RetailFrames.SetHeading(new AcFrame(at, Quaternion.Identity), heading))));
        return walker;
    }

    /// <summary>
    /// The server's walk-to-use as ACE sends it to the player (a MoveTo with ACE's parameters: retail's default flags with CanCharge and a walk/run threshold of 1, so the
    /// body runs at its run rate), to a position rather than Renald's object so the body keeps walking into whatever stops it; answers each rise between frames.
    /// </summary>
    private List<(float Rise, float PlaneZ, CellFrame At)> MoveTo(OfflineWalker walker, Vector3 target, double seconds)
    {
        Assert.True(walker.Physics.Weenie!.TryGetRunRate(out var runRate));
        var parameters = new MovementParameters() with { Flags = MovementParameters.DefaultFlags | MovementParameterFlags.CanCharge, WalkRunThreshold = 1f };
        var moveTo = new WireMovement(WireMovementType.MoveToPosition, NonCombat, false, false, default, MoveTo: new WireMoveTo(0, new CellFrame(RenaldFrontRoomCell, new AcFrame(target, Quaternion.Identity)), parameters, runRate));
        Assert.Equal(MovementUnpackResult.Applied, ((IPlayerMovementBody)walker.Body).ApplyServerMovement(moveTo, [], autonomous: false, out _));
        var rises = new List<(float Rise, float PlaneZ, CellFrame At)>();
        var now = 0.0;
        var last = walker.Position.Frame.Origin.Z;
        while (now < seconds)
        {
            now += 1.0 / Fps;
            walker.Controller.Tick(now);
            var z = walker.Position.Frame.Origin.Z;
            if (z - last > 0.04f)
            {
                var plane = walker.Physics.ContactPlane;
                rises.Add((z - last, -plane.D / plane.Normal.Z, walker.Position));
                output.WriteLine($"t={now:F3} rise {z - last:F3} m onto a plane at {-plane.D / plane.Normal.Z:F3}: {walker.Position}");
            }

            last = z;
        }

        output.WriteLine($"end {walker.Position} moving_to={walker.Physics.IsMovingTo}");
        return rises;
    }

    /// <summary>Walks the body forward for <paramref name="seconds"/>; answers its height every frame.</summary>
    private List<float> Walk(OfflineWalker walker, double seconds)
    {
        walker.DoMotion(MotionCommands.WalkForward);
        var heights = new List<float>();
        for (var now = 1.0 / Fps; now <= seconds; now += 1.0 / Fps)
        {
            walker.Controller.Tick(now);
            heights.Add(walker.Position.Frame.Origin.Z);
        }

        output.WriteLine($"walked {seconds} s heading {RetailFrames.GetHeading(walker.Position.Frame):F0}: {walker.Position}");
        return heights;
    }

    private PhysicsWorld Holtburg()
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

        return world;
    }
}
