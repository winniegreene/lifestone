using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.World;
using Lifestone.Data.Magic;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M5 7 F9 (D89): the fight's war spell chosen live by situation (<see cref="WarSpellPicker"/>): the bolt, a streak, a volley, a blast or the ring by the seconds all in a kill, each choice logged when it changes and the picks
/// counted on the verdict line (<c>picks=</c>, report-only). The cast goes through the same <c>CastAttempt</c> as the bolt's; the bolt stays when the data for a pick is missing.
/// </summary>
public sealed partial class FightScenario
{
    private const int WarTargetCap = 16;
    private const uint WarCreatureItemType = 0x10; // ACE ItemType.Creature, as WorldObjectTable filters
    private const float WarSeeMetres = 12f;

    private readonly WarTarget[] _warTargets = new WarTarget[WarTargetCap];
    private WarPicks? _warPicks;
    private (int Level, uint Skill, int Cap, uint ManaConversion, ulong Floors) _warBuiltFor = (-1, 0, 0, 0, 0);
    private bool _warActive;
    private RingCasts? _ringCasts;
    private RingCasts? _volleyCasts;
    private double _volleyPredicted;
    private bool _volleyOpen;
    private string? _volleyLineText;
    private int _streakFinishers;
    private double _ringPredicted;
    private bool _ringOpen;
    private string? _ringLineText;
    private double _ringAnchorHealth = double.NaN;
    private readonly RingCandidate[] _ringCandidates = new RingCandidate[WarTargetCap];

    /// <summary>M5 7 P7s (D148): the session-clock seconds between two reads of the floor-only lines (the counts are read allocation-free; Lifestone's own).</summary>
    public const double FloorCheckSeconds = 5;

    private LogisticsCounters? _floorCounters; // M5 7 P7s (D148): null when no carried line is floor-only (or in the arena)
    private IReadOnlyDictionary<uint, IReadOnlyList<string>> _floorOnlyLines = new Dictionary<uint, IReadOnlyList<string>>();
    private ulong _floorMask;
    private double _floorReadAt = double.NegativeInfinity;

    /// <summary>A creature the ring will hit, for the choice of its anchor (<see cref="ChooseRingAnchor"/>).</summary>
    /// <param name="Guid">The creature.</param>
    /// <param name="Health">Its remaining health share, 0 to 1 (the live health model's).</param>
    /// <param name="Clear">True when the player has a clear line to it.</param>
    internal readonly record struct RingCandidate(uint Guid, float Health, bool Clear);

    /// <summary>
    /// M5 7 F28: the index of the creature the ring is aimed at: the one with the most remaining health among the candidates with a clear line; a tie keeps <paramref name="current"/> (the pick already made), else the first listed.
    /// -1 when no candidate has a clear line (the aim stays as picked).
    /// </summary>
    /// <param name="candidates">The creatures the ring will hit.</param>
    /// <param name="current">The guid picked as the cast target before the ring was chosen.</param>
    internal static int ChooseRingAnchor(ReadOnlySpan<RingCandidate> candidates, uint current)
    {
        var best = -1;
        for (var i = 0; i < candidates.Length; i++)
        {
            if (!candidates[i].Clear)
            {
                continue;
            }

            if (best < 0 || candidates[i].Health > candidates[best].Health || (candidates[i].Health == candidates[best].Health && candidates[i].Guid == current))
            {
                best = i;
            }
        }

        return best;
    }

    /// <summary>M5 7 P7s (D148): the names of the war options built last (option 0 the bolt), for a test. Allocates.</summary>
    internal IReadOnlyList<string> WarOptionNames => _warPicks is { } picks ? [.. picks.Options.Select(o => o.Name)] : [];

    /// <summary>M5 7 P7s (D148): the floor-only carried lines of the ground (<see cref="ComponentFloor.FloorOnly"/>) and the counters that read them; none in the arena.</summary>
    private void InitFloors()
    {
        if (_options.ArenaHere)
        {
            return;
        }

        _floorOnlyLines = ComponentFloor.FloorOnly(_ground, _spells);
        if (_floorOnlyLines.Count > 0)
        {
            _floorCounters = new LogisticsCounters(_ground, _floorOnlyLines.Keys);
        }
    }

