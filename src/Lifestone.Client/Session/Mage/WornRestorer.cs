using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Mage;

/// <summary>One piece of the worn set: the object and the EquipMask it was worn at.</summary>
/// <param name="Guid">The piece.</param>
/// <param name="Location">Its location.</param>
public sealed record WornEntry(uint Guid, uint Location);

/// <summary>
/// M5 7 S2d: the worn set the mage had while alive (closes <c>TODO(m5-self-equip-death-rewear)</c>): every worn piece that is no caster (the caster is the pick's, <see cref="SelfEquipJudge.BestCaster"/>), by object id and location. The autopilot takes a
/// snapshot every few seconds while the mage lives and writes it into its checkpoint; the death's take-back and the start-up audit wear it again. A death empties the equipped list (ACE moves what it drops to the corpse), so a snapshot is never taken
/// at 0 health or after the death is seen.
/// </summary>
public sealed class WornMemory
{
    private readonly Dictionary<uint, uint> _set = [];

    /// <summary>The memory of the process.</summary>
    public static WornMemory Shared { get; } = new();

    /// <summary>The set, by guid.</summary>
    public IReadOnlyDictionary<uint, uint> Set => _set;

    /// <summary>The set as the checkpoint keeps it.</summary>
    public List<WornEntry> Entries() => [.. _set.Select(p => new WornEntry(p.Key, p.Value))];

    /// <summary>Replaces the set with what <paramref name="state"/> wears now (the casters left out).</summary>
    public void Snapshot(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _set.Clear();
        foreach (var e in state.Self.Inventory.Equipped)
        {
            if (state.Objects.TryGet(e.Guid, out var o) && (o.ItemType & SelfEquipJudge.CasterType) == 0 && (e.Location & SelfEquipJudge.Held) == 0)
            {
                _set[e.Guid] = e.Location;
            }
        }
    }

    /// <summary>Seeds the set from a checkpoint (an empty memory only: a live snapshot is newer).</summary>
    public void Seed(IEnumerable<WornEntry>? entries)
    {
        if (entries is null || _set.Count > 0)
        {
            return;
        }

        foreach (var e in entries)
        {
            _set[e.Guid] = e.Location;
        }
    }

    /// <summary>Forgets the set (tests).</summary>
    public void Clear() => _set.Clear();
}

/// <summary>
/// M5 7 S2d: puts the mage's set back on, one action at a time (a frame-stepped machine for the death's take-back and the start-up audit). In order: (1) each piece of the remembered worn set (<see cref="WornMemory"/>) held and not worn, at
/// its location; (2) any clothing or armour held whose coverage no worn piece overlaps (ACE's rule, Creature_Equipment.cs:50-58 and 93-101), the best armour level first, then the lightest (the shirt and breeches of a login with no memory);
/// (3) the wearables with a useful cantrip in a free slot (<see cref="SelfEquipJudge"/>); (4) the one best caster held, the review and kit packs included (<see cref="SelfEquipJudge.BestCaster"/>), the wielded one to the kit pack first.
/// Each wear and each refusal is logged with its reason; every item is tried once. Every send is <see cref="ActionSource.Automation"/>.
/// </summary>
public sealed class WornRestorer
{
    private readonly ILogSink _log;
    private readonly WornMemory _memory;
    private readonly string _category;
    private readonly string _why;
    private SelfEquipJudge? _judge;
    private readonly PackRules _rules;
    private readonly HashSet<uint> _tried = [];
    private readonly Queue<(uint Guid, uint Container, uint Location, string Line)> _queue = new();
    private ActionHandle? _pending;
    private string _line = string.Empty;
    private uint _expect;
    private bool _casterDone;
    private bool _done;
    private bool _identifying;
    private readonly bool _memoryOnly;
    private Queue<uint>? _toAppraise;

    /// <summary>M5 7 S2e: the most appraisals one restore asks for (a pack holds 102 and three side packs 24 each).</summary>
    public const int AppraisalCap = 60;

