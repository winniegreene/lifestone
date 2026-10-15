using System.Numerics;
using Lifestone.Client.Tests;
using Lifestone.Data.Physics;
using static Lifestone.Data.Tests.Physics.PhysicsTestKit;

namespace Lifestone.Data.Tests.Physics;

/// <summary>
/// A transition allocates nothing once warm (m2-plan PH3): positions, spheres and cell arrays are reused, and no query
/// builds a list or a closure. Measured on this thread in the non-parallel allocation collection; a window with a GC
/// inside is not judged.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class PhysicsAllocationTests
{
    [Fact]
    public void Walking_into_walls_up_steps_and_along_a_ledge_allocates_nothing_once_warm()
    {
        var world = WorldOf(Flat(
            Block,
            Box(Block, 100.1f, 100f, 0f, new Vector3(0.2f, 10f, 3f)),
            Box(Block, 96f, 108f, 0f, new Vector3(4f, 4f, 0.3f))));
        var body = new PhysicsTestBody(world);
        var sweepFrom = At(Block, 90f, 100f, 1.5f);
        var sweepTo = sweepFrom.AddOffset(new Vector3(12f, 0f, 0f));

        void Walk()
        {
            body.Place(At(Block, 94f, 96f, 0f));
            for (var i = 0; i < 40; i++)
            {
                body.MoveBy(new Vector3(0.2f, 0.15f, 0f));
            }

            world.SweepViewer(sweepFrom, sweepTo, 0.3f);
        }

        Walk();
        Walk();
        var judged = 0;
        for (var attempt = 0; attempt < 10 && judged < 3; attempt++)
        {
            var window = AllocationWindow.Open();
            Walk();
            var (bytes, gcInside) = window.Close();
            if (gcInside)
            {
                continue;
            }

            judged++;
            Assert.Equal(0, bytes);
        }

        Assert.True(judged > 0, "every measured window had a GC inside");
    }
}
