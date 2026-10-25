using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
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
/// Retail's client runs the same placement on its own player (<c>CPhysicsObj::enter_world</c>, 11.6096 0x00516E10), so it
/// would land where ACE did. These tests pin that Lifestone's placement does too once the lifestone is a physics object in
/// the player's world, which no host makes yet (<c>TODO(d25-server-object-host)</c>, docs/physics.md "Other objects").
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

        var setup = PhysicsSetup.Load(Dats, LifestoneSetupId, new PhysicsGeometryLibrary(Dats));
        Assert.NotNull(setup);
        Assert.True(setup!.CylSpheres.Count + setup.Spheres.Count > 0, "the lifestone's Setup has no collision shape");
        var lifestone = new PhysicsObj(world, setup, id: 0x7A9B404F);
        Assert.Equal(SetPositionError.OK, lifestone.ForceIntoCell(new CellFrame(Cell, new AcFrame(LifestoneOrigin with { Z = 94.0f }, Heading))));
        return (world, lifestone);
    }
}
