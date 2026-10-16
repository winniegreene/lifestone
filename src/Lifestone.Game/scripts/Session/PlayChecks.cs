using Godot;
using Lifestone.Client.Audio;
using Lifestone.Client.Navigation;
using Lifestone.Client.Play;
using Lifestone.Client.PortalSpace;
using Lifestone.Client.RetailUi.Chat;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Terrain;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.PortalSpace;
using Lifestone.Game.Player;
using Lifestone.Game.RetailUi;
using Lifestone.Game.World;
using Lifestone.Game.World.Interiors;

namespace Lifestone.Game.Sessions;

/// <summary>
/// <c>--play-checks</c> (m2-plan G6 W9; docs/session.md section 21): play mode in the engine on a scripted stand-in server, with no backend. A
/// <see cref="StandInVisit"/> plays what ACE would send (the character list, an entry, the player created hidden at Holtburg, the unhide, a teleport to the
/// Training Academy, a logoff) through the session's own reducer, and this node watches the real <see cref="PlayMode"/>, the real portal view and the real
/// retail UI react: the world waits for the player; the player is built from its own CreateObject with the session's movement sink; the world streams
/// around it and physics places it on the ground; portal space plays and sends LoginComplete once, before the server unhides the player; a server teleport
/// moves the world, the interiors, the physics and the camera; a logoff takes it all away and the UI is back at character management.
/// </summary>
/// <remarks>
/// <para>Verdict lines (stdout and the log): <c>[playcheck] PASS|FAIL name</c>, then <c>all play checks hold</c> (exit 0) or <c>PLAY CHECKS FAILED</c> (exit 1).
/// What it cannot check: the bytes on the wire, ACE's acceptance of anything, and the real LoginComplete (the stand-in session refuses every server action);
/// the live login scenario of W12 does.</para>
/// <para>The player's look is the base human body (the stand-in sends no description): a placeholder, as the stand-in character of <c>--offline-player</c> is.</para>
/// </remarks>
public sealed partial class PlayChecks : Node
{
    /// <summary>The Training Academy's start room, where the visit teleports (an EnvCell: the interiors, the dungeon's streaming and indoor physics are exercised).</summary>
    public const uint AcademyCell = 0x860201AD;

    /// <summary>The Academy's start position in <see cref="AcademyCell"/> (ACE's start location for a new character; <c>InteriorLocations</c>).</summary>
    public static System.Numerics.Vector3 AcademyOrigin { get; } = new(12.3199f, -28.482f, 0.005f);

    /// <summary>The visit the checks play: Holtburg, the unhide after portal space has had time to send LoginComplete, a teleport to the Academy, a logoff.</summary>
    public static StandInVisit Visit { get; } = new()
    {
        HoldEntry = true, // g4 live-gate fix 3: the server answers as a live one does, EnterAfter seconds after the retail character list is on the screen (docs/session.md section 27)
        EnterAfter = 0.2, // M3 1.2 s5: the plan's rehearsal: Enter 0.2 s after the list, so the arrival lands while the shell is still warming (was 0.1)
        UnhideAfter = 20.0,
        Stats = new StandInPlayerStats { Enchantments = StandInPlayerStats.LiveShapedEnchantments(), Level = 1, TotalExperience = 2_000_000, AvailableExperience = 2_000_000, Spells = StandInPlayerStats.ProbeSpells(), SpellbookFilters = StandInPlayerStats.ProbeSpellbookFilters, ExtraEnchantments = StandInPlayerStats.ProbeEnchantments() }, // M3 2.2 fix B: the probe spellbook (R5) and a harmful enchantment and a vitae (R6) on either path, synthetic or replayed; // level and experience: placeholders the raise check spends; // the live gate's PlayerDescription shape: 138 enchantments with layered duplicates (docs/session.md section 27)
        Recorded = FindRecorded(), // M3 1.2 s5: the live-shaped rehearsal: a recorded PlayerDescription from cache/sessions when one exists, else the synthetic one above
        Teleports = [new StandInTeleport(4.0, AcademyCell, AcademyOrigin)],
        Objects =
        [
            new StandInObject(0x80000A01, "Stand-in Guard", new System.Numerics.Vector3(3f, 0f, 0f), DescriptionFlags: 0x10), // attackable: a gold creature blip
            new StandInObject(0x80000A02, "Stand-in Statue", new System.Numerics.Vector3(0f, 4f, 0f), MotionTable: 0, BlipColor: 8), // the server's yellow blip colour
            new StandInObject(PortalGuid, "Stand-in Portal", new System.Numerics.Vector3(-3f, 0f, 0f), MotionTable: 0, PortalTo: new StandInTeleport(0, AcademyCell, AcademyOrigin)),

            new StandInObject(ApparitionGuid, "Stand-in Apparition", new System.Numerics.Vector3(-2f, 5f, 0f), MotionTable: 0, UnhideAfter: 0.0), // D63: created hidden, unhidden with the player: its materialise fade is drawn by the same path as the player's

            new StandInObject(0x80000A04, "Stand-in Drudge", new System.Numerics.Vector3(2f, 9f, 0f), MotionTable: 0, DescriptionFlags: 0x10), // g5b: an attackable creature ahead of the player, selected by the selection checks (name, health, indicator)

            // g3b: placeholders so the radar has blips of each colour away from the centre; all behind or beside the player (south), out of the click checks' way.
            new StandInObject(0x80000A10, "Stand-in Creature", new System.Numerics.Vector3(-20f, -25f, 0f), MotionTable: 0, DescriptionFlags: 0x10), // gold
            new StandInObject(0x80000A11, "Stand-in Vendor", new System.Numerics.Vector3(25f, -10f, 0f), MotionTable: 0, DescriptionFlags: 0x200), // yellow
            new StandInObject(0x80000A12, "Stand-in Lifestone", new System.Numerics.Vector3(-35f, -5f, 0f), MotionTable: 0, BlipColor: 1), // the server's blue
            new StandInObject(0x80000A13, "Stand-in Player Killer", new System.Numerics.Vector3(10f, -40f, 0f), MotionTable: 0, DescriptionFlags: 0x8 | 0x20), // red
            new StandInObject(0x80000A14, "Stand-in Player", new System.Numerics.Vector3(-5f, -55f, 0f), MotionTable: 0, DescriptionFlags: 0x8), // white
        ],
        Speech = [new StandInSpeech(1.0, "Stand-in Town Crier", 0x80000A03, "Hear ye: the portal to the Academy stands open.")], // a placeholder speaker for the chat window
        Scripts = [new StandInScript(1.2, 0, (uint)DatReaderWriter.Enums.PlayScript.LevelUp, 1f)], // M3 1.3 s0: one F755 on the player (the human table's LevelUp: 3 particle emitters and a sound hook), as ACE sends a level-up's effect
        Sounds = [new StandInSound(1.2, 0, (uint)DatReaderWriter.Enums.Sound.EnchantUp, 0.75f)], // M3 1.3 s0: one F750 on the player (a type the human SoundTable maps), right behind it
        VitalUpdates = [new StandInVitalUpdate(2.0, 2, 41)], // g3a: the current health 52 of 75 becomes 41 (PrivateUpdateAttribute2ndLevel) two seconds after the unhide
        LogOffDelay = 6.0, // longer than retail's 3 s wait and the transition's collapse, so portal space's logoff tunnel is up before the character list returns
    };

    /// <summary>The visit after a reconnect (the disconnected screen's button runs the stand-in again): the arrival only, with the unhide a little earlier.</summary>
    public static StandInVisit ReplayVisit { get; } = Visit with { HoldEntry = false, EnterAfter = 3.0, UnhideAfter = 14.0, Teleports = [], Objects = [], Speech = [], Scripts = [], Sounds = [] };

    /// <summary>The stand-in portal's object id (a server object's: 0x80000000 and up). A Use of it teleports the player to the Academy.</summary>
    public const uint PortalGuid = 0x80000A03;

    /// <summary>The stand-in object the server creates hidden and unhides with the player (the materialise fade of an object, D63).</summary>
    public const uint ApparitionGuid = 0x80000A05;

    /// <summary>
    /// The materialise fade the checks expect, in seconds: UnHide script 0x3300032F's one Transparent hook (docs/avatars.md section 17). The measured time from the first
    /// fading frame to the first opaque one is the hook's 0.75 s plus up to a frame, so it is judged within <see cref="FadeToleranceSeconds"/> of it.
    /// </summary>
    public const double FadeSeconds = 0.75;

    /// <summary>The tolerance of the measured fade time, in seconds, plus one 30 Hz frame: the window is cut on whole frames at both ends.</summary>
    public const double FadeToleranceSeconds = 0.10;

    /// <summary>Fading frames that may show no twin part yet: the server-objects layer runs after this node, so its count of a frame is read one frame late (S4).</summary>
    public const int TwinLagFrames = 2;

    // The particle GfxObjs of the human script table's LevelUp script (0x330006F7: three emitters, 40 particles each, 3 s): what the F755 shows on the player.
    private const uint LevelUpGfxA = 0x01001062;
    private const uint LevelUpGfxB = 0x01001067;
    private const uint LevelUpGfxC = 0x010010A2;

    private const string LogCategory = "playcheck";
    private const double FrameLimitMilliseconds = 50.0;
    private const double MaxUiStepMilliseconds = 12.0; // no retail UI step after Enter is longer: the shell is built over frames (GamePlayScreen.BuildBudgetMilliseconds)
    private const double EnterWindowNearMilliseconds = 30.0;
    private const int ListSettleFrames = 3; // as the login check: the frames in which the character list itself is built are not the entering's

    private readonly List<(string Name, bool Ok)> _results = [];
    private Main _main = null!;
    private GameServices _services = null!;
    private SessionHost _host = null!;
    private PlayMode _mode = null!;
    private PortalSpaceHandles? _portal;
    private RetailUiLayer? _ui;
    private WorldRoot _world = null!;
    private InteriorController? _interiors;
    private FlyCamera _camera = null!;
    private bool _sawTunnel;
    private readonly ShellWatch _shellWatch = new(); // g7: the login check's shell watch, proved here on the stand-in session
    private string? _framesDirectory; // --play-frames: where the shell's review frames go (null = none are taken, and the run is a timing run)
    private Vector2I _framesSize;
    private int _framesFailures;
    private int _tunnelTallySkip; // frames after a read-back whose tunnel tallies are not counted (the read-back stalls the frame; a frames run is not a timing run)
    private readonly HashSet<string> _framesTaken = [];
    private int _shellFrames;
    private int _shellHeldFrames;
    private int _bareWorldFrames;
    private int _tunnelFramesInViewport;
    private bool _spewSawPortalNotice;
    private int _tunnelFramesOutsideViewport;
    private int _tunnelHandOverFrames;
    private bool _shellRootShown;
    private string? _firstOutside;
    private bool _savedTunnelFrame;
    private bool _sawTunnelAfterTeleport;
    /// <summary>The longest the logoff tunnel may be down between its first showing and the character list's screen (the frame between the machine's reset and the screen change), in seconds.</summary>
    private const double LogOffTunnelGapToleranceSeconds = 0.25;

    private bool _tunnelWasVisible;
    private bool _arrivalSequenceStarted;
    private bool _arrivalSequenceDone;
    private double _arrivalSequenceSeconds;
    private double _arrivalTunnelSeconds;
    private int _tunnelCoveredFrames;
    private bool _watchLogOffTunnel;
    private bool _logOffTunnelSeen;
    private double _logOffTunnelSeconds;
    private double _logOffTunnelGapSeconds;
    private int _tunnelShowings;
    private int _tunnelFramesIndicatorOff; // g5b: frames the tunnel showed with the portal cue holding the selection indicator off
    private int _tunnelFramesIndicatorDrawn; // g5b: frames the tunnel showed with the indicator drawn anyway (must stay 0)
    private int _tunnelFramesSelected; // g5b: frames the tunnel showed with an object selected
    private bool _afterTeleport;
    private bool _afterArrival;
    private double _maxFrameMs;
    private string _maxFrameStage = "none";
    private double _maxFrameSecondsSinceArrival;
    private long _frameNumber;
    private long _listFrame = -1; // the frame the retail character list came up in
    private double _fromListMaxMs; // the longest frame from three frames after the list came up to the arrival (the live login check's window starts there)
    private int _fromListFrames;
    private bool _warmUpRanAtEnter; // the shell's warm-up had not finished when Enter came (the live gate's shape)
    private double _enterWindowMaxMs; // the longest frame from Enter to the arrival (the PlayerDescription's frame is in it)
    private int _enterWindowFrames;
    private double _uiStepMaxMs; // the longest retail UI step (the whole layer step) from Enter to the server's first teleport
    private bool _uiStepWindowOpen;
    private int _shellBuildFrames; // the staged shell's own figures (GamePlayScreen.BuildFrames and the longest frame and unit), from the first shell that was built
    private double _shellBuildLongestFrameMs;
    private double _shellBuildLongestUnitMs;
    private bool _shellStaged;
    private ulong _arrivedAtMs;
    private long _slowFrames;
    private double _teleportMaxFrameMs;
    private long _teleportSlowFrames;
    private readonly PipelineCompilationWatch _pipelines = new();
    private readonly List<FadeWatch> _fadeWatches = [new("player", 0, true), new("object", ApparitionGuid, false)]; // D63: what the materialise fade does on screen, frame by frame
    private bool _fadeFrameTaken;
    private bool _effectWindowOpen; // M3 1.3 s0: the frames from the one the server's F755 was applied in to ten frames after the wait ended (reported, not judged)
    private double _effectWindowMaxMs;
    private int _effectWindowFrames;
    private ulong _canvasPipelinesFromEnter; // canvas pipelines compiled from Enter on (the arrival's frames must compile none: docs/session.md section 27)

