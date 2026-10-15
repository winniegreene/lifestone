using Godot;
using Lifestone.Client.Avatars;
using Lifestone.Client.Movement;
using Lifestone.Client.Play;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.World;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Game.Core;
using Lifestone.Game.World;
using Lifestone.Game.World.Interiors;
using Lifestone.Game.World.ServerObjects;

namespace Lifestone.Game.Player;

/// <summary>What <see cref="PlayerRig.Build"/> needs to make a player: the scene it joins, the player's description and where it stands.</summary>
/// <param name="Main">The main scene the player's nodes join.</param>
/// <param name="Services">The game services (dats, workers, the main-thread queue).</param>
/// <param name="World">The streamed world (the player becomes its streaming focus).</param>
/// <param name="Interiors">The interiors layer, or null.</param>
/// <param name="Camera">The world camera the chase camera drives.</param>
/// <param name="Sources">The dats the avatars are made from.</param>
/// <param name="Create">The player's description: the stand-in's offline, the server's own CreateObject online.</param>
/// <param name="Spawn">Where the player stands.</param>
/// <param name="Guid">The player's guid.</param>
/// <param name="Sink">Where the movement controller's messages go: a <see cref="DryRunMovementSink"/> offline, the session's <see cref="IMovementSink"/> (the movement channel) online.</param>
/// <param name="StandInLabel">The on-screen line that says the character is a placeholder (offline), or null.</param>
/// <param name="Avatars">
/// The session's avatar set when the player lives among the server's other objects (online): the player must already be in it, created from its own CreateObject by
/// the session adapter (<see cref="SessionAvatarAdapter"/>), and <paramref name="Create"/> is then unused. Null (offline): the rig makes a set holding the player alone.
/// </param>
/// <param name="LightTable">The light-set table shared with the server-objects layer (one table owns the shader global), or null to make one.</param>
/// <param name="Geometry">The collision shapes already decoded (shared across arrivals and warmed at start-up), or null to start an empty library.</param>
/// <param name="DeferFocus">True: the world's streaming focus is not set by <see cref="PlayerRig.Build"/> but by <see cref="PlayerRig.UseAsWorldFocus"/>, which the caller calls when it starts the world around the player (in a later frame, so the build and the world's start are two frames' work).</param>
/// <param name="RunByDefault">The character's "run by default" option (retail <c>PlayerModule::ToggleRun</c>; the run key then walks). The rig keeps <c>TODO(g5-run-by-default)</c> logged only while the request leaves this default of true (the host has not read the option).</param>
/// <param name="DeferKeys">True: the keys are not set up by <see cref="PlayerRig.Build"/> but by <see cref="PlayerRig.AttachKeys"/>, which the caller calls in a later frame (reading the key maps takes a few milliseconds; online the player cannot move until portal space ends).</param>
public sealed record PlayerRigRequest(
    Node Main,
    GameServices Services,
    WorldRoot World,
    InteriorController? Interiors,
    FlyCamera Camera,
    AvatarSources Sources,
    AvatarCreate Create,
    CellFrame Spawn,
    uint Guid,
    IMovementSink Sink,
    string? StandInLabel,
    AvatarSet? Avatars = null,
    ObjectLightTable? LightTable = null,
    PhysicsGeometryLibrary? Geometry = null,
    bool DeferKeys = false,
    bool DeferFocus = false,
    IPhysicsWeenie? Weenie = null,
    bool RunByDefault = true);

/// <summary>
/// The player the game plays (M2 G5, shared with play mode in G6 W9): the avatar, its meshes, the physics body with its collision streaming, the controller
/// with the retail keys, the chase camera and the on-screen text, built the same way whether the character is the offline stand-in
/// (<see cref="OfflinePlayerSetup"/>) or the one the server created (<c>PlayMode</c>). What differs is the description it is made from and the sink its
/// movement goes to.
/// </summary>
public sealed class PlayerRig
{
    private readonly Node _main;
    private readonly Lifestone.Data.Dat.DatSet _dats;
    private readonly WorldRoot _world;
    private readonly PlayerFocus _focus;

