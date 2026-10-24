using System.Numerics;
using Lifestone.Data.Motion;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Scenery;
using Lifestone.Net.Types;

namespace Lifestone.Loopback.Tests.Net;

/// <summary>
/// The live net tests' walker and its AutonomousPosition cadence, without a backend or dats (a synthetic landblock): what the live tests rely on to send
/// positions the way retail's <c>ShouldSendPositionEvent</c> would. These run in the default test run.
/// </summary>
public sealed class KinematicWalkerTests
{
    private static readonly LandblockCoord Block = new(0xA9, 0xB4);

    private static TerrainSampler FlatTerrain(float height = 12f)
    {
        var table = new float[256];
        Array.Fill(table, height);
        return new TerrainSampler(new LandblockTerrain(Block, new ushort[TerrainConstants.VertexCount], new byte[TerrainConstants.VertexCount]), table);
    }

    private static KinematicWalker Walker(Vector2 start, params Vector2[] route) =>
        new(FlatTerrain(), Block, start, route, MotionInterpreter.WalkAnimSpeed);

    [Fact]
    public void A_point_outside_the_landblock_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Walker(new Vector2(10f, 10f), new Vector2(192f, 10f)));
        Assert.Throws<ArgumentException>(() => Walker(new Vector2(-1f, 10f), new Vector2(10f, 10f)));
        Assert.Throws<ArgumentException>(() => Walker(new Vector2(10f, 10f)));
    }

    [Fact]
    public void Retail_cadence_waits_strictly_more_than_a_second_and_needs_a_changed_frame()
    {
        var cadence = new PositionCadence();
        var here = new AcPosition(0xA9B40019, new Vector3(84f, 7.1f, 94f), Quaternion.Identity);
        Assert.True(cadence.ShouldSend(0, here, polygon: 24));
        cadence.Sent(0, here, polygon: 24);

        var moved = here with { Origin = new Vector3(84f, 9f, 94f) };
        Assert.False(cadence.ShouldSend(0.5, moved, 24));
        Assert.False(cadence.ShouldSend(1.0, moved, 24));
        Assert.True(cadence.ShouldSend(1.001, moved, 24));
        Assert.False(cadence.ShouldSend(5.0, here, 24), "the same frame is not sent again");
    }

    [Fact]
    public void A_new_cell_or_terrain_triangle_is_sent_at_once_and_any_message_restarts_the_timer()
    {
        var cadence = new PositionCadence();
        var here = new AcPosition(0xA9B40019, new Vector3(84f, 23.9f, 94f), Quaternion.Identity);
        cadence.Sent(0, here, 24);
        Assert.True(cadence.ShouldSend(0.1, here with { CellId = 0xA9B4001A }, 24), "another cell");
        Assert.True(cadence.ShouldSend(0.1, here with { Origin = new Vector3(84f, 23.95f, 94f) }, 25), "another triangle in the same cell");

        // A MoveToState at 0.9 s restarts the 1.0 s timer, whatever kind of message it was.
        var moved = here with { Origin = new Vector3(84f, 22f, 94f) };
        cadence.Sent(0.9, moved, 24);
        Assert.False(cadence.ShouldSend(1.5, here, 24));
        Assert.True(cadence.ShouldSend(1.91, here, 24));
    }

    [Fact]
    public void The_stress_cadence_sends_at_its_interval_and_keeps_the_cell_rule()
    {
        var cadence = new PositionCadence(stressIntervalSeconds: 0.2);
        var here = new AcPosition(0xA9B40019, new Vector3(84f, 7.1f, 94f), Quaternion.Identity);
        cadence.Sent(0, here, 24);
        Assert.False(cadence.ShouldSend(0.19, here, 24));
        Assert.True(cadence.ShouldSend(0.2, here, 24));
        Assert.True(cadence.ShouldSend(0.05, here with { CellId = 0xA9B4001A }, 24));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PositionCadence(0));
    }
}
