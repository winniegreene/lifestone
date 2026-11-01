using Godot;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Physics;
using Lifestone.Client.Play;
using Lifestone.Client.Scheduling;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
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
    /// Makes the server-object physics host over the player's <paramref name="physics"/> world and gives it to the avatar set (M5 item 5 h2, D70), so the player's transitions meet
    /// doors, lifestones, NPCs and creatures. A shared set (online) already holds the objects the server created before the rig was built (the login's burst), so each is created in the
    /// host once, from what the set knows of it (its Setup, scale, the body's current state, its position); every later create, move, state change and delete reaches the host through the set.
    /// An own set (offline) holds the player alone at this point, so nothing is fed.
    /// </summary>
    private static void ConnectServerObjects(AvatarSet avatars, PhysicsWorld physics, DatSet dats, PhysicsGeometryLibrary geometry, ILogSink log)
    {
        var host = new ServerObjectPhysicsHost(physics, dats, geometry, log);
        avatars.ServerObjects = host;
        foreach (var avatar in avatars.All)
        {
            if (avatar.IsPlayer)
            {
                continue;
            }

            var created = avatar.Created;
            host.Create(
                new ServerObjectDescription(avatar.Guid, created.SetupId, created.Scale, avatar.Body.State, created.IsCreature, IsSelf: false, created.DescriptionFlags),
                avatar.Body.InWorld ? avatar.Body.Position : null);
        }
    }

    /// <summary>
    /// The one place every avatar set gets its dynamic-light wiring (M3 4.1 fix A, finding R1): <c>--viewer-light=on|off</c> and the interior table's source (the cell glow reads the
    /// frame the avatars fill). PlayMode's set and the offline rig's own set both call it, so neither path ignores the option or leaves the walls dark.
    /// </summary>
    public static void WireDynamicLights(AvatarSet avatars, InteriorController? interiors)
    {
        ArgumentNullException.ThrowIfNull(avatars);
        avatars.DynamicLights.ViewerLightEnabled = Lifestone.Client.Interiors.LightOptions.Parse(OS.GetCmdlineUserArgs()).ViewerLight; // --viewer-light=on|off (M3 4.1)
        if (interiors is not null) { interiors.DynamicLights.Source = avatars.DynamicLightFrame; } // the cell glow reads the frame the avatars fill (M3 4.1 r2)
        Log.Info("play", "dynamic lights wired", ("viewer_light", avatars.DynamicLights.ViewerLightEnabled ? "on" : "off"), ("cell_glow_source", interiors is not null ? "bound" : "none (no interiors layer)"));
    }

    /// <summary>
    /// Builds the player and joins its nodes to <see cref="PlayerRigRequest.Main"/>, all in this call (the offline player, tests). Returns null (after logging why) when the avatar set refuses the description.
    /// The caller installs the world's streaming focus (<see cref="Controller"/> is the focus source's player) and the retail UI's router. Play mode builds the same player in three frames
    /// through <see cref="BeginBuild"/>; this is that build with every step run at once, so the two give the same player.
    /// </summary>
    public static PlayerRig? Build(PlayerRigRequest request)
    {
        var pending = BeginBuild(request);
        pending.RunAll();
        return pending.Rig;
    }

    /// <summary>
    /// Starts the player's build as three steps for the host to run one per frame (M5 item 8, <c>m5-8-login-walk-and-frames</c>: one frame built all of it, 15-25 ms, once 43 ms):
    /// <list type="number">
    /// <item><see cref="AvatarOtherMeshesStep"/>: the avatar's first frame, the draw node and the meshes of the variant the character is not drawn with;</item>
    /// <item><see cref="DrawnMeshesAndPhysicsStep"/>: the meshes of the variant it is drawn with, the physics world and its collision streaming;</item>
    /// <item><see cref="WalkerStep"/>: the walker (the physics body, its motion) placed at the spawn;</item>
    /// <item><see cref="ControllerAndCameraStep"/>: the controller, the chase camera and the on-screen text.</item>
    /// </list>
    /// Nothing of the player is drawn until the last step: the draw node updates only from the controller, which the last step makes, so the player is never seen half built.
    /// </summary>
    public static PendingBuild BeginBuild(PlayerRigRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new PendingBuild(request);
    }

    /// <summary>The name of the build's first step (the avatar, the draw node and the meshes of the variant the character is not drawn with).</summary>
    public const string AvatarOtherMeshesStep = "avatar_other_meshes";

    /// <summary>The name of the build's second step (the meshes of the variant the character is drawn with, the physics world and the collision streamer).</summary>
    public const string DrawnMeshesAndPhysicsStep = "drawn_meshes_and_physics";

    /// <summary>The name of the build's third step (the walker and its placement at the spawn).</summary>
    public const string WalkerStep = "walker";

    /// <summary>The name of the build's fourth step (the controller, the chase camera and the on-screen text).</summary>
    public const string ControllerAndCameraStep = "controller_and_camera";

    /// <summary>
    /// A player build in progress (<see cref="BeginBuild"/>). <see cref="RunNext"/> runs one step; <see cref="Rig"/> is set once the last step ran. <see cref="Cancel"/> takes out
    /// what an unfinished build already added to the scene (a log off or a re-create between two steps).
    /// </summary>
    public sealed class PendingBuild
    {
        private readonly PlayerRigRequest _request;
        private readonly (Node Main, GameServices Services, WorldRoot World, InteriorController? Interiors, FlyCamera Camera) _scene;
        private readonly DatSet _dats;
        private AvatarSet? _avatars;
        private PlayerAvatar? _player;
        private MotionPlayer? _parts;
        private PlayerView? _view;
        private bool _logged;
        private PhysicsWorld? _physics;
        private PhysicsGeometryLibrary? _geometry;
        private CollisionStreamer? _streamer;
        private AvatarsOptions? _options;
        private MovementOptions? _movement;
        private OfflineWalker? _walker;
        private PlayerPhysicsEvents? _events;

        internal PendingBuild(PlayerRigRequest request)
        {
            _request = request;
            _scene = (request.Main, request.Services, request.World, request.Interiors, request.Camera);
            _dats = request.Services.Dats ?? throw new InvalidOperationException("the player needs the dats");
            Work = new StagedWork(
                [
                    new StagedStep(AvatarOtherMeshesStep, AvatarAndOtherMeshes),
                    new StagedStep(DrawnMeshesAndPhysicsStep, DrawnMeshesAndPhysics),
                    new StagedStep(WalkerStep, Walker),
                    new StagedStep(ControllerAndCameraStep, ControllerAndCamera),
                ],
                onCancel: Free);
        }

        /// <summary>The steps (names, timings, progress).</summary>
        public StagedWork Work { get; }

        /// <summary>The finished player, or null before the last step ran or when a step failed (it logged why).</summary>
        public PlayerRig? Rig { get; private set; }

        /// <summary>True when a step failed: there is no player and no further step runs.</summary>
        public bool Failed => Work.IsFailed;

        /// <summary>Runs the next step. Returns false when it failed. The step that completes the build logs the <c>player built</c> line, which carries each step's milliseconds.</summary>
        public bool RunNext()
        {
            var ok = Work.RunNext();
            LogWhenComplete();
            return ok;
        }

        /// <summary>Runs every remaining step now.</summary>
        public bool RunAll()
        {
            var ok = Work.RunAll();
            LogWhenComplete();
            return ok;
        }

        /// <summary>Stops the build and takes out the nodes its steps added so far.</summary>
        public void Cancel() => Work.Cancel();

        private void Free()
        {
            if (_view is not null && GodotObject.IsInstanceValid(_view))
            {
                _view.QueueFree();
            }

            _view = null;
            _streamer?.Dispose(); // a build cancelled after step 2 owns a collision streamer no controller will dispose
            _streamer = null;
            if (_avatars is { } avatars && _walker is not null && ReferenceEquals(avatars.PlayerBody, _walker.Body))
            {
                avatars.PlayerBody = null; // the walker of a build cancelled after its step 3 must not stay the set's player body
            }
        }

        private void LogWhenComplete()
        {
            if (_logged || !Work.IsComplete || Rig is null)
            {
                return;
            }

            _logged = true;
            var at = _request.Spawn;
            Log.Info(
                "play",
                "player built",
                [
                    ("guid", $"0x{_request.Guid:X8}"),
                    ("cell", $"0x{at.CellId:X8}"),
                    ("at", at.Origin),
                    ("parts", Rig.View.Batches.Count),
                    ("sink", _request.Sink.GetType().Name),
                    ("ms", Work.TotalMilliseconds), // the work's own milliseconds, summed over its steps (the frames between them are not in it)
                    .. Work.TimingFields(), // M5 8 build: build_steps and each step's ms, so the login's line names the step that cost the most
                ]);
        }

        // Step 1: the avatar's first frame (it names the parts), the draw node, and the meshes of the variant the character is not drawn with.
        private bool AvatarAndOtherMeshes()
        {
            var (main, services, world, interiors, _) = _scene;
            var log = GodotLogSink.Instance;
            var request = _request;
            var now = Time.GetTicksUsec() / 1_000_000.0;
            var spawn = request.Spawn;

            var avatars = request.Avatars ?? new AvatarSet(request.Sources, log);
            if (request.Avatars is null) { WireDynamicLights(avatars, interiors); } // an own set (offline) is wired here; a shared set is wired by its owner (PlayMode.NewObjects)
            if (request.Avatars is null && avatars.Create(request.Create, now) != AvatarApply.Applied)
            {
                Log.Error("play", "the avatar set refused the player's description; there is no player", ("guid", $"0x{request.Guid:X8}"));
                return false;
            }

            if (avatars.Player is not { Parts: { } parts } player || player.Guid != request.Guid)
            {
                Log.Error("play", "the avatar set has no player with a motion table for this guid; there is no player", ("guid", $"0x{request.Guid:X8}"), ("shared_set", request.Avatars is not null));
                return false;
            }

            // The first frame of the avatar, before the physics body takes it over: it names the parts to build the meshes for. The player is stepped first and always.
            // A shared set (online) is given a deadline already passed, so this call steps the player and only the first 8 of the other objects (AvatarSet.Advance
            // checks its budget every 8 avatars) and the rest wait for the controller's advance, which is the frame's; an own set holds the player alone.
            avatars.Advance(now, new AvatarViewer(spawn), request.Avatars is null ? long.MaxValue : 0);
            var lightTable = request.LightTable ?? new ObjectLightTable();
            var view = new PlayerView(services, world, interiors, lightTable, new PlayerCellLights(services));
            view.DynamicLights = avatars.DynamicLightFrame; // M3 4.1 r1: the player's light row takes the frame's dynamic lights ahead of the cell's
            main.AddChild(view);
            _view = view;
            _avatars = avatars;
            _player = player;
            _parts = parts;
            view.PrepareOtherVariant(avatars.Frame, player);
            return true;
        }

        // Step 2: the meshes of the variant the character is drawn with, assigned to the part batches (the view draws nothing yet: it updates only from the controller, built in step 4),
        // then the physics world, the server-object host and the collision streamer.
        private bool DrawnMeshesAndPhysics()
        {
            var services = _scene.Services;
            var request = _request;
            var log = GodotLogSink.Instance;
            var avatars = _avatars!;
            _view!.PrepareDrawnVariant(avatars.Frame, _player!);

            var physics = new PhysicsWorld();
            physics.SetTime(Time.GetTicksUsec() / 1_000_000.0);
            var geometry = request.Geometry ?? new PhysicsGeometryLibrary(_dats);
            ConnectServerObjects(avatars, physics, _dats, geometry, log);
            var builder = new LandblockCollisionBuilder(_dats, geometry, services.CellStructures, services.Scenery);
            _streamer = new CollisionStreamer(physics, (coord, cancellation) => builder.Build(coord, cancellation), services.Workers, log);
            _options = AvatarsOptions.Parse(OS.GetCmdlineUserArgs());
            avatars.ServerObjectTransitions = _options.ServerObjectTransitions; // --server-object-transitions=off: the diagnostics switch (M5 item 5 t1)
            foreach (var problem in _options.Errors)
            {
                Log.Warn("play", "bad argument", ("problem", problem));
            }

            _physics = physics;
            _geometry = geometry;
            return true;
        }

        // Step 3: the walker over the physics body, placed at the spawn.
        private bool Walker()
        {
            var request = _request;
            var log = GodotLogSink.Instance;
            var spawn = request.Spawn;
            var avatars = _avatars!;
            var options = _options!;

            // The character's "run by default" option (PlayerModule::ToggleRun) comes with the request; a host that has not read it leaves the default (true) and the TODO is logged.
            // P5a S4: scripted walks no longer depend on this value being guessed right: the host binds it to MoveIntents (RunByDefault) and RouteDriver and the runner
            // hold the run key as run != RunByDefault, so "run" runs whatever it is. TODO(g5-run-by-default) stays open for its other half (the option's source: a later toggle, the offline player).
            var movement = new MovementOptions { RunByDefault = request.RunByDefault };
            if (request.RunByDefault)
            {
                Log.Warn(
                    "play",
                    "TODO(g5-run-by-default): retail's per-character run-by-default option (PlayerModule::ToggleRun) is not read by this host; the player runs by default and the run key (Left Shift) walks",
                    ("run_by_default", movement.RunByDefault));
            }

            var walker = OfflineWalker.Create(_dats, _physics!, log, weenie: request.Weenie, geometry: _geometry, parts: _parts!, id: request.Guid, sink: request.Sink, options: movement);
            walker.Motion.RetailNegativeRunRateCap = options.RetailRunRateCap;
            var events = new PlayerPhysicsEvents(_player!);
            walker.Physics.Events = events;
            walker.Physics.TargetWorld = avatars;
            avatars.PlayerBody = walker.Body;
            _streamer!.SetFocus(spawn.CellId);
            if (!walker.Place(spawn))
            {
                Log.Error("play", "no valid position at the spawn; the player waits there", ("cell", $"0x{spawn.CellId:X8}"));
            }

            _movement = movement;
            _walker = walker;
            _events = events;
            return true;
        }

        // Step 4: the controller, the chase camera and the on-screen text; the player exists (and draws) from the frame after this step.
        private bool ControllerAndCamera()
        {
            var (main, services, world, interiors, camera) = _scene;
            var request = _request;
            var log = GodotLogSink.Instance;
            var spawn = request.Spawn;
            var options = _options!;
            var view = _view!;
            var physics = _physics!;
            var focus = new PlayerStreamingFocus(cellId => EnvCellData.Read(_dats, cellId)?.SeenOutside, log);
            var controller = new PlayerController(services, _walker!, _avatars!, _player!, view, _streamer!, focus, _events!, interiors, spawn)
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

            if (Diagnostics.VideoOptions.Camera() is { } videoCamera)
            {
                controller.CameraModel.SetVideoOffset(videoCamera.Distance, videoCamera.Height);
                Log.Info("play", "--video-camera: the chase camera is widened for the video", ("distance", videoCamera.Distance), ("height", videoCamera.Height));
            }

            // The camera runs after the controller (priority -5 against -20) and before the free camera it drives (0).
            var chase = new ChaseCamera(camera, controller, controller.CameraModel, physics, interiors);
            main.AddChild(chase);
            controller.Camera = chase;
            var hud = AddHud(main, controller, chase, options.PhysicsOverlay, request.StandInLabel, showKeys: request.Avatars is null);
            var rig = new PlayerRig(main, _dats, world, worldFocus, controller, view, chase, hud, physics);
            if (!request.DeferKeys)
            {
                rig.AttachKeys();
            }

            Rig = rig;
            return true;
        }
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
