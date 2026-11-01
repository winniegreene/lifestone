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
public sealed class ScenarioStation(string name, StationNeeds needs, Func<JourneyContext, IScenario> create, Func<IScenario, string> fields, Func<IScenario, ScenarioStatus, bool> ok, Func<string?>? judgeWhy = null, Action<JourneyContext>? sample = null) : IStation
{
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

        public string Name => station.Name;

        public string Stage => _precondition is not null ? "precondition" : _scenario?.Stage ?? "start";

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

            if (!_checked)
            {
                _checked = true;
                if (StationPreconditions.Miss(station.Needs, session.State, context.Log, context.Dungeon) is { } miss)
                {
                    _precondition = miss;
                    context.Log.Warn(JourneyVerdict.LogCategory, $"station {station.Name}: precondition {miss} does not hold at its first step: ended without running (why=precondition:{miss})");
                    return _status = ScenarioStatus.Failed;
                }

                _scenario = station._create(context);
                station._sample?.Invoke(context);
            }

            _status = _scenario!.Step(session, now);
            station._sample?.Invoke(context);
            return _status;
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
