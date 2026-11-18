using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// A station's needs read against the session's state (M4 P5b 3.4): the first one missing, by name (<c>where</c>, <c>peace</c>, <c>health</c>,
/// <c>stamina</c>, <c>mana</c>, <c>kits</c>, <c>potions</c>, <c>buffed</c>), so a miss is <c>precondition:&lt;x&gt;</c>, never a silent place.
/// The journey checks it before a station (a miss skips it) and <see cref="ScenarioStation"/> again on its first step.
/// </summary>
public static class StationPreconditions
{
    /// <summary>The first need <paramref name="state"/> misses, or null when every need holds.</summary>
    /// <param name="needs">The needs.</param>
    /// <param name="state">The session's state.</param>
    /// <param name="log">Where the <c>buffed</c> TODO is logged, or null.</param>
    /// <param name="dungeon">The journey's dungeon ground (<see cref="JourneyContext.Dungeon"/>): the den the <c>where</c> need names. Required when the needs name <see cref="StationWhere.Den"/>.</param>
    public static string? Miss(StationNeeds needs, GameState state, ILogSink? log = null, HuntingGround? dungeon = null)
    {
        ArgumentNullException.ThrowIfNull(needs);
        ArgumentNullException.ThrowIfNull(state);
        var self = state.Self;
        if (!JourneyGrounds.At(needs.Where, self.Position, dungeon))
        {
            return "where";
        }

        var q = self.Qualities;
        if (needs.Peace && q.CombatMode != CombatModes.NonCombat)
        {
            return "peace";
        }

        if (Below(q, MageTemplate.MaxHealth, needs.MinHealthPercent))
        {
            return "health";
        }

        if (Below(q, MageTemplate.MaxStamina, needs.MinStaminaPercent))
        {
            return "stamina";
        }

        if (Below(q, MageTemplate.MaxMana, needs.MinManaPercent))
        {
            return "mana";
        }

        var scratch = new List<uint>();
        if (needs.Kits > 0 && Kits(state, scratch) < needs.Kits)
        {
            return "kits";
        }

        if (needs.Potions > 0 && (!VitalsMeans.FindPotions(state, scratch, out _, out var potions) || potions < needs.Potions))
        {
            return "potions";
        }

        if (needs.Buffed)
        {
            // TODO(m4-journey-buffed): "buffed" reads any active enchantment on the player, not the character's buff profile spell by spell (the buff
            // station's own verdict, buffs=n/n, is the judged one). Logged every time so the weak check is never silent.
            log?.Warn(JourneyVerdict.LogCategory, "TODO(m4-journey-buffed): the buffed need reads any active enchantment, not the buff profile");
            if (q.Enchantments.IsDefaultOrEmpty)
            {
                return "buffed";
            }
        }

        return null;
    }

    /// <summary>M6 14 precond: true for a need she can restore herself (<c>health</c>, <c>stamina</c>, <c>mana</c>); the others (where, peace, kits, potions, buffed) are not the keeper's.</summary>
    /// <param name="miss">The need missed (<see cref="Miss"/>).</param>
    public static bool IsVital(string miss) => miss is "health" or "stamina" or "mana";

    /// <summary>The three vitals as the server last sent them, for a check's line: <c>health 170/219, stamina 305/305, mana 403/410</c>.</summary>
    /// <param name="state">The session's state.</param>
    public static string Shares(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var q = state.Self.Qualities;
        string One(string name, uint id) => q.IsKnown && q.TryGetVitalLevels(id, out var c, out var m) ? $"{name} {c}/{m}" : $"{name} unknown";
        return $"{One("health", MageTemplate.MaxHealth)}, {One("stamina", MageTemplate.MaxStamina)}, {One("mana", MageTemplate.MaxMana)}";
    }

    /// <summary>The healing kits held (main pack and side packs).</summary>
    /// <param name="state">The state.</param>
    /// <param name="scratch">A reusable list.</param>
    public static int Kits(GameState state, List<uint> scratch)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(scratch);
        GearInventory.Collect(state, scratch);
        var kits = 0;
        foreach (var guid in scratch)
        {
            if (state.Objects.TryGet(guid, out var o) && o.Weenie.WeenieClassId == FighterKit.HealingKitWcid)
            {
                kits++;
            }
        }

