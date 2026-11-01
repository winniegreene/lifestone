using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Grounds;

/// <summary>
/// The respawn schedule of a hunting ground (M5 item 2 g6, D25, D40, D46): every generator slot of the profile (<see cref="RespawnModel.Slots"/>), standing or dead, and when a dead slot is due back. Any ground's: the
/// generators, their delays and ticks and the slots are profile values (the den's three 900 s generators, Tusker Temple's one 300 s generator).
/// </summary>
/// <remarks>
/// <para><b>A kill</b> is paired to its slot by the creature's spawn (2-R1): when a creature of a slot's weenie class id is created in that slot's cell, the schedule remembers its guid for that slot (the first standing, unclaimed
/// slot of that wcid and cell, in the profile's order; a create that brings a dead slot back claims that slot), and a kill of that guid is that slot's, wherever the creature died (a creature the player hits runs at the player and often
/// dies a room or two from its spawn). Item 0 paired each death with the next new guid, m5-plan.md section 8. <b>The fallback</b>, for a guid the schedule never saw created in a slot's cell (it came into sight elsewhere, or the caller gave
/// none): the first standing, unclaimed slot of the dying creature's wcid in the cell it died in, named in the log ("paired by the cell it died in"). A kill neither rule takes is unpaired and named once; it changes nothing.</para>
/// <para><b>The due time</b> of a dead slot: its death plus ITS generator's delay (<see cref="GeneratorModel.DelaySeconds"/>), rounded up to that generator's next tick (<see cref="GeneratorModel.TickSeconds"/>; ACE's generator
/// checks its slots at each <c>RegenerationInterval</c>, item 0 measured 903-955 s for the den's 900 s with creates in batches at the 60 s tick). The tick's phase is learned per generator from the first create the schedule
/// sees that brings one of its dead slots back (a create of the slot's wcid in the slot's cell, inside the slot's window: at or after its death plus the delay and at most one tick later); until a phase is seen, the due time is
/// the death plus the delay plus one tick, the latest the generator can be, named "phase unknown". A create of a standing slot's wcid (the player coming into sight of it) teaches no phase: those arrive at the teleport,
/// not at a tick.</para>
/// <para><b>Standing again</b>: a create of a dead slot's wcid in its cell at or after its death plus its delay makes the earliest-dead such slot standing (a creature seen again), inside its window or after it.</para>
/// <para><b>The seed</b> (F4): a creature that stands in a slot's cell when the schedule is made, or when the fight starts, was created before the schedule existed, so no create reached it. <see cref="Seed(WorldObjectTable, double)"/> takes every living
/// creature of the object table whose wcid is a slot's and that stands in a slot's cell as that slot's creature, by the rule a create uses (<see cref="Created"/>), so its kill pairs by its spawn wherever it dies; a creature first seen
/// elsewhere is left to the named fallback. The seed allocates once, when it is made; nothing per frame.</para>
/// <para>Every slot stands at the start: the schedule knows only the deaths it is told of and what the player sees (F5, D47). <b>Seen empty</b> (<see cref="Look"/>): a hunted standing slot whose cell has been in the player's
/// view (the player's cell and its visible-cell list from the dat, the cells whose objects ACE sends) for <see cref="SeenEmptySettleSeconds"/> with no living creature of its own in the object table (its remembered guid alive, or an
/// unclaimed living creature of its wcid in its cell, which the look claims for it as a create would) is dead from that moment: its death time is unknown, so it is due at the latest at the moment seen plus its generator's delay,
/// up to the next tick (phase unknown: plus one tick), logged once per slot. A create of its wcid in its cell brings it back at any time (its real death was earlier) and teaches no phase. <c>TODO(ground-slot-dead-at-start)</c>
/// is narrowed to what is left: a slot that died before the schedule began and has not yet been in view reads as standing (the tour walks to look at it, purpose <c>presumed</c>).</para>
/// <para>The queries (<see cref="IsStanding"/>, <see cref="DueAt"/>, <see cref="EarliestDueOn"/>, <see cref="StandingOn"/>, <see cref="StandingHunted"/>) allocate nothing. Game thread only.</para>
/// </remarks>
public sealed class RespawnSchedule
{
    /// <summary>A create this many seconds before a slot's death plus its delay still brings it back (the server's clock and the session's differ by the latency).</summary>
    public const double CreateEarlySlackSeconds = 1.0;

