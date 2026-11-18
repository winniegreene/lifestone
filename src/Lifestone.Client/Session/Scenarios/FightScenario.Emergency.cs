using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M5 7 F16 (D91, the owner): the emergency drain. The fight never retreats on the den. When the health falls under the live emergency line (<see cref="FightEmergency"/>) the mage targets the living tusker in reach (range and line) with
/// the most health left and casts Drain Health Other I at it, always level I (one gesture, no wind-up: it lands before the next hits and repeats at once), and again after each cast at the fullest tusker then in reach, until the health is at
/// <c>--fight-emergency-safe</c>; only then the picker's normal choices (the higher drains included) apply. With no tusker in reach it casts Heal Self as the drain rule does. It is the first step of <c>Choose</c>: it outranks the stamina
/// refill, the opener, the picker's war spells and self-defence's target choice. Any ground (D46): the line, the target list and the range are the profile's and the spell's.
/// </summary>
public sealed partial class FightScenario
{
    /// <summary>The Drain Health Other level the emergency always casts (id 1237, level I).</summary>
    public const uint EmergencySpellId = 1237;

    private const int EmergencyCandidateCap = 16;
    private const string ResistSuffix = " resists your spell"; // ACE's line, WorldObject_Magic.cs 192 @95ef426d (FightTally reads the same)

    private readonly FightEmergency _emergency;
    private readonly uint[] _emergencyGuids = new uint[EmergencyCandidateCap];
    private readonly float[] _emergencyHealth = new float[EmergencyCandidateCap];
    private readonly float[] _emergencyDistance = new float[EmergencyCandidateCap];
    private readonly bool[] _emergencyBlocked = new bool[EmergencyCandidateCap];
    private bool _emergencyCasting;
    private double _emergencyCastAt;
    private bool _emergencyUnavailableNoted;
    private readonly MeasuredHeals _heals = new();
    private readonly int[] _emergencyIndex = new int[8];
    private readonly HashSet<uint> _drainedSinceAnswer = [];
    private double _emergencyHealSeconds = double.NaN;
    private IDisposable? _healChatSubscription;
    private readonly Dictionary<uint, (ActionHandle Handle, double At)> _healthAsks = [];
    private readonly double[] _emergencyFormula = new double[8];
    private uint _finishGuid;
    private int _emergencyUndrainable;
    private int _emergencyFinishes;
    private string _castSpell = string.Empty;
    private double _castFormula;
    private int _boltResists;
    private int _boltResistMark;
    private bool _finishInFlight;
    private bool _finishHoldNoted;

    /// <summary>M5 7 F19: a wound-up drain level is offered only when its cast seconds times this are within the seconds the mage's health lasts at the incoming rate (Lifestone's own margin).</summary>
    public const double EmergencySecondsMargin = 2.0;

    /// <summary>M5 7 F19: the longest the emergency waits for the server's answer to a target's health before it chooses on the client's last value (Lifestone's own; a localhost answer takes well under it).</summary>
    public const double EmergencyQuerySeconds = 1.0;

    /// <summary>
    /// M5 7 F19: the health taken for a target whose maximum health is known nowhere (Lifestone's own, as F7's large-target assumption, <c>TODO(m5-drain-target-stats)</c>). M6 16 E1b: that is no longer every creature
    /// outside the ground's list: the fight's read (<see cref="TargetStatsOf"/>) gives the bestiary's health, so this stands only for a class the bestiary does not hold, or holds with no maximum health, before its appraisal:
    /// named once a class, <c>TODO(engine-bestiary-unknown-kind)</c>.
    /// </summary>
    public const int EmergencyUnknownTargetHealth = 400;

    /// <summary>M6 7 Y: a creature of the targets this near the mage counts as a second attacker (Lifestone's own: melee reach 0.75 m, ACE's <c>MaxMeleeRange</c>, plus about a second of a tusker's run).</summary>
    public const double SecondAttackerMetres = 5;

    /// <summary>M6 7 Y: true from the emergency's choice of the finishing bolt until the next pick, M6 7 AA: and while a bolt already in flight is the one that finishes: the retreat rule waits for it (<see cref="RetreatInput.Finishing"/>).</summary>
    public bool FinishingBolt => _finishGuid != 0 || (_finishInFlight && _phase == Phase.Hit);

    /// <summary>M6 7 Y: the finishing bolts the emergency chose.</summary>
    public int EmergencyFinishes => _emergencyFinishes;

    /// <summary>M5 7 F19 (D92): the measured heals (a test reads and seeds them).</summary>
    internal MeasuredHeals MeasuredHeals => _heals;

    /// <summary>The emergency drains that landed (<c>emergency=</c>, report-only).</summary>
    public int EmergencyDrains => _emergency.Landed;

    /// <summary>The emergency's state (a test reads it).</summary>
    internal FightEmergency Emergency => _emergency;

