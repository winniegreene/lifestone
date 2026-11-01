using Godot;
using Lifestone.Client.PortalSpace;
using Lifestone.Client.Streaming;
using Lifestone.Game.Core;
using Lifestone.Game.Render;

namespace Lifestone.Game.PortalSpace;

/// <summary>
/// Steps the <see cref="PortalSpaceDirector"/> once a frame and applies what its <see cref="PortalFrame"/> says
/// (docs/portal-space.md §11.1): whether the world is drawn (the <see cref="WorldVisibility"/> arbiter, the only writer of
/// <c>Disable3D</c>), whether the tunnel shows and how it rolls, and the zoom's lens on whichever view shows (the tunnel's camera,
/// the world camera through <see cref="WorldLens"/>; the sky follows the world camera).
/// </summary>
/// <remarks>
/// <para><b>Order.</b> Process priority <see cref="Priority"/> (15): after the session host (−900), the teleport panel and the
/// world (0 to 5), before the sky controller (20). The panel's hook (<see cref="OfflinePortalTransition"/>) therefore comes first in a
/// frame and the director steps later in the same frame, so the world hides on the hook's frame and the destination never flashes.
/// The hook's camera move runs at the start of the next frame (the presenter's <c>afterStep</c> arms it), while the destination cell
/// the presenter reads is 0 (never ready) until it has run.</para>
/// <para><b>Sound.</b> The director plays the enter and exit whooshes and the tunnel hum itself, through the
/// <see cref="Lifestone.Client.Audio.ISoundSink"/> it was built with (the audio service's retail sound player); the presenter
/// does nothing for <see cref="PortalCues.PlayEnterPortal"/> and <see cref="PortalCues.PlayExitPortal"/>, so no whoosh plays twice
/// (docs/portal-space.md §15.1).</para>
/// <para><b>Cues.</b> The busy cue is raised through <see cref="BusyChanged"/> (the retail UI's busy count, g6), and it also holds the explore camera's input (<c>busyChanged</c>):
/// retail is busy through the whole sequence, and flying the hidden camera would move the readiness destination under the wait.</para>
/// <para><b>The notice line</b> (the D32 placeholder) shows while the tunnel does (<see cref="PortalFrame.TunnelVisible"/>, states 2 to 5)
/// and is gone when the world is back (m2-log-in §4.1 step 3, §4.4).</para>
/// <para><b>A presenter that starts mid-sequence, or after a reset, follows the steady fields</b> (<see cref="PortalFrame.TunnelVisible"/>,
/// <see cref="PortalFrame.WorldVisible"/>, <see cref="PortalFrame.Lens"/>), never the cues, so it needs no history.</para>
/// <para><b>Clock.</b> Real seconds by default (<see cref="PortalSpaceSetup.Now"/>); a capture or a test gives it a fixed step, which
/// advances only while a sequence runs (or an update is in progress), so the sequence, the tunnel's spin and the roll are the same frame
/// for frame whatever the machine's speed or the time spent waiting for the world (<c>--portal-space-frames</c>).</para>
/// </remarks>
public partial class PortalSpacePresenter : Node
{
    /// <summary>Process priority (docs/game-core.md §1.2): after the session host and the world.</summary>
    public const int Priority = 15;

    private PortalSpaceDirector _director = null!;
    private TunnelObject _tunnel = null!;
    private PortalTunnelView _view = null!;
    private WorldLens _lens = null!;
    private WorldVisibility _visibility = null!;
    private IWorldAttachments _world = null!;
    private Func<uint> _playerCell = null!;
    private PortalNoticeLine? _notice;
    private double _now;
    private double _fixedStep;
    private TeleportAnimState _state = TeleportAnimState.Off;
    private bool _busy;
    private Action<bool>? _busyChanged;

    /// <summary>Told when the busy state changes (true while portal space plays): the retail UI raises and lowers its busy count from it (g6; closes <c>TODO(portal): G2 busy count</c>).</summary>
    public event Action<bool>? BusyChanged;

    /// <summary>
    /// Told once per arrival, when state 6 (<see cref="TeleportAnimState.Arriving"/>) ends and the machine is off again: the world has been shown for
    /// the whole of state 6 (M5 7 P7d: the render caches' trim, <see cref="Lifestone.Client.Rendering.ArrivalCacheTrim"/>).
    /// </summary>
    public event Action? Arrived;
    private Action? _afterStep;

    /// <summary>
    /// Whether the vivid target indicator may show (retail <c>m_bEnabled</c>): true at the start, off from the cue that starts the tunnel to the cue that ends it
    /// (<see cref="PortalCues.SelectionIndicatorOff"/> and <see cref="PortalCues.SelectionIndicatorOn"/>). The shell's indicator reads it (play mode copies it each frame).
    /// </summary>
    public bool SelectionIndicatorEnabled { get; private set; } = true;

    /// <summary>The frame last applied.</summary>
    public PortalFrame LastFrame { get; private set; }

    /// <summary>Frames stepped.</summary>
    public int Frames { get; private set; }

    /// <summary>The director this presenter steps.</summary>
    public PortalSpaceDirector Director => _director;

    /// <summary>The world camera's lens controller.</summary>
    public WorldLens Lens => _lens;

    /// <summary>The time the next step will run at (seconds on the presenter's clock).</summary>
    public double Now => _now;

