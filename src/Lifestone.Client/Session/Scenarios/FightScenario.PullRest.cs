using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions.Trackers;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 7 AB: the rest before the next pull (r3d, level 10 in the Burrow: a kill cost 3 casts, she walked 62 m to the next tusker at 57 of 133 mana, the heals ran it dry and she died with the tusker at 91%).
/// At a <c>Choose</c> point with no attacker, after the emergency and the buff refresh and before the pick and any walk, a vital under the ground's floor (<see cref="GroundPullRest"/>, the profile's data; a ground without it has no rule) starts a rest where she
/// stands: peace, the posture, the wait, the stop key, the magic mode.
/// <para><b>The dance</b> (M6 7 AD: it needs no Mana Conversion, ACE only discounts the cost with it, <c>Creature_Magic.cs:41-44</c> @95ef426d). A character who knows Stamina to Mana Self rests by it: she sits until the stamina is near full
/// (<see cref="PullRestRule.PressStaminaShare"/>; sitting is 2.5 times standing, stamina takes the stance factor and mana does not), stands, takes the magic mode, presses once (<see cref="PullRestRule.PressLevel"/>: never a press that nets nothing), takes peace, sits again, on to
/// the rest's top. Each press is checked before (the vitals read again, nothing cut the rest) and after (the mana read back, one line with its net; a fizzle is the cast's own count on that line, and a dance that did not land is set aside as in the fight). An attacker cuts it at any step but
/// the 3 s of a cast already sent, and she stands in the magic mode then. The rest's seconds and its bound are unchanged; a rate is measured only from a rest with no press, so the bound stays the regeneration's.</para>
/// <para>The fight rests itself and not through <see cref="VitalsKeeper"/>: the keeper runs between the autopilot's stations, heals with mana, and stops only at its cap, where this rest answers the first hit. The keys and their settling are the keeper's
/// (<see cref="VitalsKeeper.SitCommand"/>, the emote key pressed and released; the stop key to stand).</para>
/// <para><b>Check, act, check.</b> Before: no living target of the ground within <see cref="GroundPullRest.AggroMetres"/> with a clear line, the fight's own target not hurt and in range. During: a hit taken, a health fall, or such a creature ends the rest (M5 D88: she
/// stands and fights back), the vitals at their targets end it, and <see cref="PullRestPlan.BoundSeconds"/> bounds it. After: the magic mode is read back; a refused magic mode is the fight's named failure, as after the rest for stamina. No loop: a refused rest, or one
/// that ran out its bound, stays aside for <see cref="RestAsideSeconds"/>; one cut by a creature stays aside for <see cref="PullRestCutAsideSeconds"/> or until the next kill; the fight goes on meanwhile. Every rest logs one line with its before, its after and its seconds.</para>
/// <para><c>rest_s=</c> and <c>rests_pull=</c> (<see cref="PullRestFields"/>, report-only, D27) reach the cycle line and the <c>[ladder]</c> line.</para>
/// <para><b>The standard recharge</b> (M6 7 AN, the owner, 2026-11-06): lie down until the stamina is back, press Stamina to Mana I, repeat. ACE <c>GetStanceMod</c> (<c>Creature_Vitals.cs:210-236</c> @95ef426d): sleeping x3, sitting x2.5, crouch x2, standing x1, a combat mode or running x0.5, on health and
/// stamina; mana x1. The walker's stance is made non-combat first: retail's <c>CMotionInterp::DoMotion</c> (11.6096 0x00529930) refuses Sleeping outside it (<c>CantLieDownInCombat</c>), as <see cref="DeathScenario"/> found. Each stretch down logs its posture, the server's echo and the rates reached (<c>rest: stretch:</c>), each cycle its real seconds with the
/// mode changes and its mana a second overall (<c>rest: cycle:</c>), so ACE's multipliers are read against live numbers. A ground's fullness bar (<see cref="GroundPullRest.FullPercent"/>) is a hard line: a creature in sight turns the lying rest into a standing one, never into a pull; a hold logs itself once; only an attacker is fought (M5 D88). <c>rest_s_per_kill=</c> is the recharge's cost in the verdict.</para>
/// </summary>
public sealed partial class FightScenario
{
    /// <summary>How often a rest looks for a creature in sight, in seconds (Lifestone's own: under a run's metre at 4 m/s, and the line test is not free).</summary>
    public const double PullRestLookSeconds = 0.25;

    /// <summary>How long a rest a creature cut stays aside, in seconds, unless a kill comes first (Lifestone's own: a creature that comes in and out of sight does not flip the modes).</summary>
    public const double PullRestCutAsideSeconds = 10;

    /// <summary>A rest's smallest gain that measures the mana rate (Lifestone's own: under it one heartbeat's rounding is the measure).</summary>
    private const uint PullRestMeasureMana = 5;

    private PullRestStage _pullRestStage;
    private PullRestPlan _pullRestPlan;
    private ActionHandle? _pullRestPending;
    private ActionHandle? _pullRestPress;
    private bool _pullRestPostureDown;
    private bool _pullRestRefusedLogged;
    private string? _pullRestCut;
    private double _pullRestStart = double.NaN;
    private double _pullRestLookAt;
    private double _pullRestAsideUntil = double.NegativeInfinity;
    private int _pullRestCutKills = -1;
    private int _pullRestHits;
    private uint _pullRestManaFrom;
    private uint _pullRestHealthFrom;
    private double _pullRestManaRate = PullRestRule.SeedManaPerSecond;
    private double _pullRestDoneSeconds;
    private int _pullRestPressIndex = -1;
    private int _pullRestLandedMark;
    private uint _pullRestPressMana;
    private uint _pullRestPressStamina;
    private double _pullRestPressAt;
    private int _pullRestPresses;
    private int _pullRestNet;
    private bool _pullRestStanding;
    private bool _pullBarHoldNoted;
    private ActionHandle? _pullRestStance;
    private double _stretchAt = double.NaN;
    private uint _stretchMana;
    private uint _stretchStamina;
    private double _cycleAt = double.NaN;
    private uint _cycleMana;
    private double _cycleDownSeconds;
    private uint _pullDrinkMana;
    private double _pullDrinkAt;
    private int _pullDrinkBusy;

