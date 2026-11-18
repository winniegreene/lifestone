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
    private (int Level, uint Skill, int Cap, uint ManaConversion, ulong Floors, int Dropped, ulong Town) _warBuiltFor = (-1, 0, 0, 0, 0, 0, 0);
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

    /// <summary>M6 item 14 (m6-goldscarab): the most of one component a cast needs in the pack (the foci formula's four Prismatic Tapers, <see cref="GearKit.FociFormulaCounts"/>): the count of a town stock component is read up to this, so the options are built again only when a cast's worth comes or goes.</summary>
    internal const int TownCastWorth = 4;

    private readonly List<uint> _townScratch = [];
    private ulong _townHeld; // M6 item 14 (m6-goldscarab): what the pack holds of each town stock line that is a spell component, four bits a line, capped at TownCastWorth
    private double _townReadAt = double.NegativeInfinity;
    private bool _townKnown; // true once the pack was read for the town stock: until then no count is claimed

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
        var pick = new Threat.FullestPick(Threat.FullestRule.RingAnchor, current); // M6 16 E2d: the one comparison, by this pick's rule (the most, the picked target among equals)
        for (var i = 0; i < candidates.Length; i++)
        {
            if (!candidates[i].Clear)
            {
                continue;
            }

            pick.Offer(i, candidates[i].Guid, candidates[i].Health);
        }

        return pick.Index;
    }

    /// <summary>M5 7 P7s (D148): the names of the war options built last (option 0 the bolt), for a test. Allocates.</summary>
    internal IReadOnlyList<string> WarOptionNames => _warPicks is { } picks ? [.. picks.Options.Select(o => o.Name)] : [];

    /// <summary>M5 7 P7s (D148): the floor-only carried lines of the ground (<see cref="ComponentFloor.FloorOnly"/>) and the counters that read them; none in the arena.</summary>
    /// <param name="accountName">M6 item 1 h1: the account's name (<see cref="BuffOptions.AccountName"/>): a spell of a school whose focus the character does not hold is read on the account's full formula; null: the foci formula (today's rule).</param>
    private void InitFloors(string? accountName)
    {
        if (_options.ArenaHere)
        {
            return;
        }

        _floorOnlyLines = ComponentFloor.FloorOnly(_ground, _spells, accountName, _holdsFocusAtStart ??= HoldsFocusAtStart); // M6 16 E5d: the counters that read them are the session's (ReadFloors)
    }

    /// <summary>
    /// M5 7 P7s (the owner's cheat-free default, D148; <c>m5-8-unsold-component-floor</c>): every <see cref="FloorCheckSeconds"/>, the floor-only lines are counted (an unread pack keeps the last answer); each that reached its floor or rose
    /// back over it is logged. The mask goes into the war options' key, so the options are built again on a change: at the floor the spells that need the line are left out (the bolt and the other options go on), over it they return.
    /// </summary>
    private void ReadFloors(GameState state, double clock)
    {
        if (_floorOnlyLines.Count == 0 || clock - _floorReadAt < FloorCheckSeconds)
        {
            return;
        }

        var counters = _floorCounters ??= Quartermaster.TripDesk.For(state).Reading(_ground, _floorOnlyLines.Keys, m => _log.Info(LogCategory, m)); // M6 16 E5d: the session's reading twin, asked once a fight: the floor read moves nothing the trip trigger reads

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

    /// <summary>
    /// M6 item 14 (m6-goldscarab; live 2026-11-12: level IV of everything for want of a Gold Scarab no vendor of Holtburg sells): every <see cref="FloorCheckSeconds"/>, the pack's count of each town stock line that is a spell component (the
    /// profile's <c>town_stock</c>: the Gold and the Pyreal Scarab bought at Uziz on the way out) is read, up to <see cref="TownCastWorth"/>. The counts go into the war options' key, so the options are built again when a cast's worth comes or
    /// goes, and a line that ran out or came back is logged once each way. Nothing stops: with none held the picks fall to the level the pack pays for (<see cref="LevelFloor"/>, <see cref="WarKitCarries"/>). A pack not read keeps the last answer.
    /// </summary>
    private void ReadTownStock(GameState state, double clock)
    {
        var lines = _ground.TownStock;
        if (_options.ArenaHere || lines.Count == 0 || clock - _townReadAt < FloorCheckSeconds)
        {
            return;
        }

        _townReadAt = clock;
        var inventory = state.Self.Inventory;
        if (!inventory.IsKnown || !inventory.ListsComplete)
        {
            return;
        }

        _townKnown = true;
        var held = 0UL;
        for (var i = 0; i < lines.Count && i < 16; i++)
        {
            if (ComponentWeenies.TryGetByWcid(lines[i].Wcid, out _))
            {
                held |= (ulong)Math.Min(TownCastWorth, GearInventory.CountOf(state, _townScratch, lines[i].Wcid)) << (4 * i);
            }
        }

        if (held == _townHeld)
        {
            return;
        }

        for (var i = 0; i < lines.Count && i < 16; i++)
        {
            var (was, now) = ((int)((_townHeld >> (4 * i)) & 0xF), (int)((held >> (4 * i)) & 0xF));
            if ((was == 0) == (now == 0))
            {
                continue;
            }

            _log.Info(LogCategory, now == 0
                ? string.Create(CultureInfo.InvariantCulture, $"components: {lines[i].Name} {lines[i].Wcid} (town stock, bought at {lines[i].Vendor.Name} in {lines[i].Town}): none left in the pack: the spells that burn it fall to the level the pack pays for until the next stop buys more; nothing stops (m6-goldscarab)")
                : string.Create(CultureInfo.InvariantCulture, $"components: {lines[i].Name} {lines[i].Wcid} (town stock, bought at {lines[i].Vendor.Name} in {lines[i].Town}): the pack holds {(now >= TownCastWorth ? TownCastWorth + " or more" : now.ToString(CultureInfo.InvariantCulture))}: the spells that burn it are castable (m6-goldscarab)"));
        }

        _townHeld = held;
    }

    /// <summary>M6 item 14 (m6-goldscarab): the pack's count of the town stock component <paramref name="wcid"/> as <see cref="ReadTownStock"/> last read it (up to <see cref="TownCastWorth"/>), with its line; false when the ground buys no such ware in a town.</summary>
    private bool TownHeld(uint wcid, out int held, out Grounds.TownStockLine line)
    {
        var lines = _ground.TownStock;
        for (var i = 0; i < lines.Count && i < 16; i++)
        {
            if (lines[i].Wcid == wcid)
            {
                (held, line) = ((int)((_townHeld >> (4 * i)) & 0xF), lines[i]);
                return true;
            }
        }

        (held, line) = (0, null!);
        return false;
    }

    private Func<DatReaderWriter.Enums.MagicSchool, bool>? _holdsFocusAtStart; // made once: the floor check runs per pick

    /// <summary>
    /// M6 item 1 h1: whether the character holds the focus of <paramref name="school"/>, as far as the ground's plan can tell before the pack is read (the floors are planned once): the kit brings the foci it lists (BSD Mage's four), and War and
    /// Life are held whatever the kit (the starter foci of the climber, whose bare kit lists none; the autopilot's audit stops the run when one is missing, <c>m6-foci-missing</c>). Creature and Item Enchantment are held only when the kit lists them.
    /// </summary>
    /// <param name="school">The spell's school.</param>
    private bool HoldsFocusAtStart(DatReaderWriter.Enums.MagicSchool school) =>
        school is DatReaderWriter.Enums.MagicSchool.WarMagic or DatReaderWriter.Enums.MagicSchool.LifeMagic || _kit.ItemOf(LevelFloor.FocusOf(school)) is not null;

    /// <summary>M5 7 P7s (D148): true (with the line's name) when a component of <paramref name="info"/>'s foci formula is a floor-only line at its floor.</summary>
    private bool WarAtFloor(SpellInfo info, out string line)
    {
        line = string.Empty;
        return _floorCounters is { } counters && _floorMask != 0 && ComponentFloor.NeedsLineAtFloor(info, counters, out line, _buffOptions.AccountName, _holdsFocusAtStart ??= HoldsFocusAtStart);
    }

    /// <summary>
    /// The kit carries the foci formula's components of <paramref name="info"/> (a stock line of the ground each, holding at least one cast's worth); the name of the first it lacks otherwise. M6 item 14 (m6-goldscarab): a component the ground
    /// buys in another town (a line of the profile's <c>town_stock</c>: no stock line, no stop line) passes while the pack holds a cast's worth of it (<see cref="ReadTownStock"/>), and is named with the count held when it does not.
    /// </summary>
    private bool WarKitCarries(SpellInfo info, out string missing)
    {
        foreach (var (component, n) in GearKit.FociFormulaCounts(info.Formula))
        {
            if (!ComponentWeenies.TryGetByComponent(component, out var weenie))
            {
                missing = $"component {component} (not in the component map)";
                return false;
            }

            if (_ground.Supplies.StockOf(weenie.Wcid) is null && TownHeld(weenie.Wcid, out var held, out var town))
            {
                if (held < n)
                {
                    missing = string.Create(CultureInfo.InvariantCulture, $"{weenie.Name} (wcid {weenie.Wcid}) x{n} a cast is bought at {town.Vendor.Name} in {town.Town} (the town stock of {_ground.Name}) and {(_townKnown ? $"the pack holds {held}" : "the pack is not read yet")}");
                    return false;
                }

                continue;
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
        ReadTownStock(state, clock); // M6 item 14 (m6-goldscarab)
        var cap = _options.ArenaHere ? Math.Clamp(_options.MaxLevel, 1, Levels) : LevelCapFor(q);
        var key = (_level, warSkill, cap, _manaConversion, _floorMask, _droppedSpells.Count, _townHeld); // M6 7 K: a spell dropped for the fight builds the options again; M6 item 14: so does a town stock component's cast's worth coming or going
        if (_warPicks is not null && key == _warBuiltFor)
        {
            return;
        }

        _warBuiltFor = key;
        _warPicks ??= new WarPicks(new WarSpellPicker(new WarPickSettings { RingMinTargets = _ground.FightRingMinTargets ?? WarPickSettings.DefaultRingMinTargets }), _log, LogCategory); // M5 7 F22: the ground's ring floor
        _ringCasts ??= new RingCasts(_warPicks.Picker.RingFactor, _log, LogCategory);
        _volleyCasts ??= new RingCasts(_warPicks.Picker.VolleyFactor, _log, LogCategory, "volley"); // M5 7 F24: the volley hits are measured as the ring ones are
        var options = new List<WarOption>();
        if (_level < 0 || !_spells.TryGetValue(_levelIds[_level], out var bolt))
        {
            _warPicks.SetOptions(options);
            _warPicks.NoteOnce("war pick: no bolt level is chosen yet; no war option is built (the picker fills the bolt from the SpellTable when one is)");
            return;
        }

        if (!WarSpellData.TryGet(bolt.Id, out var boltRow) || WarOptionOf(bolt, _boltLineName, boltRow) is not { } boltOption)
        {
            // M6 1 f (m5-war-pick-data closed by data): every element's bolt line I to VIII has its projectile row (WarSpellData), so only a spell outside them lands here, named by id.
            _warPicks.SetOptions(options);
            _warPicks.NoteOnce(string.Create(CultureInfo.InvariantCulture, $"TODO(m5-war-pick-data): {bolt.Name} (spell {bolt.Id}) has no projectile row in WarSpellData; no war option, the bolt scored from the SpellTable without its damage"));
            return;
        }

        options.Add(boltOption);
        if (_boltElement != "fire")
        {
            // M6 14 the element line: the profile's war options (the streak, the volley, the blast, the ring) are fire's shapes; with another line in hand the bolt is the one option
            _warPicks.NoteOnce($"war pick: the bolt in hand is {_boltLineName}: the fire line's other shapes are not offered (they are fire)");
            _warPicks.SetOptions(options);
            return;
        }

        foreach (var line in ArenaProfile.Lines.Concat(ArenaProfile.Extras))
        {
            if (line.Role != ProfileRole.WarOption)
            {
                continue;
            }

            var bestRow = WarSpellData.HighestUnderCap(
                line.Spells.Select(spell => spell.Id),
                cap,
                id => _spells.TryGetValue(id, out var info) && !_droppedSpells.Contains(id) && (book is not { IsKnown: true } || book.Knows(id)) && GearCasts.Chance(warSkill, info.Power) >= _buffOptions.CastChance,
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

        var known = _live.TryGet(o.Guid, max, out var fraction, out var source);
        ShadowHealth(Threat.HealthReader.FightWar, o.Guid, known, fraction, max, source, _live.AgeOf(o.Guid, _lastClock), _lastClock); // M6 16 E2d: the one picture is asked the same beside it (report-only)
        return known ? fraction : 1.0f;
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
            if (StandsAt(session, o) is not { } p || (o.ItemType & WarCreatureItemType) == 0 || o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord } || _killed.Contains(o.Guid) || !(isCurrent || IsTargetWcid(o.Weenie.WeenieClassId)))
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
        if (!objects.TryGet(_target, out var cast) || StandsAt(session, cast) is not { } castAt)
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

            if (o.Guid == _target || StandsAt(session, o) is not { } p || (o.ItemType & WarCreatureItemType) == 0 || o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord } || _killed.Contains(o.Guid))
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