    /// <summary>Creates the presenter.</summary>
    /// <param name="director">The director to step.</param>
    /// <param name="tunnel">The tunnel object the director's machine runs.</param>
    /// <param name="view">The tunnel on screen.</param>
    /// <param name="lens">The world camera's lens controller.</param>
    /// <param name="visibility">The world-visibility arbiter.</param>
    /// <param name="world">What the world has attached (the destination's readiness).</param>
    /// <param name="playerCell">The destination's cell id this frame (the viewer's cell offline).</param>
    /// <param name="notice">The placeholder status line, or null.</param>
    /// <param name="clockStart">The clock's first reading (the tunnel object's <c>createdAt</c>).</param>
    /// <param name="fixedStep">Seconds per frame on a fixed clock, or 0 for real time.</param>
    /// <param name="busyChanged">Told when the busy state (retail <c>teleportInProgress</c>) changes: the explore camera's input is held while it is true.</param>
    /// <param name="afterStep">Called at the end of each step (the offline transition arms its pending camera move with it).</param>
    public static PortalSpacePresenter Create(
        PortalSpaceDirector director,
        TunnelObject tunnel,
        PortalTunnelView view,
        WorldLens lens,
        WorldVisibility visibility,
        IWorldAttachments world,
        Func<uint> playerCell,
        PortalNoticeLine? notice,
        double clockStart,
        double fixedStep = 0.0,
        Action<bool>? busyChanged = null,
        Action? afterStep = null)
    {
        ArgumentNullException.ThrowIfNull(director);
        ArgumentNullException.ThrowIfNull(tunnel);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(lens);
        ArgumentNullException.ThrowIfNull(visibility);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(playerCell);
        return new PortalSpacePresenter
        {
            Name = "PortalSpacePresenter",
            ProcessPriority = Priority,
            _director = director,
            _tunnel = tunnel,
            _view = view,
            _lens = lens,
            _visibility = visibility,
            _world = world,
            _playerCell = playerCell,
            _notice = notice,
            _now = clockStart,
            _fixedStep = fixedStep,
            _busyChanged = busyChanged,
            _afterStep = afterStep,
        };
    }

    /// <summary><c>--portal-space</c>: begins a local transition now and keeps the update from completing, so the tunnel holds in state 3 until the run is quit.</summary>
    public void StartHeld()
    {
        _director.Progress.BeginLocal();
        _director.Progress.Hold(true);
        Log.Info("portal", "portal space held for review (--portal-space)");
    }

    public override void _Process(double delta)
    {
        if (_fixedStep <= 0.0)
        {
            _now = PortalSpaceSetup.Now();
        }
        else if (LastFrame.State != TeleportAnimState.Off || _director.Progress.InProgress)
        {
            _now += _fixedStep; // the fixed clock runs only while a sequence does, so waiting for the world to settle does not change it
        }

        Apply(_director.Step(_now, _playerCell(), _world));
        _afterStep?.Invoke();
    }

    /// <summary>Applies one frame (also the checks' entry point).</summary>
    public void Apply(in PortalFrame frame)
    {
        LastFrame = frame;
        Frames++;

        // What is drawn. The world's arbiter is the only writer of Disable3D; the tunnel's view renders only while shown.
        _visibility.Follow(WorldVisibility.PortalSpaceOwner, frame.WorldVisible);
        _view.SetShown(frame.TunnelVisible);
        if (frame.TunnelVisible)
        {
            _view.SetLens(frame.Lens); // when the zoom is not overriding, Focal is the game's own f and this is the game's own projection
            _view.PlaceCamera(frame.RollDegrees);
            _view.Pose(_tunnel);
        }

        // The zoom on the world camera only while the world is drawn (it is hidden for the middle of a sequence, and its camera
        // keeps the lens it had when it went); the sky and the interiors read the camera's lens from the camera itself.
        if (frame.WorldVisible)
        {
            _lens.Follow(frame.Lens);
        }

        _notice?.SetActive(frame.TunnelVisible);

        if (frame.Busy != _busy)
        {
            _busy = frame.Busy;
            _busyChanged?.Invoke(_busy);
            BusyChanged?.Invoke(_busy); // the retail UI's busy count (ClientUISystem::IncrementBusyCount 11.6096 0x00565610 / DecrementBusyCount 0x00565630, TODO(portal): their 11.4186 addresses are not looked up; Main wires it to UiElementManager.Busy, docs/retail-ui.md section 21.8)
        }

        // VividTargetIndicator::SetEnabled (11.6096 0x004F6CE0): the cue is the whole story, and the shell's indicator (g5b) follows SelectionIndicatorEnabled.
        if (frame.Cues.HasFlag(PortalCues.SelectionIndicatorOn))
        {
            SelectionIndicatorEnabled = true;
        }
        else if (frame.Cues.HasFlag(PortalCues.SelectionIndicatorOff))
        {
            SelectionIndicatorEnabled = false;
        }

        if (frame.State != _state)
        {
            var arrived = _state == TeleportAnimState.Arriving && frame.State == TeleportAnimState.Off;
            _state = frame.State;
            Log.Info("portal", "portal space state", ("state", frame.State), ("tunnel", frame.TunnelVisible), ("world", frame.WorldVisible), ("frame", _tunnel.CurrentFrame), ("f", frame.Lens.Focal));
            if (arrived)
            {
                Arrived?.Invoke();
            }
        }
    }
}
