using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
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
    private string _castSpell = string.Empty;
    private double _castFormula;

    /// <summary>M5 7 F19: a wound-up drain level is offered only when its cast seconds times this are within the seconds the mage's health lasts at the incoming rate (Lifestone's own margin).</summary>
    public const double EmergencySecondsMargin = 2.0;

    /// <summary>M5 7 F19: the longest the emergency waits for the server's answer to a target's health before it chooses on the client's last value (Lifestone's own; a localhost answer takes well under it).</summary>
    public const double EmergencyQuerySeconds = 1.0;

    /// <summary>M5 7 F19: the health taken for a target the profile gives no max_health for (Lifestone's own, as F7's large-target assumption, <c>TODO(m5-drain-target-stats)</c>).</summary>
    public const int EmergencyUnknownTargetHealth = 400;

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
    private bool EmergencyWanted(IGameSessionView session, double clock, out uint health, out uint healthMax)
    {
        health = healthMax = 0;
        if (!_emergency.Enabled || _options.ArenaHere || !session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out health, out healthMax))
        {
            return false;
        }

        var incoming = Picker.Rates.IncomingPerSecond;
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
    private bool EmergencyStep(IGameSessionView session, double clock, in CellFrame here)
    {
        if (!EmergencyWanted(session, clock, out var health, out var healthMax))
        {
            return false;
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

        if (levels.Length > 0 && AskHealths(session, in here, reach, clock))
        {
            return true; // the candidates' health is asked of the server first: an unknown health is never taken as full (D92)
        }

        var healthFraction = 0f;
        var distance = 0f;
        var best = levels.Length == 0 ? 0 : EmergencyTarget(session, in here, reach, out healthFraction, out distance);
        var objects = session.State.Objects;
        var name = "creature";
        var targetMax = 0;
        if (best != 0 && objects.TryGet(best, out var target))
        {
            (name, targetMax) = (target.Name ?? "creature", TargetStatsOf(target.Weenie.WeenieClassId).MaxHealth);
        }

        var targetHealth = (uint)Math.Round(healthFraction * (targetMax > 0 ? targetMax : EmergencyUnknownTargetHealth));
        var missing = healthMax > health ? healthMax - health : 0u;
        var life = GearCasts.TryCurrentSkill(q, MageTemplate.LifeMagic, out var current, out _) ? current : initPlusRanks; // the chance is read at the skill now, buffs up
        var defence = best != 0 && objects.TryGet(best, out var defender) ? (uint)TargetStatsOf(defender.Weenie.WeenieClassId).Defence : 0u;
        var count = 0;
        if (best != 0)
        {
            var secondsLeft = health / Math.Max(Picker.Rates.IncomingPerSecond, FightEmergency.SeedIncomingPerSecond);
            var situation = new DrainSituation(health, healthMax, mana, life, initPlusRanks, targetHealth, defence, distance);
            for (var i = 0; i < levels.Length && count < _healCandidates.Length; i++)
            {
                if (mana < GearCasts.ManaCeiling(levels[i].Info, 0) || distance > CastRange(levels[i].Info, initPlusRanks))
                {
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
        var choice = HealPick.Choose(in self, canSelf, _healCandidates.AsSpan(0, count), Picker.Rates.SecondsPerMana, _options.HealDamageWeight, out var selfRate, out var bestDrain, out var bestRate);
        if (choice != HealPick.Nothing)
        {
            LogHealPick("emergency", choice, in self, selfRate, _healCandidates.AsSpan(0, count), bestDrain, bestRate);
        }

        var selfHeal = self.Heal;
        var why = choice == HealPick.HealSelf
            ? (count == 0 ? "no tusker in reach" : "Heal Self scores at least the best drain")
            : "the drain scores more than Heal Self";
        if (choice == HealPick.Nothing)
        {
            return false; // nothing is paid for: the mana order (the dance) brings it back
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
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"emergency: {info.Name} on {name} 0x{best:X8} at health {health}/{healthMax} (line {_emergency.LineHealth:0}, target health {healthFraction:P0} = {targetHealth}, {distance:0.#} m, expected {_healCandidates[choice].Heal:0.#}, incoming {Picker.Rates.IncomingPerSecond:0.0} a second): {why}"));
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
            if (spell == _castSpell && _castFormula > 0)
            {
                _heals.NoteRatio(spell, gain, _castFormula);
            }
            else
            {
                _heals.Note(spell, null, gain);
            }

            NoteYield(gain, spell); // M5 7 F20: what the target lost (ACE's dest = src x (1 - loss)) reduces its live health, and the yield is measured

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

        if (e.Line.ChatType == FightTally.MagicChatType && FightTally.Classify(text, out var amount, out _) == MagicLineKind.DamageDealt && DealtTarget(text) is { Length: > 0 } victim && _view is { } view
            && view.State.Self.Position is { } at && view.State.Objects.NearestLivingCreatureNamed(victim, at, view.State.Self.Guid, out var guid))
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

    /// <summary>The target of "You &lt;verb&gt; &lt;target&gt; for &lt;n&gt; points with &lt;spell&gt;.", or empty.</summary>
    internal static string DealtTarget(string text)
    {
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
            if (!IsEmergencyCandidate(o) || o.Position is not { } p || p.ToGlobal().DistanceTo(origin) > range)
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
    private bool IsEmergencyCandidate(WorldObject o)
    {
        if (o.Position is not { } p || (o.ItemType & WarCreatureItemType) == 0 || o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord } || _killed.Contains(o.Guid))
        {
            return false;
        }

        var wcid = o.Weenie.WeenieClassId;
        for (var i = 0; i < _targetWcids.Count; i++)
        {
            if (_targetWcids[i] == wcid)
            {
                return _cellFilter is null || _cellFilter(p.CellId);
            }
        }

        return false;
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

        return _live.TryGet(o.Guid, max > 0 ? max : EmergencyUnknownTargetHealth, out var fraction, out _) ? fraction : 1f; // M5 7 F20: the one live model (pushed, queried or modelled); no health and nothing dealt: taken as full, so it is preferred
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
    /// <c>--fight-ignore-line</c>, in a clear line. The line is tried on the best candidate first and the next best when it is blocked.
    /// </summary>
    private uint EmergencyTarget(IGameSessionView session, in CellFrame here, float range, out float healthFraction, out float distance)
    {
        (healthFraction, distance) = (0, 0);
        var objects = session.State.Objects;
        var origin = here.ToGlobal();
        var count = 0;
        foreach (var o in objects.All)
        {
            if (count >= EmergencyCandidateCap)
            {
                break;
            }

            if (o.Position is not { } p || (o.ItemType & WarCreatureItemType) == 0 || o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord } || _killed.Contains(o.Guid))
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

            var d = (float)p.ToGlobal().DistanceTo(origin);
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

        if (_emergency.Ended(end.Succeeded(), end == CastEnd.Resisted, seconds, clock))
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"emergency: {FightEmergency.MaxFailsInARow} casts in a row did not land ({end.Name()}); the emergency is set aside {FightEmergency.AsideSeconds:0} s: the drain rule decides"));
        }
    }
}
