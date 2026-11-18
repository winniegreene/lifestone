using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 16 E3.1 (docs/automation-engine.md E3, <c>TODO(engine-guard-over-subfight)</c>): one healer in a quest's sub-fight, and the way home kept there too. A quest's kill or hunt-until step runs the mage's fight inside
/// the quest station, in the Trip state, where the survival layer and the vitals guard are on: until this slice two deciders healed one character there, the guard froze the fight while it did, and nobody kept mana for
/// the way home or asked the ground fight's leaves.
/// <para><b>A staging step, not the end state.</b> The owner's rule is that keeping her alive is one layer above quests, walks, trips and fights, and the fights move under it in stages (D119). This slice does not move
/// the fight under the layer: it makes the sub-fight and the ground fight ONE case (the fight's own deciders, watched from outside for the leaves), so each later stage flips one decider once, for both
/// (<c>TODO(engine-survival-flip)</c>).</para>
/// <list type="bullet">
/// <item><b>The stand-down.</b> While the Trip state's station steps a <see cref="FightScenario"/> (<see cref="IStationSubFight"/>, the interface the errand lapse gave the station) whose emergency is on
/// (<see cref="FightScenario.EmergencyOn"/>) and whose stop has not been asked, the survival layer's frame is not run: no heal pick of the guard, no wait of the layer, as in the Fight state
/// (<see cref="SurvivalRule.KeepsItsOwnBars"/>). The station's walks, talks and waits are under the layer as before, and so is a fight that is not the mage's, has its emergency off, or is stopping.</item>
/// <item><b>The watch.</b> The stop reasons of a ground fight that are about her staying alive are asked each frame, in the ground fight's order: the retreat on the fight line with the lost race in it
/// (<see cref="RetreatDue"/>, the line read on the sub-fight's own ground), then the mana way out, which arms the fight's reserve as it does a ground fight's (<see cref="ManaRetreatDue"/>). A yes is a leave, carried
/// out as the layer's leave (<see cref="LeaveByName"/>): the quest station ends by the name <c>failed:quest:&lt;key&gt;:retreat:&lt;token&gt;</c> and she goes home (Lifestone Recall at once for a lost race and for
/// the way out, else the retreat's way home). The quest stays callable and is called again from home, within the ground fight's own bound kept for the quest (<see cref="SubFightLeave"/>). It never counts toward the
/// rung's <c>retreat:again</c>: she was not on the rung's ground. The ground's own reasons (the stop rules, the trips,
/// the counters, the hour, the move-on, the trial, the kill task) are not asked.</item>
/// <item><b>The health floor</b> is asked on the ground fight's cadence and condition. A ground fight ends for the keeper's heal in place and begins again; a sub-fight cannot end without failing its step, so with no
/// hostile in reach the station is held (the layer's hold: <see cref="SurvivalBegin"/>) while the keeper's pass heals her to the share a fight starts at, and the fight resumes where it stopped. With a hostile in reach
/// it is not held: the fight's emergency and the retreat rule decide, as in a ground fight on the ladder.</item>
/// </list>
/// Each decision is one line, <c>survival: trip: ...</c>; the retreat's and the way out's own lines (<c>retreat: decide</c>, <c>retreat: mana: decide</c>, <c>way out: armed</c>) name the asker <c>sub-fight</c>.
/// </summary>
public sealed partial class AutopilotScenario
{
    /// <summary>The asker's name in the retreat rule's and the way out's lines when a station's fight is asked (<c>fight</c> for a ground fight).</summary>
    public const string SubFightWhere = "sub-fight";

    /// <summary>How long the health floor's hold over a station's fight waits for the action lanes to free before it is given up, in seconds (Lifestone's own: an action in flight ends with its answer or its own time-out of 5 s, and a frozen fight is the worse harm).</summary>
    public const double SubFloorLaneSeconds = 6;

    private FightScenario? _subFightWatched; // the station's fight the layer last stood down over (a new one is said once)
    private double _subFloorAt = double.NegativeInfinity;
    private bool _subFloorWaits; // the floor's hold stands and waits for the lanes before the keeper's pass
    private bool _subFloorSaid; // why the floor took no hold was said for this episode
    private int _subLeaveCycle; // the ground fight's count (TakeRetreat's), kept for a quest: the cycle, the quest and its leaves on the fight line in it
    private string? _subLeaveKey;
    private int _subLeavesHere;
    private string? _subQuestOwed; // the quest a leave kept callable, to be called again before she next leaves home
    private bool _subQuestOwedLadder;