    /// <summary>ACE's WeenieError YoureTooBusy, the UseDone of a use sent while a motion still runs.</summary>
    private const uint PullDrinkBusyCode = 0x1D;

    /// <summary>The tries a too-busy drink gets before the potions stand aside (Lifestone's own: a kill's last motion is over within two seconds).</summary>
    public const int PullDrinkBusyTries = 4;

    /// <summary>The wait between them, in seconds (Lifestone's own).</summary>
    public const double PullDrinkBusySeconds = 0.7;

    private enum PullRestStage
    {
        None,
        ToPeace,
        Down,
        Stance,
        Settling,
        Resting,
        Standing,
        ToMagic,
        Pressing,
        Drinking, // M6 8: a Mana Potion instead of the rest (m6-potion-before-rest)
        DrinkVital,
        DrinkBusy,
    }

    /// <summary>The rests before a pull that started (M6 7 AB).</summary>
    public int PullRests { get; private set; }

    /// <summary>The rests a hit or a creature in sight cut (M6 7 AB).</summary>
    public int PullRestsCut { get; private set; }

    /// <summary>The rests wanted and refused: a creature in sight, or the peace not entered (M6 7 AB).</summary>
    public int PullRestsRefused { get; private set; }

    /// <summary>The seconds at rest before a pull, a rest still open counted to now (<c>rest_s=</c>, M6 7 AB).</summary>
    public double PullRestSeconds => _pullRestDoneSeconds + OpenSeconds(_pullRestStart);

    /// <summary>The presses of Stamina to Mana Self sent by the rests before a pull (M6 7 AD).</summary>
    public int PullRestPressesSent { get; private set; }

    /// <summary>The mana those presses netted, read back (M6 7 AD).</summary>
    public int PullRestPressNet { get; private set; }

    /// <summary>The verdict's <c>rest_s= rests_pull= rests_cut= rests_refused= rest_presses= rest_net=</c> (M6 7 AB, AD, report-only, D27: the optimizer's share of the time at rest).</summary>
    public string PullRestFields() => string.Create(CultureInfo.InvariantCulture, $"rest_s={PullRestSeconds:0} rests_pull={PullRests} rests_cut={PullRestsCut} rests_refused={PullRestsRefused} rest_presses={PullRestPressesSent} rest_net={PullRestPressNet} rest_s_per_kill={(_tally.Kills > 0 ? PullRestSeconds / _tally.Kills : 0):0.#}");

