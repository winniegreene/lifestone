using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The pre-trip recovery (M4 3.1 d1 follow-up F1, D20): with the death loop's fight on, the corpse trip comes after the fight, and a mage that took blows may be under the trip's health precondition
/// (<see cref="DeathOptions.RecoverMinHealthPercent"/>, 60% by default), which would skip the trip and defeat D20. Before the corpse stage declares that precondition failed (every other precondition holding),
/// stage <c>pretrip-heal</c> heals the mage: while the health share is under the minimum it casts Heal Self VII (<see cref="HealSelfVii"/>, <see cref="ArenaProfile.FightExtras"/>) through
/// <c>IActionApi.Cast</c>, at most <see cref="PreTripMaxHeals"/> casts, each logged (the share before, the outcome, the share after); a cast is skipped when the mana is under the spell's cost
/// (<see cref="GearCasts.ManaCeiling"/>) or when the spell is not in the SpellTable, and a cast that ends with anything but a fizzle or a success ends the casting; then, still under the minimum, it waits
/// for regeneration up to <see cref="PreTripRegenSeconds"/> s of the session clock (logged on each 10% step). Only a mage still under the minimum after that skips the trip, with <c>TODO(m4-death-trip-health)</c>.
/// </summary>
/// <remarks>
/// <para><b>The combat mode.</b> The fight scenario ends in non-combat (its <c>mode-peace</c> stage, then the leave from the den), and a cast needs the magic combat mode: the client refuses a cast made in any other mode
/// (<c>StanceGuards.CastRefusal</c>, D40; ACE would end it with a silent UseDone), so the stage enters the magic mode before its first cast and leaves it for non-combat when it is done. A mage already in the magic mode casts at once.</para>
/// <para>Not in M4 2.2's order (the fight off): the trip then starts at the respawn's 75% and the precondition keeps its old behaviour. The stage reads the mana once per decision and the health on each frame; it allocates nothing but log lines on events.
/// A cast skipped for mana does not resume when the mana regenerates (the wait is for the health to regenerate; <c>TODO(m4-death-trip-heal-resume)</c>).</para>
/// </remarks>
public sealed partial class DeathScenario
{
    /// <summary>Heal Self VII, the spell the pre-trip recovery casts (<see cref="ArenaProfile.FightExtras"/>; the mage knows it).</summary>
    public const uint HealSelfVii = 2073;

    /// <summary>The most Heal Self casts of the pre-trip recovery.</summary>
    public const int PreTripMaxHeals = 4;

    /// <summary>The most seconds of session clock the recovery waits for the health to regenerate after its casts.</summary>
    public const double PreTripRegenSeconds = 60;

    private enum HealPhase
    {
        Decide,
        Mode,
        Cast,
        Regen,
        Peace,
    }

    private HealPhase _healPhase;
    private int _pretripHeals;
    private bool _healTried;
    private bool _healNoCast;
    private double _healRegenStart = double.NaN;
    private int _healBucket = -1;
    private double _healBefore;

    /// <summary><c>pretrip_heals=</c>: the Heal Self casts the pre-trip recovery sent (0 when it did not run).</summary>
    public int PretripHeals => _pretripHeals;

    /// <summary>
    /// Called by the corpse stage before it judges the health precondition: true when the recovery began (the stage is entered), false when it is not due (the fight is off, it ran already, the health is not under the minimum or unreadable,
    /// or another precondition fails: then the corpse stage names that one).
    /// </summary>
    private bool TryBeginPretripHeal(IGameSessionView session, double clock, double health)
    {
        if (!FightOn || _healTried || double.IsNaN(health) || health >= _options.RecoverMinHealthPercent || CorpsePrecondition(session, 100) is not null)
        {
            return false;
        }

        _healTried = true;
        _healPhase = HealPhase.Decide;
        _healNoCast = false;
        _log.Info(
            LogCategory,
            string.Create(
                CultureInfo.InvariantCulture,
                $"corpse: the health share is {health:0}% after the fight, under --death-recover-min-health {_options.RecoverMinHealthPercent:0.##}%: recovering before the trip (up to {PreTripMaxHeals} casts of Heal Self VII, spell {HealSelfVii}, then up to {PreTripRegenSeconds:0} s of regeneration)"));
        Enter("pretrip-heal", clock);
        return true;
    }

    private ScenarioStatus PretripHealStep(IGameSessionView session, double clock)
    {
        if (OrderGuard(session, "pretrip-heal") is { } died)
        {
            return died;
        }

        switch (_healPhase)
        {
            case HealPhase.Mode:
                if (_pending is not { Outcome: { } mode })
                {
                    return ScenarioStatus.Running;
                }

                _pending = null;
                if (!mode.Succeeded)
                {
                    _healNoCast = true;
                    _log.Warn(LogCategory, "pretrip: the magic combat mode was not entered: " + Describe(mode) + "; no cast, regeneration only");
                }

                _healPhase = HealPhase.Decide;
                return ScenarioStatus.Running;

            case HealPhase.Cast:
                return HealCastDone(session);

            case HealPhase.Regen:
                return HealRegen(session, clock);

            case HealPhase.Peace:
                if (_pending is not { Outcome: { } peace })
                {
                    return ScenarioStatus.Running;
                }

                _pending = null;
                if (!peace.Succeeded)
                {
                    _log.Warn(LogCategory, "pretrip: the mage was left in the magic combat mode: " + Describe(peace)); // report-only, as the fight scenario's mode-peace
                }

                return AfterPretripHeal(session, clock);

            default:
                return HealDecide(session, clock);
        }
    }