    /// <summary>The offered index of Drain Health Other I among <c>_drain.Levels</c>, or -1 when it is not offered (not known, not paid for or no row).</summary>
    private int EmergencyLevelIndex()
    {
        var levels = _drain.Levels;
        for (var i = 0; i < levels.Length; i++)
        {
            if (levels[i].Info.Id == EmergencySpellId)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// True when the emergency holds now: health known, under the live line (or inside the hysteresis up to the safe line) and not set aside. Logs the line when it has moved by more than five points.
    /// </summary>
    private OpenContainerPreempt? _emergencyPreempt;

    /// <summary>
    /// M6 14 recall unblock (m6-recall-preempt; live 2026-11-14 17:31:54 UTC: the Health Tonic waited its turn behind the Use lane and a cast's hold, and its own answer was lost): the emergency's drink and heal go before the loot. An open
    /// ground container (a corpse's view) is closed first and the server's answer checked (<see cref="OpenContainerPreempt"/>); true while the close is out (the emergency sends nothing yet).
    /// </summary>
    private bool EmergencyPreempts(IGameSessionView session, double clock)
    {
        _emergencyPreempt ??= new OpenContainerPreempt(session.Actions, session.State, _log, LogCategory, "emergency");
        return _emergencyPreempt.Step("the emergency's drink or heal", clock);
    }

    private bool EmergencyWanted(IGameSessionView session, double clock, out uint health, out uint healthMax)
    {
        health = healthMax = 0;
        if (!_emergency.Enabled || _options.ArenaHere || !session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out health, out healthMax))
        {
            return false;
        }

        var incoming = Picker.Rates.Read(clock).PerSecond; // M6 7 Y: the recent hits, the retreat rule's number
        var wanted = _emergency.Wanted(health, healthMax, incoming, clock);
        if (_emergency.LineNote(healthMax, incoming) is { } note)
        {
            _log.Info(LogCategory, note);
        }

        return wanted;
    }

    /// <summary>
    /// The first step of <c>Choose</c>: true when an emergency cast began (the phase is the drain's) or the target's health is being asked first. M5 7 F19 (D92, the owner: do the thing that WORKS): the heal is chosen from the MEASURED
    /// results (<see cref="MeasuredHeals"/>, ACE's own chat lines), never the spell table alone: every offered Drain Health Other level at the fullest tusker in reach and Heal Self are scored by expected health a second of casting
    /// (the measured mean, seeded from ACE's formula with the target's REAL health, asked of the server before the choice when the mage has drained that target since its last answer) and scored in health a second by <see cref="HealPick"/> (M5 7 F21, D92: the chance it lands, the cast seconds and the mana's time at the dance's rate, a small credit for the damage a drain does); the most wins, a tie and a drain under Heal Self go to Heal Self, and with no tusker in reach Heal Self. False leaves the rest of <c>Choose</c> (the old drain rule, the stamina order, the picker, the bolt) to go.
    /// </summary>
    private bool EmergencyStepOwn(IGameSessionView session, double clock, in CellFrame here) // M6 16 E3.0: the old EmergencyStep, unchanged; the name is now its wrapper with the survival layer's ask beside it (FightScenario.SurvivalShadow.cs)
    {
        if (!EmergencyWanted(session, clock, out var health, out var healthMax))
        {
            // M6 8: set aside after failed casts, the health still under its line: a held health drink goes all the same (it is no cast and costs no mana)
            return healthMax > 0 && _emergency.AsideNow(clock) && (EmergencyPreempts(session, clock) || EmergencyPotionStep(session, clock, health, healthMax));
        }

        if (EmergencyPreempts(session, clock))
        {
            return true; // M6 14 recall unblock (m6-recall-preempt): an open corpse is closed first, then the drink or the heal
        }

        if (EmergencyPotionStep(session, clock, health, healthMax))
        {
            return true; // M6 7 AR: a held health potion goes first: no mana, no fizzle
        }

        var q = session.State.Self.Qualities;
        if (!q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _) || !q.TryGetSkill(MageTemplate.LifeMagic, out var lifeSkill))
        {
            return false;
        }

        var levels = _drain.Levels;
        if (levels.Length == 0 && !_drain.HasHeal)
        {
            if (!_emergencyUnavailableNoted)
            {
                _emergencyUnavailableNoted = true;
                _log.Warn(LogCategory, "emergency: no Drain Health Other level and no Heal Self is offered (not in the spellbook, not paid for by the pack's scarabs and tapers, or no transfer row); the drain rule decides instead (TODO(f16-drain-one-supplies))");
            }

            return false;
        }

        var initPlusRanks = lifeSkill.Init + lifeSkill.Ranks;
        var reach = 0f;
        for (var i = 0; i < levels.Length; i++)
        {
            reach = Math.Max(reach, CastRange(levels[i].Info, initPlusRanks));
        }

        if (levels.Length > 0 && !AssumesHealth(session) && AskHealths(session, in here, reach, clock))
        {
            return true; // the candidates' health is asked of the server first: an unknown health is never taken as full (D92)
        }

        var healthFraction = 0f;
        var distance = 0f;
        _emergencyUndrainable = 0;
        var best = levels.Length == 0 ? 0 : EmergencyTarget(session, in here, reach, out healthFraction, out distance);
        var objects = session.State.Objects;
        var name = "creature";
        var targetMax = 0;
        var resist = 1.0;
        if (best != 0 && objects.TryGet(best, out var target))
        {
            (name, targetMax) = (target.Name ?? "creature", TargetStatsOf(target.Weenie.WeenieClassId).MaxHealth);
            resist = DrainResistOf(target.Weenie.WeenieClassId, target.Name); // M6 14 drain resistance: the kind's share of a drain's take (EmergencyTarget offers no kind that lets nothing through)
        }

        var targetHealth = (uint)Math.Round(healthFraction * (targetMax > 0 ? targetMax : EmergencyUnknownTargetHealth));
        var missing = healthMax > health ? healthMax - health : 0u;
        var life = GearCasts.TryCurrentSkill(q, MageTemplate.LifeMagic, out var current, out _) ? current : initPlusRanks; // the chance is read at the skill now, buffs up
        var defence = best != 0 && objects.TryGet(best, out var defender) ? (uint)TargetStatsOf(defender.Weenie.WeenieClassId).Defence : 0u;
        var count = 0;
        var spared = 0;
        var read = Picker.Rates.Read(clock);
        if (read.NextHitKills(health))
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"emergency: the next hit kills: the biggest hit seen is {read.BiggestHit:0} against health {health}/{healthMax} (incoming {read.PerSecond:0.0} a second, a hit every {read.IntervalSeconds:0.0} s, the last {read.SinceLastHit:0.0} s ago)"));
        }

        if (best != 0)
        {
            var secondsLeft = health / Math.Max(read.PerSecond, FightEmergency.SeedIncomingPerSecond);
            var situation = new DrainSituation(health, healthMax, mana, life, initPlusRanks, targetHealth, defence, distance, resist);
            for (var i = 0; i < levels.Length && count < _healCandidates.Length; i++)
            {
                if (mana < GearCasts.ManaCeiling(levels[i].Info, 0) || distance > CastRange(levels[i].Info, initPlusRanks))
                {
                    continue;
                }

                if (WayOutSparesDrain(session, clock, mana, GearCasts.ManaCeiling(levels[i].Info, 0), read.NextHitKills(health)))
                {
                    spared++; // M6 7 BE: passed over, so that a cheaper level may still go
                    continue;
                }

                var candidate = DrainCandidate(in levels[i], in situation, best, out var formula);
                if (count > 0 && candidate.CastSeconds * EmergencySecondsMargin > secondsLeft)
                {
                    continue; // a wound-up level only when the mage has the seconds (the line's damage rate against the health left)
                }

                _emergencyFormula[count] = formula;
                _healCandidates[count] = candidate;
                _emergencyIndex[count++] = i;
            }
        }

        // M5 7 F21 (D92, the owner): Heal Self against each drain candidate in health a second; a drain is cast only when it scores more, a tie goes to Heal Self.
        var canSelf = HealSelfCandidate(missing, mana, life, out var self);
        // M6 14 tactics: the emergency picks by the health a landed cast gives her a second now (HealPick.ChooseNow: no mana time for mana she holds, no damage credit), as the vitals guard's pick does.
        var choice = HealPick.ChooseNow(in self, canSelf, _healCandidates.AsSpan(0, count), out var selfRate, out var bestDrain, out var bestRate);
        var surerSave = false;
        if (choice >= 0 && canSelf && read.Live && !read.Survives(health, _healCandidates[choice].CastSeconds))
        {
            ref readonly var drain = ref _healCandidates[choice];
            surerSave = FightHealPick.HealSelfSavesSurer(in self, in drain, in read, health, out var selfSave, out var drainSave);
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"emergency: the drain's cast outlasts her: {self.Name} saves at {selfSave:0.##} ({self.Chance:P0} in {self.CastSeconds:0.0} s, heal {self.Heal:0}), {drain.Name} at {drainSave:0.##} ({drain.Chance:P0} in {drain.CastSeconds:0.0} s, heal {drain.Heal:0}): {(surerSave ? "Heal Self, whatever the health a second says" : "the drain stands")}"));
            choice = surerSave ? HealPick.HealSelf : choice;
        }

        if (choice != HealPick.Nothing)
        {
            LogHealPick("emergency", choice, in self, selfRate, _healCandidates.AsSpan(0, count), bestDrain, bestRate);
        }

        var (healRate, healSeconds) = choice switch
        {
            HealPick.Nothing => (0.0, 0.0),
            HealPick.HealSelf => (selfRate, self.CastSeconds),
            _ => (bestRate, _healCandidates[choice].CastSeconds),
        };
        if (FinishStep(session, clock, in here, in read, healRate, healSeconds))
        {
            return false; // M6 7 Y: Choose starts the bolt
        }

        var selfHeal = self.Heal;
        var why = choice == HealPick.HealSelf
            ? (count == 0 ? (best == 0 && _emergencyUndrainable > 0 ? "no tusker in reach that a drain takes from (those in reach resist drains wholly or are set off)" : "no tusker in reach") : surerSave ? "the drain's cast outlasts her and Heal Self is the surer save" : "Heal Self scores at least the best drain")
            : "the drain scores more than Heal Self";
        if (choice == HealPick.Nothing)
        {
            if (spared > 0)
            {
                WayOutNoHeal(mana); // M6 7 BE: it could pay a drain, but not with the recall still paid: she leaves
            }

            return false; // nothing is paid for: the mana order (the dance) brings it back
        }

        if (WayOutRefusesHeal(session, clock, mana, (uint)Math.Ceiling(choice == HealPick.HealSelf ? self.Mana : _healCandidates[choice].Mana), read.NextHitKills(health)))
        {
            return false; // M6 7 BE: the mana is the recall's: she leaves (FightScenario.WayOut.cs)
        }

        if (choice == HealPick.HealSelf)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"emergency: {why}; {_drain.HealName} at health {health}/{healthMax}, mana {mana} (expected {selfHeal:0.#})"));
            _emergencyCasting = true;
            _emergencyCastAt = clock;
            _drain.StartHeal(session, clock);
            NoteDrainStart(session, clock);
            _phase = Phase.Drain;
            return true;
        }

        var index = _emergencyIndex[choice];
        var info = levels[index].Info;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"emergency: {info.Name} on {name} 0x{best:X8} at health {health}/{healthMax} (line {_emergency.LineHealth:0}, target health {healthFraction:P0} = {targetHealth}, {distance:0.#} m, expected {_healCandidates[choice].Heal:0.#}, incoming {read.PerSecond:0.0} a second, biggest hit {read.BiggestHit:0}): {why}"));
        if (best != _target)
        {
            _log.Info(LogCategory, $"target 0x{best:X8}: the emergency drain's fullest tusker in reach");
            _target = best;
            _tally.SetTarget(name);
        }

        _killMark = _tally.Kills;
        _targetKillMark = _tally.TargetKills;
        _emergencyCasting = true;
        _emergencyCastAt = clock;
        _drainedSinceAnswer.Add(best);
        (_castSpell, _castFormula) = (info.Name, _emergencyFormula[choice]);
        BeginYield(session, index, best, clock);
        _drain.Start(session, index, best, clock, attack: true);
        NoteDrainStart(session, clock);
        Picker.Count(info.Name);
        _phase = Phase.Drain;
        return true;
    }

    /// <summary>
    /// M6 7 Y (r2b, level 10: a lone Female Tusker at 35 health, Flame Bolt II's mean 44, and two drains chosen while the mage died one bolt short): the bolt that is expected to kill the only attacker beats the emergency's heal or drain when the mage
    /// is expected to live through the cast, and also when no heal can save her anyway: a dead attacker stops the incoming. The arithmetic is the picker's (<see cref="FightPicker.Finishing"/>) over the emergency's own scores. True when it chose
    /// (<c>_finishGuid</c> is set; the phase is still the pick's). A second attacker leaves the emergency as it was: killing one does not stop the other.
    /// </summary>
    private bool FinishStep(IGameSessionView session, double clock, in CellFrame here, in IncomingRead read, double healPerSecond, double healSeconds)
    {
        var guid = _options.SpellId == 0 ? OnlyAttacker(session, in here, clock) : 0;
        if (guid == 0 || !session.State.Objects.TryGet(guid, out var attacker) || StandsAt(session, attacker) is not { } at || at.ToGlobal().DistanceTo(here.ToGlobal()) > _range
            || !session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _) || mana < (_triesSpent ? _manaNeeded : _manaFloor)
            || !FillSnapshot(session, clock, in here, guid))
        {
            return false;
        }

        var finish = Picker.Finishing(in _snap, in read, healPerSecond, healSeconds, InFlightDamageOn(guid, at.ToGlobal(), clock), clock);
        var line = string.Create(CultureInfo.InvariantCulture, $"emergency: finishing bolt: {_snap.BoltName} on {_snap.TargetName} 0x{guid:X8} at health {_snap.Health}/{_snap.HealthMax}: {finish.Remaining:0} left, expected {finish.Expected:0} at {finish.Landing:P0} in {_snap.BoltCastSeconds:0.0} s, incoming {read.PerSecond:0.0} a second, biggest hit {read.BiggestHit:0}, survives {(finish.Survives ? "yes" : "no")}, heal {healPerSecond:0.0} a second saves {(finish.HealSaves ? "yes" : "no")}: {finish.Why}");
        if (!finish.Wins || !(_options.IgnoreLine || LineIsClear(session, in here, guid)))
        {
            _log.Info(LogCategory, finish.Wins ? line + "; no clear line: the heal stands" : line);
            return false;
        }

        _log.Info(LogCategory, line);
        _finishGuid = guid;
        _emergencyFinishes++;
        return true;
    }

    /// <summary>
    /// M6 7 AA (r3c, level 10: Flame Bolt II resisted at 2.6 m, and the fight sat out its 3 s wait while two Female Tuskers took 22 health to 5; the emergency's first read was at health 5): the hit wait never blinds the fight. Read every frame of the wait, on the same numbers
    /// as <c>Choose</c>: under the emergency line the wait ends and the emergency acts, with no miss read (the bolt stays in the in-flight ledger, and its hit or kill counts when it lands). One exception, Y's arithmetic: a bolt in flight that is expected to kill the only attacker,
    /// when the mage is expected to live until it lands: a dead attacker stops the incoming, the kill may be a tenth of a second away, and leaving would cost a cast for nothing. Its resist or its time ends the hold.
    /// </summary>
    private bool EmergencyEndsWait(IGameSessionView session, double clock)
    {
        if (!EmergencyWanted(session, clock, out var health, out var healthMax) || session.State.Self.Position is not { } here)
        {
            return false;
        }

        var read = Picker.Rates.Read(clock);
        var guid = _options.SpellId == 0 ? OnlyAttacker(session, in here, clock) : 0;
        if (guid != 0 && InFlightFinishes(session, guid, clock, out var seconds) && read.Survives(health, seconds))
        {
            _finishInFlight = true;
            if (!_finishHoldNoted)
            {
                _finishHoldNoted = true;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"hit wait: held at health {health}/{healthMax}: the bolt in flight is expected to kill the only attacker 0x{guid:X8} in {seconds:0.00} s and she lives until then (incoming {read.PerSecond:0.0} a second, biggest hit {read.BiggestHit:0}); no second cast"));
            }

            return false;
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"hit wait: ended {clock - _awaitSince:0.00} s in: health {health}/{healthMax} is under the emergency line {_emergency.LineHealth:0} (incoming {read.PerSecond:0.0} a second, biggest hit {read.BiggestHit:0}): the emergency acts now; the bolt stays counted in flight, no miss"));
        return true;
    }

    /// <summary>
    /// M6 7 AA: "X resists your spell" is the bolt's answer (r3c: it came 0.08 s after the cast and the wait ran on for 2.9 s): the wait ends at once. A resist stays a miss, as it was when the wait ran out, so three in a row still walk.
    /// </summary>
    private ScenarioStatus ResistEndsWait(IGameSessionView session, double clock)
    {
        _tally.NoteMiss();
        _missesInARow++;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"miss: the bolt was resisted {clock - _awaitSince:0.00} s after the cast; the wait ends (misses in a row {_missesInARow})"));
        if (_missesInARow >= MissesBeforeWalking)
        {
            return StartWalk(session, clock, $"{_missesInARow} misses in a row");
        }

        _phase = Phase.Choose;
        return ScenarioStatus.Running;
    }

    /// <summary>
    /// The only creature attacking the mage, or 0: exactly one live attacker (<c>_attackers</c>, as self-defence holds them) and no other target creature within <see cref="SecondAttackerMetres"/> of the mage: the notices name the attacker, never its
    /// guid, so two of the same name read as one (the nearest).
    /// </summary>
    private uint OnlyAttacker(IGameSessionView session, in CellFrame here, double clock)
    {
        var objects = session.State.Objects;
        var only = 0u;
        foreach (var a in _attackers)
        {
            if (_killed.Contains(a.Guid) || TargetGone(objects, a.Guid) || clock - a.LastAt > _options.DefendForgetSeconds)
            {
                continue;
            }

            if (only != 0)
            {
                return 0;
            }

            only = a.Guid;
        }

        if (only == 0)
        {
            return 0;
        }

        var origin = here.ToGlobal();
        foreach (var o in objects.All)
        {
            if (o.Guid != only && IsEmergencyCandidate(session, o) && StandsAt(session, o) is { } p && p.ToGlobal().DistanceTo(origin) <= SecondAttackerMetres)
            {
                return 0;
            }
        }

        return only;
    }

    /// <summary>M5 7 F19: the measured heals hear ACE's chat lines from the fight's start (every heal of the fight is measured, the emergency's and the drain rule's).</summary>
    private void SubscribeHeals(IGameSessionView session)
    {
        _healChatSubscription ??= session.Events.Subscribe<ChatReceived>(OnHealChat);
        _liveHealthSubscription ??= session.Events.Subscribe<HealthUpdated>(OnLiveHealth); // M5 7 F20
    }

    private void DisposeHeals()
    {
        _healChatSubscription?.Dispose();
        _healChatSubscription = null;
        _liveHealthSubscription?.Dispose();
        _liveHealthSubscription = null;
    }

    private void OnHealChat(in ChatReceived e)
    {
        if (e.Line.Kind != ChatKind.System)
        {
            return;
        }

        var text = e.Line.Text;
        if (MeasuredHeals.TryParseDrain(text, out var gain, out var spell, out _))
        {
            // M5 7 F20: what the target lost (ACE's dest = src x (1 - loss)) reduces its live health, and the yield is measured. M6 14 drain resistance: a 0 that is the creature kind's resistance (NoteYield logged it) is no measure of
            // the spell: its measured ratio is left as it was, so the next drain of that level on another kind is not taken for nothing.
            if (NoteYield(gain, spell))
            {
                _castFormula = 0;
                return;
            }

            if (spell == _castSpell && _castFormula > 0)
            {
                _heals.NoteRatio(spell, gain, _castFormula);
            }
            else
            {
                _heals.Note(spell, null, gain);
            }

            _castFormula = 0;
            _log.Info(LogCategory, "health: " + _heals.LastNote);
            return;
        }

        if (MeasuredHeals.TryParseSelfHeal(text, out gain, out spell))
        {
            _heals.Note(spell, null, gain);
            _log.Info(LogCategory, "health: " + _heals.LastNote);
            return;
        }

        if (e.Line.ChatType == FightTally.MagicChatType && FightTally.TryHarmDealt(text, out var harmed, out var harmSpell, out var harmVictim))
        {
            NoteHarmLine(harmed, harmSpell, harmVictim); // M6 14 (m6-life-through): what a landed Harm Other took comes off the creature's live health
            return;
        }

        if (e.Line.ChatType == FightTally.MagicChatType && FightTally.Classify(text, out var taken, out _) == MagicLineKind.DamageTaken)
        {
            Picker.Rates.Hits.Note(_view?.Clock.Now ?? _lastClock, taken); // M6 7 Y: a creature's spell is a hit like a melee blow, as HostileWatch hears it
            return;
        }

        if (e.Line.ChatType == FightTally.MagicChatType && text.EndsWith(ResistSuffix, StringComparison.Ordinal) && _view is { } seen)
        {
            NoteInFlightResist(seen, text[..^ResistSuffix.Length]); // M6 7 AA: the bolt's answer
            return;
        }

        if (e.Line.ChatType == FightTally.MagicChatType && FightTally.Classify(text, out var amount, out _) == MagicLineKind.DamageDealt && DealtTarget(text) is { Length: > 0 } victim && _view is { } view
            && view.State.Self.Position is { } at && view.State.Objects.NearestLivingCreatureNamed(victim, at, view.State.Self.Guid, out var guid, view.Live))
        {
            AddDealt(guid, (int)amount); // the bolts' and the rings' hits reduce the creature's live health (M5 7 F20)
            NoteInFlightHit(guid, text); // M5 7 D124 (S1b): a hit landed is no longer in flight
            if (_ringLineText is { } ringText && _ringCasts is { } rings && text.Contains(ringText, StringComparison.Ordinal))
            {
                rings.NoteHit(amount); // M5 7 F22: a hit of the ring, counted for the measured factor
            }

            if (_volleyLineText is { } volleyText && _volleyCasts is { } volleys && text.Contains(volleyText, StringComparison.Ordinal))
            {
                volleys.NoteHit(amount); // M5 7 F24: a hit of the volley
            }
        }
    }

    /// <summary>
    /// The target of "You &lt;verb&gt; &lt;target&gt; for &lt;n&gt; points with &lt;spell&gt;.", or empty. M6 7 Y: ACE's prefixes come first ("Critical hit! You incinerate ..."): r2b's critical bolt was not read, so the bolt stayed in flight, its target
    /// doomed, and the mage held 0.2 s with no cast begun until the tusker's next blow.
    /// </summary>
    internal static string DealtTarget(string text)
    {
        foreach (var prefix in Prefixes)
        {
            if (text.StartsWith(prefix, StringComparison.Ordinal))
            {
                return DealtTarget(text[prefix.Length..]);
            }
        }

        var verbEnd = text.IndexOf(' ', 4);
        var forAt = text.LastIndexOf(" for ", StringComparison.Ordinal);
        return text.StartsWith("You ", StringComparison.Ordinal) && verbEnd > 0 && forAt > verbEnd ? text[(verbEnd + 1)..forAt] : string.Empty;
    }

    /// <summary>
    /// A war spell's damage line: the creature's live health falls by it. ACE pushes the selected target's new health as the blow lands, just before the chat line, so a push within <see cref="PushSameEventSeconds"/> already holds the
    /// damage and nothing is taken twice (M5 7 F20).
    /// </summary>
    private void AddDealt(uint guid, int amount)
    {
        var max = _view is { } v && v.State.Objects.TryGet(guid, out var o) ? TargetStatsOf(o.Weenie.WeenieClassId).MaxHealth : 0;
        _live.Took(guid, amount, max, _lastClock, _lastClock - PushSameEventSeconds);
    }

    /// <summary>
    /// The candidates in reach whose health the client does not know, or knows from before the mage drained them, are asked of the server (QueryHealth, GameAction 0x01BF; the client learns a creature's health only from the answer,
    /// <c>ActionPolicy.cs</c> 124, <c>WorldObjectTable.cs</c> 350). True while an answer is awaited (at most <see cref="EmergencyQuerySeconds"/>); an unanswered one is estimated from the damage dealt to it.
    /// </summary>
    private bool AskHealths(IGameSessionView session, in CellFrame here, float range, double clock)
    {
        var origin = here.ToGlobal();
        var waiting = false;
        foreach (var o in session.State.Objects.All)
        {
            if (!IsEmergencyCandidate(session, o) || StandsAt(session, o) is not { } p || p.ToGlobal().DistanceTo(origin) > range)
            {
                continue;
            }

            var stale = o.Health is null || _drainedSinceAnswer.Contains(o.Guid);
            if (_healthAsks.TryGetValue(o.Guid, out var ask))
            {
                if (ask.Handle.Outcome is not null)
                {
                    _healthAsks.Remove(o.Guid);
                    _drainedSinceAnswer.Remove(o.Guid);
                    continue;
                }

                if (clock - ask.At < EmergencyQuerySeconds)
                {
                    waiting = true;
                    continue;
                }

                if (clock - ask.At < EmergencyQuerySeconds * 5)
                {
                    continue; // unanswered: estimated from the damage dealt for a while before it is asked again
                }

                _healthAsks.Remove(o.Guid);
            }

            if (stale && _healthAsks.Count < EmergencyCandidateCap)
            {
                _healthAsks[o.Guid] = (session.Actions.QueryHealth(o.Guid, ActionSource.Host), clock);
                _live.Asked(o.Guid, clock); // the asked creature is the selected target now
                _log.Info(LogCategory, $"emergency: the health of {o.Name} 0x{o.Guid:X8} is asked of the server first ({(o.Health is null ? "unknown: never taken as full" : "drained since its last answer")})");
                waiting = true;
            }
        }

        return waiting;
    }

    /// <summary>A living listed target, not killed, in the cell filter (the emergency's candidates).</summary>
    private bool IsEmergencyCandidate(IGameSessionView session, WorldObject o)
    {
        if (StandsAt(session, o) is not { } p || (o.ItemType & WarCreatureItemType) == 0 || o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord } || _killed.Contains(o.Guid))
        {
            return false;
        }

        return (IsFightCreature(o.Weenie.WeenieClassId) || o.Guid == _target) && (_cellFilter is null || _cellFilter(p.CellId));
    }

    /// <summary>
    /// The health share the emergency reads for a candidate (never an unknown taken as full, D92): the server's answer when it is fresh, else the profile's max_health less the damage the mage dealt it (its bolts', its rings' and
    /// its drains' takings); a creature with no damage dealt and no answer is taken as full, so it is preferred, as it should be.
    /// </summary>
    private float HealthEstimate(WorldObject o)
    {
        var max = TargetStatsOf(o.Weenie.WeenieClassId).MaxHealth;
        if (o.Health is { } held)
        {
            _live.Seed(o.Guid, held);
        }

        var known = _live.TryGet(o.Guid, max > 0 ? max : EmergencyUnknownTargetHealth, out var fraction, out var source);
        ShadowHealth(Threat.HealthReader.FightEmergency, o.Guid, known, fraction, max, source, _live.AgeOf(o.Guid, _lastClock), _lastClock); // M6 16 E2d: the one picture is asked the same beside it (report-only)
        return known ? fraction : 1f; // M5 7 F20: the one live model (pushed, queried or modelled); no health and nothing dealt: taken as full, so it is preferred
    }

    internal static int DrainLevelOf(uint id)
    {
        for (var i = 0; i < DrainPicker.Ids.Count; i++)
        {
            if (DrainPicker.Ids[i] == id)
            {
                return i + 1;
            }
        }

        return 1;
    }

    /// <summary>
    /// The living tusker in reach (range and line) with the most health left (the nearest among equals), or 0: the profile's targets, alive, not killed, in the cell filter, within <paramref name="range"/> of the caster and, unless
    /// <c>--fight-ignore-line</c>, in a clear line. The line is tried on the best candidate first and the next best when it is blocked. M6 14 drain resistance: a creature whose kind lets no drain through (<see cref="DrainResist"/>: an
    /// Armored Tusker's <c>ResistHealthDrain</c> is 0) is no candidate, as a creature whose drains are off is not; <c>_emergencyUndrainable</c> counts those passed over.
    /// </summary>
    private uint EmergencyTarget(IGameSessionView session, in CellFrame here, float range, out float healthFraction, out float distance)
    {
        (healthFraction, distance) = (0, 0);
        _emergencyUndrainable = 0;
        var objects = session.State.Objects;
        var origin = here.ToGlobal();
        var count = 0;
        foreach (var o in objects.All)
        {
            if (count >= EmergencyCandidateCap)
            {
                break;
            }

            if (StandsAt(session, o) is not { } p || (o.ItemType & WarCreatureItemType) == 0 || o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord } || _killed.Contains(o.Guid))
            {
                continue;
            }

            if (!(IsFightCreature(o.Weenie.WeenieClassId) || o.Guid == _target) || (_cellFilter is not null && !_cellFilter(p.CellId))) // M6 7 AN: nothing here keeps a drain to the band or to a dungeon
            {
                continue;
            }

            var d = (float)p.ToGlobal().DistanceTo(origin);
            if (d <= range && DrainResistOf(o.Weenie.WeenieClassId, o.Name) <= 0)
            {
                _emergencyUndrainable++; // M6 14 drain resistance: its kind gives a drain nothing
                continue;
            }

            if (d <= range && !_live.DrainOff(o.Guid, out _)) // M5 7 F20: a creature the last drain gave little from is not the emergency's drain target
            {
                (_emergencyGuids[count], _emergencyHealth[count], _emergencyDistance[count], _emergencyBlocked[count]) = (o.Guid, HealthEstimate(o), d, false);
                count++;
            }
        }

        for (var tries = 0; tries < count; tries++)
        {
            var best = FightEmergency.Fullest(_emergencyHealth, _emergencyDistance, _emergencyBlocked, count);
            if (best < 0)
            {
                return 0;
            }

            if (_options.IgnoreLine || LineIsClear(session, in here, _emergencyGuids[best]))
            {
                (healthFraction, distance) = (_emergencyHealth[best], _emergencyDistance[best]);
                return _emergencyGuids[best];
            }

            _emergencyBlocked[best] = true;
        }

        return 0;
    }

    /// <summary>A walk is on and the emergency holds with a tusker in reach: the walk is dropped and the fight chooses again (the emergency drain outranks the walk). True when it dropped.</summary>
    private bool EmergencyDropsWalk(IGameSessionView session, double clock)
    {
        if (!EmergencyWanted(session, clock, out _, out _) || EmergencyLevelIndex() < 0 || session.State.Self.Position is not { } here || !session.State.Self.Qualities.TryGetSkill(MageTemplate.LifeMagic, out var lifeSkill))
        {
            return false;
        }

        if (EmergencyTarget(session, in here, CastRange(_drain.Levels[EmergencyLevelIndex()].Info, lifeSkill.Init + lifeSkill.Ranks), out _, out _) == 0)
        {
            return false;
        }

        _walk?.Abandon();
        _log.Info(LogCategory, "emergency: the walk is dropped; a tusker is in reach with a clear line");
        _phase = Phase.Choose;
        return true;
    }

    /// <summary>One line for every hit the player takes (M5 7 F16, the owner: log basically everything): the damage amount of the 0x01B2 notice, the health after it as the read model has it, the run's hits, total and biggest.</summary>
    private void NoteHit(CombatEvent e)
    {
        Picker.Rates.Hits.Note(_view?.Clock.Now ?? _lastClock, e.Damage); // M6 7 Y: the recent hits, as HostileWatch hears them
        var health = _view is { } v && v.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var h, out var max) ? $"{h}/{max}" : "n/a";
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"hit: {e.Who} hit the player for {e.Damage}{(e.Critical ? " (critical)" : string.Empty)} (type 0x{e.DamageType:X}, body {e.BodyLocation}); health {health} before the update; hits {_tally.HitsTaken}, damage taken {_tally.DamageTaken}, biggest {_tally.HitMax}"));
    }

    /// <summary>The emergency drain's or heal's cast ends: its seconds join the cast time of the line, and a run of casts that did not land sets the emergency aside (named).</summary>
    private void EmergencyEnded(CastEnd end, double clock)
    {
        var seconds = clock - _emergencyCastAt;
        if (_drain.IsHeal && end.Succeeded() && seconds > 0 && seconds < 30)
        {
            _emergencyHealSeconds = double.IsNaN(_emergencyHealSeconds) ? seconds : _emergencyHealSeconds + (FightEmergency.CastWeight * (seconds - _emergencyHealSeconds)); // M5 7 F19: Heal Self's measured cast seconds
        }
        else if (!_drain.IsHeal && end.Succeeded())
        {
            _heals.NoteSeconds(_drain.LastName, seconds);
        }

        if (_emergency.Ended(end.Succeeded(), end is CastEnd.Resisted or CastEnd.InTheAir, seconds, clock)) // M6 14 airborne: held in the air counts toward no set-aside, as a resist does not
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"emergency: {FightEmergency.MaxFailsInARow} casts in a row did not land ({end.Name()}); the emergency is set aside {FightEmergency.AsideSeconds:0} s: the drain rule decides"));
        }
    }
}
