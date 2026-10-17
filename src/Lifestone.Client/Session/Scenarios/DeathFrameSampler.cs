using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The death leg's frame measure (<c>--login-check=bsd-death</c>, M3 3.2 dd; plan section 5, "The fight's frames"): <c>death_frame_max_ms</c>, the worst frame from the player's Dead motion to the first frame after the player
/// is present again at the death's arrival: the Dead animation, portal space, the arrival landblock's load and the materialise burst (report-only: a new judge). One call per frame, no allocation, no clock of its own (the
/// engine's delta is the previous frame's whole time). It also keeps the order of the death's own messages that the review frames need and a per-frame counter cannot see: <see cref="CorpseBeforePortal"/> (ACE creates the
/// corpse, then sends the teleport, <c>Player_Death.cs</c> 247-292, both in one tick) and the arrivals.
/// </summary>
/// <remarks>
/// <para><b>The window.</b> It opens in the frame the player's own F74C carries the Dead motion (<see cref="DeadMotions"/> rises; that frame's own time comes in the next call's delta, so the first delta counted is the next
/// call's) and closes <see cref="TailFrames"/> calls after the call that saw the player present (<see cref="SelfTravel.Present"/> after the death's arrival): that call's delta is the last frame of portal space, the next
/// call's the frame the player materialised in, and the one after it the first frame after the arrival. The arrival itself (the <see cref="PositionVerdict.SelfTeleport"/> position after the death's F751) is counted, not
/// waited for: ACE sends the F751 and that position in one call (<c>Player_Location.cs</c> 686-696, a placeholder position so the client starts loading), so portal space begins there and ends only when ACE clears Hidden
/// after the client's LoginComplete, the signal the scenario's own respawn wait reads (<c>DeathScenario.ArrivalStep</c>). A second death (the suicide of the unbuff, then the tuskers) opens the window again; the maximum is
/// over all of them. A frame flagged <c>skip</c> (the one after a saved review frame, the read-back stalls it) is left out.</para>
/// <para><b>Fed from the bus</b>, not the tally, because the order matters: the tally counts the corpse and the teleport, this class says which came first and counts the arrivals (a self teleport after a death's F751).</para>
/// </remarks>
public sealed class DeathFrameSampler : IDisposable
{
    /// <summary>The calls kept after the one that saw the player present: the frame the player materialised in and the first frame after it.</summary>
    public const int TailFrames = 2;

    private const string CorpsePrefix = "Corpse of ";

    private readonly List<IDisposable> _subscriptions = [];
    private IGameSessionView? _view;
    private bool _awaitingPortal;
    private bool _awaitingArrival;
    private bool _awaitingPresent;
    private bool _open;
    private int _tail;
    private bool _fresh;
    private double _maxMs = double.NaN;
    private long _frames;
    private int _windows;

    /// <summary>Own Dead motions seen (F74C, forward command Dead); a suicide's counts too.</summary>
    public int DeadMotions { get; private set; }

    /// <summary>The death teleports (F751) that followed a Dead motion.</summary>
    public int Portals { get; private set; }

    /// <summary>The self teleports (positions) that followed such a F751: the death's arrival.</summary>
    public int Arrivals { get; private set; }

    /// <summary>The player's corpses created inside a death's window (CreateObject with the corpse flag, named "Corpse of" and the player's name, after a Dead motion and before that death's arrival: the death's own, M3 3.2 review R1).</summary>
    public int Corpses { get; private set; }

    /// <summary>The corpses of the player created outside a death's window (an earlier death's corpse coming into view again; report-only).</summary>
    public int StaleCorpses { get; private set; }

    /// <summary>True when the last corpse's CreateObject came after a Dead motion and before that death's F751 (ACE's order); false when it came after the F751 or with no death pending.</summary>
    public bool CorpseBeforePortal { get; private set; }

    /// <summary>The worst frame in a window, in milliseconds, or null when no window has run.</summary>
    public double? DeathFrameMaxMs => double.IsNaN(_maxMs) ? null : _maxMs;

    /// <summary>The frames counted in windows.</summary>
    public long Frames => _frames;

