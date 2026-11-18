using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M5 7 P7o (the item-end gate's <c>storeys_visited 1 under 2</c>, D44 c2b): with <c>--fight-storeys-min</c> 2 or more (the mage journey's den fight tours two storeys, <see cref="Journey.JourneyPlans.MageDenFightArguments"/>), the kill quota
/// alone does not win the fight while fewer storeys than the minimum are visited and the tour can reach another. D88's self-defence (F4) drops the tour's walk for every attacker, so three attackers on the arrival's storey had filled the quota
/// there. The fight goes on: it still fights back every attacker, the tour's walk is taken up after each kill (the pick prefers the next storey's targets, <see cref="GroundTour.WantsToLeave"/>), and it is won at the first kill after the
/// storeys are met; <c>--fight-seconds</c> bounds it as before, by name. With no other storey reachable the fight ends as before, with one named line. An open-ended fight (the autopilot's, no kill quota) and a fight with a minimum of 1
/// are not touched.
/// <para><b>M6 7 D (<c>m6-gate-den-storey-race</c>): the crossing outlives the budget.</b> Five gates' den fights (M6 3, 4 rounds 3 and 4, 5, 6): the walk from the arrival's floor to z -6 is about 20 s uninterrupted, but the floor between holds 18 to 21
/// attackers at about 13 s a kill, every one of them a real attacker (the only other pick in those fights is the first), so the body crossed at 233 to 290 s, or not by 300 s (M6 5), and "the next kill" fell past 300 s (M6 4 round 3). The time goes to D88,
/// which stands: the fight still kills what attacks it and walks when no attacker is left in reach. What changed is that the seconds budget no longer decides an owed crossing: with the quota met and the win held for the storeys
/// (<see cref="StoreysHoldTheWin"/>), the fight goes on past <see cref="FightOptions.FailSeconds"/> while it progresses (a kill, or the body in a cell of the fight it had not stood in) within <see cref="CrossingStallSeconds"/>, at most
/// <see cref="CrossingBudgetTimes"/> times the budget; each end is named. A leg cut by an attacker leaves the body where it got to, and the next leg starts there; no walk toward an attacker gives that ground back (<see cref="CrossingOwed"/>).</para>
/// </summary>
public sealed partial class FightScenario
{
    private int _storeyHoldKills = -1; // the kills when the win was last held for the storeys (-1: never held)
    private int _storeyHoldLogged = -1; // the kill count the hold last logged
    private bool _storeysMetLogged;
    private bool _storeysUnreachableLogged;

    private readonly HashSet<uint> _crossingCells = []; // the cells the body stood in during this fight (bounded by the ground's cells)
    private int _crossingKills;
    private double _crossingProgressAt = double.NaN; // the clock of the last kill or new cell (NaN: not yet)
    private bool _crossingOvertimeLogged;
    private string _crossingEndNote = string.Empty; // the failure's tail when the overtime ended it

    /// <summary>M6 7 D: the longest quiet spell an owed crossing keeps its overtime through (Lifestone's own: about one and a half times the 39.1 s, the longest gap between two kills in the five gates' den fights).</summary>
    public const double CrossingStallSeconds = 60;

    /// <summary>M6 7 D: the owed crossing's hard bound as a multiple of <see cref="FightOptions.FailSeconds"/> (Lifestone's own: 300 s becomes at most 600 s, inside the den-fight station's limit of 945 s).</summary>
    public const double CrossingBudgetTimes = 2;

    /// <summary>M6 7 D: the line a fight logs once when its seconds run out with the crossing owed and still progressing.</summary>
    public const string CrossingOvertimeLine = "the fight's seconds are up with the storeys owed and the crossing progressing";

    /// <summary>The line a fight that cannot reach another storey logs once when it ends with fewer storeys than the minimum.</summary>
    public const string StoreysUnmetLine = "storeys unmet: no other storey";

    /// <summary>The line a fight logs when its kill quota is met but its storeys are not (once per kill count).</summary>
    public const string StoreysHoldLine = "the fight is not won yet: the storeys are not met";

    /// <summary>
    /// D44 c2b: true while the kill quota's win waits for the storeys: fewer than <see cref="FightOptions.StoreysMin"/> storeys visited by the BODY (<c>storeys_visited=</c>, the field the gate judges, never the tour's count, which a kill made from range on another storey raises) and the tour reaches another storey; or the storeys met
    /// by the walk with no kill made since (the win comes with the first kill there). Each decision is logged.
    /// </summary>
    private bool StoreysHoldTheWin(IGameSessionView session, double clock)
    {
        if (_options.StoreysMin < 2 || _options.ArenaHere || _walk?.Tour is not GroundTour tour)
        {
            return false;
        }

        var visited = _storeysVisited; // the body's count, the one the gate judges (storeys_visited=); a kill on another storey made from range does not count (the tour's own count does)
        if (visited >= _options.StoreysMin)
        {
            if (_storeyHoldKills < 0 || _tally.Kills > _storeyHoldKills)
            {
                if (_storeyHoldKills >= 0)
                {
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"storeys met: {visited} of {_options.StoreysMin} storeys visited and kill {_tally.Kills} made after them: the fight is won"));
                }

                return false;
            }

