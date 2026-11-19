using System.Globalization;
using System.Runtime.CompilerServices;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>What <see cref="AutopilotQuestRule"/> reads (M6 item 5 Q5, D58): the rung's ground, the quests, the timers, the pack, the level, the book and where the body stands.</summary>
/// <param name="GroundKey">The rung's ground key (<see cref="LadderRung.GroundKey"/>), or null when the run has no ground rung (the den job, a quest rung, before the first check).</param>
/// <param name="Quests">The quest catalog (<see cref="QuestCatalog.All"/>; a test passes its own records).</param>
/// <param name="Timers">The character's quest timers (<see cref="JourneyContext.QuestTimersInUse"/>).</param>
/// <param name="Now">The time (UTC; the session's clock).</param>
/// <param name="Held">The count of a weenie class in the pack (stacks summed).</param>
/// <param name="Level">The character's level (its own property).</param>
/// <param name="KnowsLifestoneRecall">Lifestone Recall is in the book (the climber learns it at 16, D43): the way home no longer passes the Refuge.</param>
/// <param name="BodyCell">The cell the body stands in, or null when unread.</param>
/// <param name="Grounds">The grounds a body may stand on (<see cref="JourneyContext.Grounds"/>).</param>
/// <param name="Routes">The travel file (<see cref="JourneyContext.Routes"/>).</param>
public sealed record TrophyQuestInput(
    string? GroundKey,
    IReadOnlyList<Quest> Quests,
    QuestTimers Timers,
    DateTimeOffset Now,
    Func<uint, int> Held,
    int Level,
    bool KnowsLifestoneRecall,
    uint? BodyCell,
    IReadOnlyList<HuntingGround> Grounds,
    TravelRoutes Routes);

/// <summary>One trophy quest's decision (<see cref="AutopilotQuestRule.Calls"/>): run the quest station from <see cref="From"/>, or a named refusal (<see cref="From"/> null).</summary>
/// <param name="Quest">The trophy quest.</param>
/// <param name="Item">The trophy's weenie class (the hunt-until step's item).</param>
/// <param name="Count">The count the hunt-until step wants.</param>
/// <param name="Due">The quest is due by the timers.</param>
/// <param name="Held">The count held in the pack.</param>
/// <param name="From"><see cref="JourneyPlans.FromGround"/> or <see cref="JourneyPlans.FromHome"/> when the station runs, else null.</param>
/// <param name="Refusal">The refusal's name when it does not run (<c>level-out-of-band</c>, <c>not-due</c>, <c>not-held</c>, <c>no-route</c>), else null.</param>
/// <param name="Why">The decision in one clause, with its inputs.</param>
public sealed record TrophyQuestCall(Quest Quest, uint Item, int Count, bool Due, int Held, string? From, string? Refusal, string Why)
{
    /// <summary>True when the quest station runs.</summary>
    public bool Runs => From is not null;

    /// <summary>The decision as one log line: <c>autopilot quest: key=tusk-female due=yes held=1 from=ground why=...</c>.</summary>
    public string Line => string.Create(CultureInfo.InvariantCulture, $"autopilot quest: key={Quest.Key} due={(Due ? "yes" : "no")} held={Held} from={From ?? "none"} why={(Refusal is null ? string.Empty : Refusal + ": ")}{Why}");
}

/// <summary>One ladder errand's decision (<see cref="AutopilotQuestRule.LadderCalls"/>; D118): run the quest station from home, or a named refusal (<see cref="From"/> null).</summary>
/// <param name="Quest">The quest (it has a <see cref="Quest.Ladder"/>).</param>
/// <param name="Due">The quest is due by the timers.</param>
/// <param name="From"><see cref="JourneyPlans.FromHome"/> when the station runs, else null.</param>
/// <param name="Refusal">The refusal's name when it does not run (<c>level-out-of-band</c>, <c>done-once</c>, <c>not-due</c>, <c>failed-this-run</c>, <c>no-route</c>), else null.</param>
/// <param name="Why">The decision in one clause, with its inputs.</param>
/// <param name="Missed">True when the level has passed the band and the quest was never recorded done (<c>TODO(m6-ladder-errand-missed)</c>): it is not run, a late attempt would walk into a refused portal.</param>
public sealed record LadderErrandCall(Quest Quest, bool Due, string? From, string? Refusal, string Why, bool Missed = false)
{
    /// <summary>True when the quest station runs.</summary>
    public bool Runs => From is not null;

