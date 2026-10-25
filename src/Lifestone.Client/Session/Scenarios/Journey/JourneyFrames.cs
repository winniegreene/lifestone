using System.Globalization;
using Lifestone.Client.Diagnostics;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// What a journey's frame observer takes beyond the one frame at each station's end (M4 P5b J5, the seam J7 builds <c>--journey-shots</c> on). Off by default:
/// the hero hook turns it on and adds the showpiece events to the stations (<see cref="IJourneyObserver.StationEvent"/>).
/// </summary>
/// <param name="Enabled">True when a station's named events (<c>frost-arrow</c>, <c>ring-of-fire</c>, <c>portal-recall</c>) each ask for a frame.</param>
/// <param name="MaxPerStation">The most event bursts one station may ask for.</param>
/// <param name="BurstFrames">The frames of one burst (J7: <c>--journey-shot-frames</c>); 1 is a single frame named without a number.</param>
/// <param name="StepSeconds">The seconds between a burst's frames (<c>--journey-shot-step</c>).</param>
/// <param name="EventGapSeconds">The least seconds between two bursts of the same event in one station (a long hunt shoots hundreds of arrows: one burst per gap).</param>
public sealed record JourneyShots(bool Enabled = false, int MaxPerStation = 12, int BurstFrames = 1, double StepSeconds = 0.2, double EventGapSeconds = 6.0)
{
    /// <summary>The default: no event frames.</summary>
    public static JourneyShots Off { get; } = new();

    /// <summary>The default burst length when <c>--journey-shots</c> is given.</summary>
    public const int DefaultBurstFrames = 6;

    /// <summary>The most frames one burst may hold (<c>--journey-shot-frames</c>).</summary>
    public const int MaxBurstFrames = 60;

    /// <summary><c>--journey-shots</c>: on, with the default burst.</summary>
    public static JourneyShots OnWithDefaults { get; } = new(true, BurstFrames: DefaultBurstFrames);
}

/// <summary>How a requested frame came about.</summary>
public enum JourneyFrameKind
{
    /// <summary>The one frame at a station's end (<c>station-&lt;name&gt;.png</c>).</summary>
    StationEnd,

    /// <summary>A named event inside a station (J7's showpieces).</summary>
    Event,
}

/// <summary>One frame the observer asks the Godot host to save.</summary>
/// <param name="Station">The station.</param>
/// <param name="Kind">How it came about.</param>
/// <param name="Name">The frame's name (the suffix of the file, without the extension).</param>
/// <param name="Path">The file's path.</param>
public sealed record JourneyFrameRequest(string Station, JourneyFrameKind Kind, string Name, string Path);

/// <summary>The frame names of a journey (M4 P5b J5): pure, so the naming is tested without Godot.</summary>
public static class JourneyFrameNames
{
    /// <summary>The frame at a station's end: <c>station-&lt;name&gt;</c>.</summary>
    /// <param name="station">The station's name.</param>
    public static string StationEnd(string station) => "station-" + Clean(station);

    /// <summary>A frame taken inside a station by one of today's legs' plans: <c>&lt;station&gt;-&lt;slot&gt;</c> (e.g. <c>hunt-c1-combat</c>, <c>den-fight-bolt</c>, <c>death-corpse</c>, <c>buff-mid-cast</c>).</summary>
    /// <param name="station">The station's name.</param>
    /// <param name="slot">The plan's slot name.</param>
    public static string InStation(string station, string slot) => Clean(station) + "-" + Clean(slot);

    /// <summary>A named event's frame: the same shape as <see cref="InStation"/>.</summary>
    /// <param name="station">The station's name.</param>
    /// <param name="name">The event's name.</param>
    public static string Event(string station, string name) => InStation(station, name);

