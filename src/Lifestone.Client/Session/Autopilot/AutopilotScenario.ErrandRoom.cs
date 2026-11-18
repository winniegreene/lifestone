using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 14 sell first (the owner's ruling, 2026-11-17 ~12:32 UTC: she must sell before she goes on this quest; live ladder58 12:23 UTC: the Atlan wand's start quest left home at burden 21,674 of 21,750 with 0 free main-pack slots, and the
/// cave hunt left loot and wanted motes for lack of room). An errand from home (the start quest, <c>--autopilot-start-quest</c>; a ladder errand, D118; a trophy's turn-in from home) whose steps gain items has her pack read first, by
/// <see cref="ErrandRoomRule"/> over her live state and the quest's own steps.
/// <list type="bullet">
/// <item><b>Check before</b>, one line (<c>autopilot quest: key=&lt;key&gt;: errand room: check before: ...</c>): her burden, the load line, her free main-pack slots, the stock lines, and what the errand gains at its peak.</item>
/// <item><b>Act</b>: when a line holds, the logistics station runs first (<c>trigger=errand-room</c>, <see cref="JourneyPlans.TripErrandRoom"/>: the sale by the existing sell rules, the scroll buys, the supplies) and the errand is asked
/// again when it ends well (<see cref="ErrandAfterRoom"/>), from home. At the fight's trip (the way home the fight stopped for) the trip it stopped for is that sale: the errand waits for its end, as <c>m6-errand-after-sale</c>'s.</item>
/// <item><b>Check after</b>, one line (<c>errand room: check after the sale trip</c>): a sale trip ended with no kill since, so the errand goes with the room she has; a line that still holds is named (a warning), never a second trip.</item>
/// </list>
/// An errand whose steps gain nothing (a talk, a kill task's arm or turn-in) is not read; a pack not read yet does not hold an errand (named); the villa's writ fetch runs at a town trip's boundary, right after its sale, and is not read.
/// </summary>
public sealed partial class AutopilotScenario
{
    /// <summary>The logistics station's spec for the sale before an errand from home (<c>trigger=errand-room</c>).</summary>
    public static StationSpec ErrandRoomTripSpec { get; } = new(LogisticsStation.StationName, StationKinds.Logistics, null, 600, new Dictionary<string, string>(StringComparer.Ordinal) { [JourneyPlans.TripArgument] = JourneyPlans.TripErrandRoom });

    /// <summary>The free main-pack slots a scripted test holds; null reads the live ones (<see cref="Quartermaster.PackView.MainPackFree"/>).</summary>
    internal int? FreeSlotsForTest { get; set; }

    /// <summary>The errand a sale trip was made for (<see cref="ErrandRoomTripSpec"/>), asked again when the trip ends well; null when none waits.</summary>
    private ErrandCall? _errandRoomPending;

    /// <summary>The run's kills when the last logistics trip ended well (-1: none yet): a trip ended and no kill since means the sale was just made (the check after).</summary>
    private int _tripEndedAtKills = -1;

    private readonly List<uint> _roomScratch = [];

    /// <summary>An errand's call, kept while its sale is made.</summary>
    private sealed record ErrandCall(QuestThen Then, bool Forced, string Key, string From, bool Ladder, string Why, string? Steps);

    /// <summary>
    /// The room check before an errand (see the class remarks): true when the sale goes first, with <paramref name="status"/> the trip begun here (from home), or null at the fight's trip (the caller's trip is the sale and its end asks
    /// the errand again, <see cref="ErrandAfterTrip"/>). False: the errand goes now.
    /// </summary>
    private bool ErrandSellsFirst(QuestThen then, bool forced, string key, string from, bool ladder, string why, string? steps, out ScenarioStatus? status)
    {
        status = null;
        if (then == QuestThen.Final || from != JourneyPlans.FromHome || _villaFetchKey == key || _session is not { } session
            || (TrophyQuests ?? QuestCatalog.All).FirstOrDefault(q => q.Key == key) is not { } quest)
        {
            return false;
        }

        var state = session.State;
        var gain = ErrandRoomRule.GainOf(quest, steps, w => GearInventory.CountOf(state, _roomScratch, w), w => HeldCopy(state, w));
        if (!gain.Gains)
        {
            return false; // no step gains an item or fights: the pack is not the errand's
        }

        var prefix = $"autopilot quest: key={key}: errand room:";
        if (!state.Self.Inventory.IsKnown && FreeSlotsForTest is null)
        {
            _log.Warn(LogCategory, $"{prefix} check before: the pack is not read yet: the errand goes (a load that cannot be read does not hold an errand; m6-errand-room-unread)");
            return false;
        }

        var load = LootContext.Of(state);
        var (burden, capacity) = (load.Burden, load.Capacity);
        if (BurdenForTest is { } percent)
        {
            capacity = capacity > 0 ? capacity : 10_000;
            burden = (int)(percent * capacity / 100);
        }

        var free = FreeSlotsForTest ?? Quartermaster.PackView.MainPackFree(state);
        var lineGround = quest.Steps.Where(s => s.Kind == QuestStepKind.HuntUntil && s.Ground is not null).Select(s => GroundCatalog.TryGet(s.Ground!)).FirstOrDefault(g => g is not null) ?? _ground;
        var loadLine = MageLootThresholds.LoadLineOf(lineGround);
        var stock = _counters.Check(state) is { Cause: LogisticsCause.StockLow } low ? low.Reason : null;
        var verdict = ErrandRoomRule.Decide(gain, burden, capacity, loadLine, free, stock);

        if (_tripEndedAtKills == _total.Kills + _tally.Kills)
        {
            // the check after: a sale trip ended and she has killed nothing since, so a second trip would sell nothing more (never a loop)
            var line = string.Create(CultureInfo.InvariantCulture, $"{prefix} check after the sale trip (no kill since it ended): {verdict.Why}");
            if (verdict.SaleFirst)
            {
                _log.Warn(LogCategory, line + "; the sale was just made, so the errand goes with the room she has (no second trip for it: m6-errand-room-after-sale)");
            }
            else
            {
                _log.Info(LogCategory, line);
            }

            return false;
        }

        if (!verdict.SaleFirst)
        {
            _log.Info(LogCategory, $"{prefix} check before: {verdict.Why}");
            return false;
        }

        _log.Info(LogCategory, $"{prefix} check before: {verdict.Why} (trigger={ErrandRoomRule.Trigger}; the owner 2026-11-17: she sells before she goes)");
        if (then == QuestThen.Trip)
        {
            _errandAfterTrip = true; // the trip the fight stopped for is the sale; TripEnded asks the errand again from where it ends
            _log.Info(LogCategory, $"{prefix} act: the trip the fight stopped for sells first; the errand from where it ends");
            return true;
        }

        _errandRoomPending = new ErrandCall(then, forced, key, from, ladder, why, steps);
        _log.Info(LogCategory, $"{prefix} act: the logistics station first (trigger={ErrandRoomRule.Trigger}), then the errand from home");
        status = BeginRun(AutopilotState.Trip, ErrandRoomTripSpec, $"{why}: the sale before the errand {key} (trigger={ErrandRoomRule.Trigger})");
        return true;
    }

    /// <summary>The sale trip an errand waited for ended well (<see cref="ErrandSellsFirst"/>): the errand is asked again, with its own checks; null when none waited or its check holds it (the line names it).</summary>
    private ScenarioStatus? ErrandAfterRoom()
    {
        if (_errandRoomPending is not { } call)
        {
            return null;
        }

        _errandRoomPending = null;
        var started = StartErrand(call.Then, call.Forced, call.Key, call.From, call.Ladder, $"after the sale trip ({ErrandRoomRule.Trigger}): {call.Why}", call.Steps);
        if (started is null)
        {
            _log.Info(LogCategory, $"autopilot quest: key={call.Key}: errand room: after the sale trip the errand's own check holds it (the line above names it); the ordinary ways home ask it again");
        }

        return started;
    }

    /// <summary>The sale trip did not end well or made no visit: the errand that waited for it is not asked by it (the ordinary ways home ask it again).</summary>
    private void ErrandRoomDropped(string what)
    {
        if (_errandRoomPending is { } call)
        {
            _errandRoomPending = null;
            _log.Warn(LogCategory, $"autopilot quest: key={call.Key}: errand room: the sale trip {what}: the errand is not started by it; the ordinary ways home ask it again");
        }
    }

    /// <summary>A held copy of weenie <paramref name="wcid"/>: its burden each (the stack's burden over its size) and its stack size at most; null when she holds none.</summary>
    private (int BurdenEach, int MaxStack)? HeldCopy(GameState state, uint wcid)
    {
        GearInventory.Collect(state, _roomScratch);
        foreach (var guid in _roomScratch)
        {
            if (state.Objects.TryGet(guid, out var item) && item.Weenie.WeenieClassId == wcid)
            {
                var stack = Math.Max(1, GearInventory.StackOf(state, item));
                return ((item.Weenie.Burden ?? 0) / stack, item.Weenie.MaxStackSize is { } max and > 1 ? max : 1);
            }
        }

        return null;
    }
}