    /// <summary>The decision as one log line, the trophy call's shape: <c>autopilot quest: key=atlan-wand due=yes errand=ladder from=home why=...</c>.</summary>
    public string Line => string.Create(CultureInfo.InvariantCulture, $"autopilot quest: key={Quest.Key} due={(Due ? "yes" : "no")} errand=ladder from={From ?? "none"} why={(Refusal is null ? string.Empty : Refusal + ": ")}{Why}");
}

/// <summary>What is known of the ladder errands across the runs of one session: which missed bands have been named, so <c>TODO(m6-ladder-errand-missed)</c> is one line and not one a run.</summary>
public sealed class LadderErrandMemory
{
    private static readonly ConditionalWeakTable<GameState, LadderErrandMemory> PerSession = new();
    private readonly HashSet<string> _missed = new(StringComparer.Ordinal);

    /// <summary>The memory of the session behind <paramref name="state"/>.</summary>
    /// <param name="state">The session's read model.</param>
    public static LadderErrandMemory For(GameState state) => PerSession.GetOrCreateValue(state ?? throw new ArgumentNullException(nameof(state)));

    /// <summary>True the first time the quest's missed band is noted this session.</summary>
    /// <param name="key">The quest's key.</param>
    public bool FirstMiss(string key) => _missed.Add(key);
}

/// <summary>
/// M6 item 5 Q5 (D58), pure: the level up job's trophy errand. A trophy quest is a catalog quest with a <c>hunt-until</c> step whose <see cref="QuestStep.Ground"/> is the rung's ground (the link lives in the quest file, D26; the ladder's
/// ground rungs carry no quests). From the timers, the pack, the level, the book and where the body stands it gives:
/// <list type="bullet">
/// <item>(a) the fight's want set (<see cref="Want"/>): the trophies of this ground whose quest is due, whose level band holds the character and whose count is not held yet (the pick-up is gated once a kind per 20 h by the item's own
/// quest string, so a second one is never wanted: facts.md section 1);</item>
/// <item>(b) the call (<see cref="Calls"/>): a due trophy held runs the quest station <c>quest=&lt;key&gt;</c> with <c>from=ground</c> when the body stands on the rung's ground, the book lacks Lifestone Recall and the ground's way home
/// (<see cref="RungRoute.Decide"/>) starts with the exit hop into the turn-in place's travel place (the island's Refuge: the turn-in sits between the island exit and 42852, map section 3); else <c>from=home</c> (a trip of its own:
/// the station goes home first, then by the Town Network to the turn-in place);</item>
/// <item>(c) otherwise a named refusal: <c>level-out-of-band</c>, <c>not-due</c>, <c>not-held</c>, or <c>no-route</c> (the turn-in place is in no travel place, or the body stands on no ground and in no travel place).</item>
/// </list>
/// One <see cref="TrophyQuestCall.Line"/> per decision. No ground, NPC or quest fact lives here (D26): each is the quest file's, the profile's or the travel file's.
/// <para>M6 item 8 (D118): the second kind of call, <see cref="LadderCalls"/>: a quest whose file has a <c>ladder</c> object runs from home at its band of levels, unprompted. A ladder errand is never a trophy (<see cref="Trophies"/> leaves it out), so a hunt-until step in one
/// wants nothing of the fight and no quest gets two calls.</para>
/// </summary>
public static class AutopilotQuestRule
{
    /// <summary>The trophy quests of <paramref name="groundKey"/>: each quest with a hunt-until step on that ground and an item, with that step.</summary>
    /// <param name="groundKey">The rung's ground key, or null (none).</param>
    /// <param name="quests">The catalog.</param>
    public static IReadOnlyList<(Quest Quest, QuestStep Hunt)> Trophies(string? groundKey, IReadOnlyList<Quest> quests)
    {
        ArgumentNullException.ThrowIfNull(quests);
        if (groundKey is null)
        {
            return [];
        }

        var found = new List<(Quest, QuestStep)>();
        foreach (var quest in quests)
        {
            if (quest.Ladder is null && quest.Steps.FirstOrDefault(s => s.Kind == QuestStepKind.HuntUntil && string.Equals(s.Ground, groundKey, StringComparison.Ordinal) && s.Item?.Wcid is not null) is { } hunt)
            {
                found.Add((quest, hunt));
            }
        }

        return found;
    }

