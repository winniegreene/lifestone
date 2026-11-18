using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.World;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 8 K (the owner, 2026-11-09): the survival layer, above every activity but the fight. Each frame of a running station it decides first (<see cref="SurvivalFrame"/>): the station is stepped only when the layer says go, so nothing runs underneath it.
/// <list type="bullet">
/// <item><b>Wait.</b> The vitals guard's heal pick under its line (every run), then on the ladder a bar under its line with no hostile on her (<see cref="SurvivalRule"/>, <see cref="LadderSurvival"/>): the walk in flight lets go of the keys (<see cref="IJourneyTravel.HoldWalk"/>), the keeper's pass runs to the back lines, the mode she was in comes back, and the same walk goes on. The station is a coroutine that was not stepped, so it resumes where it stopped; its own waits do not count the hold (<see cref="IStationHeld"/>) and neither does the state's watchdog.</item>
/// <item><b>Leave.</b> In a Trip on the ladder (an errand too), attacked, health and mana under their lines and no heal payable: the station ends by name (<c>failed:quest:&lt;key&gt;:retreat:no-mana</c>, or the activity's own) and she goes home by the retreat's way. It does not count toward <c>retreat:again</c>: she was not on the rung's ground.</item>
/// <item><b>Leave, the losing race</b> (M6 14, live 2026-11-15 04:13:27 UTC: the guard drained a losing race on the Holding's approach until she died): in a Trip, or a Travel that is not her way home (the rung change's way out), when the
/// vitals guard found the race lost with Lifestone Recall paid (<see cref="AutopilotVitalsGuard.RaceLost"/>): the station ends by name (<c>failed:&lt;state&gt;:&lt;station&gt;:retreat:losing-race</c>) and she goes home by Lifestone Recall
/// (<see cref="RecallHomeSpec"/>), the guard holding off its heals while the recall is cast.</item>
/// <item><b>Stands down</b> for a scripted death, portal space, a portal's Use in flight (<see cref="IStationCommitted"/>), the way home of a retreat, the urgent recall and the recall before a stop.</item>
/// </list>
/// Every decision is one line, <c>survival: &lt;state&gt;: &lt;decision&gt;: &lt;reason and numbers&gt;</c>.
/// </summary>
public sealed partial class AutopilotScenario
{
    /// <summary>How long the layer starts no new wait after one ended with nothing done (the keeper blocked, or its pass found the needs met), in seconds (Lifestone's own: long enough not to loop, short against a walk).</summary>
    public const double SurvivalAsideSeconds = 30;

    /// <summary>How long the mode she was in is waited for after a keeper's pass, in seconds (Lifestone's own, the guard's and the peace's order).</summary>
    public const double SurvivalModeSeconds = 5;

    private readonly List<SurvivalAttacker> _survivalAttackers = [];
    private bool _survivalHeld; // a hold stands: the station is not stepped and its walk has let go of the keys
    private bool _survivalKeeper; // the keeper's pass of a wait runs
    private bool _survivalHalting; // that pass is being halted
    private bool _survivalModeBack; // the mode she was in is being restored
    private bool _survivalModeAsked;
    private bool _survivalRestLogged;
    private int _survivalMode;
    private double _survivalHeldAt;
    private double _survivalPassAt;
    private double _survivalHaltAt;
    private double _survivalModeAt;
    private double _survivalAsideUntil = double.NegativeInfinity;
    private string _survivalDown = string.Empty;
    private int _survivalHoldsCycle;
    private int _survivalHoldsRun;
    private int _survivalLeavesCycle;
    private int _survivalLeavesRun;

    /// <summary>M6 8 K: the holds the survival layer made this run.</summary>
    public int SurvivalHolds => _survivalHoldsRun;

    /// <summary>M6 8 K: the activities the survival layer ended this run.</summary>
    public int SurvivalLeaves => _survivalLeavesRun;