    /// <summary>Adds the checks to the scene; they start when the scene is ready.</summary>
    public static void Install(Main main, GameServices services, SessionHost host, PlayMode mode, PortalSpaceHandles? portal, RetailUiLayer? ui, WorldRoot world, InteriorController? interiors, FlyCamera camera, string? framesDirectory = null)
    {
        var checks = new PlayChecks
        {
            Name = "PlayChecks",
            _main = main,
            _services = services,
            _host = host,
            _mode = mode,
            _portal = portal,
            _ui = ui,
            _world = world,
            _interiors = interiors,
            _camera = camera,
            _framesDirectory = framesDirectory,
        };
        main.AddChild(checks);
    }

    /// <inheritdoc/>
    public override void _Ready() => _ = RunAsync();

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        // The shell is watched every frame too: from the frame it exists, no frame may show the world without it painted, and the tunnel must be
        // drawn in the SmartBox viewport (the retail UI's hole), not by the tunnel layer's own picture.
        var shellScreen = _ui?.Screens?.Flow.CurrentScreen as GamePlayScreen;
        _shellWatch.Step(_ui);
        var compiled = _pipelines.Step();
        if (compiled.Foreground > 0)
        {
            _canvasPipelinesFromEnter += _mode.Flow.Stage >= PlayStage.Entering ? compiled.Canvas : 0;
            Log.Info(LogCategory, "pipeline compilation", ("after_enter", _mode.Flow.Stage >= PlayStage.Entering), ("after_arrival", _afterArrival), ("stage", _mode.Flow.Stage), ("tunnel", _portal?.Director.Machine.TunnelVisible), ("shell_frames", _shellFrames), ("frame_ms", delta * 1000.0), ("pipelines_canvas", compiled.Canvas), ("pipelines_mesh", compiled.Mesh), ("pipelines_surface", compiled.Surface), ("pipelines_draw", compiled.Draw));
        }

        if (shellScreen is not null)
        {
            _shellFrames++;
            if (!_shellStaged && shellScreen is { IsBuilt: true, BuildFrames: > 0 })
            {
                _shellStaged = true;
                _shellBuildFrames = shellScreen.BuildFrames;
                _shellBuildLongestFrameMs = shellScreen.LongestBuildFrameMilliseconds;
                _shellBuildLongestUnitMs = shellScreen.LongestBuildUnitMilliseconds;
            }

            _shellRootShown |= shellScreen.Root is { IsShown: true };
            if (!shellScreen.ShellPainted)
            {
                _shellHeldFrames++;
                if (_ui is { WantsWorldHidden: false })
                {
                    _bareWorldFrames++;
                }
            }

            if (_tunnelTallySkip > 0)
            {
                _tunnelTallySkip--;
            }
            else if (shellScreen.ShellPainted && _portal is { } tunnelPortal && tunnelPortal.View.Shown) // before it is painted the tunnel shows on its own layer (the viewport does not exist yet) and the world is held hidden
            {
                var picture = _ui?.Viewports.PictureOf(PortalTunnelViewportSource.ViewportId);
                if (shellScreen.SelectionUi is { } selectionUi)
                {
                    _tunnelFramesIndicatorOff += tunnelPortal.Presenter.SelectionIndicatorEnabled ? 0 : 1;
                    _tunnelFramesIndicatorDrawn += selectionUi.Indicator.State != Lifestone.Client.RetailUi.Selection.VividTargetState.Hidden ? 1 : 0;
                    _tunnelFramesSelected += selectionUi.Selected != 0 ? 1 : 0;
                }

                if (picture is { Visible: true } && tunnelPortal.View.DrawnByViewport)
                {
                    _tunnelFramesInViewport++;
                    _spewSawPortalNotice |= shellScreen.Chat?.Spew?.Contains(PortalSpaceMachine.PortalNotice) == true;
                    if (!_savedTunnelFrame && _tunnelFramesInViewport == 2)
                    {
                        _savedTunnelFrame = true;
                        SaveReviewFrame("01-shell-portal-space");
                    }
                }
                else if (_shellFrames <= 2 && picture is null)
                {
                    // The hand-over frame: the shell painted in its first frame (the hidden panel host made it cheap) before the viewport's picture exists; the tunnel layer's own picture still draws the tunnel, so
                    // nothing is missing on screen. Not counted as outside (the viewport takes over the next frame, and the check needs frames in the viewport).
                    _tunnelHandOverFrames++;
                }
                else
                {
                    _tunnelFramesOutsideViewport++;
                    _firstOutside ??= $"picture={(picture is null ? "none" : $"visible {picture.Visible}")} drawn_by_viewport={tunnelPortal.View.DrawnByViewport} shell_frame={_shellFrames} painted={shellScreen.ShellPainted}";
                }
            }
        }

        // The tunnel is watched every frame, so a short one is not missed between two polls.
        if (_portal is { } portal)
        {
            var visible = portal.Director.Machine.TunnelVisible;
            if (!_arrivalSequenceDone)
            {
                // The arrival's sequence (DoD 5): from the first state after Off to the machine being Off again; the tunnel part of it, and the frames the tunnel showed behind the shell's black backdrop.
                if (portal.Director.Machine.State != TeleportAnimState.Off)
                {
                    _arrivalSequenceStarted = true;
                    _arrivalSequenceSeconds += delta;
                    if (visible)
                    {
                        _arrivalTunnelSeconds += delta;
                        if (portal.View.Shown && _ui is { BackdropShown: true })
                        {
                            _tunnelCoveredFrames++;
                        }
                    }
                }
                else if (_arrivalSequenceStarted)
                {
                    _arrivalSequenceDone = true;
                }
            }

            if (visible)
            {
                _sawTunnel = true;
                _sawTunnelAfterTeleport |= _afterTeleport;
                if (!_tunnelWasVisible)
                {
                    _tunnelShowings++; // each time the tunnel comes up: an arrival, a teleport or a log off
                }
            }

            _tunnelWasVisible = visible;

            // D28 (the logoff fix): from the log off's key until the retail character list shows, the tunnel stays up; a short gap is the frame between the machine's reset and the screen change.
            if (_watchLogOffTunnel && _ui?.Screens?.Flow.CurrentMode != UiModes.CharacterManagement)
            {
                if (visible)
                {
                    _logOffTunnelSeen = true;
                    _logOffTunnelSeconds += delta;
                }
                else if (_logOffTunnelSeen)
                {
                    _logOffTunnelGapSeconds += delta;
                }
            }
        }

        _frameNumber++;
        if (_listFrame < 0 && _ui?.Screens?.Flow.CurrentMode == UiModes.CharacterManagement)
        {
            // The held visit begins now: the stand-in server answers Enter as fast as the live one (about 0.1 s), so the arrival comes while the shell is still being warmed.
            _listFrame = _frameNumber;
            _host.StandIn?.Release(Visit.EnterAfter);
        }

        if (!_afterArrival && _listFrame >= 0 && _frameNumber >= _listFrame + ListSettleFrames)
        {
            var listMs = delta * 1000.0;
            _fromListFrames++;
            _fromListMaxMs = Math.Max(_fromListMaxMs, listMs);
            if (_fromListFrames == 1)
            {
                _warmUpRanAtEnter = _ui is { ShellWarmUpRunning: true };
            }

            if (listMs > EnterWindowNearMilliseconds)
            {
                Log.Info(LogCategory, "a slow frame between the character list and the arrival", ("ms", listMs), ("stage", _mode.Flow.Stage), ("frame", _fromListFrames), ("warm_up_running", _ui is { ShellWarmUpRunning: true }), ("process_ms_previous_frame", Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0));
            }
        }

        if (!_afterTeleport && _ui is not null && _mode.Flow.Stage >= PlayStage.Entering && _mode.Flow.Stage < PlayStage.LoggingOff)
        {
            // Every UI step from Enter on, the shell's staged build included: none may be long, since the build is spread over frames (docs/session.md section 27).
            if (!_uiStepWindowOpen)
            {
                _uiStepWindowOpen = true;
                _ui.ResetWorstStep();
            }

            _uiStepMaxMs = Math.Max(_uiStepMaxMs, _ui.WorstStepMilliseconds);
        }

        if (!_afterArrival && _mode.Flow.Stage >= PlayStage.Entering && _mode.Flow.Stage < PlayStage.LoggingOff)
        {
            // The enter window: every frame from Enter to the player's arrival, which holds the PlayerDescription's handlers (the live gate's 80 ms frame).
            var enterMs = delta * 1000.0;
            _enterWindowFrames++;
            _enterWindowMaxMs = Math.Max(_enterWindowMaxMs, enterMs);
            if (enterMs > EnterWindowNearMilliseconds)
            {
                Log.Info(LogCategory, "a slow frame in the enter window", ("ms", enterMs), ("stage", _mode.Flow.Stage), ("frame", _enterWindowFrames), ("process_ms_previous_frame", Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0));
            }
        }

