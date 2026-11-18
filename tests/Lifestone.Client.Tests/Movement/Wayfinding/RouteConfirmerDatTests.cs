using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Movement.MovementTestKit;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// The route check on real ground (skips without the dats): spots the world walk's long walks showed it refusing wrongly, each walked two metres straight through. The refused sample is the path's middle; at the spot itself no probe of the body's size
/// finds ground, so these reach the side look, which flat test terrain cannot.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class RouteConfirmerDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private static NavWaypoint W(uint block, float x, float y, float z) =>
        new(new CellFrame(CellIds.LandCellAt(new LandblockCoord((byte)(block >> 24), (byte)(block >> 16)), x, y)!.Value, new AcFrame(new Vector3(x, y, z), Quaternion.Identity)), false);

    /// <param name="block">The landblock.</param>
    /// <param name="fromX">The start, one metre before the spot.</param>
    /// <param name="fromY">The start.</param>
    /// <param name="fromZ">The ground at the start.</param>
    /// <param name="toX">The end, one metre past the spot.</param>
    /// <param name="toY">The end.</param>
    /// <param name="toZ">The ground at the end.</param>
    [RequiresDatsTheory]
    [InlineData(0x21290000u, 121.177f, 120.123f, 22.05f, 119.763f, 121.537f, 23.18f)] // the tip of a steep land triangle, half a metre from a land corner
    [InlineData(0x2B2D0000u, 146.54f, 96.82f, 78.5f, 148.54f, 96.82f, 78.83f)] // a static's edge the foot sphere clips on level ground
    public void A_refused_spot_a_body_is_carried_past_confirms_by_the_side_look(uint block, float fromX, float fromY, float fromZ, float toX, float toY, float toZ)
    {
        var geometry = new PhysicsGeometryLibrary(fixture.Dats);
        var builder = new LandblockCollisionBuilder(fixture.Dats, geometry, new CellStructureLibrary(fixture.Dats));
        var world = new PhysicsWorld(PhysicsCompat.Ace);
        var coord = new LandblockCoord((byte)(block >> 24), (byte)(block >> 16));
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (coord.Offset(dx, dy) is { } near && LandDefs.IsOnMap(near))
                {
                    world.Attach(builder.Build(near));
                }
            }
        }

        using var confirmer = new RouteConfirmer(world, HumanSetup);
        var status = confirmer.ConfirmAll([W(block, fromX, fromY, fromZ), W(block, toX, toY, toZ)]);
        output.WriteLine($"{status}: start lifts {confirmer.StartLifts}, side looks {confirmer.SidesTaken}, {confirmer.Failure}");
        Assert.True(status == RouteConfirmStatus.Confirmed, confirmer.Failure?.ToString());
        Assert.True(confirmer.SidesTaken > 0, "the spot needs the side look, or this test proves nothing");
    }
}
