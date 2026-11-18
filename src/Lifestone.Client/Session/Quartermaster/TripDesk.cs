using System.Globalization;
using System.Runtime.CompilerServices;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Quartermaster;

/// <summary>
/// M6 item 16, slice E5d (docs/automation-engine.md section 2.5; m6-plan.md D139, D78, D105, D123): the quartermaster's trip desk, one a session.
/// <para><b>The one set of counters a ground</b> (<see cref="On"/>). Whether she goes to town, and for what, was worked out by a <see cref="LogisticsCounters"/> built at seven places with different arguments: the outer loop's
/// (asked every second of a fight), the fight's own for its floors, the logistics station's, the town trip's, the buy-trip's and the panel's two. The desk builds the counters once for a ground of a session, and every site asks
/// it. What the seven read differently, and what became of each:</para>
/// <list type="bullet">
/// <item><b>The floor-only lines</b> (D148: a carried line only the war options burn drops those spells at its floor instead of stopping the run). The outer loop, the fight, the logistics station and the buy-trip each stated
/// them from their own reading of the foci she holds; the town trip and the panel's own counters stated none, so the same pack with the den's Diamond Scarab under its stop line read "all well" in the field and
/// <c>stop:Diamond Scarab</c> at home. Now one statement a ground: the job's (the outer loop's) stands once made; without a job the last station's or fight's does, and a statement that differs from the one standing is said by
/// name, never applied in silence. A site that knows none (the town trip, the panel) states none and reads what stands.</item>
/// <item><b>The order lines</b> (the learn plan's lines a job adds: the den run's travel spells, a quest's bolt line). The outer loop set them on its own counters alone, so a trip it called for one of those scrolls was counted
/// again by the station without them. They are the session's counters' now, read by every site: the station counts the learn the trigger called the trip for, and a trip that buys no scroll holds that learn's trigger, not another's.</item>
/// <item><b>The buff cycles and the mark of the last trip</b> (the schedule, D96). Only the outer loop counts cycles; it marks the trip's end on the session's counters and states its own mark again when it is rebuilt, as its
/// own counters did. A station passes no count and never reads the schedule, as before: the schedule's trip is the outer loop's call, and the outer loop forces the station for it.</item>
/// <item><b>The learn holds</b>. The logistics station could be given its own (a test's) while the town trip read the process's; the session's counters hold one set, stated at their build.</item>
/// <item><b>The visit's cut keeps</b> (<see cref="LogisticsCounters.CutKeep"/>). Deliberate: the town trip buys to the keeps its burden budget allows, the fight keeps the profile's. The town trip's own counters died with the
/// trip; on the session's the cut ends when the visit's buy list is made (<see cref="LogisticsCounters.UncutKeeps"/>).</item>
/// <item><b>The sale estimate</b> (M6 16 E5c2): the rule book's answer over the session's context (<see cref="SaleEstimate"/>), the same for every set and its reading twin; the shadow E5c put beside the outer loop's
/// estimate went with the estimate's own list.</item>
/// </list>
/// <para><b>A read that moves nothing</b> (<see cref="Reading"/>). <see cref="LogisticsCounters.Check"/> writes what it counted, and releases a learn hold the purse has outgrown; the panel's read and the audit's went through
/// the outer loop's own counters and moved them. They, and the fight's floor read, ask a reading twin: a second set built by the same line with the same statement, marked <see cref="LogisticsCounters.ReadOnly"/> (a hold is
/// asked, never released), with no trip mark. It shares no count with the counters that decide, so no read changes what the trigger reads next.</para>
/// </summary>
public sealed partial class TripDesk
{
    private static readonly ConditionalWeakTable<GameState, TripDesk> PerSession = new();
    private readonly GameState _state;
    private readonly Dictionary<HuntingGround, Entry> _grounds = new(ReferenceEqualityComparer.Instance);

    private TripDesk(GameState state) => _state = state;

    private sealed class Entry(LogisticsCounters deciding, uint[] floorOnly, LearnHolds? holds)
    {
        public LogisticsCounters Deciding { get; } = deciding;

        public LogisticsCounters? Reading { get; set; }

        public uint[] FloorOnly { get; set; } = floorOnly;

        public LearnHolds? Holds { get; } = holds;

        public bool JobStated { get; set; }
    }

    /// <summary>The trip desk of a session, made on first use.</summary>
    /// <param name="state">The session's read model.</param>
    public static TripDesk For(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return PerSession.GetValue(state, static s => new TripDesk(s));
    }

    /// <summary>The one construction of the counters: the deciding set and its reading twin, from one statement.</summary>
    private static LogisticsCounters Build(HuntingGround ground, uint[] floorOnly, LearnHolds? holds, bool readOnly) => new LogisticsCounters(ground, floorOnly, holds) { ReadOnly = readOnly };

    private static uint[] Sorted(IEnumerable<uint>? floorOnly)
    {
        uint[] set = floorOnly is null ? [] : [.. floorOnly.Distinct()];
        Array.Sort(set);
        return set;
    }

    private static string Words(uint[] set) => set.Length == 0 ? "none" : string.Join(", ", set.Select(w => w.ToString(CultureInfo.InvariantCulture)));