    private PlayerRig(Node main, Lifestone.Data.Dat.DatSet dats, WorldRoot world, PlayerFocus focus, PlayerController controller, PlayerView view, ChaseCamera chase, CanvasLayer hud, PhysicsWorld physics)
    {
        _main = main;
        _dats = dats;
        _world = world;
        _focus = focus;
        Controller = controller;
        View = view;
        Chase = chase;
        Hud = hud;
        Physics = physics;
    }

    /// <summary>The controller (also the player's input and the streaming focus).</summary>
    public PlayerController Controller { get; }

    /// <summary>The player's avatars.</summary>
    public AvatarSet Avatars => Controller.Avatars;

    /// <summary>The draw node.</summary>
    public PlayerView View { get; }

    /// <summary>The chase camera.</summary>
    public ChaseCamera Chase { get; }

    /// <summary>The on-screen text layer.</summary>
    public CanvasLayer Hud { get; }

    /// <summary>The physics world.</summary>
    public PhysicsWorld Physics { get; }

    /// <summary>
    /// Builds the player and joins its nodes to <see cref="PlayerRigRequest.Main"/>. Returns null (after logging why) when the avatar set refuses the description.
    /// The caller installs the world's streaming focus (<see cref="Controller"/> is the focus source's player) and the retail UI's router.
    /// </summary>
    public static PlayerRig? Build(PlayerRigRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (main, services, world, interiors, camera) = (request.Main, request.Services, request.World, request.Interiors, request.Camera);
        var dats = services.Dats ?? throw new InvalidOperationException("the player needs the dats");
        var log = GodotLogSink.Instance;
        var now = Time.GetTicksUsec() / 1_000_000.0;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var spawn = request.Spawn;

        var avatars = request.Avatars ?? new AvatarSet(request.Sources, log);
        if (request.Avatars is null && avatars.Create(request.Create, now) != AvatarApply.Applied)
        {
            Log.Error("play", "the avatar set refused the player's description; there is no player", ("guid", $"0x{request.Guid:X8}"));
            return null;
        }

        if (avatars.Player is not { Parts: { } parts } player || player.Guid != request.Guid)
        {
            Log.Error("play", "the avatar set has no player with a motion table for this guid; there is no player", ("guid", $"0x{request.Guid:X8}"), ("shared_set", request.Avatars is not null));
            return null;
        }

        // The first frame of the avatar, before the physics body takes it over: it names the parts to build the meshes for. The player is stepped first and always.
        // A shared set (online) is given a deadline already passed, so this call steps the player and only the first 8 of the other objects (AvatarSet.Advance
        // checks its budget every 8 avatars) and the rest wait for the controller's advance, which is the frame's; an own set holds the player alone.
        avatars.Advance(now, new AvatarViewer(spawn), request.Avatars is null ? long.MaxValue : 0);
        var lightTable = request.LightTable ?? new ObjectLightTable();
        var view = new PlayerView(services, world, interiors, lightTable, new PlayerCellLights(services));
        main.AddChild(view);
        var lapAvatar = timer.Elapsed.TotalMilliseconds;
        view.Prepare(avatars.Frame, player);
        var lapView = timer.Elapsed.TotalMilliseconds;

        var physics = new PhysicsWorld();
        physics.SetTime(now);
        var geometry = request.Geometry ?? new PhysicsGeometryLibrary(dats);
        var builder = new LandblockCollisionBuilder(dats, geometry, services.CellStructures, services.Scenery);
        var streamer = new CollisionStreamer(physics, (coord, cancellation) => builder.Build(coord, cancellation), services.Workers, log);
        var options = AvatarsOptions.Parse(OS.GetCmdlineUserArgs());
        foreach (var problem in options.Errors)
        {
            Log.Warn("play", "bad argument", ("problem", problem));
        }

        // The character's "run by default" option (PlayerModule::ToggleRun) comes with the request; a host that has not read it leaves the default (true) and the TODO is logged.
        var movement = new MovementOptions { RunByDefault = request.RunByDefault };
        if (request.RunByDefault)
        {
            Log.Warn(
                "play",
                "TODO(g5-run-by-default): retail's per-character run-by-default option (PlayerModule::ToggleRun) is not read by this host; the player runs by default and the run key (Left Shift) walks",
                ("run_by_default", movement.RunByDefault));
        }
        var lapPhysics = timer.Elapsed.TotalMilliseconds;
        var walker = OfflineWalker.Create(dats, physics, log, weenie: request.Weenie, geometry: geometry, parts: parts, id: request.Guid, sink: request.Sink, options: movement);
        walker.Motion.RetailNegativeRunRateCap = options.RetailRunRateCap;
        var events = new PlayerPhysicsEvents(player);
        walker.Physics.Events = events;
        walker.Physics.TargetWorld = avatars;
        avatars.PlayerBody = walker.Body;
        streamer.SetFocus(spawn.CellId);
        if (!walker.Place(spawn))
        {
            Log.Error("play", "no valid position at the spawn; the player waits there", ("cell", $"0x{spawn.CellId:X8}"));
        }

        var lapWalker = timer.Elapsed.TotalMilliseconds;
        var focus = new PlayerStreamingFocus(cellId => EnvCellData.Read(dats, cellId)?.SeenOutside, log);
        var controller = new PlayerController(services, walker, avatars, player, view, streamer, focus, events, interiors, spawn)
        {
            RetailStepping = options.RetailStepping,
            InputAllowed = () => camera.InputEnabled,
        };
        main.AddChild(controller);
        var worldFocus = new PlayerFocus(camera, controller);
        if (!request.DeferFocus)
        {
            world.FocusSource = worldFocus;
        }

        if (options.Camera == PlayerCameraMode.First)
        {
            controller.CameraModel.SetInHead();
        }

        var lapInput = timer.Elapsed.TotalMilliseconds;

        // The camera runs after the controller (priority -5 against -20) and before the free camera it drives (0).
        var chase = new ChaseCamera(camera, controller, controller.CameraModel, physics, interiors);
        main.AddChild(chase);
        controller.Camera = chase;
        var hud = AddHud(main, controller, chase, options.PhysicsOverlay, request.StandInLabel, showKeys: request.Avatars is null);
        Log.Info(
            "play",
            "player built",
            ("guid", $"0x{request.Guid:X8}"),
            ("cell", $"0x{spawn.CellId:X8}"),
            ("at", spawn.Origin),
            ("parts", view.Batches.Count),
            ("sink", request.Sink.GetType().Name),
            ("ms", timer.Elapsed.TotalMilliseconds));
        Log.Debug(
            "play",
            "player build laps (cumulative ms)",
            ("avatar_and_view_node", lapAvatar),
            ("meshes_prepared", lapView),
            ("physics_and_streamer", lapPhysics),
            ("walker_placed", lapWalker),
            ("controller_and_input", lapInput),
            ("camera_and_hud", timer.Elapsed.TotalMilliseconds));
        var rig = new PlayerRig(main, dats, world, worldFocus, controller, view, chase, hud, physics);
        if (!request.DeferKeys)
        {
            rig.AttachKeys();
        }

        return rig;
    }

