using Lifestone.Client.Diagnostics;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session;

/// <summary>
/// Gives a session's Action API the retail SpellTable (M3 1.2 s4c; a session without it takes the no-table outcome, <c>TODO(m3-cast-no-spell-table)</c>): <c>ActionApi.SpellLookup</c> reads it, so a cast knows whether its spell is a
/// self enchantment, a projectile or neither (<c>CastTracker.Classify</c>), and <c>ActionApi.ProjectileSetupLookup</c> reads the war spells' projectile Setups (the ACE world extract the visuals
/// already use) so a projectile is only taken for the cast's when it wears the spell's model.
/// </summary>
/// <remarks>
/// The table (0x0E00000E, about four thousand spells) is decoded on a worker thread started when the session is given its dats (M3 1.2 fix R5), never on the game thread: a lookup before the
/// load has finished answers null (the spell is "Unknown": the enchantment or a projectile confirms, UseDone 0 alone is Sent, the rule a session without dats has always had), so the first cast
/// never pays the decode in its frame. The load time is logged once at Info. A table that cannot be read is logged once as a WARNING and leaves every cast "Unknown"; the worker catches every
/// exception because nothing could observe one escaping it. <see cref="Loaded"/> completes when both reads have ended (hosts that cast at once, the play-checks and the tests, await it).
/// A lookup is a read of an immutable dictionary published once, so any thread may call it.
/// </remarks>
internal sealed class SpellTableHost
{
    private volatile IReadOnlyDictionary<uint, SpellInfo>? _spells;
    private volatile IReadOnlyDictionary<uint, uint>? _setups;
    private readonly SetsBox _sets = new();

    private SpellTableHost(Func<IReadOnlyDictionary<uint, SpellInfo>> loadSpells, Func<IReadOnlyDictionary<uint, uint>?> loadSetups, ILogSink log, TimeProvider time, SetsBox? sets = null)
    {
        _sets = sets ?? new SetsBox();
        Loaded = Task.Run(() => Load(loadSpells, loadSetups, log, time));
    }

    /// <summary>The SpellTable's equipment sets (set id, then the piece count, then the spells cast), or null before the load or without one (M5 7 D137).</summary>
    internal IReadOnlyDictionary<uint, IReadOnlyDictionary<uint, IReadOnlyList<uint>>>? SpellSets => _spells is null ? null : _sets.Value;

    private sealed class SetsBox
    {
        public volatile IReadOnlyDictionary<uint, IReadOnlyDictionary<uint, IReadOnlyList<uint>>>? Value;
    }

    /// <summary>Completes when the worker has finished (loaded or failed; never faults).</summary>
    internal Task Loaded { get; }

    /// <summary>True once the spells are published.</summary>
    internal bool Ready => _spells is not null;

    /// <summary>The managed thread id the load ran on (0 before it started), for tests.</summary>
    internal int LoadThreadId { get; private set; }

    /// <summary>Starts decoding the SpellTable and the projectile Setups of <paramref name="dats"/> on a worker.</summary>
    internal static SpellTableHost Start(DatSet dats, ILogSink log, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(dats);
        var sets = new SetsBox();
        var host = new SpellTableHost(
            () =>
            {
                var table = SpellTableData.Load(dats);
                sets.Value = table.SpellSets; // M5 7 D137: the equipment sets the examine window's set details read (published with the spells)
                return table.Spells;
            },
            LoadProjectileSetups,
            log,
            time,
            sets);
        return host;
    }

    /// <summary>Starts the two loads on a worker (the seam the tests use: neither runs on the calling thread).</summary>
    internal static SpellTableHost Start(Func<IReadOnlyDictionary<uint, SpellInfo>> loadSpells, Func<IReadOnlyDictionary<uint, uint>?> loadSetups, ILogSink log, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(loadSpells);
        ArgumentNullException.ThrowIfNull(loadSetups);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(time);
        return new SpellTableHost(loadSpells, loadSetups, log, time);
    }

    /// <summary>The spell <paramref name="id"/>, or null: not loaded yet, not loadable, or not in the table.</summary>
    internal SpellInfo? Find(uint id) => _spells is { } spells && spells.TryGetValue(id, out var info) ? info : null;

    /// <summary>The Setup of the projectile that spell <paramref name="spellId"/> launches, or null when the world extract has none for it (or is not loaded).</summary>
    internal uint? ProjectileSetup(uint spellId) => _setups is { } setups && setups.TryGetValue(spellId, out var setup) ? setup : null;

    private static Dictionary<uint, uint> LoadProjectileSetups()
    {
        var world = AceWorldExtract.Load();
        return world.ProjectilesBySpell.ToDictionary(p => p.Key, p => p.Value.SetupId);
    }

    private void Load(Func<IReadOnlyDictionary<uint, SpellInfo>> loadSpells, Func<IReadOnlyDictionary<uint, uint>?> loadSetups, ILogSink log, TimeProvider time)
    {
        LoadThreadId = Environment.CurrentManagedThreadId;
        var started = time.GetTimestamp();
        IReadOnlyDictionary<uint, uint>? setups = null;
        try
        {
            setups = loadSetups();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            log.Warn(SessionLog.Category, "TODO(m3-cast-projectile-attribution): the projectile Setups could not be read, so a projectile is not checked against its spell's model: " + e.GetType().Name + ": " + e.Message);
        }

        IReadOnlyDictionary<uint, SpellInfo> spells;
        try
        {
            spells = loadSpells();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            log.Warn(SessionLog.Category, "TODO(m3-cast-no-spell-table): the SpellTable could not be read, so a cast's kind stays Unknown: " + e.GetType().Name + ": " + e.Message);
            return;
        }

        // Published together, after both reads: a lookup before this answers null.
        _setups = setups;
        _spells = spells;
        log.Info(SessionLog.Category, "the SpellTable loaded on a worker in " + (long)time.GetElapsedTime(started).TotalMilliseconds + " ms (" + spells.Count + " spells, " + (setups?.Count ?? 0) + " projectile Setups)");
    }
}
