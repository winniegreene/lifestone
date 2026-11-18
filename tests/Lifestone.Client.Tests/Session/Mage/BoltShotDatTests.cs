using System.Diagnostics;
using System.Numerics;
using Lifestone.Client.Session.Casting;
using Lifestone.Data.Animation;
using Lifestone.Data.Magic;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Scenery;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 8 L: the shot check's sums and its sweep on the real dats. The live number that fixes the start point: at 20:15:44 UTC of round 2 each Flame Bolt I was created 0.9 m from the player, aimed at a Drudge Skulker that had fallen from the ledge and stood 6.0 m away, almost straight below.
/// </summary>
public sealed class BoltShotDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint HumanSetup = 0x02000001;
    private const uint FlameBolt = 27;
    private const uint DrudgeSetup = 33556445;
    private const float DrudgeScale = 0.95f; // weenie 7, PropertyFloat 39 (DefaultScale), SELECT in the pinned world

    private PhysicsWorld Den()
    {
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(fixture.Dats).Build(new LandblockCoord(1, 5)));
        return world;
    }

    private static CellFrame At(uint cell, float x, float y, float z) => new(cell, new AcFrame(new Vector3(x, y, z), Quaternion.Identity));

    private (ShotBody Me, ShotBody It, BoltBody Bolt) Bodies()
    {
        var geometry = new PhysicsGeometryLibrary(fixture.Dats);
        var human = PhysicsSetup.Load(fixture.Dats, HumanSetup, geometry)!;
        var drudge = PhysicsSetup.Load(fixture.Dats, DrudgeSetup, geometry)!;
        var entry = AceWorldExtract.Load().ProjectilesBySpell[FlameBolt];
        var bolt = PhysicsSetup.Load(fixture.Dats, entry.SetupId, geometry)!;
        var scale = entry.DefaultScale ?? 1f;
        var me = new ShotBody(human.Height, human.CylSpheres.Count > 0 ? human.CylSpheres[0].Radius : human.Spheres[0].Radius, human.Radius);
        var it = new ShotBody(drudge.Height * DrudgeScale, (drudge.CylSpheres.Count > 0 ? drudge.CylSpheres[0].Radius : drudge.Spheres[0].Radius) * DrudgeScale, drudge.Radius * DrudgeScale);
        return (me, it, new BoltBody(new PhysSphere(bolt.Spheres[0].Center * scale, bolt.Spheres[0].Radius * scale), bolt.Height * scale));
    }

    [RequiresDatsFact]
    public void The_bolts_start_for_a_target_almost_straight_below_is_the_live_0_9_m_and_its_flat_heading_points_back_at_the_player()
    {
        var (me, it, bolt) = Bodies();
        var caster = At(0x01050383, 100f, -170f, 0f);
        var target = At(0x01050383, 100.3f, -170f, -6f); // 6.0 m away, 0.3 m across

        var start = BoltLaunch.Start(in caster, in me, in target, in it, in bolt);

        var offset = start.Frame.Origin - caster.Frame.Origin;
        output.WriteLine($"me {me} it {it} bolt r={bolt.Sphere.Radius} h={bolt.Height}: start offset {offset} = {offset.Length():0.00} m");
        Assert.InRange(offset.Length(), 0.75f, 1.05f);
        Assert.True(offset.X > 0.3f, "the bolt starts past the target's flat spot, so its flat heading points back: the tracker's 'it flies toward the player'");
    }

    [RequiresDatsFact]
    public void The_sweep_is_blocked_through_rock_and_through_the_floor_and_clear_in_open_air_and_a_check_costs_well_under_a_frame()
    {
        var world = Den();
        var (me, it, bolt) = Bodies();
        var arrival = At(0x01050375, 100.43f, -208.92f, 0.03f);
        var behindRock = At(0x01050383, 116.7f, -167.9f, 0.06f);
        var plazaA = At(0x01050383, 116.58f, -170.50f, 0.06f);
        var plazaB = At(0x01050383, 115.8f, -176.3f, 0.06f);
        var below = At(0x01050383, 116.9f, -170.50f, -5.94f);

        Assert.Equal(ShotAnswer.Blocked, PhysicsShotCheck.Sweep(world, in arrival, in me, in behindRock, in it, in bolt).Answer);
        Assert.Equal(ShotAnswer.Blocked, PhysicsShotCheck.Sweep(world, in plazaA, in me, in below, in it, in bolt).Answer);
        var clear = PhysicsShotCheck.Sweep(world, in plazaA, in me, in plazaB, in it, in bolt);
        Assert.True(clear.Answer == ShotAnswer.Clear, clear.Why);

        const int Rounds = 300;
        var began = Stopwatch.GetTimestamp();
        for (var i = 0; i < Rounds; i++)
        {
            PhysicsShotCheck.Sweep(world, in arrival, in me, in behindRock, in it, in bolt);
            PhysicsShotCheck.Sweep(world, in plazaA, in me, in plazaB, in it, in bolt);
        }

        var ms = Stopwatch.GetElapsedTime(began).TotalMilliseconds / (2 * Rounds);
        output.WriteLine($"mean cost of one check: {ms:0.000} ms");
        Assert.True(ms < 8.0, $"one check took {ms:0.000} ms on average (a bound for a loaded machine, not a target)");
    }

    /// <summary>M6 8 L: outdoors the check must not refuse a fair shot: the live hill of gate A (a bolt launched, then dead 44 m out; OutdoorLineDatTests) is blocked on the flight line, and a shot along the same ground short of the hill is clear.</summary>
    [RequiresDatsFact]
    public void Outdoors_the_live_hill_is_blocked_on_the_flight_line_and_a_fair_shot_short_of_it_is_clear()
    {
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(fixture.Dats).Build(new LandblockCoord(0xAA, 0xB3)));
        var (me, it, bolt) = Bodies();
        var from = At(0xAAB30008, 3.8f, 171.4f, 100.8f);
        var far = At(0xAAB30000 | 11, 47.7f, 119.6f, 111.7f);
        var sampler = new TerrainSampler(fixture.Dats.GetLandblockTerrain(new LandblockCoord(0xAA, 0xB3))!, fixture.Dats.TerrainRegion.LandHeightTable);
        var near = At(0xAAB30008, 9.0f, 165.3f, sampler.GetHeight(9.0f, 165.3f));

        var hill = PhysicsShotCheck.Sweep(world, in from, in me, in far, in it, in bolt);
        var fair = PhysicsShotCheck.Sweep(world, in from, in me, in near, in it, in bolt);
        output.WriteLine($"hill: {hill.Answer}: {hill.Why}; fair: {fair.Answer}: {fair.Why}");

        Assert.Equal(ShotAnswer.Blocked, hill.Answer);
        Assert.Equal(ShotAnswer.Clear, fair.Answer);
    }

    [Fact]
    public void A_target_on_the_level_starts_two_radii_out_along_the_heading_at_two_thirds_height()
    {
        var me = new ShotBody(1.8f, 0.35f, 0.5f);
        var it = new ShotBody(1.8f, 0.35f, 0.5f);
        var bolt = new BoltBody(new PhysSphere(Vector3.Zero, 0.25f), 0.5f);
        var caster = At(0x01050383, 10f, 10f, 0f);
        var target = At(0x01050383, 10f, 20f, 0f);

        var start = BoltLaunch.Start(in caster, in me, in target, in it, in bolt);
        var end = BoltLaunch.End(in start, in target, in it, in bolt);

        Assert.Equal(10f, start.Frame.Origin.X, 3);
        Assert.Equal(10f + (2f * 0.6f), start.Frame.Origin.Y, 3);
        Assert.Equal(1.2f, start.Frame.Origin.Z, 3);
        Assert.Equal(1.3f, end.Frame.Origin.Z, 1); // the eye: its height less the bolt's, pulled back along the path by the two radii
        Assert.True(end.Frame.Origin.Y < 20f && end.Frame.Origin.Y > 19f);
    }
}