    private ScenarioStatus HealDecide(IGameSessionView session, double clock)
    {
        if (!TryHealthShare(session, out var share))
        {
            _log.Warn(LogCategory, "pretrip: the health share cannot be read; ending the recovery");
            return FinishHeal(session, clock);
        }

        var minimum = _options.RecoverMinHealthPercent;
        if (share >= minimum)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"pretrip: the health share is {share:0}%, at the minimum {minimum:0.##}% after {_pretripHeals} cast(s); the trip's precondition holds"));
            return FinishHeal(session, clock);
        }

        if (!_healNoCast && _pretripHeals < PreTripMaxHeals)
        {
            var q = session.State.Self.Qualities;
            if (!_spells.TryGetValue(HealSelfVii, out var info))
            {
                _healNoCast = true;
                _log.Warn(LogCategory, $"pretrip: Heal Self VII (spell {HealSelfVii}) is not in the SpellTable: no cast, regeneration only");
            }
            else
            {
                var cost = GearCasts.ManaCeiling(info, session.State.Self.Inventory.Equipped.Count);
                q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _);
                if (mana < cost)
                {
                    _healNoCast = true;
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"pretrip: the mana {mana} is under the {cost} {info.Name} costs: no cast, regeneration only"));
                }
                else if (q.CombatMode != CombatModes.Magic)
                {
                    _pending = session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Host); // a cast needs the magic combat mode; the fight left the mage in non-combat
                    _healPhase = HealPhase.Mode;
                    return ScenarioStatus.Running;
                }
                else
                {
                    _healBefore = share;
                    _pretripHeals++;
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"pretrip: heal {_pretripHeals} of {PreTripMaxHeals}: {info.Name}, health {share:0}% before, mana {mana} (cost {cost})"));
                    _pending = session.Actions.Cast(HealSelfVii, ActionSource.Host);
                    _healPhase = HealPhase.Cast;
                    return ScenarioStatus.Running;
                }
            }
        }

        _healPhase = HealPhase.Regen;
        _healRegenStart = clock;
        _healBucket = -1;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"pretrip: health {share:0}% is under the minimum {minimum:0.##}% with no cast to make; waiting up to {PreTripRegenSeconds:0} s for regeneration"));
        return ScenarioStatus.Running;
    }

    private ScenarioStatus HealCastDone(IGameSessionView session)
    {
        if (_pending is not { Outcome: { } cast })
        {
            return ScenarioStatus.Running;
        }

        _pending = null;
        var after = TryHealthShare(session, out var share) ? share.ToString("0", CultureInfo.InvariantCulture) : "n/a";
        var before = _healBefore.ToString("0", CultureInfo.InvariantCulture);
        if (cast.Succeeded)
        {
            _log.Info(LogCategory, $"pretrip: heal {_pretripHeals} landed ({cast.Kind}): health {before}% to {after}%");
        }
        else if (cast.Kind == ActionOutcomeKind.ServerError && cast.Code == GearCasts.FizzleCode)
        {
            _log.Info(LogCategory, $"pretrip: heal {_pretripHeals} fizzled: health {before}% to {after}%");
        }
        else
        {
            _healNoCast = true;
            _log.Warn(LogCategory, $"pretrip: heal {_pretripHeals} ended {Describe(cast)}: health {before}% to {after}%; no more casts");
        }

        _healPhase = HealPhase.Decide;
        return ScenarioStatus.Running;
    }

    private ScenarioStatus HealRegen(IGameSessionView session, double clock)
    {
        if (!TryHealthShare(session, out var share))
        {
            return FinishHeal(session, clock);
        }

        var bucket = (int)(share / 10);
        if (bucket != _healBucket)
        {
            _healBucket = bucket;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"pretrip: regeneration: health {share:0}% after {clock - _healRegenStart:0} s"));
        }

        if (share >= _options.RecoverMinHealthPercent)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"pretrip: the health share is {share:0}%, at the minimum {_options.RecoverMinHealthPercent:0.##}% after {clock - _healRegenStart:0} s of regeneration"));
            return FinishHeal(session, clock);
        }

        if (clock - _healRegenStart >= PreTripRegenSeconds)
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"pretrip: {PreTripRegenSeconds:0} s of regeneration left the health share at {share:0}%, under the minimum {_options.RecoverMinHealthPercent:0.##}%"));
            return FinishHeal(session, clock);
        }

        return ScenarioStatus.Running;
    }

    /// <summary>The end of the recovery: out of the magic mode the casts needed, then the corpse stage's preconditions are judged again.</summary>
    private ScenarioStatus FinishHeal(IGameSessionView session, double clock)
    {
        if (session.State.Self.Qualities.CombatMode != CombatModes.NonCombat)
        {
            _pending = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
            _healPhase = HealPhase.Peace;
            return ScenarioStatus.Running;
        }

        return AfterPretripHeal(session, clock);
    }

    private ScenarioStatus AfterPretripHeal(IGameSessionView session, double clock) =>
        TryBeginCorpse(session, clock) ? ScenarioStatus.Running : NextPost(session, clock);
}