    /// <summary>(a) The fight's want set: the items of this ground's trophy quests that are due, in the level's band and not held to their count (distinct, in catalog order).</summary>
    /// <param name="input">The inputs.</param>
    public static IReadOnlyList<uint> Want(TrophyQuestInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var want = new List<uint>();
        foreach (var (quest, hunt) in Trophies(input.GroundKey, input.Quests))
        {
            var item = hunt.Item!.Wcid!.Value;
            if ((quest.Level?.Fits(input.Level) ?? true) && input.Timers.IsDue(quest, input.Now) && input.Held(item) < (hunt.Count ?? 1) && !want.Contains(item))
            {
                want.Add(item);
            }
        }

        return want;
    }

    /// <summary>
    /// M6 audit (the owner's decision of 2026-11-19: every tusk when it is due): the carriers of the wanted trophies of <paramref name="groundKey"/>, from each quest file's hunt-until <c>carriers</c> (the creature whose create list holds the
    /// tusk; on the Tusker Barracks 5 of its 54 Tusker Guards, wcid 22593 among 49 of wcid 1629 by the pinned world SQL's landblock_instance rows of 0x5A48), distinct, in catalog order; empty when none is wanted or no file names one.
    /// </summary>
    /// <param name="groundKey">The rung's ground key, or null.</param>
    /// <param name="quests">The catalog.</param>
    /// <param name="want">The fight's want set (<see cref="Want"/>).</param>
    public static IReadOnlyList<Scenarios.WantCarrier> Carriers(string? groundKey, IReadOnlyList<Quest> quests, IReadOnlyList<uint> want)
    {
        ArgumentNullException.ThrowIfNull(want);
        var list = new List<Scenarios.WantCarrier>();
        foreach (var (_, hunt) in Trophies(groundKey, quests))
        {
            var item = hunt.Item!.Wcid!.Value;
            if (!want.Contains(item) || hunt.Carriers is not { } carriers)
            {
                continue;
            }

            foreach (var c in carriers)
            {
                if (!list.Exists(x => x.Creature == c.Wcid && x.Item == item))
                {
                    list.Add(new Scenarios.WantCarrier(c.Wcid, item, c.Shade, hunt.Item.Name));
                }
            }
        }

        return list;
    }

    /// <summary>(b) and (c): the decision for each trophy quest of the ground, in catalog order (empty when the ground has none).</summary>
    /// <param name="input">The inputs.</param>
    public static IReadOnlyList<TrophyQuestCall> Calls(TrophyQuestInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var calls = Trophies(input.GroundKey, input.Quests).Select(t => Decide(input, t.Quest, t.Hunt)).ToList();

        // M6 7 lead (2026-11-07, live: the Female Tusk taken on the job to the Burrow, then the run on the Lodge's ground never turned it in): a trophy held to its count is called wherever it was hunted.
        foreach (var ground in input.Quests.SelectMany(q => q.Steps).Where(s => s.Kind == QuestStepKind.HuntUntil && s.Ground is not null).Select(s => s.Ground!).Distinct(StringComparer.Ordinal))
        {
            if (string.Equals(ground, input.GroundKey, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var (quest, hunt) in Trophies(ground, input.Quests))
            {
                if (hunt.Item?.Wcid is { } item && input.Held(item) >= (hunt.Count ?? 1) && !calls.Any(c => c.Quest.Key == quest.Key))
                {
                    calls.Add(Decide(input with { GroundKey = ground }, quest, hunt));
                }
            }
        }

        return calls;
    }

    /// <summary>The first trophy quest that runs, or null.</summary>
    /// <param name="calls">The decisions (<see cref="Calls"/>).</param>
    public static TrophyQuestCall? First(IReadOnlyList<TrophyQuestCall> calls) => (calls ?? throw new ArgumentNullException(nameof(calls))).FirstOrDefault(c => c.Runs);

    /// <summary>
    /// M6 item 8 (D118): the decision for each quest that has a <c>ladder</c> object, the band that closes first first (then catalog order), so a late climber takes the errand she is about to lose. It runs <c>from=home</c> when the level is in the band, it is due by the timers (and, with
    /// <c>once</c>, was never recorded done), it has not failed this run and the body's place and the first place of the quest give a route; else a named refusal.
    /// </summary>
    /// <param name="input">The inputs (<see cref="TrophyQuestInput.GroundKey"/> is not read).</param>
    /// <param name="failed">The quests that failed this run: key to the failure's stop name.</param>
    public static IReadOnlyList<LadderErrandCall> LadderCalls(TrophyQuestInput input, IReadOnlyDictionary<string, string> failed)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(failed);
        return [.. input.Quests.Where(q => q.Ladder is not null).OrderBy(q => q.Ladder!.BeforeLevel).Select(q => DecideLadder(input, q, failed))];
    }