    /// <summary>M6 16 E3.1: the station fights the survival layer stood down over this run (each said once).</summary>
    public int SubFightsKept { get; private set; }

    /// <summary>M6 16 E3.1: the station fights left by name this run (the retreat, the lost race, the way out).</summary>
    public int SubFightLeaves { get; private set; }

    /// <summary>M6 16 E3.1: the holds for the health floor's heal in place over a station fight this run.</summary>
    public int SubFightFloorHolds { get; private set; }

    /// <summary>
    /// The station's fight that keeps her bars this frame, or null: the Trip state's station steps a <see cref="FightScenario"/> with its emergency on and no stop asked, and no hold of the layer's stands over it (a
    /// keeper's pass or a mode step under way runs to its end first, by the layer's own frame). Allocation-free; a line when a watch ends.
    /// </summary>
    private FightScenario? SubFightKeepingHerBars()
    {
        if (_state != AutopilotState.Trip || _run is not IStationSubFight { SubFight: FightScenario { EmergencyOn: true, StopReason: null } fight })
        {
            if (_subFightWatched is not null)
            {
                _subFightWatched = null;
                _subFloorWaits = false;
                _log.Info(LogCategory, $"survival: {StateName(_state)}: back: the station's fight ended or is stopping: the layer and the vitals guard decide again (M6 16 E3.1)");
            }

            return null;
        }

        return _survivalKeeper || _survivalModeBack || (_survivalHeld && !_subFloorWaits) ? null : fight;
    }

    /// <summary>One frame over the station's fight that keeps her bars: the leaves first, then the health floor. Null: the station is stepped. Running: held for the floor's heal. Any other status: the leave's.</summary>
    private ScenarioStatus? SubFightWatch(IGameSessionView session, FightScenario fight)
    {
        if (!ReferenceEquals(fight, _subFightWatched))
        {
            _subFightWatched = fight;
            SubFightsKept++;
            (_subFloorAt, _subFloorWaits, _subFloorSaid) = (_clock, false, false);
            _retreat.Reset(); // a new fight: another fight's hold is not this one's
            _retreatUnder = false;
            _log.Info(LogCategory, $"survival: {StateName(_state)}: stand-down: the {_spec?.Name ?? "?"} station steps a fight on {fight.Ground.Key} that keeps her bars ({fight.HealDecidersWords()}): the vitals guard and the layer's wait stand down over it as over a ground fight; it is watched for the retreat on the fight line, the lost race, the way out and the health floor, and a leave ends the station by name (M6 16 E3.1: one case with the ground fight, a stage toward the fight under the layer)");
        }

        if (_finalStop is not null)
        {
            return null; // the recall before a stop is under way: no leave is laid over it
        }

        var due = RetreatDue(session, SubFightWhere, sub: fight);
        _hourRaceLost = false; // the hour's load is the rung's ground's: a race read in a quest's fight is not its cycle's
        if (due)
        {
            if (WayOutOn() && fight.DrinkForRecall(session))
            {
                return null; // M6 8 (the sixth death), as the ground fight: the retreat waits while the fight drinks for the recall
            }

            return SubFightLeave(session, _retreatToken, forMana: false);
        }

        if (ManaRetreatDue(session, fight, SubFightWhere))
        {
            _retreatForMana = false;
            var (payable, paid) = UrgentRecallRead(session);
            if (!payable)
            {
                _wayOutAsideUntil = _clock + WayOutAsideSeconds;
                _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"retreat: mana: check before the recall: not payable ({paid}): no recall is cast; she fights on and the rule is read again in {WayOutAsideSeconds:0} s"));
                return null;
            }