    /// <summary>
    /// M5 7 P7s (the owner's cheat-free default, D148; <c>m5-8-unsold-component-floor</c>): every <see cref="FloorCheckSeconds"/>, the floor-only lines are counted (an unread pack keeps the last answer); each that reached its floor or rose
    /// back over it is logged. The mask goes into the war options' key, so the options are built again on a change: at the floor the spells that need the line are left out (the bolt and the other options go on), over it they return.
    /// </summary>
    private void ReadFloors(GameState state, double clock)
    {
        if (_floorCounters is not { } counters || clock - _floorReadAt < FloorCheckSeconds)
        {
            return;
        }

        _floorReadAt = clock;
        if (counters.Check(state).Kind == LogisticsKind.Unread)
        {
            return;
        }

        var mask = counters.FloorMask;
        if (mask == _floorMask)
        {
            return;
        }

        for (var i = 0; i < counters.LineCount && i < 64; i++)
        {
            var bit = 1UL << i;
            if ((mask & bit) == (_floorMask & bit))
            {
                continue;
            }

            var line = counters.LineAt(i);
            var spells = _floorOnlyLines.TryGetValue(line.Wcid, out var names) ? string.Join(" and ", names) : "the spells that need it";
            _log.Info(LogCategory, (mask & bit) != 0
                ? string.Create(CultureInfo.InvariantCulture, $"components: {line.Name} at its floor ({counters.HeldOf(i)} held, under its stop line {line.StopUnder}; carried, no shop sells it): {spells} is left out of the war pick until more turn up; the bolt goes on (D148)")
                : string.Create(CultureInfo.InvariantCulture, $"components: {line.Name} back over its floor ({counters.HeldOf(i)} held, its stop line {line.StopUnder}): {spells} returns to the war pick (D148)"));
        }

        _floorMask = mask;
    }

    /// <summary>M5 7 P7s (D148): true (with the line's name) when a component of <paramref name="info"/>'s foci formula is a floor-only line at its floor.</summary>
    private bool WarAtFloor(SpellInfo info, out string line)
    {
        line = string.Empty;
        return _floorCounters is { } counters && _floorMask != 0 && ComponentFloor.NeedsLineAtFloor(info, counters, out line);
    }

    /// <summary>The kit carries the foci formula's components of <paramref name="info"/> (a stock line of the ground each, holding at least one cast's worth); the name of the first it lacks otherwise.</summary>
    private bool WarKitCarries(SpellInfo info, out string missing)
    {
        foreach (var (component, n) in GearKit.FociFormulaCounts(info.Formula))
        {
            if (!ComponentWeenies.TryGetByComponent(component, out var weenie))
            {
                missing = $"component {component} (not in the component map)";
                return false;
            }

            if (_ground.Supplies.StockOf(weenie.Wcid) is not { } stock || stock.StopUnder < n)
            {
                missing = $"{weenie.Name} (wcid {weenie.Wcid}) x{n} a cast is no stock line of {_ground.Name}";
                return false;
            }
        }

        missing = string.Empty;
        return true;
    }

    private WarOption? WarOptionOf(SpellInfo info, string family, WarSpellRow row)
    {
        var kind = row.Kind;
        return new WarOption(family, info.Name, row, info.Power, info.BaseMana, (info.Flags & SpellFlags.IgnoresManaConversion) != 0, kind == WarKind.Ring ? info.BaseRangeConstant : CastRange(info, _initPlusRanks), WarSpellData.EffectSeconds(kind));
    }

