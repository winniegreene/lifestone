using Lifestone.Client.World;
using Lifestone.Data.Terrain;

namespace Lifestone.Client.Streaming;

/// <summary>Where the streaming focus (the camera or player) is.</summary>
public enum StreamingFocusKind
{
    /// <summary>Outside, or inside a building cell that can see outside: the landscape ring is loaded.</summary>
    Outdoor,

    /// <summary>
    /// In an EnvCell that cannot see outside (a dungeon): only <see cref="StreamingSettings.IndoorLoadRadius"/> rings
    /// around the focus landblock are kept. Retail does not draw the landscape from such cells.
    /// </summary>
    Indoor,
}

/// <summary>The streaming focus: a global position and whether it is outdoors.</summary>
public readonly record struct StreamingFocus(GlobalPosition Position, StreamingFocusKind Kind = StreamingFocusKind.Outdoor);

/// <summary>Streaming radii and limits.</summary>
public sealed record StreamingSettings
{
    /// <summary>
    /// Retail default landscape radius: <c>mid_radius</c> = 8 (acclient.c:45608), used by
    /// <c>SmartBox::SetRegion</c> → <c>SmartBox::set_mid_radius</c> → <c>LScape::SetMidRadius</c>, which builds a
    /// square grid of <c>2r + 1</c> landblocks (<c>mid_width</c>) around the viewer's block. The overall-quality
    /// presets set 3, 5, 8, 11 or 15 (<c>Render::SetOverallGraphicsQuality</c>, acclient.c:378753-378793); safe render
    /// settings set 8. (ACE's server-side port defaults to 5, <c>LScape.MidRadius</c>.)
    /// </summary>
    public const int RetailDefaultLoadRadius = 8;

    /// <summary>Rings of landblocks to load around the focus landblock (Chebyshev distance), i.e. a (2r+1)² square.</summary>
    public int LoadRadius { get; init; } = RetailDefaultLoadRadius;

    /// <summary>Extra rings a loaded landblock may drift out before it is unloaded (hysteresis against thrashing).</summary>
    public int UnloadHysteresis { get; init; } = 1;

    /// <summary>Rings kept around the focus landblock while <see cref="StreamingFocusKind.Indoor"/>.</summary>
    public int IndoorLoadRadius { get; init; }

    /// <summary>Most landblocks preparing at once; new loads start only when one finishes.</summary>
    public int MaxInFlight { get; init; } = 8;

    /// <summary>Throws when a value is out of range.</summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(LoadRadius);
        ArgumentOutOfRangeException.ThrowIfNegative(UnloadHysteresis);
        ArgumentOutOfRangeException.ThrowIfNegative(IndoorLoadRadius);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxInFlight);
    }
}

/// <summary>What the planner tracks for a landblock.</summary>
public enum LandblockLoadState
{
    /// <summary>A load was started and has not completed.</summary>
    Loading,

    /// <summary>The landblock is attached.</summary>
    Loaded,

    /// <summary>The load failed; it is not retried until the landblock leaves the unload radius and comes back.</summary>
    Failed,
}

/// <summary>One planning step's actions. Every list is in nearest-first order.</summary>
/// <param name="ToLoad">Start loading these (now tracked as <see cref="LandblockLoadState.Loading"/>).</param>
/// <param name="ToCancel">Cancel these in-flight loads: they left the unload radius (no longer tracked).</param>
/// <param name="ToUnload">Detach these loaded landblocks: they left the unload radius (no longer tracked).</param>
public sealed record StreamingPlan(
    IReadOnlyList<LandblockCoord> ToLoad,
    IReadOnlyList<LandblockCoord> ToCancel,
    IReadOnlyList<LandblockCoord> ToUnload)
{
    public static readonly StreamingPlan Empty = new([], [], []);

    public bool IsEmpty => ToLoad.Count == 0 && ToCancel.Count == 0 && ToUnload.Count == 0;
}

/// <summary>
/// Decides which landblocks to load and unload around a moving focus: square rings of landblocks (retail's
/// <c>mid_width</c> x <c>mid_width</c> grid) with a load radius, an unload radius one or more rings further out
/// (hysteresis), nearest-first priority, an in-flight limit, and cancellation of loads that went stale.
/// </summary>
/// <remarks>
/// <para>Pure bookkeeping, no threads and no I/O: the caller starts and cancels the work and reports completion with
/// <see cref="MarkLoaded"/> or <see cref="MarkFailed"/>. Not thread-safe; drive it from one thread (the main thread).</para>
/// <para><b>Priority.</b> Candidates are ordered by the distance from the focus point to the landblock's square (0 for
/// the landblock under the focus), then by the distance to its centre, then by (X, Y), so the order is deterministic.
/// </para>
/// <para><b>Map edge.</b> Only landblocks 0x00-0xFE on each axis exist (65,025 = 255², <c>LandDefs::in_bounds</c>);
/// rings are clipped there.</para>
/// </remarks>
public sealed class StreamingPlanner
{
    private readonly Dictionary<LandblockCoord, LandblockLoadState> _states = new();
    private readonly List<(LandblockCoord Coord, double Edge, double Centre)> _candidates = new();