    /// <summary>One frame of a showpiece burst: <c>&lt;station&gt;-&lt;event&gt;-&lt;k&gt;</c> with k from 1 (a one-frame burst has no number: <see cref="Event"/>).</summary>
    /// <param name="station">The station's name.</param>
    /// <param name="name">The event's name.</param>
    /// <param name="number">The frame's number in the burst, from 1.</param>
    /// <param name="frames">The burst's length.</param>
    public static string Burst(string station, string name, int number, int frames) =>
        frames <= 1 ? Event(station, name) : Event(station, name) + "-" + number.ToString("00", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The file for a frame: <c>&lt;stem&gt;-&lt;name&gt;.png</c> beside the asked path (<c>--play-screenshot</c>), as every leg's plan names its frames.</summary>
    /// <param name="stem">The asked path (any extension).</param>
    /// <param name="name">The frame's name.</param>
    public static string PathFor(string stem, string name) => Path.ChangeExtension(stem, null) + "-" + name + ".png";

    /// <summary>The slot names a station takes frames for inside itself, in the plan's order; empty for a station with none (its end frame only).</summary>
    /// <param name="station">The station's name.</param>
    public static IReadOnlyList<string> SlotsOf(string station) => station switch
    {
        JourneyStationNames.Hunt => HuntUiShotPlan.Names,
        JourneyStationNames.Vendor => [HuntUiShotPlan.Names[HuntUiShotPlan.Vendor]],
        JourneyStationNames.DenFight => FightShotNames,
        JourneyStationNames.Death => DeathShotPlan.Names,
        JourneyStationNames.Buff => [BuffMidCast],
        _ => [],
    };

    /// <summary>The buff's frame, taken mid-cast.</summary>
    public const string BuffMidCast = "mid-cast";

    /// <summary>The fight's three frames (<c>--login-check=bsd-fight</c>'s bolt, hit and kill).</summary>
    public static readonly string[] FightShotNames = ["bolt", "hit", "kill"];

    private static string Clean(string text) => text.Trim().ToLowerInvariant().Replace(' ', '-');
}

/// <summary>A station run that wraps one of today's scenarios and shows it (J5: the Godot host drives the legs' review-frame plans from it).</summary>
public interface IStationInner
{
    /// <summary>The wrapped scenario once the station has started it, else null.</summary>
    IScenario? Scenario { get; }
}

/// <summary>The station names the frame plans know (the same strings as <see cref="StationKinds"/>).</summary>
public static class JourneyStationNames
{
    /// <summary>The buff station.</summary>
    public const string Buff = StationKinds.Buff;

    /// <summary>The hunt station.</summary>
    public const string Hunt = StationKinds.Hunt;

    /// <summary>The vendor station.</summary>
    public const string Vendor = StationKinds.Vendor;

    /// <summary>The den fight station.</summary>
    public const string DenFight = StationKinds.DenFight;

    /// <summary>The death station.</summary>
    public const string Death = StationKinds.Death;
}

/// <summary>
/// The journey's observer for the Godot host (M4 P5b J5): turns the journey's station callbacks into frame requests (<c>station-&lt;name&gt;.png</c> at each station's end,
/// event frames when <see cref="JourneyShots.Enabled"/>) and keeps the per-station frame times, reported as today's samplers report theirs. Game thread only; the host
/// dequeues requests with <see cref="TryDequeue"/> and saves them.
/// </summary>
public sealed class JourneyFrameObserver : IJourneyObserver
{
    private readonly string? _stem;
    private readonly JourneyShots _shots;
    private readonly ILogSink _log;
    private readonly Queue<JourneyFrameRequest> _pending = new();
    private readonly Dictionary<string, int> _events = [];
    private readonly Dictionary<string, StationFrames> _frames = [];
    private readonly List<string> _requested = [];
    private readonly List<Burst> _bursts = [];
    private readonly Dictionary<string, double> _lastBurst = [];

    /// <summary>An observer.</summary>
    /// <param name="stem">The asked review-frame path (<c>--play-screenshot</c>), or null when no frames are asked for (then nothing is requested; the frame times are still kept).</param>
    /// <param name="shots">The seam for J7; null is <see cref="JourneyShots.Off"/>.</param>
    /// <param name="log">The log.</param>
    public JourneyFrameObserver(string? stem, JourneyShots? shots, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(log);
        (_stem, _shots, _log) = (stem, shots ?? JourneyShots.Off, log);
    }

    /// <summary>The station that is running, or null between stations.</summary>
    public string? Current { get; private set; }

    /// <summary>The event options.</summary>
    public JourneyShots Shots => _shots;

    /// <summary>True when frames are asked for.</summary>
    public bool FramesAsked => _stem is not null;

    /// <summary>Every frame name requested so far, in order (the log and the tests read it).</summary>
    public IReadOnlyList<string> Requested => _requested;

    /// <summary>The frame requests waiting to be saved.</summary>
    public int Pending => _pending.Count;

    /// <inheritdoc />
    public void StationStarting(string station, double now)
    {
        Current = station;
        _events[station] = 0;
        _frames[station] = new StationFrames();
    }

    /// <inheritdoc />
    public void StationEnded(string station, bool ok, double now)
    {
        if (Current == station)
        {
            Current = null;
        }

        Request(station, JourneyFrameKind.StationEnd, JourneyFrameNames.StationEnd(station));
    }

