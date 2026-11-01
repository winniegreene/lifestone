using System.Globalization;
using DatReaderWriter.Enums;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.World;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M5 7 F23 (soak round 2's death: 50 s in the upkeep with health 110, 95, 72, 35, 24, 1 of 195 and the mana at 313-334, and nothing cast). The vitals guard runs in every autopilot state but the fight (which has its own emergency, F16/F19/F21)
/// while the mage is in the world and alive: under the emergency line (<see cref="FightEmergency"/>, the fight's own line and options, <c>--fight-emergency-floor/-safe/-margin</c>, the line raised by the incoming rate the
/// <see cref="HostileWatch"/> measures) it runs the fight's heal pick (<see cref="HealPick"/> over <see cref="FightHealPick"/>'s candidates: Heal Self against each drain level on the fullest target whose REAL health the server gave, never an
/// unknown read as full, D92) at once, and again after each cast, until the safe line. It preempts the state's own casts: while it holds, the autopilot does not step the state's station, so the station starts no cast; the busy rules hold, as
/// the guard's own cast waits for the Action API's lanes to be free (an action of the station in flight ends first). Every decision is one line, <c>guard: &lt;state&gt;: ...</c>.
/// </summary>
internal sealed class AutopilotVitalsGuard
{
    private const uint CreatureItemType = 0x10;

    private readonly IReadOnlyDictionary<uint, SpellInfo> _spells;
    private readonly HuntingGround _ground;
    private readonly ILogSink _log;
    private readonly HostileWatch _watch;
    private readonly FightEmergency _emergency;
    private readonly FightDrain _drain;
    private readonly FightRefill _payable;
    private readonly MeasuredHeals _heals = new();
    private readonly double _castChance;
    private readonly double _damageWeight;
    private readonly HealCandidate[] _candidates = new HealCandidate[FightDrain.MaxLevels];
    private readonly int[] _levelIndex = new int[FightDrain.MaxLevels];
    private string _state = string.Empty;
    private bool _waitLogged;
    private bool _nothingLogged;
    private bool _settleLogged;
    private double _castAt;
    private double _retryAt = double.NegativeInfinity;
    private double _modeAskedAt = double.NegativeInfinity;
    private string _refusalLogged = string.Empty;

    /// <summary>The least seconds between two attempts after a refused cast or an unanswered mode change (M5 7 F24: the corpse trip's guard retried dozens of times in one frame).</summary>
    public const double RetrySeconds = 1.0;

    /// <summary>The guard of an autopilot run.</summary>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="ground">The ground (its targets' max health and magic defence for the drains).</param>
    /// <param name="options">The autopilot's options (the fight's emergency line and the heal pick's damage weight, as the fight reads them).</param>
    /// <param name="castChance">The least cast chance of the Heal Self level (the buff options').</param>
    /// <param name="watch">The hostiles and the incoming rate.</param>
    /// <param name="log">Where it logs (the autopilot's category).</param>
    public AutopilotVitalsGuard(IReadOnlyDictionary<uint, SpellInfo> spells, HuntingGround ground, AutopilotOptions options, double castChance, HostileWatch watch, ILogSink log)
    {
        (_spells, _ground, _watch, _log, _castChance) = (spells, ground, watch, log, castChance);
        _emergency = new FightEmergency(options.EmergencyFloorPercent, options.EmergencySafePercent, options.EmergencyMarginPercent);
        _damageWeight = options.HealDamageWeight;
        _drain = new FightDrain(spells, log, 1, 100);
        _payable = new FightRefill(spells, log, 0);
    }

    /// <summary>True while the guard holds the state (under the line, waiting for the lanes or casting).</summary>
    public bool Holding { get; private set; }

    /// <summary>The guard's heals and drains that landed.</summary>
    public int Landed { get; private set; }

    /// <summary>The guard's casts that ended in ACE's YoureTooBusy (0x001D) and wait a second for the next try (M5 7 F30).</summary>
    public int Busy { get; private set; }

    /// <summary>The guard's casts started.</summary>
    public int Casts { get; private set; }

    /// <summary>One frame of the guard in <paramref name="state"/>. True while it holds the state (the caller does not step the state's station).</summary>
    /// <param name="session">The session.</param>
    /// <param name="clock">The session clock.</param>
    /// <param name="state">The state's token, for the log.</param>
    public bool Step(IGameSessionView session, double clock, string state)
    {
        _state = state;
        if (_drain.Cast is { IsRunning: true } running)
        {
            var end = running.Step(clock);
            if (end == CastEnd.Running)
            {
                return true;
            }

            CastEnded(session, end, clock);
        }

        if (!_emergency.Enabled || !session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var max) || health == 0 || max == 0)
        {
            return Release(null);
        }

        if (session.State.Self.Travel != SelfTravel.Present || session.State.Self.Position is null)
        {
            // M5 7 F30 (soak round 7: three Heal Self casts at the login were refused 0x001D, ACE's YoureTooBusy while the player is still in portal space): the guard casts nothing until the portal space is over and the player is placed.
            if (!_settleLogged)
            {
                _settleLogged = true;
                _log.Info(AutopilotScenario.LogCategory, $"guard: {state}: the login is still settling (travel {session.State.Self.Travel}, placed {(session.State.Self.Position is null ? "no" : "yes")}): no cast until the portal space is over and the player is placed");
            }

            return Release(null);
        }

        _settleLogged = false;

        if (!_emergency.Wanted(health, max, _watch.IncomingPerSecond(clock), clock))
        {
            return Release(string.Create(CultureInfo.InvariantCulture, $"health {health}/{max} at the safe line"));
        }

        if (session.Actions is ActionApi api && (api.QueuedCount > 0 || api.InFlightCount > 0))
        {
            if (!_waitLogged)
            {
                _waitLogged = true;
                _log.Info(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"guard: {state}: health {health}/{max} under the line {_emergency.LineHealth:0}: the state's action in flight ends first (the busy rules), then the heal pick"));
            }

            Holding = true;
            return true;
        }

        _waitLogged = false;
        Holding = true;
        if (clock < _retryAt)
        {
            return true; // M5 7 F24: a refused cast backs off, at most one attempt a second; the state's station stays held meanwhile, as for any heal
        }

        return Pick(session, clock, health, max);
    }

    private bool Pick(IGameSessionView session, double clock, uint health, uint max)
    {
        var self = session.State.Self;
        var q = self.Qualities;
        if (!q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _) || !q.TryGetSkill(MageTemplate.LifeMagic, out var lifeSkill))
        {
            return Release("no mana or Life Magic read");
        }

        _drain.Fill(q, self.Spellbook, LevelFloor.HeldFor(session.State, MagicSchool.LifeMagic), _castChance, _payable);
        var initPlusRanks = lifeSkill.Init + lifeSkill.Ranks;
        var life = GearCasts.TryCurrentSkill(q, MageTemplate.LifeMagic, out var current, out _) ? current : initPlusRanks;
        var missing = max > health ? max - health : 0u;
        var target = KnownFullest(session, out var targetHealth, out var defence, out var distance, out var name);
        var count = 0;
        if (target != 0)
        {
            var levels = _drain.Levels;
            var situation = new DrainSituation(health, max, mana, life, initPlusRanks, targetHealth, defence, distance);
            for (var i = 0; i < levels.Length && count < _candidates.Length; i++)
            {
                if (mana < GearCasts.ManaCeiling(levels[i].Info, 0) || distance > FightScenario.CastRange(levels[i].Info, initPlusRanks))
                {
                    continue;
                }

                _candidates[count] = FightHealPick.DrainCandidate(in levels[i], in situation, target, _heals, out _);
                _levelIndex[count++] = i;
            }
        }

        var canSelf = FightHealPick.SelfCandidate(_drain, _heals, double.NaN, missing, mana, life, out var healSelf);
        var choice = HealPick.Choose(in healSelf, canSelf, _candidates.AsSpan(0, count), FightRates.SeedSecondsPerMana, _damageWeight, out var selfRate, out var best, out var bestRate);
        if (choice == HealPick.Nothing)
        {
            if (!_nothingLogged)
            {
                _nothingLogged = true;
                _log.Warn(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"guard: {_state}: health {health}/{max} under the line {_emergency.LineHealth:0}, but no Heal Self and no drain is paid for (mana {mana}): the state goes on (TODO(f23-guard-nothing-paid))"));
            }

            Holding = false;
            return false;
        }

        _nothingLogged = false;
        if (q.CombatMode != CombatModes.Magic)
        {
            // M5 7 F24 (the corpse trip: the caster refused "not in the magic combat mode" dozens of times in one frame): the mode first, as the fight and the keeper do; the next frame casts once the change has landed.
            if (clock - _modeAskedAt >= RetrySeconds)
            {
                _modeAskedAt = clock;
                _log.Info(AutopilotScenario.LogCategory, $"guard: {_state}: entering the magic mode for {(choice == HealPick.HealSelf ? _drain.HealName : _drain.Levels[_levelIndex[choice]].Info.Name)} (the caster cannot cast outside it)");
                session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
            }

            return true;
        }

        var line = HealPick.Line(choice, in healSelf, selfRate, _candidates.AsSpan(0, count), best, bestRate);
        _castAt = clock;
        Casts++;
        if (choice == HealPick.HealSelf)
        {
            _log.Info(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"guard: {_state}: {line}; {_drain.HealName} at health {health}/{max} (line {_emergency.LineHealth:0}), mana {mana}{(target == 0 ? ", no target of known health in reach" : string.Empty)}"));
            _drain.StartHeal(session, clock);
            return true;
        }

        _log.Info(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"guard: {_state}: {line}; {_drain.Levels[_levelIndex[choice]].Info.Name} on {name} 0x{target:X8} at health {health}/{max} (line {_emergency.LineHealth:0}, target health {targetHealth}, {distance:0.#} m)"));
        _drain.Start(session, _levelIndex[choice], target, clock, attack: true);
        return true;
    }

    private void CastEnded(IGameSessionView session, CastEnd end, double clock)
    {
        session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var max);
        var cast = _drain.Cast!;
        var seconds = clock - _castAt;
        if (end.Succeeded())
        {
            Landed++;
            if (!_drain.IsHeal && seconds > 0)
            {
                _heals.NoteSeconds(_drain.LastName, seconds);
            }
        }

        _emergency.Ended(end.Succeeded(), end == CastEnd.Resisted, seconds, clock);
        if (end.Succeeded())
        {
            _refusalLogged = string.Empty;
            _log.Info(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"guard: {_state}: {_drain.LastName} landed in {seconds:0.0} s; health {health}/{max}"));
            return;
        }

        if (end == CastEnd.Busy)
        {
            // M5 7 F30: ACE's YoureTooBusy (0x001D) past the cast rules' own busy waits is a wait, not a failure: the guard tries again after a second.
            _retryAt = clock + RetrySeconds;
            Busy++;
            _log.Info(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"guard: {_state}: {_drain.LastName} met WeenieError 0x001D (YoureTooBusy) in {seconds:0.0} s: a wait, not a failure; the next try in {RetrySeconds:0.#} s; health {health}/{max}"));
            return;
        }

        _retryAt = clock + RetrySeconds; // M5 7 F24: a refused or failed cast is tried again after a second at the soonest, and logged once per reason
        var reason = end.Name() + ": " + (cast.Report.Why ?? "?");
        if (!string.Equals(reason, _refusalLogged, StringComparison.Ordinal))
        {
            _refusalLogged = reason;
            _log.Info(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"guard: {_state}: {_drain.LastName} did not land ({reason}) in {seconds:0.0} s; health {health}/{max}; the next try in {RetrySeconds:0.#} s at the soonest"));
        }
    }

    private bool Release(string? why)
    {
        if (Holding && why is not null)
        {
            _log.Info(AutopilotScenario.LogCategory, $"guard: {_state}: {why}: the state goes on");
        }

        (Holding, _waitLogged) = (false, false);
        return false;
    }

    /// <summary>The living target of the ground with the most health left whose health the server gave (never an unknown read as full, D92), within the watch's reach, or 0.</summary>
    private uint KnownFullest(IGameSessionView session, out uint targetHealth, out uint defence, out float distance, out string name)
    {
        (targetHealth, defence, distance, name) = (0, 0, 0, string.Empty);
        if (session.State.Self.Position is not { } at)
        {
            return 0;
        }

        var self = at.ToGlobal();
        var best = 0u;
        var bestFraction = -1f;
        foreach (var o in session.State.Objects.All)
        {
            if (o.Position is not { } p || (o.ItemType & CreatureItemType) == 0 || o.Health is not { } fraction || fraction <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord })
            {
                continue;
            }

            GroundTarget? listed = null;
            foreach (var t in _ground.Targets)
            {
                if (t.Wcid == o.Weenie.WeenieClassId && t.Role == GroundRole.Target)
                {
                    listed = t;
                    break;
                }
            }

            var d = (float)p.ToGlobal().DistanceTo(self);
            if (listed is null || d > _watch.ReachMetres || fraction <= bestFraction)
            {
                continue;
            }

            var maxHealth = listed.MaxHealth > 0 ? listed.MaxHealth : FightScenario.EmergencyUnknownTargetHealth;
            (best, bestFraction, distance, name, defence) = (o.Guid, fraction, d, o.Name ?? "creature", (uint)Math.Max(0, listed.MagicDefence));
            targetHealth = (uint)Math.Round(fraction * maxHealth);
        }

        return best;
    }
}