        return kits;
    }

    private static bool Below(PlayerQualities q, uint maxId, double percent)
    {
        if (percent <= 0)
        {
            return false;
        }

        if (!q.IsKnown || !q.TryGetVitalLevels(maxId, out var current, out var max) || max == 0)
        {
            return true; // unknown vitals cannot meet a need
        }

        return current * 100.0 / max < percent;
    }
}

/// <summary>
/// The one adapter from an embedded scenario to a station (M4 P5b 3.4): its run asserts the station's needs against <c>session.State</c> on its first
/// step (a miss ends the run Failed with <c>why=precondition:&lt;x&gt;</c>, never a place), then creates and steps the wrapped scenario and reads its
/// fields at the end. J4 wires gear, buff, den-fight, death, hunt and vendor through it.
/// </summary>
/// <param name="name">The station's name.</param>
/// <param name="needs">The station's needs.</param>
/// <param name="create">Creates the embedded scenario (no login, no place of its own, no leave: <c>endSession:false</c>).</param>
/// <param name="fields">Reads the scenario's verdict fields at its end (today's leg names).</param>
/// <param name="ok">Judges the scenario by its leg's verdict rules from the scenario and its last status.</param>
/// <param name="judgeWhy">Names the failing judges after <paramref name="ok"/> ran (M4 P5b fix FC-b1), or null: the station's <c>why=</c> when the scenario itself has no failure sentence.</param>
/// <param name="sample">Called once when the scenario is made and again after each of its steps (M5 item 5 c1: the den-fight station's <see cref="PhysicsSpan"/> reads the host's physics cost); null for a station that samples nothing.</param>
/// <param name="fightGuard">
/// M6 14 precond: for a station whose own job is the fight (the den fight, the hunt), its in-combat health guard on her live state (<see cref="FightGuard"/>: the fight's emergency line, the hunt's retreat share), so a health need missed with a
/// hostile near starts the fight while she is over that line; null for every other station (a hostile near then ends it by name).
/// </param>
public sealed class ScenarioStation(string name, StationNeeds needs, Func<JourneyContext, IScenario> create, Func<IScenario, string> fields, Func<IScenario, ScenarioStatus, bool> ok, Func<string?>? judgeWhy = null, Action<JourneyContext>? sample = null, Func<GameState, FightGuard?>? fightGuard = null) : IStation
{
    private readonly Func<GameState, FightGuard?>? _fightGuard = fightGuard;

    private readonly Func<JourneyContext, IScenario> _create = create ?? throw new ArgumentNullException(nameof(create));
    private readonly Func<IScenario, string> _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly Func<string?>? _judgeWhy = judgeWhy;
    private readonly Action<JourneyContext>? _sample = sample;
    private readonly Func<IScenario, ScenarioStatus, bool> _ok = ok ?? throw new ArgumentNullException(nameof(ok));

    /// <inheritdoc />
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

