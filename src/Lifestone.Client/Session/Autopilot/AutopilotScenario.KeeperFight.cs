using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 7 BD, the one rule (live, 2026-11-07 03:34:49 to 03:37:53 UTC: a logistics trip asked in the Lodge with tuskers on her; the Trip state's keeper's pass rested and danced through 56 hits with no bolt, to her death; the third of this family after AZ's
/// pass in a fight and BB's keeper before a fight). In any state's keeper phase (<see cref="RunPhase.Vitals"/>, <see cref="RunPhase.Peace"/>) and any station's wait (<see cref="IStationWait"/>), on a ground, when she is attacked or a hostile is in reach
/// (<see cref="HostileWatch.InReach"/>), the keeper stops at once (<see cref="VitalsKeeper.Halt"/>), she fights back (M5 D88) until no hostile is in reach (<see cref="KeeperFightOver"/>), and the state resumes from its start (<see cref="KeeperResume()"/>).
/// It stands down for what must not fight: the retreat's way home, the urgent recall, the recall before a stop, a death not yet handled, a scripted death (<see cref="GuardStandsDown"/>), the heal in place under the retreat line (the retreat rule decides),
/// and a one-shot task, whose own fight-back is bounded (M6 6 FXM1: <see cref="TaskFightBackOver"/>; the task's stations fight back with their own legs). Every step is one line, <c>autopilot keeper:</c>.
/// </summary>
public sealed partial class AutopilotScenario
{
    /// <summary>The fight stop reason of the keeper's fight-back: no hostile is in reach any more, the state it stood in for resumes.</summary>
    public static FightStopReason KeeperFightDone { get; } = new("keeper-fight-back");

    /// <summary>The most seconds a halted keeper's action in flight is waited for before the fight starts anyway (named in the log): a cast's own bound is under it.</summary>
    public const double KeeperHaltSeconds = 15;

    /// <summary>
    /// The seconds with no attack heard after which a creature still in reach no longer holds the state (<see cref="KeeperFightOver"/>): the read model holds no creature's target, so a sleeper behind a wall reads like an attacker
    /// (<c>TODO(keeper-fight-awake-read)</c>). Shares the task fight-back's cap (<see cref="TaskFightBackMaxSeconds"/>). An attacker on her has no cap: the fight's own emergency, the retreat rule and the death end that.
    /// </summary>
    public const double KeeperFightQuietCapSeconds = TaskFightBackMaxSeconds;

    /// <summary>What the fight-back stands in for: the state and spec that resume, whether it was the heal in place, and when it began.</summary>
    private sealed record KeeperBack(AutopilotState State, StationSpec Spec, bool HealOnly, string? Detail, double At);

    private KeeperBack? _keeperBack; // the fight running stands in for this state
    private double _keeperHaltAt = double.NaN; // a halted keeper's action in flight is ending
    private string _keeperHaltHostile = string.Empty;
    private string _keeperHaltWait = string.Empty;
    private bool _keeperReachSpent; // the last fight-back ended at its quiet cap: only a new attack stops the keeper again
    private bool _tripLullLogged; // the trip's wait for the lull, logged once a wait
    private bool _tripUrgent; // the fight stops for a stop line with a hostile on her: the recall, then the trip
    private bool _urgentAsked; // the next BeginRun is the urgent recall
    private bool _urgentRecall; // the urgent recall's run: no keeper's pass, and the rule stands down

    /// <summary>Clears what a fight-back and a halt hold: every run's start and the known state (a run that begins for any other reason owes nothing to the state before it).</summary>
    private void KeeperForget()
    {
        (_keeperBack, _keeperHaltAt, _tripUrgent) = (null, double.NaN, false);
        SurvivalForget();
    }

    /// <summary>The rule's condition (see the class): true when a hostile is on her, on a ground, and nothing that must not fight is running.</summary>
    private bool KeeperGivesWay(IGameSessionView session, out string hostile)
    {
        hostile = string.Empty;
        if (_hostiles is not { } watch || !watch.InReach(_clock, out hostile))
        {
            _keeperReachSpent = false;
            return false;
        }

        if (_retreating || _urgentRecall || _finalStop is not null || _deathSeen || (_state == AutopilotState.Fight && _fightBack) || GuardStandsDown(_state, _unscripted, SawDeath(_deathTally))
            || _options.Task.Kind != AutopilotTaskKind.Job || (_healOnly && UnderRetreatLine(session))
            || session.State.Self.Travel != SelfTravel.Present || Classify(session.State, _ground) != AutopilotPlace.Ground)
        {
            return false;
        }

        if (_clock - watch.LastHitAt < HostileWatch.AttackQuietSeconds)
        {
            _keeperReachSpent = false;
            return true;
        }

        return !_keeperReachSpent;
    }

    /// <summary>True when the health is under the ground's retreat line: in the heal in place the retreat rule decides there (M6 6 F2: home, or the heal that outheals what comes in).</summary>
    private bool UnderRetreatLine(IGameSessionView session) =>
        session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var max) && max > 0 && health * 100.0 / max < RetreatLineNow(session, out _);

    /// <summary>The rule in the keeper's phases: a frame of <see cref="RunPhase.Vitals"/> or <see cref="RunPhase.Peace"/>. Null: nothing stands against the phase, it goes on.</summary>
    private ScenarioStatus? KeeperStands(IGameSessionView session, double now)
    {
        if (!double.IsNaN(_keeperHaltAt))
        {
            return KeeperHaltStep(session, now);
        }

        if (_pending is { IsCompleted: false } || !KeeperGivesWay(session, out var hostile))
        {
            return null;
        }

        var wait = _phase == RunPhase.Vitals ? "the keeper's pass" : "the peace before the station";
        _log.Warn(LogCategory, $"autopilot keeper: check before: {wait} of the {StateName(_state)} state ({_spec?.Name ?? "none"}): a hostile is on her ({hostile}): the keeper stops at once and she fights back (M5 D88, M6 7 BD)");
        if (_phase == RunPhase.Vitals && _context.Keeper is { } keeper && keeper.Halt(now, $"a hostile is on her ({hostile})") && keeper.Halting)
        {
            (_keeperHaltAt, _keeperHaltHostile, _keeperHaltWait) = (_clock, hostile, wait);
            return ScenarioStatus.Running;
        }

        return KeeperFight(session, hostile, wait);
    }

    /// <summary>The halted keeper's action in flight ends (the check after the halt), then the fight, or the state goes on when the hostile went away meanwhile.</summary>
    private ScenarioStatus KeeperHaltStep(IGameSessionView session, double now)
    {
        var keeper = _context.Keeper!;
        keeper.Step(now);
        if (_deathSeen)
        {
            _keeperHaltAt = double.NaN;
            return UnscriptedDeath();
        }

        if (keeper.Halting)
        {
            if (_clock - _keeperHaltAt < KeeperHaltSeconds)
            {
                return ScenarioStatus.Running;
            }

            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot keeper: check after: the halted keeper's action did not end within {KeeperHaltSeconds:0} s: the fight starts anyway"));
        }

        _keeperHaltAt = double.NaN;
        return KeeperFight(session, _keeperHaltHostile, _keeperHaltWait);
    }

    /// <summary>
    /// M6 7 finish, closes <c>keeper-fight-fix-phase</c>: the recovery's mana dance is a keeper's pass too, so the rule holds there: with a hostile on her the dance stops at once, the fix and its retry are dropped by name, she fights back, and the state resumes from its start. Only the dance: the other fixes
    /// are single actions that end in seconds, and a fight-back before the caster is wielded would have nothing to fight with. Null: nothing stands against the fix.
    /// </summary>
    private ScenarioStatus? ManaDanceGivesWay(IGameSessionView session)
    {
        if (!double.IsNaN(_keeperHaltAt))
        {
            return KeeperHaltStep(session, _clock);
        }

        if (_retry is not { Fix: RecoveryFix.ManaDance } retry || _spec is null || _deathSeen || _pending is { IsCompleted: false } || !KeeperGivesWay(session, out var hostile))
        {
            return null;
        }

        const string Wait = "the recovery's mana dance";
        _log.Warn(LogCategory, $"autopilot keeper: check before: {Wait} (reason={retry.Reason}) of the {StateName(_state)} state: a hostile is on her ({hostile}): the keeper stops at once, the fix and its retry are dropped, and she fights back (M5 D88, M6 7 BD)");
        (_fix, _retry) = (null, null);
        if (_context.Keeper is { } keeper && keeper.Halt(_clock, $"a hostile is on her ({hostile})") && keeper.Halting)
        {
            (_keeperHaltAt, _keeperHaltHostile, _keeperHaltWait) = (_clock, hostile, Wait);
            return ScenarioStatus.Running;
        }

        return KeeperFight(session, hostile, Wait);
    }

    /// <summary>The rule in a station's wait (<see cref="IStationWait"/>): the station ends by name and runs again from its start after the fight. Null: it does not wait, or nothing stands against it.</summary>
    private ScenarioStatus? StationWaitStands(IGameSessionView session)
    {
        if (((_run as IStationInner)?.Scenario as IStationWait ?? _run as IStationWait)?.Waiting is not { } wait || !KeeperGivesWay(session, out var hostile))
        {
            return null;
        }

        var name = _spec!.Name;
        _log.Warn(LogCategory, $"autopilot keeper: check before: {wait} in the {name} station of the {StateName(_state)} state: a hostile is on her ({hostile}): the station ends and she fights back (M5 D88, M6 7 BD)");
        DisposeRun();
        _phase = RunPhase.Done;
        _context.Observer.StationEnded(name, false, _clock);
        return KeeperFight(session, hostile, wait);
    }

    /// <summary>The fight from where she stands, in place of the state (a keeper's pass of the fight itself just gives way to it); when no hostile is on her any more, the state goes on.</summary>
    private ScenarioStatus KeeperFight(IGameSessionView session, string hostile, string wait)
    {
        var back = new KeeperBack(_state, _spec!, _healOnly, _upkeepDetail, _clock);
        if (!KeeperGivesWay(session, out _))
        {
            _log.Info(LogCategory, $"autopilot keeper: check after: no hostile is on her any more ({hostile} was): the {StateName(_state)} state goes on");
            return KeeperResume(back);
        }

        NoteWhy(KeeperFightDone.Name);
        if (_state == AutopilotState.Fight)
        {
            return BeginRun(AutopilotState.Fight, FightSpec, $"a hostile is on her ({hostile}) in {wait}: the fight starts at once (D88)");
        }

        var state = StateName(_state);
        var started = BeginRun(AutopilotState.Fight, FightSpec, $"a hostile is on her ({hostile}) in {wait} of the {state} state: she fights back (D88), then the state resumes (M6 7 BD)");
        if (_state == AutopilotState.Fight)
        {
            _keeperBack = back with { At = _clock };
        }

        return started;
    }

    /// <summary>
    /// True when the fight that stands in for a state is over: no hostile is in reach; or no attack was heard for <see cref="KeeperFightQuietCapSeconds"/> and what stands in reach never comes (it no longer holds the state: only a new attack does).
    /// While a fight-back runs no other trigger of the fight is read: the state it stands in for is what comes next.
    /// </summary>
    private bool KeeperFightOver()
    {
        if (_keeperBack is not { } back || _hostiles is not { } watch)
        {
            return false;
        }

        var ran = _clock - back.At;
        if (!watch.InReach(_clock, out var what))
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot keeper: the fight-back is over after {ran:0.#} s: no attack for {HostileWatch.AttackQuietSeconds:0} s and no hostile within {watch.ReachMetres:0.#} m: the {StateName(back.State)} state resumes"));
            return true;
        }

        var quiet = _clock - Math.Max(watch.LastHitAt, back.At);
        if (quiet < KeeperFightQuietCapSeconds)
        {
            return false;
        }

        _keeperReachSpent = true;
        _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot keeper: TODO(keeper-fight-awake-read): the fight-back heard no attack for {quiet:0} s and {what} still stands in reach (asleep, or out of the fight's line; the read model holds no creature's target): the {StateName(back.State)} state resumes, and only a new attack stops it"));
        return true;
    }

    /// <summary>The state the fight stood in for runs again from its start (<see cref="KeeperBack"/>).</summary>
    private ScenarioStatus KeeperResume() => _keeperBack is { } back ? KeeperResume(back) : EnterKnown("the keeper's fight-back is over and no state waits");

    private ScenarioStatus KeeperResume(KeeperBack back)
    {
        if (back.HealOnly)
        {
            _upkeepDetail = back.Detail;
            return BeginHeal("the keeper's fight-back is over: the heal in place resumes");
        }

        return BeginRun(back.State, back.Spec, $"the keeper's fight-back is over: the {StateName(back.State)} state resumes ({back.Spec.Name})", resume: true);
    }

    // ------------------------------------------------------------------------------------------------ the trip waits for the lull

    /// <summary>The seconds a stop request waits for a lull at most: a pack that never lets up does not hold the stop for ever.</summary>
    public const double StopLullSeconds = 120;

    private double _stopLullSince = -1;
    private bool _stopLullOver;

    /// <summary>
    /// M6 8 (live 2026-11-11 15:22 UTC, the climb's second death): a stop request (the stop file, the minutes, the level) reached a fight with tuskers on her. The fight ended at once, the way home's needs healed the mana away and
    /// rested her standing among them, and she died 55 s later with the recall never cast. As a trip (<see cref="TripWaitsForLull"/>), a stop does not leave a fight with attackers on her: she fights on until no hostile is in reach,
    /// at most <see cref="StopLullSeconds"/> (logged when it begins and when the bound ends it). The retreats, for health and for mana, are read after it as ever.
    /// </summary>
    private bool StopWaitsForLull(string stop)
    {
        if (_stopLullOver || _hostiles is not { } watch || !watch.InReach(_clock, out var hostile))
        {
            if (_stopLullSince >= 0 && !_stopLullOver)
            {
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot fight: the stop ({stop}) waited {_clock - _stopLullSince:0} s for the lull: no hostile is in reach now, the fight is asked to stop"));
            }

            _stopLullSince = -1;
            return false;
        }

        if (_stopLullSince < 0)
        {
            _stopLullSince = _clock;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot fight: the stop ({stop}) waits for the lull, at most {StopLullSeconds:0} s: {hostile} (M6 8: a stop does not leave a fight with attackers on her)"));
        }

        if (_clock - _stopLullSince < StopLullSeconds)
        {
            return true;
        }

        _stopLullOver = true;
        _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot fight: the stop ({stop}) waited {StopLullSeconds:0} s and a hostile is still in reach ({hostile}): the fight is asked to stop now"));
        return false;
    }

    /// <summary>
    /// M6 7 BD: true while a logistics trip asked in the fight waits for the lull: a hostile is in reach, and a trip never leaves a fight with attackers on her (the live death began with a trip asked with tuskers on her). The wait is logged once. The check repeats with the counters'.
    /// </summary>
    /// <param name="what">The trip, for the line.</param>
    private bool TripWaitsForLull(string what)
    {
        if (_hostiles is not { } watch || !watch.InReach(_clock, out var hostile))
        {
            _tripLullLogged = false;
            return false;
        }

        if (!_tripLullLogged)
        {
            _tripLullLogged = true;
            _log.Info(LogCategory, $"autopilot fight: the trip waits for the lull: {what}: {hostile} (M6 7 BD: a trip does not leave a fight with attackers on her)");
        }

        return true;
    }

    /// <summary>
    /// M6 7 BD: the stop line's trip (she is out of a needed component, so a lull may never serve her): with no hostile in reach, the trip as ever; with one, the urgent recall when it is payable, by name (<see cref="UrgentRecallPayable"/>), else she
    /// fights on and the trip waits for the lull, by name. True when the fight stops for the trip.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="what">The stop line, for the line.</param>
    private bool TripUrgent(IGameSessionView session, string what)
    {
        if (_hostiles is not { } watch || !watch.InReach(_clock, out var hostile))
        {
            _tripLullLogged = false;
            return true;
        }

        var (payable, why) = UrgentRecallRead(session);
        if (!payable)
        {
            if (!_tripLullLogged)
            {
                _tripLullLogged = true;
                _log.Warn(LogCategory, $"autopilot fight: urgent-trip: {what}: {hostile}: Lifestone Recall is not payable ({why}): she fights on and the trip waits for the lull");
            }

            return false;
        }

        _tripUrgent = true;
        _log.Warn(LogCategory, $"autopilot fight: urgent-trip: {what}: {hostile}: Lifestone Recall is payable ({why}): she leaves the fight by it, with no keeper's pass and no leave refresh, and the trip runs from home");
        return true;
    }

    /// <summary>The urgent recall begins: the trip stays owed (it runs from home, forced: the stop line is what it restocks); the state is Travel, no keeper's pass, and the rule stands down for this run.</summary>
    private ScenarioStatus BeginUrgentRecall()
    {
        (_tripPending, _tripForced, _tripCutWork, _tripStartedAt) = (true, true, false, _clock);
        _urgentAsked = true;
        return BeginRun(AutopilotState.Travel, RecallHomeSpec, "urgent-trip: a stop line with a hostile on her: Lifestone Recall, then the trip");
    }

    /// <summary>What the urgent recall reads from the session, as <c>TravelLeg.Retreat</c> judges it: the book, the tie, the pack's components, the mana for the cast and the station's need.</summary>
    private (bool Payable, string Why) UrgentRecallRead(IGameSessionView session)
    {
        if (!RecallNeeds(session, out var cost, out var shortOf))
        {
            return (false, "the spell table does not hold it");
        }

        var self = session.State.Self;
        var manaRead = self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _);
        return UrgentRecallPayable(self.Spellbook.Knows(Fighter.RecallHome.LifestoneRecallSpell), _context.Facts.LifestoneRecallUnavailable, _context.Facts.LifestoneTied, shortOf, manaRead, mana, cost);
    }

    /// <summary>What Lifestone Recall needs now, from the spell table and the live numbers: <paramref name="cost"/>, the cast's ceiling or the station's share of the mana, whichever is more; <paramref name="shortOf"/>, the component the pack lacks. False: the table does not hold it.</summary>
    private bool RecallNeeds(IGameSessionView session, out uint cost, out string? shortOf)
    {
        (cost, shortOf) = (0, null);
        if (!_context.Spells.TryGetValue(Fighter.RecallHome.LifestoneRecallSpell, out var info))
        {
            return false;
        }

        var self = session.State.Self;
        var account = _options.Buff?.AccountName;
        if (LevelFloor.HeldForAccount(session.State, info.School, account) is { } held && LevelFloor.ShortOf(held, info, out var need, account))
        {
            shortOf = need;
        }

        self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out _, out var max);
        cost = Math.Max(GearCasts.ManaCeiling(info, self.Inventory.Equipped.Count), (uint)Math.Ceiling(RecallHomeStation.MinManaPercent * max / 100));
        return true;
    }

    /// <summary>Whether Lifestone Recall can be paid now, and why, each condition by its name (the same checks as <c>TravelLeg.Retreat</c>, with the station's own mana need). An unread mana is no payment.</summary>
    /// <param name="known">The book holds it.</param>
    /// <param name="unavailable">It was refused for components this run (<see cref="JourneyFacts.LifestoneRecallUnavailable"/>).</param>
    /// <param name="tied">The lifestone tie: false is known untied, null unknown.</param>
    /// <param name="shortOf">The component the pack is short of, or null.</param>
    /// <param name="manaRead">The mana was read.</param>
    /// <param name="mana">The mana held.</param>
    /// <param name="cost">The mana the cast and the station need.</param>
    internal static (bool Payable, string Why) UrgentRecallPayable(bool known, bool unavailable, bool? tied, string? shortOf, bool manaRead, uint mana, uint cost)
    {
        var c = CultureInfo.InvariantCulture;
        return !known ? (false, "not in the book")
            : unavailable ? (false, "refused for components this run")
            : tied == false ? (false, "the lifestone tie is known false")
            : shortOf is not null ? (false, "the pack is short: " + shortOf)
            : !manaRead ? (false, "the mana is unread")
            : mana < cost ? (false, string.Create(c, $"mana {mana} under {cost}"))
            : (true, string.Create(c, $"mana {mana} of {cost} needed, the components held"));
    }
}