    /// <summary>The windows opened (one a death).</summary>
    public int Windows => _windows;

    /// <summary>True while a window is open.</summary>
    public bool WindowOpen => _open;

    /// <summary>Subscribes to the bus and takes the baselines (everything zero); calling it again re-subscribes.</summary>
    /// <param name="view">The session.</param>
    public void Start(IGameSessionView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        Dispose();
        _view = view;
        _subscriptions.Add(view.Events.Subscribe<ObjectMotionChanged>(OnMotion));
        _subscriptions.Add(view.Events.Subscribe<PlayerTeleportAnnounced>(OnTeleport));
        _subscriptions.Add(view.Events.Subscribe<ObjectMoved>(OnMoved));
        _subscriptions.Add(view.Events.Subscribe<SelfTravelChanged>(OnTravel));
        _subscriptions.Add(view.Events.Subscribe<ObjectCreated>(OnCreated));
    }

    /// <summary>Unsubscribes; the counters stay readable.</summary>
    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _view = null;
    }

    /// <summary>Takes one frame.</summary>
    /// <param name="deltaSeconds">The engine's delta, in seconds: the previous frame's whole time.</param>
    /// <param name="skip">True for a frame to leave out (the one after a saved review frame).</param>
    /// <returns>True when this frame set a new maximum (the caller keeps the frame's parts).</returns>
    public bool Step(double deltaSeconds, bool skip)
    {
        var fresh = _fresh;
        _fresh = false;
        var setMax = false;
        if (_open && !fresh && !skip && deltaSeconds > 0 && !double.IsNaN(deltaSeconds))
        {
            _frames++;
            var ms = deltaSeconds * 1000.0;
            if (double.IsNaN(_maxMs) || ms > _maxMs)
            {
                _maxMs = ms;
                setMax = true;
            }
        }

        if (_open && _tail > 0 && !fresh && --_tail == 0)
        {
            _open = false;
        }

        return setMax;
    }

    private void OnMotion(in ObjectMotionChanged e)
    {
        if (!e.IsSelf || e.Motion.Forward != WorldObjectTable.DeadMotionLowWord)
        {
            return;
        }

        DeadMotions++;
        _awaitingPortal = true;
        _awaitingArrival = false;
        _awaitingPresent = false;
        if (!_open)
        {
            _windows++;
        }

        _open = true;
        _tail = 0;
        _fresh = true;
    }

    private void OnTeleport(in PlayerTeleportAnnounced e)
    {
        if (!_awaitingPortal)
        {
            return;
        }

        _awaitingPortal = false;
        _awaitingArrival = true;
        Portals++;
    }

    private void OnMoved(in ObjectMoved e)
    {
        if (!_awaitingArrival || !e.IsSelf || e.Verdict != PositionVerdict.SelfTeleport)
        {
            return;
        }

        _awaitingArrival = false;
        _awaitingPresent = true; // the position came with the F751 (one ACE call): portal space runs on until the player is present
        Arrivals++;
    }

    private void OnTravel(in SelfTravelChanged e)
    {
        if (!_awaitingPresent || e.Travel != SelfTravel.Present)
        {
            return;
        }

        _awaitingPresent = false;
        _tail = TailFrames + 1; // the call that saw the player present counts (its delta is the last frame of portal space), then the frame the player materialised in and the first frame after it
    }

    private void OnCreated(in ObjectCreated e)
    {
        if (_view is null || e.IsSelf || !_view.State.Objects.TryGet(e.Guid, out var obj) || (obj.DescriptionFlags & WorldObjectTable.CorpseFlag) == 0)
        {
            return;
        }

        var name = _view.State.Self.Name;
        var corpse = obj.Name.AsSpan();
        if (name is null || !corpse.StartsWith(CorpsePrefix, StringComparison.Ordinal) || !corpse[CorpsePrefix.Length..].SequenceEqual(name))
        {
            return;
        }

        if (!_awaitingPortal && !_awaitingArrival)
        {
            StaleCorpses++; // not this death's: the den keeps the earlier runs' corpses and re-creates them at every arrival
            return;
        }

        Corpses++;
        CorpseBeforePortal = _awaitingPortal;
    }
}