    public StreamingPlanner(StreamingSettings? settings = null)
    {
        Settings = settings ?? new StreamingSettings();
        Settings.Validate();
    }

    public StreamingSettings Settings { get; }

    /// <summary>The focus of the last <see cref="Update"/>, if any.</summary>
    public StreamingFocus? Focus { get; private set; }

    /// <summary>The landblock under the last focus (clamped to the map).</summary>
    public LandblockCoord FocusLandblock { get; private set; }

    /// <summary>Loads started and not yet completed.</summary>
    public int InFlightCount { get; private set; }

    /// <summary>Attached landblocks.</summary>
    public int LoadedCount { get; private set; }

    /// <summary>Wanted landblocks not yet started (waiting for an in-flight slot) after the last update.</summary>
    public int PendingCount { get; private set; }

    /// <summary>Landblocks inside the current load radius.</summary>
    public int DesiredCount { get; private set; }

    /// <summary>True when nothing is pending or in flight.</summary>
    public bool IsIdle => PendingCount == 0 && InFlightCount == 0;

    /// <summary>The tracked state of a landblock, or null when untracked.</summary>
    public LandblockLoadState? StateOf(LandblockCoord coord) => _states.TryGetValue(coord, out var s) ? s : null;

    /// <summary>Every tracked landblock and its state (a snapshot).</summary>
    public IReadOnlyDictionary<LandblockCoord, LandblockLoadState> Snapshot() => new Dictionary<LandblockCoord, LandblockLoadState>(_states);

    /// <summary>The load radius that applies to a focus kind.</summary>
    public int LoadRadiusFor(StreamingFocusKind kind) => kind == StreamingFocusKind.Indoor ? Settings.IndoorLoadRadius : Settings.LoadRadius;

    /// <summary>
    /// Plans one step for <paramref name="focus"/>: unloads/cancels what left the unload radius and starts the nearest
    /// wanted landblocks while in-flight slots are free.
    /// </summary>
    public StreamingPlan Update(StreamingFocus focus)
    {
        Focus = focus;
        var center = AcCoordinates.ClampedLandblockAt(focus.Position);
        FocusLandblock = center;
        var loadRadius = LoadRadiusFor(focus.Kind);
        var unloadRadius = loadRadius + Settings.UnloadHysteresis;

        List<LandblockCoord>? toCancel = null;
        List<LandblockCoord>? toUnload = null;
        List<LandblockCoord>? forget = null;
        foreach (var (coord, state) in _states)
        {
            if (Ring(coord, center) <= unloadRadius)
            {
                continue;
            }

            (forget ??= []).Add(coord);
            if (state == LandblockLoadState.Loading)
            {
                (toCancel ??= []).Add(coord);
            }
            else if (state == LandblockLoadState.Loaded)
            {
                (toUnload ??= []).Add(coord);
            }
        }

        if (forget is not null)
        {
            foreach (var coord in forget)
            {
                Forget(coord);
            }
        }

        // Candidates: untracked landblocks inside the load radius, nearest first.
        _candidates.Clear();
        var desired = 0;
        for (var x = center.X - loadRadius; x <= center.X + loadRadius; x++)
        {
            for (var y = center.Y - loadRadius; y <= center.Y + loadRadius; y++)
            {
                if (x is < 0 or >= AcCoordinates.LandblocksPerSide || y is < 0 or >= AcCoordinates.LandblocksPerSide)
                {
                    continue;
                }

                desired++;
                var coord = new LandblockCoord((byte)x, (byte)y);
                if (_states.ContainsKey(coord))
                {
                    continue;
                }

                _candidates.Add((coord, EdgeDistanceSquared(focus.Position, coord), CentreDistanceSquared(focus.Position, coord)));
            }
        }

        DesiredCount = desired;
        _candidates.Sort(static (a, b) =>
        {
            var c = a.Edge.CompareTo(b.Edge);
            if (c == 0)
            {
                c = a.Centre.CompareTo(b.Centre);
            }

            if (c == 0)
            {
                c = a.Coord.X.CompareTo(b.Coord.X);
            }

            return c != 0 ? c : a.Coord.Y.CompareTo(b.Coord.Y);
        });

        var slots = Math.Max(0, Settings.MaxInFlight - InFlightCount);
        var start = Math.Min(slots, _candidates.Count);
        var toLoad = new List<LandblockCoord>(start);
        for (var i = 0; i < start; i++)
        {
            var coord = _candidates[i].Coord;
            _states[coord] = LandblockLoadState.Loading;
            InFlightCount++;
            toLoad.Add(coord);
        }

        PendingCount = _candidates.Count - start;
        if (toLoad.Count == 0 && toCancel is null && toUnload is null)
        {
            return StreamingPlan.Empty;
        }

        return new StreamingPlan(toLoad, Sorted(toCancel, focus.Position), Sorted(toUnload, focus.Position));
    }

