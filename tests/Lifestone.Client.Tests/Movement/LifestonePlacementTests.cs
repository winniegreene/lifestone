using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Physics;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement;

/// <summary>
/// M4 4.2 FS (live gate round 1, the Soak tier's BSD Mage login): Lifestone Recall (1635) arrives at the tied lifestone's own
/// position (ACE WorldObject_Magic.cs 1087-1095 recalls to <c>LinkedLifestone</c>, which Lifestone Tie sets to the lifestone's
/// location), inside the lifestone's cylinder. A character that logs off there is created at that spot on its next login
/// (ACE Player_Networking.cs 224, <c>SendSelf</c>), and only then does ACE's own placement move it out
/// (<c>CPhysicsObj::enter_world</c>, ACE WorldObject.cs 209-220: <c>enter_world</c>, then <c>SyncLocation</c>), with no
/// message to the client: live, the server's where-am-I said (81.33, 14.02) while the CreateObject said (81.33, 11.80).
/// Retail's client runs the same placement on its own player (<c>CPhysicsObj::enter_world</c>, 11.6096 0x00516E10; a teleport arrival is a sliding <c>SetPositionSimple</c>, 0x00516DB0), so it
/// would land where ACE did. These tests pin that Lifestone's placement does too, with the lifestone a physics object in the player's world made by the server-object physics host
/// (M5 item 5 h1 and h2, <c>TODO(d25-server-object-host)</c> closed; docs/physics.md "Other objects"), and (slice l1) what happens when the lifestone's create arrives after the teleport:
/// the transition holds a body that is already inside an object, so the body is placed again by the same sliding placement (<c>TODO(m5-create-overlap)</c>).
/// </summary>
public sealed class LifestonePlacementTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>The Holtburg lifestone's Setup (its CreateObject's PhysicsDesc, weenie 509).</summary>
    private const uint LifestoneSetupId = 0x020002EE;

    /// <summary>The Holtburg lifestone's cell (ACE's landblock_instance 0x7A9B404F).</summary>
    private const uint Cell = 0xA9B40019;

    /// <summary>The lifestone's origin and the recall's arrival (ACE's landblock_instance, the live teleport at 81.3304, 11.7974, 94.005).</summary>
    private static readonly Vector3 LifestoneOrigin = new(81.3304f, 11.7974f, 94.005f);

    /// <summary>The lifestone's and the arrival's heading (the live q(0.839091 0 0 -0.543991)).</summary>
    private static readonly Quaternion Heading = new(0f, 0f, -0.543991f, 0.839091f);

    /// <summary>Where ACE's placement put the BSD Mage on that login (its where-am-I).</summary>
    private static readonly Vector2 AcePlaced = new(81.3304f, 14.019623f);

    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void A_player_entering_the_world_on_the_lifestone_is_placed_outside_it_where_ACE_placed_the_mage()
    {
        var (world, _) = Holtburg(withLifestone: true);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance);
        var arrival = new CellFrame(Cell, new AcFrame(LifestoneOrigin, Heading));

        Assert.True(walker.Place(arrival));
        var placed = walker.Physics.Position.Frame.Origin;
        var moved = Vector2.Distance(new Vector2(placed.X, placed.Y), new Vector2(LifestoneOrigin.X, LifestoneOrigin.Y));
        var fromAce = Vector2.Distance(new Vector2(placed.X, placed.Y), AcePlaced);
        output.WriteLine($"placed at {walker.Physics.Position} ({moved:F2} m from the lifestone's centre, {fromAce:F2} m from ACE's placement)");

        // The live gate's disagreement was 2.22 m; the placement moves the body out of the cylinder by about that much.
        Assert.InRange(moved, 1.5f, 3.0f);
        Assert.True(fromAce <= 0.5f, $"the placement is {fromAce:F2} m from ACE's (81.33, 14.02): Lifestone and ACE would still disagree after a login on the lifestone");
    }

    [RequiresDatsFact]
    public void Without_the_lifestone_as_a_physics_object_the_player_stays_inside_it_as_the_live_client_did()
    {
        var (world, _) = Holtburg(withLifestone: false);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance);
        var arrival = new CellFrame(Cell, new AcFrame(LifestoneOrigin, Heading));

        Assert.True(walker.Place(arrival));
        var placed = walker.Physics.Position.Frame.Origin;
        var moved = Vector2.Distance(new Vector2(placed.X, placed.Y), new Vector2(LifestoneOrigin.X, LifestoneOrigin.Y));
        output.WriteLine($"placed at {walker.Physics.Position} ({moved:F2} m from the lifestone's centre)");
        Assert.True(moved < 0.1f, $"moved {moved:F2} m with nothing but the dat collision there");
    }

    [RequiresDatsFact]
    public void A_lifestone_created_after_the_teleport_holds_the_body_inside_until_the_body_is_placed_again_out_of_it()
    {
        var (world, _) = Holtburg(withLifestone: false);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance);
        var arrival = new CellFrame(Cell, new AcFrame(LifestoneOrigin, Heading));
        Assert.True(walker.Place(arrival));
        var now = Tick(walker, world.CurrentTime, 1.0);
        var host = new ServerObjectPhysicsHost(world, Dats, log: NullLogSink.Instance);
        host.Create(Lifestone(), arrival);

        // The measured finding: the walker's transition finds the body already inside the stone's sphere and holds it, in every heading.
        foreach (var heading in new[] { 0f, 90f, 180f, 270f })
        {
            walker.Physics.SetHeading(heading);
            walker.DoMotion(MotionCommands.WalkForward);
            now = Tick(walker, now, 2.0);
            walker.StopMotion(MotionCommands.WalkForward);
            var held = FlatDistance(walker.Physics.Position.Frame.Origin);
            output.WriteLine($"heading {heading}: after 2 s of walking the body is {held:F2} m from the stone's centre");
            Assert.True(held < 0.1f, $"heading {heading}: the body moved {held:F2} m out of a stone created on it: the placement below proves nothing");
        }

        // Retail's sliding placement (the arrival's own) puts it out where it puts an arrival, and it then walks away freely.
        var moved = walker.ReplaceAfterObjectCreate();
        var placed = FlatDistance(walker.Physics.Position.Frame.Origin);
        output.WriteLine($"placed again: moved {moved:F2} m, {placed:F2} m from the stone's centre");
        Assert.InRange(placed, 1.5f, 3.0f);
        Assert.True(Vector2.Distance(new Vector2(walker.Physics.Position.Frame.Origin.X, walker.Physics.Position.Frame.Origin.Y), AcePlaced) <= 0.5f, "the second placement is not where ACE's is");
        now = Tick(walker, now, 1.0);
        walker.Physics.SetHeading(0f);
        walker.DoMotion(MotionCommands.WalkForward);
        Tick(walker, now, 2.0);
        walker.StopMotion(MotionCommands.WalkForward);
        var away = FlatDistance(walker.Physics.Position.Frame.Origin);
        output.WriteLine($"then walked 2 s north: {away:F2} m from the stone's centre");
        Assert.True(away > placed + 2f, $"the body only reached {away:F2} m from the stone's centre after being placed out");
    }

    [RequiresDatsFact]
    public void The_placement_after_a_create_leaves_a_body_that_is_clear_of_the_object_where_it_is()
    {
        var (world, _) = Holtburg(withLifestone: true);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance);
        var clear = new CellFrame(Cell, new AcFrame(LifestoneOrigin + new Vector3(0f, 6f, 0f), Heading));
        Assert.True(walker.Place(clear));
        Tick(walker, world.CurrentTime, 1.0);
        var before = walker.Physics.Position.Frame.Origin;
        Assert.Equal(0f, walker.ReplaceAfterObjectCreate(), 0.01f);
        Assert.Equal(before.X, walker.Physics.Position.Frame.Origin.X, 0.01f);
        Assert.Equal(before.Y, walker.Physics.Position.Frame.Origin.Y, 0.01f);
    }

    /// <summary>
    /// M5 item 5 F5: the walker's re-placement meets the same refusal as a hosted creature's blip inside Holtburg's building 0x01000C17's hull (an outdoor-cell spot
    /// inside the building, where retail's <c>CTransition::find_placement_position</c>, 11.6096 0x0050CC40, validates no initial placement): it stays where it was, and
    /// the refusal is counted instead of reading as "clear"; on open ground nothing is counted.
    /// </summary>
    [RequiresDatsFact]
    public void A_walker_re_placed_inside_a_buildings_hull_is_refused_and_counted()
    {
        var (world, _) = Holtburg(withLifestone: false);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance);
        var open = new CellFrame(0xA9B40021, new AcFrame(new Vector3(106f, 16f, 94f), Heading));
        Assert.True(walker.Place(open));
        var now = Tick(walker, world.CurrentTime, 0.5);
        Assert.Equal(0f, walker.ReplaceAfterObjectCreate(), 0.01f);
        Assert.Equal(0, walker.ReplacementsRefused);

        var inside = new CellFrame(0xA9B40022, new AcFrame(new Vector3(110f, 30f, 94f), Heading));
        Assert.False(walker.Place(inside), "a placement inside the hull was taken: the scene proves nothing");
        Assert.Equal(SetPositionError.OK, walker.Physics.ForceIntoCell(inside));
        Tick(walker, now, 0.5);
        var before = walker.Physics.Position.Frame.Origin;
        output.WriteLine($"inside the hull: on ground {walker.OnGround} at {before}");
        Assert.True(walker.OnGround, "the walker forced into the hull is not on the ground: the re-placement is never tried");
        Assert.Equal(0f, walker.ReplaceAfterObjectCreate());
        Assert.Equal(1, walker.ReplacementsRefused);
        Assert.Equal(before, walker.Physics.Position.Frame.Origin);
    }

    private static float FlatDistance(Vector3 at) => Vector2.Distance(new Vector2(at.X, at.Y), new Vector2(LifestoneOrigin.X, LifestoneOrigin.Y));

    private static ServerObjectDescription Lifestone() =>
        new(0x7A9B404F, LifestoneSetupId, 1f, (uint)PhysicsState.ReportCollisions, IsCreature: false, IsSelf: false, DescriptionFlags: 0x4000);

    private static double Tick(OfflineWalker walker, double now, double seconds)
    {
        var frames = (int)Math.Round(seconds * 60.0);
        for (var i = 0; i < frames; i++)
        {
            now += 1.0 / 60.0;
            walker.Tick(now);
        }

        return now;
    }

    private (PhysicsWorld World, PhysicsObj? Lifestone) Holtburg(bool withLifestone)
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

        world.UseTime(0.0);
        if (!withLifestone)
        {
            return (world, null);
        }

        var host = new ServerObjectPhysicsHost(world, Dats, log: NullLogSink.Instance);
        host.Create(Lifestone(), new CellFrame(Cell, new AcFrame(LifestoneOrigin with { Z = 94.0f }, Heading)));
        Assert.True(host.TryGetObject(0x7A9B404F, out var lifestone), "the host kept no object for the lifestone");
        Assert.True(lifestone.Setup!.CylSpheres.Count + lifestone.Setup.Spheres.Count > 0, "the lifestone's Setup has no collision shape");
        return (world, lifestone);
    }
}
