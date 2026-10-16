using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.Client.Audio;

/// <summary>
/// The ambient player's terrain source (M3 1.3 fix A, 1.3-R1 (d)): a landblock's terrain words are read from the dats on a pool thread, never on the frame
/// thread. <see cref="Get"/> returns what is loaded and, for a landblock not asked for yet, queues one read and returns null; <see cref="Revision"/> goes
/// up when a read finishes, so the player re-scans (it only scans when the listener's cell changes, as retail does) with the block it was missing.
/// </summary>
/// <remarks>
/// Frame thread for <see cref="Get"/> and <see cref="Revision"/>; the reads run on the thread pool and are serialised by one lock only for the dictionary, never
/// around the dat read. With <paramref name="background"/> false (the engine check, which has no frames to wait over) the read runs inside <see cref="Get"/>.
/// At most <see cref="Limit"/> landblocks are kept.
/// </remarks>
public class BackgroundAmbientTerrain(Func<LandblockCoord, LandblockTerrain?> read, bool background = true) : IAmbientTerrain
{
    /// <summary>Landblocks kept before the cache is cleared (nine are in use at a time).</summary>
    public const int Limit = 64;

    private readonly Func<LandblockCoord, LandblockTerrain?> _read = read ?? throw new ArgumentNullException(nameof(read));
    private readonly object _gate = new();
    private readonly Dictionary<LandblockCoord, LandblockTerrain?> _blocks = [];
    private readonly HashSet<LandblockCoord> _pending = [];
    private int _revision;
    private int _readsOnCallerThread;

    /// <inheritdoc/>
    public int Revision => Volatile.Read(ref _revision);

    /// <summary>Reads that ran inside <see cref="Get"/> (always 0 in the background mode; the engine check's synchronous mode counts them).</summary>
    public int ReadsOnCallerThread => Volatile.Read(ref _readsOnCallerThread);

    /// <summary>Reads started (one per landblock asked for, again after a clear).</summary>
    public int ReadsStarted { get; private set; }

    /// <summary>Reads finished.</summary>
    public int ReadsFinished => Volatile.Read(ref _finished);

    private int _finished;
    private int _failures;

    /// <summary>Reads that threw (their landblocks have no ambient sound).</summary>
    public int Failures => Volatile.Read(ref _failures);

    /// <inheritdoc/>
    public LandblockTerrain? Get(LandblockCoord coord)
    {
        lock (_gate)
        {
            if (_blocks.TryGetValue(coord, out var terrain))
            {
                return terrain;
            }

            if (!_pending.Add(coord))
            {
                return null; // its read is on its way
            }

            ReadsStarted++;
        }

        if (!background)
        {
            Interlocked.Increment(ref _readsOnCallerThread);
            return Complete(coord);
        }

        _ = Task.Run(() => Complete(coord));
        return null;
    }

    private LandblockTerrain? Complete(LandblockCoord coord)
    {
        LandblockTerrain? terrain = null;
        try
        {
            terrain = _read(coord);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException)
        {
            // A landblock that cannot be read has no ambient sound; kept as "no terrain" so it is not retried every scan, and counted (the node logs the count).
            Interlocked.Increment(ref _failures);
        }

        lock (_gate)
        {
            if (_blocks.Count >= Limit)
            {
                _blocks.Clear();
            }

            _blocks[coord] = terrain;
            _pending.Remove(coord);
        }

        // M3 2.2 fix B (2.2-R10): the revision is published before the count, so a reader that sees the read finished sees the new revision too.
        Interlocked.Increment(ref _revision);
        Interlocked.Increment(ref _finished);
        return terrain;
    }
}
