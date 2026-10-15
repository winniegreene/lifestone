using Godot;
using Lifestone.Client.Avatars;
using Lifestone.Client.Camera;
using Lifestone.Client.Movement;
using Lifestone.Client.Options;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Appearance;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Game.Core;
using Lifestone.Game.World;
using Lifestone.Game.World.Interiors;
using Lifestone.Game.World.ServerObjects;

namespace Lifestone.Game.Player;

/// <summary>
/// <c>--offline-player</c>: explore mode with your own character (M2 G5). One call from <c>Main._Ready</c>, after the world, the camera
/// and the interiors are up, builds the player the way a logged-in one is built and wires it in:
/// <list type="number">
/// <item>the stand-in's look (<see cref="OfflinePlayerLook"/>) through <see cref="CharGenAppearance.Compose"/> and the avatars
/// track (<see cref="AvatarSet"/>, <see cref="PlayerAvatar"/>);</item>
/// <item>a <see cref="PhysicsWorld"/> whose collision around the player is streamed by a <see cref="CollisionStreamer"/> pumped inside the
/// frame budget (no <see cref="ILandblockLayer"/>), and an <see cref="OfflineWalker"/> hosted on the avatar's own part array;</item>
/// <item>the character's meshes and shader variants, built before the first frame (<see cref="PlayerView.Prepare"/>);</item>
/// <item>the world's streaming focus: the player (<see cref="PlayerFocus"/>), with the dungeon rule;</item>
/// <item>the on-screen label that says the character is a placeholder, and the performance overlay's rows.</item>
/// </list>
/// Nothing of this exists without the option: M1's explore mode is the free camera and its keys, unchanged.
/// </summary>
/// <remarks>
/// <para><b>Walking:</b> the keys are retail's (the game key map's movement map 4, <see cref="Lifestone.Client.Play.MovementBindings"/>) through the
/// input router (<see cref="PlayerInput"/>) to the movement controller, whose messages a dry-run sink logs and drops; the camera is retail's chase
/// camera (<see cref="ChaseCamera"/>) or first person (<c>--player-camera</c>), swept through collision; the character is drawn interpolated
/// between physics steps (<c>--motion-stepping=retail</c> restores stepping, <c>--physics-overlay</c> shows the physics).</para>
/// <para><b>Placeholders, labelled on screen:</b> the stand-in character's look, its Run and Jump skills (the walker's), and the run-by-default option
/// (<c>TODO(g5-run-by-default)</c>: retail's per-character default is not read, so the player runs and Left Shift walks). Each is also a logged
/// TODO: this one at install, the first-person turn and the mouse-look preferences when the player first uses them.</para>
/// <para><b>The spawn</b> is the start location (<c>--start</c>, Holtburg's drop point by default) at its heading: the spot the overview camera
/// looks at, or the spot <c>--walk</c> stands on. The chase camera is behind it; the character stands in its ready state, lit by the sun
/// outdoors or by the cell's lights indoors, until a key moves it.</para>
/// </remarks>
public static class OfflinePlayerSetup
{
    /// <summary>True when the command line asks for the offline player.</summary>
    public static bool Requested() => AvatarsOptions.Parse(OS.GetCmdlineUserArgs()).OfflinePlayer;

    /// <summary>
    /// Builds and installs the player. Returns null (after logging why) when it cannot be built, in which case explore mode is M1's.
    /// </summary>
    public static PlayerController? Install(Node main, GameServices services, WorldRoot world, InteriorController? interiors, WorldLocation start, FlyCamera camera)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(start);
        var dats = services.Dats ?? throw new InvalidOperationException("the player needs the dats");
        var log = GodotLogSink.Instance;
        var timer = System.Diagnostics.Stopwatch.StartNew();

        Cg3DViewData data;
        CharGenComposition composition;
        try
        {
            data = Cg3DViewData.FromDats(dats, services.Textures, services.GfxObjMeshes);
            var look = OfflinePlayerLook.Create(data.Record, data.Dats);
            composition = CharGenAppearance.Compose(data.Record, look, data.Dats);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException)
        {
            Log.Error("play", "--offline-player: the stand-in character cannot be composed; explore mode stays M1's", ("error", ex.Message));
            return null;
        }

        if (!composition.Result.AllApplied)
        {
            Log.Warn("play", "the stand-in's appearance has problems (retail ignores them)", ("problems", string.Join("; ", composition.Result.Problems)));
        }

        if (composition.SetupId != OfflineWalker.HumanSetupId || composition.MotionTableId != OfflineWalker.HumanMotionTableId)
        {
            Log.Warn(
                "play",
                "the stand-in is not on the human Setup and motion table the offline walker's collision is built from",
                ("setup", $"0x{composition.SetupId:X8}"),
                ("motion_table", $"0x{composition.MotionTableId:X8}"));
        }

        var spawn = SpawnOf(services, start);
        var rig = PlayerRig.Build(new PlayerRigRequest(
            main,
            services,
            world,
            interiors,
            camera,
            data.Sources,
            OfflinePlayerLook.Describe(composition, spawn),
            spawn,
            OfflinePlayerLook.PlayerGuid,
            new DryRunMovementSink(GodotLogSink.Instance),
            OfflinePlayerLook.Label));
        if (rig is null)
        {
            Log.Error("play", "--offline-player: the avatar set refused the stand-in; explore mode stays M1's");
            return null;
        }

        Log.Info(
            "play",
            "offline player installed (stand-in character, placeholder look)",
            ("cell", $"0x{spawn.CellId:X8}"),
            ("at", spawn.Origin),
            ("setup", $"0x{composition.SetupId:X8}"),
            ("motion_table", $"0x{composition.MotionTableId:X8}"),
            ("ms", timer.Elapsed.TotalMilliseconds));
        return rig.Controller;
    }

    /// <summary>The start location as a placed position: the cell, the local origin (the ground's height when the location has none) and the heading.</summary>
    internal static CellFrame SpawnOf(GameServices services, WorldLocation start)
    {
        var local = start.Position.Local;
        if (!start.HasHeight && AcCoordinates.IsOutdoorCell(start.Position.CellId) && services.Ground.TryGetHeight(start.Global, out var ground))
        {
            local = local with { Z = (float)ground };
        }

        return new CellFrame(start.Position.CellId, new AcFrame(local, PositionFrames.WithHeading(System.Numerics.Quaternion.Identity, (float)start.HeadingDegrees)));
    }
}