    /// <summary>The gate at a <c>Choose</c> point with no attacker: true when a rest started (the phase is the rest's).</summary>
    private bool PullRestBegins(IGameSessionView session, double clock, in CellFrame here)
    {
        if (PullRule(session) is not { } rule)
        {
            return false;
        }

        if (clock < _pullRestAsideUntil && (_pullRestCutKills < 0 || _pullRestCutKills == _tally.Kills))
        {
            return false;
        }

        var q = session.State.Self.Qualities;
        if (!q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax) || !q.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var healthMax))
        {
            return false;
        }

        q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax);
        var input = new PullRestInput(mana, manaMax, health, healthMax, rule, _pullRestManaRate, stamina, staminaMax);
        var plan = PullRestRule.Decide(in input);
        if (PullHealBegins(session, clock, rule, mana, manaMax, health, healthMax))
        {
            return true; // M6 14: Heal Self first, while the mana pays it over the pull's own; the next Choose asks again
        }

        if (rule.ToFloors && PullPressBegins(session, clock, mana, manaMax, stamina, staminaMax))
        {
            return true; // M6 8 (the owner): Stamina to Mana first; the next Choose asks again
        }

        if (!plan.Wanted)
        {
            _pullRestRefusedLogged = false;
            return false;
        }

        if (rule.Potions && mana < plan.ManaTarget && ManaDrinks.Fits(session.State, _scratch) && PullRestDrinkBegins(session, clock, mana, manaMax, in plan))
        {
            return true; // M6 8: the potion first; the next Choose asks again (another potion, the rest for what is still short, or the pull)
        }

        var standing = false;
        if (PullRestHostile(session, in here, rule, out var what, out var hurtTarget))
        {
            if (rule.FullPercent > 0 && !hurtTarget)
            {
                standing = true; // M6 7 AN (the owner): under the bar a creature in sight is no reason to pull: she rests on her feet, ready to answer it
                _log.Info(LogCategory, $"rest: {what}: she does not lie down with it awake, and under the bar she does not pull: a standing rest");
            }
            else
            {
                if (!_pullRestRefusedLogged)
                {
                    _pullRestRefusedLogged = true;
                    PullRestsRefused++;
                    _log.Info(LogCategory, $"rest: refused: {plan.Why}, but {what}; the fight goes on");
                }

                return false;
            }
        }

        _pullRestRefusedLogged = false;
        _pullBarHoldNoted = false;
        _pullRestStanding = standing;
        PullRests++;
        _pullRestPlan = plan;
        (_pullRestManaFrom, _pullRestHealthFrom, _pullRestHits) = (mana, health, _tally.HitsTaken + _tally.Evades);
        (_pullRestStart, _pullRestLookAt, _pullRestCut, _pullRestPresses, _pullRestNet, _pullRestPressIndex) = (clock, clock + PullRestLookSeconds, null, 0, 0, -1);
        (_cycleAt, _cycleMana, _cycleDownSeconds, _stretchAt) = (clock, mana, 0, double.NaN);
        _log.Info(
            LogCategory,
            string.Create(
                CultureInfo.InvariantCulture,
                $"rest: begins before the next pull: {plan.Why}; on to mana {plan.ManaTarget}, stamina {plan.StaminaTarget}, health {plan.HealthTarget} (until {rule.UntilPercent}%, bar {rule.FullPercent}%), posture {(standing ? "stand" : rule.Posture.ToString().ToLowerInvariant())}, at {_pullRestManaRate:0.##} mana a second at most {plan.BoundSeconds:0} s; no attacker{(standing ? ", a creature in sight" : $", nothing in sight within {rule.AggroMetres:0.#} m")}"));
        _phase = Phase.PullRest;
        if (PullRestPressDue(session, clock) is >= 0 and var first)
        {
            _pullRestPressIndex = first; // the stamina is already near full: the press first, then the sit
            PullRestToMagic(session, q);
        }
        else if (q.CombatMode == CombatModes.NonCombat)
        {
            _pullRestStage = PullRestStage.Down;
        }
        else
        {
            _pullRestStage = PullRestStage.ToPeace;
            _pullRestPending = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Automation);
        }

        return true;
    }

    /// <summary>One step of the rest; allocation-free while it waits.</summary>
    private ScenarioStatus PullRestRun(IGameSessionView session, double clock)
    {
        var rule = _ground.PullRest!;
        var q = session.State.Self.Qualities;
        switch (_pullRestStage)
        {
            case PullRestStage.ToPeace:
                if (_pullRestPending is not { Outcome: { } peace })
                {
                    return ScenarioStatus.Running;
                }

                _pullRestPending = null;
                if (!peace.Succeeded)
                {
                    PullRestsRefused++;
                    _pullRestCut = "the non-combat mode was refused";
                    _pullRestAsideUntil = clock + RestAsideSeconds;
                    _pullRestCutKills = -1;
                    _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"rest: refused: the non-combat mode was not entered ({Describe(peace)}); the fight goes on, no rest for {RestAsideSeconds:0} s"));
                    return PullRestToMagic(session, q);
                }

                _pullRestStage = PullRestStage.Down;
                return ScenarioStatus.Running;

            case PullRestStage.Down:
                if (PullRestCutBy(session, clock, rule) is { } cutEarly)
                {
                    return PullRestCutNow(session, clock, q, cutEarly);
                }

                if (rule.Posture == RestPosture.Stand || _pullRestStanding || session.Actions.KillSwitch)
                {
                    PullRestStretchBegins(q, clock);
                    _pullRestStage = PullRestStage.Resting;
                    return ScenarioStatus.Running;
                }

                // M6 7 AN: the walker's stance first (retail refuses Sleeping outside non-combat, CMotionInterp::DoMotion 11.6096 0x00529930; DeathScenario's finding): a cast's TurnTo leaves it in the Magic stance
                _pullRestStance = session.Actions.Move.Command(CombatStances.NonCombat, true, ActionSource.Automation);
                _pullRestStage = PullRestStage.Stance;
                return ScenarioStatus.Running;

            case PullRestStage.Stance:
                if (_pullRestStance is not { Outcome: { } synced })
                {
                    return ScenarioStatus.Running;
                }

                _pullRestStance = null;
                if (synced.Kind is not (ActionOutcomeKind.Local or ActionOutcomeKind.Confirmed or ActionOutcomeKind.Sent))
                {
                    _log.Warn(LogCategory, $"rest: the posture {rule.Posture.ToString().ToLowerInvariant()} was not taken (the walker's stance was not synced to non-combat: {Describe(synced)}); resting standing in peace");
                    PullRestStretchBegins(q, clock);
                    _pullRestStage = PullRestStage.Resting;
                    return ScenarioStatus.Running;
                }

                var command = rule.Posture == RestPosture.Sit ? VitalsKeeper.SitCommand : VitalsKeeper.LieCommand;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"rest: {(rule.Posture == RestPosture.Sit ? "sitting" : "lying")} down (0x{command:X8}, the emote key pressed and released)"));
                _pullRestPress = session.Actions.Move.Command(command, true, ActionSource.Automation);
                _pullRestPending = session.Actions.Move.Command(command, false, ActionSource.Automation);
                _pullRestStage = PullRestStage.Settling;
                return ScenarioStatus.Running;

            case PullRestStage.Settling:
                if (_pullRestPending is not { Outcome: { } release })
                {
                    return ScenarioStatus.Running;
                }

                _pullRestPending = null;
                var press = _pullRestPress?.Outcome;
                _pullRestPress = null;
                _pullRestPostureDown = release.Kind == ActionOutcomeKind.Local && press is { Kind: ActionOutcomeKind.Local };
                if (!_pullRestPostureDown)
                {
                    _log.Warn(LogCategory, $"rest: the posture {rule.Posture.ToString().ToLowerInvariant()} was not taken ({Describe(press ?? release)}); resting standing in peace (mana takes no stance factor, the health stands at 1)");
                }

                PullRestStretchBegins(q, clock);
                _pullRestStage = PullRestStage.Resting;
                return ScenarioStatus.Running;

            case PullRestStage.Resting:
                if (PullRestCutBy(session, clock, rule) is { } cut)
                {
                    return PullRestCutNow(session, clock, q, cut);
                }

                q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMaxNow);
                q.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out _);
                q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out _);
                if (mana >= _pullRestPlan.ManaTargetNow(manaMaxNow) && health >= _pullRestPlan.HealthTarget && stamina >= _pullRestPlan.StaminaTarget)
                {
                    return PullRestStand(session, q);
                }

                if (clock - _pullRestStart >= _pullRestPlan.BoundSeconds)
                {
                    return PullRestBoundUp(session, q, clock, mana, health);
                }

                if (PullRestPressDue(session, clock) is >= 0 and var due)
                {
                    _pullRestPressIndex = due;
                    return PullRestStand(session, q); // stand, the magic mode, the press (PullRestToMagic)
                }

                return ScenarioStatus.Running;

            case PullRestStage.Standing:
                if (_pullRestPending is not { Outcome: { } stood })
                {
                    return ScenarioStatus.Running;
                }

                _pullRestPending = null;
                _pullRestPostureDown = false;
                if (stood.Kind != ActionOutcomeKind.Local)
                {
                    _log.Warn(LogCategory, $"rest: the stand-up key was not taken ({Describe(stood)}); the magic mode stands her up");
                }

                return PullRestToMagic(session, q);

            case PullRestStage.ToMagic:
                if (_pullRestPending is not { Outcome: { } magic })
                {
                    return ScenarioStatus.Running;
                }

                _pullRestPending = null;
                if (!magic.Succeeded)
                {
                    PullRestClose(clock, session);
                    return Fail(session, "fight: the magic combat mode was not entered after the rest before a pull: " + Describe(magic));
                }

                if (_pullRestPressIndex >= 0)
                {
                    return PullRestPress(session, q, clock);
                }

                PullRestClose(clock, session);
                _phase = Phase.Choose;
                return ScenarioStatus.Running;

            case PullRestStage.Pressing:
                var danced = DanceStep(session, clock);
                return danced != ScenarioStatus.Running || _manaPhase == ManaPhase.Dancing ? danced : PullRestPressed(session, q, clock);

            case PullRestStage.Drinking:
                if (_pullRestPending is not { Outcome: { } drunk })
                {
                    return ScenarioStatus.Running;
                }

                _pullRestPending = null;
                if (!drunk.Succeeded && drunk.Code == PullDrinkBusyCode && _pullDrinkBusy < PullDrinkBusyTries)
                {
                    // Live 2026-11-11 10:05 UTC: the first drink after a kill was refused as too busy (the last motion still ran) and the potions stood aside for the whole fight. Busy is a wait, not a failure.
                    _pullDrinkBusy++;
                    _pullDrinkAt = clock;
                    _pullRestStage = PullRestStage.DrinkBusy;
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"rest: potion: check after: the server was too busy for the Mana Potion (try {_pullDrinkBusy} of {PullDrinkBusyTries}); again in {PullDrinkBusySeconds:0.#} s (m6-potion-before-rest)"));
                    return ScenarioStatus.Running;
                }

                if (!drunk.Succeeded)
                {
                    _potionsAside = true;
                    _log.Warn(LogCategory, "rest: potion: check after: the Mana Potion was not drunk (" + Describe(drunk) + "); Mana Potions are set aside for the fight, the rest as before (m6-potion-before-rest)");
                    return PullRestDrinkEnds(session, clock);
                }

                _pullDrinkBusy = 0;
                _potionsDrunk++;
                PullRestPotions++;
                _pullDrinkAt = clock;
                _pullRestStage = PullRestStage.DrinkVital;
                return ScenarioStatus.Running;

            case PullRestStage.DrinkBusy:
                if (clock - _pullDrinkAt < PullDrinkBusySeconds)
                {
                    return ScenarioStatus.Running;
                }

                if (_tally.HitsTaken + _tally.Evades == _pullRestHits && VitalsMeans.FindManaPotions(session.State, _scratch, out var again, out _))
                {
                    _pullRestStage = PullRestStage.Drinking;
                    _pullRestPending = session.Actions.Drink(again, ActionSource.Automation);
                    return ScenarioStatus.Running;
                }

                _pullRestStage = PullRestStage.None; // an attack came, or none is held: the choice answers
                _phase = Phase.Choose;
                return ScenarioStatus.Running;

            case PullRestStage.DrinkVital:
                // As the fight's own potion step: ACE sends the vital's update 0.74 s into the use; no change within the wait is named and sets the potions aside.
                q.TryGetVitalLevels(MageTemplate.MaxMana, out var drunkMana, out var drunkMax);
                if (drunkMana != _pullDrinkMana)
                {
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"rest: potion: check after: the Mana Potion brought the mana from {_pullDrinkMana} to {drunkMana} of {drunkMax} (target {_pullRestPlan.ManaTarget}; {PullRestPotions} drunk before a pull in this fight)"));
                    return PullRestDrinkEnds(session, clock);
                }

                if (drunkMax > 0 && drunkMana >= drunkMax)
                {
                    // Live 2026-11-11 14:59 UTC: a potion drunk at the maximum restores nothing and changes nothing: the mana is full, which is the drink's end and no failure of the potions.
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"rest: potion: check after: the mana stands at its maximum {drunkMana} of {drunkMax} (target {_pullRestPlan.ManaTarget} was planned on a higher maximum): the drinking ends, the potions stay in use (m6-potion-before-rest)"));
                    return PullRestDrinkEnds(session, clock);
                }

                if (clock - _pullDrinkAt > VitalsKeeper.PotionVitalWaitSeconds)
                {
                    _potionsAside = true;
                    _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"rest: potion: check after: the mana did not change within {VitalsKeeper.PotionVitalWaitSeconds:0} s of a confirmed Mana Potion (still {drunkMana}); Mana Potions are set aside for the fight, the rest as before (m6-potion-before-rest)"));
                    return PullRestDrinkEnds(session, clock);
                }

                return ScenarioStatus.Running;

            default:
                return Fail(session, $"TODO(state): unknown rest stage {_pullRestStage}");
        }
    }

    /// <summary>M6 14: the Heal Self casts one heal-up before a pull may take (Lifestone's own bound: Heal Self I gave 8 to 25 a cast live, and the rest's reach is 20 points of a hundred).</summary>
    public const int MaxPullHeals = 5;

    private bool _pullHealUp;
    private int _pullHealCasts;
    private bool _pullHealShortNoted;

    /// <summary>The Heal Self casts made before a pull in place of a rest (report-only count).</summary>
    public int PullHeals { get; private set; }

    /// <summary>
    /// M6 14 (the Cave's run of 2026-11-12: 37 of her 46 rests before a pull were for "health under 70%", about 39 s each lain down, with the mana at 246 of 290 and a bolt at 5: a fifth of the cycle's time): before a rest
    /// for health she heals herself, as a player with a full mana bar does. A heal-up begins with the health under the rule's floor (<see cref="GroundPullRest.HealthPercent"/>) and goes on to the rest's own reach
    /// (<see cref="GroundPullRest.UntilPercent"/>), one Heal Self a pick (the level the drain rule holds: the highest the book has, her skill casts and the pack pays), while the mana pays it over the pull's own
    /// mana (<see cref="GroundPullRest.ManaPercent"/>), at most <see cref="MaxPullHeals"/> casts. With no heal, the mana short of that, or the bound spent, the rest as before, said once. True when a cast began.
    /// </summary>
    private bool PullHealBegins(IGameSessionView session, double clock, GroundPullRest rule, uint mana, uint manaMax, uint health, uint healthMax)
    {
        if (rule.HealthPercent <= 0 || healthMax == 0 || !_drain.HasHeal)
        {
            _pullHealUp = false;
            return false;
        }

        if ((ulong)health * 100 < (ulong)rule.HealthPercent * healthMax)
        {
            if (!_pullHealUp)
            {
                (_pullHealUp, _pullHealCasts) = (true, 0);
            }
        }
        else if ((ulong)health * 100 >= (ulong)rule.UntilPercent * healthMax)
        {
            (_pullHealUp, _pullHealShortNoted) = (false, false);
        }

        if (!_pullHealUp)
        {
            return false;
        }

        var keep = (uint)Math.Ceiling(manaMax * Math.Max(rule.ManaPercent, 0) / 100.0);
        var unpaid = mana < keep + _drain.HealCeiling;
        if (unpaid || _pullHealCasts >= MaxPullHeals)
        {
            if (!_pullHealShortNoted)
            {
                _pullHealShortNoted = true;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"rest: no {_drain.HealName} before it: {(unpaid ? $"mana {mana} of {manaMax} does not pay it ({_drain.HealCeiling}) over the pull's {keep}" : $"{_pullHealCasts} casts left the health at {health} of {healthMax} (the bound {MaxPullHeals})")}: the rest as before (m6-pull-heal)"));
            }

            _pullHealUp = false;
            return false;
        }

        _pullHealCasts++;
        PullHeals++;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"rest: {_drain.HealName} in place of a rest: health {health} of {healthMax} is under {(_pullHealCasts == 1 ? rule.HealthPercent : rule.UntilPercent)}%, mana {mana} of {manaMax} pays it over the pull's {keep} (cast {_pullHealCasts} of at most {MaxPullHeals}; m6-pull-heal)"));
        _drain.StartHeal(session, clock);
        NoteDrainStart(session, clock);
        _phase = Phase.Drain;
        return true;
    }

    /// <summary>The ground's rest rule as it stands now: with a Mana Potion held and the potions in use, the potion line's (<see cref="GroundPullRest.WithPotionsInUse"/>); M6 14: for the climber under the policy, its lines (<see cref="PolicyPullRule"/>).</summary>
    private GroundPullRest? PullRule(IGameSessionView session) =>
        PolicyPullRule(session, _ground.PullRest is { Potions: true, PotionManaPercent: > 0 } rule ? rule.WithPotionsInUse(PotionsInUse(session)) : _ground.PullRest);

    /// <summary>
    /// M6 14: the stamina the rest before a pull begins under now (the rule as it stands, <see cref="PullRule"/>: the fullness bar's share, else the stamina's own), or 0 with no rule or no stamina line. The mana loop's press
    /// with ample mana held does not take her under it (<see cref="ManaLoopFacts.StaminaKeep"/>).
    /// </summary>
    private uint PullRestStaminaLine(IGameSessionView session, uint staminaMax)
    {
        if (PullRule(session) is not { } rule)
        {
            return 0;
        }

        var percent = rule.FullPercent > 0 ? rule.FullPercent : rule.StaminaPercent;
        return percent <= 0 ? 0 : (uint)Math.Ceiling(staminaMax * percent / 100.0);
    }

    /// <summary>The mana at which the potions stop now: the potion line's share of the maximum read now, or no line (the rest's own target stands).</summary>
    private uint PullPotionLine(IGameSessionView session, uint manaMax) =>
        PullRule(session) is { ToFloors: true } rule ? (uint)Math.Ceiling(manaMax * (double)rule.ManaPercent / 100) : uint.MaxValue;

    /// <summary>
    /// M6 8 (the owner's rules of 2026-11-11: she still uses Stamina to Mana, and stamina does not sit full, where it regenerates nothing): the check before a pull on a potion ground, before any potion: the stamina at
    /// <see cref="PullRestRule.PressStaminaShare"/> and the mana with room for the whole press: the fight's own press (the dance, with its busy and fizzle rules). False: no press now.
    /// </summary>
    private bool PullPressBegins(IGameSessionView session, double clock, uint mana, uint manaMax, uint stamina, uint staminaMax)
    {
        if (!_options.Mana.On || staminaMax == 0 || manaMax <= mana || stamina < PullRestRule.PressStaminaShare * staminaMax || clock < _danceAsideUntil)
        {
            return false;
        }

        var level = ManaOrder.BestNetLevel(_danceLevels.AsSpan(0, _danceCount), stamina, mana, _manaConversion, _options.Mana.TryChance, out var net);
        if (level < 0 || net <= 0 || manaMax - mana < net)
        {
            return false;
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"rest: press: check before: stamina {stamina} of {staminaMax} at {PullRestRule.PressStaminaShare:P0} or over, mana {mana} of {manaMax} has room for the net {net:0.#}: Stamina to Mana before the pull, no potion and no rest (m6-press-before-potion)"));
        _preempting = true;
        _phase = Phase.Mana;
        StartDance(session, level, mana, manaMax, stamina, staminaMax, clock);
        return true;
    }

    /// <summary>M6 8: the Mana Potions drunk in place of a rest before a pull (they count in <see cref="PotionsDrunk"/> too).</summary>
    public int PullRestPotions { get; private set; }

    /// <summary>
    /// M6 8 (the owner's decision of 2026-11-11): the check before a rest for mana on a ground whose rule says <see cref="GroundPullRest.Potions"/>: a Mana Potion is held and not set aside, so it
    /// is drunk (25 mana in 1.5 to 2.5 s against 0.2 to 0.3 a second of regeneration). No rest is counted and no posture changes. False: none held, or set aside: the rest as before.
    /// </summary>
    private bool PullRestDrinkBegins(IGameSessionView session, double clock, uint mana, uint manaMax, in PullRestPlan plan)
    {
        if (_potionsAside || !VitalsMeans.FindManaPotions(session.State, _scratch, out var potion, out var held))
        {
            return false;
        }

        _pullRestPlan = plan;
        _pullRestHits = _tally.HitsTaken + _tally.Evades;
        _pullDrinkMana = mana;
        _pullDrinkAt = clock;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"rest: potion: check before: {plan.Why}; mana {mana} of {manaMax}, target {plan.ManaTarget}, {held} Mana Potion(s) held: Mana Potion 0x{potion:X8} is drunk, no rest (m6-potion-before-rest)"));
        _phase = Phase.PullRest;
        _pullRestStage = PullRestStage.Drinking;
        _pullRestPending = session.Actions.Drink(potion, ActionSource.Automation);
        return true;
    }

    /// <summary>M6 8: one potion is down: the next one while the mana is under the rest's target, else back to the choice, which asks the rule again.</summary>
    private ScenarioStatus PullRestDrinkEnds(IGameSessionView session, double clock)
    {
        // As a rest, a potion step goes on to the rest's target, not just over the floor: the next potion at once, unless an attack came (the choice answers it) or none is left.
        if (_tally.HitsTaken + _tally.Evades == _pullRestHits
            && session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax)
            && mana < Math.Min(_pullRestPlan.ManaTargetNow(manaMax), PullPotionLine(session, manaMax)) // the maximum read now: a buff that lapsed lowered it under the plan's target, and the potion line with it
            && ManaDrinks.Fits(session.State, _scratch) // the owner, 2026-11-11: a drink that would overflow is not drunk; M6 14 T: the pick's own boost (a tincture's 50, a potion's 25), never an assumed 25
            && PullRestDrinkBegins(session, clock, mana, manaMax, in _pullRestPlan))
        {
            return ScenarioStatus.Running;
        }

        _pullRestStage = PullRestStage.None;
        _phase = Phase.Choose;
        return ScenarioStatus.Running;
    }

    /// <summary>What ends the rest now: a hit, a health fall, or a creature in sight (read each <see cref="PullRestLookSeconds"/>); null while nothing does.</summary>
    private string? PullRestCutBy(IGameSessionView session, double clock, GroundPullRest rule)
    {
        if (_tally.HitsTaken + _tally.Evades != _pullRestHits)
        {
            return "an attack came";
        }

        if (session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out _) && health < _pullRestHealthFrom)
        {
            return string.Create(CultureInfo.InvariantCulture, $"the health fell from {_pullRestHealthFrom} to {health}");
        }

        if (_pullRestStanding || clock < _pullRestLookAt || session.State.Self.Position is not { } here)
        {
            return null; // M6 7 AN: a standing rest under the bar began with the creature in sight: only an attack ends it
        }

        _pullRestLookAt = clock + PullRestLookSeconds;
        return PullRestHostile(session, in here, rule, out var what, out _) ? what : null;
    }

    /// <summary>A hit or a creature cut the rest: she stands (M5 D88) and the fight goes on; the rest stays aside for <see cref="PullRestCutAsideSeconds"/> or until the next kill.</summary>
    private ScenarioStatus PullRestCutNow(IGameSessionView session, double clock, PlayerQualities q, string why)
    {
        PullRestsCut++;
        _pullRestCut = why;
        _pullRestAsideUntil = clock + PullRestCutAsideSeconds;
        _pullRestCutKills = _tally.Kills;
        _pullRestPressIndex = -1;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"rest: cut after {clock - _pullRestStart:0.#} s: {why}; she stands and fights back (M5 D88)"));
        return PullRestStand(session, q);
    }

    /// <summary>The bound is up: she rises, the rest stays aside <see cref="RestAsideSeconds"/>.</summary>
    private ScenarioStatus PullRestBoundUp(IGameSessionView session, PlayerQualities q, double clock, uint mana, uint health)
    {
        _pullRestCut = string.Create(CultureInfo.InvariantCulture, $"the bound of {_pullRestPlan.BoundSeconds:0} s");
        _pullRestAsideUntil = clock + RestAsideSeconds;
        _pullRestCutKills = -1;
        _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"rest: the bound of {_pullRestPlan.BoundSeconds:0} s is up at mana {mana} of {_pullRestPlan.ManaTarget}, health {health} of {_pullRestPlan.HealthTarget}; she rises, no rest for {RestAsideSeconds:0} s"));
        return PullRestStand(session, q);
    }

    /// <summary>M6 7 AD: the level (an index of the dance levels) a press would use now (<see cref="PullRestRule.PressLevel"/>), or -1: none known, set aside, or not worth it at these vitals.</summary>
    private int PullRestPressDue(IGameSessionView session, double clock)
    {
        var q = session.State.Self.Qualities;
        if (_danceCount == 0 || clock < _danceAsideUntil || !q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax) || !q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax))
        {
            return -1;
        }

        var input = new PullRestPressInput(mana, manaMax, _pullRestPlan.ManaTarget, stamina, staminaMax, _manaConversion, _options.Mana.TryChance);
        return PullRestRule.PressLevel(_danceLevels.AsSpan(0, _danceCount), in input, out _);
    }

    /// <summary>M6 7 AD: in the magic mode, the check before the press: nothing cut the rest, the press still pays at the vitals now (they moved in the seconds of standing up). Then the press; if it no longer pays she sits again, named.</summary>
    private ScenarioStatus PullRestPress(IGameSessionView session, PlayerQualities q, double clock)
    {
        _pullRestPressIndex = -1;
        if (PullRestCutBy(session, clock, _ground.PullRest!) is { } cut)
        {
            return PullRestCutNow(session, clock, q, cut);
        }

        var index = PullRestPressDue(session, clock);
        q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax);
        q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax);
        if (index < 0)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"rest: no press after all: mana {mana} of {manaMax}, stamina {stamina} of {staminaMax} net nothing now"));
            return PullRestAgain(session, q, clock);
        }

        (_pullRestPressMana, _pullRestPressStamina, _pullRestPressAt, _pullRestLandedMark) = (mana, stamina, clock, _dancesLanded);
        _preempting = false;
        _pullRestStage = PullRestStage.Pressing;
        _pullRestPresses++;
        PullRestPressesSent++;
        StartDance(session, index, mana, manaMax, stamina, staminaMax, clock);
        return ScenarioStatus.Running;
    }

    /// <summary>M6 7 AD: the press ended (<see cref="DanceStep"/> counted it, and set a dance that did not land aside): the check after it, the mana read back, one line; then the rest's next step.</summary>
    private ScenarioStatus PullRestPressed(IGameSessionView session, PlayerQualities q, double clock)
    {
        q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax);
        q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax);
        var net = (int)mana - (int)_pullRestPressMana;
        var report = _manaCast!.Report;
        _pullRestNet += net;
        PullRestPressNet += net;
        if (!double.IsNaN(_cycleAt))
        {
            // M6 7 AN: the real time of a cycle, the mode changes in it: the owner's estimate is about 0.8 mana a second overall
            var cycle = clock - _cycleAt;
            var gained = (int)mana - (int)_cycleMana;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"rest: cycle {_pullRestPresses}: {cycle:0.#} s in all, {_cycleDownSeconds:0.#} s down, {clock - _pullRestPressAt:0.#} s pressing, {Math.Max(0, cycle - _cycleDownSeconds - (clock - _pullRestPressAt)):0.#} s changing modes; mana {_cycleMana} -> {mana}, {(cycle > 0 ? gained / cycle : 0):0.##} a second overall"));
            (_cycleAt, _cycleMana, _cycleDownSeconds) = (clock, mana, 0);
        }

        var line = string.Create(CultureInfo.InvariantCulture, $"rest: press {_pullRestPresses}: mana {_pullRestPressMana} -> {mana} of {manaMax}, stamina {_pullRestPressStamina} -> {stamina} of {staminaMax}, net {net}, {clock - _pullRestPressAt:0.#} s, {report.Tries} tries, {report.Fizzles} fizzled");
        if (_dancesLanded > _pullRestLandedMark)
        {
            _log.Info(LogCategory, line);
        }
        else
        {
            _log.Warn(LogCategory, line + ": it did not land, and the rest goes on by the regeneration while the dance stands aside");
        }

        return PullRestAgain(session, q, clock);
    }

    /// <summary>M6 7 AD: in the magic mode, after a press or its refusal: a cut stands her, the vitals at the top or the bound end the rest, else peace and the posture again.</summary>
    private ScenarioStatus PullRestAgain(IGameSessionView session, PlayerQualities q, double clock)
    {
        if (PullRestCutBy(session, clock, _ground.PullRest!) is { } cut)
        {
            return PullRestCutNow(session, clock, q, cut);
        }

        q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMaxNow);
        q.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out _);
        q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out _);
        if (mana >= _pullRestPlan.ManaTargetNow(manaMaxNow) && health >= _pullRestPlan.HealthTarget && stamina >= _pullRestPlan.StaminaTarget)
        {
            return PullRestToMagic(session, q);
        }

        if (clock - _pullRestStart >= _pullRestPlan.BoundSeconds)
        {
            return PullRestBoundUp(session, q, clock, mana, health);
        }

        _pullRestPending = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Automation);
        _pullRestStage = PullRestStage.ToPeace;
        return ScenarioStatus.Running;
    }

    /// <summary>The stop key when a posture is held, then the magic mode.</summary>
    private ScenarioStatus PullRestStand(IGameSessionView session, PlayerQualities q)
    {
        PullRestStretchEnds(session, session.Clock.Now);
        if (!_pullRestPostureDown || session.Actions.KillSwitch)
        {
            return PullRestToMagic(session, q);
        }

        _log.Info(LogCategory, "rest: standing up (the stop key, Ready 0x41000003)");
        _pullRestPending = session.Actions.Move.Command(MotionCommands.Ready, true, ActionSource.Automation);
        _pullRestStage = PullRestStage.Standing;
        return ScenarioStatus.Running;
    }

    /// <summary>M6 7 AN: a stretch at rest begins: its start is kept for the rates.</summary>
    private void PullRestStretchBegins(PlayerQualities q, double clock)
    {
        q.TryGetVitalLevels(MageTemplate.MaxMana, out _stretchMana, out _);
        q.TryGetVitalLevels(MageTemplate.MaxStamina, out _stretchStamina, out _);
        _stretchAt = clock;
    }

    /// <summary>M6 7 AN: a stretch at rest ends: one line with the posture asked, what the server's motion shows, the seconds and the rates reached, set against ACE's multiplier.</summary>
    private void PullRestStretchEnds(IGameSessionView session, double clock)
    {
        if (double.IsNaN(_stretchAt))
        {
            return;
        }

        var seconds = clock - _stretchAt;
        _stretchAt = double.NaN;
        _cycleDownSeconds += seconds;
        var q = session.State.Self.Qualities;
        q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _);
        q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out _);
        var asked = _pullRestStanding || !_pullRestPostureDown ? RestPosture.Stand : _ground.PullRest!.Posture;
        var echo = session.State.Objects.TryGet(session.State.Self.Guid, out var self) && self.Motion is { } motion ? (motion.Forward is { } forward ? "0x" + forward.ToString("X4", CultureInfo.InvariantCulture) : "default") : "unread";
        if (asked == RestPosture.Lie && echo != "0x" + DeathScenario.LieDownWire.ToString("X4", CultureInfo.InvariantCulture))
        {
            _log.Warn(LogCategory, $"rest: check: she was asked to lie down, but the server's motion shows {echo}, not Sleeping (TODO(m6-rest-lie-echo)); the rate below tells which multiplier she really had");
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"rest: stretch: posture {asked.ToString().ToLowerInvariant()} (the server's motion {echo}), {seconds:0.#} s, stamina {_stretchStamina} -> {stamina} ({(seconds > 0 ? ((double)stamina - _stretchStamina) / seconds : 0):0.##} a second, ACE {PullRestRule.StaminaPerSecondAtOne * PullRestRule.HealthStance(asked):0.##} before Rejuvenation), mana {_stretchMana} -> {mana} ({(seconds > 0 ? ((double)mana - _stretchMana) / seconds : 0):0.##} a second)"));
    }

    /// <summary>
    /// M6 7 AN (the owner): under the ground's fullness bar she does not pull. True with nothing to fight back and the bar not met: the choice stops here, named once, until a rest may begin again; a hurt target of hers in range is the fight in hand, not a pull.
    /// </summary>
    private bool PullBarHolds(IGameSessionView session, in CellFrame here)
    {
        if (PullRule(session) is not { FullPercent: > 0 } rule)
        {
            return false; // no bar, or Mana Potions in use (M6 8: with potions she pulls on the potion line, not on the bar)
        }

        var q = session.State.Self.Qualities;
        q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax);
        q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax);
        if (!PullRestRule.UnderFullBar(rule, mana, manaMax, stamina, staminaMax))
        {
            if (_pullBarHoldNoted)
            {
                _pullBarHoldNoted = false;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"rest: check: the bar {rule.FullPercent}% is met: mana {mana} of {manaMax}, stamina {stamina} of {staminaMax}: the pull may go"));
            }

            return false;
        }

        if (PullRestHostile(session, in here, rule, out _, out var hurtTarget) && hurtTarget)
        {
            return false;
        }

        if (!_pullBarHoldNoted)
        {
            _pullBarHoldNoted = true;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"rest: check: no pull: mana {mana} of {manaMax}, stamina {stamina} of {staminaMax} under the bar {rule.FullPercent}%; she holds until a rest may begin"));
        }

        return true;
    }

    /// <summary>The magic mode again (a mode already there is read and not sent), and the rest's line once it is back; or, a press being due, the press.</summary>
    private ScenarioStatus PullRestToMagic(IGameSessionView session, PlayerQualities q)
    {
        if (q.CombatMode == CombatModes.Magic)
        {
            if (_pullRestPressIndex >= 0)
            {
                return PullRestPress(session, q, session.Clock.Now);
            }

            PullRestClose(session.Clock.Now, session);
            _phase = Phase.Choose;
            return ScenarioStatus.Running;
        }

        _pullRestPending = session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
        _pullRestStage = PullRestStage.ToMagic;
        return ScenarioStatus.Running;
    }

    /// <summary>Ends the rest: its seconds join the total, a long enough gain measures the mana rate, and the line says the before, the after, the seconds and why it ended.</summary>
    private void PullRestClose(double clock, IGameSessionView session)
    {
        if (double.IsNaN(_pullRestStart))
        {
            return;
        }

        var seconds = Math.Max(0, clock - _pullRestStart);
        _pullRestDoneSeconds += seconds;
        _pullRestStart = double.NaN;
        _pullRestStage = PullRestStage.None;
        var q = session.State.Self.Qualities;
        q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax);
        q.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var healthMax);
        q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax);
        if (_pullRestPresses == 0 && mana >= _pullRestManaFrom + PullRestMeasureMana && seconds > 0) // M6 7 AD: the regeneration's own rate: the bound must hold when the dance stands aside
        {
            _pullRestManaRate = (mana - _pullRestManaFrom) / seconds;
        }

        _log.Info(
            LogCategory,
            string.Create(
                CultureInfo.InvariantCulture,
                $"rest: mana {_pullRestManaFrom}/{manaMax} -> {mana}/{manaMax}, health {_pullRestHealthFrom}/{healthMax} -> {health}/{healthMax}, stamina now {stamina}/{staminaMax} in {seconds:0} s, {_pullRestPlan.Why}; ended: {_pullRestCut ?? "the vitals are back"}; dance presses={_pullRestPresses} net={_pullRestNet}; {_pullRestManaRate:0.##} mana a second regenerated; rest_s total {_pullRestDoneSeconds:0}"));
    }

    /// <summary>The fight ends while a rest runs (a stop, the minutes, a failure): the rest's line is written, and a held posture stands up (the next station's mode is its own).</summary>
    private void PullRestAbandon(IGameSessionView session, double clock, string why)
    {
        if (_pullRestStage is PullRestStage.Drinking or PullRestStage.DrinkVital or PullRestStage.DrinkBusy)
        {
            (_pullRestStage, _pullRestPending) = (PullRestStage.None, null); // M6 8: a potion in flight is no rest: nothing to stand up from
        }

        if (double.IsNaN(_pullRestStart))
        {
            return;
        }

        _pullRestCut = why;
        if (_pullRestPostureDown && !session.Actions.KillSwitch)
        {
            _log.Info(LogCategory, "rest: standing up (the stop key) as the fight ends");
            session.Actions.Move.Command(MotionCommands.Ready, true, ActionSource.Automation);
        }

        if (_pullRestStage == PullRestStage.Pressing && _manaCast is { IsRunning: true })
        {
            _manaCast.Stop(why, clock); // M6 7 AD: the press in flight stops with the fight
        }

        PullRestStretchEnds(session, clock);
        _pullRestPostureDown = false;
        _pullRestPending = null;
        _pullRestPressIndex = -1;
        PullRestClose(clock, session);
    }

    /// <summary>
    /// True when a rest may not start or go on: the fight's own target is hurt, alive and in range (it comes for her), or a living target creature of the ground stands within <see cref="GroundPullRest.AggroMetres"/> with a clear line (a wall hides her:
    /// ACE's awareness looks for the player with a line, <c>Monster_Awareness.cs</c>).
    /// </summary>
    private bool PullRestHostileOwn(IGameSessionView session, in CellFrame here, GroundPullRest rule, out string what, out bool hurtTarget)
    {
        what = string.Empty;
        hurtTarget = false;
        var objects = session.State.Objects;
        var origin = here.ToGlobal();
        if (_target != 0 && !_killed.Contains(_target) && !TargetGone(objects, _target) && objects.TryGet(_target, out var engaged) && engaged.Health is < 1f && StandsAt(session, engaged) is { } at && at.ToGlobal().DistanceTo(origin) <= _range)
        {
            what = string.Create(CultureInfo.InvariantCulture, $"the target {engaged.Name ?? "creature"} 0x{engaged.Guid:X8} is hurt and alive, {at.ToGlobal().DistanceTo(origin):0.#} m away");
            hurtTarget = true;
            return true;
        }

        foreach (var o in objects.All)
        {
            if (StandsAt(session, o) is not { } p || (o.ItemType & WarCreatureItemType) == 0 || o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord } || _killed.Contains(o.Guid) || !IsFightCreature(o.Weenie.WeenieClassId))
            {
                continue;
            }

            var d = p.ToGlobal().DistanceTo(origin);
            if (d <= rule.AggroMetres && LineIsClear(session, in here, o.Guid))
            {
                what = string.Create(CultureInfo.InvariantCulture, $"{o.Name ?? "creature"} 0x{o.Guid:X8} is in sight {d:0.#} m away");
                return true;
            }
        }

        return false;
    }
}
