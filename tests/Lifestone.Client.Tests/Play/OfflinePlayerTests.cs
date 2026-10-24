using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Options;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Data.Animation;
using Lifestone.Data.Appearance;
using Lifestone.Data.Dat;
using Lifestone.Data.Models;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.Textures;
using Lifestone.Data.World;
using Lifestone.Data.World.Scenery;

namespace Lifestone.Client.Tests.Play;

/// <summary>
/// The explore-mode player of M2 G5 (<c>--offline-player</c>) as the game builds it, on the real dats: the stand-in's look through the
/// CharGen composition, the avatar on the human Setup and motion table, the physics walker hosted on the avatar's own part array,
/// the avatar's draw output of the standing and the walking character. The Godot half (lights, batches) is
/// <c>--interior-engine-checks</c> and the off-screen run.
/// </summary>
public sealed class OfflinePlayerTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private DatSet Dats => fixture.Dats;

    [Fact]
    public void The_option_is_off_unless_given()
    {
        Assert.False(AvatarsOptions.Parse(["--start=holtburg", "--walk"]).OfflinePlayer);
        Assert.True(AvatarsOptions.Parse(["--start=holtburg", "--offline-player"]).OfflinePlayer);
        Assert.Contains(OptionCatalog.Avatars().Options, o => o.Name == "--offline-player");
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void The_stand_in_is_an_aluvian_on_the_human_setup_and_motion_table_with_every_appearance_change_applied()
    {
        var (_, composition, look) = Compose();
        Assert.Equal(OfflinePlayerLook.Heritage, look.Heritage);
        Assert.Equal(OfflinePlayerLook.Sex, look.Sex);
        Assert.Equal(OfflineWalker.HumanSetupId, composition.SetupId);
        Assert.Equal(OfflineWalker.HumanMotionTableId, composition.MotionTableId);
        Assert.True(composition.Result.AllApplied, string.Join("; ", composition.Result.Problems));
        Assert.NotEqual(GearChoice.None, look.Shirt);
        Assert.NotEqual(GearChoice.None, look.Pants);
        Assert.NotEqual(GearChoice.None, look.Footwear);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void The_look_is_the_same_every_time()
    {
        var (data, _, first) = Compose();
        Assert.Equal(first, OfflinePlayerLook.Create(data.Record, data.Dats));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void The_walker_is_hosted_on_the_avatars_part_array_and_the_avatar_draws_its_pose()
    {
        var (data, composition, _) = Compose();
        var terrain = new TerrainSampler(Dats.GetLandblockTerrain(LandblockCoord.Holtburg)!, Dats.TerrainRegion.LandHeightTable);
        var spawn = new CellFrame(0xA9B40019, new AcFrame(new Vector3(84f, 7.1f, terrain.GetHeight(84f, 7.1f)), Quaternion.Identity));
        var avatars = new AvatarSet(data.Sources);
        Assert.Equal(AvatarApply.Applied, avatars.Create(OfflinePlayerLook.Describe(composition, spawn), now: 0.0));
        var player = avatars.Player!;
        Assert.NotNull(player.Parts);

        var builder = new LandblockCollisionBuilder(Dats);
        var world = new PhysicsWorld();
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                world.Attach(builder.Build(LandblockCoord.Holtburg.Offset(dx, dy)!.Value));
            }
        }

        world.SetTime(0.0);
        var log = new RecordingLogSink();
        var walker = OfflineWalker.Create(Dats, world, log, parts: player.Parts, id: OfflinePlayerLook.PlayerGuid);
        var events = new PlayerPhysicsEvents(player);
        walker.Physics.Events = events;
        walker.Physics.TargetWorld = avatars;
        avatars.PlayerBody = walker.Body;
        Assert.Same(player.Parts, walker.Physics.MotionPlayer);
        Assert.Equal(OfflinePlayerLook.PlayerGuid, walker.Physics.Id);
        Assert.Same(walker.Body, avatars.PlayerBody);
        Assert.True(walker.Place(spawn));

        // Standing: on the ground, drawn, in its cell, every part of the Setup with a mesh.
        var now = Run(walker, avatars, 0.0, 2.0);
        Assert.True(walker.OnGround, $"not on the ground at {walker.Position}");
        var draw = Assert.Single(avatars.Frame.Avatars.ToArray(), d => d.IsPlayer);
        Assert.True(draw.Visible);
        Assert.Equal(walker.Position.CellId, draw.CellId);
        Assert.False(draw.InEnvCell);
        Assert.Equal(player.Body.Setup.Parts.Count, draw.PartCount);
        var parts = avatars.Frame.Parts.Slice(draw.PartStart, draw.PartCount);
        Assert.True(parts.ToArray().Count(p => p.GfxObjId != 0) >= 10, "the character has parts to draw");
        Assert.Equal(0, avatars.Frame.PartTranslucency.Slice(draw.PartStart, draw.PartCount).ToArray().Count(t => t != 0f));
        var standingPose = draw.PoseVersion;

        // Walking: it moves, and the avatar's pose changes with the walk cycle.
        var start = walker.Position;
        Assert.Equal(WeenieError.None, walker.DoMotion(MotionCommands.WalkForward));
        now = Run(walker, avatars, now, 2.0);
        var walked = start.GetOffset(walker.Position);
        Assert.InRange(new Vector2(walked.X, walked.Y).Length(), 3f, 7f);
        Assert.True(walker.OnGround);
        var walking = Assert.Single(avatars.Frame.Avatars.ToArray(), d => d.IsPlayer);
        Assert.True(walking.PoseVersion > standingPose + 30, "the walk cycle poses the parts every frame");
        Assert.Equal(LogSeverity.Warning, Assert.Single(log.Entries, e => e.Severity >= LogSeverity.Warning).Severity); // the placeholder skills, loud once
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void The_player_in_a_dungeon_cell_draws_in_its_cell_and_streams_indoor()
    {
        var (data, composition, _) = Compose();
        var spawn = new CellFrame(0x00070143, new AcFrame(new Vector3(70f, -60f, 0.005f), Quaternion.Identity));
        var avatars = new AvatarSet(data.Sources);
        Assert.Equal(AvatarApply.Applied, avatars.Create(OfflinePlayerLook.Describe(composition, spawn), now: 0.0));
        avatars.Advance(0.1, new AvatarViewer(spawn), long.MaxValue);
        var draw = Assert.Single(avatars.Frame.Avatars.ToArray(), d => d.IsPlayer);
        Assert.True(draw.InEnvCell);
        Assert.True(draw.Visible);
        var focus = new PlayerStreamingFocus(id => Lifestone.Data.World.Cells.EnvCellData.Read(Dats, id)?.SeenOutside);
        Assert.Equal(Lifestone.Client.Streaming.StreamingFocusKind.Indoor, focus.Of(spawn).Kind);
    }

    private (Cg3DViewData Data, CharGenComposition Composition, CharGenLook Look) Compose()
    {
        var data = Cg3DViewData.FromDats(Dats, new TextureLoader(Dats), new GfxObjMeshCache(Dats));
        var look = OfflinePlayerLook.Create(data.Record, data.Dats);
        return (data, CharGenAppearance.Compose(data.Record, look, data.Dats), look);
    }

    /// <summary>Runs the frame loop of the game at 60 frames a second for <paramref name="seconds"/>; answers the clock.</summary>
    private static double Run(OfflineWalker walker, AvatarSet avatars, double now, double seconds)
    {
        var frames = (int)Math.Round(seconds * 60.0);
        for (var i = 0; i < frames; i++)
        {
            now += 1.0 / 60.0;
            walker.Controller.Tick(now);
            avatars.Advance(now, new AvatarViewer(walker.Position), long.MaxValue);
        }

        return now;
    }
}