    /// <summary>A dead slot this long past its due time with no create seen is taken as standing by <see cref="Expire"/> (Lifestone's own; <c>TODO(ground-respawn-unseen)</c>).</summary>
    public const double UnseenGraceSeconds = 60.0;

    /// <summary>F5: a slot's cell must be in view this long, with no living creature of its own, before <see cref="Look"/> takes it as seen empty (Lifestone's own: ACE needs a moment to send the creates of newly visible cells).</summary>
    public const double SeenEmptySettleSeconds = 2.0;

    private const string LogCategory = "fight";

    private readonly HuntingGround _ground;
    private readonly ILogSink _log;
    private readonly RespawnSlot[] _slots;
    private readonly int[] _generatorOf;
    private readonly int[] _storeyOf;
    private readonly bool[] _hunted;
    private readonly double[] _deathAt;
    private readonly GeneratorModel[] _generators;
    private readonly double[] _phase;
    private readonly Dictionary<uint, int> _slotOfGuid = [];
    private readonly uint[] _guidOfSlot;
    private readonly double[] _inViewSince;
    private readonly bool[] _seenEmpty;

    /// <summary>The schedule of <paramref name="ground"/>'s slots, every one standing.</summary>
    /// <param name="ground">The ground.</param>
    /// <param name="log">Where kills, phases and unpaired kills are logged.</param>
    /// <param name="known">The session's object table when the schedule is made with one (F4): the creatures already in it are seeded (<see cref="Seed(WorldObjectTable, double)"/>). Null: none known.</param>
    /// <param name="now">The session clock, for the seed.</param>
    /// <exception cref="InvalidDataException">A slot names a generator the profile does not list (the loader refuses it; a ground built by hand is checked here).</exception>
    public RespawnSchedule(HuntingGround ground, ILogSink log, WorldObjectTable? known = null, double now = 0)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(log);
        (_ground, _log) = (ground, log);
        _generators = [.. ground.Respawn.Generators];
        _slots = [.. ground.Respawn.Slots];
        _generatorOf = new int[_slots.Length];
        _storeyOf = new int[_slots.Length];
        _hunted = new bool[_slots.Length];
        _deathAt = new double[_slots.Length];
        _phase = new double[_generators.Length];
        _guidOfSlot = new uint[_slots.Length];
        _inViewSince = new double[_slots.Length];
        _seenEmpty = new bool[_slots.Length];
        Array.Fill(_inViewSince, double.NaN);
        Array.Fill(_deathAt, double.NaN);
        Array.Fill(_phase, double.NaN);
        for (var i = 0; i < _slots.Length; i++)
        {
            var slot = _slots[i];
            _generatorOf[i] = Array.FindIndex(_generators, g => g.Guid == slot.Generator);
            if (_generatorOf[i] < 0)
            {
                throw new InvalidDataException($"ground '{ground.Key}': slot {i} names generator 0x{slot.Generator:X8}, which the profile does not list");
            }

            _storeyOf[i] = ground.StoreyOf(slot.Cell);
            _hunted[i] = ground.RoleOf(slot.Wcid) == GroundRole.Target && ground.IsWalkable(slot.Cell);
        }

        HuntedCount = _hunted.Count(h => h);
        if (_slots.Length > 0)
        {
            log.Info(LogCategory, $"the respawn schedule of {ground.Name}: {_slots.Length} slot(s), {HuntedCount} hunted, over {_generators.Length} generator(s); every slot stands at the start (TODO(ground-slot-dead-at-start): a death the schedule was not told of reads as standing until its cell is in view and seen empty)");
        }