    /// <summary>The ladder's own job only: other runs keep the guard alone (their lines are not written).</summary>
    private bool SurvivalOn() => _options.Job == AutopilotJob.LevelUp && _options.Task.Kind == AutopilotTaskKind.Job; // no rung needed: the start audit's trip runs before the first rung is read

    private LadderSurvival SurvivalLines => (_ladder ??= LadderLoader.WarMage).Survival;

    /// <summary>One frame of the layer for a running station. Null: go, the station is stepped. Running: held. Any other status: the leave's.</summary>
    private ScenarioStatus? SurvivalFrame(IGameSessionView session, double now)
    {
        if (DenRunStandsDown())
        {
            return null; // M6 item 14 (the den run): the road is run only: nothing holds her (AutopilotScenario.DenRun.cs)
        }

        SurvivalInput input = default;
        if (_survivalKeeper)
        {
            return SurvivalPassStep(session, now);
        }

        if (_survivalModeBack)
        {
            return SurvivalModeStep(session);
        }

        // The leave comes first: the guard waits for the lanes to be free before it picks a heal, and someone else's action in flight must not keep her among attackers with nothing to pay for a heal. Never under the guard's own cast, which may yet land.
        var read = _guard is not { CastRunning: true } && SurvivalRead(session, out input);
        var decided = read ? SurvivalRule.Decide(in input, SurvivalLines) : default;
        if (decided.Verdict == SurvivalVerdict.Leave)
        {
            return SurvivalLeave(session, in decided);
        }

        if (GuardHolds(session))
        {
            GuardOverSubFight(session); // M6 16 E3.0: the guard over a quest's sub-fight, one line a sub-fight (report-only, AutopilotScenario.SubFightShadow.cs)
            SurvivalBegin(session, SurvivalReason.Heal, "the vitals guard's heal pick is under way");
            return SurvivalStillHeld();
        }

        if (GuardRetreat(session) is { } retreat)
        {
            return retreat;
        }

        if (decided.Verdict == SurvivalVerdict.Wait)
        {
            return SurvivalWait(session, now, in decided);
        }

        SurvivalEnd("nothing calls for a hold");
        return null;
    }