    /// <summary>A restorer.</summary>
    /// <param name="ground">The ground (its keep-aside cantrips, its war family, its pack rules).</param>
    /// <param name="level">The character's level (the war family).</param>
    /// <param name="spells">The SpellTable's spells (the cantrips' names).</param>
    /// <param name="log">Where the lines go.</param>
    /// <param name="category">The log category.</param>
    /// <param name="why">What began it (<c>the death's take-back</c>, <c>the start-up audit</c>).</param>
    /// <param name="memory">The worn set's memory (<see cref="WornMemory.Shared"/> by default).</param>
    /// <param name="memoryOnly">M6 7 AE: only phase (1), the remembered set held and not worn; no appraisal, no clothing or caster pick, and the memory is not snapshotted at the end (the death handler's wear after the respawn: a piece on the corpse is still wanted by the take-back).</param>
    public WornRestorer(HuntingGround ground, int level, IReadOnlyDictionary<uint, SpellInfo> spells, ILogSink log, string category, string why, WornMemory? memory = null, bool memoryOnly = false)
    {
        _memoryOnly = memoryOnly;
        _memory = memory ?? WornMemory.Shared;
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(spells);
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _category = category;
        _why = why;
        _rules = new PackRules(ground);
        _ground = ground;
        _spells = spells;
    }

    private readonly HuntingGround _ground;
    private readonly IReadOnlyDictionary<uint, SpellInfo> _spells;

    /// <summary>M6 16 E5e2: the session's one wear judge on the restore's ground (<see cref="Quartermaster.Wardrobe"/>), asked at the first plan: the session is not at hand when the restorer is made.</summary>
    private SelfEquipJudge Judge(GameState state) => _judge ??= Asked(state, out _);

    /// <summary>
    /// M6 16 E5e3: the wardrobe's judge with the restore's element line, written at the first ask (it was the constructor's, read from the rung and the plan with no session at hand: the element is the fight's own choice on
    /// the ground now, which is read from her book), and the loud stub when it reads none while the fight has a line.
    /// </summary>
    private SelfEquipJudge Asked(GameState state, out Quartermaster.WearInputs read)
    {
        var wardrobe = Quartermaster.Wardrobe.For(state);
        var judge = wardrobe.JudgeOn(_ground, _spells, out read);
        if (!_elementLogged)
        {
            _elementLogged = true;
            _log.Info(_category, string.Create(CultureInfo.InvariantCulture, $"gear: restore: element 0x{read.Element:X}: {read.ElementSource}"));
        }

        wardrobe.NoteBlindElement(_log, _category, _ground, read);
        return judge;
    }

    private bool _elementLogged;

    /// <summary>
    /// M6 16 Q10c (<c>TODO(engine-wear-terms)</c>): the judge of the restore's armour and jewellery pick: the session's wear judge with the wear pick's terms of her now (<see cref="Quartermaster.Wardrobe.JudgeWithTerms"/>), asked at
    /// the pick as the caster pick is. The restore only puts on (a free slot, a Wear verdict); a replace the terms would name is the loot step's and the trip's, never the restore's. It is asked only after the rule of before has passed a piece over, so every wear that rule made, and its line, stays. The caster pick keeps its own judge.
    /// </summary>
    private SelfEquipJudge WearableJudge(GameState state) => _wearable ??= Quartermaster.Wardrobe.For(state).JudgeWithTerms(_ground, _spells, out _);

    private SelfEquipJudge? _wearable;

    /// <summary>
    /// M6 14 (<c>m6-caster</c>), M6 16 E5e2: the judge at the restore's caster pick, asked of the wardrobe again there, once, so its caster terms are the character's of that moment (the ladder's weights, the plan's bolt, the
    /// auras the book holds), which the pick's line names.
    /// </summary>
    private SelfEquipJudge CasterJudge(GameState state)
    {
        if (_casterJudged)
        {
            return Judge(state);
        }

        _casterJudged = true;
        _judge = Asked(state, out var read);
        _log.Info(_category, string.Create(CultureInfo.InvariantCulture, $"gear: restore: caster score: {read.Caster}; the bolt: {read.BoltSource}"));
        return _judge;
    }

    private bool _casterJudged;