    /// <summary>Builds the options (option 0 the bolt in hand) when the level, the War Magic skill, the cap or Mana Conversion changed since they were built. Allocates only then.</summary>
    private void WarBuildOptions(PlayerQualities q, Spellbook? book, uint warSkill, GameState state, double clock)
    {
        ReadFloors(state, clock); // M5 7 P7s (D148)
        var cap = _options.ArenaHere ? Math.Clamp(_options.MaxLevel, 1, Levels) : LevelCapFor(q);
        var key = (_level, warSkill, cap, _manaConversion, _floorMask);
        if (_warPicks is not null && key == _warBuiltFor)
        {
            return;
        }

        _warBuiltFor = key;
        _warPicks ??= new WarPicks(new WarSpellPicker(new WarPickSettings { RingMinTargets = _ground.FightRingMinTargets ?? WarPickSettings.DefaultRingMinTargets }), _log, LogCategory); // M5 7 F22: the ground's ring floor
        _ringCasts ??= new RingCasts(_warPicks.Picker.RingFactor, _log, LogCategory);
        _volleyCasts ??= new RingCasts(_warPicks.Picker.VolleyFactor, _log, LogCategory, "volley"); // M5 7 F24: the volley hits are measured as the ring ones are
        var options = new List<WarOption>();
        if (_level < 0 || !_spells.TryGetValue(_levelIds[_level], out var bolt) || !WarSpellData.TryGet(bolt.Id, out var boltRow) || WarOptionOf(bolt, ArenaProfile.WarLineName, boltRow) is not { } boltOption)
        {
            _warPicks.SetOptions(options);
            _warPicks.NoteOnce("TODO(m5-war-pick-data): the bolt in hand has no projectile row; the bolt stays");
            return;
        }

        options.Add(boltOption);
        foreach (var line in ArenaProfile.Lines.Concat(ArenaProfile.Extras))
        {
            if (line.Role != ProfileRole.WarOption)
            {
                continue;
            }

            var bestRow = WarSpellData.HighestUnderCap(
                line.Spells.Select(spell => spell.Id),
                cap,
                id => _spells.TryGetValue(id, out var info) && (book is not { IsKnown: true } || book.Knows(id)) && GearCasts.Chance(warSkill, info.Power) >= _buffOptions.CastChance,
                out var capped);
            var best = bestRow is not null && _spells.TryGetValue(bestRow.Id, out var bestInfo) ? bestInfo : null;
            var short_ = line.Name.EndsWith("Ring of Fire", StringComparison.Ordinal) ? "ring" : line.Name;
            if (best is null || bestRow is null)
            {
                _warPicks.NoteOnce(capped ? $"{short_}: no level payable under the cap ({Numerals[cap - 1]}); never cast above it" : $"{short_}: no known level reaches a cast chance of {_buffOptions.CastChance} at War Magic skill {warSkill}");
                continue;
            }

            if (bestRow.Kind == WarKind.Arc)
            {
                _warPicks.NoteOnce($"{short_}: the code cannot test an arc's lob for a clear line, and a lobbed projectile does not track a moving target (TODO(m5-war-pick-arc-line)); the bolt");
                continue;
            }

            if (!_options.ArenaHere && !WarKitCarries(best, out var missing))
            {
                _warPicks.NoteOnce($"{short_}: left out, the kit does not carry its components: {missing}");
                continue;
            }

            if (!_options.ArenaHere && WarAtFloor(best, out var floor))
            {
                _log.Info(LogCategory, $"war pick: {short_}: left out at a carried component's floor ({floor}): not cast until more turn up, the bolt goes on (D148, m5-8-unsold-component-floor)");
                continue;
            }

            if (WarOptionOf(best, line.Name, bestRow) is { } option)
            {
                options.Add(option);
                if (option.Row.Kind == WarKind.Ring)
                {
                    _ringLineText = " with " + option.Name; // ACE's damage line: "You scorch <target> for <n> points with <spell>."
                }
                else if (option.Row.Kind == WarKind.Volley)
                {
                    _volleyLineText = " with " + option.Name; // M5 7 F24: "... points with Flame Volley VI."
                }
            }
        }

        _warPicks.SetOptions(options);
    }

    /// <summary>The live health model's share for a war pick (M5 7 F20); a creature never told and with nothing dealt is taken as unhurt, which only makes its kill take longest (named in the notes).</summary>
    private float LiveHealthOrFull(WorldObject o)
    {
        var max = TargetStatsOf(o.Weenie.WeenieClassId).MaxHealth;
        if (o.Health is { } held)
        {
            _live.Seed(o.Guid, held);
        }

        return _live.TryGet(o.Guid, max, out var fraction, out _) ? fraction : 1.0f;
    }

