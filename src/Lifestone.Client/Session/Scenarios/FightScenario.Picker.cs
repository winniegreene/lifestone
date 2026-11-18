using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.World;
using Lifestone.Data.Magic;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M5 7 F10 (D90, the owner's option C): the fight's one live picker (<see cref="FightPicker"/>). At each decision with a target picked and the bolt's mana order passed, the fight asks D88's drain-health floor first (F7's
/// <see cref="DrainPicker"/> picks its level), then the picker, which scores the bolt, F9's war options (<see cref="WarSpellPicker"/>), Drain Health Other as an attack and the debuffs of <see cref="FightDebuffs"/> (the profile's
/// <c>opener</c> names the ones a target allows; F8's <see cref="TryBeginOpener"/> casts it) in all-in seconds, and the least wins. D88's stamina floor asks <see cref="FightPicker.StaminaDrainWins"/> whether Drain Stamina Other on the target
/// gives more stamina an all-in second than Revitalize Self. One <c>pick:</c> line when the pick changes; <c>picks=</c> on the verdict line (report-only).
/// </summary>
public sealed partial class FightScenario
{
    /// <summary>The Extras line of the stamina drain (the build learns it with the other extras).</summary>
    public const string StaminaDrainLine = "Drain Stamina Other";

    private static readonly CastRules StaminaDrainRules = CastRules.Utility(3);

    private FightPicker? _picker;
    private FightSnapshot _snap;
    private int _warChoice;
    private double _danceStartAt = double.NaN;
    private uint _danceStartMana;
    private double _refillStartAt = double.NaN;
    private double _refillSeconds;
    private double _healStartAt = double.NaN;
    private uint _healStartHealth;
    private uint _healStartMana;
    private CastAttempt? _staminaDrainCast;
    private CastChatProof? _staminaDrainProof;
    private uint[] _staminaDrainIds = [];
    private double _staminaDrainAsideUntil = double.NegativeInfinity;
    private string _staminaDrainNoted = string.Empty;
    private int _staminaDrains;
    private uint _staminaDrainGuid; // M5 7 F15 (7-F4): the target, the stamina before and the gain expected of the drain in flight (the measure's three inputs)
    private uint _staminaDrainBefore;
    private double _staminaDrainExpected;
    private readonly Dictionary<uint, double> _staminaDrainYield = []; // the share of the expected gain the last drain on a target measured; under StaminaDrainMinYieldShare the target is set aside for this drain

    /// <summary>M5 7 F15 (7-F4): the share of its expected gain under which a measured Drain Stamina Other sets that target aside for the drain (Lifestone's own; ACE writes "You gain 0 points" for a target with no stamina left).</summary>
    public const double StaminaDrainMinYieldShare = 0.25;
    /// <summary>M6 1 f: the key of the measured seconds of a debuff cast at another (<see cref="MeasuredHeals.NoteSeconds"/>, fed by the opener's landed or resisted casts).</summary>
    internal const string DebuffCastKey = "debuff cast at another";

    private readonly Dictionary<uint, int> _wireMaxHealth = []; // M6 1 f: a creature class's maximum health from an appraisal answer (ACE's creature profile, CreatureProfile.cs:47-53 @95ef426d)
    private readonly Dictionary<uint, (uint Guid, IdentifyHandle Handle)> _maxHealthAsks = []; // M6 1 f: the appraisal asked for each class whose maximum health no ground row gives (one a class)
    private readonly HashSet<uint> _maxHealthUnknown = []; // M6 1 f: the classes whose appraisal answered without a health (named once each)
    private bool _debuffSeedNoted;

    /// <summary>The picker (a test reads it).</summary>
    internal FightPicker Picker => _picker ??= NewPicker();

    /// <summary>M5 7 F16: one decide: line every decision. M6 1 f: the debuff rows I to VII from the SpellTable, the levels it lacks named once.</summary>
    private FightPicker NewPicker()
    {
        var rows = DebuffRows.FromSpellTable(_spells, out var missing);
        if (missing.Count > 0)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"pick: {missing.Count} debuff level(s) below VI are not in the SpellTable and are not scored (first: {missing[0]})"));
        }

        return new FightPicker(_log, LogCategory) { LogEveryDecision = true, Rows = rows };
    }

    /// <summary>The Drain Stamina Other casts of the stamina floor that landed.</summary>
    public int StaminaDrains => _staminaDrains;

    /// <summary>The verdict field <c>picks=&lt;spell:count,...&gt;</c> (report-only; <c>picks=none</c> before the first cast).</summary>
    public string PickField() => _picker?.Field() ?? "picks=none";

    /// <summary>
    /// The decision point of <c>Choose</c> (target picked, the bolt's mana order passed): the drain-health floor (D88, F7), then the picker. True when a move other than the bolt began (the phase is that move's); false leaves the bolt or F9's
    /// war option (<see cref="_warChoice"/>) to go.
    /// </summary>
    private bool PickerDecide(IGameSessionView session, double clock, in CellFrame here, uint guid)
    {
        _warChoice = 0;
        var picker = Picker;
        picker.Rates.NoteIncoming(clock, _tally.DamageTaken);
        picker.EmergencyLine = _emergency.Enabled ? _emergency.LineHealth : 0;
        if (_finishGuid != 0)
        {
            return false; // M6 7 Y: the emergency chose the bolt in hand: no drain floor, no debuff, no other war option comes before it
        }

        if (DrainPossible(session) && HealthWait(session, clock, guid))
        {
            return true; // M5 7 F20: a drain could be chosen and the target's health is not fresh: asked of the server first (at most 1 s)
        }

        if (DrainDecide(session, clock, in here, guid))
        {
            NoteDrainStart(session, clock);
            picker.Count(_drain.LastName);
            return true; // D88's floor: Drain Health Other at F7's level
        }

        if (_options.SpellId != 0 || !FillSnapshot(session, clock, in here, guid))
        {
            return TryBeginOpener(session, clock, guid); // no snapshot: F8's rule as it was (the profile's opener is the default)
        }

        // M6 1 f (f10-debuff-levels closed): the debuffs are scored at every level I to VII the SpellTable holds (DebuffRows), so a cap under VI no longer falls back to the opener unscored.
        var pick = picker.Decide(in _snap, clock);
        if (pick.Kind == FightMoveKind.Debuff)
        {
            if (TryBeginOpener(session, clock, guid))
            {
                picker.Count(_openerSpell); // the level F8's rule casts (the plan cap's, the pack's)
                return true;
            }

            // M5 7 F15 (7-F6): the picker chose a debuff but the target's opener is settled (cast, resisted, skipped or under half health), so no debuff will be cast: decide again without the debuffs, and the war picks (the streak,
            // the arc, the ring) are weighed instead of the bolt falling out of the switch below.
            _snap.AllowedDebuffs = 0;
            pick = picker.Decide(in _snap, clock);
        }

        if (pick.Kind == FightMoveKind.DrainHealth && !DrainAttackBeatsHealSelf(session, guid, pick.Level))
        {
            // M5 7 F21 (D92, the owner): a drain is cast only when it would help more than Heal Self; the drain the model scores under it is never cast, so the picker decides again without the drains.
            (_snap.DrainVI, _snap.DrainVII) = (false, false);
            pick = picker.Decide(in _snap, clock);
        }

        switch (pick.Kind)
        {
            case FightMoveKind.DrainHealth when StartDrainAttack(session, clock, guid, pick.Level):
                picker.Count(pick.Spell);
                return true;

            case FightMoveKind.Debuff when TryBeginOpener(session, clock, guid):
                picker.Count(_openerSpell); // the level F8's rule casts (the plan cap's, the pack's)
                return true;

            case FightMoveKind.War:
                _warChoice = WarIndexOf(pick.Spell);
                ReanchorRing(session, clock, in here); // M5 7 F28: the ring is aimed at the fullest tusker it will hit
                return false;

            case FightMoveKind.Bolt when double.IsNaN(pick.AllIn) && TryBeginOpener(session, clock, guid):
                picker.Count(_openerSpell); // the data is missing: the profile's opener is the default (F8)
                return true;

            default:
                return false;
        }
    }

    /// <summary>The levels array and index the bolt path casts: F9's option the picker chose, else the bolt in hand. Counts the cast for <c>picks=</c>.</summary>
    private (uint[] Levels, int Index) WarCastFor()
    {
        _warActive = false;
        if (_warChoice > 0 && _warPicks is { } picks && _warChoice < picks.Options.Count)
        {
            _warActive = true;
            if (picks.Options[_warChoice].Row.Kind == WarKind.Ring && _ringCasts is { } rings)
            {
                rings.Begin(_ringPredicted, _lastClock, _ringAnchorHealth); // M5 7 F22: the hits are counted from ACE's chat lines against this prediction
                _ringOpen = true;
            }

            if (picks.Options[_warChoice].Row.Kind == WarKind.Volley && _volleyCasts is { } volleys)
            {
                volleys.Begin(_volleyPredicted, _lastClock); // M5 7 F24
                _volleyOpen = true;
            }

            if (picks.Options[_warChoice].Row.Kind == WarKind.Streak)
            {
                _streakFinishers++;
                _log.Info(LogCategory, $"streak: a finisher cast (the target live health is under the finisher line, F24); {_streakFinishers} so far");
            }

            Picker.Count(picks.Options[_warChoice].Name);
            return (picks.Options[_warChoice].Levels, 0);
        }

        Picker.Count(_level >= 0 && _spells.TryGetValue(_levelIds[_level], out var bolt) ? bolt.Name : "bolt");
        return (_levelIds, _level);
    }

    private int WarIndexOf(string name)
    {
        if (_warPicks is { } picks)
        {
            for (var i = 1; i < picks.Options.Count; i++)
            {
                if (string.Equals(picks.Options[i].Name, name, StringComparison.Ordinal))
                {
                    return i;
                }
            }
        }

        return 0;
    }

    /// <summary>M6 3 a2: the ground profile's row for the target class, or null. Allocates nothing.</summary>
    private GroundTarget? GroundTargetOf(uint wcid)
    {
        var targets = _ground.Targets;
        for (var i = 0; i < targets.Count; i++)
        {
            if (targets[i].Wcid == wcid)
            {
                return targets[i];
            }
        }

        return null;
    }

    /// <summary>Fills <see cref="_snap"/> from the live state. False when the mage's skills or the target are unknown. Allocation-free once F9's options are built.</summary>
    private bool FillSnapshot(IGameSessionView session, double clock, in CellFrame here, uint guid)
    {
        var q = session.State.Self.Qualities;
        var objects = session.State.Objects;
        if (!objects.TryGet(guid, out var target) || StandsAt(session, target) is not { } at || !GearCasts.TryCurrentSkill(q, MageTemplate.WarMagic, out var war, out _) || !GearCasts.TryCurrentSkill(q, MageTemplate.LifeMagic, out var life, out _))
        {
            return false;
        }

        var creatureTrained = GearCasts.TryCurrentSkill(q, MageTemplate.CreatureEnchantment, out var creature, out _);
        GearCasts.TryCurrentSkill(q, MageTemplate.MeleeDefense, out var melee, out _);
        q.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var healthMax);
        q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax);
        q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax);
        var distance = at.ToGlobal().DistanceTo(here.ToGlobal());
        var (maxHealth, defence) = TargetStatsOf(target.Weenie.WeenieClassId);
        if (GroundTargetOf(target.Weenie.WeenieClassId) is not { MaxHealth: > 0 } && WireMaxHealth(session, guid, target.Weenie.WeenieClassId, target.Name ?? "creature") is var wire and > 0)
        {
            maxHealth = wire; // M6 1 f: an outdoor ground's creatures carry no health row; M6 16 E1b: the appraisal is still asked once a class (the server's own word), and the bestiary's health stands until it answers (TargetStatsOf)
        }
        var fraction = 1f;
        var known = ReadHealth(objects, guid, clock, out var f, out var healthSource, out var healthAge, Threat.HealthReader.FightPicker); // M5 7 F20: the one live model
        if (known)
        {
            fraction = f;
        }

        ref var s = ref _snap;
        (s.Health, s.HealthMax, s.Stamina, s.StaminaMax, s.Mana, s.ManaMax) = (health, healthMax, stamina, staminaMax, mana, manaMax);
        (s.WarSkill, s.LifeSkill, s.CreatureSkill, s.ManaConversion, s.MeleeDefense) = (war, life, creature, _manaConversion, melee);
        (s.TargetGuid, s.TargetName, s.TargetHealthFraction, s.TargetMaxHealth, s.TargetMagicDefense, s.Distance, s.LineClear) = (guid, target.Name ?? "creature", fraction, maxHealth, defence, distance, true);
        (s.TargetHealthSource, s.TargetHealthAge, s.TargetHealthUnknown) = (known ? TargetHealthModel.Name(healthSource) : null, healthAge, !known);
        s.DrainOffWhy = _live.DrainOff(guid, out var offWhy) ? offWhy : null;
        s.TargetHealthDrainResistance = DrainResistOf(target.Weenie.WeenieClassId, target.Name); // M6 14 drain resistance: the drains' and Harm Other's share (FightDebuffs), and no drain offered at 0
        s.TargetStamina = 0; // TODO(f10-target-stamina): the profile carries no stamina; the attack picker does not read it
        FightPicker.FillTargetStats(ref s, GroundTargetOf(target.Weenie.WeenieClassId)); // M6 3 a2: the profile's attributes and swing for this creature, zero when it has none (the debuffs that read them are skipped, named)
        var cap = LevelCapFor(q);
        s.DebuffLevel = Math.Clamp(cap, 1, 7); // M6 1 f: the bolt's cap at this moment (slice a sets it from the ladder; the fight only reads it)
        s.AllowedDebuffs = AllowedDebuffsOf(target.Weenie.WeenieClassId);
        var book = _options.ArenaHere ? null : session.State.Self.Spellbook;
        FillDebuffBook(ref s, book, creatureTrained);
        FillDrainOffer(ref s, q, distance);

        // The bolt and F9's best war option.
        (s.BoltName, s.BoltMeanDamage, s.BoltCastSeconds, s.BoltBaseMana, s.BoltPower, s.BoltLevel) = (string.Empty, 0, 0, 0, 0, _level + 1);
        (s.WarName, s.WarSeconds, s.WarBoltSeconds, s.WarMana, s.WarLevel) = (null, 0, 0, 0, 0);
        WarBuildOptions(q, book, war, session.State, clock);
        _ringPredicted = 0;
        _volleyPredicted = 0;
        _ringCasts?.Tick(clock);
        _volleyCasts?.Tick(clock);
        if (_warPicks is not { Options.Count: > 0 } picks)
        {
            // M6 1 f (f10-picker-data): no war option was built; the bolt's numbers come from the bolt the fight will cast (the SpellTable's mana and power, the projectile row's damage, the cast seconds the code holds).
            if (_level >= 0 && _level < _levelIds.Length && _spells.TryGetValue(_levelIds[_level], out var hand))
            {
                FightPicker.FillBolt(ref s, FallbackWarPicker, hand.Id, hand.Name, hand.BaseMana, hand.Power, war, distance);
            }

            s.DebuffCastSeconds = DebuffCastSeconds(FallbackWarPicker.Settings);
            SeedHealRate(in s);
            return true;
        }

        s.RingFactor = _ringLineText is null ? 0 : picks.Picker.RingFactor.Factor;

        var settings = picks.Picker.Settings;
        var bolt = picks.Options[0];
        s.BoltName = bolt.Name;
        s.BoltMeanDamage = picks.Picker.MeanHit(bolt, war);
        s.BoltCastSeconds = bolt.EffectSeconds + settings.CastOverheadSeconds + (bolt.Row.SpeedMetresPerSecond > 0 ? distance / bolt.Row.SpeedMetresPerSecond : 0);
        (s.BoltBaseMana, s.BoltPower, s.BoltLevel) = (bolt.BaseMana, bolt.Power, bolt.Row.Level);
        s.DebuffCastSeconds = DebuffCastSeconds(settings); // M6 1 f (f10-debuff-gesture closed): the measured seconds of the opener's casts, the bolt's gesture until one is measured
        SeedHealRate(in s);
        if (picks.Options.Count > 1)
        {
            var count = WarGather(session, in here);
            if (count > 0)
            {
                var index = picks.Picker.Choose(picks.Options, _warTargets.AsSpan(0, count), new WarMage(war, _manaConversion, mana, maxHealth), out var seconds, out var boltSeconds, out var missing);
                if (index > 0 && missing == WarSpellPicker.Unusable.None)
                {
                    var o = picks.Options[index];
                    _ringPredicted = o.Row.Kind == WarKind.Ring ? picks.Picker.PredictedHits(o, _warTargets.AsSpan(0, count)) : 0;
                    _volleyPredicted = o.Row.Kind == WarKind.Volley ? picks.Picker.PredictedHits(o, _warTargets.AsSpan(0, count)) : 0;
                    (s.WarName, s.WarSeconds, s.WarBoltSeconds, s.WarLevel) = (o.Name, seconds, boltSeconds, o.Row.Level);
                    s.WarMana = ManaConversionModel.ExpectedCost(o.BaseMana, o.Power, _manaConversion, o.IgnoresManaConversion);
                }
            }
        }

        return true;
    }

    /// <summary>M6 1 f: the bolt's numbers when no war option was built (F9's estimates: the gesture, the overhead, the crit and the rating).</summary>
    private static readonly WarSpellPicker FallbackWarPicker = new(new WarPickSettings());

    private readonly HashSet<string> _pickNoted = new(StringComparer.Ordinal); // M6 1 f: the once-only warnings (a ground's opener line the profile does not hold)

    /// <summary>
    /// M6 1 f (<c>f10-picker-data</c>): the maximum health of a creature class no ground row gives, from the wire. Check: a class answered before, or an appraisal answer held for the guid asked. Act: one appraisal (GameAction 0x00C8,
    /// the Query lane, no hold) of the first creature of the class the picker meets; ACE answers every creature appraisal with the creature profile's health and maximum health, success or not (<c>CreatureProfile.cs:47-53</c>,
    /// <c>AppraiseInfo.cs:568</c> @95ef426d; a class's weenie fixes its maximum health). Check: the answer on a later decision; an ask that completed without a health names the class once, <c>TODO(f10-picker-data)</c>, and is never repeated.
    /// 0 while unknown. M6 16 E1b: the fight's read (<see cref="TargetStatsOf"/>) then gives the bestiary's health, so only a class the bestiary does not hold stays unscored (the picker then keeps the bolt); an answer that differs from
    /// the bestiary's is named once, <c>TODO(engine-bestiary-health-differs)</c>.
    /// </summary>
    private int WireMaxHealth(IGameSessionView session, uint guid, uint wcid, string name)
    {
        if (_wireMaxHealth.TryGetValue(wcid, out var known))
        {
            return known;
        }

        if (_maxHealthUnknown.Contains(wcid))
        {
            return 0;
        }

        var asked = _maxHealthAsks.TryGetValue(wcid, out var ask);
        var answeredGuid = asked ? ask.Guid : guid;
        if (session.State.Appraisals.TryGet(answeredGuid, out var answer) && answer.CreatureHealthMax is { } max && max > 0)
        {
            _wireMaxHealth[wcid] = (int)max;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"pick: {name} (class {wcid}) has a maximum health of {max} by its appraisal (0x{answeredGuid:X8}); the picker scores this class from now on"));
            if (Bestiary.Find(wcid)?.Vitals.MaxHealth is > 0 and var recorded && recorded != (int)max)
            {
                // M6 16 E1b: the server's word and the tracked tables disagree (an enchantment on this one, or a table to regenerate): a finding, said once a class; the appraisal stands
                _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"TODO(engine-bestiary-health-differs): {name} (class {wcid}): its appraisal gives a maximum health of {max}, the bestiary {recorded}; the appraisal stands for this fight"));
            }

            return (int)max;
        }

        if (!asked)
        {
            if (session.State.Objects.TryGet(guid, out _) && session.Actions.Identify(guid, ActionSource.Automation) is { } handle)
            {
                _maxHealthAsks[wcid] = (guid, handle);
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"pick: {name} 0x{guid:X8} (class {wcid}): no ground row gives its maximum health; appraising it once for the class (ACE's creature profile carries it)"));
            }

            return 0;
        }

        if (ask.Handle.IsCompleted)
        {
            _maxHealthUnknown.Add(wcid);
            var stays = Bestiary.Find(wcid)?.Vitals.MaxHealth is > 0 and var recorded
                ? string.Create(CultureInfo.InvariantCulture, $"the bestiary's {recorded} stands, named once") // M6 16 E1b: the class is scored on the tracked tables' health
                : "this class stays unscored (the bolt), named once";
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"TODO(f10-picker-data): {name} (class {wcid}): the appraisal of 0x{ask.Guid:X8} ended ({ask.Handle.Outcome?.Kind.ToString() ?? "no outcome"}) with no maximum health; {stays}"));
        }

        return 0;
    }

    /// <summary>M6 1 f: a bit per debuff kind whose spell at the cap level the spellbook holds, with its school trained (Life Magic is, or the snapshot was not filled); none read in the arena (no book).</summary>
    private void FillDebuffBook(ref FightSnapshot s, Spellbook? book, bool creatureTrained)
    {
        s.KnownDebuffs = 0;
        s.BookRead = book is not null;
        if (book is null)
        {
            return;
        }

        var rows = Picker.Rows;
        var level = Math.Min(s.DebuffLevel, 7);
        foreach (var kind in DebuffKinds)
        {
            if (rows.Find(kind, level) is { } spell && book.Knows(spell.Id) && (spell.School == DebuffSchool.LifeMagic || creatureTrained))
            {
                s.KnownDebuffs |= FightPicker.Bit(kind);
            }
        }
    }

    private static readonly DebuffKind[] DebuffKinds = Enum.GetValues<DebuffKind>();

    /// <summary>
    /// M6 1 f (<c>f10-debuff-gesture</c> closed): a debuff's cast seconds are the measured mean of the opener's casts at another (<see cref="DebuffCastKey"/>); until one is measured, the bolt's gesture and overhead (the seed, named once).
    /// </summary>
    private double DebuffCastSeconds(WarPickSettings settings)
    {
        var measured = _heals.Seconds(DebuffCastKey, double.NaN);
        if (!double.IsNaN(measured))
        {
            return measured;
        }

        var seed = WarSpellData.EffectSeconds(WarKind.Bolt) + settings.CastOverheadSeconds;
        if (!_debuffSeedNoted)
        {
            _debuffSeedNoted = true;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"pick: a debuff's cast seconds are the bolt's gesture and overhead ({seed:0.00} s) until an opener's cast is measured"));
        }

        return seed;
    }

    /// <summary>The debuff kinds the target's profile entry allows: its <c>opener</c> line (the hint and the default; F8).</summary>
    private uint AllowedDebuffsOf(uint wcid)
    {
        if (_options.OpenerTries <= 0 || _options.ArenaHere || _ground.OpenerOf(wcid) is not { } line)
        {
            return 0;
        }

        var spells = FightDebuffs.Spells;
        for (var i = 0; i < spells.Count; i++)
        {
            if (spells[i].Name.StartsWith(line, StringComparison.Ordinal))
            {
                return FightPicker.Bit(spells[i].Kind);
            }
        }

        return 0;
    }

    /// <summary>
    /// Which Drain Health Other levels (VI 1242, VII 2328) are offered now: known, paid for, the mana over the floor and the target in range; M6 14 drain resistance: none on a target whose kind lets no drain through
    /// (<see cref="FightSnapshot.TargetHealthDrainResistance"/> 0).
    /// </summary>
    private void FillDrainOffer(ref FightSnapshot s, PlayerQualities q, double distance)
    {
        (s.DrainVI, s.DrainVII) = (false, false);
        if (_drain.Count == 0 || s.TargetHealthDrainResistance is <= 0 || !q.TryGetSkill(MageTemplate.LifeMagic, out var lifeSkill))
        {
            return;
        }

        _drain.Floors(_manaConversion, _options.Mana, _manaReserve, _drainFloors);
        var levels = _drain.Levels;
        for (var i = 0; i < levels.Length; i++)
        {
            if (s.Mana < _drainFloors[i] || distance > CastRange(levels[i].Info, lifeSkill.Init + lifeSkill.Ranks))
            {
                continue;
            }

            if (levels[i].Info.Id == 1242)
            {
                s.DrainVI = true;
            }
            else if (levels[i].Info.Id == 2328)
            {
                s.DrainVII = true;
            }
        }
    }

    /// <summary>Seeds the heal's seconds a health point from Drain Health Other VI's yield on this target at the current skill (cast, mana at the dance's rate, over the expected heal), until a drain or heal is measured.</summary>
    private void SeedHealRate(in FightSnapshot s)
    {
        var rates = Picker.Rates;
        if (rates.HealthSamples > 0 || s.TargetMaxHealth <= 0 || FightDebuffs.Find(DebuffKind.DrainHealthOther, 6) is not { } drain)
        {
            return;
        }

        var drained = Math.Min(Math.Round(s.TargetMaxHealth * drain.Proportion), drain.TransferCap);
        var heal = Math.Round(drained * (1.0 - drain.LossPercent)) * FightDebuffs.Chance(s.LifeSkill, s.TargetMagicDefense);
        var mana = ManaConversionModel.ExpectedCost(drain.BaseMana, drain.Power, _manaConversion);
        rates.SeedHeal((s.DebuffCastSeconds + (mana * rates.SecondsPerMana)) / heal);
    }

    /// <summary>True when a drain could be chosen now: a level is known, the mage is hurt (at full health a drain heals nothing), the target is not set off, and the fight is not a one-spell run.</summary>
    private bool DrainPossible(IGameSessionView session) =>
        _drain.Count > 0 && _options.SpellId == 0 && !_options.ArenaHere && session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var healthMax) && health < healthMax;

    /// <summary>
    /// M5 7 F21 (D92): the picker chose Drain Health Other as an attack at <paramref name="level"/> (6 or 7) on <paramref name="guid"/>; true when that drain scores more than Heal Self in health a second (<see cref="HealPick"/>) at the
    /// snapshot's numbers. False when the offered level is not found (the cast would fail anyway) or Heal Self scores at least as much.
    /// </summary>
    private bool DrainAttackBeatsHealSelf(IGameSessionView session, uint guid, int level)
    {
        var id = level == 7 ? 2328u : 1242u;
        var levels = _drain.Levels;
        for (var i = 0; i < levels.Length; i++)
        {
            if (levels[i].Info.Id == id)
            {
                ref readonly var s = ref _snap;
                var targetHealth = (uint)Math.Round(s.TargetMaxHealth * Math.Clamp(s.TargetHealthFraction, 0, 1));
                var life = GearCasts.TryCurrentSkill(session.State.Self.Qualities, MageTemplate.LifeMagic, out var current, out _) ? current : (uint)Math.Round(s.LifeSkill);
                var situation = new DrainSituation(s.Health, s.HealthMax, s.Mana, life, 0, targetHealth, (uint)Math.Round(s.TargetMagicDefense), s.Distance, s.TargetHealthDrainResistance ?? 1.0);
                return DrainBeatsHealSelf("pick", in levels[i], in situation, guid, out _, out _);
            }
        }

        return true;
    }

    /// <summary>Starts Drain Health Other as an attack at <paramref name="level"/> (6 or 7) on <paramref name="guid"/>.</summary>
    private bool StartDrainAttack(IGameSessionView session, double clock, uint guid, int level)
    {
        var id = level == 7 ? 2328u : 1242u;
        var levels = _drain.Levels;
        for (var i = 0; i < levels.Length; i++)
        {
            if (levels[i].Info.Id == id)
            {
                _killMark = _tally.Kills;
                _targetKillMark = _tally.TargetKills;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"health: drain as an attack: {levels[i].Info.Name} on 0x{guid:X8}"));
                BeginYield(session, i, guid, clock);
                _drain.Start(session, i, guid, clock, attack: true);
                NoteDrainStart(session, clock);
                _phase = Phase.Drain;
                return true;
            }
        }

        return false;
    }

    /// <summary>A drain or heal begins: its start is kept for the heal's measured rate.</summary>
    private void NoteDrainStart(IGameSessionView session, double clock)
    {
        var q = session.State.Self.Qualities;
        q.TryGetVitalLevels(MageTemplate.MaxHealth, out _healStartHealth, out _);
        q.TryGetVitalLevels(MageTemplate.MaxMana, out _healStartMana, out _);
        _healStartAt = clock;
    }

    /// <summary>A drain or heal ended: a landing is a sample of the heal's seconds a health point (its seconds and its mana at the dance's rate, over the health gained).</summary>
    private void NoteDrainEnded(IGameSessionView session, double clock, bool landed)
    {
        if (double.IsNaN(_healStartAt) || !landed)
        {
            _healStartAt = double.NaN;
            return;
        }

        var q = session.State.Self.Qualities;
        q.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out _);
        q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _);
        var rates = Picker.Rates;
        var spent = _healStartMana > mana ? _healStartMana - mana : 0;
        rates.NoteHeal((clock - _healStartAt) + (spent * rates.SecondsPerMana), health > _healStartHealth ? health - _healStartHealth : 0);
        _healStartAt = double.NaN;
    }

    /// <summary>A dance begins: its start is kept for the dance's measured rate.</summary>
    private void NoteDanceStart(uint mana, double clock) => (_danceStartAt, _danceStartMana) = (double.IsNaN(_danceStartAt) ? clock : _danceStartAt, double.IsNaN(_danceStartAt) ? mana : _danceStartMana);

    /// <summary>A dance landed: a sample of the dance's seconds a mana point (its presses and the refills since the last sample, over the net mana).</summary>
    private void NoteDanceLanded(uint mana, double clock)
    {
        if (!double.IsNaN(_danceStartAt) && mana > _danceStartMana)
        {
            Picker.Rates.NoteDance((clock - _danceStartAt) + _refillSeconds, mana - _danceStartMana);
        }

        (_danceStartAt, _refillSeconds) = (double.NaN, 0);
    }

    /// <summary>A Revitalize Self cast begins or ends: its seconds count toward the next dance's sample.</summary>
    private void NoteRefill(double clock, bool ended)
    {
        if (!ended)
        {
            _refillStartAt = clock;
        }
        else if (!double.IsNaN(_refillStartAt))
        {
            _refillSeconds += clock - _refillStartAt;
            _refillStartAt = double.NaN;
        }
    }

    /// <summary>
    /// D88's stamina floor with the owner's addition: when the order says Revitalize Self, a living target is in reach (range and line) and Drain Stamina Other gives more stamina an all-in second
    /// (<see cref="FightPicker.StaminaDrainWins"/>), the drain is cast at the target instead. True when it began (<see cref="ManaPhase.StaminaDrain"/>).
    /// </summary>
    private bool TryStaminaDrain(IGameSessionView session, double clock, int refillIndex, uint stamina, uint staminaMax)
    {
        if (clock < _staminaDrainAsideUntil || _options.ArenaHere || _target == 0 || refillIndex < 0 || refillIndex >= _refill.Levels.Length || session.State.Self.Position is not { } here)
        {
            return false;
        }

        var objects = session.State.Objects;
        if (_killed.Contains(_target) || TargetGone(objects, _target) || !objects.TryGet(_target, out var target) || StandsAt(session, target) is not { } at)
        {
            return false;
        }

        var yieldShare = 1.0;
        if (_staminaDrainYield.TryGetValue(_target, out var shown))
        {
            if (shown < StaminaDrainMinYieldShare)
            {
                NoteStaminaDrain(string.Create(CultureInfo.InvariantCulture, $"pick: Drain Stamina Other is set aside for 0x{_target:X8} (the last one gave {shown:P0} of its expected gain); Revitalize Self"));
                return false;
            }

            yieldShare = shown; // the measured share scales the drain's score
        }

        if (ArenaProfile.ExtraNamed(StaminaDrainLine) is not { } line)
        {
            NoteStaminaDrain("pick: TODO(f10-stamina-drain-line): the profile holds no Drain Stamina Other line; Revitalize Self");
            return false;
        }

        var q = session.State.Self.Qualities;
        var book = session.State.Self.Spellbook;
        if (!GearCasts.TryCurrentSkill(q, MageTemplate.LifeMagic, out var life, out _) || !q.TryGetSkill(MageTemplate.LifeMagic, out var lifeSkill))
        {
            return false;
        }

        var held = LevelFloor.HeldForAccount(session.State, line.School, _buffOptions.AccountName); // M6 1 h1
        var index = ChooseOpenerLevel(line, _spells, book, life, _buffOptions.CastChance, LevelCapFor(q), held is null ? null : info => LevelFloor.ShortOf(held, info, out var need, _buffOptions.AccountName) ? need : null, out var why);
        if (index < 0)
        {
            NoteStaminaDrain("pick: Drain Stamina Other is not cast (" + why + "); Revitalize Self");
            return false;
        }

        var info = _spells[line.Spells[index].Id];
        if (Picker.Rows.Find(DebuffKind.DrainStaminaOther, index + 1) is not { } drain) // M6 1 h1 (f10-stamina-drain-row closed): the picker's rows, levels I to VII (DebuffRows.FromSpellTable)
        {
            NoteStaminaDrain("pick: Drain Stamina Other level " + (index + 1).ToString(CultureInfo.InvariantCulture) + " has no transfer row (the SpellTable lacks it: DebuffRows.FromSpellTable named it); Revitalize Self");
            return false;
        }

        var distance = at.ToGlobal().DistanceTo(here.ToGlobal());
        if (distance > CastRange(info, lifeSkill.Init + lifeSkill.Ranks) || (!_options.IgnoreLine && !LineIsClear(session, in here, _target)) || !FillSnapshot(session, clock, in here, _target))
        {
            return false;
        }

        if (_snap.TargetStamina <= 0)
        {
            // TODO(f10-target-stamina): the ground profile gives no stamina for the target; taken as enough for the cap to bind (named, as F7 names an unknown target's health).
            _snap.TargetStamina = drain.TransferCap / drain.Proportion;
            NoteStaminaDrain("pick: TODO(f10-target-stamina): the target's stamina is unknown; Drain Stamina Other is scored with the cap binding");
        }

        var revitalize = _refill.Levels[refillIndex];
        var boost = 0.0;
        foreach (var row in BuffStatMods.Revitalize)
        {
            if (row.Id == revitalize.SpellId)
            {
                boost = Math.Min(row.Boost + (row.Variance / 2.0), staminaMax > stamina ? staminaMax - stamina : 0);
            }
        }

        var revitalizeMana = ManaConversionModel.ExpectedCost(revitalize.BaseMana, revitalize.Power, _manaConversion);
        var mana = ManaConversionModel.ExpectedCost(drain.BaseMana, drain.Power, _manaConversion);
        if (_snap.Mana < mana || !Picker.StaminaDrainWinsMeasured(in _snap, in drain, boost, GearCasts.Chance(life, revitalize.Power), revitalizeMana, yieldShare, out var drainRate, out var revitalizeRate, out var gainIfLanded))
        {
            return false;
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"pick: {info.Name} on {target.Name ?? "creature"} 0x{_target:X8} | stamina {drainRate:0.0} a second vs Revitalize Self {revitalizeRate:0.0} | hit {FightDebuffs.Chance(life, _snap.TargetMagicDefense) * 100:0}% | mana {mana:0.#} | why the stamina floor: the drain refills faster"));
        StartStaminaDrain(session, clock, _target, line, index, info.Name, stamina, gainIfLanded);
        return true;
    }

    /// <summary>Sends a Drain Stamina Other at <paramref name="guid"/>; <see cref="StaminaDrainStep"/> reads what it gave.</summary>
    private void StartStaminaDrain(IGameSessionView session, double clock, uint guid, ProfileLine line, int index, string name, uint stamina, double gainIfLanded)
    {
        if (_staminaDrainIds.Length != line.Spells.Count)
        {
            _staminaDrainIds = new uint[line.Spells.Count];
        }

        for (var i = 0; i < line.Spells.Count; i++)
        {
            _staminaDrainIds[i] = line.Spells[i].Id;
        }

        Picker.Count(name);
        _staminaDrainCast ??= new CastAttempt(session.Actions, session.State, _log, _spells);
        _staminaDrainProof ??= new CastChatProof(session.State, _spells);
        (_staminaDrainGuid, _staminaDrainBefore, _staminaDrainExpected) = (guid, stamina, gainIfLanded);
        _staminaAskAt.Remove(guid); // M6 7 AN: its stamina is read again after the cast
        _staminaDrainCast.Start(new CastRequest { Levels = _staminaDrainIds, Index = index, Target = guid, Rules = StaminaDrainRules, Purpose = "fight stamina drain", Proof = _staminaDrainProof }, clock);
        _manaPhase = ManaPhase.StaminaDrain;
    }

    /// <summary>The stamina drain's cast ends: counted; a cast that did not land (not a resist) sets the drain aside <see cref="FightRefill.AsideSeconds"/>; the order decides again.</summary>
    private ScenarioStatus StaminaDrainStep(IGameSessionView session, double clock)
    {
        var cast = _staminaDrainCast!;
        var end = cast.Step(clock);
        if (end == CastEnd.Running)
        {
            return ScenarioStatus.Running;
        }

        var report = cast.Report;
        _castTally.Add(in report);
        if (end is CastEnd.Cancelled or CastEnd.DryRun)
        {
            return Fail(session, "fight: the Drain Stamina Other cast was not carried out: " + (cast.LastOutcome is { } stopped ? Describe(stopped) : end + ": " + report.Why));
        }

        session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax);
        if (end.Succeeded())
        {
            _staminaDrains++;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: Drain Stamina Other landed; stamina {stamina} of {staminaMax} now"));
            MeasureStaminaDrain(stamina);
        }
        else if (end is not (CastEnd.Resisted or CastEnd.InTheAir)) // M6 14 airborne: held in the air is the moment's: the drain is not set aside
        {
            _staminaDrainAsideUntil = clock + FightRefill.AsideSeconds;
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: Drain Stamina Other did not land ({end.Name()}: {report.Why}); set aside {FightRefill.AsideSeconds:0} s: Revitalize Self"));
        }

        NoteManaLoopAfter(session, "drain", end.Succeeded()); // M6 8 H: the check after the act: what it really gave
        ReadRefill(session);
        _manaPhase = ManaPhase.Decide;
        return ScenarioStatus.Running;
    }

    /// <summary>
    /// M5 7 F15 (7-F4): the landed drain's gain is the stamina after minus the stamina before. ACE writes its landing line even when the target had nothing to give ("You gain 0 points", WorldObject_Magic.cs 855), so the line proves
    /// the cast, not the yield. The share of the expected gain is kept for that target: scoring uses it, and a share under <see cref="StaminaDrainMinYieldShare"/> sets the target aside for the drain (Revitalize Self takes over).
    /// </summary>
    private void MeasureStaminaDrain(uint stamina)
    {
        if (_staminaDrainGuid == 0 || _staminaDrainExpected <= 0)
        {
            return;
        }

        NoteStaminaDrainYield(_staminaDrainGuid, _staminaDrainBefore, _staminaDrainExpected, stamina);
        _staminaDrainGuid = 0;
    }

    /// <summary>Keeps the measured share of one landed drain (the stamina after minus before, over the expected gain) for its target and logs it; the seam the tests drive.</summary>
    internal void NoteStaminaDrainYield(uint guid, uint before, double expected, uint after)
    {
        var gain = after > before ? after - before : 0u;
        var share = Math.Clamp(gain / expected, 0, 1);
        _staminaDrainYield[guid] = share;
        _drainedFrom[guid] = _drainedFrom.GetValueOrDefault(guid) + gain; // M6 14: an unread creature's assumed stamina is its kind's read less this
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: Drain Stamina Other on 0x{guid:X8} gave {gain} stamina of the {expected:0.#} expected ({share:P0}){(share < StaminaDrainMinYieldShare ? "; set aside for this target: Revitalize Self takes over" : string.Empty)}"));
    }

    /// <summary>True when the last measured drain on <paramref name="guid"/> gave under <see cref="StaminaDrainMinYieldShare"/> of its expected gain: the drain is set aside for that target.</summary>
    internal bool StaminaDrainSetAside(uint guid) => _staminaDrainYield.TryGetValue(guid, out var share) && share < StaminaDrainMinYieldShare;

    /// <summary>Forgets the measured yields of the targets that left the object table (the list stays as long as the creatures in view).</summary>
    private void PruneStaminaYield(WorldObjectTable objects)
    {
        if (_staminaDrainYield.Count == 0)
        {
            return;
        }

        foreach (var guid in _staminaDrainYield.Keys)
        {
            if (!objects.TryGet(guid, out _))
            {
                _staminaDrainYield.Remove(guid);
                _drainedFrom.Remove(guid);
                return; // one a frame: no allocation, the next frame takes the next
            }
        }
    }

    private void NoteStaminaDrain(string text)
    {
        if (!string.Equals(_staminaDrainNoted, text, StringComparison.Ordinal))
        {
            _staminaDrainNoted = text;
            _log.Info(LogCategory, text);
        }
    }

    /// <summary>The opener landed: the debuff goes in the picker's ledger with its magnitude and its duration from now.</summary>
    private void NoteOpenerLanded(uint guid, string spell, double clock)
    {
        if (Picker.Rows.Named(spell) is { } row) // M6 1 f: every level I to VII the rows hold
        {
            Picker.NoteLanded(guid, row.Kind, row.Magnitude, clock + row.DurationSeconds);
        }
    }
}
