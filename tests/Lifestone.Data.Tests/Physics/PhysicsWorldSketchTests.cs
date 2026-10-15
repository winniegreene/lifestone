using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.Data.Tests.Physics;

/// <summary>
/// The C-physics contract of <see cref="PhysicsWorld"/>: attaching and detaching work, and cells of unattached landblocks
/// are null, so the camera sweep is blocked and the ground probe (PH8, <see cref="GroundProbeTests"/>) finds nothing.
/// </summary>
public sealed class PhysicsWorldSketchTests
{
    private static readonly CellFrame HoltburgCell = new(0xA9B40019, new AcFrame(new Vector3(84f, 7f, 94f), Quaternion.Identity));

    [Fact]
    public void Attach_and_detach_track_the_attached_landblocks()
    {
        var world = new PhysicsWorld();
        Assert.Same(PhysicsCompat.Retail, world.Compat);
        Assert.False(world.IsAttached(LandblockCoord.Holtburg));

        world.Attach(new LandblockCollision(LandblockCoord.Holtburg));
        world.Attach(new LandblockCollision(LandblockCoord.Holtburg));
        Assert.True(world.IsAttached(LandblockCoord.Holtburg));
        Assert.Equal(1, world.AttachedCount);

        Assert.True(world.Detach(LandblockCoord.Holtburg));
        Assert.False(world.Detach(LandblockCoord.Holtburg));
        Assert.Equal(0, world.AttachedCount);
    }

    [Fact]
    public void A_world_runs_with_the_compat_switches_it_was_given()
    {
        Assert.Same(PhysicsCompat.Ace, new PhysicsWorld(PhysicsCompat.Ace).Compat);
    }

    [Fact]
    public void Cells_of_landblocks_that_are_not_attached_are_null()
    {
        var world = new PhysicsWorld();
        Assert.Null(world.GetCell(HoltburgCell.CellId));
        Assert.Null(world.GetCell(0));

        var sweep = world.SweepViewer(HoltburgCell, HoltburgCell, 0.3f);
        Assert.True(sweep.Blocked);
        Assert.Equal(HoltburgCell, sweep.Position);
    }

    [Fact]
    public void The_ground_probe_of_an_empty_world_finds_nothing()
    {
        IGroundProbe probe = new PhysicsWorld();
        Assert.False(probe.TryGroundZ(HoltburgCell, 0.48f, out var z, out var contact));
        Assert.Equal(0f, z);
        Assert.Equal(default, contact);
    }
}