    /// <summary>Makes the player the streamed world's focus (a rig built with <see cref="PlayerRigRequest.DeferFocus"/> does not until this is called).</summary>
    public void UseAsWorldFocus() => _world.FocusSource = _focus;

    /// <summary>Sets up the player's keys (retail's movement and camera maps through the input router). A failure is logged and the player stands still.</summary>
    public void AttachKeys()
    {
        if (Controller.Input is not null)
        {
            return;
        }

        try
        {
            Controller.AttachInput(PlayerInput.CreateOwn(_main, _dats, Controller, Controller, Controller.OnClientActive));
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or FileNotFoundException)
        {
            Log.Error("play", "the keys cannot be set up (the key maps and layouts of the dats); the player stands still", ("error", ex.Message));
        }
    }

    /// <summary>Takes the player's nodes out of the scene (at the end of the frame). The world's focus source is the caller's to reset.</summary>
    public void Free()
    {
        Controller.SetProcess(false); // queue_free takes effect at the end of the frame; nothing of the old player may step in this one
        Chase.SetProcess(false);
        Avatars.PlayerBody = null;
        foreach (Node node in new Node[] { Chase, Controller, View, Hud })
        {
            if (GodotObject.IsInstanceValid(node))
            {
                node.QueueFree();
            }
        }
    }