    /// <summary>The first ladder errand that runs, or null.</summary>
    /// <param name="calls">The decisions (<see cref="LadderCalls"/>).</param>
    public static LadderErrandCall? FirstLadder(IReadOnlyList<LadderErrandCall> calls) => (calls ?? throw new ArgumentNullException(nameof(calls))).FirstOrDefault(c => c.Runs);

    /// <summary>The place a quest first goes to: its first go step with a place, else its start.</summary>
    /// <param name="quest">The quest.</param>
    public static QuestPlace FirstPlace(Quest quest)
    {
        ArgumentNullException.ThrowIfNull(quest);
        return quest.Steps.FirstOrDefault(s => s.Kind == QuestStepKind.Go && s.Place is not null)?.Place ?? quest.Start;
    }

    /// <summary>The turn-in place: the first go step with a place after the hunt-until step, else the quest's start.</summary>
    /// <param name="quest">The quest.</param>
    /// <param name="hunt">Its hunt-until step.</param>
    public static QuestPlace TurnInPlace(Quest quest, QuestStep hunt)
    {
        ArgumentNullException.ThrowIfNull(quest);
        ArgumentNullException.ThrowIfNull(hunt);
        return quest.Steps.Where(s => s.Index > hunt.Index).FirstOrDefault(s => s.Kind == QuestStepKind.Go && s.Place is not null)?.Place ?? quest.Start;
    }

    private static TrophyQuestCall Decide(TrophyQuestInput input, Quest quest, QuestStep hunt)
    {
        var c = CultureInfo.InvariantCulture;
        var item = hunt.Item!.Wcid!.Value;
        var count = hunt.Count ?? 1;
        var held = input.Held(item);
        var due = input.Timers.IsDue(quest, input.Now);
        TrophyQuestCall Refuse(string name, string why) => new(quest, item, count, due, held, null, name, why);

        if (quest.Level is { } band && !band.Fits(input.Level))
        {
            return Refuse("level-out-of-band", string.Create(c, $"level {input.Level} is out of the quest's band {band}"));
        }

        if (!due)
        {
            var next = input.Timers.NextDue(quest);
            return Refuse("not-due", next is { } n ? string.Create(c, $"next due {n:yyyy-MM-ddTHH:mm:ssZ} (the timer {quest.TimerSeconds?.ToString(c) ?? "none"} s after the last turn-in)") : "done once and recorded");
        }

        if (held < count)
        {
            return Refuse("not-held", string.Create(c, $"{held} of {count} {hunt.Item.Name} ({item}) in the pack: the fight wants it on {hunt.Ground}"));
        }

        var place = TurnInPlace(quest, hunt);
        var goal = place.Cell >> 16;
        var goalPlace = TownNetworkTrip.PlaceKeyOf(goal, input.Routes);
        if (goalPlace is null)
        {
            return Refuse("no-route", string.Create(c, $"the turn-in place {place.Name} (0x{place.Cell:X8}) is in no travel place of {TravelRoutes.FileName}"));
        }

        var ground = input.Grounds.FirstOrDefault(g => string.Equals(g.Key, hunt.Ground, StringComparison.Ordinal));
        var onGround = input.BodyCell is { } cell && ground is not null && ground.InLandblocks(cell);
        if (!onGround)
        {
            var bodyPlace = input.BodyCell is { } at ? TownNetworkTrip.PlaceKeyOf(at >> 16, input.Routes) : null;
            var onAny = input.BodyCell is { } at2 && input.Grounds.Any(g => g.InLandblocks(at2));
            if (bodyPlace is null && !onAny)
            {
                return Refuse("no-route", input.BodyCell is { } at3 ? string.Create(c, $"the body stands in cell 0x{at3:X8}, on no ground and in no travel place: the station has no way from there") : "the body's position is unread");
            }

            return new(quest, item, count, due, held, JourneyPlans.FromHome, null, string.Create(c, $"the body stands off {hunt.Ground} ({bodyPlace ?? "on another ground"}): a trip of its own to {place.Name} in {goalPlace}"));
        }

        if (input.KnowsLifestoneRecall)
        {
            return new(quest, item, count, due, held, JourneyPlans.FromHome, null, string.Create(c, $"Lifestone Recall is in the book, so the way home from {hunt.Ground} does not pass {goalPlace} (D58): a trip of its own from Holtburg by the Town Network to {place.Name}"));
        }

        var leads = ExitLeadsTo(ground!, input.BodyCell!.Value, input.Level, goal, input.Routes, out var plan);
        return leads
            ? new(quest, item, count, due, held, JourneyPlans.FromGround, null, string.Create(c, $"below Lifestone Recall the way home from {hunt.Ground} (route {plan}) starts with its exit into {goalPlace}: the turn-in to {place.Name} sits between the exit and the way home (D58)"))
            : new(quest, item, count, due, held, JourneyPlans.FromHome, null, string.Create(c, $"the way home from {hunt.Ground} (route {plan}) does not start with an exit into {goalPlace}: a trip of its own to {place.Name}"));
    }