    /// <inheritdoc />
    public StationNeeds Needs { get; } = needs ?? throw new ArgumentNullException(nameof(needs));

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new Run(this, context);
    }

    private sealed class Run(ScenarioStation station, JourneyContext context) : IStationRun, IStationInner, IDisposable
    {
        public IScenario? Scenario => _scenario;

        private IScenario? _scenario;
        private ScenarioStatus _status = ScenarioStatus.Running;
        private string? _precondition;
        private bool _checked;
        private string? _restoring; // M6 14 precond: the vital the keeper's pass restores before the station runs
        private bool _restoreTried;
        private double _restoreStart = double.NaN;
        private bool _guardStart; // M6 14 precond: the fight starts under the station's health need, over its own guard's line, with a hostile near

        public string Name => station.Name;

        public string Stage => _precondition is not null ? "precondition" : _restoring is not null ? "restore:" + _restoring : _scenario?.Stage ?? "start";

        public string? Failure => _precondition is not null ? "precondition:" + _precondition : _scenario?.Failure ?? (_status != ScenarioStatus.Running ? station._judgeWhy?.Invoke() : null);

        public double? WhereMaxMetres => _scenario?.WhereMaxMetres;

        public long Unmatched => _scenario?.Unmatched ?? 0;

        public bool Ok => _precondition is null && _scenario is not null && _status != ScenarioStatus.Running && station._ok(_scenario, _status);

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            ArgumentNullException.ThrowIfNull(session);
            if (_status != ScenarioStatus.Running)
            {
                return _status;
            }

            if (_restoring is not null)
            {
                return RestoreStep(session, now);
            }

            if (!_checked)
            {
                if (StationPreconditions.Miss(_guardStart ? station.Needs with { MinHealthPercent = 0 } : station.Needs, session.State, context.Log, context.Dungeon) is { } miss)
                {
                    if (!_restoreTried && StationPreconditions.IsVital(miss) && BeginRestore(session, now, miss))
                    {
                        return _restoring is null ? Step(session, now) : ScenarioStatus.Running; // met at once: checked again; else the keeper's pass runs
                    }

                    _checked = true;
                    _precondition ??= miss;
                    context.Log.Warn(JourneyVerdict.LogCategory, $"station {station.Name}: precondition {_precondition} does not hold at its first step: ended without running (why=precondition:{_precondition})");
                    return _status = ScenarioStatus.Failed;
                }

                _checked = true;
                _scenario = station._create(context);
                station._sample?.Invoke(context);
            }

            _status = _scenario!.Step(session, now);
            station._sample?.Invoke(context);
            return _status;
        }

        /// <summary>
        /// M6 14 precond (live 2026-11-17 06:29:00, ladder54: the Atlan wand's mote hunt, a den-fight sub-run of the quest station, met her at 170/219 health, under the fight stations' 80% (<see cref="JourneyPlans.FightHealthPercent"/>, Lifestone's
        /// own, the hunt's 80% heal rule) and ended "precondition health does not hold at its first step" with 403/410 mana and Heal Self in her book): check, act, check. A vital need (health, stamina, mana) she can restore herself
        /// is restored first by the journey's keeper (<see cref="VitalsKeeper"/>: Heal Self or her heal pick, the stamina-to-mana dance, a rest), with its check before and after logged, and the station then runs. It ends by name when
        /// she cannot: no keeper in the journey (<c>precondition:health:no-keeper</c>), a hostile within the awake reach (<c>:under-attack</c>; she does not sit down to heal among them, the survival layer's), the keeper blocked
        /// (<c>:&lt;its why&gt;</c>), its pass over <see cref="JourneyScenario.VitalsTimeoutSeconds"/>, or the need still missed after the pass. One pass a station, never a loop. False when no pass is begun (the station ends).
        /// </summary>
        private bool BeginRestore(IGameSessionView session, double now, string miss)
        {
            _restoreTried = true;
            var needs = station.Needs;
            var before = StationPreconditions.Shares(session.State);
            if (context.Keeper is not { } keeper)
            {
                _precondition = miss + ":no-keeper";
                context.Log.Warn(JourneyVerdict.LogCategory, $"station {station.Name}: precondition {miss}: check before: {before}; the journey runs no vitals keeper, so she cannot restore it here");
                return false;
            }

            var hostiles = HostileReach.CountWithin(session, FightScenario.PackPeakReachMetres);
            if (hostiles > 0 && miss == "health" && station._fightGuard?.Invoke(session.State) is { } guard
                && session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var healthMax) && healthMax > 0)
            {
                // M6 14 precond (the coordinator's amendment): a fight station with a hostile near does not sit down to heal and does not end: over its own guard's line the fight starts and its guard heals in combat; under it the
                // station ends by name and the survival layer above it decides.
                var line = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"health {health}/{healthMax} under the station's {needs.MinHealthPercent:0}% with {hostiles} hostile(s) within {FightScenario.PackPeakReachMetres:0} m (the nearest at {NearestMetres(session):0.#} m)");
                if (health > guard.LineHealth)
                {
                    _guardStart = true;
                    context.Log.Info(JourneyVerdict.LogCategory, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"station {station.Name}: precondition health: check before: {line}: the fight starts; its guard heals in combat, {guard.Words} is {guard.LineHealth:0} ({guard.LineHealth * 100 / healthMax:0}%)"));
                    return true;
                }

                _precondition = miss + ":under-attack";
                context.Log.Warn(JourneyVerdict.LogCategory, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"station {station.Name}: precondition health: check before: {line}, at or under {guard.Words} {guard.LineHealth:0}: the fight does not start; the survival layer decides"));
                return false;
            }

            if (hostiles > 0)
            {
                _precondition = miss + ":under-attack";
                context.Log.Warn(JourneyVerdict.LogCategory, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"station {station.Name}: precondition {miss}: check before: {before}; {hostiles} hostile(s) within {FightScenario.PackPeakReachMetres:0} m (the awake reach): no keeper's pass among them"));
                return false;
            }

            context.Log.Info(JourneyVerdict.LogCategory, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"station {station.Name}: precondition {miss}: check before: {before} (needs health {needs.MinHealthPercent:0}%, stamina {needs.MinStaminaPercent:0}%, mana {needs.MinManaPercent:0}%); act: the keeper's pass restores it, then the station runs"));
            if (keeper.NeedsFor(new VitalsNeeds((int)Math.Ceiling(needs.MinHealthPercent), (int)Math.Ceiling(needs.MinStaminaPercent), (int)Math.Ceiling(needs.MinManaPercent))))
            {
                context.Log.Info(JourneyVerdict.LogCategory, $"station {station.Name}: precondition {miss}: check after: the keeper finds every need met now ({StationPreconditions.Shares(session.State)})");
                return true;
            }

            _restoring = miss;
            _restoreStart = now;
            return true;
        }

        /// <summary>The distance to the nearest hostile within the awake reach, where the session draws it, or NaN.</summary>
        private static double NearestMetres(IGameSessionView session)
        {
            var nearest = double.NaN;
            foreach (var metres in HostileReach.Distances(session))
            {
                nearest = double.IsNaN(nearest) ? metres : Math.Min(nearest, metres);
            }

            return nearest;
        }

        private ScenarioStatus RestoreStep(IGameSessionView session, double now)
        {
            var keeper = context.Keeper!;
            var miss = _restoring!;
            switch (keeper.Step(now))
            {
                case VitalsKeeperState.Ready:
                    _restoring = null;
                    context.Log.Info(JourneyVerdict.LogCategory, $"station {station.Name}: precondition {miss}: check after: {StationPreconditions.Shares(session.State)}; the keeper's pass is done, the needs are read again");
                    return Step(session, now);
                case VitalsKeeperState.Blocked:
                    return EndRestore(session, miss + ":" + (keeper.Why ?? "blocked"), "the keeper is blocked: " + (keeper.Why ?? "no reason given"));
                default:
                    return now - _restoreStart > JourneyScenario.VitalsTimeoutSeconds
                        ? EndRestore(session, miss + ":timeout", string.Create(System.Globalization.CultureInfo.InvariantCulture, $"the keeper's pass ran over {JourneyScenario.VitalsTimeoutSeconds:0} s"))
                        : ScenarioStatus.Running;
            }
        }

        private ScenarioStatus EndRestore(IGameSessionView session, string precondition, string why)
        {
            _restoring = null;
            _checked = true;
            _precondition = precondition;
            context.Log.Warn(JourneyVerdict.LogCategory, $"station {station.Name}: precondition {precondition}: check after: {StationPreconditions.Shares(session.State)}; {why}: ended without running (why=precondition:{precondition})");
            return _status = ScenarioStatus.Failed;
        }

        public string VerdictFields()
        {
            if (_precondition is not null)
            {
                return "why=precondition:" + _precondition;
            }

            return _scenario is null ? string.Empty : station._fields(_scenario);
        }

        public void Dispose() => (_scenario as IDisposable)?.Dispose();
    }
}

/// <summary>M6 14 precond: a fight station's in-combat health guard on her live state: the line in health points under which its own guard acts, and its name for the log.</summary>
/// <param name="LineHealth">The line, in health points.</param>
/// <param name="Words">What the line is, e.g. "the emergency line".</param>
public readonly record struct FightGuard(double LineHealth, string Words);
