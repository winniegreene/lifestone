using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>What an order asks the running scenario to do at its next safe point (the command registry's scenario side, <c>Commands/CommandRegistry.cs</c>).</summary>
public enum ScenarioOrderKind
{
    /// <summary>One logistics trip now: home, the sale by the pack rules, the buys with the scroll plan, the supplies, and back (the forced trip's station).</summary>
    Trip,

    /// <summary>Her own buff pass now, from her spell bar: the start pass, which renews every line due within a buff cycle.</summary>
    Rebuff,

    /// <summary>The buff bot's stop at home (home first when she is away).</summary>
    RebuffBot,

    /// <summary>One quest's errand by the start-quest path (home first when she is away); <see cref="ScenarioOrder.Arg"/> is the key.</summary>
    Quest,

    /// <summary>A safe point only: the order's owner acts on it (the pause stops her where she stands).</summary>
    Point,
}

/// <summary>Where an order stands in the scenario.</summary>
public enum ScenarioOrderState
{
    /// <summary>Asked; it waits for the next safe point.</summary>
    Waiting,

    /// <summary>Taken at a safe point; its work is running.</summary>
    Taken,

    /// <summary>Its work ended well (the check after passed).</summary>
    Done,

    /// <summary>It was refused at its safe point, or its work did not end well.</summary>
    Failed,
}

/// <summary>One order the scenario carries (one at a time). The scenario writes its state; its owner reads it each frame.</summary>
/// <param name="id">The order's id, for the log.</param>
/// <param name="kind">What it asks.</param>
/// <param name="arg">Its argument (a quest key), or null.</param>
/// <param name="steps">A quest's step range (<c>&lt;from&gt;-&lt;to&gt;</c>, 0-based), or null for all its steps.</param>
public sealed class ScenarioOrder(string id, ScenarioOrderKind kind, string? arg = null, string? steps = null)
{
    /// <summary>The order's id.</summary>
    public string Id { get; } = id ?? throw new ArgumentNullException(nameof(id));

    /// <summary>What it asks.</summary>
    public ScenarioOrderKind Kind { get; } = kind;

    /// <summary>Its argument (a quest key), or null.</summary>
    public string? Arg { get; } = arg;

    /// <summary>A quest's step range, or null.</summary>
    public string? Steps { get; } = steps;

    /// <summary>Where it stands.</summary>
    public ScenarioOrderState State { get; internal set; } = ScenarioOrderState.Waiting;

    /// <summary>The outcome's token (<c>trip-done</c>, <c>failed:trip-given-up</c>, ...); null while it runs.</summary>
    public string? Outcome { get; internal set; }

    /// <summary>The outcome in words, or the last thing it did.</summary>
    public string Detail { get; internal set; } = "waiting for a safe point";

    /// <summary>The safe point it was taken at: <c>known-state</c>, <c>lull</c> or <c>errand-lull</c>; null before.</summary>
    public string? TakenAt { get; internal set; }

    /// <summary>The scenario state when it was taken (the check that it was never taken mid-cast: a station's run is never in hand there).</summary>
    public AutopilotState? TakenInState { get; internal set; }

    /// <summary>The session clock when it was taken.</summary>
    public double TakenClock { get; internal set; } = double.NaN;

    /// <summary>True when it went home first (a bot visit or a quest from the field), so the take waits for the known state at home.</summary>
    public bool WentHome { get; internal set; }

    /// <summary>True once it has ended (done or failed).</summary>
    public bool Ended => State is ScenarioOrderState.Done or ScenarioOrderState.Failed;

    internal int TripsAt { get; set; }

    internal int GiveUpsAt { get; set; }

    internal StationSpec? Spec { get; set; }

    internal bool TripFirst { get; set; }
}

/// <summary>
/// The scenario's side of the command registry (the owner's ruling of 2026-11-17: finer-grained commands for her (a sell trip, a rebuff, a named quest)). An order is asked any time and taken only at a safe
/// point, the same ones the re-seat uses (the steer contract, section 4): the known state (present, the audit finished, no corpse owed, no trip, death or spend in flight, at home or on the ground, no hostile in reach) and the fight's lull
/// (its last arm: every survival and stop trigger ranks above it, no hostile in reach, no pull or keeper's fight-back), plus the errand fight's lull for a trip. Never mid-cast: at the known state no station runs, and at a lull the fight is
/// asked to stop and ends cleanly. Each take logs a check before, each end a check after, and each act runs an existing station (the logistics trip, the buff pass, the bot's stop, the quest station) through <see cref="BeginRun"/>.
/// </summary>
public sealed partial class AutopilotScenario
{
    /// <summary>The fight's stop reason for an order taken in a lull (routed in <see cref="FightEnded"/>).</summary>
    public static FightStopReason OrderStop { get; } = new("order");

    private ScenarioOrder? _order;
    private double _safePointAt = double.NaN;
    private (string Key, bool Ladder, string? Steps)? _resumeErrand; // an errand an order's trip cut: it runs again from home, from what she holds

    /// <summary>The order the scenario carries, or null.</summary>
    public ScenarioOrder? Order => _order;

    /// <summary>True on the frame a safe point was reached (the known state's or a lull's), whatever was asked: the pause reads it.</summary>
    public bool SafePointNow => _safePointAt == _clock;

    /// <summary>The trips this scenario has made (each counted when its station ended well).</summary>
    public int TripsMade { get; private set; }

    /// <summary>The trips this scenario has given up after their retry.</summary>
    public int TripsGivenUp { get; private set; }

    /// <summary>Asks <paramref name="order"/> at the next safe point. One at a time.</summary>
    /// <param name="order">The order.</param>
    /// <returns>Null when asked; else the refusal's token.</returns>
    public string? AskOrder(ScenarioOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (_end != ScenarioStatus.Running)
        {
            return "not-running";
        }

        if (_stopAsked is not null || _finalStop is not null)
        {
            return "stopping";
        }

        if (_order is { Ended: false })
        {
            return "busy";
        }

        if (order.Kind == ScenarioOrderKind.Quest && order.Arg is { } key && QuestOrderRefusal(key) is { } refused)
        {
            return refused;
        }

        if (order.Kind == ScenarioOrderKind.RebuffBot && !BuffBotApplies())
        {
            return "bot-off";
        }

        _order = order;
        _log.Info(LogCategory, $"autopilot order: {order.Id} {Describe(order)}: check before: asked in the {StateName(_state)} state; it waits for the next safe point (the known state, or a lull with no hostile in reach)");
        return null;
    }

    /// <summary>Takes back an order that has not been taken yet.</summary>
    /// <param name="why">Why, for the log.</param>
    /// <returns>True when one was waiting.</returns>
    public bool CancelOrder(string why)
    {
        if (_order is not { State: ScenarioOrderState.Waiting } order)
        {
            return false;
        }

        _order = null;
        _log.Info(LogCategory, $"autopilot order: {order.Id} {Describe(order)}: cancelled before its safe point ({why})");
        return true;
    }

    /// <summary>
    /// Why a quest order for <paramref name="key"/> is refused now, by the start-quest path's own checks (the catalog, the ladder band, the level gate, a once-quest done, its timer, the run's quest selection), or null. The level is read live.
    /// </summary>
    /// <param name="key">The quest's key.</param>
    public string? QuestOrderRefusal(string key)
    {
        if ((TrophyQuests ?? QuestCatalog.All).All(q => q.Key != key))
        {
            return "unknown-quest";
        }

        if (_session is not { } session)
        {
            return null; // nothing read yet: the take checks again
        }

        var level = session.State.Self.Qualities.TryGetInt(PlayerQualities.LevelProperty, out var read) ? read : 0;
        return StartQuestRefusal(key, level, session.Clock.Time.GetUtcNow()) is { } why ? JourneyVerdict.Token(why.Split(':')[0]) : null;
    }

    private static string Describe(ScenarioOrder order) => order.Kind switch
    {
        ScenarioOrderKind.Trip => "trip",
        ScenarioOrderKind.Rebuff => "rebuff",
        ScenarioOrderKind.RebuffBot => "rebuff bot",
        ScenarioOrderKind.Quest => "quest " + order.Arg + (order.Steps is { } s ? " steps " + s : string.Empty),
        _ => "safe point",
    };

    /// <summary>Each frame: a taken trip's check after (its count rose, or it was given up), and a run that ended under an order.</summary>
    private void OrderWatch()
    {
        if (_order is not { Ended: false } order)
        {
            return;
        }

        if (_end != ScenarioStatus.Running)
        {
            OrderEnd(order, ok: false, "failed:run-ended", $"the run ended ({StopName ?? "?"}) before the order's work did");
            return;
        }

        if (order.State != ScenarioOrderState.Taken || order.Kind != ScenarioOrderKind.Trip || order.TripFirst)
        {
            return;
        }

        if (TripsGivenUp > order.GiveUpsAt)
        {
            OrderEnd(order, ok: false, "failed:trip-given-up", "check after: the trip failed twice and was given up for this cycle");
        }
    }

    private void OrderEnd(ScenarioOrder order, bool ok, string outcome, string detail)
    {
        order.State = ok ? ScenarioOrderState.Done : ScenarioOrderState.Failed;
        order.Outcome = outcome;
        order.Detail = detail;
        _log.Info(LogCategory, $"autopilot order: {order.Id} {Describe(order)}: {(ok ? "done" : "failed")}: {outcome}: {detail}");
    }

    private void OrderTake(ScenarioOrder order, string where)
    {
        order.State = ScenarioOrderState.Taken;
        order.TakenAt = where;
        order.TakenInState = _state;
        order.TakenClock = _clock;
        order.TripsAt = TripsMade;
        order.GiveUpsAt = TripsGivenUp;
        _log.Info(LogCategory, $"autopilot order: {order.Id} {Describe(order)}: taken at the {where} ({StateName(_state)} state, no hostile in reach): act");
    }

    /// <summary>The known state's safe point (as the seat's: <see cref="SeatAtKnownState"/>): the order acts here, or null to let the known state decide.</summary>
    private ScenarioStatus? OrderAtKnownState(IGameSessionView session)
    {
        if (_stopAsked is not null || _audit is not null || _tripPending || _deathPending || _ladderSpend is not null)
        {
            return null;
        }

        var place = Classify(session.State, _ground);
        if (place is not (AutopilotPlace.Home or AutopilotPlace.Ground) || _hostiles?.InReach(_clock, out _) == true)
        {
            return null;
        }

        _safePointAt = _clock;
        if (_order is not { State: ScenarioOrderState.Waiting } order)
        {
            return ResumeCutErrand(place == AutopilotPlace.Home);
        }

        var home = place == AutopilotPlace.Home;
        switch (order.Kind)
        {
            case ScenarioOrderKind.Trip:
                OrderTake(order, "known-state");
                return OrderBegin(order, AutopilotState.Trip, TripSpec(forced: true), $"order {order.Id}: one logistics trip now (sale, buys with the scroll plan, supplies, back)");
            case ScenarioOrderKind.Rebuff:
                OrderTake(order, "known-state");
                return OrderBegin(order, AutopilotState.Buff, home ? BuffSpec : GroundBuffSpec(start: true), $"order {order.Id}: her own pass now, from her spell bar (every line due within a buff cycle)");
            case ScenarioOrderKind.RebuffBot when home:
                OrderTake(order, "known-state");
                return OrderBegin(order, AutopilotState.Buff, BuffBotSpec, $"order {order.Id}: the buff bot's stop now");
            case ScenarioOrderKind.Quest when home:
                return OrderQuestAtHome(session, order);
            case ScenarioOrderKind.RebuffBot or ScenarioOrderKind.Quest:
                return OrderGoHome(order, "known-state");
            default:
                OrderTake(order, "known-state");
                return null; // a point only: its owner acts after this step
        }
    }

    /// <summary>The fight's lull, its last arm (as <see cref="SeatAtLull"/>): the reason the fight is asked to stop for, or null.</summary>
    private FightStopReason? OrderAtLull(IStoppableFight fight)
    {
        if (_stopAsked is not null || _tripPending || _deathPending || _ladderSpend is not null || _keeperBack is not null || fight is FightScenario { PullRunning: true })
        {
            return null;
        }

        if (_hostiles is not { } watch || watch.InReach(_clock, out _))
        {
            return null;
        }

        _safePointAt = _clock;
        if (_order is not { State: ScenarioOrderState.Waiting } order)
        {
            return null;
        }

        OrderTake(order, "lull");
        if (order.Kind == ScenarioOrderKind.Point)
        {
            return null; // a point only: the fight goes on until its owner acts
        }

        return OrderStop;
    }

    /// <summary>The fight stopped for an order in its lull: the order's station begins.</summary>
    private ScenarioStatus OrderAfterFight()
    {
        if (_order is not { State: ScenarioOrderState.Taken } order)
        {
            return EnterKnown("the fight stopped for an order that is no longer asked");
        }

        return order.Kind switch
        {
            ScenarioOrderKind.Trip => OrderBegin(order, AutopilotState.Trip, TripSpec(forced: true), $"order {order.Id}: one logistics trip now, from the lull"),
            ScenarioOrderKind.Rebuff => OrderBegin(order, AutopilotState.Buff, GroundBuffSpec(start: true), $"order {order.Id}: her own pass now, in the ground, from her spell bar"),
            _ => OrderGoHome(order, "lull"),
        };
    }

    private ScenarioStatus OrderBegin(ScenarioOrder order, AutopilotState state, StationSpec spec, string why)
    {
        order.Spec = spec;
        order.Detail = why;
        return BeginRun(state, spec, why);
    }

    /// <summary>The bot and a quest start from home: the way home first; the order waits again and is taken at the known state at home.</summary>
    private ScenarioStatus OrderGoHome(ScenarioOrder order, string where)
    {
        order.State = ScenarioOrderState.Waiting;
        order.WentHome = true;
        order.Detail = "the way home first; it is taken at home";
        _log.Info(LogCategory, $"autopilot order: {order.Id} {Describe(order)}: at the {where} away from home: the way home first; the order is taken at the known state at home");
        return BeginRun(AutopilotState.Travel, HomeSpec(), $"order {order.Id}: the way home for {Describe(order)}");
    }

    /// <summary>A quest order at home: the start-quest path's checks again (the state moved since the ask), then the pack: over the load line or short of slots, a trip first; then the errand.</summary>
    private ScenarioStatus? OrderQuestAtHome(IGameSessionView session, ScenarioOrder order)
    {
        var key = order.Arg ?? string.Empty;
        if (QuestOrderRefusal(key) is { } refused)
        {
            OrderTake(order, "known-state");
            OrderEnd(order, ok: false, "refused:" + refused, $"check before at home: the start-quest path refuses {key} ({refused})");
            return null;
        }

        if (!order.TripFirst && TryLoadShares(out var free, out var burden) && (burden > _counters.BurdenTripPercent || free < _counters.MinFreeSlots))
        {
            // TODO(m6-cmd-quest-sellfirst): the sell-first rule of m6/14-sellfirst is not merged; until it is, the order's own check runs the trip before the errand
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot order: {order.Id} quest {key}: check before: burden {burden:0}% (trip line {_counters.BurdenTripPercent}%), {free} free slots (floor {_counters.MinFreeSlots}): a trip first, then the errand (TODO(m6-cmd-quest-sellfirst): the order's own check stands in for the sell-first rule)"));
            order.TripFirst = true;
            order.Detail = "a trip first: the pack is at the load line or short of slots";
            return BeginRun(AutopilotState.Trip, TripSpec(forced: true), $"order {order.Id}: the sale before the quest {key}");
        }

        OrderTake(order, "known-state");
        var quest = (TrophyQuests ?? QuestCatalog.All).First(q => q.Key == key);
        var started = StartErrand(QuestThen.Home, forced: false, key, JourneyPlans.FromHome, ladder: quest.Ladder is not null, $"order {order.Id}: the quest {key} now by the start-quest path, from home", order.Steps);
        if (started is null)
        {
            OrderEnd(order, ok: false, "failed:held", $"the errand's own check holds {key} (the line above names it)");
            return null;
        }

        order.Detail = $"the quest {key}'s errand runs";
        return started;
    }

    /// <summary>The quest station ended: a quest order of that key reads it as its check after.</summary>
    private void OrderQuestEnded(string key, bool done, string? why)
    {
        if (_order is { State: ScenarioOrderState.Taken, Kind: ScenarioOrderKind.Quest } order && order.Arg == key)
        {
            OrderEnd(order, done, done ? "task-done:quest:" + key : "failed:quest:" + key + ":" + JourneyVerdict.Token(why ?? "not-done"), done ? $"check after: the quest station ended with {key} done" : $"check after: the quest station ended without {key} done ({why ?? "not done"})");
        }
    }

    /// <summary>A station the order began ended: a buff order reads it as its check after.</summary>
    private void OrderStationEnded(bool ok, string? why)
    {
        if (_order is not { State: ScenarioOrderState.Taken } order || order.Spec is null || !ReferenceEquals(order.Spec, _spec))
        {
            return;
        }

        if (order.Kind is ScenarioOrderKind.Rebuff or ScenarioOrderKind.RebuffBot)
        {
            OrderEnd(order, ok, ok ? (order.Kind == ScenarioOrderKind.Rebuff ? "rebuff-done" : "bot-done") : "failed:buff:" + JourneyVerdict.Token(why ?? "failed"), ok ? $"check after: the {_spec!.Name} station ended well" : $"check after: the {_spec!.Name} station failed ({why ?? "failed"})");
        }
    }

    /// <summary>A logistics trip ended well (counted): a trip order's check after, at once (the trip's end may start an errand in the same step); a quest order's sale lets the quest be taken at home.</summary>
    private void OrderTripEnded(string fields)
    {
        if (_order is { State: ScenarioOrderState.Taken, Kind: ScenarioOrderKind.Trip } trip && TripsMade > trip.TripsAt)
        {
            OrderEnd(trip, ok: true, "trip-done", string.Create(CultureInfo.InvariantCulture, $"check after: the trip's station ended well (trips this run {trip.TripsAt} -> {TripsMade}): sold {Int(fields, "sold")}, bought {Int(fields, "bought")}, scrolls {Int(fields, "scrolls_bought")}"));
            return;
        }

        if (_order is { State: ScenarioOrderState.Waiting, TripFirst: true } order)
        {
            order.Detail = "the sale is made: the quest is taken at home";
            _log.Info(LogCategory, $"autopilot order: {order.Id} {Describe(order)}: check after: the sale before the errand ended; the errand is taken at home");
        }
    }

    /// <summary>
    /// The errand fight's lull (a trip order only; the quest station's own fight, the Trip state): the errand is cut where she stands with nothing in reach, the trip runs, and the errand runs again from home afterwards, from what she
    /// holds (<see cref="QuestResume"/>: a hunt-until counts the items held, so its count is kept). Null when there is no such lull.
    /// </summary>
    private ScenarioStatus? OrderAtErrandLull(IGameSessionView session)
    {
        if (_order is not { State: ScenarioOrderState.Waiting, Kind: ScenarioOrderKind.Trip } order || !_questTripActive || _run is not IStationSubFight station || station.SubFight is not { } fight)
        {
            return null;
        }

        if (fight.StopReason is not null || fight is FightScenario { PullRunning: true } || _stopAsked is not null || _deathSeen || session.State.Self.Travel != SelfTravel.Present || _hostiles is not { } watch || watch.InReach(_clock, out _))
        {
            return null;
        }

        var key = _questKey;
        var held = HuntHeld(key);
        var name = _spec!.Name;
        OrderTake(order, "errand-lull");
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot order: {order.Id} trip: check before: the errand {key}'s fight is in a lull at {Cell(session)} with {held} of its hunted items held: act: the errand is cut, the trip runs, and the errand runs again from home from what she holds"));
        var ladder = _questLadderErrand;
        FoldInFlight(clearFinal: false);
        _phase = RunPhase.Done;
        _context.Observer.StationEnded(name, false, _clock);
        _questTripActive = false;
        _questLadderErrand = false;
        _errandAfterTrip = ladder; // a ladder errand: the trip's end calls it again by its own rule, from where the trip ends (the path a sale-first errand takes)
        _resumeErrand = ladder ? null : (key, false, null); // any other errand: from home, at the next known state there
        return OrderBegin(order, AutopilotState.Trip, TripSpec(forced: true), $"order {order.Id}: one logistics trip now, from the errand {key}'s lull");
    }

    /// <summary>After an order's trip cut an errand that is not a ladder errand: at home, the errand runs again by its own start, from what she holds; on the ground, the way home first. Null when none was cut.</summary>
    private ScenarioStatus? ResumeCutErrand(bool home)
    {
        if (_resumeErrand is not { } cut || _order is { Ended: false, Kind: ScenarioOrderKind.Trip })
        {
            return null;
        }

        if (!home)
        {
            _log.Info(LogCategory, $"autopilot order: the errand {cut.Key} the order's trip cut runs again from home: the way home first");
            return BeginRun(AutopilotState.Travel, HomeSpec(), $"the way home for the errand {cut.Key} the order's trip cut");
        }

        _resumeErrand = null;
        var held = HuntHeld(cut.Key);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot order: the errand {cut.Key} the order's trip cut runs again from home: check before: {held} of its hunted items held (its hunt counts from there)"));
        return StartErrand(QuestThen.Home, forced: false, cut.Key, JourneyPlans.FromHome, cut.Ladder, $"the errand {cut.Key} again after the order's trip (its count kept: {held} held)", cut.Steps);
    }
}
