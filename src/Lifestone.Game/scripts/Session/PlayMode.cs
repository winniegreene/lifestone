using System.Diagnostics;
using Godot;
using Lifestone.Client.Audio;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.PortalSpace;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Game.Audio;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.PortalSpace;
using Lifestone.Game.Player;
using Lifestone.Game.RetailUi;
using Lifestone.Game.World;
using Lifestone.Game.World.Interiors;
using Lifestone.Game.World.Objects;
using Lifestone.Game.World.ServerObjects;

namespace Lifestone.Game.Sessions;

/// <summary>
/// Play mode in the main scene (<c>--play</c>; m2-plan G6 W9): the explore scene with a server behind it. It hides M1's panels and keys (owner decision
/// D41: T, Tab, Esc, F, F7 and F9 are dropped; F1 and F3 stay), holds the world paused until the server creates the player, builds the player from its own
/// CreateObject when it does (<see cref="PlayerRig"/>, with the session's real movement sink in place of the dry-run one), starts the world around it and
/// moves the world, the interiors, the physics and the camera on a server teleport (<see cref="PlayerPlacement"/>), and tears the player down when it leaves.
/// Portal space is the <see cref="PortalSessionBridge"/>'s: it turns the same events into <c>TeleportProgress</c> calls and sends LoginComplete exactly once per
/// arrival, at the end of portal state 6.
/// </summary>
/// <remarks>
/// <para><b>What decides what.</b> <see cref="PlayPresentationFlow"/> (Client, tested) reads the session's events and calls this node through
/// <see cref="IPlayWorld"/>; every call is queued and runs at the start of this node's next process (priority <see cref="Priority"/>: after the session host,
/// before the player and the world), so building the player never happens inside the session's tick.</para>
/// <para><b>The server's objects (G5 W6, W10).</b> Play mode owns the session's one <see cref="AvatarSet"/>, fed by the <see cref="SessionAvatarAdapter"/> from the
/// bus (every object event, the player's own included, in the reducer's order, applied inside a time budget at the start of this node's process) and drawn by the
/// <see cref="ServerObjectsLayer"/>; the player's rig binds to the player's avatar in that set when the server has created it, so the character, its clothing and
/// its colours are its own CreateObject's. The set is made again at every log off.</para>
/// <para><b>Online flows (G6 W11).</b> A server teleport (a tools <c>place</c>, a portal's Use, a recall) is <see cref="PlayerTeleported"/>: the world, the
/// interiors, the physics and the camera move, and portal space plays from the same events (the bridge). The log-off key (<see cref="EndSessionKey"/>, retail's
/// Shift+Escape "End Character Session") calls <c>Actions.LogOff</c> only; the bridge drives portal space and the server's character list ends it. A boot or a
/// silent server ends the session in failure: the player is taken away here and the retail UI shows the disconnected screen, whose button reconnects (D43;
/// <see cref="SessionHost.Reconnect"/>). The player's walker has the session's <c>QualitiesWeenie</c> (the server's Run and Jump skills), and a server MoveTo
/// takes control from the keys (<c>PhysicsPlayerBody.ServerControl</c>).</para>
/// <para><b>Placeholders, labelled:</b> the shell's windows without content are named in the game screen's note (<c>GamePlayScreen</c>), and a one-line status (<see cref="PlayStatusLine"/>) shows the stage outside the shell (the shell's spew box and chat replace it, G4 g2); the log-off key
/// does not ask first, as retail's key does not (<c>TODO(g6-logoff-confirm)</c> is closed as retail-exact; the options panel's Exit button asks).</para>
/// <para><b>Picking and selection</b> (G5 W7, docs/avatars.md §18): a left click on the world selects the object under the pointer (or clears), a double click Uses it
/// through <c>Actions.Use</c>; <see cref="Selection"/> is the one selected object, cleared by its delete, a teleport and a log off, and <see cref="Picking"/> is the
/// <see cref="ISelectionProjector"/> the retail UI layer is given.</para>
/// </remarks>
public sealed partial class PlayMode : Node, IPlayWorld
{
    /// <summary>The process priority: after the session host (-900), before the player (-20), the world (10) and the portal presenter (15).</summary>
    public const int Priority = -800;

    private const string LogCategory = "play";

    private readonly Queue<(ulong Frame, Action Work)> _work = new();
    private Main _main = null!;
    private GameServices _services = null!;
    private SessionHost _host = null!;
    private WorldRoot _world = null!;
    private InteriorController? _interiors;
    private FlyCamera _camera = null!;
    private RetailUiLayer? _ui;
    private PlayPresentationFlow _flow = null!;
    private PlayStatusLine _status = null!;
    private PortalSessionBridge? _bridge;
    private PortalSpaceHandles? _portal;
    private AvatarSources? _sources;
    private PlayerRig? _rig;
    private MoveIntents? _boundMove;
    private Action<string>? _humanKey;
    private uint _destinationCell;
    private bool _loginCompleteFallback;
    private AvatarSet? _avatars;
    private readonly List<AvatarNotice> _frameNotices = []; // the avatar set's notices of this frame: the one drain owner is _Process (M5 item 7, m5-avatar-notices-full); the look shots read this list
    private AvatarNoticeTally? _noticeTally;
    private ChatLogTap? _chatLog; // M5 7 F17: every chat line the server sends, logged from the session's event (no UI needed)
    private VitalsLog? _vitalsLog; // M5 7 F17: --log-vitals-s
    private SoakFrames? _soakFrames;
    private DollReviewFrames? _dollFrames;
    private ExamineReviewFrames? _examineFrames;
    private ExamineWornFrames? _wornFrames;
    private Lifestone.Client.RetailUi.Panels.ExamineCreatureCapture? _creatureCapture;
    private bool _creatureCaptureLooked;
    private ExamineCaptureFrames? _captureFrames;

    /// <summary>Where the capture's proof frames go when <c>--examine-review-frames</c> names no folder (M5 7 D138).</summary>
    public const string DefaultCaptureFramesDirectory = "cache/examine-capture";
    private SoundHookRouter? _hookSounds;
    private SessionAvatarAdapter? _adapter;
    private ServerObjectsLayer? _layer;
    private ObjectLightTable? _lightTable;
    private PhysicsGeometryLibrary? _geometry;
    private EndSessionKey? _endKey;
    private SkillTableData? _skills;
    private WorldPicking? _picking;

    /// <summary>The router priority of the log-off key (the player's own maps use 0 and the camera's 1; the key's map is its own).</summary>
    private const int EndKeyPriority = 0;

    /// <summary>The installed play mode, or null.</summary>
    public static PlayMode? Current { get; private set; }

    /// <summary>The log-off key's handler (Shift+Escape), or null without the retail UI.</summary>
    public EndSessionKey? EndKey => _endKey;

    /// <summary>The flow (stage, status text, arrivals).</summary>
    public PlayPresentationFlow Flow => _flow;

    /// <summary>The player, or null before the server creates it and after it leaves.</summary>
    public PlayerRig? Rig => _rig;

    /// <summary>How long the adapter may spend applying the session's object events in a frame, in milliseconds (one event may overrun it: a create is 1 to 10 ms).</summary>
    public const double AdapterBudgetMilliseconds = 3.0;

