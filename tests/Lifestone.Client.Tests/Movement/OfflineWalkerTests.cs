using System.Numerics;
using DatReaderWriter.DBObjs;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Data.World.Scenery;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement;

/// <summary>
/// The explore-mode player (PH6) on the real dats: the human Setup 0x02000001 and motion table 0x09000001 walking,
/// running and jumping on Holtburg's terrain, stopping at a building wall, and teleported into the Training Academy.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class OfflineWalkerTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>The Training Academy's first starting location (CharGen 0x0E000002, StartingAreas[0].Locations[0]).</summary>
    private const uint AcademySpawnCell = 0x860201AD;

    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void Walking_north_for_ten_seconds_follows_the_Holtburg_terrain_on_the_ground()
    {
        var (walker, log) = HoltburgWalker();
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogSeverity.Warning, entry.Severity);
        Assert.Contains("placeholder", entry.Message, StringComparison.Ordinal);

        var terrain = new TerrainSampler(Dats.GetLandblockTerrain(LandblockCoord.Holtburg)!, Dats.TerrainRegion.LandHeightTable);
        var now = Settle(walker, At(60f, 60f, terrain.GetHeight(60f, 60f) + 0.5f), 0.0);
        var start = walker.Position;
        Assert.Equal(WeenieError.None, walker.DoMotion(MotionCommands.WalkForward));

        var frames = 0;
        var worst = 0f;
        for (var i = 0; i < 600; i++)
        {
            now += 1.0 / 60.0;
            walker.Tick(now);
            Assert.True(walker.OnGround, $"off the ground at {walker.Position}");
            var origin = walker.Position.Frame.Origin;

            // Retail sets the lower sphere's lowest point on the terrain triangle's plane (OBJECTINFO::validate_walkable):
            // the origin stays 5 mm above the terrain under it (0.48 - 0.475), on slopes too.
            worst = MathF.Max(worst, MathF.Abs(origin.Z - 0.005f - terrain.GetHeight(origin.X, origin.Y)));
            var plane = terrain.Planes[terrain.FindTerrainPolygon(origin.X, origin.Y)];
            Assert.Equal(plane.Normal, walker.Physics.ContactPlane.Normal);
            frames++;
        }

        var walked = start.GetOffset(walker.Position);
        var pace = new Vector2(walked.X, walked.Y).Length() / 10f;
        output.WriteLine($"walked {walked} in 10 s ({pace:F3} m/s across); worst height error {worst:F6} m over {frames} frames");
        Assert.True(worst < 1e-3f);
        Assert.True(walked.Z < -5f, "the path runs down the slope north of (60, 60)");
        Assert.Equal(0f, walked.X, 3);

        // The human walk cycle's root motion, not the interpreter's 3.12 m/s (docs/avatars.md finding): about 2.54 m/s.
        Assert.InRange(pace, 2.4f, 2.7f);
    }

    [RequiresDatsFact]
    public void Running_moves_at_the_run_cycle_times_the_placeholder_run_rate()
    {
        var (walker, _) = HoltburgWalker();
        var now = Settle(walker, At(96f, 20f, 95f), 0.0);
        walker.Physics.SetHeading(90f);
        Assert.True(walker.Weenie.TryGetRunRate(out var rate));
        Assert.Equal(MovementSystem.GetRunRate(0f, OfflinePlayerWeenie.PlaceholderRunSkill, 1f), rate, 5);

        walker.SetHoldRun(true);
        walker.DoMotion(MotionCommands.WalkForward);
        Assert.Equal(MotionCommands.RunForward, walker.Motion.Interpreted.ForwardCommand);
        Assert.Equal(rate, walker.Motion.Interpreted.ForwardSpeed, 5);
        now = Tick(walker, now, 1.0);
        var start = walker.Position;
        Tick(walker, now, 3.0);
        var pace = start.GetOffset(walker.Position).Length() / 3f;
        output.WriteLine($"ran at {pace:F3} m/s with run rate {rate:F4}");
        Assert.True(walker.OnGround);
        Assert.InRange(pace, rate * 3.5f, rate * 4.5f);
    }

    [RequiresDatsFact]
    public void A_full_jump_rises_by_the_placeholder_jump_height_and_lands()
    {
        var (walker, _) = HoltburgWalker();
        var now = Settle(walker, At(96f, 20f, 95f), 0.0);
        var ground = walker.Position.Frame.Origin.Z;
        var height = (float)MovementSystem.GetJumpHeight(0f, OfflinePlayerWeenie.PlaceholderJumpSkill, 1f, 1f);

        Assert.Equal(WeenieError.None, walker.Jump(1f));
        Assert.False(walker.OnGround);
        Assert.Equal(MotionCommands.Falling, walker.Physics.MotionPlayer!.State.Substate);
        var apex = ground;
        for (var i = 0; i < 180; i++)
        {
            now += 1.0 / 60.0;
            walker.Tick(now);
            apex = MathF.Max(apex, walker.Position.Frame.Origin.Z);
        }

        output.WriteLine($"apex {apex - ground:F4} m, jump height {height:F4} m");
        Assert.Equal(height, apex - ground, 0.02f);
        Assert.True(walker.OnGround);
        Assert.Equal(ground, walker.Position.Frame.Origin.Z, 3);
        Assert.Equal(MotionCommands.Ready, walker.Physics.MotionPlayer.State.Substate);
    }

    [RequiresDatsFact]
    public void Walking_into_a_Holtburg_building_wall_stops_without_entering_it()
    {
        var (walker, _) = HoltburgWalker();
        var building = walker.World.GetLandblock(LandblockCoord.Holtburg)!.Buildings.First(b => b.Part.Shape.Physics is not null);
        var physics = building.Part.Shape.Physics!;
        var frame = building.Position.Frame;
        var wall = physics.Polygons
            .Where(p => MathF.Abs(p.Plane.Normal.Z) < 0.05f)
            .OrderByDescending(Area)
            .First();
        var normal = frame.LocalToGlobalVec(wall.Plane.Normal);
        var centre = frame.LocalToGlobal(wall.Vertices.ToArray().Aggregate(Vector3.Zero, (s, v) => s + v) / wall.NumPoints);
        var start = centre + (normal * 3f) + new Vector3(0, 0, 1f);
        var now = Settle(walker, At(start.X, start.Y, start.Z), 0.0);
        walker.Physics.SetHeading((float)((Math.Atan2(-normal.X, -normal.Y) * 180.0 / Math.PI) + 360.0) % 360f);
        walker.DoMotion(MotionCommands.WalkForward);
        now = Tick(walker, now, 3.0);
        var stopped = walker.Position;
        Tick(walker, now, 1.0);

        output.WriteLine($"stopped at {walker.Position}; wall centre {centre}");
        Assert.Equal(stopped, walker.Position);
        Assert.True(walker.OnGround);
        Assert.True(Vector3.Distance(walker.Position.Frame.Origin, centre) < 3.5f, $"walker at {walker.Position}, wall at {centre}");
        foreach (var height in new[] { 0.475f, 1.35f })
        {
            var sphere = new PhysSphere(frame.GlobalToLocal(walker.Position.Frame.Origin + new Vector3(0, 0, height)), 0.48f - (2 * PhysicsGlobals.Epsilon));
            Assert.DoesNotContain(physics.Polygons, p => p.HitsSphere(sphere));
        }
    }

    [RequiresDatsFact]
    public void A_teleport_into_the_Academy_spawn_lands_on_its_floor_and_walks_indoors()
    {
        Assert.True(Dats.TryGetPortal<CharGen>(0x0E000002, out var charGen));
        var spawn = charGen!.StartingAreas[0].Locations[0];
        Assert.Equal(AcademySpawnCell, spawn.CellId);
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(Dats).Build(LandblockCoord.FromId(AcademySpawnCell)));
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance);
        world.UseTime(0.0);
        var at = new CellFrame(spawn.CellId, new AcFrame(spawn.Frame.Origin, spawn.Frame.Orientation)).Normalized();

        Assert.Equal(SetPositionError.OK, walker.Physics.SetPositionSimple(at, sliding: true));
        var now = Tick(walker, 0.0, 1.0);
        output.WriteLine($"spawned at {at}; rests at {walker.Position}");
        Assert.True(CellIds.IsEnvCell(walker.Position.CellId));
        Assert.True(walker.OnGround);
        Assert.Equal(at.Frame.Origin.X, walker.Position.Frame.Origin.X, 2);
        Assert.Equal(at.Frame.Origin.Y, walker.Position.Frame.Origin.Y, 2);
        Assert.InRange(walker.Position.Frame.Origin.Z, at.Frame.Origin.Z - 0.6f, at.Frame.Origin.Z + 0.1f);

        // Two seconds forward, still indoors and on a floor.
        walker.DoMotion(MotionCommands.WalkForward);
        Tick(walker, now, 2.0);
        output.WriteLine($"walked to {walker.Position}");
        Assert.True(CellIds.IsEnvCell(walker.Position.CellId));
        Assert.True(walker.OnGround);
        Assert.True(at.GetOffset(walker.Position).Length() > 1f);
    }

    /// <summary>A walker in a world of Holtburg and its eight neighbours, with what it logged.</summary>
    private (OfflineWalker Walker, RecordingLogSink Log) HoltburgWalker()
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

        var log = new RecordingLogSink();
        var walker = OfflineWalker.Create(Dats, world, log);
        Assert.Same(walker.Physics, world.Player);
        world.UseTime(0.0);
        return (walker, log);
    }

    /// <summary>Places the walker and lets it come to rest for a second; answers the clock.</summary>
    private static double Settle(OfflineWalker walker, in CellFrame at, double now)
    {
        Assert.True(walker.Place(at), $"placement at {at}");
        now = Tick(walker, now, 1.0);
        Assert.True(walker.OnGround, $"not on the ground at {walker.Position}");
        return now;
    }

    /// <summary>Ticks the walker at 60 frames a second for <paramref name="seconds"/>; answers the clock.</summary>
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

    /// <summary>A position in Holtburg's land cell under (<paramref name="x"/>, <paramref name="y"/>), facing north.</summary>
    private static CellFrame At(float x, float y, float z) =>
        new(CellIds.LandCellAt(LandblockCoord.Holtburg, x, y)!.Value, new AcFrame(new Vector3(x, y, z), Quaternion.Identity));

    private static float Area(PhysPolygon polygon)
    {
        var vertices = polygon.Vertices;
        var sum = Vector3.Zero;
        for (var i = 1; i + 1 < vertices.Length; i++)
        {
            sum += Vector3.Cross(vertices[i] - vertices[0], vertices[i + 1] - vertices[0]);
        }

        return sum.Length() * 0.5f;
    }
}