            if (!_storeysMetLogged)
            {
                _storeysMetLogged = true;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"storeys met by the walk: the body visited {visited} of {_options.StoreysMin} storeys (tour {tour.StoreysVisited}); the fight is won at the next kill"));
            }

            return true;
        }

        var here = session.State.Self.Position?.CellId ?? 0;
        var next = tour.OtherStorey(here, clock);
        if (next < 0)
        {
            if (!_storeysUnreachableLogged)
            {
                _storeysUnreachableLogged = true;
                _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{StoreysUnmetLine}: kills={_tally.Kills} of {_options.Kills} with {visited} of {_options.StoreysMin} storeys visited, and the tour reaches no other storey with a standing hunted slot: the fight ends as before (storeys_visited stays under the minimum)"));
            }

            return false;
        }

        _storeyHoldKills = _tally.Kills;
        if (_storeyHoldLogged != _tally.Kills)
        {
            _storeyHoldLogged = _tally.Kills;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{StoreysHoldLine}: kills={_tally.Kills} of {_options.Kills} with {visited} of {_options.StoreysMin} storeys visited by the body (tour {tour.StoreysVisited}); the tour's walk goes on to storey '{_ground.Storeys[next].Name}' and attackers are still fought back (D88), within {_options.FailSeconds:0.#} s or, while it progresses, {_options.FailSeconds * CrossingBudgetTimes:0.#} s"));
        }

        return true;
    }

    /// <summary>M6 7 D: every fight frame of a fight that asks for 2 or more storeys: a kill or a cell the body had not stood in is progress.</summary>
    private void NoteCrossingProgress(uint cell, double clock)
    {
        if (_options.StoreysMin < 2)
        {
            return;
        }

        var newCell = cell != 0 && _crossingCells.Add(cell);
        if (newCell || _tally.Kills != _crossingKills || double.IsNaN(_crossingProgressAt))
        {
            _crossingKills = _tally.Kills;
            _crossingProgressAt = clock;
        }
    }

    /// <summary>
    /// M6 7 D (<c>m6-gate-den-storey-race</c>): read when <see cref="FightOptions.FailSeconds"/> have passed: true while the quota is met, the win is held for the storeys and the crossing progressed within <see cref="CrossingStallSeconds"/>, up to
    /// <see cref="CrossingBudgetTimes"/> times the budget. The overtime's start and its end are one line each; a fight under its quota fails at the budget as before.
    /// </summary>
    private bool CrossingOutlivesTheBudget(IGameSessionView session, double clock)
    {
        if (_options.UnlimitedKillCount || _tally.Kills < _options.Kills || !StoreysHoldTheWin(session, clock))
        {
            return false;
        }

        var seconds = clock - _fightStart;
        var quiet = clock - _crossingProgressAt;
        var bound = _options.FailSeconds * CrossingBudgetTimes;
        if (seconds > bound || !(quiet <= CrossingStallSeconds))
        {
            var why = seconds > bound ? string.Create(CultureInfo.InvariantCulture, $"the bound {bound:0.#} s ({CrossingBudgetTimes:0.#} x --fight-seconds) passed") : string.Create(CultureInfo.InvariantCulture, $"no kill and no new cell for {CrossingStallSeconds:0.#} s");
            _crossingEndNote = string.Create(CultureInfo.InvariantCulture, $"; the owed crossing's overtime ended at {seconds:0} s: {why}");
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"storeys: the owed crossing's overtime ends at {seconds:0} s with {_storeysVisited} of {_options.StoreysMin} storeys visited and kills={_tally.Kills}: {why}"));
            return false;
        }

        if (!_crossingOvertimeLogged)
        {
            _crossingOvertimeLogged = true;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{CrossingOvertimeLine}: {seconds:0} s with kills={_tally.Kills} of {_options.Kills} and {_storeysVisited} of {_options.StoreysMin} storeys visited, the last kill or new cell {quiet:0.#} s ago; the fight goes on while one comes within {CrossingStallSeconds:0.#} s, at most {bound:0.#} s"));
        }

        return true;
    }

    /// <summary>M6 7 D: the tour wants the body off this storey for <c>--fight-storeys-min</c> (<see cref="GroundTour.WantsToLeave"/>; never with a minimum of 1): the crossing is owed. <paramref name="storey"/> is the storey it goes to.</summary>
    private bool CrossingOwed(IGameSessionView session, double clock, out int storey)
    {
        storey = -1;
        return !_options.ArenaHere && _walk?.Tour is GroundTour tour && session.State.Self.Position is { } here && tour.WantsToLeave(here.CellId, clock, out storey);
    }
}