    private static LadderErrandCall DecideLadder(TrophyQuestInput input, Quest quest, IReadOnlyDictionary<string, string> failed)
    {
        var c = CultureInfo.InvariantCulture;
        var band = quest.Ladder!;
        var record = input.Timers.Get(quest.Key);
        var due = input.Timers.IsDue(quest, input.Now);
        LadderErrandCall Refuse(string name, string why, bool missed = false) => new(quest, due, null, name, why, missed);

        if (!band.Fits(input.Level))
        {
            var missed = band.Passed(input.Level) && record is null;
            return Refuse("level-out-of-band", string.Create(c, $"level {input.Level} is out of the ladder band {band}{(missed ? ", passed with the quest undone" : string.Empty)}"), missed);
        }

        if (band.Once && record is not null)
        {
            return Refuse("done-once", string.Create(c, $"a one-time climbing errand, recorded done {record.Completions} time(s), last {record.LastCompleted:yyyy-MM-ddTHH:mm:ssZ} ({record.How})"));
        }

        if (!due)
        {
            var next = input.Timers.NextDue(quest);
            return Refuse("not-due", next is { } n ? string.Create(c, $"next due {n:yyyy-MM-ddTHH:mm:ssZ} (the timer {quest.TimerSeconds?.ToString(c) ?? "none"} s after the last completion)") : "done once and recorded");
        }

        if (failed.TryGetValue(quest.Key, out var stop))
        {
            return Refuse("failed-this-run", $"it failed this run ({stop}): not called again until the next run");
        }

        var place = FirstPlace(quest);
        var goalPlace = TownNetworkTrip.PlaceKeyOf(place.Cell >> 16, input.Routes);
        if (goalPlace is null)
        {
            return Refuse("no-route", string.Create(c, $"the quest's first place {place.Name} (0x{place.Cell:X8}) is in no travel place of {TravelRoutes.FileName}"));
        }

        if (input.BodyCell is not { } at)
        {
            return Refuse("no-route", "the body's position is unread");
        }

        var bodyPlace = TownNetworkTrip.PlaceKeyOf(at >> 16, input.Routes);
        var ground = input.Grounds.FirstOrDefault(g => g.InLandblocks(at));
        if (bodyPlace is null && ground is null)
        {
            return Refuse("no-route", string.Create(c, $"the body stands in cell 0x{at:X8}, on no ground and in no travel place: the station has no way from there"));
        }

        return new(quest, due, JourneyPlans.FromHome, null, string.Create(c, $"level {input.Level} is in the ladder band {band}; the body stands {(bodyPlace is null ? "on " + ground!.Key : "in " + bodyPlace)}: a trip of its own from home to {place.Name} in {goalPlace} (D118)"));
    }

    /// <summary>True when <see cref="RungRoute.Decide"/>'s home plan from <paramref name="ground"/> (below Lifestone Recall) starts with the ground's exit hop and that portal leads into <paramref name="goal"/>'s travel place: the quest station's own test (<c>QuestRun.ExitHopTo</c>).</summary>
    private static bool ExitLeadsTo(HuntingGround ground, uint cell, int level, uint goal, TravelRoutes routes, out string plan)
    {
        var landblock = cell >> 16;
        var input = new RungRouteInput(ground, RungDirection.Home, RungWhere.AtGround, TownNetworkTrip.PlaceKeyOf(landblock, routes) ?? RungRoute.Hex(landblock), level, false, false, false, PortalTie.Unknown, false);
        var decided = RungRoute.Decide(input, routes);
        plan = decided.RouteKey;
        var first = decided.Legs.Count == 0 ? null : decided.Legs[0];
        return first is { Kind: RungLegKind.Hop, Portal: { } p } && first.Label.StartsWith("exit:", StringComparison.Ordinal) && SamePlace(p.DestLandblock, goal, routes);
    }

    private static bool SamePlace(uint landblock, uint goal, TravelRoutes routes) =>
        landblock == goal || (TownNetworkTrip.PlaceKeyOf(landblock, routes) is { } a && a == TownNetworkTrip.PlaceKeyOf(goal, routes));
}