            _log.Info(LogCategory, $"retreat: mana: check before the recall: {paid}");
            return SubFightLeave(session, ManaWayOut.Leave, forMana: true);
        }

        return SubFightFloor(session, fight);
    }

    /// <summary>
    /// A leave of the station's fight, by the ground fight's rule (the lead's review of E3.1): it ends the step and takes her home, and the quest stays callable this run, as a retreat from a ground fight goes home and back
    /// out. The bound is the ground fight's own (<see cref="TakeRetreat"/>): a leave for mana is counted nowhere (<c>retreat:again</c> never counts one); the leaves on the fight line (the lost race among them) are counted
    /// for the quest in the cycle, and the <see cref="RetreatsPerGroundCycle"/>th gives the quest up for the run by the name <c>failed:quest:&lt;key&gt;:retreat:again:&lt;token&gt;</c> (a ground stops the run there; a
    /// quest is failed for it). A quest kept callable is called again from home before she next leaves it (<see cref="SubFightQuestAgain"/>), after the retreat's keeper's pass and the known state's buff pass; a quest
    /// task is begun again by its own arrow, and the villa's fetch by the villa's schedule.
    /// </summary>
    private ScenarioStatus SubFightLeave(IGameSessionView session, string word, bool forMana)
    {
        SubFightLeaves++;
        _subFightWatched = null;
        var token = word;
        var callable = true;
        if (_questTripActive)
        {
            var key = _questKey;
            if (!forMana)
            {
                if (_subLeaveCycle != _cycle || !string.Equals(_subLeaveKey, key, StringComparison.Ordinal))
                {
                    (_subLeaveCycle, _subLeaveKey, _subLeavesHere) = (_cycle, key, 0);
                }

                callable = ++_subLeavesHere < RetreatsPerGroundCycle;
            }

            if (callable)
            {
                var own = _questThen == QuestThen.Task ? "the quest task's own arrow begins it again" : _villaFetchKey == key ? "the villa's schedule asks it again" : null;
                if (own is null)
                {
                    (_subQuestOwed, _subQuestOwedLadder) = (key, _questLadderErrand);
                }

                var counted = forMana
                    ? $"a leave for mana is not counted, as {RetreatAgainPrefix}<ground> never counts a retreat for mana"
                    : string.Create(CultureInfo.InvariantCulture, $"leave {_subLeavesHere} of {RetreatsPerGroundCycle - 1} on the fight line for this quest in cycle {_cycle}");
                _log.Info(LogCategory, $"autopilot quest: key={key}: the leave of its fight ({word}) does not fail it for the run, as a retreat from a ground fight goes home and back out: {own ?? "it is called again before she next leaves home, after the keeper's pass and her buffs"}; {counted} (M6 16 E3.1)");
            }
            else
            {
                token = "again:" + word;
                _subQuestOwed = string.Equals(_subQuestOwed, key, StringComparison.Ordinal) ? null : _subQuestOwed;
                _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot quest: key={key}: the leave {_subLeavesHere} of its fight on the fight line in cycle {_cycle} ({word}): the quest is given up for the run by name, the ground fight's bound ({RetreatAgainPrefix}<ground> at the retreat {RetreatsPerGroundCycle} from one ground in one cycle) (M6 16 E3.1)"));
            }
        }

        return LeaveByName(session, token, word, _retreatWhy + ": the station's fight is left as a ground fight is (M6 16 E3.1)", race: !forMana && string.Equals(word, RaceToken, StringComparison.Ordinal), manaRecall: forMana, callable: callable);
    }

    /// <summary>
    /// The quest a station fight's leave kept callable, called again from home (<see cref="StartQuestNow"/> asks first, before she leaves home: the retreat's keeper's pass and the known state's buff pass have run): the
    /// errand's own start with its own checks (<see cref="StartErrand"/>). A hunt-until goes on from what she holds (the step counts the items held, <c>QuestRun.HuntStep</c>), a kill step from the server's counter. Null:
    /// none owed, or the errand's own check holds it (named; the ordinary ways home ask it again). Asked once a leave.
    /// </summary>
    private ScenarioStatus? SubFightQuestAgain(string why)
    {
        if (_subQuestOwed is not { } key || _questTripActive || _session is null)
        {
            return null;
        }

        var ladder = _subQuestOwedLadder;
        _subQuestOwed = null;
        if (_questFailed.TryGetValue(key, out var failed))
        {
            _log.Warn(LogCategory, $"autopilot quest: key={key}: owed again after the leave of its fight, but failed this run meanwhile ({failed}): not called");
            return null;
        }

        var reason = string.Create(CultureInfo.InvariantCulture, $"{why}: the quest {key} again after the leave of its fight: it goes on from what she holds ({HuntHeld(key)} of its hunted items held) (M6 16 E3.1)");
        _log.Info(LogCategory, $"autopilot quest: key={key}: {reason}");
        var started = StartErrand(QuestThen.Home, forced: false, key, JourneyPlans.FromHome, ladder, reason);
        if (started is null)
        {
            _log.Info(LogCategory, $"autopilot quest: key={key}: the errand's own check holds it (the line above names it); the ordinary ways home ask it again");
        }

        return started;
    }

    /// <summary>The health floor over the station's fight, every <see cref="UpkeepCheckSeconds"/>: under it with no hostile in reach, the hold for the keeper's heal in place; with one in reach, or with no keeper, said once and the fight goes on.</summary>
    private ScenarioStatus? SubFightFloor(IGameSessionView session, FightScenario fight)
    {
        if (_subFloorWaits)
        {
            return SubFloorStep(session);
        }

        if (_clock - _subFloorAt < UpkeepCheckSeconds)
        {
            return null;
        }

        _subFloorAt = _clock;
        var ground = fight.Ground;
        var floor = DerivedLineNow(session, ground) is { } line ? Math.Min(line, JourneyPlans.FightHealthPercent) : HealthFloorFor(ground, _options); // HealthFloorNow's sum, on the fight's own ground
        if (floor <= 0 || !session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var max) || max == 0 || health == 0 || health * 100.0 / max >= floor)
        {
            _subFloorSaid = false;
            return null;
        }

        string? not = null;
        if (_hostiles is { } watch && watch.InReach(_clock, out var hostile))
        {
            not = $"a hostile is in reach ({hostile}): the fight is not held for the keeper's heal in place; its emergency and the retreat rule decide (m6-floor-under-attack)";
        }
        else if (_context.Keeper is null)
        {
            not = "this run has no vitals keeper: the fight's own emergency heals";
        }
        else if (_clock < _survivalAsideUntil)
        {
            not = "the keeper stood aside a moment ago: the fight's own emergency heals";
        }

        if (not is not null)
        {
            if (!_subFloorSaid)
            {
                _subFloorSaid = true;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"survival: {StateName(_state)}: sub-fight: health {health} of {max} is under the {floor:0}% floor, and {not} (M6 16 E3.1)"));
            }

            return null;
        }

        _subFloorSaid = false;
        SubFightFloorHolds++;
        SurvivalBegin(session, SurvivalReason.Heal, string.Create(CultureInfo.InvariantCulture, $"health {health} of {max} is under the {floor:0}% floor of the station's fight with no hostile in reach: the keeper's heal in place to {JourneyPlans.FightHealthPercent:0}%, as a ground fight's health floor (M6 16 E3.1)"));
        _subFloorWaits = true;
        return SubFloorStep(session);
    }

    /// <summary>The floor's hold before the keeper's pass: a hostile in reach ends it; the fight's action in flight ends first (the busy rules); then the keeper's pass, which the layer's own frame runs to its end.</summary>
    private ScenarioStatus? SubFloorStep(IGameSessionView session)
    {
        if (!_survivalHeld)
        {
            _subFloorWaits = false;
            return null;
        }

        if (_hostiles is { } watch && watch.InReach(_clock, out var hostile))
        {
            _subFloorWaits = false;
            SurvivalEnd($"a hostile is in reach ({hostile}): the fight's own deciders answer");
            return null;
        }

        if (session.Actions is ActionApi api && (api.QueuedCount > 0 || api.InFlightCount > 0))
        {
            if (_clock - _survivalHeldAt <= SubFloorLaneSeconds)
            {
                return SurvivalStillHeld();
            }

            // a fight is never left frozen behind a lane that does not free: the hold is given up and the fight's own deciders go on
            _subFloorWaits = false;
            _survivalAsideUntil = _clock + SurvivalAsideSeconds;
            SurvivalEnd(string.Create(CultureInfo.InvariantCulture, $"the action lanes did not free within {SubFloorLaneSeconds:0} s ({api.QueuedCount} queued, {api.InFlightCount} in flight): the fight goes on by its own deciders, no new wait for {SurvivalAsideSeconds:0} s"));
            return null;
        }

        _subFloorWaits = false;
        if (_context.Keeper!.NeedsFor(new VitalsNeeds((int)JourneyPlans.FightHealthPercent)))
        {
            _survivalAsideUntil = _clock + SurvivalAsideSeconds;
            SurvivalEnd(string.Create(CultureInfo.InvariantCulture, $"the keeper finds its needs met: no new wait for {SurvivalAsideSeconds:0} s"));
            return null;
        }

        (_survivalKeeper, _survivalPassAt) = (true, _clock);
        return SurvivalStillHeld();
    }
}