        SampleFades(delta);
        if (_afterArrival)
        {
            var ms = delta * 1000.0;
            if (_afterTeleport)
            {
                // The server teleport is G6 W11's (the world's re-centre, the dungeon's first cells): measured and reported, not judged here.
                _teleportMaxFrameMs = Math.Max(_teleportMaxFrameMs, ms);
                if (ms > FrameLimitMilliseconds)
                {
                    _teleportSlowFrames++;
                }
            }
            else
            {
                if (ms > _maxFrameMs)
                {
                    _maxFrameMs = ms;
                    _maxFrameStage = _mode.Flow.Stage.ToString();
                    _maxFrameSecondsSinceArrival = (Time.GetTicksMsec() - _arrivedAtMs) / 1000.0;
                }

                if (ms > EnterWindowNearMilliseconds && ms <= FrameLimitMilliseconds)
                {
                    // M3 1.2 s5, TODO(login-burst-frame): the attribution of a frame near the limit, reported (not judged) so the live probe's number can be compared with the offline one.
                    Log.Warn(LogCategory, "TODO(login-burst-frame): a frame over 30 ms in the judged window, near the 50 ms limit", ("ms", ms), ("stage", _mode.Flow.Stage), ("seconds_since_arrival", (Time.GetTicksMsec() - _arrivedAtMs) / 1000.0), ("tunnel", _portal?.Director.Machine.TunnelVisible), ("process_ms_previous_frame", Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0));
                }

                if (ms > FrameLimitMilliseconds)
                {
                    _slowFrames++;
                    Log.Warn(LogCategory, "a frame over 50 ms in the judged window", ("ms", ms), ("stage", _mode.Flow.Stage), ("seconds_since_arrival", (Time.GetTicksMsec() - _arrivedAtMs) / 1000.0), ("tunnel", _portal?.Director.Machine.TunnelVisible));
                }
            }
        }
    }

    /// <summary>
    /// Every frame: the player's and the stand-in apparition's draw, from the avatar set's frame, into their <see cref="FadeWatch"/>. With <c>--play-frames</c> the frame
    /// <c>02a-materialising</c> is saved once both are about half way (to a sub-folder of the frames folder: the shell's ten frames are counted by file).
    /// </summary>
    private void SampleFades(double delta)
    {
        if (_effectWindowOpen && _mode.Avatars?.Stats.ServerScriptsPlayed >= 1)
        {
            _effectWindowFrames++;
            _effectWindowMaxMs = Math.Max(_effectWindowMaxMs, delta * 1000.0);
        }

        if (_mode.Avatars is not { } avatars || _mode.Rig is null)
        {
            return;
        }

        var now = Time.GetTicksMsec() / 1000.0;
        foreach (var draw in avatars.Frame.Avatars)
        {
            foreach (var watch in _fadeWatches)
            {
                if (watch.Player ? draw.IsPlayer : draw.Guid == watch.Guid)
                {
                    // What was drawn through the blended twin (S4): the player's from its view (updated earlier this frame), the object's from the layer (it runs after this node, so its count is
                    // the previous frame's: TwinLagFrames allows for that).
                    watch.Step(delta, draw.Visible, draw.Translucency, now, watch.Player ? _mode.Rig?.View.TwinParts ?? 0 : _mode.Layer?.TwinParts ?? 0);
                }
            }
        }

        if (_framesDirectory is not null && !_fadeFrameTaken && _fadeWatches.All(w => w.Intermediate > 0 && !w.Done && w.Previous <= 0.6f))
        {
            _fadeFrameTaken = true;
            var path = $"{ShellFramesPlan.FolderFor(_framesDirectory, _framesSize.X, _framesSize.Y)}/extra/{ShellFramesPlan.FileName("02a-materialising")}";
            _tunnelTallySkip = 2;
            if (!ScreenshotWriter.Save(GetViewport(), path, LogCategory, ("frame", "02a-materialising"), ("player_translucency", _fadeWatches[0].Previous), ("object_translucency", _fadeWatches[1].Previous)))
            {
                FrameFailed("02a-materialising could not be saved");
            }
        }
    }

    /// <summary>One drawn thing's materialise fade as the frames show it: hidden, then translucency falling through intermediate values to 0, never opaque before that.</summary>
    private sealed class FadeWatch(string name, uint guid, bool player)
    {
        public string Name { get; } = name;

        public uint Guid { get; } = guid;

        public bool Player { get; } = player;

        public bool SawHidden;
        public bool Popped; // drawn opaque with no fade before it (what retail's re-hide flash looks like)
        public bool Rose; // the translucency went up during the fade
        public bool Done; // an opaque frame after intermediate ones
        public int Intermediate; // frames drawn with 0 < translucency < 1
        public int TwinFrames; // of those, the frames in which parts were drawn through the blended twin (S4: the translucency alone only says what was asked)
        public float Previous = 1f;
        public double StartedAt;
        public double Seconds; // from the first intermediate frame to the first opaque one
        public double WindowMaxMs; // the longest frame from the first intermediate frame to the first opaque one

        public void Step(double delta, bool visible, float translucency, double now, int twinParts)
        {
            if (!visible)
            {
                SawHidden = true;
                return;
            }

            if (!SawHidden || Done || translucency >= 1f)
            {
                return;
            }

            if (translucency > 0f)
            {
                if (Intermediate++ == 0)
                {
                    StartedAt = now;
                }

                if (twinParts > 0)
                {
                    TwinFrames++;
                }

                Rose |= translucency > Previous + 1e-6f;
                Previous = translucency;
                WindowMaxMs = Math.Max(WindowMaxMs, delta * 1000.0);
                return;
            }

            if (Intermediate == 0)
            {
                Popped = true;
                Done = true;
                return;
            }

            Done = true;
            Seconds = now - StartedAt;
            WindowMaxMs = Math.Max(WindowMaxMs, delta * 1000.0);
        }

        public string Describe() => $"{Name}: hidden={SawHidden} intermediate_frames={Intermediate} twin_frames={TwinFrames} done={Done} popped={Popped} rose={Rose} fade_s={Seconds:0.000} window_max_ms={WindowMaxMs:0.0}";
    }

    /// <summary>With <c>--play-frames</c>: one more review frame, in the frames folder's <c>extra</c> sub-folder (the shell's ten frames are counted by file, so an extra one never joins them).</summary>
    private void SaveExtraFrame(string name)
    {
        if (_framesDirectory is not { } directory)
        {
            return;
        }

        _tunnelTallySkip = 2;
        var path = $"{ShellFramesPlan.FolderFor(directory, _framesSize.X, _framesSize.Y)}/extra/{ShellFramesPlan.FileName(name)}";
        if (!ScreenshotWriter.Save(GetViewport(), path, LogCategory, ("frame", name), ("scale", _ui?.Scale)))
        {
            FrameFailed($"{name} could not be saved");
        }
    }

    /// <summary>A sounded counter for a log line: the number, or <c>n/a</c> when the router's sink cannot say how many voices it started.</summary>
    private static string SoundedText(SoundHookRouter? router, int? count) => router is { CountsSounded: true } && count is { } n ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : "n/a";

    /// <summary>
    /// Review frames of the shell for the owner (<see cref="ShellFramesPlan"/>): with <c>--play-frames[=DIR]</c> the checks save the window to DIR (default
    /// <see cref="PlayOptions.DefaultFramesDirectory"/> plus the window's <c>WxH</c>) when each state is reached, and check the picture is the size asked for and not blank.
    /// The read-back is slow, so a run that saves frames is not a timing run (the frame-time judgement is reported, not judged). Gitignored places only.
    /// </summary>
    private void SaveReviewFrame(string name)
    {
        if (_framesDirectory is not { } directory)
        {
            return;
        }

        if (SaveCheckedFrame($"{ShellFramesPlan.FolderFor(directory, _framesSize.X, _framesSize.Y)}/{ShellFramesPlan.FileName(name)}", name))
        {
            _framesTaken.Add(name);
        }
    }

    // Saves the window to path and checks it is the size asked for and not blank (a failure is counted and logged); shared by the shell's frames and the panel pages' (PlayChecks.Panels.cs).
    private bool SaveCheckedFrame(string path, string name)
    {
        _tunnelTallySkip = 2;
        if (!ScreenshotWriter.Save(GetViewport(), path, LogCategory, ("frame", name), ("scale", _ui?.Scale)))
        {
            FrameFailed($"{name} could not be saved");
            return false;
        }

        using var image = GetViewport().GetTexture().GetImage();
        var size = new Vector2I(image.GetWidth(), image.GetHeight());
        if (size != _framesSize)
        {
            FrameFailed($"{name} is {size.X}x{size.Y}, not the {_framesSize.X}x{_framesSize.Y} requested");
            return false;
        }

        var seen = new HashSet<uint>();
        for (var gy = 0; gy < 36; gy++)
        {
            for (var gx = 0; gx < 64; gx++)
            {
                seen.Add(image.GetPixel((gx * size.X + size.X / 2) / 64, (gy * size.Y + size.Y / 2) / 36).ToRgba32() >> 8);
            }
        }

        if (seen.Count < 8)
        {
            FrameFailed($"{name} is blank");
            return false;
        }

        return true;
    }

    private void FrameFailed(string message)
    {
        _framesFailures++;
        Log.Error(LogCategory, "shell frame failed: " + message);
    }

    /// <summary>
    /// Makes the window the size <c>--resolution</c> asked for whatever the desktop is (a decorated window is kept inside the work area: 2560x1440 on a 2560x1440 display opens
    /// 2560x1421, so the frames' size would depend on the machine; a borderless one is not limited), as the retail UI frames run does. False when it is not that size.
    /// </summary>
    private async Task<bool> FitWindowForFrames()
    {
        var project = new Vector2I((int)ProjectSettings.GetSetting("display/window/size/viewport_width"), (int)ProjectSettings.GetSetting("display/window/size/viewport_height"));
        var requested = ShellFramesPlan.RequestedWindowSize(System.Environment.GetCommandLineArgs(), project.X, project.Y) is { } size ? new Vector2I(size.Width, size.Height) : DisplayServer.WindowGetSize();
        if (DisplayServer.WindowGetSize() != requested)
        {
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
            DisplayServer.WindowSetSize(requested);
            await Frames(3);
        }

        _framesSize = requested;
        var window = DisplayServer.WindowGetSize();
        var visible = GetViewport().GetVisibleRect().Size;
        var fits = window == requested && new Vector2I((int)visible.X, (int)visible.Y) == requested && _ui?.WindowSize == requested;
        Log.Info(LogCategory, "shell frames start", ("dir", ShellFramesPlan.FolderFor(_framesDirectory!, requested.X, requested.Y)), ("size", $"{requested.X}x{requested.Y}"), ("scale", _ui?.Scale), ("window_is_that_size", fits));
        return fits;
    }

    private async Task RunAsync()
    {
        var exitCode = 1;
        try
        {
            await CheckAllAsync();
            exitCode = _results.Any(r => !r.Ok) ? 1 : 0;
            if (_framesDirectory is not null)
            {
                var missing = ShellFramesPlan.FrameNames.Where(n => !_framesTaken.Contains(n)).ToArray();
                Check("every-shell-review-frame-is-taken-the-size-asked-for-and-not-blank", missing.Length == 0 && _framesFailures == 0, $"taken={_framesTaken.Count}/{ShellFramesPlan.FrameNames.Count} missing=[{string.Join(", ", missing)}] failures={_framesFailures}");
                exitCode = _results.Any(r => !r.Ok) ? 1 : 0;
                Log.Info(LogCategory, ShellFramesPlan.Verdict(_framesSize.X, _framesSize.Y, _ui?.Scale ?? 0, _framesTaken.Count, _framesFailures + (missing.Length > 0 ? 1 : 0)));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Exception(LogCategory, "play checks crashed", ex);
        }

        Log.Info(
            LogCategory,
            exitCode == 0 ? "all play checks hold" : "PLAY CHECKS FAILED",
            ("pass", _results.Count(r => r.Ok)),
            ("fail", _results.Count(r => !r.Ok)),
            ("max_frame_ms_after_arrival", _maxFrameMs),
            ("max_frame_stage", _maxFrameStage),
            ("max_frame_seconds_since_arrival", _maxFrameSecondsSinceArrival),
            ("enter_window_max_frame_ms", _enterWindowMaxMs),
            ("list_to_walking_max_frame_ms", Math.Max(_fromListMaxMs, _maxFrameMs)),
            ("slow_frames_over_50_ms", _slowFrames),
            ("teleport_max_frame_ms", _teleportMaxFrameMs),
            ("teleport_slow_frames_over_50_ms", _teleportSlowFrames),
            ("canvas_pipelines_from_enter", _canvasPipelinesFromEnter));
        if (_teleportSlowFrames > 0)
        {
            Log.Warn(LogCategory, "frames over 50 ms around a server teleport on the stand-in server (the world's re-centre and the destination's first cells); reported here, judged live: --login-check=all judges every frame from a leg's server teleport to a second after the player is present (teleport_frame_max_ms, measured 7.8 to 12.7 ms, docs/session.md section 27), so g6-teleport-frame-time is closed", ("slow_frames", _teleportSlowFrames), ("max_ms", _teleportMaxFrameMs));
        }
        Log.FlushToGodot();
        GetTree().Quit(exitCode);
    }

    private async Task CheckAllAsync()
    {
        var session = _host.Session ?? throw new InvalidOperationException("the checks need the stand-in session");
        var ui = _ui;
        if (_framesDirectory is not null)
        {
            var fits = await FitWindowForFrames();
            Check("the-frames-run-window-is-the-size-asked-for", fits, $"window={DisplayServer.WindowGetSize()} asked={_framesSize}");
        }

        // --- before the server creates the player ---
        Check("world-waits-for-the-player", _world.FocusSource is null && _mode.Rig is null, $"focus={_world.FocusSource?.GetType().Name ?? "none"} rig={(_mode.Rig is null ? "none" : "built")}");
        Check(
            "m1-panels-and-keys-are-off",
            _camera.Locked && _interiors is { DebugKeys: false } && _main.GetNodeOrNull<Label>("Hud/PositionHud") is { Visible: false } && _main.GetNodeOrNull("SkyUi/TeleportPanel") is null && _main.GetNodeOrNull("SkyUi/TimePanel") is null && _main.GetNodeOrNull("AboutUi") is not null,
            $"locked={_camera.Locked} debug_keys={_interiors?.DebugKeys} teleport_panel={_main.GetNodeOrNull("SkyUi/TeleportPanel") is not null} about={_main.GetNodeOrNull("AboutUi") is not null}");
        Check("retail-ui-runs-on-the-session", ui is not null && ReferenceEquals(ui.Session, session) && ui.Flow is not null, $"ui={ui is not null}");
        Check("the-session-is-the-stand-in-not-a-server", session.State.IsOffline, $"offline={session.State.IsOffline}");

        // --- the arrival ---
        var arrived = await WaitFor(() => _mode.Rig is not null, 60.0);
        Check("the-player-is-created-by-the-server", arrived, $"stage={_mode.Flow.Stage}");
        if (!arrived)
        {
            return;
        }

        _afterArrival = true;
        var rig = _mode.Rig!;

        // g4 live-gate fix 2: the stand-in's PlayerDescription has the live gate's shape (138 enchantments, layered duplicates). The state keeps them and the vitals' maximums apply the strongest of
        // each spell category (docs/retail-ui.md section 21.3): Endurance 120 + 20 and Self 100 are the attributes; health (140 + 1) / 2 + 5 ranks, stamina 140 + 20, mana 100.
        var qualities = session.State.Self.Qualities;
        var vitalsKnown = qualities.TryGetVitalLevels(1, out var healthNow, out var healthMax) & qualities.TryGetVitalLevels(3, out var staminaNow, out var staminaMax) & qualities.TryGetVitalLevels(5, out var manaNow, out var manaMax);
        Check(
            "the-description-s-enchantments-are-kept-and-applied-to-the-vitals-maximums",
            vitalsKnown && qualities.Enchantments.Length == Expected.Enchantments && (healthNow, staminaNow, manaNow) == (Expected.Health, Expected.Stamina, Expected.Mana) && (healthMax, staminaMax, manaMax) == (Expected.HealthMax, Expected.StaminaMax, Expected.ManaMax),
            $"enchantments={qualities.Enchantments.Length} current={healthNow}/{staminaNow}/{manaNow} maximum={healthMax}/{staminaMax}/{manaMax} (expected {Expected.Enchantments}, {Expected.Health}/{Expected.Stamina}/{Expected.Mana}, {Expected.HealthMax}/{Expected.StaminaMax}/{Expected.ManaMax}; {Expected.Source})");
        // M3 1.2 s5: the live-shaped rehearsal: the registry the read model holds is the count of the description the stand-in sent (the recording's when one exists, else the synthetic 138), read from
        // PlayerQualities.Enchantments and ActiveEnchantments (the registry the vitals and the enchantment events read).
        Check(
            "the-registry-holds-the-count-of-the-description-the-stand-in-sent",
            qualities.Enchantments.Length == Expected.Enchantments && qualities.ActiveEnchantments.Length == Expected.Enchantments && Expected.Enchantments > 0,
            $"registry={qualities.Enchantments.Length} active={qualities.ActiveEnchantments.Length} expected={Expected.Enchantments} source='{Expected.Source}' contained_objects={Visit.Recorded?.Items.Length ?? 0}");
        var arrivedAt = Time.GetTicksMsec();
        _arrivedAtMs = arrivedAt;
        Check(
            "the-player-is-built-from-its-own-create-object",
            rig.Controller.Avatar.Guid == StandInSessions.FirstCharacterGuid && rig.Controller.Walker.Physics.Id == StandInSessions.FirstCharacterGuid && rig.Controller.Avatar.Parts is not null,
            $"guid=0x{rig.Controller.Avatar.Guid:X8}");
        Check(
            "movement-goes-to-the-session-not-a-dry-run",
            rig.Controller.Walker.Sink is PresenceGatedMovementSink { Inner: var channel } && ReferenceEquals(channel, session.Actions.Movement) && !channel.GetType().Name.Contains("DryRun", StringComparison.Ordinal),
            $"sink={rig.Controller.Walker.Sink.GetType().Name}");
        var started = await WaitFor(() => _world.FocusSource is not null && _portal?.Attachments.CollisionSource is not null, 5.0); // the world starts in the frame after the player's build
        Check("the-world-starts-around-the-player", started, $"focus={_world.FocusSource?.GetType().Name ?? "none"}");

        // --- the server's objects (G5 W6, W10): the adapter feeds the one avatar set the player lives in, and the layer draws what is fully attached ---
        var known = await WaitFor(() => _mode.Avatars is { } set && Visit.Objects.All(o => set.TryGet(o.Guid, out _)), 20.0);
        Check(
            "server-objects-reach-the-avatar-set",
            known && _mode.Avatars is { } avatars && avatars.Player is { } own && ReferenceEquals(own, rig.Controller.Avatar) && _mode.Adapter is { Stats.Failed: 0 },
            $"avatars={_mode.Avatars?.Count} applied={_mode.Adapter?.Stats.Applied} failed={_mode.Adapter?.Stats.Failed} pending={_mode.Adapter?.Pending} player_in_the_shared_set={_mode.Avatars?.Player is { } p && ReferenceEquals(p, rig.Controller.Avatar)}");
        // --retail-unhide-flash reaches the live set (a flag restores retail): the set the mode built reads back what the command line asked for.
        var unhideFlashWanted = _services.Options.Play.RetailUnhideFlash;
        Check("retail-unhide-flash-reaches-the-live-avatar-set", _mode.Avatars is { } flashSet && flashSet.RetailUnhideFlash == unhideFlashWanted, $"wanted={unhideFlashWanted} set={_mode.Avatars?.RetailUnhideFlash}");
        var drawn = await WaitFor(() => _mode.Layer is { } layer && Visit.Objects.All(o => layer.IsDrawn(o.Guid)), 60.0);
        var layerNow = _mode.Layer;
        Check(
            "server-objects-are-drawn-when-fully-attached",
            drawn && layerNow is { Active: true, BatchCount: > 0 },
            $"drawn={layerNow?.DrawnObjects} waiting={layerNow?.Waiting} max_waiting={layerNow?.MaxWaiting} batches={layerNow?.BatchCount} uploads={layerNow?.Uploads} meshes_built={(layerNow?.Meshes is { } m ? m.Outdoor.Built + m.Indoor.Built : 0)}");

        var gameScreen = await WaitFor(() => ui?.Flow?.Flow.CurrentMode == UiModes.GamePlay, 10.0);
        var shell = ui?.Screens?.Flow.CurrentScreen as GamePlayScreen;
        await WaitFor(() => shell is { ShellPainted: true }, 10.0);
        Check("the-ui-shows-the-game-screen-and-the-world", gameScreen && ui is { WantsWorldHidden: false } && shell is not null, $"mode={(ui?.Flow is { } flow ? UiModes.NameOf(flow.Flow.CurrentMode) : "none")} screen={ui?.Screens?.Flow.CurrentScreen?.GetType().Name ?? "none"}");
        Check(
            "the-game-screen-is-the-in-game-shell-shown-over-the-world",
            shell is { Root.IsShown: true, SmartBox: not null, Radar: not null, StackedVitals: not null, MainChat: not null, Toolbar: not null } && _shellRootShown && ui is { ShellHoldsWorld: false } && ui.GameViewRect == new Godot.Rect2I(Godot.Vector2I.Zero, ui.WindowSize),
            $"screen={shell?.GetType().Name ?? "none"} root_shown={_shellRootShown} frames={_shellFrames} game_view={ui?.GameViewRect}");
        Check(
            "the-shell-was-composited-before-the-world-showed",
            shell is { ShellPainted: true, PaintTimedOut: false } && _bareWorldFrames == 0 && _shellHeldFrames >= 0,
            $"held_frames={_shellHeldFrames} bare_world_frames={_bareWorldFrames} painted_after_frames={shell?.PaintFrames} ms={shell?.PaintMilliseconds:0} timed_out={shell?.PaintTimedOut}");

        var tunnel = await WaitFor(() => _sawTunnel, 5.0);
        Check("portal-space-plays-on-arrival", tunnel && _portal is not null, $"tunnel_seen={_sawTunnel}");
        await WaitFor(() => _arrivalSequenceDone, 15.0);
        Check(
            "the-arrival-tunnel-shows-from-its-first-frame-and-the-sequence-lasts-5-seconds",
            _arrivalSequenceDone && _arrivalSequenceSeconds >= 5.0 && _arrivalTunnelSeconds >= 4.0 && _tunnelCoveredFrames <= 1,
            $"portal_seconds={_arrivalSequenceSeconds:0.00} tunnel_seconds={_arrivalTunnelSeconds:0.00} tunnel_covered_frames={_tunnelCoveredFrames} done={_arrivalSequenceDone} (DoD 5: 1 s expand, 2 s hold, 1 s collapse, 1 s world; the shell's backdrop gives way to the tunnel while the shell is built)");
        await WaitFor(() => _tunnelFramesInViewport > 0, 3.0); // the shell can take a few hundred ms to paint when Enter came before its warm-up was done; the tunnel shows behind it from then on
        Check(
            "portal-space-draws-in-the-smartbox-viewport",
            _tunnelFramesInViewport > 0 && _tunnelFramesOutsideViewport == 0,
            $"tunnel_frames_in_viewport={_tunnelFramesInViewport} hand_over={_tunnelHandOverFrames} outside={_tunnelFramesOutsideViewport} first_outside={_firstOutside ?? "none"}");

        // g2: the spew box showed the portal-space notice while the tunnel played, and no chat window did (type 26 is the spew box's only).
        var spewBox = shell?.Chat?.Spew;
        await WaitFor(() => _spewSawPortalNotice, 8.0); // the director posts at the start of the tunnel (before the shell exists) and again at each roll segment
        Check(
            "the-spew-box-shows-the-portal-space-notice",
            _spewSawPortalNotice && spewBox is { Accepted: >= 1 } && shell!.Chat!.Windows.All(w => !w.LogText.Contains("Portal Space", StringComparison.Ordinal)),
            $"seen_in_spew={_spewSawPortalNotice} accepted={spewBox?.Accepted} rows=[{string.Join(" | ", spewBox?.Lines ?? [])}] max_rows={spewBox?.MaxRows}");

        // g3a: the vitals from the stand-in's description (health 52 of 75, stamina 120 of 160, mana 75 of 100: the maximums with the description's enchantments applied), the window its options choose, and no update yet.
        var vitals = shell?.Vitals;
        var stacked = vitals?.Stacked;
        Check(
            "the-vitals-show-the-stand-ins-values-in-the-window-its-options-choose",
            vitals is { Chosen: not null } && ReferenceEquals(vitals.Chosen, stacked) && stacked is { Window.IsShown: true } && vitals.SideBySide is { Window.IsShown: false }
                && stacked.HealthMeter is { } hm && Math.Abs(hm.Position - ((float)Expected.Health / Expected.HealthMax)) < 0.001f && stacked.StaminaMeter is { } sm && Math.Abs(sm.Position - ((float)Expected.Stamina / Expected.StaminaMax)) < 0.001f && stacked.ManaMeter is { } mm && Math.Abs(mm.Position - ((float)Expected.Mana / Expected.ManaMax)) < 0.001f
                && stacked.HealthLabel!.Text.Contains(Expected.Health.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal) && stacked.HealthLabel.Text.Contains(Expected.HealthMax.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal) && stacked.ManaLabel!.Text.Contains(Expected.Mana.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal),
            $"chosen={(ReferenceEquals(vitals?.Chosen, stacked) ? "stacked" : "side-by-side")} health={stacked?.HealthMeter?.Position:0.000} '{stacked?.HealthLabel?.Text}' stamina={stacked?.StaminaMeter?.Position:0.000} '{stacked?.StaminaLabel?.Text}' mana={stacked?.ManaMeter?.Position:0.000} '{stacked?.ManaLabel?.Text}' options=0x{session.State.Self.Qualities.CharacterOptions1:X8}");

        // R15: the stand-in session's placeholder values are labelled on screen while the shell is up (the status line is hidden then).
        Check(
            "a-stand-in-note-labels-the-placeholder-values-in-the-shell",
            _mode.Status.Visible && _mode.Status.Text == PlayStatusLine.StandInNote,
            $"visible={_mode.Status.Visible} text='{_mode.Status.Text.Replace('\n', '|')}'");

        // g3b: the radar shows the stand-in portal 3 m west of the player (the player faces north, the visit is outdoors: 75 m, two pixels a metre and a half): a purple cross left of the centre.
        var radar = shell?.RadarUi;
        await WaitFor(() => radar is { } r && r.Blips.Any(b => b.Guid == PortalGuid), 5.0);
        var portalBlip = radar?.Blips.FirstOrDefault(b => b.Guid == PortalGuid);
        var guardBlip = radar?.Blips.FirstOrDefault(b => b.Guid == 0x80000A01);
        Check(
            "the-radar-shows-the-stand-in-portal-as-a-purple-cross-left-of-the-centre",
            radar is { Range: 75.0, Radius: 50 } && portalBlip is { Shape: Lifestone.Client.RetailUi.Radar.RadarBlipShape.Cross, Argb: 0xFFBF63FF, Y: 60 } && portalBlip.Value.X is >= 57 and <= 58
                && guardBlip is { Argb: 0xFFFFAA00, Y: 60 } && guardBlip.Value.X is >= 61 and <= 62 && radar.Blips.Count == Visit.Objects.Length && shell is { Placeholders: var left } && !left.Contains("radar"),
            $"range={radar?.Range} blips={radar?.Blips.Count}/{Visit.Objects.Length} portal=({portalBlip?.X}, {portalBlip?.Y}) 0x{portalBlip?.Argb:X8} {portalBlip?.Shape} guard=({guardBlip?.X}, {guardBlip?.Y}) 0x{guardBlip?.Argb:X8} updates={radar?.Updates}");

        // g7: the fields the login check adds to its verdict, read here from the same watch and the same description: the shell up with no bare-world frame, the vitals filled (the stand-in's 52, 120, 75),
        // the spew box having heard portal space, the stacked window shown (the stand-in's options choose it) and the radar's blips counted.
        var shellFields = LoginCheckShell.Fields(_shellWatch.ShellUp, _shellWatch.VitalsOnScreen, _shellWatch.SpewPortal, _shellWatch.ShownWindow);
        Check(
            "the-login-check-s-shell-fields-read-ok-on-the-stand-in",
            shellFields == $" shell=ok vitals={Expected.Health}/{Expected.Stamina}/{Expected.Mana} spew_portal=yes vitals_window=stacked" && LoginCheckShell.VitalsWindow(session.State.Self.Qualities) == "stacked" && _shellWatch.RadarBlipsMax >= Visit.Objects.Length,
            $"fields='{shellFields.TrimStart()}' shell_frames={_shellWatch.Frames} bare_world_frames={_shellWatch.BareWorldFrames} radar_blips_max={_shellWatch.RadarBlipsMax} session_vitals={LoginCheckShell.Vitals(session.State.Self.Qualities)}");

        var placed = await WaitFor(() => !rig.Controller.Placing, 60.0);
        var where = rig.Controller.Position;
        var spawn = Visit.Origin;
        var horizontal = Math.Sqrt(Math.Pow(where.Origin.X - spawn.X, 2) + Math.Pow(where.Origin.Y - spawn.Y, 2));
        Check(
            "physics-places-the-player-on-the-ground",
            placed && rig.Controller.Walker.OnGround && horizontal < 3.0 && AcCoordinatesIsOutdoor(where.CellId),
            $"placed={placed} on_ground={rig.Controller.Walker.OnGround} cell=0x{where.CellId:X8} horizontal_m={horizontal:0.00} seconds={(Time.GetTicksMsec() - arrivedAt) / 1000.0:0.0}");
        var streamed = await WaitFor(() => _world.Streamer.Planner.LoadedCount > 0, 60.0);
        Check("the-world-streams-around-the-player", streamed, $"loaded={_world.Streamer.Planner.LoadedCount}");

        // --- LoginComplete once, then the server unhides the player ---
        var present = await WaitFor(() => _mode.Flow.Stage == Lifestone.Client.Play.PlayStage.InWorld, 90.0);
        Check("the-server-unhides-the-player", present, $"stage={_mode.Flow.Stage}");
        var materialised = await WaitFor(() => rig.View.DrawnParts > 0, 10.0);
        var adapterStats = _mode.Adapter?.Stats;
        Check(
            "the-hidden-player-materialises",
            materialised && adapterStats is { PlayerCreatedHidden: 1, PlayerUnhides: 1 } && _mode.Layer?.Particles is { MaxObjects: > 0 },
            $"drawn_parts={rig.View.DrawnParts} created_hidden={adapterStats?.PlayerCreatedHidden} unhides={adapterStats?.PlayerUnhides} particle_objects_max={_mode.Layer?.Particles?.MaxObjects}");
        // --- D63: the materialise fade, on screen: the player and an object the server unhid pass through intermediate alpha and end opaque ---
        var faded = await WaitFor(() => _fadeWatches.All(w => w.Done), 10.0);
        // S4: the blended twin is really drawn on the fading frames (above 0, and on all of them but a pipeline lag), and no fading part was drawn solid for want of a twin.
        var twinMissing = (_mode.Rig?.View.TwinMissing ?? false) || (_mode.Layer?.TwinMissing ?? false);
        var twinDrawn = _fadeWatches.All(w => w.TwinFrames > 0 && w.TwinFrames >= w.Intermediate - TwinLagFrames);
        var fadeJudged = _fadeWatches.All(w => w.SawHidden && w.Intermediate >= 5 && !w.Popped && !w.Rose && Math.Abs(w.Seconds - FadeSeconds) <= FadeToleranceSeconds + 1.0 / 30.0)
            && _fadeWatches.All(w => w.WindowMaxMs <= FrameLimitMilliseconds || _framesDirectory is not null)
            && twinDrawn && !twinMissing;
        Check(
            "an-unhidden-object-and-the-player-pass-through-intermediate-alpha-and-end-opaque-with-no-frame-over-50-ms",
            faded && fadeJudged,
            $"fade_ms={_fadeWatches[0].Seconds * 1000.0:0}/{_fadeWatches[1].Seconds * 1000.0:0} (player/object, want {FadeSeconds * 1000.0:0} within {FadeToleranceSeconds * 1000.0:0} ms plus a frame) twin_drawn={twinDrawn} twin_missing={twinMissing} | {string.Join(" | ", _fadeWatches.Select(w => w.Describe()))}{(_framesDirectory is not null ? " (frame time REPORTED, NOT JUDGED: a --play-frames run is not a timing run)" : string.Empty)}");
        // --- M3 1.3 s0: the server's F755 PlayEffect and F750 Sound on the player reach its body (read from the avatar set's counters: what was applied, not what was sent) ---
        _effectWindowOpen = true;
        var effectsApplied = await WaitFor(() => _mode.Avatars?.Stats is { ServerScriptsPlayed: >= 1, ServerSoundsPlayed: >= 1 }, 15.0);
        await WaitFor(() => _mode.Layer?.Particles is { PlayerObjectsLastFrame: 1 }, 5.0); // the layer hands the player's emitters to the particle renderer (the mesh of a particle is queued, then attached)
        await WaitFor(() => false, 0.7); // real time, not frames (a frame is a few ms here): the particles' meshes are prepared on workers and attached within a budget, then the emitters' particles (3 s of life) are drawn
        _effectWindowOpen = false;
        var effectStats = _mode.Avatars?.Stats;
        var scriptParticles = 0;
        var scriptEmitters = 0;
        if (_mode.Avatars?.Player?.Body.Effects.Particles.Emitters is { } emitters)
        {
            foreach (var emitter in emitters)
            {
                if (emitter.Info.HwGfxObjId is LevelUpGfxA or LevelUpGfxB or LevelUpGfxC)
                {
                    scriptEmitters++;
                    scriptParticles += emitter.NumParticles;
                }
            }
        }

        Check(
            "the-servers-script-and-sound-are-applied-to-the-players-body",
            effectsApplied && effectStats is { ServerScriptsPlayed: >= 1, ServerSoundsPlayed: >= 1, ServerScriptsMissing: 0, ServerEffectsDropped: 0 } && scriptParticles > 0,
            $"server_scripts={effectStats?.ServerScriptsPlayed} server_sounds={effectStats?.ServerSoundsPlayed} scripts_missing={effectStats?.ServerScriptsMissing} effects_dropped={effectStats?.ServerEffectsDropped} sounds_delivered={effectStats?.EffectEventsDelivered} sounds_dropped_no_sink={effectStats?.EffectEventsDropped} levelup_emitters={scriptEmitters} levelup_particles={scriptParticles} player_particle_objects={_mode.Layer?.Particles?.PlayerObjectsLastFrame} particle_batches={_mode.Layer?.Particles?.BatchesLastFrame} effect_window_max_ms={_effectWindowMaxMs:0.0} effect_window_frames={_effectWindowFrames} (report-only fields: no judge in smoke)");
        SaveExtraFrame("02b-server-script");
        Check("login-complete-is-sent-once-before-the-unhide", _portal is { Director.LoginCompletesSent: 1 }, $"sent={_portal?.Director.LoginCompletesSent} suppressed={_portal?.Director.LoginCompletesSuppressed}");
        var back = await WaitFor(() => _portal is { Director.Machine: { TunnelVisible: false, WorldVisible: true } }, 15.0);
        Check("portal-space-ends-and-the-world-is-back", back, $"tunnel={_portal?.Director.Machine.TunnelVisible} world={_portal?.Director.Machine.WorldVisible}");
        await Frames(120);
        SaveReviewFrame("02-shell-arrival");
        Check("the-player-keys-are-on-the-ui-router", rig.Controller.Input is not null, $"input={(rig.Controller.Input is null ? "none" : "set")}");
        // --- M3 1.3 s1, fix B (R2): the player's walk fires its footstep hooks and they are handed to the sound player, and the server's sound reached it too. Judged: what the names say, and no more.
        // The player's own footsteps are counted apart from every object's (the avatars tell the router which body fired). "Handed" is a call to the sound player; "sounded" is a voice it started: the
        // human sound table 0x20000001 has no variant for the footstep types 55 and 56 and retail uses the object's own table only (no fallback), so the player's footsteps are handed over and
        // never sound, which is printed apart and not judged here. Read from the router's counters, independent of the volume (an automated run is silent unless --volume, M2 D8). ---
        var hookSounds = _mode.HookSounds;
        var stepsBefore = hookSounds?.PlayerFootsteps ?? 0;
        rig.Controller.Controller.HandleCommand(Lifestone.Data.Animation.MotionCommands.WalkForward, true);
        var stepped = hookSounds is not null && await WaitFor(() => hookSounds.PlayerFootsteps > stepsBefore, 6.0);
        await WaitFor(() => false, 0.5);
        rig.Controller.Controller.HandleCommand(Lifestone.Data.Animation.MotionCommands.WalkForward, false);
        await Frames(10);
        Check(
            "the-players-walk-hands-its-footsteps-to-the-sound-player-and-the-servers-sound-reaches-it",
            stepped && hookSounds is { PlayerFootsteps: >= 1, ServerSounds: >= 1 },
            $"player_footsteps_handed={hookSounds?.PlayerFootsteps - stepsBefore} (all objects' footsteps handed={hookSounds?.Footsteps}) server_sounds_handed={hookSounds?.ServerSounds} " +
            $"sounded (voices started; report-only, a footstep with the human table is silent as retail): player_footsteps_sounded={SoundedText(hookSounds, hookSounds?.PlayerFootstepsSounded)} hooks_sounded={SoundedText(hookSounds, hookSounds?.HooksSounded)} server_sounded={SoundedText(hookSounds, hookSounds?.ServerSounded)} " +
            $"hooks_handed={hookSounds?.HooksPlayed} hook_outcomes=[{hookSounds?.Diagnostics.Summary()}] no_table={hookSounds?.NoTable} ignored={hookSounds?.Ignored} events_dropped_no_sink={_mode.Avatars?.Stats.EffectEventsDropped} (report-only fields: no judge in smoke)");

        // --- g2: a stand-in speech line in the main chat, and a typed line sent as Talk and answered ---
        var chat = (ui?.Screens?.Flow.CurrentScreen as GamePlayScreen)?.Chat;
        var heard = await WaitFor(() => chat?.Main?.LogText.Contains("Hear ye", StringComparison.Ordinal) == true, 30.0);
        Check(
            "a-stand-in-speech-line-reaches-the-main-chat",
            heard && chat!.Main!.LogText.Contains("Stand-in Town Crier says,", StringComparison.Ordinal) && chat.Spew!.Lines.All(l => !l.Contains("Hear ye", StringComparison.Ordinal)),
            $"main_chat=[{chat?.Main?.LogText}]");
        if (chat?.Main is { } mainChat)
        {
            mainChat.TypeIntoEntry("Greetings, Holtburg.");
            mainChat.ProcessCommand();
            var talk = chat.LastTalk;
            var talked = talk is not null && await WaitFor(() => talk.IsCompleted, 10.0);
            await WaitFor(() => mainChat.LogText.Contains("You say, \"Greetings, Holtburg.\"", StringComparison.Ordinal), 5.0);
            Check(
                "a-typed-line-is-sent-as-talk-and-answered",
                talked && talk!.Kind == ActionKind.Talk && talk.Outcome?.Kind == ActionOutcomeKind.Confirmed && mainChat.Entry!.Text.Length == 0 && mainChat.LogText.Contains("You say, \"Greetings, Holtburg.\"", StringComparison.Ordinal),
                $"talk={talk?.Outcome?.Kind} entry_empty={mainChat.Entry!.Text.Length == 0} history={mainChat.History.Count}");
            // For the review frame only: fill the spew box over the world with two retail notices, alternating (only the top row is compared, so none replaces another); the lower rows
            // show below the stacked vitals window, which covers the first rows of the box in the layout's data.
            foreach (var notice in new[] { PortalSpaceMachine.PortalNotice, "Cannot log off while in mid-air.", PortalSpaceMachine.PortalNotice, "Cannot log off while in mid-air." })
            {
                ui!.Screens!.Notices.DisplayStringInfo(ChatTextTypes.SpewBox, Lifestone.Data.Ui.UiStringRef.FromLiteral(notice));
                await Frames(2);
            }

            // g3a: the server's vital update (the current health 41) reaches the meter and its label.
            var healthMeter = shell?.Vitals?.Stacked?.HealthMeter;
            var healthLabel = shell?.Vitals?.Stacked?.HealthLabel;
            var changed = await WaitFor(() => healthLabel is not null && healthLabel.Text.Contains("41", StringComparison.Ordinal), 30.0);
            Check(
                "a-vital-update-moves-the-health-meter-and-label",
                changed && healthMeter is not null && Math.Abs(healthMeter.Position - (41f / Expected.HealthMax)) < 0.001f && !healthLabel!.Text.TrimStart().StartsWith(Expected.Health.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal) && Math.Abs(shell!.Vitals!.Stacked!.StaminaMeter!.Position - ((float)Expected.Stamina / Expected.StaminaMax)) < 0.001f,
                $"health={healthMeter?.Position:0.000} label='{healthLabel?.Text}'");

            await Frames(40);
            SaveReviewFrame("03-shell-spew-and-chat");
        }

        // --- g4: the toolbar, the panel host and the link indicator ---
        if (shell is { PanelHost: { } panelHost, ToolbarUi: not null, IndicatorsUi.Link: { } link } && ui?.Manager is { } uiManager)
        {
            async Task Press(Lifestone.Client.RetailUi.Core.UiElement element)
            {
                var box = element.ScreenBox;
                uiManager.Input.MouseMove((box.X0 + box.X1) / 2, (box.Y0 + box.Y1) / 2);
                await Frames(2);
                // Move again in the same frame as the press: anything that moved the router's mouse in the two frames between (the OS cursor over the window) cannot send the press elsewhere.
                uiManager.Input.MouseMove((box.X0 + box.X1) / 2, (box.Y0 + box.Y1) / 2);
                uiManager.Input.MouseButton(Lifestone.Client.RetailUi.Input.UiMouseButton.Left, true);
                uiManager.Input.MouseButton(Lifestone.Client.RetailUi.Input.UiMouseButton.Left, false);
                await Frames(2);
            }

            // One press of the message's OK, no retry: a click that does not register fails the check that follows (review R2). okPresses is reported in that check's line.
            var okPresses = 0;
            async Task PressOk(Lifestone.Client.RetailUi.Core.UiElement? message)
            {
                if (message is not null && ReferenceEquals(Dialog(), message) && message.GetChildRecursive(Lifestone.Client.RetailUi.Dialogs.UiMessageDialog.OkButton) is { } ok)
                {
                    okPresses++;
                    await Press(ok);
                }
            }

            Lifestone.Client.RetailUi.Core.UiElement? Dialog() => uiManager.Dialogs.OpenDialogs.FirstOrDefault();
            string DialogText(Lifestone.Client.RetailUi.Core.UiElement? dialog) => (dialog?.GetChildRecursive(Lifestone.Client.RetailUi.Dialogs.UiDialog.TextElement) as Lifestone.Client.RetailUi.Text.UiTextElement)?.Text ?? "";

            Check("the-panel-host-is-hidden-at-login-and-the-toolbar-is-built", !panelHost.IsOpen && shell.Placeholders.All(p => p is not ("toolbar" or "panels" or "indicators")), $"open={panelHost.IsOpen} placeholders=[{string.Join(", ", shell.Placeholders)}]");

            // One press of a panel button whose page no slice builds (the map; the inventory is b1's) opens the retail message ("Not in Lifestone M3"); its OK closes it.
            if (shell.Root!.GetChildRecursive(0x1000019A) is { } mapButton)
            {
                await Press(mapButton);
                var message = Dialog();
                var text = DialogText(message);
                Check("the-map-button-opens-the-not-in-m3-message", text == Lifestone.Client.RetailUi.Dialogs.NotInM3.Prefix + "Map" && !panelHost.IsOpen, $"dialog='{text}' host_open={panelHost.IsOpen}");
                await Frames(40);
                SaveReviewFrame("04-shell-toolbar-and-message");
                await PressOk(message);

                Check("the-message-closes-with-ok", Dialog() is null && okPresses == 1, $"open_dialogs={uiManager.Dialogs.OpenDialogs.Count()} ok_presses={okPresses}");
            }

            // One press of a combat button.
            if (shell.Root!.GetChildRecursive(0x10000192) is { } combatButton)
            {
                await Press(combatButton);
                var message = Dialog();
                var text = DialogText(message);
                Check("a-combat-button-opens-the-not-in-m3-message", text == Lifestone.Client.RetailUi.Dialogs.NotInM3.Prefix + "Combat and spellcasting", $"dialog='{text}'");
                await PressOk(message);
            }

            // The link indicator with the stand-in (no link to measure: connected and just heard), then with the server silent for 25 and 45 seconds.
            var standInState = link.State;
            var standInLink = link.LinkState;
            var source = link.Source;
            link.Source = () => new Lifestone.Client.RetailUi.Indicators.LinkReading(25, true);
            link.UpdateLinkState();
            var slow = link.State;
            link.Source = () => new Lifestone.Client.RetailUi.Indicators.LinkReading(45, true);
            link.UpdateLinkState();
            var bad = link.State;
            link.Source = source;
            link.UpdateLinkState();
            Check("the-link-indicator-follows-the-time-since-the-last-packet", standInState == 17 && standInLink == 1 && slow is 18 or 19 && bad == 20 && link.State == 17, $"stand-in={standInState}/{standInLink} 25s={slow} 45s={bad} back={link.State}");

            // --- g6: character information, the options panel (SideBySideVitals) and the logout confirmation ---
            var infoAction = panelHost.ElementFor(3)?.VisibilityToggleAction ?? 0;
            var infoOpened = infoAction != 0 && uiManager.DoVisibilityToggleAction(infoAction);
            await Frames(10);
            var report = shell.CharacterInfo?.Report ?? "";
            Check("character-information-opens-with-the-report-of-the-description", infoOpened && panelHost.CurrentPanel == 3 && report.Contains("Innate Strength", StringComparison.Ordinal) && !report.Contains("string table error", StringComparison.Ordinal), $"opened={infoOpened} panel={panelHost.CurrentPanel} report_chars={report.Length}");
            SaveReviewFrame("06-character-information");
            uiManager.DoVisibilityToggleAction(infoAction);
            await Frames(2);

            if (shell.Root!.GetChildRecursive(0x1000019B) is { } optionsButton && shell is { OptionsPanel: { } optionsPanel, Options: { } options, Logout: { } logout })
            {
                var sideBeforePress = options.SideBySideVitals;
                await Press(optionsButton);
                var optionsPage = panelHost.ElementFor(10) as Lifestone.Client.RetailUi.Widgets.UiPanel;
                var optionsShown = panelHost.CurrentPanel == 10 && optionsPage is { Visible: true };
                optionsPage?.OpenTab(0x1000020E); // the Character tab
                optionsPanel.ScrollTo(Lifestone.Client.RetailUi.Options.PlayerOptionTable.SideBySideVitals);
                await Frames(4);
                var sideBox = optionsPanel.BoxOf(Lifestone.Client.RetailUi.Options.PlayerOptionTable.SideBySideVitals);
                if (sideBox is not null)
                {
                    await Press(sideBox);
                    if (optionsPanel.Apply is { } apply)
                    {
                        await Press(apply);
                    }
                }

                await Frames(4);
                SaveReviewFrame("07-options-side-by-side-vitals");
                Check("the-side-by-side-option-swaps-the-vitals-window", optionsShown && !sideBeforePress && options.SideBySideVitals && shell.SideBySideVitals!.Visible && !shell.StackedVitals!.Visible, $"options_shown={optionsShown} before={sideBeforePress} now={options.SideBySideVitals} side_visible={shell.SideBySideVitals?.Visible} stacked_visible={shell.StackedVitals?.Visible}");
                var ghostedBox = optionsPanel.Boxes.First(static b => b.Option is { Honoured: false });
                var ghostedBefore = ghostedBox.Current;
                await Press(ghostedBox);
                Check("a-ghosted-option-does-not-change", ghostedBox.Current == ghostedBefore && options.ChangeCount == 1, $"option={ghostedBox.Option?.Name} before={ghostedBefore} now={ghostedBox.Current} changes={options.ChangeCount}");

                // The logout: the options panel's first tab has the button. It asks; No stays.
                optionsPage?.OpenTab(0x1000020D);
                await Frames(3);
                if (shell.Root.GetChildRecursive(0x10000203) is { } exitButton)
                {
                    await Press(exitButton);
                    var confirm = Dialog();
                    var confirmText = DialogText(confirm);
                    Check("the-logout-control-asks-first", logout.IsOpen && confirmText.StartsWith("This will exit your character from the game world.", StringComparison.Ordinal) && confirm is Lifestone.Client.RetailUi.Dialogs.UiConfirmationDialog, $"open={logout.IsOpen} text='{confirmText.Replace((char)10, (char)124)}'");
                    await Frames(40);
                    SaveReviewFrame("08-logout-confirmation");
                    if (confirm?.GetChildRecursive(Lifestone.Client.RetailUi.Dialogs.UiConfirmationDialog.NoButton) is { } no)
                    {
                        await Press(no);
                    }

                    Check("no-stays-in-the-world", Dialog() is null && logout.Confirmed == 0 && session.State.Phase.ToString() == "InWorld", $"dialog_open={Dialog() is not null} confirmed={logout.Confirmed} phase={session.State.Phase}");
                    // Yes sends Actions.LogOff, the same call as retail's key (the visit has one log off, the key's below; GameShellG6Tests.Yes_logs_off covers Yes).
                }

                await Press(optionsButton); // closes the panel
            }
        }
        else
        {
            Check("the-panel-host-is-hidden-at-login-and-the-toolbar-is-built", false, "the shell has no panel host, toolbar or link indicator");
        }

        // --- a server teleport ---
        _afterTeleport = true;
        var teleported = await WaitFor(() => _mode.Flow.Teleports >= 1, 60.0);
        Check("the-server-teleports-the-player", teleported, $"teleports={_mode.Flow.Teleports}");
        var settled = await WaitFor(() => rig.Controller.Position.CellId == AcademyCell && !rig.Controller.Placing, 60.0);
        var academy = rig.Controller.Position;
        var d = Math.Sqrt(Math.Pow(academy.Origin.X - AcademyOrigin.X, 2) + Math.Pow(academy.Origin.Y - AcademyOrigin.Y, 2));
        Check("the-physics-body-moves-to-the-destination", settled && d < 3.0, $"cell=0x{academy.CellId:X8} horizontal_m={d:0.00}");
        var target = Lifestone.Client.World.AcCoordinates.ToGlobal(new Lifestone.Client.World.CellPosition(academy.CellId, academy.Origin));
        var expectedOrigin = Lifestone.Client.World.AcCoordinates.ClampedLandblockAt(target);
        Check(
            "the-world-moves-with-the-teleport",
            _world.Origin.Origin == expectedOrigin && (_interiors?.Tracker is null || _interiors.Tracker.CellId == AcademyCell),
            $"origin={_world.Origin.Origin} expected={expectedOrigin} viewer_cell=0x{_interiors?.Tracker?.CellId ?? 0:X8}");
        var camera = _camera.WorldPosition;
        var cameraGap = Math.Sqrt(Math.Pow(camera.X - target.X, 2) + Math.Pow(camera.Y - target.Y, 2) + Math.Pow(camera.Z - target.Z, 2));
        Check("the-camera-follows-the-teleport", cameraGap < 12.0, $"camera_to_player_m={cameraGap:0.0}");
        var tunnelAgain = await WaitFor(() => _sawTunnelAfterTeleport, 10.0);
        Check("portal-space-plays-for-the-teleport", tunnelAgain, $"tunnel_seen={_sawTunnelAfterTeleport}");
        // LoginComplete goes out at the end of the last portal state, after the tunnel is already gone.
        await WaitFor(() => _portal is { Director.LoginCompletesSent: >= 2 }, 30.0);
        await Frames(10);
        // ACE takes LoginComplete as "the client has arrived" after every teleport too (Player.OnTeleportComplete): one per arrival, so two now.
        Check("login-complete-is-sent-once-per-arrival", _portal is { Director.LoginCompletesSent: 2 }, $"sent={_portal?.Director.LoginCompletesSent}");

        // --- the tools teleport (G6 W11, G7 lane A): F7 opens the tools panel, a search and a click on Holtburg -> PlayerTools.Place -> the server's reply and teleport -> portal space -> the arrival ---
        var tunnelsBefore = _tunnelShowings;
        var teleportsBefore = _mode.Flow.Teleports;
        var place = await ToolsPanelPlaceAsync(ui);
        var placeDone = place is not null && await WaitFor(() => place.IsCompleted, 10.0);
        Check("tools-place-is-confirmed-by-the-server", placeDone && place!.Outcome?.Kind == ActionOutcomeKind.Confirmed && place.Result is { } result && result.Destination.CellId == 0xA9B40019, $"outcome={place?.Outcome?.Kind} detail={place?.Outcome?.Detail} cell=0x{place?.Result?.Destination.CellId ?? 0:X8}");
        var inHoltburg = await WaitFor(() => _mode.Flow.Teleports > teleportsBefore && rig.Controller.Position.CellId >> 16 == 0xA9B4 && !rig.Controller.Placing, 60.0);
        Check("the-tools-teleport-moves-the-player-to-the-destination", inHoltburg, $"teleports={_mode.Flow.Teleports} cell=0x{rig.Controller.Position.CellId:X8} placing={rig.Controller.Placing}");
        Check("portal-space-plays-for-the-tools-teleport", await WaitFor(() => _tunnelShowings > tunnelsBefore, 10.0), $"tunnel_showings={_tunnelShowings - tunnelsBefore}");
        await WaitFor(() => _portal is { Director.LoginCompletesSent: >= 3 }, 30.0);
        Check("login-complete-is-sent-for-the-tools-teleport", _portal is { Director.LoginCompletesSent: 3 }, $"sent={_portal?.Director.LoginCompletesSent}");
        await WaitFor(() => _portal is { Director.Machine: { TunnelVisible: false, WorldVisible: true } }, 15.0);

        // --- M3 2.2: the panel pages (PlayChecks.Panels.cs: one hook per slice, then the panel frames). They run here, in the world after the tools teleport: before the server teleport
        // they outlasted the stand-in's fixed teleport time, so its tunnel showed during them (missed by the teleport check) and the panel frames showed portal space. ---
        await PanelChecksAsync(shell);
        await ToolsPanelWhereAndXpAsync(session, ui); // G7 lane A: Where am I and Add XP through the panel, and its review frame
        await ThrowawayToolOpsAsync(session, ui); // p2: the throwaway leg's driver presses the same buttons (PlayChecks.Throwaway.cs)

        // --- picking and selection (G5 W7): the stand-in portal is picked at its projected screen point; a double click Uses it ---
        var picking = _mode.Picking;
        var selection = _mode.Selection;
        var portalDrawn = await WaitFor(() => _mode.Layer?.IsDrawn(PortalGuid) == true && picking.TryProject(PortalGuid, out _, out _), 20.0);
        // The portal stands 3 m west of the player: at retail's 800x600 (a narrower view than 16:9) the chase camera does not show it, so the camera is orbited with the mouse look until it does
        // (the steps are reported). The check had assumed a wider window than retail's native one.
        var lookSteps = 0;
        var window = ui is not null ? new Vector2(ui.LogicalSize.X, ui.LogicalSize.Y) : GetViewport().GetVisibleRect().Size;
        async Task LookAtThePortal()
        {
            // The chase camera eases back after a mouse look, so this is repeated before the double click.
            for (var steps = 0; steps < 60 && !(picking.TryProject(PortalGuid, out var looked, out var inView) && inView && looked.X + (looked.Width / 2f) is > 0f && looked.X + (looked.Width / 2f) < window.X && looked.Y + (looked.Height / 2f) is > 0f && looked.Y + (looked.Height / 2f) < window.Y); steps++)
            {
                rig.Controller.OnMouseLook(6f, 0f);
                await Frames(2);
                lookSteps++;
            }

            await Frames(2);
        }

        await LookAtThePortal();
        await Frames(10);
        var projected = picking.TryProject(PortalGuid, out var portalRect, out var portalOnScreen);
        Check("the-stand-in-portal-projects-onto-the-screen", portalDrawn && projected && portalOnScreen, $"drawn={_mode.Layer?.IsDrawn(PortalGuid)} projected={projected} on_screen={portalOnScreen} look_steps={lookSteps} rect={portalRect}");
        // The point of the view nearest the projected centre: the portal's bounds may reach past the window's edge, and a click is always inside the window.
        var viewSize = window;
        var clickX = (int)Math.Clamp(MathF.Round(portalRect.X + (portalRect.Width / 2f)), 0f, viewSize.X - 1f);
        var clickY = (int)Math.Clamp(MathF.Round(portalRect.Y + (portalRect.Height / 2f)), 0f, viewSize.Y - 1f);
        rig.Controller.OnWorldPointer(clickX, clickY, UiMouseButton.Left, true, false);
        rig.Controller.OnWorldPointer(clickX, clickY, UiMouseButton.Left, false, false);
        Check("a-click-on-the-portal-selects-it", selection.Selected == PortalGuid, $"selected=0x{selection.Selected:X8} at=({clickX}, {clickY}) view={viewSize.X}x{viewSize.Y} rect={portalRect}");
        rig.Controller.OnWorldPointer(1, 1, UiMouseButton.Left, true, false);
        rig.Controller.OnWorldPointer(1, 1, UiMouseButton.Left, false, false);
        Check("a-click-on-empty-space-clears-the-selection", !selection.HasSelection, $"selected=0x{selection.Selected:X8}");
        await SelectionShellChecks(ui); // g5b: the toolbar's name and health and the vivid target indicator for the stand-in drudge (docs/retail-ui.md section 21.7)
        tunnelsBefore = _tunnelShowings;
        teleportsBefore = _mode.Flow.Teleports;
        await LookAtThePortal();
        picking.TryProject(PortalGuid, out portalRect, out _);
        clickX = (int)Math.Clamp(MathF.Round(portalRect.X + (portalRect.Width / 2f)), 0f, viewSize.X - 1f);
        clickY = (int)Math.Clamp(MathF.Round(portalRect.Y + (portalRect.Height / 2f)), 0f, viewSize.Y - 1f);
        rig.Controller.OnWorldPointer(clickX, clickY, UiMouseButton.Left, true, false);
        rig.Controller.OnWorldPointer(clickX, clickY, UiMouseButton.Left, false, false);
        rig.Controller.OnWorldPointer(clickX, clickY, UiMouseButton.Left, true, true);
        rig.Controller.OnWorldPointer(clickX, clickY, UiMouseButton.Left, false, false);
        var use = rig.Controller.LastUse;
        Check("a-double-click-on-the-portal-uses-it", selection.Selected == PortalGuid && use is not null, $"selected=0x{selection.Selected:X8} use={(use is null ? "none" : "sent")}");
        var useDone = use is not null && await WaitFor(() => use.IsCompleted, 10.0);
        Check("use-of-a-portal-is-confirmed-by-the-server", useDone && use!.Outcome?.Kind == ActionOutcomeKind.Confirmed, $"outcome={use?.Outcome?.Kind} detail={use?.Outcome?.Detail}");
        var usedPortal = await WaitFor(() => _mode.Flow.Teleports > teleportsBefore && rig.Controller.Position.CellId == AcademyCell && !rig.Controller.Placing, 60.0);
        Check("the-portal-teleports-the-player", usedPortal, $"teleports={_mode.Flow.Teleports} cell=0x{rig.Controller.Position.CellId:X8}");
        Check("portal-space-plays-for-the-portal", await WaitFor(() => _tunnelShowings > tunnelsBefore, 10.0), $"tunnel_showings={_tunnelShowings - tunnelsBefore}");
        // g5b: with the tunnel up the portal cue (SelectionIndicatorOff) holds the indicator off even for a selected object; the next cue (On) lets it show again.
        var cueOff = await WaitFor(() => _portal is { Presenter.SelectionIndicatorEnabled: false }, 10.0);
        _mode.Selection.Select(PortalGuid);
        await Frames(3);
        var holdsOff = (_ui?.Screens?.Flow.CurrentScreen as GamePlayScreen)?.SelectionUi is { Indicator: { Enabled: false, State: Lifestone.Client.RetailUi.Selection.VividTargetState.Hidden } };
        _mode.Selection.Clear();
        await WaitFor(() => _portal is { Director.Machine: { TunnelVisible: false, WorldVisible: true } }, 15.0);
        var cueOn = await WaitFor(() => _portal is { Presenter.SelectionIndicatorEnabled: true }, 15.0);
        await Frames(3); // play mode copies the presenter flag to the indicator on its next update
        Check(
            "the-portal-cue-hides-the-vivid-target-indicator-while-the-tunnel-shows",
            cueOff && holdsOff && cueOn && _tunnelFramesIndicatorDrawn == 0 && (_ui?.Screens?.Flow.CurrentScreen as GamePlayScreen)?.SelectionUi is { Indicator.Enabled: true },
            $"cue_off={cueOff} held_off_for_a_selected_object={holdsOff} cue_on_again={cueOn} tunnel_frames_cue_off={_tunnelFramesIndicatorOff} indicator_drawn_in_them={_tunnelFramesIndicatorDrawn} frames_with_an_object_selected={_tunnelFramesSelected}");
        await WaitFor(() => _portal is { Director.LoginCompletesSent: >= 4 }, 30.0);
        Check("login-complete-is-sent-for-the-portal", _portal is { Director.LoginCompletesSent: 4 }, $"sent={_portal?.Director.LoginCompletesSent}");
        await WaitFor(() => _portal is { Director.Machine: { TunnelVisible: false, WorldVisible: true } }, 15.0);
        await InspectorShellChecks(ui); // G7 lane B: the automation inspector shows this run's own actions (docs/retail-ui.md section 22.2)
        await RaiseCheckAsync(session); // M3 1.1 s1b: one RaiseAttribute against the stand-in, priced by the retail XpTable
        await SpellAndItemChecksAsync(session); // M3 1.1 s2b: one spell learn (learned, then known) and one item against the stand-in
        await ActionChecksAsync(session); // M3 1.2 s4c: a stand-in self cast, a fizzle, a combat-mode change, a wield and an unwield complete as the trackers say
        await CastChecksAsync(session); // M3 2.1 s2b: a wand wielded, the Magic mode, a cast with a wind-up: cast_scripts>=1 cast_sounds>=1 and a mid-cast review frame
        await KitChecksAsync(session); // M3 1.4 s0: SpawnGear against the stand-in: the kit arrives in the pack (kit=n/n), a second run asks for nothing (kit_topup=0)

        // --- a boot by the server: the player is taken away and the retail disconnected screen has the button that reconnects (D43) ---
        var server = _host.StandIn ?? throw new InvalidOperationException("the stand-in session has no server levers");
        server.Boot(0.2, "stand-in boot");
        var booted = await WaitFor(() => _mode.Rig is null && ui?.Flow?.Flow.CurrentMode == UiModes.Disconnected, 15.0);
        Check("a-boot-takes-the-player-away-and-shows-the-disconnected-screen", booted, $"rig={(_mode.Rig is null ? "none" : "built")} mode={(ui?.Flow is { } bf ? UiModes.NameOf(bf.Flow.CurrentMode) : "none")} status='{_mode.Flow.StatusText}'");
        Check("a-boot-leaves-no-objects-and-no-tunnel", _mode.Avatars is { Count: 0 } && _mode.Layer is { Active: false } && _portal is { Director.Machine.TunnelVisible: false }, $"avatars={_mode.Avatars?.Count} layer_active={_mode.Layer?.Active} tunnel={_portal?.Director.Machine.TunnelVisible}");
        var reconnects = session is GameSession gs ? gs.Reconnects : -1;
        Check("the-disconnected-button-says-reconnect", ui is not null && ClickDisconnectedButton(ui, click: false), "the button is on the screen");
        Check("the-button-reconnects", ClickDisconnectedButton(ui!, click: true) && session is GameSession { } after && after.Reconnects == reconnects + 1, $"reconnects={(session as GameSession)?.Reconnects}");
        var listAgain = await WaitFor(() => ui?.Flow?.Flow.CurrentMode == UiModes.CharacterManagement || _mode.Rig is not null, 30.0);
        Check("after-the-reconnect-the-character-list-or-the-world-is-back", listAgain, $"mode={(ui?.Flow is { } rf ? UiModes.NameOf(rf.Flow.CurrentMode) : "none")} runs={_host.StandInRuns}");

        // --- the second arrival (the same play mode, a new avatar set), then a server that goes quiet: the silent-server timeout ends on the same screen ---
        var again = await WaitFor(() => _mode.Rig is not null, 60.0);
        Check("the-player-arrives-again-after-the-reconnect", again && _mode.Rig is { } second && !ReferenceEquals(second, rig) && _mode.Avatars is { Player: not null }, $"rig={(_mode.Rig is null ? "none" : "built")} stage={_mode.Flow.Stage}");
        var secondRig = _mode.Rig;
        await WaitFor(() => _mode.Flow.Stage == Lifestone.Client.Play.PlayStage.InWorld, 60.0);
        server.GoQuiet(0.2, "stand-in server went quiet");
        var silent = await WaitFor(() => _mode.Rig is null && ui?.Flow?.Flow.CurrentMode == UiModes.Disconnected, 15.0);
        Check("a-silent-server-shows-the-disconnected-screen", silent && session.State.FailureMessage is not null, $"rig={(_mode.Rig is null ? "none" : "built")} mode={(ui?.Flow is { } sf ? UiModes.NameOf(sf.Flow.CurrentMode) : "none")} status='{_mode.Flow.StatusText}'");
        Check("the-button-reconnects-after-a-timeout", ClickDisconnectedButton(ui!, click: true) && session is GameSession { Reconnects: 2 }, $"reconnects={(session as GameSession)?.Reconnects}");
        var third = await WaitFor(() => _mode.Rig is not null && !ReferenceEquals(_mode.Rig, secondRig), 60.0);
        Check("the-player-arrives-a-third-time", third, $"stage={_mode.Flow.Stage}");
        var present3 = await WaitFor(() => _mode.Flow.Stage == Lifestone.Client.Play.PlayStage.InWorld, 60.0);
        Check("the-third-arrival-is-in-the-world", present3, $"stage={_mode.Flow.Stage}");

        // --- a log off through retail's key (Shift+Escape, End Character Session): Actions.LogOff only; the bridge drives portal space ---
        var key = _mode.EndKey;
        var input = ui?.Manager.Input;
        tunnelsBefore = _tunnelShowings;
        _watchLogOffTunnel = true;
        var requestsBefore = key?.Requests ?? 0;
        if (input is not null)
        {
            input.KeyDown(DikScanCodes.LShift);
            input.KeyDown(DikScanCodes.Escape);
            input.KeyUp(DikScanCodes.Escape);
            input.KeyUp(DikScanCodes.LShift);
        }

        Check("shift-escape-asks-for-a-log-off", key is { Last: not null } && key.Requests == requestsBefore + 1, $"requests={key?.Requests} before={requestsBefore}");
        var logOff = key?.Last;
        Check("the-log-off-starts-portal-space-s-logoff-transition", await WaitFor(() => _portal is { Director.Machine.LogOffStarted: true } && _tunnelShowings > tunnelsBefore, 10.0), $"log_off_started={_portal?.Director.Machine.LogOffStarted} tunnel_showings={_tunnelShowings - tunnelsBefore}");
        var gone = await WaitFor(() => _mode.Rig is null, 60.0);
        await Frames(3);
        Check(
            "a-logoff-takes-the-player-away",
            gone && _world.FocusSource is null && PlayerController.Current is null && _portal?.Attachments.CollisionSource is null,
            $"rig={(_mode.Rig is null ? "none" : "built")} focus={_world.FocusSource?.GetType().Name ?? "none"} controller={(PlayerController.Current is null ? "none" : "alive")}");
        Check("the-log-off-is-confirmed-by-the-character-list", logOff is not null && await WaitFor(() => logOff.IsCompleted, 10.0) && logOff.Outcome?.Kind == ActionOutcomeKind.Confirmed, $"outcome={logOff?.Outcome?.Kind} detail={logOff?.Outcome?.Detail}");
        Check(
            "server-objects-leave-with-the-logoff",
            _mode.Avatars is { Count: 0 } && _mode.Layer is { Active: false, DrawnObjects: 0 } && _mode.Adapter is { Pending: 0 },
            $"avatars={_mode.Avatars?.Count} layer_active={_mode.Layer?.Active} drawn={_mode.Layer?.DrawnObjects} pending={_mode.Adapter?.Pending}");
        var characters = await WaitFor(() => ui?.Flow?.Flow.CurrentMode == UiModes.CharacterManagement, 10.0);
        Check("the-ui-is-back-at-character-management", characters, $"mode={(ui?.Flow is { } f ? UiModes.NameOf(f.Flow.CurrentMode) : "none")}");
        Check("the-flow-is-back-at-the-character-list", _mode.Flow.Stage == Lifestone.Client.Play.PlayStage.CharacterList, $"stage={_mode.Flow.Stage}");
        _watchLogOffTunnel = false;
        Check(
            "the-logoff-tunnel-is-held-until-the-character-list",
            _logOffTunnelSeen && _logOffTunnelGapSeconds <= LogOffTunnelGapToleranceSeconds,
            $"logoff_tunnel={(_logOffTunnelSeen && _logOffTunnelGapSeconds <= LogOffTunnelGapToleranceSeconds ? "held" : _logOffTunnelSeen ? "dropped" : "never-shown")} visible_seconds={_logOffTunnelSeconds:0.00} gap_seconds={_logOffTunnelGapSeconds:0.00} (D28: the tunnel stays from the collapse until the retail character list)");

        // DoD 15 from the player's arrival to the server's first teleport: the build, the world's start, the objects' creates and the wait for the unhide.
        Check(
            "no-frame-over-50-ms-from-the-arrival-to-the-teleport",
            _slowFrames == 0 || _framesDirectory is not null,
            $"slow_frames={_slowFrames} max_ms={_maxFrameMs:0.0}{(_framesDirectory is not null ? " (REPORTED, NOT JUDGED: a --play-frames run is not a timing run, each frame's read-back stalls the frame it is taken in)" : "")} (teleport frames, reported not judged: slow={_teleportSlowFrames} max_ms={_teleportMaxFrameMs:0.0})");

        // DoD 15 as the live gate meets it: Enter pressed within 0.2 s of the character list (the shell's warm-up is still building and painting), and every frame from the list to the player's
        // walking under 50 ms. The stand-in's character management used to last a second, so the warm-up finished before Enter and the checks never saw its heavy frame (docs/session.md section 27).
        Check(
            "enter-right-after-character-select",
            (_fromListMaxMs <= FrameLimitMilliseconds && _maxFrameMs <= FrameLimitMilliseconds && _fromListFrames > 0) || _framesDirectory is not null,
            $"list_to_arrival_max_ms={_fromListMaxMs:0.0} frames={_fromListFrames} arrival_to_teleport_max_ms={_maxFrameMs:0.0} warm_up_running_at_enter={_warmUpRanAtEnter}{(_framesDirectory is not null ? " (REPORTED, NOT JUDGED: a --play-frames run is not a timing run)" : string.Empty)}");

        // DoD 15 from Enter to the arrival, with the heavy description: the frame that handles the 138-enchantment PlayerDescription, the arrival and the shell's construction (a frame of its own,
        // UiFlow.QueueUIModeAfterAFrame) measured 75 ms before the shell's code was compiled on a worker (ShellJitWarmUp) and the shell started a frame later. docs/session.md section 27.
        Check(
            "no-frame-over-50-ms-from-enter-to-the-arrival-with-a-heavy-player-description",
            _enterWindowMaxMs <= FrameLimitMilliseconds || _framesDirectory is not null,
            $"enter_window_max_ms={_enterWindowMaxMs:0.0} frames={_enterWindowFrames}{(_framesDirectory is not null ? " (REPORTED, NOT JUDGED: a --play-frames run is not a timing run)" : string.Empty)}");

        // The shell is made over frames, not in one: after Enter no single retail UI step (the runtime tick, the screens, the paint, the upload and the shell's slice of the build) is long. The shell's own
        // figures are in the log line 'the in-game shell was built over N frames'.
        Check(
            "no-ui-step-after-enter-is-over-12-ms",
            (_uiStepWindowOpen && _uiStepMaxMs <= MaxUiStepMilliseconds) || _framesDirectory is not null,
            $"worst_ui_step_ms={_uiStepMaxMs:0.0} limit={MaxUiStepMilliseconds:0} shell_build_frames={_shellBuildFrames} shell_longest_frame_ms={_shellBuildLongestFrameMs:0.0} shell_longest_unit_ms={_shellBuildLongestUnitMs:0.0} shell_staged={_shellStaged}{(_framesDirectory is not null ? " (REPORTED, NOT JUDGED: a --play-frames run is not a timing run)" : string.Empty)}");

        // The arrival's first frame used to compile a canvas pipeline (15 to 80 ms; docs/session.md section 27): CanvasWarmUp draws that item at start-up, so none compiles from Enter on.
        Check(
            "no-canvas-pipeline-is-compiled-from-enter-on",
            _canvasPipelinesFromEnter == 0,
            $"canvas_pipelines_from_enter={_canvasPipelinesFromEnter} (each compile is logged above as 'pipeline compilation' with its stage and frame time)");

        // p2: the throwaway leg's screen driver on the character list the log off came back to (after the timing checks, which it would disturb): character creation by clicks, Delete, Restore.
        await ThrowawayListOpsAsync(session, ui);
    }

    /// <summary>
    /// g5b (docs/retail-ui.md section 21.7): selecting the stand-in drudge shows its name in the toolbar, the stand-in answers the health query and the meter shows the fraction, the vivid
    /// target indicator is drawn for it (corners or the off-screen arrow), and clearing the selection clears all of it.
    /// </summary>
    private async Task SelectionShellChecks(RetailUiLayer? ui)
    {
        const uint guardGuid = 0x80000A04; // the stand-in drudge: attackable, ahead of the player
        var shell = ui?.Screens?.Flow.CurrentScreen as GamePlayScreen;
        var display = shell?.SelectionUi;
        Check("the-shell-has-the-selection-display-and-no-selection-placeholder", display is not null && !shell!.Placeholders.Contains("selection"), $"display={(display is null ? "none" : "built")} placeholders={string.Join(",", shell?.Placeholders ?? [])}");
        if (display is null)
        {
            return;
        }

        var picking = _mode.Picking;
        await WaitFor(() => _mode.Layer?.IsDrawn(guardGuid) == true && picking.TryProject(guardGuid, out _, out _), 20.0);
        _mode.Selection.Select(guardGuid);
        var named = await WaitFor(() => display.Selected == guardGuid && display.FieldShown && display.NameText == "Stand-in Drudge", 5.0);
        Check("selecting-the-stand-in-drudge-shows-its-name-in-the-toolbar", named, $"selected=0x{display.Selected:X8} field={display.FieldShown} name='{display.NameText}'");
        var healthy = await WaitFor(() => display.HealthShown, 10.0);
        Check(
            "the-stand-in-answers-the-health-query-and-the-toolbar-shows-a-fraction",
            healthy && display.LastQuery is { Outcome.Kind: ActionOutcomeKind.Confirmed } && Math.Abs(display.HealthFraction!.Value - 0.6f) < 0.001f,
            $"queries={display.Queries} outcome={display.LastQuery?.Outcome?.Kind} health={display.HealthFraction?.ToString("0.00") ?? "none"}");
        var indicator = display.Indicator;
        var drawn = await WaitFor(() => indicator.State != Lifestone.Client.RetailUi.Selection.VividTargetState.Hidden, 5.0);
        picking.TryProject(guardGuid, out var guardRect, out var guardOnScreen);
        Check(
            "the-vivid-target-indicator-is-drawn-for-the-selected-drudge",
            drawn && indicator.OptionOn && indicator.Enabled,
            $"state={indicator.State} frame={indicator.FrameBox} arrow={indicator.ArrowPosition} arrow_image={indicator.ArrowImage} rect={guardRect} on_screen={guardOnScreen} option={indicator.OptionOn}");
        await Frames(4);
        SaveReviewFrame("05-shell-selection-and-vivid-indicator");

        _mode.Selection.Clear();
        var cleared = await WaitFor(() => display.Selected == 0 && !display.FieldShown && !display.HealthShown && display.NameText.Length == 0 && indicator.State == Lifestone.Client.RetailUi.Selection.VividTargetState.Hidden, 5.0);
        Check("clearing-the-selection-clears-the-name-the-health-and-the-indicator", cleared, $"selected=0x{display.Selected:X8} field={display.FieldShown} health={display.HealthShown} name='{display.NameText}' state={indicator.State}");
    }

    /// <summary>
    /// G7 lane B (docs/retail-ui.md section 22.2): the automation inspector opens over the shell and lists this run's own actions with their outcomes (the typed line's Talk, the
    /// portal's Use, the tools teleport), the counters, and the kill switch and dry run read-only; one review frame; it hides again.
    /// </summary>
    private async Task InspectorShellChecks(RetailUiLayer? ui)
    {
        var shell = ui?.Screens?.Flow.CurrentScreen as GamePlayScreen;
        var inspector = shell?.Inspector;
        Check("the-shell-has-the-automation-inspector-closed", inspector is { IsOpen: false }, $"inspector={(inspector is null ? "none" : "built")}");
        if (inspector is null || ui?.Session is not { } inspected)
        {
            return;
        }

        var log = inspected.ActionLog;
        inspector.Open();
        await Frames(4);
        Check(
            "the-inspector-opens-and-lists-the-action-log",
            inspector.IsOpen && inspector.RowCount == Math.Min(log.Total, ActionLog.Capacity) && inspector.RowCount > 0,
            $"open={inspector.IsOpen} rows={inspector.RowCount} logged={log.Total} counters='{inspector.CountersText.Replace((char)10, '/')}'");
        string? Find(string a, string b)
        {
            for (var i = 0; i < inspector.RowCount; i++)
            {
                if (inspector.RowText(i) is { } row && row.Contains(a, StringComparison.Ordinal) && row.Contains(b, StringComparison.Ordinal))
                {
                    return row;
                }
            }

            return null;
        }

        var talk = Find("Talk", "-> Confirmed");
        var use = Find("Use guid=0x80000A03", "-> Confirmed");
        var place = Find("ToolPlace Holtburg", "-> Confirmed");
        Check("the-inspector-shows-the-typed-talk-with-its-outcome", talk is not null, $"row='{talk}'");
        Check("the-inspector-shows-the-portal-use-with-its-outcome", use is not null, $"row='{use}'");
        Check("the-inspector-shows-the-tools-teleport-with-its-outcome", place is not null, $"row='{place}'");
        Check(
            "the-inspector-newest-row-is-the-logs-newest-action",
            inspector.RowText(0) == Lifestone.Client.RetailUi.LifestonePanels.AutomationInspector.FormatRow(log[log.Count - 1]),
            $"row0='{inspector.RowText(0)}'");
        Check(
            "the-inspector-counts-the-outcomes-and-shows-the-switches-read-only",
            inspector.CountersText.Contains($"Confirmed {log.CountOf(ActionOutcomeKind.Confirmed):N0} |", StringComparison.Ordinal) && inspector.StateText.StartsWith("Kill switch: off | Dry run: off", StringComparison.Ordinal),
            $"state='{inspector.StateText}' confirmed={log.CountOf(ActionOutcomeKind.Confirmed)}");
        await Frames(6);
        Check("the-tools-panel-is-hidden-while-the-inspector-shows", shell?.ToolsUi is { IsOpen: false, Window.IsShown: false }, $"tools_open={shell?.ToolsUi?.IsOpen} tools_shown={shell?.ToolsUi?.Window?.IsShown}");
        SaveReviewFrame("09-shell-automation-inspector");
        inspector.Close();
        await Frames(2);
        Check("the-inspector-hides-again", !inspector.IsOpen, $"open={inspector.IsOpen}");
    }

    /// <summary>
    /// The disconnected screen's button: true when it is on the screen; with <paramref name="click"/> it is also clicked (the screen's own handler reconnects).
    /// </summary>
    private static bool ClickDisconnectedButton(RetailUiLayer ui, bool click)
    {
        foreach (var root in ui.Manager.Roots)
        {
            if (root.FindRelative(DisconnectedScreen.ButtonId) is { } button)
            {
                if (click)
                {
                    button.BroadcastElementMessage(UiMessageIds.ButtonClick);
                }

                return true;
            }
        }

        return false;
    }

    private static bool AcCoordinatesIsOutdoor(uint cell) => Lifestone.Client.World.AcCoordinates.IsOutdoorCell(cell);

    private void Check(string name, bool ok, string detail)
    {
        _results.Add((name, ok));
        var line = $"{(ok ? "PASS" : "FAIL")} {name}";
        if (ok)
        {
            Log.Info(LogCategory, line, ("detail", detail));
        }
        else
        {
            Log.Error(LogCategory, line, ("detail", detail));
        }
    }

    /// <summary>Waits for <paramref name="condition"/>, a frame at a time, for at most <paramref name="seconds"/>; returns whether it held.</summary>
    /// <summary>
    /// M3 1.1 s1b: one <c>RaiseAttribute</c> (Strength, one rank at the retail XpTable's price, loaded from the dats) against the stand-in completes Confirmed on the attribute's private update,
    /// and the read model shows the new rank, the experience spent and the available experience reduced by exactly that price. Offline: it proves the client's path, not ACE's acceptance
    /// (the live <c>StatsLiveTests</c> do that).
    /// </summary>
    private async Task RaiseCheckAsync(IGameSessionView session)
    {
        const string name = "a-raise-attribute-is-confirmed-by-the-private-update-and-the-read-model-shows-it";
        if (_services.Dats is not { } dats)
        {
            Check(name, false, "no dats: the stand-in cannot price the raise");
            return;
        }

        var table = Visit.XpTable ??= XpTableData.Load(dats);
        var cost = table.XpToRaiseAttribute(0, 1);
        var q = session.State.Self.Qualities;
        _ = q.TryGetAttribute(1, out var before);
        var availableBefore = q.AvailableExperience;
        var raise = session.Actions.RaiseAttribute(1, (uint)cost, ActionSource.Host);
        var done = await WaitFor(() => raise.IsCompleted, 10.0);
        _ = q.TryGetAttribute(1, out var after);
        Check(
            name,
            done && raise.Outcome?.Kind == ActionOutcomeKind.Confirmed && after.Ranks == before.Ranks + 1 && after.Experience == before.Experience + cost && q.AvailableExperience == availableBefore - (long)cost,
            $"outcome={raise.Outcome?.Kind} detail={raise.Outcome?.Detail} cost={cost} ranks={before.Ranks}->{after.Ranks} spent={before.Experience}->{after.Experience} available={availableBefore}->{q.AvailableExperience} level={q.Level}");
    }

    /// <summary>
    /// M3 1.1 s2b: <c>PlayerTools.LearnSpells</c> against the stand-in, with the retail SpellTable loaded from the dats: Strength Self I (id 2) is learned the first time and known the
    /// second, and an id outside the table fails by name; <c>PlayerTools.SpawnItem</c> creates Pyreal with the stack clamped to the item's maximum. Offline: it proves the client's
    /// typed path and the stand-in's answers in the mod's reply bytes, not ACE's acceptance (the live <c>ModVerbsLiveTests</c> do that).
    /// </summary>
    private async Task SpellAndItemChecksAsync(IGameSessionView session)
    {
        const string spellName = "spell-learn-is-learned-then-known-and-an-unknown-id-fails-by-name";
        const string itemName = "item-creates-a-clamped-stack-in-the-pack";
        if (_services.Dats is not { } dats)
        {
            Check(spellName, false, "no dats: the stand-in cannot read the SpellTable");
            Check(itemName, false, "no dats: skipped with the spell check");
            return;
        }

        Visit.SpellTable ??= Lifestone.Data.Magic.SpellTableData.Load(dats);
        var first = session.Tools.LearnSpells([2, 3], ActionSource.Host);
        var firstDone = await WaitFor(() => first.IsCompleted, 10.0);
        var second = session.Tools.LearnSpells([2, 3], ActionSource.Host);
        var secondDone = await WaitFor(() => second.IsCompleted, 10.0);
        var bad = session.Tools.LearnSpells([2, 99999999], ActionSource.Host);
        var badDone = await WaitFor(() => bad.IsCompleted, 10.0);
        Check(
            spellName,
            firstDone && secondDone && badDone
                && first.Outcome?.Kind == ActionOutcomeKind.Confirmed && first.Result is { Ok: true, Learned: 2, Known: 0 }
                && second.Outcome?.Kind == ActionOutcomeKind.Confirmed && second.Result is { Ok: true, AllAlreadyKnown: true }
                && bad.Outcome?.Kind == ActionOutcomeKind.ServerError && bad.Result is { Ok: false, Unknown: 1 } && bad.Outcome?.Detail?.Contains("99999999", StringComparison.Ordinal) == true,
            $"first={first.Outcome?.Kind} {first.Result?.Learned}L/{first.Result?.Known}K name={first.Result?.Spells.FirstOrDefault()?.Name} second={second.Outcome?.Kind} {second.Result?.Learned}L/{second.Result?.Known}K bad={bad.Outcome?.Kind} detail={bad.Outcome?.Detail}");

        var item = session.Tools.SpawnItem(StandInItemKind.PyrealWcid, 60_000, ActionSource.Host);
        var itemDone = await WaitFor(() => item.IsCompleted, 10.0);
        var unknown = session.Tools.SpawnItem(99_999_999, 1, ActionSource.Host);
        var unknownDone = await WaitFor(() => unknown.IsCompleted, 10.0);
        Check(
            itemName,
            itemDone && unknownDone
                && item.Outcome?.Kind == ActionOutcomeKind.Confirmed && item.Result is { Clamped: true, Stack: 25_000, Requested: 60_000, MaxStack: 25_000 } && item.Result.Guid >= 0x80000000
                && ItemOutcomes.Of(unknown) == ItemOutcome.UnknownWcid,
            $"item={item.Outcome?.Kind} guid=0x{item.Result?.Guid:X8} stack={item.Result?.Stack} requested={item.Result?.Requested} max={item.Result?.MaxStack} clamped={item.Result?.Clamped} unknown={ItemOutcomes.Of(unknown)}");
    }

    private async Task<bool> WaitFor(Func<bool> condition, double seconds)
    {
        var deadline = Time.GetTicksMsec() + (ulong)(seconds * 1000.0);
        while (!condition())
        {
            if (Time.GetTicksMsec() > deadline)
            {
                return false;
            }

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        return true;
    }

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