    /// <summary>A frame whose queued work took longer than this leaves the adapter's events to the next frame, in milliseconds.</summary>
    public const double HeavyWorkMilliseconds = 8.0;

    /// <summary>Frames the player's build waits for the adapter to apply the player's own CreateObject (it is the first object event of an arrival).</summary>
    private const int AvatarWaitFrames = 600;

    /// <summary>Frames the tunnel shows before the player's build starts, so the build does not share a frame with the tunnel's first-use render compile (DoD 15).</summary>
    private const int TunnelLeadFrames = 2;

    /// <summary>How long the last <c>_Process</c> of this node took, in milliseconds: the queued work, the adapter's events and the shell's wiring (the live login check's slow-frame lines, <c>TODO(login-burst-frame)</c>).</summary>
    public double LastStepMilliseconds { get; private set; }

    /// <summary>The part of <see cref="LastStepMilliseconds"/> the queued work (the player's build, the world's start) took.</summary>
    public double LastWorkMilliseconds { get; private set; }

    /// <summary>The part of <see cref="LastStepMilliseconds"/> the session adapter's events took (<see cref="LastEventsApplied"/> of them).</summary>
    public double LastAdapterMilliseconds { get; private set; }

    /// <summary>How many session object events the last step applied.</summary>
    public int LastEventsApplied { get; private set; }

    /// <summary>The session's avatar set: the player and every object the server created. A new one after each log off.</summary>
    public AvatarSet? Avatars => _avatars;

    /// <summary>
    /// The sound sink of the avatars' and the living statics' hook events and of the server's sounds (M3 1.3 s1): the sound router over the audio service's
    /// retail sound player. Its counters (<see cref="SoundHookRouter.HooksPlayed"/>, <see cref="SoundHookRouter.Footsteps"/>, <see cref="SoundHookRouter.ServerSounds"/>)
    /// read what was handed to the player, whatever the volume (an automated run is silent unless <c>--volume</c>). Null with no audio service.
    /// </summary>
    public SoundHookRouter? HookSounds => _hookSounds;

    /// <summary>The adapter that feeds <see cref="Avatars"/> from the session's events.</summary>
    public SessionAvatarAdapter? Adapter => _adapter;

    /// <summary>The selected object (G5 W7); it follows the session and is cleared with each log off.</summary>
    public ObjectSelection Selection { get; } = new();

    /// <summary>Picking and the selection's projection over the world camera (G5 W7); also the retail UI layer's <see cref="ISelectionProjector"/>.</summary>
    public WorldPicking Picking => _picking!;

    /// <summary>The examination's live review frames (M5 7 S6, <c>--examine-review-frames</c>), or null before the first frame made them; the play checks wait for them over the stand-in's magic item.</summary>
    public ExamineReviewFrames? ExamineFrames => _examineFrames;

    /// <summary>The capture's proof frames (M5 7 D138, with <c>--examine-capture-creatures</c>), or null without the option.</summary>
    public ExamineCaptureFrames? CaptureFrames => _captureFrames;

    /// <summary>The status line (outside the shell) or the stand-in note (in the shell of an offline stand-in session).</summary>
    public PlayStatusLine Status => _status;

    /// <summary>The layer that draws the server's objects other than the player.</summary>
    public ServerObjectsLayer? Layer => _layer;

    /// <summary>The interiors (which env cells are shown), or null when the host has none (the login check's drawn census).</summary>
    public InteriorController? Interiors => _interiors;

    /// <summary>The gate between the player's body and the session's movement channel (messages are held while the server has the player hidden), or null without a player.</summary>
    public PresenceGatedMovementSink? MovementGate { get; private set; }

    /// <summary>
    /// The login check's hand on the player (<c>--login-check=academy-exit|holtburg-laps|all</c>): when set before the player is built, the player's controller sends
    /// through <see cref="RoutePlayHost.Sink"/> (the scenario's audit, then the presence gate), and the gate also holds while the scenario holds the movement back.
    /// Null in ordinary play.
    /// </summary>
    public RoutePlayHost? ScenarioHost { get; set; }

    /// <summary>
    /// The destination's cell for portal space's readiness rule: the cell the server put the player in (the arrival or a teleport), then the player's own cell
    /// once its physics body is placed; 0 without a player.
    /// </summary>
    public uint DestinationCell => _destinationCell;

    private static void LogSessionWarmUpWhenDone(SceneTree tree)
    {
        void Poll()
        {
            if (Lifestone.Client.Session.SessionJitWarmUp.Done is not { } result)
            {
                return;
            }

            tree.ProcessFrame -= Poll;
            Log.Info("play", "the session's message path compiled in the background", ("sample_description_decoded", result.DescriptionDecoded), ("methods", result.Methods), ("skipped", result.Skipped), ("run_ms", result.RunMilliseconds), ("ms", result.Milliseconds));
        }

        tree.ProcessFrame += Poll;
    }

    /// <summary>
    /// Makes play mode: hides M1's panels and keys, pauses the world and follows the session. Call it after the sky track, the retail UI and the interiors are
    /// installed, and before the portal view (which asks for <see cref="DestinationCell"/>); then give the portal view to <see cref="AttachPortal"/>.
    /// </summary>
    public static PlayMode Install(Main main, GameServices services, SessionHost host, WorldRoot world, InteriorController? interiors, FlyCamera camera, RetailUiLayer? ui, bool autoEnter = true)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(camera);
        var session = host.Session ?? throw new InvalidOperationException("play mode needs the live session");
        session.RetailCastAnyMode = services.Options.Play.RetailCastAnyMode; // D40: the cast guard's retail switch, per session (M3 4.2 fix A, R1)
        var mode = new PlayMode
        {
            Name = "PlayMode",
            ProcessPriority = Priority,
            _main = main,
            _services = services,
            _host = host,
            _world = world,
            _interiors = interiors,
            _camera = camera,
            _ui = ui,
        };

        // M1's panels and keys (D41): the time and teleport panels were not made (SkyTrack); the free camera's Tab, Esc and F, and the interiors' F7 and F9, are off.
        camera.Locked = true;
        if (interiors is not null)
        {
            interiors.DebugKeys = false;
        }

        if (main.GetNodeOrNull<Label>("Hud/PositionHud") is { } hud)
        {
            hud.Visible = false;
            hud.SetProcess(false);
        }