    /// <summary>
    /// M6 2 e (<c>m5-self-equip-element</c>, the restore's and the audit's call sites): the war element of <paramref name="ground"/> at <paramref name="level"/>, the ladder rung's first (the rung of the level, when it names this ground),
    /// else the spell plan's family, with the source named (<see cref="SelfEquipJudge.ElementOf(string?, string?, out string)"/>); the same rule as the resupply's judge.
    /// </summary>
    /// <param name="ground">The ground.</param>
    /// <param name="level">The character's level.</param>
    /// <param name="source">Where the element came from, or the TODO that names its absence.</param>
    public static uint ElementFor(HuntingGround ground, int level, out string source)
    {
        ArgumentNullException.ThrowIfNull(ground);
        var ladder = LadderLoader.WarMage;
        var rung = level >= 1 && level <= ladder.TopLevel && ladder.RungFor(level) is { } r && (string.Equals(r.GroundKey, ground.Key, StringComparison.Ordinal) || Grounds.RungFallback.Names(r, ground.Key)) ? r : null; // M6 redoubt: the rung's outdoor ground is the rung's too
        var element = SelfEquipJudge.ElementOf(rung?.Element, ground.PlanFor(level)?.Family, out var how);
        source = rung is null ? how + string.Create(CultureInfo.InvariantCulture, $" (no ladder rung of level {level} names {ground.Key})") : how + $" (rung {rung.Key})";
        return element;
    }

    /// <summary>True once the restore has ended (nothing more to put on).</summary>
    public bool IsDone => _done;

    /// <summary>The pieces put on (a caster swapped in counts one).</summary>
    public int Restored { get; private set; }

    /// <summary>The wears refused (by the server, or named before a send).</summary>
    public int Refused { get; private set; }

    /// <summary>One frame: true while an action is in flight or due, false once the set is back.</summary>
    /// <param name="session">The session.</param>
    /// <param name="skills">The character's numbers (null: read from the session's qualities).</param>
    public bool Step(IGameSessionView session, IGearSkills? skills = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_done)
        {
            return false;
        }

        if (_pending is { Outcome: null })
        {
            return true;
        }

        if (_pending is { Outcome: { } outcome })
        {
            var ok = outcome.Kind == ActionOutcomeKind.Confirmed;
            if (_identifying)
            {
                _identifying = false; // an appraisal for the picks below: nothing to count
            }
            else if (_expect != 0)
            {
                Restored += ok ? 1 : 0;
                Refused += ok ? 0 : 1;
                if (ok)
                {
                    PackTally.AddEquip();
                    if (session.State.Objects.TryGet(_expect, out var wielded) && (wielded.ItemType & SelfEquipJudge.CasterType) != 0)
                    {
                        // M6 2 e (slice b's out-of-mana flag): a caster wielded gets its spells back with its mana (Player_Inventory.cs:313-315 @95ef426d), so the flag clears for it
                        CasterSpellsDropped.Shared.Clear(_expect, wielded.Name);
                    }
                }

                _log.Info(_category, $"gear: restore: {_line}: {outcome.Kind}{(outcome.Detail is null ? string.Empty : ": " + outcome.Detail)}");
            }
            else if (ok)
            {
                _log.Info(_category, $"gear: restore: {_line}: {outcome.Kind}"); // M5 7 D139 (k): the held caster's pack, logged
            }
            else if (HeldCaster.Held(session.State))
            {
                _queue.Clear(); // the held caster did not leave the hand: no swap
                _log.Info(_category, $"gear: restore: {_line}: {outcome.Kind}; the swap is not made");
            }
            else
            {
                // M5 7 D139 (l): ACE unwielded it before the pack refused, and its fallback put it in a pack, not back in the hands (HeldCaster's remarks): the hands are empty, so the pick is wielded now.
                _log.Info(_category, $"gear: restore: {_line}: {outcome.Kind}{(outcome.Detail is null ? string.Empty : ": " + outcome.Detail)}; the hands hold no caster now (ACE's fallback): the pick is wielded next");
            }

            _pending = null;
        }

        var state = session.State;

        // M5 7 S2e: the live restore of 2026-10-29 16:21:53 saw "0 usable caster(s)": at a login the session holds appraisals of the worn pieces only, and a caster or wearable with none cannot be judged (its element, bonus and
        // requirements are assessment properties, AppraiseInfo.BuildProperties). Every held caster and wearable with no appraisal is asked about first, one Identify at a time (a read).
        if (_toAppraise is null && _memoryOnly)
        {
            _toAppraise = new Queue<uint>(); // M6 7 AE: the remembered pieces were worn, so they were appraised then; none asked
        }