    /// <summary>The keys, one line on screen.</summary>
    private const string Keys = "keys: W/Up run   X/Down back   A/D turn   Z/C strafe   Space jump (hold to charge)   Left Shift walk   Q autorun   S stop      camera: number pad 4/6 rotate, 8/2 raise and lower, -/+ zoom, point first person, 0 default; middle mouse button look";

    /// <summary>
    /// The player's on-screen text: the label that says the character is a stand-in (offline), the keys (the offline explore player only: in play mode they
    /// duplicate retail's own key bindings and the M1 overlay would sit unlabelled over the shell), the notice line (autorun on or off, a labelled placeholder
    /// until it goes to retail's chat window), and with <c>--physics-overlay</c> the physics overlay.
    /// </summary>
    private static CanvasLayer AddHud(Node main, PlayerController controller, ChaseCamera chase, bool physicsOverlay, string? standInLabel, bool showKeys)
    {
        var layer = new CanvasLayer { Name = "PlayerHud", Layer = 5 };
        if (standInLabel is not null)
        {
            var standIn = HudLabel("StandInLabel", standInLabel, 13);
            standIn.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
            standIn.OffsetLeft = 12;
            standIn.OffsetBottom = -10;
            standIn.GrowVertical = Control.GrowDirection.Begin;
            layer.AddChild(standIn);
        }

        if (showKeys)
        {
            var keys = HudLabel("KeysLabel", Keys, 12);
            keys.SetAnchorsPreset(Control.LayoutPreset.TopRight);
            keys.GrowHorizontal = Control.GrowDirection.Begin;
            keys.GrowVertical = Control.GrowDirection.End;
            keys.OffsetRight = -12;
            keys.OffsetTop = 6;
            keys.HorizontalAlignment = HorizontalAlignment.Right;
            keys.AutowrapMode = TextServer.AutowrapMode.Word;
            keys.CustomMinimumSize = new Vector2(620, 0);
            layer.AddChild(keys);
        }

        var notice = HudLabel("NoticeLabel", string.Empty, 18);
        notice.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        notice.GrowHorizontal = Control.GrowDirection.Both;
        notice.OffsetTop = 70;
        notice.Visible = false;
        layer.AddChild(notice);
        controller.NoticeLabel = notice;
        if (physicsOverlay)
        {
            var overlay = new PhysicsOverlay(controller, chase) { Name = "PhysicsOverlay" };
            overlay.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            overlay.OffsetLeft = 12;
            overlay.OffsetTop = 150;
            overlay.AddThemeColorOverride("font_color", new Color(0.7f, 1f, 0.8f));
            overlay.AddThemeColorOverride("font_outline_color", Colors.Black);
            overlay.AddThemeConstantOverride("outline_size", 4);
            overlay.AddThemeFontSizeOverride("font_size", 12);
            overlay.MouseFilter = Control.MouseFilterEnum.Ignore;
            layer.AddChild(overlay);
        }

        main.AddChild(layer);
        return layer;
    }

    private static Label HudLabel(string name, string text, int fontSize)
    {
        var label = new Label { Name = name, Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 4);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }
}