        // The server's objects: one avatar set for the session, an adapter that feeds it and the layer that draws it (the player is built into the same set).
        var dats = services.Dats ?? throw new InvalidOperationException("play mode needs the dats");
        mode._sources = AvatarSources.FromDats(dats, services.Textures, services.GfxObjMeshes);
        mode._lightTable = new ObjectLightTable();
        mode._geometry = new PhysicsGeometryLibrary(dats);
        mode._skills = SkillTableData.Load(dats); // the weenie's Run and Jump rules (read once, not in the arrival's frame)
        PlayWarmUp.Run(services, mode._sources, mode._geometry);
        ServerObjectsWarmUp.Run(services, main); // the moving objects' shader variants and pipelines (the arrival's first-use compiles)
        Lifestone.Game.RetailUi.CanvasWarmUp.Run(main); // the canvas pipeline the portal tunnel's first frame needs (TODO(arrival-pipelines))
        Lifestone.Client.Session.SessionJitWarmUp.Start(TimeProvider.System); // the message path's code compiled on a worker, so the world-entry burst's PlayerDescription is not a 50 ms frame (DoD 15, docs/session.md section 27)
        Lifestone.Game.RetailUi.ShellJitWarmUp.Start(); // the retail UI's code compiled on a worker, so the shell's construction is not the arrival's first-use cost (docs/session.md section 27)
        Lifestone.Game.RetailUi.ShellJitWarmUp.LogWhenDone(main.GetTree());
        LogSessionWarmUpWhenDone(main.GetTree());
        mode._layer = new ServerObjectsLayer(services, world, interiors, mode._lightTable);
        main.AddChild(mode._layer);
        world.GetNodeOrNull<Lifestone.Game.Diagnostics.SoakLine>("SoakLine")?.Bind(session, () => mode._avatars, mode._lightTable); // M5 item 0 c3: the [soak] line's session, avatar and light-row sources
        mode._picking = new WorldPicking(camera.Camera, world, () => mode._avatars, () => mode._layer, () => ui is not null ? new Vector2(ui.LogicalSize.X, ui.LogicalSize.Y) : main.GetViewport().GetVisibleRect().Size, id => PickShape.FromMesh(services.GfxObjMeshes.Get(id)));
        mode.Selection.Attach(session.Events);
        ui?.SetSelectionProjector(mode._picking);
        mode.NewObjects();

        // The world waits for the player: nothing streams around the explore scene's start place.
        world.FocusSource = null;
        mode._status = PlayStatusLine.Create();
        main.AddChild(mode._status);
        mode._flow = new PlayPresentationFlow(session, mode, new PlayFlowOptions(autoEnter ? host.Options.Character : null), GodotLogSink.Instance);
        main.AddChild(mode);
        Log.Info(LogCategory, "play mode: M1's panels and keys are off (F1 and F3 stay); the world waits for the player", ("character", host.Options.Character is null ? "none" : "named"));
        if (ui is not null)
        {
            // Retail's log-off key (Shift+Escape, End Character Session): Actions.LogOff only. The panel for it is the shell's (G4) and the tools panel's (G7).
            mode._endKey = new EndSessionKey(session, GodotLogSink.Instance);
            ui.Manager.Input.RegisterInputMap(EndSessionKey.InputMap, mode._endKey, EndKeyPriority);
            Log.Info(LogCategory, "the log-off key is Shift+Escape (retail's End Character Session, gmDefaultMap)");
        }

        if (autoEnter)
        {
            LiveRaiseCheck.InstallIfAsked(main, host, ui); // M6 7 S: --live-raise-check, the owner's click-to-raise proof on Lifestone Throwaway
            RebuffDriverHost.Install(main, services, host, ui); // M4 4.1 lane c: the auto-buff driver (--buff-auto, the Pause key); a login-check leg runs its own
            if (services.Dats is not null)
            {
                AutopilotHost.Install(main, services, host, mode, ui); // M5 4 h3: the autopilot's dial (Num Lock, the tools panel's Autopilot button); a login-check leg runs its own
            }
        }