        if (_toAppraise is null)
        {
            _toAppraise = new Queue<uint>();
            foreach (var item in PackLayout.Items(state))
            {
                if (_toAppraise.Count < AppraisalCap && KeepAsideRule.NeedsAppraisal(item.ItemType) && !state.Appraisals.TryGet(item.Guid, out _))
                {
                    _toAppraise.Enqueue(item.Guid);
                }
            }

            if (_toAppraise.Count > 0)
            {
                _log.Info(_category, string.Create(CultureInfo.InvariantCulture, $"gear: restore: appraising {_toAppraise.Count} held caster(s) and wearable(s) with no appraisal before the picks"));
            }
        }

        if (_toAppraise.Count > 0)
        {
            _identifying = true;
            _pending = session.Actions.Identify(_toAppraise.Dequeue(), ActionSource.Host);
            return true;
        }

        if (_queue.Count == 0)
        {
            Plan(state, skills ?? new QualitiesSkills(state.Self.Qualities));
        }

        if (_queue.Count == 0)
        {
            _done = true;
            if (!_memoryOnly)
            {
                _memory.Snapshot(state);
            }

            _log.Info(_category, string.Create(CultureInfo.InvariantCulture, $"gear: restore ({_why}): worn_restored={Restored} refused={Refused}"));
            return false;
        }

        var (guid, container, location, line) = _queue.Dequeue();
        _line = line;
        if (container != 0)
        {
            _expect = 0;
            _pending = session.Actions.MoveToPack(guid, container, 0, ActionSource.Automation);
        }
        else
        {
            _expect = guid;
            var wield = new Quartermaster.WieldResult();
            _pending = Quartermaster.Wardrobe.Send(session, new Quartermaster.WieldRequest(guid) { Location = location }, wield); // M6 16 E5e1: the one wield routine's send (its check before, the slot, the wield); the check after is this step's own
            if (_pending is null)
            {
                Refused++; // not held, or worn already: nothing was sent, counted and named as a refusal is
                _log.Info(_category, $"gear: restore: {_line}: {wield.Why}");
            }
        }

