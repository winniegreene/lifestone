using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 16 E3.0 (docs/automation-engine.md E3, <c>TODO(engine-guard-over-subfight)</c>): evidence for one decision, report-only. A quest's sub-fight runs in the Trip state, where the survival layer and the vitals guard
/// are on, and the fight under them has its own emergency, drain rule and way out: two deciders of one heal. When the guard holds the station (its heal pick, the lanes it waits for, its own cast) while the station steps
/// a fight inside it (<see cref="IStationSubFight"/>), one line says what each of the two holds at that moment, once a sub-fight:
/// <c>survival-shadow: guard-over-subfight: &lt;station&gt;: the vitals guard ...; the fight under it ...</c>. It changes nothing: the guard holds and heals as before, and the fight is not stepped while it does, as before.
/// <para>M6 16 E3.1: the decision is taken (<c>AutopilotScenario.SubFightWatch.cs</c>): over the mage's fight with its emergency on and no stop asked, the guard no longer runs, so the line no longer fires there. It stays
/// for the states where the guard still acts above a station's fight: a fight that is not the mage's (no heal decider of its own), one whose emergency is off, and one that has been asked to stop.</para>
/// </summary>
public sealed partial class AutopilotScenario
{
    private IStoppableFight? _subFightSaid;

    /// <summary>M6 16 E3.0: the sub-fights the vitals guard held over this run (each said once).</summary>
    public int GuardOverSubFights { get; private set; }

    /// <summary>The guard holds this frame: when the running station steps a fight inside it, the one line of the two deciders (once a sub-fight).</summary>
    private void GuardOverSubFight(IGameSessionView session)
    {
        if (_guard is not { } guard || _run is not IStationSubFight { SubFight: { } fight } || ReferenceEquals(fight, _subFightSaid))
        {
            return;
        }

        _subFightSaid = fight;
        GuardOverSubFights++;
        var q = session.State.Self.Qualities;
        q.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var healthMax);
        q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax);
        var theirs = fight is FightScenario real
            ? "the fight under it is not stepped while the guard holds, and holds its own deciders: " + real.HealDecidersWords()
            : "the fight under it is not stepped while the guard holds (not the mage's fight: no heal decider of its own is read)";
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{FightScenario.SurvivalShadowToken}guard-over-subfight: {_spec?.Name ?? "?"}: the vitals guard {guard.HoldWords} at health {health}/{healthMax}, mana {mana}/{manaMax}, under its line {guard.LineHealth:0} ({guard.Casts} cast(s) started, {guard.Landed} landed this run); {theirs}; two deciders of one heal (TODO(engine-guard-over-subfight)); said once a sub-fight, report-only"));
    }
}