        if (known is not null)
        {
            Seed(known, now);
        }
    }

    /// <summary>The creatures <see cref="Seed(WorldObjectTable, double)"/> remembered for a slot (F4).</summary>
    public int Seeded { get; private set; }

    /// <summary>
    /// F4: seeds the schedule from the creatures the session already knows (the fight's start, or a schedule made with the object table): every living creature in <paramref name="objects"/> whose wcid is a slot's is offered as a create in
    /// the cell it stands in (<see cref="Created"/>, in guid order, so the same table always seeds the same way), so one that stands in a slot's cell is that slot's creature and its kill pairs by its spawn, wherever it dies. A creature
    /// that stands in no slot's cell stays for the named fallback; a dead one (health 0, or the Dead motion) is skipped. Allocates once, here.
    /// </summary>
    /// <param name="objects">The session's object table.</param>
    /// <param name="now">The session clock.</param>
    /// <returns>How many creatures were remembered for a slot.</returns>
    public int Seed(WorldObjectTable objects, double now)
    {
        ArgumentNullException.ThrowIfNull(objects);
        if (_slots.Length == 0)
        {
            return 0;
        }

        var known = new List<(uint Wcid, uint Cell, uint Guid)>();
        foreach (var o in objects.All)
        {
            if (o.Position is { } at && IsSlotWcid(o.Weenie.WeenieClassId) && Living(o))
            {
                known.Add((o.Weenie.WeenieClassId, at.CellId, o.Guid));
            }
        }

        known.Sort(static (a, b) => a.Guid.CompareTo(b.Guid));
        return Seed(known, now);
    }

    /// <summary>F4: the seed over creatures given as (wcid, cell, guid), in the order given (see <see cref="Seed(WorldObjectTable, double)"/>).</summary>
    /// <param name="known">The creatures already known.</param>
    /// <param name="now">The session clock.</param>
    /// <returns>How many creatures were remembered for a slot.</returns>
    public int Seed(IReadOnlyList<(uint Wcid, uint Cell, uint Guid)> known, double now)
    {
        ArgumentNullException.ThrowIfNull(known);
        var claimed = 0;
        for (var k = 0; k < known.Count; k++)
        {
            var (wcid, cell, guid) = known[k];
            if (guid != 0 && !_slotOfGuid.ContainsKey(guid))
            {
                Created(wcid, cell, now, guid);
                if (_slotOfGuid.ContainsKey(guid))
                {
                    claimed++;
                }
            }
        }

        Seeded += claimed;
        if (_slots.Length > 0)
        {
            _log.Info(LogCategory, $"respawn: seeded from the creatures already known: {known.Count} of a slot's wcid, {claimed} standing in a slot's cell and remembered for their slots (their kills pair by their spawn); the other {known.Count - claimed} are left to the cell fallback");
        }

        return claimed;
    }

    private bool IsSlotWcid(uint wcid)
    {
        for (var i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].Wcid == wcid)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The ground.</summary>
    public HuntingGround Ground => _ground;

    /// <summary>How many slots (the profile's, every role).</summary>
    public int Count => _slots.Length;

    /// <summary>How many slots are hunted: a target's slot in a walkable cell (a target's slot in a no-go cell is left standing, as the walk never goes there).</summary>
    public int HuntedCount { get; }

    /// <summary>The kills no slot took (named in the log, each once).</summary>
    public int Unpaired { get; private set; }

    /// <summary>The kills paired to a slot (by the creature's spawn, <see cref="PairedBySpawn"/>, or by the cell it died in, <see cref="PairedByCell"/>).</summary>
    public int Paired { get; private set; }

    /// <summary>The kills paired by the creature's guid, seen created in the slot's cell (2-R1), wherever it died.</summary>
    public int PairedBySpawn { get; private set; }

    /// <summary>The kills paired by the cell the creature died in, the fallback for a guid first seen elsewhere (2-R1).</summary>
    public int PairedByCell { get; private set; }

    /// <summary>The slot whose creature was seen created with guid <paramref name="guid"/>, or -1. Allocates nothing.</summary>
    /// <param name="guid">The creature's guid.</param>
    public int SlotOfGuid(uint guid) => _slotOfGuid.TryGetValue(guid, out var slot) ? slot : -1;

    /// <summary>The slot at <paramref name="index"/>.</summary>
    /// <param name="index">The slot's index (the profile's order).</param>
    public RespawnSlot Slot(int index) => _slots[index];

    /// <summary>The storey (index in <see cref="HuntingGround.Storeys"/>) of slot <paramref name="index"/>'s cell, or -1. Allocates nothing.</summary>
    /// <param name="index">The slot's index.</param>
    public int StoreyOf(int index) => _storeyOf[index];

    /// <summary>True when slot <paramref name="index"/> is hunted (see <see cref="HuntedCount"/>). Allocates nothing.</summary>
    /// <param name="index">The slot's index.</param>
    public bool IsHunted(int index) => _hunted[index];

    /// <summary>True when slot <paramref name="index"/>'s creature stands (never killed, or seen again). Allocates nothing.</summary>
    /// <param name="index">The slot's index.</param>
    public bool IsStanding(int index) => double.IsNaN(_deathAt[index]);

    /// <summary>The death time of slot <paramref name="index"/> on the session clock, or NaN when it stands.</summary>
    /// <param name="index">The slot's index.</param>
    public double DeathAt(int index) => _deathAt[index];

    /// <summary>True when the generator of slot <paramref name="index"/> has shown its tick's phase.</summary>
    /// <param name="index">The slot's index.</param>
    public bool PhaseKnown(int index) => !double.IsNaN(_phase[_generatorOf[index]]);

    /// <summary>The phase (seconds into the tick, 0 to the tick) learned for the generator of guid <paramref name="generator"/>, or NaN while unknown.</summary>
    /// <param name="generator">The generator's guid.</param>
    public double PhaseOf(uint generator)
    {
        for (var g = 0; g < _generators.Length; g++)
        {
            if (_generators[g].Guid == generator)
            {
                return _phase[g];
            }
        }

        return double.NaN;
    }

    /// <summary>
    /// When slot <paramref name="index"/> is due back, on the session clock: NaN when it stands; else its death plus its generator's delay, up to the generator's next tick of the learned phase, or with the phase unknown the death
    /// plus the delay plus one tick. Allocates nothing.
    /// </summary>
    /// <param name="index">The slot's index.</param>
    public double DueAt(int index)
    {
        var death = _deathAt[index];
        if (double.IsNaN(death))
        {
            return double.NaN;
        }

        var g = _generatorOf[index];
        var generator = _generators[g];
        var ready = death + generator.DelaySeconds;
        var tick = generator.TickSeconds;
        if (tick <= 0)
        {
            return ready;
        }

        var phase = _phase[g];
        return double.IsNaN(phase) ? ready + tick : phase + (Math.Ceiling((ready - phase) / tick) * tick);
    }

    /// <summary>The hunted slots that stand on storey <paramref name="storey"/> (-1: on every storey). Allocates nothing.</summary>
    /// <param name="storey">The storey's index, or -1.</param>
    public int StandingOn(int storey)
    {
        var n = 0;
        for (var i = 0; i < _slots.Length; i++)
        {
            if (_hunted[i] && double.IsNaN(_deathAt[i]) && (storey < 0 || _storeyOf[i] == storey))
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>The hunted slots that stand, on every storey. Allocates nothing.</summary>
    public int StandingHunted => StandingOn(-1);

    /// <summary>The dead hunted slot of storey <paramref name="storey"/> (-1: any storey) that is due first, or -1 when none is dead. Allocates nothing.</summary>
    /// <param name="storey">The storey's index, or -1.</param>
    public int EarliestDueOn(int storey)
    {
        var best = -1;
        var bestDue = double.PositiveInfinity;
        for (var i = 0; i < _slots.Length; i++)
        {
            if (!_hunted[i] || double.IsNaN(_deathAt[i]) || (storey >= 0 && _storeyOf[i] != storey))
            {
                continue;
            }

            var due = DueAt(i);
            if (due < bestDue)
            {
                (best, bestDue) = (i, due);
            }
        }

        return best;
    }

    /// <summary>The hunted slots <see cref="Look"/> took as seen empty (<c>slots_seen_empty=</c>, F5).</summary>
    public int SeenEmpty { get; private set; }

    /// <summary>True when slot <paramref name="index"/> is dead because it was seen empty (<see cref="Look"/>), not told of a kill: its due time is the latest it can be. Allocates nothing.</summary>
    /// <param name="index">The slot's index.</param>
    public bool IsSeenEmpty(int index) => _seenEmpty[index];

    /// <summary>
    /// F5 (D47): the schedule learns from what the player sees. Each hunted standing slot whose cell has been in <paramref name="inView"/> (the player's cell and its visible-cell list) for <see cref="SeenEmptySettleSeconds"/>
    /// with no living creature of its own in <paramref name="objects"/> is taken as seen empty (the remarks), logged once per slot. A slot's own creature is its remembered guid alive anywhere in the table, or a living,
    /// unclaimed creature of its wcid standing in its cell, which this claims for the slot as <see cref="Created"/> would (the seed's rule). A slot out of view starts its settle again. Allocates nothing but a claim and the log line.
    /// </summary>
    /// <param name="inView">The cells in the player's view.</param>
    /// <param name="objects">The session's object table.</param>
    /// <param name="now">The session clock.</param>
    /// <returns>The slots this look took as seen empty.</returns>
    public int Look(IReadOnlySet<uint> inView, WorldObjectTable objects, double now)
    {
        ArgumentNullException.ThrowIfNull(inView);
        ArgumentNullException.ThrowIfNull(objects);
        var n = 0;
        for (var i = 0; i < _slots.Length; i++)
        {
            if (!_hunted[i] || !inView.Contains(_slots[i].Cell))
            {
                _inViewSince[i] = double.NaN;
                continue;
            }

            if (double.IsNaN(_inViewSince[i]))
            {
                _inViewSince[i] = now;
            }

            if (!double.IsNaN(_deathAt[i]) || now - _inViewSince[i] < SeenEmptySettleSeconds || HasOwnCreature(i, objects))
            {
                continue;
            }

            Release(i);
            _deathAt[i] = now;
            _seenEmpty[i] = true;
            SeenEmpty++;
            n++;
            var g = _generatorOf[i];
            _log.Info(
                LogCategory,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"respawn: slot {i} (wcid {_slots[i].Wcid} in 0x{_slots[i].Cell:X8}) is seen empty at {now:0.0} s (in view {now - _inViewSince[i]:0.0} s, no living creature of its own): its death is unknown, so it is due at the latest at {DueAt(i):0.0} s ({_generators[g].DelaySeconds:0} s delay, {_generators[g].TickSeconds:0} s tick{(double.IsNaN(_phase[g]) ? ", phase unknown: one tick added" : string.Empty)})"));
        }

        return n;
    }

    /// <summary>True when slot <paramref name="i"/>'s own creature lives in <paramref name="objects"/>: its remembered guid, else an unclaimed living creature of its wcid in its cell (claimed here, as a create would).</summary>
    private bool HasOwnCreature(int i, WorldObjectTable objects)
    {
        if (_guidOfSlot[i] != 0 && objects.TryGet(_guidOfSlot[i], out var own) && Living(own))
        {
            return true;
        }

        foreach (var o in objects.All)
        {
            if (o.Position is { } at && at.CellId == _slots[i].Cell && o.Weenie.WeenieClassId == _slots[i].Wcid && Living(o) && !_slotOfGuid.ContainsKey(o.Guid))
            {
                Claim(i, o.Guid);
                return true;
            }
        }

        return false;
    }

    /// <summary>True for a creature that is not dead in the read model (health above 0 or unknown, and not the Dead motion).</summary>
    private static bool Living(WorldObject o) => o.Health is not <= 0f && o.Motion is not { Forward: WorldObjectTable.DeadMotionLowWord };

    /// <summary>
    /// <c>TODO(ground-respawn-unseen)</c>: a dead slot more than <see cref="UnseenGraceSeconds"/> past its due time whose create the schedule never saw (out of sight, or a create the fight missed) is taken as standing,
    /// logged once per slot, so a tour does not wait for or turn back to a slot that is long back. Called by the tour before each choice; allocates nothing unless a slot expires.
    /// </summary>
    /// <param name="now">The session clock.</param>
    /// <returns>How many slots were taken as standing.</returns>
    public int Expire(double now)
    {
        var n = 0;
        for (var i = 0; i < _slots.Length; i++)
        {
            if (!double.IsNaN(_deathAt[i]) && now > DueAt(i) + UnseenGraceSeconds)
            {
                _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"respawn: slot {i} (wcid {_slots[i].Wcid} in 0x{_slots[i].Cell:X8}) was due at {DueAt(i):0.0} s and no create was seen by {now:0.0} s; it is taken as standing (TODO(ground-respawn-unseen))"));
                _deathAt[i] = double.NaN;
                _seenEmpty[i] = false;
                Expired++;
                n++;
            }
        }

        return n;
    }

    /// <summary>The dead slots <see cref="Expire"/> took as standing with no create seen.</summary>
    public int Expired { get; private set; }

    /// <summary>
    /// A creature of the ground died (2-R1). With its <paramref name="guid"/> remembered from its create in a slot's cell (<see cref="Created"/>), its slot is dead from <paramref name="now"/>, wherever it died. Else, the fallback: the
    /// first standing slot of its wcid in the cell it died in that no other creature is remembered for, named in the log. With neither, the kill is unpaired and named.
    /// </summary>
    /// <param name="wcid">The dying creature's weenie class id.</param>
    /// <param name="cell">The cell it died in.</param>
    /// <param name="now">The session clock.</param>
    /// <param name="guid">The dying creature's guid, or 0 when the caller has none (the death-cell rule alone).</param>
    /// <returns>The slot's index, or -1 when unpaired.</returns>
    public int Killed(uint wcid, uint cell, double now, uint guid = 0)
    {
        if (guid != 0 && _slotOfGuid.TryGetValue(guid, out var spawned))
        {
            Release(spawned);
            if (double.IsNaN(_deathAt[spawned]))
            {
                Paired++;
                PairedBySpawn++;
                return Die(spawned, wcid, cell, now, _slots[spawned].Cell == cell ? "paired by its spawn" : $"paired by its spawn in 0x{_slots[spawned].Cell:X8}; it died away from the slot's cell");
            }
        }

        for (var i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].Wcid == wcid && _slots[i].Cell == cell && double.IsNaN(_deathAt[i]) && _guidOfSlot[i] == 0)
            {
                Paired++;
                PairedByCell++;
                return Die(i, wcid, cell, now, "paired by the cell it died in: its guid was not seen created in a slot's cell");
            }
        }

        Unpaired++;
        _log.Warn(LogCategory, $"respawn: a kill of wcid {wcid} in 0x{cell:X8} pairs with no standing slot (not by its spawn, and none of that wcid and cell without a creature of its own; unpaired kill {Unpaired}; the schedule is unchanged)");
        return -1;
    }

    private int Die(int i, uint wcid, uint cell, double now, string how)
    {
        _deathAt[i] = now;
        _seenEmpty[i] = false;
        var g = _generatorOf[i];
        _log.Info(
            LogCategory,
            string.Create(
                CultureInfo.InvariantCulture,
                $"respawn: slot {i} (wcid {wcid} in 0x{_slots[i].Cell:X8}, generator 0x{_slots[i].Generator:X8}) died at {now:0.0} s in 0x{cell:X8} ({how}); due at {DueAt(i):0.0} s ({_generators[g].DelaySeconds:0} s delay, {_generators[g].TickSeconds:0} s tick, {(double.IsNaN(_phase[g]) ? "phase unknown: one tick added" : $"phase {_phase[g]:0.0} s")})"));
        return i;
    }

    /// <summary>Slot <paramref name="slot"/> has no creature remembered for it any more (it died, or is taken as standing unseen).</summary>
    private void Release(int slot)
    {
        if (_guidOfSlot[slot] != 0)
        {
            _slotOfGuid.Remove(_guidOfSlot[slot]);
            _guidOfSlot[slot] = 0;
        }
    }

    /// <summary>Remembers creature <paramref name="guid"/> for slot <paramref name="slot"/> (the slot's earlier creature is forgotten).</summary>
    private void Claim(int slot, uint guid)
    {
        Release(slot);
        _guidOfSlot[slot] = guid;
        _slotOfGuid[guid] = slot;
    }

    /// <summary>
    /// A creature was created (or came into sight): a create of a dead slot's wcid in its cell at or after its death plus its delay makes the earliest-dead such slot standing, and the first such create inside the slot's window
    /// teaches its generator the tick's phase. Any other create changes nothing.
    /// </summary>
    /// <param name="wcid">The creature's weenie class id.</param>
    /// <param name="cell">The cell it was created in.</param>
    /// <param name="now">The session clock.</param>
    /// <param name="guid">The creature's guid, or 0 when the caller has none. A create of a slot's wcid in the slot's cell remembers the guid for that slot (a dead slot it brings back, else the first standing slot with no creature
    /// remembered), so <see cref="Killed"/> pairs the creature's death with that slot wherever it dies (2-R1); a guid already remembered (a creature seen again) changes nothing.</param>
    /// <returns>The slot made standing, or -1.</returns>
    public int Created(uint wcid, uint cell, double now, uint guid = 0)
    {
        if (guid != 0 && _slotOfGuid.ContainsKey(guid))
        {
            return -1;
        }

        var best = -1;
        for (var i = 0; i < _slots.Length; i++)
        {
            var death = _deathAt[i];
            if (_slots[i].Wcid != wcid || _slots[i].Cell != cell || double.IsNaN(death) || (!_seenEmpty[i] && now < death + _generators[_generatorOf[i]].DelaySeconds - CreateEarlySlackSeconds))
            {
                continue; // F5: a slot seen empty died at an unknown time before it was seen, so any create of its wcid in its cell brings it back
            }

            if (best < 0 || death < _deathAt[best])
            {
                best = i;
            }
        }

        if (best < 0)
        {
            if (guid != 0)
            {
                for (var i = 0; i < _slots.Length; i++)
                {
                    if (_slots[i].Wcid == wcid && _slots[i].Cell == cell && double.IsNaN(_deathAt[i]) && _guidOfSlot[i] == 0)
                    {
                        Claim(i, guid);
                        break;
                    }
                }
            }

            return -1;
        }

        var g = _generatorOf[best];
        var generator = _generators[g];
        var ready = _deathAt[best] + generator.DelaySeconds;
        var wasSeenEmpty = _seenEmpty[best];
        _seenEmpty[best] = false;
        if (!wasSeenEmpty && double.IsNaN(_phase[g]) && generator.TickSeconds > 0 && now <= ready + generator.TickSeconds + CreateEarlySlackSeconds)
        {
            _phase[g] = ((now % generator.TickSeconds) + generator.TickSeconds) % generator.TickSeconds;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"respawn: generator 0x{generator.Guid:X8}'s tick phase is {_phase[g]:0.0} s of {generator.TickSeconds:0} s (its first create seen bringing a slot back, at {now:0.0} s)"));
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"respawn: slot {best} (wcid {wcid} in 0x{cell:X8}) stands again at {now:0.0} s, {now - _deathAt[best]:0.0} s after {(wasSeenEmpty ? "it was seen empty" : "its death")}"));
        _deathAt[best] = double.NaN;
        if (guid != 0)
        {
            Claim(best, guid);
        }

        return best;
    }
}