        return true;
    }

    private void Plan(GameState state, IGearSkills skills)
    {
        var worn = SelfEquipJudge.Worn(state);
        var inventory = state.Self.Inventory;

        // (1) the remembered set
        foreach (var (guid, location) in _memory.Set)
        {
            var place = inventory.Find(guid);
            if (!place.Held || place.Kind == InventoryPlaceKind.Equipped || !_tried.Add(guid) || !state.Objects.TryGet(guid, out var o))
            {
                continue;
            }

            var candidate = SelfEquipJudge.CandidateOf(state, o);
            if (Blocked(candidate, location, worn) is { } blocker)
            {
                Refuse(o.Name, guid, "remembered at 0x" + location.ToString("X8", CultureInfo.InvariantCulture) + ", but " + blocker);
                continue;
            }

            _queue.Enqueue((guid, 0, location, string.Create(CultureInfo.InvariantCulture, $"wear '{o.Name}' 0x{guid:X8} at 0x{location:X8} (the worn set before {_why})")));
            return;
        }

        if (_memoryOnly)
        {
            return;
        }

        // (2) clothing and armour whose coverage is free; (3) the cantrip wearables
        GearCandidate? cloth = null;
        foreach (var item in PackLayout.Items(state))
        {
            if (_tried.Contains(item.Guid) || !state.Objects.TryGet(item.Guid, out var o) || (o.ItemType & SelfEquipJudge.CasterType) != 0)
            {
                continue;
            }

            var c = SelfEquipJudge.CandidateOf(state, o);
            var choice = Judge(state).Judge(c, worn, skills);
            if (choice.Kind != SelfEquipKind.Wear)
            {
                choice = WearableJudge(state).Judge(c, worn, skills); // M6 16 Q10c: where the rule of before passes a piece over, the wear pick's terms, as the trip's pick reads them, may wear it into the free slot (it never removes a decision the rule made)
            }

            if (choice.Kind == SelfEquipKind.Wear)
            {
                _tried.Add(item.Guid);
                _queue.Enqueue((item.Guid, 0, choice.Location, $"wear '{o.Name}' 0x{item.Guid:X8}: {choice.Why}"));
                return;
            }

            if ((o.ItemType & (0x2 | 0x4)) != 0 && c.Coverage != 0 && SelfEquipJudge.FreeLocation(c, worn, out _) is not null
                && (c.Appraisal is null || SelfEquipJudge.Unmet(c.Appraisal, skills) is null) && (cloth is null || BetterCloth(c, cloth)))
            {
                cloth = c;
            }
        }

        if (cloth is not null)
        {
            _tried.Add(cloth.Guid);
            _queue.Enqueue((cloth.Guid, 0, cloth.ValidLocations, string.Create(CultureInfo.InvariantCulture, $"wear '{cloth.Name}' 0x{cloth.Guid:X8}: its coverage 0x{cloth.Coverage:X} is free (armour level {cloth.Appraisal?.ArmorLevel ?? 0}, burden {cloth.Burden})")));
            return;
        }

        // (4) the one best caster
        if (_casterDone)
        {
            return;
        }

        _casterDone = true;
        var casters = new List<GearCandidate>();
        foreach (var item in PackLayout.Items(state))
        {
            if ((item.ItemType & SelfEquipJudge.CasterType) != 0 && state.Objects.TryGet(item.Guid, out var o))
            {
                casters.Add(SelfEquipJudge.CandidateOf(state, o));
            }
        }

        var pick = CasterJudge(state).BestCaster(casters, worn, skills, out var why);
        _log.Info(_category, "gear: restore: " + why.Replace("gear: ", string.Empty, StringComparison.Ordinal));
        if (pick is null)
        {
            return;
        }

        if (pick.Replaces != 0 && state.Self.Qualities.CombatMode is CombatModes.Melee or CombatModes.Missile or CombatModes.Magic)
        {
            // M6 14 (m6-caster): a swap only in peace mode, as retail's player swaps; in a combat mode the restore keeps the caster in the hands and the next visit's gear phase (at home, the station's peace) swaps it
            _log.Info(_category, string.Create(CultureInfo.InvariantCulture, $"gear: restore: no swap in combat mode {state.Self.Qualities.CombatMode}: '{pick.Name}' 0x{pick.Guid:X8} waits for the next visit's gear phase"));
            return;
        }

        if (pick.Replaces != 0)
        {
            // M5 7 D139 (k): only to a pack with a free slot (a wielded caster moved to a full pack ends unwielded in the main pack, the hands empty: HeldCaster's remarks); none, no swap.
            var into = HeldCaster.SwapPack(PackLayout.Of(state, _rules, _ => false, PackMemory.Shared.Rotation.Contains), out var where);
            if (into == 0)
            {
                _log.Info(_category, string.Create(CultureInfo.InvariantCulture, $"gear: restore: no swap: the held caster 0x{pick.Replaces:X8} has no pack to go to: {where}"));
                return;
            }

            PackMemory.Shared.Rotation.Add(pick.Replaces);
            _queue.Enqueue((pick.Replaces, into, 0, string.Create(CultureInfo.InvariantCulture, $"the held caster 0x{pick.Replaces:X8} to {where}")));
        }

        _queue.Enqueue((pick.Guid, 0, SelfEquipJudge.Held, $"wield '{pick.Name}' 0x{pick.Guid:X8}: {pick.Why}"));
    }

    private static string? Blocked(GearCandidate candidate, uint location, IReadOnlyList<WornPiece> worn)
    {
        if (candidate.Coverage != 0)
        {
            return SelfEquipJudge.FreeLocation(candidate, worn, out var blocker) is null ? blocker : null;
        }

        var taken = worn.FirstOrDefault(w => (w.Location & location) != 0);
        return taken is null ? null : $"the worn '{taken.Name}' 0x{taken.Guid:X8} holds 0x{taken.Location & location:X8}";
    }

    private static bool BetterCloth(GearCandidate a, GearCandidate b)
    {
        var (la, lb) = (a.Appraisal?.ArmorLevel ?? 0, b.Appraisal?.ArmorLevel ?? 0);
        return la != lb ? la > lb : a.Burden != b.Burden ? a.Burden < b.Burden : a.Guid < b.Guid;
    }

    private void Refuse(string name, uint guid, string why)
    {
        Refused++;
        _log.Info(_category, $"gear: restore: refused '{name}' 0x{guid:X8}: {why}");
    }
}