    /// <summary>
    /// The counters a job states before the session's read model is known (a scenario is built before its first step): the same construction, with its floor-only lines. The desk adopts them at the job's first step
    /// (<see cref="Adopt"/>): they become the session's, or give way to the session's when the ground has a set already.
    /// </summary>
    /// <param name="ground">The job's ground.</param>
    /// <param name="floorOnly">The floor-only lines the job reads (<see cref="ComponentFloor.FloorOnly"/>).</param>
    public static LogisticsCounters Stated(HuntingGround ground, IEnumerable<uint> floorOnly)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(floorOnly);
        return Build(ground, Sorted(floorOnly), null, readOnly: false);
    }

    /// <summary>
    /// The job (the outer loop) states itself at its first step of a session: the session's counters over <paramref name="ground"/> are returned, <paramref name="stated"/> itself when the ground has none yet. The job's
    /// floor-only lines stand from here on (a later statement that differs is named and not applied). The caller states the rest again on what it is given: its order lines and its trip mark.
    /// </summary>
    /// <param name="ground">The job's ground.</param>
    /// <param name="stated">The counters the job held before (<see cref="Stated"/>, or a session's before a reconnect).</param>
    /// <param name="floorOnly">The job's floor-only lines.</param>
    /// <param name="note">Told what the statement changed, in words; null: not told.</param>
    public LogisticsCounters Adopt(HuntingGround ground, LogisticsCounters stated, IEnumerable<uint> floorOnly, Action<string>? note = null)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(stated);
        ArgumentNullException.ThrowIfNull(floorOnly);
        var set = Sorted(floorOnly);
        if (!_grounds.TryGetValue(ground, out var entry))
        {
            _grounds[ground] = entry = new Entry(stated, set, null);
        }
        else if (!entry.FloorOnly.AsSpan().SequenceEqual(set))
        {
            note?.Invoke($"trip: the floor-only lines of {ground.Key} are the job's now: {Words(set)} (they stood at {Words(entry.FloorOnly)}, as read before the job stated its own)");
            Restate(entry, set);
        }

        entry.JobStated = true;
        return entry.Deciding;
    }

    /// <summary>
    /// The session's counters over <paramref name="ground"/>: the one set that decides, built at the first ask. A site that reads the floor-only lines states them (<paramref name="floorOnly"/>): they apply while no job has
    /// stated its own; against a job's statement a difference is named (<c>TODO(engine-trip-floor-statements)</c>) and the job's stands. A site that knows none passes null and reads what stands.
    /// </summary>
    /// <param name="ground">The ground.</param>
    /// <param name="floorOnly">The floor-only lines the asking site reads, or null when it reads none.</param>
    /// <param name="holds">The learn holds a site was given (a test's); null: the process's (<see cref="LearnHolds.Shared"/>). A set built over other holds is built again.</param>
    /// <param name="note">Told what the statement changed or where it differs, in words; null: not told.</param>
    public LogisticsCounters On(HuntingGround ground, IEnumerable<uint>? floorOnly = null, LearnHolds? holds = null, Action<string>? note = null)
    {
        ArgumentNullException.ThrowIfNull(ground);
        if (!_grounds.TryGetValue(ground, out var entry) || (holds is not null && !ReferenceEquals(holds, entry.Holds ?? LearnHolds.Shared)))
        {
            var first = Sorted(floorOnly ?? entry?.FloorOnly);
            _grounds[ground] = entry = new Entry(Build(ground, first, holds, readOnly: false), first, holds);
            return entry.Deciding;
        }

        if (floorOnly is null)
        {
            return entry.Deciding;
        }

        var set = Sorted(floorOnly);
        if (entry.FloorOnly.AsSpan().SequenceEqual(set))
        {
            return entry.Deciding;
        }

        if (entry.JobStated)
        {
            note?.Invoke($"trip: TODO(engine-trip-floor-statements): this site reads the floor-only lines of {ground.Key} as {Words(set)}, the job stated {Words(entry.FloorOnly)}: the job's stand");
            return entry.Deciding;
        }

        note?.Invoke($"trip: the floor-only lines of {ground.Key} are stated again: {Words(set)} (they stood at {Words(entry.FloorOnly)})");
        Restate(entry, set);
        return entry.Deciding;
    }

    private static void Restate(Entry entry, uint[] set)
    {
        entry.FloorOnly = set;
        entry.Deciding.RestateFloorOnly(set);
        entry.Reading?.RestateFloorOnly(set);
    }

    /// <summary>
    /// The reading twin of the session's counters over <paramref name="ground"/> (see the class): a check through it counts the pack and judges it by the same statement and moves nothing of the counters that decide. For the
    /// panel, the audit and the fight's floor read. The order lines follow the deciding set's at each ask.
    /// </summary>
    /// <param name="ground">The ground.</param>
    /// <param name="floorOnly">As <see cref="On"/>: the floor-only lines the asking site reads, or null.</param>
    /// <param name="note">As <see cref="On"/>.</param>
    public LogisticsCounters Reading(HuntingGround ground, IEnumerable<uint>? floorOnly = null, Action<string>? note = null)
    {
        var deciding = On(ground, floorOnly, null, note);
        var entry = _grounds[ground];
        var reading = entry.Reading ??= Build(ground, entry.FloorOnly, entry.Holds, readOnly: true);
        if (!ReferenceEquals(reading.OrderLines, deciding.OrderLines))
        {
            reading.OrderLines = deciding.OrderLines;
        }

        return reading;
    }

    /// <summary>M6 16 E5c2: the session's counters over <paramref name="ground"/> when it has any, else null: a read that must make none (the pack budget read outside a trip, which reads their lines alone).</summary>
    /// <param name="ground">The ground.</param>
    public LogisticsCounters? Standing(HuntingGround ground) => _grounds.TryGetValue(ground, out var entry) ? entry.Deciding : null;

    /// <summary>The floor-only lines standing for <paramref name="ground"/> (sorted), empty when none was stated or the ground was never asked.</summary>
    /// <param name="ground">The ground.</param>
    public IReadOnlyList<uint> FloorOnlyOf(HuntingGround ground) => _grounds.TryGetValue(ground, out var entry) ? entry.FloorOnly : [];
}