    /// <summary>Reports that a load finished and the landblock is attached.</summary>
    /// <returns>False when the landblock is no longer wanted (cancelled or unloaded meanwhile): discard the result.</returns>
    public bool MarkLoaded(LandblockCoord coord)
    {
        if (!_states.TryGetValue(coord, out var state) || state != LandblockLoadState.Loading)
        {
            return false;
        }

        _states[coord] = LandblockLoadState.Loaded;
        InFlightCount--;
        LoadedCount++;
        return true;
    }

    /// <summary>Reports that a load failed; the landblock is not retried while it stays in range.</summary>
    /// <returns>False when the landblock was no longer tracked.</returns>
    public bool MarkFailed(LandblockCoord coord)
    {
        if (!_states.TryGetValue(coord, out var state) || state != LandblockLoadState.Loading)
        {
            return false;
        }

        _states[coord] = LandblockLoadState.Failed;
        InFlightCount--;
        return true;
    }

    /// <summary>Forgets every landblock (e.g. on teleport); returns the loaded and in-flight ones so they can be torn down.</summary>
    public (IReadOnlyList<LandblockCoord> Loaded, IReadOnlyList<LandblockCoord> InFlight) Reset()
    {
        var loaded = _states.Where(p => p.Value == LandblockLoadState.Loaded).Select(p => p.Key).ToList();
        var inFlight = _states.Where(p => p.Value == LandblockLoadState.Loading).Select(p => p.Key).ToList();
        _states.Clear();
        InFlightCount = 0;
        LoadedCount = 0;
        PendingCount = 0;
        return (loaded, inFlight);
    }

    /// <summary>
    /// Priority of a landblock for the current focus (smaller = sooner): squared metres from the focus point to the
    /// landblock's square. Use it to order main-thread attach work.
    /// </summary>
    public double PriorityOf(LandblockCoord coord) =>
        Focus is { } focus ? EdgeDistanceSquared(focus.Position, coord) : 0.0;

    /// <summary>Chebyshev ring of <paramref name="coord"/> around <paramref name="center"/>.</summary>
    public static int Ring(LandblockCoord coord, LandblockCoord center) =>
        Math.Max(Math.Abs(coord.X - center.X), Math.Abs(coord.Y - center.Y));

    /// <summary>Squared horizontal distance from a point to a landblock's square (0 inside it).</summary>
    public static double EdgeDistanceSquared(GlobalPosition position, LandblockCoord coord)
    {
        var minX = coord.X * AcCoordinates.LandblockLength;
        var minY = coord.Y * AcCoordinates.LandblockLength;
        var dx = Math.Max(Math.Max(minX - position.X, 0.0), position.X - (minX + AcCoordinates.LandblockLength));
        var dy = Math.Max(Math.Max(minY - position.Y, 0.0), position.Y - (minY + AcCoordinates.LandblockLength));
        return (dx * dx) + (dy * dy);
    }

    private static double CentreDistanceSquared(GlobalPosition position, LandblockCoord coord)
    {
        var dx = ((coord.X + 0.5) * AcCoordinates.LandblockLength) - position.X;
        var dy = ((coord.Y + 0.5) * AcCoordinates.LandblockLength) - position.Y;
        return (dx * dx) + (dy * dy);
    }

    private void Forget(LandblockCoord coord)
    {
        if (!_states.Remove(coord, out var state))
        {
            return;
        }

        if (state == LandblockLoadState.Loading)
        {
            InFlightCount--;
        }
        else if (state == LandblockLoadState.Loaded)
        {
            LoadedCount--;
        }
    }

    private static IReadOnlyList<LandblockCoord> Sorted(List<LandblockCoord>? coords, GlobalPosition focus)
    {
        if (coords is null)
        {
            return [];
        }

        coords.Sort((a, b) =>
        {
            var c = EdgeDistanceSquared(focus, a).CompareTo(EdgeDistanceSquared(focus, b));
            if (c == 0)
            {
                c = a.X.CompareTo(b.X);
            }

            return c != 0 ? c : a.Y.CompareTo(b.Y);
        });
        return coords;
    }
}