    /// <summary>The frame's reads; false when the layer has nothing to decide here (not the ladder's job, or the bars unread).</summary>
    private bool SurvivalRead(IGameSessionView session, out SurvivalInput input)
    {
        input = default;
        var q = session.State.Self.Qualities;
        if (!SurvivalOn() || !q.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var healthMax) || !q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax)
            || !q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax) || healthMax == 0 || manaMax == 0 || staminaMax == 0 || health == 0)
        {
            return false;
        }

        var down = SurvivalStandsDown(session);
        if (!string.Equals(_survivalDown, down ?? string.Empty, StringComparison.Ordinal))
        {
            _survivalDown = down ?? string.Empty;
            _log.Info(LogCategory, $"survival: {StateName(_state)}: {(down is null ? "back: the layer decides again" : "stand-down: " + down)}");
        }

        var watch = _hostiles;
        var near = false;
        if (watch is not null)
        {
            near = watch.InReach(_clock, out _);
        }

        var attacked = watch is not null && _clock - watch.LastHitAt < HostileWatch.AttackQuietSeconds;
        var lines = SurvivalLines;
        var manaPercent = mana * 100.0 / manaMax;
        var payable = true;
        if (attacked && manaPercent < lines.LeaveMana && _guard is { } guard && guard.AssessHeal(session, out _, out var canPay))
        {
            payable = canPay;
        }

        if (attacked)
        {
            ReadAttackers(session, watch!);
        }
        else
        {
            _survivalAttackers.Clear();
        }

        input = new SurvivalInput
        {
            Held = _survivalHeld,
            HealthPercent = health * 100.0 / healthMax,
            ManaPercent = manaPercent,
            StaminaPercent = stamina * 100.0 / staminaMax,
            HostileNear = near,
            Attacked = attacked,
            HealPayable = payable,
            GuardNothingPaid = _guard is { NothingPaid: true },
            GuardRaceLost = _guard is { RaceLost: true },
            RaceWhy = _guard?.RaceWhy,
            MayLeaveRace = down is null && RaceLeaveState(),
            MayWait = down is null && !SurvivalRule.KeepsItsOwnBars(_state) && _context.Keeper is not null && _clock >= _survivalAsideUntil,
            MayLeave = down is null && _state == AutopilotState.Trip,
            InFight = false,
            Attackers = _survivalAttackers,
        };
        return true;
    }

    /// <summary>What the layer stands down for now, or null.</summary>
    private string? SurvivalStandsDown(IGameSessionView session)
    {
        var self = session.State.Self;
        if (self.Travel != SelfTravel.Present || self.Position is null)
        {
            return "portal space or not placed: the station's own arrival checks run";
        }

        if (GuardStandsDown(_state, _unscripted, SawDeath(_deathTally)))
        {
            return "the scripted death";
        }

        if (_retreating || _urgentRecall || _finalStop is not null)
        {
            return "she is on her way home";
        }

        return ((_run as IStationInner)?.Scenario as IStationCommitted ?? _run as IStationCommitted)?.Committed;
    }

    /// <summary>Who is attacking her: the creatures of the last attacker's name within the watch's reach, with what the session has read of them.</summary>
    private void ReadAttackers(IGameSessionView session, HostileWatch watch)
    {
        _survivalAttackers.Clear();
        if (session.State.Self.Position is not { } at || watch.LastHitBy.Length == 0)
        {
            return;
        }

        var self = at.ToGlobal();
        var live = session.Live; // M6 16 E2b (the one world picture): where the client draws it, else the server's report
        foreach (var o in session.State.Objects.All)
        {
            if (o.Position is null || !string.Equals(o.Name, watch.LastHitBy, StringComparison.Ordinal) || o.Health is <= 0f || live.At(o) is not { } p)
            {
                continue;
            }

            var metres = p.ToGlobal().DistanceTo(self);
            if (metres <= watch.ReachMetres)
            {
                _survivalAttackers.Add(new SurvivalAttacker(o.Guid, o.Name ?? string.Empty, metres, o.Health, session.State.Appraisals.TryGet(o.Guid, out var seen) ? seen.CreatureStamina : null));
            }
        }
    }

    /// <summary>A hold begins (once): the walk lets go of the keys, counted and logged.</summary>
    private void SurvivalBegin(IGameSessionView session, SurvivalReason reason, string why)
    {
        if (_survivalHeld)
        {
            return;
        }

        _survivalHeld = true;
        _survivalHeldAt = _clock;
        _survivalMode = session.State.Self.Qualities.CombatMode;
        _survivalRestLogged = false;
        _survivalHoldsCycle++;
        _survivalHoldsRun++;
        _context.Travel.HoldWalk();
        _log.Info(LogCategory, $"survival: {StateName(_state)}: wait: {reason.ToString().ToLowerInvariant()}: {why}; the {_spec?.Name ?? "?"} station is held and resumes where it stopped");
    }

    /// <summary>A held frame: a hold is no lack of progress for the state's watchdog.</summary>
    private ScenarioStatus SurvivalStillHeld()
    {
        _watchdogs.Progress(_clock);
        return ScenarioStatus.Running;
    }

    /// <summary>The hold is over (if one stood): the station is told how long, and the same walk goes on.</summary>
    private void SurvivalEnd(string why)
    {
        if (!_survivalHeld)
        {
            return;
        }

        _survivalHeld = false;
        var seconds = _clock - _survivalHeldAt;
        ((_run as IStationInner)?.Scenario as IStationHeld ?? _run as IStationHeld)?.NoteHeld(seconds);
        _context.Travel.ReleaseWalk(_clock);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"survival: {StateName(_state)}: go: {why}: held {seconds:0.0} s; the {_spec?.Name ?? "?"} station goes on"));
    }

    /// <summary>Clears the layer's hold when the run it held has ended (every run's start and the known state); a keeper's pass still running is halted.</summary>
    private void SurvivalForget()
    {
        if (_survivalKeeper && !_survivalHalting)
        {
            _context.Keeper?.Halt(_clock, "the activity the survival layer held has ended");
        }

        if (_survivalHeld)
        {
            _context.Travel.ReleaseWalk(_clock);
        }

        (_survivalHeld, _survivalKeeper, _survivalHalting, _survivalModeBack) = (false, false, false, false);
        (_subFightWatched, _subFloorWaits) = (null, false); // M6 16 E3.1: the run the layer stood down over has ended with it
    }

    /// <summary>The wait: check before (the lanes free), the keeper's pass to the back lines, or the needs found met and the layer stands aside by name.</summary>
    private ScenarioStatus? SurvivalWait(IGameSessionView session, double now, in SurvivalDecision decided)
    {
        SurvivalBegin(session, decided.Reason, decided.Why);
        if (session.Actions is ActionApi api && (api.QueuedCount > 0 || api.InFlightCount > 0))
        {
            return SurvivalStillHeld(); // the busy rules: the station's action in flight ends first
        }

        var lines = SurvivalLines;
        if (_context.Keeper!.NeedsFor(new VitalsNeeds(lines.BackHealth, lines.BackStamina, lines.BackMana)))
        {
            _survivalAsideUntil = _clock + SurvivalAsideSeconds;
            SurvivalEnd(string.Create(CultureInfo.InvariantCulture, $"the keeper finds its needs met ({decided.Why}): no new wait for {SurvivalAsideSeconds:0} s"));
            return null;
        }

        (_survivalKeeper, _survivalPassAt) = (true, _clock);
        return SurvivalStillHeld();
    }

    /// <summary>One frame of the keeper's pass of a wait; a hostile on her halts it and the station's own fight-back answers (M5 D88).</summary>
    private ScenarioStatus? SurvivalPassStep(IGameSessionView session, double now)
    {
        var keeper = _context.Keeper!;
        if (_survivalHalting)
        {
            keeper.Step(now);
            if (keeper.Halting && _clock - _survivalHaltAt < KeeperHaltSeconds)
            {
                return SurvivalStillHeld();
            }

            (_survivalKeeper, _survivalHalting) = (false, false);
            SurvivalEnd("the keeper's pass is halted");
            return null;
        }

        if (_hostiles is { } watch && watch.InReach(_clock, out var hostile))
        {
            return SurvivalHalt(keeper, now, $"a hostile is on her ({hostile}): the station's own fight-back answers (M5 D88)");
        }

        switch (keeper.Step(now))
        {
            case VitalsKeeperState.Ready:
                _survivalKeeper = false;
                return SurvivalModeStep(session);
            case VitalsKeeperState.Blocked:
                _survivalKeeper = false;
                _survivalAsideUntil = _clock + SurvivalAsideSeconds;
                SurvivalEnd(string.Create(CultureInfo.InvariantCulture, $"the keeper is blocked ({keeper.Why ?? "blocked"}): the activity goes on, no new wait for {SurvivalAsideSeconds:0} s"));
                return null;
            case VitalsKeeperState.Resting when !_survivalRestLogged:
                _survivalRestLogged = true;
                _log.Info(LogCategory, $"survival: {StateName(_state)}: wait: rest: the keeper's pass is resting for a bar");
                break;
        }

        if (_clock - _survivalPassAt > JourneyScenario.VitalsTimeoutSeconds)
        {
            _survivalAsideUntil = _clock + SurvivalAsideSeconds;
            return SurvivalHalt(keeper, now, string.Create(CultureInfo.InvariantCulture, $"the keeper's pass took over {JourneyScenario.VitalsTimeoutSeconds:0} s: the activity goes on, no new wait for {SurvivalAsideSeconds:0} s"));
        }

        return SurvivalStillHeld();
    }

    private ScenarioStatus? SurvivalHalt(VitalsKeeper keeper, double now, string why)
    {
        _log.Info(LogCategory, $"survival: {StateName(_state)}: go: {why}");
        if (keeper.Halt(now, why) && keeper.Halting)
        {
            (_survivalHalting, _survivalHaltAt) = (true, _clock);
            return SurvivalStillHeld();
        }

        _survivalKeeper = false;
        SurvivalEnd("the keeper's pass is halted");
        return null;
    }

    /// <summary>After the pass: the magic mode she was in comes back (check, act, check; the keeper ends in peace), then the station goes on.</summary>
    private ScenarioStatus? SurvivalModeStep(IGameSessionView session)
    {
        var mode = session.State.Self.Qualities.CombatMode;
        if (_survivalMode != CombatModes.Magic || mode == CombatModes.Magic)
        {
            _survivalModeBack = false;
            SurvivalEnd("over the back lines");
            return null;
        }

        if (!_survivalModeBack)
        {
            (_survivalModeBack, _survivalModeAsked, _survivalModeAt) = (true, false, _clock);
        }

        if (!_survivalModeAsked)
        {
            _survivalModeAsked = true;
            _log.Info(LogCategory, $"survival: {StateName(_state)}: wait: mode: check before: she was in the magic mode and stands in {mode}: asked back before the station goes on");
            session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
        }

        if (_clock - _survivalModeAt > SurvivalModeSeconds)
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"survival: {StateName(_state)}: go: check after: the magic mode did not come back within {SurvivalModeSeconds:0} s (mode {mode}): the station goes on and its own mode step answers"));
            _survivalModeBack = false;
            SurvivalEnd("the mode did not come back");
            return null;
        }

        return SurvivalStillHeld();
    }

    /// <summary>The leave: the running station ends by name, an errand is not called again this run, and she goes home by the retreat's way (a walk or portals where the travel file has one, else Lifestone Recall).</summary>
    private ScenarioStatus SurvivalLeave(IGameSessionView session, in SurvivalDecision decided)
    {
        var race = decided.Reason == SurvivalReason.LosingRace;
        var word = race ? RaceToken : decided.Reason.ToString().ToLowerInvariant();
        return LeaveByName(session, decided.Reason == SurvivalReason.NoMana ? "no-mana" : word, word, decided.Why, race);
    }

    /// <summary>
    /// The leave carried out (<see cref="SurvivalLeave"/>'s body, unchanged; M6 16 E3.1: a quest's sub-fight leaves by it too, <c>AutopilotScenario.SubFightWatch.cs</c>): the station ends by the name
    /// <c>failed:quest:&lt;key&gt;:retreat:&lt;token&gt;</c> (or the activity's own), counted, and she goes home: by Lifestone Recall at once for a lost race while it is paid, else the retreat's way home.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="token">The reason's token in the name.</param>
    /// <param name="word">The reason's word in the line.</param>
    /// <param name="why">The reason and the numbers.</param>
    /// <param name="race">The leave is a lost race's: Lifestone Recall, now.</param>
    /// <param name="manaRecall">M6 16 E3.1: the leave is the way out's (the mana at its line, the recall read paid by the caller): Lifestone Recall by name, as the ground fight's retreat for mana, and the cycle's <c>why=retreat-mana</c>.</param>
    /// <param name="callable">M6 16 E3.1: the quest is not failed for the run by this leave (a station fight's leave within the ground fight's bound, <c>AutopilotScenario.SubFightWatch.cs</c>); false, the layer's own leave: failed for the run, as ever.</param>
    private ScenarioStatus LeaveByName(IGameSessionView session, string token, string word, string why, bool race, bool manaRecall = false, bool callable = false)
    {
        var station = _spec?.Name ?? "?";
        token = "retreat:" + token;
        var name = _questTripActive ? $"failed:quest:{_questKey}:{token}" : $"failed:{StateName(_state)}:{station}:{token}";
        _survivalLeavesCycle++;
        _survivalLeavesRun++;
        _log.Warn(LogCategory, $"survival: {StateName(_state)}: leave: {word}: {why}: the {station} station ends by name {name}; she goes home");
        if (_run is not null)
        {
            DisposeRun();
            _phase = RunPhase.Done;
            _context.Observer.StationEnded(station, false, _clock);
        }

        SurvivalForget();
        VillaStepLeft("the survival layer's leave (" + name + ")"); // M6 villa
        if (_questTripActive)
        {
            if (!callable)
            {
                _questFailed[_questKey] = name;
            }

            if (_questLadderErrand)
            {
                LastResortArm(_questKey); // M6 8 long round 3 (D120): a leave out of a ladder errand is a failed errand: if its way home fails, /lifestone
                _lastResortForLeave = _lastResortKey is not null;
            }

            _questTripActive = false;
            _questLadderErrand = false;
            _tripPending = _questThen == QuestThen.Trip;
        }

        NoteWhy(manaRecall ? RetreatManaWhy : RetreatWhy);
        _retreat.Reset();
        _retreatUnder = false;
        _tally.Retreats++;
        FoldInFlight();
        if (manaRecall)
        {
            (_retreating, _manaRetreating) = (true, true); // the check after is the way out's own (RetreatHome, NoteManaRecallFailed)
            return BeginRun(AutopilotState.Travel, RecallHomeSpec, $"retreat: {name}: {why}");
        }

        if (race)
        {
            // M6 14 the losing race: the way home is Lifestone Recall, now (the guard asked the leave only while it is paid); a walk home among the attackers would be the death the race foretold
            _guard?.RaceTaken(_clock);
            var (payable, paid) = UrgentRecallRead(session);
            if (payable)
            {
                _retreating = true;
                _log.Warn(LogCategory, $"survival: {StateName(_state)}: leave: {RaceToken}: Lifestone Recall, check before: {paid}; the vitals guard holds off its heals for {AutopilotVitalsGuard.HoldOffSeconds:0.##} s while it is cast (M6 14 the losing race)");
                return BeginRun(AutopilotState.Travel, RecallHomeSpec, $"retreat: {name}: {why}");
            }

            _log.Warn(LogCategory, $"survival: {StateName(_state)}: leave: {RaceToken}: Lifestone Recall is no longer payable ({paid}): the retreat's own way home");
        }

        var way = HomeDecision();
        if (way is { Way: TravelWay.AtHome })
        {
            _log.Info(LogCategory, $"survival: {StateName(_state)}: leave: she stands at home ({way.Reason}): no way home is run");
            return RetreatHome("she stood at home");
        }

        _retreating = true;
        return BeginRun(AutopilotState.Travel, way is { Way: TravelWay.RungChange } ? RungHomeSpec : RecallHomeSpec, $"retreat: {name}: {why}");
    }

    /// <summary>M6 14 the losing race: the token of its leave (<c>retreat:losing-race</c>) and of the retreat rule's arm (<see cref="RetreatRule"/>).</summary>
    public const string RaceToken = RetreatRule.LosingToken;

    /// <summary>M6 14 the losing race: true in a state whose activity the layer may end for a lost race: a Trip (an errand too), or a Travel that is not her way home (the rung change's way out, the den's travel).</summary>
    private bool RaceLeaveState() => RaceLeaveStateOf(_state, homeward: _retreating || _urgentRecall || _finalStop is not null || ReferenceEquals(_spec, RungHomeSpec) || ReferenceEquals(_spec, RecallHomeSpec));

    /// <summary>The state test of <see cref="RaceLeaveState"/>, pure: a Trip, or a Travel that is not <paramref name="homeward"/> (a retreat's, the urgent recall's, the stop's, or a way home's station).</summary>
    /// <param name="state">The state.</param>
    /// <param name="homeward">The travel is a way home.</param>
    internal static bool RaceLeaveStateOf(AutopilotState state, bool homeward) => state == AutopilotState.Trip || (state == AutopilotState.Travel && !homeward);

    /// <summary>
    /// M6 14 the losing race: the leave the vitals guard can hand a lost race to now (<see cref="AutopilotVitalsGuard.LeaveRoad"/>): the survival layer's (a Trip or a Travel out, on the ladder's job, the layer not standing down) or the
    /// retreat rule's (the known state, the upkeep or the buff on the ground with a retreat line: <see cref="GuardRetreat"/>), by Lifestone Recall while it is paid (<see cref="UrgentRecallRead"/>: the book, the tie, the components, the mana
    /// the cast and the station need), on the ladder's job only (the den's run keeps its order). Closed with why otherwise: the guard heals on.
    /// </summary>
    private (bool Open, string Why) RaceRoad()
    {
        if (_session is not { } session)
        {
            return (false, "no session");
        }

        var survival = SurvivalOn() && RaceLeaveState() && SurvivalStandsDown(session) is null;
        var ground = SurvivalOn() && !_retreating && _state is AutopilotState.KnownState or AutopilotState.Upkeep or AutopilotState.Buff && Classify(session.State, _ground) == AutopilotPlace.Ground
            && RetreatLineNow(session, out _) > 0; // a ground whose line is 0 never retreats (the rule is off): the guard heals on there
        return RaceRoadOf(StateName(_state), survival, ground, survival || ground ? UrgentRecallRead(session) : (false, "not read"));
    }

    /// <summary>The road of a lost race (<see cref="RaceRoad"/>), pure: open by the survival layer's leave or the retreat rule's while Lifestone Recall is paid; closed with why otherwise.</summary>
    /// <param name="state">The state's name, for the words.</param>
    /// <param name="survival">The survival layer may leave this activity for the race (a trip or a travel out on the ladder's job, the layer not standing down).</param>
    /// <param name="ground">The retreat rule may take it (the known state, the upkeep or the buff, on the ground, no retreat under way).</param>
    /// <param name="recall">Whether Lifestone Recall is paid now, and why (<see cref="UrgentRecallRead"/>).</param>
    internal static (bool Open, string Why) RaceRoadOf(string state, bool survival, bool ground, (bool Payable, string Why) recall)
    {
        if (!survival && !ground)
        {
            return (false, $"the {state} state takes no leave for it here (a trip, a travel out on the ladder's job, or a state on the ground does; she is on her way home, or the layer stands down)");
        }

        return recall.Payable
            ? (true, $"{(survival ? "the survival layer's leave" : "the retreat rule's")} by Lifestone Recall ({recall.Why})")
            : (false, "Lifestone Recall is not payable: " + recall.Why);
    }

    /// <summary>M6 8 K, start ready: a ladder errand starts with the bars a fight starts with (the same <see cref="StartPhases"/> path, the ladder's numbers). With no keeper nobody could bring them, and a need nobody can meet would only fail the errand.</summary>
    private StationSpec ReadyErrandSpec(string key, string from, string? steps = null)
    {
        var spec = QuestSpec(key, from, steps);
        if (_context.Keeper is null)
        {
            return spec;
        }

        var lines = SurvivalLines;
        return spec with { NeedsOverride = new StationNeeds(StationWhere.Here, Peace: true, MinHealthPercent: lines.StartHealth, MinStaminaPercent: lines.StartStamina, MinManaPercent: lines.StartMana, Buffed: true) };
    }

    /// <summary>M6 8 K, start ready: no enchantment stands on her: the errand does not start (never a failed precondition): the way the caller was going goes on, the run's own buff pass comes (at home, or on the ground with no enchantment up), and the next way home asks again.</summary>
    private bool ErrandWaitsForBuffs(string key)
    {
        if (_context.Keeper is null || _session is not { } session || !session.State.Self.Qualities.Enchantments.IsDefaultOrEmpty)
        {
            return false;
        }

        _log.Info(LogCategory, $"survival: {StateName(_state)}: wait: buff: no enchantment stands before the ladder errand {key}: it is asked again at the next way home, after the run's own buff pass");
        return true;
    }
}