        return mode;
    }

    private readonly HashSet<uint> _soundTablesPrefetched = [];
    private readonly Queue<uint> _wavesToPrefetch = new();
    private readonly List<uint> _prefetchBatch = [];
    private int _wavesPrefetched;
    private int _prefetchFrames;

    /// <summary>Waves a frame may hand to the audio service's decoders (M3 1.3 fix A, 1.3-R1 (b)): the player's table is spread over frames, not requested in the burst's frame.</summary>
    internal const int WavesPerFrame = 6;

    /// <summary>Waves of the player's sound table handed to the decoders so far, and the frames that did it (the arrival's attribution fields).</summary>
    internal (int Waves, int Frames, int Waiting) PrefetchProgress => (_wavesPrefetched, _prefetchFrames, _wavesToPrefetch.Count);

    /// <summary>Main thread, once per frame that did no heavy work: the next few waves of the player's table, then the next few particle models of the script tables seen.</summary>
    private void PumpArrivalPrefetch()
    {
        if (_wavesToPrefetch.Count > 0 && AudioService.Current is { } audio)
        {
            _prefetchBatch.Clear();
            while (_wavesToPrefetch.Count > 0 && _prefetchBatch.Count < WavesPerFrame)
            {
                _prefetchBatch.Add(_wavesToPrefetch.Dequeue());
            }

            audio.Prefetch(_prefetchBatch);
            _wavesPrefetched += _prefetchBatch.Count;
            _prefetchFrames++;
            if (_wavesToPrefetch.Count == 0)
            {
                Log.Info(LogCategory, "the player's sound table is handed to the decoders", ("waves", _wavesPrefetched), ("frames", _prefetchFrames), ("per_frame", WavesPerFrame));
            }
        }

        ServerObjectParticles.PumpPending(ServerObjectParticles.ModelsPerFrame);
    }

    /// <summary>
    /// M3 1.3 s1: the player's body was made with a sound table. A worker lists the waves of every sound type in it (footsteps, the server's sounds, the level-up's), and the audio
    /// service's workers decode them into their streams (<see cref="AudioService.Prefetch"/>, a few waves a frame: <see cref="PumpArrivalPrefetch"/>), so the first step and the first server sound play on the frame they are asked for and
    /// not behind the world's streaming work on the pool. Once per table; nothing without an audio service.
    /// </summary>
    private void PrefetchPlayerSounds(uint soundTableId)
    {
        if (AudioService.Current is not { } audio || !_soundTablesPrefetched.Add(soundTableId))
        {
            return;
        }

        var services = _services;
        _ = services.Workers.RunSync(cancellation =>
        {
            var waves = new List<uint>();
            if (audio.Source.GetSoundTable(soundTableId) is { } table)
            {
                foreach (var node in table.Root.Children)
                {
                    foreach (var entry in node.Entries)
                    {
                        waves.Add(entry.WaveId);
                    }
                }
            }

            return waves.ToArray();
        }).ContinueWith(
            t =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    services.MainThread.Enqueue(() =>
                    {
                        foreach (var id in t.Result)
                        {
                            _wavesToPrefetch.Enqueue(id); // handed to the decoders a few a frame (PumpArrivalPrefetch), not all in the frame the worker finishes
                        }
                    });
                }
                else
                {
                    Log.Warn(LogCategory, "the player's sound table could not be prefetched; its waves are decoded when first played", ("table", $"0x{soundTableId:X8}"), ("problem", t.Exception?.GetBaseException().Message ?? "cancelled"));
                }
            },
            TaskScheduler.Default);
    }

    /// <summary>M3 1.3 s1: a body with a PhysicsScriptTable was made; its scripts' particle meshes are prepared ahead (<see cref="ServerObjectParticles.PrepareScriptTable"/>).</summary>
    private void PrepareScriptTable(uint scriptTableId) => ServerObjectParticles.PrepareScriptTable(_services, scriptTableId);

    /// <summary>
    /// Plays the sound hooks (M3 1.3 s1; the M2 stub <c>TODO(g5-w10-sounds)</c>, which dropped them, is gone): the avatars' effect events (animation hooks, the server's F750 sounds) and
    /// the living statics' hooks go to a <see cref="SoundHookRouter"/> over the audio service's retail sound player. The audio service is made after play mode (it comes with the portal view), so this
    /// runs from <see cref="AttachPortal"/>. With no audio service the events are dropped and counted (<c>AvatarSet.Stats.EffectEventsDropped</c>), loudly, once.
    /// </summary>
    private void AttachHookSounds()
    {
        if (_hookSounds is not null)
        {
            return;
        }

        if (AudioService.Current is not { } audio)
        {
            Log.Warn(LogCategory, "no audio service: the avatars' and living statics' sound hooks and the server's sounds are dropped (counted), nothing plays; logged once");
            return;
        }

        _hookSounds = new SoundHookRouter(audio.Sound);
        LivingStaticsSystem.EffectEvents = _hookSounds;
        if (_avatars is not null)
        {
            _avatars.EffectEvents = _hookSounds;
        }

        Log.Info(LogCategory, "the sound hooks are played: the avatars' hooks and the server's sounds, and the living statics' hooks, go to the audio service's sound player");
        Log.Info(LogCategory, "the sound listener is the camera (retail: SmartBox::set_viewer 0x00452C80 -> SoundManager::SetPlayerPosition): the avatars' sounds are positioned and panned from it, as the living statics' and the ambient sound are (M3 4.2 x2; closes TODO(avatars-listener-camera))");
    }

    /// <summary>
    /// Gives play mode the portal view: the <see cref="PortalSessionBridge"/> turns the session's events into <c>TeleportProgress</c> calls and sends
    /// LoginComplete when the destination is ready and the arrival sequence has played. Without a portal view (it could not be built: an error line already
    /// said so) LoginComplete is sent at the player's arrival, loudly, so the game stays usable.
    /// </summary>
    public void AttachPortal(PortalSpaceHandles? portal)
    {
        _portal = portal;
        AttachHookSounds();
        if (GetParent()?.GetNodeOrNull<Lifestone.Game.PortalSpace.PortalNoticeLine>("PortalNoticeLine") is { } portalNotice)
        {
            portalNotice.Shell = () => _ui?.Screens?.Notices; // G4 g2: the shell's spew box takes the portal-space notice; the labelled line stays for a run without the shell
        }

        if (portal is null)
        {
            _loginCompleteFallback = true;
            Log.Error(LogCategory, "TODO(g6-portal-fallback): there is no portal view, so LoginComplete is sent at the player's arrival with no tunnel; portal space is the fidelity rule (DoD 5)");
            return;
        }

        _bridge = new PortalSessionBridge(_host.Session!, portal.Director);
        _host.Session!.Cull.Cells = portal.Attachments; // M5 0 fix R1: the cull reads a dungeon cell's visible cells from the interior registry the streamer loads
        Log.Info(LogCategory, "the portal view is attached: portal space plays on arrival, on server teleports and on logoff");
    }

    /// <inheritdoc/>
    public void PlayerArriving(WorldObject player, bool recreated)
    {
        ArgumentNullException.ThrowIfNull(player);
        _destinationCell = player.Position?.CellId ?? 0;

        // The login screens give way to the game screen now (the character management screen queues it too, but a character entered by --character
        // may arrive before that screen exists).
        if (_ui?.Flow is { } flow && flow.Flow.CurrentMode != UiModes.GamePlay && flow.Flow.NextMode != UiModes.GamePlay)
        {
            flow.Flow.QueueUIModeAfterAFrame(UiModes.GamePlay); // a frame of its own: the shell's construction is not part of the arrival's frame (UiFlow.QueueUIModeAfterAFrame)
        }

        // The baseline of the avatar set's re-creates is taken now, inside the event's dispatch, before the adapter has applied the event: a re-create (F7DB) is
        // known to be applied when the count has risen past it.
        var recreatesBefore = _avatars?.Stats.Recreated ?? 0;
        Post(() => BuildPlayer(player, recreated, recreatesBefore, lead: TunnelLeadFrames));
    }

    // --play-screenshot: the seconds the placed player has been in the world, and whether the frame was taken.
    private double _playShotWait;
    private bool _playShotDone;

    // --play-screenshot hides the developer HUD (the FPS and statistics text) for the capture frame: the frames drawn since it was hidden (-1: not hidden), and what was hidden.
    private int _playShotHiddenFrames = -1;
    private readonly List<(Node Node, bool WasVisible)> _playShotHidden = [];

    /// <summary>The Lifestone developer text over the world (the main scene's <c>Hud</c> layer: position line, F3 overlay and objects rows; the interiors line; the player's key hints), the names <c>--clean-hud</c> hides too.</summary>
    internal static readonly string[] PlayShotDeveloperHudNames = ["Hud", "InteriorsHud", "KeysLabel"];

    /// <summary>Frames the capture waits after hiding the developer HUD, so the viewport's last rendered frame is drawn without it.</summary>
    private const int PlayShotHiddenFrames = 2;

    /// <summary>
    /// <c>--play-screenshot</c>: once the player is present and its physics body is placed, waits <c>--play-screenshot-after</c> seconds and saves the viewport (a review frame of the character as it
    /// is after a scenario), then quits with 0, or 1 when the save failed; with <c>--play-checks</c> it only saves (the checks end the run). A frame that has not got the player yet restarts the wait.
    /// The frame has no developer HUD (the FPS and statistics text is a Lifestone debug overlay, not retail art; M3 1.4 fix B): the HUD is hidden when the wait is over, the capture is taken
    /// <see cref="PlayShotHiddenFrames"/> frames later and the HUD is shown again at once, whether or not the save worked. The retail UI and the placeholder notices stay.
    /// </summary>
    private void StepPlayScreenshot(double delta, string path)
    {
        if (!IsPlayerPresent() || _rig is not { } rig || rig.Controller.Placing || rig.Controller.Position.CellId == 0)
        {
            _playShotWait = 0;
            RestoreDeveloperHud();
            return;
        }

        _playShotWait += delta;
        if (_playShotWait < _services.Options.Play.ScreenshotAfterSeconds)
        {
            return;
        }

        if (_playShotHiddenFrames < 0)
        {
            HideDeveloperHud();
            _playShotHiddenFrames = 0;
            return;
        }

        if (++_playShotHiddenFrames < PlayShotHiddenFrames)
        {
            return;
        }

        _playShotDone = true;
        var saved = ScreenshotWriter.Save(GetViewport(), path, LogCategory, ("frame", "play-screenshot"), ("after_seconds", _playShotWait), ("developer_hud", "hidden"));
        RestoreDeveloperHud();
        Log.Info(LogCategory, saved ? "play screenshot saved" : "play screenshot failed");
        if (_services.Options.Checks != GameCheckSuite.Play)
        {
            GetTree().Quit(saved ? 0 : 1);
        }
    }

    private void HideDeveloperHud()
    {
        _playShotHidden.Clear();
        foreach (var name in PlayShotDeveloperHudNames)
        {
            switch (GetTree().Root.FindChild(name, recursive: true, owned: false))
            {
                case CanvasLayer layer:
                    _playShotHidden.Add((layer, layer.Visible));
                    layer.Visible = false;
                    break;
                case CanvasItem item:
                    _playShotHidden.Add((item, item.Visible));
                    item.Visible = false;
                    break;
            }
        }

        Log.Info(LogCategory, "play screenshot: the developer HUD is hidden for the capture frame", ("hidden", _playShotHidden.Count));
    }

    private void RestoreDeveloperHud()
    {
        if (_playShotHiddenFrames < 0)
        {
            return;
        }

        foreach (var (node, wasVisible) in _playShotHidden)
        {
            if (!GodotObject.IsInstanceValid(node))
            {
                continue;
            }

            switch (node)
            {
                case CanvasLayer layer:
                    layer.Visible = wasVisible;
                    break;
                case CanvasItem item:
                    item.Visible = wasVisible;
                    break;
            }
        }

        _playShotHidden.Clear();
        _playShotHiddenFrames = -1;
    }

    /// <inheritdoc/>
    public void PlayerPresent() => Log.Info(LogCategory, "the player is in the world", ("cell", $"0x{_destinationCell:X8}"));

    /// <inheritdoc/>
    public void PlayerTeleported(in ObjectMoved move)
    {
        var at = move.Position;
        _destinationCell = at.CellId;
        Post(() =>
        {
            if (_rig is { } rig)
            {
                PlayerPlacement.Teleport(_world, _interiors, rig, at);
                if (_loginCompleteFallback)
                {
                    // ACE needs a LoginComplete after every teleport (Player.OnTeleportComplete), not only after the arrival: without the portal view's bridge
                    // the player would stay hidden and the movement gate closed for good after a server teleport.
                    _host.Session!.Actions.LoginComplete(ActionSource.Client);
                    Log.Warn(LogCategory, "TODO(g6-portal-fallback): no portal view, so LoginComplete is sent right after the teleport", ("cell", $"0x{at.CellId:X8}"));
                }
            }
        });
    }

    /// <inheritdoc/>
    public void PlayerPositioned(in ObjectMoved move)
    {
        var moved = move;
        Post(() =>
        {
            if (_rig is { } rig)
            {
                rig.Controller.Controller.Apply(moved, PlayerController.Clock());
            }
        });
    }

    /// <inheritdoc/>
    /// <remarks>The adapter feeds the player's state to the avatar set, in order with every other object's events (a cleared Hidden bit plays the UnHide script).</remarks>
    public void PlayerStateChanged(in ObjectStateChanged change)
    {
    }

    /// <inheritdoc/>
    /// <remarks>The adapter feeds the player's server movements (an emote, an action, a MoveTo) to the avatar set.</remarks>
    public void PlayerMotionChanged(in ObjectMotionChanged change)
    {
    }

    /// <inheritdoc/>
    /// <remarks>The adapter feeds the player's new look to the avatar set.</remarks>
    public void PlayerDescChanged(in ObjectDescChanged change)
    {
    }

    /// <inheritdoc/>
    public void PlayerLeft(PlayLeaveReason reason)
    {
        _destinationCell = 0;
        Post(() =>
        {
            TearDown(reason);
            NewObjects(); // the objects belong to the arrival that is over
        });
    }

    /// <inheritdoc/>
    public override void _EnterTree() => Current = this;

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        Selection.Dispose();
        if (_endKey is not null)
        {
            _ui?.Manager.Input.UnregisterCallback(_endKey);
        }

        _bridge?.Dispose();
        _flow?.Dispose();
        _adapter?.Dispose();
        if (_avatars is not null && _host?.Session is { } bodiless && ReferenceEquals(bodiless.Live.Bodies, _avatars))
        {
            bodiless.UseBodies(null); // M6 14 (m6-live-position): this mode's avatar set is no longer stepped; the server's report answers again
        }

        _chatLog?.Dispose();
        _creatureCapture?.Dispose();
        _captureFrames?.LogNotSeen();
        if (ReferenceEquals(LivingStaticsSystem.EffectEvents, _hookSounds))
        {
            LivingStaticsSystem.EffectEvents = null;
        }

        _lightTable?.Dispose(); // stops its pre-draw upload; a late Release or Write from a node leaving the tree after this one does nothing
        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        using var framePhase = Lifestone.Game.Diagnostics.FramePhases.Play(); // M6 16 Eperf2: this call's share of a long frame
        // The work first (building the player is the heavy item), then the session's object events, in order, inside a budget: a frame that did heavy work leaves the
        // events to the next one, so no frame carries both (DoD 15). The player's own CreateObject is the first event of an arrival; the player's build waits for it.
        var frameStart = Stopwatch.GetTimestamp();
        var queued = _work.Count;
        var thisFrame = Engine.GetProcessFrames();
        while (_work.Count > 0 && _work.Peek().Frame < thisFrame)
        {
            var (_, work) = _work.Dequeue();
            try
            {
                work();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log.Error(LogCategory, "play mode work threw", ("type", ex.GetType().Name), ("problem", ex.Message));
                if (_services.Options.IsAutomated)
                {
                    GetTree().Quit(1);
                }
            }
        }

        var workMs = Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds;
        var applied = 0;
        var pumpMs = 0.0;
        if (_adapter is not null && workMs < HeavyWorkMilliseconds)
        {
            var pumpStart = Stopwatch.GetTimestamp();
            applied = _adapter.Pump(pumpStart + (long)(AdapterBudgetMilliseconds * Stopwatch.Frequency / 1000.0));
            pumpMs = Stopwatch.GetElapsedTime(pumpStart).TotalMilliseconds;
            if (pumpMs < HeavyWorkMilliseconds / 4)
            {
                PumpArrivalPrefetch(); // the sound and particle prefetch of the creation burst, a few items a frame, never on a frame that did the burst
            }
        }

        DrainAvatarNotices(); // every frame, so the set's 1,024-notice list never fills in a run of hours (M5 item 7)

        if (!_playShotDone && _services.Options.Play.ScreenshotPath is { } shotPath)
        {
            StepPlayScreenshot(delta, shotPath);
        }

        if (!_panelShotsDone && _services.Options.Play.PanelShotsDirectory is { } panelShotsDirectory)
        {
            StepPanelShots(delta, panelShotsDirectory);
        }

        if (!_lookShotsDone && _services.Options.Play.LookShotsDirectory is { } lookShotsDirectory)
        {
            StepLookShots(delta, lookShotsDirectory); // M4 4.1 lane v0: the look capture (PlayMode.LookShots.cs)
        }

        // Once the physics body is placed, the destination is the player's own cell (it follows portals and landblock crossings).
        if (_rig is { } rig && !rig.Controller.Placing && rig.Controller.Position.CellId != 0)
        {
            _destinationCell = rig.Controller.Position.CellId;
        }

        // The soak frames (--soak-frame-s): the countdown starts when the player's body is first placed in the world (and again for a new login's body).
        _soakFrames ??= new SoakFrames(_services.Options.Play.SoakFrameSeconds, _services.Options.Play.SoakFrameDirectory);
        if (_soakFrames.Active)
        {
            _soakFrames.Step(GetViewport(), _rig is { } placed && !placed.Controller.Placing && placed.Controller.Position.CellId != 0 ? placed : null);
        }

        // M5 7 D129: the paper doll's live review frames (--doll-review-frames), once, the first time the doll shows.
        _dollFrames ??= new DollReviewFrames(_services.Options.Play.DollReviewFramesDirectory, HideDeveloperHud, RestoreDeveloperHud);
        if (_dollFrames.Active)
        {
            _dollFrames.Step(
                GetViewport(),
                _ui?.Screens?.Flow.CurrentScreen as Lifestone.Client.RetailUi.Screens.GamePlayScreen,
                playerPlaced: _rig is { } placedRig && !placedRig.Controller.Placing && placedRig.Controller.Position.CellId != 0,
                tunnelVisible: _portal?.Director.Machine.TunnelVisible == true,
                playerMaterialised: _avatars?.Player?.Body.Materialised == true);
        }

        // M5 7 S6 (D136): the examination's live review frames (--examine-review-frames), once, the first magic item a container window shows.
        _examineFrames ??= new ExamineReviewFrames(_services.Options.Play.ExamineReviewFramesDirectory, HideDeveloperHud, RestoreDeveloperHud);
        if (_examineFrames.Active)
        {
            _examineFrames.Step(
                GetViewport(),
                _ui?.Screens?.Flow.CurrentScreen as Lifestone.Client.RetailUi.Screens.GamePlayScreen,
                _ui?.LogicalSize ?? default,
                playerPlaced: _rig is { } examineRig && !examineRig.Controller.Placing && examineRig.Controller.Position.CellId != 0,
                tunnelVisible: _portal?.Director.Machine.TunnelVisible == true,
                playerMaterialised: _avatars?.Player?.Body.Materialised == true);
        }

        // M5 7 D142: the worn caster's and robe's examination frames (--examine-worn-frames), once each, when their pages show a spell-changed value.
        _wornFrames ??= new ExamineWornFrames(_services.Options.Play.ExamineWornFramesDirectory, HideDeveloperHud, RestoreDeveloperHud);
        if (_wornFrames.Active)
        {
            _wornFrames.Step(
                GetViewport(),
                _ui?.Screens?.Flow.CurrentScreen as Lifestone.Client.RetailUi.Screens.GamePlayScreen,
                _host.Session,
                _ui?.LogicalSize ?? default,
                playerPlaced: _rig is { } wornRig && !wornRig.Controller.Placing && wornRig.Controller.Position.CellId != 0,
                tunnelVisible: _portal?.Director.Machine.TunnelVisible == true,
                playerMaterialised: _avatars?.Player?.Body.Materialised == true);
        }

        // M5 7 D137: the failed-roll live capture (--examine-capture-creatures=N): each new creature the fight aims at is inspected; one Identify an ask, nothing waits.
        if (!_creatureCaptureLooked && _services.Options.Play.ExamineCaptureCreatures is { } answers && _host.Session is { } captureSession && _ui?.Manager is { } captureUi)
        {
            _creatureCaptureLooked = true;
            _creatureCapture = new Lifestone.Client.RetailUi.Panels.ExamineCreatureCapture(captureSession, captureUi.TargetMode, GodotLogSink.Instance, answers);
            _captureFrames = new ExamineCaptureFrames(_services.Options.Play.ExamineReviewFramesDirectory ?? DefaultCaptureFramesDirectory, HideDeveloperHud, RestoreDeveloperHud); // D138: the live proof frames
        }

        if (_captureFrames is { Active: true } proof)
        {
            proof.Step(
                GetViewport(),
                _ui?.Screens?.Flow.CurrentScreen as Lifestone.Client.RetailUi.Screens.GamePlayScreen,
                playerPlaced: _rig is { } proofRig && !proofRig.Controller.Placing && proofRig.Controller.Position.CellId != 0,
                tunnelVisible: _portal?.Director.Machine.TunnelVisible == true,
                playerMaterialised: _avatars?.Player?.Body.Materialised == true);
        }

        if (_creatureCapture is { Active: true } capture && _ui?.Screens?.Flow.CurrentScreen is Lifestone.Client.RetailUi.Screens.GamePlayScreen { PanelHost.Examine: not null })
        {
            capture.Step();
        }

        // M5 7 F17: the vitals line every --log-vitals-s seconds (one clock compare a frame; the text is built when a line is due).
        _vitalsLog ??= new VitalsLog(_services.Options.Play.LogVitalsSeconds, GodotLogSink.Instance);
        if (_vitalsLog.Enabled)
        {
            _vitalsLog.Step(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency, _host.Session?.State.Self.Qualities);
        }

        // g3b: the radar follows the live physics body (the server's last position lags it) and the selection (docs/retail-ui.md section 21.4).
        if (_ui?.Screens?.Flow.CurrentScreen is Lifestone.Client.RetailUi.Screens.GamePlayScreen { RadarUi: { SelectedProvider: null } radar })
        {
            radar.SelectedProvider = () => Selection.Selected;
            radar.PlayerProvider = () => _rig is { } live && !live.Controller.Placing && live.Controller.Position.CellId != 0 ? live.Controller.Position : _host.Session!.State.Self.Position;
        }

        // g7: the tools panel's Where am I compares the server's position with the walker body's (not the session state's, which is the server's own); null until the body is placed.
        if (_ui?.Screens?.Flow.CurrentScreen is Lifestone.Client.RetailUi.Screens.GamePlayScreen { ToolsUi: { Model.ClientPosition: null } tools })
        {
            tools.Model.ClientPosition = () => _rig is { } live && !live.Controller.Placing && live.Controller.Position.CellId != 0 ? live.Controller.Position : null;
        }

        // g5b: the toolbar's selection display and the vivid target indicator follow the selection and the layer's projector; portal space turns the indicator off (docs/retail-ui.md section 21.7).
        if (_ui?.Screens?.Flow.CurrentScreen is Lifestone.Client.RetailUi.Screens.GamePlayScreen { SelectionUi: { } shellSelection })
        {
            shellSelection.SelectedProvider ??= () => Selection.Selected;
            shellSelection.SelectRequester ??= guid => Selection.Select(guid); // M5 7 D122: the paperdoll figure's left click
            shellSelection.ProjectorProvider ??= () => _ui?.SelectionProjector;
            shellSelection.Indicator.Enabled = _portal?.Presenter.SelectionIndicatorEnabled ?? true;
        }

        // The shell's spew box and chat replace the status line; in the offline stand-in session a short note stays (its vitals, radar objects and chat are placeholder values, review R15). Never in a live session.
        var shellUp = _ui?.Screens?.Flow.CurrentScreen is Lifestone.Client.RetailUi.Screens.GamePlayScreen;
        var standInNote = shellUp && _host.StandIn is not null;
        if (standInNote)
        {
            _status.SetNote(PlayStatusLine.StandInNote);
        }
        else
        {
            _status.SetText(_flow.StatusText);
        }

        _status.Visible = !shellUp || standInNote;
        var totalMs = Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds;
        LastStepMilliseconds = totalMs;
        LastWorkMilliseconds = workMs;
        LastAdapterMilliseconds = pumpMs;
        LastEventsApplied = applied;
        if (totalMs > 8.0)
        {
            // The parts of a slow play-mode step (TODO(login-burst-frame)): at Information so a failing live run's log names the part without a debug rerun.
            Log.Info(LogCategory, "play mode took long this frame", ("total_ms", totalMs), ("work_ms", workMs), ("adapter_ms", pumpMs), ("events_applied", applied), ("work_items", queued), ("session_tick_ms", _host.Session?.LastTickMs ?? 0.0));
        }
    }

    /// <summary>
    /// Moves the avatar set's notices of this frame into <see cref="_frameNotices"/> (one reused list, nothing allocated on a frame without any) and hands them to the tally, which counts them
    /// by kind and logs each problem kind at most once a minute. The look shots read the same list for their parent-failure check.
    /// </summary>
    private void DrainAvatarNotices()
    {
        _frameNotices.Clear();
        if (_avatars is null || _avatars.DrainNotices(_frameNotices) == 0)
        {
            return;
        }

        _noticeTally ??= new AvatarNoticeTally(GodotLogSink.Instance, "avatars", _services.Options.Play.LogObjectsMetres); // --log-objects-m: the creates and deletes near the player (M5 7 F17)
        _noticeTally.Add(_frameNotices, Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
    }

    /// <summary>The character option "run by default" (<c>ToggleRun</c>, word 1 bit 0x400; <c>PlayerModule::ToggleRun</c>) from the PlayerDescription's options; true until the description is known (a new character's default has it on).</summary>
    internal static bool RunByDefaultOf(Lifestone.Client.Session.Contracts.PlayerQualities qualities) => !qualities.IsKnown || (qualities.CharacterOptions1 & RunByDefaultOptionMask) != 0;

    private const uint RunByDefaultOptionMask = 0x400;

    /// <summary>
    /// The character's run-by-default option (M2 wave 3 lanes B and C): <c>PlayerRigRequest.RunByDefault</c> carries it to <c>PlayerRig.Build</c>. The option is read when the rig is
    /// built, so a toggle in the options panel later in the session does not reach it.
    /// </summary>
    private static PlayerRigRequest WithRunByDefault(PlayerRigRequest request, bool runByDefault) => request with { RunByDefault = runByDefault };

    /// <summary>
    /// Queues work for the start of this node's process in a LATER frame than the one that asks: the session's events arrive inside the session host's tick, in the
    /// same frame as its burst of CreateObjects, so the work that builds the player waits a frame instead of piling on that one (DoD 15, no frame over 50 ms).
    /// </summary>
    private void Post(Action work) => _work.Enqueue((Engine.GetProcessFrames(), work));

    /// <summary>
    /// A new avatar set, adapter and (empty) layer content: at start-up and after every log off. The adapter subscribes at once, before the next login's first
    /// CreateObject.
    /// </summary>
    private void NewObjects()
    {
        _adapter?.Dispose();
        _chatLog?.Dispose(); // one tap on the session's events, made again with the set (a log off must not leave two)
        _chatLog = new ChatLogTap(_host.Session!.Events, GodotLogSink.Instance);
        Selection.Clear(); // a log off (and the start): the objects belong to the arrival that is over
        _avatars = new AvatarSet(_sources!, GodotLogSink.Instance) { RetailUnhideFlash = _services.Options.Play.RetailUnhideFlash, RetailSpherelessMissile = _services.Options.Play.RetailSphereless, EffectEvents = _hookSounds, ScriptTableSeen = PrepareScriptTable, PlayerSoundTableSeen = PrefetchPlayerSounds }; // --retail-unhide-flash (docs/avatars.md section 17)
        _avatars.Listener = new Lifestone.Game.World.ServerObjects.FlyCameraListener(_camera); // the sound listener is the camera (M3 4.2 x2, TODO(avatars-listener-camera) closed)
        PlayerRig.WireDynamicLights(_avatars, _interiors); // --viewer-light and the cell glow's source, the one wiring every avatar set shares (M3 4.1 fix A, R1)
        var session = _host.Session!;
        _adapter = new SessionAvatarAdapter(session.Events, session.State, _avatars, PlayerController.Clock, GodotLogSink.Instance);
        session.UseBodies(_avatars); // M6 14 (m6-live-position): the fight reads a creature where this set draws it, not where the server last reported it
        if (_layer is not null)
        {
            _layer.Active = false;
            _layer.Avatars = _avatars;
        }
    }

    private void BuildPlayer(WorldObject obj, bool recreated, long recreatesBefore, int waited = 0, int lead = 0)
    {
        if (lead > 0)
        {
            // The tunnel's first frames come first: the render server compiles the tunnel's canvas pipeline in the frame after the tunnel starts (15-38 ms,
            // TODO(arrival-pipelines)), and a build of the player's 34 parts in the very next frame landed on it (17 ms of the main thread behind the render
            // thread's 30: 50 to 56 ms in 5 of 6 first runs, 60 Hz vsync rounding the sum up to three refreshes).
            Post(() => BuildPlayer(obj, recreated, recreatesBefore, waited, lead - 1));
            return;
        }

        var avatars = _avatars!;
        // The player's avatar is the adapter's: wait (a frame at a time) until it has applied this arrival's CreateObject.
        var ready = avatars.Player is { } player && player.Guid == obj.Guid && (!recreated || avatars.Stats.Recreated > recreatesBefore);
        if (!ready)
        {
            if (waited >= AvatarWaitFrames)
            {
                Log.Error(LogCategory, "the avatar set never took the player's CreateObject; the world stays paused", ("guid", $"0x{obj.Guid:X8}"), ("pending_events", _adapter?.Pending ?? -1));
                if (_services.Options.IsAutomated)
                {
                    GetTree().Quit(1);
                }

                return;
            }

            var next = waited + 1;
            Post(() => BuildPlayer(obj, recreated, recreatesBefore, next));
            return;
        }

        if (_rig is not null)
        {
            TearDown(PlayLeaveReason.LoggedOff); // a re-create (F7DB): the old player goes, the new one is built from the new description
        }

        if (obj.Position is not { } at)
        {
            Log.Error(LogCategory, "the player has no position; there is nothing to build the world around");
            return;
        }

        var scenarioHost = ScenarioHost;
        var gate = new PresenceGatedMovementSink(_host.Session!.Actions.Movement, scenarioHost is null ? IsPlayerPresent : () => IsPlayerPresent() && !scenarioHost.Holding);
        if (scenarioHost is not null)
        {
            scenarioHost.Inner = gate;
        }

        // The walker's Run and Jump are the server's: the session's qualities, read afresh at every question (the PlayerDescription may arrive after this build).
        var weenie = QualitiesWeenie.FromSkillTable(_host.Session.State.Self.Qualities, _skills!, GodotLogSink.Instance); // a new one per arrival: a reconnect resets the state
        var request = new PlayerRigRequest(
            _main,
            _services,
            _world,
            _interiors,
            _camera,
            _sources!,
            AvatarWire.FromWorldObject(obj),
            at,
            obj.Guid,
            scenarioHost is null ? gate : scenarioHost.Sink,
            StandInLabel: null,
            Avatars: avatars,
            LightTable: _lightTable,
            Geometry: _geometry,
            DeferKeys: true,
            DeferFocus: true,
            Weenie: weenie);
        // M5 8 build: the player's build runs as three steps on three consecutive frames (avatar and first meshes, the drawn meshes, the body and camera), not in one frame of 15-25 ms.
        // Nothing of the player is drawn or stepped until the last step (the controller is made there), and _rig is set only then.
        _pendingBuild?.Cancel();
        var pending = PlayerRig.BeginBuild(WithRunByDefault(request, RunByDefaultOf(_host.Session.State.Self.Qualities)));
        _pendingBuild = pending;
        LastPlayerBuild = pending.Work;
        _playerBuildFrames.Clear();
        StepPlayerBuild(pending, at, gate, avatars, recreated);
    }

    /// <summary>The player's build in progress, between its steps (null when none); a log off or a re-create cancels it (<see cref="TearDown"/>, <see cref="BuildPlayer"/>).</summary>
    private PlayerRig.PendingBuild? _pendingBuild;

    /// <summary>The last player build's steps (names, per-step milliseconds), for the play-checks' report-only line.</summary>
    internal Lifestone.Client.Scheduling.StagedWork? LastPlayerBuild { get; private set; }

    /// <summary>The process frame each step of the last player build ran in (one step per frame), for the play-checks' report-only line.</summary>
    internal IReadOnlyList<ulong> PlayerBuildFrames => _playerBuildFrames;

    private readonly List<ulong> _playerBuildFrames = [];

    /// <summary>True when the rig existed before the build's last step ran: the player would have been usable half built (must stay false).</summary>
    internal bool PlayerSeenHalfBuilt { get; private set; }

    /// <summary>Runs the next step of <paramref name="pending"/> and, unless it was the last, posts the one after to the next frame.</summary>
    private void StepPlayerBuild(PlayerRig.PendingBuild pending, Lifestone.Data.World.CellFrame at, PresenceGatedMovementSink gate, AvatarSet avatars, bool recreated)
    {
        if (!ReferenceEquals(_pendingBuild, pending))
        {
            return; // cancelled between two steps
        }

        pending.RunNext();
        _playerBuildFrames.Add(Engine.GetProcessFrames());
        if (pending.Work.HasNext && _rig is not null)
        {
            PlayerSeenHalfBuilt = true;
        }

        if (pending.Failed)
        {
            _pendingBuild = null;
            Log.Error(LogCategory, "the player could not be built; the world stays paused", ("recreated", recreated), ("failed_after", pending.Work.NextName ?? "the last step"));
            if (_services.Options.IsAutomated)
            {
                GetTree().Quit(1);
            }

            return;
        }

        if (pending.Work.HasNext)
        {
            Post(() => StepPlayerBuild(pending, at, gate, avatars, recreated));
            return;
        }

        _pendingBuild = null;
        FinishPlayerBuild(pending.Rig!, at, gate, avatars, recreated);
    }

    /// <summary>The player is built: bind it to the session and start the keys and the world around it in the next frames (what followed <c>PlayerRig.Build</c> in one frame before the build was staged).</summary>
    private void FinishPlayerBuild(PlayerRig rig, Lifestone.Data.World.CellFrame at, PresenceGatedMovementSink gate, AvatarSet avatars, bool recreated)
    {
        _rig = rig;
        MovementGate = gate;
        rig.Controller.Picking = _picking;
        rig.Controller.Selection = Selection;
        rig.Controller.Actions = _host.Session!.Actions;
        if (_services.Dats is { } shotDats)
        {
            _host.Session.Actions.UseShots(new Lifestone.Client.Session.Casting.PhysicsShotCheck(() => _rig?.Physics, shotDats)); // M6 8 L: both fights check the shot on the player's own physics world before a bolt is paid for
        }

        rig.Controller.TargetMode = _ui?.Manager.TargetMode; // M5 7 S6: the toolbar's Examine with nothing selected asks for the object the next click names
        BindMoveIntents(rig, _host.Session.Actions.Move);
        if (_layer is not null)
        {
            _layer.Avatars = avatars;
            _layer.PlayerSource = rig.Controller;
            _layer.Active = true;
        }

        // The keys are set up in the next frame (their maps come from the dats), through the retail UI's router as in the offline player.
        Post(() =>
        {
            if (ReferenceEquals(_rig, rig))
            {
                rig.AttachKeys();
                if (_ui is not null)
                {
                    rig.Controller.Input?.UseRetailUi(_ui);
                }
            }
        });

        if (_portal is not null)
        {
            _portal.Attachments.CollisionSource = rig.Controller.Streamer;
        }

        _destinationCell = at.CellId;

        // The world starts around the player in the next frame: the player's build and the world's start are two frames' work (DoD 15).
        Post(() =>
        {
            if (!ReferenceEquals(_rig, rig))
            {
                return;
            }

            var worldStart = Stopwatch.GetTimestamp();
            rig.UseAsWorldFocus();
            PlayerPlacement.StartWorldAroundSplit(_world, _interiors, at, work => Post(() => { if (ReferenceEquals(_rig, rig)) { work(); } }));
            Log.Info(LogCategory, "the world started around the player (work in this frame)", ("ms", Stopwatch.GetElapsedTime(worldStart).TotalMilliseconds));
            Log.Info(LogCategory, "the world starts around the player", ("cell", $"0x{at.CellId:X8}"), ("at", at.Origin), ("recreated", recreated));
            if (_loginCompleteFallback)
            {
                _host.Session!.Actions.LoginComplete(ActionSource.Client);
            }
        });
    }

    private bool IsPlayerPresent() => _host.Session?.State.Self.Travel == SelfTravel.Present;

    /// <summary>
    /// Scripted movement goes through <c>IActionApi.Move</c> in this host: the rig's controller is bound with the character's run-by-default
    /// option (the one the rig was built with, so the run key means run or walk whatever the option), and a movement key a person presses is
    /// reported to it (<see cref="MoveIntents.HumanKey"/>, R6: the human's key wins).
    /// </summary>
    private void BindMoveIntents(PlayerRig rig, MoveIntents move)
    {
        move.Bind(rig.Controller.Walker.Controller, rig.Controller.Walker.Controller.Options.RunByDefault);
        _humanKey = move.ReportHumanKey;
        rig.Controller.HumanKey += _humanKey;
        rig.Controller.KeyLock = move.KeyLock; // M5 7 D145: the movement keys do nothing while the autopilot holds the lock
        _boundMove = move;
    }

    private void UnbindMoveIntents(PlayerRig rig)
    {
        if (_humanKey is not null)
        {
            rig.Controller.HumanKey -= _humanKey;
            _humanKey = null;
        }

        rig.Controller.KeyLock = null;

        _boundMove?.Unbind(rig.Controller.Walker.Controller);
        _boundMove = null;
    }

    private void TearDown(PlayLeaveReason reason)
    {
        _pendingBuild?.Cancel(); // a log off between two steps of the player's build: the nodes its steps added go
        _pendingBuild = null;
        if (_portal is not null)
        {
            _portal.Attachments.CollisionSource = null;
        }

        if (_layer is not null)
        {
            _layer.Active = false;
            _layer.PlayerSource = null;
        }

        if (_rig is { } rig)
        {
            UnbindMoveIntents(rig);
            rig.Free();
            _rig = null;
            MovementGate = null;
        }

        _world.FocusSource = null; // paused until the next arrival
        Log.Info(LogCategory, "the player left the world; the world waits", ("reason", reason));
    }
}