    /// <inheritdoc />
    public void StationEvent(string station, string name, double now)
    {
        if (!_shots.Enabled)
        {
            return;
        }

        var taken = _events.GetValueOrDefault(station);
        var key = station + "/" + name;
        if (_lastBurst.TryGetValue(key, out var last) && now - last < _shots.EventGapSeconds)
        {
            return; // the same showpiece again within the gap: one burst per gap
        }

        if (taken >= _shots.MaxPerStation)
        {
            _log.Info("journey", $"journey frames: station {station} asked for more than {_shots.MaxPerStation} event frames; '{name}' is not taken");
            return;
        }

        _events[station] = taken + 1;
        _lastBurst[key] = now;
        if (_stem is null)
        {
            return;
        }

        if (_shots.BurstFrames <= 1)
        {
            Request(station, JourneyFrameKind.Event, JourneyFrameNames.Event(station, name));
            return;
        }

        _bursts.Add(new Burst(station, name, Math.Max(1, _shots.BurstFrames)));
    }

    /// <summary>
    /// Advances the observer's clock (the host's run clock, once a frame, before it dequeues): a burst's frames become due at the clock of the first tick after the event plus the step times their number, so the cadence is the host's own and the naming
    /// needs no engine. Frames of a burst are named <see cref="JourneyFrameNames.Burst"/> and requested as <see cref="JourneyFrameKind.Event"/>.
    /// </summary>
    /// <param name="now">The host's run clock, in seconds.</param>
    public void Tick(double now)
    {
        for (var i = _bursts.Count - 1; i >= 0; i--)
        {
            var burst = _bursts[i];
            if (double.IsNaN(burst.StartedAt))
            {
                burst.StartedAt = now;
            }

            while (burst.Next < burst.Frames && now >= burst.StartedAt + (burst.Next * _shots.StepSeconds))
            {
                burst.Next++;
                Request(burst.Station, JourneyFrameKind.Event, JourneyFrameNames.Burst(burst.Station, burst.Name, burst.Next, burst.Frames));
                break; // at most one frame of a burst a tick: a save stalls the frame after it
            }

            if (burst.Next >= burst.Frames)
            {
                _bursts.RemoveAt(i);
            }
        }
    }

    /// <summary>The bursts still owing frames.</summary>
    public int BurstsOpen => _bursts.Count;

    private sealed class Burst(string station, string name, int frames)
    {
        public string Station { get; } = station;

        public string Name { get; } = name;

        public int Frames { get; } = frames;

        public int Next { get; set; }

        public double StartedAt { get; set; } = double.NaN;
    }

    /// <summary>Records a frame the host took itself inside a station (its name, as <see cref="JourneyFrameNames.InStation"/> gives it).</summary>
    /// <param name="name">The frame's name.</param>
    public void NoteTaken(string name) => _requested.Add(name);

    /// <summary>Takes the next frame to save, or false when none waits.</summary>
    /// <param name="request">The request.</param>
    public bool TryDequeue(out JourneyFrameRequest request) => _pending.TryDequeue(out request!);

    /// <summary>Records one rendered frame of the running station (<paramref name="skip"/>: the frame after a saved review frame, whose read-back stalls it, is left out).</summary>
    /// <param name="deltaSeconds">The previous frame's length.</param>
    /// <param name="skip">True to leave the frame out.</param>
    public void Frame(double deltaSeconds, bool skip)
    {
        if (skip || Current is not { } station || !_frames.TryGetValue(station, out var frames))
        {
            return;
        }

        frames.Count++;
        frames.MaxMs = Math.Max(frames.MaxMs, deltaSeconds * 1000.0);
    }

    /// <summary>The frame-time fields of a station for the verdict (report-only; <c>frames=contended</c> is the parallel phase's flag, set by smoke): <c>frame_max_ms=12.3 frames=200</c>, or <c>frame_max_ms=n/a frames=0</c>.</summary>
    /// <param name="station">The station's name.</param>
    public string FrameFields(string station) =>
        _frames.TryGetValue(station, out var f) && f.Count > 0
            ? string.Create(CultureInfo.InvariantCulture, $"frame_max_ms={f.MaxMs:0.0} frames={f.Count}")
            : "frame_max_ms=n/a frames=0";

    private void Request(string station, JourneyFrameKind kind, string name)
    {
        if (_stem is null)
        {
            return;
        }

        _requested.Add(name);
        _pending.Enqueue(new JourneyFrameRequest(station, kind, name, JourneyFrameNames.PathFor(_stem, name)));
    }

    private sealed class StationFrames
    {
        public long Count { get; set; }

        public double MaxMs { get; set; }
    }
}