    /// <summary>
    /// M5 7 F28: when the pick is the ring, the creature it is aimed at (the anchor) becomes the one with the most remaining health among those the ring will hit (the living targets of the ground within the ring's reach, with a clear
    /// line): a nearly dead tusker is likely to die during the wind-up, and the release then finds no target (0x042C). The hit-count rule is the picker's and is unchanged; only the anchor among the qualifying creatures is chosen here. The
    /// ring's predicted hits are worked out again for the new anchor; the anchor's health share is logged and goes to the ring's close line.
    /// </summary>
    private void ReanchorRing(IGameSessionView session, double clock, in CellFrame here)
    {
        _ringAnchorHealth = double.NaN;
        if (_warPicks is not { } picks || _warChoice <= 0 || _warChoice >= picks.Options.Count || picks.Options[_warChoice] is not { Row.Kind: WarKind.Ring } ring)
        {
            return;
        }

        var objects = session.State.Objects;
        var origin = here.ToGlobal();
        var n = 0;
        foreach (var o in objects.All)
        {
            if (n >= WarTargetCap)
            {
                break;
            }

            var isCurrent = o.Guid == _target;
            if (o.Position is not { } p || (o.ItemType & WarCreatureItemType) == 0 || o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord } || _killed.Contains(o.Guid) || !(isCurrent || IsTargetWcid(o.Weenie.WeenieClassId)))
            {
                continue;
            }

            if (!isCurrent && ((_cellFilter is not null && !_cellFilter(p.CellId)) || p.ToGlobal().DistanceTo(origin) > ring.ReachMetres))
            {
                continue;
            }

            _ringCandidates[n++] = new RingCandidate(o.Guid, LiveHealthOrFull(o), isCurrent || _options.IgnoreLine || LineIsClear(session, in here, o.Guid)); // the current target was picked with a clear line
        }

        var seen = n;
        var best = _target;
        var bestHealth = double.NaN;
        if (ChooseRingAnchor(_ringCandidates.AsSpan(0, n), _target) is var index and >= 0)
        {
            (best, bestHealth) = (_ringCandidates[index].Guid, _ringCandidates[index].Health);
        }

        _ringAnchorHealth = bestHealth;
        if (best == _target)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ring: anchor stays 0x{_target:X8}, health {bestHealth * 100:0}% (the fullest of {seen} in reach)"));
            return;
        }

        var previous = _target;
        _target = best;
        _tally.SetTarget(objects.TryGet(best, out var chosen) ? chosen.Name : null);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ring: anchor moved from 0x{previous:X8} to 0x{best:X8}, health {bestHealth * 100:0}% (the fullest of {seen} in reach; a nearly dead anchor may die during the wind-up)"));
        SelectTarget(session, best, clock);
        var count = WarGather(session, in here);
        if (count > 0)
        {
            _ringPredicted = picks.Picker.PredictedHits(ring, _warTargets.AsSpan(0, count));
        }
    }

    /// <summary>The cast target first, then the live targets of the ground within sight, with their distances and health. Allocation-free; the count.</summary>
    private int WarGather(IGameSessionView session, in CellFrame here)
    {
        var objects = session.State.Objects;
        var origin = here.ToGlobal();
        var count = 0;
        if (!objects.TryGet(_target, out var cast) || cast.Position is not { } castAt)
        {
            return 0;
        }

        _warTargets[count++] = new WarTarget(castAt.ToGlobal().DistanceTo(origin), LiveHealthOrFull(cast));
        foreach (var o in objects.All)
        {
            if (count >= WarTargetCap)
            {
                break;
            }

            if (o.Guid == _target || o.Position is not { } p || (o.ItemType & WarCreatureItemType) == 0 || o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord } || _killed.Contains(o.Guid))
            {
                continue;
            }

            var wcid = o.Weenie.WeenieClassId;
            var listed = false;
            for (var i = 0; i < _targetWcids.Count; i++)
            {
                if (_targetWcids[i] == wcid)
                {
                    listed = true;
                    break;
                }
            }

            if (!listed || (_cellFilter is not null && !_cellFilter(p.CellId)))
            {
                continue;
            }

            var d = p.ToGlobal().DistanceTo(origin);
            if (d <= WarSeeMetres)
            {
                _warTargets[count++] = new WarTarget(d, LiveHealthOrFull(o));
            }
        }

        return count;
    }
}
